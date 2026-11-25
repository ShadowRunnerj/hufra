using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Hufra.Core.Discord
{
    public record OwnedGuild(string Id, string Name, bool BotIn);
    public record LoginResult(string Session, string UserName, string ClientId, List<OwnedGuild> Guilds);

    /// <summary>
    /// تسجيل الدخول بديسكورد (OAuth2) عبر الوسيط. لا يوجد أي توكن بوت داخل التطبيق:
    /// المستخدم يسجّل دخوله، والوسيط يعطيه جلسة تسمح له فقط بالسيرفرات اللي يملكها.
    /// </summary>
    public static class DiscordLogin
    {
        public const int Port = 53682;
        public static string RedirectUri => $"http://127.0.0.1:{Port}/callback";
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

        /// <summary>رابط الوسيط من ملف backend.txt بجانب البرنامج (أول سطر ليس تعليقاً)</summary>
        public static string BackendUrl()
        {
            try
            {
                string f = Path.Combine(AppContext.BaseDirectory, "backend.txt");
                if (!File.Exists(f)) return null;
                string line = File.ReadAllLines(f).Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0 && !l.StartsWith("#"));
                if (string.IsNullOrEmpty(line) || line.Contains("YOUR-")) return null;
                return line.TrimEnd('/');
            }
            catch { return null; }
        }

        public static string ClientIdFallback => "1574866992257826967";

        public static string InviteUrl(string clientId, string guildId) =>
            $"https://discord.com/oauth2/authorize?client_id={clientId}&permissions=8&integration_type=0&scope=bot&guild_id={guildId}&disable_guild_select=true";

        public static async Task<LoginResult> LoginAsync(string backend, string clientId, CancellationToken ct = default)
        {
            string state = Guid.NewGuid().ToString("N");
            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
            try { listener.Start(); }
            catch (Exception) { throw new DiscordApiException(0, $"تعذّر فتح المنفذ {Port} لاستقبال تسجيل الدخول (برنامج ثاني يستخدمه؟)."); }

            string authUrl = "https://discord.com/oauth2/authorize?response_type=code&scope=identify%20guilds" +
                             $"&client_id={clientId}&state={state}&redirect_uri={Uri.EscapeDataString(RedirectUri)}";
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            var ctxTask = listener.GetContextAsync();
            var done = await Task.WhenAny(ctxTask, Task.Delay(TimeSpan.FromMinutes(3), ct));
            if (done != ctxTask) throw new DiscordApiException(0, "انتهت مهلة تسجيل الدخول (3 دقائق).");
            var ctx = await ctxTask;

            string code = ctx.Request.QueryString["code"], st = ctx.Request.QueryString["state"], err = ctx.Request.QueryString["error"];
            bool ok = code != null && st == state;
            string html = ok
                ? "<html dir='rtl'><meta charset='utf-8'><body style='font-family:Segoe UI;text-align:center;padding:60px'><h2>✅ تم تسجيل الدخول</h2><p>ارجع إلى برنامج حُفرة.</p></body></html>"
                : "<html dir='rtl'><meta charset='utf-8'><body style='font-family:Segoe UI;text-align:center;padding:60px'><h2>❌ لم يكتمل تسجيل الدخول</h2><p>ارجع إلى حُفرة وحاول مرة ثانية.</p></body></html>";
            byte[] buf = Encoding.UTF8.GetBytes(html);
            ctx.Response.ContentType = "text/html; charset=utf-8";
            ctx.Response.OutputStream.Write(buf, 0, buf.Length);
            ctx.Response.Close();
            listener.Stop();

            if (!ok) throw new DiscordApiException(0, err == "access_denied" ? "رفضت تسجيل الدخول." : "لم يكتمل تسجيل الدخول.");

            using var req = new HttpRequestMessage(HttpMethod.Post, backend + "/auth")
            {
                Content = new StringContent(JsonSerializer.Serialize(new { code, redirect_uri = RedirectUri }), Encoding.UTF8, "application/json"),
            };
            return Parse(await SendAsync(req, ct));
        }

        public static async Task<LoginResult> RefreshAsync(string backend, string session, CancellationToken ct = default)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, backend + "/guilds");
            req.Headers.TryAddWithoutValidation("Authorization", "Session " + session);
            var r = Parse(await SendAsync(req, ct));
            return r with { Session = session };
        }

        private static async Task<string> SendAsync(HttpRequestMessage req, CancellationToken ct)
        {
            HttpResponseMessage resp;
            try { resp = await Http.SendAsync(req, ct); }
            catch (HttpRequestException) { throw new DiscordApiException(0, "تعذّر الوصول إلى خادم حُفرة الوسيط. تأكد من الإنترنت ومن الرابط في backend.txt."); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new DiscordApiException(0, "انتهت مهلة الاتصال بخادم حُفرة."); }
            string text = await resp.Content.ReadAsStringAsync(ct);
            if (resp.IsSuccessStatusCode) return text;
            if ((int)resp.StatusCode == 401) throw new DiscordApiException(401, "انتهت الجلسة أو فشل تسجيل الدخول. سجّل دخولك مرة ثانية.");
            throw new DiscordApiException((int)resp.StatusCode, $"خطأ من خادم حُفرة ({(int)resp.StatusCode}).");
        }

        private static LoginResult Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            string session = root.TryGetProperty("session", out var s) ? s.GetString() : null;
            string name = root.TryGetProperty("user", out var u) ? u.GetProperty("name").GetString() : null;
            string cid = root.TryGetProperty("clientId", out var c) ? c.GetString() : ClientIdFallback;
            var guilds = root.GetProperty("guilds").EnumerateArray()
                .Select(g => new OwnedGuild(g.GetProperty("id").GetString(), g.GetProperty("name").GetString(), g.GetProperty("botIn").GetBoolean()))
                .ToList();
            return new LoginResult(session, name, cid, guilds);
        }
    }
}
