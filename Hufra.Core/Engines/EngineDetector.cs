using System;
using System.IO;
using System.Linq;

namespace Hufra.Core.Engines
{
    public class DetectionResult
    {
        public EngineType Engine { get; set; }
        public int Confidence { get; set; } // 0-100
        public string Reason { get; set; }   // سبب القرار، يُعرض للمستخدم لو احتاج تفاصيل
    }

    /// <summary>
    /// يفحص مجلد اللعبة بحثاً عن "بصمات" (ملفات أو مجلدات مميزة) تدل على محرك معين.
    /// هذا الفحص سريع (بدون فتح/تحليل الملفات الثنائية) ويعطي تخمين أولي فقط؛
    /// الـ Adapter المختص هو من يؤكد لاحقاً عند محاولة الاستخراج الفعلي.
    /// </summary>
    public static class EngineDetector
    {
        public static DetectionResult[] DetectAll(string gameFolderPath)
        {
            if (!Directory.Exists(gameFolderPath))
                return Array.Empty<DetectionResult>();

            var files = SafeGetFiles(gameFolderPath);
            var dirs = SafeGetDirs(gameFolderPath);

            var results = new[]
            {
                DetectUnity(gameFolderPath, files, dirs),
                DetectUnreal(gameFolderPath, files, dirs),
                DetectGodot(gameFolderPath, files, dirs),
                DetectGameMaker(gameFolderPath, files, dirs),
                DetectRpgMaker(gameFolderPath, files, dirs),
                DetectRenPy(gameFolderPath, files, dirs),
                DetectXmlLocalization(gameFolderPath, files, dirs),
            };

            // محركات الملفات النصية (po/properties/Source/Tyrano/JSON) - كل Adapter يفحص بنفسه
            results = results.Concat(AdapterRegistry.All.OfType<TextFileAdapterBase>()
                                      .Select(a => a.Detect(gameFolderPath, files))).ToArray();

            var paradox = (AdapterRegistry.Get(EngineType.Paradox) as ParadoxLocalizationAdapter)?.Detect(gameFolderPath, files);
            if (paradox != null) results = results.Append(paradox).ToArray();

            return results.Where(r => r.Confidence > 0)
                          .OrderByDescending(r => r.Confidence)
                          .ToArray();
        }

        /// <summary>يرجّع أعلى محرك ثقة، أو Unknown لو ما انكشف شي واضح</summary>
        public static DetectionResult DetectBest(string gameFolderPath)
        {
            var all = DetectAll(gameFolderPath);
            return all.Length > 0
                ? all[0]
                : new DetectionResult { Engine = EngineType.Unknown, Confidence = 0, Reason = "لم يتم التعرف على محرك معروف" };
        }

        private static DetectionResult DetectUnity(string root, string[] files, string[] dirs)
        {
            // بصمة أكيدة: مجلد ...\<Game>_Data\Managed يحتوي UnityPlayer.dll
            bool hasUnityPlayer = files.Any(f => Path.GetFileName(f).Equals("UnityPlayer.dll", StringComparison.OrdinalIgnoreCase));
            bool hasDataFolder = dirs.Any(d => d.EndsWith("_Data", StringComparison.OrdinalIgnoreCase));
            bool hasManaged = dirs.Any(d => Path.GetFileName(d).Equals("Managed", StringComparison.OrdinalIgnoreCase));

            if (hasUnityPlayer && hasDataFolder)
                return Confident(EngineType.Unity, 95, "وجدت UnityPlayer.dll ومجلد _Data");
            if (hasDataFolder && hasManaged)
                return Confident(EngineType.Unity, 80, "وجدت مجلد _Data يحتوي Managed");
            return None(EngineType.Unity);
        }

        private static DetectionResult DetectUnreal(string root, string[] files, string[] dirs)
        {
            bool hasPak = files.Any(f => f.EndsWith(".pak", StringComparison.OrdinalIgnoreCase));
            bool hasEngineDir = dirs.Any(d => Path.GetFileName(d).Equals("Engine", StringComparison.OrdinalIgnoreCase));
            bool hasContentDir = dirs.Any(d => Path.GetFileName(d).Equals("Content", StringComparison.OrdinalIgnoreCase));

            if (hasPak && (hasEngineDir || hasContentDir))
                return Confident(EngineType.UnrealEngine, 90, "وجدت ملفات .pak مع مجلد Engine/Content");
            if (hasPak)
                return Confident(EngineType.UnrealEngine, 60, "وجدت ملفات .pak بدون تأكيد إضافي");
            return None(EngineType.UnrealEngine);
        }

        private static DetectionResult DetectGodot(string root, string[] files, string[] dirs)
        {
            bool hasPck = files.Any(f => f.EndsWith(".pck", StringComparison.OrdinalIgnoreCase));
            bool hasProjectGodot = files.Any(f => Path.GetFileName(f).Equals("project.godot", StringComparison.OrdinalIgnoreCase));

            if (hasProjectGodot)
                return Confident(EngineType.Godot, 95, "وجدت project.godot (مشروع غير مُصدّر أو مصدّر بدون تشفير)");
            if (hasPck)
                return Confident(EngineType.Godot, 75, "وجدت ملف .pck");
            return None(EngineType.Godot);
        }

        private static DetectionResult DetectGameMaker(string root, string[] files, string[] dirs)
        {
            bool hasDataWin = files.Any(f => Path.GetFileName(f).Equals("data.win", StringComparison.OrdinalIgnoreCase));
            if (hasDataWin)
                return Confident(EngineType.GameMaker, 95, "وجدت data.win");
            return None(EngineType.GameMaker);
        }

        private static DetectionResult DetectRpgMaker(string root, string[] files, string[] dirs)
        {
            // RPG Maker MV/MZ: مجلد www/data يحتوي ملفات JSON مثل Map001.json, Actors.json
            bool hasWwwData = dirs.Any(d => Path.GetFileName(d).Equals("data", StringComparison.OrdinalIgnoreCase)) &&
                               files.Any(f => Path.GetFileName(f).Equals("Actors.json", StringComparison.OrdinalIgnoreCase));
            // RPG Maker VX/VX Ace: أرشيفات .rgss*a
            bool hasRgss = files.Any(f => f.Contains(".rgss", StringComparison.OrdinalIgnoreCase));

            if (hasWwwData)
                return Confident(EngineType.RpgMaker, 90, "وجدت ملفات بيانات RPG Maker MV/MZ (Actors.json)");
            if (hasRgss)
                return Confident(EngineType.RpgMaker, 90, "وجدت أرشيف RGSS (RPG Maker VX/VX Ace)");
            return None(EngineType.RpgMaker);
        }

        private static DetectionResult DetectRenPy(string root, string[] files, string[] dirs)
        {
            bool hasRpa = files.Any(f => f.EndsWith(".rpa", StringComparison.OrdinalIgnoreCase));
            bool hasGameDir = dirs.Any(d => Path.GetFileName(d).Equals("game", StringComparison.OrdinalIgnoreCase));
            bool hasRenpyDir = dirs.Any(d => Path.GetFileName(d).Equals("renpy", StringComparison.OrdinalIgnoreCase));

            if ((hasRpa || hasRenpyDir) && hasGameDir)
                return Confident(EngineType.RenPy, 95, "وجدت مجلد game مع أرشيف .rpa أو مجلد renpy");
            if (hasGameDir)
                return Confident(EngineType.RenPy, 50, "وجدت مجلد game بدون تأكيد إضافي");
            return None(EngineType.RenPy);
        }

        private static readonly string[] LanguageFolderHints = { "language", "languages", "localiz", "strings", "text" };

        /// <summary>
        /// يفحص ملفات .xml بحثاً عن نمط &lt;string id="..." text="..." /&gt; -
        /// شائع بمحركات خاصة كثيرة (مثل محرك TaleWorlds في Mount &amp; Blade II: Bannerlord)
        /// كصيغة رسمية لملفات الترجمة، حتى لو المحرك نفسه غير معروف لنا.
        /// نعطي أولوية لملفات بأسماء/مسارات دالة (Language, Strings...) ونفحصها كاملة،
        /// وباقي ملفات XML نفحص أول 4KB منها فقط لتفادي إبطاء الفحص بألعاب فيها آلاف ملفات XML.
        /// </summary>
        private static DetectionResult DetectXmlLocalization(string root, string[] files, string[] dirs)
        {
            var xmlFiles = files.Where(f => f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)).ToList();

            var prioritized = xmlFiles.Where(f =>
                LanguageFolderHints.Any(hint => f.Contains(hint, StringComparison.OrdinalIgnoreCase)));
            var others = xmlFiles.Except(prioritized).Take(100);

            foreach (var file in prioritized.Concat(others))
            {
                if (FileContainsXmlStringPattern(file))
                    return Confident(EngineType.XmlLocalization, 70,
                        $"وجدت نمط ملفات ترجمة XML (id/text) بملف {Path.GetFileName(file)}");
            }

            return None(EngineType.XmlLocalization);
        }

        private static bool FileContainsXmlStringPattern(string file)
        {
            try
            {
                string content = File.ReadAllText(file);
                return content.Contains("<string ", StringComparison.OrdinalIgnoreCase) &&
                       content.Contains(" text=\"", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static DetectionResult Confident(EngineType e, int c, string reason) =>
            new DetectionResult { Engine = e, Confidence = c, Reason = reason };

        private static DetectionResult None(EngineType e) =>
            new DetectionResult { Engine = e, Confidence = 0, Reason = null };

        private static string[] SafeGetFiles(string root)
        {
            try { return Directory.GetFiles(root, "*", SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }

        private static string[] SafeGetDirs(string root)
        {
            try { return Directory.GetDirectories(root, "*", SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }
    }
}
