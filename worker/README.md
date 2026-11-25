# خادم حُفرة الوسيط (Cloudflare Worker) — الإعداد مرة واحدة

الهدف: توكن البوت يبقى **عندك فقط** داخل Cloudflare، والمستخدم يسجّل دخوله بديسكورد ويدير **السيرفرات اللي يملكها فقط**.

## 0) أهم شي
- من لوحة المطورين → Bot → **Reset Token** (التوكن القديم انكشف في المحادثة).
- من OAuth2 → **Reset Secret** وانسخ الـ Client Secret.
- من OAuth2 → Redirects → **Add Redirect** → `http://127.0.0.1:53682/callback` ثم Save.

## 1) إنشاء الـ Worker (من الموقع، بدون أوامر)
1. سجّل مجاناً في dash.cloudflare.com ← **Workers & Pages** ← **Create** ← **Create Worker** ← Deploy.
2. اضغط **Edit code** ← امسح الكود ← الصق محتوى `worker/index.js` كله ← **Deploy**.
3. **Settings ← Variables and Secrets** ← أضف:
   - `CLIENT_ID` (نص عادي) = `1574866992257826967`
   - `BOT_TOKEN` (**Secret**) = التوكن الجديد
   - `CLIENT_SECRET` (**Secret**) = الـ Client Secret الجديد
   - `SESSION_SECRET` (**Secret**) = أي نص عشوائي طويل (٣٠ حرف أو أكثر)
4. انسخ رابط الـ Worker (مثل `https://hufra-backend.xxx.workers.dev`) وافتحه، لازم يظهر `{"ok":true,...}`.

## 2) ربط التطبيق
اكتب الرابط في `Hufra.App/backend.txt` (سطر واحد) وارفعه للمستودع، ثم انشر إصدار جديد.

## وش يسمح به الوسيط؟
فقط: قراءة وإنشاء الرتب والرومات، حذف رتبة/روم، تعديل صلاحيات روم، ترتيب الرتب — وفقط داخل سيرفر يملكه المستخدم.
لا طرد، لا حظر، لا حذف سيرفر، ولا منح صلاحية Administrator لرتبة.
