using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Игровой мир: чанки, генерация, запись блоков (единый путь → физика),
/// освещение, сущности, огонь.
/// </summary>
public sealed partial class GameWorld : IDisposable {
    public const int RenderDistance = 5;

    public readonly int Seed;
    public readonly WorldGenerator Generator;
    public readonly FireSystem Fire;
    public readonly FluidEngine Fluids;
    public readonly List<ItemPickup> Pickups = new();
    public readonly List<Animal> Animals = new();
    public readonly List<HostileMob> HostileMobs = new();
    public readonly List<ArrowProjectile> Arrows = new();
    public readonly List<FallingBlock> FallingBlocks = new();
    public readonly Dictionary<Vec3i, FurnaceData> Furnaces = new();
    public readonly Dictionary<Vec3i, Container> Chests = new();
    public readonly HashSet<Vec3i> PlacedChests = new();
    public readonly HashSet<Vec3i> LootedStructureChests = new();
    public Vec3i SpawnBlock;

    private readonly WorldGrid _grid = new();
    private readonly Dictionary<Vec3i, GameChunk> _chunks = new();
    private readonly Dictionary<Vec3i, List<Vec3i>> _decor = new();   // чанк → позиции факелов/костров
    private readonly HashSet<GameChunk> _lightDirty = new();
    private readonly HashSet<GameChunk> _meshDirty = new();
    private readonly Random _random;
    private float _animalSpawnTimer = 3f;
    private float _hostileSpawnTimer = 5f;
    private float _cropTimer = 1.0f;
    private float _grassSpreadTimer = 0.4f;
    private readonly List<GameChunk> _lightBatch = new();
    private readonly Queue<GameChunk> _lightOrder = new();
    private readonly List<GameChunk> _tempChunkList = new();
    private readonly List<Vec3i> _tempDecorList = new();

    public GameWorld(int seed) {
        Seed = seed;
        Generator = new WorldGenerator(seed);
        _random = new Random(seed ^ 0x5F3759DF);
        Fire = new FireSystem(this);
        Fluids = new FluidEngine(this);
    }

    public Dimension Dimension { get; set; } = Dimension.Overworld;
    public IReadOnlyCollection<GameChunk> Chunks => _chunks.Values;

    // ── Чанки ────────────────────────────────────────────────────────────────

    public GameChunk? TryGetChunk(Vec3i cc) => _chunks.TryGetValue(cc, out var gc) ? gc : null;

    public GameChunk GetOrCreateChunk(Vec3i cc) {
        if (_chunks.TryGetValue(cc, out var gc)) return gc;
        var core = _grid.GetOrCreateChunk(cc);
        bool isNew = core.Version == 0;
        if (isNew) {
            if (Dimension == Dimension.Nether) {
                Generator.GenerateNetherChunk(core, cc.X * Chunk.SizeX, cc.Y * Chunk.SizeY, cc.Z * Chunk.SizeZ);
            } else if (Dimension == Dimension.End) {
                Generator.GenerateEndChunk(core, cc.X * Chunk.SizeX, cc.Y * Chunk.SizeY, cc.Z * Chunk.SizeZ);
            } else {
                Generator.GenerateChunk(core);
            }
        }
        gc = new GameChunk(cc, core);
        gc.RecomputeAllSurfaces();
        _chunks.Add(cc, gc);
        ScanDecorations(gc);
        if (isNew && Dimension == Dimension.End) {
            ScanEndCrystals(gc);
        }
        if (isNew && cc.Y == 1 && Dimension == Dimension.Overworld) {
            SpawnInitialChunkAnimals(gc);
        }
        // Новый чанк сам вычислит свой свет, засеявшись с границ соседей.
        // Свет СОСЕДЕЙ не трогаем — их массивы не изменились; достаточно
        // перестроить их меши, чтобы они перечитали наши значения на шве.
        // (Раньше здесь ставилась вся колонка + кольцо соседей: при загрузке
        // мира это давало сотни записей в очереди света, которые душили
        // события от игрока и сжигали миллисекунды кадра.)
        QueueLightRelight(gc);
        _meshDirty.Add(gc);
        foreach (var n in NeighborCoords(cc)) {
            if (_chunks.TryGetValue(n, out var ngc)) _meshDirty.Add(ngc);
        }
        return gc;
    }

    private static readonly Vec3i[] NeighborOffsets = {
        new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1), new(0, 1, 0), new(0, -1, 0),
    };

    private static IEnumerable<Vec3i> NeighborCoords(Vec3i cc) {
        foreach (var o in NeighborOffsets) yield return cc + o;
    }

    /// <summary>Синхронно загружает все чанки в заданном радиусе (используется при создании мира).</summary>
    public void EnsureLoadedAroundSync(Vector3 playerPos, int radius = RenderDistance) {
        var pc = Chunk.CoordOf(new Vec3i((int)MathF.Floor(playerPos.X), (int)MathF.Floor(playerPos.Y), (int)MathF.Floor(playerPos.Z)));
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                for (int dy = -1; dy <= 2; dy++) {
                    GetOrCreateChunk(new Vec3i(pc.X + dx, pc.Y + dy, pc.Z + dz));
                }
            }
        }
    }

    /// <summary>
    /// Генерирует чанки вокруг игрока:
    /// - Ближние чанки (радиус 1 вокруг игрока) загружаются немедленно, чтобы игрок никогда не падал в пустоту.
    /// - Внешние чанки (радиус 2..radius) плавно догружаются пачками (до 4 чанков за кадр).
    /// </summary>
    public void EnsureLoadedAround(Vector3 playerPos, int radius = RenderDistance) {
        var pc = Chunk.CoordOf(new Vec3i((int)MathF.Floor(playerPos.X), (int)MathF.Floor(playerPos.Y), (int)MathF.Floor(playerPos.Z)));
        
        // 1. Ближний радиус (дистанция 0..1): критичен для физики, гарантируем загрузку
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                for (int dy = -1; dy <= 2; dy++) {
                    var cc = new Vec3i(pc.X + dx, pc.Y + dy, pc.Z + dz);
                    if (!_chunks.ContainsKey(cc)) {
                        GetOrCreateChunk(cc);
                    }
                }
            }
        }

        // 2. Внешний радиус: стримим пачками до 4 чанков за кадр от ближних к дальним
        int newChunkThisFrame = 0;
        const int maxNewPerFrame = 4;
        for (int dist = 2; dist <= radius; dist++) {
            for (int dx = -dist; dx <= dist; dx++) {
                for (int dz = -dist; dz <= dist; dz++) {
                    if (Math.Abs(dx) != dist && Math.Abs(dz) != dist) continue;
                    for (int dy = -1; dy <= 2; dy++) {
                        var cc = new Vec3i(pc.X + dx, pc.Y + dy, pc.Z + dz);
                        if (!_chunks.ContainsKey(cc)) {
                            GetOrCreateChunk(cc);
                            newChunkThisFrame++;
                            if (newChunkThisFrame >= maxNewPerFrame) return;
                        }
                    }
                }
            }
        }
    }

    // ── Запросы ──────────────────────────────────────────────────────────────

    public GameChunk? ChunkAt(Vec3i world) => TryGetChunk(Chunk.CoordOf(world));

    public VoxelData GetVoxel(Vec3i w) {
        var gc = TryGetChunk(Chunk.CoordOf(w));
        if (gc == null) return VoxelData.Air;
        return gc.Chunk.Get(w.X & 31, w.Y & 31, w.Z & 31);
    }

    public BlockType? GetBlockType(Vec3i w) {
        var v = GetVoxel(w);
        return v.TypeId == 0 ? null : GameData.GetBlock(v.TypeId);
    }

    public bool IsSolidAt(Vec3i w) {
        var v = GetVoxel(w);
        if (GameData.IsDoor(v.TypeId)) {
            return (v.SubGridLayerMask & 8) == 0;
        }
        return (v.Flags & VoxelFlags.Solid) != 0;
    }

    /// <summary>Есть ли в ячейке вообще какой-либо блок.</summary>
    public bool IsBlockAt(Vec3i w) => GetVoxel(w).TypeId != 0;

    /// <summary>Является ли блок таргетируемым прицелом (жидкости пропускаются по умолчанию).</summary>
    public bool IsTargetableBlock(Vec3i w, bool hitFluids = false) {
        ushort type = GetVoxel(w).TypeId;
        return type != 0 && (hitFluids || (type != GameData.BWater.Id && type != GameData.BLava.Id));
    }

    public bool IsOpaqueAt(Vec3i w) {
        var v = GetVoxel(w);
        return v.TypeId != 0 && GameData.GetBlock(v.TypeId).IsOpaque;
    }

    public int GetColumnSurfaceHeight(int wx, int wz) {
        int cx = wx >> 5, cz = wz >> 5;
        int lx = wx & 31, lz = wz & 31;
        for (int cy = 3; cy >= -2; cy--) {
            if (_chunks.TryGetValue(new Vec3i(cx, cy, cz), out var gc)) {
                int s = gc.Surface[gc.SurfaceIndex(lx, lz)];
                if (s != int.MinValue) return s;
            }
        }
        return int.MinValue;
    }

    public byte GetBlockLight(Vec3i w) {
        var gc = TryGetChunk(Chunk.CoordOf(w));
        return gc == null ? (byte)0 : gc.BlockLight[Chunk.Index(w.X & 31, w.Y & 31, w.Z & 31)];
    }

    public byte GetSunLight(Vec3i w) {
        var gc = TryGetChunk(Chunk.CoordOf(w));
        return gc == null ? (byte)15 : gc.SunLight[Chunk.Index(w.X & 31, w.Y & 31, w.Z & 31)];
    }

    // ── Запись блоков (единый путь: мир → физика → освещение → меши) ─────────

    /// <summary>VoxelData установленного игроком блока (без структурных напряжений, 1 блок = 1 ячейка).</summary>
    public static VoxelData MakePlacedVoxel(BlockType block, byte facing = 0) {
        var flags = VoxelFlags.None;
        if (block.IsSolid) flags |= VoxelFlags.Solid;
        return new VoxelData {
            TypeId = block.Id,
            Flags = flags,
            SubGridLayerMask = facing,
        };
    }

    public void PlacePlacedBlock(Vec3i w, BlockType block, byte facing = 0) {
        SetVoxelInternal(w, MakePlacedVoxel(block, facing));
        if (block.Id == GameData.BChest.Id) {
            PlacedChests.Add(w);
            Chests[w] = new Container();
        }
    }

    public void SetBlock(Vec3i w, ushort typeId) {
        if (typeId == 0) {
            RemoveBlock(w);
        } else {
            var block = GameData.GetBlock(typeId);
            PlacePlacedBlock(w, block);
        }
    }

    public void SetVoxelRaw(Vec3i w, in VoxelData v) {
        SetVoxelInternal(w, v);
    }

    public void CheckGravityBlocksAbove(Vec3i pos) {
        var check = pos + new Vec3i(0, 1, 0);
        while (true) {
            var vox = GetVoxel(check);
            if (vox.TypeId == GameData.BSand.Id || vox.TypeId == GameData.BGravel.Id) {
                var below = check + new Vec3i(0, -1, 0);
                if (!IsSolidAt(below)) {
                    var block = GameData.GetBlock(vox.TypeId);
                    SetVoxelInternal(check, VoxelData.Air);
                    FallingBlocks.Add(new FallingBlock(block, new Vector3(check.X + 0.5f, check.Y + 0.5f, check.Z + 0.5f)));
                    check = check + new Vec3i(0, 1, 0);
                    continue;
                }
            }
            break;
        }
    }

    public event Action<Vec3i, ushort>? OnBlockRemoved;
    public event Action<Vector3, int>? OnDustSpawned;
    public event Action<Vector3, int>? OnCritSpawned;

    public void SpawnDust(Vector3 pos, int count = 4) => OnDustSpawned?.Invoke(pos, count);
    public void SpawnCrit(Vector3 pos, int count = 14) => OnCritSpawned?.Invoke(pos, count);

    public void RemoveBlock(Vec3i w) {
        var curVox = GetVoxel(w);
        if (curVox.TypeId != 0) {
            OnBlockRemoved?.Invoke(w, curVox.TypeId);
        }
        Fire.Extinguish(w);
        if (Furnaces.Remove(w, out var furnace)) {
            if (furnace.Input.HasValue && furnace.Input.Value.Quantity > 0)
                SpawnPickup(furnace.Input.Value.Item.Definition.Id, furnace.Input.Value.Quantity, w);
            if (furnace.Fuel.HasValue && furnace.Fuel.Value.Quantity > 0)
                SpawnPickup(furnace.Fuel.Value.Item.Definition.Id, furnace.Fuel.Value.Quantity, w);
            if (furnace.Output.HasValue && furnace.Output.Value.Quantity > 0)
                SpawnPickup(furnace.Output.Value.Item.Definition.Id, furnace.Output.Value.Quantity, w);
        }
        if (Chests.Remove(w, out var chestInv)) {
            for (int i = 0; i < chestInv.Slots.Length; i++) {
                var entry = chestInv.Slots[i];
                if (entry.HasValue && entry.Value.Quantity > 0) {
                    SpawnPickup(entry.Value.Item.Definition.Id, entry.Value.Quantity, w);
                }
            }
        }

        SetVoxelInternal(w, VoxelData.Air);
        CheckGravityBlocksAbove(w);

        // Разрушение растений и факелов, росших на этом блоке (трава не висит в воздухе)
        var above = w + new Vec3i(0, 1, 0);
        var aboveVox = GetVoxel(above);
        if (aboveVox.TypeId == GameData.BTallGrass.Id || aboveVox.TypeId == GameData.BWheatCrop.Id || aboveVox.TypeId == GameData.BTorch.Id) {
            if (aboveVox.TypeId == GameData.BWheatCrop.Id) {
                if (aboveVox.SubGridLayerMask >= 3) {
                    SpawnPickup(GameData.WheatItem.Id, 1, above);
                    SpawnPickup(GameData.WheatSeedsItem.Id, 1, above);
                } else {
                    SpawnPickup(GameData.WheatSeedsItem.Id, 1, above);
                }
            } else if (aboveVox.TypeId == GameData.BTallGrass.Id) {
                if (_random.NextDouble() < 0.25) {
                    SpawnPickup(GameData.WheatSeedsItem.Id, 1, above);
                }
            } else if (aboveVox.TypeId == GameData.BTorch.Id) {
                SpawnPickup(GameData.TorchItem.Id, 1, above);
            }
            RemoveBlock(above);
        }

        // Связанное удаление парной части кровати
        if (curVox.TypeId == GameData.BBed.Id || curVox.TypeId == GameData.BBedHead.Id) {
            Vec3i[] cardDirs = { new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1) };
            foreach (var d in cardDirs) {
                var otherPos = w + d;
                var otherVox = GetVoxel(otherPos);
                if (curVox.TypeId == GameData.BBed.Id && otherVox.TypeId == GameData.BBedHead.Id) {
                    SetVoxelInternal(otherPos, VoxelData.Air);
                    break;
                } else if (curVox.TypeId == GameData.BBedHead.Id && otherVox.TypeId == GameData.BBed.Id) {
                    SetVoxelInternal(otherPos, VoxelData.Air);
                    break;
                }
            }
        }

        // Связанное удаление парной части двери
        if (curVox.TypeId == GameData.BDoorLower.Id) {
            var upperPos = w + new Vec3i(0, 1, 0);
            if (GetVoxel(upperPos).TypeId == GameData.BDoorUpper.Id) {
                SetVoxelInternal(upperPos, VoxelData.Air);
            }
        } else if (curVox.TypeId == GameData.BDoorUpper.Id) {
            var lowerPos = w + new Vec3i(0, -1, 0);
            if (GetVoxel(lowerPos).TypeId == GameData.BDoorLower.Id) {
                SetVoxelInternal(lowerPos, VoxelData.Air);
                SpawnPickup(GameData.DoorItem.Id, 1, w);
            }
        }
    }

    public Container GetOrCreateChest(Vec3i pos, GameSession? session = null) {
        if (Chests.TryGetValue(pos, out var inv)) return inv;
        var newInv = new Container();

        if (PlacedChests.Contains(pos) || LootedStructureChests.Contains(pos)) {
            // Сундук игрока или уже разграбленная структура: создается чистый пустой сундук без дюпа
            Chests[pos] = newInv;
            return newInv;
        }

        LootedStructureChests.Add(pos);
        var rng = new Random(Seed ^ (pos.X * 73856093 ^ pos.Y * 19349663 ^ pos.Z * 83492791));

        var slots = Enumerable.Range(0, newInv.Capacity).OrderBy(x => rng.Next()).ToArray();
        int s = 0;
        void Add(ItemDefinition def, int qty) { if (qty > 0 && s < slots.Length) newInv.InsertAt(slots[s++], new ItemEntry(GameData.NewItem(def), qty)); }

        if (Dimension == Dimension.Nether || pos.Y >= 50 && (pos.X * 37 + pos.Z * 19) % 29 == 0) {
            Add(GameData.GoldIngotItem, rng.Next(1, 4));
            Add(GameData.FlintAndSteelItem, 1);
            Add(GameData.ObsidianItem, rng.Next(1, 3));
            Add(GameData.CoalItem, rng.Next(2, 6));
            if (rng.NextDouble() < 0.35) Add(GameData.NetherQuartzItem, rng.Next(2, 6));
            if (rng.NextDouble() < 0.20) Add(GameData.IronIngotItem, rng.Next(1, 3));
            if (rng.NextDouble() < 0.10) Add(GameData.GoldenAppleItem, 1);
        } else if (pos.Y <= 38 && pos.Y >= 10) {
            Add(GameData.IronIngotItem, rng.Next(1, 4));
            Add(GameData.StringItem, rng.Next(2, 6));
            Add(GameData.BoneItem, rng.Next(2, 7));
            Add(GameData.GunpowderItem, rng.Next(1, 4));
            Add(GameData.BreadItem, rng.Next(1, 3));
            Add(GameData.TorchItem, rng.Next(4, 10));
            if (rng.NextDouble() < 0.15) Add(GameData.EnchantedBookItem, 1);
            if (rng.NextDouble() < 0.10) Add(GameData.MusicDiscItem, 1);
        } else {
            Add(GameData.BreadItem, rng.Next(2, 5));
            Add(GameData.WheatSeedsItem, rng.Next(3, 8));
            Add(GameData.AppleItem, rng.Next(1, 3));
            Add(GameData.TorchItem, rng.Next(4, 10));
            Add(GameData.PlankItem, rng.Next(3, 8));
            if (rng.NextDouble() < 0.45) Add(GameData.CoalItem, rng.Next(1, 3));
            if (rng.NextDouble() < 0.30) Add(GameData.StonePickaxeItem, 1);
            if (rng.NextDouble() < 0.20) Add(GameData.IronIngotItem, rng.Next(1, 2));
        }
        Chests[pos] = newInv;
        return newInv;
    }

    public static void CreateExplosion(Vector3 pos, float radius, float maxDamage, GameSession? session, bool breakBlocks = true) {
        SoundSystem.PlayExplosion();
        if (session != null) {
            float dist = Vector3.Distance(session.Player.Position, pos);
            if (dist < radius * 1.5f) {
                float dmg = (1.0f - dist / (radius * 1.5f)) * maxDamage;
                session.Player.ApplyDamage(dmg, session, pos);
                session.AddMessage($"Взрыв нанёс урон -{dmg:F0} HP!");
            }
        }

        if (session != null && breakBlocks) {
            var center = new Vec3i((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y), (int)MathF.Floor(pos.Z));
            int r = (int)MathF.Ceiling(radius);
            var rng = new Random();
            for (int dx = -r; dx <= r; dx++) {
                for (int dy = -r; dy <= r; dy++) {
                    for (int dz = -r; dz <= r; dz++) {
                        if (dx * dx + dy * dy + dz * dz <= radius * radius) {
                            var target = center + new Vec3i(dx, dy, dz);
                            var vox = session.World.GetVoxel(target);
                            if (vox.TypeId != 0 && vox.TypeId != GameData.BBedrock.Id && vox.TypeId != GameData.BObsidian.Id) {
                                var b = GameData.GetBlock(vox.TypeId);
                                session.World.RemoveBlock(target);
                                if (b.DropItemId != 0 && rng.NextDouble() < 0.35) {
                                    session.World.SpawnPickup(b.DropItemId, 1, target);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Поиск безопасной точки возрождения игрока без риска застрять в блоках или упасть в лаву/бездну.</summary>
    public Vector3 GetSafeRespawnPosition(Vec3i baseSpawn) {
        for (int r = 0; r <= 6; r++) {
            for (int dx = -r; dx <= r; dx++) {
                for (int dz = -r; dz <= r; dz++) {
                    int wx = baseSpawn.X + dx;
                    int wz = baseSpawn.Z + dz;
                    for (int wy = 120; wy >= 2; wy--) {
                        var footPos = new Vec3i(wx, wy, wz);
                        var belowPos = new Vec3i(wx, wy - 1, wz);
                        var headPos = new Vec3i(wx, wy + 1, wz);

                        var belowVox = GetVoxel(belowPos);
                        var footVox = GetVoxel(footPos);
                        var headVox = GetVoxel(headPos);

                        if (belowVox.TypeId != 0 && belowVox.TypeId != GameData.BLava.Id && belowVox.TypeId != GameData.BWater.Id) {
                            var belowBlock = GameData.GetBlock(belowVox.TypeId);
                            if (belowBlock.IsSolid) {
                                bool footFree = footVox.TypeId == 0 || (GameData.GetBlock(footVox.TypeId) is { IsSolid: false } fb && fb.Id != GameData.BLava.Id);
                                bool headFree = headVox.TypeId == 0 || (GameData.GetBlock(headVox.TypeId) is { IsSolid: false } hb && hb.Id != GameData.BLava.Id);
                                if (footFree && headFree) {
                                    // Центр хитбокса игрока (HalfExtents.Y = 0.9f) над поверхностью блока
                                    return new Vector3(wx + 0.5f, wy + 0.95f, wz + 0.5f);
                                }
                            }
                        }
                    }
                }
            }
        }
        int surf = Generator.SurfaceHeight(baseSpawn.X, baseSpawn.Z);
        return new Vector3(baseSpawn.X + 0.5f, surf + 1.95f, baseSpawn.Z + 0.5f);
    }

    /// <summary>Загрузка чанка из сохранения: типы + частичные объёмы содержимого + маски ориентации/слоев.</summary>
    public void LoadChunk(Vec3i cc, ushort[] types, Dictionary<int, byte>? masks = null) {
        var core = _grid.GetOrCreateChunk(cc);
        for (int i = 0; i < types.Length; i++) {
            if (types[i] == 0) {
                if (core.Get(i).TypeId != 0) {
                    var air = VoxelData.Air;
                    core.SetVoxel(i, in air);
                }
                continue;
            }
            var block = GameData.GetBlock(types[i]);
            byte mask = masks != null && masks.TryGetValue(i, out byte m) ? m : (byte)0;
            var pv = MakePlacedVoxel(block, mask);
            core.SetVoxel(i, in pv);
        }
        var gc = new GameChunk(cc, core);
        gc.RecomputeAllSurfaces();
        _chunks[cc] = gc;
        ScanDecorations(gc);
        if (Dimension == Dimension.End) ScanEndCrystals(gc);
        // Как в GetOrCreateChunk: свет считаем только для нового чанка,
        // соседям достаточно перестроить меши (см. комментарий там).
        QueueLightRelight(gc);
        _meshDirty.Add(gc);
        for (int dy = -2; dy <= 3; dy++) {
            if (_chunks.TryGetValue(new Vec3i(cc.X, dy, cc.Z), out var cCol)) _meshDirty.Add(cCol);
        }
        foreach (var n in NeighborCoords(cc)) {
            if (_chunks.TryGetValue(n, out var ngc)) _meshDirty.Add(ngc);
        }
    }

    private static Vec3i ChunkLocalToWorld(Vec3i cc, int index) {
        var (lx, ly, lz) = Chunk.LocalFromIndex(index);
        return new Vec3i(cc.X * Chunk.SizeX + lx, cc.Y * Chunk.SizeY + ly, cc.Z * Chunk.SizeZ + lz);
    }

    private void SetVoxelInternal(Vec3i w, in VoxelData voxel) {
        var oldVoxel = GetVoxel(w);
        GetOrCreateChunk(Chunk.CoordOf(w));
        _grid.SetVoxel(w, in voxel);   // событие VoxelChanged → уведомление слоя освещения/мешей
        OnBlockChanged(w, in voxel);

        if (oldVoxel.TypeId == GameData.BLog.Id && voxel.TypeId != GameData.BLog.Id) {
            CheckLeavesDecay(w);
        }
    }

    private void CheckLeavesDecay(Vec3i brokenLogPos) {
        var leavesToCheck = new List<Vec3i>();
        for (int dx = -4; dx <= 4; dx++) {
            for (int dy = -4; dy <= 4; dy++) {
                for (int dz = -4; dz <= 4; dz++) {
                    var p = brokenLogPos + new Vec3i(dx, dy, dz);
                    if (GetVoxel(p).TypeId == GameData.BLeaves.Id) {
                        leavesToCheck.Add(p);
                    }
                }
            }
        }

        foreach (var leafPos in leavesToCheck) {
            if (!IsLeavesConnectedToLog(leafPos)) {
                RemoveBlock(leafPos);

                double roll = _random.NextDouble();
                ItemDefinition? leafDrop = roll < 0.12 ? GameData.AppleItem : roll < 0.30 ? GameData.StickItem : null;
                if (leafDrop != null) {
                    SpawnPickup(leafDrop.Id, 1, leafPos);
                }
            }
        }
    }

    private bool IsLeavesConnectedToLog(Vec3i startLeaf) {
        var queue = new Queue<(Vec3i Pos, int Depth)>();
        var visited = new HashSet<Vec3i>();
        
        queue.Enqueue((startLeaf, 0));
        visited.Add(startLeaf);

        while (queue.Count > 0) {
            var (pos, depth) = queue.Dequeue();
            ushort type = GetVoxel(pos).TypeId;

            if (type == GameData.BLog.Id) return true;
            if (depth >= 4) continue;

            var neighbors = new Vec3i[] {
                pos + new Vec3i(1, 0, 0),
                pos + new Vec3i(-1, 0, 0),
                pos + new Vec3i(0, 1, 0),
                pos + new Vec3i(0, -1, 0),
                pos + new Vec3i(0, 0, 1),
                pos + new Vec3i(0, 0, -1)
            };

            foreach (var n in neighbors) {
                if (visited.Contains(n)) continue;
                ushort nt = GetVoxel(n).TypeId;
                if (nt == GameData.BLeaves.Id || nt == GameData.BLog.Id) {
                    visited.Add(n);
                    queue.Enqueue((n, depth + 1));
                }
            }
        }

        return false;
    }

    /// <summary>Реестр декора (факелы, посевы, трава) для рендера без сканирования всех вокселей.</summary>
    public IEnumerable<Vec3i> DecorPositions => _decor.Values.SelectMany(v => v);

    private void ScanDecorations(GameChunk gc) {
        var cc = gc.Coord;
        var list = new List<Vec3i>();
        for (int i = 0; i < Chunk.VoxelCount; i++) {
            ushort t = gc.Chunk.Get(i).TypeId;
            if (t == GameData.BTorch.Id || t == GameData.BWheatCrop.Id || t == GameData.BTallGrass.Id)
                list.Add(ChunkLocalToWorld(cc, i));
        }
        _decor[cc] = list;
    }

    private void UpdateDecor(Vec3i cc, Vec3i w, in VoxelData voxel) {
        bool isDecor = voxel.TypeId == GameData.BTorch.Id || voxel.TypeId == GameData.BWheatCrop.Id || voxel.TypeId == GameData.BTallGrass.Id;
        if (!_decor.TryGetValue(cc, out var list)) {
            if (!isDecor) return;
            _decor[cc] = list = new List<Vec3i>();
        }
        if (isDecor) {
            if (!list.Contains(w)) list.Add(w);
        } else {
            list.Remove(w);
        }
    }

                    private void OnBlockChanged(Vec3i w, in VoxelData voxel) {
        var cc = Chunk.CoordOf(w);
        var gc = TryGetChunk(cc);
        if (gc == null) return;
        UpdateDecor(cc, w, in voxel);
        int lx = w.X & 31, lz = w.Z & 31;
        gc.RecomputeSurfaceColumn(lx, lz);

        LightEngine.RecomputeSun(gc, this);
        LightEngine.RecomputeBlock(gc, this);
        _meshDirty.Add(gc);

        // Помечаем соседние чанки грязными для мгновенного обновления видимых граней
        foreach (var n in NeighborCoords(cc)) {
            if (_chunks.TryGetValue(n, out var ngc)) {
                _meshDirty.Add(ngc);
            }
        }
        Fluids.ScheduleUpdate(w);
        Fluids.ScheduleUpdate(w + new Vec3i(0, 1, 0));
        Fluids.ScheduleUpdate(w + new Vec3i(0, -1, 0));
        Fluids.ScheduleUpdate(w + new Vec3i(1, 0, 0));
        Fluids.ScheduleUpdate(w + new Vec3i(-1, 0, 0));
        Fluids.ScheduleUpdate(w + new Vec3i(0, 0, 1));
        Fluids.ScheduleUpdate(w + new Vec3i(0, 0, -1));
    }

    public void MarkLightDirty(Vec3i w) {
        var gc = TryGetChunk(Chunk.CoordOf(w));
        if (gc != null) QueueLightRelight(gc);
    }

    /// <summary>Ставит чанк в очередь пересчёта света (без дублей в наборе).</summary>
    private void QueueLightRelight(GameChunk gc) {
        if (_lightDirty.Add(gc)) _lightOrder.Enqueue(gc);
    }

    /// <summary>Собирает грязные по мешам чанки в буфер вызывающего (без аллокаций в кадре).</summary>
    public void CollectMeshDirty(List<GameChunk> buffer) {
        buffer.AddRange(_meshDirty);
        _meshDirty.Clear();
    }

    public void MarkMeshDirtyAll() {
        foreach (var gc in _chunks.Values) _meshDirty.Add(gc);
    }

    // ── Луч (DDA) для выделения и установки блоков ───────────────────────────

    /// <summary>
    /// Бросает луч по сетке и находит первый блок (любой, не только твёрдый —
    /// листву и факелы тоже можно ломать). Ячейка, в которой начинается луч
    /// (глаз игрока), пропускается — нельзя подсветить/сломать блок, внутри
    /// которого находится камера.
    /// </summary>
    public bool RaycastBlock(Vector3 origin, Vector3 dir, float maxDist,
                             out Vec3i hit, out Vec3i prevCell, out Vec3i normal, bool hitFluids = false) {
        hit = default; prevCell = default; normal = default;
        int x = (int)MathF.Floor(origin.X), y = (int)MathF.Floor(origin.Y), z = (int)MathF.Floor(origin.Z);
        int stepX = dir.X > 0 ? 1 : -1, stepY = dir.Y > 0 ? 1 : -1, stepZ = dir.Z > 0 ? 1 : -1;
        float tMaxX = Bound(origin.X, dir.X), tMaxY = Bound(origin.Y, dir.Y), tMaxZ = Bound(origin.Z, dir.Z);
        float tDeltaX = dir.X != 0 ? MathF.Abs(1f / dir.X) : float.MaxValue;
        float tDeltaY = dir.Y != 0 ? MathF.Abs(1f / dir.Y) : float.MaxValue;
        float tDeltaZ = dir.Z != 0 ? MathF.Abs(1f / dir.Z) : float.MaxValue;
        Vec3i last = new(x, y, z);
        bool first = true;

        while (true) {
            var cell = new Vec3i(x, y, z);
            if (!first && IsTargetableBlock(cell, hitFluids)) {
                hit = cell;
                prevCell = last;
                normal = new Vec3i(x - last.X, y - last.Y, z - last.Z);
                return true;
            }
            first = false;
            if (tMaxX >= maxDist && tMaxY >= maxDist && tMaxZ >= maxDist) return false;
            last = cell;
            if (tMaxX < tMaxY && tMaxX < tMaxZ) { x += stepX; tMaxX += tDeltaX; }
            else if (tMaxY < tMaxZ) { y += stepY; tMaxY += tDeltaY; }
            else { z += stepZ; tMaxZ += tDeltaZ; }
        }
    }

    private static float Bound(float s, float d) => d == 0f ? float.MaxValue :
        d > 0f ? (MathF.Floor(s) + 1f - s) / d : (s - MathF.Floor(s)) / -d;

    // ── Сущности ─────────────────────────────────────────────────────────────

    public void SpawnPickup(ushort itemId, int quantity, Vec3i at) {
        if (itemId == 0 || !GameData.Items.TryGetValue(itemId, out var def)) return;
        // Слияние с близким пикапом того же предмета.
        var pos = new Vector3(at.X + 0.5f, at.Y + 0.5f, at.Z + 0.5f);
        foreach (var p in Pickups) {
            if (p.Item.Definition == def && Vector3.Distance(p.Position, pos) < 0.7f) {
                p.Quantity += quantity;
                return;
            }
        }
        Pickups.Add(new ItemPickup(GameData.NewItem(def), quantity, pos));
    }

    public void Tick(float dt, Player? player = null) {
        TickLight();
        // Пикапы (притяжение к игроку и сбор — в TickPickups).
        for (int i = Pickups.Count - 1; i >= 0; i--) {
            if (Pickups[i].Quantity <= 0) Pickups.RemoveAt(i);
        }

        // Жидкости (Вода и Лава)
        Fluids.Tick(dt);

        // Животные: спавн и тик.
        _animalSpawnTimer -= dt;
        if (_animalSpawnTimer <= 0f) {
            _animalSpawnTimer = 4f;
            TrySpawnAnimal();
        }
        foreach (var a in Animals) a.Tick(dt, this, player);
        Animals.RemoveAll(a => !a.Alive);

        // Падающие обломки (обратный цикл защищает от исключения при падении блоков сверху)
        for (int i = FallingBlocks.Count - 1; i >= 0; i--) {
            FallingBlocks[i].Tick(dt, this);
        }
        FallingBlocks.RemoveAll(f => !f.Alive);

        // Стриминг: выгрузка дальних чанков (агентам проще — не держать весь мир в памяти)
        if (player != null) {
            var pc = new Vec3i((int)MathF.Floor(player.Position.X) >> 5, (int)MathF.Floor(player.Position.Y) >> 5, (int)MathF.Floor(player.Position.Z) >> 5);
            TickStreaming(pc, dt);
        }

        // Огонь.

        // Рост посевов пшеницы на грядках
        TickCrops(dt);

        // Распространение травы на блоки земли и отмирание под твердыми блоками
        TickGrassSpread(dt);

        // Автономная фоновая плавка печей во всех активных блоках
        foreach (var fn in Furnaces.Values) fn.Tick(dt, this);
    }

    /// <summary>
    /// Пересчёт света с бюджетом за тик. FIFO-очередь без голодания: порядок
    /// вставки повторяет порядок событий, дальние секции гарантированно
    /// обрабатываются в ближайшие тики. Если у обработанного чанка остался
    /// необработанный сосед — чанк встаёт в хвост очереди, чтобы швы на
    /// границах сошлись после его пересчёта.
    /// </summary>
    private void TickLight() {
        const int budget = 6;

        _lightBatch.Clear();
        while (_lightBatch.Count < budget && _lightOrder.Count > 0) {
            var gc = _lightOrder.Dequeue();
            // Чанк могли выгрузить, пока он ждал в очереди (UnloadFarChunks
            // убирает его из набора) — такие записи просто пропускаем.
            if (!_lightDirty.Remove(gc)) continue;
            _lightBatch.Add(gc);
        }
        if (_lightBatch.Count == 0) return;

        // Сначала все поверхности партии, потом свет (соседние колонки читают свежую карту).
        foreach (var gc in _lightBatch) gc.RecomputeAllSurfaces();
        foreach (var gc in _lightBatch) {
            LightEngine.RecomputeSun(gc, this);
            LightEngine.RecomputeBlock(gc, this);
            _meshDirty.Add(gc);
        }

        // Сходимость швов на границах.
        foreach (var gc in _lightBatch) {
            foreach (var off in NeighborOffsets) {
                var nCoord = new Vec3i(gc.Coord.X + off.X, gc.Coord.Y + off.Y, gc.Coord.Z + off.Z);
                if (_chunks.TryGetValue(nCoord, out var ngc) && _lightDirty.Contains(ngc)) {
                    QueueLightRelight(gc);
                    break;
                }
            }
        }
    }

    public void TickGrassSpread(float dt) {
        _grassSpreadTimer -= dt;
        if (_grassSpreadTimer > 0f) return;
        _grassSpreadTimer = 0.35f;

        if (_chunks.Count == 0) return;
        _tempChunkList.Clear();
        foreach (var chunk in _chunks.Values) {
            _tempChunkList.Add(chunk);
        }
        if (_tempChunkList.Count == 0) return;

        // Берем случайные активные чанки за тик
        int chunksToTick = Math.Min(_tempChunkList.Count, 8);
        for (int c = 0; c < chunksToTick; c++) {
            var chunk = _tempChunkList[_random.Next(_tempChunkList.Count)];
            for (int r = 0; r < 4; r++) {
                int lx = _random.Next(Chunk.SizeX);
                int lz = _random.Next(Chunk.SizeZ);
                int ly = _random.Next(Chunk.SizeY);

                int wx = chunk.Coord.X * Chunk.SizeX + lx;
                int wz = chunk.Coord.Z * Chunk.SizeZ + lz;
                var blockPos = new Vec3i(wx, ly, wz);

                var vox = GetVoxel(blockPos);
                if (vox.TypeId == GameData.BGrass.Id) {
                    var abovePos = blockPos + new Vec3i(0, 1, 0);
                    var aboveVox = GetVoxel(abovePos);
                    var aboveBlock = GameData.GetBlock(aboveVox.TypeId);

                    // Если блок сверху непрозрачный и твердый — трава погибает и становится землей
                    if (aboveBlock != null && aboveBlock.IsSolid && aboveBlock.IsOpaque) {
                        PlacePlacedBlock(blockPos, GameData.BDirt);
                        continue;
                    }

                    // Попытка распространить траву на соседнюю землю (dx: -1..1, dz: -1..1, dy: -3..1)
                    int targetWx = wx + _random.Next(-1, 2);
                    int targetWz = wz + _random.Next(-1, 2);
                    int targetWy = ly + _random.Next(-3, 2);
                    var targetPos = new Vec3i(targetWx, targetWy, targetWz);

                    if (GetVoxel(targetPos).TypeId == GameData.BDirt.Id) {
                        var targetAbove = targetPos + new Vec3i(0, 1, 0);
                        var targetAboveVox = GetVoxel(targetAbove);
                        var targetAboveBlock = GameData.GetBlock(targetAboveVox.TypeId);

                        if (targetAboveBlock == null || !targetAboveBlock.IsSolid || !targetAboveBlock.IsOpaque) {
                            PlacePlacedBlock(targetPos, GameData.BGrass);
                        }
                    }
                }
            }
        }
    }

    public const float CropGrowthInterval = 30f;

    public void TickCrops(float dt) {
        _cropTimer -= dt;
        if (_cropTimer > 0f) return;
        _cropTimer = CropGrowthInterval;

        _tempDecorList.Clear();
        foreach (var pos in DecorPositions) {
            _tempDecorList.Add(pos);
        }

        for (int i = 0; i < _tempDecorList.Count; i++) {
            var pos = _tempDecorList[i];
            var vox = GetVoxel(pos);
            if (vox.TypeId == GameData.BWheatCrop.Id) {
                var below = pos + new Vec3i(0, -1, 0);
                var belowVox = GetVoxel(below);
                if (belowVox.TypeId != GameData.BFarmland.Id) {
                    // Без грядки посев ломается и падает семенами
                    SetBlock(pos, 0);
                    SpawnPickup(GameData.WheatSeedsItem.Id, 1, pos);
                    continue;
                }

                // Рост пшеницы: переход на следующую стадию каждые 30 секунд
                int currentStage = vox.SubGridLayerMask; // 0..3
                if (currentStage < 3) {
                    int nextStage = currentStage + 1;
                    var newVox = MakePlacedVoxel(GameData.BWheatCrop);
                    newVox.SubGridLayerMask = (byte)nextStage;
                    SetVoxelRaw(pos, in newVox);
                }
            }
        }
    }

    public void TickHostileMobs(float dt, Player player, GameSession session) {
        _hostileSpawnTimer -= dt;
        if (_hostileSpawnTimer <= 0f) {
            _hostileSpawnTimer = 8f;
            int nearbyHostiles = 0;
            foreach (var m in HostileMobs) {
                if (m.Alive && Vector3.DistanceSquared(m.Position, player.Position) < 45f * 45f) {
                    nearbyHostiles++;
                }
            }
            if (nearbyHostiles < 8) {
                TrySpawnHostileNearPlayer(player, session);
            }
        }
        _spawnerTimer -= dt;
        if (_spawnerTimer <= 0f) {
            _spawnerTimer = 4.0f + (float)_random.NextDouble() * 3.0f;
            TickNearbySpawners(player);
        }

        foreach (var h in HostileMobs) h.Tick(dt, this, player, session);
        HostileMobs.RemoveAll(h => !h.Alive);

        // Расталкивание мобов, животных и игрока
        ResolveEntityCollisions(player);

        // Обновление летящих стрел скелетов
        for (int i = Arrows.Count - 1; i >= 0; i--) {
            var arr = Arrows[i];
            arr.Tick(dt, this, player, session);
            if (!arr.Alive) Arrows.RemoveAt(i);
        }
    }

    /// <summary>
    /// Физическое расталкивание (Push physics / коллизия сущностей):
    /// Предотвращает прохождение мобов и животных друг сквозь друга.
    /// </summary>
    public void ResolveEntityCollisions(Player? player) {
        // 1. HostileMob vs HostileMob / Animal / Player
        for (int i = 0; i < HostileMobs.Count; i++) {
            var mobA = HostileMobs[i];
            if (!mobA.Alive) continue;

            for (int j = i + 1; j < HostileMobs.Count; j++) {
                var mobB = HostileMobs[j];
                if (!mobB.Alive) continue;
                PushEntities(ref mobA.Position, ref mobA.Velocity, mobA.HalfSizeX, mobA.HalfSizeY, mobA.HalfSizeZ,
                             ref mobB.Position, ref mobB.Velocity, mobB.HalfSizeX, mobB.HalfSizeY, mobB.HalfSizeZ);
            }

            // HostileMob vs Animals
            for (int k = 0; k < Animals.Count; k++) {
                var a = Animals[k];
                if (!a.Alive) continue;
                PushEntities(ref mobA.Position, ref mobA.Velocity, mobA.HalfSizeX, mobA.HalfSizeY, mobA.HalfSizeZ,
                             ref a.Position, ref a.Velocity, a.HalfSizeX, a.HalfSizeY, a.HalfSizeZ);
            }

            // HostileMob vs Player
            if (player != null) {
                var pPos = player.Position;
                var pVel = player.Velocity;
                PushEntities(ref mobA.Position, ref mobA.Velocity, mobA.HalfSizeX, mobA.HalfSizeY, mobA.HalfSizeZ,
                             ref pPos, ref pVel, Player.HalfExtents.X, Player.HalfExtents.Y, Player.HalfExtents.Z);
                player.Position = pPos;
                player.Velocity = pVel;
            }
        }

        // 2. Animal vs Animal
        for (int i = 0; i < Animals.Count; i++) {
            var a1 = Animals[i];
            if (!a1.Alive) continue;

            for (int j = i + 1; j < Animals.Count; j++) {
                var a2 = Animals[j];
                if (!a2.Alive) continue;
                PushEntities(ref a1.Position, ref a1.Velocity, a1.HalfSizeX, a1.HalfSizeY, a1.HalfSizeZ,
                             ref a2.Position, ref a2.Velocity, a2.HalfSizeX, a2.HalfSizeY, a2.HalfSizeZ);
            }

            // Animal vs Player
            if (player != null) {
                var pPos = player.Position;
                var pVel = player.Velocity;
                PushEntities(ref a1.Position, ref a1.Velocity, a1.HalfSizeX, a1.HalfSizeY, a1.HalfSizeZ,
                             ref pPos, ref pVel, Player.HalfExtents.X, Player.HalfExtents.Y, Player.HalfExtents.Z);
                player.Position = pPos;
                player.Velocity = pVel;
            }
        }
    }

    private void PushEntities(ref Vector3 posA, ref Vector3 velA, float hxA, float hyA, float hzA,
                              ref Vector3 posB, ref Vector3 velB, float hxB, float hyB, float hzB) {
        float dx = posA.X - posB.X;
        float dz = posA.Z - posB.Z;
        float minDistX = hxA + hxB;
        float minDistZ = hzA + hzB;
        float distSq = dx * dx + dz * dz;
        float minDist = MathF.Max(minDistX, minDistZ);

        if (distSq >= minDist * minDist || distSq < 0.00001f) return;

        float dist = MathF.Sqrt(distSq);
        float overlap = (minDist - dist) * 0.5f;
        float nx = dx / dist;
        float nz = dz / dist;

        var extA = new Vector3(hxA, hyA, hzA);
        var newPosA = posA + new Vector3(nx * overlap, 0f, nz * overlap);
        if (!Collision.IntersectsSolid(this, newPosA - extA, newPosA + extA) || Collision.IntersectsSolid(this, posA - extA, posA + extA)) {
            posA = newPosA;
            velA.X += nx * 0.8f;
            velA.Z += nz * 0.8f;
        }

        var extB = new Vector3(hxB, hyB, hzB);
        var newPosB = posB - new Vector3(nx * overlap, 0f, nz * overlap);
        if (!Collision.IntersectsSolid(this, newPosB - extB, newPosB + extB) || Collision.IntersectsSolid(this, posB - extB, posB + extB)) {
            posB = newPosB;
            velB.X -= nx * 0.8f;
            velB.Z -= nz * 0.8f;
        }
    }

    private void TrySpawnHostileNearPlayer(Player player, GameSession session) {
        var pPos = player.Position;
        float skyFactor = session.DayNight.SkyFactor;

        for (int attempt = 0; attempt < 8; attempt++) {
            float angle = (float)_random.NextDouble() * MathF.Tau;
            float r = 24f + (float)_random.NextDouble() * 18f; // 24..42м от игрока
            int spawnX = (int)MathF.Floor(pPos.X + MathF.Cos(angle) * r);
            int spawnZ = (int)MathF.Floor(pPos.Z + MathF.Sin(angle) * r);

            // Ищем подходящую высоту пола вокруг игрока (поверхность или пещера)
            int startY = Math.Clamp((int)pPos.Y + 8, 4, Chunk.SizeY * 3 - 4);
            int endY = Math.Clamp((int)pPos.Y - 12, 2, Chunk.SizeY * 3 - 4);

            for (int y = startY; y >= endY; y--) {
                var floorCell = new Vec3i(spawnX, y, spawnZ);
                var feetCell = new Vec3i(spawnX, y + 1, spawnZ);
                var headCell = new Vec3i(spawnX, y + 2, spawnZ);

                // Должен быть твердый пол и воздух на уровне ног и головы
                if (!IsSolidAt(floorCell)) continue;
                if (IsSolidAt(feetCell) || IsSolidAt(headCell)) continue;

                var feetVoxel = GetVoxel(feetCell);
                if (feetVoxel.TypeId == GameData.BWater.Id || feetVoxel.TypeId == GameData.BLava.Id) continue;

                // Проверка освещения: спавн только в глубокой темноте (свет <= 6). Факелы гарантированно защищают дом!
                byte blockLight = GetBlockLight(feetCell);
                byte sunLight = GetSunLight(feetCell);
                float effectiveLight = MathF.Max(blockLight, sunLight * skyFactor);
                if (blockLight >= 6 || effectiveLight > 6.0f) continue;

                HostileType type;
                if (Dimension == Dimension.Nether) {
                    double mobRoll = _random.NextDouble();
                    if (mobRoll < 0.70) type = HostileType.ZombiePigman;
                    else if (mobRoll < 0.88) type = HostileType.Blaze;
                    else type = HostileType.Skeleton;
                } else if (Dimension == Dimension.End) {
                    // Энд почти полностью населён эндэрменами
                    type = HostileType.Enderman;
                } else {
                    // Обычное измерение: эндэрмены появляются лишь изредка в темноте
                    type = (HostileType)_random.Next(0, 4);
                }

                var half = HostileMob.GetHalfSize(type);

                var spawnPos = new Vector3(spawnX + 0.5f, y + 1.0f + half.Y, spawnZ + 0.5f);

                // Обязательная проверка коллизии всего хитбокса моба
                if (!Collision.IntersectsSolid(this, spawnPos - half, spawnPos + half)) {
                    HostileMobs.Add(new HostileMob(type, spawnPos));
                    return;
                }
            }
        }
    }

    private float _spawnerTimer = 3.0f;

    private void TickNearbySpawners(Player player) {
        int px = (int)MathF.Floor(player.Position.X);
        int py = (int)MathF.Floor(player.Position.Y);
        int pz = (int)MathF.Floor(player.Position.Z);

        for (int dx = -14; dx <= 14; dx += 2) {
            for (int dz = -14; dz <= 14; dz += 2) {
                for (int dy = -6; dy <= 6; dy += 2) {
                    var spawnerPos = new Vec3i(px + dx, py + dy, pz + dz);
                    if (GetVoxel(spawnerPos).TypeId == GameData.BMobSpawner.Id) {
                        int spawnCount = _random.Next(1, 3);
                        for (int k = 0; k < spawnCount; k++) {
                            int sx = spawnerPos.X + _random.Next(-3, 4);
                            int sz = spawnerPos.Z + _random.Next(-3, 4);
                            int sy = spawnerPos.Y + _random.Next(-1, 2);

                            var feet = new Vec3i(sx, sy, sz);
                            var head = new Vec3i(sx, sy + 1, sz);
                            if (IsSolidAt(new Vec3i(sx, sy - 1, sz)) && !IsSolidAt(feet) && !IsSolidAt(head)) {
                                var mobType = Dimension == Dimension.Nether ? HostileType.Blaze : Dimension == Dimension.End ? HostileType.Enderman : (HostileType)_random.Next(0, 4);
                                float hy = HostileMob.GetHalfSize(mobType).Y;
                                var mPos = new Vector3(sx + 0.5f, sy + hy, sz + 0.5f);
                                if (HostileMobs.Count < 24) {
                                    HostileMobs.Add(new HostileMob(mobType, mPos));
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>Пикапы: покачивание, притяжение к игроку, сбор в инвентарь.</summary>
    public void TickPickups(float dt, Player player) {
        for (int i = Pickups.Count - 1; i >= 0; i--) {
            var p = Pickups[i];
            if (p.Quantity <= 0) { Pickups.RemoveAt(i); continue; }
            p.BobPhase += dt;
            p.Tick(dt, this, player);
        }
    }

    private void SpawnInitialChunkAnimals(GameChunk gc) {
        // Сбалансированный спавн животных (18% шанс на чанк, максимум 16 на мир)
        if (_random.NextDouble() > 0.18) return;
        if (Animals.Count >= 16) return;
        var animalType = (AnimalType)_random.Next(0, 3); // Pig, Cow, Sheep
        int count = _random.Next(2, 5); // 2..4 особи в стаде
        int baseLx = _random.Next(4, Chunk.SizeX - 4);
        int baseLz = _random.Next(4, Chunk.SizeZ - 4);

        for (int i = 0; i < count; i++) {
            int lx = Math.Clamp(baseLx + _random.Next(-3, 4), 0, Chunk.SizeX - 1);
            int lz = Math.Clamp(baseLz + _random.Next(-3, 4), 0, Chunk.SizeZ - 1);
            int wx = gc.Coord.X * Chunk.SizeX + lx;
            int wz = gc.Coord.Z * Chunk.SizeZ + lz;
            int surface = GetColumnSurfaceHeight(wx, wz);
            if (surface == int.MinValue || surface <= WorldGenerator.SeaLevel) continue;
            var biome = Generator.GetBiome(wx, surface, wz);
            if (biome == BiomeType.Ocean || biome == BiomeType.River || biome == BiomeType.Desert) continue;

            var surfaceBlock = GetVoxel(new Vec3i(wx, surface, wz));
            if (surfaceBlock.TypeId != GameData.BGrass.Id) continue;

            var anim = new Animal(animalType, Vector3.Zero);
            var pos = new Vector3(wx + 0.5f, surface + 1.0f + anim.HalfSizeY + 0.05f, wz + 0.5f);
            var half = new Vector3(anim.HalfSizeX, anim.HalfSizeY, anim.HalfSizeZ);
            if (!Collision.IntersectsSolid(this, pos - half, pos + half)) {
                anim.Position = pos;
                Animals.Add(anim);
            }
        }
    }

    private void TrySpawnAnimal() {
        if (Animals.Count >= 8 || _chunks.Count == 0) return;
        if (_random.NextDouble() > 0.15) return;
        // Случайный чанк без материализации списка (иначе аллокация каждые 4 секунды).
        int target = _random.Next(_chunks.Count);
        GameChunk? gc = null;
        foreach (var kv in _chunks) {
            if (target-- == 0) { gc = kv.Value; break; }
        }
        if (gc == null) return;
        if (gc.Coord.Y != 1) return;

        for (int attempt = 0; attempt < 4; attempt++) {
            int lx = _random.Next(0, Chunk.SizeX);
            int lz = _random.Next(0, Chunk.SizeZ);
            int wx = gc.Coord.X * Chunk.SizeX + lx;
            int wz = gc.Coord.Z * Chunk.SizeZ + lz;
            int surface = GetColumnSurfaceHeight(wx, wz);
            if (surface == int.MinValue || surface <= WorldGenerator.SeaLevel) continue;
            var biome = Generator.GetBiome(wx, surface, wz);
            if (biome == BiomeType.Ocean || biome == BiomeType.River || biome == BiomeType.Desert) continue;

            var surfaceBlock = GetVoxel(new Vec3i(wx, surface, wz));
            if (surfaceBlock.TypeId != GameData.BGrass.Id) continue;

            var animalType = (AnimalType)_random.Next(0, 3);
            var anim = new Animal(animalType, Vector3.Zero);
            var pos = new Vector3(wx + 0.5f, surface + 1.0f + anim.HalfSizeY + 0.05f, wz + 0.5f);
            var half = new Vector3(anim.HalfSizeX, anim.HalfSizeY, anim.HalfSizeZ);
            if (!Collision.IntersectsSolid(this, pos - half, pos + half)) {
                anim.Position = pos;
                Animals.Add(anim);
                return;
            }
        }
    }

    public FurnaceData GetOrCreateFurnace(Vec3i pos) {
        if (!Furnaces.TryGetValue(pos, out var f)) {
            f = new FurnaceData(pos);
            Furnaces[pos] = f;
        }
        return f;
    }

    public void Dispose() {
        foreach (var gc in _chunks.Values) gc.UnloadMesh();
        _chunks.Clear();
    }
}

/// <summary>
/// Автономная печь: плавит руду и пищу в фоне даже когда игрок закрыл экран или отошёл.
/// </summary>
public sealed class FurnaceData {
    public Vec3i Position;
    public ItemEntry? Input;
    public ItemEntry? Fuel;
    public ItemEntry? Output;
    public float FuelTimer;
    public float MaxFuelTimer = 1f;
    public float SmeltTimer;

    public FurnaceData(Vec3i pos) {
        Position = pos;
    }

    public void Tick(float dt, GameWorld world) {
        if (FuelTimer > 0f) {
            FuelTimer -= dt;
        }

        ItemDefinition? recipeOut = null;
        int recipeCount = 0;
        bool hasRecipe = false;
        if (Input != null && GameData.SmeltingRecipes.TryGetValue(Input.Value.Item.Definition.Id, out var recipe)) {
            hasRecipe = true;
            recipeOut = recipe.Output;
            recipeCount = recipe.Count;
        }

        bool canSmelt = hasRecipe && recipeOut != null && Input?.Quantity > 0 &&
                        (Output == null || (Output.Value.Item.Definition.Id == recipeOut.Id && Output.Value.Quantity + recipeCount <= 64));

        if (canSmelt) {
            if (FuelTimer <= 0f && Fuel != null && Fuel.Value.Quantity > 0) {
                float dur = (Fuel.Value.Item.Definition.Id == GameData.CoalItem.Id || Fuel.Value.Item.Definition.Id == GameData.CharcoalItem.Id) ? 80f :
                            (Fuel.Value.Item.Definition.Id == GameData.LogItem.Id || Fuel.Value.Item.Definition.Id == GameData.PlankItem.Id) ? 15f : 5f;
                FuelTimer = dur;
                MaxFuelTimer = dur;
                int remainingFuel = Fuel.Value.Quantity - 1;
                Fuel = remainingFuel > 0 ? Fuel.Value with { Quantity = remainingFuel } : null;
            }

            if (FuelTimer > 0f) {
                SmeltTimer += dt;
                if (SmeltTimer >= 8.0f) { // 8 секунд на выплавку 1 предмета
                    SmeltTimer = 0f;
                    int remainingIn = Input!.Value.Quantity - 1;
                    Input = remainingIn > 0 ? Input.Value with { Quantity = remainingIn } : null;

                    if (Output == null) {
                        Output = new ItemEntry(GameData.NewItem(recipeOut!), recipeCount);
                    } else {
                        Output = Output.Value with { Quantity = Output.Value.Quantity + recipeCount };
                    }
                }
            } else {
                SmeltTimer = MathF.Max(0f, SmeltTimer - dt * 2f);
            }
        } else {
            SmeltTimer = 0f;
        }
    }
}



