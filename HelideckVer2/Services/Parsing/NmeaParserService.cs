using System;
using System.Collections.Generic;
using System.Globalization;
using HelideckVer2.Models;

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

        // Lock để ngăn race condition khi nhiều COM port gọi Parse() đồng thời từ ThreadPool threads
        private readonly object _parseLock = new();

        // Đếm lỗi liên tiếp theo cổng
        private readonly Dictionary<string, int> _badCount = new();

        // Cache giá trị tốt cuối cùng — _lastHeading/_lastSpeed retained for external readers;
        // _lastWind/_lastMotion/_lastPosition removed (were only used by stale-value re-emit, now removed)
        private double? _lastHeading;
        private double? _lastSpeed;

        // Trạng thái nhiễu công khai (để badge trạng thái hiển thị)
        private readonly Dictionary<string, bool> _isNoisy = new();
        public bool IsPortNoisy(string portName) =>
            _isNoisy.TryGetValue(portName, out bool v) && v;

        // port → danh sách suffix được phép (vd: "COM1" → ["GGA","VTG"])
        // Nếu port không có entry → không lọc (auto-detect).
        private readonly Dictionary<string, string[]> _portAllowedSentences = new();

        public void SetPortTasks(IEnumerable<DeviceTask> tasks)
        {
            _portAllowedSentences.Clear();
            foreach (var t in tasks)
            {
                if (string.IsNullOrWhiteSpace(t.SentenceType)) continue;
                var list = new List<string>();
                foreach (var part in t.SentenceType.Split(','))
                {
                    var s = part.Trim().ToUpperInvariant();
                    if (s.Length > 0) list.Add(s);
                }
                if (list.Count > 0)
                    _portAllowedSentences[t.PortName] = list.ToArray();
            }
        }

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
            lock (_parseLock)  // COM ports fire DataReceived on ThreadPool — prevent concurrent Dictionary writes
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

                // Nếu port có danh sách câu được phép, bỏ qua câu không nằm trong danh sách.
                if (_portAllowedSentences.TryGetValue(portName, out string[] allowed))
                {
                    bool matched = false;
                    foreach (var suffix in allowed)
                        if (p[0].EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { matched = true; break; }
                    if (!matched) return;
                }

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
                        // Plausibility: discard physically impossible sensor readings
                        if (wSpeed < 0 || wSpeed > 100 || wDir < 0 || wDir > 360) return;
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
                        OnMotionParsed?.Invoke(r, pi, h);
                    return;
                }

                // 4b. $PASHR – Xsens Sirius AHRS: roll, pitch, heave SDI trực tiếp
                // Không timestamp: $PASHR,HHH.HH,T,Roll,Pitch,Heave,RollRate,PitchRate,HeadRate,Q1,Q2
                // Có timestamp:    $PASHR,HHMMSS.SS,HHH.HH,T,Roll,Pitch,Heave,...
                if (p[0].Equals("$PASHR", StringComparison.OrdinalIgnoreCase) && p.Length >= 6)
                {
                    bool withTime = p.Length >= 4 && p[3].Equals("T", StringComparison.OrdinalIgnoreCase);
                    bool noTime   = p.Length >= 3 && p[2].Equals("T", StringComparison.OrdinalIgnoreCase);
                    if (withTime || noTime)
                    {
                        int hdgIdx   = withTime ? 2 : 1;
                        int rollIdx  = hdgIdx + 2;
                        int pitchIdx = hdgIdx + 3;
                        int heaveIdx = hdgIdx + 4;

                        if (rollIdx < p.Length && pitchIdx < p.Length &&
                            double.TryParse(p[rollIdx],  style, ci, out double roll) &&
                            double.TryParse(p[pitchIdx], style, ci, out double pitch))
                        {
                            double heave = (heaveIdx < p.Length &&
                                            !string.IsNullOrWhiteSpace(p[heaveIdx]) &&
                                            double.TryParse(p[heaveIdx], style, ci, out double hm))
                                           ? hm * 100.0                                       // SDI heave: m → cm
                                           : HeaveArm * Math.Sin(pitch * Math.PI / 180.0);    // fallback nếu không có SDI
                            OnMotionParsed?.Invoke(roll, pitch, heave);
                        }
                    }
                    return;
                }

                // 4c. $PHTRO – Xsens proprietary pitch/roll
                // $PHTRO,PPP.PP,B,RRR.RR,S*CC  (B=P/M bow indicator, S=S/B roll indicator)
                if (p[0].Equals("$PHTRO", StringComparison.OrdinalIgnoreCase) && p.Length >= 4)
                {
                    if (double.TryParse(p[1], style, ci, out double pitch) &&
                        double.TryParse(p[3], style, ci, out double roll))
                    {
                        double heave = HeaveArm * Math.Sin(pitch * Math.PI / 180.0);
                        OnMotionParsed?.Invoke(roll, pitch, heave);
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
                    OnPositionParsed?.Invoke(fLat, fLon);
                    return;
                }

                // 6. SPEED ($xxVTG) — $GPVTG / $IIVTG / $GNVTG,...
                if (p[0].EndsWith("VTG", StringComparison.OrdinalIgnoreCase) && p.Length >= 6)
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

            // Mark port as noisy. Do NOT re-emit stale cached values — letting DataHub go stale
            // after 2s gives operators an honest "LOST" badge instead of a misleading "OK" with
            // frozen data. Re-emitting old values masked sensor failure from the operator.
            if (!_isNoisy.ContainsKey(portName) || !_isNoisy[portName])
            {
                _isNoisy[portName] = true;
                SystemLogger.LogInfo($"[NMEA] Port {portName} noisy ({_badCount[portName]} consecutive errors). Sensor will show LOST after 2s.");
            }
        }

    }
}
