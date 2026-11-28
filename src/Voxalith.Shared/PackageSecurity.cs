using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Voxalith.Shared;

/// <summary>
/// Общие проверки безопасности релизных пакетов (SemVer, теги/репозитории GitHub,
/// распаковка ZIP с защитой от Zip Slip и проверка SHA256SUMS.txt).
/// Используется в Launcher, Installer и модульных проверках SelfCheck.
/// </summary>
internal static class PackageSecurity {
    public static (Version Core, string Pre) ParseSemVer(string? tag) {
        if (string.IsNullOrWhiteSpace(tag)) return (new Version(0, 0, 0), "");
        string clean = tag.Trim().TrimStart('v', 'V');
        int dash = clean.IndexOf('-');
        string coreStr = dash >= 0 ? clean[..dash] : clean;
        string pre = dash >= 0 ? clean[(dash + 1)..] : "";
        var parts = coreStr.Split('.');
        int major = parts.Length > 0 && int.TryParse(parts[0], out var m) ? m : 0;
        int minor = parts.Length > 1 && int.TryParse(parts[1], out var n) ? n : 0;
        int build = parts.Length > 2 && int.TryParse(parts[2], out var b) ? b : 0;
        return (new Version(major, minor, build), pre);
    }

    public static int CompareVersions(string? a, string? b) {
        var pa = ParseSemVer(a);
        var pb = ParseSemVer(b);
        int cmp = pa.Core.CompareTo(pb.Core);
        if (cmp != 0) return cmp;
        if (string.IsNullOrEmpty(pa.Pre) && string.IsNullOrEmpty(pb.Pre)) return 0;
        if (string.IsNullOrEmpty(pa.Pre)) return 1;
        if (string.IsNullOrEmpty(pb.Pre)) return -1;
        return string.Compare(pa.Pre, pb.Pre, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsNewerVersion(string? latestTag, string? currentVer) {
        try {
            return CompareVersions(latestTag, currentVer) > 0;
        } catch {
            return false;
        }
    }

    public static string SanitizeTagComponent(string? tag) {
        if (string.IsNullOrWhiteSpace(tag)) return "";
        string t = tag.Trim();
        if (t.Contains("..") || t.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return "";
        foreach (char c in t) {
            if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_') return "";
        }
        return t;
    }

    public static string SanitizeGitHubRepo(string? repo, string defaultRepo = "nuiladnolol-art/Voxalith") {
        if (string.IsNullOrWhiteSpace(repo)) return defaultRepo;
        string trimmed = repo.Trim().Trim('/');
        var parts = trimmed.Split('/');
        if (parts.Length != 2) return defaultRepo;
        foreach (var part in parts) {
            if (part.Length == 0 || part.Length > 100 || part.Contains("..")) return defaultRepo;
            foreach (char c in part) {
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_') return defaultRepo;
            }
        }
        return $"{parts[0]}/{parts[1]}";
    }

    public static string? ParseExpectedSha256(string sumsText, string expectedFileName) {
        if (string.IsNullOrWhiteSpace(sumsText) || string.IsNullOrWhiteSpace(expectedFileName)) return null;
        foreach (var rawLine in sumsText.Split('\n')) {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[^1].TrimStart('*').Equals(expectedFileName, StringComparison.OrdinalIgnoreCase)) {
                return parts[0].Trim().ToLowerInvariant();
            }
        }
        return null;
    }

    public static string ComputeFileSha256Hex(string filePath) {
        using var fs = File.OpenRead(filePath);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    public static void ExtractZipSafe(string zipPath, string targetDir, Action<int>? onProgress = null) {
        using var archive = ZipFile.OpenRead(zipPath);
        ExtractZipSafe(archive, targetDir, onProgress);
    }

    public static void ExtractZipSafe(ZipArchive archive, string targetDir, Action<int>? onProgress = null) {
        string fullTarget = Path.GetFullPath(targetDir)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(targetDir);

        int total = archive.Entries.Count;
        int current = 0;
        foreach (var entry in archive.Entries) {
            if (Path.IsPathRooted(entry.FullName)) {
                throw new InvalidDataException($"Обнаружен небезопасный путь в архиве: {entry.FullName}");
            }
            string dest = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
            if (!dest.StartsWith(fullTarget, StringComparison.OrdinalIgnoreCase)) {
                throw new InvalidDataException($"Обнаружен небезопасный путь в архиве: {entry.FullName}");
            }
            if (string.IsNullOrEmpty(entry.Name)) {
                Directory.CreateDirectory(dest);
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
            current++;
            onProgress?.Invoke(Math.Clamp(10 + (current * 80) / Math.Max(1, total), 10, 95));
        }
    }
}
