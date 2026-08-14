using VoxelFrame.Core.Inventory;

namespace VoxelFrame.Core.Materials;

/// <summary>One side of a recipe: discrete items or volume-continuous bulk.</summary>
public abstract record RecipePart {
    public abstract Material Material { get; }
    public abstract double VolumeM3 { get; }          // solid-equivalent volume
    public abstract double MassKg { get; }
    public abstract double StorageVolumeM3 { get; }   // what a container must reserve
}

public sealed record ItemPart(ItemDefinition Item, int Quantity) : RecipePart {
    public override Material Material => Item.Material;
    public override double VolumeM3 => Item.VolumeM3 * Quantity;
    public override double MassKg => Item.MassKg * Quantity;
    public override double StorageVolumeM3 => VolumeM3;
}

public sealed record BulkPart : RecipePart {
    public BulkPart(Material material, double volumeM3, bool isScrap = false) {
        Material = material;
        VolumeM3 = volumeM3;
        IsScrap = isScrap;
    }

    public override Material Material { get; }
    public override double VolumeM3 { get; }
    public bool IsScrap { get; }

    public override double MassKg => Material.MassOf(VolumeM3);
    public override double StorageVolumeM3 => VolumeM3 * Material.PackingFactor;
}

/// <summary>
/// A transformation of matter. Conservation (volume ≤ in, mass == in) is
/// validated at registration — a violating recipe cannot exist in the game.
/// </summary>
public sealed class CraftingRecipe {
    public required string Id { get; init; }
    public required IReadOnlyList<RecipePart> Inputs { get; init; }
    public required IReadOnlyList<RecipePart> Outputs { get; init; }
    public double CraftTimeSeconds { get; init; } = 1.0;
}
