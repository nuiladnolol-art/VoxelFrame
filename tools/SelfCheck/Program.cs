using System;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.World;

namespace VoxelFrame.Tools.SelfCheck;

/// <summary>
/// Zero-dependency verification of VoxelFrame.Core components:
///   1. Container inventory slots, capacity, insertion & removal
///   2. VoxelData representation and subgrid layer masks
///   3. Chunk voxel indexing and storage
///   4. WorldGrid voxel manipulation
/// </summary>
internal static class Program {
    private static int _passed, _failed;

    private static void Main() {
        Console.WriteLine("== VoxelFrame.Core self-check ==");
        Console.WriteLine("Verifying: inventory containers · voxel data · chunks · world grid\n");

        Test_Container_BasicOperations();
        Test_VoxelData_Properties();
        Test_Chunk_Indexing();
        Test_WorldGrid_VoxelAccess();

        Console.WriteLine($"\n{_passed} passed, {_failed} failed.");
        if (_failed > 0) Environment.Exit(1);
    }

    private static void Check(bool condition, string what) {
        Console.WriteLine($"    {(condition ? "PASS" : "FAIL")}  {what}");
        if (condition) _passed++; else _failed++;
    }

    private static void Test_Container_BasicOperations() {
        Console.WriteLine("[1] Container: slot insertion and capacity limits");
        var container = new Container();
        var mat = new VoxelFrame.Core.Materials.Material { Id = 1, Name = "Dirt" };
        var def = new ItemDefinition { Id = 1, Name = "Dirt", MaxStack = 64, Material = mat };
        var item = new ItemInstance(def, 1);

        Check(container.Slots.Length == 36, "container initialized with 36 slots");
        Check(container.TryInsert(item, 32), "inserted 32 dirt");
        Check(container.Slots[0].HasValue && container.Slots[0]!.Value.Quantity == 32, "slot 0 contains 32 dirt");

        Check(container.TryInsert(item, 40), "inserted 40 more dirt (overflows to next slot)");
        Check(container.Slots[0]!.Value.Quantity == 64, "slot 0 filled to max stack 64");
        Check(container.Slots[1]!.Value.Quantity == 8, "slot 1 contains remainder 8");

        Check(container.CountOf(def) == 72, "total count of dirt is 72");

        Check(container.TryRemove(def, 10), "removed 10 dirt");
        Check(container.CountOf(def) == 62, "remaining count of dirt is 62");
    }

    private static void Test_VoxelData_Properties() {
        Console.WriteLine("[2] VoxelData: bitfields and state");
        var vox = new VoxelData { TypeId = 42, Flags = VoxelFlags.Solid, SubGridLayerMask = 0b1010 };
        Check(vox.TypeId == 42, "TypeId is 42");
        Check(vox.IsSolid, "IsSolid is true");
        Check(vox.SubGridLayerMask == 0b1010, "SubGridLayerMask preserved");
    }

    private static void Test_Chunk_Indexing() {
        Console.WriteLine("[3] Chunk: indexing and local voxels");
        var chunk = new Chunk(new Vec3i(0, 0, 0));
        int idx = Chunk.Index(5, 10, 7);
        var vox = new VoxelData { TypeId = 3, Flags = VoxelFlags.Solid };
        chunk.SetVoxel(idx, in vox);

        var retrieved = chunk.Get(idx);
        Check(retrieved.TypeId == 3 && retrieved.IsSolid, "retrieved voxel matches set data");
    }

    private static void Test_WorldGrid_VoxelAccess() {
        Console.WriteLine("[4] WorldGrid: coordinate access across chunks");
        var world = new WorldGrid();
        var pos = new Vec3i(35, 12, -20);
        var vox = new VoxelData { TypeId = 7, Flags = VoxelFlags.Solid };

        world.SetVoxel(pos, vox);
        bool ok = world.TryGetVoxel(pos, out var readBack);
        Check(ok && readBack.TypeId == 7, "voxel retrieved across chunk boundaries matches");
    }
}
