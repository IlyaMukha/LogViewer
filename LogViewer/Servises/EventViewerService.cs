using ConsoleApp1;
using System.Diagnostics.Eventing.Reader;
using LogLevel = ConsoleApp1.LogLevel;

namespace LogViewer.Servises
{
    public static class EventViewerService
    {
        public static readonly string[] logNames = ["Application", "System"];

        public static List<LogEntity> GetEvetViewerLog(DateTime? StartDate = null, DateTime? EndDate = null)
        {
            EndDate ??= DateTime.Now;
            StartDate ??= EndDate.Value.AddDays(-7);

            if (StartDate > EndDate)
                throw new ArgumentException(
                    "Дата начала не может быть больше даты окончания.");

            var result = new List<LogEntity>();

            foreach (string logName in logNames)
            {
                try
                {
                    string startDate = StartDate.Value
                        .ToUniversalTime()
                        .ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                    string endDate = EndDate.Value
                        .ToUniversalTime()
                        .ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

                    string query =
                        $"*[System[" +
                        $"TimeCreated[@SystemTime >= '{startDate}' " +
                        $"and @SystemTime <= '{endDate}']" +
                        $"]]";

                    using var reader = new EventLogReader(
                        new EventLogQuery(
                            logName,
                            PathType.LogName,
                            query)
                        {
                            ReverseDirection = true
                        });

                    while (true)
                    {
                        EventRecord? record;

                        try
                        {
                            record = reader.ReadEvent();
                        }
                        catch (EventLogException ex)
                        {
                            Console.Error.WriteLine(
                                $"Ошибка чтения журнала {logName}: {ex.Message}");

                            break;
                        }

                        if (record == null)
                            break;

                        using (record)
                        {
                            result.Add(ConvertToLogEntity(record, logName));
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"Ошибка обработки журнала {logName}: {ex.Message}");
                }
            }

            return GroupLogs(result);
        }

        private static List<LogEntity> GroupLogs(List<LogEntity> logs)
        {
            return logs
                .GroupBy(x => new
                {
                    Title = LogFileService.NormalizeText(x.Title),
                    x.Type,
                    File = x.LogOfImpactList.FirstOrDefault()?.File
                })
                .Select(group =>
                {
                    var orderedLogs = group
                        .OrderBy(x => x.StartDate)
                        .ToList();

                    var firstLog = orderedLogs[0];

                    return new LogEntity
                    {
                        Id = firstLog.Id,
                        Title = group.Key.Title,
                        FullText = firstLog.FullText,
                        Type = group.Key.Type,
                        StartDate = orderedLogs.Min(x => x.StartDate),
                        EndDate = orderedLogs.Max(x => x.EndDate),
                        LogOfImpactList = orderedLogs
                            .SelectMany(x => x.LogOfImpactList)
                            .OrderByDescending(x => x.Time)
                            .ToList()
                    };
                })
                .OrderByDescending(x => x.EndDate)
                .ToList();
        }

        private static LogEntity ConvertToLogEntity(
            EventRecord record,
            string logName)
        {
            DateTime logTime = record.TimeCreated ?? DateTime.MinValue;

            string provider = record.ProviderName ?? "Unknown";

            string description;

            try
            {
                description = record.FormatDescription()
                    ?? "(нет описания)";
            }
            catch (EventLogException)
            {
                description = "(не удалось получить описание события)";
            }

            LogLevel level = GetLogLevel(record.Level);

            string title = $"{provider} - {description}";

            // Нормализация применяется только к заголовку.
            // Полное описание события сохраняется без изменений.
            string normalizedTitle = LogFileService.NormalizeText(title);

            return new LogEntity
            {
                Id = Guid.NewGuid(),
                Title = normalizedTitle,
                FullText = title,
                Type = level,
                StartDate = logTime,
                EndDate = logTime,
                LogOfImpactList =
                [
                    new LogOfImpact
                    {
                        Text = description,
                        File = logName,
                        Time = logTime
                    }
                ]
            };
        }

        private static LogLevel GetLogLevel(byte? lvl)
        {
            return lvl switch
            {
                0 => LogLevel.Unknow,
                1 => LogLevel.Fatal,
                2 => LogLevel.Error,
                3 => LogLevel.Warn,
                4 => LogLevel.Info,
                5 => LogLevel.Debug,
                _ => LogLevel.Unknow
            };
        }
    }
}
