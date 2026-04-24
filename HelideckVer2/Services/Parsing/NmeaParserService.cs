using System;
using System.Globalization;

namespace HelideckVer2.Services.Parsing
{
    public class NmeaParserService
    {
        public double HeaveArm { get; set; } = 10.0;

        public event Action<double> OnHeadingParsed;
        public event Action<double, double> OnWindParsed;
        public event Action<double, double, double> OnMotionParsed;
        public event Action<string, string> OnPositionParsed;
        public event Action<double> OnSpeedParsed;

        // HÀM KIỂM TRA CHECKSUM CHUẨN CÔNG NGHIỆP
        private bool IsValidNmeaChecksum(string sentence)
        {
            // Lọc Full Header: Phải có $, *, và đủ độ dài tối thiểu
            if (string.IsNullOrWhiteSpace(sentence) || sentence.Length < 4) return false;
            if (sentence[0] != '$') return false;

            int asteriskIndex = sentence.IndexOf('*');
            if (asteriskIndex < 0 || asteriskIndex + 3 > sentence.Length) return false;

            string dataToCalculate = sentence.Substring(1, asteriskIndex - 1);
            string providedChecksum = sentence.Substring(asteriskIndex + 1, 2);

            int calculatedChecksum = 0;
            foreach (char c in dataToCalculate)
            {
                calculatedChecksum ^= (byte)c; // Thuật toán XOR mã ASCII
            }

            return calculatedChecksum.ToString("X2").Equals(providedChecksum, StringComparison.OrdinalIgnoreCase);
        }

        public void Parse(string portName, string data)
        {
            try
            {
                // 1. KIỂM TRA TOÀN VẸN GÓI TIN (DATA INTEGRITY)
                if (!IsValidNmeaChecksum(data)) return; // Sai checksum -> Nhiễu -> Vứt bỏ ngay lập tức

                int starIndex = data.IndexOf('*');
                if (starIndex > -1) data = data.Substring(0, starIndex);

                string[] p = data.Split(',');
                var culture = CultureInfo.InvariantCulture;
                var style = NumberStyles.Any;

                // 2. HEADING ($HEHDT)
                if (p[0].EndsWith("HDT") && p.Length >= 2)
                {
                    if (double.TryParse(p[1], style, culture, out double heading))
                        OnHeadingParsed?.Invoke(heading);
                    return;
                }

                // 3. WIND ($WIMWV)
                if (p[0].EndsWith("MWV") && p.Length >= 4)
                {
                    if (double.TryParse(p[1], style, culture, out double wDir) &&
                        double.TryParse(p[3], style, culture, out double wSpeed))
                        OnWindParsed?.Invoke(wSpeed, wDir);
                    return;
                }

                // 4. MOTION ($CNTB hoặc $PRDID)
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

                // 5. POSITION ($GPGGA)
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

                // 6. SPEED ($GPVTG)
                if (p[0] == "$GPVTG")
                {
                    double k = TryGetNumberAfterToken(p, "N");
                    if (!double.IsNaN(k)) OnSpeedParsed?.Invoke(k);
                    return;
                }
            }
            catch { /* Bỏ qua các chuỗi rác, không làm treo máy */ }
        }

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