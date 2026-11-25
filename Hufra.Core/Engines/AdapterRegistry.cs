using System.Collections.Generic;

namespace Hufra.Core.Engines
{
    /// <summary>
    /// نقطة الدخول الوحيدة لباقي البرنامج للوصول لأي Adapter.
    /// إضافة محرك جديد مستقبلاً = سطر واحد هنا + كلاس ينفّذ IEngineAdapter.
    /// </summary>
    public static class AdapterRegistry
    {
        private static readonly Dictionary<EngineType, IEngineAdapter> _adapters =
            new Dictionary<EngineType, IEngineAdapter>
            {
                [EngineType.Unity] = new UnityEngineAdapter(),
                [EngineType.UnrealEngine] = new UnrealEngineAdapter(),
                [EngineType.Godot] = new GodotEngineAdapter(),
                [EngineType.GameMaker] = new GameMakerEngineAdapter(),
                [EngineType.RpgMaker] = new RpgMakerEngineAdapter(),
                [EngineType.RenPy] = new RenPyEngineAdapter(),
                [EngineType.XmlLocalization] = new XmlLocalizationAdapter(),
                [EngineType.Universal] = new UniversalTextAdapter(),
                [EngineType.Paradox] = new ParadoxLocalizationAdapter(),
                [EngineType.Gettext] = new GettextPoAdapter(),
                [EngineType.Properties] = new PropertiesAdapter(),
                [EngineType.SourceEngine] = new SourceEngineAdapter(),
                [EngineType.TyranoKs] = new TyranoKsAdapter(),
                [EngineType.JsonLocalization] = new JsonLocalizationAdapter(),
            };

        public static IEngineAdapter Get(EngineType engine) =>
            _adapters.TryGetValue(engine, out var adapter) ? adapter : null;

        /// <summary>يفحص مجلد لعبة ويرجّع الـ Adapter الأنسب مباشرة (أو null لو ما انعرف المحرك)</summary>
        public static IEngineAdapter DetectAdapter(string gameFolderPath)
        {
            var best = EngineDetector.DetectBest(gameFolderPath);
            return best.Engine == EngineType.Unknown ? null : Get(best.Engine);
        }

        public static IEnumerable<IEngineAdapter> All => _adapters.Values;
    }
}
