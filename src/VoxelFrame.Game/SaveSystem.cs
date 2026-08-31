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
    public const int Version = 20;

    public static string CurrentWorldPath = "";
    public static int SelectedWorldSlot = 1;
    public static string SaveDirectory {
        get {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxelFrame", "saves");
            Directory.CreateDirectory(dir);
            return dir;
        }
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

    public static string CreateWorldSavePath(string worldName) {
        string safeName = string.Join("_", worldName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "World";
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
    public static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxelFrame", "settings.json");

    public static void DeleteSave(string path) {
        if (File.Exists(path)) File.Delete(path);
    }

    public static void Save(GameSession session, string path) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
        using (var bw = new BinaryWriter(gz, Encoding.UTF8, leaveOpen: false)) {
            bw.Write(Magic);
            bw.Write(Version);
            string worldName = Path.GetFileNameWithoutExtension(path);
            bw.Write(worldName);
            // Мастер-сид — сид Обычного мира; Нижний и Энд выводятся из него XOR-ом.
            var overworld0 = session.World.Dimension == Dimension.Overworld ? session.World : session.OverworldWorld;
            bw.Write(overworld0?.Seed ?? session.MasterSeed);
            bw.Write(session.DayNight.TimeOfDay);
            bw.Write((int)session.GameMode);
            bw.Write(session.KeepInventory);
            bw.Write(session.CheatsEnabled);
            bw.Write((int)session.Dimension);

            var p = session.Player;
            WriteVec3(bw, p.Position);
            bw.Write(p.Yaw);
            bw.Write(p.Pitch);
            bw.Write(p.Health);
            bw.Write(p.Hunger);
            bw.Write(p.Saturation);
            bw.Write(p.HighestYInAir);
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
                if (GameData.GetToolTier(e!.Value.Item.Definition.Id) > 0) bw.Write(e.Value.Item.Durability);
            }

            if (p.OffhandEntry != null) {
                bw.Write(true);
                bw.Write(p.OffhandEntry.Value.Item.Definition.Id);
                bw.Write(p.OffhandEntry.Value.Quantity);
                if (GameData.GetToolTier(p.OffhandEntry.Value.Item.Definition.Id) > 0) bw.Write(p.OffhandEntry.Value.Item.Durability);
            } else {
                bw.Write(false);
            }

            // 4 слота экипировки брони
            for (int a = 0; a < 4; a++) {
                if (p.Armor[a] is { } ae && ae.Quantity > 0) {
                    bw.Write(true);
                    bw.Write(ae.Item.Definition.Id);
                    bw.Write(ae.Quantity);
                    bw.Write(ae.Item.Durability);
                } else {
                    bw.Write(false);
                }
            }

            // Пишем все существующие миры: Обычный + каждый посещённый Нижний/Энд.
            // Недоступные измерения остаются null — их сид выводится заново при первом входе.
            var worlds = CollectWorlds(session);
            bw.Write(worlds.Count);
            foreach (var (dim, w) in worlds) {
                bw.Write((int)dim);
                bw.Write(w.Seed);
                bw.Write(w.SpawnBlock.X);
                bw.Write(w.SpawnBlock.Y);
                bw.Write(w.SpawnBlock.Z);
                WriteWorldData(bw, w);
            }
        }
        // Атомарная подмена: предыдущий рабочий сейв уходит в .bak.
        // Краши посреди сохранения оставляют либо новый main, либо старый main + .bak —
        // мир не теряется никогда.
        if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
        else File.Move(tmp, path);
    }

    // ── Сериализация миров (Обычный/Нижний/Энд) ─────────────────────────────

    /// <summary>Собирает существующие миры сессии. Обычный всегда первый.</summary>
    private static List<(Dimension Dim, GameWorld World)> CollectWorlds(GameSession session) {
        var list = new List<(Dimension, GameWorld)>();
        void Add(GameWorld? w) { if (w != null) list.Add((w.Dimension, w)); }
        Add(session.World.Dimension == Dimension.Overworld ? session.World : session.OverworldWorld);
        Add(session.World.Dimension == Dimension.Nether ? session.World : session.NetherWorld);
        Add(session.World.Dimension == Dimension.End ? session.World : session.EndWorld);
        return list;
    }

    /// <summary>Пишет данные одного мира: босс Энда, чанки, сущности, печи, сундуки, огонь.</summary>
    private static void WriteWorldData(BinaryWriter bw, GameWorld world) {
        // Босс Слизня Края (для не-Эндовых миров — false/null).
        bw.Write(world.EndBossDefeated);
        // Мини-боссы (встречаются один раз за мир)
        bw.Write(world.NetherBossSpawned);
        bw.Write(world.SwampBossSpawned);
        bw.Write(world.DesertBossSpawned);
        if (world.EndBoss is { Alive: true } eb) {
            bw.Write(true);
            bw.Write(eb.Health);
            WriteVec3(bw, eb.Position);
        } else {
            bw.Write(false);
        }

        // Истинный босс Бездны
        bw.Write(world.TrueVoidBossDefeated);
        bw.Write(world.VoidAltarTriggered);
        if (world.TrueVoidBoss is { Alive: true } tb) {
            bw.Write(true);
            bw.Write(tb.Health);
            WriteVec3(bw, tb.Position);
        } else {
            bw.Write(false);
        }

        var chunkByteBuf = new byte[Chunk.VoxelCount * sizeof(ushort)];
        var types = new ushort[Chunk.VoxelCount];

        bw.Write(world.Chunks.Count);
        foreach (var gc in world.Chunks) {
            bw.Write(gc.Coord.X);
            bw.Write(gc.Coord.Y);
            bw.Write(gc.Coord.Z);
            var masks = new List<(int Index, byte Mask)>();
            for (int i = 0; i < Chunk.VoxelCount; i++) {
                var v = gc.Chunk.Get(i);
                types[i] = v.TypeId;
                if (v.TypeId != 0 && v.SubGridLayerMask != 0)
                    masks.Add((i, v.SubGridLayerMask));
            }
            System.Runtime.InteropServices.MemoryMarshal.AsBytes(types.AsSpan()).CopyTo(chunkByteBuf);
            bw.Write(chunkByteBuf);

            bw.Write(masks.Count);
            foreach (var (idx, mask) in masks) {
                bw.Write(idx);
                bw.Write(mask);
            }
        }

        bw.Write(world.Pickups.Count);
        foreach (var pk in world.Pickups) {
            bw.Write(pk.Item.Definition.Id);
            bw.Write(pk.Quantity);
            WriteVec3(bw, pk.Position);
        }

        bw.Write(world.Animals.Count);
        foreach (var a in world.Animals) {
            bw.Write((int)a.Type);
            WriteVec3(bw, a.Position);
            bw.Write(a.Health);
        }

        bw.Write(world.HostileMobs.Count);
        foreach (var h in world.HostileMobs) {
            bw.Write((int)h.Type);
            WriteVec3(bw, h.Position);
            bw.Write(h.Health);
        }

        bw.Write(world.FallingBlocks.Count);
        foreach (var f in world.FallingBlocks) {
            bw.Write(f.Block.Id);
            WriteVec3(bw, f.Position);
        }

        bw.Write(world.Fire.Burning.Count);
        foreach (var (pos, remaining) in world.Fire.Burning) {
            bw.Write(pos.X); bw.Write(pos.Y); bw.Write(pos.Z);
            bw.Write(remaining);
        }
        bw.Write(world.Fire.Campfires.Count);
        foreach (var pos in world.Fire.Campfires) {
            bw.Write(pos.X); bw.Write(pos.Y); bw.Write(pos.Z);
        }

        // Сундуки мира
        bw.Write(world.Chests.Count);
        foreach (var (cpos, cinv) in world.Chests) {
            bw.Write(cpos.X); bw.Write(cpos.Y); bw.Write(cpos.Z);
            var cNonNull = cinv.Slots.Select((e, idx) => (e, idx)).Where(x => x.e != null).ToList();
            bw.Write(cNonNull.Count);
            foreach (var (e, idx) in cNonNull) {
                bw.Write(idx);
                bw.Write(e!.Value.Item.Definition.Id);
                bw.Write(e.Value.Quantity);
            }
        }

        // Реестр сундуков (размещенные игроком и облутанные структуры)
        bw.Write(world.PlacedChests.Count);
        foreach (var pos in world.PlacedChests) {
            bw.Write(pos.X); bw.Write(pos.Y); bw.Write(pos.Z);
        }
        bw.Write(world.LootedStructureChests.Count);
        foreach (var pos in world.LootedStructureChests) {
            bw.Write(pos.X); bw.Write(pos.Y); bw.Write(pos.Z);
        }

        // Печи мира
        bw.Write(world.Furnaces.Count);
        foreach (var (fpos, f) in world.Furnaces) {
            bw.Write(fpos.X); bw.Write(fpos.Y); bw.Write(fpos.Z);
            bw.Write(f.FuelTimer);
            bw.Write(f.MaxFuelTimer);
            bw.Write(f.SmeltTimer);

            bw.Write(f.Input.HasValue);
            if (f.Input.HasValue) {
                bw.Write(f.Input.Value.Item.Definition.Id);
                bw.Write(f.Input.Value.Quantity);
            }
            bw.Write(f.Fuel.HasValue);
            if (f.Fuel.HasValue) {
                bw.Write(f.Fuel.Value.Item.Definition.Id);
                bw.Write(f.Fuel.Value.Quantity);
            }
            bw.Write(f.Output.HasValue);
            if (f.Output.HasValue) {
                bw.Write(f.Output.Value.Item.Definition.Id);
                bw.Write(f.Output.Value.Quantity);
            }
        }
    }

    /// <summary>
    /// Загружает мир, а при повреждении основного файла автоматически
    /// поднимает резервную копию (.bak). Возвращает признак восстановления.
    /// </summary>
    public static (GameSession Session, bool FromBackup) LoadWithRecovery(string path, bool headless) {
        Exception mainError;
        try {
            return (Load(path, headless), false);
        } catch (Exception ex) {
            mainError = ex;
        }
        var bak = path + ".bak";
        if (File.Exists(bak)) {
            try {
                return (Load(bak, headless), true);
            } catch { /* бэкап тоже бит — отдаём исходную ошибку */ }
        }
        throw new InvalidDataException($"Сохранение повреждено ({mainError.Message}), резервная копия не найдена", mainError);
    }

    public static GameSession Load(string path, bool headless) {
        using var fs = File.OpenRead(path);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var br = new BinaryReader(gz, Encoding.UTF8);
        if (br.ReadUInt32() != Magic) throw new InvalidDataException("Неверный формат сохранения");
        int version = br.ReadInt32();
        if (version < 2 || version > Version) throw new InvalidDataException($"Версия сохранения {version} не поддерживается");

        string worldName = version >= 7 ? br.ReadString() : Path.GetFileNameWithoutExtension(path);
        if (version >= 15) return LoadWorlds(br, headless, version);
        int seed = br.ReadInt32();
        float timeOfDay = br.ReadSingle();
        if (version < 13) br.ReadDouble(); // legacy smoke mass (удалена в v13)
        var spawn = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
        var savedDim = version >= 11 ? (Dimension)br.ReadInt32() : Dimension.Overworld;
        var savedGameMode = version >= 12 ? (GameMode)br.ReadInt32() : GameMode.Survival;
        bool savedKeepInv = version >= 12 && br.ReadBoolean();
        bool savedCheats = version >= 20 ? br.ReadBoolean() : (savedGameMode == GameMode.Creative);

        var session = new GameSession(headless) {
            World = new GameWorld(seed) { Dimension = savedDim },
            DayNight = new DayNightCycle(timeOfDay),
            Player = new Player(),
            GameMode = savedGameMode,
            KeepInventory = savedKeepInv,
            CheatsEnabled = savedCheats,
        };
        session.World.SpawnBlock = spawn;
        if (savedDim == Dimension.Nether) {
            session.NetherWorld = session.World;
            session.OverworldWorld = new GameWorld(seed) { Dimension = Dimension.Overworld };
        } else if (savedDim == Dimension.End) {
            session.EndWorld = session.World;
            session.OverworldWorld = new GameWorld(seed) { Dimension = Dimension.Overworld };
        }

        var loadedPos = ReadVec3(br);
        if (float.IsNaN(loadedPos.X) || float.IsNaN(loadedPos.Y) || float.IsNaN(loadedPos.Z) ||
            float.IsInfinity(loadedPos.X) || float.IsInfinity(loadedPos.Y) || float.IsInfinity(loadedPos.Z)) {
            session.Player.Position = session.World.GetSafeRespawnPosition(spawn);
        } else {
            session.Player.Position = loadedPos;
        }
        session.Player.Yaw = br.ReadSingle();
        session.Player.Pitch = br.ReadSingle();
        session.Player.Health = br.ReadSingle();
        if (version >= 7) {
            session.Player.Hunger = br.ReadSingle();
            session.Player.Saturation = br.ReadSingle();
        }
        session.Player.HighestYInAir = br.ReadSingle();
        session.Player.SelectedSlot = br.ReadInt32();

        int entryCount = br.ReadInt32();
        for (int i = 0; i < entryCount; i++) {
            int index = br.ReadInt32();
            ushort defId = br.ReadUInt16();
            int qty = br.ReadInt32();
            if (version >= 5 && version < 13) br.ReadSingle(); // legacy condition (удалена в v13)
            int dur = version >= 16 && GameData.GetToolTier(defId) > 0 ? br.ReadInt32() : 0;
            if (GameData.Items.TryGetValue(defId, out var def)) {
                var item = GameData.NewItem(def);
                if (dur > 0) item.Durability = dur;
                session.Player.Inventory.InsertAt(index, new ItemEntry(item, qty));
            }
        }

        if (version >= 9) {
            bool hasOffhand = br.ReadBoolean();
            if (hasOffhand) {
                ushort defId = br.ReadUInt16();
                int qty = br.ReadInt32();
                if (version < 13) br.ReadSingle(); // legacy condition (удалена в v13)
                int dur = version >= 16 && GameData.GetToolTier(defId) > 0 ? br.ReadInt32() : 0;
                if (GameData.Items.TryGetValue(defId, out var def)) {
                    var item = GameData.NewItem(def);
                    if (dur > 0) item.Durability = dur;
                    session.Player.OffhandEntry = new ItemEntry(item, qty);
                }
            }
        }

        if (version >= 19) {
            for (int a = 0; a < 4; a++) {
                if (br.ReadBoolean()) {
                    ushort defId = br.ReadUInt16();
                    int qty = br.ReadInt32();
                    int dur = br.ReadInt32();
                    if (GameData.Items.TryGetValue(defId, out var def)) {
                        var item = GameData.NewItem(def);
                        if (dur > 0) item.Durability = dur;
                        session.Player.Armor[a] = new ItemEntry(item, qty);
                    }
                }
            }
        }

        int chunkCount = br.ReadInt32();
        for (int c = 0; c < chunkCount; c++) {
            var cc = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            var types = new ushort[Chunk.VoxelCount];
            if (version >= 12) {
                byte[] chunkBytes = br.ReadBytes(Chunk.VoxelCount * sizeof(ushort));
                System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(chunkBytes).CopyTo(types);
            } else {
                for (int i = 0; i < types.Length; i++) types[i] = br.ReadUInt16();
            }
            if (version < 13) {
                int partialCount = br.ReadInt32(); // legacy content volume (удалён в v13)
                for (int i = 0; i < partialCount; i++) {
                    _ = br.ReadInt32();
                    _ = br.ReadSingle();
                }
            }
            Dictionary<int, byte>? masks = null;
            if (version >= 10) {
                int maskCount = br.ReadInt32();
                masks = new Dictionary<int, byte>();
                for (int i = 0; i < maskCount; i++) {
                    int idx = br.ReadInt32();
                    masks[idx] = br.ReadByte();
                }
            }
            session.World.LoadChunk(cc, types, masks);
        }

        int pickupCount = br.ReadInt32();
        session.World.Pickups.Clear();
        for (int i = 0; i < pickupCount; i++) {
            ushort defId = br.ReadUInt16();
            int qty = br.ReadInt32();
            var pos = ReadVec3(br);
            if (GameData.Items.TryGetValue(defId, out var def))
                session.World.Pickups.Add(new ItemPickup(GameData.NewItem(def), qty, pos));
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
            // Повреждённый/нулевой id падающего блока пропускаем, а не роняем загрузку
            if (typeId != 0 && GameData.TryGetBlock(typeId, out var fallingBlock))
                session.World.FallingBlocks.Add(new FallingBlock(fallingBlock, pos));
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
                if (!session.World.Chests.TryGetValue(cpos, out var cinv)) {
                    cinv = new Container();
                    session.World.Chests[cpos] = cinv;
                }
                session.World.LootedStructureChests.Add(cpos);
                int cEntries = br.ReadInt32();
                for (int ce = 0; ce < cEntries; ce++) {
                    int cidx = br.ReadInt32();
                    ushort cDefId = br.ReadUInt16();
                    int cQty = br.ReadInt32();
                    if (version < 13) br.ReadSingle(); // legacy condition (удалена в v13)
                    if (GameData.Items.TryGetValue(cDefId, out var cDef)) {
                        var cItem = GameData.NewItem(cDef);
                        cinv.InsertAt(cidx, new ItemEntry(cItem, cQty));
                    }
                }
            }
        }

        if (version >= 10) {
            int placedCount = br.ReadInt32();
            for (int i = 0; i < placedCount; i++) {
                session.World.PlacedChests.Add(new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32()));
            }
            int lootedCount = br.ReadInt32();
            for (int i = 0; i < lootedCount; i++) {
                session.World.LootedStructureChests.Add(new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32()));
            }
        }

        if (version >= 8) {
            int furnaceCount = br.ReadInt32();
            for (int fn = 0; fn < furnaceCount; fn++) {
                var fpos = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
                var f = session.World.GetOrCreateFurnace(fpos);
                f.FuelTimer = br.ReadSingle();
                f.MaxFuelTimer = br.ReadSingle();
                f.SmeltTimer = br.ReadSingle();

                if (br.ReadBoolean()) {
                    ushort defId = br.ReadUInt16();
                    int qty = br.ReadInt32();
                    if (version < 13) br.ReadSingle(); // legacy condition (удалена в v13)
                    if (GameData.Items.TryGetValue(defId, out var def)) {
                        var itm = GameData.NewItem(def);
                        f.Input = new ItemEntry(itm, qty);
                    }
                } else f.Input = null;

                if (br.ReadBoolean()) {
                    ushort defId = br.ReadUInt16();
                    int qty = br.ReadInt32();
                    if (version < 13) br.ReadSingle(); // legacy condition (удалена в v13)
                    if (GameData.Items.TryGetValue(defId, out var def)) {
                        var itm = GameData.NewItem(def);
                        f.Fuel = new ItemEntry(itm, qty);
                    }
                } else f.Fuel = null;

                if (br.ReadBoolean()) {
                    ushort defId = br.ReadUInt16();
                    int qty = br.ReadInt32();
                    if (version < 13) br.ReadSingle(); // legacy condition (удалена в v13)
                    if (GameData.Items.TryGetValue(defId, out var def)) {
                        var itm = GameData.NewItem(def);
                        f.Output = new ItemEntry(itm, qty);
                    }
                } else f.Output = null;
            }
        }

        if (version >= 14) {
            session.World.EndBossDefeated = br.ReadBoolean();
            if (br.ReadBoolean()) {
                float bossHp = br.ReadSingle();
                var bossPos = ReadVec3(br);
                float islandTop = session.World.Generator.EndSurfaceHeight(0, 0);
                var islandCenter = new Vector3(0.5f, islandTop, 0.5f);
                var boss = new EndSlime(bossPos, islandCenter, session.World.Seed) { Health = bossHp };
                session.World.EndBoss = boss;
            }
        }

        return session;
    }

    // ── Загрузка формата v15: все измерения ─────────────────────────────────

    private static GameSession LoadWorlds(BinaryReader br, bool headless, int version) {
        int masterSeed = br.ReadInt32();
        float timeOfDay = br.ReadSingle();
        var savedGameMode = (GameMode)br.ReadInt32();
        bool savedKeepInv = br.ReadBoolean();
        bool savedCheats = version >= 20 ? br.ReadBoolean() : (savedGameMode == GameMode.Creative);
        var currentDim = (Dimension)br.ReadInt32();

        var session = new GameSession(headless) {
            MasterSeed = masterSeed,
            DayNight = new DayNightCycle(timeOfDay),
            Player = new Player(),
            GameMode = savedGameMode,
            KeepInventory = savedKeepInv,
            CheatsEnabled = savedCheats,
        };

        var p = session.Player;
        var loadedPos = ReadVec3(br);
        p.Yaw = br.ReadSingle();
        p.Pitch = br.ReadSingle();
        p.Health = br.ReadSingle();
        p.Hunger = br.ReadSingle();
        p.Saturation = br.ReadSingle();
        p.HighestYInAir = br.ReadSingle();
        p.SelectedSlot = br.ReadInt32();

        int entryCount = br.ReadInt32();
        for (int i = 0; i < entryCount; i++) {
            int index = br.ReadInt32();
            ushort defId = br.ReadUInt16();
            int qty = br.ReadInt32();
            int dur = version >= 16 && GameData.GetToolTier(defId) > 0 ? br.ReadInt32() : 0;
            if (GameData.Items.TryGetValue(defId, out var def)) {
                var item = GameData.NewItem(def);
                if (dur > 0) item.Durability = dur;
                p.Inventory.InsertAt(index, new ItemEntry(item, qty));
            }
        }

        if (br.ReadBoolean()) {
            ushort defId = br.ReadUInt16();
            int qty = br.ReadInt32();
            int dur = version >= 16 && GameData.GetToolTier(defId) > 0 ? br.ReadInt32() : 0;
            if (GameData.Items.TryGetValue(defId, out var def)) {
                var item = GameData.NewItem(def);
                if (dur > 0) item.Durability = dur;
                p.OffhandEntry = new ItemEntry(item, qty);
            }
        }

        if (version >= 19) {
            for (int a = 0; a < 4; a++) {
                if (br.ReadBoolean()) {
                    ushort defId = br.ReadUInt16();
                    int qty = br.ReadInt32();
                    int dur = br.ReadInt32();
                    if (GameData.Items.TryGetValue(defId, out var def)) {
                        var item = GameData.NewItem(def);
                        if (dur > 0) item.Durability = dur;
                        p.Armor[a] = new ItemEntry(item, qty);
                    }
                }
            }
        }

        // Миры: Обычный/Нижний/Энд (столько, сколько было посещено)
        int worldCount = br.ReadInt32();
        GameWorld? overworld = null, nether = null, end = null;
        for (int i = 0; i < worldCount; i++) {
            var dim = (Dimension)br.ReadInt32();
            int seed = br.ReadInt32();
            var spawn = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            var w = ReadWorldData(br, seed, dim, version);
            w.SpawnBlock = spawn;
            switch (dim) {
                case Dimension.Overworld: overworld = w; break;
                case Dimension.Nether: nether = w; break;
                case Dimension.End: end = w; break;
            }
        }
        session.OverworldWorld = overworld;
        session.NetherWorld = nether;
        session.EndWorld = end;

        var currentWorld = currentDim switch {
            Dimension.Overworld => overworld,
            Dimension.Nether => nether,
            Dimension.End => end,
            _ => null,
        };
        session.World = currentWorld ?? overworld ?? new GameWorld(masterSeed) { Dimension = Dimension.Overworld };

        if (float.IsNaN(loadedPos.X) || float.IsNaN(loadedPos.Y) || float.IsNaN(loadedPos.Z) ||
            float.IsInfinity(loadedPos.X) || float.IsInfinity(loadedPos.Y) || float.IsInfinity(loadedPos.Z)) {
            p.Position = session.World.GetSafeRespawnPosition(session.World.SpawnBlock);
        } else {
            p.Position = loadedPos;
        }
        return session;
    }

    private static GameWorld ReadWorldData(BinaryReader br, int seed, Dimension dim, int version) {
        var world = new GameWorld(seed) { Dimension = dim };

        // Босс Слизня Края (только у Энда; для других измерений — false/null)
        world.EndBossDefeated = br.ReadBoolean();
        // Мини-боссы (флаги встречи; в старых сейвах считаем ещё не встреченными)
        if (version >= 17) {
            world.NetherBossSpawned = br.ReadBoolean();
            world.SwampBossSpawned = br.ReadBoolean();
            world.DesertBossSpawned = br.ReadBoolean();
        }
        if (br.ReadBoolean()) {
            float bossHp = br.ReadSingle();
            var bossPos = ReadVec3(br);
            float islandTop = world.Generator.EndSurfaceHeight(0, 0);
            var islandCenter = new Vector3(0.5f, islandTop, 0.5f);
            world.EndBoss = new EndSlime(bossPos, islandCenter, seed) { Health = bossHp };
        }

        if (version >= 18) {
            world.TrueVoidBossDefeated = br.ReadBoolean();
            world.VoidAltarTriggered = br.ReadBoolean();
            if (br.ReadBoolean()) {
                float tbHp = br.ReadSingle();
                var tbPos = ReadVec3(br);
                world.TrueVoidBoss = new TrueEndSlime(tbPos, new Vector3(0.5f, 11f, 0.5f), seed) { Health = tbHp };
            }
        }

        int chunkCount = br.ReadInt32();
        for (int c = 0; c < chunkCount; c++) {
            var cc = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            var types = new ushort[Chunk.VoxelCount];
            byte[] chunkBytes = br.ReadBytes(Chunk.VoxelCount * sizeof(ushort));
            System.Runtime.InteropServices.MemoryMarshal.Cast<byte, ushort>(chunkBytes).CopyTo(types);
            int maskCount = br.ReadInt32();
            Dictionary<int, byte>? masks = null;
            if (maskCount > 0) {
                masks = new Dictionary<int, byte>();
                for (int i = 0; i < maskCount; i++) {
                    int idx = br.ReadInt32();
                    masks[idx] = br.ReadByte();
                }
            }
            world.LoadChunk(cc, types, masks);
        }

        int pickupCount = br.ReadInt32();
        world.Pickups.Clear();
        for (int i = 0; i < pickupCount; i++) {
            ushort defId = br.ReadUInt16();
            int qty = br.ReadInt32();
            var pos = ReadVec3(br);
            if (GameData.Items.TryGetValue(defId, out var def))
                world.Pickups.Add(new ItemPickup(GameData.NewItem(def), qty, pos));
        }

        int animalCount = br.ReadInt32();
        world.Animals.Clear();
        for (int i = 0; i < animalCount; i++) {
            var animalType = (AnimalType)br.ReadInt32();
            var pos = ReadVec3(br);
            float hp = br.ReadSingle();
            world.Animals.Add(new Animal(animalType, pos) { Health = hp });
        }

        int hostileCount = br.ReadInt32();
        world.HostileMobs.Clear();
        for (int i = 0; i < hostileCount; i++) {
            var hType = (HostileType)br.ReadInt32();
            var pos = ReadVec3(br);
            float hp = br.ReadSingle();
            world.HostileMobs.Add(new HostileMob(hType, pos) { Health = hp });
        }

        int fallingCount = br.ReadInt32();
        world.FallingBlocks.Clear();
        for (int i = 0; i < fallingCount; i++) {
            ushort typeId = br.ReadUInt16();
            var pos = ReadVec3(br);
            // Повреждённый/нулевой id падающего блока пропускаем, а не роняем загрузку
            if (typeId != 0 && GameData.TryGetBlock(typeId, out var fallingBlock))
                world.FallingBlocks.Add(new FallingBlock(fallingBlock, pos));
        }

        int burningCount = br.ReadInt32();
        world.Fire.Burning.Clear();
        for (int i = 0; i < burningCount; i++) {
            var pos = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            float remaining = br.ReadSingle();
            world.Fire.Burning[pos] = remaining;
        }
        int campCount = br.ReadInt32();
        world.Fire.Campfires.Clear();
        for (int i = 0; i < campCount; i++) {
            var pos = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            world.Fire.Campfires.Add(pos);
        }

        int chestCount = br.ReadInt32();
        world.Chests.Clear();
        for (int ch = 0; ch < chestCount; ch++) {
            var cpos = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            if (!world.Chests.TryGetValue(cpos, out var cinv)) {
                cinv = new Container();
                world.Chests[cpos] = cinv;
            }
            world.LootedStructureChests.Add(cpos);
            int cEntries = br.ReadInt32();
            for (int ce = 0; ce < cEntries; ce++) {
                int cidx = br.ReadInt32();
                ushort cDefId = br.ReadUInt16();
                int cQty = br.ReadInt32();
                if (GameData.Items.TryGetValue(cDefId, out var cDef))
                    cinv.InsertAt(cidx, new ItemEntry(GameData.NewItem(cDef), cQty));
            }
        }

        int placedCount = br.ReadInt32();
        world.PlacedChests.Clear();
        for (int i = 0; i < placedCount; i++) {
            world.PlacedChests.Add(new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32()));
        }
        int lootedCount = br.ReadInt32();
        world.LootedStructureChests.Clear();
        for (int i = 0; i < lootedCount; i++) {
            world.LootedStructureChests.Add(new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32()));
        }

        int furnaceCount = br.ReadInt32();
        for (int fn = 0; fn < furnaceCount; fn++) {
            var fpos = new Vec3i(br.ReadInt32(), br.ReadInt32(), br.ReadInt32());
            var f = world.GetOrCreateFurnace(fpos);
            f.FuelTimer = br.ReadSingle();
            f.MaxFuelTimer = br.ReadSingle();
            f.SmeltTimer = br.ReadSingle();

            if (br.ReadBoolean()) {
                ushort defId = br.ReadUInt16();
                int qty = br.ReadInt32();
                if (GameData.Items.TryGetValue(defId, out var def))
                    f.Input = new ItemEntry(GameData.NewItem(def), qty);
            } else f.Input = null;

            if (br.ReadBoolean()) {
                ushort defId = br.ReadUInt16();
                int qty = br.ReadInt32();
                if (GameData.Items.TryGetValue(defId, out var def))
                    f.Fuel = new ItemEntry(GameData.NewItem(def), qty);
            } else f.Fuel = null;

            if (br.ReadBoolean()) {
                ushort defId = br.ReadUInt16();
                int qty = br.ReadInt32();
                if (GameData.Items.TryGetValue(defId, out var def))
                    f.Output = new ItemEntry(GameData.NewItem(def), qty);
            } else f.Output = null;
        }

        return world;
    }

    private static void WriteVec3(BinaryWriter bw, Vector3 v) {
        bw.Write(float.IsNaN(v.X) || float.IsInfinity(v.X) ? 0f : v.X);
        bw.Write(float.IsNaN(v.Y) || float.IsInfinity(v.Y) ? 64f : v.Y);
        bw.Write(float.IsNaN(v.Z) || float.IsInfinity(v.Z) ? 0f : v.Z);
    }

    private static Vector3 ReadVec3(BinaryReader br) => new(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

    public static string GetPlayerDataDirectory() {
        string dir = Path.Combine(SaveDirectory, "playerdata");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetPlayerDataPath(string playerName) {
        string safeName = string.Join("_", playerName.Split(Path.GetInvalidFileNameChars()));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "player";
        return Path.Combine(GetPlayerDataDirectory(), $"{safeName.ToLowerInvariant()}.dat");
    }

    public static bool HasPlayerData(string playerName) {
        return File.Exists(GetPlayerDataPath(playerName));
    }

    public static void SavePlayerData(string playerName, Player player) {
        try {
            string path = GetPlayerDataPath(playerName);
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var gz = new GZipStream(fs, CompressionLevel.Fastest))
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

                // Инвентарь
                var nonNullSlots = player.Inventory.Slots
                    .Select((e, idx) => (e, idx))
                    .Where(x => x.e != null)
                    .ToList();
                bw.Write(nonNullSlots.Count);
                foreach (var (e, idx) in nonNullSlots) {
                    bw.Write(idx);
                    bw.Write(e!.Value.Item.Definition.Id);
                    bw.Write(e.Value.Quantity);
                    bw.Write(e.Value.Item.Durability);
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
            File.Move(tmp, path, overwrite: true);
        } catch { }
    }

    public static bool LoadPlayerData(string playerName, Player player) {
        try {
            string path = GetPlayerDataPath(playerName);
            if (!File.Exists(path)) return false;
            using var fs = File.OpenRead(path);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var br = new BinaryReader(gz, Encoding.UTF8, leaveOpen: false);

            if (br.ReadUInt32() != Magic) return false;
            int version = br.ReadInt32();
            string name = br.ReadString();
            player.Name = name;
            player.Position = ReadVec3(br);
            player.Yaw = br.ReadSingle();
            player.Pitch = br.ReadSingle();
            player.Health = br.ReadSingle();
            player.Hunger = br.ReadSingle();
            player.Saturation = br.ReadSingle();
            player.HighestYInAir = br.ReadSingle();
            player.SelectedSlot = br.ReadInt32();

            // Очищаем и загружаем инвентарь
            for (int i = 0; i < player.Inventory.Capacity; i++) player.Inventory.RemoveAt(i);
            int entryCount = br.ReadInt32();
            for (int i = 0; i < entryCount; i++) {
                int index = br.ReadInt32();
                ushort defId = br.ReadUInt16();
                int qty = br.ReadInt32();
                int dur = br.ReadInt32();
                if (GameData.Items.TryGetValue(defId, out var def)) {
                    var item = GameData.NewItem(def);
                    item.Durability = dur;
                    player.Inventory.InsertAt(index, new ItemEntry(item, qty));
                }
            }

            // Оффхэнд
            if (br.ReadBoolean()) {
                ushort defId = br.ReadUInt16();
                int qty = br.ReadInt32();
                int dur = br.ReadInt32();
                if (GameData.Items.TryGetValue(defId, out var def)) {
                    var item = GameData.NewItem(def);
                    item.Durability = dur;
                    player.OffhandEntry = new ItemEntry(item, qty);
                }
            } else {
                player.OffhandEntry = null;
            }

            // Броня
            for (int a = 0; a < 4; a++) {
                if (br.ReadBoolean()) {
                    ushort defId = br.ReadUInt16();
                    int qty = br.ReadInt32();
                    int dur = br.ReadInt32();
                    if (GameData.Items.TryGetValue(defId, out var def)) {
                        var item = GameData.NewItem(def);
                        item.Durability = dur;
                        player.Armor[a] = new ItemEntry(item, qty);
                    }
                } else {
                    player.Armor[a] = null;
                }
            }
            return true;
        } catch {
            return false;
        }
    }

    // ── Настройки (JSON) ───────────────────────────────────────────────────

    public enum GraphicsPreset {
        Fast = 0,       // Быстрая: максимальный FPS, упрощенные облака/частицы, без тяжелых каркасов
        Fancy = 1,      // Красивая: сбалансированная графика (3D облака, тени, подводный туман)
        Fabulous = 2    // Ультра: максимальный визуал, плотный туман, полный набор частиц и света
    }

    public static GraphicsPreset GraphicsQuality = GraphicsPreset.Fancy;
    public static bool FancyGraphics {
        get => GraphicsQuality != GraphicsPreset.Fast;
        set => GraphicsQuality = value ? GraphicsPreset.Fancy : GraphicsPreset.Fast;
    }
    public static int CloudsMode = 2; // 0=Off, 1=Fast, 2=Fancy
    public static int ParticlesMode = 2; // 0=Minimal, 1=Decreased, 2=All
    public static bool DynamicLighting = true;
    public static bool EntityShadows = true;
    public static int SoundVolume = 100; // 0..100% (Общая громкость)
    public static int MusicVolume = 70;  // 0..100% (Музыка)
    public static int BlocksVolume = 100; // 0..100% (Блоки, шаги, копание)
    public static int CreaturesVolume = 100; // 0..100% (Мобы и животные)
    public static int WeatherVolume = 100; // 0..100% (Погода, гром, вода)
    public static int PlayerVolume = 100; // 0..100% (Игрок и интерфейс)
    public static int RenderDistanceSetting = 5; // 2..20
    public static int MouseSensitivity = 100; // 20..200%
    public static int FovSetting = 70; // 50..110
    public static bool SmoothLighting = true;
    public static int UiScaleMode = 0; // 0 = Авто (по высоте окна), иначе процент 50..300
    public static int PostFxMode = 1;  // 0 = выкл, 1 = вкл (виньетка/золотой час/bloom в настройках графики)
    public static bool PostFxVignette = true;
    public static int PostFxVignetteStrength = 35; // 0..60 %
    public static bool PostFxGoldenHour = true;
    public static bool PostFxBloom = true;

    public static string SelectedSkin = "steve";

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
                ["Sprint"] = (int)KeyBinds.Sprint,
                ["Drop"] = (int)KeyBinds.Drop,
                ["Inventory"] = (int)KeyBinds.Inventory,
                ["Pause"] = (int)KeyBinds.Pause,
                ["Fullscreen"] = Raylib_cs.Raylib.IsWindowState(Raylib_cs.ConfigFlags.UndecoratedWindow) || Raylib_cs.Raylib.IsWindowFullscreen(),
                ["Width"] = Raylib_cs.Raylib.GetScreenWidth(),
                ["Height"] = Raylib_cs.Raylib.GetScreenHeight(),
                ["GraphicsQuality"] = (int)GraphicsQuality,
                ["FancyGraphics"] = FancyGraphics,
                ["CloudsMode"] = CloudsMode,
                ["ParticlesMode"] = ParticlesMode,
                ["DynamicLighting"] = DynamicLighting,
                ["EntityShadows"] = EntityShadows,
                ["SoundVolume"] = SoundVolume,
                ["MusicVolume"] = MusicVolume,
                ["BlocksVolume"] = BlocksVolume,
                ["CreaturesVolume"] = CreaturesVolume,
                ["WeatherVolume"] = WeatherVolume,
                ["PlayerVolume"] = PlayerVolume,
                ["RenderDistanceSetting"] = RenderDistanceSetting,
                ["MouseSensitivity"] = MouseSensitivity,
                ["FovSetting"] = FovSetting,
                ["SmoothLighting"] = SmoothLighting,
                ["UiScaleMode"] = UiScaleMode,
                ["PostFxMode"] = PostFxMode,
                ["PostFxVignette"] = PostFxVignette,
                ["PostFxVignetteStrength"] = PostFxVignetteStrength,
                ["PostFxGoldenHour"] = PostFxGoldenHour,
                ["PostFxBloom"] = PostFxBloom,
                ["PlayerNick"] = Screens.PlayerNick,
                ["SelectedSkin"] = SelectedSkin,
                ["DirectConnectIp"] = Screens.DirectConnectIp,
                ["DirectConnectPort"] = Screens.DirectConnectPort,
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
            void S(string key, Action<string> set) { if (root.TryGetProperty(key, out var v) && v.GetString() is { } s && !string.IsNullOrWhiteSpace(s)) set(s); }
            R("Forward", v => KeyBinds.Forward = (Raylib_cs.KeyboardKey)v);
            R("Backward", v => KeyBinds.Backward = (Raylib_cs.KeyboardKey)v);
            R("Left", v => KeyBinds.Left = (Raylib_cs.KeyboardKey)v);
            R("Right", v => KeyBinds.Right = (Raylib_cs.KeyboardKey)v);
            R("Jump", v => KeyBinds.Jump = (Raylib_cs.KeyboardKey)v);
            R("Crouch", v => KeyBinds.Crouch = (Raylib_cs.KeyboardKey)v);
            R("Sprint", v => KeyBinds.Sprint = (Raylib_cs.KeyboardKey)v);
            R("Drop", v => KeyBinds.Drop = (Raylib_cs.KeyboardKey)v);
            R("Inventory", v => KeyBinds.Inventory = (Raylib_cs.KeyboardKey)v);
            R("Pause", v => KeyBinds.Pause = (Raylib_cs.KeyboardKey)v);
            R("GraphicsQuality", v => {
                GraphicsQuality = (GraphicsPreset)Math.Clamp(v, 0, 2);
            });
            B("FancyGraphics", v => {
                if (!root.TryGetProperty("GraphicsQuality", out _)) {
                    GraphicsQuality = v ? GraphicsPreset.Fancy : GraphicsPreset.Fast;
                }
            });
            R("CloudsMode", v => CloudsMode = Math.Clamp(v, 0, 2));
            R("ParticlesMode", v => ParticlesMode = Math.Clamp(v, 0, 2));
            B("DynamicLighting", v => DynamicLighting = v);
            B("EntityShadows", v => EntityShadows = v);
            R("SoundVolume", v => SoundVolume = Math.Clamp(v, 0, 100));
            R("MusicVolume", v => MusicVolume = Math.Clamp(v, 0, 100));
            R("BlocksVolume", v => BlocksVolume = Math.Clamp(v, 0, 100));
            R("CreaturesVolume", v => CreaturesVolume = Math.Clamp(v, 0, 100));
            R("WeatherVolume", v => WeatherVolume = Math.Clamp(v, 0, 100));
            R("PlayerVolume", v => PlayerVolume = Math.Clamp(v, 0, 100));
            R("RenderDistanceSetting", v => RenderDistanceSetting = v);
            R("MouseSensitivity", v => MouseSensitivity = Math.Clamp(v, 20, 200));
            R("FovSetting", v => FovSetting = Math.Clamp(v, 50, 110));
            B("SmoothLighting", v => SmoothLighting = v);
            R("UiScaleMode", v => UiScaleMode = v == 0 ? 0 : Math.Clamp(v, 50, 300));
            R("PostFxMode", v => PostFxMode = Math.Clamp(v, 0, 1));
            B("PostFxVignette", v => PostFxVignette = v);
            R("PostFxVignetteStrength", v => PostFxVignetteStrength = Math.Clamp(v, 0, 60));
            B("PostFxGoldenHour", v => PostFxGoldenHour = v);
            B("PostFxBloom", v => PostFxBloom = v);
            S("PlayerNick", v => Screens.PlayerNick = v);
            S("SelectedSkin", v => {
                SelectedSkin = v;
                SkinSystem.SetSkin(v);
            });
            S("DirectConnectIp", v => Screens.DirectConnectIp = v);
            S("DirectConnectPort", v => Screens.DirectConnectPort = v);
        } catch { }
    }
}
