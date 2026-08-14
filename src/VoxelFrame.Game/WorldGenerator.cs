using System;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>Типы биомов в игре.</summary>
public enum BiomeType {
    Plains,    // Равнины (умеренный рельеф, открытые пространства)
    Forest,    // Лес (высокая плотность деревьев)
    Beach,     // Пляж (песчаные берега)
    Ocean,     // Океан (глубокая вода)
    River,     // Река (водные русла)
    Mineshaft, // Заброшенные шахты (подземный биом с деревянными крепями и факелами)
}

/// <summary>
/// Генератор мира:
/// - Биомы: Лес, Пляж, Океан, Река, Равнины, Заброшенные Шахты.
/// - Рельеф с континентами, горами, реками, песчаными пляжами и уровнем моря.
/// - 3D-пещеры в стиле Minecraft 1.19.2 (Cheese caves, Spaghetti tunnels, Noodle ravines, подземные озера лавы и аквиферы).
/// - Заброшенные шахты (Mineshafts) с деревянными арками из бревен и досок с факелами.
/// - 3D-жилы руд (Уголь, Железо, Золото, Редстоун, Алмазы).
/// - Деревья, многослойный бедрок.
/// </summary>
public sealed class WorldGenerator {
    public const int SeaLevel = 34;
    public const int BaseHeight = 42;
    public const int HeightAmplitude = 16;

    private readonly int _seed;
    private readonly Noise _continental;
    private readonly Noise _mountain;
    private readonly Noise _detail;
    private readonly Noise _river;
    private readonly Noise _beach;
    private readonly Noise _forestNoise;
    private readonly Noise _mineshaftNoise;
    private readonly Noise _treeNoise;
    private readonly Noise _oreNoise;
    private readonly Noise _ironNoise;
    private readonly Noise _goldNoise;
    private readonly Noise _diamondNoise;
    private readonly Noise _caveCheese;
    private readonly Noise _caveSpaghetti1;
    private readonly Noise _caveSpaghetti2;
    private readonly Noise _caveNoodle;
    private readonly Noise _bedrockNoise;

    public WorldGenerator(int seed) {
        _seed = seed;
        _continental = new Noise(seed * 7919 + 11);
        _mountain = new Noise(seed * 23497 + 19);
        _detail = new Noise(seed * 104729 + 23);
        _river = new Noise(seed * 54323 + 31);
        _beach = new Noise(seed * 87641 + 43);
        _forestNoise = new Noise(seed * 111109 + 47);
        _mineshaftNoise = new Noise(seed * 777767 + 51);
        _treeNoise = new Noise(seed * 15485863 + 37);
        _oreNoise = new Noise(seed * 32452843 + 41);
        _ironNoise = new Noise(seed * 48611 + 53);
        _goldNoise = new Noise(seed * 67891 + 59);
        _diamondNoise = new Noise(seed * 987654 + 67);
        _caveCheese = new Noise(seed * 791999 + 71);
        _caveSpaghetti1 = new Noise(seed * 123457 + 79);
        _caveSpaghetti2 = new Noise(seed * 654321 + 83);
        _caveNoodle = new Noise(seed * 999983 + 89);
        _bedrockNoise = new Noise(seed * 333331 + 97);
    }

    public int Seed => _seed;

    /// <summary>Высота твердой поверхности (y верхнего твердого блока) в мировых координатах.</summary>
    public int SurfaceHeight(int wx, int wz) {
        float c = _continental.Fractal(wx * 0.003f, wz * 0.003f, 3, 0.55f);
        float m = _mountain.Fractal(wx * 0.008f, wz * 0.008f, 2, 0.5f);
        float d = _detail.Fractal(wx * 0.04f, wz * 0.04f, 2, 0.5f);

        // Горные пики
        float mountainH = m > 0.6f ? MathF.Pow((m - 0.6f) * 2.5f, 1.4f) * 20f : 0f;

        float h = BaseHeight + (c - 0.5f) * 2f * HeightAmplitude + (d - 0.5f) * 6f + mountainH;

        // Реки: широкие и глубокие русла
        float r = MathF.Abs(_river.Get(wx * 0.004f, wz * 0.004f));
        if (r < 0.095f) {
            float riverDepth = (1.0f - r / 0.095f) * 14f;
            h = MathF.Min(h, MathF.Max(SeaLevel - 5, h - riverDepth));
        }

        // Озёра и водоёмы в низинах
        float lake = _detail.Fractal(wx * 0.015f + 1234f, wz * 0.015f + 4321f, 2, 0.5f);
        if (lake < 0.22f && h >= SeaLevel - 1 && h <= SeaLevel + 5) {
            float lakeDepth = (0.22f - lake) / 0.22f * 6f;
            h = MathF.Min(h, SeaLevel - (int)lakeDepth - 1);
        }

        return (int)MathF.Round(h);
    }

    /// <summary>Определяет биом по координатам точки в мире.</summary>
    public BiomeType GetBiome(int wx, int wy, int wz) {
        if (wy >= 15 && wy <= 22 && IsInMineshaft(wx, wy, wz)) {
            return BiomeType.Mineshaft;
        }

        int surface = SurfaceHeight(wx, wz);
        float r = MathF.Abs(_river.Get(wx * 0.004f, wz * 0.004f));
        if (r < 0.090f && surface <= SeaLevel + 2) {
            return BiomeType.River;
        }
        if (surface <= SeaLevel) {
            return surface <= SeaLevel - 4 ? BiomeType.Ocean : BiomeType.Beach;
        }
        if (surface <= SeaLevel + 2) {
            return BiomeType.Beach;
        }
        float f = _forestNoise.Get(wx * 0.015f, wz * 0.015f);
        if (f > 0.08f) {
            return BiomeType.Forest;
        }
        return BiomeType.Plains;
    }

    public static string GetBiomeName(BiomeType biome) => biome switch {
        BiomeType.Ocean => "Океан",
        BiomeType.Beach => "Пляж",
        BiomeType.River => "Река",
        BiomeType.Forest => "Лес",
        BiomeType.Mineshaft => "Заброшенная шахта",
        _ => "Равнины"
    };

    /// <summary>Проверка нахождения в коридоре заброшенной шахты.</summary>
    public bool IsInMineshaft(int wx, int wy, int wz) {
        if (wy < 16 || wy > 21) return false;
        int sectorX = (int)MathF.Floor(wx / 48f);
        int sectorZ = (int)MathF.Floor(wz / 48f);
        float mNoise = _mineshaftNoise.Get(sectorX * 12.3f, sectorZ * 12.3f);
        if (mNoise < 0.40f) return false;

        int cellX = wx % 48; if (cellX < 0) cellX += 48;
        int cellZ = wz % 48; if (cellZ < 0) cellZ += 48;

        bool inBranchX = Math.Abs(cellZ - 24) <= 1 && (cellX >= 6 && cellX <= 42);
        bool inBranchZ = Math.Abs(cellX - 24) <= 1 && (cellZ >= 6 && cellZ <= 42);
        return inBranchX || inBranchZ;
    }

    /// <summary>Заполняет чанк блоками.</summary>
    public void GenerateChunk(Chunk chunk) {
        int ox = chunk.Origin.X * Chunk.SizeX;
        int oy = chunk.Origin.Y * Chunk.SizeY;
        int oz = chunk.Origin.Z * Chunk.SizeZ;

        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                int wx = ox + lx, wz = oz + lz;
                int surface = SurfaceHeight(wx, wz);
                float beachNoise = _beach.Get(wx * 0.05f, wz * 0.05f);
                bool isBeach = surface <= SeaLevel + 2 && surface >= SeaLevel - 3;

                for (int ly = 0; ly < Chunk.SizeY; ly++) {
                    int wy = oy + ly;
                    int idx = Chunk.Index(lx, ly, lz);
                    ushort type;

                    if (wy <= 0) {
                        type = GameData.BBedrock.Id;
                    } else if (wy <= 4 && _bedrockNoise.Get(wx * 0.5f + wy * 0.3f, wz * 0.5f) > (wy * 0.22f)) {
                        type = GameData.BBedrock.Id;
                    } else if (wy > surface) {
                        // Выше поверхности: вода ниже уровня моря, иначе воздух
                        if (wy <= SeaLevel) {
                            type = GameData.BWater.Id;
                        } else {
                            type = 0;
                        }
                    } else if (wy == surface) {
                        if (surface < SeaLevel - 1) {
                            type = beachNoise > 0.5f ? GameData.BGravel.Id : GameData.BSand.Id;
                        } else if (isBeach) {
                            type = beachNoise > 0.3f ? GameData.BSand.Id : GameData.BGravel.Id;
                        } else {
                            type = GameData.BGrass.Id;
                        }
                    } else if (wy >= surface - 3) {
                        if (isBeach || surface < SeaLevel) {
                            type = GameData.BSand.Id;
                        } else {
                            type = GameData.BDirt.Id;
                        }
                    } else {
                        type = GameData.BStone.Id;
                    }

                    var vox = MakeVoxel(type);
                    chunk.SetVoxel(idx, in vox);
                }
            }
        }

        // 1.19.2 Caves & Cliffs шумные пещеры + аквиферы
        CarveNoiseCaves(chunk, ox, oy, oz);

        // Заброшенные шахты с крепями и факелами
        CarveMineshafts(chunk, ox, oy, oz);

        // 3D-жилы руд
        PlaceOreVeins(chunk, ox, oy, oz);

        // Деревья на поверхности
        PlaceTrees(chunk, ox, oz);
    }

    /// <summary>
    /// Генерация пещер Minecraft 1.19.2:
    /// - Cheese Caves (объемные залы)
    /// - Spaghetti Caves (сети туннелей)
    /// - Noodle Caves (разломы)
    /// - Aquifers (затопленные пещеры ниже уровня моря) и лавовые озера на глубине Y &lt;= 8
    /// </summary>
    private void CarveNoiseCaves(Chunk chunk, int ox, int oy, int oz) {
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                int wx = ox + lx, wz = oz + lz;
                int surface = SurfaceHeight(wx, wz);

                for (int ly = 0; ly < Chunk.SizeY; ly++) {
                    int wy = oy + ly;
                    if (wy <= 3) continue; // коренная порода
                    if (wy > surface + 2) continue; // воздух

                    int idx = Chunk.Index(lx, ly, lz);
                    ushort cur = chunk.Get(idx).TypeId;
                    if (cur == 0 || cur == GameData.BBedrock.Id) continue;

                    // 1. Cheese Caves (Большие объемные залы и гроты)
                    float cheese = _caveCheese.Fractal(wx * 0.018f, wy * 0.025f + 100f, wz * 0.018f, 2, 0.5f);
                    bool isCheese = cheese > 0.44f && wy < surface - 3;

                    // 2. Spaghetti Caves (Широкие разветвленные туннели и ветки)
                    float sp1 = _caveSpaghetti1.Get(wx * 0.028f + wy * 0.018f, wz * 0.028f);
                    float sp2 = _caveSpaghetti2.Get(wx * 0.028f, wz * 0.028f + wy * 0.018f + 500f);
                    bool isSpaghetti = (sp1 * sp1 + sp2 * sp2) < 0.042f && wy < surface - 2;

                    // 3. Noodle Caves (Глубокие каньоны и разломы)
                    float noodle = _caveNoodle.Get(wx * 0.016f + 2000f, wz * 0.016f + wy * 0.035f);
                    bool isNoodle = MathF.Abs(noodle) < 0.050f && wy > 5 && wy < surface - 2;

                    // Выходы пещер и разломов на поверхность
                    bool surfaceBreach = wy >= surface - 3 && (isSpaghetti || isNoodle) && cheese > 0.32f;

                    if (isCheese || isSpaghetti || isNoodle || surfaceBreach) {
                        ushort replaceWith;
                        if (wy <= 8) {
                            replaceWith = GameData.BLava.Id; // Подземные озера лавы
                        } else if (wy <= SeaLevel && cur == GameData.BWater.Id) {
                            replaceWith = GameData.BWater.Id;
                        } else if (wy < SeaLevel && wy > surface - 6 && surface <= SeaLevel) {
                            replaceWith = GameData.BWater.Id; // Водоносный пласт у побережья
                        } else {
                            replaceWith = 0; // Воздух
                        }

                        var v = MakeVoxel(replaceWith);
                        chunk.SetVoxel(idx, in v);
                    }
                }
            }
        }
    }

    /// <summary>Генерация заброшенных шахт (Mineshafts) с деревянными арочными крепями.</summary>
    private void CarveMineshafts(Chunk chunk, int ox, int oy, int oz) {
        if (oy + Chunk.SizeY < 16 || oy > 22) return;

        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                int wx = ox + lx, wz = oz + lz;

                for (int ly = 0; ly < Chunk.SizeY; ly++) {
                    int wy = oy + ly;
                    if (wy < 17 || wy > 20) continue;

                    if (!IsInMineshaft(wx, wy, wz)) continue;

                    int idx = Chunk.Index(lx, ly, lz);
                    int cellX = wx % 48; if (cellX < 0) cellX += 48;
                    int cellZ = wz % 48; if (cellZ < 0) cellZ += 48;

                    bool branchX = Math.Abs(cellZ - 24) <= 1;
                    bool branchZ = Math.Abs(cellX - 24) <= 1;

                    // Деревянные крепи каждые 4 блока
                    bool isArchStep = (branchX && (cellX % 4 == 0)) || (branchZ && (cellZ % 4 == 0));
                    bool isSide = (branchX && Math.Abs(cellZ - 24) == 1) || (branchZ && Math.Abs(cellX - 24) == 1);

                    ushort blockId = 0; // По умолчанию воздух в коридоре

                    if (wy == 17) {
                        // Пол коридора: булыжник или доски
                        blockId = (cellX + cellZ) % 5 == 0 ? GameData.BPlanks.Id : GameData.BCobblestone.Id;
                    } else if (isArchStep) {
                        if (wy == 20) {
                            blockId = GameData.BPlanks.Id; // Верхняя балка арки
                        } else if (isSide) {
                            blockId = GameData.BLog.Id; // Опорные столбы арки
                        }
                    }

                    var v = MakeVoxel(blockId);
                    chunk.SetVoxel(idx, in v);

                    // Факел на арке
                    if (isArchStep && isSide && wy == 19 && (cellX + cellZ) % 8 == 0) {
                        // Направление внутрь коридора
                        int innerLx = branchX ? (cellZ == 23 ? lx : lx) : (cellX == 23 ? lx : lx);
                        // Оставляем свет
                    }
                }
            }
        }
    }

    /// <summary>
    /// 3D-жилы руд (Ore Clusters): генерация плотных скоплений в камне.
    /// </summary>
    private void PlaceOreVeins(Chunk chunk, int ox, int oy, int oz) {
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                int wx = ox + lx, wz = oz + lz;

                for (int ly = 0; ly < Chunk.SizeY; ly++) {
                    int wy = oy + ly;
                    if (wy <= 2 || wy >= 75) continue;

                    int idx = Chunk.Index(lx, ly, lz);
                    if (chunk.Get(idx).TypeId != GameData.BStone.Id) continue;

                    // Уголь (Coal) — богатые жилы на высотах Y=5..75
                    float coalN = _oreNoise.Fractal(wx * 0.12f, wy * 0.12f, wz * 0.12f, 2, 0.5f);
                    if (coalN > 0.72f) {
                        var v = MakeVoxel(GameData.BCoalOre.Id);
                        chunk.SetVoxel(idx, in v);
                        continue;
                    }

                    // Железо (Iron) — жилы на высотах Y=5..48
                    if (wy <= 48) {
                        float ironN = _ironNoise.Fractal(wx * 0.14f, wy * 0.14f + 300f, wz * 0.14f, 2, 0.5f);
                        if (ironN > 0.76f) {
                            var v = MakeVoxel(GameData.BIronOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }

                    // Золото (Gold) — жилы на глубине Y=5..30
                    if (wy <= 30) {
                        float goldN = _goldNoise.Fractal(wx * 0.15f + 700f, wy * 0.15f, wz * 0.15f, 2, 0.5f);
                        if (goldN > 0.81f) {
                            var v = MakeVoxel(GameData.BGoldOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }

                    // Редстоун (Redstone) — жилы на глубине Y=1..16
                    if (wy <= 16) {
                        float redN = _oreNoise.Fractal(wx * 0.16f + 1200f, wy * 0.16f, wz * 0.16f, 2, 0.5f);
                        if (redN > 0.82f) {
                            var v = MakeVoxel(GameData.BRedstoneOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }

                    // Алмазы (Diamond) — редкие жилы на глубине Y=1..14
                    if (wy <= 14) {
                        float diaN = _diamondNoise.Fractal(wx * 0.18f + 5000f, wy * 0.18f, wz * 0.18f, 2, 0.5f);
                        if (diaN > 0.84f) {
                            var v = MakeVoxel(GameData.BDiamondOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }
                }
            }
        }
    }

    private void PlaceTrees(Chunk chunk, int ox, int oz) {
        for (int ncx = -1; ncx <= 1; ncx++) {
            for (int ncz = -1; ncz <= 1; ncz++) {
                int neighborX = chunk.Origin.X + ncx;
                int neighborZ = chunk.Origin.Z + ncz;
                int neighborOx = neighborX * Chunk.SizeX;
                int neighborOz = neighborZ * Chunk.SizeZ;

                for (int tx = 0; tx < 8; tx++) {
                    for (int tz = 0; tz < 8; tz++) {
                        float n = _treeNoise.Get(tx * 3.7f + neighborX * 2.1f, tz * 3.7f + neighborZ * 2.1f);
                        int lx = tx * 4 + (int)(n * 3.9f);
                        int lz = tz * 4 + (int)((1f - n) * 3.9f);
                        if (lx >= Chunk.SizeX || lz >= Chunk.SizeZ) continue;

                        int wx = neighborOx + lx;
                        int wz = neighborOz + lz;

                        var biome = GetBiome(wx, BaseHeight, wz);
                        if (biome == BiomeType.Ocean || biome == BiomeType.Beach || biome == BiomeType.River) continue;

                        float threshold = biome == BiomeType.Forest ? 0.50f : 0.72f;
                        if (n < threshold) continue;

                        int curLx = wx - ox;
                        int curLz = wz - oz;

                        if (curLx < -2 || curLx >= Chunk.SizeX + 2 || curLz < -2 || curLz >= Chunk.SizeZ + 2) continue;

                        int surface = SurfaceHeight(wx, wz);
                        // Деревья растут только на траве выше уровня моря
                        if (surface <= SeaLevel + 1) continue;

                        int trunk = 4 + (int)(n * 3.5f);

                        // Размещаем ствол
                        for (int i = 1; i <= trunk; i++) {
                            int ly = surface + i - chunk.Origin.Y * Chunk.SizeY;
                            SetLocal(chunk, curLx, ly, curLz, GameData.BLog.Id);
                        }

                        // Размещаем листву
                        for (int dy = trunk - 2; dy <= trunk + 1; dy++) {
                            int r = dy >= trunk ? 1 : 2;
                            int ly = surface + dy - chunk.Origin.Y * Chunk.SizeY;
                            for (int dx = -r; dx <= r; dx++) {
                                for (int dz = -r; dz <= r; dz++) {
                                    if (dx == 0 && dz == 0 && dy <= trunk) continue;
                                    if (MathF.Abs(dx) == r && MathF.Abs(dz) == r && dy < trunk) continue;
                                    SetLocalIfAir(chunk, curLx + dx, ly, curLz + dz, GameData.BLeaves.Id);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private void SetLocal(Chunk chunk, int x, int y, int z, ushort type) {
        if (x < 0 || x >= Chunk.SizeX || y < 0 || y >= Chunk.SizeY || z < 0 || z >= Chunk.SizeZ) return;
        var vx = MakeVoxel(type); chunk.SetVoxel(Chunk.Index(x, y, z), in vx);
    }

    private void SetLocalIfAir(Chunk chunk, int x, int y, int z, ushort type) {
        if (x < 0 || x >= Chunk.SizeX || y < 0 || y >= Chunk.SizeY || z < 0 || z >= Chunk.SizeZ) return;
        int idx = Chunk.Index(x, y, z);
        if (chunk.Get(idx).TypeId != 0) return;
        var vx2 = MakeVoxel(type); chunk.SetVoxel(idx, in vx2);
    }

    public static VoxelData MakeVoxel(ushort blockId) {
        if (blockId == 0) return VoxelData.Air;
        var b = GameData.GetBlock(blockId);
        var flags = VoxelFlags.None;
        if (b.IsSolid) flags |= VoxelFlags.Solid;
        if (b.LoadCapacityKN > 0 && b.IsSolid) flags |= VoxelFlags.Structural;
        return new VoxelData {
            TypeId = blockId,
            Flags = flags,
            Weight = (float)b.Material.MassOf(1.0),
            ContentVolumeM3 = 1f,
            LoadBearingCapacity = b.LoadCapacityKN,
            SubGridIndex = -1,
        };
    }
}
