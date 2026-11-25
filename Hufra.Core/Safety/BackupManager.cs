using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Hufra.Core.Safety
{
    public class BackupEntry
    {
        public string Rel { get; set; }
        public string Sha256 { get; set; }
    }

    public class BackupManifest
    {
        public string GameFolder { get; set; }
        public DateTime Created { get; set; } = DateTime.Now;
        public List<BackupEntry> Files { get; set; } = new();
        /// <summary>ملفات أنشأها حُفرة (مثل خطوط جديدة) تُحذف عند الاستعادة</summary>
        public List<string> CreatedFiles { get; set; } = new();
    }

    public class RestoreResult
    {
        public int Restored { get; set; }
        public int Deleted { get; set; }
        public List<string> Failed { get; } = new();
        public bool HadBackup { get; set; }
    }

    /// <summary>
    /// نسخ احتياطي مركزي: قبل أي تعديل نحفظ الأصل في %AppData%\Hufra\backups (خارج مجلد اللعبة)
    /// مع بصمة SHA-256، وأول نسخة هي المرجع دائماً (ما تُستبدل لو طبّقت أكثر من مرة).
    /// الاستعادة ترجّع الملفات الأصلية وتحذف اللي أنشأناه.
    /// </summary>
    public static class BackupManager
    {
        private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

        private static string Root => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hufra", "backups");

        public static string BackupDir(string gameFolder)
        {
            string full = Path.GetFullPath(gameFolder).TrimEnd('\\', '/');
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToLowerInvariant()))).Substring(0, 8);
            string name = new string(Path.GetFileName(full).Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
            return Path.Combine(Root, name + "_" + hash);
        }

        private static string ManifestPath(string gameFolder) => Path.Combine(BackupDir(gameFolder), "manifest.json");

        public static bool HasBackup(string gameFolder) => File.Exists(ManifestPath(gameFolder));

        public static BackupManifest Load(string gameFolder)
        {
            try
            {
                if (File.Exists(ManifestPath(gameFolder)))
                    return JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(ManifestPath(gameFolder))) ?? New(gameFolder);
            }
            catch { }
            return New(gameFolder);
        }

        private static BackupManifest New(string gameFolder) => new() { GameFolder = Path.GetFullPath(gameFolder) };

        private static void Save(string gameFolder, BackupManifest m)
        {
            Directory.CreateDirectory(BackupDir(gameFolder));
            File.WriteAllText(ManifestPath(gameFolder), JsonSerializer.Serialize(m, Json), new UTF8Encoding(false));
        }

        public static string Sha256(string path)
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs));
        }

        /// <summary>يحفظ نسخة أصلية لكل ملف موجود (مرة وحدة فقط). يرمي استثناء لو فشل نسخ ملف، عشان ما نعدّل بدون حماية.</summary>
        public static void BackupFiles(string gameFolder, IEnumerable<string> relativePaths)
        {
            var manifest = Load(gameFolder);
            string filesDir = Path.Combine(BackupDir(gameFolder), "files");

            foreach (var rel in relativePaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string src = Path.Combine(gameFolder, rel);
                if (!File.Exists(src)) continue;
                if (manifest.Files.Any(f => f.Rel.Equals(rel, StringComparison.OrdinalIgnoreCase))) continue;
                if (manifest.CreatedFiles.Any(f => f.Equals(rel, StringComparison.OrdinalIgnoreCase))) continue;

                string dst = Path.Combine(filesDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst));
                File.Copy(src, dst, true);
                manifest.Files.Add(new BackupEntry { Rel = rel, Sha256 = Sha256(src) });
            }
            Save(gameFolder, manifest);
        }

        public static void RecordCreated(string gameFolder, string rel)
        {
            var manifest = Load(gameFolder);
            if (!manifest.CreatedFiles.Contains(rel, StringComparer.OrdinalIgnoreCase)) manifest.CreatedFiles.Add(rel);
            Save(gameFolder, manifest);
        }

        public static void RecordCreatedMany(string gameFolder, IEnumerable<string> rels)
        {
            var manifest = Load(gameFolder);
            foreach (var rel in rels)
                if (!manifest.CreatedFiles.Contains(rel, StringComparer.OrdinalIgnoreCase)) manifest.CreatedFiles.Add(rel);
            Save(gameFolder, manifest);
        }

        public static RestoreResult Restore(string gameFolder)
        {
            var result = new RestoreResult { HadBackup = HasBackup(gameFolder) };
            if (!result.HadBackup) return result;

            var manifest = Load(gameFolder);
            string filesDir = Path.Combine(BackupDir(gameFolder), "files");

            foreach (var f in manifest.Files)
            {
                try
                {
                    string src = Path.Combine(filesDir, f.Rel);
                    string dst = Path.Combine(gameFolder, f.Rel);
                    if (!File.Exists(src)) { result.Failed.Add(f.Rel + " (النسخة الاحتياطية مفقودة)"); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    File.Copy(src, dst, true);
                    if (Sha256(dst) == f.Sha256) result.Restored++;
                    else result.Failed.Add(f.Rel + " (البصمة لا تطابق بعد الاستعادة)");
                }
                catch (Exception ex) { result.Failed.Add($"{f.Rel} ({ex.Message})"); }
            }

            foreach (var rel in manifest.CreatedFiles)
            {
                try
                {
                    string p = Path.Combine(gameFolder, rel);
                    if (File.Exists(p)) { File.Delete(p); result.Deleted++; }
                }
                catch (Exception ex) { result.Failed.Add($"{rel} ({ex.Message})"); }
            }

            if (result.Failed.Count == 0)
            {
                try { Directory.Delete(BackupDir(gameFolder), true); } catch { }
            }
            return result;
        }
    }
}
