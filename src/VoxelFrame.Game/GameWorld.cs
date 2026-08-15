using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.Physics;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Игровой мир: чанки, генерация, запись блоков (единый путь → физика),
/// освещение, сущности, огонь, интеграция stress-солвера.
/// </summary>
public sealed class GameWorld : IDisposable {
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
    public Vec3i SpawnBlock;

    private readonly WorldGrid _grid = new();
    private readonly Dictionary<Vec3i, GameChunk> _chunks = new();
    private readonly Dictionary<Vec3i, List<Vec3i>> _decor = new();   // чанк → позиции факелов/костров
    private readonly PhysicsGridCoordinator _physics;
    private readonly HashSet<GameChunk> _lightDirty = new();
    private readonly HashSet<GameChunk> _meshDirty = new();
    private readonly Random _random;
    private float _animalSpawnTimer = 3f;
    private float _hostileSpawnTimer = 5f;

    public GameWorld(int seed) {
        Seed = seed;
        Generator = new WorldGenerator(seed);
        _random = new Random(seed ^ 0x5F3759DF);
        _physics = new PhysicsGridCoordinator(_grid);
        Fire = new FireSystem(this);
        Fluids = new FluidEngine(this);
    }

    public PhysicsGridCoordinator Physics => _physics;
    public IReadOnlyCollection<GameChunk> Chunks => _chunks.Values;

    // ── Чанки ────────────────────────────────────────────────────────────────

    public GameChunk? TryGetChunk(Vec3i cc) => _chunks.TryGetValue(cc, out var gc) ? gc : null;

    public GameChunk GetOrCreateChunk(Vec3i cc) {
        if (_chunks.TryGetValue(cc, out var gc)) return gc;
        var core = _grid.GetOrCreateChunk(cc);
        bool isNew = core.Version == 0;
        if (isNew) Generator.GenerateChunk(core);   // новый чанк — генерация
        gc = new GameChunk(cc, core);
        gc.RecomputeAllSurfaces();
        _chunks.Add(cc, gc);
        ScanDecorations(gc);
        if (isNew && cc.Y == 1) {
            SpawnInitialChunkAnimals(gc);
        }
        _lightDirty.Add(gc);
        _meshDirty.Add(gc);
        // Границы соседей могли измениться — их меши тоже пересобрать.
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

    /// <summary>Генерирует чанки вокруг позиции игрока в радиусе видимости.</summary>
    public void EnsureLoadedAround(Vector3 playerPos, int radius = RenderDistance) {
        var pc = Chunk.CoordOf(new Vec3i((int)MathF.Floor(playerPos.X), (int)MathF.Floor(playerPos.Y), (int)MathF.Floor(playerPos.Z)));
        for (int dx = -radius; dx <= radius; dx++) {
            for (int dz = -radius; dz <= radius; dz++) {
                for (int dy = -1; dy <= 2; dy++) {
                    GetOrCreateChunk(new Vec3i(pc.X + dx, pc.Y + dy, pc.Z + dz));
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
        return (v.Flags & VoxelFlags.Solid) != 0;
    }

    /// <summary>Есть ли в ячейке вообще какой-либо блок (для прицела/ломания).</summary>
    public bool IsBlockAt(Vec3i w) => GetVoxel(w).TypeId != 0;

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

    /// <summary>VoxelData установленного игроком блока (Minecraft-стандарт: без структурных напряжений, 1 блок = 1 ячейка).</summary>
    public static VoxelData MakePlacedVoxel(BlockType block, float contentVolumeM3 = 1f, byte facing = 0) {
        var flags = VoxelFlags.None;
        if (block.IsSolid) flags |= VoxelFlags.Solid;
        return new VoxelData {
            TypeId = block.Id,
            Flags = flags,
            SubGridLayerMask = facing,
            Weight = 0f,
            ContentVolumeM3 = 1f,
            LoadBearingCapacity = 0f,
            SubGridIndex = -1,
        };
    }

    public void PlacePlacedBlock(Vec3i w, BlockType block, float contentVolumeM3 = 1f, byte facing = 0) {
        SetVoxelInternal(w, MakePlacedVoxel(block, contentVolumeM3, facing));
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
    }

    public Container GetOrCreateChest(Vec3i pos) {
        if (Chests.TryGetValue(pos, out var inv)) return inv;
        var newInv = new Container(1000000.0, 1000000.0);
        // Если сундук сгенерирован в шахте, наполняем каноничным шахтным лутом
        if (pos.Y >= 17 && pos.Y <= 20) {
            var rng = new Random(Seed ^ (pos.X * 73856093 ^ pos.Y * 19349663 ^ pos.Z * 83492791));
            newInv.InsertAt(0, new ItemEntry(GameData.NewItem(GameData.IronIngotItem), rng.Next(1, 5)));
            newInv.InsertAt(1, new ItemEntry(GameData.NewItem(GameData.CoalItem), rng.Next(3, 9)));
            newInv.InsertAt(2, new ItemEntry(GameData.NewItem(GameData.TorchItem), rng.Next(4, 13)));
            newInv.InsertAt(3, new ItemEntry(GameData.NewItem(GameData.BreadItem), rng.Next(1, 4)));
            newInv.InsertAt(4, new ItemEntry(GameData.NewItem(GameData.StringItem), rng.Next(1, 5)));
            if (rng.NextDouble() < 0.25)
                newInv.InsertAt(8, new ItemEntry(GameData.NewItem(GameData.GoldIngotItem), rng.Next(1, 3)));
            if (rng.NextDouble() < 0.10)
                newInv.InsertAt(13, new ItemEntry(GameData.NewItem(GameData.DiamondItem), 1));
        }
        Chests[pos] = newInv;
        return newInv;
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

    /// <summary>Загрузка чанка из сохранения: типы + частичные объёмы содержимого.</summary>
    public void LoadChunk(Vec3i cc, ushort[] types, Dictionary<int, float> partialContent) {
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
            float content = partialContent.TryGetValue(i, out float c) ? c : 1f;
            var pv = MakePlacedVoxel(block, content);
            core.SetVoxel(i, in pv);
        }
        _physics.RegisterChunk(core);
        var gc = new GameChunk(cc, core);
        gc.RecomputeAllSurfaces();
        _chunks[cc] = gc;
        ScanDecorations(gc);
        _lightDirty.Add(gc);
        _meshDirty.Add(gc);
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
        _grid.SetVoxel(w, in voxel);   // событие VoxelChanged → синхронизация солвера
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

    /// <summary>Реестр декора (факелы/костры) для рендера без сканирования вокселей.</summary>
    public IEnumerable<Vec3i> DecorPositions => _decor.Values.SelectMany(v => v);

    private void ScanDecorations(GameChunk gc) {
        var cc = gc.Coord;
        var list = new List<Vec3i>();
        for (int i = 0; i < Chunk.VoxelCount; i++) {
            ushort t = gc.Chunk.Get(i).TypeId;
            if (t == GameData.BTorch.Id)
                list.Add(ChunkLocalToWorld(cc, i));
        }
        _decor[cc] = list;
    }

    private void UpdateDecor(Vec3i cc, Vec3i w, in VoxelData voxel) {
        bool isDecor = voxel.TypeId == GameData.BTorch.Id;
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
        gc.RecomputeSurfaceColumn(w.X & 31, w.Z & 31);
        _lightDirty.Add(gc);
        _meshDirty.Add(gc);

        // Помечаем всю вертикальную колонку чанков для корректного пересчета солнечного света
        for (int dy = -2; dy <= 3; dy++) {
            if (_chunks.TryGetValue(new Vec3i(cc.X, dy, cc.Z), out var cCol)) {
                _lightDirty.Add(cCol);
                _meshDirty.Add(cCol);
            }
        }

        foreach (var n in NeighborCoords(cc)) {
            if (_chunks.TryGetValue(n, out var ngc)) {
                _lightDirty.Add(ngc);
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
        if (gc != null) _lightDirty.Add(gc);
    }

    /// <summary>Грязные по свету чанки (обрабатываются в тике).</summary>
    public IEnumerable<GameChunk> DrainLightDirty() {
        var list = _lightDirty.ToList();
        _lightDirty.Clear();
        return list;
    }

    /// <summary>Грязные по мешам чанки (обрабатываются рендерером).</summary>
    public IEnumerable<GameChunk> DrainMeshDirty() {
        var list = _meshDirty.ToList();
        _meshDirty.Clear();
        return list;
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
                             out Vec3i hit, out Vec3i prevCell, out Vec3i normal) {
        hit = default; prevCell = default; normal = default;
        int x = (int)MathF.Floor(origin.X), y = (int)MathF.Floor(origin.Y), z = (int)MathF.Floor(origin.Z);
        int stepX = dir.X > 0 ? 1 : -1, stepY = dir.Y > 0 ? 1 : -1, stepZ = dir.Z > 0 ? 1 : -1;
        float tMaxX = Bound(origin.X, dir.X), tMaxY = Bound(origin.Y, dir.Y), tMaxZ = Bound(origin.Z, dir.Z);
        float tDeltaX = dir.X != 0 ? MathF.Abs(1f / dir.X) : float.MaxValue;
        float tDeltaY = dir.Y != 0 ? MathF.Abs(1f / dir.Y) : float.MaxValue;
        float tDeltaZ = dir.Z != 0 ? MathF.Abs(1f / dir.Z) : float.MaxValue;
        Vec3i last = new(x, y, z);

        while (true) {
            var cell = new Vec3i(x, y, z);
            if (IsBlockAt(cell)) {
                hit = cell;
                prevCell = last;
                normal = new Vec3i(x - last.X, y - last.Y, z - last.Z);
                return true;
            }
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
            if (p.Definition == def && Vector3.Distance(p.Position, pos) < 0.7f) {
                p.Quantity += quantity;
                return;
            }
        }
        Pickups.Add(new ItemPickup(def, quantity, pos));
    }

    public void Tick(float dt, Player? player = null) {
        // Освещение.
        foreach (var gc in DrainLightDirty()) {
            gc.RecomputeAllSurfaces();
            LightEngine.RecomputeSun(gc, this);
            LightEngine.RecomputeBlock(gc, this);
        }

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

        // Падающие обломки.
        foreach (var f in FallingBlocks) f.Tick(dt, this);
        FallingBlocks.RemoveAll(f => !f.Alive);

        // Огонь.
        Fire.Tick(dt);

        // Автономная фоновая плавка печей во всех активных блоках
        foreach (var fn in Furnaces.Values) fn.Tick(dt, this);
    }

    public void TickHostileMobs(float dt, Player player, GameSession session) {
        _hostileSpawnTimer -= dt;
        if (_hostileSpawnTimer <= 0f) {
            _hostileSpawnTimer = 4f;
            if (HostileMobs.Count < 10) {
                TrySpawnHostileNearPlayer(player, session);
            }
        }
        foreach (var h in HostileMobs) h.Tick(dt, this, player, session);
        HostileMobs.RemoveAll(h => !h.Alive);

        // Обновление летящих стрел скелетов
        for (int i = Arrows.Count - 1; i >= 0; i--) {
            var arr = Arrows[i];
            arr.Tick(dt, this, player, session);
            if (!arr.Alive) Arrows.RemoveAt(i);
        }
    }

    private void TrySpawnHostileNearPlayer(Player player, GameSession session) {
        var pPos = player.Position;
        float skyFactor = session.DayNight.SkyFactor;

        for (int attempt = 0; attempt < 8; attempt++) {
            float angle = (float)_random.NextDouble() * MathF.Tau;
            float r = 14f + (float)_random.NextDouble() * 16f; // 14..30м от игрока
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

                // Проверка освещения: спавн только в темноте (свет <= 7)
                byte blockLight = GetBlockLight(feetCell);
                byte sunLight = GetSunLight(feetCell);
                float effectiveLight = MathF.Max(blockLight, sunLight * skyFactor);
                if (effectiveLight > 7.0f) continue;

                var type = (HostileType)_random.Next(0, 4);
                float halfX = type == HostileType.Spider ? 0.65f : 0.4f;
                float halfY = type == HostileType.Spider ? 0.35f : 0.85f;
                float halfZ = type == HostileType.Spider ? 0.65f : 0.4f;
                var half = new Vector3(halfX, halfY, halfZ);

                var spawnPos = new Vector3(spawnX + 0.5f, y + 1.0f + halfY, spawnZ + 0.5f);

                // Обязательная проверка коллизии всего хитбокса моба
                if (!Collision.IntersectsSolid(this, spawnPos - half, spawnPos + half)) {
                    HostileMobs.Add(new HostileMob(type, spawnPos));
                    return;
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
        // Шанс 30% спавна стада животных в новом чанке
        if (_random.NextDouble() > 0.30) return;
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
            if (biome == BiomeType.Ocean || biome == BiomeType.River) continue;

            var surfaceBlock = GetVoxel(new Vec3i(wx, surface, wz));
            if (surfaceBlock.TypeId == GameData.BLeaves.Id || surfaceBlock.TypeId == GameData.BLog.Id ||
                surfaceBlock.TypeId == GameData.BLava.Id || surfaceBlock.TypeId == GameData.BWater.Id) continue;

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
        if (Animals.Count >= 20 || _chunks.Count == 0) return;
        var gcList = _chunks.Values.ToList();
        var gc = gcList[_random.Next(gcList.Count)];
        if (gc.Coord.Y != 1) return;

        for (int attempt = 0; attempt < 6; attempt++) {
            int lx = _random.Next(0, Chunk.SizeX);
            int lz = _random.Next(0, Chunk.SizeZ);
            int wx = gc.Coord.X * Chunk.SizeX + lx;
            int wz = gc.Coord.Z * Chunk.SizeZ + lz;
            int surface = GetColumnSurfaceHeight(wx, wz);
            if (surface == int.MinValue || surface <= WorldGenerator.SeaLevel) continue;
            var biome = Generator.GetBiome(wx, surface, wz);
            if (biome == BiomeType.Ocean || biome == BiomeType.River) continue;

            var surfaceBlock = GetVoxel(new Vec3i(wx, surface, wz));
            if (surfaceBlock.TypeId == GameData.BLeaves.Id || surfaceBlock.TypeId == GameData.BLog.Id ||
                surfaceBlock.TypeId == GameData.BLava.Id || surfaceBlock.TypeId == GameData.BWater.Id) continue;

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

    // ── Интеграция stress-солвера: обрушение → падающие обломки ─────────────

    public void ProcessSolverEvents() {
        while (_physics.TryDequeueEvent(out _)) {}
    }

    public FurnaceData GetOrCreateFurnace(Vec3i pos) {
        if (!Furnaces.TryGetValue(pos, out var f)) {
            f = new FurnaceData(pos);
            Furnaces[pos] = f;
        }
        return f;
    }

    public void Dispose() {
        _physics.Dispose();
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
