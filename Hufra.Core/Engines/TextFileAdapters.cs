using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Hufra.Core.Models;

namespace Hufra.Core.Engines
{
    /// <summary>أدوات قراءة/كتابة ملفات نصية مع الحفاظ على الترميز (UTF-8/UTF-16) ونهاية السطر + نسخة احتياطية.</summary>
    internal static class TextIo
    {
        public static string[] ReadLines(string path, out Encoding enc, out string newline)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) enc = new UnicodeEncoding(false, true);
            else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) enc = new UnicodeEncoding(true, true);
            else if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) enc = new UTF8Encoding(true);
            else enc = new UTF8Encoding(false);

            string text = enc.GetString(bytes);
            if (text.Length > 0 && text[0] == '\uFEFF') text = text.Substring(1);
            newline = text.Contains("\r\n") ? "\r\n" : "\n";
            return text.Replace("\r\n", "\n").Split('\n');
        }

        public static void WriteLines(string path, IEnumerable<string> lines, Encoding enc, string newline)
        {
            BackupOnce(path);
            File.WriteAllText(path, string.Join(newline, lines), enc);
        }

        /// <summary>ينسخ الملف الأصلي مرة وحدة بجانبه (.hufra.bak) قبل أول تعديل</summary>
        public static void BackupOnce(string path)
        {
            // النسخ الاحتياطي صار مركزياً (BackupManager) ويتم قبل التركيب، فما نترك ملفات .bak داخل مجلد اللعبة.
        }

        public static string[] SafeFiles(string root)
        {
            try { return Directory.GetFiles(root, "*", SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }

        public static bool HasLetter(string s) => s.Any(char.IsLetter);

        // يختار ملف اللغة المصدر (الإنجليزي أو بدون لاحقة لغة) من مجموعة ملفات لغات متعددة بنفس المجلد
        private static readonly Regex LangRx = new Regex(
            @"^(?:(?<pre>.*?)[_.\-])?(?<lang>en|de|fr|es|ru|ja|zh|pt|it|ko|pl|tr|ar|nl|cs|sv|uk|hu|fi|da|no|ro|el|th|vi|id)(?:[_\-][A-Za-z]{2,4})?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static IEnumerable<string> PickSourceLanguageFiles(IEnumerable<string> files)
        {
            var groups = new Dictionary<string, (int rank, string file)>();
            foreach (var f in files)
            {
                string stem = Path.GetFileNameWithoutExtension(f);
                var m = LangRx.Match(stem);
                string groupKey; int rank;
                if (m.Success)
                {
                    groupKey = Path.GetDirectoryName(f) + "|" + m.Groups["pre"].Value.ToLowerInvariant() + Path.GetExtension(f).ToLowerInvariant();
                    rank = m.Groups["lang"].Value.Equals("en", StringComparison.OrdinalIgnoreCase) ? 0 : 2;
                }
                else
                {
                    groupKey = Path.GetDirectoryName(f) + "|" + stem.ToLowerInvariant() + Path.GetExtension(f).ToLowerInvariant();
                    rank = 1;
                }
                if (!groups.TryGetValue(groupKey, out var cur) || rank < cur.rank) groups[groupKey] = (rank, f);
            }
            return groups.Values.Select(v => v.file);
        }
    }

    /// <summary>
    /// أساس محركات الملفات النصية السطرية. كل Adapter يحدد: أنماط الملفات، طريقة تحليل السطر، وطريقة إعادة كتابته.
    /// التركيب يتحقق إن السطر ما تغيّر قبل الاستبدال، ويعمل نسخة احتياطية (.hufra.bak) أول مرة.
    /// </summary>
    public abstract class TextFileAdapterBase : IEngineAdapter
    {
        public abstract EngineType Engine { get; }
        public abstract string DisplayName { get; }

        public abstract DetectionResult Detect(string root, string[] files);
        protected abstract IEnumerable<string> CandidateFiles(string root);
        protected abstract IEnumerable<(int Line, string Text)> Parse(string[] lines, string filePath);
        protected abstract string Rewrite(string line, string original, string translated, string filePath);

        public int DetectConfidence(string gameFolderPath) =>
            Detect(gameFolderPath, TextIo.SafeFiles(gameFolderPath)).Confidence;

        public virtual IReadOnlyList<TextEntry> ExtractTexts(string gameFolderPath)
        {
            var results = new List<TextEntry>();
            foreach (var file in CandidateFiles(gameFolderPath))
            {
                string[] lines;
                try { lines = TextIo.ReadLines(file, out _, out _); } catch { continue; }
                string rel = Path.GetRelativePath(gameFolderPath, file);
                foreach (var (line, text) in Parse(lines, file))
                {
                    results.Add(new TextEntry
                    {
                        SourceFile = rel,
                        LocationKey = $"line:{line}",
                        OriginalText = text,
                        HasPlaceholders = LooseTextExtractor.ContainsPlaceholder(text),
                    });
                }
            }
            return results;
        }

        public virtual bool ApplyTranslations(string gameFolderPath, IReadOnlyList<TextEntry> entries)
        {
            bool allOk = true;
            var byFile = entries.Where(e => e.IsApproved && !string.IsNullOrEmpty(e.TranslatedText))
                                .GroupBy(e => e.SourceFile);
            foreach (var g in byFile)
            {
                string full = Path.Combine(gameFolderPath, g.Key);
                if (!File.Exists(full)) { allOk = false; continue; }
                try
                {
                    var lines = TextIo.ReadLines(full, out var enc, out var nl).ToList();
                    ApplyToLines(lines, g.ToList(), full);
                    TextIo.WriteLines(full, lines, enc, nl);
                }
                catch { allOk = false; }
            }
            return allOk;
        }

        protected virtual void ApplyToLines(List<string> lines, List<TextEntry> entries, string filePath)
        {
            foreach (var e in entries)
            {
                if (!e.LocationKey.StartsWith("line:") || !int.TryParse(e.LocationKey.Substring(5), out int i)) continue;
                if (i < 0 || i >= lines.Count) continue;
                string updated = Rewrite(lines[i], e.OriginalText, e.TranslatedText, filePath);
                if (updated != null) lines[i] = updated;
            }
        }

        public bool RemoveArabization(string gameFolderPath)
        {
            bool any = false;
            foreach (var bak in TextIo.SafeFiles(gameFolderPath).Where(f => f.EndsWith(".hufra.bak")))
            {
                try { File.Copy(bak, bak.Substring(0, bak.Length - ".hufra.bak".Length), true); any = true; } catch { }
            }
            return any;
        }
    }

    // ------------------------------------------------------------------
    // gettext (.po) — شائع جداً (Godot، ألعاب Python/C++، مشاريع مفتوحة المصدر)
    // ------------------------------------------------------------------
    public class GettextPoAdapter : TextFileAdapterBase
    {
        public override EngineType Engine => EngineType.Gettext;
        public override string DisplayName => "gettext (.po)";

        public override DetectionResult Detect(string root, string[] files)
        {
            int n = files.Count(f => f.EndsWith(".po", StringComparison.OrdinalIgnoreCase));
            return n > 0
                ? new DetectionResult { Engine = Engine, Confidence = 55, Reason = $"وجدت {n} ملف ترجمة .po" }
                : new DetectionResult { Engine = Engine };
        }

        protected override IEnumerable<string> CandidateFiles(string root) =>
            TextIo.PickSourceLanguageFiles(TextIo.SafeFiles(root).Where(f => f.EndsWith(".po", StringComparison.OrdinalIgnoreCase)));

        private static string Unq(string s)
        {
            s = s.Trim();
            if (s.Length >= 2 && s[0] == '"' && s[^1] == '"') s = s.Substring(1, s.Length - 2);
            return Regex.Replace(s, @"\\(.)", m => m.Groups[1].Value switch { "n" => "\n", "t" => "\t", "\"" => "\"", "\\" => "\\", var x => "\\" + x });
        }

        private static string Q(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r\n", "\n").Replace("\n", "\\n").Replace("\t", "\\t") + "\"";

        protected override IEnumerable<(int Line, string Text)> Parse(string[] lines, string filePath)
        {
            var result = new List<(int, string)>();
            for (int i = 0; i < lines.Length; i++)
            {
                if (!lines[i].StartsWith("msgid ")) continue;
                var id = new StringBuilder(Unq(lines[i].Substring(6)));
                int j = i + 1;
                while (j < lines.Length && lines[j].TrimStart().StartsWith("\"")) id.Append(Unq(lines[j++]));
                if (j >= lines.Length || lines[j].StartsWith("msgid_plural")) { i = j; continue; }
                if (!lines[j].StartsWith("msgstr ")) continue;
                string text = id.ToString();
                if (text.Length >= 2 && TextIo.HasLetter(text))
                    result.Add((j, text)); // مفتاح التطبيق = سطر msgstr
                i = j;
            }
            return result;
        }

        protected override string Rewrite(string line, string original, string translated, string filePath) => null; // يُستخدم ApplyToLines

        protected override void ApplyToLines(List<string> lines, List<TextEntry> entries, string filePath)
        {
            // ننفّذ من الأسفل للأعلى عشان حذف أسطر الاستمرار ما يخرّب الفهارس
            foreach (var e in entries.OrderByDescending(x => int.Parse(x.LocationKey.Substring(5))))
            {
                int j = int.Parse(e.LocationKey.Substring(5));
                if (j < 0 || j >= lines.Count || !lines[j].StartsWith("msgstr ")) continue;
                int end = j + 1;
                while (end < lines.Count && lines[end].TrimStart().StartsWith("\"")) end++;
                lines.RemoveRange(j + 1, end - j - 1);
                lines[j] = "msgstr " + Q(e.TranslatedText);
            }
        }
    }

    // ------------------------------------------------------------------
    // .properties / .lang (Java، LibGDX، ألعاب بنمط Minecraft)
    // ------------------------------------------------------------------
    public class PropertiesAdapter : TextFileAdapterBase
    {
        public override EngineType Engine => EngineType.Properties;
        public override string DisplayName => "Java .properties / .lang";

        private static readonly Regex KvRx = new Regex(@"^(\s*[^#!=:\s][^=:]*?)\s*=\s?(.*)$", RegexOptions.Compiled);

        public override DetectionResult Detect(string root, string[] files)
        {
            var hits = files.Where(IsLangFile).ToList();
            return hits.Count > 0
                ? new DetectionResult { Engine = Engine, Confidence = 55, Reason = $"وجدت {hits.Count} ملف لغة (.properties/.lang)" }
                : new DetectionResult { Engine = Engine };
        }

        private static bool IsLangFile(string f)
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            if (ext == ".lang") return true;
            if (ext != ".properties") return false;
            string n = Path.GetFileName(f).ToLowerInvariant();
            return n.Contains("messages") || n.Contains("lang") || n.Contains("text") || n.Contains("string") ||
                   n.Contains("bundle") || n.Contains("i18n") || n.Contains("locale") || n.Contains("dialog");
        }

        protected override IEnumerable<string> CandidateFiles(string root) =>
            TextIo.PickSourceLanguageFiles(TextIo.SafeFiles(root).Where(IsLangFile));

        private static string Decode(string v) =>
            Regex.Replace(v, @"\\u([0-9a-fA-F]{4})", m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

        private static string Encode(string v, bool escapeUnicode)
        {
            v = v.Replace("\r\n", "\n").Replace("\n", "\\n");
            if (!escapeUnicode) return v;
            var sb = new StringBuilder();
            foreach (char c in v) sb.Append(c > 127 ? $"\\u{(int)c:x4}" : c.ToString());
            return sb.ToString();
        }

        protected override IEnumerable<(int Line, string Text)> Parse(string[] lines, string filePath)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                var m = KvRx.Match(lines[i]);
                if (!m.Success) continue;
                string val = Decode(m.Groups[2].Value.TrimEnd());
                if (val.EndsWith("\\")) continue; // قيمة متعددة الأسطر - نتجاوزها
                if (val.Length >= 2 && LooseTextExtractor.LooksLikeDisplayText(val)) yield return (i, val);
            }
        }

        protected override string Rewrite(string line, string original, string translated, string filePath)
        {
            var m = KvRx.Match(line);
            if (!m.Success || Decode(m.Groups[2].Value.TrimEnd()) != original) return null;
            bool esc = Path.GetExtension(filePath).Equals(".properties", StringComparison.OrdinalIgnoreCase);
            return m.Groups[1].Value + "=" + Encode(translated, esc);
        }
    }

    // ------------------------------------------------------------------
    // Source Engine (Valve): resource/*_english.txt بصيغة "key" "value"
    // ------------------------------------------------------------------
    public class SourceEngineAdapter : TextFileAdapterBase
    {
        public override EngineType Engine => EngineType.SourceEngine;
        public override string DisplayName => "Source Engine (Valve)";

        private static readonly Regex KvRx = new Regex(
            @"^(?<pre>\s*""(?<k>[^""]+)""\s+)""(?<v>(?:[^""\\]|\\.)*)""(?<post>\s*(?://.*)?)$", RegexOptions.Compiled);

        private static bool IsTokenFile(string f)
        {
            string n = Path.GetFileName(f).ToLowerInvariant();
            return n.EndsWith("_english.txt") && f.Replace('\\', '/').Contains("/resource", StringComparison.OrdinalIgnoreCase);
        }

        public override DetectionResult Detect(string root, string[] files)
        {
            var tokens = files.Where(IsTokenFile).ToList();
            bool gameinfo = files.Any(f => Path.GetFileName(f).Equals("gameinfo.txt", StringComparison.OrdinalIgnoreCase));
            if (tokens.Count > 0 && gameinfo)
                return new DetectionResult { Engine = Engine, Confidence = 85, Reason = "وجدت gameinfo.txt وملفات resource/*_english.txt" };
            if (tokens.Count > 0)
                return new DetectionResult { Engine = Engine, Confidence = 60, Reason = "وجدت ملفات resource/*_english.txt" };
            return new DetectionResult { Engine = Engine };
        }

        protected override IEnumerable<string> CandidateFiles(string root) =>
            TextIo.SafeFiles(root).Where(IsTokenFile);

        private static string Unesc(string s) => s.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r\n", "\n").Replace("\n", "\\n");

        protected override IEnumerable<(int Line, string Text)> Parse(string[] lines, string filePath)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                var m = KvRx.Match(lines[i]);
                if (!m.Success) continue;
                string k = m.Groups["k"].Value;
                if (k.Equals("Language", StringComparison.OrdinalIgnoreCase)) continue;
                string v = Unesc(m.Groups["v"].Value);
                if (v.Length >= 2 && TextIo.HasLetter(v)) yield return (i, v);
            }
        }

        protected override string Rewrite(string line, string original, string translated, string filePath)
        {
            var m = KvRx.Match(line);
            if (!m.Success || Unesc(m.Groups["v"].Value) != original) return null;
            return m.Groups["pre"].Value + "\"" + Esc(translated) + "\"" + m.Groups["post"].Value;
        }
    }

    // ------------------------------------------------------------------
    // TyranoBuilder / KAG (.ks) — روايات بصرية بنص حر
    // ------------------------------------------------------------------
    public class TyranoKsAdapter : TextFileAdapterBase
    {
        public override EngineType Engine => EngineType.TyranoKs;
        public override string DisplayName => "TyranoBuilder / KAG (.ks)";

        private static readonly Regex TagRx = new Regex(@"\[[^\]]*\]", RegexOptions.Compiled);

        public override DetectionResult Detect(string root, string[] files)
        {
            int ks = files.Count(f => f.EndsWith(".ks", StringComparison.OrdinalIgnoreCase));
            bool tyrano = files.Any(f => f.Replace('\\', '/').Contains("/tyrano/", StringComparison.OrdinalIgnoreCase));
            if (ks >= 1 && tyrano)
                return new DetectionResult { Engine = Engine, Confidence = 90, Reason = $"وجدت مجلد tyrano و{ks} سكربت .ks" };
            if (ks >= 3)
                return new DetectionResult { Engine = Engine, Confidence = 65, Reason = $"وجدت {ks} سكربت .ks" };
            return new DetectionResult { Engine = Engine };
        }

        protected override IEnumerable<string> CandidateFiles(string root) =>
            TextIo.SafeFiles(root).Where(f => f.EndsWith(".ks", StringComparison.OrdinalIgnoreCase) &&
                                              !f.Replace('\\', '/').Contains("/tyrano/", StringComparison.OrdinalIgnoreCase));

        protected override IEnumerable<(int Line, string Text)> Parse(string[] lines, string filePath)
        {
            bool inScript = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i].Trim();
                if (t.StartsWith("[iscript", StringComparison.OrdinalIgnoreCase)) inScript = true;
                if (inScript) { if (t.Contains("[endscript", StringComparison.OrdinalIgnoreCase)) inScript = false; continue; }
                if (t.Length == 0 || t[0] == ';' || t[0] == '*' || t[0] == '@' || t[0] == '#') continue;

                string plain = TagRx.Replace(t, "").Trim();
                if (plain.Length < 2 || !TextIo.HasLetter(plain)) continue;
                yield return (i, t);
            }
        }

        protected override string Rewrite(string line, string original, string translated, string filePath)
        {
            if (line.Trim() != original) return null;
            string indent = line.Substring(0, line.Length - line.TrimStart().Length);
            return indent + translated.Replace("\r\n", "[r]").Replace("\n", "[r]");
        }
    }

    // ------------------------------------------------------------------
    // ألعاب الويب / Electron / NW.js ومجلدات لغات JSON العامة (locales, lang, i18n ...)
    // ------------------------------------------------------------------
    public class JsonLocalizationAdapter : TextFileAdapterBase
    {
        public override EngineType Engine => EngineType.JsonLocalization;
        public override string DisplayName => "Web / Electron (JSON locales)";

        private static readonly string[] Dirs = { "locales", "locale", "lang", "langs", "i18n", "localization", "localisation", "translations", "languages" };

        private static bool IsLocaleJson(string f)
        {
            if (!f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
            if (LooseTextExtractor.IsExcludedFileName(f)) return false;
            var parts = f.Replace('\\', '/').Split('/');
            return parts.Take(parts.Length - 1).Any(p => Dirs.Contains(p, StringComparer.OrdinalIgnoreCase));
        }

        public override DetectionResult Detect(string root, string[] files)
        {
            var hits = files.Where(IsLocaleJson).ToList();
            bool web = files.Any(f => Path.GetFileName(f).Equals("package.json", StringComparison.OrdinalIgnoreCase)) ||
                       files.Any(f => Path.GetFileName(f).Equals("resources.pak", StringComparison.OrdinalIgnoreCase));
            if (hits.Count == 0) return new DetectionResult { Engine = Engine };
            return new DetectionResult { Engine = Engine, Confidence = web ? 70 : 55,
                Reason = $"وجدت {hits.Count} ملف JSON داخل مجلد لغات" };
        }

        protected override IEnumerable<string> CandidateFiles(string root) =>
            TextIo.PickSourceLanguageFiles(TextIo.SafeFiles(root).Where(IsLocaleJson));

        protected override IEnumerable<(int Line, string Text)> Parse(string[] lines, string filePath) =>
            Enumerable.Empty<(int, string)>();
        protected override string Rewrite(string line, string original, string translated, string filePath) => null;

        public override IReadOnlyList<TextEntry> ExtractTexts(string gameFolderPath)
        {
            var results = new List<TextEntry>();
            foreach (var f in CandidateFiles(gameFolderPath))
                LooseTextExtractor.ExtractFromJson(gameFolderPath, f, results);
            return results;
        }

        public override bool ApplyTranslations(string gameFolderPath, IReadOnlyList<TextEntry> entries)
        {
            bool allOk = true;
            foreach (var g in entries.Where(e => e.IsApproved && !string.IsNullOrEmpty(e.TranslatedText)).GroupBy(e => e.SourceFile))
            {
                string full = Path.Combine(gameFolderPath, g.Key);
                if (!File.Exists(full)) { allOk = false; continue; }
                try { TextIo.BackupOnce(full); LooseTextExtractor.ApplyJson(full, g.ToList()); }
                catch { allOk = false; }
            }
            return allOk;
        }
    }
}
