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
            // dueTime=0 → watchdog fires immediately on a threadpool thread (not UI thread),
            // opening ports right away so hot-reload feels instant.
            // period=5000 → subsequent retries every 5s.
            _watchdogTimer = new System.Threading.Timer(WatchdogCheck, null, 0, 5000);
            SystemLogger.LogInfo("ComEngine initialized.");
        }

        private string GetTaskName(string portName) =>
            _currentTasks?.FirstOrDefault(t => t.PortName == portName)?.TaskName ?? portName;

        private void WatchdogCheck(object state)
        {
            if (_isDisposed || _currentTasks == null) return;

            // Two-phase open to avoid deadlock:
            // SerialPort.Open() is a blocking OS call that can stall for several seconds
            // on faulted hardware. Holding _portLock during Open() blocks GetManagedPort(),
            // which is called from MruService and MeteoService background threads → deadlock.
            // Fix: collect ports to open inside the lock (cheap), then open outside the lock.
            var portsToOpen = new List<(DeviceTask task, SerialPort port)>();

            lock (_portLock)
            {
                foreach (var task in _currentTasks)
                {
                    if (string.IsNullOrEmpty(task.PortName)) continue;

                    if (_nextRetry.TryGetValue(task.PortName, out DateTime retryAt) && DateTime.Now < retryAt)
                        continue;

                    bool isMissing = !_managedPorts.ContainsKey(task.PortName);
                    bool isClosed  = !isMissing && !_managedPorts[task.PortName].IsOpen;
                    bool isModbus  = task.TaskName == "METEO" || task.TaskName == "MRU";
                    bool isTimeout = false;

                    if (!isModbus && !isMissing && _managedPorts[task.PortName].IsOpen &&
                        _lastDataTime.TryGetValue(task.PortName, out DateTime last))
                    {
                        isTimeout = (DateTime.Now - last).TotalSeconds > 10;
                    }

                    if (isMissing || isClosed || isTimeout)
                    {
                        if (isTimeout)
                            try { _managedPorts[task.PortName].Close(); } catch { }

                        bool alreadyOffline = _isOffline.TryGetValue(task.PortName, out bool o) && o;
                        bool wasEverOpen    = _everOpened.TryGetValue(task.PortName, out bool ev) && ev;
                        if (!alreadyOffline && (wasEverOpen || isTimeout))
                        {
                            string reason = isTimeout ? "no data received" : "port unavailable";
                            SystemLogger.LogInfo($"[COM] {task.PortName} ({task.TaskName}) offline – {reason}.");
                        }
                        _isOffline[task.PortName] = true;

                        int count = _retryCount.TryGetValue(task.PortName, out int rc) ? rc : 0;
                        _retryCount[task.PortName] = count + 1;
                        _nextRetry[task.PortName]  = DateTime.Now.AddSeconds(BackoffSec[Math.Min(count, BackoffSec.Length - 1)]);

                        // Build SerialPort (cheap, no IO) inside lock; defer Open() to outside
                        if (!_managedPorts.ContainsKey(task.PortName))
                            _managedPorts[task.PortName] = BuildSerialPort(task);

                        portsToOpen.Add((task, _managedPorts[task.PortName]));
                    }
                }
            }

            // Open ports outside lock — blocking Open() must not hold _portLock
            foreach (var (task, sp) in portsToOpen)
                TryOpenPort(task, sp);
        }

        /// <summary>
        /// Returns the managed SerialPort for a given port name if it is currently open; otherwise null.
        /// Used by Modbus-polling services (e.g. MeteoService) that share ComEngine port lifecycle.
        /// </summary>
        public SerialPort GetManagedPort(string portName)
        {
            lock (_portLock)
            {
                if (_managedPorts.TryGetValue(portName, out var sp) && sp.IsOpen)
                    return sp;
                return null;
            }
        }

        private SerialPort BuildSerialPort(DeviceTask task)
        {
            bool isModbus = task.TaskName == "METEO" || task.TaskName == "MRU";
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
            if (task.TaskName == "MRU")
            {
                sp.DtrEnable      = false;
                sp.RtsEnable      = false;
                sp.Handshake      = Handshake.None;
                sp.ReadBufferSize = 8192;
                sp.ReadTimeout    = 5000;
                sp.WriteTimeout   = 300;
            }
            else if (isModbus)
            {
                sp.ReadTimeout  = 600;
                sp.WriteTimeout = 300;
            }
            else
            {
                sp.DataReceived += OnSerialDataReceived;
            }
            return sp;
        }

        // Called outside _portLock — port.Open() can block for seconds on faulted hardware.
        private void TryOpenPort(DeviceTask task, SerialPort port)
        {
            try
            {
                if (port.IsOpen) return;
                port.Open();
                port.DiscardInBuffer();
                _lastDataTime[task.PortName] = DateTime.Now;

                bool isFirstOpen = !_everOpened.TryGetValue(task.PortName, out bool ev) || !ev;
                if (isFirstOpen)
                {
                    SystemLogger.LogInfo($"[COM] Connected {task.PortName} @ {port.BaudRate} baud ({task.TaskName})");
                    _everOpened[task.PortName] = true;
                }
            }
            catch (Exception ex)
            {
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
