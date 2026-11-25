using System;
using System.Collections.Generic;
using System.IO;
using Hufra.Core.Models;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// Adapter محرك Unreal Engine.
    ///
    /// ملاحظة نطاق مهمة: أغلب ألعاب Unreal المنشورة تحزّم كل أصولها (بما فيها
    /// النصوص) داخل ملفات .pak (أحياناً مشفّرة بمفتاح AES خاص باللعبة). فك
    /// هذي الملفات يحتاج أداة UnrealPak أو مكتبة تحليل صيغة .pak (مثل
    /// CUE4Parse)، وأحياناً معرفة مفتاح التشفير الخاص باللعبة نفسها - ما
    /// نقدر نضمنه بشكل عام لكل لعبة.
    ///
    /// هذا الـ Adapter يغطي فقط النصوص "الحرة" الموجودة خارج .pak: ملفات
    /// JSON/txt/ini المستخدمة أحياناً من ألعاب indie أو نسخ تطوير غير معبأة
    /// بالكامل (unpaked content)، بالإضافة لملفات .csv/.json داخل مجلدات
    /// Content/Localization لو كانت موجودة كملفات مصدر (Archive) غير مُجمّعة
    /// بعد كملف .locres الثنائي.
    /// </summary>
    public class UnrealEngineAdapter : IEngineAdapter
    {
        public EngineType Engine => EngineType.UnrealEngine;
        public string DisplayName => "Unreal Engine";

        private static readonly string[] TextFileExtensions = { ".json", ".txt", ".csv" };
        private static readonly string[] CandidateFolders = { "Content", "Localization" };

        public int DetectConfidence(string gameFolderPath)
        {
            foreach (var r in EngineDetector.DetectAll(gameFolderPath))
                if (r.Engine == Engine) return r.Confidence;
            return 0;
        }

        public IReadOnlyList<TextEntry> ExtractTexts(string gameFolderPath)
        {
            var results = new List<TextEntry>();

            foreach (var candidateFolder in CandidateFolders)
            {
                foreach (var dir in LooseTextExtractor.SafeFindDirs(gameFolderPath, candidateFolder))
                {
                    foreach (var file in LooseTextExtractor.SafeGetFiles(dir))
                    {
                        if (LooseTextExtractor.IsExcludedFileName(file)) continue;

                        var ext = Path.GetExtension(file).ToLowerInvariant();
                        if (Array.IndexOf(TextFileExtensions, ext) < 0) continue;

                        if (ext == ".json")
                            LooseTextExtractor.ExtractFromJson(gameFolderPath, file, results);
                        else
                            LooseTextExtractor.ExtractFromPlainText(gameFolderPath, file, results);
                    }
                }
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
