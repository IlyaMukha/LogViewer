using LogViewer.Servises.Interfaces;
using System.Globalization;
using System.Text.RegularExpressions;
using LogLevel = ConsoleApp1.LogLevel;

namespace LogViewer.Servises.Abstractions;

public abstract class BaseLogRule : ILogParsingRule
{
    protected abstract Regex HeaderRegex { get; }
    protected abstract string DateTimeFormat { get; }

    public bool IsMatch(string line, out Match match)
    {
        match = HeaderRegex.Match(line);
        return match.Success;
    }

    public virtual (DateTime LogTime, LogLevel Level) ParseHeader(Match match)
    {
        string dateStr = match.Groups[1].Value.Replace(',', '.');
        string levelStr = match.Groups.Count > 2 ? match.Groups[2].Value : string.Empty;

        DateTime.TryParseExact(dateStr, DateTimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt);

        return (dt, ParseLogLevel(levelStr));
    }

    private static LogLevel ParseLogLevel(string levelStr)
    {
        if (string.IsNullOrWhiteSpace(levelStr)) return LogLevel.Unknow;
        return levelStr.Trim().ToUpper() switch
        {
            "INFO" => LogLevel.Info,
            "WARN" => LogLevel.Warn,
            "ERROR" => LogLevel.Error,
            "FATAL" => LogLevel.Fatal,
            "DEBUG" => LogLevel.Debug,
            _ => LogLevel.Unknow
        };
    }
}
