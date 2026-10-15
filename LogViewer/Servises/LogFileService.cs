using ConsoleApp1;
using LogViewer.Enums;
using LogViewer.Models;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LogViewer.Servises
{
    public static class LogFileService
    {
        private static readonly Regex LogLineRegex = new(@"^(?<date>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2},\d{3})\s+", RegexOptions.Compiled);

        public static async Task<List<LogRecord>> ReadLogFileAsync(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Не указан путь к файлу.", nameof(path));
            }

            string fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("Файл лога не найден.", fullPath);
            }

            var lines = await File.ReadAllLinesAsync(fullPath, Encoding.UTF8);
            var records = new List<LogRecord>();
            LogRecord? currentRecord = null;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentRecord != null)
                    {
                        currentRecord.Text += Environment.NewLine;
                    }

                    continue;
                }

                var match = LogLineRegex.Match(line);

                if (match.Success &&
                    DateTime.TryParseExact(
                        match.Groups["date"].Value,
                        "yyyy-MM-dd HH:mm:ss,fff",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var dateTime))
                {
                    currentRecord = new LogRecord
                    {
                        Text = line,
                        dateTime = dateTime,
                        File = Path.GetFileName(fullPath)
                    };

                    records.Add(currentRecord);
                }
                else if (currentRecord != null)
                {
                    // Продолжение предыдущей записи, например stack trace.
                    currentRecord.Text += Environment.NewLine + line;
                }
                else
                {
                    // В начале файла может быть строка без даты.
                    // Не теряем её и не прерываем чтение файла.
                    records.Add(new LogRecord
                    {
                        Text = line,
                        dateTime = DateTime.MinValue,
                        File = Path.GetFileName(fullPath)
                    });
                }
            }

            return records;
        }

        public static List<LogDirectorie> GetLogFiles(string directoriePath)
        {
            var result = new List<LogDirectorie>();
            foreach (var dir in Directory.GetDirectories(directoriePath))
            {
                LogDirectorie temp = new LogDirectorie() { Name = dir, ShortName = Path.GetFileName(dir.TrimEnd('\\')), Type = FileSystemType.Directory };
                result.Add(temp);
            }
            foreach (var file in Directory.GetFiles(directoriePath))
            {
                LogDirectorie temp = new LogDirectorie() { Name = file, ShortName = Path.GetFileName(file.TrimEnd('\\')), Type = FileSystemType.File };
                result.Add(temp);
            }

            return result;
        }

    }
}
