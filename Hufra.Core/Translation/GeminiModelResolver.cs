using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Hufra.Core.Translation
{
    /// <summary>
    /// يسأل Google عن النماذج المتاحة للمفتاح ويختار أحدث نموذج Flash مستقر يدعم generateContent،
    /// بدل تثبيت اسم نموذج قد يُوقف لاحقاً (سبب أخطاء 404).
    /// </summary>
    public static class GeminiModelResolver
    {
        private static string _cachedKey;
        private static string _cachedModel;

        public static string Current => _cachedModel ?? "gemini-flash-latest";

        /// <summary>يرجّع (model, httpStatusOfListCall). model=null لو فشل المفتاح/الاتصال.</summary>
        public static async Task<(string Model, int Status)> ResolveAsync(string key)
        {
            key = key.Trim();
            if (_cachedKey == key && _cachedModel != null) return (_cachedModel, 200);

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
            using var req = new HttpRequestMessage(HttpMethod.Get,
                "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200");
            req.Headers.Add("x-goog-api-key", key);

            var resp = await http.SendAsync(req);
            int status = (int)resp.StatusCode;
            if (!resp.IsSuccessStatusCode) return (null, status);

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            var candidates = doc.RootElement.GetProperty("models").EnumerateArray()
                .Where(m => m.TryGetProperty("supportedGenerationMethods", out var g) &&
                            g.EnumerateArray().Any(x => x.GetString() == "generateContent"))
                .Select(m => m.GetProperty("name").GetString().Replace("models/", ""))
                .ToList();

            string[] bad = { "lite", "image", "tts", "live", "audio", "thinking", "exp", "preview", "latest", "robotics", "computer", "embedding", "learnlm" };
            var rx = new Regex(@"^gemini-(\d+(?:\.\d+)?)-flash(?:-\d+)?$");

            string best = candidates
                .Where(n => rx.IsMatch(n) && !bad.Any(b => n.Contains(b)))
                .OrderByDescending(n => double.Parse(rx.Match(n).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture))
                .ThenBy(n => n.Length)
                .FirstOrDefault();

            best ??= candidates.FirstOrDefault(n => n == "gemini-flash-latest")
                  ?? candidates.FirstOrDefault(n => n.Contains("flash") && !n.Contains("lite") && !n.Contains("image") && !n.Contains("tts"))
                  ?? candidates.FirstOrDefault(n => n.StartsWith("gemini-"));

            if (best != null) { _cachedKey = key; _cachedModel = best; }
            return (best, 200);
        }
    }
}
