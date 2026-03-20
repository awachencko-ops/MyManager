using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Replica
{
    public static class Logger
    {
        private static readonly object _lock = new object();
        public static string LogFilePath { get; set; } = "manager.log";
        public static string? CurrentCorrelationId => LogContext.CorrelationId;

        public static IDisposable BeginCorrelationScope(string? correlationId = null)
            => LogContext.BeginCorrelationScope(correlationId);

        public static string EnsureCorrelationId()
            => LogContext.EnsureCorrelationId();

        public static IDisposable BeginScope(params (string Key, string? Value)[] fields)
            => LogContext.BeginScope(fields);

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

                    var line = FormatLine(level, message, LogContext.GetPropertiesSnapshot());
                    File.AppendAllText(LogFilePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // молча
            }
        }

        private static string FormatLine(string level, string message, IReadOnlyDictionary<string, string> properties)
        {
            var builder = new StringBuilder(256);
            builder.Append($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}");

            if (properties != null && properties.Count > 0)
            {
                foreach (var kv in properties.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value))
                        continue;

                    builder.Append(" | ");
                    builder.Append(kv.Key);
                    builder.Append('=');
                    builder.Append(kv.Value);
                }
            }

            builder.Append(Environment.NewLine);
            return builder.ToString();
        }
    }
}
