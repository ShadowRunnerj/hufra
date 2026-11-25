using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hufra.Core.Discord
{
    /// <summary>
    /// مخطِّط محلي بدون ذكاء اصطناعي ولا مفاتيح: يفهم الطلبات الشائعة بالعربي (قوالب سيرفرات، قوائم رتب ورومات، حذف).
    /// أضعف من Gemini في الطلبات الحرة، لكنه يعمل فوراً بدون إعداد.
    /// </summary>
    public static partial class LocalPlanner
    {
        public const string Help =
            "أفهم بدون مفتاح طلبات مثل:\n" +
            "• سوّ سيرفر متجر / ألعاب / مجتمع / تعليمي (يجهّز رتب ورومات كاملة)\n" +
            "• سوّ رتب: مالك، إدارة، مشرف، عضو   (الأولى هي الأعلى)\n" +
            "• سوّ رومات: شات عام، صور، أخبار   |   سوّ صوت: غرفة 1، غرفة 2\n" +
            "• سوّ روم خاص بالإدارة: شات الإدارة\n" +
            "• احذف روم اسم   |   احذف رتبة اسم\n" +
            "للطلبات الحرة الأعقد أضف مفتاح Gemini من الإعدادات (اختياري).";

        private static readonly string[] Palette =
            { "#e74c3c", "#e67e22", "#f1c40f", "#2ecc71", "#3498db", "#9b59b6", "#e91e63", "#95a5a6" };

        private static string Norm(string s) =>
            Regex.Replace((s ?? "").Replace("أ", "ا").Replace("إ", "ا").Replace("آ", "ا").Replace("ة", "ه").Replace("ى", "ي"), "[ً-ْـ]", "").ToLowerInvariant();

        private static bool Has(string n, params string[] words) => words.Any(w => n.Contains(Norm(w)));

        private static List<string> SplitList(string s) =>
            Regex.Split(s ?? "", @"[،,\n؛;]|\s+و\s+")
                 .Select(x => x.Trim().Trim('"', '«', '»', '.', ':', '-', '•').Trim())
                 .Where(x => x.Length > 0 && x.Length <= 80).Distinct().ToList();

        // ---------- أدوات بناء الإجراءات ----------
        private static DiscordAction Role(string name, int idx, string[] perms = null, bool hoist = false) => new DiscordAction
        {
            Type = "create_role", Name = name, Color = Palette[idx % Palette.Length],
            Permissions = perms?.ToList() ?? new List<string>(), Hoist = hoist, Mentionable = hoist,
        };

        private static string[] PermsFor(string roleName)
        {
            string n = Norm(roleName);
            if (Has(n, "مالك", "owner", "ادمن", "admin", "اداره", "ادارة", "مدير"))
                return new[] { "ManageChannels", "ManageRoles", "ManageMessages", "KickMembers", "BanMembers", "ModerateMembers", "MentionEveryone", "ViewAuditLog" };
            if (Has(n, "مشرف", "mod", "ميود"))
                return new[] { "ManageMessages", "KickMembers", "ModerateMembers", "ViewAuditLog" };
            if (Has(n, "دعم", "support", "مساعد"))
                return new[] { "ManageMessages", "ModerateMembers" };
            return Array.Empty<string>();
        }

        private static bool IsStaff(string roleName) => PermsFor(roleName).Length > 0;

        private static DiscordAction Cat(string name, List<string> visibleTo = null) =>
            new DiscordAction { Type = "create_category", Name = name, VisibleTo = visibleTo };

        private static DiscordAction Txt(string name, string category, bool ro = false, List<string> writable = null, List<string> visibleTo = null, string topic = null) => new DiscordAction
        {
            Type = "create_channel", Kind = "text", Name = name, Category = category, ReadOnly = ro,
            WritableBy = writable, VisibleTo = visibleTo, Topic = topic,
        };

        private static DiscordAction Voice(string name, string category, List<string> visibleTo = null) =>
            new DiscordAction { Type = "create_channel", Kind = "voice", Name = name, Category = category, VisibleTo = visibleTo };

        // ---------- الخطة ----------
        public static DiscordPlan Plan(string text, IEnumerable<string> existingRoles, IEnumerable<string> existingChannels)
        {
            string n = Norm(text);
            var haveRoles = new HashSet<string>(existingRoles ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var haveCh = new HashSet<string>((existingChannels ?? Array.Empty<string>()).Select(x => x.ToLowerInvariant().Replace(' ', '-')));
            var plan = new DiscordPlan();

            // 1) حذف
            var del = Regex.Match(text ?? "", @"(?:احذف|امسح|شيل|ازل|delete)\s+(?:ال)?(روم|قناه|قناة|شات|فئه|فئة|كاتيجوري|رتبه|رتبة|رول)\s+(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (del.Success)
            {
                string kind = Norm(del.Groups[1].Value), name = del.Groups[2].Value.Trim().Trim('"', '«', '»', '.', ':');
                bool isRole = Has(kind, "رتبه", "رول");
                plan.Actions.Add(new DiscordAction { Type = isRole ? "delete_role" : "delete_channel", Name = name });
                plan.Message = $"فهمت إنك تبي تحذف «{name}». راجع الخطة ووافق إذا متأكد (الحذف نهائي).";
                return plan;
            }

            // 2) قالب سيرفر كامل
            bool intent = HasIntent(n);
            bool saysServer = Has(n, "سيرفر", "server");
            bool wantServer = saysServer && intent;
            var detected = (intent || saysServer) ? DetectTemplate(n) : null;
            string template = detected != null ? detected.Key
                : wantServer || (intent && Has(n, "مجتمع", "community")) ? "community" : null;

            bool hasList = Regex.IsMatch(text ?? "", @"[:：]\s*\S");
            // محادثة عادية (تحية / مساعدة / قوالب / شكر) بدون نية تنفيذ
            if (!intent && !saysServer && !hasList)
            {
                var c = Chat(n);
                if (c != null) { plan.Message = c; return plan; }
            }
            bool wantRooms = Has(n, "رومات", "روم", "قنوات", "شاتات", "قنواتي", "channels", "تنظيم", "نظم", "رتب", "رول", "roles");
            // طلب عام بدون قائمة («سوي رومات»، «نظم السيرفر») → قالب مجتمع جاهز
            if (template == null && !hasList && wantRooms && intent && !Has(n, "احذف", "امسح")) template = "community";

            if (template != null && !Regex.IsMatch(n, @"(رتب|رول|روم|قنوات|قناه|شات)\w*\s*[:：]"))
            {
                var (roles, structure) = Template(template);
                int idx = 0;
                foreach (var r in roles)
                {
                    if (!haveRoles.Contains(r))
                        plan.Actions.Add(Role(r, idx, PermsFor(r), IsStaff(r)));
                    idx++;
                }
                foreach (var a in structure)
                {
                    string key = (a.Name ?? "").ToLowerInvariant().Replace(' ', '-');
                    if (!haveCh.Contains(key)) plan.Actions.Add(a);
                }
                plan.Actions.Add(new DiscordAction { Type = "reorder_roles", Order = roles.ToList() });
                var tpl = Catalog.First(x => x.Key == template);
                int nr = plan.Actions.Count(x => x.Type == "create_role"), nc = plan.Actions.Count(x => x.Type == "create_channel"), ng = plan.Actions.Count(x => x.Type == "create_category");
                plan.Message = $"تمام! جهّزت لك {tpl.Pitch}.\nالخطة: {nr} رتبة، {ng} أقسام، {nc} روم (مع قسم إدارة خاص). راجعها ثم اضغط تنفيذ.";
                return plan;
            }

            // 3) قوائم: رتب / رومات / صوت
            var segs = new List<(string Kind, List<string> Items, string Raw)>();
            foreach (Match m in Regex.Matches(text ?? "",
                @"(رتب\w*|رول\w*|roles?|رومات|روم|قنوات|قناه|قناة|شاتات|شات|صوتيات|صوتي|صوت|فويس|voice|text)\s*(?:خاص\w*\s*(?:ب\w*)?\s*[^:：\n]*)?[:：]\s*(.+?)(?=\s+(?:رتب\w*|رول\w*|رومات|روم|قنوات|شاتات|صوتيات|صوت|فويس)\s*[:：]|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string head = Norm(m.Value.Substring(0, Math.Max(0, m.Value.IndexOfAny(new[] { ':', '：' }))));
                string kind = Has(head, "رتب", "رول", "role") ? "role" : Has(head, "صوت", "فويس", "voice") ? "voice" : "text";
                segs.Add((kind, SplitList(m.Groups[2].Value), head));
            }

            if (segs.Count == 0)
            {
                plan.Message = Chat(n) ?? ("ما فهمت الطلب بالضبط.\n" + Help + "\n\nجرّب كتابة «القوالب» عشان أعرض لك أنواع السيرفرات الجاهزة.");
                return plan;
            }

            var newRoleNames = new List<string>();
            foreach (var seg in segs.Where(x => x.Kind == "role"))
            {
                int i = 0;
                foreach (var name in seg.Items)
                {
                    if (!haveRoles.Contains(name)) plan.Actions.Add(Role(name, i, PermsFor(name), IsStaff(name)));
                    newRoleNames.Add(name);
                    i++;
                }
            }

            foreach (var seg in segs.Where(x => x.Kind != "role"))
            {
                // «خاص بـ الإدارة» في مقدمة القسم
                List<string> visible = null;
                var priv = Regex.Match(seg.Raw, @"خاص\w*\s*(?:ب\w*)?\s*(.+)$");
                if (priv.Success) visible = SplitList(priv.Groups[1].Value);
                bool ro = Has(seg.Raw, "قراءه فقط", "للقراءه");
                foreach (var name in seg.Items)
                {
                    string key = name.ToLowerInvariant().Replace(' ', '-');
                    if (haveCh.Contains(key)) continue;
                    plan.Actions.Add(seg.Kind == "voice" ? Voice(name, null, visible) : Txt(name, null, ro, null, visible));
                }
            }

            if (newRoleNames.Count > 1)
                plan.Actions.Add(new DiscordAction { Type = "reorder_roles", Order = newRoleNames });

            plan.Message = plan.Actions.Count == 0
                ? "كل اللي طلبته موجود مسبقاً في السيرفر."
                : "فهمت طلبك. هذي الخطة — راجعها ثم نفّذ.";
            return plan;
        }
    }
}
