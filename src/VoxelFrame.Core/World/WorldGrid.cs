namespace VoxelFrame.Core.World;

/// <summary>
/// Разрежённое (in-memory) хранилище чанков.
/// Единый путь записи: все изменения вокселей проходят через SetVoxel,
/// чтобы слой освещения/мешей видел каждую пару (before, after).
/// </summary>
public sealed class WorldGrid {
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

    public Chunk? GetChunk(Vec3i chunkCoord) => _chunks.TryGetValue(chunkCoord, out var c) ? c : null;

    public bool TryGetVoxel(Vec3i world, out VoxelData voxel) {
        if (!_chunks.TryGetValue(Chunk.CoordOf(world), out var chunk)) {
            voxel = VoxelData.Air;
            return false;
        }
        voxel = chunk.Get(world.X & 31, world.Y & 31, world.Z & 31);
        return true;
    }

    public void SetVoxel(Vec3i world, in VoxelData value) {
        var chunk = GetOrCreateChunk(Chunk.CoordOf(world));
        int idx = Chunk.Index(world.X & 31, world.Y & 31, world.Z & 31);
        var before = chunk.Get(idx);
        chunk.SetVoxel(idx, in value);
        VoxelChanged?.Invoke(world, before, value);
    }
}
