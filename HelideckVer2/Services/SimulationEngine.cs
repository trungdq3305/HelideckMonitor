using System;

namespace HelideckVer2.Services
{
    public class SimulationEngine
    {
        private System.Windows.Forms.Timer _simTimer;
        private double _simTimeCounter = 0;
        private Action<string, string> _onDataGenerated;

        public void Start(Action<string, string> onDataGenerated)
        {
            _onDataGenerated = onDataGenerated;
            _simTimer = new System.Windows.Forms.Timer { Interval = 100 };
            Random rnd = new Random();

            _simTimer.Tick += (s, e) =>
            {
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                _simTimeCounter += 0.1;

                string lat = "1045." + rnd.Next(100, 999), lon = "10640." + rnd.Next(100, 999);
                double speed = 5.0 + rnd.NextDouble() * 2;
                double windSpeed = 15.0 + (rnd.NextDouble() * 10 - 5);
                double windDir = 120.0 + (rnd.NextDouble() * 20 - 10);

                double roll = 2.0 * Math.Sin(_simTimeCounter * 2 * Math.PI / 8.0) + (rnd.NextDouble() * 0.2 - 0.1);
                double pitch = 1.5 * Math.Cos(_simTimeCounter * 2 * Math.PI / 6.0) + (rnd.NextDouble() * 0.2 - 0.1);
                double heave = 30.0 * Math.Sin(_simTimeCounter * 2 * Math.PI / 5.0) + (rnd.NextDouble() * 0.2 - 0.1);
                double heading = 180.0 + (rnd.NextDouble() * 4 - 2);

                // Gửi kèm Checksum THẬT để lọt qua màng lọc khắt khe của Parser
                _onDataGenerated?.Invoke("COM1", AppendChecksum($"$GPGGA,083226.00,{lat},N,{lon},E,1,08,1.0,10.0,M,0.0,M,,"));
                _onDataGenerated?.Invoke("COM1", AppendChecksum($"$GPVTG,360.0,T,348.7,M,0.0,N,{speed.ToString("0.0", ci)},K"));
                _onDataGenerated?.Invoke("COM2", AppendChecksum($"$WIMWV,{windDir.ToString("0.0", ci)},R,{windSpeed.ToString("0.0", ci)},M,A"));
                _onDataGenerated?.Invoke("COM3", AppendChecksum($"$CNTB,{roll.ToString("0.00", ci)},{pitch.ToString("0.00", ci)},{heave.ToString("0.0", ci)}"));
                _onDataGenerated?.Invoke("COM4", AppendChecksum($"$HEHDT,{heading.ToString("0.0", ci)},T"));
            };
            _simTimer.Start();
        }

        // HÀM TẠO CHECKSUM CHO DỮ LIỆU FAKE
        private string AppendChecksum(string sentence)
        {
            int checksum = 0;
            for (int i = 1; i < sentence.Length; i++)
            {
                checksum ^= Convert.ToByte(sentence[i]);
            }
            return sentence + "*" + checksum.ToString("X2");
        }

        public void Stop()
        {
            if (_simTimer != null)
            {
                _simTimer.Stop();
                _simTimer.Dispose();
            }
        }
    }
}