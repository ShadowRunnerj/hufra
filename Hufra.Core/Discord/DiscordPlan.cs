using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hufra.Core.Discord
{
    public class DiscordAction
    {
        [JsonPropertyName("type")] public string Type { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("color")] public string Color { get; set; }
        [JsonPropertyName("permissions")] public List<string> Permissions { get; set; }
        [JsonPropertyName("hoist")] public bool Hoist { get; set; }
        [JsonPropertyName("mentionable")] public bool Mentionable { get; set; }
        [JsonPropertyName("kind")] public string Kind { get; set; }
        [JsonPropertyName("category")] public string Category { get; set; }
        [JsonPropertyName("topic")] public string Topic { get; set; }
        [JsonPropertyName("visible_to")] public List<string> VisibleTo { get; set; }
        [JsonPropertyName("read_only")] public bool ReadOnly { get; set; }
        [JsonPropertyName("writable_by")] public List<string> WritableBy { get; set; }
        [JsonPropertyName("slowmode")] public int Slowmode { get; set; }
        [JsonPropertyName("target")] public string Target { get; set; }
        [JsonPropertyName("role")] public string Role { get; set; }
        [JsonPropertyName("allow")] public List<string> Allow { get; set; }
        [JsonPropertyName("deny")] public List<string> Deny { get; set; }
        [JsonPropertyName("order")] public List<string> Order { get; set; }

        public bool IsDestructive => Type == "delete_channel" || Type == "delete_role";

        public string Describe()
        {
            string perms = Permissions is { Count: > 0 } ? " — صلاحيات: " + string.Join("، ", Permissions) : "";
            return Type switch
            {
                "create_role" => $"➕ رتبة «{Name}» {Color}{perms}",
                "create_category" => $"📁 فئة «{Name}»" + (VisibleTo is { Count: > 0 } ? " (خاصة بـ " + string.Join("، ", VisibleTo) + ")" : ""),
                "create_channel" => $"{(Kind == "voice" ? "🔊" : Kind == "announcement" ? "📢" : Kind == "forum" ? "🗂" : "💬")} روم «{Name}»" +
                                    (string.IsNullOrWhiteSpace(Category) ? "" : $" داخل «{Category}»") +
                                    (VisibleTo is { Count: > 0 } ? " (خاص بـ " + string.Join("، ", VisibleTo) + ")" : "") +
                                    (ReadOnly ? " (قراءة فقط)" : ""),
                "set_permissions" => $"🔐 صلاحيات «{Role}» في «{Target}»: سماح [{string.Join("، ", Allow ?? new List<string>())}] منع [{string.Join("، ", Deny ?? new List<string>())}]",
                "reorder_roles" => "↕ ترتيب الرتب: " + string.Join(" > ", Order ?? new List<string>()),
                "delete_channel" => $"🗑 حذف الروم/الفئة «{Name}»",
                "delete_role" => $"🗑 حذف الرتبة «{Name}»",
                _ => $"❓ إجراء غير معروف: {Type}",
            };
        }
    }

    public class DiscordPlan
    {
        [JsonPropertyName("message")] public string Message { get; set; }
        [JsonPropertyName("actions")] public List<DiscordAction> Actions { get; set; } = new();

        public const int MaxActions = 60;

        public static DiscordPlan Parse(string json)
        {
            json = (json ?? "").Trim();
            if (json.StartsWith("```"))
            {
                int nl = json.IndexOf('\n');
                if (nl > 0) json = json.Substring(nl + 1);
                int end = json.LastIndexOf("```", StringComparison.Ordinal);
                if (end >= 0) json = json.Substring(0, end);
            }
            var plan = JsonSerializer.Deserialize<DiscordPlan>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? throw new InvalidOperationException("خطة فارغة.");
            plan.Actions ??= new List<DiscordAction>();
            if (plan.Actions.Count > MaxActions)
                throw new InvalidOperationException($"الخطة كبيرة جداً ({plan.Actions.Count} إجراء). قسّم طلبك لأجزاء (الحد {MaxActions}).");
            return plan;
        }

        public bool HasDestructive => Actions.Any(a => a.IsDestructive);

        /// <summary>تحذيرات تُعرض قبل التنفيذ (صلاحيات خطرة، Administrator...)</summary>
        public List<string> Warnings()
        {
            var w = new List<string>();
            foreach (var a in Actions)
            {
                var all = (a.Permissions ?? new List<string>()).Concat(a.Allow ?? new List<string>());
                if (all.Any(p => p.Replace("_", "").Equals("Administrator", StringComparison.OrdinalIgnoreCase)))
                    w.Add($"⛔ «{a.Name ?? a.Role}» فيها Administrator — سأرفض هذا الإجراء.");
                var danger = all.Where(p => DiscordPermissions.Dangerous.Contains(p.Replace("_", ""), StringComparer.OrdinalIgnoreCase)).Distinct().ToList();
                if (danger.Count > 0) w.Add($"⚠️ «{a.Name ?? a.Role}» تحصل على صلاحيات حساسة: {string.Join("، ", danger)}");
            }
            if (HasDestructive) w.Add("🗑 الخطة فيها حذف — لا يمكن التراجع عنه.");
            return w;
        }

        public string Preview()
        {
            var sb = new StringBuilder();
            int i = 1;
            foreach (var a in Actions) sb.AppendLine($"{i++}. {a.Describe()}");
            return sb.ToString().TrimEnd();
        }
    }
}
