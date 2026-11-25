namespace Hufra.Core.Engines
{
    public enum EngineType
    {
        Unknown,
        Unity,
        UnrealEngine,
        Godot,
        GameMaker,
        RpgMaker,
        RenPy,
        Paradox,
        Universal,
        Gettext,
        Properties,
        SourceEngine,
        TyranoKs,
        JsonLocalization,
        XmlLocalization, // محركات خاصة (custom) تستخدم ملفات XML بصيغة <string id="..." text="..." />
    }
}
