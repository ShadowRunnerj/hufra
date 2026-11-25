using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Hufra.Core.Models;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// Adapter لملفات ترجمة XML بصيغة &lt;string id="..." text="..." /&gt; - نمط شائع
    /// بمحركات خاصة (custom) كثيرة (أشهر مثال: محرك TaleWorlds في Mount &amp; Blade II:
    /// Bannerlord، ويستخدمه المجتمع فعلاً لصنع ترجمات مثل هذه اللعبة بالضبط).
    ///
    /// هذا الـ Adapter عام: يشتغل مع أي لعبة تستخدم نفس نمط السمات (id, text) بغض
    /// النظر عن اسم المحرك، لأن الصيغة نفسها (لا المحرك) هي اللي نتعرف عليها.
    /// </summary>
    public class XmlLocalizationAdapter : IEngineAdapter
    {
        public EngineType Engine => EngineType.XmlLocalization;
        public string DisplayName => "ملفات ترجمة XML (محرك خاص)";

        // يلقط: id="..." text="..." بأي ترتيب سمات (id قبل أو بعد text)، مع سمات إضافية بينهم أحياناً
        private static readonly Regex StringElementRegex = new Regex(
            "<string\\s+(?:[a-zA-Z_]+=\"[^\"]*\"\\s+)*id=\"(?<id>[^\"]*)\"(?:\\s+[a-zA-Z_]+=\"[^\"]*\")*\\s+text=\"(?<text>[^\"]*)\"",
            RegexOptions.Compiled);

        public int DetectConfidence(string gameFolderPath)
        {
            foreach (var r in EngineDetector.DetectAll(gameFolderPath))
                if (r.Engine == Engine) return r.Confidence;
            return 0;
        }

        // نفس تلميحات أسماء المجلدات المستخدمة بالكشف - نعطيها أولوية بالاستخراج
        // لتفادي فحص آلاف ملفات XML الغير متعلقة بالنص (شبكات، مواد، فيزياء...) بالألعاب الكبيرة
        private static readonly string[] LanguageFolderHints = { "language", "languages", "localiz", "strings" };
        private const int MaxFilesWhenNoHintFolder = 800; // سقف أمان لو ما فيه مجلد لغة واضح، لتفادي بطء شديد

        public IReadOnlyList<TextEntry> ExtractTexts(string gameFolderPath)
        {
            var results = new List<TextEntry>();
            var allXmlFiles = LooseTextExtractor.SafeGetFiles(gameFolderPath, "*.xml").ToList();

            var hintedFiles = allXmlFiles.Where(f =>
                LanguageFolderHints.Any(hint => f.Contains(hint, StringComparison.OrdinalIgnoreCase))).ToList();

            // لو لقينا مجلد لغة واضح، نكتفي فيه بس - أسرع بمراحل وأدق (يتجنب ملفات XML تقنية بعيدة عن النصوص)
            var filesToScan = hintedFiles.Count > 0 ? hintedFiles : allXmlFiles.Take(MaxFilesWhenNoHintFolder);

            foreach (var file in filesToScan)
            {
                ExtractFromXmlFile(gameFolderPath, file, results);
            }

            return results;
        }

        private void ExtractFromXmlFile(string root, string filePath, List<TextEntry> results)
        {
            string content;
            try { content = File.ReadAllText(filePath); }
            catch { return; }

            if (!content.Contains("<string ", StringComparison.OrdinalIgnoreCase)) return;

            string relative = Path.GetRelativePath(root, filePath);

            foreach (Match match in StringElementRegex.Matches(content))
            {
                string id = match.Groups["id"].Value;
                string rawText = match.Groups["text"].Value;
                string text = UnescapeXml(rawText);

                if (string.IsNullOrWhiteSpace(text) || text.Length < 2) continue;
                if (!LooseTextExtractor.LooksLikeDisplayText(text)) continue;

                results.Add(new TextEntry
                {
                    SourceFile = relative,
                    LocationKey = $"xmlid:{id}",
                    OriginalText = text,
                    HasPlaceholders = LooseTextExtractor.ContainsPlaceholder(text),
                });
            }
        }

        public bool ApplyTranslations(string gameFolderPath, IReadOnlyList<TextEntry> entries)
        {
            var byFile = new Dictionary<string, List<TextEntry>>();
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.TranslatedText) || !e.IsApproved) continue;
                if (!byFile.TryGetValue(e.SourceFile, out var list))
                    byFile[e.SourceFile] = list = new List<TextEntry>();
                list.Add(e);
            }

            bool allOk = true;
            foreach (var kv in byFile)
            {
                string fullPath = Path.Combine(gameFolderPath, kv.Key);
                if (!File.Exists(fullPath)) { allOk = false; continue; }

                try { ApplyToXmlFile(fullPath, kv.Value); }
                catch (Exception) { allOk = false; }
            }

            return allOk;
        }

        private void ApplyToXmlFile(string fullPath, List<TextEntry> entries)
        {
            string content = File.ReadAllText(fullPath);

            // فهرسة الترجمات بالـ id لتطبيق سريع بمرور واحد على الملف
            var byId = new Dictionary<string, string>();
            foreach (var e in entries)
            {
                if (e.LocationKey.StartsWith("xmlid:"))
                    byId[e.LocationKey.Substring("xmlid:".Length)] = e.TranslatedText;
            }

            string updated = StringElementRegex.Replace(content, match =>
            {
                string id = match.Groups["id"].Value;
                if (!byId.TryGetValue(id, out var translated))
                    return match.Value; // ما تغيّر - نرجّع نفس المطابقة كما هي

                string escapedNew = EscapeXml(translated);
                var textGroup = match.Groups["text"];

                // نستبدل بس محتوى سمة text داخل المطابقة، ونحافظ على باقي السمات كما هي
                int startInMatch = textGroup.Index - match.Index;
                string before = match.Value.Substring(0, startInMatch);
                string after = match.Value.Substring(startInMatch + textGroup.Length);
                return before + escapedNew + after;
            });

            File.WriteAllText(fullPath, updated);
        }

        private static string UnescapeXml(string s) =>
            s.Replace("&quot;", "\"").Replace("&apos;", "'")
             .Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&");

        private static string EscapeXml(string s) =>
            s.Replace("&", "&amp;").Replace("\"", "&quot;")
             .Replace("<", "&lt;").Replace(">", "&gt;");

        public bool RemoveArabization(string gameFolderPath) =>
            throw new NotImplementedException("استعادة النسخة الأصلية تتم عبر BackupManager العام.");
    }
}
