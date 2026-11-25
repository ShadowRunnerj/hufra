using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Hufra.Core.Steam
{
    /// <summary>
    /// يكتشف مسار تثبيت Steam من الريجستري، يقرأ كل "مكتبات" الألعاب
    /// (libraryfolders.vdf يدعم عدة أقراص/مسارات)، ثم يقرأ ملفات
    /// appmanifest_*.acf لكل لعبة مثبتة (اسمها ومجلدها).
    ///
    /// ملاحظة: نستخدم Regex بسيط لقراءة صيغة VDF بدل مكتبة خارجية،
    /// كافٍ للحقول اللي نحتاجها (name/installdir/path). لو احتجنا لاحقاً
    /// قراءة أعمق (مثل قوائم تشغيل أو DLC) يفضّل التبديل لمكتبة VDF كاملة.
    /// </summary>
    public static class SteamLibraryScanner
    {
        public static string GetSteamInstallPath()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var path = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(path))
                    return path.Replace('/', Path.DirectorySeparatorChar);
            }
            catch { /* الريجستري غير متاح أو Steam غير مثبت */ }

            // مسار افتراضي شائع كخطة بديلة
            string fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            return Directory.Exists(fallback) ? fallback : null;
        }

        public static List<string> GetLibraryFolders()
        {
            var result = new List<string>();
            string steamPath = GetSteamInstallPath();
            if (steamPath == null) return result;

            result.Add(steamPath); // مكتبة Steam الأساسية نفسها

            string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath)) return result;

            string content;
            try { content = File.ReadAllText(vdfPath); }
            catch { return result; }

            // نلقط كل سطر فيه "path"  "C:\\Some\\Path"
            foreach (Match m in Regex.Matches(content, "\"path\"\\s*\"([^\"]+)\""))
            {
                string libPath = m.Groups[1].Value.Replace("\\\\", "\\");
                if (!result.Contains(libPath, StringComparer.OrdinalIgnoreCase))
                    result.Add(libPath);
            }

            return result;
        }

        public static List<SteamGame> ScanInstalledGames()
        {
            var games = new List<SteamGame>();

            foreach (var library in GetLibraryFolders())
            {
                string steamappsDir = Path.Combine(library, "steamapps");
                if (!Directory.Exists(steamappsDir)) continue;

                foreach (var manifestFile in Directory.GetFiles(steamappsDir, "appmanifest_*.acf"))
                {
                    var game = ParseManifest(manifestFile, Path.Combine(steamappsDir, "common"));
                    if (game != null) games.Add(game);
                }
            }

            return games;
        }

        private static SteamGame ParseManifest(string manifestPath, string commonDir)
        {
            string content;
            try { content = File.ReadAllText(manifestPath); }
            catch { return null; }

            string appId = ExtractField(content, "appid");
            string name = ExtractField(content, "name");
            string installDir = ExtractField(content, "installdir");

            if (string.IsNullOrEmpty(installDir)) return null;

            string fullPath = Path.Combine(commonDir, installDir);
            if (!Directory.Exists(fullPath)) return null;

            return new SteamGame
            {
                AppId = appId,
                Name = string.IsNullOrEmpty(name) ? installDir : name,
                InstallFolderPath = fullPath,
            };
        }

        private static string ExtractField(string vdfContent, string key)
        {
            var m = Regex.Match(vdfContent, $"\"{key}\"\\s*\"([^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }
    }
}
