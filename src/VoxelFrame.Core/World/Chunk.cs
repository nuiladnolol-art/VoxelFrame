namespace VoxelFrame.Core.World;

/// <summary>
/// 32×32×32 voxel chunk. Y-major indexing: a vertical column — the stress
/// solver's hot path — is contiguous in memory.
/// VoxelData is 20 bytes → 655 KB per chunk; SubGrids are sparse.
/// </summary>
public sealed class Chunk {
    public const int SizeX = 32, SizeY = 32, SizeZ = 32;
    public const int VoxelCount = SizeX * SizeY * SizeZ;

    public readonly Vec3i Origin;
    /// Bumped on every write — the solver's re-solve key (stale-result detection).
    public int Version { get; private set; }

    private readonly VoxelData[] _voxels = new VoxelData[VoxelCount];   // 655 KB
    private readonly SubGrid?[] _subGrids = new SubGrid?[VoxelCount];   // sparse; arena-pooled in production

    public Chunk(Vec3i origin) => Origin = origin;

    public static Vec3i CoordOf(Vec3i worldPos) => new(worldPos.X >> 5, worldPos.Y >> 5, worldPos.Z >> 5);

    /// Y-major: index(x, y, z) = (x·32 + z)·32 + y  ⇒  y±1 is ±1 in memory.
    public static int Index(int x, int y, int z) => (x * SizeZ + z) * SizeY + y;
    public static (int X, int Y, int Z) LocalFromIndex(int i) =>
        (i / (SizeY * SizeZ), i % SizeY, (i / SizeY) % SizeZ);

    public ref VoxelData Get(int x, int y, int z) => ref _voxels[Index(x, y, z)];
    public ref VoxelData Get(int index) => ref _voxels[index];

    public SubGrid? GetSubGrid(int index) => _subGrids[index];

    public SubGrid GetOrCreateSubGrid(int index) {
        if (_subGrids[index] is null) {
            _subGrids[index] = new SubGrid();
            _voxels[index].Flags |= VoxelFlags.Occupied;
        }
        return _subGrids[index]!;
    }

    public void ReleaseSubGrid(int index) {
        if (_subGrids[index] != null) {
            _subGrids[index] = null;
            _voxels[index].Flags &= ~VoxelFlags.Occupied;
            _voxels[index].SubGridLayerMask = 0;
        }
    }

    /// <summary>
    /// Recomputes a framework cell's physical totals from its components.
    /// Call after component add/remove, then SetVoxel(pos, cell) — the voxel
    /// write notifies the physics layer exactly like any other change.
    /// </summary>
    public void RefreshPhysicals(int index) {
        var sg = _subGrids[index];
        if (sg is null) return;
        ref var v = ref _voxels[index];
        v.Weight = (float)sg.TotalMassKg;
        v.ContentVolumeM3 = (float)sg.TotalVolumeM3;
        v.LoadBearingCapacity = (float)sg.TotalCapacityKN;
        v.SubGridLayerMask = sg.OccupiedLayerMask;
        v.Flags |= VoxelFlags.Structural;
    }

    public void SetVoxel(int index, in VoxelData value) {
        if (_voxels[index].Equals(value)) return;
        _voxels[index] = value;
        if (!value.HasSubGrid) ReleaseSubGrid(index);
        Version++;
    }
}
