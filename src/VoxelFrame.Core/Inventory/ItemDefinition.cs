using VoxelFrame.Core.Materials;

namespace VoxelFrame.Core.Inventory;

/// <summary>
/// Определение предмета. Поля «научной» физики (масса, объём, pack-grid,
/// состояние) удалены — остались Id, имя, стак и категория-метка материала.
/// </summary>
public sealed class ItemDefinition {
    public required ushort Id { get; init; }
    public int MaxStack { get; init; } = 64;
    public required string Name { get; init; }
    public required Material Material { get; init; }
}

/// <summary>
/// Один предмет. Прочность инструментов/оружия хранится в <see cref="Durability"/>
/// (0 у не-инструментов). Сбрасывается до максимума при создании (GameData.NewItem).
/// </summary>
public sealed class ItemInstance {
    public ItemDefinition Definition { get; }
    public ulong InstanceId { get; }
    public int Durability { get; set; }

    public ItemInstance(ItemDefinition definition, ulong instanceId) {
        Definition = definition;
        InstanceId = instanceId;
    }
}

public readonly record struct ItemEntry(ItemInstance Item, int Quantity);
