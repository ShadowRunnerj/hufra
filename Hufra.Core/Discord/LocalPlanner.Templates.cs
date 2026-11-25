using System;
using System.Collections.Generic;
using System.Linq;

namespace Hufra.Core.Discord
{
    public static partial class LocalPlanner
    {
        /// <summary>قالب سيرفر: الاسم، الكلمات المفتاحية، رتب إضافية، وأقسام (r=قراءة فقط، t=نصي، v=صوتي)</summary>
        private sealed class Tpl
        {
            public string Key, Ar, Pitch;
            public string[] Keys, ExtraRoles;
            public (string Cat, string[] Chans)[] Cats;
        }

        private static readonly string[] StaffRoles = { "👑 مالك", "🛡️ إدارة", "⚔️ مشرف" };

        private static readonly Tpl[] Catalog =
        {
            new Tpl { Key="minecraft", Ar="ماينكرافت", Pitch="سيرفر ماينكرافت: أخبار، آي بي، تصويتات، وغرف صوتية",
                Keys=new[]{"ماين","ماينكرافت","minecraft"}, ExtraRoles=new[]{"💎 VIP","⛏️ لاعب","👤 عضو"},
                Cats=new[]{ ("🧱 السيرفر",new[]{"r:📢・أخبار-السيرفر","r:🌐・آي-بي-السيرفر","t:🗳️・التصويت","t:🆘・الدعم"}),
                            ("⛏️ اللعب",new[]{"t:💬・شات-اللعب","t:🏠・بناءات-اللاعبين","t:📷・لقطات","t:🤝・تجارة"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 عام","v:🔊 مجموعة 1","v:🔊 مجموعة 2"}) } },
            new Tpl { Key="roleplay", Ar="رول بلاي", Pitch="سيرفر رول بلاي: قوانين، وظائف، طلبات دخول، وإذاعة",
                Keys=new[]{"رول بلاي","رولبلاي","roleplay","rp","قراند","gta"}, ExtraRoles=new[]{"👮 شرطة","🚑 إسعاف","🧑‍💼 مواطن","🆕 مقدّم طلب"},
                Cats=new[]{ ("📝 التقديم",new[]{"t:📝・طلب-دخول","r:📋・نتائج-القبول","r:📜・قوانين-الرول"}),
                            ("🎭 داخل المدينة",new[]{"t:💬・شات-المدينة","t:📻・الإذاعة","t:🚗・المركبات","t:💼・الوظائف"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 انتظار","v:🔊 اللاسلكي","v:🔊 المدينة 1","v:🔊 المدينة 2"}) } },
            new Tpl { Key="clan", Ar="كلان / فريق", Pitch="سيرفر كلان: تشكيلة، تدريب، ومباريات",
                Keys=new[]{"كلان","تيم","clan","esports","اسبورت","فريقي","فرقه"}, ExtraRoles=new[]{"🏆 قائد","🎯 لاعب أساسي","🎮 احتياط","👤 ضيف"},
                Cats=new[]{ ("⚔️ الكلان",new[]{"r:📋・التشكيلة","t:📅・جدول-التدريب","t:🏆・المباريات","t:📊・الإحصائيات"}),
                            ("💬 المجتمع",new[]{"t:💬・شات-الكلان","t:🎬・مقاطع","t:🔎・طلبات-الانضمام"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 التدريب","v:🔊 المباراة","v:🔊 استراحة"}) } },
            new Tpl { Key="streamer", Ar="ستريمر / صانع محتوى", Pitch="سيرفر مجتمع ستريمر: إشعارات البث ومقاطع المتابعين",
                Keys=new[]{"ستريمر","بث","يوتيوب","تيك توك","تيكتوك","صانع محتوى","content","stream","twitch"}, ExtraRoles=new[]{"💜 مشترك","⭐ متابع مميّز","👤 متابع"},
                Cats=new[]{ ("📺 المحتوى",new[]{"r:🔴・إشعارات-البث","r:🎬・مقاطع-جديدة","t:💡・اقتراحات-المحتوى"}),
                            ("💬 المتابعين",new[]{"t:💬・شات-المتابعين","t:🎨・فن-المتابعين","t:😂・ميمز","t:🎁・مسابقات"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 غرفة البث","v:🔊 سوالف","v:🔊 ألعاب مع المتابعين"}) } },
            new Tpl { Key="anime", Ar="أنمي", Pitch="سيرفر أنمي: ترشيحات ونقاش حلقات وصور",
                Keys=new[]{"انمي","مانجا","اوتاكو","anime","manga"}, ExtraRoles=new[]{"🎌 أوتاكو","📚 قارئ مانجا","👤 عضو"},
                Cats=new[]{ ("🎌 الأنمي",new[]{"t:🔥・ترشيحات","t:📺・نقاش-الحلقات","t:⚠️・سبويلر","t:📚・مانجا"}),
                            ("🖼️ المجتمع",new[]{"t:💬・الشات-العام","t:🎨・فن-وصور","t:😂・ميمز"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 مشاهدة جماعية","v:🔊 سوالف"}) } },
            new Tpl { Key="dev", Ar="مبرمجين", Pitch="سيرفر مبرمجين: أسئلة، مشاريع، وموارد",
                Keys=new[]{"مبرمج","برمجه","كود","تقنيه","dev","code","programming","developer"}, ExtraRoles=new[]{"💻 مطوّر","🎓 مبتدئ","👤 عضو"},
                Cats=new[]{ ("💻 البرمجة",new[]{"t:❓・أسئلة-برمجية","t:🧩・مشاريع","t:📚・مصادر-وشروحات","t:🐞・مساعدة-أخطاء"}),
                            ("🌐 اللغات",new[]{"t:🐍・بايثون","t:🟨・جافاسكربت","t:🔷・سي-شارب","t:☕・جافا"}),
                            ("💬 المجتمع",new[]{"t:💬・الشات-العام","t:💼・وظائف"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 برمجة جماعية","v:🔊 مراجعة كود"}) } },
            new Tpl { Key="sports", Ar="رياضة", Pitch="سيرفر رياضي: نقاش مباريات وتوقعات",
                Keys=new[]{"رياضه","كره","دوري","كوره","sports","football","مباريات"}, ExtraRoles=new[]{"⚽ مشجّع","🏅 محلّل","👤 عضو"},
                Cats=new[]{ ("⚽ الرياضة",new[]{"r:📅・جدول-المباريات","t:🔥・نقاش-المباريات","t:🎯・التوقعات","t:📰・أخبار-الأندية"}),
                            ("💬 المجتمع",new[]{"t:💬・الشات-العام","t:😂・ميمز","t:📷・صور"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 مشاهدة المباراة","v:🔊 سوالف"}) } },
            new Tpl { Key="music", Ar="موسيقى", Pitch="سيرفر موسيقى: أوامر البوت وتوصيات وغرف استماع",
                Keys=new[]{"موسيقى","اغاني","music","اغنيه"}, ExtraRoles=new[]{"🎧 DJ","🎵 مستمع"},
                Cats=new[]{ ("🎵 الموسيقى",new[]{"t:🤖・أوامر-البوت","t:💡・توصيات","t:📝・طلبات-الأغاني"}),
                            ("💬 المجتمع",new[]{"t:💬・الشات-العام","t:🎤・مواهب"}),
                            ("🔊 الغرف",new[]{"v:🎶 غرفة موسيقى 1","v:🎶 غرفة موسيقى 2","v:🔊 استراحة"}) } },
            new Tpl { Key="news", Ar="أخبار", Pitch="سيرفر أخبار: قنوات للأخبار حسب المجال ونقاش",
                Keys=new[]{"اخبار","news","خبر"}, ExtraRoles=new[]{"📰 محرّر","👤 عضو"},
                Cats=new[]{ ("📰 الأخبار",new[]{"r:🌍・أخبار-عامة","r:💻・أخبار-التقنية","r:⚽・أخبار-الرياضة","r:💰・أخبار-الاقتصاد"}),
                            ("💬 النقاش",new[]{"t:💬・نقاش-عام","t:📢・اقتراح-مصدر"}) } },
            new Tpl { Key="trading", Ar="تداول", Pitch="سيرفر تداول: تحليلات ونقاش (للتعليم وليس نصيحة مالية)",
                Keys=new[]{"تداول","اسهم","كريبتو","trading","crypto","فوركس"}, ExtraRoles=new[]{"📈 محلّل","🎓 متداول","👤 عضو"},
                Cats=new[]{ ("📈 الأسواق",new[]{"r:📰・أخبار-السوق","t:📊・تحليلات","t:💬・نقاش-التداول","t:🎓・تعليم-للمبتدئين"}),
                            ("💬 المجتمع",new[]{"t:💬・الشات-العام","t:❓・أسئلة"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 غرفة التداول","v:🔊 سوالف"}) } },
            new Tpl { Key="events", Ar="فعاليات ومسابقات", Pitch="سيرفر فعاليات: جدول، مشاركات، وجوائز",
                Keys=new[]{"مسابقات","فعاليات","قيف اوي","جوائز","giveaway","event","مسابقه"}, ExtraRoles=new[]{"🎁 فائز","🎉 مشارك","👤 عضو"},
                Cats=new[]{ ("🎉 الفعاليات",new[]{"r:📅・جدول-الفعاليات","r:🎁・الجوائز","t:📝・التسجيل","r:🏆・الفائزين"}),
                            ("💬 المجتمع",new[]{"t:💬・الشات-العام","t:🤝・اقتراحات"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 الفعاليات","v:🔊 عام"}) } },
            new Tpl { Key="support", Ar="دعم فني", Pitch="سيرفر دعم: تذاكر، قاعدة معرفة، وأسئلة شائعة",
                Keys=new[]{"دعم","تذاكر","تذكره","ticket","support","helpdesk"}, ExtraRoles=new[]{"🎧 فريق الدعم","🙋 عميل"},
                Cats=new[]{ ("🎫 الدعم",new[]{"t:🎫・فتح-تذكرة","r:❓・الأسئلة-الشائعة","r:📚・الشروحات","r:📢・حالة-الخدمة"}),
                            ("💬 المجتمع",new[]{"t:💬・الشات-العام","t:💡・اقتراحات","t:🐞・بلاغات-الأخطاء"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 دعم صوتي"}) } },
            new Tpl { Key="friends", Ar="شلة / عائلة", Pitch="سيرفر خاص بالأصدقاء والعائلة: سوالف وصور وألعاب",
                Keys=new[]{"اصدقاء","شله","عائله","اهل","friends","family","ربع","خوياي"}, ExtraRoles=new[]{"⭐ الشلة","👤 ضيف"},
                Cats=new[]{ ("💬 السوالف",new[]{"t:💬・سوالف","t:📷・صور","t:😂・ميمز","t:📅・طلعات"}),
                            ("🎮 الألعاب",new[]{"t:🎮・نلعب-وش","t:🏆・نتائج"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 المجلس","v:🔊 ألعاب","v:🔊 سوالف خاصة"}) } },
            new Tpl { Key="store", Ar="متجر", Pitch="سيرفر متجر: منتجات، أسعار، تذاكر شراء، وتقييمات",
                Keys=new[]{"متجر","ستور","بيع","تجاره","store","shop","مبيعات"}, ExtraRoles=new[]{"💼 بائع","🛒 عميل","👤 عضو"},
                Cats=new[]{ ("🛒 المتجر",new[]{"r:🛍️・المنتجات","r:💳・الأسعار","t:🎫・فتح-تذكرة","t:⭐・التقييمات"}),
                            ("💬 المجتمع",new[]{"t:💬・الشات-العام","t:📷・الصور"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 عام","v:🔊 الدعم"}) } },
            new Tpl { Key="education", Ar="تعليمي", Pitch="سيرفر تعليمي: مواد، أسئلة، واجبات، وقاعات",
                Keys=new[]{"تعليم","دراسه","دورات","مدرسه","جامعه","study","education","طلاب","مذاكره"}, ExtraRoles=new[]{"📚 مدرّس","🎓 طالب","👤 عضو"},
                Cats=new[]{ ("📚 الدراسة",new[]{"r:📚・المواد","t:❓・الأسئلة","t:📝・الواجبات","t:📎・الملفات"}),
                            ("💬 المجتمع",new[]{"t:💬・الشات-العام"}),
                            ("🔊 القاعات",new[]{"v:🔊 قاعة 1","v:🔊 قاعة 2","v:🔊 مذاكرة جماعية"}) } },
            new Tpl { Key="gaming", Ar="ألعاب", Pitch="سيرفر ألعاب: نقاش، دور فريق، ومقاطع",
                Keys=new[]{"قيمنق","قيمز","العاب","gaming","game","لعبه","ببجي","فورتنايت","فالورانت"}, ExtraRoles=new[]{"🎮 لاعب محترف","🎮 لاعب","👤 عضو"},
                Cats=new[]{ ("🎮 الألعاب",new[]{"t:🎮・نقاش-الألعاب","t:🔎・دور-فريق","t:🏆・الإنجازات","t:🎬・مقاطع"}),
                            ("💬 المجتمع",new[]{"t:💬・الشات-العام","t:😂・ميمز"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 غرفة 1","v:🔊 غرفة 2","v:🔊 غرفة 3"}) } },
            new Tpl { Key="community", Ar="مجتمع", Pitch="سيرفر مجتمع عام: شات وصور وميمز وغرف صوتية",
                Keys=new[]{"مجتمع","community","عام","دردشه"}, ExtraRoles=new[]{"⭐ عضو مميّز","👤 عضو"},
                Cats=new[]{ ("💬 المجتمع",new[]{"t:💬・الشات-العام","t:📷・الصور","t:😂・ميمز","t:🤖・الأوامر"}),
                            ("🔊 الصوتيات",new[]{"v:🔊 عام","v:🔊 غرفة 1","v:🔊 غرفة 2"}) } },
        };

        private static string[] Words(string normalized) =>
            System.Text.RegularExpressions.Regex.Split(normalized, @"[^\p{L}\p{N}]+").Where(w => w.Length > 0).ToArray();

        /// <summary>تطابق كلمة كاملة (مع تجاهل بادئات و/ب/ل/ف و«ال») بدل البحث الجزئي الخطر</summary>
        private static bool WordHit(string normalized, string[] words, string key)
        {
            key = Norm(key);
            if (key.Contains(' ')) return normalized.Contains(key);
            foreach (var w0 in words)
            {
                var variants = new List<string> { w0 };
                if (w0.Length > 3 && "وبلف".IndexOf(w0[0]) >= 0) variants.Add(w0.Substring(1));
                foreach (var v in variants.ToArray())
                {
                    if (v.StartsWith("لل") && v.Length > 3) variants.Add(v.Substring(2));
                    if (v.StartsWith("ال") && v.Length > 3) variants.Add(v.Substring(2));
                }
                foreach (var v in variants)
                    if (v == key || (key.Length >= 4 && v.StartsWith(key))) return true;
            }
            return false;
        }

        private static Tpl DetectTemplate(string normalized)
        {
            var words = Words(normalized);
            foreach (var t in Catalog.Where(x => x.Key != "community"))
                if (t.Keys.Any(k => WordHit(normalized, words, k))) return t;
            return null;
        }

        private static bool HasIntent(string normalized)
        {
            var words = Words(normalized);
            return new[] { "سو", "سوي", "جهز", "جهزلي", "انشئ", "اعمل", "ابي", "ابغي", "ابغا", "ابغى", "اريد", "ركب", "نظم", "قالب", "create", "make" }
                .Any(k => WordHit(normalized, words, k));
        }

        private static (string[] Roles, List<DiscordAction> Structure) Template(string key)
        {
            var t = Catalog.FirstOrDefault(x => x.Key == key) ?? Catalog.Last();
            var staff = StaffRoles.ToList();
            var writers = new List<string> { StaffRoles[0], StaffRoles[1] };
            var roles = StaffRoles.Concat(t.ExtraRoles).ToArray();
            var s = new List<DiscordAction>();

            s.Add(Cat("📌 معلومات"));
            s.Add(Txt("👋・الترحيب", "📌 معلومات", true, writers));
            s.Add(Txt("📜・القوانين", "📌 معلومات", true, writers));
            s.Add(Txt("📢・الإعلانات", "📌 معلومات", true, writers));

            foreach (var (cat, chans) in t.Cats)
            {
                s.Add(Cat(cat));
                foreach (var c in chans)
                {
                    char kind = c[0]; string name = c.Substring(2);
                    if (kind == 'v') s.Add(Voice(name, cat));
                    else s.Add(Txt(name, cat, kind == 'r', kind == 'r' ? writers : null));
                }
            }

            s.Add(Cat("🔒 الإدارة", staff));
            s.Add(Txt("🔒・شات-الإدارة", "🔒 الإدارة", visibleTo: staff));
            s.Add(Txt("📋・سجل-القرارات", "🔒 الإدارة", visibleTo: staff));
            s.Add(Voice("🔊 اجتماع الإدارة", "🔒 الإدارة", staff));
            return (roles, s);
        }

        /// <summary>رد حواري بدون خطة (تحية، مساعدة، قائمة القوالب، شكر)</summary>
        private static string Chat(string n)
        {
            string list = string.Join("، ", Catalog.Select(c => c.Ar));
            if (Has(n, "القوالب", "قوالب", "وش عندك", "ايش عندك", "انواع السيرفرات", "اشكال", "اقتراحات", "اقترح"))
                return "عندي قوالب سيرفرات جاهزة: " + list + ".\nاكتب مثلاً: «سوّ لي سيرفر ماينكرافت» أو «ابي سيرفر دعم فني»، وأجهّز لك الرتب والرومات كاملة.";
            if (Has(n, "شكرا", "يعطيك العافيه", "مشكور", "thanks", "thx"))
                return "العفو! إذا تبي شي ثاني اكتب لي.";
            if (Has(n, "وش تقدر", "ايش تقدر", "وش تسوي", "كيف استخدم", "كيف اشتغل", "ساعدني", "مساعده", "help", "اوامر"))
                return Help + "\n\nوعندي " + Catalog.Length + " قالب سيرفر جاهز: " + list + ".";
            if (n.Length < 40 && Has(n, "مرحبا", "السلام", "هلا", "اهلا", "هاي", "hi", "hello", "صباح", "مساء", "يا بوت"))
                return "هلا والله! أنا مساعد حُفرة. قل لي وش تبي أسوي في سيرفرك، مثلاً «سوّ لي سيرفر متجر»، أو اكتب «القوالب» وأعرض لك الأنواع الجاهزة.";
            return null;
        }
    }
}
