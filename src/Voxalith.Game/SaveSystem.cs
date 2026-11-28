using System.IO.Compression;
using System.Numerics;
using System.Text;
using Voxalith.Core;
using Voxalith.Core.World;
using Voxalith.Core.Inventory;

namespace Voxalith.Game;

/// <summary>
/// Сохранение и загрузка мира: бинарный формат + gzip. Сохраняется всё
/// состояние: мир, игрок, инвентарь, сущности, огонь, время суток.
/// </summary>
public static partial class SaveSystem {
    public const uint Magic = 0x56465331;   // "VFS1"
    public const int Version = 22;   // v22: в сейв пишется измерение Бездны

    public static string CurrentWorldPath = "";
    public static int SelectedWorldSlot = 1;
    public static int SelectedThemeIndex = 0;

    private static bool _saveDirInitialized;
    private static string? _cachedSaveDir;

    public static void Initialize() {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "Voxalith", "saves");
        string oldDir = Path.Combine(appData, "VoxelFrame", "saves");
        if (!Directory.Exists(dir) && Directory.Exists(oldDir)) {
            try {
                Directory.CreateDirectory(dir);
                // Auto-migrate saves from VoxelFrame
                foreach (var f in Directory.GetFiles(oldDir)) {
                    File.Copy(f, Path.Combine(dir, Path.GetFileName(f)), true);
                }
            } catch (Exception ex) {
                Console.WriteLine($"[SaveSystem] Ошибка миграции сохранений: {ex.Message}");
            }
        }
        Directory.CreateDirectory(dir);
        _cachedSaveDir = dir;
        _saveDirInitialized = true;
    }

    public static string SaveDirectory {
        get {
            if (!_saveDirInitialized || string.IsNullOrEmpty(_cachedSaveDir)) {
                Initialize();
            }
            return _cachedSaveDir!;
        }
    }

    private static byte[] ReadExactlyOrThrow(BinaryReader br, int byteCount) {
        byte[] buf = new byte[byteCount];
        int offset = 0;
        while (offset < byteCount) {
            int n = br.Read(buf, offset, byteCount - offset);
            if (n <= 0) throw new EndOfStreamException($"Неожиданный конец потока сохранения ({offset}/{byteCount} байт).");
            offset += n;
        }
        return buf;
    }

    public record WorldSaveInfo(string FilePath, string Name, DateTime LastPlayed, long SizeBytes, int Seed);

    public static List<WorldSaveInfo> GetAllWorlds() {
        var list = new List<WorldSaveInfo>();
        try {
            var files = Directory.GetFiles(SaveDirectory, "*.dat");
            foreach (var f in files) {
                var fi = new FileInfo(f);
                string worldName = Path.GetFileNameWithoutExtension(f);
                int seed = 0;
                try {
                    using var fs = File.OpenRead(f);
                    using var gz = new GZipStream(fs, CompressionMode.Decompress);
                    using var br = new BinaryReader(gz, Encoding.UTF8);
                    uint magic = br.ReadUInt32();
                    if (magic == Magic) {
                        int ver = br.ReadInt32();
                        if (ver >= 7) {
                            worldName = br.ReadString();
                        }
                        seed = br.ReadInt32();
                    }
                } catch { }
                list.Add(new WorldSaveInfo(f, worldName, fi.LastWriteTime, fi.Length, seed));
            }
        } catch { }
        return list.OrderByDescending(w => w.LastPlayed).ToList();
    }

    private static string SanitizeFileComponent(string? raw, string fallback) =>
        Voxalith.Core.Security.PathSafety.SanitizeFileComponent(raw, fallback);

    public static bool IsPathInsideDirectory(string path, string directory) =>
        Voxalith.Core.Security.PathSafety.IsPathInsideDirectory(path, directory);

    public static string CreateWorldSavePath(string worldName) {
        string safeName = SanitizeFileComponent(worldName, "World");
        string path = Path.Combine(SaveDirectory, $"{safeName}.dat");
        int counter = 1;
        while (File.Exists(path)) {
            path = Path.Combine(SaveDirectory, $"{safeName}_{counter++}.dat");
        }
        return path;
    }

    public static string SavePathForWorld(int slot) => Path.Combine(SaveDirectory, $"world_{slot}.dat");
    public static bool SaveExistsForWorld(int slot) => File.Exists(SavePathForWorld(slot));
    public static string SavePath => !string.IsNullOrEmpty(CurrentWorldPath) ? CurrentWorldPath : SavePathForWorld(SelectedWorldSlot);
    public static bool SaveExists => File.Exists(SavePath);
    public static string SettingsPath {
        get {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string newPath = Path.Combine(appData, "Voxalith", "settings.json");
            string oldPath = Path.Combine(appData, "VoxelFrame", "settings.json");
            if (!File.Exists(newPath) && File.Exists(oldPath)) {
                try {
                    Directory.CreateDirectory(Path.Combine(appData, "Voxalith"));
                    File.Copy(oldPath, newPath);
                } catch { }
            }
            return newPath;
        }
    }

    public static void DeleteSave(string path) {
        if (!IsPathInsideDirectory(path, SaveDirectory)) {
            Console.WriteLine($"[SaveSystem] Отклонено удаление пути вне SaveDirectory: {path}");
            return;
        }
        if (File.Exists(path)) File.Delete(path);
        string bak = path + ".bak";
        if (File.Exists(bak)) File.Delete(bak);
        string playersDir = Path.Combine(Path.GetDirectoryName(path)!, Path.GetFileNameWithoutExtension(path) + "_players");
        if (Directory.Exists(playersDir) && IsPathInsideDirectory(playersDir, SaveDirectory)) {
            try { Directory.Delete(playersDir, true); } catch { }
        }
    }
}
