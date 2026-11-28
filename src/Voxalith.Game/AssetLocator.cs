using System;
using System.Collections.Generic;
using System.IO;

namespace Voxalith.Game;

/// <summary>
/// Единый поиск каталогов и файлов ассетов (текстур, шрифтов, звуков, музыки, скинов, иконки)
/// относительно рабочего каталога, базового каталога приложения и родительских папок репозитория.
/// </summary>
internal static class AssetLocator {
    public static List<string> EnumerateSearchRoots(int maxParentDepth = 6) {
        var roots = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path) {
            if (string.IsNullOrWhiteSpace(path)) return;
            try {
                string full = Path.GetFullPath(path);
                if (seen.Add(full)) roots.Add(full);
            } catch { }
        }

        string[] starts = {
            Directory.GetCurrentDirectory(),
            AppDomain.CurrentDomain.BaseDirectory,
            AppContext.BaseDirectory
        };

        foreach (var start in starts) Add(start);

        foreach (var start in starts) {
            if (string.IsNullOrWhiteSpace(start)) continue;
            try {
                var dir = new DirectoryInfo(start);
                for (int i = 0; i < maxParentDepth && dir?.Parent != null; i++) {
                    dir = dir.Parent;
                    Add(dir.FullName);
                }
            } catch { }
        }

        return roots;
    }

    public static string? FindAssetSubdir(string assetSubdir, Func<string, string, bool>? priorityCheck = null) {
        var roots = EnumerateSearchRoots();
        if (priorityCheck != null) {
            foreach (var root in roots) {
                string candidate = Path.Combine(root, "assets", assetSubdir);
                if (Directory.Exists(candidate) && priorityCheck(root, candidate)) return candidate;
            }
        }
        foreach (var root in roots) {
            string candidate = Path.Combine(root, "assets", assetSubdir);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    public static IEnumerable<string> EnumerateAssetSubdirs(string subdir, bool includeDirectSubdir = false) {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in EnumerateSearchRoots()) {
            string inAssets = Path.Combine(root, "assets", subdir);
            if (Directory.Exists(inAssets) && seen.Add(inAssets)) yield return inAssets;
            if (includeDirectSubdir) {
                string direct = Path.Combine(root, subdir);
                if (Directory.Exists(direct) && seen.Add(direct)) yield return direct;
            }
        }
    }

    public static string? FindAssetFile(params string[] relativePathsUnderAssets) {
        var roots = EnumerateSearchRoots();
        foreach (var root in roots) {
            foreach (var rel in relativePathsUnderAssets) {
                string inAssets = Path.Combine(root, "assets", rel);
                if (File.Exists(inAssets)) return inAssets;
                string direct = Path.Combine(root, rel);
                if (File.Exists(direct)) return direct;
            }
        }
        return null;
    }
}
