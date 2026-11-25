using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// يكتشف الملفات المحزومة/المشفّرة اللي تخزّن فيها المحركات نصوصها. حُفرة ما يقدر يعدّلها حالياً،
    /// فنقول للمستخدم بصراحة بدل ما نوهمه إن التعريب سيظهر.
    /// </summary>
    public static class PackedAssetScanner
    {
        public static List<string> Find(string gameFolder, EngineType engine)
        {
            var found = new List<string>();
            string[] files;
            try { files = Directory.GetFiles(gameFolder, "*", SearchOption.AllDirectories); }
            catch { return found; }

            bool Any(Func<string, bool> p) => files.Any(p);
            string Name(string f) => Path.GetFileName(f).ToLowerInvariant();

            switch (engine)
            {
                case EngineType.Unity:
                    if (Any(f => Name(f).StartsWith("resources.assets") || Name(f) == "globalgamemanagers" || Name(f).StartsWith("sharedassets")
                              || Name(f).EndsWith(".bundle") || Name(f) == "data.unity3d"))
                        found.Add("نصوص Unity الأساسية داخل ملفات assets/bundles الثنائية");
                    break;
                case EngineType.UnrealEngine:
                    if (Any(f => Name(f).EndsWith(".pak") || Name(f).EndsWith(".utoc") || Name(f).EndsWith(".ucas")))
                        found.Add("نصوص Unreal داخل أرشيفات .pak/.utoc (وغالباً بصيغة .locres الثنائية)");
                    break;
                case EngineType.Godot:
                    if (Any(f => Name(f).EndsWith(".pck")))
                        found.Add("موارد Godot داخل أرشيف .pck");
                    break;
                case EngineType.GameMaker:
                    if (Any(f => Name(f) == "data.win" || Name(f) == "game.unx" || Name(f) == "game.ios"))
                        found.Add("نصوص GameMaker داخل data.win");
                    break;
            }
            if (Any(f => Name(f).EndsWith(".xp3") || Name(f).EndsWith(".rpa") || Name(f).EndsWith(".rgss3a")))
                found.Add("أرشيف مغلق (.xp3/.rpa/.rgss3a)");
            return found;
        }
    }
}
