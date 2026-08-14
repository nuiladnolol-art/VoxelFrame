using VoxelFrame.Core.Materials;

namespace VoxelFrame.Core.World;

public enum VoxelShape : byte { Cube, Beam, Panel, Fluid, Debris }

/// <summary>Static definition of a voxel kind — the per-cell cost model.</summary>
public sealed class VoxelType {
    public required ushort Id { get; init; }
    public required string Name { get; init; }
    public required Material Material { get; init; }
    public VoxelShape Shape { get; init; } = VoxelShape.Cube;
    /// Material volume inside a 1 m³ cell (1.0 = solid cube; 0.01 = 0.1×0.1×1
    /// beam). This is what building CONSUMES — conservation holds because you
    /// can never place more material than the inventory paid for.
    public double FillVolumeM3 { get; init; } = 1.0;
    /// Load this voxel can carry, kN. 0 = non-structural (terrain anchors).
    public float LoadCapacityKN { get; init; }

    public bool IsStructural => LoadCapacityKN > 0;
    public double MassKg => Material.MassOf(FillVolumeM3);
}

public sealed class VoxelCatalog {
    private readonly Dictionary<ushort, VoxelType> _types = new();

    public VoxelType Register(VoxelType type) {
        if (type.Id == 0) throw new ArgumentException("Voxel type id 0 is reserved for Air.");
        if (_types.ContainsKey(type.Id)) throw new ArgumentException($"Duplicate voxel type id {type.Id}.");
        if (type.FillVolumeM3 <= 0 || type.FillVolumeM3 > 1.0)
            throw new ArgumentException($"Voxel '{type.Name}': FillVolumeM3 must be in (0, 1].");
        _types.Add(type.Id, type);
        return type;
    }

    public VoxelType Get(ushort id) => _types[id];

    /// The ONLY way voxels enter the world: mass/volume/capacity derive from
    /// the type definition — a voxel cannot weigh more than its type.
    public VoxelData CreateVoxel(ushort typeId) {
        var t = _types[typeId];
        var flags = VoxelFlags.Solid;
        if (t.IsStructural) flags |= VoxelFlags.Structural;
        return new VoxelData {
            TypeId = typeId,
            Flags = flags,
            Weight = (float)t.MassKg,
            ContentVolumeM3 = (float)t.FillVolumeM3,
            LoadBearingCapacity = t.LoadCapacityKN,
            SubGridIndex = -1,
        };
    }
}
