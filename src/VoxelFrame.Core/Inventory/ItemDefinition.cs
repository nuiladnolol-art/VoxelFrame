using VoxelFrame.Core.Materials;

namespace VoxelFrame.Core.Inventory;

/// <summary>
/// An item's physical definition. Volume is exact; mass ALWAYS derives from
/// material density (there is no independent mass field), so an item cannot
/// be lighter or heavier than its material — the conservation invariant
/// cannot be bypassed through items.
/// </summary>
public sealed class ItemDefinition {
    public required ushort Id { get; init; }
    public required string Name { get; init; }
    public required Material Material { get; init; }
    /// Exact material volume of one unit, m³ (solid-equivalent; bulk packs
    /// more loosely in storage via Material.PackingFactor).
    public required double VolumeM3 { get; init; }
    public PhysicalState State { get; init; } = PhysicalState.Solid;
    /// Footprint on a 0.125 m packing grid — for the 3D-packing inventory UI.
    public byte WidthCells { get; init; } = 1;
    public byte DepthCells { get; init; } = 1;
    public byte HeightCells { get; init; } = 1;

    public double MassKg => Material.MassOf(VolumeM3);
}

/// <summary>
/// One physical object. Identity exists so durability/condition can be
/// tracked — but wear never changes mass: matter is conserved.
/// </summary>
public sealed class ItemInstance {
    public ItemDefinition Definition { get; }
    public ulong InstanceId { get; }
    /// 0..1; degradation costs no mass.
    public double Condition { get; set; } = 1.0;

    public ItemInstance(ItemDefinition definition, ulong instanceId) {
        Definition = definition;
        InstanceId = instanceId;
    }

    public double VolumeM3 => Definition.VolumeM3;
    public double MassKg => Definition.MassKg;
}

public readonly record struct ItemEntry(ItemInstance Item, int Quantity) {
    public double VolumeM3 => Item.VolumeM3 * Quantity;
    public double MassKg => Item.MassKg * Quantity;
}
