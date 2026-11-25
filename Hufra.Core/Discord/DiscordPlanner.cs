using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hufra.Core.Settings;
using Hufra.Core.Translation;

namespace Hufra.Core.Discord
{
    /// <summary>يحوّل طلب المستخدم (بالعربي أو الإنجليزي) إلى خطة JSON منظّمة عبر Gemini. لا ينفّذ شيئاً.</summary>
    public static class DiscordPlanner
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(90) };

        private const string SystemPrompt = @"You are the planning engine of a Discord server-management assistant. The user chats in Arabic (or English) and asks to create or change roles, categories, channels and permissions. You do NOT execute anything; you output a JSON plan that the app shows to the user for approval.

Output ONLY a JSON object: {""message"": string, ""actions"": [ ... ]}
- ""message"": a short friendly reply in the user's language (Arabic by default) explaining the plan, or answering a question if no action is needed. Never claim anything was done.
- ""actions"": ordered list. Each action has ""type"" and fields:
  * create_role: name, color (hex like #3498db), permissions (array of names), hoist (bool, show separately), mentionable (bool)
  * create_category: name, visible_to (array of role names; omit/empty = public to everyone)
  * create_channel: name, kind (""text""|""voice""|""announcement""|""forum""), category (name of an existing or earlier-created category), topic, visible_to (role names that can see it; empty = inherits the category), read_only (bool: everyone can read but not write), writable_by (role names allowed to write when read_only), slowmode (seconds)
  * set_permissions: target (channel or category name), role (role name or ""@everyone""), allow (array), deny (array)
  * reorder_roles: order (array of role names from highest to lowest)
  * delete_channel: name   |   delete_role: name
Permission names (exact): CreateInstantInvite, KickMembers, BanMembers, ManageChannels, ManageGuild, AddReactions, ViewAuditLog, ViewChannel, SendMessages, ManageMessages, EmbedLinks, AttachFiles, ReadMessageHistory, MentionEveryone, UseExternalEmojis, Connect, Speak, MuteMembers, DeafenMembers, MoveMembers, UseVAD, ChangeNickname, ManageNicknames, ManageRoles, ManageWebhooks, UseApplicationCommands, ManageEvents, ManageThreads, CreatePublicThreads, CreatePrivateThreads, SendMessagesInThreads, ModerateMembers, Stream, PrioritySpeaker.

Rules:
1. NEVER use the Administrator permission. If the user asks for admin-level power, give the closest safe set (e.g. ManageMessages, KickMembers, ModerateMembers) and say so in message.
2. Never delete anything unless the user explicitly asked to delete that exact thing. Use exact existing names from the server structure below.
3. Create roles BEFORE channels that reference them. Create a category before channels placed in it.
4. Keep it minimal and sensible: do what was asked, with good defaults (e.g. a staff hierarchy from highest to lowest, muted-by-default read-only announcement rooms). Role colours should be distinct. Use at most 60 actions.
5. Names may include a leading emoji. Use the user's language for names unless told otherwise.
6. If the request is unclear or impossible via roles/channels/permissions, return an empty actions array and ask a short clarifying question in message.
Do not wrap the JSON in markdown.";

        public static async Task<DiscordPlan> PlanAsync(string structure, IReadOnlyList<(string Role, string Text)> history, CancellationToken ct = default)
        {
            string key = AppSettingsStore.GetGeminiApiKey();
            if (string.IsNullOrWhiteSpace(key))
                throw new InvalidOperationException("ما عندك مفتاح Gemini. أضفه من «الإعدادات» عشان يشتغل الشات.");

            var (model, status) = await GeminiModelResolver.ResolveAsync(key);
            if (model == null)
                throw new InvalidOperationException(status is 400 or 401 or 403
                    ? "مفتاح Gemini غير صحيح. تأكد منه في «الإعدادات»."
                    : "تعذّر الاتصال بـ Gemini (رمز " + status + ").");

            var contents = new List<object>();
            foreach (var h in history.TakeLast(8))
                contents.Add(new { role = h.Role == "user" ? "user" : "model", parts = new[] { new { text = h.Text } } });

            var body = new
            {
                systemInstruction = new { parts = new[] { new { text = SystemPrompt + "\n\nCurrent server structure:\n" + structure } } },
                contents,
                generationConfig = new { temperature = 0.2, responseMimeType = "application/json", maxOutputTokens = 8192 },
            };

            for (int attempt = 0; attempt < 4; attempt++)
            {
                using var req = new HttpRequestMessage(HttpMethod.Post,
                    $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent");
                req.Headers.Add("x-goog-api-key", key);
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

                HttpResponseMessage resp;
                try { resp = await Http.SendAsync(req, ct); }
                catch (HttpRequestException) { throw new InvalidOperationException("لا يوجد اتصال بالإنترنت."); }
                catch (TaskCanceledException) when (!ct.IsCancellationRequested) { throw new InvalidOperationException("انتهت مهلة الاتصال بـ Gemini."); }

                string text = await resp.Content.ReadAsStringAsync(ct);
                int code = (int)resp.StatusCode;
                if (code is 429 or 500 or 503 && attempt < 3) { await Task.Delay(2000 * (attempt + 1), ct); continue; }
                if (!resp.IsSuccessStatusCode)
                    throw new InvalidOperationException(code == 429
                        ? "تجاوزت حد Gemini المجاني. انتظر قليلاً وجرّب."
                        : "خطأ من Gemini (" + code + ").");

                string json;
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    json = doc.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
                }
                catch { throw new InvalidOperationException("رد Gemini غير مفهوم (قد يكون محجوباً). أعد صياغة الطلب."); }

                try { return DiscordPlan.Parse(json); }
                catch (JsonException) when (attempt < 3) { continue; }
                catch (JsonException) { throw new InvalidOperationException("تعذّر فهم الخطة. أعد صياغة الطلب بشكل أبسط."); }
            }
            throw new InvalidOperationException("فشل إنشاء الخطة.");
        }

        /// <summary>وصف مختصر للهيكل الحالي (رتب + رومات) يُرسل للنموذج لكي يستخدم الأسماء الصحيحة</summary>
        public static string DescribeStructure(IEnumerable<RoleInfo> roles, IEnumerable<ChannelInfo> channels)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Roles (high to low): " + string.Join(", ", roles.Where(r => !r.Managed && r.Name != "@everyone").OrderByDescending(r => r.Position).Select(r => r.Name)));
            var list = channels.ToList();
            foreach (var cat in list.Where(c => c.Type == 4).OrderBy(c => c.Position))
            {
                sb.AppendLine($"Category: {cat.Name}");
                foreach (var ch in list.Where(c => c.ParentId == cat.Id).OrderBy(c => c.Position))
                    sb.AppendLine($"  - {ch.Name} ({TypeName(ch.Type)})");
            }
            foreach (var ch in list.Where(c => c.Type != 4 && c.ParentId == null))
                sb.AppendLine($"(no category) {ch.Name} ({TypeName(ch.Type)})");
            return sb.ToString();
        }

        private static string TypeName(int t) => t switch { 0 => "text", 2 => "voice", 5 => "announcement", 13 => "stage", 15 => "forum", _ => "type" + t };
    }
}
