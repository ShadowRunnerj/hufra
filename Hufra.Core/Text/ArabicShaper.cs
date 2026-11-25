using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Hufra.Core.Translation;

namespace Hufra.Core.Text
{
    /// <summary>
    /// تجهيز النص العربي للألعاب اللي ما تدعم العربية/RTL (مثل ألعاب Paradox):
    /// 1) تشكيل الحروف (ربط: أول/وسط/آخر/منفصل) عبر Arabic Presentation Forms-B
    /// 2) عكس ترتيب الكلمات والحروف ليظهر صحيحاً داخل محرك يرسم من اليسار لليمين.
    /// المتغيرات والوسوم (مثل [Scope.GetName] و $var$ و #bold ... #!) تبقى سليمة غير معكوسة.
    /// ملاحظة: اللعبة لازم يكون خطها يحتوي أشكال العرض العربية (FE70–FEFF) وإلا تظهر مربعات.
    /// </summary>
    public static class ArabicShaper
    {
        // [منفصل, نهاية, بداية, وسط]  (0 = غير موجود)
        private static readonly Dictionary<char, char[]> Forms = Build();

        private static Dictionary<char, char[]> Build()
        {
            var d = new Dictionary<char, char[]>();
            void Two(char c, int iso, int fin) => d[c] = new[] { (char)iso, (char)fin, '\0', '\0' };
            void Four(char c, int iso) => d[c] = new[] { (char)iso, (char)(iso + 1), (char)(iso + 2), (char)(iso + 3) };

            d['ء'] = new[] { 'ﺀ', '\0', '\0', '\0' };
            Two('آ', 0xFE81, 0xFE82); Two('أ', 0xFE83, 0xFE84); Two('ؤ', 0xFE85, 0xFE86);
            Two('إ', 0xFE87, 0xFE88); Four('ئ', 0xFE89); Two('ا', 0xFE8D, 0xFE8E);
            Four('ب', 0xFE8F); Two('ة', 0xFE93, 0xFE94); Four('ت', 0xFE95); Four('ث', 0xFE99);
            Four('ج', 0xFE9D); Four('ح', 0xFEA1); Four('خ', 0xFEA5); Two('د', 0xFEA9, 0xFEAA);
            Two('ذ', 0xFEAB, 0xFEAC); Two('ر', 0xFEAD, 0xFEAE); Two('ز', 0xFEAF, 0xFEB0);
            Four('س', 0xFEB1); Four('ش', 0xFEB5); Four('ص', 0xFEB9); Four('ض', 0xFEBD);
            Four('ط', 0xFEC1); Four('ظ', 0xFEC5); Four('ع', 0xFEC9); Four('غ', 0xFECD);
            Four('ف', 0xFED1); Four('ق', 0xFED5); Four('ك', 0xFED9); Four('ل', 0xFEDD);
            Four('م', 0xFEE1); Four('ن', 0xFEE5); Four('ه', 0xFEE9); Two('و', 0xFEED, 0xFEEE);
            Two('ى', 0xFEEF, 0xFEF0); Four('ي', 0xFEF1);
            return d;
        }

        private static bool IsArabicLetter(char c) => Forms.ContainsKey(c);
        private static bool IsDiacritic(char c) => c >= 'ً' && c <= 'ْ';
        private static bool JoinsNext(char c) => Forms.TryGetValue(c, out var f) && f[2] != '\0'; // دual-joining
        private static bool JoinsPrev(char c) => Forms.TryGetValue(c, out var f) && f[1] != '\0';

        public static bool ContainsArabic(string s) => s.Any(c => c >= '؀' && c <= 'ۿ');

        // ---- تشكيل كلمة عربية (ترتيب منطقي -> أشكال عرض، لسا غير معكوس) ----
        private static string ShapeRun(string run)
        {
            var chars = run.Where(c => !IsDiacritic(c) && c != 'ـ').ToArray();
            var sb = new StringBuilder();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool prevJoin = i > 0 && IsArabicLetter(chars[i - 1]) && JoinsNext(chars[i - 1]);

                if (c == 'ل' && i + 1 < chars.Length)
                {
                    char n = chars[i + 1];
                    int iso = n switch { 'آ' => 0xFEF5, 'أ' => 0xFEF7, 'إ' => 0xFEF9, 'ا' => 0xFEFB, _ => 0 };
                    if (iso != 0)
                    {
                        sb.Append((char)(prevJoin ? iso + 1 : iso));
                        i++;
                        continue;
                    }
                }

                if (!Forms.TryGetValue(c, out var f)) { sb.Append(c); continue; }
                bool nextJoin = JoinsNext(c) && i + 1 < chars.Length && IsArabicLetter(chars[i + 1]) && JoinsPrev(chars[i + 1]);

                char shaped = prevJoin && nextJoin ? f[3]
                            : prevJoin ? f[1]
                            : nextJoin ? f[2]
                            : f[0];
                if (shaped == '\0') shaped = prevJoin && f[1] != '\0' ? f[1] : f[0];
                sb.Append(shaped);
            }
            return sb.ToString();
        }

        private static string Mirror(string s) =>
            new string(s.Select(c => c switch { '(' => ')', ')' => '(', '<' => '>', '>' => '<', _ => c }).ToArray());

        // كلمة واحدة: نقسمها لمقاطع (عربي / غير عربي)، نشكّل العربي ونعكسه، ونعكس ترتيب المقاطع
        private static string ShapeWord(string word)
        {
            var runs = new List<string>();
            var cur = new StringBuilder();
            bool? arabic = null;
            foreach (char c in word)
            {
                bool isAr = (c >= '؀' && c <= 'ۿ') || (c >= 'ﹰ' && c <= '﻿');
                bool neutralJoin = c == 'ـ';
                if (arabic != null && isAr != arabic && !neutralJoin)
                {
                    runs.Add((arabic.Value ? "A" : "L") + cur);
                    cur.Clear();
                }
                arabic = isAr || neutralJoin ? true : isAr;
                cur.Append(c);
            }
            if (cur.Length > 0) runs.Add((arabic == true ? "A" : "L") + cur);

            var outRuns = runs.Select(r =>
            {
                string body = r.Substring(1);
                if (r[0] == 'A') return new string(ShapeRun(body).Reverse().ToArray());
                return Mirror(body);
            }).ToList();
            outRuns.Reverse();
            return string.Concat(outRuns);
        }

        private sealed class Atom { public string Text; public bool IsToken; }

        // سطر واحد (بدون \n)
        private static string ShapeLine(string line)
        {
            var atoms = new List<Atom>();
            int pos = 0;
            foreach (Match m in PlaceholderGuard.TokenRegex.Matches(line))
            {
                if (m.Index > pos) SplitText(line.Substring(pos, m.Index - pos), atoms);
                atoms.Add(new Atom { Text = m.Value, IsToken = true });
                pos = m.Index + m.Length;
            }
            if (pos < line.Length) SplitText(line.Substring(pos), atoms);

            // تبديل أدوار وسوم التنسيق #key ... #! قبل العكس حتى تبقى سليمة بعده
            var stack = new Stack<int>();
            var openerOf = new Dictionary<int, int>();
            for (int i = 0; i < atoms.Count; i++)
            {
                if (!atoms[i].IsToken) continue;
                string t = atoms[i].Text;
                if (t.Length > 1 && t[0] == '#' && char.IsLetter(t[1])) stack.Push(i);
                else if (t == "#!" && stack.Count > 0) openerOf[i] = stack.Pop();
            }
            foreach (var kv in openerOf)
            {
                string opener = atoms[kv.Value].Text;
                atoms[kv.Value].Text = "#!";
                atoms[kv.Key].Text = opener + " ";
            }
            foreach (int idx in stack) atoms[idx].Text = ""; // وسم مفتوح بدون إغلاق: نحذفه بدل ما يسرّب التنسيق

            atoms.Reverse();
            return string.Concat(atoms.Select(a => a.IsToken ? a.Text : ShapeWord(a.Text)));
        }

        private static void SplitText(string text, List<Atom> atoms)
        {
            var sb = new StringBuilder();
            foreach (char c in text)
            {
                if (c == ' ')
                {
                    if (sb.Length > 0) { atoms.Add(new Atom { Text = sb.ToString() }); sb.Clear(); }
                    atoms.Add(new Atom { Text = " ", IsToken = true }); // المسافة ثابتة (لا تُشكّل)
                }
                else sb.Append(c);
            }
            if (sb.Length > 0) atoms.Add(new Atom { Text = sb.ToString() });
        }

        /// <summary>النص العربي بعد التشكيل والعكس. نصوص بلا عربي ترجع كما هي.</summary>
        public static string PrepareForLtrEngine(string text)
        {
            if (string.IsNullOrEmpty(text) || !ContainsArabic(text)) return text;

            // الأسطر (\n النصية أو الحقيقية) تُعالج كل سطر لوحده بنفس الترتيب
            var parts = Regex.Split(text, @"(\\n|\r?\n)");
            for (int i = 0; i < parts.Length; i += 2)
                parts[i] = ShapeLine(parts[i]);
            return string.Concat(parts);
        }
    }
}
