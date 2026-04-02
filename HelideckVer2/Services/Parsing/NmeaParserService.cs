using System;
using System.Globalization;

namespace HelideckVer2.Services.Parsing
{
    /// <summary>
    /// Chuyên gia đọc và dịch chuỗi NMEA từ Cảng COM.
    /// Không chứa bất kỳ giao diện (UI) hay lưu trữ nào.
    /// </summary>
    public class NmeaParserService
    {
        public double HeaveArm { get; set; } = 10.0;

        // Định nghĩa các Event báo hiệu khi dịch xong 1 loại dữ liệu
        public event Action<double> OnHeadingParsed;
        public event Action<double, double> OnWindParsed; // Speed, Dir
        public event Action<double, double, double> OnMotionParsed; // Roll, Pitch, Heave
        public event Action<string, string> OnPositionParsed; // formatted Lat, Lon
        public event Action<double> OnSpeedParsed; // knot

        public void Parse(string portName, string data)
        {
            try
            {
                if (string.IsNullOrEmpty(data) || !data.StartsWith("$")) return;

                // Cắt bỏ phần Checksum sau dấu *
                int starIndex = data.IndexOf('*');
                if (starIndex > -1) data = data.Substring(0, starIndex);

                string[] p = data.Split(',');
                var culture = CultureInfo.InvariantCulture;
                var style = NumberStyles.Any;

                // 1. HEADING ($HEHDT)
                if (p[0].EndsWith("HDT") && p.Length >= 2)
                {
                    if (double.TryParse(p[1], style, culture, out double heading))
                        OnHeadingParsed?.Invoke(heading);
                    return;
                }

                // 2. WIND ($WIMWV)
                if (p[0].EndsWith("MWV") && p.Length >= 4)
                {
                    if (double.TryParse(p[1], style, culture, out double wDir) &&
                        double.TryParse(p[3], style, culture, out double wSpeed))
                        OnWindParsed?.Invoke(wSpeed, wDir);
                    return;
                }

                // 3. MOTION ($CNTB hoặc $PRDID)
                if ((p[0].EndsWith("CNTB") && p.Length >= 4) || (p[0] == "$PRDID" && p.Length >= 3))
                {
                    double r = 0, pi = 0, h = 0;
                    bool ok = false;

                    if (p[0].EndsWith("CNTB"))
                    {
                        ok = double.TryParse(p[1], style, culture, out r) &&
                             double.TryParse(p[2], style, culture, out pi) &&
                             double.TryParse(p[3], style, culture, out h);
                    }
                    else
                    {
                        if (double.TryParse(p[1], style, culture, out double rawP) &&
                            double.TryParse(p[2], style, culture, out double rawR))
                        {
                            r = rawR;
                            pi = rawP;
                            h = HeaveArm * Math.Sin(rawP * Math.PI / 180.0);
                            ok = true;
                        }
                    }

                    if (ok) OnMotionParsed?.Invoke(r, pi, h);
                    return;
                }

                // 4. POSITION ($GPGGA)
                if (p[0].EndsWith("GGA") && p.Length >= 6)
                {
                    string lat = p[2], latD = p[3], lon = p[4], lonD = p[5];
                    if (lat.Length > 4 && lon.Length > 5)
                    {
                        string fLat = $"{lat.Substring(0, 2)}°{lat.Substring(2)}'{latD}";
                        string fLon = $"{lon.Substring(0, 3)}°{lon.Substring(3)}'{lonD}";
                        OnPositionParsed?.Invoke(fLat, fLon);
                    }
                    else
                    {
                        OnPositionParsed?.Invoke("NO FIX", "NO FIX");
                    }
                    return;
                }

                // 5. SPEED ($GPVTG)
                if (p[0] == "$GPVTG")
                {
                    double k = TryGetNumberAfterToken(p, "N");
                    if (!double.IsNaN(k)) OnSpeedParsed?.Invoke(k);
                    return;
                }
            }
            catch { /* Bỏ qua các chuỗi rác, không làm treo máy */ }
        }

        // Hàm hỗ trợ tìm số trong chuỗi NMEA
        private double TryGetNumberAfterToken(string[] p, string t)
        {
            for (int i = 0; i < p.Length - 1; i++)
                if (string.Equals(p[i], t, StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(p[i + 1], NumberStyles.Any, CultureInfo.InvariantCulture, out double v))
                    return v;
            return double.NaN;
        }
    }
}