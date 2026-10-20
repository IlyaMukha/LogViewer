using ConsoleApp1;
using System.Diagnostics.Eventing.Reader;
using LogLevel = ConsoleApp1.LogLevel;

namespace LogViewer.Servises
{
    public static class EventViewerService
    {
        //public static readonly int maxEvents = 100;
        //public static readonly DateTime startTime = DateTime.Now.AddDays(-1);
        public static readonly string[] logNames = ["Application", "System"];
        public static List<LogEntry> GetEvetViewerLog(DateTime? StartDate = null, DateTime? EndDate = null)
        {
            var result = new List<LogEntry>();
            foreach (string logName in logNames)
            {
                try
                {
                    var conditions = new List<string>{"(Level=1 or Level=2)"};

                    if (StartDate.HasValue)
                    {
                        string startDate = StartDate.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                        conditions.Add($"TimeCreated[@SystemTime >= '{startDate}']");
                    }

                    if (EndDate.HasValue)
                    {
                        string endDate = EndDate.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                        conditions.Add($"TimeCreated[@SystemTime <= '{endDate}']");
                    }

                    string query =$"*[System[{string.Join(" and ", conditions)}]]";

                    using var reader = new EventLogReader(
                        new EventLogQuery(logName, PathType.LogName, query)
                        {
                            ReverseDirection = true
                        }
                    );

                    LogEntry temp = new LogEntry
                    {
                        Title = logName,
                        Type = LogLevel.Fatal
                    };

                    while(true)
                    {
                        EventRecord? record;

                        try
                        {
                            record = reader.ReadEvent();
                        }
                        catch (EventLogException)
                        {
                            break;
                        }

                        if (record == null)
                            break;

                        using (record)
                        {
                            temp.AllLogs.Add(new LogRecord
                            {
                                Text = $"{record.ProviderName}\n{record.FormatDescription() ?? "(нет описания)"}",
                                dateTime = record.TimeCreated ?? DateTime.MinValue,
                                Type = GetLogLevel(record.Level)
                            });
                        }
                    }

                    result.Add(temp);
                }
                catch (Exception ex)
                {
                    return result;
                }
            }
            return result;
        }

        private static LogLevel GetLogLevel(byte? lvl)
        {
            return lvl switch
            {
                1 => LogLevel.Error,
                2 => LogLevel.Error,
                3 => LogLevel.Warn,
                4 => LogLevel.Fatal,
                5 => LogLevel.Fatal,
                _ => LogLevel.Fatal
            };
        }
    }
}
