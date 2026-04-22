using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Threading;
using HelideckVer2.Models;

namespace HelideckVer2.Services
{
    public class ComEngine
    {
        public event Action<string, string> OnDataReceived;
        private List<SerialPort> _activePorts = new List<SerialPort>();

        public void Initialize(List<DeviceTask> tasks)
        {
            CloseAll();

            foreach (var task in tasks)
            {
                if (string.IsNullOrEmpty(task.PortName)) continue;

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

                if (task.TaskName == "GPS" || task.TaskName == "R/P/H")
                {
                    sp.BaudRate = 9600;
                }
                else
                {
                    sp.BaudRate = 4800;
                }

                sp.Parity = Parity.None;
                sp.DataBits = 8;
                sp.StopBits = StopBits.One;
                sp.Handshake = Handshake.None;

                sp.DtrEnable = true;
                sp.RtsEnable = true;

                sp.ReadTimeout = 2000;
                sp.WriteTimeout = 500;

                sp.DataReceived += (s, e) =>
                {
                    var port = (SerialPort)s;
                    try
                    {
                        string line = port.ReadLine();
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            OnDataReceived?.Invoke(port.PortName, line.Trim());
                        }
                    }
                    catch (TimeoutException) { }
                    catch (Exception) { }
                };

                sp.Open();
                MessageBox.Show($"Đã mở cổng {sp.PortName} (Task: {task.TaskName}) với tốc độ BaudRate thực tế là: {sp.BaudRate}", "Kiểm tra Baudrate");
                lock (_activePorts)
                {
                    _activePorts.Add(sp);
                }
            }
            catch (Exception ex)
            {
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