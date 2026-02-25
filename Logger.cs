using System;
using System.IO;
using System.Text;

namespace MyManager
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        public static string LogFilePath { get; set; } = "manager.log";

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);

        private static void Write(string level, string message)
        {
            try
            {
                lock (_lock)
                {
                    string? dir = Path.GetDirectoryName(LogFilePath);
                    if (!string.IsNullOrWhiteSpace(dir))
                        Directory.CreateDirectory(dir);

                    File.AppendAllText(LogFilePath,
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}",
                        Encoding.UTF8);
                }
            }
            catch
            {
                // молча
            }
        }
    }
}
