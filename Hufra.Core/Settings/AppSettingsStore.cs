using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Hufra.Core.Settings
{
    public class TranslationHistoryItem
    {
        public string GameName { get; set; }
        public string EngineName { get; set; }
        public int TranslatedCount { get; set; }
        public DateTime Date { get; set; }
    }
    /// <summary>
    /// تخزين بسيط لإعدادات التطبيق محلياً على جهاز المستخدم (مجلد AppData).
    ///
    /// ملاحظة أمان: المفتاح يُحفظ كنص عادي بملف JSON على القرص، بدون تشفير.
    /// هذا مقبول لأداة شخصية على جهاز المستخدم نفسه (نفس مستوى حماية أي ملف
    /// إعدادات محلي عادي)، لكنه ليس مناسباً لو الجهاز مشترك مع أشخاص لا تثق
    /// فيهم. لتحسينه لاحقاً: تشفير عبر Windows DPAPI (ProtectedData class).
    /// </summary>
    public static class AppSettingsStore
    {
        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Hufra", "settings.json");

        private class SettingsData
        {
            public string AnthropicApiKey { get; set; }
            public string GeminiApiKey { get; set; }
            public bool DiscordJoined { get; set; }
            public bool? ShapeArabic { get; set; }
            public string CustomLibraryPath { get; set; }
            public string Ck3ModPath { get; set; }
            public string DiscordBotTokenProtected { get; set; }
            public string DiscordGuildId { get; set; }
            public string MinecraftModsPath { get; set; }
            public string Ck3GamePath { get; set; }
            public string ProfileName { get; set; }
            public string ProfileAvatarPath { get; set; }
            public List<TranslationHistoryItem> History { get; set; } = new List<TranslationHistoryItem>();
        }

        private static SettingsData Load()
        {
            try
            {
                if (!File.Exists(SettingsPath)) return new SettingsData();
                var json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<SettingsData>(json) ?? new SettingsData();
            }
            catch
            {
                return new SettingsData();
            }
        }

        private static void Save(SettingsData data)
        {
            var dir = Path.GetDirectoryName(SettingsPath);
            Directory.CreateDirectory(dir!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data));
        }

        public static string GetAnthropicApiKey() => Load().AnthropicApiKey;

        public static void SetAnthropicApiKey(string key)
        {
            var data = Load();
            data.AnthropicApiKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
            Save(data);
        }

        /// <summary>توكن البوت مشفّر بـ DPAPI (يُقرأ فقط من نفس حساب ويندوز)</summary>
        public static string GetDiscordBotToken() => SecretStore.Unprotect(Load().DiscordBotTokenProtected);

        public static void SetDiscordBotToken(string token)
        {
            var data = Load();
            data.DiscordBotTokenProtected = string.IsNullOrWhiteSpace(token) ? null : SecretStore.Protect(token.Trim());
            Save(data);
        }

        public static string GetDiscordGuildId() => Load().DiscordGuildId;

        public static void SetDiscordGuildId(string id)
        {
            var data = Load();
            data.DiscordGuildId = string.IsNullOrWhiteSpace(id) ? null : id.Trim();
            Save(data);
        }

        public static string GetGeminiApiKey() => Load().GeminiApiKey;

        public static void SetGeminiApiKey(string key)
        {
            var data = Load();
            data.GeminiApiKey = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
            Save(data);
        }

        /// <summary>تشكيل الحروف + عكس الاتجاه للألعاب اللي ما تدعم العربية (افتراضياً مفعّل)</summary>
        public static bool GetShapeArabic() => Load().ShapeArabic ?? true;

        public static void SetShapeArabic(bool value)
        {
            var data = Load();
            data.ShapeArabic = value;
            Save(data);
        }

        public static bool GetDiscordJoined() => Load().DiscordJoined;

        public static void SetDiscordJoined(bool joined)
        {
            var data = Load();
            data.DiscordJoined = joined;
            Save(data);
        }

        public static string GetMinecraftModsPath() => Load().MinecraftModsPath;

        public static void SetMinecraftModsPath(string path)
        {
            var data = Load();
            data.MinecraftModsPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            Save(data);
        }

        public static string GetCk3GamePath() => Load().Ck3GamePath;

        public static void SetCk3GamePath(string path)
        {
            var data = Load();
            data.Ck3GamePath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            Save(data);
        }

        /// <summary>مسار مجلد مودات CK3 المخصص (null = الافتراضي في المستندات)</summary>
        public static string GetCk3ModPath() => Load().Ck3ModPath;

        public static void SetCk3ModPath(string path)
        {
            var data = Load();
            data.Ck3ModPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            Save(data);
        }

        /// <summary>مسار مجلد يحتوي عدة ألعاب جواه (مثل C:\Vault Games) - يُحفظ عشان ما يُعاد اختياره كل مرة</summary>
        public static string GetCustomLibraryPath() => Load().CustomLibraryPath;

        public static void SetCustomLibraryPath(string path)
        {
            var data = Load();
            data.CustomLibraryPath = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            Save(data);
        }

        /// <summary>الاسم وصورة الملف الشخصي (محلي بالكامل، بدون أي حساب أو سيرفر)</summary>
        public static (string Name, string AvatarPath) GetProfile()
        {
            var data = Load();
            return (data.ProfileName, data.ProfileAvatarPath);
        }

        public static void SetProfile(string name, string avatarPath)
        {
            var data = Load();
            data.ProfileName = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
            data.ProfileAvatarPath = string.IsNullOrWhiteSpace(avatarPath) ? null : avatarPath;
            Save(data);
        }

        /// <summary>سجل الألعاب اللي طُبّقت عليها ترجمة، الأحدث أولاً</summary>
        public static List<TranslationHistoryItem> GetHistory()
        {
            var history = Load().History ?? new List<TranslationHistoryItem>();
            history.Sort((a, b) => b.Date.CompareTo(a.Date));
            return history;
        }

        public static void AddHistoryEntry(string gameName, string engineName, int translatedCount)
        {
            var data = Load();
            data.History ??= new List<TranslationHistoryItem>();
            data.History.Add(new TranslationHistoryItem
            {
                GameName = gameName,
                EngineName = engineName,
                TranslatedCount = translatedCount,
                Date = DateTime.Now,
            });
            Save(data);
        }
    }
}
