using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using HelideckVer2.Models;

namespace HelideckVer2.Services
{
    /// <summary>
    /// Quản lý các cổng COM serial. Thiết kế cho độ trễ thấp nhất có thể:
    /// - ReadExisting() (không block) thay vì ReadLine() (có thể block)
    /// - Buffer ghép dòng theo chuẩn NMEA (bắt đầu bằng $, kết thúc bằng \n)
    /// - Watchdog tự động phục hồi khi mất kết nối hoặc timeout
    /// </summary>
    public class ComEngine : IDisposable
    {
        public event Action<string, string> OnDataReceived;

        private readonly Dictionary<string, SerialPort> _managedPorts = new();
        private readonly Dictionary<string, bool> _portErrorState = new();
        private readonly Dictionary<string, StringBuilder> _lineBuffers = new();

        // ConcurrentDictionary cho timestamps – cập nhật từ DataReceived thread, đọc từ Watchdog thread
        private readonly ConcurrentDictionary<string, DateTime> _lastDataTime = new();

        private List<DeviceTask> _currentTasks;
        private System.Threading.Timer _watchdogTimer;
        private readonly object _portLock = new();    // bảo vệ _managedPorts, _portErrorState
        private readonly object _bufferLock = new();  // bảo vệ _lineBuffers
        private bool _isDisposed = false;

        public void Initialize(List<DeviceTask> tasks)
        {
            _currentTasks = tasks;
            _watchdogTimer = new System.Threading.Timer(WatchdogCheck, null, 1000, 5000);
            SystemLogger.LogInfo("ComEngine initialized.");
        }

        private void WatchdogCheck(object state)
        {
            if (_isDisposed || _currentTasks == null) return;

            lock (_portLock)
            {
                foreach (var task in _currentTasks)
                {
                    if (string.IsNullOrEmpty(task.PortName)) continue;

                    bool isMissing = !_managedPorts.ContainsKey(task.PortName);
                    bool isClosed  = !isMissing && !_managedPorts[task.PortName].IsOpen;
                    bool isTimeout = false;

                    if (!isMissing && _managedPorts[task.PortName].IsOpen)
                    {
                        if (_lastDataTime.TryGetValue(task.PortName, out DateTime last))
                            isTimeout = (DateTime.Now - last).TotalSeconds > 10;
                    }

                    if (isMissing || isClosed || isTimeout)
                    {
                        if (isTimeout)
                        {
                            SystemLogger.LogInfo($"[Watchdog] {task.PortName} timeout – resetting...");
                            try { _managedPorts[task.PortName].Close(); } catch { }
                        }
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
                        PortName  = task.PortName,
                        BaudRate  = baud,
                        Parity    = Parity.None,
                        DataBits  = 8,
                        StopBits  = StopBits.One,
                        DtrEnable = true,
                        RtsEnable = true,
                        // Không set ReadTimeout – dùng ReadExisting (non-blocking)
                        ReceivedBytesThreshold = 1
                    };

                    sp.DataReceived += OnSerialDataReceived;
                    _managedPorts[task.PortName] = sp;
                }

                var port = _managedPorts[task.PortName];
                if (!port.IsOpen)
                {
                    port.Open();
                    port.DiscardInBuffer(); // Xoá dữ liệu cũ trước khi bắt đầu đọc mới
                    _lastDataTime[task.PortName] = DateTime.Now;
                    _portErrorState[task.PortName] = false;
                    SystemLogger.LogInfo($"[COM] Connected {task.PortName} @ {port.BaudRate} baud ({task.TaskName})");
                }
            }
            catch (Exception ex)
            {
                if (!_portErrorState.ContainsKey(task.PortName) || _portErrorState[task.PortName] == false)
                {
                    SystemLogger.LogError($"[COM] Cannot open {task.PortName}", ex);
                    _portErrorState[task.PortName] = true;
                }
            }
        }

        /// <summary>
        /// Handler non-blocking: dùng ReadExisting() lấy toàn bộ byte đang có trong buffer hệ thống,
        /// ghép vào line-buffer nội bộ, trích xuất từng dòng NMEA hoàn chỉnh.
        /// </summary>
        private void OnSerialDataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            var port = (SerialPort)sender;
            _lastDataTime[port.PortName] = DateTime.Now; // Cập nhật heartbeat NGAY LẬP TỨC

            try
            {
                string incoming = port.ReadExisting(); // Không bao giờ block
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
                    {
                        try { sp.Close(); sp.Dispose(); } catch { }
                    }
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
