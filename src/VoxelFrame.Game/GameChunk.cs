using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Игровой чанк: ядро (Core.Chunk) + освещение + карта поверхности + GPU-меш.
/// </summary>
public sealed class GameChunk {
    public readonly Vec3i Coord;
    public readonly Chunk Chunk;

    public readonly byte[] SunLight = new byte[Chunk.VoxelCount];
    public readonly byte[] BlockLight = new byte[Chunk.VoxelCount];
    /// Мировая y верхнего твёрдого блока для каждой колонки (x, z) чанка.
    public readonly int[] Surface = new int[Chunk.SizeX * Chunk.SizeZ];

    public readonly List<Mesh> Meshes = new();
    public bool MeshUploaded;
    public bool MeshDirty = true;
    public bool LightDirty = true;

    public GameChunk(Vec3i coord, Chunk chunk) {
        Coord = coord;
        Chunk = chunk;
        Array.Fill(Surface, int.MinValue);
    }

    public int SurfaceIndex(int lx, int lz) => lz * Chunk.SizeX + lx;

    /// <summary>Пересчитывает карту поверхности колонки (x, z) по данным чанка.</summary>
    public void RecomputeSurfaceColumn(int lx, int lz) {
        int surface = int.MinValue;
        for (int ly = Chunk.SizeY - 1; ly >= 0; ly--) {
            var v = Chunk.Get(lx, ly, lz);
            if (v.TypeId != 0) {
                var b = GameData.GetBlock(v.TypeId);
                if (b.IsOpaque) {
                    surface = Coord.Y * Chunk.SizeY + ly;
                    break;
                }
            }
        }
        Surface[SurfaceIndex(lx, lz)] = surface;
    }

    public void RecomputeAllSurfaces() {
        for (int lx = 0; lx < Chunk.SizeX; lx++)
            for (int lz = 0; lz < Chunk.SizeZ; lz++)
                RecomputeSurfaceColumn(lx, lz);
    }

    public void UnloadMesh() {
        if (MeshUploaded) {
            foreach (var m in Meshes) Raylib.UnloadMesh(m);
            MeshUploaded = false;
        }
        Meshes.Clear();
    }
}
