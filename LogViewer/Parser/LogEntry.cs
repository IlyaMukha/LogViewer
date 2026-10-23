using System;
using System.Globalization;

namespace ConsoleApp1
{
    public enum LogLevel
    {
        Info = 0,
        Warn = 1,
        Error = 2,
        Fatal = 3,
        Debug = 4,
        Unknow = 5,
    }

    public class LogEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string FullText { get; set; } = string.Empty;
        public LogLevel Type { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        public List<LogOfImpact> LogOfImpactList { get; set; } = [];
    }

    public class LogOfImpact
    {
        public string Text { get; set; } = string.Empty;
        public string File { get; set; } = string.Empty;
        public DateTime Time { get; set; }
    }


    public class HelperModel
    {
        public string Text { get; set; } = string.Empty;
        public string Description {  get; set; } = string.Empty;
        public LogLevel Type { get; set; }
        public DateTime Date { get; set; }
        public string GroupName { get; set; }
    }
}