using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using HelideckVer2.Models;

namespace HelideckVer2.Services
{
    public class ComEngine
    {
        // Sự kiện bắn dữ liệu về Form1 (PortName, RawData)
        public event Action<string, string> OnDataReceived;

        // Danh sách quản lý các cổng đang mở để sau này Close cho sạch
        private List<SerialPort> _activePorts = new List<SerialPort>();

        /// <summary>
        /// Khởi tạo kết nối thực tế
        /// </summary>
        /// <param name="tasks">Danh sách cấu hình lấy từ Config</param>
        public void Initialize(List<DeviceTask> tasks)
        {
            CloseAll(); // Reset trước khi mở

            foreach (var task in tasks)
            {
                // Bỏ qua nếu không có tên cổng
                if (string.IsNullOrEmpty(task.PortName)) continue;

                // Tạo luồng riêng để mở cổng, tránh làm đơ giao diện nếu cổng bị treo
                Thread t = new Thread(() => OpenPortSafe(task));
                t.IsBackground = true;
                t.Start();
            }
        }

        private void OpenPortSafe(DeviceTask task)
        {
            try
            {
                SerialPort sp = new SerialPort();
                sp.PortName = task.PortName;
                sp.BaudRate = task.BaudRate > 0 ? task.BaudRate : 9600; // Mặc định 9600 nếu lỗi
                sp.Parity = Parity.None;
                sp.DataBits = 8;
                sp.StopBits = StopBits.One;
                sp.Handshake = Handshake.None;

                // QUAN TRỌNG VỚI THIẾT BỊ CÔNG NGHIỆP:
                // Nhiều thiết bị yêu cầu tín hiệu DTR/RTS mức cao mới chịu gửi data
                sp.DtrEnable = true;
                sp.RtsEnable = true;

                // Timeout đọc để tránh treo luồng đọc
                sp.ReadTimeout = 2000;
                sp.WriteTimeout = 500;

                sp.DataReceived += (s, e) =>
                {
                    var port = (SerialPort)s;
                    try
                    {
                        // Đọc từng dòng (Chuẩn NMEA luôn kết thúc bằng \r\n)
                        // ReadLine sẽ chờ cho đến khi gặp ký tự xuống dòng
                        string line = port.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            OnDataReceived?.Invoke(port.PortName, line.Trim());
                        }
                    }
                    catch (TimeoutException)
                    {
                        // Timeout là bình thường nếu thiết bị gửi chậm, không cần xử lý
                    }
                    catch (Exception)
                    {
                        // Lỗi khác (rút cáp, mất kết nối...)
                    }
                };

                sp.Open();

                // Thêm vào danh sách quản lý (cần lock vì đa luồng)
                lock (_activePorts)
                {
                    _activePorts.Add(sp);
                }
            }
            catch (Exception ex)
            {
                // Log lỗi ra Console hoặc File nếu cần (Ví dụ: Cổng không tồn tại)
                System.Diagnostics.Debug.WriteLine($"Không thể mở {task.PortName}: {ex.Message}");
            }
        }

        public void CloseAll()
        {
            lock (_activePorts)
            {
                foreach (var sp in _activePorts)
                {
                    if (sp != null && sp.IsOpen)
                    {
                        try
                        {
                            // Gỡ sự kiện trước khi đóng để tránh lỗi
                            sp.DataReceived -= null;
                            sp.Close();
                            sp.Dispose();
                        }
                        catch { }
                    }
                }
                _activePorts.Clear();
            }
        }
    }
}