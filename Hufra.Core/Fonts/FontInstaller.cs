using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Hufra.Core.Safety;

namespace Hufra.Core.Fonts
{
    public class FontScanItem
    {
        public string Rel { get; set; }
        public bool SupportsArabic { get; set; }
        public bool Replaceable { get; set; }
        public string Note { get; set; }
    }

    public class FontInstallResult
    {
        public int Replaced { get; set; }
        public int Verified { get; set; }
        public List<string> Failed { get; } = new();
    }

    /// <summary>
    /// يفحص الخطوط الحرة (TTF) داخل مجلد اللعبة ويستبدل اللي ما يدعم العربية بخط Noto Sans Arabic (ترخيص OFL)
    /// بنفس اسم الملف عشان اللعبة تحمّله بدون تعديل إعداداتها. الأصل يُحفظ ويمكن استعادته.
    /// ما يقدر على الخطوط المضمّنة داخل ملفات محزومة (Unity assets، Unreal pak، Godot pck).
    /// </summary>
    public static class FontInstaller
    {
        private static readonly string[] SkipNames = { "icon", "symbol", "emoji", "awesome", "glyph", "dingbat", "material", "wingding" };

        private static string FontsDir => Path.Combine(AppContext.BaseDirectory, "Fonts");

        public static List<FontScanItem> Scan(string gameFolder)
        {
            var items = new List<FontScanItem>();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(gameFolder, "*.*", SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".otf", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".ttc", StringComparison.OrdinalIgnoreCase))
                    .Take(300).ToList();
            }
            catch { return items; }

            foreach (var f in files)
            {
                string rel = Path.GetRelativePath(gameFolder, f);
                string name = Path.GetFileNameWithoutExtension(f);
                var item = new FontScanItem { Rel = rel };

                if (SkipNames.Any(s => name.Contains(s, StringComparison.OrdinalIgnoreCase)))
                { item.Note = "خط رموز/أيقونات - تم تجاهله"; items.Add(item); continue; }

                var cov = FontInspector.Inspect(f);
                if (!cov.Readable) { item.Note = "صيغة غير مدعومة للفحص"; items.Add(item); continue; }

                item.SupportsArabic = cov.Arabic;
                if (cov.Arabic) item.Note = "يدعم العربية";
                else if (!f.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)) item.Note = "لا يدعم العربية، لكن صيغته (otf/ttc) لا يمكن استبدالها بأمان";
                else { item.Replaceable = true; item.Note = "لا يدعم العربية - قابل للاستبدال"; }
                items.Add(item);
            }
            return items;
        }

        public static FontInstallResult Install(string gameFolder, IEnumerable<FontScanItem> items)
        {
            var result = new FontInstallResult();
            string regular = Path.Combine(FontsDir, "ArabicRegular.ttf");
            string bold = Path.Combine(FontsDir, "ArabicBold.ttf");
            if (!File.Exists(regular)) throw new FileNotFoundException("ملف الخط العربي المرفق مع البرنامج غير موجود: " + regular);

            var targets = items.Where(i => i.Replaceable).ToList();
            BackupManager.BackupFiles(gameFolder, targets.Select(t => t.Rel)); // يرمي استثناء لو فشل - ما نعدّل بدون حماية

            foreach (var t in targets)
            {
                try
                {
                    string dst = Path.Combine(gameFolder, t.Rel);
                    string name = Path.GetFileNameWithoutExtension(dst);
                    bool isBold = name.Contains("bold", StringComparison.OrdinalIgnoreCase) ||
                                  name.Contains("black", StringComparison.OrdinalIgnoreCase) ||
                                  name.Contains("heavy", StringComparison.OrdinalIgnoreCase);
                    File.Copy(isBold && File.Exists(bold) ? bold : regular, dst, true);
                    result.Replaced++;

                    if (FontInspector.Inspect(dst).Arabic) result.Verified++;
                    else result.Failed.Add(t.Rel + " (لم يتحقق دعم العربية بعد النسخ)");
                }
                catch (Exception ex) { result.Failed.Add($"{t.Rel} ({ex.Message})"); }
            }
            return result;
        }
    }
}
