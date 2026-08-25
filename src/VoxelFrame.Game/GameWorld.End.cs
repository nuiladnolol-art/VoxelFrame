using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

// ── Энд: босс (Слизень Края) и эндер-кристаллы ─────────────────────────────
public sealed partial class GameWorld {
    /// <summary>Босс измерения — гигантский Слизень Края.</summary>
    public EndSlime? EndBoss;

    /// <summary>Повержен ли босс (персистентно).</summary>
    public bool EndBossDefeated;

    /// <summary>Позиции всех когда-либо сгенерированных эндер-кристаллов (для подсчёта живых).</summary>
    private readonly HashSet<Vec3i> _endCrystals = new();

    /// <summary>Регистрирует эндер-кристаллы в свежесозданном чанке Энда.</summary>
    private void ScanEndCrystals(GameChunk gc) {
        for (int x = 0; x < Chunk.SizeX; x++) {
            for (int z = 0; z < Chunk.SizeZ; z++) {
                for (int y = 0; y < Chunk.SizeY; y++) {
                    if (gc.Chunk.Get(x, y, z).TypeId == GameData.BEnderCrystal.Id) {
                        int wx = gc.Coord.X * Chunk.SizeX + x;
                        int wy = gc.Coord.Y * Chunk.SizeY + y;
                        int wz = gc.Coord.Z * Chunk.SizeZ + z;
                        _endCrystals.Add(new Vec3i(wx, wy, wz));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Число живых эндер-кристаллов (пока хоть один жив — Слизень Края лечится).
    /// Медленно пробегаем по набору позиций и проверяем, что блок остался кристаллом.
    /// </summary>
    public int CountAliveEndCrystals() {
        int count = 0;
        foreach (var p in _endCrystals) {
            if (GetVoxel(p).TypeId == GameData.BEnderCrystal.Id) count++;
        }
        return count;
    }

    /// <summary>Тик босса и спавн его при первом входе в измерение.</summary>
    public void TickEndSlime(float dt, Player player, GameSession session, Vector3 islandCenter, float islandTopY) {
        if (Dimension != Dimension.End || EndBossDefeated) return;

        if (EndBoss == null) {
            EndBoss = new EndSlime(islandCenter + new Vector3(24f, islandTopY + 30f, 0f), islandCenter, Seed ^ 0x5A1E5);
        }
        EndBoss.Tick(dt, this, player, session, islandCenter, islandTopY);
    }
}
