namespace VoxelFrame.Core.World;

/// <summary>
/// 32×32×32 воксельный чанк. Y-major индексация: вертикальный столбец
/// непрерывен в памяти.
/// </summary>
public sealed class Chunk {
    public const int SizeX = 32, SizeY = 32, SizeZ = 32;
    public const int VoxelCount = SizeX * SizeY * SizeZ;

    public readonly Vec3i Origin;
    /// Увеличивается при каждой записи — ключ пересборки меша.
    public int Version { get; private set; }

    private readonly VoxelData[] _voxels = new VoxelData[VoxelCount];

    public Chunk(Vec3i origin) => Origin = origin;

    public static Vec3i CoordOf(Vec3i worldPos) => new(worldPos.X >> 5, worldPos.Y >> 5, worldPos.Z >> 5);

    /// Y-major: index(x, y, z) = (x·32 + z)·32 + y  ⇒  y±1 is ±1 in memory.
    public static int Index(int x, int y, int z) => (x * SizeZ + z) * SizeY + y;
    public static (int X, int Y, int Z) LocalFromIndex(int i) =>
        (i / (SizeY * SizeZ), i % SizeY, (i / SizeY) % SizeZ);

    public ref VoxelData Get(int x, int y, int z) => ref _voxels[Index(x, y, z)];
    public ref VoxelData Get(int index) => ref _voxels[index];

    public void SetVoxel(int index, in VoxelData value) {
        if (_voxels[index].Equals(value)) return;
        _voxels[index] = value;
        Version++;
    }
}
