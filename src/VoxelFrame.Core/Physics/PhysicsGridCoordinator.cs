using System.Collections.Concurrent;
using VoxelFrame.Core.World;

namespace VoxelFrame.Core.Physics;

/// <summary>
/// PhysicsGridCoordinator — owns the structural integrity simulation.
///
/// Pipeline (cross-thread handoffs are queue-based, hot paths lock-free):
///   1. Every voxel write flows through WorldGrid.SetVoxel → OnVoxelChanged:
///      the main thread syncs that chunk's SoA projection and marks it dirty.
///   2. Tick() (fixed 20 TPS cadence) rebuilds islands over dirty chunks and
///      posts solve jobs to the worker pool.
///   3. Workers solve islands concurrently — islands never share voxels —
///      reading only SoA arrays, never VoxelData (no torn struct reads).
///   4. Main thread drains results: accepts fresh solves, discards stale ones
///      (a write happened mid-solve — the rebuild already covers it), and
///      surfaces StructuralEvents to the game. Collapse = the game consuming
///      an event → mutating the world → a new VoxelChanged → the cascade
///      re-enters step 1.
///
/// The solve never blocks the main thread: Tick() dispatches and returns.
/// TPS stability comes from island parallelism + the exact column early-out.
/// </summary>
public sealed class PhysicsGridCoordinator : IDisposable {
    public const float GravityM_S2 = 9.81f;
    public const float KN_Per_Kg = GravityM_S2 / 1000f;   // 1 kg ≈ 0.00981 kN

    private readonly IChunkSource _world;
    private readonly BlockingCollection<SolveJob> _jobs;
    private readonly Thread[] _workers;
    private readonly ConcurrentQueue<SolveJob> _completed = new();
    private readonly ConcurrentQueue<StructuralEvent> _events = new();
    private readonly ConcurrentDictionary<Chunk, int> _chunkToState = new();
    private readonly ConcurrentDictionary<Vec3i, int> _chunkCoordToState = new();

    // Copy-on-write snapshot: workers capture it at dispatch and never see a
    // partially-grown array. ChunkStressState objects themselves are stable.
    private volatile ChunkStressState[] _states = Array.Empty<ChunkStressState>();

    // Main-thread only (guarded by _islandLock):
    private readonly List<SupportIsland> _islands = new();
    private readonly HashSet<ChunkStressState> _dirtyChunks = new();
    private readonly HashSet<SupportIsland> _dirtyIslands = new();
    private readonly object _islandLock = new();

    public int IslandCount { get { lock (_islandLock) return _islands.Count; } }
    public int PendingJobs => _jobs.Count;

    public PhysicsGridCoordinator(IChunkSource world, int workerCount = -1) {
        _world = world;
        int workers = workerCount > 0 ? workerCount : Math.Max(1, Environment.ProcessorCount - 2);
        _jobs = new BlockingCollection<SolveJob>(new ConcurrentQueue<SolveJob>());
        _workers = new Thread[workers];
        for (int i = 0; i < workers; i++) {
            _workers[i] = new Thread(WorkerLoop) { IsBackground = true, Name = $"StressWorker-{i}" };
            _workers[i].Start();
        }
        world.VoxelChanged += OnVoxelChanged;
    }

    // ── Main thread: world writes ────────────────────────────────────────────

    private void OnVoxelChanged(Vec3i worldPos, VoxelData before, VoxelData after) {
        if (before.Equals(after)) return;
        var state = GetOrCreateState(worldPos);
        int idx = Chunk.Index(worldPos.X & 31, worldPos.Y & 31, worldPos.Z & 31);
        state.SyncVoxel(idx, in after);

        // Only structural facts force a re-solve: topology, self weight, capacity.
        bool structuralFactsChanged =
            ((before.Flags ^ after.Flags) & VoxelFlags.Structural) != 0 ||
            before.Weight != after.Weight ||
            before.LoadBearingCapacity != after.LoadBearingCapacity;
        if (!structuralFactsChanged) return;
        lock (_islandLock) _dirtyChunks.Add(state);
    }

    public const int MaxIslandNodes = 4096;

    /// <summary>
    /// World-gen entry point: bulk-load a chunk into the solver in one pass
    /// (no per-voxel events) and schedule it for an initial solve.
    /// </summary>
    public ChunkStressState RegisterChunk(Chunk chunk) {
        if (_chunkToState.TryGetValue(chunk, out int id)) return _states[id];
        var state = new ChunkStressState(_states.Length, chunk);
        var grown = new ChunkStressState[_states.Length + 1];
        Array.Copy(_states, grown, _states.Length);
        grown[^1] = state;
        _states = grown;                                  // volatile: workers see the whole array
        _chunkToState[chunk] = state.Id;
        _chunkCoordToState[chunk.Origin] = state.Id;
        state.BulkSync();
        // Do NOT flood-fill all natural world terrain on initial chunk load; only player edits trigger island solves.
        return state;
    }

    private ChunkStressState GetOrCreateState(Vec3i worldPos) =>
        RegisterChunk(_world.GetOrCreateChunk(Chunk.CoordOf(worldPos)));

    // ── Main thread: fixed-step update ───────────────────────────────────────

    /// Fixed-step tick (1/20 s). Dispatches dirty islands and drains results.
    /// Non-blocking: the workers run concurrently.
    public void Tick(double dtSeconds) {
        DrainCompleted();
        lock (_islandLock) {
            RebuildDirtyIslands();
            DispatchDirtyIslands();
        }
    }

    private void DrainCompleted() {
        var states = _states;
        while (_completed.TryDequeue(out var job)) {
            if (VersionOf(job.Island, states) != job.Island.BuiltAtVersion) {
                // A voxel changed mid-solve → stale result; the dirty-chunk
                // rebuild has already created a fresh island that covers it.
                continue;
            }
            foreach (var e in job.Events!) _events.Enqueue(e);   // worker fills Events before enqueueing
        }
    }

    private void RebuildDirtyIslands() {
        if (_dirtyChunks.Count == 0) return;
        var states = _states;

        // 1. Drop islands touching a dirty chunk; clear their members' flags.
        _islands.RemoveAll(isl => {
            if (!isl.TouchesAny(_dirtyChunks)) return false;
            foreach (var nd in isl.Nodes) Interlocked.And(ref states[nd.StateId].Flags[nd.Cell], ~(int)StressFlags.InIsland);
            return true;
        });

        // 2. Flood-fill islands over every structural voxel of dirty chunks,
        //    crossing into clean neighbour chunks as needed.
        var seeds = new Queue<(int StateId, int Cell)>();
        foreach (var st in _dirtyChunks) {
            var flags = st.Flags;
            for (int i = 0; i < flags.Length; i++) {
                if (((StressFlags)flags[i] & StressFlags.Structural) != 0 && ((StressFlags)flags[i] & StressFlags.InIsland) == 0)
                    seeds.Enqueue((st.Id, i));
            }
        }
        while (seeds.Count > 0) {
            var seed = seeds.Dequeue();
            // A previous flood fill may have claimed this seed already.
            if (((StressFlags)states[seed.StateId].Flags[seed.Cell] & StressFlags.InIsland) != 0) continue;
            var island = new SupportIsland();
            FloodFill(seed.StateId, seed.Cell, states, island);
            if (island.Nodes.Count > 0) {
                _islands.Add(island);
                _dirtyIslands.Add(island);   // fresh islands need their first solve
            }
        }
        _dirtyChunks.Clear();
    }

    private void FloodFill(int stateId, int cell, ChunkStressState[] states, SupportIsland island) {
        var work = new Queue<(int StateId, int Cell)>();
        work.Enqueue((stateId, cell));
        Interlocked.Or(ref states[stateId].Flags[cell], (int)StressFlags.InIsland);
        while (work.Count > 0) {
            if (island.Nodes.Count >= MaxIslandNodes) {
                // Large continuous terrain or massive mega-base anchored in ground: cap expansion
                while (work.Count > 0) {
                    var (remSid, remCell) = work.Dequeue();
                    Interlocked.And(ref states[remSid].Flags[remCell], ~(int)StressFlags.InIsland);
                }
                break;
            }
            var (sid, c) = work.Dequeue();
            island.Nodes.Add(new IslandNode(sid, c));
            island.MemberChunks.Add(states[sid]);
            var st = states[sid];
            (int lx, int ly, int lz) = Chunk.LocalFromIndex(c);
            TryEnqueueNeighbor(st, sid, lx + 1, ly, lz, states, work);
            TryEnqueueNeighbor(st, sid, lx - 1, ly, lz, states, work);
            TryEnqueueNeighbor(st, sid, lx, ly + 1, lz, states, work);
            TryEnqueueNeighbor(st, sid, lx, ly - 1, lz, states, work);
            TryEnqueueNeighbor(st, sid, lx, ly, lz + 1, states, work);
            TryEnqueueNeighbor(st, sid, lx, ly, lz - 1, states, work);
        }
    }

    private void TryEnqueueNeighbor(ChunkStressState st, int sid, int nx, int ny, int nz,
                                    ChunkStressState[] states, Queue<(int, int)> work) {
        if (nx >= 0 && nx < Chunk.SizeX && ny >= 0 && ny < Chunk.SizeY && nz >= 0 && nz < Chunk.SizeZ) {
            int nc = Chunk.Index(nx, ny, nz);
            if (((StressFlags)st.Flags[nc] & StressFlags.Structural) != 0 && ((StressFlags)st.Flags[nc] & StressFlags.InIsland) == 0) {
                Interlocked.Or(ref st.Flags[nc], (int)StressFlags.InIsland);
                work.Enqueue((sid, nc));
            }
            return;
        }
        // Cross-chunk neighbour.
        var origin = st.Chunk.Origin;
        int wx = origin.X * Chunk.SizeX + nx, wy = origin.Y * Chunk.SizeY + ny, wz = origin.Z * Chunk.SizeZ + nz;
        var cc = Chunk.CoordOf(new Vec3i(wx, wy, wz));
        if (_chunkCoordToState.TryGetValue(cc, out int nid) && nid < states.Length) {
            var nst = states[nid];
            int nc = Chunk.Index(wx & 31, wy & 31, wz & 31);
            if (((StressFlags)nst.Flags[nc] & StressFlags.Structural) != 0 && ((StressFlags)nst.Flags[nc] & StressFlags.InIsland) == 0) {
                Interlocked.Or(ref nst.Flags[nc], (int)StressFlags.InIsland);
                work.Enqueue((nid, nc));
            }
        }
    }

    private void DispatchDirtyIslands() {
        if (_dirtyIslands.Count == 0) return;
        var states = _states;
        foreach (var island in _dirtyIslands) {
            island.BuiltAtVersion = VersionOf(island, states);
            _jobs.Add(new SolveJob { Island = island, States = states });
        }
        _dirtyIslands.Clear();
    }

    private static int VersionOf(SupportIsland island, ChunkStressState[] states) {
        int v = 0;
        foreach (var nd in island.Nodes) {
            int ver = states[nd.StateId].Version;
            if (ver > v) v = ver;
        }
        return v;
    }

    // ── Worker threads ───────────────────────────────────────────────────────

    private void WorkerLoop() {
        foreach (var job in _jobs.GetConsumingEnumerable()) {
            var events = new List<StructuralEvent>(8);
            StressSolver.Solve(job.Island, job.States, _chunkCoordToState, events);
            job.Events = events;
            _completed.Enqueue(job);
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    /// Main thread: pull structural events (failures, detachments).
    public bool TryDequeueEvent(out StructuralEvent ev) => _events.TryDequeue(out ev);

    /// Debug/UI: current computed load on a voxel (kN), or NaN when unknown.
    public float GetLoadKN(Vec3i worldPos) {
        var cc = Chunk.CoordOf(worldPos);
        if (!_chunkCoordToState.TryGetValue(cc, out int sid)) return float.NaN;
        return _states[sid].LoadKN[Chunk.Index(worldPos.X & 31, worldPos.Y & 31, worldPos.Z & 31)];
    }

    public void Dispose() {
        _jobs.CompleteAdding();
        foreach (var t in _workers) t.Join();
        _world.VoxelChanged -= OnVoxelChanged;
    }

    private sealed class SolveJob {
        public required SupportIsland Island { get; init; }
        public required ChunkStressState[] States { get; init; }
        public List<StructuralEvent>? Events;   // filled by the worker
    }
}
