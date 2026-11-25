using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// يحدد الملف التنفيذي الرئيسي للعبة داخل مجلدها ويشغّله.
    /// أغلب مجلدات الألعاب فيها أكثر من ملف .exe واحد (أدوات فحص الأعطال،
    /// إعادة توزيع مكتبات، أنظمة مكافحة غش...)، فنحتاج نستبعد المعروف منها
    /// ونرجّح الأقرب لاسم مجلد اللعبة أو الأكبر حجماً.
    /// </summary>
    public static class GameLauncher
    {
        // أسماء شائعة لملفات مساعدة مش اللعبة نفسها - نستبعدها من الترشيح
        private static readonly string[] ExcludedPatterns =
        {
            "unitycrashhandler", "crashpad", "vcredist", "dxsetup", "dxwebsetup",
            "easyanticheat", "battleye", "unrealcefsubprocess", "installer",
            "uninstall", "redist", "helper", "updater", "launcher_", "cef_",
        };

        /// <summary>يرجّع أفضل مرشح لملف اللعبة التنفيذي، أو null لو ما لقى شي مناسب</summary>
        public static string FindMainExecutable(string gameFolderPath)
        {
            if (!Directory.Exists(gameFolderPath)) return null;

            var candidates = Directory.GetFiles(gameFolderPath, "*.exe", SearchOption.TopDirectoryOnly)
                .Where(f => !ExcludedPatterns.Any(p =>
                    Path.GetFileName(f).Contains(p, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            if (candidates.Count == 0) return null;
            if (candidates.Count == 1) return candidates[0];

            string folderName = new DirectoryInfo(gameFolderPath).Name;

            // نرجّح أول ملف اسمه يطابق اسم مجلد اللعبة (نمط شائع جداً)
            var nameMatch = candidates.FirstOrDefault(f =>
                Path.GetFileNameWithoutExtension(f).Equals(folderName, StringComparison.OrdinalIgnoreCase));
            if (nameMatch != null) return nameMatch;

            // وإلا نرجّح الأكبر حجماً (عادة اللعبة الفعلية أثقل من أي أداة مرافقة)
            return candidates.OrderByDescending(f => new FileInfo(f).Length).First();
        }

        /// <summary>يشغّل اللعبة. يرمي استثناء واضح لو ما لقى ملف تنفيذي مناسب</summary>
        public static Process Launch(string gameFolderPath)
        {
            string exePath = FindMainExecutable(gameFolderPath);
            if (exePath == null)
                throw new FileNotFoundException(
                    "ما قدرنا نحدد الملف التنفيذي الرئيسي لهذه اللعبة تلقائياً. جرّب تشغيلها يدوياً من مجلدها.");

            return Process.Start(new ProcessStartInfo(exePath)
            {
                WorkingDirectory = gameFolderPath, // مهم: ألعاب كثيرة تفشل لو الـ working directory غلط
                UseShellExecute = true,
            });
        }
    }
}
