using System;
using System.Collections.Generic;
using System.Linq;

namespace Hufra.Core.Discord
{
    /// <summary>أسماء صلاحيات ديسكورد وقيمها (bit flags حسب توثيق Discord API).</summary>
    public static class DiscordPermissions
    {
        public static readonly Dictionary<string, ulong> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["CreateInstantInvite"] = 1UL << 0, ["KickMembers"] = 1UL << 1, ["BanMembers"] = 1UL << 2,
            ["Administrator"] = 1UL << 3, ["ManageChannels"] = 1UL << 4, ["ManageGuild"] = 1UL << 5,
            ["AddReactions"] = 1UL << 6, ["ViewAuditLog"] = 1UL << 7, ["PrioritySpeaker"] = 1UL << 8,
            ["Stream"] = 1UL << 9, ["ViewChannel"] = 1UL << 10, ["SendMessages"] = 1UL << 11,
            ["SendTTSMessages"] = 1UL << 12, ["ManageMessages"] = 1UL << 13, ["EmbedLinks"] = 1UL << 14,
            ["AttachFiles"] = 1UL << 15, ["ReadMessageHistory"] = 1UL << 16, ["MentionEveryone"] = 1UL << 17,
            ["UseExternalEmojis"] = 1UL << 18, ["Connect"] = 1UL << 20, ["Speak"] = 1UL << 21,
            ["MuteMembers"] = 1UL << 22, ["DeafenMembers"] = 1UL << 23, ["MoveMembers"] = 1UL << 24,
            ["UseVAD"] = 1UL << 25, ["ChangeNickname"] = 1UL << 26, ["ManageNicknames"] = 1UL << 27,
            ["ManageRoles"] = 1UL << 28, ["ManageWebhooks"] = 1UL << 29, ["ManageGuildExpressions"] = 1UL << 30,
            ["UseApplicationCommands"] = 1UL << 31, ["RequestToSpeak"] = 1UL << 32, ["ManageEvents"] = 1UL << 33,
            ["ManageThreads"] = 1UL << 34, ["CreatePublicThreads"] = 1UL << 35, ["CreatePrivateThreads"] = 1UL << 36,
            ["UseExternalStickers"] = 1UL << 37, ["SendMessagesInThreads"] = 1UL << 38, ["ModerateMembers"] = 1UL << 40,
        };

        public static readonly ulong View = Map["ViewChannel"];
        public static readonly ulong Send = Map["SendMessages"];

        /// <summary>صلاحيات خطرة نُنبّه المستخدم عليها في المعاينة</summary>
        public static readonly string[] Dangerous =
            { "KickMembers", "BanMembers", "ManageGuild", "ManageRoles", "ManageChannels", "MentionEveryone", "ManageWebhooks", "ModerateMembers" };

        /// <summary>يحوّل أسماء الصلاحيات إلى رقم. Administrator مرفوضة عمداً. الأسماء المجهولة تُرجع في unknown.</summary>
        public static ulong Parse(IEnumerable<string> names, out List<string> unknown)
        {
            unknown = new List<string>();
            ulong v = 0;
            if (names == null) return 0;
            foreach (var n in names)
            {
                string key = (n ?? "").Trim().Replace("_", "").Replace(" ", "");
                if (key.Equals("Administrator", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("صلاحية Administrator ممنوعة في الخطط التلقائية لأمانك. أعطها يدوياً من ديسكورد إن لزم.");
                if (Map.TryGetValue(key, out var bit)) v |= bit; else unknown.Add(n);
            }
            return v;
        }

        public static IEnumerable<string> Names(ulong value) => Map.Where(kv => (value & kv.Value) != 0).Select(kv => kv.Key);
    }
}
