using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Hufra.Core.Models;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// منطق استخراج/تطبيق مشترك بين كل المحركات اللي تخزّن نصوصها بملفات
    /// JSON أو نصية عادية "حرة" (غير معبأة داخل أرشيف ثنائي خاص بالمحرك).
    /// كل Adapter يقرر أي مجلدات/ملفات يفحص، وهذا الكلاس يتكفّل بالتفاصيل
    /// المشتركة (المشي على شجرة JSON، الفلترة، القراءة والكتابة الآمنة).
    /// </summary>
    public static class LooseTextExtractor
    {
        // ملفات إعداد/بيانات تقنية معروفة بأي محرك - نستبعدها حتى لو امتدادها json/txt عادي
        private static readonly HashSet<string> ExcludedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "package.json", "package-lock.json", "tsconfig.json", "boot.config",
            "app.config", "project.godot", "export_presets.cfg", "manifest.json",
        };

        public static bool IsExcludedFileName(string filePath) =>
            ExcludedFileNames.Contains(Path.GetFileName(filePath));

        public static void ExtractFromJson(string root, string filePath, List<TextEntry> results)
        {
            string relative = Path.GetRelativePath(root, filePath);
            string json;
            try { json = File.ReadAllText(filePath); }
            catch { return; }

            JsonDocument doc;
            try { doc = JsonDocument.Parse(json); }
            catch { return; } // ملف غير صالح كـ JSON (أو ملف ثنائي بامتداد خاطئ) - نتجاوزه

            WalkJson(doc.RootElement, "$", relative, results);
        }

        private static void WalkJson(JsonElement element, string path, string relativeFile, List<TextEntry> results)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var prop in element.EnumerateObject())
                        WalkJson(prop.Value, $"{path}.{prop.Name}", relativeFile, results);
                    break;

                case JsonValueKind.Array:
                    int i = 0;
                    foreach (var item in element.EnumerateArray())
                    {
                        WalkJson(item, $"{path}[{i}]", relativeFile, results);
                        i++;
                    }
                    break;

                case JsonValueKind.String:
                    string value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value) && value.Length >= 2 && LooksLikeDisplayText(value))
                    {
                        results.Add(new TextEntry
                        {
                            SourceFile = relativeFile,
                            LocationKey = path,
                            OriginalText = value,
                            HasPlaceholders = ContainsPlaceholder(value),
                        });
                    }
                    break;
            }
        }

        public static void ExtractFromPlainText(string root, string filePath, List<TextEntry> results)
        {
            string relative = Path.GetRelativePath(root, filePath);
            string[] lines;
            try { lines = File.ReadAllLines(filePath); }
            catch { return; }

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (!string.IsNullOrWhiteSpace(line) && LooksLikeDisplayText(line))
                {
                    results.Add(new TextEntry
                    {
                        SourceFile = relative,
                        LocationKey = $"line:{i}",
                        OriginalText = line,
                        HasPlaceholders = ContainsPlaceholder(line),
                    });
                }
            }
        }

        public static void ApplyJson(string fullPath, List<TextEntry> entries)
        {
            string json = File.ReadAllText(fullPath);
            var root = System.Text.Json.Nodes.JsonNode.Parse(json);
            if (root == null) return;

            foreach (var e in entries)
            {
                try { SetJsonValueByPath(root, e.LocationKey, e.TranslatedText); }
                catch { /* مسار غير موجود (تغيّر الملف) - نتجاوزه بدل ما نوقف الباقي */ }
            }

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };
            File.WriteAllText(fullPath, root.ToJsonString(options));
        }

        public static void ApplyPlainText(string fullPath, List<TextEntry> entries)
        {
            var lines = File.ReadAllLines(fullPath);
            foreach (var e in entries)
            {
                if (e.LocationKey.StartsWith("line:") &&
                    int.TryParse(e.LocationKey.Substring(5), out int lineIndex) &&
                    lineIndex >= 0 && lineIndex < lines.Length)
                {
                    lines[lineIndex] = e.TranslatedText;
                }
            }
            File.WriteAllLines(fullPath, lines);
        }

        private static void SetJsonValueByPath(System.Text.Json.Nodes.JsonNode root, string path, string newValue)
        {
            var segments = ParsePathSegments(path);
            if (segments.Count == 0) return;

            System.Text.Json.Nodes.JsonNode current = root;
            for (int i = 0; i < segments.Count - 1; i++)
            {
                current = segments[i] is int idx ? current[idx] : current[(string)segments[i]];
                if (current == null) return;
            }

            var last = segments[^1];
            if (last is int lastIdx)
                current[lastIdx] = System.Text.Json.Nodes.JsonValue.Create(newValue);
            else
                current[(string)last] = System.Text.Json.Nodes.JsonValue.Create(newValue);
        }

        private static List<object> ParsePathSegments(string path)
        {
            var segments = new List<object>();
            int i = path.StartsWith("$") ? 1 : 0;

            while (i < path.Length)
            {
                if (path[i] == '.')
                {
                    i++;
                    int start = i;
                    while (i < path.Length && path[i] != '.' && path[i] != '[') i++;
                    segments.Add(path.Substring(start, i - start));
                }
                else if (path[i] == '[')
                {
                    i++;
                    int start = i;
                    while (i < path.Length && path[i] != ']') i++;
                    segments.Add(int.Parse(path.Substring(start, i - start)));
                    i++;
                }
                else
                {
                    i++;
                }
            }

            return segments;
        }

        public static bool LooksLikeDisplayText(string s)
        {
            s = s.Trim();
            if (s.Length < 2) return false;

            // نرفض بس لو السطر كله عبارة عن وسم XML (يبدأ بـ '<')، مو أي نص فيه
            // وسم تنسيق داخلي مثل <color=red>...</color> ضمن حوار حقيقي (نص شائع بالألعاب)
            if (s.StartsWith("<")) return false;
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"\w+\s*=\s*""")) return false;

            // مسارات/روابط
            if (Uri.IsWellFormedUriString(s, UriKind.Absolute)) return false;

            // كلمة واحدة بحرف كبير أول (Start, Options, Inventory) = غالباً عنوان زر/قائمة، مو معرّف برمجي
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Z][a-z]{2,24}[.!?:]?$")) return true;

            // مفتاح/معرّف برمجي بدون مسافات (namespace.Class أو path/to/file)
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Za-z0-9_\.\-/\\]+$") && !s.Contains(' '))
                return false;

            // أسماء أنواع/مساحات أسماء معروفة (UnityEngine.X، System.X...) حتى لو جات بجملة فيها مسافات
            if (System.Text.RegularExpressions.Regex.IsMatch(s, @"^[A-Za-z]+(\.[A-Za-z][A-Za-z0-9]*){2,}"))
                return false;

            // كلمات كود شائعة تدل إنه سطر برمجي مو حوار/نص واجهة
            string[] codeMarkers = { "namespace ", "using ", "class ", "public ", "private ", "protected ",
                                      "void ", "fullname", "preserve=", "xmlns", "<?xml" };
            foreach (var marker in codeMarkers)
                if (s.Contains(marker, StringComparison.OrdinalIgnoreCase)) return false;

            // لازم نسبة معقولة من الحروف الفعلية (نستبعد أسطر أغلبها رموز/أرقام/علامات ترقيم)
            int letters = 0;
            foreach (char c in s) if (char.IsLetter(c)) letters++;
            if (letters < s.Length / 3) return false;

            return true;
        }

        public static bool ContainsPlaceholder(string s) =>
            System.Text.RegularExpressions.Regex.IsMatch(s,
                @"\{[0-9a-zA-Z_]+\}|%[sd]|\[[A-Za-z_]+\]|<[^>]+>|\\n");

        public static IEnumerable<string> SafeFindDirs(string root, string name)
        {
            try { return Directory.GetDirectories(root, name, SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }

        public static IEnumerable<string> SafeGetFiles(string dir, string searchPattern = "*")
        {
            try { return Directory.GetFiles(dir, searchPattern, SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }
    }
}
