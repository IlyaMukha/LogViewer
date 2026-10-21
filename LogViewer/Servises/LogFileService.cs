using ConsoleApp1;
using LogViewer.Enums;
using LogViewer.Models;
using LogViewer.Servises.Interfaces;
using LogViewer.Servises.LogRule;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using LogLevel = ConsoleApp1.LogLevel;

namespace LogViewer.Servises;

public static partial class LogFileService
{
    private static readonly List<ILogParsingRule> ParsingRules =
    [
        new ApplicationLogRule(),
        new WebServerLogRule()
    ];
    private static readonly Regex GuidRegex = new Regex(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        RegexOptions.Compiled);

    private static readonly Regex WebLogCleanRegex = new Regex(
        @"(GET|POST|PUT|DELETE)\s+([^\s?]+)[^\s]*\s+\d+\s+-\s+::1\s+.*?(http[^\s]+)?\s+(\d{3})\s+\d+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex FkRegex = new Regex(
        @"\bFK[a-zA-Z0-9]{15,40}\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

    public static async Task<List<LogEntity>> ReadLogFileNewAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Не указан путь к файлу.", nameof(path));

        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("Файл лога не найден.", fullPath);

        var result = new List<LogEntity>();
        LogEntity? currentLog = null;
        var currentBody = new StringBuilder();

        await foreach (var line in File.ReadLinesAsync(fullPath, Encoding.UTF8))
        {
            if (line.StartsWith('#')) continue;

            ILogParsingRule? matchedRule = null;
            Match? matchedMatch = null;

            foreach (var rule in ParsingRules)
            {
                if (rule.IsMatch(line, out var match))
                {
                    matchedRule = rule;
                    matchedMatch = match;
                    break;
                }
            }

            if (matchedRule != null && matchedMatch != null)
            {
                if (currentLog != null)
                {
                    currentLog.FullText = currentBody.ToString().TrimEnd();
                    result.Add(currentLog);
                }

                var (logTime, level) = matchedRule.ParseHeader(matchedMatch);

                currentLog = new LogEntity
                {
                    Id = Guid.NewGuid(),
                    Type = level,
                    StartDate = logTime,
                    EndDate = logTime,
                    Title = line.Substring(matchedMatch.Length).Trim()
                };

                currentLog.LogOfImpactList.Add(new LogOfImpact
                {
                    Text = currentBody.ToString().TrimEnd(),
                    File = Path.GetFileName(path),
                    Time = logTime
                });

                currentBody.Clear();
                currentBody.AppendLine(line);
            }
            else
            {
                if (currentLog != null)
                {
                    currentBody.AppendLine(line);
                }
                else if (!string.IsNullOrWhiteSpace(line))
                {
                    result.Add(new LogEntity
                    {
                        Type = LogLevel.Unknow,
                        Title = "Неизвестный фрагмент (без даты начала)",
                        FullText = line,
                    });
                }
            }
        }

        if (currentLog != null)
        {
            currentLog.FullText = currentBody.ToString().TrimEnd();
            result.Add(currentLog);
        }

        return result;
    }

    public static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        string decoded = text.Replace("\\u002B", "+");
        try
        {
            decoded = Uri.UnescapeDataString(decoded);
        }
        catch { }

        decoded = decoded.Replace("\\u0022", "\"");

        decoded = decoded.Replace("\\u0027", "'");
        decoded = decoded.Replace("\\u003E", ">");
        decoded = decoded.Replace("\\u003C", "<"); 


        decoded = GuidRegex.Replace(decoded, "*");
        decoded = FkRegex.Replace(decoded, "*");

        var webMatch = WebLogCleanRegex.Match(decoded);
        if (webMatch.Success)
        {
            string method = webMatch.Groups[1].Value.ToUpper();
            string urlPath = webMatch.Groups[2].Value;
            string statusCode = webMatch.Groups[4].Value;
            return $"{method} {urlPath} - {statusCode}";
        }

        return decoded.Trim();
    }
}
