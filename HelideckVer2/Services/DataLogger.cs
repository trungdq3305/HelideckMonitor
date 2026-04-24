using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Timers;

namespace HelideckVer2.Services
{
    public class DataLogger : IDisposable
    {
        private readonly string _baseFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");

        // Dùng ConcurrentQueue để nhiều luồng cùng nhét data vào mà không bị Crash (Thread-safe)
        private readonly ConcurrentQueue<string> _logBuffer = new ConcurrentQueue<string>();

        // Timer dùng để gom data xả xuống ổ cứng
        private readonly System.Timers.Timer _flushTimer;
        private bool _isDisposed = false;

        public DataLogger()
        {
            Directory.CreateDirectory(_baseFolder);

            // 1. TỰ ĐỘNG DỌN RÁC (> 90 NGÀY) MỖI KHI MỞ APP
            CleanOldLogs(90);

            // 2. KHỞI TẠO TIMER XẢ BUFFER (10 giây / lần)
            _flushTimer = new System.Timers.Timer(10000);
            _flushTimer.Elapsed += FlushTimer_Elapsed;
            _flushTimer.Start();
        }

        // --- CÁC HÀM NÀY CHỈ LÀM ĐÚNG 1 VIỆC: NHÉT VÀO RAM (RẤT NHANH) ---

        public void LogSnapshot(
            double speedKnot, double headingDeg,
            double rollDeg, double pitchDeg, double heaveCm,
            double heavePeriodSec,
            double windSpeedMs, double windDirDeg)
        {
            DateTime now = DateTime.Now;
            string line = $"{now:HH:mm:ss.fff},DATA,{speedKnot:0.0},{headingDeg:0.0},{rollDeg:0.00},{pitchDeg:0.00},{heaveCm:0.0},{heavePeriodSec:0.0},{windSpeedMs:0.0},{windDirDeg:0.0},,,,";
            _logBuffer.Enqueue(line);
        }

        public void LogAlarmEvent(string eventName, string alarmId, string alarmState, double value, double limit, string raw = "")
        {
            DateTime now = DateTime.Now;
            string line = $"{now:HH:mm:ss.fff},ALARM,,,,,,,,,{alarmId},{alarmState},{value:0.###},{limit:0.###},{eventName}|{raw}";
            _logBuffer.Enqueue(line);
        }

        // --- CORE LOGIC: XẢ XUỐNG Ổ CỨNG ---

        private void FlushTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_logBuffer.IsEmpty) return;

            try
            {
                // Rút toàn bộ data đang có trong Buffer ra
                List<string> linesToWrite = new List<string>();
                while (_logBuffer.TryDequeue(out string line))
                {
                    linesToWrite.Add(line);
                }

                if (linesToWrite.Count == 0) return;

                // Xác định tên file dựa trên thời điểm hiện tại (Cắt file mỗi 30 phút như logic cũ của bạn)
                DateTime now = DateTime.Now;
                string folder = Path.Combine(_baseFolder, now.ToString("yyyyMMdd"));
                Directory.CreateDirectory(folder);

                int block = (now.Minute < 30) ? 0 : 30;
                string filePath = Path.Combine(folder, $"Log_{now:yyyyMMdd_HH}{block:00}.csv");

                // Thêm Header nếu file mới tinh
                if (!File.Exists(filePath))
                {
                    linesToWrite.Insert(0, "Time,Type,SpeedKnot,HeadingDeg,RollDeg,PitchDeg,HeaveCm,HeavePeriodSec,WindSpeedMs,WindDirDeg,AlarmId,AlarmState,Value,Limit,Raw");
                }

                // Ghi TOÀN BỘ List xuống ổ cứng ĐÚNG 1 LẦN (Nhanh và bảo vệ ổ cứng)
                File.AppendAllLines(filePath, linesToWrite);
            }
            catch (Exception ex)
            {
                // Nếu ghi thất bại (ví dụ file đang bị ai đó mở), đẩy sang SystemLogger
                SystemLogger.LogError("DataLogger_Flush", ex);
            }
        }

        // --- HÀM TỰ ĐỘNG DỌN RÁC CÔNG NGHIỆP ---
        private void CleanOldLogs(int daysToKeep)
        {
            try
            {
                DateTime cutoffDate = DateTime.Now.Date.AddDays(-daysToKeep);
                var directories = Directory.GetDirectories(_baseFolder);

                foreach (var dir in directories)
                {
                    DirectoryInfo dirInfo = new DirectoryInfo(dir);
                    // Parse tên thư mục (yyyyMMdd) để so sánh tuổi
                    if (DateTime.TryParseExact(dirInfo.Name, "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out DateTime dirDate))
                    {
                        if (dirDate < cutoffDate)
                        {
                            dirInfo.Delete(true); // true = Xóa thư mục và toàn bộ file bên trong
                            SystemLogger.LogInfo($"Deleted old log folder: {dirInfo.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                SystemLogger.LogError("DataLogger_CleanOldLogs", ex);
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _flushTimer?.Stop();
            _flushTimer?.Dispose();
            FlushTimer_Elapsed(null, null); // Xả nốt những gì còn sót lại trước khi tắt app
            _isDisposed = true;
        }
    }
}