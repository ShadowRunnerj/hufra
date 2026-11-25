using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Hufra.Core.Models;
using Hufra.Core.Safety;
using Hufra.Core.Settings;
using Hufra.Core.Text;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// ألعاب Paradox (Crusader Kings III، Victoria 3، Imperator: Rome ...): ملفات localization/english/*_l_english.yml
    /// بصيغة:  key:0 "النص".
    ///
    /// التركيب لا يلمس ملفات اللعبة: يولّد مود جاهز في Documents\Paradox Interactive\&lt;اللعبة&gt;\mod\hufra_arabic
    /// يستبدل النصوص الإنجليزية (localization/replace/english). يفعّله المستخدم من لانشر Paradox ويلعب بلغة English.
    ///
    /// بما إن هذه الألعاب لا تدعم العربية/RTL، النص يُشكَّل ويُعكس عند الكتابة (حسب إعداد التطبيق)،
    /// ولازم خط اللعبة يحتوي أشكال العرض العربية (FE70–FEFF).
    /// </summary>
    public class ParadoxLocalizationAdapter : IEngineAdapter
    {
        public EngineType Engine => EngineType.Paradox;
        public string DisplayName => "Paradox (CK3 / Victoria 3 / Imperator)";

        /// <summary>ملاحظة تُعرض للمستخدم بعد الاستخراج (مثلاً: تم تحديد عدد النصوص)</summary>
        public static string LastExtractNote { get; private set; } = "";

        /// <summary>سقف النصوص بالاستخراج الواحد (الأهم أولاً) عشان الترجمة المجانية والمراجعة تبقى عملية</summary>
        public const int MaxEntries = 12000;

        private static readonly Regex LineRx = new Regex(
            @"^(?<pre>\s*(?<key>[A-Za-z0-9_.\-']+):(?<ver>\d*)\s+)""(?<val>.*)""(?<post>\s*(?:#.*)?)$", RegexOptions.Compiled);

        private static readonly string[] SkipNameHints = { "names_l_", "name_list", "character_names", "_names_", "dynasty_names", "culture_names" };
        private static readonly string[] PriorityHints = { "gui", "ui", "interface", "menu", "concept", "tooltip", "message", "game_rules", "trait", "decision", "modifier", "lobby", "tutorial" };

        private static bool IsLocFile(string f)
        {
            string p = f.Replace('\\', '/');
            return p.EndsWith("_l_english.yml", StringComparison.OrdinalIgnoreCase) &&
                   p.Contains("/localization/english/", StringComparison.OrdinalIgnoreCase) &&
                   !p.Contains("/replace/", StringComparison.OrdinalIgnoreCase);
        }

        public DetectionResult Detect(string root, string[] files)
        {
            int n = files.Count(IsLocFile);
            if (n == 0) return new DetectionResult { Engine = Engine };
            bool paradox = Directory.Exists(Path.Combine(root, "game")) || Directory.Exists(Path.Combine(root, "launcher"));
            return new DetectionResult { Engine = Engine, Confidence = paradox ? 92 : 75,
                Reason = $"وجدت {n} ملف localization/english من نوع Paradox" };
        }

        public int DetectConfidence(string gameFolderPath) =>
            Detect(gameFolderPath, TextIo.SafeFiles(gameFolderPath)).Confidence;

        public IReadOnlyList<TextEntry> ExtractTexts(string gameFolderPath)
        {
            var files = TextIo.SafeFiles(gameFolderPath).Where(IsLocFile)
                .Where(f => !SkipNameHints.Any(h => Path.GetFileName(f).Contains(h, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(f => PriorityHints.Any(h => Path.GetFileName(f).Contains(h, StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
                .ThenBy(f => f)
                .ToList();

            var results = new List<TextEntry>();
            int totalFound = 0;

            foreach (var file in files)
            {
                string[] lines;
                try { lines = TextIo.ReadLines(file, out _, out _); } catch { continue; }
                string rel = Path.GetRelativePath(gameFolderPath, file);

                foreach (var line in lines)
                {
                    var m = LineRx.Match(line);
                    if (!m.Success) continue;

                    string val = m.Groups["val"].Value.Replace("\\\"", "\"");
                    string stripped = Hufra.Core.Translation.PlaceholderGuard.TokenRegex.Replace(val, " ");
                    if (stripped.Count(char.IsLetter) < 2) continue; // مرجع/رمز فقط ($other_key$ مثلاً)

                    totalFound++;
                    if (results.Count >= MaxEntries) continue;

                    results.Add(new TextEntry
                    {
                        SourceFile = rel,
                        LocationKey = "key:" + m.Groups["key"].Value + ":" + m.Groups["ver"].Value,
                        OriginalText = val,
                        HasPlaceholders = LooseTextExtractor.ContainsPlaceholder(val) || val.Contains('$') || val.Contains('['),
                    });
                }
            }

            LastExtractNote = totalFound > results.Count
                ? $"ℹ️ في اللعبة {totalFound} نص، عُرض أهم {results.Count} (الواجهة، المفاهيم، الصفات...) لتبقى الترجمة والمراجعة سريعة. باقي النصوص تبقى بالإنجليزية."
                : "";
            return results;
        }

        private static string ModsRoot(string gameFolderPath)
        {
            string name = new DirectoryInfo(gameFolderPath.TrimEnd('\\', '/')).Name;
            string docsName =
                name.Contains("Victoria", StringComparison.OrdinalIgnoreCase) ? "Victoria 3" :
                name.Contains("Imperator", StringComparison.OrdinalIgnoreCase) ? "Imperator" :
                "Crusader Kings III";

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                "Paradox Interactive", docsName, "mod");
        }

        /// <summary>يتحقق إن ملفات المود انكتبت فعلاً وفيها نصوص بعدد معقول</summary>
        public static VerificationReport VerifyMod(string gameFolderPath, int expectedTexts, bool applyReportedOk)
        {
            var report = new VerificationReport { FilesExpected = 1 };
            string modDir = Path.Combine(ModsRoot(gameFolderPath), "hufra_arabic");
            string locDir = Path.Combine(modDir, "localization", "replace", "english");

            int lines = 0, files = 0;
            if (applyReportedOk && Directory.Exists(locDir))
            {
                foreach (var f in Directory.GetFiles(locDir, "*_l_english.yml"))
                {
                    var l = File.ReadAllLines(f);
                    if (l.Length > 1 && l[0].Contains("l_english:")) { files++; lines += l.Length - 1; }
                }
            }
            bool descriptor = File.Exists(Path.Combine(ModsRoot(gameFolderPath), "hufra_arabic.mod"));

            report.FilesChanged = files;
            report.TextsChecked = expectedTexts;
            report.TextsVerified = Math.Min(lines, expectedTexts);
            if (!descriptor) report.Problems.Add("ملف hufra_arabic.mod غير موجود");

            report.Status = files == 0 || !descriptor ? VerificationStatus.Failed
                          : lines >= expectedTexts * 0.95 ? VerificationStatus.Success : VerificationStatus.Partial;
            report.Summary = report.Status == VerificationStatus.Failed
                ? "لم يتم إنشاء ملفات المود. لم يتم التعريب."
                : $"تم إنشاء المود: {files} ملف و{lines} نص في Documents\\Paradox Interactive. (يتحقق من الملفات فقط؛ العرض داخل اللعبة يعتمد على الخط.)";
            return report;
        }

        public bool ApplyTranslations(string gameFolderPath, IReadOnlyList<TextEntry> entries)
        {
            var approved = entries.Where(e => e.IsApproved && !string.IsNullOrWhiteSpace(e.TranslatedText)).ToList();
            if (approved.Count == 0) return false;

            bool shape = AppSettingsStore.GetShapeArabic();
            string modsRoot = ModsRoot(gameFolderPath);
            string modDir = Path.Combine(modsRoot, "hufra_arabic");
            var utf8Bom = new UTF8Encoding(true);

            try
            {
                if (Directory.Exists(modDir)) Directory.Delete(modDir, true);
                Directory.CreateDirectory(Path.Combine(modDir, ".metadata"));

                foreach (var group in approved.GroupBy(e => e.SourceFile))
                {
                    // نحافظ على اسم الملف (لازم ينتهي بـ _l_english.yml) داخل مجلد replace
                    string fileName = Path.GetFileName(group.Key);
                    string outPath = Path.Combine(modDir, "localization", "replace", "english", "hufra_" + fileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath));

                    var sb = new StringBuilder();
                    sb.Append("l_english:\n");
                    foreach (var e in group)
                    {
                        var parts = e.LocationKey.Split(':');
                        if (parts.Length < 2) continue;
                        string text = shape ? ArabicShaper.PrepareForLtrEngine(e.TranslatedText) : e.TranslatedText;
                        text = text.Replace("\r\n", "\n").Replace("\n", "\\n").Replace("\"", "\\\"");
                        string ver = parts.Length > 2 ? parts[2] : "0";
                        sb.Append(' ').Append(parts[1]).Append(':').Append(ver).Append(" \"").Append(text).Append("\"\n");
                    }
                    File.WriteAllText(outPath, sb.ToString(), utf8Bom);
                }

                File.WriteAllText(Path.Combine(modDir, ".metadata", "metadata.json"),
                    "{\n  \"name\": \"Hufra Arabic\",\n  \"id\": \"hufra_arabic\",\n  \"version\": \"1.0\",\n" +
                    "  \"supported_game_version\": \"*\",\n  \"short_description\": \"Arabic localization generated by Hufra\",\n" +
                    "  \"tags\": [\"Translation\"],\n  \"relations\": [],\n  \"game_custom_data\": { \"multiplayer_synchronized\": false }\n}\n",
                    new UTF8Encoding(false));

                File.WriteAllText(Path.Combine(modDir, "descriptor.mod"),
                    "version=\"1.0\"\ntags={\n\t\"Translation\"\n}\nname=\"Hufra Arabic\"\nsupported_version=\"*\"\n",
                    new UTF8Encoding(false));

                // ملف التعريف الخارجي اللي يقرأه اللانشر
                string modFile = Path.Combine(modsRoot, "hufra_arabic.mod");
                File.WriteAllText(modFile,
                    "version=\"1.0\"\ntags={\n\t\"Translation\"\n}\nname=\"Hufra Arabic\"\nsupported_version=\"*\"\n" +
                    "path=\"" + modDir.Replace('\\', '/') + "\"\n",
                    new UTF8Encoding(false));

                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool RemoveArabization(string gameFolderPath)
        {
            try
            {
                string modsRoot = ModsRoot(gameFolderPath);
                string modDir = Path.Combine(modsRoot, "hufra_arabic");
                if (Directory.Exists(modDir)) Directory.Delete(modDir, true);
                string modFile = Path.Combine(modsRoot, "hufra_arabic.mod");
                if (File.Exists(modFile)) File.Delete(modFile);
                return true;
            }
            catch { return false; }
        }
    }
}
