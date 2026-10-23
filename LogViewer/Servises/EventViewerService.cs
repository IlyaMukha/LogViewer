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
            var result = new List<LogEntity>();
            var helperList = new List<HelperModel>();
            foreach (string logName in logNames)
            {
                try
                {
                    var conditions = new List<string>();

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

                    string query = conditions.Count > 0 ? $"*[System[{string.Join(" and ", conditions)}]]" : "*";

                    using var reader = new EventLogReader(
                        new EventLogQuery(logName, PathType.LogName, query)
                        {
                            ReverseDirection = true
                        }
                    );

                    while (true)
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
                            HelperModel helper = new HelperModel();
                            string description = "";
                            try
                            {
                                description = record.FormatDescription() ?? "(нет описания)";
                            }
                            catch (Exception ex)
                            {
                                description = $"Не удалось получить описание события: {ex.Message}";
                            }


                            helper.Date = record.TimeCreated ?? DateTime.MinValue; // Дата ошибки
                            helper.Text = record.ProviderName; // инициатор ошибки (.NET или IIS POOL)
                            helper.Description = description; // Описание ошибки
                            helper.Type = GetLogLevel(record.Level); // Тип ошибки
                            helper.GroupName = logName; // Группа System или Application
                            helperList.Add(helper);
                        }
                    }

                }
                catch (Exception ex)
                {
                    return result;
                }
            }

            Console.WriteLine(helperList); // Список всех ошибок изи Просмотра событий
            // Нужно это програть через алгоритм и вернуть List<LogEntity>


            return result;
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
