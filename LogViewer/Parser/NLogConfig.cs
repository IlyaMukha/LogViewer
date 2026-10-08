using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ConsoleApp1
{
    internal sealed class NLogTarget
    {
        public string Name { get; set; } = "";
        public string? FileName { get; set; }
        public string? Layout { get; set; }
        public string? Basename { get; set; }
        public bool IsJson { get; set; }
    }

    internal sealed class NLogRule
    {
        public string LoggerPattern { get; set; } = "";
        public string? WriteTo { get; set; }
        public string? MinLevel { get; set; }
        public string? MaxLevel { get; set; }
        public bool Final { get; set; }
    }

    internal sealed class NLogConfig
    {
        public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, NLogTarget> Targets { get; } = new(StringComparer.Ordinal);
        public List<NLogRule> Rules { get; } = new();

        private static readonly Regex VarRegex =
            new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", RegexOptions.Compiled);

        public string ExpandVariables(string text, int depth = 0)
        {
            if (depth > 20 || string.IsNullOrEmpty(text)) return text;
            return VarRegex.Replace(text, m =>
            {
                var name = m.Groups[1].Value;
                return Variables.TryGetValue(name, out var v)
                    ? ExpandVariables(v, depth + 1)
                    : m.Value;
            });
        }

        public static NLogConfig Load(string configPath)
        {
            var cfg = new NLogConfig();
            var root = LoadXmlWithIncludes(configPath);
            if (root == null) return cfg;

            var ns = root.Name.Namespace;

            foreach (var v in root.Descendants(ns + "variable"))
            {
                var name = (string?)v.Attribute("name");
                var value = (string?)v.Attribute("value");
                if (!string.IsNullOrEmpty(name) && value != null)
                    cfg.Variables[name] = value;
            }

            var targetsEl = root.Element(ns + "targets");
            if (targetsEl != null)
            {
                foreach (var t in targetsEl.Elements())
                {
                    if (t.Name.LocalName is "default-wrapper" or "default-target-parameters")
                        continue;

                    var name = (string?)t.Attribute("name");
                    if (string.IsNullOrEmpty(name)) continue;

                    var target = new NLogTarget
                    {
                        Name = name,
                        FileName = (string?)t.Attribute("fileName"),
                        Layout = (string?)t.Attribute("layout"),
                    };

                    var nestedLayout = t.Element(ns + "layout");
                    if (nestedLayout != null)
                    {
                        var typeAttr = nestedLayout.Attribute(
                            XName.Get("type", "http://www.w3.org/2001/XMLSchema-instance"));
                        if (typeAttr != null && typeAttr.Value.Contains("Json", StringComparison.OrdinalIgnoreCase))
                            target.IsJson = true;
                    }

                    if (target.FileName != null) target.FileName = cfg.ExpandVariables(target.FileName);
                    if (target.Layout != null) target.Layout = cfg.ExpandVariables(target.Layout);

                    if (target.FileName != null)
                    {
                        var idx = target.FileName.LastIndexOfAny(new[] { '/', '\\' });
                        var basename = idx >= 0 ? target.FileName[(idx + 1)..] : target.FileName;
                        if (!basename.Contains("${"))
                            target.Basename = basename;
                    }

                    cfg.Targets[name] = target;
                }
            }

            var rulesEl = root.Element(ns + "rules");
            if (rulesEl != null)
            {
                foreach (var r in rulesEl.Elements(ns + "logger"))
                {
                    cfg.Rules.Add(new NLogRule
                    {
                        LoggerPattern = (string?)r.Attribute("name") ?? "*",
                        WriteTo = (string?)r.Attribute("writeTo"),
                        MinLevel = (string?)r.Attribute("minlevel"),
                        MaxLevel = (string?)r.Attribute("maxlevel"),
                        Final = (string?)r.Attribute("final") == "true",
                    });
                }
            }

            return cfg;
        }

        private static XElement? LoadXmlWithIncludes(string path)
        {
            if (!File.Exists(path)) return null;

            XDocument doc;
            try { doc = XDocument.Load(path); }
            catch { return null; }

            var root = doc.Root;
            if (root == null) return null;

            var ns = root.Name.Namespace;
            var baseDir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";

            foreach (var include in root.Elements(ns + "include").ToList())
            {
                var fileAttr = (string?)include.Attribute("file");
                if (string.IsNullOrEmpty(fileAttr)) { include.Remove(); continue; }

                var includePath = Path.IsPathRooted(fileAttr)
                    ? fileAttr
                    : Path.Combine(baseDir, fileAttr);

                var includedRoot = LoadXmlWithIncludes(includePath);
                if (includedRoot != null)
                {
                    foreach (var child in includedRoot.Elements().ToList())
                    {
                        child.Remove();
                        include.AddBeforeSelf(child);
                    }
                }
                include.Remove();
            }

            return root;
        }
    }
}