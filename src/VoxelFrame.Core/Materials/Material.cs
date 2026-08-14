namespace VoxelFrame.Core.Materials;

public enum MaterialCategory : byte { Wood, Stone, Metal, Soil, Fluid, Organic }

/// <summary>
/// Physical definition of a substance. Mass derives from density alone —
/// there is no independent "weight" field anywhere in the engine, so a
/// material can never weigh more or less than its volume × density.
/// </summary>
public sealed class Material {
    public required ushort Id { get; init; }
    public required string Name { get; init; }
    /// kg per m³ — the source of truth for mass.
    public required double DensityKgPerM3 { get; init; }
    /// Compressive strength used to derive load capacity. 0 = non-structural
    /// (soil, water, scrap piles) — such matter can never carry a load.
    public double CompressiveStrengthKPa { get; init; }
    public MaterialCategory Category { get; init; }
    public PhysicalState State { get; init; } = PhysicalState.Solid;
    /// Storage multiplier for Bulk materials (loose packing): a pile of
    /// sawdust reserves 2× its solid-equivalent volume in a container.
    /// The conservation ledger counts SOLID-equivalent volume only — packing
    /// is a storage concern, never a matter concern.
    public double PackingFactor { get; init; } = 1.0;

    public double MassOf(double volumeM3) => DensityKgPerM3 * volumeM3;
    public double VolumeOf(double massKg) => massKg / DensityKgPerM3;
}
