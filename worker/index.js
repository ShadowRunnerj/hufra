// حُفرة — الوسيط الآمن (Cloudflare Worker)
// يحتفظ بتوكن البوت سراً، ويسمح لمالك السيرفر فقط (بعد تسجيل دخوله بديسكورد) بتنفيذ عمليات محدودة على سيرفره.
// المتغيرات السرية: BOT_TOKEN, CLIENT_SECRET, SESSION_SECRET   |  عادي: CLIENT_ID

const API = "https://discord.com/api/v10";
const SESSION_HOURS = 8;
const enc = new TextEncoder();

const json = (obj, status = 200) =>
  new Response(JSON.stringify(obj), { status, headers: { "content-type": "application/json; charset=utf-8" } });

// ---------- جلسة موقّعة (HMAC) ----------
const b64u = (buf) => btoa(String.fromCharCode(...new Uint8Array(buf))).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
const unb64u = (s) => Uint8Array.from(atob(s.replace(/-/g, "+").replace(/_/g, "/")), (c) => c.charCodeAt(0));
const hmacKey = (secret, usage) => crypto.subtle.importKey("raw", enc.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, [usage]);

export async function signSession(payload, secret) {
  const body = b64u(enc.encode(JSON.stringify(payload)));
  const sig = await crypto.subtle.sign("HMAC", await hmacKey(secret, "sign"), enc.encode(body));
  return body + "." + b64u(sig);
}

export async function verifySession(token, secret) {
  try {
    const [body, sig] = (token || "").split(".");
    if (!body || !sig) return null;
    const ok = await crypto.subtle.verify("HMAC", await hmacKey(secret, "verify"), unb64u(sig), enc.encode(body));
    if (!ok) return null;
    const p = JSON.parse(new TextDecoder().decode(unb64u(body)));
    return p.exp > Math.floor(Date.now() / 1000) ? p : null;
  } catch { return null; }
}

// ---------- تحقق المسارات المسموحة ----------
// يرجّع { ok, guild?, channel? } — channel يعني لازم نتأكد أن القناة تابعة لسيرفر يملكه المستخدم
export function checkRoute(method, path, owned, botId) {
  const own = (g) => owned.includes(g);
  let m;
  if (method === "GET" && path === "users/@me") return { ok: true };
  if ((m = path.match(/^guilds\/(\d+)$/)) && method === "GET") return { ok: own(m[1]) };
  if ((m = path.match(/^guilds\/(\d+)\/(roles|channels)$/))) {
    if (!own(m[1])) return { ok: false };
    if (method === "GET" || method === "POST") return { ok: true, adminCheck: m[2] === "roles" };
    if (method === "PATCH" && m[2] === "roles") return { ok: true };
    return { ok: false };
  }
  if ((m = path.match(/^guilds\/(\d+)\/roles\/(\d+)$/)) && method === "DELETE") return { ok: own(m[1]) };
  if ((m = path.match(/^guilds\/(\d+)\/members\/(\d+)$/)) && method === "GET") return { ok: own(m[1]) && m[2] === botId };
  if ((m = path.match(/^channels\/(\d+)$/)) && method === "DELETE") return { ok: true, channel: m[1] };
  if ((m = path.match(/^channels\/(\d+)\/permissions\/(\d+)$/)) && method === "PUT") return { ok: true, channel: m[1] };
  return { ok: false };
}

const hasAdmin = (v) => { try { return (BigInt(v ?? "0") & 8n) !== 0n; } catch { return true; } };

const botFetch = (env, path, init = {}) =>
  fetch(API + "/" + path, { ...init, headers: { Authorization: "Bot " + env.BOT_TOKEN, "content-type": "application/json", ...(init.headers || {}) } });

async function botGuildIds(env) {
  const r = await botFetch(env, "users/@me/guilds?limit=200");
  return r.ok ? (await r.json()).map((g) => g.id) : [];
}

async function listOwned(session, env) {
  const inBot = new Set(await botGuildIds(env));
  return session.owned.map(([id, name]) => ({ id, name, botIn: inBot.has(id) }));
}

export default {
  async fetch(req, env) {
    const url = new URL(req.url);
    const path = url.pathname.replace(/^\/+/, "");

    if (path === "" || path === "health") return json({ ok: true, name: "hufra-backend" });

    // 1) تسجيل الدخول: يستبدل code بهوية المستخدم ويرجّع جلسة + سيرفراته
    if (path === "auth" && req.method === "POST") {
      const { code, redirect_uri } = await req.json().catch(() => ({}));
      if (!code || !redirect_uri) return json({ error: "bad_request" }, 400);
      const tok = await fetch(API + "/oauth2/token", {
        method: "POST", headers: { "content-type": "application/x-www-form-urlencoded" },
        body: new URLSearchParams({ client_id: env.CLIENT_ID, client_secret: env.CLIENT_SECRET, grant_type: "authorization_code", code, redirect_uri }),
      });
      if (!tok.ok) return json({ error: "oauth_failed" }, 401);
      const { access_token } = await tok.json();
      const h = { Authorization: "Bearer " + access_token };
      const [me, gs] = await Promise.all([fetch(API + "/users/@me", { headers: h }), fetch(API + "/users/@me/guilds", { headers: h })]);
      if (!me.ok || !gs.ok) return json({ error: "discord_failed" }, 502);
      const user = await me.json();
      const owned = (await gs.json()).filter((g) => g.owner).map((g) => [g.id, g.name]);
      const session = await signSession({ uid: user.id, owned, exp: Math.floor(Date.now() / 1000) + SESSION_HOURS * 3600 }, env.SESSION_SECRET);
      return json({ session, user: { id: user.id, name: user.global_name || user.username }, clientId: env.CLIENT_ID, guilds: await listOwned({ owned }, env) });
    }

    // باقي المسارات تحتاج جلسة صالحة
    const auth = req.headers.get("authorization") || "";
    const session = await verifySession(auth.replace(/^Session\s+/i, ""), env.SESSION_SECRET);
    if (!session) return json({ error: "session_expired" }, 401);
    const owned = session.owned.map((x) => x[0]);

    // 2) تحديث حالة السيرفرات (هل البوت داخلها؟)
    if (path === "guilds" && req.method === "GET") return json({ clientId: env.CLIENT_ID, guilds: await listOwned(session, env) });

    // 3) وسيط Discord المحدود
    if (path.startsWith("discord/")) {
      const dpath = path.slice("discord/".length) + (url.search || "");
      const cleanPath = path.slice("discord/".length);
      const rule = checkRoute(req.method, cleanPath, owned, env.CLIENT_ID);
      if (!rule.ok) return json({ message: "غير مسموح لهذا الحساب أو لهذا الإجراء." }, 403);

      if (rule.channel) {
        const ch = await botFetch(env, "channels/" + rule.channel);
        if (!ch.ok) return new Response(await ch.text(), { status: ch.status, headers: ch.headers });
        const c = await ch.json();
        if (!owned.includes(c.guild_id)) return json({ message: "هذه القناة ليست في سيرفر تملكه." }, 403);
      }

      let body;
      if (req.method !== "GET" && req.method !== "DELETE") {
        body = await req.text();
        if (rule.adminCheck) {
          try {
            const b = JSON.parse(body || "{}");
            const perms = Array.isArray(b) ? [] : [b.permissions];
            if (perms.some(hasAdmin)) return json({ message: "منح صلاحية Administrator مرفوض." }, 403);
          } catch { return json({ message: "طلب غير صالح." }, 400); }
        }
      }
      const headers = {};
      const reason = req.headers.get("x-audit-log-reason");
      if (reason) headers["X-Audit-Log-Reason"] = reason;
      const r = await botFetch(env, dpath, { method: req.method, headers, body });
      return new Response(r.body, { status: r.status, headers: { "content-type": r.headers.get("content-type") || "application/json" } });
    }

    return json({ error: "not_found" }, 404);
  },
};
