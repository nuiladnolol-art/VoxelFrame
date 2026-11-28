using System;
using System.IO;
using System.IO.Compression;
using Voxalith.Core;
using Voxalith.Core.Inventory;
using Voxalith.Core.Net;
using Voxalith.Core.Security;
using Voxalith.Core.World;
using Voxalith.Shared;

namespace Voxalith.Tools.SelfCheck;

/// <summary>
/// Zero-dependency verification of Voxalith.Core and shared package-security helpers:
///   1. Container inventory slots, capacity, insertion & removal
///   2. VoxelData representation and subgrid layer masks
///   3. Chunk voxel indexing and storage
///   4. WorldGrid voxel manipulation
///   5. Container edge cases & durability stacks
///   6. WorldGrid negative coordinates & chunk boundaries
///   7. Security helpers: PathSafety, SemVer, Zip Slip & SHA256SUMS parsing
///   8. Core Noise determinism & ProtocolFraming limits
/// </summary>
internal static class Program {
    private static int _passed, _failed;

    private static void Main() {
        Console.WriteLine("== Voxalith.Core self-check ==");
        Console.WriteLine("Verifying: inventory · voxel data · chunks · world grid · security · noise · framing\n");

        Test_Container_BasicOperations();
        Test_VoxelData_Properties();
        Test_Chunk_Indexing();
        Test_WorldGrid_VoxelAccess();
        Test_Container_EdgeCasesAndDurability();
        Test_WorldGrid_NegativeCoords();
        Test_Security_PathSafetySemVerAndZipSlip();
        Test_Noise_And_ProtocolFraming();

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
        var mat = new Voxalith.Core.Materials.Material { Id = 1, Name = "Dirt" };
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

    private static void Test_Container_EdgeCasesAndDurability() {
        Console.WriteLine("[5] Container: edge cases, InsertAtOrPack & distinct durability items");
        bool invalidCapThrew = false;
        try {
            _ = new Container(0);
        } catch (ArgumentOutOfRangeException) {
            invalidCapThrew = true;
        }
        Check(invalidCapThrew, "zero capacity throws ArgumentOutOfRangeException");

        var mat = new Voxalith.Core.Materials.Material { Id = 1, Name = "Stone" };
        var stoneDef = new ItemDefinition { Id = 1, Name = "Stone", MaxStack = 64, Material = mat };
        var stone = new ItemInstance(stoneDef, 1);

        var c = new Container(2);
        Check(!c.TryInsert(stone, 0) && !c.TryInsert(stone, -3), "TryInsert rejects non-positive quantities");
        Check(c.TryInsert(stone, 64), "slot 0 filled with 64");
        Check(!c.TryRemove(stoneDef, 0) && !c.TryRemove(stoneDef, 65), "TryRemove rejects 0 or excessive quantity without mutating");
        Check(c.CountOf(stoneDef) == 64, "count remains 64 after rejected removal");

        Check(c.InsertAtOrPack(99, new ItemEntry(stone, 10)), "InsertAtOrPack falls back to free slot when index out of range");
        Check(c.Slots[1]?.Quantity == 10, "packed into slot 1");

        var full = new Container(1);
        full.InsertAt(0, new ItemEntry(stone, 64));
        Check(!full.InsertAtOrPack(-1, new ItemEntry(stone, 1)), "InsertAtOrPack returns false when container is full");

        // Tools with MaxStack = 1 keep separate slots and individual durability
        var swordDef = new ItemDefinition { Id = 2, Name = "Sword", MaxStack = 1, Material = mat };
        var swordA = new ItemInstance(swordDef, 101) { Durability = 120 };
        var swordB = new ItemInstance(swordDef, 102) { Durability = 45 };
        var toolBox = new Container(4);
        Check(toolBox.TryInsert(swordA, 1) && toolBox.TryInsert(swordB, 1), "inserted two distinct durability swords");
        Check(toolBox.CountOf(swordDef) == 2 &&
              toolBox.Slots[0]?.Item.Durability == 120 &&
              toolBox.Slots[1]?.Item.Durability == 45,
              "distinct durability items do not merge and keep exact durability");
    }

    private static void Test_WorldGrid_NegativeCoords() {
        Console.WriteLine("[6] WorldGrid & Chunk: negative coordinates across chunk boundaries");
        var world = new WorldGrid();
        var negPos = new Vec3i(-17, -1, -33);
        var expectedChunk = Chunk.CoordOf(negPos);
        Check(expectedChunk == new Vec3i(-1, -1, -2), "Chunk.CoordOf maps negative coords via arithmetic shift");

        var vox = new VoxelData { TypeId = 19, Flags = VoxelFlags.Solid };
        world.SetVoxel(negPos, vox);
        Check(world.TryGetVoxel(negPos, out var readNeg) && readNeg == vox, "negative coordinate voxel round-trip matches");

        var boundaryA = new Vec3i(-1, 0, -32);
        var boundaryB = new Vec3i(0, 0, -33);
        world.SetVoxel(boundaryA, new VoxelData { TypeId = 5, Flags = VoxelFlags.Solid });
        world.SetVoxel(boundaryB, new VoxelData { TypeId = 6, Flags = VoxelFlags.None });
        Check(world.TryGetVoxel(boundaryA, out var ra) && ra.TypeId == 5 && ra.IsSolid, "chunk -1 boundary voxel accurate");
        Check(world.TryGetVoxel(boundaryB, out var rb) && rb.TypeId == 6 && !rb.IsSolid, "chunk -2 boundary voxel accurate");
        Check(!world.TryGetVoxel(new Vec3i(500, 10, 500), out _), "unloaded chunk returns false");

        world.SetVoxel(negPos, VoxelData.Air);
        Check(world.TryGetVoxel(negPos, out var cleared) && cleared.IsAir, "cleared negative coordinate returns Air");
    }

    private static void Test_Security_PathSafetySemVerAndZipSlip() {
        Console.WriteLine("[7] Security: PathSafety, SemVer, Zip Slip & SHA256SUMS");
        string s1 = PathSafety.SanitizeFileComponent("../../etc/passwd", "fallback");
        string s2 = PathSafety.SanitizeFileComponent("..\\..\\evil", "World");
        Check(s1 == "fallback" && !s1.Contains("..") && s2 == "World",
              "SanitizeFileComponent blocks traversal sequences");
        Check(PathSafety.SanitizeFileComponent("CON", "Player") == "Player" &&
              PathSafety.SanitizeFileComponent("COM1", "Player") == "Player",
              "SanitizeFileComponent rejects Windows reserved device names");

        string rootDir = Path.Combine(Path.GetTempPath(), "vox_sec_root");
        string siblingDir = Path.Combine(Path.GetTempPath(), "vox_sec_root_evil", "file.txt");
        Check(PathSafety.IsPathInsideDirectory(Path.Combine(rootDir, "world.dat"), rootDir), "child path recognized inside directory");
        Check(!PathSafety.IsPathInsideDirectory(siblingDir, rootDir), "sibling prefix attack blocked");
        Check(!PathSafety.TryResolveInsideDirectory(rootDir, "../escape.txt", out _), "relative escape blocked");

        // SemVer pre-release comparison
        Check(PackageSecurity.IsNewerVersion("v1.1.0", "v1.1.0-alpha"), "release v1.1.0 > pre-release v1.1.0-alpha");
        Check(PackageSecurity.IsNewerVersion("v1.1.0-beta", "v1.1.0-alpha"), "v1.1.0-beta > v1.1.0-alpha");
        Check(!PackageSecurity.IsNewerVersion("v1.1.0-alpha", "v1.1.0"), "pre-release v1.1.0-alpha is NOT newer than release");
        Check(PackageSecurity.CompareVersions("v1.2.0-alpha", "v1.1.9") > 0, "v1.2.0-alpha > v1.1.9");
        Check(PackageSecurity.SanitizeTagComponent("../bad/tag") == "" &&
              PackageSecurity.SanitizeTagComponent("v1.1.0-alpha") == "v1.1.0-alpha",
              "SanitizeTagComponent validates release tags");

        // SHA256SUMS parsing
        string sums = "deadbeef  other.zip\r\nabcdef0123456789 *Voxalith-v1.1.0-win-x64.zip\r\n";
        Check(PackageSecurity.ParseExpectedSha256(sums, "Voxalith-v1.1.0-win-x64.zip") == "abcdef0123456789",
              "ParseExpectedSha256 finds binary-marked archive checksum");
        Check(PackageSecurity.ParseExpectedSha256(sums, "missing.zip") == null,
              "ParseExpectedSha256 returns null for missing file");

        // Zip Slip in-memory archive guard
        string sandbox = Path.Combine(Path.GetTempPath(), $"vox_zipcheck_{Guid.NewGuid():N}");
        try {
            string goodTarget = Path.Combine(sandbox, "good");
            using (var msGood = new MemoryStream()) {
                using (var za = new ZipArchive(msGood, ZipArchiveMode.Create, leaveOpen: true)) {
                    var e = za.CreateEntry("sub/hello.txt");
                    using var sw = new StreamWriter(e.Open());
                    sw.Write("ok");
                }
                msGood.Position = 0;
                using var zr = new ZipArchive(msGood, ZipArchiveMode.Read);
                PackageSecurity.ExtractZipSafe(zr, goodTarget);
            }
            Check(File.Exists(Path.Combine(goodTarget, "sub", "hello.txt")), "ExtractZipSafe extracts valid nested files");

            string badTarget = Path.Combine(sandbox, "bad");
            bool blockedZipSlip = false;
            using (var msBad = new MemoryStream()) {
                using (var za = new ZipArchive(msBad, ZipArchiveMode.Create, leaveOpen: true)) {
                    var e = za.CreateEntry("../../pwned.txt");
                    using var sw = new StreamWriter(e.Open());
                    sw.Write("evil");
                }
                msBad.Position = 0;
                using var zr = new ZipArchive(msBad, ZipArchiveMode.Read);
                try {
                    PackageSecurity.ExtractZipSafe(zr, badTarget);
                } catch (InvalidDataException) {
                    blockedZipSlip = true;
                }
            }
            Check(blockedZipSlip && !File.Exists(Path.Combine(sandbox, "pwned.txt")),
                  "ExtractZipSafe blocks Zip Slip path traversal without writing outside target");
        } finally {
            try { if (Directory.Exists(sandbox)) Directory.Delete(sandbox, true); } catch { }
        }
    }

    private static void Test_Noise_And_ProtocolFraming() {
        Console.WriteLine("[8] Noise determinism & ProtocolFraming limits");
        var n1 = new Noise(98765);
        var n2 = new Noise(98765);
        float a = n1.Fractal(12.34f, -56.78f, 4);
        float b = n2.Fractal(12.34f, -56.78f, 4);
        float n3d = n1.Fractal(1.5f, 2.5f, -3.5f, 3);
        Check(Math.Abs(a - b) < 1e-6f && a >= 0f && a <= 1f && n3d >= 0f && n3d <= 1f,
              "Noise 2D/3D fractal generation is deterministic and bounded [0..1]");

        Check(ProtocolFraming.SanitizePlayerName("../CON", "Fallback") == "Fallback" &&
              ProtocolFraming.SanitizePlayerName("CON", "Fallback") == "Fallback",
              "ProtocolFraming.SanitizePlayerName rejects reserved CON name");
        Check(ProtocolFraming.SanitizePlayerName("User/\\:Name!", "Fallback") == "UserName",
              "ProtocolFraming.SanitizePlayerName strips path/punctuation chars");

        byte[] small = ProtocolFraming.BuildPacket(5, w => w.Write("hello"));
        using (var ms = new MemoryStream(small)) {
            bool ok = ProtocolFraming.TryReadFrame(ms, 1024, 4096, 99, false, out byte pt, out byte[] buf, out int len);
            if (ok) System.Buffers.ArrayPool<byte>.Shared.Return(buf);
            Check(ok && pt == 5 && len > 1, "ProtocolFraming round-trips standard framed packet");
        }

        byte[] oversized = ProtocolFraming.BuildPacket(5, w => w.Write(new byte[256]));
        using (var ms = new MemoryStream(oversized)) {
            bool ok = ProtocolFraming.TryReadFrame(ms, 64, 4096, 99, allowLargePacket: true, out _, out byte[] buf, out _);
            if (ok) System.Buffers.ArrayPool<byte>.Shared.Return(buf);
            Check(!ok, "ProtocolFraming rejects non-sync packet exceeding maxStandardPacketSize");
        }

        byte[] allowedLarge = ProtocolFraming.BuildPacket(99, w => w.Write(new byte[256]));
        using (var ms = new MemoryStream(allowedLarge)) {
            bool ok = ProtocolFraming.TryReadFrame(ms, 64, 4096, 99, allowLargePacket: true, out byte pt, out byte[] buf, out int len);
            if (ok) System.Buffers.ArrayPool<byte>.Shared.Return(buf);
            Check(ok && pt == 99 && len > 256, "ProtocolFraming allows designated large sync packet when permitted");
        }
    }
}
