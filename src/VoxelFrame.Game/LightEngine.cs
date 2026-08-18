using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Освещение. Солнечный свет — строгая тень по карте поверхности (без
/// распространения). Блочный свет (факелы, костры) — BFS с затуханием 1 на
/// блок внутри чанка; через границы свет попадает из соседних чанков.
/// </summary>
public static class LightEngine {
    private static readonly Vec3i[] Dirs6 = {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    public static void RecomputeSun(GameChunk gc, GameWorld world) {
        int ox = gc.Coord.X * Chunk.SizeX;
        int oz = gc.Coord.Z * Chunk.SizeZ;
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                int wx = ox + lx;
                int wz = oz + lz;
                int surface = world.GetColumnSurfaceHeight(wx, wz);
                for (int ly = 0; ly < Chunk.SizeY; ly++) {
                    int wy = gc.Coord.Y * Chunk.SizeY + ly;
                    gc.SunLight[Chunk.Index(lx, ly, lz)] = (byte)(wy >= surface && surface != int.MinValue ? 15 : 0);
                }
            }
        }
    }

    /// <summary>Пересчитывает блочный свет чанка (источники + свет соседей на границе).</summary>
    public static void RecomputeBlock(GameChunk gc, GameWorld world) {
        Array.Clear(gc.BlockLight);

        var queue = new Queue<(int X, int Y, int Z, byte Level)>();
        // Источники внутри чанка.
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int ly = 0; ly < Chunk.SizeY; ly++) {
                for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                    int idx = Chunk.Index(lx, ly, lz);
                    ushort type = gc.Chunk.Get(idx).TypeId;
                    if (type == 0) continue;
                    byte level = GameData.GetBlock(type).LightLevel;
                    if (level > 0) {
                        gc.BlockLight[idx] = level;
                        queue.Enqueue((lx, ly, lz, level));
                    }
                }
            }
        }
        // Свет соседних чанков на границах (затухает на 1 при входе).
        int ox = gc.Coord.X * Chunk.SizeX, oy = gc.Coord.Y * Chunk.SizeY, oz = gc.Coord.Z * Chunk.SizeZ;
        for (int ly = 0; ly < Chunk.SizeY; ly++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                Seed(world, gc, queue, 0, ly, lz, new Vec3i(ox - 1, oy + ly, oz + lz));
                Seed(world, gc, queue, Chunk.SizeX - 1, ly, lz, new Vec3i(ox + Chunk.SizeX, oy + ly, oz + lz));
            }
        }
        for (int ly = 0; ly < Chunk.SizeY; ly++) {
            for (int lx = 0; lx < Chunk.SizeX; lx++) {
                Seed(world, gc, queue, lx, ly, 0, new Vec3i(ox + lx, oy + ly, oz - 1));
                Seed(world, gc, queue, lx, ly, Chunk.SizeZ - 1, new Vec3i(ox + lx, oy + ly, oz + Chunk.SizeZ));
            }
        }
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                Seed(world, gc, queue, lx, 0, lz, new Vec3i(ox + lx, oy - 1, oz + lz));
                Seed(world, gc, queue, lx, Chunk.SizeY - 1, lz, new Vec3i(ox + lx, oy + Chunk.SizeY, oz + lz));
            }
        }

        // BFS с затуханием.
        while (queue.Count > 0) {
            var (x, y, z, level) = queue.Dequeue();
            if (level <= 1) continue;
            foreach (var d in Dirs6) {
                int nx = x + d.X, ny = y + d.Y, nz = z + d.Z;
                if (nx < 0 || nx >= Chunk.SizeX || ny < 0 || ny >= Chunk.SizeY || nz < 0 || nz >= Chunk.SizeZ) continue;
                int idx = Chunk.Index(nx, ny, nz);
                if (gc.BlockLight[idx] >= level - 1) continue;
                var v = gc.Chunk.Get(idx);
                if (v.TypeId != 0 && GameData.GetBlock(v.TypeId).IsOpaque) continue; // свет не проходит
                gc.BlockLight[idx] = (byte)(level - 1);
                queue.Enqueue((nx, ny, nz, (byte)(level - 1)));
            }
        }
    }

    /// <summary>Подмешивает свет из соседнего чанка в приграничную ячейку.</summary>
    private static void Seed(GameWorld world, GameChunk gc, Queue<(int, int, int, byte)> queue,
                             int lx, int ly, int lz, Vec3i outsideWorld) {
        byte incoming = world.GetBlockLight(outsideWorld);
        if (incoming <= 1) return;
        int idx = Chunk.Index(lx, ly, lz);
        var v = gc.Chunk.Get(idx);
        if (v.TypeId != 0 && GameData.GetBlock(v.TypeId).IsOpaque) return;
        if (incoming - 1 > gc.BlockLight[idx]) {
            gc.BlockLight[idx] = (byte)(incoming - 1);
            queue.Enqueue((lx, ly, lz, (byte)(incoming - 1)));
        }
    }
}
