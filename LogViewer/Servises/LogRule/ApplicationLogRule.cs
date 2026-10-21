using LogViewer.Servises.Abstractions;
using System.Text.RegularExpressions;

namespace LogViewer.Servises.LogRule;

public class ApplicationLogRule : BaseLogRule
{
    protected override Regex HeaderRegex { get; } = new(
        @"^(\d{4}-\d{2}-\d{2}\s\d{2}:\d{2}:\d{2},\d{3})\s+(?:\[.*?\]\s+)?([A-Z]{4,5})?\s*",
        RegexOptions.Compiled);

    protected override string DateTimeFormat => "yyyy-MM-dd HH:mm:ss.fff";
}
