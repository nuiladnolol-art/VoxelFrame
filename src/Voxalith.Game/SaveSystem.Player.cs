using System.IO.Compression;
using System.Numerics;
using System.Text;
using Voxalith.Core;
using Voxalith.Core.World;
using Voxalith.Core.Inventory;

namespace Voxalith.Game;
public static partial class SaveSystem {
    public static string GetPlayerDataDirectory() {
        string baseDir = !string.IsNullOrEmpty(CurrentWorldPath)
            ? Path.Combine(Path.GetDirectoryName(CurrentWorldPath)!, Path.GetFileNameWithoutExtension(CurrentWorldPath) + "_players")
            : Path.Combine(SaveDirectory, "playerdata");
        Directory.CreateDirectory(baseDir);
        return baseDir;
    }

    public static string GetPlayerDataPath(string playerName) {
        string safeName = SanitizeFileComponent(playerName, "player");
        return Path.Combine(GetPlayerDataDirectory(), $"{safeName.ToLowerInvariant()}.dat");
    }

    public static bool HasPlayerData(string playerName) {
        return File.Exists(GetPlayerDataPath(playerName));
    }

    /// <summary>Сериализует записи PlayerData: автосейв и отключение клиента не должны
    /// одновременно писать в один и тот же .tmp (иначе IOException и потеря прогресса).</summary>
    private static readonly object PlayerSaveLock = new();

    public static void SavePlayerData(string playerName, Player player) {
        lock (PlayerSaveLock) {
            string tmp = "";
            try {
                string path = GetPlayerDataPath(playerName);
                // Уникальный tmp: даже при параллельных вызовах файлы не перетираются.
                tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using (var fs = File.Create(tmp)) {
                    using (var gz = new GZipStream(fs, CompressionLevel.Fastest, leaveOpen: true))
                    using (var bw = new BinaryWriter(gz, Encoding.UTF8, leaveOpen: false)) {
                        bw.Write(Magic);
                        bw.Write(Version);
                        bw.Write(player.Name);
                        WriteVec3(bw, player.Position);
                        bw.Write(player.Yaw);
                        bw.Write(player.Pitch);
                        bw.Write(player.Health);
                        bw.Write(player.Hunger);
                        bw.Write(player.Saturation);
                        bw.Write(player.HighestYInAir);
                        bw.Write(player.SelectedSlot);
                        bw.Write((byte)player.Dimension);

                        // Инвентарь
                        int nonNullCount = 0;
                        for (int i = 0; i < player.Inventory.Capacity; i++) {
                            var s = player.Inventory.Slots[i];
                            if (s != null && s.Value.Quantity > 0) nonNullCount++;
                        }
                        bw.Write(nonNullCount);
                        for (int i = 0; i < player.Inventory.Capacity; i++) {
                            var s = player.Inventory.Slots[i];
                            if (s != null && s.Value.Quantity > 0) {
                                bw.Write(i);
                                bw.Write(s.Value.Item.Definition.Id);
                                bw.Write(s.Value.Quantity);
                                bw.Write(s.Value.Item.Durability);
                            }
                        }

                        // Оффхэнд
                        if (player.OffhandEntry != null) {
                            bw.Write(true);
                            bw.Write(player.OffhandEntry.Value.Item.Definition.Id);
                            bw.Write(player.OffhandEntry.Value.Quantity);
                            bw.Write(player.OffhandEntry.Value.Item.Durability);
                        } else {
                            bw.Write(false);
                        }

                        // Броня
                        for (int a = 0; a < 4; a++) {
                            if (player.Armor[a] is { } ae && ae.Quantity > 0) {
                                bw.Write(true);
                                bw.Write(ae.Item.Definition.Id);
                                bw.Write(ae.Quantity);
                                bw.Write(ae.Item.Durability);
                            } else {
                                bw.Write(false);
                            }
                        }
                    }
                    fs.Flush(flushToDisk: true);
                }
                string bak = path + ".bak";
                if (File.Exists(path)) File.Replace(tmp, path, bak);
                else File.Move(tmp, path);
            } catch (Exception ex) {
                Console.WriteLine($"[SaveSystem] Ошибка сохранения данных игрока {playerName}: {ex.Message}");
                try { if (tmp.Length > 0 && File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
    }

    public static bool LoadPlayerData(string playerName, Player player) {
        string path = GetPlayerDataPath(playerName);
        if (TryLoadPlayerDataFile(path, player)) return true;
        string bak = path + ".bak";
        if (File.Exists(bak) && TryLoadPlayerDataFile(bak, player)) {
            Console.WriteLine($"[SaveSystem] Восстановлены данные игрока {playerName} из резервной копии .bak");
            return true;
        }
        return false;
    }

    private static bool TryLoadPlayerDataFile(string path, Player player) {
        try {
            if (!File.Exists(path)) return false;
            using var fs = File.OpenRead(path);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var br = new BinaryReader(gz, Encoding.UTF8, leaveOpen: false);

            if (br.ReadUInt32() != Magic) return false;
            int version = br.ReadInt32();
            string name = br.ReadString();
            var pos = ReadVec3(br);
            float yaw = br.ReadSingle();
            float pitch = br.ReadSingle();
            float hp = br.ReadSingle();
            float hunger = br.ReadSingle();
            float sat = br.ReadSingle();
            float highY = br.ReadSingle();
            if (!float.IsFinite(pos.X) || !float.IsFinite(pos.Y) || !float.IsFinite(pos.Z) ||
                !float.IsFinite(yaw) || !float.IsFinite(pitch)) {
                return false;
            }
            player.Name = name;
            player.Position = pos;
            player.Yaw = yaw;
            player.Pitch = pitch;
            player.Health = float.IsFinite(hp) ? Math.Clamp(hp, 0f, player.MaxHealth) : player.MaxHealth;
            player.Hunger = float.IsFinite(hunger) ? Math.Clamp(hunger, 0f, Player.MaxHunger) : Player.MaxHunger;
            player.Saturation = float.IsFinite(sat) ? Math.Clamp(sat, 0f, 20f) : 5f;
            player.HighestYInAir = float.IsFinite(highY) ? highY : pos.Y;
            player.SelectedSlot = Math.Clamp(br.ReadInt32(), 0, 8);
            if (version >= 2) {
                try {
                    byte dimB = br.ReadByte();
                    player.Dimension = dimB <= (byte)Dimension.Void ? (Dimension)dimB : Dimension.Overworld;
                } catch {
                    player.Dimension = Dimension.Overworld;
                }
            }

            // Очищаем и загружаем инвентарь
            for (int i = 0; i < player.Inventory.Capacity; i++) player.Inventory.RemoveAt(i);
            int entryCount = br.ReadInt32();
            if (entryCount < 0 || entryCount > 256) return false;
            for (int i = 0; i < entryCount; i++) {
                int index = br.ReadInt32();
                if (ReadClampedItemEntry(br.ReadUInt16(), br.ReadInt32(), br.ReadInt32()) is { } entry) {
                    player.Inventory.InsertAtOrPack(index, entry);
                }
            }

            // Оффхэнд
            if (br.ReadBoolean()) {
                player.OffhandEntry = ReadClampedItemEntry(br.ReadUInt16(), br.ReadInt32(), br.ReadInt32());
            } else {
                player.OffhandEntry = null;
            }

            // Броня
            for (int a = 0; a < 4; a++) {
                if (br.ReadBoolean()) {
                    player.Armor[a] = ReadClampedItemEntry(br.ReadUInt16(), br.ReadInt32(), br.ReadInt32());
                } else {
                    player.Armor[a] = null;
                }
            }
            return true;
        } catch (Exception ex) {
            Console.WriteLine($"[SaveSystem] Ошибка чтения файла данных игрока {path}: {ex.Message}");
            return false;
        }
    }

    private static ItemEntry? ReadClampedItemEntry(ushort defId, int qty, int dur) {
        if (!GameData.Items.TryGetValue(defId, out var def)) return null;
        var item = GameData.NewItem(def);
        int maxDur = GameData.GetMaxItemDurability(def.Id);
        if (dur > 0 && maxDur > 0) item.Durability = Math.Clamp(dur, 1, maxDur);
        return new ItemEntry(item, Math.Clamp(qty, 1, def.MaxStack));
    }
}
