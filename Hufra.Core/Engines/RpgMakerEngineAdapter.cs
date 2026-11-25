using System;
using System.Collections.Generic;
using System.IO;
using Hufra.Core.Models;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// Adapter محرك RPG Maker (نسختي MV وMZ تحديداً).
    ///
    /// خبر جيد بالنسبة لهذا المحرك: على عكس Unity/Godot/Unreal، ألعاب RPG
    /// Maker MV/MZ تخزّن كل بياناتها (حوارات، أسماء، أحداث) كملفات JSON عادية
    /// غير معبأة أو مشفّرة داخل مجلد data (أو www/data بالنسخ الأقدم) - إلا
    /// لو المطوّر فعّل خيار "تشفير الموارد" صراحة عند التصدير. هذا يعني دعم
    /// حقيقي وواسع لأغلب ألعاب RPG Maker المنشورة بدون تعديل إضافي.
    ///
    /// نسخة VX/VX Ace الأقدم تستخدم أرشيف RGSS مضغوط (صيغة مختلفة تماماً) -
    /// غير مدعومة هنا حالياً.
    /// </summary>
    public class RpgMakerEngineAdapter : IEngineAdapter
    {
        public EngineType Engine => EngineType.RpgMaker;
        public string DisplayName => "RPG Maker (MV/MZ)";

        private static readonly string[] CandidateFolders = { "data" };

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
                    foreach (var file in LooseTextExtractor.SafeGetFiles(dir, "*.json"))
                    {
                        if (LooseTextExtractor.IsExcludedFileName(file)) continue;
                        LooseTextExtractor.ExtractFromJson(gameFolderPath, file, results);
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

                try { LooseTextExtractor.ApplyJson(fullPath, kv.Value); }
                catch (Exception) { allOk = false; }
            }

            return allOk;
        }

        public bool RemoveArabization(string gameFolderPath) =>
            throw new NotImplementedException("استعادة النسخة الأصلية تتم عبر BackupManager العام.");
    }
}
