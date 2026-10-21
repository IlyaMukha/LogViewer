using System.Text.RegularExpressions;
using LogLevel = ConsoleApp1.LogLevel;


namespace LogViewer.Servises.Interfaces;

public interface ILogParsingRule
{
    bool IsMatch(string line, out Match match);
    (DateTime LogTime, LogLevel Level) ParseHeader(Match match);
}
