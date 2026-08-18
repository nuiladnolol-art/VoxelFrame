using System.Runtime.InteropServices;

namespace VoxelFrame.Core.World;

[Flags]
public enum VoxelFlags : byte {
    None        = 0,
    Solid       = 1 << 0,   // physically fills the cell: blocks movement, can carry a load
    Structural  = 1 << 1,   // participates in the load-bearing graph
    Dynamic     = 1 << 2,   // not static: debris, loose material — handled by the dynamics layer
    Occupied    = 1 << 3,   // cell holds sub-voxel framework components (SubGrid allocated)
    Dirty       = 1 << 4,   // written this tick (producer flag, not yet consumed)
}

/// <summary>
/// One cubic metre of world — the AoS face of the voxel store.
/// 20 bytes, deliberately the ONLY persistent per-voxel payload: transient
/// solver state (current load, support pointers, island membership) lives in
/// SoA arrays owned by PhysicsGridCoordinator, never here.
///
/// Volume and mass are exact at creation and audited upstream: mass always
/// equals material density × ContentVolumeM3, and building a voxel consumes
/// exactly ContentVolumeM3 from the builder's inventory.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct VoxelData : IEquatable<VoxelData> {
    /// VoxelCatalog index. 0 = Air.
    public ushort TypeId;
    public VoxelFlags Flags;
    /// Occupied Y-layers of the sub-grid; 0 = no framework components.
    public byte SubGridLayerMask;
    /// Static mass of the cell's material content, kg.
    public float Weight;
    /// Exact material volume inside the cell, m³ (≤ 1). Solid cube = 1.0;
    /// a framework cell = sum of its components' volumes.
    public float ContentVolumeM3;
    /// Maximum sustainable load, kN. 0 = cannot carry anything.
    public float LoadBearingCapacity;
    /// Index into the chunk's sparse SubGrid pool; -1 = none.
    public int SubGridIndex;

    public const float CellVolumeM3 = 1f;   // every cell is exactly 1 m³

    public readonly bool IsAir => TypeId == 0;
    public readonly bool IsSolid => (Flags & VoxelFlags.Solid) != 0;
    public readonly bool IsStructural => (Flags & VoxelFlags.Structural) != 0;
    public readonly bool IsDynamic => (Flags & VoxelFlags.Dynamic) != 0;
    public readonly bool HasSubGrid => SubGridIndex >= 0;

    public static VoxelData Air => default;

    /// A load-bearing solid block (cube).
    public static VoxelData Solid(ushort typeId, float weightKg, float contentVolumeM3, float loadCapacityKN) => new() {
        TypeId = typeId,
        Flags = VoxelFlags.Solid | VoxelFlags.Structural,
        Weight = weightKg,
        ContentVolumeM3 = contentVolumeM3,
        LoadBearingCapacity = loadCapacityKN,
        SubGridIndex = -1,
    };

    /// Terrain: solid, but NOT structural — it anchors structures and never
    /// participates in the load graph itself.
    public static VoxelData Terrain(ushort typeId, float weightKg, float contentVolumeM3) => new() {
        TypeId = typeId,
        Flags = VoxelFlags.Solid,
        Weight = weightKg,
        ContentVolumeM3 = contentVolumeM3,
        LoadBearingCapacity = 0f,
        SubGridIndex = -1,
    };

    /// A framework cell: sub-voxel components carry the load (you walk through
    /// a beam — the cell is not solid). Capacity/weight are summed from the
    /// SubGrid by Chunk.RefreshPhysicals.
    public static VoxelData FrameCell(ushort typeId, float weightKg, float contentVolumeM3, byte layerMask) => new() {
        TypeId = typeId,
        Flags = VoxelFlags.Structural | VoxelFlags.Occupied,
        Weight = weightKg,
        ContentVolumeM3 = contentVolumeM3,
        LoadBearingCapacity = 0f,
        SubGridLayerMask = layerMask,
        SubGridIndex = -1,
    };

    public readonly bool Equals(VoxelData o) =>
        TypeId == o.TypeId && Flags == o.Flags && SubGridLayerMask == o.SubGridLayerMask &&
        Weight == o.Weight && ContentVolumeM3 == o.ContentVolumeM3 &&
        LoadBearingCapacity == o.LoadBearingCapacity && SubGridIndex == o.SubGridIndex;

    public override readonly bool Equals(object? o) => o is VoxelData v && Equals(v);
    public override readonly int GetHashCode() =>
        HashCode.Combine(TypeId, Flags, SubGridLayerMask, Weight, ContentVolumeM3, LoadBearingCapacity, SubGridIndex);
    public static bool operator ==(VoxelData a, VoxelData b) => a.Equals(b);
    public static bool operator !=(VoxelData a, VoxelData b) => !a.Equals(b);
    public override readonly string ToString() =>
        $"#{TypeId} {(IsSolid ? "solid" : "")}{(IsStructural ? " structural" : "")} {Weight:0.#}kg {LoadBearingCapacity:0.#}kN";
}
