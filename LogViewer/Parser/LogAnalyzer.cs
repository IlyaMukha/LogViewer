using LogViewer.Parser;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ConsoleApp1
{
    public sealed class LogAnalyzer
    {
        private static readonly Regex TimestampPrefixRegex = new(
            @"^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}[,.]\d{3,}",
            RegexOptions.Compiled);

        private static readonly Regex FallbackRegex = new(
            @"^(?<ts>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}[,.]\d{3,})\s+" +
            @"(?:\[(?<thread>[^\]]+)\]\s+)?" +
            @"(?<level>ERROR|WARN|WARNING|FATAL)\s+" +
            @"(?:.*?\s+-\s+)?(?<msg>.*)$",
            RegexOptions.Compiled);

        private static readonly Regex ExceptionMarker = new(
            @"(?<exType>(?:[A-Za-z_][A-Za-z0-9_]*\.)+[A-Za-z_][A-Za-z0-9_]*(?:Exception|Error))" +
            @"(?:\s+\(0x[0-9A-Fa-f]+\))?" +
            @"\s*:\s*(?<exMessage>[^\r\n|]+)",
            RegexOptions.Compiled);

        private static readonly Regex BrokenJsonHeader = new(
            @"^\{\s*""time""\s*:\s*""(?<ts>[^""]*)""\s*," +
            @"\s*""level""\s*:\s*""(?<level>[^""]*)""",
            RegexOptions.Compiled);

        private static LogLevel ParseLevel(string s) => s switch
        {
            "FATAL" => LogLevel.Fatal,
            "ERROR" => LogLevel.Error,
            _ => LogLevel.Warn
        };

        // ============================================================
        //  Сгруппированный список (LogEntry).
        // ============================================================
        public async Task<List<LogEntry>> AnalyzeAsync(
            string logsFolderPath,
            string? nlogConfigPath = null,
            CancellationToken ct = default)
        {
            var records = await ReadAllRecordsAsync(logsFolderPath, nlogConfigPath, ct);

            var dict = new Dictionary<string, LogEntry>(StringComparer.Ordinal);

            foreach (var r in records)
            {
                if (!dict.TryGetValue(r.Signature, out var entry))
                {
                    entry = new LogEntry
                    {
                        Type = r.Level,
                        Title = r.Title,
                        FullText = r.FullText
                    };
                    dict[r.Signature] = entry;
                }
                entry.Exceptions.Add(r.Record);
            }

            foreach (var e in dict.Values)
                e.Exceptions.Sort((a, b) => a.dateTime.CompareTo(b.dateTime));

            return dict.Values
                .OrderByDescending(x => x.Type is LogLevel.Error or LogLevel.Fatal)
                .ThenByDescending(x => x.Exceptions.Count)
                .ToList();
        }

        // ============================================================
        //  Плоский список.
        // ============================================================
        public async Task<List<LogRecord>> AnalyzeFlatAsync(
            string logsFolderPath,
            string? nlogConfigPath = null,
            CancellationToken ct = default)
        {
            var records = await ReadAllRecordsAsync(logsFolderPath, nlogConfigPath, ct);
            return records
                .Select(x => x.Record)
                .OrderBy(x => x.dateTime)
                .ThenBy(x => x.File, StringComparer.Ordinal)
                .ToList();
        }

        // ============================================================
        //  Промежуточная модель.
        // ============================================================
        private sealed class Parsed
        {
            public LogLevel Level;
            public string Signature = "";
            public string Title = "";      // первая строка
            public string FullText = "";   // заголовок + все продолжения
            public LogRecord Record = null!;
        }

        private async Task<List<Parsed>> ReadAllRecordsAsync(
            string logsFolderPath, string? nlogConfigPath, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(logsFolderPath))
                throw new ArgumentException("Путь к папке логов пуст.", nameof(logsFolderPath));
            if (!Directory.Exists(logsFolderPath))
                throw new DirectoryNotFoundException($"Папка не найдена: {logsFolderPath}");

            var basenameToTarget = new Dictionary<string, NLogTarget>(StringComparer.OrdinalIgnoreCase);
            var layoutRegexCache = new ConcurrentDictionary<string, Regex?>(StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(nlogConfigPath) && File.Exists(nlogConfigPath))
            {
                try
                {
                    var nlog = NLogConfig.Load(nlogConfigPath);
                    foreach (var t in nlog.Targets.Values)
                        if (!string.IsNullOrEmpty(t.Basename))
                            basenameToTarget[t.Basename] = t;
                }
                catch { }
            }

            var sink = new ConcurrentBag<Parsed>();

            var files = Directory.EnumerateFiles(logsFolderPath, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                    f.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                    f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                .ToList();

            await Parallel.ForEachAsync(files, ct, async (file, token) =>
            {
                try
                {
                    var basename = Path.GetFileName(file);
                    basenameToTarget.TryGetValue(basename, out var target);

                    string? firstLine = null;
                    using (var sr = new StreamReader(file))
                    {
                        string? l;
                        while ((l = await sr.ReadLineAsync()) != null)
                        {
                            if (!string.IsNullOrWhiteSpace(l)) { firstLine = l; break; }
                        }
                    }

                    if (firstLine != null && BrokenJsonHeader.IsMatch(firstLine))
                    {
                        await ProcessBrokenJsonAsync(file, sink, token);
                        return;
                    }

                    bool isJson = target?.IsJson == true ||
                                  basename.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                    if (isJson)
                    {
                        await ProcessJsonAsync(file, sink, token);
                        return;
                    }

                    Regex? parser = null;
                    if (target?.Layout is { Length: > 0 } layout)
                        parser = layoutRegexCache.GetOrAdd(layout, LayoutParser.Compile);

                    await ProcessTextAsync(file, parser, sink, token);
                }
                catch { }
            });

            return sink.ToList();
        }

        // ---------------- Текст ----------------

        private static async Task ProcessTextAsync(
            string file,
            Regex? parser,
            ConcurrentBag<Parsed> sink,
            CancellationToken ct)
        {
            var lines = await File.ReadAllLinesAsync(file, ct);
            var fileName = Path.GetFileName(file);

            string? open = null;
            string level = "", headerMsg = "", ts = "";
            var buffer = new StringBuilder();

            void Flush()
            {
                if (open is null) return;

                var fullBody = buffer.ToString().TrimEnd();
                var signature = BuildSignature(level, headerMsg, fullBody);

                if (signature.Length >= 4 && signature.Any(char.IsLetter))
                {
                    sink.Add(new Parsed
                    {
                        Level = ParseLevel(level),
                        Signature = signature,
                        Title = headerMsg,
                        FullText = fullBody,
                        Record = LogRecord.Create(headerMsg, ts, fileName)
                    });
                }

                open = null;
                buffer.Clear();
            }

            foreach (var line in lines)
            {
                Match? m = parser?.Match(line);
                if (m == null || !m.Success) m = FallbackRegex.Match(line);

                if (m.Success)
                {
                    Flush();

                    var lvl = m.Groups["level"].Success
                        ? m.Groups["level"].Value.ToUpperInvariant() : "";
                    if (lvl == "WARNING") lvl = "WARN";
                    if (lvl is not ("ERROR" or "WARN" or "FATAL")) continue;

                    level = lvl;
                    ts = m.Groups["ts"].Success ? m.Groups["ts"].Value : "";
                    headerMsg = m.Groups["msg"].Success ? m.Groups["msg"].Value.Trim() : "";
                    open = line;
                    buffer.AppendLine(headerMsg);
                }
                else if (open is not null)
                {
                    if (TimestampPrefixRegex.IsMatch(line)) { Flush(); continue; }
                    buffer.AppendLine(line);
                }
            }

            Flush();
        }

        // ---------------- JSON ----------------

        private static async Task ProcessJsonAsync(
            string file,
            ConcurrentBag<Parsed> sink,
            CancellationToken ct)
        {
            var lines = await File.ReadAllLinesAsync(file, ct);
            var fileName = Path.GetFileName(file);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line) || line[0] != '{') continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;

                    var level = GetString(root, "level", "Level", "lvl").ToUpperInvariant();
                    if (level == "WARNING") level = "WARN";
                    if (level is not ("ERROR" or "WARN" or "FATAL")) continue;

                    var msg = GetString(root, "message", "Message", "msg");
                    if (string.IsNullOrEmpty(msg)) msg = GetString(root, "exception", "Exception");
                    if (string.IsNullOrEmpty(msg)) msg = line;

                    var ts = GetString(root, "time", "Time", "d", "timestamp", "Timestamp", "@timestamp");

                    var signature = BuildSignature(level, msg, msg);
                    if (signature.Length < 4 || !signature.Any(char.IsLetter)) continue;

                    sink.Add(new Parsed
                    {
                        Level = ParseLevel(level),
                        Signature = signature,
                        Title = msg,
                        FullText = msg,
                        Record = LogRecord.Create(msg, ts, fileName)
                    });
                }
                catch { }
            }
        }

        private static async Task ProcessBrokenJsonAsync(
            string file,
            ConcurrentBag<Parsed> sink,
            CancellationToken ct)
        {
            var lines = await File.ReadAllLinesAsync(file, ct);
            var fileName = Path.GetFileName(file);

            var chunks = new List<StringBuilder>();
            StringBuilder? cur = null;

            foreach (var line in lines)
            {
                if (BrokenJsonHeader.IsMatch(line))
                {
                    cur = new StringBuilder();
                    chunks.Add(cur);
                }
                cur?.AppendLine(line);
            }

            foreach (var sb in chunks)
            {
                var record = sb.ToString();
                var head = BrokenJsonHeader.Match(record);
                if (!head.Success) continue;

                var level = head.Groups["level"].Value.ToUpperInvariant();
                if (level == "WARNING") level = "WARN";
                if (level is not ("ERROR" or "WARN" or "FATAL")) continue;

                var ts = head.Groups["ts"].Value;
                var msg = ExtractBrokenMessage(record);

                var signature = BuildSignature(level, msg, msg);
                if (signature.Length < 4 || !signature.Any(char.IsLetter)) continue;

                sink.Add(new Parsed
                {
                    Level = ParseLevel(level),
                    Signature = signature,
                    Title = msg,
                    FullText = record,
                    Record = LogRecord.Create(msg, ts, fileName)
                });
            }
        }

        private static string ExtractBrokenMessage(string record)
        {
            var idx = record.IndexOf("\"message\"", StringComparison.Ordinal);
            if (idx < 0) return record.Trim();
            var colon = record.IndexOf(':', idx);
            if (colon < 0) return record.Trim();
            var body = record[(colon + 1)..].Trim();
            body = Regex.Replace(body, @"\s*\}\s*\}\s*$", "").Trim();
            return body;
        }

        private static string GetString(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
                if (obj.TryGetProperty(n, out var v))
                    return v.ValueKind switch
                    {
                        JsonValueKind.String => v.GetString() ?? "",
                        JsonValueKind.Null => "",
                        _ => v.GetRawText()
                    };
            return "";
        }

        // Сигнатура — только для группировки (в UI не выводится).
        private static string BuildSignature(string level, string headerMsg, string fullBody)
        {
            var em = ExceptionMarker.Match(fullBody);
            if (em.Success)
                return $"{level}|{em.Groups["exType"].Value}: {em.Groups["exMessage"].Value.Trim()}";

            var body = fullBody.Trim();
            if (body.Length == 0) body = headerMsg;
            body = Regex.Replace(body, @"[ \t]+", " ");
            body = Regex.Replace(body, @"(\r?\n)+", " ").Trim();
            return $"{level}|{body}";
        }
    }
}