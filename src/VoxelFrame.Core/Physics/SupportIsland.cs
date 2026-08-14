namespace VoxelFrame.Core.Physics;

/// <summary>One voxel in the solver's flat view: which chunk state + local index.</summary>
public readonly struct IslandNode {
    public readonly int StateId;
    public readonly int Cell;
    public IslandNode(int stateId, int cell) { StateId = stateId; Cell = cell; }
}

/// <summary>
/// A connected component of the support graph. Islands never share voxels,
/// so they are the unit of parallelism: each is solved by one worker thread.
/// </summary>
public sealed class SupportIsland {
    public readonly List<IslandNode> Nodes = new();
    public readonly HashSet<ChunkStressState> MemberChunks = new();

    /// Member versions at dispatch; mismatch at drain ⇒ stale result, discard.
    public int BuiltAtVersion;
    public bool IsAnchored;      // ≥1 node rests on terrain or the world floor
    public bool IsStable;        // last solve produced no failures
    public float MaxLoadRatio;   // worst load/capacity at last solve (debug/UI)

    public bool TouchesAny(HashSet<ChunkStressState> dirtyChunks) {
        foreach (var c in MemberChunks)
            if (dirtyChunks.Contains(c)) return true;
        return false;
    }
}
