using System;
using System.Collections.Generic;
using System.IO;
using Hufra.Core.Models;
using System.Linq;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// Adapter محرك Unity.
    ///
    /// ملاحظة نطاق مهمة: ألعاب Unity تخزّن أغلب نصوصها داخل ملفات ثنائية
    /// مضغوطة (resources.assets, *.bundle) يصعب تحليلها بدون مكتبة متخصصة
    /// (مثل AssetsTools.NET). هذا الـ Adapter يغطي فقط النصوص "الحرة" الشائعة:
    /// ملفات JSON/txt داخل StreamingAssets أو Resources غير المعبأة.
    /// </summary>
    public class UnityEngineAdapter : IEngineAdapter
    {
        public EngineType Engine => EngineType.Unity;
        public string DisplayName => "Unity";

        private static readonly string[] TextFileExtensions = { ".json", ".txt", ".csv" };
        private static readonly string[] CandidateFolders = { "StreamingAssets", "Resources" };

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

            // نصوص ملتقطة بواسطة XUnity (لو ركّبنا الأداة وشغّل المستخدم اللعبة)
            results.AddRange(UnityXUnityTool.ReadCapture(gameFolderPath));

            return results;
        }

        public bool ApplyTranslations(string gameFolderPath, IReadOnlyList<TextEntry> entries)
        {
            bool allOk = true;
            var captured = entries.Where(e => e.SourceFile == UnityXUnityTool.CaptureRel).ToList();
            if (captured.Count > 0)
            {
                try { allOk &= UnityXUnityTool.WriteCapture(gameFolderPath, captured); } catch { allOk = false; }
            }

            var byFile = new Dictionary<string, List<TextEntry>>();
            foreach (var e in entries)
            {
                if (e.SourceFile == UnityXUnityTool.CaptureRel) continue;
                if (string.IsNullOrEmpty(e.TranslatedText) || !e.IsApproved) continue;
                if (!byFile.TryGetValue(e.SourceFile, out var list))
                    byFile[e.SourceFile] = list = new List<TextEntry>();
                list.Add(e);
            }

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
            throw new NotImplementedException(
                "استعادة النسخة الأصلية تتم عبر BackupManager العام، راجع طبقة النسخ الاحتياطي.");
    }
}
