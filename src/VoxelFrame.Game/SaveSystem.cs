using System.IO.Compression;
using System.Numerics;
using System.Text;
using VoxelFrame.Core;
using VoxelFrame.Core.World;
using VoxelFrame.Core.Inventory;

namespace VoxelFrame.Game;

/// <summary>
/// Сохранение и загрузка мира: бинарный формат + gzip. Сохраняется всё
/// состояние: мир, игрок, инвентарь, сущности, огонь, время суток.
/// </summary>
public static class SaveSystem {
    public const uint Magic = 0x56465331;   // "VFS1"
    public const int Version = 5;

    public static int SelectedWorldSlot = 1;
    public static string SaveDirectory {
        get {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxelFrame", "saves");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
    public static string SavePathForWorld(int slot) => Path.Combine(SaveDirectory, $"world_{slot}.dat");
    public static bool SaveExistsForWorld(int slot) => File.Exists(SavePathForWorld(slot));
    public static string SavePath => SavePathForWorld(SelectedWorldSlot);
    public static bool SaveExists => SaveExistsForWorld(SelectedWorldSlot);
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxelFrame", "settings.json");

    public static void DeleteSave(int slot) {
        string path = SavePathForWorld(slot);
        if (File.Exists(path)) File.Delete(path);
    }

    public static void Save(GameSession session, string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var gz = new GZipStream(fs, CompressionLevel.SmallestSize))
        using (var bw = new BinaryWriter(gz, Encoding.UTF8, leaveOpen: false)) {
            bw.Write(Magic);
            bw.Write(Version);
            bw.Write(session.World.Seed);
            bw.Write(session.DayNight.TimeOfDay);
            bw.Write(session.World.Fire.TotalSmokeKg);
            bw.Write(session.World.SpawnBlock.X);
            bw.Write(session.World.SpawnBlock.Y);
            bw.Write(session.World.SpawnBlock.Z);

            var p = session.Player;
            WriteVec3(bw, p.Position);
            bw.Write(p.Yaw);
            bw.Write(p.Pitch);
            bw.Write(p.Health);
            bw.Write(0f); // placeholder
            bw.Write(p.SelectedSlot);

            var inv = p.Inventory;
            var nonNullSlots = inv.Slots
                .Select((e, idx) => (e, idx))
                .Where(x => x.e != null)
                .ToList();
            bw.Write(nonNullSlots.Count);
            foreach (var (e, idx) in nonNullSlots) {
                bw.Write(idx);
                bw.Write(e!.Value.Item.Definition.Id);
                bw.Write(e.Value.Quantity);
                bw.Write((float)e.Value.Item.Condition);
            }

            bw.Write(session.World.Chunks.Count);
            foreach (var gc in session.World.Chunks) {
                bw.Write(gc.Coord.X);
                bw.Write(gc.Coord.Y);
                bw.Write(gc.Coord.Z);
                var partials = new List<(int Index, float Content)>();
                var types = new ushort[Chunk.VoxelCount];
                for (int i = 0; i < Chunk.VoxelCount; i++) {
                    var v = gc.Chunk.Get(i);
                    types[i] = v.TypeId;
                    if (v.TypeId != 0 && MathF.Abs(v.ContentVolumeM3 - 1f) > 0.001f)
                        partials.Add((i, v.ContentVolumeM3));
                }
                foreach (var t in types) bw.Write(t);
                bw.Write(partials.Count);
                foreach (var (idx, content) in partials) {
                    bw.Write(idx);
                    bw.Write(content);
                }
            }

            bw.Write(session.World.Pickups.Count);
            foreach (var pk in session.World.Pickups) {
                bw.Write(pk.Definition.Id);
                bw.Write(pk.Quantity);
                WriteVec3(bw, pk.Position);
            }

            bw.Write(session.World.Animals.Count);
            foreach (var a in session.World.Animals) {
                bw.Write((int)a.Type);
                WriteVec3(bw, a.Position);
                bw.Write(a.Health);
            }

            bw.Write(session.World.HostileMobs.Count);
            foreach (var h in session.World.HostileMobs) {
                bw.Write((int)h.Type);
                WriteVec3(bw, h.Position);
                bw.Write(h.Health);
            }

            bw.Write(session.World.FallingBlocks.Count);
            foreach (var f in session.World.FallingBlocks) {
                bw.Write(f.Block.Id);
                WriteVec3(bw, f.Position);
            }

            bw.Write(session.World.Fire.Burning.Count);
            foreach (var (pos, remaining) in session.World.Fire.Burning) {
                bw.Write(pos.X); bw.Write(pos.Y); bw.Write(pos.Z);
                bw.Write(remaining);
            }
            bw.Write(session.World.Fire.Campfires.Count);
            foreach (var pos in session.World.Fire.Campfires) {
                bw.Write(pos.X); bw.Write(pos.Y); bw.Write(pos.Z);
            }

            // Сундуки мира
            bw.Write(session.World.Chests.Count);
            foreach (var (cpos, cinv) in session.World.Chests) {
                bw.Write(cpos.X); bw.Write(cpos.Y); bw.Write(cpos.Z);
                var cNonNull = cinv.Slots.Select((e, idx) => (e, idx)).Where(x => x.e != null).ToList();
                bw.Write(cNonNull.Count);
                foreach (var (e, idx) in cNonNull) {
                    bw.Write(idx);
                    bw.Write(e!.Value.Item.Definition.Id);
                    bw.Write(e.Value.Quantity);
                    bw.Write((float)e.Value.Item.Condition);
                }
            }
        }
        File.Move(tmp, path, overwrite: true);
    }

    public static GameSession Load(string path, bool headless) {
        using var fs = File.OpenRead(path);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var br = new BinaryReader(gz, Encoding.UTF8);
        if (br.ReadUInt32() != Magic) throw new InvalidDataException("Неверный формат сохранения");
        int version = br.ReadInt32();
        if (version < 2 || version > Version) throw new InvalidDataException($"Версия сохранения {version} не поддерживается");

        int seed = br.ReadInt32();
        float timeOfDay = br.ReadSingle();
        double smokeKg = br.ReadDouble();
        var spawn = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());

        var session = new GameSession(headless) {
            World = new GameWorld(seed),
            DayNight = new DayNightCycle(timeOfDay),
            Player = new Player(),
        };
        session.World.Fire.TotalSmokeKg = smokeKg;
        session.World.SpawnBlock = spawn;

        session.Player.Position = ReadVec3(br);
        session.Player.Yaw = br.ReadSingle();
        session.Player.Pitch = br.ReadSingle();
        session.Player.Health = br.ReadSingle();
        br.ReadSingle(); // placeholder
        session.Player.SelectedSlot = br.ReadInt32();

        int entryCount = br.ReadInt32();
        for (int i = 0; i < entryCount; i++) {
            int index = br.ReadInt32();
            ushort defId = br.ReadUInt16();
            int qty = br.ReadInt32();
            float cond = version >= 5 ? br.ReadSingle() : 1.0f;
            if (GameData.Items.TryGetValue(defId, out var def)) {
                var item = GameData.NewItem(def);
                item.Condition = Math.Clamp(cond, 0.0, 1.0);
                session.Player.Inventory.InsertAt(index, new ItemEntry(item, qty));
            }
        }

        int chunkCount = br.ReadInt32();
        for (int c = 0; c < chunkCount; c++) {
            var cc = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            var types = new ushort[Chunk.VoxelCount];
            for (int i = 0; i < types.Length; i++) types[i] = br.ReadUInt16();
            int partialCount = br.ReadInt32();
            var partials = new Dictionary<int, float>();
            for (int i = 0; i < partialCount; i++) {
                int idx = br.ReadInt32();
                partials[idx] = br.ReadSingle();
            }
            session.World.LoadChunk(cc, types, partials);
        }

        int pickupCount = br.ReadInt32();
        session.World.Pickups.Clear();
        for (int i = 0; i < pickupCount; i++) {
            ushort defId = br.ReadUInt16();
            int qty = br.ReadInt32();
            var pos = ReadVec3(br);
            if (GameData.Items.TryGetValue(defId, out var def))
                session.World.Pickups.Add(new ItemPickup(def, qty, pos));
        }

        int animalCount = br.ReadInt32();
        session.World.Animals.Clear();
        for (int i = 0; i < animalCount; i++) {
            var animalType = version >= 4 ? (AnimalType)br.ReadInt32() : AnimalType.Pig;
            var pos = ReadVec3(br);
            float hp = br.ReadSingle();
            session.World.Animals.Add(new Animal(animalType, pos) { Health = hp });
        }

        if (version >= 3) {
            int hostileCount = br.ReadInt32();
            for (int i = 0; i < hostileCount; i++) {
                var hType = (HostileType)br.ReadInt32();
                var pos = ReadVec3(br);
                float hp = br.ReadSingle();
                var mob = new HostileMob(hType, pos) { Health = hp };
                session.World.HostileMobs.Add(mob);
            }
        }

        int fallingCount = br.ReadInt32();
        for (int i = 0; i < fallingCount; i++) {
            ushort typeId = br.ReadUInt16();
            var pos = ReadVec3(br);
            session.World.FallingBlocks.Add(new FallingBlock(GameData.GetBlock(typeId), pos));
        }

        int burningCount = br.ReadInt32();
        for (int i = 0; i < burningCount; i++) {
            var pos = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            float remaining = br.ReadSingle();
            session.World.Fire.Burning[pos] = remaining;
        }
        int campCount = br.ReadInt32();
        for (int i = 0; i < campCount; i++) {
            var pos = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            session.World.Fire.Campfires.Add(pos);
        }

        if (version >= 5) {
            int chestCount = br.ReadInt32();
            for (int ch = 0; ch < chestCount; ch++) {
                var cpos = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
                var cinv = session.World.GetOrCreateChest(cpos);
                int cEntries = br.ReadInt32();
                for (int ce = 0; ce < cEntries; ce++) {
                    int cidx = br.ReadInt32();
                    ushort cDefId = br.ReadUInt16();
                    int cQty = br.ReadInt32();
                    float cCond = br.ReadSingle();
                    if (GameData.Items.TryGetValue(cDefId, out var cDef)) {
                        var cItem = GameData.NewItem(cDef);
                        cItem.Condition = Math.Clamp(cCond, 0.0, 1.0);
                        cinv.InsertAt(cidx, new ItemEntry(cItem, cQty));
                    }
                }
            }
        }

        session.AddMessage("Мир загружен");
        return session;
    }

    private static void WriteVec3(BinaryWriter bw, Vector3 v) {
        bw.Write(v.X);
        bw.Write(v.Y);
        bw.Write(v.Z);
    }

    private static Vector3 ReadVec3(BinaryReader br) => new(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

    // ── Настройки (JSON) ───────────────────────────────────────────────────

    public static bool FancyGraphics = true;
    public static int SoundVolume = 100; // 0..100%
    public static int RenderDistanceSetting = 5; // 3, 5, 7
    public static bool KeepInventory = false; // По умолчанию инвентарь выпадает при смерти

    public static void SaveSettings() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var obj = new Dictionary<string, object> {
                ["Forward"] = (int)KeyBinds.Forward,
                ["Backward"] = (int)KeyBinds.Backward,
                ["Left"] = (int)KeyBinds.Left,
                ["Right"] = (int)KeyBinds.Right,
                ["Jump"] = (int)KeyBinds.Jump,
                ["Crouch"] = (int)KeyBinds.Crouch,
                ["Drop"] = (int)KeyBinds.Drop,
                ["Inventory"] = (int)KeyBinds.Inventory,
                ["Crafting"] = (int)KeyBinds.Crafting,
                ["Pause"] = (int)KeyBinds.Pause,
                ["Fullscreen"] = Raylib_cs.Raylib.IsWindowState(Raylib_cs.ConfigFlags.UndecoratedWindow) || Raylib_cs.Raylib.IsWindowFullscreen(),
                ["Width"] = Raylib_cs.Raylib.GetScreenWidth(),
                ["Height"] = Raylib_cs.Raylib.GetScreenHeight(),
                ["FancyGraphics"] = FancyGraphics,
                ["SoundVolume"] = SoundVolume,
                ["RenderDistanceSetting"] = RenderDistanceSetting,
                ["KeepInventory"] = KeepInventory,
            };
            File.WriteAllText(SettingsPath, System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        } catch { }
    }

    public static void LoadSettings() {
        try {
            if (!File.Exists(SettingsPath)) return;
            string json = File.ReadAllText(SettingsPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            void R(string key, Action<int> set) { if (root.TryGetProperty(key, out var v)) set(v.GetInt32()); }
            void B(string key, Action<bool> set) { if (root.TryGetProperty(key, out var v)) set(v.GetBoolean()); }
            R("Forward", v => KeyBinds.Forward = (Raylib_cs.KeyboardKey)v);
            R("Backward", v => KeyBinds.Backward = (Raylib_cs.KeyboardKey)v);
            R("Left", v => KeyBinds.Left = (Raylib_cs.KeyboardKey)v);
            R("Right", v => KeyBinds.Right = (Raylib_cs.KeyboardKey)v);
            R("Jump", v => KeyBinds.Jump = (Raylib_cs.KeyboardKey)v);
            R("Crouch", v => KeyBinds.Crouch = (Raylib_cs.KeyboardKey)v);
            R("Drop", v => KeyBinds.Drop = (Raylib_cs.KeyboardKey)v);
            R("Inventory", v => KeyBinds.Inventory = (Raylib_cs.KeyboardKey)v);
            R("Crafting", v => KeyBinds.Crafting = (Raylib_cs.KeyboardKey)v);
            R("Pause", v => KeyBinds.Pause = (Raylib_cs.KeyboardKey)v);
            B("FancyGraphics", v => FancyGraphics = v);
            R("SoundVolume", v => SoundVolume = v);
            R("RenderDistanceSetting", v => RenderDistanceSetting = v);
            B("KeepInventory", v => KeepInventory = v);
        } catch { }
    }
}
