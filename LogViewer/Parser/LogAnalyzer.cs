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
    /// <summary>
    /// Анализ лог-файлов в папке. На вход — папка с логами и (опционально) nlog.config.
    /// На выход — список уникальных сообщений с уровнем и количеством вхождений.
    /// </summary>
    public sealed class LogAnalyzer
    {
        // Признак начала новой записи (обычный текстовый формат логов).
        private static readonly Regex TimestampPrefixRegex = new(
            @"^\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}[,.]\d{3,}",
            RegexOptions.Compiled);

        // Резервный парсер для обычных текстовых логов.
        private static readonly Regex FallbackRegex = new(
            @"^(?<ts>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}[,.]\d{3,})\s+" +
            @"(?:\[(?<thread>[^\]]+)\]\s+)?" +
            @"(?<level>ERROR|WARN|WARNING|FATAL)\s+" +
            @"(?<logger>.*?)\s+-\s+(?<msg>.*)$",
            RegexOptions.Compiled);

        // "message: <текст>"
        private static readonly Regex MessageMarker = new(
            @"^\s*message:\s*(?<text>.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // "stack: <первая строка>"
        private static readonly Regex StackMarker = new(
            @"^\s*stack:\s*(?<text>.+?)\s*$",
            RegexOptions.Compiled | RegexOptions.Multiline);

        // ExceptionType (0x...): сообщение
        private static readonly Regex ExceptionMarker = new(
            @"(?<exType>(?:[A-Za-z_][A-Za-z0-9_]*\.)+[A-Za-z_][A-Za-z0-9_]*(?:Exception|Error))" +
            @"(?:\s+\(0x[0-9A-Fa-f]+\))?" +
            @"\s*:\s*(?<exMessage>[^\r\n|]+)",
            RegexOptions.Compiled);

        // Заголовок «сломанного» NLog JsonLayout: { "time": "...", ..., "level": "..."
        private static readonly Regex BrokenJsonHeader = new(
            @"^\{\s*""time""\s*:\s*""(?<ts>[^""]*)""\s*," +
            @"\s*""level""\s*:\s*""(?<level>[^""]*)""",
            RegexOptions.Compiled);

        // Универсальная выемка "key": "value" из заголовочной части записи.
        private static readonly Regex BrokenJsonField = new(
            @"""(?<key>[A-Za-z_][A-Za-z0-9_]*)""\s*:\s*""(?<val>[^""]*)""",
            RegexOptions.Compiled);

        /// <summary>
        /// Проанализировать все логи в папке.
        /// </summary>
        /// <param name="logsFolderPath">Путь к папке с логами (рекурсивно).</param>
        /// <param name="nlogConfigPath">Путь к nlog.config (может быть null).</param>
        /// <param name="ct">Токен отмены.</param>
        /// <returns>
        /// Список уникальных сообщений. Поля: Message (ошибка), Count (вхождения), Level (ERROR/FATAL/WARN).
        /// </returns>
        public async Task<List<LogEntry>> AnalyzeAsync(
            string logsFolderPath,
            string? nlogConfigPath = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(logsFolderPath))
                throw new ArgumentException("Путь к папке логов пуст.", nameof(logsFolderPath));
            if (!Directory.Exists(logsFolderPath))
                throw new DirectoryNotFoundException($"Папка не найдена: {logsFolderPath}");

            var basenameToTarget = new Dictionary<string, NLogTarget>(StringComparer.OrdinalIgnoreCase);
            var layoutRegexCache = new ConcurrentDictionary<string, Regex?>(StringComparer.Ordinal);

            // ---- nlog.config ----
            if (!string.IsNullOrWhiteSpace(nlogConfigPath) && File.Exists(nlogConfigPath))
            {
                try
                {
                    var nlog = NLogConfig.Load(nlogConfigPath);

                    foreach (var t in nlog.Targets.Values)
                        if (!string.IsNullOrEmpty(t.Basename))
                            basenameToTarget[t.Basename] = t;

                    // Динамические файлы (universalAppender и т.п.): достраиваем имя из правил.
                    foreach (var rule in nlog.Rules)
                    {
                        if (string.IsNullOrEmpty(rule.WriteTo)) continue;
                        if (!nlog.Targets.TryGetValue(rule.WriteTo, out var t)) continue;
                        if (!string.IsNullOrEmpty(t.Basename)) continue;
                        if (rule.LoggerPattern.Contains('*')) continue;

                        var shortName = rule.LoggerPattern.Split('.').Last();
                        if (string.IsNullOrEmpty(shortName)) continue;

                        basenameToTarget.TryAdd(shortName + ".log", t);
                    }
                }
                catch
                {
                    // как и раньше — просто игнорируем ошибку загрузки конфига
                }
            }

            var entries = new ConcurrentDictionary<string, LogEntry>(StringComparer.Ordinal);

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

                    // 1) Первая непустая строка — чтобы понять формат.
                    string? firstLine = null;
                    using (var sr = new StreamReader(file))
                    {
                        string? l;
                        while ((l = await sr.ReadLineAsync()) != null)
                        {
                            if (!string.IsNullOrWhiteSpace(l)) { firstLine = l; break; }
                        }
                    }

                    // 2) «Сломанный» NLog JsonLayout (encode="false").
                    if (firstLine != null && BrokenJsonHeader.IsMatch(firstLine))
                    {
                        await ProcessBrokenNLogJsonFileAsync(file, target, entries, token);
                        return;
                    }

                    // 3) Обычный JSON.
                    bool isJson = target?.IsJson == true ||
                                  basename.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
                    if (isJson)
                    {
                        await ProcessJsonFileAsync(file, target, entries, token);
                        return;
                    }

                    // 4) Обычный текст.
                    Regex? parser = null;
                    bool hasLevelGroup = true;
                    if (target?.Layout is { Length: > 0 } layout)
                    {
                        parser = layoutRegexCache.GetOrAdd(layout, l => LayoutParser.Compile(l));
                        if (parser != null)
                            hasLevelGroup = parser.GetGroupNames().Contains("level");
                    }

                    await ProcessTextFileAsync(file, parser, hasLevelGroup, target, entries, token);
                }
                catch
                {
                    // файл не прочитался — пропускаем
                }
            });

            return entries.Values
                .OrderByDescending(x => x.IsError)
                .ThenByDescending(x => x.Count)
                .ThenBy(x => x.Signature, StringComparer.Ordinal)
                .ToList();
        }

        // ---------------- Текстовые логи ----------------

        private static async Task ProcessTextFileAsync(
            string file,
            Regex? parser,
            bool hasLevelGroup,
            NLogTarget? target,
            ConcurrentDictionary<string, LogEntry> entries,
            CancellationToken ct)
        {
            var lines = await File.ReadAllLinesAsync(file, ct);

            string? headerLine = null;
            string level = "", logger = "", headerMsg = "", ts = "";
            var buffer = new StringBuilder();

            void Flush()
            {
                if (headerLine is null) return;

                var body = buffer.ToString();
                var signature = BuildSignature(level, logger, headerMsg, body);

                if (signature.Length >= 4 && signature.Any(char.IsLetter))
                {
                    var capturedLevel = level;
                    var capturedLogger = logger;
                    var capturedTs = ts;
                    var capturedFile = Path.GetFileName(file);

                    var entry = entries.GetOrAdd(signature, _ =>
                    {
                        var dashIdx = signature.IndexOf(" - ", StringComparison.Ordinal);
                        var pretty = dashIdx >= 0 ? signature[(dashIdx + 3)..] : signature;
                        return new LogEntry(capturedLevel, capturedLogger, pretty, signature);
                    });

                    Interlocked.Increment(ref entry.Count);

                    if (entry.FirstFile is null)
                    {
                        entry.FirstFile = capturedFile;
                        entry.FirstTimestamp = capturedTs;
                    }
                }

                headerLine = null;
                buffer.Clear();
            }

            foreach (var line in lines)
            {
                Match? m = null;

                if (parser != null)
                {
                    var mm = parser.Match(line);
                    if (mm.Success) m = mm;
                }

                if (m == null)
                {
                    var mm = FallbackRegex.Match(line);
                    if (mm.Success) m = mm;
                }

                if (m != null && m.Success)
                {
                    Flush();

                    var parsedLevel = m.Groups["level"].Success
                        ? m.Groups["level"].Value.ToUpperInvariant()
                        : "";
                    if (parsedLevel == "WARNING") parsedLevel = "WARN";

                    if (string.IsNullOrEmpty(parsedLevel) && !hasLevelGroup)
                    {
                        var raw = m.Groups["msg"].Success ? m.Groups["msg"].Value : line;
                        if (Regex.IsMatch(raw, @"\b(ERROR|FATAL)\b") || raw.Contains("Exception"))
                            parsedLevel = "ERROR";
                        else if (Regex.IsMatch(raw, @"\b(WARN(ING)?)\b"))
                            parsedLevel = "WARN";
                    }

                    if (parsedLevel is not ("ERROR" or "WARN" or "FATAL"))
                        continue;

                    level = parsedLevel;
                    ts = m.Groups["ts"].Success ? m.Groups["ts"].Value : "";
                    logger = m.Groups["logger"].Success ? m.Groups["logger"].Value.Trim() : "";
                    if (string.IsNullOrEmpty(logger) && target != null)
                        logger = target.Name;

                    headerMsg = m.Groups["msg"].Success ? m.Groups["msg"].Value.Trim() : "";
                    headerLine = line;
                    buffer.AppendLine(headerMsg);
                }
                else if (headerLine is not null)
                {
                    if (TimestampPrefixRegex.IsMatch(line))
                    {
                        Flush();
                        continue;
                    }
                    buffer.AppendLine(line);
                }
            }

            Flush();
        }

        // ---------------- Обычный JSON (построчный) ----------------

        private static async Task ProcessJsonFileAsync(
            string file,
            NLogTarget? target,
            ConcurrentDictionary<string, LogEntry> entries,
            CancellationToken ct)
        {
            var lines = await File.ReadAllLinesAsync(file, ct);

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line[0] != '{') continue;

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;

                    var level = GetString(root, "level", "Level", "lvl");
                    if (string.IsNullOrEmpty(level)) continue;
                    level = level.ToUpperInvariant();
                    if (level == "WARNING") level = "WARN";
                    if (level is not ("ERROR" or "WARN" or "FATAL")) continue;

                    var msg = GetString(root, "message", "Message", "msg");
                    if (string.IsNullOrEmpty(msg))
                    {
                        if (root.TryGetProperty("messageObject", out var mo) ||
                            root.TryGetProperty("MessageObject", out mo))
                        {
                            msg = mo.ValueKind == JsonValueKind.String
                                ? mo.GetString()
                                : mo.GetRawText();
                        }
                    }
                    if (string.IsNullOrEmpty(msg))
                        msg = GetString(root, "exception", "Exception");
                    if (string.IsNullOrEmpty(msg)) msg = line;

                    var logger = GetString(root, "logger", "Logger", "app", "App");
                    if (string.IsNullOrEmpty(logger) && target != null)
                        logger = target.Name;

                    var ts = GetString(root, "time", "Time", "d", "timestamp", "Timestamp", "@timestamp");
                    var ex = GetString(root, "exception", "Exception");

                    var body = msg;
                    if (!string.IsNullOrEmpty(ex) && !body.Contains(ex))
                        body = body + Environment.NewLine + ex;

                    var signature = BuildSignature(level, logger, msg, body);

                    if (signature.Length >= 4 && signature.Any(char.IsLetter))
                    {
                        var capturedLevel = level;
                        var capturedLogger = logger;
                        var capturedTs = ts;
                        var capturedFile = Path.GetFileName(file);

                        var entry = entries.GetOrAdd(signature, _ =>
                        {
                            var dashIdx = signature.IndexOf(" - ", StringComparison.Ordinal);
                            var pretty = dashIdx >= 0 ? signature[(dashIdx + 3)..] : signature;
                            return new LogEntry(capturedLevel, capturedLogger, pretty, signature);
                        });

                        Interlocked.Increment(ref entry.Count);

                        if (entry.FirstFile is null)
                        {
                            entry.FirstFile = capturedFile;
                            entry.FirstTimestamp = capturedTs;
                        }
                    }
                }
                catch
                {
                    // некорректная JSON-строка — пропускаем
                }
            }
        }

        // ---------------- «Сломанный» NLog JsonLayout ----------------

        private static async Task ProcessBrokenNLogJsonFileAsync(
            string file,
            NLogTarget? target,
            ConcurrentDictionary<string, LogEntry> entries,
            CancellationToken ct)
        {
            var lines = await File.ReadAllLinesAsync(file, ct);

            var records = new List<StringBuilder>();
            StringBuilder? current = null;

            foreach (var line in lines)
            {
                if (BrokenJsonHeader.IsMatch(line))
                {
                    current = new StringBuilder();
                    records.Add(current);
                }
                current?.AppendLine(line);
            }

            foreach (var sb in records)
            {
                var record = sb.ToString();
                var head = BrokenJsonHeader.Match(record);
                if (!head.Success) continue;

                var level = head.Groups["level"].Value.ToUpperInvariant();
                if (level == "WARNING") level = "WARN";
                if (level is not ("ERROR" or "WARN" or "FATAL")) continue;

                var ts = head.Groups["ts"].Value;
                var app = ExtractBrokenField(record, "app");
                var logger = !string.IsNullOrEmpty(app) ? app : (target?.Name ?? "?");

                var msg = ExtractBrokenMessage(record);

                var signature = BuildSignature(level, logger, msg, msg);
                if (signature.Length < 4 || !signature.Any(char.IsLetter)) continue;

                var capturedLevel = level;
                var capturedLogger = logger;
                var capturedTs = ts;
                var capturedFile = Path.GetFileName(file);

                var entry = entries.GetOrAdd(signature, _ =>
                {
                    var dashIdx = signature.IndexOf(" - ", StringComparison.Ordinal);
                    var pretty = dashIdx >= 0 ? signature[(dashIdx + 3)..] : signature;
                    return new LogEntry(capturedLevel, capturedLogger, pretty, signature);
                });

                Interlocked.Increment(ref entry.Count);

                if (entry.FirstFile is null)
                {
                    entry.FirstFile = capturedFile;
                    entry.FirstTimestamp = capturedTs;
                }
            }
        }

        private static string ExtractBrokenField(string record, string key)
        {
            foreach (Match m in BrokenJsonField.Matches(record))
            {
                if (string.Equals(m.Groups["key"].Value, key, StringComparison.Ordinal))
                    return m.Groups["val"].Value;
            }
            return "";
        }

        private static string ExtractBrokenMessage(string record)
        {
            var idx = record.IndexOf("\"message\"", StringComparison.Ordinal);
            if (idx < 0) return record.Trim();

            var colon = record.IndexOf(':', idx);
            if (colon < 0) return record.Trim();

            var body = record[(colon + 1)..].Trim();
            body = Regex.Replace(body, @"\s*\}\s*\}\s*$", "").Trim();

            var errMatch = Regex.Match(body, @"ErrorMessage\s*=\s*(?<err>[^\r\n]+)");
            if (errMatch.Success)
                return errMatch.Groups["err"].Value.Trim();

            return body;
        }

        private static string GetString(JsonElement obj, params string[] names)
        {
            foreach (var n in names)
            {
                if (obj.TryGetProperty(n, out var v))
                {
                    return v.ValueKind switch
                    {
                        JsonValueKind.String => v.GetString() ?? "",
                        JsonValueKind.Number => v.ToString(),
                        JsonValueKind.True => "true",
                        JsonValueKind.False => "false",
                        JsonValueKind.Null => "",
                        _ => v.GetRawText()
                    };
                }
            }
            return "";
        }

        // ---------------- Сигнатура ----------------

        private static string BuildSignature(string level, string logger, string headerMsg, string fullBody)
        {
            var mm = MessageMarker.Match(fullBody);
            if (mm.Success)
                return $"{level}|{logger} - {mm.Groups["text"].Value.Trim()}";

            var em = ExceptionMarker.Match(fullBody);
            if (em.Success)
            {
                var exType = em.Groups["exType"].Value;
                var exMsg = em.Groups["exMessage"].Value.Trim();
                return $"{level}|{logger} - {exType}: {exMsg}";
            }

            var sm = StackMarker.Match(fullBody);
            if (sm.Success)
                return $"{level}|{logger} - {sm.Groups["text"].Value.Trim()}";

            var body = fullBody.Trim();
            if (body.Length == 0) body = headerMsg;
            body = Regex.Replace(body, @"[ \t]+", " ");
            body = Regex.Replace(body, @"(\r?\n)+", " ").Trim();
            return $"{level}|{logger} - {body}";
        }
    }
}