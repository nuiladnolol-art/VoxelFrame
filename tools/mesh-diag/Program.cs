using VoxelFrame.Core;
using VoxelFrame.Core.World;
using VoxelFrame.Game;

var Faces = new[] {
    (X: 1, Y: 0, Z: 0, Nx: 1f, Ny: 0f, Nz: 0f), (X: -1, Y: 0, Z: 0, Nx: -1f, Ny: 0f, Nz: 0f),
    (X: 0, Y: 1, Z: 0, Nx: 0f, Ny: 1f, Nz: 0f), (X: 0, Y: -1, Z: 0, Nx: 0f, Ny: -1f, Nz: 0f),
    (X: 0, Y: 0, Z: 1, Nx: 0f, Ny: 0f, Nz: 1f), (X: 0, Y: 0, Z: -1, Nx: 0f, Ny: 0f, Nz: -1f),
};

// Репликация логики ChunkMesher.Build без GPU: подсчёт граней по направлениям,
// вершин, и проверка данных на NaN/инф. Всё headless, Raylib не инициализируется.

var session = GameSession.NewGame(12345, headless: true);
var world = session.World;
var spawn = world.SpawnBlock;
Console.WriteLine($"Spawn: {spawn.X},{spawn.Y},{spawn.Z}");
Console.WriteLine($"Surface at (0,0): {world.Generator.SurfaceHeight(0, 0)}");

int totalVerts = 0;
long totalFaces = 0;
var perDir = new long[6];
var perType = new Dictionary<ushort, long>();
var nanCount = 0;

foreach (var gc in world.Chunks) {
    var faces = CountFaces(gc, world, out int verts, out int nan);
    totalVerts += verts;
    totalFaces += faces.Sum();
    nanCount += nan;
    for (int f = 0; f < 6; f++) perDir[f] += faces[f];
    for (int i = 0; i < Chunk.VoxelCount; i++) {
        ushort t = gc.Chunk.Get(i).TypeId;
        if (t != 0) perType[t] = perType.GetValueOrDefault(t) + 1;
    }
}

Console.WriteLine($"Chunks: {world.Chunks.Count}");
Console.WriteLine($"Total verts: {totalVerts:N0}, faces: {totalFaces:N0}, NaN/Inf in positions: {nanCount}");
Console.WriteLine($"Faces: +X={perDir[0]} -X={perDir[1]} +Y(top)={perDir[2]} -Y(bottom)={perDir[3]} +Z={perDir[4]} -Z={perDir[5]}");
Console.WriteLine("Voxels by type: " + string.Join(", ", perType.Select(kv => $"#{kv.Key}={kv.Value:N0}")));

// Проверка поверхности вокруг спавна: что реально стоит под ногами.
Console.WriteLine("\nColumn at (0, z):");
for (int wy = spawn.Y + 3; wy >= spawn.Y - 5; wy--) {
    var v = world.GetVoxel(new Vec3i(0, wy, 0));
    Console.WriteLine($"  y={wy}: type={v.TypeId} solid={v.IsSolid}");
}

// Проверка, что внутри чанка спавна твёрдые блоки видят друг друга как непрозрачные.
var gcSpawn = world.TryGetChunk(Chunk.CoordOf(spawn))!;
long topFaces = CountDirection(gcSpawn, world, 2);
long sideFaces = CountDirection(gcSpawn, world, 0) + CountDirection(gcSpawn, world, 4);
long bottomFaces = CountDirection(gcSpawn, world, 3);
Console.WriteLine($"\nSpawn chunk: top={topFaces} bottom={bottomFaces} sides(+X+Z)={sideFaces}");

// Сколько граней генерирует целиком забитый камень чанк (y=-1) — если все 6 сторон,
// то отбраковка соседей не работает.
var gcDeep = world.TryGetChunk(new Vec3i(0, -1, 0));
if (gcDeep != null) {
    long faces = CountFaces(gcDeep, world, out _, out _).Sum();
    long solid = 0;
    for (int i = 0; i < Chunk.VoxelCount; i++) if (gcDeep.Chunk.Get(i).IsSolid) solid++;
    Console.WriteLine($"\nFully-solid chunk (0,-1,0): solid voxels={solid:N0}, faces={faces:N0} (expect ~2*32*32=2048 boundary faces)");
}

long[] CountFaces(GameChunk gc, GameWorld world, out int verts, out int nan) {
    var res = new long[6];
    verts = 0; nan = 0;
    var neighbors = new GameChunk?[3, 3, 3];
    for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                neighbors[dx + 1, dy + 1, dz + 1] = world.TryGetChunk(gc.Coord + new Vec3i(dx, dy, dz));

    for (int lx = 0; lx < Chunk.SizeX; lx++)
        for (int ly = 0; ly < Chunk.SizeY; ly++)
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                var v = gc.Chunk.Get(Chunk.Index(lx, ly, lz));
                if (v.TypeId == 0) continue;
                var block = GameData.GetBlock(v.TypeId);
                if (!block.IsSolid && !block.IsOpaque) continue;
                for (int f = 0; f < 6; f++) {
                    var (dx, dy, dz, _, _, _) = Faces[f];
                    if (!IsOpaque(neighbors, lx, ly, lz, dx, dy, dz)) {
                        res[f]++;
                        verts += 4;
                    }
                }
            }
    return res;
}

long CountDirection(GameChunk gc, GameWorld world, int dir) {
    var neighbors = new GameChunk?[3, 3, 3];
    for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
            for (int dz = -1; dz <= 1; dz++)
                neighbors[dx + 1, dy + 1, dz + 1] = world.TryGetChunk(gc.Coord + new Vec3i(dx, dy, dz));
    long n = 0;
    for (int lx = 0; lx < Chunk.SizeX; lx++)
        for (int ly = 0; ly < Chunk.SizeY; ly++)
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                var v = gc.Chunk.Get(Chunk.Index(lx, ly, lz));
                if (v.TypeId == 0) continue;
                var block = GameData.GetBlock(v.TypeId);
                if (!block.IsSolid && !block.IsOpaque) continue;
                var (dx, dy, dz, _, _, _) = Faces[dir];
                if (!IsOpaque(neighbors, lx, ly, lz, dx, dy, dz)) n++;
            }
    return n;
}

bool IsOpaque(GameChunk?[,,] neighbors, int lx, int ly, int lz, int ox, int oy, int oz) {
    int nlx = lx + ox, nly = ly + oy, nlz = lz + oz;
    int cdx = 0, cdy = 0, cdz = 0;
    if (nlx < 0) { cdx = -1; nlx += 32; } else if (nlx >= 32) { cdx = 1; nlx -= 32; }
    if (nly < 0) { cdy = -1; nly += 32; } else if (nly >= 32) { cdy = 1; nly -= 32; }
    if (nlz < 0) { cdz = -1; nlz += 32; } else if (nlz >= 32) { cdz = 1; nlz -= 32; }
    var nChunk = neighbors[cdx + 1, cdy + 1, cdz + 1];
    if (nChunk == null) return false;
    ushort typeId = nChunk.Chunk.Get(nlx, nly, nlz).TypeId;
    return typeId != 0 && GameData.GetBlock(typeId).IsOpaque;
}
