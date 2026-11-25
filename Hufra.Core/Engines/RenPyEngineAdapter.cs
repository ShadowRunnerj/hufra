using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Hufra.Core.Models;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// Adapter محرك Ren'Py.
    ///
    /// ملاحظة نطاق مهمة: أغلب ألعاب Ren'Py المنشورة تُجمَّع نصوصها كملفات
    /// .rpyc (bytecode ثنائي) وأحياناً تُحزم داخل أرشيف .rpa. هذا الـ Adapter
    /// يدعم فقط ملفات .rpy المصدرية النصية (لو المطوّر تركها كما هي بجانب
    /// النسخة المجمّعة - شائع بألعاب indie المجانية وأقل شيوعاً بالتجارية).
    ///
    /// نستخدم قواعد صياغة Ren'Py المعروفة (سطر حوار = اسم شخصية اختياري +
    /// نص بين علامتي اقتباس) بدل استخدام الفاحص العام للملفات النصية، لأن
    /// أكواد Ren'Py العادية (jump, label, if...) تحتوي نصوص كثيرة لا نريد لمسها.
    /// </summary>
    public class RenPyEngineAdapter : IEngineAdapter
    {
        public EngineType Engine => EngineType.RenPy;
        public string DisplayName => "Ren'Py";

        // سطر حوار نموذجي: [اسم متغير الشخصية] "النص هنا" [:] - نتجاهل كل شي عدا هذا الشكل
        private static readonly Regex DialogueLineRegex = new Regex(
            @"^\s*(?:[A-Za-z_][A-Za-z0-9_]*\s+)?""((?:[^""\\]|\\.)*)""\s*:?\s*$",
            RegexOptions.Compiled);

        // أول كلمة بالسطر لو كانت من هذي، فالسطر كود مو حوار (حتى لو فيه علامات اقتباس)
        private static readonly HashSet<string> CodeKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "label", "jump", "call", "menu", "if", "elif", "else", "python", "init",
            "define", "default", "image", "show", "scene", "hide", "return", "screen",
            "transform", "style", "translate", "$", "while", "for", "pass", "window",
        };

        public int DetectConfidence(string gameFolderPath)
        {
            foreach (var r in EngineDetector.DetectAll(gameFolderPath))
                if (r.Engine == Engine) return r.Confidence;
            return 0;
        }

        public IReadOnlyList<TextEntry> ExtractTexts(string gameFolderPath)
        {
            var results = new List<TextEntry>();

            foreach (var file in LooseTextExtractor.SafeGetFiles(gameFolderPath, "*.rpy"))
            {
                // نتجاوز ملفات الترجمة الجاهزة (game/tl/...) لأن صياغتها مختلفة (أزواج old/new)
                if (file.Replace('\\', '/').Contains("/tl/", StringComparison.OrdinalIgnoreCase)) continue;

                ExtractFromRpyFile(gameFolderPath, file, results);
            }

            return results;
        }

        private void ExtractFromRpyFile(string root, string filePath, List<TextEntry> results)
        {
            string relative = Path.GetRelativePath(root, filePath);
            string[] lines;
            try { lines = File.ReadAllLines(filePath); }
            catch { return; }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if (trimmed.Length == 0 || trimmed.StartsWith("#")) continue;

                string firstToken = trimmed.Split(new[] { ' ', '(', ':' }, 2)[0];
                if (CodeKeywords.Contains(firstToken)) continue;

                var match = DialogueLineRegex.Match(line);
                if (!match.Success) continue;

                string raw = match.Groups[1].Value;
                string text = raw.Replace("\\\"", "\"").Replace("\\'", "'");

                if (text.Length < 2 || !ContainsLetter(text)) continue;

                results.Add(new TextEntry
                {
                    SourceFile = relative,
                    LocationKey = $"rpyline:{i}",
                    OriginalText = text,
                    HasPlaceholders = text.Contains('[') || text.Contains('{'),
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

                try { ApplyToRpyFile(fullPath, kv.Value); }
                catch (Exception) { allOk = false; }
            }

            return allOk;
        }

        private void ApplyToRpyFile(string fullPath, List<TextEntry> entries)
        {
            var lines = File.ReadAllLines(fullPath);

            foreach (var e in entries)
            {
                if (!e.LocationKey.StartsWith("rpyline:")) continue;
                if (!int.TryParse(e.LocationKey.Substring("rpyline:".Length), out int lineIndex)) continue;
                if (lineIndex < 0 || lineIndex >= lines.Length) continue;

                string original = e.OriginalText.Replace("\"", "\\\"");
                string translated = (e.TranslatedText ?? "").Replace("\"", "\\\"");

                string quotedOriginal = "\"" + original + "\"";
                string quotedTranslated = "\"" + translated + "\"";

                int idx = lines[lineIndex].IndexOf(quotedOriginal, StringComparison.Ordinal);
                if (idx >= 0)
                {
                    lines[lineIndex] = lines[lineIndex].Substring(0, idx) + quotedTranslated +
                                        lines[lineIndex].Substring(idx + quotedOriginal.Length);
                }
            }

            File.WriteAllLines(fullPath, lines);
        }

        private static bool ContainsLetter(string s)
        {
            foreach (char c in s)
                if (char.IsLetter(c)) return true;
            return false;
        }

        public bool RemoveArabization(string gameFolderPath) =>
            throw new NotImplementedException("استعادة النسخة الأصلية تتم عبر BackupManager العام.");
    }
}
