using System;
using System.Collections.Generic;
using System.Globalization;

namespace HelideckVer2.Services.Parsing
{
    /// <summary>
    /// Parse câu NMEA-0183 chuẩn công nghiệp:
    ///   - Lọc full header ($) + checksum XOR chuẩn NMEA
    ///   - Noise management: nếu >5 checksum lỗi liên tiếp trên 1 cổng
    ///     → phát lại giá trị tốt cuối cùng (freeze, không bị mất dữ liệu)
    ///   - Chỉ cập nhật giá trị khi checksum hợp lệ
    /// </summary>
    public class NmeaParserService
    {
        public double HeaveArm { get; set; } = 10.0;

        public event Action<double>         OnHeadingParsed;
        public event Action<double, double> OnWindParsed;
        public event Action<double, double, double> OnMotionParsed;
        public event Action<string, string> OnPositionParsed;
        public event Action<double>         OnSpeedParsed;
        
        private const int NoiseTriggerCount = 5; // Số lần checksum lỗi liên tiếp để kích hoạt freeze

        // Đếm lỗi liên tiếp theo cổng
        private readonly Dictionary<string, int> _badCount = new();

        // Cache giá trị tốt cuối cùng theo loại cảm biến
        private double? _lastHeading;
        private (double speed, double dir)? _lastWind;
        private (double r, double p, double h)? _lastMotion;
        private (string lat, string lon)? _lastPosition;
        private double? _lastSpeed;

        // Trạng thái nhiễu công khai (để badge trạng thái hiển thị)
        private readonly Dictionary<string, bool> _isNoisy = new();
        public bool IsPortNoisy(string portName) =>
            _isNoisy.TryGetValue(portName, out bool v) && v;

        // ── KIỂM TRA CHECKSUM CHUẨN NMEA XOR ────────────────────────────────
        private bool IsValidNmeaChecksum(string sentence)
        {
            if (string.IsNullOrWhiteSpace(sentence) || sentence.Length < 6) return false;
            if (sentence[0] != '$') return false;

            int starIdx = sentence.LastIndexOf('*');
            if (starIdx < 1 || starIdx + 3 > sentence.Length) return false;

            int calc = 0;
            for (int i = 1; i < starIdx; i++)
                calc ^= (byte)sentence[i];

            string provided = sentence.Substring(starIdx + 1, 2);
            return calc.ToString("X2").Equals(provided, StringComparison.OrdinalIgnoreCase);
        }

        // ── ENTRY POINT ───────────────────────────────────────────────────────
        public void Parse(string portName, string data)
        {
            try
            {
                // 1. KIỂM TRA TOÀN VẸN DỮ LIỆU
                if (!IsValidNmeaChecksum(data))
                {
                    HandleBadChecksum(portName);
                    return;
                }

                // Checksum tốt → reset bộ đếm nhiễu
                _badCount[portName] = 0;
                _isNoisy[portName] = false;

                // Bỏ phần checksum trước khi xử lý
                int starIdx = data.LastIndexOf('*');
                string clean = starIdx > 0 ? data.Substring(0, starIdx) : data;

                string[] p = clean.Split(',');
                var ci    = CultureInfo.InvariantCulture;
                var style = NumberStyles.Any;

                // 2. HEADING ($xxHDT)
                if (p[0].EndsWith("HDT", StringComparison.OrdinalIgnoreCase) && p.Length >= 2)
                {
                    if (double.TryParse(p[1], style, ci, out double heading))
                    {
                        _lastHeading = heading;
                        OnHeadingParsed?.Invoke(heading);
                    }
                    return;
                }

                // 3. WIND ($xxMWV) – Wind Speed and Angle
                if (p[0].EndsWith("MWV", StringComparison.OrdinalIgnoreCase) && p.Length >= 5)
                {
                    if (double.TryParse(p[1], style, ci, out double wDir) &&
                        double.TryParse(p[3], style, ci, out double wSpeed))
                    {
                        _lastWind = (wSpeed, wDir);
                        OnWindParsed?.Invoke(wSpeed, wDir);
                    }
                    return;
                }

                // 4. MOTION ($CNTB Roll/Pitch/Heave hoặc $PRDID)
                if ((p[0].EndsWith("CNTB", StringComparison.OrdinalIgnoreCase) && p.Length >= 4) ||
                    (p[0].Equals("$PRDID", StringComparison.OrdinalIgnoreCase) && p.Length >= 3))
                {
                    double r = 0, pi = 0, h = 0;
                    bool ok = false;

                    if (p[0].EndsWith("CNTB", StringComparison.OrdinalIgnoreCase))
                    {
                        ok = double.TryParse(p[1], style, ci, out r) &&
                             double.TryParse(p[2], style, ci, out pi) &&
                             double.TryParse(p[3], style, ci, out h);
                    }
                    else
                    {
                        if (double.TryParse(p[1], style, ci, out double rawP) &&
                            double.TryParse(p[2], style, ci, out double rawR))
                        {
                            r  = rawR;
                            pi = rawP;
                            h  = HeaveArm * Math.Sin(rawP * Math.PI / 180.0);
                            ok = true;
                        }
                    }

                    if (ok)
                    {
                        _lastMotion = (r, pi, h);
                        OnMotionParsed?.Invoke(r, pi, h);
                    }
                    return;
                }

                // 5. POSITION ($xxGGA)
                if (p[0].EndsWith("GGA", StringComparison.OrdinalIgnoreCase) && p.Length >= 6)
                {
                    string lat = p[2], latD = p[3], lon = p[4], lonD = p[5];
                    string fLat, fLon;
                    if (lat.Length > 4 && lon.Length > 5)
                    {
                        fLat = $"{lat.Substring(0, 2)}°{lat.Substring(2)}'{latD}";
                        fLon = $"{lon.Substring(0, 3)}°{lon.Substring(3)}'{lonD}";
                    }
                    else
                    {
                        fLat = "NO FIX";
                        fLon = "NO FIX";
                    }
                    _lastPosition = (fLat, fLon);
                    OnPositionParsed?.Invoke(fLat, fLon);
                    return;
                }

                // 6. SPEED ($GPVTG) — $GPVTG,COG_T,T,COG_M,M,SOG_knots,N,SOG_kph,K
                if (p[0].Equals("$GPVTG", StringComparison.OrdinalIgnoreCase) && p.Length >= 6)
                {
                    if (double.TryParse(p[5], style, ci, out double knots))
                    {
                        _lastSpeed = knots;
                        OnSpeedParsed?.Invoke(knots);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                SystemLogger.LogError($"[NMEA] Parse error. Port={portName} Raw={data}", ex);
            }
        }

        // ── XỬ LÝ KHI CHECKSUM LỖI (NOISE MANAGEMENT) ───────────────────────
        private void HandleBadChecksum(string portName)
        {
            if (!_badCount.ContainsKey(portName)) _badCount[portName] = 0;
            _badCount[portName]++;

            if (_badCount[portName] < NoiseTriggerCount) return;

            // Đánh dấu nhiễu và phát lại giá trị gần nhất (freeze)
            if (!_isNoisy.ContainsKey(portName) || !_isNoisy[portName])
            {
                _isNoisy[portName] = true;
                SystemLogger.LogInfo($"[NMEA] Port {portName} noisy ({_badCount[portName]} consecutive errors). Freezing last known values.");
            }

            // Phát lại để DataHub không đánh dấu stale ngay lập tức
            if (_lastHeading.HasValue)   OnHeadingParsed?.Invoke(_lastHeading.Value);
            if (_lastWind.HasValue)      OnWindParsed?.Invoke(_lastWind.Value.speed, _lastWind.Value.dir);
            if (_lastMotion.HasValue)    OnMotionParsed?.Invoke(_lastMotion.Value.r, _lastMotion.Value.p, _lastMotion.Value.h);
            if (_lastPosition.HasValue)  OnPositionParsed?.Invoke(_lastPosition.Value.lat, _lastPosition.Value.lon);
            if (_lastSpeed.HasValue)     OnSpeedParsed?.Invoke(_lastSpeed.Value);
        }

    }
}
