using System;
using System.Text;
using System.Text.RegularExpressions;

namespace LogViewer.Parser
{
    internal static class LayoutParser
    {
        public static Regex? Compile(string layout)
        {
            if (string.IsNullOrEmpty(layout)) return null;

            var sb = new StringBuilder();
            sb.Append('^');

            int i = 0;
            while (i < layout.Length)
            {
                var c = layout[i];

                // NLog-токен ${...} (с поддержкой вложенных фигурных скобок)
                if (c == '$' && i + 1 < layout.Length && layout[i + 1] == '{')
                {
                    int depth = 1;
                    int j = i + 2;
                    while (j < layout.Length && depth > 0)
                    {
                        if (layout[j] == '{') depth++;
                        else if (layout[j] == '}') depth--;
                        j++;
                    }
                    sb.Append(TokenToRegex(layout.Substring(i, j - i)));
                    i = j;
                    continue;
                }

                // пробельные последовательности → \s+
                if (char.IsWhiteSpace(c))
                {
                    while (i < layout.Length && char.IsWhiteSpace(layout[i])) i++;
                    sb.Append(@"\s+");
                    continue;
                }

                sb.Append(Regex.Escape(c.ToString()));
                i++;
            }

            sb.Append('$');

            try { return new Regex(sb.ToString(), RegexOptions.Compiled); }
            catch { return null; }
        }

        private static string TokenToRegex(string token)
        {
            // token = ${ ... }
            var inner = token.Substring(2, token.Length - 3);

            if (inner.StartsWith("uppercase:"))
                return @"(?<level>TRACE|DEBUG|INFO|WARNING|WARN|ERROR|FATAL)";
            if (inner.StartsWith("lowercase:"))
                return @"(?<level>trace|debug|info|warning|warn|error|fatal)";
            if (inner.StartsWith("whenEmpty:") || inner.StartsWith("padding:"))
                return @"\S*?";

            var name = inner.Split(':')[0];

            switch (name)
            {
                case "date":
                case "longdate":
                case "shortdate":
                    return @"(?<ts>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[,.]\d{1,7})?)";
                case "level":
                    return @"(?<level>TRACE|DEBUG|INFO|WARNING|WARN|ERROR|FATAL)";
                case "logger":
                    return @"(?<logger>\S+)";
                case "message":
                case "messageObject":
                    return @"(?<msg>.*)";
                case "threadid":
                case "threadname":
                    return @"(?<thread>\S*?)";
                case "callsite":
                    return @"\S*?";
                case "aspnet-user-identity":
                    return @"\S*?";
                default:
                    return @"\S*?";
            }
        }
    }
}