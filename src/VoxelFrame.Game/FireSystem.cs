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
    public double TotalSmokeKg;

    public FireSystem(GameWorld world) => _world = world;

    public void RegisterCampfire(Vec3i pos) => Campfires.Add(pos);
    public void UnregisterCampfire(Vec3i pos) => Campfires.Remove(pos);

    /// <summary>Поджечь блок, если он горючий.</summary>
    public void Ignite(Vec3i pos) {
        if (Burning.ContainsKey(pos)) return;
        var block = _world.GetBlockType(pos);
        if (block == null || !block.IsFlammable) return;
        Burning[pos] = block.BurnTimeSeconds;
        _world.MarkLightDirty(pos);
    }

    public void Extinguish(Vec3i pos) {
        if (Burning.Remove(pos)) _world.MarkLightDirty(pos);
    }

    public void Tick(float dt) {
        _spreadTimer -= dt;
        if (_spreadTimer <= 0f) {
            _spreadTimer = 0.6f;
            Spread();
        }

        if (Burning.Count == 0) return;
        foreach (var pos in Burning.Keys.ToList()) {
            float remaining = Burning[pos] - dt;
            if (remaining > 0f) {
                Burning[pos] = remaining;
                continue;
            }
            BurnOut(pos);
        }
    }

    private void Spread() {
        // Поджигание от костров (4 стороны).
        foreach (var camp in Campfires.ToList()) {
            TryIgnite(camp + new Vec3i(1, 0, 0));
            TryIgnite(camp + new Vec3i(-1, 0, 0));
            TryIgnite(camp + new Vec3i(0, 0, 1));
            TryIgnite(camp + new Vec3i(0, 0, -1));
        }
        // Распространение от горящих блоков (6 соседей).
        foreach (var pos in Burning.Keys.ToList()) {
            foreach (var d in WorldGridDirs) {
                if (_random.NextDouble() < 0.25) TryIgnite(pos + d);
            }
        }
        // Поджигание от лавы (соседние горючие блоки)
        foreach (var lavaPos in _world.Fluids.ActiveLava.ToList()) {
            foreach (var d in WorldGridDirs) {
                var np = lavaPos + d;
                var block = _world.GetBlockType(np);
                if (block != null && block.IsFlammable) {
                    if (_random.NextDouble() < 0.35) Ignite(np);
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
        var block = _world.GetBlockType(pos);
        if (block == null) { Burning.Remove(pos); return; }
        Burning.Remove(pos);

        double mass = _world.GetVoxel(pos).Weight;
        _world.RemoveBlock(pos);
        TotalSmokeKg += mass;
        _world.MarkLightDirty(pos);
    }
}
