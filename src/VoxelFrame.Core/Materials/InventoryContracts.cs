namespace VoxelFrame.Core.Materials;

/// <summary>
/// Where materials come from during crafting. Implemented by Container.
/// Single-assembly note: Inventory implements these; Materials depends only
/// on the interface — no dependency cycle.
/// </summary>
public interface IInventorySource {
    /// Atomically removes all inputs, or none. Never partially succeeds.
    bool TryTakeAll(IReadOnlyList<RecipePart> inputs);

    /// Refund path: returns inputs untouched (used when the sink rejects outputs).
    void ReturnAll(IReadOnlyList<RecipePart> inputs);
}

public interface IInventorySink {
    /// Atomically places all outputs, or none.
    bool TryAddAll(IReadOnlyList<RecipePart> outputs);
}
