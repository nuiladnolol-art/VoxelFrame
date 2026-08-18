using VoxelFrame.Core.World;

namespace VoxelFrame.Core.Physics;

[Flags]
public enum StressFlags : int {
    None       = 0,
    Solid      = 1 << 0,   // voxel is solid (can anchor loads)
    Structural = 1 << 1,   // participates in the support graph
    Framework  = 1 << 2,   // has sub-voxel components → horizontal redistribution edges
    InIsland   = 1 << 3,   // currently assigned to an island (flood-fill mark)
    Broken     = 1 << 4,   // failed or detached; awaiting collapse processing
}

/// <summary>
/// Per-chunk SoA projection of structural state. This is what solver worker
/// threads touch — never VoxelData directly (no torn struct reads).
/// The main thread keeps it in sync on every voxel write; a torn float read
/// during a solve is benign: the island re-solves on the next version bump.
/// </summary>
public sealed class ChunkStressState {
    public readonly int Id;
    public readonly Chunk Chunk;

    public readonly float[] WeightKN;      // self weight, kN
    public readonly float[] CapacityKN;    // load capacity, kN
    public readonly float[] LoadKN;        // current steady-state load, kN (solver output)
    public readonly ushort[] TypeId;       // voxel type, for events
    public readonly int[] Flags;   // per-voxel solver flags

    /// Bumped on every sync. Islands snapshot it at dispatch and compare at
    /// drain to detect writes that happened during the solve.
    public volatile int Version;

    public ChunkStressState(int id, Chunk chunk) {
        Id = id;
        Chunk = chunk;
        int n = Chunk.VoxelCount;
        WeightKN = new float[n];
        CapacityKN = new float[n];
        LoadKN = new float[n];
        TypeId = new ushort[n];
        Flags = new int[n];
    }

    /// Full sync of one voxel's projection (main thread, on voxel write).
    /// Preserves Broken — a voxel stays broken until the world mutates it.
    public void SyncVoxel(int index, in VoxelData v) {
        WeightKN[index] = v.Weight * PhysicsGridCoordinator.KN_Per_Kg;
        CapacityKN[index] = v.LoadBearingCapacity;
        TypeId[index] = v.TypeId;
        var f = Flags[index] & (int)StressFlags.Broken;
        if ((v.Flags & VoxelFlags.Solid) != 0) f |= (int)StressFlags.Solid;
        if ((v.Flags & VoxelFlags.Structural) != 0) f |= (int)StressFlags.Structural;
        if ((v.Flags & VoxelFlags.Occupied) != 0) f |= (int)StressFlags.Framework;
        Flags[index] = f;
        Version++;
    }

    /// One-shot sync of a whole chunk (world-gen path, once per chunk load).
    public void BulkSync() {
        for (int i = 0; i < Chunk.VoxelCount; i++) SyncVoxel(i, in Chunk.Get(i));
    }
}
