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
                lock (_lockObj) // Đảm bảo không bị đụng độ luồng khi nhiều chỗ cùng văng lỗi
                {
                    // Giới hạn kích thước file log dưới 5MB để không làm đầy ổ cứng
                    if (File.Exists(_logFilePath) && new FileInfo(_logFilePath).Length > 5 * 1024 * 1024)
                    {
                        File.Delete(_logFilePath); // Xóa file cũ nếu quá lớn
                    }

                    string errorMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [ERROR] [{context}] - {ex.Message}\nStackTrace: {ex.StackTrace}\n" + new string('-', 50) + "\n";
                    File.AppendAllText(_logFilePath, errorMessage);
                }
            }
            catch
            {
                // Nếu chính hàm ghi lỗi cũng bị lỗi (ví dụ: ổ cứng hỏng vật lý), thì đành chịu, không throw thêm để tránh sập app.
            }
        }

        public static void LogInfo(string message)
        {
            try
            {
                lock (_lockObj)
                {
                    string infoMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [INFO] - {message}\n";
                    File.AppendAllText(_logFilePath, infoMessage);
                }
            }
            catch { }
        }
    }
}