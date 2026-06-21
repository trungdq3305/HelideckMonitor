using System;
using System.IO;

namespace HelideckVer2.Services
{
    public static class SystemLogger
    {
        private static readonly string _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "System_Error.log");
        private static readonly object _lockObj = new object();

        /// <summary>
        /// Ghi nhận các lỗi Exception nghiêm trọng của hệ thống (Crash, Mất kết nối, Lỗi Parser...)
        /// </summary>
        public static void LogError(string context, Exception ex)
        {
            try
            {
                lock (_lockObj)
                {
                    CheckRotate();
                    string errorMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] [{context}] - {ex.Message}\nStackTrace: {ex.StackTrace}\n" + new string('-', 50) + "\n";
                    File.AppendAllText(_logFilePath, errorMessage);
                }
            }
            catch { }
        }

        public static void LogInfo(string message)
        {
            try
            {
                lock (_lockObj)
                {
                    CheckRotate();
                    string infoMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] - {message}\n";
                    File.AppendAllText(_logFilePath, infoMessage);
                }
            }
            catch { }
        }

        // Rotate log file when it exceeds 5 MB — preserves last 3 rotated files for incident investigation.
        // Must be called under _lockObj.
        private static void CheckRotate()
        {
            if (!File.Exists(_logFilePath)) return;
            if (new FileInfo(_logFilePath).Length <= 5 * 1024 * 1024) return;
            for (int i = 2; i >= 1; i--)
            {
                string older = _logFilePath + "." + (i + 1);
                string newer = _logFilePath + "." + i;
                if (File.Exists(newer))
                {
                    if (File.Exists(older)) File.Delete(older);
                    File.Move(newer, older);
                }
            }
            File.Move(_logFilePath, _logFilePath + ".1");
        }
    }
}