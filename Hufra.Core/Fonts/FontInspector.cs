using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Hufra.Core.Fonts
{
    public class FontCoverage
    {
        public bool Readable { get; set; }
        public bool Arabic { get; set; }            // الحروف العربية الأساسية (U+0627, U+0645 ...)
        public bool PresentationForms { get; set; } // أشكال العرض (U+FE8D, U+FEE1 ...) - تلزم لو شكّلنا النص بأنفسنا
    }

    /// <summary>يقرأ جدول cmap من ملف TTF/OTF ويتأكد فعلياً إن الخط يحتوي الحروف العربية (بدون الاعتماد على الاسم).</summary>
    public static class FontInspector
    {
        private static readonly int[] Basic = { 0x0627, 0x0628, 0x0645, 0x064A };
        private static readonly int[] Forms = { 0xFE8D, 0xFEE1, 0xFEF3, 0xFE8E };

        public static FontCoverage Inspect(string path)
        {
            try
            {
                byte[] d = File.ReadAllBytes(path);
                if (d.Length < 12) return new FontCoverage();
                string magic = Encoding.ASCII.GetString(d, 0, 4);
                if (magic != "OTTO" && magic != "true" && U32(d, 0) != 0x00010000) return new FontCoverage(); // يشمل ttcf

                int numTables = U16(d, 4);
                int cmap = -1;
                for (int i = 0; i < numTables; i++)
                {
                    int rec = 12 + i * 16;
                    if (rec + 16 > d.Length) break;
                    if (Encoding.ASCII.GetString(d, rec, 4) == "cmap") { cmap = (int)U32(d, rec + 8); break; }
                }
                if (cmap < 0 || cmap + 4 > d.Length) return new FontCoverage();

                var subtables = new List<int>();
                int n = U16(d, cmap + 2);
                for (int i = 0; i < n; i++)
                {
                    int rec = cmap + 4 + i * 8;
                    int platform = U16(d, rec), enc = U16(d, rec + 2);
                    int off = cmap + (int)U32(d, rec + 4);
                    if (platform == 0 || (platform == 3 && (enc == 1 || enc == 10))) subtables.Add(off);
                }

                bool Has(int cp) { foreach (var st in subtables) if (HasGlyph(d, st, cp)) return true; return false; }
                bool all(int[] cps) { foreach (var c in cps) if (!Has(c)) return false; return true; }

                return new FontCoverage { Readable = true, Arabic = all(Basic), PresentationForms = all(Forms) };
            }
            catch
            {
                return new FontCoverage();
            }
        }

        private static int U16(byte[] d, int o) => (d[o] << 8) | d[o + 1];
        private static uint U32(byte[] d, int o) => (uint)((d[o] << 24) | (d[o + 1] << 16) | (d[o + 2] << 8) | d[o + 3]);

        private static bool HasGlyph(byte[] d, int st, int cp)
        {
            int format = U16(d, st);
            if (format == 4)
            {
                int segX2 = U16(d, st + 6);
                int endPos = st + 14, startPos = endPos + segX2 + 2, deltaPos = startPos + segX2, rangePos = deltaPos + segX2;
                for (int i = 0; i < segX2; i += 2)
                {
                    int end = U16(d, endPos + i);
                    if (cp > end) continue;
                    int start = U16(d, startPos + i);
                    if (cp < start) return false;
                    int delta = (short)U16(d, deltaPos + i);
                    int range = U16(d, rangePos + i);
                    if (range == 0) return ((cp + delta) & 0xFFFF) != 0;
                    int gp = rangePos + i + range + 2 * (cp - start);
                    if (gp + 1 >= d.Length) return false;
                    int g = U16(d, gp);
                    return g != 0;
                }
                return false;
            }
            if (format == 12)
            {
                uint groups = U32(d, st + 12);
                for (uint i = 0; i < groups; i++)
                {
                    int g = st + 16 + (int)i * 12;
                    if (g + 12 > d.Length) break;
                    uint s = U32(d, g), e = U32(d, g + 4);
                    if (cp >= s && cp <= e) return true;
                }
            }
            return false;
        }
    }
}
