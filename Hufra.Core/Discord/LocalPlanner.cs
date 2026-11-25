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
    public static class LocalPlanner
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

        // ---------- القوالب ----------
        private static (string[] Roles, List<DiscordAction> Structure) Template(string kind)
        {
            string[] staffRoles = { "👑 مالك", "🛡️ إدارة", "⚔️ مشرف" };
            var staff = new List<string> { "👑 مالك", "🛡️ إدارة", "⚔️ مشرف" };
            var writers = new List<string> { "👑 مالك", "🛡️ إدارة" };

            var s = new List<DiscordAction>();
            string[] roles;

            void Info(string cat)
            {
                s.Add(Cat(cat));
                s.Add(Txt("👋・الترحيب", cat, true, writers));
                s.Add(Txt("📜・القوانين", cat, true, writers));
                s.Add(Txt("📢・الإعلانات", cat, true, writers));
            }
            void StaffArea()
            {
                s.Add(Cat("🔒 الإدارة", staff));
                s.Add(Txt("🔒・شات-الإدارة", "🔒 الإدارة", visibleTo: staff));
                s.Add(Txt("📋・سجل-القرارات", "🔒 الإدارة", visibleTo: staff));
                s.Add(Voice("🔊 اجتماع الإدارة", "🔒 الإدارة", staff));
            }

            switch (kind)
            {
                case "store":
                    roles = staffRoles.Concat(new[] { "💼 بائع", "🛒 عميل", "👤 عضو" }).ToArray();
                    Info("📌 معلومات");
                    s.Add(Cat("🛒 المتجر"));
                    s.Add(Txt("🛍️・المنتجات", "🛒 المتجر", true, writers));
                    s.Add(Txt("💳・الأسعار", "🛒 المتجر", true, writers));
                    s.Add(Txt("🎫・فتح-تذكرة", "🛒 المتجر"));
                    s.Add(Txt("⭐・التقييمات", "🛒 المتجر"));
                    s.Add(Cat("💬 المجتمع"));
                    s.Add(Txt("💬・الشات-العام", "💬 المجتمع"));
                    s.Add(Txt("📷・الصور", "💬 المجتمع"));
                    s.Add(Cat("🔊 الصوتيات"));
                    s.Add(Voice("🔊 عام", "🔊 الصوتيات"));
                    s.Add(Voice("🔊 الدعم", "🔊 الصوتيات"));
                    StaffArea();
                    break;
                case "gaming":
                    roles = staffRoles.Concat(new[] { "🎮 لاعب محترف", "🎮 لاعب", "👤 عضو" }).ToArray();
                    Info("📌 معلومات");
                    s.Add(Cat("🎮 الألعاب"));
                    s.Add(Txt("🎮・نقاش-الألعاب", "🎮 الألعاب"));
                    s.Add(Txt("🔎・دور-فريق", "🎮 الألعاب"));
                    s.Add(Txt("🏆・الإنجازات", "🎮 الألعاب"));
                    s.Add(Txt("🎬・مقاطع", "🎮 الألعاب"));
                    s.Add(Cat("💬 المجتمع"));
                    s.Add(Txt("💬・الشات-العام", "💬 المجتمع"));
                    s.Add(Txt("😂・ميمز", "💬 المجتمع"));
                    s.Add(Cat("🔊 الصوتيات"));
                    s.Add(Voice("🔊 غرفة 1", "🔊 الصوتيات"));
                    s.Add(Voice("🔊 غرفة 2", "🔊 الصوتيات"));
                    s.Add(Voice("🔊 غرفة 3", "🔊 الصوتيات"));
                    StaffArea();
                    break;
                case "education":
                    roles = staffRoles.Concat(new[] { "📚 مدرّس", "🎓 طالب", "👤 عضو" }).ToArray();
                    Info("📌 معلومات");
                    s.Add(Cat("📚 الدراسة"));
                    s.Add(Txt("📚・المواد", "📚 الدراسة", true, writers));
                    s.Add(Txt("❓・الأسئلة", "📚 الدراسة"));
                    s.Add(Txt("📝・الواجبات", "📚 الدراسة"));
                    s.Add(Txt("📎・الملفات", "📚 الدراسة"));
                    s.Add(Cat("💬 المجتمع"));
                    s.Add(Txt("💬・الشات-العام", "💬 المجتمع"));
                    s.Add(Cat("🔊 القاعات"));
                    s.Add(Voice("🔊 قاعة 1", "🔊 القاعات"));
                    s.Add(Voice("🔊 قاعة 2", "🔊 القاعات"));
                    s.Add(Voice("🔊 مذاكرة جماعية", "🔊 القاعات"));
                    StaffArea();
                    break;
                default: // community
                    roles = staffRoles.Concat(new[] { "⭐ عضو مميّز", "👤 عضو" }).ToArray();
                    Info("📌 معلومات");
                    s.Add(Cat("💬 المجتمع"));
                    s.Add(Txt("💬・الشات-العام", "💬 المجتمع"));
                    s.Add(Txt("📷・الصور", "💬 المجتمع"));
                    s.Add(Txt("😂・ميمز", "💬 المجتمع"));
                    s.Add(Txt("🤖・الأوامر", "💬 المجتمع"));
                    s.Add(Cat("🔊 الصوتيات"));
                    s.Add(Voice("🔊 عام", "🔊 الصوتيات"));
                    s.Add(Voice("🔊 غرفة 1", "🔊 الصوتيات"));
                    s.Add(Voice("🔊 غرفة 2", "🔊 الصوتيات"));
                    StaffArea();
                    break;
            }
            return (roles, s);
        }

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
            bool wantServer = Has(n, "سيرفر", "server") && Has(n, "سو", "جهز", "سوي", "انشئ", "اعمل", "ابي", "ابغى", "ابغا", "اريد", "create", "make");
            string template = Has(n, "متجر", "ستور", "بيع", "تجاره", "store", "shop") ? "store"
                : Has(n, "قيمنق", "قيمز", "العاب", "gaming", "game", "كلان") ? "gaming"
                : Has(n, "تعليم", "دراسه", "دورات", "مدرسه", "جامعه", "study", "education") ? "education"
                : wantServer || Has(n, "مجتمع", "community") ? "community" : null;

            bool hasList = Regex.IsMatch(text ?? "", @"[:：]\s*\S");
            bool wantRooms = Has(n, "رومات", "روم", "قنوات", "شاتات", "قنواتي", "channels", "تنظيم", "نظم", "رتب", "رول", "roles");
            // طلب عام بدون قائمة («سوي رومات»، «نظم السيرفر») → قالب مجتمع جاهز
            if (template == null && !hasList && wantRooms && !Has(n, "احذف", "امسح")) template = "community";

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
                string tname = template == "store" ? "متجر" : template == "gaming" ? "ألعاب" : template == "education" ? "تعليمي" : "مجتمع";
                plan.Message = "جهّزت لك سيرفر كامل بقالب «" + tname + "»: رتب مرتّبة، رومات منظّمة، قسم إدارة خاص، وغرف صوتية. راجع الخطة ثم نفّذ.";
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
                plan.Message = "ما فهمت الطلب بدون الذكاء الاصطناعي.\n\n" + Help;
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
