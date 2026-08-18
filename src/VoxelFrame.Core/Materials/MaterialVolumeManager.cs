namespace VoxelFrame.Core.Materials;

/// <summary>
/// Thrown when a recipe violates the First Law of the world: mass cannot be
/// created or destroyed, and processed volume can never exceed source volume
/// (surplus must be declared as scrap).
/// </summary>
public sealed class ConservationViolationException : Exception {
    public ConservationViolationException(string message) : base(message) { }
}

public readonly record struct ConservationStats(double InputVolumeM3, double InputMassKg,
                                                double OutputVolumeM3, double OutputMassKg);

/// <summary>
/// MaterialVolumeManager — the world's single source of truth for matter.
///
/// Responsibilities:
///  1. Register materials (density, strength). Mass always derives from
///     density × volume; there is no independent weight field anywhere.
///  2. Register recipes and REJECT any that break conservation.
///  3. Execute crafting atomically against inventory sources/sinks.
///  4. Keep a global VolumeLedger so conservation is auditable at runtime.
///
/// The invariant this enforces is the whole game's economy:
///   1 Oak Log (0.250 m³, 190 kg)
///     → 2 Oak Planks (0.200 m³, 152 kg) + Sawdust scrap (0.050 m³, 38 kg)
///   0.250 m³ in, 0.250 m³ out. 190 kg in, 190 kg out. Never more.
/// </summary>
public sealed class MaterialVolumeManager {
    public const double VolumeToleranceM3 = 1e-6;   // 1 cm³
    public const double MassToleranceKg = 1e-3;     // 1 g
    public static bool EnableConservationChecks = true;

    private readonly Dictionary<ushort, Material> _materials = new();
    private readonly Dictionary<string, CraftingRecipe> _recipes = new(StringComparer.Ordinal);
    private readonly VolumeLedger _ledger = new();

    public IReadOnlyDictionary<ushort, Material> Materials => _materials;
    public IReadOnlyDictionary<string, CraftingRecipe> Recipes => _recipes;
    public VolumeLedger Ledger => _ledger;

    public Material RegisterMaterial(Material material) {
        if (material.Id == 0) throw new ArgumentException("Material id 0 is reserved for Air.");
        if (_materials.ContainsKey(material.Id))
            throw new ArgumentException($"Material id {material.Id} ('{material.Name}') is already registered.");
        if (material.DensityKgPerM3 <= 0)
            throw new ArgumentException($"Material '{material.Name}' must have a positive density.");
        if (material.CompressiveStrengthKPa < 0)
            throw new ArgumentException($"Material '{material.Name}' cannot have negative strength.");
        _materials.Add(material.Id, material);
        return material;
    }

    public CraftingRecipe RegisterRecipe(CraftingRecipe recipe) {
        ValidateConservation(recipe);
        if (_recipes.ContainsKey(recipe.Id))
            throw new ArgumentException($"Recipe '{recipe.Id}' is already registered.");
        _recipes.Add(recipe.Id, recipe);
        return recipe;
    }

    /// <summary>
    /// The conservation gate, applied to every recipe at registration — a
    /// recipe that creates volume or mass simply cannot exist in the game.
    /// </summary>
    public static void ValidateConservation(CraftingRecipe recipe) {
        if (!EnableConservationChecks) return;
        var s = ComputeConservation(recipe);
        if (s.OutputVolumeM3 > s.InputVolumeM3 + VolumeToleranceM3)
            throw new ConservationViolationException(
                $"Recipe '{recipe.Id}': output volume {s.OutputVolumeM3:F4} m³ exceeds input volume {s.InputVolumeM3:F4} m³. " +
                "Processed material may never exceed its source — declare the surplus as scrap.");
        if (Math.Abs(s.OutputMassKg - s.InputMassKg) > MassToleranceKg)
            throw new ConservationViolationException(
                $"Recipe '{recipe.Id}': mass {s.InputMassKg:F3} kg → {s.OutputMassKg:F3} kg (Δ {s.OutputMassKg - s.InputMassKg:F3} kg). " +
                "Mass must be conserved exactly; any missing mass must be declared as scrap output.");
    }

    public static ConservationStats ComputeConservation(CraftingRecipe recipe) {
        double inV = 0, inM = 0, outV = 0, outM = 0;
        foreach (var p in recipe.Inputs) { inV += p.VolumeM3; inM += p.MassKg; }
        foreach (var p in recipe.Outputs) { outV += p.VolumeM3; outM += p.MassKg; }
        return new ConservationStats(inV, inM, outV, outM);
    }

    /// <summary>
    /// The only sanctioned way to transform matter. Atomic: either the whole
    /// recipe happens (inputs removed, outputs placed, ledger updated) or
    /// nothing happens.
    /// </summary>
    public bool TryCraft(string recipeId, IInventorySource source, IInventorySink sink) {
        if (!_recipes.TryGetValue(recipeId, out var recipe)) return false;
        if (!source.TryTakeAll(recipe.Inputs)) return false;          // reserve inputs (all-or-nothing)
        if (!sink.TryAddAll(recipe.Outputs)) {                        // commit outputs; refund on rejection
            source.ReturnAll(recipe.Inputs);
            return false;
        }
        foreach (var p in recipe.Inputs) _ledger.Add(p.Material, -p.VolumeM3);
        foreach (var p in recipe.Outputs) _ledger.Add(p.Material, p.VolumeM3);
        return true;
    }

    public double MassOf(double volumeM3, Material m) => m.MassOf(volumeM3);
    public double VolumeOf(double massKg, Material m) => m.VolumeOf(massKg);
}
