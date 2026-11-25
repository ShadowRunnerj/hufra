using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Hufra.Core.Models;
using Hufra.Core.Safety;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// بحث شامل: يمسح كل الملفات النصية المفتوحة داخل اللعبة بأي محرك (JSON، INI/properties، YAML، CSV/TSV، XML/HTML،
    /// TXT، وسكربتات lua/js/gd بنصوص جمل واضحة) ويستخرج النصوص القابلة للعرض. يُستخدم تلقائياً لما محرك اللعبة
    /// غير معروف أو ما أعطى نتائج. لا يدخل الملفات الثنائية/المحزومة.
    /// </summary>
    public class UniversalTextAdapter : IEngineAdapter
    {
        public EngineType Engine => EngineType.Universal;
        public string DisplayName => "بحث شامل في الملفات النصية";

        private const long MaxFileBytes = 8L * 1024 * 1024;
        private const int MaxEntries = 30000;
        private const int MaxFiles = 5000;

        private static readonly HashSet<string> TextExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".text", ".md", ".ini", ".cfg", ".conf", ".properties", ".lang", ".yml", ".yaml",
            ".csv", ".tsv", ".xml", ".xaml", ".resx", ".html", ".htm", ".lua", ".js", ".gd"
        };

        private static readonly string[] SkipDirs =
            { "\\bepinex\\", "\\node_modules\\", "\\.git\\", "\\logs\\", "\\crashes\\", "\\mono\\", "\\managed\\", "\\_redist", "\\directx", "\\vcredist" };

        public int DetectConfidence(string gameFolderPath) => 0; // لا يُكتشف تلقائياً؛ يُستخدم كخيار احتياطي

        private sealed class Span
        {
            public int Start, Length;
            public string Text;
            public Func<string, string> Escape;
        }

        // ---------------- اختيار الملفات ----------------
        private static IEnumerable<string> Files(string root)
        {
            IEnumerable<string> all = SafeEnumerate(root);

            int n = 0;
            foreach (var f in all)
            {
                string ext = Path.GetExtension(f);
                if (!TextExts.Contains(ext) && !ext.Equals(".json", StringComparison.OrdinalIgnoreCase)) continue;
                string lower = f.ToLowerInvariant();
                if (SkipDirs.Any(d => lower.Contains(d))) continue;
                if (LooseTextExtractor.IsExcludedFileName(f)) continue;
                try { if (new FileInfo(f).Length > MaxFileBytes || new FileInfo(f).Length == 0) continue; } catch { continue; }
                if (LooksBinary(f)) continue;
                if (++n > MaxFiles) yield break;
                yield return f;
            }
        }

        private static IEnumerable<string> SafeEnumerate(string root)
        {
            try { return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).ToList(); }
            catch { return Array.Empty<string>(); }
        }

        private static bool LooksBinary(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                var b = new byte[2048];
                int n = fs.Read(b, 0, b.Length);
                if (n >= 2 && ((b[0] == 0xFF && b[1] == 0xFE) || (b[0] == 0xFE && b[1] == 0xFF))) return false; // UTF-16 BOM
                for (int i = 0; i < n; i++) if (b[i] == 0) return true;
                return false;
            }
            catch { return true; }
        }

        // ---------------- استخراج ----------------
        public IReadOnlyList<TextEntry> ExtractTexts(string gameFolderPath)
        {
            var results = new List<TextEntry>();

            foreach (var file in Files(gameFolderPath))
            {
                if (results.Count >= MaxEntries) break;
                string ext = Path.GetExtension(file).ToLowerInvariant();
                string rel = Path.GetRelativePath(gameFolderPath, file);

                if (ext == ".json")
                {
                    try { LooseTextExtractor.ExtractFromJson(gameFolderPath, file, results); } catch { }
                    continue;
                }

                string[] lines;
                try { lines = TextIo.ReadLines(file, out _, out _); } catch { continue; }
                for (int i = 0; i < lines.Length && results.Count < MaxEntries; i++)
                {
                    if (lines[i].Length > 5000) continue; // غالباً ملف مضغوط (minified)
                    var spans = SpansFor(ext, lines[i], file);
                    for (int n = 0; n < spans.Count; n++)
                        results.Add(new TextEntry
                        {
                            SourceFile = rel,
                            LocationKey = $"line:{i}:{n}",
                            OriginalText = spans[n].Text,
                            HasPlaceholders = LooseTextExtractor.ContainsPlaceholder(spans[n].Text),
                        });
                }
            }
            return results;
        }

        // ---------------- تركيب ----------------
        public bool ApplyTranslations(string gameFolderPath, IReadOnlyList<TextEntry> entries)
        {
            bool allOk = true;
            var approved = entries.Where(e => e.IsApproved && !string.IsNullOrEmpty(e.TranslatedText)).GroupBy(e => e.SourceFile);

            foreach (var g in approved)
            {
                string full = Path.Combine(gameFolderPath, g.Key);
                if (!File.Exists(full)) { allOk = false; continue; }
                string ext = Path.GetExtension(full).ToLowerInvariant();

                try
                {
                    if (ext == ".json") { LooseTextExtractor.ApplyJson(full, g.ToList()); continue; }

                    var lines = TextIo.ReadLines(full, out var enc, out var nl).ToList();
                    foreach (var lineGroup in g.GroupBy(e => ParseKey(e.LocationKey).line))
                    {
                        int i = lineGroup.Key;
                        if (i < 0 || i >= lines.Count) { allOk = false; continue; }
                        string line = lines[i];
                        var spans = SpansFor(ext, line, full);

                        // من اليمين لليسار عشان الإزاحات ما تخرّب بعضها
                        foreach (var e in lineGroup.OrderByDescending(x => ParseKey(x.LocationKey).n))
                        {
                            int n = ParseKey(e.LocationKey).n;
                            if (n < 0 || n >= spans.Count || spans[n].Text != e.OriginalText) { allOk = false; continue; }
                            var s = spans[n];
                            line = line.Substring(0, s.Start) + s.Escape(e.TranslatedText) + line.Substring(s.Start + s.Length);
                        }
                        lines[i] = line;
                    }
                    TextIo.WriteLines(full, lines, enc, nl);
                }
                catch { allOk = false; }
            }
            return allOk;
        }

        public bool RemoveArabization(string gameFolderPath) => BackupManager.Restore(gameFolderPath).HadBackup;

        private static (int line, int n) ParseKey(string key)
        {
            var p = key.Split(':');
            return p.Length == 3 && int.TryParse(p[1], out int l) && int.TryParse(p[2], out int n) ? (l, n) : (-1, -1);
        }

        // ---------------- تحليل سطر حسب الصيغة ----------------
        private static bool IsText(string s) => s != null && s.Trim().Length >= 2 && LooseTextExtractor.LooksLikeDisplayText(s);

        private static List<Span> SpansFor(string ext, string line, string path)
        {
            switch (ext)
            {
                case ".ini": case ".cfg": case ".conf": case ".properties": case ".lang": return KeyValue(line, ext);
                case ".yml": case ".yaml": return Yaml(line);
                case ".csv": return Csv(line, ',');
                case ".tsv": return Csv(line, '\t');
                case ".xml": case ".xaml": case ".resx": case ".html": case ".htm": return Xml(line);
                case ".lua": case ".js": case ".gd": return Script(line);
                default: return PlainLine(line);
            }
        }

        private static string EscNewlines(string t) => t.Replace("\r\n", "\n").Replace("\n", "\\n");

        private static List<Span> PlainLine(string line)
        {
            var list = new List<Span>();
            string t = line.Trim();
            if (!IsText(t)) return list;
            int start = line.IndexOf(t, StringComparison.Ordinal);
            list.Add(new Span { Start = start, Length = t.Length, Text = t, Escape = EscNewlines });
            return list;
        }

        private static readonly Regex KvRx = new(@"^(\s*[^#;!=\s\[][^=]*?\s*[=]\s*)(.*?)\s*$", RegexOptions.Compiled);

        private static List<Span> KeyValue(string line, string ext)
        {
            var list = new List<Span>();
            var m = KvRx.Match(line);
            if (!m.Success) return list;

            string val = m.Groups[2].Value;
            int start = m.Groups[2].Index, len = m.Groups[2].Length;
            bool quoted = val.Length >= 2 && val[0] == '"' && val[^1] == '"';
            if (quoted) { val = val.Substring(1, val.Length - 2); start++; len -= 2; }
            if (!IsText(val) || val.EndsWith("\\")) return list;

            bool unicodeEscape = ext == ".properties";
            list.Add(new Span
            {
                Start = start, Length = len, Text = val,
                Escape = t =>
                {
                    t = EscNewlines(t);
                    if (quoted) t = t.Replace("\"", "\\\"");
                    if (!unicodeEscape) return t;
                    var sb = new StringBuilder();
                    foreach (char c in t) sb.Append(c > 127 ? $"\\u{(int)c:x4}" : c.ToString());
                    return sb.ToString();
                }
            });
            return list;
        }

        private static readonly Regex YamlQuotedRx = new(@"^(\s*(?:-\s+)?[^\s:#""'][^:#]*:\s+)(?<q>[""'])(?<v>.*)\k<q>\s*(#.*)?$", RegexOptions.Compiled);
        private static readonly Regex YamlPlainRx = new(@"^(\s*(?:-\s+)?[\w.\-]+:\s+)(?<v>[A-Za-z][^#:""'{}\[\]]*[A-Za-z.!?])\s*$", RegexOptions.Compiled);

        private static List<Span> Yaml(string line)
        {
            var list = new List<Span>();
            var q = YamlQuotedRx.Match(line);
            if (q.Success)
            {
                var g = q.Groups["v"];
                string quote = q.Groups["q"].Value;
                string val = g.Value.Replace("\\\"", "\"");
                if (IsText(val))
                    list.Add(new Span
                    {
                        Start = g.Index, Length = g.Length, Text = val,
                        Escape = t => quote == "\""
                            ? EscNewlines(t).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\\\\n", "\\n")
                            : t.Replace("'", "''").Replace("\n", " ")
                    });
                return list;
            }

            var p = YamlPlainRx.Match(line);
            if (p.Success)
            {
                var g = p.Groups["v"];
                if (g.Value.Contains(' ') && IsText(g.Value))
                    list.Add(new Span
                    {
                        Start = g.Index, Length = g.Length, Text = g.Value,
                        Escape = t => "\"" + EscNewlines(t).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\\\\n", "\\n") + "\""
                    });
            }
            return list;
        }

        private static List<Span> Csv(string line, char delim)
        {
            var list = new List<Span>();
            if (line.Count(c => c == '"') % 2 != 0) return list; // خلية متعددة الأسطر - نتجاوزها
            if (delim == ',' && line.Count(c => c == ';') > line.Count(c => c == ',')) delim = ';';

            var cells = new List<(int start, int end)>();
            int cellStart = 0; bool inQ = false;
            for (int i = 0; i <= line.Length; i++)
            {
                if (i == line.Length || (!inQ && line[i] == delim)) { cells.Add((cellStart, i)); cellStart = i + 1; continue; }
                if (line[i] == '"') inQ = !inQ;
            }

            foreach (var (start, end) in cells)
            {
                string raw = line.Substring(start, end - start);
                string trimmed = raw.Trim();
                int lead = raw.Length == 0 ? 0 : raw.IndexOf(trimmed, StringComparison.Ordinal);
                if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
                {
                    string inner = trimmed.Substring(1, trimmed.Length - 2).Replace("\"\"", "\"");
                    if (IsText(inner))
                        list.Add(new Span { Start = start + lead + 1, Length = trimmed.Length - 2, Text = inner, Escape = EscapeCsvInner });
                }
                else if (IsText(trimmed) && (trimmed.Contains(' ') || Regex.IsMatch(trimmed, @"^[A-Z][a-z]{2,24}[.!?:]?$")))
                {
                    char d = delim;
                    list.Add(new Span { Start = start + lead, Length = trimmed.Length, Text = trimmed,
                        Escape = t => NeedsQuote(t, d) ? "\"" + EscapeCsvInner(t) + "\"" : t });
                }
            }
            return list;
        }

        private static string EscapeCsvInner(string t) => t.Replace("\r\n", "\n").Replace("\n", " ").Replace("\"", "\"\"");
        private static bool NeedsQuote(string t, char d) => t.Contains(d) || t.Contains('"') || t.Contains('\n');

        private static readonly Regex XmlTextRx = new(@">([^<>]{2,}?)<", RegexOptions.Compiled);
        private static readonly Regex XmlAttrRx = new(
            @"\b(?:text|title|label|tooltip|caption|content|description|desc|message|header|hint)\s*=\s*""([^""<>]{2,})""",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static List<Span> Xml(string line)
        {
            var found = new List<Span>();
            string Esc(string t) => WebUtility.HtmlEncode(EscNewlines(t)).Replace("&#39;", "'");

            foreach (Match m in XmlTextRx.Matches(line))
            {
                string raw = m.Groups[1].Value, t = raw.Trim();
                string dec = WebUtility.HtmlDecode(t);
                if (!IsText(dec)) continue;
                int lead = raw.IndexOf(t, StringComparison.Ordinal);
                found.Add(new Span { Start = m.Groups[1].Index + lead, Length = t.Length, Text = dec, Escape = Esc });
            }
            foreach (Match m in XmlAttrRx.Matches(line))
            {
                string dec = WebUtility.HtmlDecode(m.Groups[1].Value);
                if (IsText(dec))
                    found.Add(new Span { Start = m.Groups[1].Index, Length = m.Groups[1].Length, Text = dec, Escape = Esc });
            }
            return found.OrderBy(s => s.Start).ToList();
        }

        private static readonly Regex StrRx = new(@"(?<q>[""'])(?<v>(?:\\.|(?!\k<q>)[^\\\r\n])*)\k<q>", RegexOptions.Compiled);

        private static List<Span> Script(string line)
        {
            var list = new List<Span>();
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//") || trimmed.StartsWith("--") || trimmed.StartsWith("#") || trimmed.StartsWith("*")) return list;

            foreach (Match m in StrRx.Matches(line))
            {
                string q = m.Groups["q"].Value;
                var g = m.Groups["v"];
                string val = g.Value.Replace("\\" + q, q).Replace("\\n", "\n").Replace("\\\\", "\\");
                if (!IsSentence(val)) continue;
                list.Add(new Span
                {
                    Start = g.Index, Length = g.Length, Text = val,
                    Escape = t => t.Replace("\\", "\\\\").Replace(q, "\\" + q).Replace("\r\n", "\n").Replace("\n", "\\n")
                });
            }
            return list;
        }

        // في السكربتات نكون متشددين: لازم يشبه جملة حقيقية مو كود/معرّف
        private static bool IsSentence(string s)
        {
            s = s.Trim();
            if (s.Length < 6 || !s.Contains(' ')) return false;
            if (!char.IsLetter(s[0])) return false;
            if (s.IndexOfAny(new[] { '{', '}', '(', ')', '=', ';', '<', '>', '_', '/', '\\', '$', '%' }) >= 0) return false;
            if (s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Count(w => w.Any(char.IsLetter)) < 2) return false;
            return LooseTextExtractor.LooksLikeDisplayText(s);
        }
    }
}
