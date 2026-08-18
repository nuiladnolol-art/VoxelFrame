namespace VoxelFrame.Core.Physics;

public enum StructuralEventKind : byte {
    /// A voxel exceeded its capacity and broke (load > capacity).
    NodeFailed,
    /// A connected piece lost all support (whole island or individual node)
    /// — it falls; the dynamics layer takes over from here.
    IslandDetached,
}

/// <summary>
/// Produced by solver worker threads, consumed on the main thread via
/// PhysicsGridCoordinator.TryDequeueEvent. Immutable value.
/// </summary>
public readonly struct StructuralEvent {
    public readonly StructuralEventKind Kind;
    public readonly Vec3i WorldPosition;
    public readonly ushort VoxelTypeId;
    public readonly float LoadKN;
    public readonly float CapacityKN;
    public readonly float ExcessRatio;      // LoadKN / CapacityKN (≥ 1 for failures)

    public StructuralEvent(StructuralEventKind kind, Vec3i worldPosition, ushort voxelTypeId,
                           float loadKN, float capacityKN) {
        Kind = kind;
        WorldPosition = worldPosition;
        VoxelTypeId = voxelTypeId;
        LoadKN = loadKN;
        CapacityKN = capacityKN;
        ExcessRatio = capacityKN > 0f ? loadKN / capacityKN
                    : loadKN > 0f ? float.PositiveInfinity : 0f;
    }

    public override string ToString() =>
        $"{Kind} @ {WorldPosition}: {LoadKN:F1}/{CapacityKN:F1} kN ({ExcessRatio:F2}×)";
}
