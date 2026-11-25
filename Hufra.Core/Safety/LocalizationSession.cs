using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Hufra.Core.Engines;
using Hufra.Core.Models;
using Hufra.Core.Text;

namespace Hufra.Core.Safety
{
    public enum VerificationStatus { Success, Partial, Failed }

    public class VerificationReport
    {
        public VerificationStatus Status { get; set; }
        public int FilesExpected { get; set; }
        public int FilesChanged { get; set; }
        public int TextsChecked { get; set; }
        public int TextsVerified { get; set; }
        public List<string> Problems { get; } = new();
        public string Summary { get; set; }
    }

    /// <summary>
    /// تطبيق التعريب بخطوات آمنة: نسخ احتياطي ← تركيب ← تحقق فعلي من محتوى الملفات.
    /// ما نعلن النجاح إلا لو الملفات تغيّرت فعلاً وفيها النصوص المترجمة.
    /// </summary>
    public static class LocalizationSession
    {
        public static VerificationReport ApplyAndVerify(IEngineAdapter adapter, string gameFolder, IReadOnlyList<TextEntry> entries)
        {
            var approved = entries.Where(e => e.IsApproved && !string.IsNullOrWhiteSpace(e.TranslatedText)).ToList();
            var report = new VerificationReport();
            if (approved.Count == 0)
            {
                report.Status = VerificationStatus.Failed;
                report.Summary = "لا يوجد نصوص مترجمة ومفعّلة للتطبيق.";
                return report;
            }

            // ألعاب Paradox: المخرجات مود خارج مجلد اللعبة، فما نحتاج نسخ ملفات اللعبة
            if (adapter is ParadoxLocalizationAdapter)
            {
                bool ok = adapter.ApplyTranslations(gameFolder, entries);
                return ParadoxLocalizationAdapter.VerifyMod(gameFolder, approved.Count, ok);
            }

            var rels = approved.Select(e => e.SourceFile).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            report.FilesExpected = rels.Count;

            try { BackupManager.BackupFiles(gameFolder, rels); }
            catch (Exception ex)
            {
                report.Status = VerificationStatus.Failed;
                report.Summary = $"تعذّر إنشاء النسخة الاحتياطية، فلم أعدّل أي ملف: {ex.Message}";
                return report;
            }

            bool applied;
            try { applied = adapter.ApplyTranslations(gameFolder, entries); }
            catch (Exception ex)
            {
                report.Status = VerificationStatus.Failed;
                report.Summary = $"فشل التركيب: {ex.Message}";
                return report;
            }

            Verify(adapter, gameFolder, approved, rels, report, applied);
            return report;
        }

        private static void Verify(IEngineAdapter adapter, string gameFolder, List<TextEntry> approved,
                                   List<string> rels, VerificationReport report, bool adapterReportedOk)
        {
            var manifest = BackupManager.Load(gameFolder);
            var fileText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var rel in rels)
            {
                string full = Path.Combine(gameFolder, rel);
                var original = manifest.Files.FirstOrDefault(f => f.Rel.Equals(rel, StringComparison.OrdinalIgnoreCase));
                if (!File.Exists(full)) { report.Problems.Add($"الملف غير موجود بعد التركيب: {rel}"); continue; }
                if (original != null && BackupManager.Sha256(full) != original.Sha256) report.FilesChanged++;
                else report.Problems.Add($"لم يتغيّر الملف: {rel}");
            }

            // إعادة استخراج النصوص من الملفات المعدّلة ومقارنتها بالترجمة
            var current = new Dictionary<string, string>();
            try
            {
                foreach (var e in adapter.ExtractTexts(gameFolder))
                    current[e.SourceFile + "|" + e.LocationKey] = e.OriginalText;
            }
            catch { }

            foreach (var e in approved.Take(3000))
            {
                report.TextsChecked++;
                bool found = current.TryGetValue(e.SourceFile + "|" + e.LocationKey, out var t) && t == e.TranslatedText;
                if (!found)
                {
                    if (!fileText.TryGetValue(e.SourceFile, out var content))
                    {
                        try { content = File.ReadAllText(Path.Combine(gameFolder, e.SourceFile), Encoding.UTF8); }
                        catch { try { content = File.ReadAllText(Path.Combine(gameFolder, e.SourceFile), Encoding.Unicode); } catch { content = ""; } }
                        fileText[e.SourceFile] = content;
                    }
                    found = ContainsProbe(content, e.TranslatedText) ||
                            ContainsProbe(content, ArabicShaper.PrepareForLtrEngine(e.TranslatedText));
                }
                if (found) report.TextsVerified++;
                else if (report.Problems.Count < 8) report.Problems.Add($"لم أجد الترجمة في {e.SourceFile}: «{Trim(e.TranslatedText)}»");
            }

            double ratio = report.TextsChecked == 0 ? 0 : (double)report.TextsVerified / report.TextsChecked;
            if (report.FilesChanged == 0) report.Status = VerificationStatus.Failed;
            else if (report.FilesChanged == report.FilesExpected && ratio >= 0.9 && adapterReportedOk) report.Status = VerificationStatus.Success;
            else report.Status = VerificationStatus.Partial;

            report.Summary = report.Status switch
            {
                VerificationStatus.Success =>
                    $"تم تعديل {report.FilesChanged} ملف فعلياً، وتحققت من {report.TextsVerified} من {report.TextsChecked} نص داخل الملفات.",
                VerificationStatus.Partial =>
                    $"تعديل جزئي: تغيّر {report.FilesChanged} من {report.FilesExpected} ملف، وتحققت من {report.TextsVerified} من {report.TextsChecked} نص.",
                _ => "لم يتغيّر أي ملف من ملفات اللعبة. لم يتم التعريب."
            };
        }

        private static string Trim(string s) => s.Length > 40 ? s.Substring(0, 40) + "…" : s;

        private static bool ContainsProbe(string content, string translated)
        {
            if (string.IsNullOrEmpty(content)) return false;
            string probe = translated.Replace("\r\n", "\n").Split('\n')[0];
            int cut = probe.IndexOfAny(new[] { '"', '\\' });
            if (cut > 0) probe = probe.Substring(0, cut);
            if (probe.Length > 25) probe = probe.Substring(0, 25);
            if (probe.Length < 2) return false;

            if (content.Contains(probe, StringComparison.Ordinal)) return true;

            // JSON/properties قد تكتب غير-ASCII بصيغة \uXXXX
            var sb = new StringBuilder();
            foreach (char c in probe) sb.Append(c > 127 ? $"\\u{(int)c:x4}" : c.ToString());
            return content.Contains(sb.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }
}
