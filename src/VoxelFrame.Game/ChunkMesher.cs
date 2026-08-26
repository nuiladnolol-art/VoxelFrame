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
    public static (List<Mesh> Opaque, List<Mesh> Translucent) Build(GameChunk gc, GameWorld world) {
        var resultOpaque = new List<Mesh>();
        var resultTranslucent = new List<Mesh>();
        var currentResult = resultOpaque;
        var verts = _tVerts ??= new List<float>(8192); verts.Clear();
        var norms = _tNorms ??= new List<float>(8192); norms.Clear();
        var uvs = _tUvs ??= new List<float>(8192); uvs.Clear();
        var cols = _tCols ??= new List<byte>(8192); cols.Clear();
        var indices = _tIndices ??= new List<ushort>(12288); indices.Clear();

        // Завершает текущий набор и начинает новый: индексы каждого меша
        // считаются от 0 своего меша, поэтому после сброса продолжаем так же.
        void Flush() {
            if (verts.Count == 0) return;
            currentResult.Add(UploadMesh(verts, norms, uvs, cols, indices));
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

        for (int pass = 0; pass < 2; pass++) {
            currentResult = pass == 0 ? resultOpaque : resultTranslucent;
            for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int ly = 0; ly < Chunk.SizeY; ly++) {
                for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                    int idx = Chunk.Index(lx, ly, lz);
                    var v = gc.Chunk.Get(idx);
                    if (v.TypeId == 0) continue;
                    var block = GameData.GetBlock(v.TypeId);
                    bool isFluid = v.TypeId == GameData.BWater.Id || v.TypeId == GameData.BLava.Id;
                    bool isWater = v.TypeId == GameData.BWater.Id;
                    bool isFoliage = v.TypeId == GameData.BTallGrass.Id || v.TypeId == GameData.BWheatCrop.Id;
                    bool isTranslucent = isFluid || isFoliage || GameData.IsDoor(v.TypeId) || v.TypeId == GameData.BGlass.Id || v.TypeId == GameData.BBed.Id || v.TypeId == GameData.BBedHead.Id
                        || v.TypeId == GameData.BNetherPortal.Id || v.TypeId == GameData.BEndPortal.Id;
                    if (pass == 0 && isTranslucent) continue;
                    if (pass == 1 && !isTranslucent) continue;

                    if (isFoliage) {
                        byte foliageTile = v.TypeId == GameData.BTallGrass.Id
                            ? (byte)TextureAtlas.TTallGrass
                            : (byte)(TextureAtlas.TWheatCrop0 + Math.Clamp((int)v.SubGridLayerMask, 0, 3));
                        
                        var (u0, v0, u1, v1) = TileUv(foliageTile);
                        var (sun, blockL) = GetFaceLight(neighbors, gc, lx, ly, lz, 0, 0, 0);
                        byte shadeSun = (byte)(255f * MathF.Max(sun, 0.4f));
                        byte shadeBlock = (byte)(255f * blockL);
                        float worldOffsetX = gc.Coord.X * Chunk.SizeX;
                        float worldOffsetY = gc.Coord.Y * Chunk.SizeY;
                        float worldOffsetZ = gc.Coord.Z * Chunk.SizeZ;

                        (float X0, float Z0, float X1, float Z1)[] crossPlanes = {
                            (0.12f, 0.12f, 0.88f, 0.88f),
                            (0.12f, 0.88f, 0.88f, 0.12f)
                        };

                        foreach (var plane in crossPlanes) {
                            if (verts.Count / 3 + 8 > MaxVertices) Flush();
                            int bv = verts.Count / 3;

                            float x0 = worldOffsetX + lx + plane.X0;
                            float z0 = worldOffsetZ + lz + plane.Z0;
                            float x1 = worldOffsetX + lx + plane.X1;
                            float z1 = worldOffsetZ + lz + plane.Z1;
                            float y0 = worldOffsetY + ly;
                            float y1 = worldOffsetY + ly + 0.95f;

                            // 4 вершины плоскости
                            verts.Add(x0); verts.Add(y0); verts.Add(z0);
                            norms.Add(0); norms.Add(1); norms.Add(0);
                            uvs.Add(u0); uvs.Add(v1);
                            cols.Add(shadeSun); cols.Add(shadeBlock); cols.Add(230); cols.Add(255);

                            verts.Add(x1); verts.Add(y0); verts.Add(z1);
                            norms.Add(0); norms.Add(1); norms.Add(0);
                            uvs.Add(u1); uvs.Add(v1);
                            cols.Add(shadeSun); cols.Add(shadeBlock); cols.Add(230); cols.Add(255);

                            verts.Add(x1); verts.Add(y1); verts.Add(z1);
                            norms.Add(0); norms.Add(1); norms.Add(0);
                            uvs.Add(u1); uvs.Add(v0);
                            cols.Add(shadeSun); cols.Add(shadeBlock); cols.Add(230); cols.Add(255);

                            verts.Add(x0); verts.Add(y1); verts.Add(z0);
                            norms.Add(0); norms.Add(1); norms.Add(0);
                            uvs.Add(u0); uvs.Add(v0);
                            cols.Add(shadeSun); cols.Add(shadeBlock); cols.Add(230); cols.Add(255);

                            // Лицевая сторона (Front face)
                            indices.Add((ushort)(bv + 0));
                            indices.Add((ushort)(bv + 1));
                            indices.Add((ushort)(bv + 2));
                            indices.Add((ushort)(bv + 0));
                            indices.Add((ushort)(bv + 2));
                            indices.Add((ushort)(bv + 3));

                            // Обратная сторона (Back face - двусторонняя видимость)
                            indices.Add((ushort)(bv + 2));
                            indices.Add((ushort)(bv + 1));
                            indices.Add((ushort)(bv + 0));
                            indices.Add((ushort)(bv + 3));
                            indices.Add((ushort)(bv + 2));
                            indices.Add((ushort)(bv + 0));
                        }
                        continue;
                    }

                    // ── Тонкие 3D-двери (панель толщиной 3/16 м с поворотом на 90° при открытии) ──
                    if (GameData.IsDoor(v.TypeId)) {
                        byte facing = (byte)(v.SubGridLayerMask & 3);
                        bool isOpen = (v.SubGridLayerMask & 8) != 0;
                        byte effFacing = isOpen ? (byte)((facing + 1) & 3) : facing;

                        float T = 0.1875f;
                        float x0 = 0f, x1 = 1f, z0 = 0f, z1 = 1f;
                        switch (effFacing) {
                            case 0: z0 = 1f - T; z1 = 1f; break;
                            case 1: x0 = 0f; x1 = T; break;
                            case 2: z0 = 0f; z1 = T; break;
                            default: x0 = 1f - T; x1 = 1f; break;
                        }

                        byte mainTile = (byte)(v.TypeId == GameData.BDoorUpper.Id ? TextureAtlas.TDoorUpper : TextureAtlas.TDoorLower);
                        byte edgeTile = (byte)TextureAtlas.TPlanks;

                        float worldOffsetX = gc.Coord.X * Chunk.SizeX;
                        float worldOffsetY = gc.Coord.Y * Chunk.SizeY;
                        float worldOffsetZ = gc.Coord.Z * Chunk.SizeZ;

                        for (int df = 0; df < 6; df++) {
                            if (verts.Count / 3 + 4 > MaxVertices) Flush();
                            int bv = verts.Count / 3;

                            byte tile = (df switch {
                                0 or 1 => (effFacing == 1 || effFacing == 3) ? mainTile : edgeTile,
                                4 or 5 => (effFacing == 0 || effFacing == 2) ? mainTile : edgeTile,
                                _ => edgeTile
                            });

                            var (dx, dy, dz, nx, ny, nz) = Faces[df];
                            var (sun, blockL) = GetFaceLight(neighbors, gc, lx, ly, lz, dx, dy, dz);
                            var (u0, v0, u1, v1) = TileUv(tile);

                            float faceDir = df switch {
                                2 => 1.0f,
                                3 => 0.55f,
                                0 or 1 => 0.82f,
                                _ => 0.68f
                            };
                            byte shadeSun = (byte)(255f * sun);
                            byte shadeBlock = (byte)(255f * blockL);
                            byte shadeDir = (byte)(255f * faceDir);

                            (float px, float py, float pz, float u, float v)[] faceVerts = df switch {
                                0 => new[] { (x1, 0f, z0, 1f, 1f), (x1, 1f, z0, 1f, 0f), (x1, 1f, z1, 0f, 0f), (x1, 0f, z1, 0f, 1f) },
                                1 => new[] { (x0, 0f, z1, 0f, 1f), (x0, 1f, z1, 0f, 0f), (x0, 1f, z0, 1f, 0f), (x0, 0f, z0, 1f, 1f) },
                                2 => new[] { (x0, 1f, z1, 0f, 1f), (x1, 1f, z1, 1f, 1f), (x1, 1f, z0, 1f, 0f), (x0, 1f, z0, 0f, 0f) },
                                3 => new[] { (x0, 0f, z0, 0f, 0f), (x1, 0f, z0, 1f, 0f), (x1, 0f, z1, 1f, 1f), (x0, 0f, z1, 0f, 1f) },
                                4 => new[] { (x0, 0f, z1, 0f, 1f), (x1, 0f, z1, 1f, 1f), (x1, 1f, z1, 1f, 0f), (x0, 1f, z1, 0f, 0f) },
                                _ => new[] { (x1, 0f, z0, 1f, 1f), (x0, 0f, z0, 0f, 1f), (x0, 1f, z0, 0f, 0f), (x1, 1f, z0, 1f, 0f) }
                            };

                            foreach (var (vx, vy, vz, vu, vv) in faceVerts) {
                                verts.Add(worldOffsetX + lx + vx);
                                verts.Add(worldOffsetY + ly + vy);
                                verts.Add(worldOffsetZ + lz + vz);
                                norms.Add(nx); norms.Add(ny); norms.Add(nz);
                                uvs.Add(u0 + (u1 - u0) * vu);
                                uvs.Add(v0 + (v1 - v0) * vv);
                                cols.Add(shadeSun); cols.Add(shadeBlock); cols.Add(shadeDir); cols.Add((byte)255);
                            }

                            indices.Add((ushort)(bv + 0));
                            indices.Add((ushort)(bv + 1));
                            indices.Add((ushort)(bv + 2));
                            indices.Add((ushort)(bv + 0));
                            indices.Add((ushort)(bv + 2));
                            indices.Add((ushort)(bv + 3));
                        }
                        continue;
                    }

                    // ── Полноценная геометрия жидкостей (Вода и Лава: ступени потока без щелей и просветов) ──
                    if (isFluid) {
                        ushort aboveType = GetTypeIdAtOffset(neighbors, gc, lx, ly, lz, 0, 1, 0);
                        float selfH = (aboveType == v.TypeId) ? 1.0f : GetFluidHeight(v.TypeId, v.SubGridLayerMask);
                        byte fluidTile = (byte)(isWater ? TextureAtlas.TWater : TextureAtlas.TLava);
                        var (u0, v0, u1, v1) = TileUv(fluidTile);
                        byte alpha = isWater ? (byte)210 : (byte)255;

                        float worldOffsetX = gc.Coord.X * Chunk.SizeX;
                        float worldOffsetY = gc.Coord.Y * Chunk.SizeY;
                        float worldOffsetZ = gc.Coord.Z * Chunk.SizeZ;

                        for (int f = 0; f < 6; f++) {
                            var (dx, dy, dz, nx, ny, nz) = Faces[f];
                            float yBottom = 0f, yTop = selfH;
                            bool renderFace = false;

                            if (f == 2) { // Верхняя грань (+Y)
                                ushort topNeighbor = GetTypeIdAtOffset(neighbors, gc, lx, ly, lz, 0, 1, 0);
                                if (topNeighbor != v.TypeId && !IsFaceOccluding(topNeighbor)) {
                                    renderFace = true;
                                    yBottom = selfH;
                                    yTop = selfH;
                                }
                            } else if (f == 3) { // Нижняя грань (-Y)
                                ushort bottomNeighbor = GetTypeIdAtOffset(neighbors, gc, lx, ly, lz, 0, -1, 0);
                                if (bottomNeighbor != v.TypeId && !IsFaceOccluding(bottomNeighbor)) {
                                    renderFace = true;
                                    yBottom = 0f;
                                    yTop = 0f;
                                }
                            } else { // Боковые грани (±X, ±Z)
                                var neighborVox = GetVoxelAtOffset(neighbors, gc, lx, ly, lz, dx, 0, dz);
                                ushort nType = neighborVox.TypeId;
                                if (nType == v.TypeId) {
                                    ushort nAbove = GetTypeIdAtOffset(neighbors, gc, lx, ly, lz, dx, 1, dz);
                                    float neighborH = (nAbove == v.TypeId) ? 1.0f : GetFluidHeight(nType, neighborVox.SubGridLayerMask);
                                    if (selfH > neighborH + 0.01f) {
                                        renderFace = true;
                                        yBottom = neighborH;
                                        yTop = selfH;
                                    }
                                } else if (!IsFaceOccluding(nType) && nType != v.TypeId) {
                                    renderFace = true;
                                    yBottom = 0f;
                                    yTop = selfH;
                                }
                            }

                            if (!renderFace) continue;

                            if (verts.Count / 3 + 4 > MaxVertices) Flush();
                            int baseVertex = verts.Count / 3;

                            var (sun, blockL) = GetFaceLight(neighbors, gc, lx, ly, lz, dx, dy, dz);
                            float faceDir = f switch {
                                2 => 1.0f,
                                3 => 0.55f,
                                0 or 1 => 0.82f,
                                _ => 0.68f
                            };
                            byte shadeDir = (byte)(255f * faceDir);
                            byte shadeSun = (byte)(255f * sun);
                            byte shadeBlock = (byte)(255f * blockL);

                            foreach (var (fx, fy, fz, fu, fv) in FaceVerts[f]) {
                                float actualFy = (f == 2) ? selfH : (f == 3) ? 0f : (fy > 0.5f ? yTop : yBottom);
                                float actualFu = fu;
                                float actualFv = (f == 2 || f == 3) ? fv : (1f - (fy > 0.5f ? yTop : yBottom));

                                verts.Add(worldOffsetX + lx + fx);
                                verts.Add(worldOffsetY + ly + actualFy);
                                verts.Add(worldOffsetZ + lz + fz);
                                norms.Add(nx); norms.Add(ny); norms.Add(nz);
                                uvs.Add(u0 + (u1 - u0) * actualFu);
                                uvs.Add(v0 + (v1 - v0) * actualFv);
                                cols.Add(shadeSun); cols.Add(shadeBlock); cols.Add(shadeDir); cols.Add(alpha);
                            }

                            indices.Add((ushort)(baseVertex + 0));
                            indices.Add((ushort)(baseVertex + 1));
                            indices.Add((ushort)(baseVertex + 2));
                            indices.Add((ushort)(baseVertex + 0));
                            indices.Add((ushort)(baseVertex + 2));
                            indices.Add((ushort)(baseVertex + 3));
                        }
                        continue;
                    }

                    // ── Портал в Нижний мир / в Энд: полупрозрачный мерцающий экран ──
                    if (v.TypeId == GameData.BNetherPortal.Id || v.TypeId == GameData.BEndPortal.Id) {
                        byte portalTile = (byte)(v.TypeId == GameData.BNetherPortal.Id ? TextureAtlas.TNetherPortal : TextureAtlas.TEndPortal);
                        var (u0, v0, u1, v1) = TileUv(portalTile);
                        float worldOffsetX = gc.Coord.X * Chunk.SizeX;
                        float worldOffsetY = gc.Coord.Y * Chunk.SizeY;
                        float worldOffsetZ = gc.Coord.Z * Chunk.SizeZ;
                        const byte alpha = 200;

                        for (int f = 0; f < 6; f++) {
                            var (dx, dy, dz, nx, ny, nz) = Faces[f];
                            ushort neighborType = GetTypeIdAtOffset(neighbors, gc, lx, ly, lz, dx, dy, dz);
                            // Не рисуем грани, примыкающие к этому же порталу, и грани, упёртые в непрозрачный блок
                            if (neighborType == v.TypeId) continue;
                            if (IsFaceOccluding(neighborType)) continue;

                            if (verts.Count / 3 + 4 > MaxVertices) Flush();
                            int baseVertex = verts.Count / 3;

                            var (sun, blockL) = GetFaceLight(neighbors, gc, lx, ly, lz, dx, dy, dz);
                            float faceDir = f switch {
                                2 => 1.0f,       // Верхняя грань (+Y)
                                3 => 0.55f,      // Нижняя грань (-Y)
                                0 or 1 => 0.82f, // Боковые грани (±X)
                                _ => 0.68f       // Боковые грани (±Z)
                            };
                            byte shadeDir = (byte)(255f * faceDir);

                            foreach (var (fx, fy, fz, fu, fv) in FaceVerts[f]) {
                                float actualFy = fy;
                                verts.Add(worldOffsetX + lx + fx);
                                verts.Add(worldOffsetY + ly + actualFy);
                                verts.Add(worldOffsetZ + lz + fz);
                                norms.Add(nx); norms.Add(ny); norms.Add(nz);
                                uvs.Add(u0 + (u1 - u0) * fu);
                                uvs.Add(v0 + (v1 - v0) * fv);
                                cols.Add((byte)(255f * sun)); cols.Add((byte)(255f * blockL)); cols.Add(shadeDir); cols.Add(alpha);
                            }

                            indices.Add((ushort)(baseVertex + 0));
                            indices.Add((ushort)(baseVertex + 1));
                            indices.Add((ushort)(baseVertex + 2));
                            indices.Add((ushort)(baseVertex + 0));
                            indices.Add((ushort)(baseVertex + 2));
                            indices.Add((ushort)(baseVertex + 3));
                        }
                        continue;
                    }

                    if (!block.IsSolid && !block.IsOpaque && !isFluid) continue;   // факелы рисуются как 3D-декор

                    var tiles = TextureAtlas.BlockTiles(v.TypeId);
                    for (int f = 0; f < 6; f++) {
                        var (dx, dy, dz, nx, ny, nz) = Faces[f];
                        ushort neighborType = GetTypeIdAtOffset(neighbors, gc, lx, ly, lz, dx, dy, dz);

                        bool visible;
                        if (v.TypeId == GameData.BGlass.Id) {
                            visible = neighborType != GameData.BGlass.Id && !IsFaceOccluding(neighborType);
                        } else if (v.TypeId == GameData.BLeaves.Id) {
                            // Листва не рисует внутренние соприкасающиеся грани с соседней листвой (буст FPS в 3-4 раза в лесу)
                            visible = neighborType != GameData.BLeaves.Id && !IsFaceOccluding(neighborType);
                        } else {
                            // Твёрдый блок показывает грань, если рядом воздух, стекло, листва или жидкость
                            // (вода/лава не заслоняют грань — иначе блок «исчезает» у лавы).
                            visible = !IsFaceOccluding(neighborType);
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
                            if ((v.TypeId == GameData.BBed.Id || v.TypeId == GameData.BBedHead.Id) && fy > 0.5f) {
                                actualFy = 0.56f;
                            }

                            float actualFu = fu, actualFv = fv;
                            if (f == 2 && (v.TypeId == GameData.BBed.Id || v.TypeId == GameData.BBedHead.Id)) {
                                byte facing = (byte)(v.SubGridLayerMask & 3);
                                (actualFu, actualFv) = facing switch {
                                    1 => (fv, 1f - fu),
                                    2 => (1f - fu, 1f - fv),
                                    3 => (1f - fv, fu),
                                    _ => (fu, fv)
                                };
                            }

                            verts.Add(worldOffsetX + lx + fx);
                            verts.Add(worldOffsetY + ly + actualFy);
                            verts.Add(worldOffsetZ + lz + fz);
                            norms.Add(nx); norms.Add(ny); norms.Add(nz);
                            uvs.Add(u0 + (u1 - u0) * actualFu);
                            uvs.Add(v0 + (v1 - v0) * actualFv);
                            cols.Add(shadeSun); cols.Add(shadeBlock); cols.Add(shadeDir); cols.Add((byte)255);
                        }
                        indices.Add((ushort)(baseVertex + 0));
                        indices.Add((ushort)(baseVertex + 1));
                        indices.Add((ushort)(baseVertex + 2));
                        indices.Add((ushort)(baseVertex + 0));
                        indices.Add((ushort)(baseVertex + 2));
                        indices.Add((ushort)(baseVertex + 3));
                    }                    }
                }
            }
            Flush();
        }

        return (resultOpaque, resultTranslucent);
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
        if (surf != int.MinValue && wy >= surf) {
            return (1f, 0f);
        }
        int selfIdx = Chunk.Index(lx, ly, lz);
        return (gc.SunLight[selfIdx] / 15f, gc.BlockLight[selfIdx] / 15f);
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

    public static float GetFluidHeight(ushort typeId, byte level) {
        if (typeId == GameData.BWater.Id) {
            if (level == 0 || level == FluidEngine.FallingLevel) return 0.88f;
            return Math.Clamp(0.88f - (level / 7.0f) * 0.65f, 0.20f, 0.88f);
        }
        if (typeId == GameData.BLava.Id) {
            if (level == 0 || level == FluidEngine.FallingLevel) return 0.88f;
            return Math.Clamp(0.88f - (level / 3.0f) * 0.65f, 0.23f, 0.88f);
        }
        return 1.0f;
    }

    private static VoxelData GetVoxelAtOffset(GameChunk?[,,] neighbors, GameChunk gc, int lx, int ly, int lz, int ox, int oy, int oz) {
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
            return wy < surf ? new VoxelData { TypeId = GameData.BStone.Id, Flags = VoxelFlags.Solid } : default;
        }
        return nChunk.Chunk.Get(nlx, nly, nlz);
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

    /// <summary>
    /// Заслоняет ли блок грань соседа. Жидкости (вода/лава) НЕ заслоняют:
    /// грань блока должна быть видна сквозь них, иначе блок «исчезает» у лавы.
    /// </summary>
    private static bool IsFaceOccluding(ushort typeId) {
        if (typeId == 0 || typeId == GameData.BWater.Id || typeId == GameData.BLava.Id) return false;
        return GameData.GetBlock(typeId).IsOpaque;
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
            byte effFacing = (byte)(facing & 3);
            return effFacing switch {
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



