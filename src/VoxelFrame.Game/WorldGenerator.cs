using System;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>Типы биомов в игре.</summary>
public enum BiomeType {
    Plains,    // Равнины (открытые луга, 0 деревьев)
    Forest,    // Лес (высокая плотность деревьев)
    Desert,    // Пустыня (пески и дюны, 0 деревьев)
    Beach,     // Пляж (песчаные берега)
    Ocean,     // Океан (глубокая вода)
    River,     // Река (водные русла)
    Mineshaft, // Заброшенные шахты (подземный биом с деревянными крепями и факелами)
}

/// <summary>
/// Генератор мира:
/// - Биомы: Лес, Равнины (без деревьев), Пустыня (без деревьев), Пляж, Океан, Река, Заброшенные Шахты.
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

    /// <summary>Определяет биом по координатам точки в мире (температура/влажность).</summary>
    public BiomeType GetBiome(int wx, int wy, int wz) {
        if (wy >= 15 && wy <= 22 && IsInMineshaft(wx, wy, wz)) {
            return BiomeType.Mineshaft;
        }

        int surface = SurfaceHeight(wx, wz);
        float r = MathF.Abs(_river.Get(wx * 0.0035f, wz * 0.0035f));
        if (r < 0.075f && surface <= SeaLevel + 2) {
            return BiomeType.River;
        }
        if (surface <= SeaLevel - 4) {
            return BiomeType.Ocean;
        }
        if (surface <= SeaLevel + 2) {
            return BiomeType.Beach;
        }

        // Климатическая модель: Температура и Влажность
        float temp = _beach.Get(wx * 0.0022f + 1200f, wz * 0.0022f + 1200f);
        float humid = _forestNoise.Get(wx * 0.0028f + 2500f, wz * 0.0028f + 2500f);

        if (temp > 0.70f && humid < 0.38f) {
            return BiomeType.Desert; // Жаркий и сухой биом пустыни (~15% суши)
        }
        if (humid > 0.50f) {
            return BiomeType.Forest; // Влажный лесистый биом (~35% суши)
        }
        return BiomeType.Plains; // Умеренные просторные равнины (~50% суши)
    }

    public static string GetBiomeName(BiomeType biome) => biome switch {
        BiomeType.Ocean => "Океан",
        BiomeType.Beach => "Пляж",
        BiomeType.River => "Река",
        BiomeType.Forest => "Лес",
        BiomeType.Desert => "Пустыня",
        BiomeType.Mineshaft => "Заброшенная шахта",
        _ => "Равнины"
    };

    /// <summary>Проверка нахождения в коридоре заброшенной шахты (редкие атмосферные структуры).</summary>
    public bool IsInMineshaft(int wx, int wy, int wz) {
        if (wy < 16 || wy > 21) return false;
        int sectorX = (int)MathF.Floor(wx / 128f);
        int sectorZ = (int)MathF.Floor(wz / 128f);
        float mNoise = _mineshaftNoise.Get(sectorX * 17.3f + 100f, sectorZ * 17.3f + 100f);
        if (mNoise < 0.78f) return false;

        int cellX = wx % 128; if (cellX < 0) cellX += 128;
        int cellZ = wz % 128; if (cellZ < 0) cellZ += 128;

        bool inBranchX = Math.Abs(cellZ - 64) <= 1 && (cellX >= 16 && cellX <= 112);
        bool inBranchZ = Math.Abs(cellX - 64) <= 1 && (cellZ >= 16 && cellZ <= 112);
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
                var biome = GetBiome(wx, BaseHeight, wz);
                bool isDesert = biome == BiomeType.Desert;
                bool isBeach = (surface <= SeaLevel + 2 && surface >= SeaLevel - 3) || biome == BiomeType.Beach;

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
                        } else if (isDesert || isBeach) {
                            type = beachNoise > 0.3f ? GameData.BSand.Id : (isDesert ? GameData.BSand.Id : GameData.BGravel.Id);
                        } else {
                            type = GameData.BGrass.Id;
                        }
                    } else if (wy >= surface - 3) {
                        if (isDesert || isBeach || surface < SeaLevel) {
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

        // Заброшенные шахты с крепями, рельсами, паутиной и сундуками
        CarveMineshafts(chunk, ox, oy, oz);

        // Подземные сокровищницы (Данжи)
        PlaceDungeons(chunk, ox, oy, oz);

        // Пустынные пирамиды и храмы
        PlaceDesertPyramids(chunk, ox, oy, oz);

        // 3D-жилы руд
        PlaceOreVeins(chunk, ox, oy, oz);

        // Деревья на поверхности
        PlaceTrees(chunk, ox, oz);

        // Растительность (2D трава)
        PlaceFoliage(chunk, ox, oz);

        // Редкие деревни с сундуками
        if (chunk.Origin.Y == 1 || chunk.Origin.Y == 2) // только наземные чанки
            PlaceVillages(chunk, ox, oz);
    }

    private void PlaceFoliage(Chunk chunk, int ox, int oz) {
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                int wx = ox + lx, wz = oz + lz;
                int surface = SurfaceHeight(wx, wz);
                if (surface <= SeaLevel) continue;

                int ly = surface + 1 - chunk.Origin.Y * Chunk.SizeY;
                int lyBelow = surface - chunk.Origin.Y * Chunk.SizeY;

                if (ly >= 0 && ly < Chunk.SizeY && lyBelow >= 0 && lyBelow < Chunk.SizeY) {
                    int idxBelow = Chunk.Index(lx, lyBelow, lz);
                    int idx = Chunk.Index(lx, ly, lz);

                    if (chunk.Get(idxBelow).TypeId == GameData.BGrass.Id && chunk.Get(idx).TypeId == 0) {
                        float fNoise = _treeNoise.Get(wx * 0.35f, wz * 0.35f);
                        var biome = GetBiome(wx, BaseHeight, wz);
                        bool isForest = biome == BiomeType.Forest;
                        bool isPlains = biome == BiomeType.Plains;
                        if (isForest || isPlains) {
                            // Оптимизированная естественная плотность 2D-травы (-60% от исходного, кластерами)
                            float chance = isPlains ? 0.22f : 0.12f;
                            if (fNoise < chance && ((wx * 19 + wz * 37) % 4 == 0)) {
                                var vx = MakeVoxel(GameData.BTallGrass.Id);
                                chunk.SetVoxel(idx, in vx);
                            }
                        }
                    }
                }
            }
        }
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

                // Вертикальные разломы / Каньоны (Ravines)
                float ravineAngle = wx * 0.018f + wz * 0.012f;
                float ravineDist = MathF.Abs(MathF.Sin(ravineAngle) * 35f - (wz * 0.035f));
                bool isRavine = ravineDist < 1.8f;

                for (int ly = 0; ly < Chunk.SizeY; ly++) {
                    int wy = oy + ly;
                    if (wy <= 3) continue; // коренная порода
                    if (wy > surface + 2) continue; // воздух

                    int idx = Chunk.Index(lx, ly, lz);
                    ushort cur = chunk.Get(idx).TypeId;
                    if (cur == 0 || cur == GameData.BBedrock.Id) continue;

                    // 1. Cheese Caves (Массивные глубокие залы и гроты)
                    float cheese = _caveCheese.Fractal(wx * 0.014f, wy * 0.020f + 100f, wz * 0.014f, 3, 0.5f);
                    bool isCheese = (wy < 36 ? cheese > 0.48f : cheese > 0.56f) && wy < surface - 3;

                    // 2. Spaghetti Caves (Извилистые разветвленные туннели)
                    float sp1 = _caveSpaghetti1.Get(wx * 0.024f + wy * 0.015f, wz * 0.024f);
                    float sp2 = _caveSpaghetti2.Get(wx * 0.024f, wz * 0.024f + wy * 0.015f + 500f);
                    bool isSpaghetti = (sp1 * sp1 + sp2 * sp2) < 0.024f && wy < surface - 2;

                    // 3. Noodle Caves (Узкие лабиринты и трещины)
                    float noodle = _caveNoodle.Get(wx * 0.015f + 2000f, wz * 0.015f + wy * 0.030f);
                    bool isNoodle = MathF.Abs(noodle) < 0.028f && wy > 6 && wy < surface - 2;

                    // 4. Каньон / Разлом (глубокий вертикальный разрез)
                    bool inRavine = isRavine && wy >= 11 && wy <= surface - 4;

                    // Выходы пещер и разломов на поверхность
                    bool surfaceBreach = wy >= surface - 3 && (isSpaghetti || isNoodle || inRavine) && cheese > 0.46f;

                    if (isCheese || isSpaghetti || isNoodle || inRavine || surfaceBreach) {
                        ushort replaceWith;
                        if (wy <= 10) {
                            replaceWith = GameData.BLava.Id; // Подземные озера лавы
                        } else if (wy == 11 && (wx + wz) % 3 == 0) {
                            replaceWith = GameData.BObsidian.Id; // Обсидиановые берега у лавы
                        } else if (wy <= SeaLevel && cur == GameData.BWater.Id) {
                            replaceWith = GameData.BWater.Id;
                        } else if (wy < SeaLevel && wy > surface - 6 && surface <= SeaLevel) {
                            replaceWith = GameData.BWater.Id; // Водоносный пласт у побережья
                        } else if (isCheese && wy >= 14 && wy <= 30 && ((wx * 11 + wz * 7 + wy * 3) % 19 == 0)) {
                            replaceWith = GameData.BMossyCobblestone.Id; // Замшелые гроты
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

    /// <summary>Генерация заброшенных шахт (Mineshafts) с деревянными крепями, факелами и сундуками.</summary>
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
                    int cellX = wx % 128; if (cellX < 0) cellX += 128;
                    int cellZ = wz % 128; if (cellZ < 0) cellZ += 128;

                    bool branchX = Math.Abs(cellZ - 64) <= 1;
                    bool branchZ = Math.Abs(cellX - 64) <= 1;

                    // Деревянные крепи каждые 4 блока
                    bool isArchStep = (branchX && (cellX % 4 == 0)) || (branchZ && (cellZ % 4 == 0));
                    bool isSide = (branchX && Math.Abs(cellZ - 64) == 1) || (branchZ && Math.Abs(cellX - 64) == 1);

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
                    } else if (wy == 18) {
                        // Редкие сундуки в шахтах
                        if ((cellX == 64 || cellZ == 64) && ((cellX * 13 + cellZ * 7) % 31 == 0)) {
                            blockId = GameData.BChest.Id;
                        }
                    }

                    // Факелы на арках шахты
                    if (blockId == 0 && wy == 19 && isArchStep && !isSide && (cellX + cellZ) % 8 == 0) {
                        blockId = GameData.BTorch.Id;
                    }

                    var v = MakeVoxel(blockId);
                    chunk.SetVoxel(idx, in v);
                }
            }
        }
    }

    /// <summary>
    /// 3D-жилы руд (Ore Clusters): генерация компактных сбалансированных скоплений в камне.
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

                    // Уголь (Coal) — компактные жилы (4-12 блоков) на высотах Y=5..75
                    float coalN = _oreNoise.Fractal(wx * 0.20f, wy * 0.20f, wz * 0.20f, 2, 0.5f);
                    if (coalN > 0.74f) {
                        var v = MakeVoxel(GameData.BCoalOre.Id);
                        chunk.SetVoxel(idx, in v);
                        continue;
                    }

                    // Железо (Iron) — жилы на высотах Y=5..54
                    if (wy <= 54) {
                        float ironN = _ironNoise.Fractal(wx * 0.22f, wy * 0.22f + 300f, wz * 0.22f, 2, 0.5f);
                        if (ironN > 0.76f) {
                            var v = MakeVoxel(GameData.BIronOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }

                    // Золото (Gold) — жилы на глубине Y=5..30
                    if (wy <= 30) {
                        float goldN = _goldNoise.Fractal(wx * 0.25f + 700f, wy * 0.25f, wz * 0.25f, 2, 0.5f);
                        if (goldN > 0.86f) {
                            var v = MakeVoxel(GameData.BGoldOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }

                    // Редстоун (Redstone) — жилы на глубине Y=1..16
                    if (wy <= 16) {
                        float redN = _oreNoise.Fractal(wx * 0.26f + 1200f, wy * 0.26f, wz * 0.26f, 2, 0.5f);
                        if (redN > 0.87f) {
                            var v = MakeVoxel(GameData.BRedstoneOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }

                    // Алмазы (Diamond) — редкие жилы на глубине Y=1..14
                    if (wy <= 14) {
                        float diaN = _diamondNoise.Fractal(wx * 0.28f + 5000f, wy * 0.28f, wz * 0.28f, 2, 0.5f);
                        if (diaN > 0.89f) {
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
                        if (biome != BiomeType.Forest) continue;

                        float threshold = 0.50f;
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

    /// <summary>
    /// Генерирует процедурные деревни с домиками и сундуками с лутом.
    /// Деревни появляются на равнинах и в лесах вне пустынь и водоёмов.
    /// </summary>
    private void PlaceVillages(Chunk chunk, int ox, int oz) {
        // Размер сектора деревни: 16×16 чанков = 512×512 блоков
        const int villageSectorChunks = 16;
        const int villageSectorBlocks = villageSectorChunks * Chunk.SizeX;

        int chunkX = ox / Chunk.SizeX;
        int chunkZ = oz / Chunk.SizeZ;

        // Определяем сектор деревни для этого чанка
        int sectorX = (int)MathF.Floor((float)chunkX / villageSectorChunks);
        int sectorZ = (int)MathF.Floor((float)chunkZ / villageSectorChunks);

        // Генерируем позицию деревни в секторе через шум
        var rng = new Random(_seed ^ (sectorX * 73856093) ^ (sectorZ * 19349663));
        float villageSeed = _mineshaftNoise.Get(sectorX * 31.73f + 123.456f, sectorZ * 31.73f + 654.321f);
        if (villageSeed < 0.50f) return; // ~50% секторов получают деревни

        // Центр деревни в мировых координатах
        int villageWX = sectorX * villageSectorBlocks + rng.Next(24, villageSectorBlocks - 24);
        int villageWZ = sectorZ * villageSectorBlocks + rng.Next(24, villageSectorBlocks - 24);

        // Проверяем, задевает ли этот чанк зону деревни (60 блоков радиус)
        const int villageRadius = 60;
        int chunkMinX = ox, chunkMaxX = ox + Chunk.SizeX - 1;
        int chunkMinZ = oz, chunkMaxZ = oz + Chunk.SizeZ - 1;
        if (villageWX + villageRadius < chunkMinX || villageWX - villageRadius > chunkMaxX) return;
        if (villageWZ + villageRadius < chunkMinZ || villageWZ - villageRadius > chunkMaxZ) return;

        // Биом центра деревни должен быть равнина или лес
        int centerSurface = SurfaceHeight(villageWX, villageWZ);
        if (centerSurface < SeaLevel + 3) return; // не в воде/пляже
        var centerBiome = GetBiome(villageWX, BaseHeight, villageWZ);
        if (centerBiome == BiomeType.Desert || centerBiome == BiomeType.Ocean ||
            centerBiome == BiomeType.Beach || centerBiome == BiomeType.River) return;

        // Генерируем 3-5 домиков вокруг центра
        int houseCount = rng.Next(3, 6);
        for (int h = 0; h < houseCount; h++) {
            float angle = (float)h / houseCount * MathF.Tau + (float)rng.NextDouble() * 0.4f;
            float dist = rng.Next(8, 26);
            int houseX = villageWX + (int)(MathF.Cos(angle) * dist);
            int houseZ = villageWZ + (int)(MathF.Sin(angle) * dist);
            PlaceVillageHouse(chunk, ox, oz, houseX, houseZ, h, rng);
        }

        // Дорога-гравий между домами (по центру)
        PlaceVillageRoad(chunk, ox, oz, villageWX, villageWZ, rng);
    }

    private void PlaceVillageHouse(Chunk chunk, int ox, int oz, int houseWX, int houseWZ, int houseIdx, Random rng) {
        int surface = SurfaceHeight(houseWX, houseWZ);
        if (surface < SeaLevel + 2) return;

        const int W = 7, D = 6, H = 4; // ширина, глубина, высота

        for (int dz = 0; dz < D; dz++) {
            for (int dx = 0; dx < W; dx++) {
                int wx = houseWX + dx - W / 2;
                int wz = houseWZ + dz - D / 2;
                int wy = surface;

                // Фундамент из булыжника и заполнение под домом
                for (int under = 0; under <= 3; under++) {
                    SetVillageBlock(chunk, ox, oz, wx, wy - under, wz, GameData.BCobblestone.Id);
                }

                // Пол внутри дома
                SetVillageBlock(chunk, ox, oz, wx, wy, wz, GameData.BPlanks.Id);

                // Стены из досок (только периметр)
                bool isEdge = dx == 0 || dx == W - 1 || dz == 0 || dz == D - 1;
                // Дверной проём: центр ближней стены (dz==0, dx==W/2)
                bool isDoor = dz == 0 && (dx == W / 2);

                for (int y = 1; y <= H - 1; y++) {
                    if (isEdge) {
                        if (!(isDoor && y <= 2)) {
                            SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, GameData.BPlanks.Id);
                        } else {
                            SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, 0); // дверной проём
                        }
                    } else {
                        // Очистка внутреннего пространства воздухом
                        SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, 0);
                    }
                }

                // Крыша из бревен (верхний ряд сплошной)
                SetVillageBlock(chunk, ox, oz, wx, wy + H, wz, GameData.BLog.Id);
            }
        }

        // Сундук внутри домика
        int chestX = houseWX + 1;
        int chestZ = houseWZ + 1;
        SetVillageBlock(chunk, ox, oz, chestX, surface + 1, chestZ, GameData.BChest.Id);

        // Факел внутри дома для уюта и света
        SetVillageBlock(chunk, ox, oz, houseWX - 1, surface + 2, houseWZ + 1, GameData.BTorch.Id);

        // Факел у входа снаружи
        SetVillageBlock(chunk, ox, oz, houseWX + 1, surface + 2, houseWZ - D / 2, GameData.BTorch.Id);
    }

    private void PlaceVillageRoad(Chunk chunk, int ox, int oz, int cx, int cz, Random rng) {
        // Простая крестообразная дорога из гравия 3 блока шириной
        for (int i = -24; i <= 24; i++) {
            for (int w = -1; w <= 1; w++) {
                // Горизонтальная ветка
                SetVillageRoadBlock(chunk, ox, oz, cx + i, cz + w);
                // Вертикальная ветка
                SetVillageRoadBlock(chunk, ox, oz, cx + w, cz + i);
            }
        }
    }

    private void SetVillageRoadBlock(Chunk chunk, int ox, int oz, int wx, int wz) {
        int surface = SurfaceHeight(wx, wz);
        if (surface < SeaLevel + 2) return;
        SetVillageBlock(chunk, ox, oz, wx, surface, wz, GameData.BGravel.Id);
    }

    private void SetVillageBlock(Chunk chunk, int ox, int oz, int wx, int wy, int wz, ushort blockId) {
        int lx = wx - ox, lz = wz - oz;
        if (lx < 0 || lx >= Chunk.SizeX || lz < 0 || lz >= Chunk.SizeZ) return;
        int ly = wy - chunk.Origin.Y * Chunk.SizeY;
        if (ly < 0 || ly >= Chunk.SizeY) return;
        var v = MakeVoxel(blockId);
        chunk.SetVoxel(Chunk.Index(lx, ly, lz), in v);
    }

    private static void SetLocal(Chunk chunk, int x, int y, int z, ushort type) {
        if (x < 0 || x >= Chunk.SizeX || y < 0 || y >= Chunk.SizeY || z < 0 || z >= Chunk.SizeZ) return;
        var vx = MakeVoxel(type); chunk.SetVoxel(Chunk.Index(x, y, z), in vx);
    }

    private void SetLocalIfAir(Chunk chunk, int x, int y, int z, ushort type) {
        if (x < 0 || x >= Chunk.SizeX || y < 0 || y >= Chunk.SizeY || z < 0 || z >= Chunk.SizeZ) return;
        int idx = Chunk.Index(x, y, z);
        if (chunk.Get(idx).TypeId != 0) return;
        var vx2 = MakeVoxel(type); chunk.SetVoxel(idx, in vx2);
    }

    public void SetWorldBlock(Chunk chunk, int ox, int oy, int oz, int wx, int wy, int wz, ushort blockId) {
        int lx = wx - ox, lz = wz - oz, ly = wy - oy;
        if (lx < 0 || lx >= Chunk.SizeX || lz < 0 || lz >= Chunk.SizeZ || ly < 0 || ly >= Chunk.SizeY) return;
        var v = MakeVoxel(blockId);
        chunk.SetVoxel(Chunk.Index(lx, ly, lz), in v);
    }

    private void PlaceDungeons(Chunk chunk, int ox, int oy, int oz) {
        if (oy > 38 || oy + Chunk.SizeY < 8) return;
        int sectorX = (int)MathF.Floor(ox / 48f);
        int sectorZ = (int)MathF.Floor(oz / 48f);
        int cx = sectorX * 48 + 24;
        int cz = sectorZ * 48 + 24;
        int cy = 12 + Math.Abs((sectorX * 37 + sectorZ * 19) % 20);

        if (Math.Abs(ox + Chunk.SizeX / 2 - cx) > 28 || Math.Abs(oz + Chunk.SizeZ / 2 - cz) > 28) return;

        // Комната 7x7x5
        for (int dx = -3; dx <= 3; dx++) {
            for (int dz = -3; dz <= 3; dz++) {
                for (int dy = 0; dy <= 4; dy++) {
                    int wx = cx + dx, wy = cy + dy, wz = cz + dz;
                    bool isWall = dx == -3 || dx == 3 || dz == -3 || dz == 3 || dy == 0 || dy == 4;
                    if (isWall) {
                        ushort wallBlock = ((wx * 7 + wz * 13 + wy * 3) % 3 == 0) ? GameData.BMossyCobblestone.Id : GameData.BCobblestone.Id;
                        SetWorldBlock(chunk, ox, oy, oz, wx, wy, wz, wallBlock);
                    } else {
                        ushort inside = 0;
                        if (dx == 0 && dz == 0 && dy == 1) {
                            inside = GameData.BMobSpawner.Id;
                        } else if ((dx == -2 && dz == 0 && dy == 1) || (dx == 2 && dz == 0 && dy == 1)) {
                            inside = GameData.BChest.Id;
                        }
                        SetWorldBlock(chunk, ox, oy, oz, wx, wy, wz, inside);
                    }
                }
            }
        }
    }

    private void PlaceDesertPyramids(Chunk chunk, int ox, int oy, int oz) {
        int sectorX = (int)MathF.Floor(ox / 96f);
        int sectorZ = (int)MathF.Floor(oz / 96f);
        int cx = sectorX * 96 + 48;
        int cz = sectorZ * 96 + 48;

        if (GetBiome(cx, 40, cz) != BiomeType.Desert) return;
        int surface = SurfaceHeight(cx, cz);
        if (surface <= SeaLevel + 2) return;

        if (Math.Abs(ox + Chunk.SizeX / 2 - cx) > 30 || Math.Abs(oz + Chunk.SizeZ / 2 - cz) > 30) return;

        // Ступенчатая пирамида (7 ярусов)
        for (int tier = 0; tier < 7; tier++) {
            int r = 8 - tier;
            int wy = surface + tier;
            for (int dx = -r; dx <= r; dx++) {
                for (int dz = -r; dz <= r; dz++) {
                    int wx = cx + dx, wz = cz + dz;
                    bool edge = Math.Abs(dx) == r || Math.Abs(dz) == r;
                    ushort b = edge ? GameData.BChiseledSandstone.Id : GameData.BSand.Id;
                    // Центральный проход
                    if (tier > 0 && Math.Abs(dx) <= 1 && Math.Abs(dz) <= 1) b = 0;
                    SetWorldBlock(chunk, ox, oy, oz, wx, wy, wz, b);
                }
            }
        }

        // Центральная вертикальная шахта вниз к секретной сокровищнице
        int vaultY = surface - 10;
        for (int y = surface; y >= vaultY; y--) {
            for (int dx = -1; dx <= 1; dx++) {
                for (int dz = -1; dz <= 1; dz++) {
                    SetWorldBlock(chunk, ox, oy, oz, cx + dx, y, cz + dz, 0);
                }
            }
        }

        // Сокровищница 7x7x4
        for (int dx = -3; dx <= 3; dx++) {
            for (int dz = -3; dz <= 3; dz++) {
                for (int dy = 0; dy <= 4; dy++) {
                    int wx = cx + dx, wy = vaultY + dy, wz = cz + dz;
                    bool wall = dx == -3 || dx == 3 || dz == -3 || dz == 3 || dy == 0 || dy == 4;
                    if (wall) {
                        SetWorldBlock(chunk, ox, oy, oz, wx, wy, wz, GameData.BChiseledSandstone.Id);
                    } else {
                        SetWorldBlock(chunk, ox, oy, oz, wx, wy, wz, 0);
                    }
                }
            }
        }

        // Подземная ловушка: 3x3 TNT под полом и нажимная плита в центре
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                SetWorldBlock(chunk, ox, oy, oz, cx + dx, vaultY - 1, cz + dz, GameData.BTNT.Id);
            }
        }
        SetWorldBlock(chunk, ox, oy, oz, cx, vaultY, cz, GameData.BPressurePlate.Id);

        // 4 сундука вокруг нажимной плиты
        SetWorldBlock(chunk, ox, oy, oz, cx + 2, vaultY, cz, GameData.BChest.Id);
        SetWorldBlock(chunk, ox, oy, oz, cx - 2, vaultY, cz, GameData.BChest.Id);
        SetWorldBlock(chunk, ox, oy, oz, cx, vaultY, cz + 2, GameData.BChest.Id);
        SetWorldBlock(chunk, ox, oy, oz, cx, vaultY, cz - 2, GameData.BChest.Id);
    }

    public void GenerateNetherChunk(Chunk chunk, int ox, int oy, int oz) {
        var rng = new Random(Seed ^ (ox * 73856093 ^ oy * 19349663 ^ oz * 83492791));
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                int wx = ox + lx, wz = oz + lz;
                for (int ly = 0; ly < Chunk.SizeY; ly++) {
                    int wy = oy + ly;
                    int idx = Chunk.Index(lx, ly, lz);

                    if (wy <= 2 || wy >= 124) {
                        chunk.SetVoxel(idx, MakeVoxel(GameData.BBedrock.Id));
                        continue;
                    }

                    // 3D пещеры Нижнего мира
                    float caveNoise = _caveCheese.Fractal(wx * 0.035f, wy * 0.035f, wz * 0.035f, 3, 0.5f);
                    bool isSolid = caveNoise > 0.45f;

                    ushort blockId = 0;
                    if (isSolid) {
                        blockId = GameData.BNetherrack.Id;
                        // Кварцевые жилы
                        if (rng.NextDouble() < 0.025) blockId = GameData.BNetherQuartzOre.Id;
                    } else {
                        // Лавовый океан на дне Ада (Y <= 31)
                        if (wy <= 31) {
                            blockId = GameData.BLava.Id;
                        }
                    }

                    if (blockId == GameData.BNetherrack.Id && wy <= 33) {
                        if (rng.NextDouble() < 0.35) blockId = GameData.BSoulSand.Id;
                        else if (rng.NextDouble() < 0.20) blockId = GameData.BGravel.Id;
                    }

                    // Светокамень (Glowstone) сталактиты на потолках
                    if (blockId == 0 && wy >= 50 && wy <= 110) {
                        float glowNoise = _oreNoise.Fractal(wx * 0.08f, wy * 0.08f, wz * 0.08f, 2, 0.5f);
                        if (glowNoise > 0.72f) blockId = GameData.BGlowstone.Id;
                    }

                    // Руины адских крепостей (Nether Fortress)
                    int fortSectorX = (int)MathF.Floor(wx / 64f);
                    int fortSectorZ = (int)MathF.Floor(wz / 64f);
                    int fortX = fortSectorX * 64 + 32;
                    int fortZ = fortSectorZ * 64 + 32;
                    if (Math.Abs(wx - fortX) <= 2 && wy >= 45 && wy <= 50) {
                        bool bridge = (Math.Abs(wx - fortX) == 2 && wy == 48) || wy == 45;
                        if (bridge) blockId = GameData.BNetherBrick.Id;
                        else if (wy == 46 && (wz % 16 == 0)) blockId = GameData.BMobSpawner.Id;
                        else blockId = 0;
                    }

                    chunk.SetVoxel(idx, MakeVoxel(blockId));
                }
            }
        }
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
