using System;
using System.Collections.Generic;
using System.IO;
using Hufra.Core.Models;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// Adapter محرك Godot.
    ///
    /// ملاحظة نطاق مهمة: الإعداد الافتراضي عند تصدير لعبة Godot هو "تضمين
    /// ملف .pck داخل الملف التنفيذي نفسه" - وبهذا الوضع ما فيه أي ملفات نصية
    /// حرة نقدر نوصلها من الخارج إطلاقاً (كل شي، حتى الأكواد، معبّى بأرشيف
    /// ثنائي واحد). فك تشفير هذا الأرشيف ممكن تقنياً (الصيغة موثقة) لكنه
    /// يحتاج قارئ/كاتب لصيغة .pck الخاصة بـ Godot (وتختلف قليلاً بين إصدار
    /// 3.x و4.x) - مو مبني هنا بعد.
    ///
    /// هذا الـ Adapter يغطي فقط الحالة اللي يكون فيها المطور اختار عدم تضمين
    /// الحزمة (نادر لكن موجود، خصوصاً بالألعاب اللي تدعم المودات)، أو ألعاب
    /// تقرأ ملفات JSON/txt خارجية بجانب اللعبة لأي سبب.
    /// </summary>
    public class GodotEngineAdapter : IEngineAdapter
    {
        public EngineType Engine => EngineType.Godot;
        public string DisplayName => "Godot";

        private static readonly string[] TextFileExtensions = { ".json", ".txt", ".csv" };
        // نستبعد هذي عشان ما نلقط ملفات إعداد Godot الداخلية اللي مو نصوص لعبة فعلية
        private static readonly string[] ExcludedFileNames = { "project.godot", "export_presets.cfg" };

        public int DetectConfidence(string gameFolderPath)
        {
            foreach (var r in EngineDetector.DetectAll(gameFolderPath))
                if (r.Engine == Engine) return r.Confidence;
            return 0;
        }

        public IReadOnlyList<TextEntry> ExtractTexts(string gameFolderPath)
        {
            var results = new List<TextEntry>();

            // ما فيه مجلد موحّد متعارف عليه بقودوت زي StreamingAssets بيونتي،
            // فنفحص المجلد كامل، ونستبعد الملفات الثنائية الكبيرة (.pck نفسه لن يُقرأ كـ JSON/txt أصلاً)
            foreach (var file in LooseTextExtractor.SafeGetFiles(gameFolderPath))
            {
                string name = Path.GetFileName(file);
                if (Array.IndexOf(ExcludedFileNames, name) >= 0) continue;
                if (LooseTextExtractor.IsExcludedFileName(file)) continue;

                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (Array.IndexOf(TextFileExtensions, ext) < 0) continue;

                if (ext == ".json")
                    LooseTextExtractor.ExtractFromJson(gameFolderPath, file, results);
                else
                    LooseTextExtractor.ExtractFromPlainText(gameFolderPath, file, results);
            }

            return results;
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

                try
                {
                    if (Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                        LooseTextExtractor.ApplyJson(fullPath, kv.Value);
                    else
                        LooseTextExtractor.ApplyPlainText(fullPath, kv.Value);
                }
                catch (Exception)
                {
                    allOk = false;
                }
            }

            return allOk;
        }

        public bool RemoveArabization(string gameFolderPath) =>
            throw new NotImplementedException("استعادة النسخة الأصلية تتم عبر BackupManager العام.");
    }
}
