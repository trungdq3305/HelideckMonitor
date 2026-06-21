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
        private const int MaxQueueDepth = 10000;

        private readonly System.Timers.Timer _flushTimer;
        private bool _isDisposed = false;

        // Disk space guard — checked every 5 minutes to avoid stat() overhead every 10s flush
        private DateTime _lastDiskCheck   = DateTime.MinValue;
        private bool     _diskCritical    = false;
        private const long DiskWarnMb     = 500;   // log warning below this
        private const long DiskCriticalMb = 100;   // suspend logging below this

        public DataLogger()
        {
            Directory.CreateDirectory(_baseFolder);

            // 1. TỰ ĐỘNG DỌN RÁC (> 90 NGÀY) MỖI KHI MỞ APP
            CleanOldLogs(30);

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
            double windSpeedMs, double windDirDeg,
            double tempC, double humidityPct, double pressureHPa,
            string gpsLat = "", string gpsLon = "")
        {
            if (_logBuffer.Count >= MaxQueueDepth) return; // drop snapshot silently when queue is full
            DateTime now = DateTime.Now;
            string line = $"{now:HH:mm:ss.fff},DATA,{speedKnot:0.000},{headingDeg:0.0},{rollDeg:0.000},{pitchDeg:0.000},{heaveCm:0.000},{heavePeriodSec:0.0},{windSpeedMs:0.000},{windDirDeg:0.0},,,,,,{tempC:0.00},{humidityPct:0.00},{pressureHPa:0.0},{gpsLat},{gpsLon}";
            _logBuffer.Enqueue(line);
        }

        public void LogAlarmEvent(string eventName, string alarmId, string alarmState, double value, double limit, string raw = "")
        {
            // Alarm events are always enqueued even when queue is near-full (priority over snapshots).
            // Re-enqueue after disk failure is handled in FlushTimer_Elapsed.
            DateTime now = DateTime.Now;
            string line = $"{now:HH:mm:ss.fff},ALARM,,,,,,,,,{alarmId},{alarmState},{value:0.###},{limit:0.###},{eventName}|{raw}";
            _logBuffer.Enqueue(line);
        }

        // --- DISK SPACE GUARD ---

        // Returns false when disk is critically full — caller should skip the write.
        // Throttled to once per 5 minutes so stat() doesn't add overhead to every flush.
        private bool CheckDiskSpace()
        {
            if ((DateTime.Now - _lastDiskCheck).TotalMinutes < 5 && !_diskCritical)
                return true;
            _lastDiskCheck = DateTime.Now;
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(_baseFolder));
                var drive = new DriveInfo(root);
                long freeMb = drive.AvailableFreeSpace / (1024 * 1024);

                if (freeMb < DiskCriticalMb)
                {
                    if (!_diskCritical)
                        SystemLogger.LogInfo($"[DataLogger] CRITICAL: {freeMb}MB free on {drive.Name} — data logging suspended. Free disk space to resume.");
                    _diskCritical = true;
                    return false;
                }

                if (_diskCritical)
                {
                    _diskCritical = false;
                    SystemLogger.LogInfo($"[DataLogger] Disk space recovered ({freeMb}MB free) — logging resumed.");
                }

                if (freeMb < DiskWarnMb)
                    SystemLogger.LogInfo($"[DataLogger] WARNING: {freeMb}MB free on {drive.Name}. Delete old logs or expand storage.");
            }
            catch { }
            return true;
        }

        // --- CORE LOGIC: XẢ XUỐNG Ổ CỨNG ---

        private void FlushTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (_logBuffer.IsEmpty) return;
            if (!CheckDiskSpace()) return; // suspend writes when critically low

            // Declare before try/catch so catch block can re-enqueue on write failure
            var linesToWrite = new List<string>();

            try
            {
                while (_logBuffer.TryDequeue(out string line))
                    linesToWrite.Add(line);

                if (linesToWrite.Count == 0) return;

                DateTime now = DateTime.Now;
                string folder = Path.Combine(_baseFolder, now.ToString("yyyyMMdd"));
                Directory.CreateDirectory(folder);

                int block = (now.Minute < 30) ? 0 : 30;
                string filePath = Path.Combine(folder, $"Log_{now:yyyyMMdd_HH}{block:00}.csv");

                if (!File.Exists(filePath))
                {
                    linesToWrite.Insert(0, "Time,Type,SpeedKnot,HeadingDeg,RollDeg,PitchDeg,HeaveCm,HeavePeriodSec,WindSpeedMs,WindDirDeg,AlarmId,AlarmState,Value,Limit,Raw,TempC,HumidityPct,PressureHPa,GpsLat,GpsLon");
                }

                File.AppendAllLines(filePath, linesToWrite);
            }
            catch (Exception ex)
            {
                SystemLogger.LogError("DataLogger_Flush", ex);

                // Re-enqueue lines that failed to write so they are not lost on disk errors.
                // Capped at MaxQueueDepth to prevent unbounded RAM growth on extended disk failure.
                if (_logBuffer.Count + linesToWrite.Count <= MaxQueueDepth)
                {
                    foreach (var l in linesToWrite)
                        _logBuffer.Enqueue(l);
                }
                else
                {
                    SystemLogger.LogInfo($"[DataLogger] Queue full ({_logBuffer.Count} entries) — {linesToWrite.Count} lines discarded due to persistent disk error.");
                }
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