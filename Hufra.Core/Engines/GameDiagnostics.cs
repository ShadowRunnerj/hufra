using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Hufra.Core.Engines
{
    /// <summary>لما ما نلقى نصوص: نشرح للمستخدم السبب الحقيقي بالأرقام بدل رسالة عامة.</summary>
    public static class GameDiagnostics
    {
        public static string Explain(string folder, EngineType engine)
        {
            var sb = new StringBuilder();
            try
            {
                var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories);
                var byExt = files.GroupBy(f => Path.GetExtension(f).ToLowerInvariant())
                                 .Select(g => (ext: string.IsNullOrEmpty(g.Key) ? "(بلا امتداد)" : g.Key, n: g.Count(), size: g.Sum(f => new FileInfo(f).Length)))
                                 .OrderByDescending(x => x.size).Take(6).ToList();

                sb.AppendLine($"فحصت {files.Length} ملف داخل مجلد اللعبة ولم أجد نصوصاً قابلة للاستخراج في الملفات المفتوحة.");
                sb.AppendLine("أكبر أنواع الملفات: " + string.Join("، ", byExt.Select(x => $"{x.ext} ({x.n} ملف، {x.size / 1024 / 1024} MB)")));

                var packed = PackedAssetScanner.Find(folder, engine);
                var big = files.Select(f => new FileInfo(f)).OrderByDescending(f => f.Length).Take(3)
                               .Select(f => $"{f.Name} ({f.Length / 1024 / 1024} MB)");
                sb.AppendLine("أكبر ملفات: " + string.Join("، ", big));

                if (packed.Count > 0)
                    sb.AppendLine("السبب الأرجح: " + string.Join("؛ ", packed) + ". نصوص هذه اللعبة محزومة داخل ملفات ثنائية ولا يمكن قراءتها كنص مباشرة.");
                else
                    sb.AppendLine("السبب الأرجح: النصوص مخزّنة داخل ملفات ثنائية/مشفّرة/مضغوطة (أو داخل الملف التنفيذي نفسه) وليست ملفات نصية مفتوحة.");

                if (engine == EngineType.Unity)
                    sb.AppendLine("لألعاب Unity من نوع Mono: استخدم زر «تركيب أداة تعريب Unity» الذي يلتقط النصوص أثناء تشغيل اللعبة.");
                else
                    sb.AppendLine("هذه حالة لا يغطيها حُفرة حالياً. لا أريد أن أوهمك بغير ذلك.");
            }
            catch (Exception ex)
            {
                sb.AppendLine("تعذّر فحص المجلد: " + ex.Message);
            }
            return sb.ToString().TrimEnd();
        }
    }
}
