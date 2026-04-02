using System;
using System.IO;

namespace HelideckVer2.Services
{
    public class DataLogger
    {
        private readonly string _baseFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        private string _currentFilePath = "";
        private string _lastFileName = "";

        public DataLogger()
        {
            Directory.CreateDirectory(_baseFolder);
        }

        private string GetDayFolder(DateTime now)
        {
            string dayFolder = Path.Combine(_baseFolder, now.ToString("yyyyMMdd"));
            Directory.CreateDirectory(dayFolder);
            return dayFolder;
        }

        private void EnsureFile(DateTime now)
        {
            int block = (now.Minute < 30) ? 0 : 30; // HH00 / HH30
            string fileName = $"Log_{now:yyyyMMdd_HH}{block:00}.csv";

            if (fileName == _lastFileName && File.Exists(_currentFilePath))
                return;

            _lastFileName = fileName;

            string folder = GetDayFolder(now);
            _currentFilePath = Path.Combine(folder, fileName);

            if (!File.Exists(_currentFilePath))
            {
                File.WriteAllText(_currentFilePath,
                    "Time,Type,SpeedKnot,HeadingDeg,RollDeg,PitchDeg,HeaveCm,HeavePeriodSec,WindSpeedMs,WindDirDeg,AlarmId,AlarmState,Value,Limit,Raw\n");
            }
        }

        public void LogSnapshot(
            double speedKnot, double headingDeg,
            double rollDeg, double pitchDeg, double heaveCm,
            double heavePeriodSec,
            double windSpeedMs, double windDirDeg)
        {
            DateTime now = DateTime.Now;
            EnsureFile(now);

            string line =
                $"{now:HH:mm:ss.fff},DATA," +
                $"{speedKnot:0.0},{headingDeg:0.0},{rollDeg:0.00},{pitchDeg:0.00},{heaveCm:0.0},{heavePeriodSec:0.0}," +
                $"{windSpeedMs:0.0},{windDirDeg:0.0}," +
                $",,,,\n";

            try { File.AppendAllText(_currentFilePath, line); } catch { }
        }

        public void LogAlarmEvent(string eventName, string alarmId, string alarmState, double value, double limit, string raw = "")
        {
            DateTime now = DateTime.Now;
            EnsureFile(now);

            string line =
                $"{now:HH:mm:ss.fff},ALARM," +
                $",,,,,,,," +
                $"{alarmId},{alarmState},{value:0.###},{limit:0.###},{eventName}|{raw}\n";

            try { File.AppendAllText(_currentFilePath, line); } catch { }
        }

    }
}