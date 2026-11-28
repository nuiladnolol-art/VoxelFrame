using System;
using System.IO;
using System.Text;

namespace Voxalith.Core.Security;

/// <summary>
/// Безопасная работа с путями и именами файлов: защита от path traversal,
/// зарезервированных имён Windows и выхода за пределы целевого каталога.
/// </summary>
public static class PathSafety {
    public static string SanitizeFileComponent(string? raw, string fallback, int maxLength = 48) {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(raw.Length);
        foreach (char c in raw.Trim()) {
            if (c == '/' || c == '\\' || c == ':' || Array.IndexOf(invalid, c) >= 0 || char.IsControl(c)) {
                sb.Append('_');
            } else {
                sb.Append(c);
            }
        }
        string safe = sb.ToString().Trim('.', ' ');
        if (string.IsNullOrWhiteSpace(safe) || safe.Contains("..") ||
            safe.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            safe.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            safe.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            safe.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (safe.Length == 4 &&
             (safe.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
              safe.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
             char.IsDigit(safe[3]))) {
            return fallback;
        }
        return safe.Length > maxLength ? safe.Substring(0, maxLength) : safe;
    }

    public static bool IsPathInsideDirectory(string path, string directory) {
        string fullDir = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);
        return fullPath.StartsWith(fullDir, StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryResolveInsideDirectory(string baseDirectory, string relativeOrEntryPath, out string fullPath) {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(relativeOrEntryPath) || Path.IsPathRooted(relativeOrEntryPath)) {
            return false;
        }
        string candidate = Path.GetFullPath(Path.Combine(baseDirectory, relativeOrEntryPath));
        if (!IsPathInsideDirectory(candidate, baseDirectory)) {
            return false;
        }
        fullPath = candidate;
        return true;
    }
}
