using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Огонь: костёр поджигает горючие блоки, горение распространяется и
/// превращает древесину в золу. Учёт массы: остаток уходит «в дым»
/// (горение — единственный санкционированный сток массы, фиксируется в статистике).
/// </summary>
public sealed class FireSystem {
    private readonly GameWorld _world;
    private readonly Random _random = new(777);
    private float _spreadTimer = 0.5f;

    /// <summary>Позиции горящих блоков → оставшееся время горения, сек.</summary>
    public readonly Dictionary<Vec3i, float> Burning = new();
    /// <summary>Позиции костров (источники поджигания).</summary>
    public readonly HashSet<Vec3i> Campfires = new();

    public FireSystem(GameWorld world) => _world = world;

    public void RegisterCampfire(Vec3i pos) => Campfires.Add(pos);
    public void UnregisterCampfire(Vec3i pos) => Campfires.Remove(pos);

    /// <summary>Поджечь блок, если он горючий или воздух над/рядом с горючим.</summary>
    public void Ignite(Vec3i pos) {
        if (Burning.ContainsKey(pos)) return;
        var vox = _world.GetVoxel(pos);
        if (vox.TypeId == GameData.BFire.Id) {
            Burning[pos] = 10f;
            return;
        }
        var block = _world.GetBlockType(pos);
        if (block == null) return;
        if (block.IsFlammable) {
            Burning[pos] = MathF.Max(3f, block.BurnTimeSeconds);
            _world.MarkLightDirty(pos);
        }
    }

    public void Extinguish(Vec3i pos) {
        if (Burning.Remove(pos)) {
            var vox = _world.GetVoxel(pos);
            if (vox.TypeId == GameData.BFire.Id) {
                _world.RemoveBlock(pos);
            }
            _world.MarkLightDirty(pos);
        }
    }

    public void Tick(float dt) {
        _spreadTimer -= dt;
        if (_spreadTimer <= 0f) {
            _spreadTimer = 0.45f;
            Spread();
        }

        if (Burning.Count == 0) return;
        foreach (var pos in Burning.Keys.ToList()) {
            if (!Burning.TryGetValue(pos, out float timeRemaining)) continue;
            float remaining = timeRemaining - dt;
            if (remaining > 0f) {
                Burning[pos] = remaining;
                continue;
            }
            BurnOut(pos);
        }
    }

    private void Spread() {
        // Поджигание от костров (соседние блоки)
        foreach (var camp in Campfires.ToList()) {
            foreach (var d in WorldGridDirs) {
                TryIgnite(camp + d);
            }
        }
        // Распространение от горящих блоков и огня
        foreach (var pos in Burning.Keys.ToList()) {
            // Проверяем соседей в радиусе 1 блока во все стороны
            foreach (var d in WorldGridDirs) {
                var target = pos + d;
                var vox = _world.GetVoxel(target);
                if (vox.TypeId == 0) {
                    // Если рядом воздух, проверяем, есть ли горючие блоки вокруг этого воздуха
                    bool hasFlammableNearby = false;
                    foreach (var nd in WorldGridDirs) {
                        var nb = _world.GetBlockType(target + nd);
                        if (nb != null && nb.IsFlammable) {
                            hasFlammableNearby = true;
                            break;
                        }
                    }
                    if (hasFlammableNearby && _random.NextDouble() < 0.25) {
                        _world.PlacePlacedBlock(target, GameData.BFire);
                        Burning[target] = 8f;
                        _world.MarkLightDirty(target);
                    }
                } else {
                    var blk = _world.GetBlockType(target);
                    if (blk != null && blk.IsFlammable && _random.NextDouble() < 0.35) {
                        Ignite(target);
                    }
                }
            }
        }
        // Поджигание от лавы
        foreach (var lavaPos in _world.Fluids.ActiveLava.ToList()) {
            foreach (var d in WorldGridDirs) {
                var np = lavaPos + d;
                var block = _world.GetBlockType(np);
                if (block != null && block.IsFlammable && _random.NextDouble() < 0.35) {
                    Ignite(np);
                }
            }
        }
    }

    private void TryIgnite(Vec3i pos) {
        if (_random.NextDouble() < 0.35) Ignite(pos);
    }

    private static readonly Vec3i[] WorldGridDirs = {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    private void BurnOut(Vec3i pos) {
        Burning.Remove(pos);
        var vox = _world.GetVoxel(pos);
        if (vox.TypeId == 0) return;

        if (vox.TypeId == GameData.BFire.Id) {
            _world.RemoveBlock(pos);
            _world.MarkLightDirty(pos);
            return;
        }

        var block = _world.GetBlockType(pos);
        if (block != null) {
            _world.RemoveBlock(pos);
            _world.MarkLightDirty(pos);
            // С шансом 40% на месте сгоревшего дерева/листвы остается догорающий огонь BFire
            if (_random.NextDouble() < 0.40) {
                _world.PlacePlacedBlock(pos, GameData.BFire);
                Burning[pos] = 6f;
            }
        }
    }
}
