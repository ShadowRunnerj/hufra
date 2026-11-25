using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Hufra.Core.Translation
{
    /// <summary>
    /// يتحقق إن الترجمة حافظت على كل المتغيرات البرمجية (placeholders) كما هي بالنص الأصلي،
    /// ويكتشف الترجمات اللي ما تُرجمت فعلياً. يُستخدم بعد كل ترجمة (Claude أو Google).
    /// </summary>
    public static class PlaceholderGuard
    {
        public static readonly Regex TokenRegex = new Regex(
            @"\{[^{}\s]{1,40}\}|\$[\w.|:\-]{1,80}\$|%\d*\$?[sdfi]|%\d+\$?[sdfi]?|\[[^\[\]\r\n]{1,100}\]|</?[A-Za-z][^<>]*>|#!|#[A-Za-z_][\w;]{0,24}(?=[ \r\n]|$)|£\w+£|§[A-Za-z0-9!]|\\n|\\t|\\r|\r?\n",
            RegexOptions.Compiled);

        private static readonly Regex ArabicLetter = new Regex(@"[؀-ۿ]", RegexOptions.Compiled);
        private static readonly Regex LatinWord = new Regex(@"[A-Za-z]{3,}", RegexOptions.Compiled);

        public static List<string> Tokens(string s) =>
            string.IsNullOrEmpty(s)
                ? new List<string>()
                : TokenRegex.Matches(s).Select(m => m.Value.Replace("\r\n", "\n")).ToList();

        /// <summary>true لو كل متغيرات الأصل موجودة بالترجمة بنفس العدد (الترتيب قد يتغير بسبب العربية)</summary>
        public static bool PlaceholdersPreserved(string original, string translated)
        {
            var a = Tokens(original).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            var b = Tokens(translated).GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
            if (a.Count != b.Count) return false;
            return a.All(kv => b.TryGetValue(kv.Key, out var c) && c == kv.Value);
        }

        /// <summary>يرجّع ملاحظة مراجعة (أو null لو الترجمة سليمة)</summary>
        public static string Check(string original, string translated)
        {
            if (string.IsNullOrWhiteSpace(translated)) return "ترجمة فارغة";
            if (!PlaceholdersPreserved(original, translated)) return "⚠ المتغيرات تغيّرت - راجعها";

            // الأصل فيه كلمات إنجليزية والترجمة بلا أي حرف عربي = غالباً ما تُرجم
            string stripped = TokenRegex.Replace(original, " ");
            if (LatinWord.IsMatch(stripped) && !ArabicLetter.IsMatch(translated))
                return "⚠ يبدو أنه لم يُترجم";

            // طول مبالغ فيه (غالباً هلوسة أو تكرار)
            if (original.Length >= 5 && translated.Length > original.Length * 4 + 20)
                return "⚠ الترجمة أطول من المتوقع";

            return null;
        }
    }
}
