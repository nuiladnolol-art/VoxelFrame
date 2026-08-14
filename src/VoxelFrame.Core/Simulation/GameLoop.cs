using VoxelFrame.Core.Physics;

namespace VoxelFrame.Core.Simulation;

/// <summary>
/// Fixed-timestep main loop. The structural solver runs on its own worker
/// threads; the main thread only dispatches dirty islands and drains events,
/// so frame rate and physics TPS are fully decoupled.
/// </summary>
public sealed class GameLoop {
    public const double TickSeconds = 1.0 / 20.0;   // 20 TPS physics cadence

    private readonly PhysicsGridCoordinator _physics;
    private readonly Action<StructuralEvent> _onStructuralEvent;
    private double _accumulator;

    public GameLoop(PhysicsGridCoordinator physics, Action<StructuralEvent> onStructuralEvent) {
        _physics = physics;
        _onStructuralEvent = onStructuralEvent;
    }

    /// Call once per rendered frame with the real elapsed time.
    public void Frame(double frameSeconds) {
        _accumulator += frameSeconds;
        while (_accumulator >= TickSeconds) {
            _accumulator -= TickSeconds;
            _physics.Tick(TickSeconds);
            while (_physics.TryDequeueEvent(out var ev)) _onStructuralEvent(ev);
        }
    }
}
