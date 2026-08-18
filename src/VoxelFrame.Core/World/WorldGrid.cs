namespace VoxelFrame.Core.World;

/// <summary>What the rest of the engine may know about the world.</summary>
public interface IChunkSource {
    event Action<Vec3i, VoxelData, VoxelData>? VoxelChanged;   // (worldPos, before, after)
    bool TryGetVoxel(Vec3i worldPos, out VoxelData voxel);
    Chunk GetOrCreateChunk(Vec3i chunkCoord);
    bool TryGetChunk(Vec3i chunkCoord, out Chunk chunk);
}

/// <summary>
/// Sparse in-memory chunk store (replaced by streaming later). The physics
/// layer depends on IChunkSource, never on this class.
/// </summary>
public sealed class WorldGrid : IChunkSource {
    private readonly Dictionary<Vec3i, Chunk> _chunks = new();

    public event Action<Vec3i, VoxelData, VoxelData>? VoxelChanged;

    public Chunk GetOrCreateChunk(Vec3i chunkCoord) {
        if (!_chunks.TryGetValue(chunkCoord, out var chunk)) {
            chunk = new Chunk(chunkCoord);
            _chunks.Add(chunkCoord, chunk);
        }
        return chunk;
    }

    public bool TryGetChunk(Vec3i chunkCoord, out Chunk chunk) => _chunks.TryGetValue(chunkCoord, out chunk!);

    public bool TryGetVoxel(Vec3i world, out VoxelData voxel) {
        if (!_chunks.TryGetValue(Chunk.CoordOf(world), out var chunk)) {
            voxel = VoxelData.Air;
            return false;
        }
        voxel = chunk.Get(world.X & 31, world.Y & 31, world.Z & 31);
        return true;
    }

    /// The single write path: all voxel mutations flow through here so the
    /// physics layer sees every change as (before, after).
    public void SetVoxel(Vec3i world, in VoxelData value) {
        var chunk = GetOrCreateChunk(Chunk.CoordOf(world));
        int idx = Chunk.Index(world.X & 31, world.Y & 31, world.Z & 31);
        var before = chunk.Get(idx);
        chunk.SetVoxel(idx, in value);
        VoxelChanged?.Invoke(world, before, value);
    }
}
