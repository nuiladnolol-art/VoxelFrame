using System.Runtime.InteropServices;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

    /// <summary>
    /// Строит GPU-меши чанка: отбрасывание скрытых граней, запечённый свет.
    /// Цвет вершины: R = солнечный свет × AO, G = блочный свет × AO.
    /// Фактор неба применяет шейдер через uniform skyFactor (без пересборки мешей).
    /// </summary>
    public static class ChunkMesher {
    public const ushort MaxVertices = 65000;   // лимит ushort-индексов raylib

    private static readonly (int X, int Y, int Z, float Nx, float Ny, float Nz)[] Faces = {
        (1, 0, 0, 1, 0, 0), (-1, 0, 0, -1, 0, 0),
        (0, 1, 0, 0, 1, 0), (0, -1, 0, 0, -1, 0),
        (0, 0, 1, 0, 0, 1), (0, 0, -1, 0, 0, -1),
    };

    // Вершины граней (x,y,z смещения), нормаль, uv. Порядок: против часовой при взгляде снаружи.
    private static readonly (int X, int Y, int Z, float U, float V)[][] FaceVerts = {
        // +X
        new[] { (1, 0, 0, 0f, 1f), (1, 1, 0, 0f, 0f), (1, 1, 1, 1f, 0f), (1, 0, 1, 1f, 1f) },
        // -X
        new[] { (0, 0, 0, 0f, 1f), (0, 0, 1, 1f, 1f), (0, 1, 1, 1f, 0f), (0, 1, 0, 0f, 0f) },
        // +Y (верх)
        new[] { (0, 1, 1, 0f, 1f), (1, 1, 1, 1f, 1f), (1, 1, 0, 1f, 0f), (0, 1, 0, 0f, 0f) },
        // -Y (низ)
        new[] { (0, 0, 0, 0f, 1f), (1, 0, 0, 1f, 1f), (1, 0, 1, 1f, 0f), (0, 0, 1, 0f, 0f) },
        // +Z
        new[] { (0, 0, 1, 0f, 1f), (1, 0, 1, 1f, 1f), (1, 1, 1, 1f, 0f), (0, 1, 1, 0f, 0f) },
        // -Z
        new[] { (0, 0, 0, 0f, 1f), (0, 1, 0, 0f, 0f), (1, 1, 0, 1f, 0f), (1, 0, 0, 1f, 1f) },
    };

    [ThreadStatic] private static List<float>? _tVerts;
    [ThreadStatic] private static List<float>? _tNorms;
    [ThreadStatic] private static List<float>? _tUvs;
    [ThreadStatic] private static List<byte>? _tCols;
    [ThreadStatic] private static List<ushort>? _tIndices;

    /// <summary>
    /// Строит GPU-меши чанка. Если вершин больше лимита ushort-индексов —
    /// возвращает НЕСКОЛЬКО мешей (большие постройки не обрезаются).
    /// </summary>
    public static List<Mesh> Build(GameChunk gc, GameWorld world) {
        var result = new List<Mesh>();
        var verts = _tVerts ??= new List<float>(8192); verts.Clear();
        var norms = _tNorms ??= new List<float>(8192); norms.Clear();
        var uvs = _tUvs ??= new List<float>(8192); uvs.Clear();
        var cols = _tCols ??= new List<byte>(8192); cols.Clear();
        var indices = _tIndices ??= new List<ushort>(12288); indices.Clear();

        // Завершает текущий набор и начинает новый: индексы каждого меша
        // считаются от 0 своего меша, поэтому после сброса продолжаем так же.
        void Flush() {
            if (verts.Count == 0) return;
            result.Add(UploadMesh(verts, norms, uvs, cols, indices));
            verts.Clear(); norms.Clear(); uvs.Clear(); cols.Clear(); indices.Clear();
        }

        // Pre-fetch 3x3x3 neighborhood of chunks for lightning-fast mesh checks
        var neighbors = new GameChunk?[3, 3, 3];
        for (int dx = -1; dx <= 1; dx++) {
            for (int dy = -1; dy <= 1; dy++) {
                for (int dz = -1; dz <= 1; dz++) {
                    neighbors[dx + 1, dy + 1, dz + 1] = world.TryGetChunk(gc.Coord + new Vec3i(dx, dy, dz));
                }
            }
        }

        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int ly = 0; ly < Chunk.SizeY; ly++) {
                for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                    int idx = Chunk.Index(lx, ly, lz);
                    var v = gc.Chunk.Get(idx);
                    if (v.TypeId == 0) continue;
                    var block = GameData.GetBlock(v.TypeId);
                    bool isFluid = v.TypeId == GameData.BWater.Id || v.TypeId == GameData.BLava.Id;
                    bool isWater = v.TypeId == GameData.BWater.Id;
                    if (!block.IsSolid && !block.IsOpaque && !isFluid) continue;   // факелы рисуются как 3D-декор

                    var tiles = TextureAtlas.BlockTiles(v.TypeId);
                    for (int f = 0; f < 6; f++) {
                        var (dx, dy, dz, nx, ny, nz) = Faces[f];
                        ushort neighborType = GetTypeIdAtOffset(neighbors, gc, lx, ly, lz, dx, dy, dz);
                        
                        bool visible;
                        if (isFluid) {
                            // Жидкость показывает грань, только если рядом воздух (и соседний чанк загружен) или неполный блок
                            int nChunkDx = (lx + dx < 0) ? -1 : (lx + dx >= 32) ? 1 : 0;
                            int nChunkDy = (ly + dy < 0) ? -1 : (ly + dy >= 32) ? 1 : 0;
                            int nChunkDz = (lz + dz < 0) ? -1 : (lz + dz >= 32) ? 1 : 0;
                            bool isLoadedNeighbor = neighbors[nChunkDx + 1, nChunkDy + 1, nChunkDz + 1] != null;

                            visible = neighborType != v.TypeId && isLoadedNeighbor && (neighborType == 0 || !GameData.GetBlock(neighborType).IsOpaque);
                        } else if (v.TypeId == GameData.BGlass.Id) {
                            visible = neighborType != GameData.BGlass.Id && !IsOpaqueAtOffset(neighbors, lx, ly, lz, dx, dy, dz);
                        } else {
                            // Твердый блок показывает грань если рядом воздух, стекло, листва или полупрозрачная вода
                            visible = neighborType == 0 || !GameData.GetBlock(neighborType).IsOpaque || neighborType == GameData.BWater.Id;
                        }
                        if (!visible) continue;

                        if (verts.Count / 3 + 4 > MaxVertices) Flush();

                        int baseVertex = verts.Count / 3;
                        byte tile = GetRotatedFaceTile(tiles, v.TypeId, v.SubGridLayerMask, f);
                        var (sun, blockL) = GetFaceLight(neighbors, gc, lx, ly, lz, dx, dy, dz);
                        var (u0, v0, u1, v1) = TileUv(tile);
                        float worldOffsetX = gc.Coord.X * Chunk.SizeX;
                        float worldOffsetY = gc.Coord.Y * Chunk.SizeY;
                        float worldOffsetZ = gc.Coord.Z * Chunk.SizeZ;
                        float faceDir = f switch {
                            2 => 1.0f,       // Верхняя грань (+Y)
                            3 => 0.55f,      // Нижняя грань (-Y)
                            0 or 1 => 0.82f, // Боковые грани (±X)
                            _ => 0.68f       // Боковые грани (±Z)
                        };
                        byte shadeDir = (byte)(255f * faceDir);

                        foreach (var (fx, fy, fz, fu, fv) in FaceVerts[f]) {
                            float ao = (SaveSystem.FancyGraphics && !isFluid) ? GetVertexAO(neighbors, gc, lx, ly, lz, f, fx, fy, fz) : 1.0f;
                            byte shadeSun = (byte)(255f * sun * ao);
                            byte shadeBlock = (byte)(255f * blockL * ao);

                            float actualFy = fy;
                            if (isWater && f == 2 && fy > 0.5f) {
                                byte lvl = v.SubGridLayerMask;
                                if (lvl == 0 || lvl == FluidEngine.FallingLevel) actualFy = 0.90f;
                                else actualFy = MathF.Max(0.35f, 0.90f - lvl * 0.08f);
                            }

                            verts.Add(worldOffsetX + lx + fx);
                            verts.Add(worldOffsetY + ly + actualFy);
                            verts.Add(worldOffsetZ + lz + fz);
                            norms.Add(nx); norms.Add(ny); norms.Add(nz);
                            uvs.Add(u0 + (u1 - u0) * fu);
                            uvs.Add(v0 + (v1 - v0) * fv);
                            cols.Add(shadeSun); cols.Add(shadeBlock); cols.Add(shadeDir); cols.Add(isWater ? (byte)210 : (byte)255);
                        }
                        indices.Add((ushort)(baseVertex + 0));
                        indices.Add((ushort)(baseVertex + 1));
                        indices.Add((ushort)(baseVertex + 2));
                        indices.Add((ushort)(baseVertex + 0));
                        indices.Add((ushort)(baseVertex + 2));
                        indices.Add((ushort)(baseVertex + 3));
                    }
                }
            }
        }

        Flush();
        return result;
    }

    private static (float Sun, float Block) GetFaceLight(GameChunk?[,,] neighbors, GameChunk gc, int lx, int ly, int lz, int dx, int dy, int dz) {
        int nlx = lx + dx;
        int nly = ly + dy;
        int nlz = lz + dz;

        int cdx = 0, cdy = 0, cdz = 0;
        if (nlx < 0) { cdx = -1; nlx += Chunk.SizeX; }
        else if (nlx >= Chunk.SizeX) { cdx = 1; nlx -= Chunk.SizeX; }

        if (nly < 0) { cdy = -1; nly += Chunk.SizeY; }
        else if (nly >= Chunk.SizeY) { cdy = 1; nly -= Chunk.SizeY; }

        if (nlz < 0) { cdz = -1; nlz += Chunk.SizeZ; }
        else if (nlz >= Chunk.SizeZ) { cdz = 1; nlz -= Chunk.SizeZ; }

        var nChunk = neighbors[cdx + 1, cdy + 1, cdz + 1];
        if (nChunk != null) {
            int nIdx = Chunk.Index(nlx, nly, nlz);
            return (nChunk.SunLight[nIdx] / 15f, nChunk.BlockLight[nIdx] / 15f);
        }

        int wy = (gc.Coord.Y + cdy) * Chunk.SizeY + nly;
        int surf = gc.Surface[gc.SurfaceIndex(lx, lz)];
        float fallbackSun = wy >= surf ? 1f : 0f;
        return (fallbackSun, 0f);
    }

    private static (float U0, float V0, float U1, float V1) TileUv(byte tile) {
        var r = TextureAtlas.TileUv(tile);
        return (r.X, r.Y, r.X + r.Width, r.Y + r.Height);
    }

    private static unsafe Mesh UploadMesh(List<float> verts, List<float> norms, List<float> uvs,
                                          List<byte> cols, List<ushort> indices) {
        int vc = verts.Count / 3;
        var mesh = new Mesh { VertexCount = vc, TriangleCount = indices.Count / 3 };
        if (vc == 0) return mesh;

        mesh.Vertices = (float*)Raylib.MemAlloc((uint)(verts.Count * 4));
        mesh.Normals = (float*)Raylib.MemAlloc((uint)(norms.Count * 4));
        mesh.TexCoords = (float*)Raylib.MemAlloc((uint)(uvs.Count * 4));
        mesh.Colors = (byte*)Raylib.MemAlloc((uint)cols.Count);
        mesh.Indices = (ushort*)Raylib.MemAlloc((uint)(indices.Count * 2));

        CopyFrom(verts, mesh.Vertices);
        CopyFrom(norms, mesh.Normals);
        CopyFrom(uvs, mesh.TexCoords);
        CopyFrom(cols, mesh.Colors);
        CopyFrom(indices, mesh.Indices);

        Raylib.UploadMesh(ref mesh, false);
        return mesh;
    }

    private static unsafe void CopyFrom<T>(List<T> list, void* dest) where T : unmanaged {
        var span = CollectionsMarshal.AsSpan(list);
        fixed (T* src = span) {
            if (src != null)
                Buffer.MemoryCopy(src, dest, span.Length * sizeof(T), span.Length * sizeof(T));
        }
    }

    private static ushort GetTypeIdAtOffset(GameChunk?[,,] neighbors, GameChunk gc, int lx, int ly, int lz, int ox, int oy, int oz) {
        int nlx = lx + ox;
        int nly = ly + oy;
        int nlz = lz + oz;

        int cdx = 0, cdy = 0, cdz = 0;
        if (nlx < 0) { cdx = -1; nlx += 32; }
        else if (nlx >= 32) { cdx = 1; nlx -= 32; }

        if (nly < 0) { cdy = -1; nly += 32; }
        else if (nly >= 32) { cdy = 1; nly -= 32; }

        if (nlz < 0) { cdz = -1; nlz += 32; }
        else if (nlz >= 32) { cdz = 1; nlz -= 32; }

        var nChunk = neighbors[cdx + 1, cdy + 1, cdz + 1];
        if (nChunk == null) {
            int wy = (gc.Coord.Y + cdy) * 32 + nly;
            int surf = gc.Surface[gc.SurfaceIndex(lx, lz)];
            return wy < surf ? GameData.BStone.Id : (ushort)0;
        }
        return nChunk.Chunk.Get(nlx, nly, nlz).TypeId;
    }

    private static bool IsSolidAtOffset(GameChunk?[,,] neighbors, GameChunk gc, int lx, int ly, int lz, int ox, int oy, int oz) {
        int nlx = lx + ox;
        int nly = ly + oy;
        int nlz = lz + oz;

        int cdx = 0, cdy = 0, cdz = 0;
        if (nlx < 0) { cdx = -1; nlx += 32; }
        else if (nlx >= 32) { cdx = 1; nlx -= 32; }

        if (nly < 0) { cdy = -1; nly += 32; }
        else if (nly >= 32) { cdy = 1; nly -= 32; }

        if (nlz < 0) { cdz = -1; nlz += 32; }
        else if (nlz >= 32) { cdz = 1; nlz -= 32; }

        var nChunk = neighbors[cdx + 1, cdy + 1, cdz + 1];
        if (nChunk == null) {
            int wy = (gc.Coord.Y + cdy) * 32 + nly;
            int surf = gc.Surface[gc.SurfaceIndex(lx, lz)];
            return wy < surf;
        }
        return (nChunk.Chunk.Get(nlx, nly, nlz).Flags & VoxelFlags.Solid) != 0;
    }

    private static bool IsOpaqueAtOffset(GameChunk?[,,] neighbors, int lx, int ly, int lz, int ox, int oy, int oz) {
        int nlx = lx + ox;
        int nly = ly + oy;
        int nlz = lz + oz;

        int cdx = 0, cdy = 0, cdz = 0;
        if (nlx < 0) { cdx = -1; nlx += 32; }
        else if (nlx >= 32) { cdx = 1; nlx -= 32; }

        if (nly < 0) { cdy = -1; nly += 32; }
        else if (nly >= 32) { cdy = 1; nly -= 32; }

        if (nlz < 0) { cdz = -1; nlz += 32; }
        else if (nlz >= 32) { cdz = 1; nlz -= 32; }

        var nChunk = neighbors[cdx + 1, cdy + 1, cdz + 1];
        if (nChunk == null) return false;
        ushort typeId = nChunk.Chunk.Get(nlx, nly, nlz).TypeId;
        return typeId != 0 && GameData.GetBlock(typeId).IsOpaque;
    }

    private static float GetVertexAO(GameChunk?[,,] neighbors, GameChunk gc, int lx, int ly, int lz, int f, int fx, int fy, int fz) {
        var (dx, dy, dz, nx, ny, nz) = Faces[f];

        int offA = 0, offB = 0;
        int taxX = 0, taxY = 0, taxZ = 0;
        int tbxX = 0, tbyY = 0, tbzZ = 0;

        if (dx != 0) { // +X or -X (tangents are Y and Z)
            offA = (fy == 0) ? -1 : 1;
            offB = (fz == 0) ? -1 : 1;
            taxY = 1;
            tbzZ = 1;
        } else if (dy != 0) { // +Y or -Y (tangents are X and Z)
            offA = (fx == 0) ? -1 : 1;
            offB = (fz == 0) ? -1 : 1;
            taxX = 1;
            tbzZ = 1;
        } else if (dz != 0) { // +Z or -Z (tangents are X and Y)
            offA = (fx == 0) ? -1 : 1;
            offB = (fy == 0) ? -1 : 1;
            taxX = 1;
            tbyY = 1;
        }

        bool side1 = IsSolidAtOffset(neighbors, gc, lx, ly, lz, dx + offA * taxX, dy + offA * taxY, dz + offA * taxZ);
        bool side2 = IsSolidAtOffset(neighbors, gc, lx, ly, lz, dx + offB * tbxX, dy + offB * tbyY, dz + offB * tbzZ);
        bool corner = IsSolidAtOffset(neighbors, gc, lx, ly, lz, dx + offA * taxX + offB * tbxX, dy + offA * taxY + offB * tbyY, dz + offA * taxZ + offB * tbzZ);

        int ao = 0;
        if (side1 && side2) {
            ao = 3;
        } else {
            if (side1) ao++;
            if (side2) ao++;
            if (corner) ao++;
        }

        return ao switch {
            0 => 1.0f,
            1 => 0.72f,
            2 => 0.48f,
            _ => 0.28f,
        };
    }

    private static byte GetRotatedFaceTile(in TextureAtlas.BlockFaceTiles tiles, ushort typeId, byte facing, int f) {
        if (f == 2) return tiles.PosY; // Верх (+Y)
        if (f == 3) return tiles.NegY; // Низ (-Y)

        // Для блоков с ориентацией (Печь, Сундук, Кровать) поворачиваем 4 боковые грани
        if (typeId == GameData.BFurnace.Id || typeId == GameData.BChest.Id || typeId == GameData.BBed.Id || typeId == GameData.BBedHead.Id) {
            return facing switch {
                1 => f switch { // перед на -X (f=1)
                    1 => tiles.PosZ, // перед
                    0 => tiles.NegZ, // зад
                    4 => tiles.NegX, // бок
                    _ => tiles.PosX  // бок
                },
                2 => f switch { // перед на -Z (f=5)
                    5 => tiles.PosZ, // перед
                    4 => tiles.NegZ, // зад
                    0 => tiles.NegX, // бок
                    _ => tiles.PosX  // бок
                },
                3 => f switch { // перед на +X (f=0)
                    0 => tiles.PosZ, // перед
                    1 => tiles.NegZ, // зад
                    4 => tiles.PosX, // бок
                    _ => tiles.NegX  // бок
                },
                _ => f switch { // default 0: перед на +Z (f=4)
                    4 => tiles.PosZ, // перед
                    5 => tiles.NegZ, // зад
                    0 => tiles.PosX, // бок
                    _ => tiles.NegX  // бок
                }
            };
        }

        return f switch {
            0 => tiles.PosX,
            1 => tiles.NegX,
            4 => tiles.PosZ,
            _ => tiles.NegZ,
        };
    }
}
