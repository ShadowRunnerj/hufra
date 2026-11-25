using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Hufra.Core.Discord
{
    public class DiscordApiException : Exception
    {
        public int Status { get; }
        public DiscordApiException(int status, string message) : base(message) { Status = status; }
    }

    public record Overwrite(string Id, int Type, ulong Allow, ulong Deny);
    public record RoleInfo(string Id, string Name, int Position, bool Managed);
    public record ChannelInfo(string Id, string Name, int Type, string ParentId, int Position, List<Overwrite> Overwrites);

    /// <summary>غلاف بسيط لـ Discord REST API v10 باستخدام توكن البوت، مع إعادة المحاولة عند 429.</summary>
    public sealed class DiscordApi : IDisposable
    {
        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private readonly bool _viaBackend;

        public DiscordApi(string botToken)
        {
            _http.BaseAddress = new Uri("https://discord.com/api/v10/");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bot " + (botToken ?? "").Trim());
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordBot (https://hufra.app, 1.0)");
        }

        /// <summary>عبر وسيط حُفرة: بدون توكن بوت في التطبيق، بجلسة المستخدم فقط</summary>
        public DiscordApi(string backendUrl, string session)
        {
            _viaBackend = true;
            _http.BaseAddress = new Uri(backendUrl.TrimEnd('/') + "/discord/");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Session " + session);
        }

        public void Dispose() => _http.Dispose();

        private async Task<JsonElement> SendAsync(HttpMethod method, string path, object body = null, string reason = null, CancellationToken ct = default)
        {
            for (int attempt = 0; attempt < 6; attempt++)
            {
                using var req = new HttpRequestMessage(method, path);
                if (body != null)
                    req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(reason))
                    req.Headers.TryAddWithoutValidation("X-Audit-Log-Reason", Uri.EscapeDataString(reason));

                HttpResponseMessage resp;
                try { resp = await _http.SendAsync(req, ct); }
                catch (HttpRequestException) { throw new DiscordApiException(0, "لا يوجد اتصال بالإنترنت أو تعذّر الوصول إلى ديسكورد."); }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new DiscordApiException(0, "انتهت مهلة الاتصال بديسكورد."); }

                string text = await resp.Content.ReadAsStringAsync(ct);

                if ((int)resp.StatusCode == 429)
                {
                    double wait = 2;
                    try { wait = JsonDocument.Parse(text).RootElement.GetProperty("retry_after").GetDouble(); } catch { }
                    await Task.Delay(TimeSpan.FromSeconds(Math.Min(wait, 30) + 0.2), ct);
                    continue;
                }

                if (resp.IsSuccessStatusCode)
                {
                    if (string.IsNullOrWhiteSpace(text)) return default;
                    using var doc = JsonDocument.Parse(text);
                    return doc.RootElement.Clone();
                }
                throw Friendly((int)resp.StatusCode, text);
            }
            throw new DiscordApiException(429, "ديسكورد يطلب الإبطاء كثيراً. جرّب بعد قليل.");
        }

        private DiscordApiException Friendly(int status, string body)
        {
            string detail = "";
            try { detail = JsonDocument.Parse(body).RootElement.GetProperty("message").GetString(); } catch { }
            return status switch
            {
                401 => new DiscordApiException(401, _viaBackend ? "انتهت الجلسة. سجّل دخولك بديسكورد مرة ثانية." : "توكن البوت غير صحيح أو ملغى."),
                403 => new DiscordApiException(403, "البوت ما عنده صلاحية لهذا الإجراء (Missing Permissions). تأكد أن رتبة البوت أعلى من الرتب المطلوبة وعنده Manage Roles وManage Channels. " + detail),
                404 => new DiscordApiException(404, "غير موجود: تأكد من Server ID وأن البوت داخل السيرفر. " + detail),
                _ => new DiscordApiException(status, $"خطأ من ديسكورد ({status}). {detail}"),
            };
        }

        public async Task<(string Id, string Name)> GetMeAsync(CancellationToken ct = default)
        {
            var e = await SendAsync(HttpMethod.Get, "users/@me", ct: ct);
            return (e.GetProperty("id").GetString(), e.GetProperty("username").GetString());
        }

        /// <summary>السيرفرات اللي البوت داخلها (للاختيار التلقائي بدون ما المستخدم يدخل Server ID)</summary>
        public async Task<List<(string Id, string Name)>> GetMyGuildsAsync(CancellationToken ct = default)
        {
            var e = await SendAsync(HttpMethod.Get, "users/@me/guilds", ct: ct);
            return e.EnumerateArray().Select(g => (g.GetProperty("id").GetString(), g.GetProperty("name").GetString())).ToList();
        }

        public async Task<string> GetGuildNameAsync(string guildId, CancellationToken ct = default)
        {
            var e = await SendAsync(HttpMethod.Get, $"guilds/{guildId}", ct: ct);
            return e.GetProperty("name").GetString();
        }

        public async Task<List<RoleInfo>> GetRolesAsync(string guildId, CancellationToken ct = default)
        {
            var e = await SendAsync(HttpMethod.Get, $"guilds/{guildId}/roles", ct: ct);
            return e.EnumerateArray().Select(r => new RoleInfo(
                r.GetProperty("id").GetString(), r.GetProperty("name").GetString(),
                r.GetProperty("position").GetInt32(),
                r.TryGetProperty("managed", out var m) && m.GetBoolean())).ToList();
        }

        public async Task<List<string>> GetBotRoleIdsAsync(string guildId, string botId, CancellationToken ct = default)
        {
            var e = await SendAsync(HttpMethod.Get, $"guilds/{guildId}/members/{botId}", ct: ct);
            return e.GetProperty("roles").EnumerateArray().Select(x => x.GetString()).ToList();
        }

        public async Task<List<ChannelInfo>> GetChannelsAsync(string guildId, CancellationToken ct = default)
        {
            var e = await SendAsync(HttpMethod.Get, $"guilds/{guildId}/channels", ct: ct);
            var list = new List<ChannelInfo>();
            foreach (var c in e.EnumerateArray())
            {
                var ows = new List<Overwrite>();
                if (c.TryGetProperty("permission_overwrites", out var arr))
                    foreach (var o in arr.EnumerateArray())
                        ows.Add(new Overwrite(o.GetProperty("id").GetString(), o.GetProperty("type").GetInt32(),
                            ulong.Parse(o.GetProperty("allow").GetString()), ulong.Parse(o.GetProperty("deny").GetString())));
                string parent = c.TryGetProperty("parent_id", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
                list.Add(new ChannelInfo(c.GetProperty("id").GetString(), c.GetProperty("name").GetString(),
                    c.GetProperty("type").GetInt32(), parent,
                    c.TryGetProperty("position", out var pos) ? pos.GetInt32() : 0, ows));
            }
            return list;
        }

        public async Task<string> CreateRoleAsync(string guildId, string name, int color, ulong permissions, bool hoist, bool mentionable, CancellationToken ct = default)
        {
            var e = await SendAsync(HttpMethod.Post, $"guilds/{guildId}/roles",
                new { name, color, permissions = permissions.ToString(), hoist, mentionable }, "Hufra Discord Assistant", ct);
            return e.GetProperty("id").GetString();
        }

        public async Task<string> CreateChannelAsync(string guildId, string name, int type, string parentId, string topic,
            IEnumerable<Overwrite> overwrites, int slowmode, CancellationToken ct = default)
        {
            var body = new Dictionary<string, object> { ["name"] = name, ["type"] = type };
            if (parentId != null) body["parent_id"] = parentId;
            if (!string.IsNullOrWhiteSpace(topic) && (type == 0 || type == 5 || type == 15)) body["topic"] = topic;
            if (slowmode > 0 && (type == 0 || type == 15)) body["rate_limit_per_user"] = Math.Min(slowmode, 21600);
            var ow = overwrites?.Select(o => new { id = o.Id, type = o.Type, allow = o.Allow.ToString(), deny = o.Deny.ToString() }).ToList();
            if (ow != null && ow.Count > 0) body["permission_overwrites"] = ow;
            var e = await SendAsync(HttpMethod.Post, $"guilds/{guildId}/channels", body, "Hufra Discord Assistant", ct);
            return e.GetProperty("id").GetString();
        }

        public Task SetOverwriteAsync(string channelId, string targetId, int type, ulong allow, ulong deny, CancellationToken ct = default) =>
            SendAsync(HttpMethod.Put, $"channels/{channelId}/permissions/{targetId}",
                new { allow = allow.ToString(), deny = deny.ToString(), type }, "Hufra Discord Assistant", ct);

        public Task DeleteChannelAsync(string channelId, CancellationToken ct = default) =>
            SendAsync(HttpMethod.Delete, $"channels/{channelId}", null, "Hufra Discord Assistant", ct);

        public Task DeleteRoleAsync(string guildId, string roleId, CancellationToken ct = default) =>
            SendAsync(HttpMethod.Delete, $"guilds/{guildId}/roles/{roleId}", null, "Hufra Discord Assistant", ct);

        public Task ReorderRolesAsync(string guildId, IEnumerable<(string Id, int Position)> order, CancellationToken ct = default) =>
            SendAsync(HttpMethod.Patch, $"guilds/{guildId}/roles",
                order.Select(o => new { id = o.Id, position = o.Position }).ToList(), "Hufra Discord Assistant", ct);

        /// <summary>رابط دعوة البوت (Manage Roles + Manage Channels + View/Send + Read History)</summary>
        public static string InviteUrl(string botId) =>
            $"https://discord.com/oauth2/authorize?client_id={botId}&scope=bot&permissions={268438544UL + (1UL << 16)}";
    }
}
