using LogViewer.Servises.Abstractions;
using System.Text.RegularExpressions;
using LogLevel = ConsoleApp1.LogLevel;

namespace LogViewer.Servises.LogRule;

public class WebServerLogRule : BaseLogRule
{
    protected override Regex HeaderRegex { get; } = new(
        @"^(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2})\s+[\:\d\w\.]+\s+(?:POST|GET|PUT|DELETE)\s*",
        RegexOptions.Compiled);

    protected override string DateTimeFormat => "yyyy-MM-dd HH:mm:ss";

    public override (DateTime LogTime, LogLevel Level) ParseHeader(Match match)
    {
        var baseResult = base.ParseHeader(match);
        return (baseResult.LogTime, LogLevel.Info);
    }
}
