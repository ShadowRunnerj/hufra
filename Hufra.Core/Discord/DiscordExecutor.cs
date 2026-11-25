using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Hufra.Core.Discord
{
    public class ExecutionResult
    {
        public int Succeeded { get; set; }
        public int Failed { get; set; }
    }

    /// <summary>ينفّذ خطة معتمدة من المستخدم على السيرفر عبر Discord API، إجراء بإجراء مع تقرير لكل خطوة.</summary>
    public sealed class DiscordExecutor
    {
        private readonly DiscordApi _api;
        private readonly string _guildId;
        private readonly string _botId;

        private readonly Dictionary<string, string> _roleIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ChannelInfo> _channels = new(StringComparer.OrdinalIgnoreCase);

        public DiscordExecutor(DiscordApi api, string guildId, string botId)
        {
            _api = api; _guildId = guildId; _botId = botId;
        }

        private string RoleId(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("اسم رتبة فارغ.");
            if (name.Trim().Equals("@everyone", StringComparison.OrdinalIgnoreCase) || name.Trim().Equals("everyone", StringComparison.OrdinalIgnoreCase))
                return _guildId; // رتبة everyone معرّفها = معرّف السيرفر
            return _roleIds.TryGetValue(name.Trim(), out var id) ? id : throw new InvalidOperationException($"الرتبة «{name}» غير موجودة.");
        }

        private ChannelInfo Channel(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("اسم روم فارغ.");
            return _channels.TryGetValue(Normalize(name), out var c) ? c : throw new InvalidOperationException($"الروم/الفئة «{name}» غير موجود.");
        }

        // ديسكورد يحوّل أسماء الرومات النصية إلى أحرف صغيرة وشرطات، فنقارن بنفس الصيغة
        private static string Normalize(string name) => name.Trim().ToLowerInvariant().Replace(' ', '-');

        private static int ParseColor(string hex)
        {
            if (string.IsNullOrWhiteSpace(hex)) return 0;
            hex = hex.Trim().TrimStart('#');
            return int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v) ? v & 0xFFFFFF : 0;
        }

        private static int KindToType(string kind) => (kind ?? "text").ToLowerInvariant() switch
        {
            "voice" => 2, "announcement" => 5, "forum" => 15, "stage" => 13, _ => 0,
        };

        public async Task<ExecutionResult> ExecuteAsync(DiscordPlan plan, Action<string> log, CancellationToken ct = default)
        {
            var result = new ExecutionResult();

            foreach (var r in await _api.GetRolesAsync(_guildId, ct)) _roleIds[r.Name] = r.Id;
            foreach (var c in await _api.GetChannelsAsync(_guildId, ct)) _channels[Normalize(c.Name)] = c;

            foreach (var a in plan.Actions)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    await RunOneAsync(a, ct);
                    result.Succeeded++;
                    log("✅ " + a.Describe());
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    result.Failed++;
                    log("❌ " + a.Describe() + "\n    السبب: " + ex.Message);
                    if (ex is DiscordApiException { Status: 401 }) break; // لا فائدة من المتابعة
                }
                await Task.Delay(300, ct); // تجنّب حدود المعدّل
            }
            return result;
        }

        private async Task RunOneAsync(DiscordAction a, CancellationToken ct)
        {
            switch (a.Type)
            {
                case "create_role":
                {
                    if (string.IsNullOrWhiteSpace(a.Name)) throw new InvalidOperationException("اسم الرتبة فارغ.");
                    ulong perms = DiscordPermissions.Parse(a.Permissions, out var unknown);
                    if (unknown.Count > 0) throw new InvalidOperationException("صلاحيات غير معروفة: " + string.Join("، ", unknown));
                    string id = await _api.CreateRoleAsync(_guildId, a.Name.Trim(), ParseColor(a.Color), perms, a.Hoist, a.Mentionable, ct);
                    _roleIds[a.Name.Trim()] = id;
                    break;
                }
                case "create_category":
                case "create_channel":
                {
                    bool isCat = a.Type == "create_category";
                    if (string.IsNullOrWhiteSpace(a.Name)) throw new InvalidOperationException("الاسم فارغ.");
                    int type = isCat ? 4 : KindToType(a.Kind);

                    string parentId = null;
                    var ow = new Dictionary<string, (ulong Allow, ulong Deny)>();
                    if (!isCat && !string.IsNullOrWhiteSpace(a.Category))
                    {
                        var parent = Channel(a.Category);
                        parentId = parent.Id;
                        // القناة الجديدة ترث صلاحيات الفئة (API لا يزامنها تلقائياً)
                        foreach (var o in parent.Overwrites) ow[o.Id] = (o.Allow, o.Deny);
                    }

                    ulong view = DiscordPermissions.View;
                    ulong readHist = DiscordPermissions.Map["ReadMessageHistory"];
                    ulong connect = DiscordPermissions.Map["Connect"];
                    ulong send = DiscordPermissions.Send;

                    void Merge(string id, ulong allow, ulong deny)
                    {
                        ow.TryGetValue(id, out var cur);
                        ow[id] = ((cur.Allow | allow) & ~deny, (cur.Deny | deny) & ~allow);
                    }

                    if (a.VisibleTo is { Count: > 0 })
                    {
                        Merge(_guildId, 0, view);
                        foreach (var r in a.VisibleTo)
                            Merge(RoleId(r), view | readHist | (type == 2 ? connect : 0), 0);
                    }
                    if (!isCat && a.ReadOnly)
                    {
                        ulong noWrite = send | DiscordPermissions.Map["AddReactions"] | DiscordPermissions.Map["CreatePublicThreads"];
                        Merge(_guildId, 0, noWrite);
                        if (a.VisibleTo != null) foreach (var r in a.VisibleTo) Merge(RoleId(r), 0, noWrite);
                        foreach (var r in a.WritableBy ?? new List<string>()) Merge(RoleId(r), send, 0);
                    }

                    var overwrites = ow.Select(kv => new Overwrite(kv.Key, 0, kv.Value.Allow, kv.Value.Deny)).ToList();
                    string newId = await _api.CreateChannelAsync(_guildId, a.Name.Trim(), type, parentId, a.Topic, overwrites, a.Slowmode, ct);
                    _channels[Normalize(a.Name)] = new ChannelInfo(newId, a.Name.Trim(), type, parentId, 0, overwrites);
                    break;
                }
                case "set_permissions":
                {
                    var ch = Channel(a.Target);
                    string roleId = RoleId(a.Role);
                    ulong allow = DiscordPermissions.Parse(a.Allow, out var u1);
                    ulong deny = DiscordPermissions.Parse(a.Deny, out var u2);
                    var unknown = u1.Concat(u2).ToList();
                    if (unknown.Count > 0) throw new InvalidOperationException("صلاحيات غير معروفة: " + string.Join("، ", unknown));
                    var cur = ch.Overwrites.FirstOrDefault(o => o.Id == roleId);
                    ulong newAllow = ((cur?.Allow ?? 0) | allow) & ~deny;
                    ulong newDeny = ((cur?.Deny ?? 0) | deny) & ~allow;
                    await _api.SetOverwriteAsync(ch.Id, roleId, 0, newAllow, newDeny, ct);
                    ch.Overwrites.RemoveAll(o => o.Id == roleId);
                    ch.Overwrites.Add(new Overwrite(roleId, 0, newAllow, newDeny));
                    break;
                }
                case "reorder_roles":
                {
                    var all = await _api.GetRolesAsync(_guildId, ct);
                    var botRoleIds = await _api.GetBotRoleIdsAsync(_guildId, _botId, ct);
                    int botTop = all.Where(r => botRoleIds.Contains(r.Id)).Select(r => r.Position).DefaultIfEmpty(1).Max();
                    var order = new List<(string, int)>();
                    int pos = Math.Max(1, botTop - 1);
                    foreach (var n in a.Order ?? new List<string>())
                    {
                        order.Add((RoleId(n), Math.Max(1, pos)));
                        pos--;
                    }
                    if (order.Count == 0) throw new InvalidOperationException("قائمة الترتيب فارغة.");
                    await _api.ReorderRolesAsync(_guildId, order, ct);
                    break;
                }
                case "delete_channel":
                {
                    var ch = Channel(a.Name);
                    await _api.DeleteChannelAsync(ch.Id, ct);
                    _channels.Remove(Normalize(a.Name));
                    break;
                }
                case "delete_role":
                {
                    string id = RoleId(a.Name);
                    if (id == _guildId) throw new InvalidOperationException("لا يمكن حذف @everyone.");
                    await _api.DeleteRoleAsync(_guildId, id, ct);
                    _roleIds.Remove(a.Name.Trim());
                    break;
                }
                default:
                    throw new InvalidOperationException("نوع إجراء غير معروف: " + a.Type);
            }
        }
    }
}
