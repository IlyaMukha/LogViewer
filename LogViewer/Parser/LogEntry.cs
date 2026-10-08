using System;
using System.Globalization;

namespace ConsoleApp1
{
    public enum LogLevel
    {
        Warn = 1,
        Error = 2,
        Fatal = 3
    }

    public sealed class LogEntry
    {
        public string Title { get; set; } = "";
        public string FullText { get; set; } = "";
        public LogLevel Type { get; set; }
        public List<LogRecord> Exceptions { get; set; } = new();

        public override string ToString() => $"{Type.ToString().ToUpperInvariant(),-5} {Title}";
    }

    public sealed class LogRecord
    {
        public string Text { get; set; } = "";
        public DateTime dateTime { get; set; }
        public string File { get; set; } = "";

        public static LogRecord Create(string text, string timestamp, string file)
            => new LogRecord
            {
                Text = text,
                File = file,
                dateTime = ParseTimestamp(timestamp)
            };

        private static DateTime ParseTimestamp(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return default;
            var n = s.Replace(',', '.');
            if (DateTime.TryParse(n, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return dt;
            if (DateTime.TryParseExact(n, "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out dt))
                return dt;
            return default;
        }
    }
}