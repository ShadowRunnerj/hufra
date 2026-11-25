using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Hufra.Core.Models;
using Hufra.Core.Settings;

namespace Hufra.Core.Translation
{
    /// <summary>
    /// خدمة ترجمة النصوص المستخرجة للعربي.
    ///
    /// عندها مزوّدان:
    /// 1) Claude API (ذكاء اصطناعي) - يُستخدم تلقائياً لو المستخدم حط مفتاح API
    ///    بالإعدادات. جودة أعلى بكثير: يفهم سياق الألعاب، يحافظ على النبرة
    ///    (رسمي/عامي)، ويحافظ على المتغيرات البرمجية ({0}, %s, [item]) بدقة.
    ///    يترجم دفعات (batches) كاملة بطلب واحد بدل نص بنص - أسرع وأدق لأن
    ///    النموذج يشوف كل النصوص مع بعض ويحافظ على تناسق الترجمة بينها.
    /// 2) Google Translate غير الرسمية (احتياطي مجاني) - تُستخدم تلقائياً لو
    ///    ما فيه مفتاح API محفوظ، أو لو فشل الاتصال بـ Claude.
    /// </summary>
    public class TranslationService
    {
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        private readonly ConcurrentDictionary<string, string> _cache = new ConcurrentDictionary<string, string>();

        private const int GoogleMaxConcurrentRequests = 12;
        private const int ClaudeBatchSize = 50;           // نص لكل طلب دفعة
        private const int ClaudeMaxConcurrentBatches = 6; // دفعات متزامنة
        private const string ClaudeModel = "claude-haiku-4-5-20251001"; // سريع ورخيص، مناسب لحجم عمل الترجمة بالجملة

        /// <summary>اسم المزوّد النشط حالياً، لعرضه للمستخدم بصراحة بالواجهة</summary>
        public string ActiveProviderName =>
            HasGemini ? "Gemini (ذكاء اصطناعي) - جودة عالية"
            : HasClaude ? "Claude (ذكاء اصطناعي) - جودة عالية"
            : "Google Translate (مجاني) - جودة أساسية";

        private static bool HasGemini => !string.IsNullOrWhiteSpace(AppSettingsStore.GetGeminiApiKey());
        private static bool HasClaude => !string.IsNullOrWhiteSpace(AppSettingsStore.GetAnthropicApiKey());
        public bool HasAiProvider => HasGemini || HasClaude;

        // Gemini المجاني محدود الطلبات بالدقيقة: دفعات أكبر وتوازي أقل
        private int BatchSize => HasGemini ? 100 : ClaudeBatchSize;
        private int MaxConcurrent => HasGemini ? 3 : ClaudeMaxConcurrentBatches;

        /// <summary>يرسل prompt للنموذج النشط (Gemini أولاً، ثم Claude) ويرجّع النص الخام. يعيد المحاولة عند 429/503.</summary>
        private async Task<string> CallLlmAsync(string prompt, int maxTokens)
        {
            for (int attempt = 0; ; attempt++)
            {
                using var request = HasGemini ? BuildGeminiRequest(prompt, maxTokens) : BuildClaudeRequest(prompt, maxTokens);
                var response = await _http.SendAsync(request);
                string body = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(body);
                    return HasGemini
                        ? doc.RootElement.GetProperty("candidates")[0].GetProperty("content")
                            .GetProperty("parts")[0].GetProperty("text").GetString() ?? ""
                        : doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
                }

                int code = (int)response.StatusCode;
                if ((code == 429 || code == 503 || code == 500) && attempt < 4)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5 * (attempt + 1)));
                    continue;
                }
                throw new InvalidOperationException($"خطأ من خدمة الذكاء الاصطناعي ({code}): {body}");
            }
        }

        private static HttpRequestMessage BuildGeminiRequest(string prompt, int maxTokens)
        {
            var body = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                generationConfig = new
                {
                    temperature = 0.3,
                    maxOutputTokens = Math.Max(maxTokens, 16000),
                    thinkingConfig = new { thinkingBudget = 0 },
                },
            };
            var req = new HttpRequestMessage(HttpMethod.Post,
                $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiModelResolver.Current}:generateContent");
            req.Headers.Add("x-goog-api-key", AppSettingsStore.GetGeminiApiKey());
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return req;
        }

        private static HttpRequestMessage BuildClaudeRequest(string prompt, int maxTokens)
        {
            var body = new
            {
                model = ClaudeModel,
                max_tokens = maxTokens,
                messages = new[] { new { role = "user", content = prompt } },
            };
            var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
            req.Headers.Add("x-api-key", AppSettingsStore.GetAnthropicApiKey());
            req.Headers.Add("anthropic-version", "2023-06-01");
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return req;
        }

        /// <summary>يختبر مفتاح Gemini ويختار النموذج المناسب. يرجّع null لو صالح، أو رسالة الخطأ بالعربي.</summary>
        public static async Task<string> ValidateGeminiKeyAsync(string key)
        {
            try
            {
                var (model, status) = await GeminiModelResolver.ResolveAsync(key);
                if (model != null) return null;
                if (status == 429 || status >= 500) return null; // المفتاح صحيح لكن وصل حد الطلبات مؤقتاً
                if (status == 400 || status == 401 || status == 403)
                    return "المفتاح غير صحيح أو غير مفعّل. تأكد إنك نسخته كامل.";
                if (status == 200)
                    return "المفتاح يعمل لكن ما لقيت نموذج Gemini مناسب لحسابك.";
                return $"تعذّر التحقق من المفتاح (رمز {status}).";
            }
            catch
            {
                return "تعذّر الاتصال بالإنترنت للتحقق من المفتاح.";
            }
        }

        /// <summary>
        /// يترجم مجموعة نصوص كاملة، يختار المزوّد تلقائياً (Claude لو فيه مفتاح، وإلا Google).
        /// يرجّع عدد النصوص اللي فشلت الترجمة نهائياً حتى بعد إعادة المحاولة.
        /// </summary>
        /// <summary>آخر سبب فشل حقيقي (يُعرض للمستخدم بدل الصمت)</summary>
        public string LastError { get; private set; }

        public async Task<int> TranslateAllAsync(
            IReadOnlyList<TextEntry> entries,
            Action<int, int> onProgress = null)
        {
            string apiKey = HasGemini ? AppSettingsStore.GetGeminiApiKey() : AppSettingsStore.GetAnthropicApiKey();
            int failed = -1;
            LastError = null;

            if (HasGemini)
            {
                try { await GeminiModelResolver.ResolveAsync(apiKey); } catch { }
            }

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                try
                {
                    failed = await TranslateAllViaClaudeAsync(entries, apiKey, onProgress);
                    if (failed >= entries.Count && entries.Count > 0) failed = -1; // كلها فشلت: جرّب جوجل
                    else await FixViolationsAsync(entries, apiKey);
                }
                catch (Exception ex)
                {
                    // فشل الاتصال بـ Claude (مفتاح خاطئ، رصيد منتهي، لا إنترنت...) -
                    // نرجع تلقائياً لجوجل المجانية بدل ما نوقف المستخدم بالكامل
                    LastError = ex.Message;
                    failed = -1;
                }
            }

            if (failed < 0)
            {
                failed = await TranslateAllViaGoogleAsync(entries, onProgress);
                await FixViolationsAsync(entries, null);
            }

            ValidateAll(entries);
            return failed;
        }

        /// <summary>يفحص كل الترجمات ويكتب ملاحظة للمشبوه منها ويشيل عنه التحديد التلقائي</summary>
        private static void ValidateAll(IReadOnlyList<TextEntry> entries)
        {
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.TranslatedText)) { e.Note = "لم تُترجم"; continue; }
                string note = PlaceholderGuard.Check(e.OriginalText, e.TranslatedText);
                e.Note = note;
                if (note != null) e.IsApproved = false; // المستخدم يراجعها ويوافق بنفسه
            }
        }

        /// <summary>
        /// النصوص اللي ضاعت متغيراتها أو ما تُرجمت: نعيد ترجمتها لوحدها (نص بنص) مع تشديد.
        /// نحاول مرتين، وللي يبقى مخالف نخلّيه بملاحظة للمراجعة اليدوية.
        /// </summary>
        private async Task FixViolationsAsync(IReadOnlyList<TextEntry> entries, string apiKey)
        {
            var bad = entries
                .Where(e => !string.IsNullOrEmpty(e.TranslatedText) &&
                            PlaceholderGuard.Check(e.OriginalText, e.TranslatedText) != null)
                .Select(e => e.OriginalText).Distinct().Take(300).ToList();
            if (bad.Count == 0) return;

            for (int attempt = 0; attempt < 2 && bad.Count > 0; attempt++)
            {
                using var sem = new SemaphoreSlim(MaxConcurrent);
                var tasks = bad.Select(async text =>
                {
                    await sem.WaitAsync();
                    try
                    {
                        string fixedText = apiKey != null
                            ? (await TranslateBatchViaClaudeAsync(new List<string> { text }, apiKey, "", strict: true))[0]
                            : await TranslateOneViaGoogleAsync(text);
                        if (PlaceholderGuard.Check(text, fixedText) == null) _cache[text] = fixedText;
                    }
                    catch { }
                    finally { sem.Release(); }
                });
                await Task.WhenAll(tasks);

                foreach (var e in entries)
                    if (_cache.TryGetValue(e.OriginalText, out var t) && PlaceholderGuard.Check(e.OriginalText, t) == null)
                        e.TranslatedText = t;

                bad = bad.Where(t => PlaceholderGuard.Check(t, _cache.TryGetValue(t, out var x) ? x : "") != null).ToList();
            }
        }

        // ============================================================
        // المزوّد الأول: Claude API (ذكاء اصطناعي)
        // ============================================================
        private async Task<int> TranslateAllViaClaudeAsync(
            IReadOnlyList<TextEntry> entries, string apiKey, Action<int, int> onProgress)
        {
            int total = entries.Count;
            int done = 0;
            int failed = 0;

            var uniqueTexts = entries.Select(e => e.OriginalText).Distinct().ToList();
            var toTranslate = uniqueTexts.Where(t => !_cache.ContainsKey(t)).ToList();

            // خطوة أولى: نبني مسرد مصطلحات ثابت (أسماء شخصيات/أماكن/عناصر) قبل الترجمة،
            // عشان كل الدفعات المتوازية تستخدم نفس الترجمة لنفس الاسم بدل ما كل دفعة تترجمه لحالها
            string glossary = await BuildGlossaryAsync(toTranslate, apiKey);

            var batches = new List<List<string>>();
            for (int i = 0; i < toTranslate.Count; i += BatchSize)
                batches.Add(toTranslate.Skip(i).Take(BatchSize).ToList());

            using var semaphore = new SemaphoreSlim(MaxConcurrent);
            var batchTasks = batches.Select(async batch =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var translations = await TranslateBatchResilientAsync(batch, apiKey, glossary);
                    for (int i = 0; i < batch.Count; i++)
                        _cache[batch[i]] = translations[i];
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    // الدفعة كاملة فشلت - نسيبها بدون ترجمة، هتُحتسب فاشلة بالحلقة تحت
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(batchTasks);

            foreach (var entry in entries)
            {
                if (_cache.TryGetValue(entry.OriginalText, out var translated))
                {
                    entry.TranslatedText = translated;
                    entry.IsApproved = true;
                }
                else
                {
                    entry.TranslatedText = "";
                    entry.IsApproved = false;
                    Interlocked.Increment(ref failed);
                }

                int current = Interlocked.Increment(ref done);
                onProgress?.Invoke(current, total);
            }

            return failed;
        }

        /// <summary>
        /// يفحص عيّنة من النصوص ويستخرج أسماء الشخصيات/الأماكن/العناصر المتكررة مع
        /// اقتراح ترجمة عربية موحّدة لكل واحد، عشان نحقنها بكل طلبات الترجمة اللاحقة
        /// فتبقى نفس الأسماء مترجمة بنفس الطريقة بكل اللعبة (لا "سارة" هنا و"سارا" هناك).
        /// </summary>
        private async Task<string> BuildGlossaryAsync(List<string> allTexts, string apiKey)
        {
            if (allTexts.Count == 0) return "";

            // عيّنة كافية لالتقاط أغلب الأسماء المتكررة بدون تكلفة عالية على ألعاب فيها آلاف الأسطر
            var sample = allTexts.Take(300).ToList();
            string inputJson = JsonSerializer.Serialize(sample);

            string prompt =
                "Here is a sample of text strings from a video game. Identify any character names, " +
                "place names, faction/organization names, or important item names that appear " +
                "(especially ones that repeat). For each, suggest a single consistent Arabic rendering " +
                "(transliteration or translation, whichever fits a game world better). " +
                "Respond with ONLY a JSON object mapping each English name to its Arabic rendering, " +
                "e.g. {\"Eldrin\": \"إلدرين\", \"Ironhold Keep\": \"قلعة أيرونهولد\"}. " +
                "If you find no clear proper nouns worth standardizing, respond with an empty JSON object {}. " +
                "No markdown, no commentary, only the JSON object.\n\nText sample:\n" + inputJson;

            try
            {
                string text = await CallLlmAsync(prompt, 2000);
                if (string.IsNullOrWhiteSpace(text)) text = "{}";

                text = CleanJsonFences(text);
                using var glossaryDoc = JsonDocument.Parse(text); // نتأكد إنه JSON صالح قبل ما نستخدمه
                return glossaryDoc.RootElement.ValueKind == JsonValueKind.Object && glossaryDoc.RootElement.EnumerateObject().Any()
                    ? text
                    : "";
            }
            catch
            {
                return ""; // فشل بناء المسرد لا يوقف الترجمة، بس نخسر ميزة الاتساق لهالمرة
            }
        }

        private static string CleanJsonFences(string text)
        {
            text = text.Trim();
            if (!text.StartsWith("```")) return text;

            int firstNewline = text.IndexOf('\n');
            text = firstNewline >= 0 ? text.Substring(firstNewline + 1) : text;
            int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0) text = text.Substring(0, lastFence);
            return text.Trim();
        }

        /// <summary>لو الدفعة فشلت (عدد غير مطابق مثلاً) نقسمها نصفين ونعيد المحاولة بدل ما نخسرها كاملة</summary>
        private async Task<List<string>> TranslateBatchResilientAsync(List<string> batch, string apiKey, string glossary)
        {
            try
            {
                return await TranslateBatchViaClaudeAsync(batch, apiKey, glossary);
            }
            catch when (batch.Count > 1)
            {
                int mid = batch.Count / 2;
                var left = await TranslateBatchResilientAsync(batch.Take(mid).ToList(), apiKey, glossary);
                var right = await TranslateBatchResilientAsync(batch.Skip(mid).ToList(), apiKey, glossary);
                return left.Concat(right).ToList();
            }
        }

        private async Task<List<string>> TranslateBatchViaClaudeAsync(List<string> texts, string apiKey, string glossary, bool strict = false)
        {
            string inputJson = JsonSerializer.Serialize(texts);

            string glossarySection = string.IsNullOrEmpty(glossary)
                ? ""
                : "\n\nUse this glossary for consistent naming - if any of these terms appear in the " +
                  "input, use EXACTLY the Arabic rendering given here:\n" + glossary + "\n";

            string strictSection = strict
                ? "\n\nCRITICAL: the previous attempt broke or dropped placeholders / left text untranslated. " +
                  "Every placeholder ({..}, %s, %d, [..], <tags>, \\n, line breaks) MUST appear in your output " +
                  "exactly as in the input, the same number of times. Write the Arabic text AROUND them. " +
                  "The output must contain real Arabic text.\n"
                : "";

            string prompt =
                "You are a professional game localization specialist translating from English (or auto-" +
                "detected source language) into Modern Standard Arabic (Fusha) - simple, clear, and natural, " +
                "matching the tone typically used in localized video games.\n\n" +
                "Rules:\n" +
                "1. Translate every player-facing string naturally - dialogue should feel natural and " +
                "engaging, not stiff literal machine translation. UI/menu labels should be short and clear, " +
                "fitting for buttons and menus. Quest/mission text should be direct and easy to understand.\n" +
                "2. NEVER translate or alter placeholders and variables - keep them exactly as-is, in the " +
                "same position: {player_name}, {0}, {KEY}, %s, %d, \\n, <color=...>, </color>, [PLAYER], " +
                "[item], or any similar bracketed/braced/percent-style placeholder.\n" +
                "3. Do not translate strings that are clearly code, file paths, or identifiers rather than " +
                "player-facing text - but every string given to you here has already been pre-filtered as " +
                "likely display text, so translate it unless it is obviously not natural language.\n" +
                "4. Character, place, weapon, and item names may be transliterated or translated, whichever " +
                "fits the game world better - use your judgement for what reads naturally in Arabic." +
                glossarySection +
                "5. Preserve punctuation, quotation style, and formatting symbols exactly where they don't " +
                "conflict with Arabic grammar.\n" +
                "6. Never add or remove entries, and never merge or split entries - the output array must " +
                "have exactly the same length and order as the input array, one translation per input string.\n" +
                "7. Do not add commentary, explanations, or notes - only the translations themselves.\n" +
                "8. Placeholders must survive the Arabic right-to-left flow: keep each placeholder token " +
                "character-for-character identical; never translate words inside <tags> attributes or braces.\n" +
                "9. Use Arabic punctuation naturally (، ؟ ؛) but keep game symbols and numbers as given. " +
                "Keep ALL-CAPS short UI labels short. Single words (Attack, Save, Items) are menu labels - " +
                "use the standard game-menu term (هجوم، حفظ، العناصر).\n" +
                "10. Never leave an English sentence untranslated unless it is a proper name or code.\n" +
                strictSection + "\n" +
                "Respond with ONLY a JSON array of strings (same length and order as the input), containing " +
                "the Arabic translations - no markdown, no code fences, nothing else.\n\n" +
                "Input:\n" + inputJson;

            string text = await CallLlmAsync(prompt, 8000);
            text = CleanJsonFences(text);

            var result = JsonSerializer.Deserialize<List<string>>(text);
            if (result == null || result.Count != texts.Count)
                throw new InvalidOperationException("رد الذكاء الاصطناعي ما طابق عدد النصوص المطلوبة بالدفعة.");

            return result;
        }

        // ============================================================
        // المزوّد الاحتياطي: Google Translate غير الرسمية (مجاني)
        // ============================================================
        private async Task<string> TranslateOneViaGoogleAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            string encoded = Uri.EscapeDataString(text);
            string url = $"https://translate.googleapis.com/translate_a/single" +
                         $"?client=gtx&sl=auto&tl=ar&dt=t&q={encoded}";

            string response;
            try
            {
                response = await _http.GetStringAsync(url);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "تعذّر الاتصال بخدمة الترجمة. تأكد من اتصالك بالإنترنت.", ex);
            }

            using var doc = JsonDocument.Parse(response);
            var segments = doc.RootElement[0];

            var result = new StringBuilder();
            foreach (var segment in segments.EnumerateArray())
            {
                if (segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
                    result.Append(segment[0].GetString());
            }

            return result.ToString();
        }

        private async Task<string> TranslateViaGoogleWithRetryAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            if (_cache.TryGetValue(text, out var cached)) return cached;

            Exception lastError = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    var translated = await TranslateOneViaGoogleAsync(text);
                    _cache[text] = translated;
                    return translated;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt == 0) await Task.Delay(400);
                }
            }

            throw lastError ?? new InvalidOperationException("فشلت الترجمة لسبب غير معروف.");
        }

        private async Task<int> TranslateAllViaGoogleAsync(
            IReadOnlyList<TextEntry> entries, Action<int, int> onProgress)
        {
            int total = entries.Count;
            int done = 0;
            int failed = 0;
            using var semaphore = new SemaphoreSlim(GoogleMaxConcurrentRequests);

            var tasks = entries.Select(async entry =>
            {
                await semaphore.WaitAsync();
                try
                {
                    entry.TranslatedText = await TranslateViaGoogleWithRetryAsync(entry.OriginalText);
                    entry.IsApproved = true;
                }
                catch (Exception ex)
                {
                    LastError = ex.Message;
                    entry.TranslatedText = "";
                    entry.IsApproved = false;
                    Interlocked.Increment(ref failed);
                }
                finally
                {
                    semaphore.Release();
                    int current = Interlocked.Increment(ref done);
                    onProgress?.Invoke(current, total);
                }
            });

            await Task.WhenAll(tasks);
            return failed;
        }
    }
}
