using System.Buffers;
using System.Collections.Concurrent;
using VoxelFrame.Core.World;

namespace VoxelFrame.Core.Physics;

/// <summary>
/// Structural stress solver. Runs entirely on worker threads.
///
/// Model:
///   • Nodes are structural voxels. A solid cube transmits load only downward
///     (edge to the voxel below). Framework elements (voxels with SubGrid
///     components — beams, panels) additionally add horizontal edges, so a
///     frame can redistribute load across columns.
///   • A node is ANCHORED when it rests on terrain (solid, non-structural
///     voxel below) or on the world floor. Grounding propagates through the
///     parent graph; anything without a path to ground detaches and falls.
///   • Load flows from a node to its parents; each parent takes a share
///     proportional to its spare capacity (greedy). Jacobi iteration until
///     convergence (≤ MaxIterations).
///   • Failure: load > capacity ⇒ the node breaks (StructuralEvent + Broken
///     flag). The game consumes events → mutates the world → the cascade
///     re-enters the coordinator naturally.
///
/// Optimization: an island without framework nodes is a set of pure vertical
/// stacks — solved exactly in O(n) (SolveColumnsExact) instead of iterating.
/// Parallelism comes from island independence (see PhysicsGridCoordinator).
/// </summary>
public static class StressSolver {
    public const int MaxIterations = 8;
    public const float ConvergenceKN = 0.02f;
    public const float CapacityMarginKN = 0f;    // gameplay tuning lever (kN added to capacity)
    public const int MaxParents = 5;             // below + 4 horizontal

    public static void Solve(SupportIsland island, ChunkStressState[] states,
                             ConcurrentDictionary<Vec3i, int> chunkIndex,
                             List<StructuralEvent> events) {
        int n = island.Nodes.Count;
        if (n == 0) return;

        float[] self = ArrayPool<float>.Shared.Rent(n);
        float[] loadA = ArrayPool<float>.Shared.Rent(n);
        float[] loadB = ArrayPool<float>.Shared.Rent(n);
        int[] parents = ArrayPool<int>.Shared.Rent(n * MaxParents);
        byte[] parentCount = ArrayPool<byte>.Shared.Rent(n);
        bool[] grounded = ArrayPool<bool>.Shared.Rent(n);
        try {
            bool hasFramework = BuildSupportGraph(island, states, chunkIndex, self, parents, parentCount, grounded);

            island.IsAnchored = false;
            for (int i = 0; i < n; i++)
                if (grounded[i]) { island.IsAnchored = true; break; }

            if (!island.IsAnchored) {
                // Nothing holds this island: the whole piece detaches.
                var first = island.Nodes[0];
                events.Add(new StructuralEvent(StructuralEventKind.IslandDetached,
                    WorldOf(states, first), states[first.StateId].TypeId[first.Cell], 0f, 0f));
                for (int i = 0; i < n; i++)
                    Interlocked.Or(ref states[island.Nodes[i].StateId].Flags[island.Nodes[i].Cell], (int)StressFlags.Broken);
                island.IsStable = false;
                return;
            }

            PropagateGrounding(n, parents, parentCount, grounded);

            if (hasFramework)
                SolveJacobi(island, states, n, self, parents, parentCount, grounded, loadA, loadB);
            else
                SolveColumnsExact(island, states, self, loadA);

            // Публикуем результат в SoA (для GetLoadKN/UI).
            for (int i = 0; i < n; i++) {
                var nd = island.Nodes[i];
                states[nd.StateId].LoadKN[nd.Cell] = loadA[i];
            }

            DetectFailures(island, states, loadA, grounded, events);
            island.IsStable = events.Count == 0;
        } finally {
            ArrayPool<float>.Shared.Return(self);
            ArrayPool<float>.Shared.Return(loadA);
            ArrayPool<float>.Shared.Return(loadB);
            ArrayPool<int>.Shared.Return(parents);
            ArrayPool<byte>.Shared.Return(parentCount);
            ArrayPool<bool>.Shared.Return(grounded);
        }
    }

    // ── Graph construction ───────────────────────────────────────────────────

    /// Fills self[], parents[], parentCount[] and initial grounded[] (anchored).
    /// Returns true if the island contains framework nodes (needs relaxation).
    private static bool BuildSupportGraph(SupportIsland island, ChunkStressState[] states,
                                          ConcurrentDictionary<Vec3i, int> chunkIndex,
                                          float[] self, int[] parents, byte[] parentCount, bool[] grounded) {
        int n = island.Nodes.Count;
        bool hasFramework = false;
        var nodeIndex = new Dictionary<(int, int), int>(n);
        for (int i = 0; i < n; i++) {
            var nd = island.Nodes[i];
            nodeIndex.Add((nd.StateId, nd.Cell), i);
            parentCount[i] = 0;
            grounded[i] = false;
            self[i] = states[nd.StateId].WeightKN[nd.Cell];
            if (((StressFlags)states[nd.StateId].Flags[nd.Cell] & StressFlags.Framework) != 0) hasFramework = true;
        }

        for (int i = 0; i < n; i++) {
            var nd = island.Nodes[i];
            var st = states[nd.StateId];
            (int wx, int wy, int wz) = WorldOf(states, nd);

            // 1) Vertical support: the voxel below, or the world floor.
            if (wy == 0) {
                grounded[i] = true;                       // rests on the world floor
            } else if (TryLookup(wx, wy - 1, wz, states, chunkIndex, out var belowSt, out int belowCell)) {
                var bf = belowSt.Flags[belowCell];
                if (((StressFlags)bf & StressFlags.Solid) != 0 && ((StressFlags)bf & StressFlags.Structural) == 0) {
                    grounded[i] = true;                   // rests on terrain
                } else if (nodeIndex.TryGetValue((belowSt.Id, belowCell), out int belowIdx)) {
                    AddParent(parents, parentCount, i, belowIdx);   // supported by structure
                }
            }

            // 2) Horizontal redistribution — only framework elements, and only
            //    when the node is not already grounded.
            if (!grounded[i] && ((StressFlags)st.Flags[nd.Cell] & StressFlags.Framework) != 0) {
                TryAddParent(wx + 1, wy, wz, nodeIndex, states, chunkIndex, parents, parentCount, i);
                TryAddParent(wx - 1, wy, wz, nodeIndex, states, chunkIndex, parents, parentCount, i);
                TryAddParent(wx, wy, wz + 1, nodeIndex, states, chunkIndex, parents, parentCount, i);
                TryAddParent(wx, wy, wz - 1, nodeIndex, states, chunkIndex, parents, parentCount, i);
            }
        }
        return hasFramework;
    }

    private static void AddParent(int[] parents, byte[] parentCount, int node, int parent) {
        if (parentCount[node] >= MaxParents) return;      // best effort; saturated
        parents[node * MaxParents + parentCount[node]] = parent;
        parentCount[node]++;
    }

    private static void TryAddParent(int wx, int wy, int wz, Dictionary<(int, int), int> nodeIndex,
                                     ChunkStressState[] states, ConcurrentDictionary<Vec3i, int> chunkIndex,
                                     int[] parents, byte[] parentCount, int node) {
        if (TryLookup(wx, wy, wz, states, chunkIndex, out var st, out int cell) &&
            nodeIndex.TryGetValue((st.Id, cell), out int parent))
            AddParent(parents, parentCount, node, parent);
    }

    private static bool TryLookup(int wx, int wy, int wz, ChunkStressState[] states,
                                  ConcurrentDictionary<Vec3i, int> chunkIndex,
                                  out ChunkStressState state, out int cell) {
        var cc = Chunk.CoordOf(new Vec3i(wx, wy, wz));
        if (chunkIndex.TryGetValue(cc, out int sid) && sid < states.Length) {
            state = states[sid];
            cell = Chunk.Index(wx & 31, wy & 31, wz & 31);
            return true;
        }
        state = null!;
        cell = 0;
        return false;
    }

    private static Vec3i WorldOf(ChunkStressState[] states, IslandNode node) {
        var st = states[node.StateId];
        (int lx, int ly, int lz) = Chunk.LocalFromIndex(node.Cell);
        var o = st.Chunk.Origin;
        return new Vec3i(o.X * Chunk.SizeX + lx, o.Y * Chunk.SizeY + ly, o.Z * Chunk.SizeZ + lz);
    }

    /// Fixpoint: a node is grounded when any parent is grounded. O(n²) worst,
    /// but islands are small; bounded by n passes.
    private static void PropagateGrounding(int n, int[] parents, byte[] parentCount, bool[] grounded) {
        for (int pass = 0; pass < n; pass++) {
            bool changed = false;
            for (int i = 0; i < n; i++) {
                if (grounded[i]) continue;
                for (int k = 0; k < parentCount[i]; k++) {
                    if (grounded[parents[i * MaxParents + k]]) { grounded[i] = true; changed = true; break; }
                }
            }
            if (!changed) break;
        }
    }

    // ── Load distribution ────────────────────────────────────────────────────

    /// Framework islands: greedy proportional Jacobi. Each node pushes its
    /// total load to parents by spare capacity; converges in a few iterations
    /// for tree-like graphs, capped for cyclic frames (over-capacity then
    /// fails the weakest link — the conservative outcome).
    private static void SolveJacobi(SupportIsland island, ChunkStressState[] states, int n,
                                    float[] self, int[] parents, byte[] parentCount, bool[] grounded,
                                    float[] loadA, float[] loadB) {
        Array.Copy(self, loadA, n);
        for (int iter = 0; iter < MaxIterations; iter++) {
            Array.Copy(self, loadB, n);
            for (int i = 0; i < n; i++) {
                if (!grounded[i]) continue;          // detached: never burdens the anchored part
                float total = loadA[i];
                int pc = parentCount[i];
                if (pc == 0) continue;               // grounded on the floor: everything sinks here
                float spareSum = 0f;
                for (int k = 0; k < pc; k++) {
                    int p = parents[i * MaxParents + k];
                    if (!grounded[p]) continue;
                    spareSum += Spare(island, states, loadA, p);
                }
                if (spareSum <= 0f) continue;        // parents saturated: node keeps the load → fails
                for (int k = 0; k < pc; k++) {
                    int p = parents[i * MaxParents + k];
                    if (!grounded[p]) continue;
                    float spare = Spare(island, states, loadA, p);
                    if (spare <= 0f) continue;
                    loadB[p] += total * (spare / spareSum);
                }
            }
            float maxDiff = 0f;
            for (int i = 0; i < n; i++) maxDiff = MathF.Max(maxDiff, MathF.Abs(loadB[i] - loadA[i]));
            Array.Copy(loadB, loadA, n);
            if (maxDiff < ConvergenceKN) break;
        }
    }

    private static float Spare(SupportIsland island, ChunkStressState[] states, float[] load, int nodeIndex) {
        var nd = island.Nodes[nodeIndex];
        return MathF.Max(states[nd.StateId].CapacityKN[nd.Cell] - load[nodeIndex], 0f);
    }

    /// Islands without framework nodes are pure vertical stacks: load(node) =
    /// self + everything above it in the same column. Exact, O(n log n) for
    /// the sort, then one pass. No iteration, no convergence risk.
    private static void SolveColumnsExact(SupportIsland island, ChunkStressState[] states,
                                          float[] self, float[] load) {
        var nodes = island.Nodes;
        int n = nodes.Count;
        int[] order = ArrayPool<int>.Shared.Rent(n);
        try {
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(order, 0, n, Comparer<int>.Create((a, b) => {
                (int ax, int ay, int az) = WorldOf(states, nodes[a]);
                (int bx, int by, int bz) = WorldOf(states, nodes[b]);
                int c = ax.CompareTo(bx);
                if (c != 0) return c;
                c = az.CompareTo(bz);
                if (c != 0) return c;
                return ay.CompareTo(by);
            }));

            // Sorted by (x, z, y): a contiguous column is a run of consecutive
            // entries. Load flows DOWN — a node carries itself plus everything
            // above it — so accumulate from the top: load[y] = self + load[y+1].
            for (int k = n - 1; k >= 0; k--) {
                int i = order[k];
                float w = self[i];
                if (k + 1 < n) {
                    int j = order[k + 1];   // next entry: same column, directly above
                    (int ax, int ay, int az) = WorldOf(states, nodes[i]);
                    (int bx, int by, int bz) = WorldOf(states, nodes[j]);
                    if (ax == bx && az == bz && by == ay + 1) w += load[j];
                }
                load[i] = w;
            }
        } finally {
            ArrayPool<int>.Shared.Return(order);
        }
    }

    // ── Failure detection ────────────────────────────────────────────────────

    private static void DetectFailures(SupportIsland island, ChunkStressState[] states,
                                       float[] load, bool[] grounded, List<StructuralEvent> events) {
        float maxRatio = 0f;
        for (int i = 0; i < island.Nodes.Count; i++) {
            var nd = island.Nodes[i];
            var st = states[nd.StateId];
            float cap = st.CapacityKN[nd.Cell];
            float l = load[i];
            float ratio = cap > 0f ? l / cap : (l > 0f ? float.PositiveInfinity : 0f);
            if (ratio > maxRatio) maxRatio = ratio;

            if (!grounded[i]) {
                // No path to ground: this piece (or the stack above it) falls.
                if (((StressFlags)st.Flags[nd.Cell] & StressFlags.Broken) == 0) {
                    events.Add(new StructuralEvent(StructuralEventKind.IslandDetached,
                        WorldOf(states, nd), st.TypeId[nd.Cell], l, cap));
                    Interlocked.Or(ref st.Flags[nd.Cell], (int)StressFlags.Broken);
                }
                continue;
            }
            if (l > cap + CapacityMarginKN + ConvergenceKN && ((StressFlags)st.Flags[nd.Cell] & StressFlags.Broken) == 0) {
                events.Add(new StructuralEvent(StructuralEventKind.NodeFailed,
                    WorldOf(states, nd), st.TypeId[nd.Cell], l, cap));
                Interlocked.Or(ref st.Flags[nd.Cell], (int)StressFlags.Broken);   // one event per failure — no re-emits
            }
        }
        island.MaxLoadRatio = maxRatio;
    }
}
