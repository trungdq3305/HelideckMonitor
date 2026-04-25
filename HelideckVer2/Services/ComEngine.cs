using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using HelideckVer2.Models;

namespace HelideckVer2.Services
{
    /// <summary>
    /// Quản lý các cổng COM serial. Thiết kế cho độ trễ thấp nhất có thể:
    /// - ReadExisting() (không block) thay vì ReadLine() (có thể block)
    /// - Buffer ghép dòng theo chuẩn NMEA (bắt đầu bằng $, kết thúc bằng \n)
    /// - Watchdog tự động phục hồi với exponential backoff khi mất kết nối
    /// - Log chỉ khi đổi trạng thái (online/offline) — không spam khi retry
    /// </summary>
    public class ComEngine : IDisposable
    {
        public event Action<string, string> OnDataReceived;

        private readonly Dictionary<string, SerialPort>        _managedPorts = new();
        private readonly Dictionary<string, StringBuilder>     _lineBuffers  = new();
        private readonly ConcurrentDictionary<string, DateTime> _lastDataTime = new();

        // State per port — dùng ConcurrentDictionary để cập nhật an toàn từ DataReceived thread
        private readonly ConcurrentDictionary<string, bool>     _isOffline  = new();
        private readonly ConcurrentDictionary<string, bool>     _everOpened = new();
        private readonly ConcurrentDictionary<string, int>      _retryCount = new();
        private readonly ConcurrentDictionary<string, DateTime> _nextRetry  = new();

        // Exponential backoff: 5s → 15s → 30s → 60s → 300s (5 phút cap)
        private static readonly int[] BackoffSec = { 5, 15, 30, 60, 300 };

        private List<DeviceTask> _currentTasks;
        private System.Threading.Timer _watchdogTimer;
        private readonly object _portLock   = new();
        private readonly object _bufferLock = new();
        private bool _isDisposed = false;

        public void Initialize(List<DeviceTask> tasks)
        {
            _currentTasks = tasks;
            _watchdogTimer = new System.Threading.Timer(WatchdogCheck, null, 1000, 5000);
            SystemLogger.LogInfo("ComEngine initialized.");
        }

        private string GetTaskName(string portName) =>
            _currentTasks?.FirstOrDefault(t => t.PortName == portName)?.TaskName ?? portName;

        private void WatchdogCheck(object state)
        {
            if (_isDisposed || _currentTasks == null) return;

            lock (_portLock)
            {
                foreach (var task in _currentTasks)
                {
                    if (string.IsNullOrEmpty(task.PortName)) continue;

                    // Backoff: chưa đến lúc retry → bỏ qua
                    if (_nextRetry.TryGetValue(task.PortName, out DateTime retryAt) && DateTime.Now < retryAt)
                        continue;

                    bool isMissing = !_managedPorts.ContainsKey(task.PortName);
                    bool isClosed  = !isMissing && !_managedPorts[task.PortName].IsOpen;
                    bool isTimeout = false;

                    if (!isMissing && _managedPorts[task.PortName].IsOpen &&
                        _lastDataTime.TryGetValue(task.PortName, out DateTime last))
                    {
                        isTimeout = (DateTime.Now - last).TotalSeconds > 10;
                    }

                    if (isMissing || isClosed || isTimeout)
                    {
                        if (isTimeout)
                            try { _managedPorts[task.PortName].Close(); } catch { }

                        // Log một lần khi bắt đầu offline — không log lại cho đến khi recover
                        bool alreadyOffline = _isOffline.TryGetValue(task.PortName, out bool o) && o;
                        if (!alreadyOffline)
                        {
                            string reason = isTimeout ? "no data received" : "port unavailable";
                            SystemLogger.LogInfo($"[COM] {task.PortName} ({task.TaskName}) offline – {reason}.");
                            _isOffline[task.PortName] = true;
                        }

                        // Tính backoff cho lần retry tiếp theo
                        int count = _retryCount.TryGetValue(task.PortName, out int rc) ? rc : 0;
                        _retryCount[task.PortName] = count + 1;
                        _nextRetry[task.PortName]  = DateTime.Now.AddSeconds(BackoffSec[Math.Min(count, BackoffSec.Length - 1)]);

                        TryOpenPort(task);
                    }
                }
            }
        }

        private void TryOpenPort(DeviceTask task)
        {
            try
            {
                if (!_managedPorts.ContainsKey(task.PortName))
                {
                    int baud = task.BaudRate > 0 ? task.BaudRate : 9600;
                    var sp = new SerialPort
                    {
                        PortName               = task.PortName,
                        BaudRate               = baud,
                        Parity                 = Parity.None,
                        DataBits               = 8,
                        StopBits               = StopBits.One,
                        DtrEnable              = true,
                        RtsEnable              = true,
                        ReceivedBytesThreshold = 1
                    };
                    sp.DataReceived += OnSerialDataReceived;
                    _managedPorts[task.PortName] = sp;
                }

                var port = _managedPorts[task.PortName];
                if (!port.IsOpen)
                {
                    port.Open();
                    port.DiscardInBuffer();
                    _lastDataTime[task.PortName] = DateTime.Now;

                    // Log "Connected" chỉ lần đầu tiên mở thành công
                    bool isFirstOpen = !_everOpened.TryGetValue(task.PortName, out bool ev) || !ev;
                    if (isFirstOpen)
                    {
                        SystemLogger.LogInfo($"[COM] Connected {task.PortName} @ {port.BaudRate} baud ({task.TaskName})");
                        _everOpened[task.PortName] = true;
                    }
                    // Nếu đang ở trạng thái offline: không log "Reconnected" ngay — chờ data thực sự về
                }
            }
            catch (Exception ex)
            {
                // Đã log offline ở WatchdogCheck — chỉ log exception nếu chưa log lần nào
                bool alreadyOffline = _isOffline.TryGetValue(task.PortName, out bool o) && o;
                if (!alreadyOffline)
                    SystemLogger.LogError($"[COM] Cannot open {task.PortName} ({task.TaskName})", ex);
            }
        }

        /// <summary>
        /// Handler non-blocking: dùng ReadExisting() lấy toàn bộ byte đang có trong buffer hệ thống.
        /// </summary>
        private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var port = (SerialPort)sender;
            _lastDataTime[port.PortName] = DateTime.Now;

            // Data flow khôi phục sau offline → log một lần và reset backoff state
            if (_isOffline.TryGetValue(port.PortName, out bool wasOffline) && wasOffline)
            {
                _isOffline[port.PortName] = false;
                _retryCount.TryRemove(port.PortName, out _);
                _nextRetry.TryRemove(port.PortName, out _);
                SystemLogger.LogInfo($"[COM] {port.PortName} ({GetTaskName(port.PortName)}): data flow restored.");
            }

            try
            {
                string incoming = port.ReadExisting();
                if (string.IsNullOrEmpty(incoming)) return;
                ProcessIncoming(port.PortName, incoming);
            }
            catch (Exception ex)
            {
                SystemLogger.LogError($"[COM] Read error {port.PortName}", ex);
            }
        }

        private void ProcessIncoming(string portName, string data)
        {
            StringBuilder buf;
            lock (_bufferLock)
            {
                if (!_lineBuffers.TryGetValue(portName, out buf))
                {
                    buf = new StringBuilder(256);
                    _lineBuffers[portName] = buf;
                }
            }

            lock (buf)
            {
                buf.Append(data);

                // Safety: bỏ buffer nếu quá lớn (kẹt dữ liệu hoặc nhiễu nặng)
                if (buf.Length > 2048)
                {
                    buf.Clear();
                    return;
                }

                string text = buf.ToString();

                // Trích xuất từng dòng hoàn chỉnh (kết thúc bằng \n)
                int nlIdx;
                while ((nlIdx = text.IndexOf('\n')) >= 0)
                {
                    string line = text.Substring(0, nlIdx).TrimEnd('\r', ' ');
                    text = text.Substring(nlIdx + 1);

                    // Chỉ xử lý câu NMEA hợp lệ (bắt đầu bằng $)
                    if (line.Length > 4 && line[0] == '$')
                        OnDataReceived?.Invoke(portName, line);
                }

                // Giữ phần chưa hoàn chỉnh trong buffer
                // Nếu không bắt đầu bằng $ → nhảy đến $ tiếp theo để đồng bộ lại
                int nextDollar = text.IndexOf('$');
                buf.Clear();
                if (nextDollar >= 0)
                    buf.Append(text.Substring(nextDollar));
            }
        }

        public void CloseAll()
        {
            lock (_portLock)
            {
                foreach (var sp in _managedPorts.Values)
                {
                    if (sp != null && sp.IsOpen)
                        try { sp.Close(); sp.Dispose(); } catch { }
                }
                _managedPorts.Clear();
                _lastDataTime.Clear();
            }
        }

        public void Dispose()
        {
            _isDisposed = true;
            _watchdogTimer?.Dispose();
            CloseAll();
        }
    }
}
