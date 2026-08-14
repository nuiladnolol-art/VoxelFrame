using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.Materials;
using VoxelFrame.Core.Physics;
using VoxelFrame.Core.World;

namespace VoxelFrame.Tools.SelfCheck;

/// <summary>
/// Zero-dependency verification of the engine's core laws:
///   1. volume/mass conservation, 2. volumetric inventory,
///   3. sub-voxel frameworks, 4. structural integrity.
/// Run: dotnet run --project tools/SelfCheck
/// </summary>
internal static class Program {
    private static int _passed, _failed;
    private static ulong _nextInstanceId;
    private static ulong NextId() => _nextInstanceId++;

    private static void Main() {
        Console.WriteLine("== VoxelFrame.Core self-check ==");
        Console.WriteLine("Verifying: mass/volume conservation · volumetric inventory ·\n" +
                          "sub-voxel frameworks · structural integrity.\n");

        var content = new GameContent();
        Test_Recipe_Conservation(content);
        Test_Recipe_Violation_Rejected(content);
        Test_Crafting_AtomicFlow(content);
        Test_Inventory_VolumeAndWeightLimits(content);
        Test_SubGrid_BeamPlacement();
        Test_Structural_StableColumn(content);
        Test_Structural_Collapse(content);

        Console.WriteLine($"\n{_passed} passed, {_failed} failed.");
        if (_failed > 0) Environment.Exit(1);
    }

    private static void Check(bool condition, string what) {
        Console.WriteLine($"    {(condition ? "PASS" : "FAIL")}  {what}");
        if (condition) _passed++; else _failed++;
    }

    // ── 1. Conservation of volume and mass ───────────────────────────────────

    private static void Test_Recipe_Conservation(GameContent c) {
        Console.WriteLine("[1] Recipe conservation: 1 Oak Log → 2 Planks + Sawdust scrap");
        var stats = MaterialVolumeManager.ComputeConservation(c.Materials.Recipes["sawing.oak"]);
        Console.WriteLine($"    in:  {stats.InputVolumeM3:F3} m³, {stats.InputMassKg:F1} kg");
        Console.WriteLine($"    out: {stats.OutputVolumeM3:F3} m³, {stats.OutputMassKg:F1} kg");
        Check(Math.Abs(stats.InputVolumeM3 - 0.25) < 1e-9, "input volume 0.250 m³");
        Check(Math.Abs(stats.OutputVolumeM3 - 0.25) < 1e-9, "output volume 0.250 m³ — never more than the source");
        Check(Math.Abs(stats.InputMassKg - 190.0) < 1e-9, "input mass 190.0 kg");
        Check(Math.Abs(stats.OutputMassKg - 190.0) < 1e-9, "output mass 190.0 kg — conserved exactly");
    }

    private static void Test_Recipe_Violation_Rejected(GameContent c) {
        Console.WriteLine("[2] Conservation enforcement: violating recipes are rejected");
        bool volumeRejected = false, massRejected = false;
        try {
            c.Materials.RegisterRecipe(new CraftingRecipe {
                Id = "cheat.volume",
                Inputs = new RecipePart[] { new ItemPart(c.OakLog, 1) },
                Outputs = new RecipePart[] { new ItemPart(c.OakPlank, 3) },   // 0.30 m³ > 0.25 m³
            });
        } catch (ConservationViolationException) { volumeRejected = true; }
        try {
            c.Materials.RegisterRecipe(new CraftingRecipe {
                Id = "cheat.mass",
                Inputs = new RecipePart[] { new ItemPart(c.OakLog, 1) },
                Outputs = new RecipePart[] {
                    new ItemPart(c.OakPlank, 2),
                    new BulkPart(c.Sawdust, 0.06),   // +45.6 kg → 197.6 kg > 190 kg
                },
            });
        } catch (ConservationViolationException) { massRejected = true; }
        Check(volumeRejected, "volume-inflating recipe rejected");
        Check(massRejected, "mass-inflating recipe rejected");
    }

    private static void Test_Crafting_AtomicFlow(GameContent c) {
        Console.WriteLine("[3] Crafting: atomic flow through the ledger");
        var source = new Container(0.5, 500);
        var sink = new Container(0.5, 500);
        source.TryInsert(new ItemInstance(c.OakLog, NextId()));
        Check(c.Materials.TryCraft("sawing.oak", source, sink), "craft succeeds");
        Check(Math.Abs(source.UsedVolumeM3) < 1e-9, "source emptied exactly");
        Check(Math.Abs(sink.UsedMassKg - 190.0) < 1e-9, "sink mass = 190 kg (mass conserved)");
        Check(Math.Abs(sink.UsedVolumeM3 - 0.30) < 1e-9, "sink volume = 0.30 m³ (0.20 planks + 0.10 packed sawdust)");
        Check(sink.Entries.Count == 1 && sink.Entries[0].Quantity == 2, "exactly 2 planks");
        Check(Math.Abs(sink.BulkVolumeM3(c.Sawdust.Id) - 0.05) < 1e-9, "0.05 m³ sawdust scrap");
        Check(c.Materials.Ledger.TotalVolumeM3 == 0, "global ledger balanced: nothing created, nothing lost");

        // atomicity: a sink that cannot hold the outputs must reject the craft,
        // and the source must be refunded untouched.
        var source2 = new Container(0.5, 500);
        var sink2 = new Container(0.05, 50);
        source2.TryInsert(new ItemInstance(c.OakLog, NextId()));
        Check(!c.Materials.TryCraft("sawing.oak", source2, sink2), "craft rejected when sink cannot hold outputs");
        Check(Math.Abs(source2.UsedVolumeM3 - 0.25) < 1e-9, "inputs refunded after failed craft");
        Check(Math.Abs(sink2.UsedVolumeM3) < 1e-9, "sink stays empty after failed craft");
    }

    // ── 2. Volumetric inventory ──────────────────────────────────────────────

    private static void Test_Inventory_VolumeAndWeightLimits(GameContent c) {
        Console.WriteLine("[4] Volumetric inventory: no abstract stacks");
        var backpack = new Container(0.30, 250);
        var log = new ItemInstance(c.OakLog, NextId());
        Check(backpack.TryInsert(log, 1), "one log fits (0.25 m³ / 190 kg ≤ 0.30 m³ / 250 kg)");
        Check(Math.Abs(backpack.UsedVolumeM3 - 0.25) < 1e-9, "used volume tracks 0.25 m³ exactly");
        Check(!backpack.TryInsert(log, 1), "second log rejected — 0.50 m³ exceeds volume");

        // Stack size emerges from physics: a 0.30 m³ box holds 3 planks — not 64.
        var plankBox = new Container(0.30, 300);
        var plank = new ItemInstance(c.OakPlank, NextId());
        Check(plankBox.TryInsert(plank, 3), "3 planks fit (0.30 m³ exactly)");
        Check(!plankBox.TryInsert(plank, 1), "4th plank rejected by volume — the limit is physics, not 64");

        // The mass limit bites first for dense materials.
        var ironPack = new Container(0.30, 200);
        var ingot = new ItemInstance(c.IronIngot, NextId());   // 0.02 m³ → 157 kg
        Check(ironPack.TryInsert(ingot, 1), "one iron ingot fits (157 kg ≤ 200 kg)");
        Check(!ironPack.TryInsert(ingot, 1), "second ingot rejected by MASS (314 kg > 200 kg)");
    }

    // ── 3. Sub-voxel frameworks ──────────────────────────────────────────────

    private static void Test_SubGrid_BeamPlacement() {
        Console.WriteLine("[5] Sub-voxel framework: components inside a 1 m³ cell");
        var world = new WorldGrid();
        var chunk = world.GetOrCreateChunk(new Vec3i(0, 0, 0));
        int idx = Chunk.Index(4, 4, 4);
        var sg = chunk.GetOrCreateSubGrid(idx);

        // 0.1 × 0.1 × 1.0 m oak beam: 0.01 m³, 7.6 kg, 80 kN capacity.
        var beam = new FrameworkComponent(1, ComponentOrientation.Vertical, 8, 0.01f, 7.6f, 80f);
        Check(sg.TryPlace(beam, 0, 0, 0), "vertical beam spans the full cell");
        Check(Math.Abs(sg.TotalVolumeM3 - 0.01) < 1e-5 && Math.Abs(sg.TotalMassKg - 7.6) < 1e-5,
              "exact volume/mass accounting (float-precision tolerance)");
        Check(!sg.TryPlace(beam, 0, 0, 0), "occupied sub-cells reject a second beam");

        var beam2 = new FrameworkComponent(1, ComponentOrientation.AxisX, 4, 0.005f, 3.8f, 40f);
        Check(sg.TryPlace(beam2, 3, 0, 0), "horizontal beam fits in free cells");
        Check(!sg.TryPlace(beam2, 7, 0, 0), "beam crossing the cell boundary rejected");

        chunk.RefreshPhysicals(idx);
        var v = chunk.Get(idx);
        Check(Math.Abs(v.Weight - 11.4) < 1e-3 && Math.Abs(v.LoadBearingCapacity - 120) < 1e-3,
              "voxel physicals recomputed from components (11.4 kg, 120 kN)");

        Check(sg.TryRemove(0), "beam removed");
        chunk.RefreshPhysicals(idx);
        Check(Math.Abs(chunk.Get(idx).Weight - 3.8) < 1e-3, "physicals after removal (3.8 kg)");
    }

    // ── 4. Structural integrity ──────────────────────────────────────────────

    private static void Test_Structural_StableColumn(GameContent c) {
        Console.WriteLine("[6] Structural integrity: 4-block column stands");
        var world = new WorldGrid();
        var physics = new PhysicsGridCoordinator(world, workerCount: 1);
        try {
            world.SetVoxel(new Vec3i(0, 0, 0), c.Voxels.CreateVoxel(c.StoneVoxelId));   // terrain anchor
            for (int y = 1; y <= 4; y++)
                world.SetVoxel(new Vec3i(0, y, 0), c.Voxels.CreateVoxel(c.PineVoxelId));   // 500 kg each
            var events = RunUntilSettled(physics);
            Check(!events.Any(e => e.Kind == StructuralEventKind.NodeFailed),
                  "4 × 4.905 kN = 19.6 kN ≤ 25 kN capacity → stands");
            Check(!events.Any(e => e.Kind == StructuralEventKind.IslandDetached),
                  "no spurious detachments — the column is one connected structure");
        } finally { physics.Dispose(); }
    }

    private static void Test_Structural_Collapse(GameContent c) {
        Console.WriteLine("[7] Structural integrity: overloaded column collapses");
        var world = new WorldGrid();
        var physics = new PhysicsGridCoordinator(world, workerCount: 1);
        try {
            world.SetVoxel(new Vec3i(0, 0, 0), c.Voxels.CreateVoxel(c.StoneVoxelId));
            for (int y = 1; y <= 6; y++)
                world.SetVoxel(new Vec3i(0, y, 0), c.Voxels.CreateVoxel(c.PineVoxelId));
            var events = RunUntilSettled(physics);
            Check(events.Any(e => e.Kind == StructuralEventKind.NodeFailed),
                  "6 × 4.905 kN = 29.4 kN > 25 kN capacity → bottom voxel fails");
            Check(!events.Any(e => e.Kind == StructuralEventKind.IslandDetached),
                  "no spurious detachments — the column is one connected structure");
        } finally { physics.Dispose(); }
    }

    private static List<StructuralEvent> RunUntilSettled(PhysicsGridCoordinator physics, int maxTicks = 40) {
        var events = new List<StructuralEvent>();
        for (int t = 0; t < maxTicks; t++) {
            physics.Tick(1.0 / 20.0);                       // dispatch dirty islands
            for (int spin = 0; spin < 100 && physics.PendingJobs > 0; spin++) Thread.Sleep(5);
            physics.Tick(1.0 / 20.0);                       // drain completed solves
            while (physics.TryDequeueEvent(out var ev)) events.Add(ev);
            if (physics.IslandCount > 0 && events.Count > 0) break;
        }
        return events;
    }

    // ── Bootstrap content ────────────────────────────────────────────────────

    private sealed class GameContent {
        public readonly MaterialVolumeManager Materials = new();
        public readonly VoxelCatalog Voxels = new();

        public readonly Material Oak, Pine, Stone, Iron, Sawdust;
        public readonly ItemDefinition OakLog, OakPlank, IronIngot;
        public readonly ushort StoneVoxelId, PineVoxelId;

        public GameContent() {
            Oak = Materials.RegisterMaterial(new Material {
                Id = 1, Name = "Oak", DensityKgPerM3 = 760, CompressiveStrengthKPa = 8000,
                Category = MaterialCategory.Wood,
            });
            Pine = Materials.RegisterMaterial(new Material {
                Id = 2, Name = "Pine", DensityKgPerM3 = 500, CompressiveStrengthKPa = 2500,
                Category = MaterialCategory.Wood,
            });
            Stone = Materials.RegisterMaterial(new Material {
                Id = 3, Name = "Stone", DensityKgPerM3 = 2600, CompressiveStrengthKPa = 18000,
                Category = MaterialCategory.Stone,
            });
            Iron = Materials.RegisterMaterial(new Material {
                Id = 4, Name = "Iron", DensityKgPerM3 = 7850, CompressiveStrengthKPa = 60000,
                Category = MaterialCategory.Metal,
            });
            Sawdust = Materials.RegisterMaterial(new Material {
                Id = 5, Name = "Sawdust", DensityKgPerM3 = 760, Category = MaterialCategory.Wood,
                State = PhysicalState.Bulk, PackingFactor = 2.0,
            });

            OakLog = new ItemDefinition { Id = 1, Name = "Oak Log", Material = Oak, VolumeM3 = 0.25 };
            OakPlank = new ItemDefinition { Id = 2, Name = "Oak Plank", Material = Oak, VolumeM3 = 0.10 };
            IronIngot = new ItemDefinition { Id = 3, Name = "Iron Ingot", Material = Iron, VolumeM3 = 0.02 };

            Materials.RegisterRecipe(new CraftingRecipe {
                Id = "sawing.oak",
                Inputs = new RecipePart[] { new ItemPart(OakLog, 1) },
                Outputs = new RecipePart[] {
                    new ItemPart(OakPlank, 2),
                    new BulkPart(Sawdust, 0.05, isScrap: true),
                },
                CraftTimeSeconds = 4,
            });

            Voxels.Register(new VoxelType { Id = 1, Name = "Oak Block", Material = Oak, FillVolumeM3 = 1.0, LoadCapacityKN = 8000 });
            PineVoxelId = 2;
            Voxels.Register(new VoxelType { Id = PineVoxelId, Name = "Pine Block", Material = Pine, FillVolumeM3 = 1.0, LoadCapacityKN = 25 });
            StoneVoxelId = 3;
            Voxels.Register(new VoxelType { Id = StoneVoxelId, Name = "Stone", Material = Stone, FillVolumeM3 = 1.0, LoadCapacityKN = 0 });   // terrain anchor
        }
    }
}
