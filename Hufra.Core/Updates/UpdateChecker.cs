using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Hufra.Core.Updates
{
    public class UpdateInfo
    {
        public Version Version { get; set; }
        public string DownloadUrl { get; set; }
        public string FileName { get; set; }
        public string Notes { get; set; }
    }

    /// <summary>
    /// تحديث تلقائي عبر GitHub Releases: يقرأ آخر Release من المستودع المكتوب في ملف update-repo.txt
    /// (بجانب Hufra.exe، بصيغة owner/repo)، ويقارن رقم الإصدار (tag مثل v1.3.1) مع الإصدار الحالي،
    /// وينزّل ملف التنصيب (*.exe) المرفق بالـ Release.
    /// أي فشل (لا إنترنت، المستودع غير مضبوط...) يرجّع null بصمت بدون إزعاج المستخدم.
    /// </summary>
    public static class UpdateChecker
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Hufra-Updater");
            c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return c;
        }

        /// <summary>owner/repo من ملف update-repo.txt، أو null لو غير مضبوط</summary>
        public static string GetRepo()
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, "update-repo.txt");
                if (!File.Exists(path)) return null;
                foreach (var raw in File.ReadAllLines(path))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    return Regex.IsMatch(line, @"^[\w.\-]+/[\w.\-]+$") && !line.StartsWith("OWNER/", StringComparison.OrdinalIgnoreCase)
                        ? line : null;
                }
            }
            catch { }
            return null;
        }

        /// <summary>سبب آخر نتيجة فحص (يُعرض في الإعدادات بدل الصمت)؛ null = أحدث إصدار فعلاً</summary>
        public static string LastProblem { get; private set; }

        public static async Task<UpdateInfo> CheckAsync(string currentVersion)
        {
            LastProblem = null;
            try
            {
                string repo = GetRepo();
                if (repo == null) { LastProblem = "المستودع غير مضبوط في update-repo.txt."; return null; }
                if (!Version.TryParse(currentVersion, out var current)) { LastProblem = "رقم الإصدار الحالي غير صالح."; return null; }

                string json;
                try { json = await Http.GetStringAsync($"https://api.github.com/repos/{repo}/releases/latest"); }
                catch (HttpRequestException hex) when (hex.StatusCode == System.Net.HttpStatusCode.NotFound)
                { LastProblem = $"ما لقيت Release منشور في «{repo}» (المستودع غلط أو Private أو ما فيه إصدار)."; return null; }
                catch (HttpRequestException hex) when (hex.StatusCode == System.Net.HttpStatusCode.Forbidden)
                { LastProblem = "GitHub رفض الطلب (حد الطلبات). جرّب بعد قليل."; return null; }
                catch (HttpRequestException) { LastProblem = "ما فيه اتصال بالإنترنت أو GitHub غير متاح."; return null; }
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                string tag = root.GetProperty("tag_name").GetString() ?? "";
                var m = Regex.Match(tag, @"\d+(\.\d+){1,3}");
                if (!m.Success || !Version.TryParse(m.Value, out var latest)) { LastProblem = $"اسم الـ tag «{tag}» ما فيه رقم إصدار (استخدم مثل v2.7.0)."; return null; }
                if (latest <= current) return null;

                foreach (var asset in root.GetProperty("assets").EnumerateArray())
                {
                    string name = asset.GetProperty("name").GetString() ?? "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        return new UpdateInfo
                        {
                            Version = latest,
                            FileName = name,
                            DownloadUrl = asset.GetProperty("browser_download_url").GetString(),
                            Notes = root.TryGetProperty("body", out var b) ? b.GetString() : null,
                        };
                    }
                }
                LastProblem = "الـ Release موجود لكن ما فيه ملف .exe مرفق (ارفع Hufra-Setup.exe)."; return null;
            }
            catch (Exception ex) { LastProblem = "تعذّر الفحص: " + ex.Message; }
            return null;
        }

        /// <summary>كل الإصدارات المنشورة (من الأحدث) — لصفحة التحديثات</summary>
        public static async Task<System.Collections.Generic.List<UpdateInfo>> ListAsync()
        {
            LastProblem = null;
            var list = new System.Collections.Generic.List<UpdateInfo>();
            try
            {
                string repo = GetRepo();
                if (repo == null) { LastProblem = "المستودع غير مضبوط في update-repo.txt."; return list; }
                string json;
                try { json = await Http.GetStringAsync($"https://api.github.com/repos/{repo}/releases?per_page=30"); }
                catch (HttpRequestException hex) when (hex.StatusCode == System.Net.HttpStatusCode.NotFound)
                { LastProblem = $"ما لقيت المستودع «{repo}» (غلط أو Private)."; return list; }
                catch (HttpRequestException) { LastProblem = "ما فيه اتصال بالإنترنت أو GitHub غير متاح/رفض الطلب."; return list; }

                using var doc = JsonDocument.Parse(json);
                foreach (var rel in doc.RootElement.EnumerateArray())
                {
                    if (rel.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
                    string tagName = rel.GetProperty("tag_name").GetString() ?? "";
                    if (tagName == "v2.7.0" || tagName == "v2.7.1") continue; // إصدارات قديمة خاطئة التسلسل
                    var m = Regex.Match(rel.GetProperty("tag_name").GetString() ?? "", @"\d+(\.\d+){1,3}");
                    if (!m.Success || !Version.TryParse(m.Value, out var ver)) continue;
                    foreach (var asset in rel.GetProperty("assets").EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(new UpdateInfo
                        {
                            Version = ver, FileName = name,
                            DownloadUrl = asset.GetProperty("browser_download_url").GetString(),
                            Notes = rel.TryGetProperty("body", out var b) ? b.GetString() : null,
                        });
                        break;
                    }
                }
                list.Sort((a, b) => b.Version.CompareTo(a.Version));
                if (list.Count == 0) LastProblem = "ما فيه إصدارات منشورة فيها ملف .exe.";
            }
            catch (Exception ex) { LastProblem = "تعذّر جلب الإصدارات: " + ex.Message; }
            return list;
        }

        /// <summary>ينزّل ملف التنصيب لمجلد Temp ويرجّع مساره (progress من 0 إلى 100)</summary>
        public static async Task<string> DownloadAsync(UpdateInfo info, IProgress<int> progress)
        {
            string dest = Path.Combine(Path.GetTempPath(), "Hufra-Update-" + info.Version + ".exe");

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Hufra-Updater");
            using var resp = await client.GetAsync(info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? -1;
            await using var src = await resp.Content.ReadAsStreamAsync();
            await using var dst = File.Create(dest);

            var buffer = new byte[81920];
            long read = 0; int n; int last = -1;
            while ((n = await src.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await dst.WriteAsync(buffer, 0, n);
                read += n;
                if (total > 0)
                {
                    int pct = (int)(read * 100 / total);
                    if (pct != last) { last = pct; progress?.Report(pct); }
                }
            }
            return dest;
        }
    }
}
