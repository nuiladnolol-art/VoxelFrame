using System;
using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>Типы биомов в игре.</summary>
public enum BiomeType {
    Plains,    // Равнины (открытые луга, редкие деревья)
    Forest,    // Лес (высокая плотность деревьев)
    Desert,    // Пустыня (пески и дюны, 0 деревьев)
    Beach,     // Пляж (песчаные берега)
    Ocean,     // Океан (глубокая вода)
    River,     // Река (водные русла)
    Mineshaft, // Заброшенные шахты (подземный биом с деревянными крепями и факелами)
    Savanna,   // Саванна (тёплая сухая, жёлтая трава, редкие раскидистые деревья)
    Swamp,     // Болото (влажное, тёмная вода, деревья у воды, ночью слаймы)
}

/// <summary>
/// Генератор мира:
/// - Биомы: Лес, Равнины (без деревьев), Пустыня (без деревьев), Пляж, Океан, Река, Заброшенные Шахты.
/// - Рельеф с континентами, горами, реками, песчаными пляжами и уровнем моря.
/// - 3D-пещеры (Cheese caves, Spaghetti tunnels, Noodle ravines, подземные озера лавы и аквиферы).
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
    private readonly Noise _coalNoise;
    private readonly Noise _ironNoise;
    private readonly Noise _goldNoise;
    private readonly Noise _diamondNoise;
    private readonly Noise _caveCheese;
    private readonly Noise _caveSpaghetti1;
    private readonly Noise _caveSpaghetti2;
    private readonly Noise _caveNoodle;
    private readonly Noise _bedrockNoise;
    private readonly Noise _lavaPoolNoise;

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
        _coalNoise = new Noise(seed * 32452843 + 41);
        _ironNoise = new Noise(seed * 48611 + 53);
        _goldNoise = new Noise(seed * 67891 + 59);
        _diamondNoise = new Noise(seed * 987654 + 67);
        _caveCheese = new Noise(seed * 791999 + 71);
        _caveSpaghetti1 = new Noise(seed * 123457 + 79);
        _caveSpaghetti2 = new Noise(seed * 654321 + 83);
        _caveNoodle = new Noise(seed * 999983 + 89);
        _bedrockNoise = new Noise(seed * 333331 + 97);
        _lavaPoolNoise = new Noise(seed * 65537 + 97);
    }

    public int Seed => _seed;

    /// <summary>Высота твердой поверхности (y верхнего твердого блока) в мировых координатах.</summary>
    public int SurfaceHeight(int wx, int wz) {
        float c = _continental.Fractal(wx * 0.003f, wz * 0.003f, 3, 0.55f);
        float m = _mountain.Fractal(wx * 0.008f, wz * 0.008f, 2, 0.5f);
        float d = _detail.Fractal(wx * 0.04f, wz * 0.04f, 2, 0.5f);

        // Горные пики: порог снижен (0.52), амплитуда до ~38 — настоящие высокие массивы
        float mountainH = m > 0.52f ? MathF.Pow((m - 0.52f) * 2.08f, 1.4f) * 38f : 0f;

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
        float r = MathF.Abs(_river.Get(wx * 0.004f, wz * 0.004f));
        if (r < 0.095f && surface <= SeaLevel + 2) {
            return BiomeType.River;
        }
        if (surface <= SeaLevel - 4) {
            return BiomeType.Ocean;
        }
        if (surface >= SeaLevel - 1 && surface <= SeaLevel + 2) {
            return BiomeType.Beach;
        }

        // Климатическая модель: Температура и Влажность (гармоничный масштаб биомов ~500-800м).
        // Noise.Get возвращает [0,1], а пороги ниже написаны для центрированного [-1,1] —
        // иначе temp>0.10 истинен почти на всей карте и мир вырождается в одну Саванну.
        float temp = _beach.Get(wx * 0.0016f + 1200f, wz * 0.0016f + 1200f) * 2f - 1f;
        float humid = _forestNoise.Get(wx * 0.0016f + 2500f, wz * 0.0016f + 2500f) * 2f - 1f;

        // 1. Жаркие регионы (Пустыни и Саванны)
        if (temp > 0.10f) {
            if (humid <= 0.02f) return BiomeType.Desert;  // Полноценные просторные пустыни (~21% суши)
            return BiomeType.Savanna;                     // Просторные солнечные саванны (~21% суши)
        }

        // 2. Влажные низины (Болота)
        if (temp < -0.10f && humid > 0.08f) {
            return BiomeType.Swamp;                       // Атмосферные болотные низины (~16% суши)
        }

        // 3. Умеренные регионы (Леса и Поля/Равнины)
        if (humid > 0.10f) {
            return BiomeType.Forest;                      // Густые лесные массивы (~21% суши)
        }

        // 4. Просторные открытые луга, поля и цветущие холмы (~21% суши)
        return BiomeType.Plains;
    }

    public static string GetBiomeName(BiomeType biome) => biome switch {
        BiomeType.Ocean => "Океан",
        BiomeType.Beach => "Пляж",
        BiomeType.River => "Река",
        BiomeType.Forest => "Лес",
        BiomeType.Desert => "Пустыня",
        BiomeType.Mineshaft => "Заброшенная шахта",
        BiomeType.Savanna => "Саванна",
        BiomeType.Swamp => "Болото",
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
                bool isSwamp = biome == BiomeType.Swamp;
                bool isBeach = !isSwamp && (biome == BiomeType.Beach || (surface <= SeaLevel + 2 && surface >= SeaLevel - 3 && !isDesert));

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
                        // Высокогорье: выше снежной линии поверхность каменная, с гравийными осыпями
                        if (surface >= 56 && !isDesert && !isBeach) {
                            float scree = _detail.Get(wx * 0.11f + 321f, wz * 0.11f + 654f);
                            type = scree > 0.62f ? GameData.BGravel.Id : GameData.BStone.Id;
                        } else if (surface < SeaLevel - 1) {
                            type = beachNoise > 0.5f ? GameData.BGravel.Id : GameData.BSand.Id;
                        } else if (isDesert || isBeach) {
                            type = beachNoise > 0.3f ? GameData.BSand.Id : (isDesert ? GameData.BSand.Id : GameData.BGravel.Id);
                        } else if (isSwamp) {
                            type = GameData.BDirt.Id; // Болото: топкая грязевая поверхность
                        } else {
                            type = GameData.BGrass.Id;
                        }
                    } else if (wy >= surface - 3) {
                        if (surface >= 56 && !isDesert && !isBeach) {
                            type = GameData.BStone.Id; // Под каменными пиками — сплошной камень
                        } else if (isDesert || isBeach || surface < SeaLevel) {
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

                // Болотная вода повсеместно: топь вместо грязи во всех низинах болота.
                // Шум bogNoise решает, где вода, где кочки — вода встречается по всему биому,
                // а не только узкой полосой у уровня моря.
                if (isSwamp) {
                    float bogNoise = _detail.Fractal(wx * 0.035f + 777f, wz * 0.035f + 555f, 2, 0.5f);
                    bool boggy = bogNoise < 0.42f || surface <= SeaLevel + 1;
                    if (boggy && surface <= SeaLevel + 2) {
                        int surfLy = surface - oy;
                        if (surfLy >= 0 && surfLy < Chunk.SizeY) {
                            var wIdx = Chunk.Index(lx, surfLy, lz);
                            if (chunk.Get(wIdx).TypeId == GameData.BDirt.Id) {
                                // Мелкая топь (глубина 1) или полноценная заводь (глубже 2)
                                int waterDepth = bogNoise < 0.30f ? Math.Min(3, 1 + (SeaLevel + 2 - surface)) : 1;
                                for (int dy = 0; dy < waterDepth; dy++) {
                                    int wy = surfLy - dy;
                                    if (wy < 0 || wy >= Chunk.SizeY) break;
                                    int wIdxDeep = Chunk.Index(lx, wy, lz);
                                    if (chunk.Get(wIdxDeep).TypeId == GameData.BDirt.Id) {
                                        var waterVox = MakeVoxel(GameData.BWater.Id);
                                        chunk.SetVoxel(wIdxDeep, in waterVox);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // 1.19.2 Caves & Cliffs шумные пещеры + аквиферы
        CarveNoiseCaves(chunk, ox, oy, oz);

        // Заброшенные шахты с крепями, рельсами, паутиной и сундуками
        CarveMineshafts(chunk, ox, oy, oz);

        // Подземные сокровищницы (Данжи)
        PlaceDungeons(chunk, ox, oy, oz);

        // Заброшенные часовни (надземный данж) и сокровищницы пустыни
        PlaceAbandonedChapel(chunk, ox, oy, oz);
        PlaceDesertVault(chunk, ox, oy, oz);

        // Пустынные пирамиды и храмы
        PlaceDesertPyramids(chunk, ox, oy, oz);

        // 3D-жилы руд
        PlaceOreVeins(chunk, ox, oy, oz);

        // Деревья на поверхности
        PlaceTrees(chunk, ox, oz);

        // Растительность (2D трава)
        PlaceFoliage(chunk, ox, oz);

        // Деревни, порталы и крепости размещаются в чанках, пересекающих зону поверхности.
        // Диапазон 0..maxSurfaceChunkY охватывает и низины (Y=0), и высокие горы (Y=4).
        int maxSurfaceChunkY = (BaseHeight + HeightAmplitude + 20) / Chunk.SizeY + 1;
        if (chunk.Origin.Y >= 0 && chunk.Origin.Y <= maxSurfaceChunkY) {
            PlaceVillages(chunk, ox, oz);
            PlaceRuinedPortals(chunk, ox, oy, oz);
            PlaceEndStrongholds(chunk, ox, oy, oz);
        }
    }

    // ── Поиск биомов и структур (для команды /locate) ──────────────────────────

    /// <summary>Ищет ближайший биом заданного типа вокруг точки (спираль с шагом 16).</summary>
    public Vector3? FindNearestBiome(Vector3 from, BiomeType target) {
        int px = (int)MathF.Floor(from.X), pz = (int)MathF.Floor(from.Z);
        int testY = target == BiomeType.Mineshaft ? 18 : 50;
        for (int ring = 0; ring <= 400; ring++) {  // до ~6400 блоков
            int r = ring * 16;
            for (int i = -ring; i <= ring; i++) {
                if (GetBiome(px + i * 16, testY, pz + r) == target)
                    return new Vector3(px + i * 16 + 0.5f, MathF.Max(SeaLevel + 1, SurfaceHeight(px + i * 16, pz + r)), pz + r + 0.5f);
                if (GetBiome(px + i * 16, testY, pz - r) == target)
                    return new Vector3(px + i * 16 + 0.5f, MathF.Max(SeaLevel + 1, SurfaceHeight(px + i * 16, pz - r)), pz - r + 0.5f);
                if (GetBiome(px + r, testY, pz + i * 16) == target)
                    return new Vector3(px + r + 0.5f, MathF.Max(SeaLevel + 1, SurfaceHeight(px + r, pz + i * 16)), pz + i * 16 + 0.5f);
                if (GetBiome(px - r, testY, pz + i * 16) == target)
                    return new Vector3(px - r + 0.5f, MathF.Max(SeaLevel + 1, SurfaceHeight(px - r, pz + i * 16)), pz + i * 16 + 0.5f);
            }
        }
        return null;
    }

    /// <summary>Ищет ближайшую структуру по названию (village/stronghold/portal/dungeon/pyramid/mineshaft).</summary>
    public Vector3? FindNearestStructure(Vector3 from, string structure) {
        int px = (int)MathF.Floor(from.X), pz = (int)MathF.Floor(from.Z);
        switch (structure.ToLowerInvariant()) {
            case "village": return FindNearestVillage(px, pz);
            case "portal": case "ruinedportal": return FindNearestRuinedPortal(px, pz);
            case "dungeon": return FindNearestDungeon(px, pz);
            case "pyramid": case "desertpyramid": return FindNearestPyramid(px, pz);
            case "mineshaft": return FindNearestMineshaft(px, pz);
            case "stronghold": return FindNearestEndStronghold(from);
            default: return null;
        }
    }

    private Vector3? FindNearestVillage(int px, int pz) {
        const int sector = 704; // 22 чанка (сбалансированная частота)
        int psx = (int)MathF.Floor((float)px / sector), psz = (int)MathF.Floor((float)pz / sector);
        Vector3? best = null; float bestDist = float.MaxValue;
        for (int dx = -6; dx <= 6; dx++) for (int dz = -6; dz <= 6; dz++) {
            int sx = psx + dx, sz = psz + dz;
            float seed = _mineshaftNoise.Get(sx * 31.73f + 123.456f, sz * 31.73f + 654.321f);
            if (seed < 0.60f) continue;
            var rng = new Random(_seed ^ (sx * 73856093) ^ (sz * 19349663));
            int vx = sx * sector + rng.Next(24, sector - 24), vz = sz * sector + rng.Next(24, sector - 24);
            var biome = GetBiome(vx, 50, vz);
            if (biome == BiomeType.Desert || biome == BiomeType.Ocean || biome == BiomeType.Beach ||
                biome == BiomeType.River || biome == BiomeType.Swamp) continue;
            if (SurfaceHeight(vx, vz) < SeaLevel + 3) continue;
            float d = (vx - px) * (vx - px) + (vz - pz) * (vz - pz);
            if (d < bestDist) { bestDist = d; best = new Vector3(vx + 0.5f, SurfaceHeight(vx, vz), vz + 0.5f); }
        }
        return best;
    }

    private Vector3? FindNearestRuinedPortal(int px, int pz) {
        const int sector = 320;
        int psx = (int)MathF.Floor((float)px / sector), psz = (int)MathF.Floor((float)pz / sector);
        Vector3? best = null; float bestDist = float.MaxValue;
        for (int dx = -10; dx <= 10; dx++) for (int dz = -10; dz <= 10; dz++) {
            int sx = psx + dx, sz = psz + dz;
            float seed = _mineshaftNoise.Get(sx * 43.19f + 555.55f, sz * 43.19f + 777.77f);
            if (seed < 0.70f) continue;
            var rng = new Random(_seed ^ (sx * 458921) ^ (sz * 912837));
            int vx = sx * sector + rng.Next(24, sector - 24), vz = sz * sector + rng.Next(24, sector - 24);
            if (SurfaceHeight(vx, vz) <= SeaLevel + 2) continue;
            float d = (vx - px) * (vx - px) + (vz - pz) * (vz - pz);
            if (d < bestDist) { bestDist = d; best = new Vector3(vx + 0.5f, SurfaceHeight(vx, vz), vz + 0.5f); }
        }
        return best;
    }

    private Vector3? FindNearestDungeon(int px, int pz) {
        const int sector = 512;
        int psx = (int)MathF.Floor((float)px / sector), psz = (int)MathF.Floor((float)pz / sector);
        Vector3? best = null; float bestDist = float.MaxValue;
        for (int dx = -8; dx <= 8; dx++) for (int dz = -8; dz <= 8; dz++) {
            int sx = psx + dx, sz = psz + dz;
            float seed = _mineshaftNoise.Get(sx * 23.45f + 111f, sz * 23.45f + 222f);
            if (seed < 0.70f) continue;
            var rng = new Random(_seed ^ (sx * 928374) ^ (sz * 123891));
            int cx = sx * sector + rng.Next(64, sector - 64), cz = sz * sector + rng.Next(64, sector - 64);
            int cy = 14 + Math.Abs((sx * 37 + sz * 19) % 18);
            if (cy > 38) continue;
            float d = (cx - px) * (cx - px) + (cz - pz) * (cz - pz);
            if (d < bestDist) { bestDist = d; best = new Vector3(cx + 0.5f, cy + 0.5f, cz + 0.5f); }
        }
        return best;
    }

    private Vector3? FindNearestPyramid(int px, int pz) {
        const int sector = 512;
        int psx = (int)MathF.Floor((float)px / sector), psz = (int)MathF.Floor((float)pz / sector);
        Vector3? best = null; float bestDist = float.MaxValue;
        for (int dx = -8; dx <= 8; dx++) for (int dz = -8; dz <= 8; dz++) {
            int sx = psx + dx, sz = psz + dz;
            float seed = _mineshaftNoise.Get(sx * 42.1f + 123f, sz * 42.1f + 321f);
            if (seed < 0.85f) continue;
            var rng = new Random(_seed ^ (sx * 49281) ^ (sz * 89123));
            int cx = sx * sector + rng.Next(64, sector - 64), cz = sz * sector + rng.Next(64, sector - 64);
            if (GetBiome(cx, 40, cz) != BiomeType.Desert) continue;
            if (SurfaceHeight(cx, cz) <= SeaLevel + 2) continue;
            float d = (cx - px) * (cx - px) + (cz - pz) * (cz - pz);
            if (d < bestDist) { bestDist = d; best = new Vector3(cx + 0.5f, SurfaceHeight(cx, cz), cz + 0.5f); }
        }
        return best;
    }

    private Vector3? FindNearestMineshaft(int px, int pz) {
        const int sector = 128;
        int psx = (int)MathF.Floor((float)px / sector), psz = (int)MathF.Floor((float)pz / sector);
        Vector3? best = null; float bestDist = float.MaxValue;
        for (int dx = -20; dx <= 20; dx++) for (int dz = -20; dz <= 20; dz++) {
            int sx = psx + dx, sz = psz + dz;
            float seed = _mineshaftNoise.Get(sx * 17.3f + 100f, sz * 17.3f + 100f);
            if (seed < 0.78f) continue;
            int cx = sx * sector + 64, cz = sz * sector + 64;
            float d = (cx - px) * (cx - px) + (cz - pz) * (cz - pz);
            if (d < bestDist) { bestDist = d; best = new Vector3(cx + 0.5f, 18f, cz + 0.5f); }
        }
        return best;
    }

    // ── Крепости Энда (strongholds) в обычном мире ─────────────────────────────
    private const int EndStrongholdsCount = 6;
    private const int EndStrongholdRingRadius = 700; // крепости ближе друг к другу (было 1200)

    /// <summary>Детерминированный центр i-й крепости Энда (мировые XZ).</summary>
    private (int X, int Z) EndStrongholdCenter(int i) {
        int hash = unchecked((int)i * -1640531535 + 2137107477); // 0x9E3779B9, 0x7F4A7C15 как int
        var rng = new Random(_seed ^ hash);
        float angle = i / (float)EndStrongholdsCount * MathF.Tau + (float)rng.NextDouble() * 0.4f;
        float dist = EndStrongholdRingRadius + rng.Next(-120, 160);
        int x = (int)(MathF.Cos(angle) * dist);
        int z = (int)(MathF.Sin(angle) * dist);
        return (x, z);
    }

    /// <summary>Ближайшая к точке крепость Энда (мировые координаты, поверхность).</summary>
    public Vector3? FindNearestEndStronghold(Vector3 from) {
        Vector3? best = null;
        float bestDist = float.MaxValue;
        for (int i = 0; i < EndStrongholdsCount; i++) {
            var (cx, cz) = EndStrongholdCenter(i);
            // Крепость не генерируется над водой/пляжем — такую не показываем глазу
            if (SurfaceHeight(cx, cz) <= SeaLevel + 6) continue;
            float dx = cx - from.X, dz = cz - from.Z;
            float d = dx * dx + dz * dz;
            if (d < bestDist) {
                bestDist = d;
                best = new Vector3(cx + 0.5f, SurfaceHeight(cx, cz) + 0.5f, cz + 0.5f);
            }
        }
        return best;
    }

    /// <summary>Кладёт крепость Энда в чанки, пересекающиеся с её зоной.</summary>
    private void PlaceEndStrongholds(Chunk chunk, int ox, int oy, int oz) {
        for (int i = 0; i < EndStrongholdsCount; i++) {
            var (cx, cz) = EndStrongholdCenter(i);
            const int radius = 28;
            if (cx + radius < ox || cx - radius > ox + Chunk.SizeX - 1) continue;
            if (cz + radius < oz || cz - radius > oz + Chunk.SizeZ - 1) continue;
            PlaceEndStronghold(chunk, ox, oy, oz, cx, cz);
        }
    }

    /// <summary>
    /// Подземная крепость Энда: большая портальная камера в толще камня,
    /// кольцо 4×4 рамок, боковые сокровищницы, коридор с библиотекой и входная шахта.
    /// </summary>
    private void PlaceEndStronghold(Chunk chunk, int ox, int oy, int oz, int cWX, int cWZ) {
        int surface = SurfaceHeight(cWX, cWZ);
        if (surface <= SeaLevel + 6) return; // не в воде/пляже

        // Портальная камера — глубоко под поверхностью (в Y-чанке 1)
        int roomY = Math.Clamp(surface - 20, 34, 100);
        const int hw = 8;   // полуширина камеры (17×17)
        const int hh = 5;   // высота камеры

        // 1. Пол из камня
        for (int dx = -hw; dx <= hw; dx++)
            for (int dz = -hw; dz <= hw; dz++)
                SetWorldBlock(chunk, ox, oy, oz, cWX + dx, roomY, cWZ + dz, GameData.BStone.Id);

        // 2. Стены (булыжник) и воздух внутри
        for (int dx = -hw; dx <= hw; dx++) {
            for (int dz = -hw; dz <= hw; dz++) {
                for (int dy = 1; dy < hh; dy++) {
                    bool wall = Math.Abs(dx) == hw || Math.Abs(dz) == hw;
                    // Проём в западной стене для коридора
                    if (dx == -hw && Math.Abs(dz) <= 1 && dy <= 3) wall = false;
                    SetWorldBlock(chunk, ox, oy, oz, cWX + dx, roomY + dy, cWZ + dz,
                        wall ? GameData.BCobblestone.Id : (ushort)0);
                }
            }
        }

        // 3. Потолок
        for (int dx = -hw; dx <= hw; dx++)
            for (int dz = -hw; dz <= hw; dz++)
                SetWorldBlock(chunk, ox, oy, oz, cWX + dx, roomY + hh, cWZ + dz, GameData.BCobblestone.Id);

        // 4. Кольцо 4×4 из 12 рамок портала Энда в центре пола
        for (int dx = -2; dx <= 1; dx++)
            for (int dz = -2; dz <= 1; dz++) {
                bool ring = dx == -2 || dx == 1 || dz == -2 || dz == 1;
                SetWorldBlock(chunk, ox, oy, oz, cWX + dx, roomY + 1, cWZ + dz,
                    ring ? GameData.BEndPortalFrame.Id : (ushort)0);
            }

        // 5. Внутренние колонны и факелы
        SetWorldBlock(chunk, ox, oy, oz, cWX - hw + 2, roomY + 1, cWZ - hw + 2, GameData.BCobblestone.Id);
        SetWorldBlock(chunk, ox, oy, oz, cWX - hw + 2, roomY + 2, cWZ - hw + 2, GameData.BTorch.Id, 2);
        SetWorldBlock(chunk, ox, oy, oz, cWX + hw - 2, roomY + 1, cWZ - hw + 2, GameData.BCobblestone.Id);
        SetWorldBlock(chunk, ox, oy, oz, cWX + hw - 2, roomY + 2, cWZ - hw + 2, GameData.BTorch.Id, 1);
        SetWorldBlock(chunk, ox, oy, oz, cWX - hw + 2, roomY + 1, cWZ + hw - 2, GameData.BCobblestone.Id);
        SetWorldBlock(chunk, ox, oy, oz, cWX - hw + 2, roomY + 2, cWZ + hw - 2, GameData.BTorch.Id, 2);
        SetWorldBlock(chunk, ox, oy, oz, cWX + hw - 2, roomY + 1, cWZ + hw - 2, GameData.BCobblestone.Id);
        SetWorldBlock(chunk, ox, oy, oz, cWX + hw - 2, roomY + 2, cWZ + hw - 2, GameData.BTorch.Id, 1);

        // 6. Сокровищницы в боковых стенах (сундуки)
        SetWorldBlock(chunk, ox, oy, oz, cWX - hw, roomY + 1, cWZ - 1, GameData.BChest.Id, 3);
        SetWorldBlock(chunk, ox, oy, oz, cWX + hw, roomY + 1, cWZ + 1, GameData.BChest.Id, 1);

        // 7. Входная шахта с поверхности в камеру (через потолок), со ступеньками
        for (int y = surface; y >= roomY + hh; y--) {
            SetWorldBlock(chunk, ox, oy, oz, cWX, y, cWZ, 0);
            SetWorldBlock(chunk, ox, oy, oz, cWX + 1, y, cWZ, 0);
            if (y % 2 == 0) {
                SetWorldBlock(chunk, ox, oy, oz, cWX, y - 1, cWZ, GameData.BCobblestone.Id);
                SetWorldBlock(chunk, ox, oy, oz, cWX + 1, y - 1, cWZ, GameData.BCobblestone.Id);
            }
        }
        // Проём в потолке камеры над шахтой
        SetWorldBlock(chunk, ox, oy, oz, cWX, roomY + hh, cWZ, 0);
        SetWorldBlock(chunk, ox, oy, oz, cWX + 1, roomY + hh, cWZ, 0);

        // 8. Коридор крепости на запад (длина 8 блоков, ширина 5, высота 4)
        for (int dx = -hw - 8; dx < -hw; dx++) {
            for (int dz = -2; dz <= 2; dz++) {
                int wx = cWX + dx, wz = cWZ + dz;
                SetWorldBlock(chunk, ox, oy, oz, wx, roomY, wz, GameData.BStone.Id); // пол
                SetWorldBlock(chunk, ox, oy, oz, wx, roomY + 4, wz, GameData.BCobblestone.Id); // потолок
                for (int dy = 1; dy <= 3; dy++) {
                    bool wall = Math.Abs(dz) == 2;
                    SetWorldBlock(chunk, ox, oy, oz, wx, roomY + dy, wz, wall ? GameData.BCobblestone.Id : (ushort)0);
                }
            }
        }
        SetWorldBlock(chunk, ox, oy, oz, cWX - hw - 4, roomY + 2, cWZ + 1, GameData.BTorch.Id, 4);

        // 9. Комната-библиотека (Library) 7x7x4 в конце коридора
        int libWX = cWX - hw - 12;
        for (int dx = -3; dx <= 3; dx++) {
            for (int dz = -3; dz <= 3; dz++) {
                int wx = libWX + dx, wz = cWZ + dz;
                SetWorldBlock(chunk, ox, oy, oz, wx, roomY, wz, GameData.BStone.Id);
                SetWorldBlock(chunk, ox, oy, oz, wx, roomY + 4, wz, GameData.BCobblestone.Id);
                for (int dy = 1; dy <= 3; dy++) {
                    bool wall = dx == -3 || dz == -3 || dz == 3;
                    if (dx == 3 && Math.Abs(dz) > 1) wall = true;
                    if (wall) {
                        SetWorldBlock(chunk, ox, oy, oz, wx, roomY + dy, wz, GameData.BCobblestone.Id);
                    } else {
                        // Книжные стеллажи из досок вдоль стен
                        bool isShelf = (dx == -2 || dz == -2 || dz == 2) && dy <= 2;
                        SetWorldBlock(chunk, ox, oy, oz, wx, roomY + dy, wz, isShelf ? GameData.BPlanks.Id : (ushort)0);
                    }
                }
            }
        }
        // Сундук в библиотеке и факелы
        SetWorldBlock(chunk, ox, oy, oz, libWX - 1, roomY + 1, cWZ, GameData.BChest.Id, 2);
        SetWorldBlock(chunk, ox, oy, oz, libWX, roomY + 3, cWZ - 2, GameData.BTorch.Id, 3);
        SetWorldBlock(chunk, ox, oy, oz, libWX, roomY + 3, cWZ + 2, GameData.BTorch.Id, 4);

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
                        bool isSavanna = biome == BiomeType.Savanna;
                        bool isSwamp = biome == BiomeType.Swamp;
                        if (isForest || isPlains || isSavanna || isSwamp) {
                            // Естественная трава и аккуратные редкие полянки цветов
                            float grassChance = isSavanna ? 0.30f : isSwamp ? 0.35f : isPlains ? 0.22f : 0.15f;
                            
                            // Редкие кластеры цветов (маки и одуванчики появляются отдельными полянками)
                            float flowerNoise = _treeNoise.Get(wx * 0.04f + 800f, wz * 0.04f + 800f);
                            if (flowerNoise > 0.78f && (isPlains || isForest) && ((wx * 17 + wz * 31) % 6 == 0)) {
                                ushort flowerId = ((wx * 7 + wz * 13) % 2 == 0) ? GameData.BRedFlower.Id : GameData.BYellowFlower.Id;
                                var vf = MakeVoxel(flowerId);
                                chunk.SetVoxel(idx, in vf);
                            } else if (fNoise < grassChance && ((wx * 19 + wz * 37) % 4 == 0)) {
                                var vx = MakeVoxel(GameData.BTallGrass.Id);
                                chunk.SetVoxel(idx, in vx);
                            } else if (isForest && fNoise > 0.85f && ((wx * 13 + wz * 29) % 19 == 0)) {
                                var vs = MakeVoxel(GameData.BSapling.Id);
                                chunk.SetVoxel(idx, in vs);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Генерация пещер:
    /// - Cheese Caves (объемные залы)
    /// - Spaghetti Caves (сети 3D-туннелей во всех направлениях)
    /// - Noodle Caves (разломы)
    /// - Лавовые бассейны только на самом дне (Y <= 3)
    /// </summary>
    private void CarveNoiseCaves(Chunk chunk, int ox, int oy, int oz) {
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                int wx = ox + lx, wz = oz + lz;
                int surface = SurfaceHeight(wx, wz);

                // Вертикальные разломы / Каньоны (Ravines) — локализованные в отдельных зонах
                float ravineRegion = _caveNoodle.Get(wx * 0.005f + 400f, wz * 0.005f + 400f);
                float ravineAngle = wx * 0.018f + wz * 0.012f;
                float ravineDist = MathF.Abs(MathF.Sin(ravineAngle) * 35f - (wz * 0.035f));
                bool isRavine = ravineRegion > 0.40f && ravineDist < (1.1f + ravineRegion * 0.5f);

                for (int ly = 0; ly < Chunk.SizeY; ly++) {
                    int wy = oy + ly;
                    if (wy <= 4) continue; // Коренная порода (Bedrock) абсолютно нерушима и не прорезается пещерами
                    if (wy > surface + 2) continue; // воздух

                    int idx = Chunk.Index(lx, ly, lz);
                    ushort cur = chunk.Get(idx).TypeId;
                    if (cur == 0 || cur == GameData.BBedrock.Id) continue;

                    // Под водой (океаны, озёра) пещеры не должны пробивать дно водоёма и оставлять висящую в воздухе воду
                    if (surface <= SeaLevel + 1 && wy >= surface - 4) continue;

                    // 1. Cheese Caves (Просторные гроты и залы)
                    float cheese = _caveCheese.Fractal(wx * 0.014f, wy * 0.020f + 100f, wz * 0.014f, 3, 0.5f);
                    bool isCheese = (wy < 32 ? cheese > 0.48f : cheese > 0.56f) && wy < surface - 3;

                    // 2. Spaghetti Caves (Извилистые 3D-туннели во всех направлениях)
                    float sp1 = _caveSpaghetti1.Fractal(wx * 0.022f, wy * 0.030f, wz * 0.022f, 2, 0.5f);
                    float sp2 = _caveSpaghetti2.Fractal(wx * 0.022f + 150f, wy * 0.030f + 150f, wz * 0.022f + 150f, 2, 0.5f);
                    bool isSpaghetti = (sp1 * sp1 + sp2 * sp2) < 0.015f && wy < surface - 2;

                    // 3. Noodle Caves (Узкие вертикальные расщелины)
                    float noodle = _caveNoodle.Fractal(wx * 0.018f + 2000f, wy * 0.028f, wz * 0.018f + 2000f, 2, 0.5f);
                    bool isNoodle = MathF.Abs(noodle) < 0.016f && wy > 5 && wy < surface - 2;

                    // 4. Каньон / Разлом (глубокий вертикальный разрез)
                    bool inRavine = isRavine && wy >= 8 && wy <= surface - 4;

                    // Выходы пещер и разломов на сушу (но не под водой)
                    bool surfaceBreach = surface > SeaLevel + 1 && wy >= surface - 3 && (isSpaghetti || isNoodle || inRavine) && cheese > 0.42f;

                    if (isCheese || isSpaghetti || isNoodle || inRavine || surfaceBreach) {
                        // Естественные лавовые озера на самом дне мира (Y <= 8) с единой ровной гладью
                        ushort replaceWith = (wy <= 8) ? GameData.BLava.Id : (ushort)0;

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
                    bool isIntersection = branchX && branchZ;

                    ushort blockId = 0; // По умолчанию воздух в коридоре

                    if (wy == 17) {
                        // Пол коридора: рельсы каждые 2 блока, иначе доски или булыжник
                        bool hasRail = (branchX && cellX % 2 == 0) || (branchZ && cellZ % 2 == 0);
                        if (hasRail && !isIntersection) {
                            blockId = GameData.BRail.Id;
                        } else {
                            blockId = (cellX + cellZ) % 5 == 0 ? GameData.BPlanks.Id : GameData.BCobblestone.Id;
                        }
                    } else if (isArchStep) {
                        if (wy == 20) {
                            blockId = GameData.BPlanks.Id; // Верхняя балка арки
                        } else if (isSide) {
                            blockId = GameData.BLog.Id; // Опорные столбы арки
                            // Опорные колонны вниз при пересечении с пещерами
                            for (int downY = 16; downY >= 2; downY--) {
                                int lDownY = downY - oy;
                                if (lDownY < 0 || lDownY >= Chunk.SizeY) continue;
                                int downIdx = Chunk.Index(lx, lDownY, lz);
                                var curV = chunk.Get(downIdx);
                                if (curV.TypeId == 0 || curV.TypeId == GameData.BLava.Id || curV.TypeId == GameData.BWater.Id) {
                                    var pillarV = MakeVoxel(GameData.BLog.Id);
                                    chunk.SetVoxel(downIdx, in pillarV);
                                } else {
                                    break;
                                }
                            }
                        }
                    } else if (wy == 18) {
                        // Паутина на перекрёстках (30% шанс)
                        if (isIntersection && ((cellX * 7 + cellZ * 13) % 10 < 3)) {
                            blockId = GameData.BWeb.Id;
                        }
                        // Редкие сундуки в шахтах
                        else if ((cellX == 64 || cellZ == 64) && ((cellX * 13 + cellZ * 7) % 31 == 0)) {
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
                    if (wy <= 4 || wy >= 86) continue;

                    int idx = Chunk.Index(lx, ly, lz);
                    if (chunk.Get(idx).TypeId != GameData.BStone.Id) continue;

                    // Уголь (Coal) — жилы на высотах Y=5..85 (горы, пещеры)
                    if (wy <= 85) {
                        float coalN = _coalNoise.Fractal(wx * 0.16f, wy * 0.16f, wz * 0.16f, 2, 0.5f);
                        if (coalN > 0.78f) {
                            var v = MakeVoxel(GameData.BCoalOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }

                    // Железо (Iron) — сбалансированные жилы на высотах Y=5..54
                    if (wy <= 54) {
                        float ironN = _ironNoise.Fractal(wx * 0.20f, wy * 0.20f + 300f, wz * 0.20f, 2, 0.5f);
                        if (ironN > 0.79f) {
                            var v = MakeVoxel(GameData.BIronOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }

                    // Золото (Gold) — жилы на глубине Y=5..32 (и до 56 в пустынях)
                    var biome = GetBiome(wx, BaseHeight, wz);
                    int maxGoldY = (biome == BiomeType.Desert) ? 56 : 32;
                    if (wy <= maxGoldY) {
                        float goldN = _goldNoise.Fractal(wx * 0.22f + 700f, wy * 0.22f, wz * 0.22f, 2, 0.5f);
                        if (goldN > 0.81f) {
                            var v = MakeVoxel(GameData.BGoldOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }

                    // Алмазы (Diamond) — РЕДКИЕ драгоценные жилы на глубине Y=5..20 (значительно реже золота)
                    if (wy <= 20) {
                        float diaN = _diamondNoise.Fractal(wx * 0.26f + 5000f, wy * 0.26f, wz * 0.26f, 2, 0.5f);
                        if (diaN > 0.85f) {
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
                        if (n < 0.50f) continue; // Ранний отсев по вероятности дерева

                        int lx = tx * 4 + (int)(n * 3.9f);
                        int lz = tz * 4 + (int)((1f - n) * 3.9f);
                        if (lx >= Chunk.SizeX || lz >= Chunk.SizeZ) continue;

                        int wx = neighborOx + lx;
                        int wz = neighborOz + lz;

                        int curLx = wx - ox;
                        int curLz = wz - oz;
                        // Дерево с радиусом кроны 2 влияет только на близкие границы
                        if (curLx < -2 || curLx >= Chunk.SizeX + 2 || curLz < -2 || curLz >= Chunk.SizeZ + 2) continue;

                        if (IsInVillage(wx, wz) || IsInRuinedPortal(wx, wz) || IsInDesertPyramid(wx, wz)) continue;

                        var biome = GetBiome(wx, BaseHeight, wz);
                        if (biome == BiomeType.Plains) {
                            if (n < 0.88f) continue; // Редкие одиночные деревья на равнинах
                        } else if (biome == BiomeType.Savanna) {
                            if (n < 0.78f) continue; // Саванна: редкие раскидистые деревья
                        } else if (biome != BiomeType.Forest && biome != BiomeType.Swamp) {
                            continue; // Лес и болото — полная плотность деревьев
                        }

                        int surface = SurfaceHeight(wx, wz);
                        // Деревья растут только на траве выше уровня моря и ниже снежной линии
                        if (surface <= SeaLevel + 1 || surface >= 56) continue;

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

    public bool IsInVillage(int wx, int wz) {
        const int villageSectorBlocks = 704;
        int sectorX = (int)MathF.Floor((float)wx / villageSectorBlocks);
        int sectorZ = (int)MathF.Floor((float)wz / villageSectorBlocks);
        float villageSeed = _mineshaftNoise.Get(sectorX * 31.73f + 123.456f, sectorZ * 31.73f + 654.321f);
        if (villageSeed < 0.60f) return false;

        var rng = new Random(_seed ^ (sectorX * 73856093) ^ (sectorZ * 19349663));
        int villageWX = sectorX * villageSectorBlocks + rng.Next(24, villageSectorBlocks - 24);
        int villageWZ = sectorZ * villageSectorBlocks + rng.Next(24, villageSectorBlocks - 24);

        int dx = wx - villageWX, dz = wz - villageWZ;
        return (dx * dx + dz * dz) <= (48 * 48);
    }

    public bool IsInRuinedPortal(int wx, int wz) {
        const int portalSectorSize = 320;
        int sectorX = (int)MathF.Floor((float)wx / portalSectorSize);
        int sectorZ = (int)MathF.Floor((float)wz / portalSectorSize);
        float pSeed = _mineshaftNoise.Get(sectorX * 43.19f + 555.55f, sectorZ * 43.19f + 777.77f);
        if (pSeed < 0.70f) return false;

        var rng = new Random(_seed ^ (sectorX * 458921) ^ (sectorZ * 912837));
        int portalWX = sectorX * portalSectorSize + rng.Next(24, portalSectorSize - 24);
        int portalWZ = sectorZ * portalSectorSize + rng.Next(24, portalSectorSize - 24);

        int dx = wx - portalWX, dz = wz - portalWZ;
        return (dx * dx + dz * dz) <= (8 * 8);
    }

    public bool IsInDesertPyramid(int wx, int wz) {
        const int pyramidSector = 512;
        int sectorX = (int)MathF.Floor((float)wx / pyramidSector);
        int sectorZ = (int)MathF.Floor((float)wz / pyramidSector);
        float pSeed = _mineshaftNoise.Get(sectorX * 42.1f + 123f, sectorZ * 42.1f + 321f);
        if (pSeed < 0.85f) return false;

        var rng = new Random(_seed ^ (sectorX * 49281) ^ (sectorZ * 89123));
        int cx = sectorX * pyramidSector + rng.Next(64, pyramidSector - 64);
        int cz = sectorZ * pyramidSector + rng.Next(64, pyramidSector - 64);

        int dx = wx - cx, dz = wz - cz;
        return (dx * dx + dz * dz) <= (14 * 14);
    }

    /// <summary>
    /// Генерирует процедурные деревни с домиками и сундуками с лутом.
    /// Деревни появляются на равнинах и в лесах вне пустынь и водоёмов.
    /// </summary>
    private void PlaceVillages(Chunk chunk, int ox, int oz) {
        // Размер сектора деревни: 704 блока (совпадает с FindNearestVillage, сбалансированная плотность)
        const int villageSectorBlocks = 704;

        int sectorX = (int)MathF.Floor((float)ox / villageSectorBlocks);
        int sectorZ = (int)MathF.Floor((float)oz / villageSectorBlocks);

        // Генерируем позицию деревни в секторе через шум
        var rng = new Random(_seed ^ (sectorX * 73856093) ^ (sectorZ * 19349663));
        float villageSeed = _mineshaftNoise.Get(sectorX * 31.73f + 123.456f, sectorZ * 31.73f + 654.321f);
        if (villageSeed < 0.60f) return; // ~20% секторов получают деревни в подходящих биомах

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
            centerBiome == BiomeType.Beach || centerBiome == BiomeType.River ||
            centerBiome == BiomeType.Swamp) return;

        // 1. Центральный колодец деревни (Village Town Well)
        PlaceVillageWell(chunk, ox, oz, villageWX, villageWZ);

        // 2. Генерируем 4-7 разнообразных зданий вокруг центра
        int houseCount = rng.Next(4, 8);
        for (int h = 0; h < houseCount; h++) {
            float angle = (float)h / houseCount * MathF.Tau + (float)rng.NextDouble() * 0.35f;
            float dist = rng.Next(12, 34);
            int houseX = villageWX + (int)(MathF.Cos(angle) * dist);
            int houseZ = villageWZ + (int)(MathF.Sin(angle) * dist);
            var (doorX, doorZ) = PlaceVillageHouse(chunk, ox, oz, houseX, houseZ, h, rng);

            // Фонарный столб возле дома
            int lampX = villageWX + (int)(MathF.Cos(angle) * (dist * 0.65f));
            int lampZ = villageWZ + (int)(MathF.Sin(angle) * (dist * 0.65f));
            PlaceVillageLampPost(chunk, ox, oz, lampX, lampZ);

            // Дорога напрямую к двери каждого дома
            PlaceVillageRoadPath(chunk, ox, oz, doorX, doorZ, villageWX, villageWZ);
        }
    }

    private void PlaceVillageWell(Chunk chunk, int ox, int oz, int wx, int wz) {
        int surface = SurfaceHeight(wx, wz);
        if (surface < SeaLevel + 2) return;

        // Колодец 4x4
        for (int dx = -2; dx <= 2; dx++) {
            for (int dz = -2; dz <= 2; dz++) {
                int cwx = wx + dx;
                int cwz = wz + dz;
                int cwy = surface;

                bool isBorder = Math.Abs(dx) == 2 || Math.Abs(dz) == 2;
                bool isCorner = Math.Abs(dx) == 2 && Math.Abs(dz) == 2;

                BuildVillageFoundation(chunk, ox, oz, cwx, cwz, cwy);

                for (int u = 0; u <= 4; u++)
                    SetVillageBlock(chunk, ox, oz, cwx, cwy - u, cwz, isBorder ? GameData.BCobblestone.Id : GameData.BWater.Id);

                if (isBorder) {
                    SetVillageBlock(chunk, ox, oz, cwx, cwy + 1, cwz, GameData.BCobblestone.Id);
                }

                if (isCorner) {
                    SetVillageBlock(chunk, ox, oz, cwx, cwy + 2, cwz, GameData.BLog.Id);
                    SetVillageBlock(chunk, ox, oz, cwx, cwy + 3, cwz, GameData.BLog.Id);
                }

                // Крыша колодца
                SetVillageBlock(chunk, ox, oz, cwx, cwy + 4, cwz, GameData.BPlanks.Id);
            }
        }
        // Факелы на столбах колодца
        SetVillageBlock(chunk, ox, oz, wx - 2, surface + 3, wz - 1, GameData.BTorch.Id, 3);
        SetVillageBlock(chunk, ox, oz, wx + 2, surface + 3, wz + 1, GameData.BTorch.Id, 4);
    }

    private void PlaceVillageLampPost(Chunk chunk, int ox, int oz, int lampWX, int lampWZ) {
        int surface = SurfaceHeight(lampWX, lampWZ);
        if (surface < SeaLevel + 2) return;

        BuildVillageFoundation(chunk, ox, oz, lampWX, lampWZ, surface);
        SetVillageBlock(chunk, ox, oz, lampWX, surface, lampWZ, GameData.BCobblestone.Id);
        SetVillageBlock(chunk, ox, oz, lampWX, surface + 1, lampWZ, GameData.BLog.Id);
        SetVillageBlock(chunk, ox, oz, lampWX, surface + 2, lampWZ, GameData.BLog.Id);
        SetVillageBlock(chunk, ox, oz, lampWX, surface + 3, lampWZ, GameData.BLog.Id);
        SetVillageBlock(chunk, ox, oz, lampWX, surface + 3, lampWZ + 1, GameData.BTorch.Id, 4);
    }

    private (int DoorX, int DoorZ) PlaceVillageHouse(Chunk chunk, int ox, int oz, int houseWX, int houseWZ, int houseIdx, Random rng) {
        int surface = SurfaceHeight(houseWX, houseWZ);
        if (surface < SeaLevel + 2) return (houseWX, houseWZ);

        int houseType = houseIdx % 5; // 0 = Коттедж, 1 = Кузница, 2 = Ферма, 3 = Башня/Церковь, 4 = Хижина

        if (houseType == 1) {
            // ── 1. Кузница селения (Blacksmith Forge) 7x5 ──
            const int W = 7, D = 5, H = 4;
            int doorWX = houseWX;
            int doorWZ = houseWZ - D / 2 - 1;

            // Очистка воздуха над кузницей
            for (int dz = -D / 2 - 1; dz <= D / 2 + 1; dz++) {
                for (int dx = -W / 2 - 1; dx <= W / 2 + 1; dx++) {
                    for (int y = 1; y <= H + 3; y++) {
                        SetVillageAirIfSolid(chunk, ox, oz, houseWX + dx, surface + y, houseWZ + dz);
                    }
                }
            }

            for (int dz = 0; dz < D; dz++) {
                for (int dx = 0; dx < W; dx++) {
                    int wx = houseWX + dx - W / 2;
                    int wz = houseWZ + dz - D / 2;
                    int wy = surface;

                    BuildVillageFoundation(chunk, ox, oz, wx, wz, wy);
                    SetVillageBlock(chunk, ox, oz, wx, wy, wz, GameData.BCobblestone.Id);

                    bool isCorner = (dx == 0 || dx == W - 1) && (dz == 0 || dz == D - 1);
                    bool isBack = dz == D - 1;
                    bool isLeft = dx == 0;

                    for (int y = 1; y <= H - 1; y++) {
                        if (isCorner) {
                            SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, GameData.BLog.Id);
                        } else if (isBack || (isLeft && y <= 2)) {
                            SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, GameData.BCobblestone.Id);
                        } else {
                            SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, 0);
                        }
                    }
                    SetVillageBlock(chunk, ox, oz, wx, wy + H, wz, GameData.BStone.Id);
                }
            }
            // 2 Печи, верстак, сундук кузнеца
            SetVillageBlock(chunk, ox, oz, houseWX - 2, surface + 1, houseWZ + 1, GameData.BFurnace.Id);
            SetVillageBlock(chunk, ox, oz, houseWX - 1, surface + 1, houseWZ + 1, GameData.BFurnace.Id);
            SetVillageBlock(chunk, ox, oz, houseWX + 1, surface + 1, houseWZ + 1, GameData.BWorkbench.Id);
            SetVillageBlock(chunk, ox, oz, houseWX + 2, surface + 1, houseWZ + 1, GameData.BChest.Id);

            // Емкость из булыжника для лавы в углу кузницы
            SetVillageBlock(chunk, ox, oz, houseWX - 2, surface + 1, houseWZ - 2, GameData.BCobblestone.Id);
            SetVillageBlock(chunk, ox, oz, houseWX - 1, surface + 1, houseWZ - 2, GameData.BCobblestone.Id);
            SetVillageBlock(chunk, ox, oz, houseWX - 1, surface + 1, houseWZ - 1, GameData.BCobblestone.Id);
            SetVillageBlock(chunk, ox, oz, houseWX - 2, surface + 1, houseWZ, GameData.BCobblestone.Id);
            SetVillageBlock(chunk, ox, oz, houseWX - 2, surface, houseWZ - 1, GameData.BCobblestone.Id);
            SetVillageBlock(chunk, ox, oz, houseWX - 2, surface + 1, houseWZ - 1, GameData.BLava.Id); // лава внутри каменной емкости
            SetVillageBlock(chunk, ox, oz, houseWX, surface + 3, houseWZ + 1, GameData.BTorch.Id, 4); // настенный факел
            return (doorWX, doorWZ);
        }

        if (houseType == 2) {
            // ── 2. Фермерский дом (Farmhouse) + Большая грядка с пшеницей ──
            const int W = 6, D = 5, H = 4;
            int doorWX = houseWX;
            int doorWZ = houseWZ - D / 2 - 1;

            // Очистка воздуха над домом
            for (int dz = -D / 2 - 1; dz <= D / 2 + 1; dz++) {
                for (int dx = -W / 2 - 1; dx <= W / 2 + 1; dx++) {
                    for (int y = 1; y <= H + 3; y++) {
                        SetVillageAirIfSolid(chunk, ox, oz, houseWX + dx, surface + y, houseWZ + dz);
                    }
                }
            }

            for (int dz = 0; dz < D; dz++) {
                for (int dx = 0; dx < W; dx++) {
                    int wx = houseWX + dx - W / 2;
                    int wz = houseWZ + dz - D / 2;
                    int wy = surface;

                    BuildVillageFoundation(chunk, ox, oz, wx, wz, wy);
                    SetVillageBlock(chunk, ox, oz, wx, wy, wz, GameData.BPlanks.Id);

                    bool isEdge = dx == 0 || dx == W - 1 || dz == 0 || dz == D - 1;
                    bool isDoor = dz == 0 && (dx == W / 2);
                    bool isWindow = (dx == 0 || dx == W - 1) && dz == D / 2;

                    for (int y = 1; y <= H - 1; y++) {
                        if (isEdge) {
                            if (isDoor && (y == 1 || y == 2)) {
                                SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, y == 1 ? GameData.BDoorLower.Id : GameData.BDoorUpper.Id, 2);
                            } else if (isWindow && y == 2) {
                                SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, GameData.BGlass.Id);
                            } else {
                                SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, GameData.BPlanks.Id);
                            }
                        } else {
                            SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, 0);
                        }
                    }
                    SetVillageBlock(chunk, ox, oz, wx, wy + H, wz, GameData.BLog.Id);
                }
            }
            // Кровать и сундук внутри
            SetVillageBlock(chunk, ox, oz, houseWX - 1, surface + 1, houseWZ + 1, GameData.BBedHead.Id, 2);
            SetVillageBlock(chunk, ox, oz, houseWX - 1, surface + 1, houseWZ, GameData.BBed.Id, 2);
            SetVillageBlock(chunk, ox, oz, houseWX + 1, surface + 1, houseWZ + 1, GameData.BChest.Id);
            SetVillageBlock(chunk, ox, oz, houseWX, surface + 3, houseWZ + 1, GameData.BTorch.Id, 4);

            // Огород рядом с домом (5x4) с водой
            for (int fz = 0; fz < 4; fz++) {
                for (int fx = 0; fx < 5; fx++) {
                    int gwx = houseWX + W / 2 + 1 + fx;
                    int gwz = houseWZ - 2 + fz;
                    int gwy = SurfaceHeight(gwx, gwz);
                    if (gwy >= SeaLevel + 1) {
                        bool isWaterCanal = (fx == 2);
                        if (isWaterCanal) {
                            SetVillageBlock(chunk, ox, oz, gwx, gwy, gwz, GameData.BWater.Id);
                        } else {
                            SetVillageBlock(chunk, ox, oz, gwx, gwy, gwz, GameData.BFarmland.Id);
                            ushort cropId = ((fx + fz) % 3) switch {
                                0 => GameData.BWheatCrop.Id,
                                1 => GameData.BCarrotCrop.Id,
                                _ => GameData.BPotatoCrop.Id
                            };
                            byte stage = (byte)(((fx * 3 + fz * 7) % 3) + 1); // стадии 1..3
                            SetVillageBlock(chunk, ox, oz, gwx, gwy + 1, gwz, cropId, stage);
                        }
                    }
                }
            }
            return (doorWX, doorWZ);
        }

        if (houseType == 3) {
            // ── 3. Сторожевая башня / Церковь (Watchtower / Church) 5x5x7 ──
            const int W = 5, D = 5, H = 7;
            int doorWX = houseWX;
            int doorWZ = houseWZ - D / 2 - 1;

            // Очистка воздуха над башней
            for (int dz = -D / 2 - 1; dz <= D / 2 + 1; dz++) {
                for (int dx = -W / 2 - 1; dx <= W / 2 + 1; dx++) {
                    for (int y = 1; y <= H + 3; y++) {
                        SetVillageAirIfSolid(chunk, ox, oz, houseWX + dx, surface + y, houseWZ + dz);
                    }
                }
            }

            for (int dz = 0; dz < D; dz++) {
                for (int dx = 0; dx < W; dx++) {
                    int wx = houseWX + dx - W / 2;
                    int wz = houseWZ + dz - D / 2;
                    int wy = surface;

                    BuildVillageFoundation(chunk, ox, oz, wx, wz, wy);
                    SetVillageBlock(chunk, ox, oz, wx, wy, wz, GameData.BCobblestone.Id);

                    bool isEdge = dx == 0 || dx == W - 1 || dz == 0 || dz == D - 1;
                    bool isDoor = dz == 0 && (dx == W / 2);

                    for (int y = 1; y <= H; y++) {
                        if (isEdge) {
                            if (isDoor && (y == 1 || y == 2)) {
                                SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, y == 1 ? GameData.BDoorLower.Id : GameData.BDoorUpper.Id, 2);
                            } else if (y == 4 && (dx == 0 || dx == W - 1 || dz == D - 1)) {
                                SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, GameData.BGlass.Id);
                            } else if (y == H) {
                                // Зубцы башни
                                bool isBattlement = (dx == 0 || dx == W - 1) && (dz == 0 || dz == D - 1);
                                SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, isBattlement ? GameData.BCobblestone.Id : (ushort)0);
                            } else {
                                SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, GameData.BCobblestone.Id);
                            }
                        } else {
                            SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, (y == H - 1) ? GameData.BPlanks.Id : (ushort)0);
                        }
                    }
                }
            }
            // Сундук и факелы на верхушке башни
            SetVillageBlock(chunk, ox, oz, houseWX, surface + H, houseWZ, GameData.BChest.Id);
            SetVillageBlock(chunk, ox, oz, houseWX - 2, surface + H + 1, houseWZ - 2, GameData.BTorch.Id);
            SetVillageBlock(chunk, ox, oz, houseWX + 2, surface + H + 1, houseWZ + 2, GameData.BTorch.Id);
            return (doorWX, doorWZ);
        }

        // ── 0 и 4. Жилой коттедж и Уютная хижина со стеклами, дверьми и кроватями ──
        int houseW = (houseType == 0) ? 6 : 5;
        int houseD = (houseType == 0) ? 6 : 5;
        const int houseH = 4;
        int doorXCoord = houseWX;
        int doorZCoord = houseWZ - houseD / 2 - 1;

        // Очистка воздуха над домом
        for (int dz = -houseD / 2 - 1; dz <= houseD / 2 + 1; dz++) {
            for (int dx = -houseW / 2 - 1; dx <= houseW / 2 + 1; dx++) {
                for (int y = 1; y <= houseH + 3; y++) {
                    SetVillageAirIfSolid(chunk, ox, oz, houseWX + dx, surface + y, houseWZ + dz);
                }
            }
        }

        for (int dz = 0; dz < houseD; dz++) {
            for (int dx = 0; dx < houseW; dx++) {
                int wx = houseWX + dx - houseW / 2;
                int wz = houseWZ + dz - houseD / 2;
                int wy = surface;

                BuildVillageFoundation(chunk, ox, oz, wx, wz, wy);
                SetVillageBlock(chunk, ox, oz, wx, wy, wz, GameData.BPlanks.Id);

                bool isCorner = (dx == 0 || dx == houseW - 1) && (dz == 0 || dz == houseD - 1);
                bool isEdge = dx == 0 || dx == houseW - 1 || dz == 0 || dz == houseD - 1;
                bool isDoor = dz == 0 && (dx == houseW / 2);
                bool isWindow = (dx == 0 || dx == houseW - 1) && dz == houseD / 2;

                for (int y = 1; y <= houseH - 1; y++) {
                    if (isCorner) {
                        SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, GameData.BLog.Id);
                    } else if (isEdge) {
                        if (isDoor && (y == 1 || y == 2)) {
                            SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, y == 1 ? GameData.BDoorLower.Id : GameData.BDoorUpper.Id, 2);
                        } else if (isWindow && y == 2) {
                            SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, GameData.BGlass.Id);
                        } else {
                            SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, GameData.BPlanks.Id);
                        }
                    } else {
                        SetVillageBlock(chunk, ox, oz, wx, wy + y, wz, 0);
                    }
                }

                SetVillageBlock(chunk, ox, oz, wx, wy + houseH, wz, GameData.BLog.Id);
            }
        }

        // Кровать в углу (изголовье у задней стены facing=2)
        SetVillageBlock(chunk, ox, oz, houseWX - 1, surface + 1, houseWZ + 1, GameData.BBedHead.Id, 2);
        SetVillageBlock(chunk, ox, oz, houseWX - 1, surface + 1, houseWZ, GameData.BBed.Id, 2);

        // Сундук и верстак
        SetVillageBlock(chunk, ox, oz, houseWX + 1, surface + 1, houseWZ + 1, GameData.BChest.Id);
        if (houseType == 0) {
            SetVillageBlock(chunk, ox, oz, houseWX + 1, surface + 1, houseWZ - 1, GameData.BWorkbench.Id);
        }

        // Настенный факел внутри дома и настенный факел над входом снаружи
        SetVillageBlock(chunk, ox, oz, houseWX, surface + 3, houseWZ + houseD / 2 - 1, GameData.BTorch.Id, 4);
        SetVillageBlock(chunk, ox, oz, houseWX, surface + 3, houseWZ - houseD / 2 - 1, GameData.BTorch.Id, 3);

        return (doorXCoord, doorZCoord);
    }

    private void BuildVillageFoundation(Chunk chunk, int ox, int oz, int wx, int wz, int startY) {
        int natSurf = SurfaceHeight(wx, wz);
        for (int under = 0; under <= 12; under++) {
            int wy = startY - under;
            if (wy <= 2) break;
            if (wy < natSurf - 1) break;
            SetVillageBlock(chunk, ox, oz, wx, wy, wz, GameData.BCobblestone.Id);
        }
    }

    private void SetVillageAirIfSolid(Chunk chunk, int ox, int oz, int wx, int wy, int wz) {
        int lx = wx - ox, lz = wz - oz;
        if (lx < 0 || lx >= Chunk.SizeX || lz < 0 || lz >= Chunk.SizeZ) return;
        int ly = wy - chunk.Origin.Y * Chunk.SizeY;
        if (ly < 0 || ly >= Chunk.SizeY) return;
        int idx = Chunk.Index(lx, ly, lz);
        ushort cur = chunk.Get(idx).TypeId;
        if (cur == GameData.BGrass.Id || cur == GameData.BDirt.Id || cur == GameData.BStone.Id ||
            cur == GameData.BTallGrass.Id || cur == GameData.BSand.Id || cur == GameData.BGravel.Id ||
            cur == GameData.BLeaves.Id || cur == GameData.BLog.Id) {
            chunk.SetVoxel(idx, VoxelData.Air);
        }
    }

    private void PlaceVillageRoadPath(Chunk chunk, int ox, int oz, int x1, int z1, int x2, int z2) {
        int cx = x1, cz = z1;
        int dx = Math.Sign(x2 - x1);
        int dz = Math.Sign(z2 - z1);
        while (cx != x2) {
            SetVillageRoadBlock(chunk, ox, oz, cx, cz);
            SetVillageRoadBlock(chunk, ox, oz, cx, cz + 1);
            cx += dx;
        }
        while (cz != z2) {
            SetVillageRoadBlock(chunk, ox, oz, cx, cz);
            SetVillageRoadBlock(chunk, ox, oz, cx + 1, cz);
            cz += dz;
        }
    }

    private void PlaceRuinedPortals(Chunk chunk, int ox, int oy, int oz) {
        const int portalSectorSize = 320; // Разрушенный портал (раньше 512)
        int sectorX = (int)MathF.Floor((float)ox / portalSectorSize);
        int sectorZ = (int)MathF.Floor((float)oz / portalSectorSize);

        var rng = new Random(_seed ^ (sectorX * 458921) ^ (sectorZ * 912837));
        float pSeed = _mineshaftNoise.Get(sectorX * 43.19f + 555.55f, sectorZ * 43.19f + 777.77f);
        if (pSeed < 0.70f) return; // ~30% секторов получают разрушенный портал

        int portalWX = sectorX * portalSectorSize + rng.Next(24, portalSectorSize - 24);
        int portalWZ = sectorZ * portalSectorSize + rng.Next(24, portalSectorSize - 24);

        int chunkMinX = ox, chunkMaxX = ox + Chunk.SizeX - 1;
        int chunkMinZ = oz, chunkMaxZ = oz + Chunk.SizeZ - 1;
        if (portalWX + 15 < chunkMinX || portalWX - 15 > chunkMaxX) return;
        if (portalWZ + 15 < chunkMinZ || portalWZ - 15 > chunkMaxZ) return;

        int surface = SurfaceHeight(portalWX, portalWZ);
        if (surface <= SeaLevel + 2) return;

        // Платформа адского камня 7x7 с маленькой лавой
        for (int dx = -3; dx <= 3; dx++) {
            for (int dz = -3; dz <= 3; dz++) {
                int r2 = dx * dx + dz * dz;
                if (r2 > 10) continue;
                int pwx = portalWX + dx, pwz = portalWZ + dz;
                int pwy = SurfaceHeight(pwx, pwz);
                if (pwy >= SeaLevel) {
                    SetVillageBlock(chunk, ox, oz, pwx, pwy, pwz, GameData.BNetherrack.Id);
                    if (r2 == 0) {
                        SetVillageBlock(chunk, ox, oz, pwx, pwy, pwz, GameData.BLava.Id);
                    }
                }
            }
        }

        // Рама разрушенного портала 4x5
        for (int px = -1; px <= 2; px++) {
            for (int py = 1; py <= 5; py++) {
                bool isFrame = px == -1 || px == 2 || py == 1 || py == 5;
                if (isFrame) {
                    int bwx = portalWX + px, bwz = portalWZ;
                    int bwy = surface + py;

                    bool broken = (px == 2 && py == 4) || (px == -1 && py == 5 && rng.NextDouble() < 0.5);
                    ushort b = broken ? (ushort)0 : (rng.NextDouble() < 0.25 ? GameData.BNetherrack.Id : GameData.BObsidian.Id);
                    if (b != 0) {
                        SetWorldBlock(chunk, ox, oy, oz, bwx, bwy, bwz, b);
                    }
                }
            }
        }

        // Сундук портала на адском камне
        SetWorldBlock(chunk, ox, oy, oz, portalWX + 2, surface + 1, portalWZ + 1, GameData.BChest.Id);
        SetWorldBlock(chunk, ox, oy, oz, portalWX - 2, surface + 1, portalWZ - 1, GameData.BGoldOre.Id);
    }

    private void SetVillageRoadBlock(Chunk chunk, int ox, int oz, int wx, int wz) {
        int surface = SurfaceHeight(wx, wz);
        if (surface < SeaLevel + 2) return;
        int lx = wx - ox, lz = wz - oz;
        if (lx < 0 || lx >= Chunk.SizeX || lz < 0 || lz >= Chunk.SizeZ) return;
        int ly = surface - chunk.Origin.Y * Chunk.SizeY;
        if (ly < 0 || ly >= Chunk.SizeY) return;
        int idx = Chunk.Index(lx, ly, lz);
        ushort cur = chunk.Get(idx).TypeId;
        if (cur == GameData.BGrass.Id || cur == GameData.BDirt.Id) {
            var v = MakeVoxel(GameData.BGravel.Id);
            chunk.SetVoxel(idx, in v);
        }
    }

    private void SetVillageBlock(Chunk chunk, int ox, int oz, int wx, int wy, int wz, ushort blockId, byte facing = 0) {
        int lx = wx - ox, lz = wz - oz;
        if (lx < 0 || lx >= Chunk.SizeX || lz < 0 || lz >= Chunk.SizeZ) return;
        int ly = wy - chunk.Origin.Y * Chunk.SizeY;
        if (ly < 0 || ly >= Chunk.SizeY) return;
        var v = MakeVoxel(blockId, facing);
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

    public void SetWorldBlock(Chunk chunk, int ox, int oy, int oz, int wx, int wy, int wz, ushort blockId, byte facing = 0) {
        int lx = wx - ox, lz = wz - oz, ly = wy - oy;
        if (lx < 0 || lx >= Chunk.SizeX || lz < 0 || lz >= Chunk.SizeZ || ly < 0 || ly >= Chunk.SizeY) return;
        var v = MakeVoxel(blockId, facing);
        chunk.SetVoxel(Chunk.Index(lx, ly, lz), in v);
    }

    private void PlaceDungeons(Chunk chunk, int ox, int oy, int oz) {
        if (oy > 38 || oy + Chunk.SizeY < 8) return;
        const int dungeonSector = 512;
        int sectorX = (int)MathF.Floor((float)ox / dungeonSector);
        int sectorZ = (int)MathF.Floor((float)oz / dungeonSector);

        // Вероятность данжа в секторе (~30%)
        float dSeed = _mineshaftNoise.Get(sectorX * 23.45f + 111f, sectorZ * 23.45f + 222f);
        if (dSeed < 0.70f) return;

        var rng = new Random(_seed ^ (sectorX * 928374) ^ (sectorZ * 123891));
        int cx = sectorX * dungeonSector + rng.Next(64, dungeonSector - 64);
        int cz = sectorZ * dungeonSector + rng.Next(64, dungeonSector - 64);
        int cy = 14 + Math.Abs((sectorX * 37 + sectorZ * 19) % 18);

        if (Math.Abs(ox + Chunk.SizeX / 2 - cx) > 28 || Math.Abs(oz + Chunk.SizeZ / 2 - cz) > 28) return;

        // Комната 7x7x5 с мшистым булыжником и сквозными арками
        for (int dx = -3; dx <= 3; dx++) {
            for (int dz = -3; dz <= 3; dz++) {
                for (int dy = 0; dy <= 4; dy++) {
                    int wx = cx + dx, wy = cy + dy, wz = cz + dz;
                    bool isWall = dx == -3 || dx == 3 || dz == -3 || dz == 3 || dy == 0 || dy == 4;
                    if (isWall) {
                        bool isDoorway = dy >= 1 && dy <= 2 && ((Math.Abs(dx) == 3 && Math.Abs(dz) <= 1) || (Math.Abs(dz) == 3 && Math.Abs(dx) <= 1));
                        if (isDoorway) {
                            SetWorldBlock(chunk, ox, oy, oz, wx, wy, wz, 0);
                        } else {
                            ushort wallBlock = ((wx * 7 + wz * 13 + wy * 3) % 3 == 0) ? GameData.BMossyCobblestone.Id : GameData.BCobblestone.Id;
                            SetWorldBlock(chunk, ox, oy, oz, wx, wy, wz, wallBlock);
                        }
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

    /// <summary>Заброшенная часовня: надземный данж на поверхности (лес/равнины/саванна),
    /// небольшая постройка из булыжника с колокольным ярусом, спавнером и двумя сундуками.</summary>
    private void PlaceAbandonedChapel(Chunk chunk, int ox, int oy, int oz) {
        const int chapelSector = 640;
        int sectorX = (int)MathF.Floor((float)ox / chapelSector);
        int sectorZ = (int)MathF.Floor((float)oz / chapelSector);

        // ~33% секторов имеют часовню
        float cSeed = _mineshaftNoise.Get(sectorX * 17.71f + 919f, sectorZ * 17.71f + 131f);
        if (cSeed < 0.67f) return;

        var rng = new Random(_seed ^ (sectorX * 557003) ^ (sectorZ * 411193));
        int cx = sectorX * chapelSector + rng.Next(96, chapelSector - 96);
        int cz = sectorZ * chapelSector + rng.Next(96, chapelSector - 96);

        // Только на открытой суше, не в деревне/пирамиде/портале и не в горах
        var biome = GetBiome(cx, BaseHeight, cz);
        if (biome is not (BiomeType.Plains or BiomeType.Forest or BiomeType.Savanna)) return;
        if (IsInVillage(cx, cz) || IsInDesertPyramid(cx, cz) || IsInRuinedPortal(cx, cz)) return;
        int surface = SurfaceHeight(cx, cz);
        if (surface <= SeaLevel + 1 || surface >= 54) return;

        if (Math.Abs(ox + Chunk.SizeX / 2 - cx) > 24 || Math.Abs(oz + Chunk.SizeZ / 2 - cz) > 24) return;
        // Часовня требует, чтобы весь её footprint был в этом чанке — иначе сосед пропустит
        if (ox + Chunk.SizeX - 1 < cx + 4 || oz + Chunk.SizeZ - 1 < cz + 4) return;

        int baseY = surface + 1;

        // Фундамент 7×7 из булыжника/мшистого
        for (int dx = -3; dx <= 3; dx++) {
            for (int dz = -3; dz <= 3; dz++) {
                ushort wall = ((cx + dx) * 5 + (cz + dz) * 11) % 4 == 0 ? GameData.BMossyCobblestone.Id : GameData.BCobblestone.Id;
                SetWorldBlock(chunk, ox, oy, oz, cx + dx, surface, cz + dz, wall);
            }
        }

        // Стены высотой 4 с дверным проёмом на юге
        for (int dy = 1; dy <= 4; dy++) {
            for (int dx = -3; dx <= 3; dx++) {
                for (int dz = -3; dz <= 3; dz++) {
                    bool isWall = dx == -3 || dx == 3 || dz == -3 || dz == 3;
                    if (!isWall) continue;
                    bool isDoor = dz == 3 && dx == 0 && dy <= 2;
                    if (isDoor) continue;
                    // Колокольный ярус: угловые столбы выше
                    bool isCorner = Math.Abs(dx) == 3 && Math.Abs(dz) == 3;
                    if (isCorner && dy <= 5) continue;
                    ushort wall = ((cx + dx) * 7 + (cz + dz) * 13 + dy * 3) % 4 == 0 ? GameData.BMossyCobblestone.Id : GameData.BCobblestone.Id;
                    SetWorldBlock(chunk, ox, oy, oz, cx + dx, surface + dy, cz + dz, wall);
                }
            }
        }

        // Крыша
        for (int dx = -4; dx <= 4; dx++) {
            for (int dz = -4; dz <= 4; dz++) {
                SetWorldBlock(chunk, ox, oy, oz, cx + dx, surface + 5, cz + dz,
                    ((cx + dx) * 3 + (cz + dz)) % 3 == 0 ? GameData.BMossyCobblestone.Id : GameData.BCobblestone.Id);
            }
        }

        // Спавнер в центре, факелы у стен, 2 сундука
        SetWorldBlock(chunk, ox, oy, oz, cx, surface + 1, cz, GameData.BMobSpawner.Id);
        SetWorldBlock(chunk, ox, oy, oz, cx - 2, surface + 1, cz - 2, GameData.BChest.Id, 2);
        SetWorldBlock(chunk, ox, oy, oz, cx + 2, surface + 1, cz + 2, GameData.BChest.Id, 1);
        SetWorldBlock(chunk, ox, oy, oz, cx - 2, surface + 3, cz, GameData.BTorch.Id);
        SetWorldBlock(chunk, ox, oy, oz, cx + 2, surface + 3, cz, GameData.BTorch.Id);
    }

    /// <summary>Сокровищница пустыни: подземный данж из резного песчаника под пустынными секторами,
    /// крестообразная схема с ловушкой из TNT и богатым лутом.</summary>
    private void PlaceDesertVault(Chunk chunk, int ox, int oy, int oz) {
        const int vaultSector = 768;
        int sectorX = (int)MathF.Floor((float)ox / vaultSector);
        int sectorZ = (int)MathF.Floor((float)oz / vaultSector);

        // ~40% пустынных секторов имеют сокровищницу
        float vSeed = _mineshaftNoise.Get(sectorX * 29.37f + 616f, sectorZ * 29.37f + 828f);
        if (vSeed < 0.60f) return;

        var rng = new Random(_seed ^ (sectorX * 771273) ^ (sectorZ * 951341));
        int cx = sectorX * vaultSector + rng.Next(96, vaultSector - 96);
        int cz = sectorZ * vaultSector + rng.Next(96, vaultSector - 96);

        if (GetBiome(cx, 40, cz) != BiomeType.Desert) return;
        int surface = SurfaceHeight(cx, cz);
        if (surface <= SeaLevel) return;
        int cy = Math.Max(12, surface - 12); // под поверхностью

        if (Math.Abs(ox + Chunk.SizeX / 2 - cx) > 24 || Math.Abs(oz + Chunk.SizeZ / 2 - cz) > 24) return;
        if (ox + Chunk.SizeX - 1 < cx + 4 || oz + Chunk.SizeZ - 1 < cz + 4) return;

        // Главная комната 9×9×5 из резного песчаника с проходами
        for (int dx = -4; dx <= 4; dx++) {
            for (int dz = -4; dz <= 4; dz++) {
                for (int dy = 0; dy <= 4; dy++) {
                    int wx = cx + dx, wy = cy + dy, wz = cz + dz;
                    bool isWall = dx == -4 || dx == 4 || dz == -4 || dz == 4 || dy == 0 || dy == 4;
                    if (isWall) {
                        SetWorldBlock(chunk, ox, oy, oz, wx, wy, wz, GameData.BChiseledSandstone.Id);
                    } else {
                        ushort inside = 0;
                        if (dx == 0 && dz == 0 && dy == 1) {
                            inside = GameData.BMobSpawner.Id;
                        } else if (Math.Abs(dx) == 2 && Math.Abs(dz) == 2 && dy == 1) {
                            inside = GameData.BChest.Id; // 4 сундука по углам
                        } else if (dx == 0 && dz == 0 && dy == 3) {
                            inside = GameData.BTNT.Id; // ловушка над спавнером
                        }
                        SetWorldBlock(chunk, ox, oy, oz, wx, wy, wz, inside);
                    }
                }
            }
        }

        // Вход-колодец с поверхности (3×3, вниз до крыши данжа)
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                for (int wy = surface; wy >= cy + 5; wy--) {
                    SetWorldBlock(chunk, ox, oy, oz, cx + dx, wy, cz + dz, 0);
                }
                SetWorldBlock(chunk, ox, oy, oz, cx + dx, surface, cz + dz, 0);
            }
        }
    }

    private void PlaceDesertPyramids(Chunk chunk, int ox, int oy, int oz) {
        const int pyramidSector = 512;
        int sectorX = (int)MathF.Floor((float)ox / pyramidSector);
        int sectorZ = (int)MathF.Floor((float)oz / pyramidSector);
        
        float pSeed = _mineshaftNoise.Get(sectorX * 42.1f + 123f, sectorZ * 42.1f + 321f);
        if (pSeed < 0.85f) return;

        var rng = new Random(_seed ^ (sectorX * 49281) ^ (sectorZ * 89123));
        int cx = sectorX * pyramidSector + rng.Next(64, pyramidSector - 64);
        int cz = sectorZ * pyramidSector + rng.Next(64, pyramidSector - 64);

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

        // Факелы на стенах сокровищницы для защиты от случайного спавна монстров на нажимную плиту
        SetWorldBlock(chunk, ox, oy, oz, cx - 2, vaultY + 2, cz - 2, GameData.BTorch.Id, 2);
        SetWorldBlock(chunk, ox, oy, oz, cx + 2, vaultY + 2, cz - 2, GameData.BTorch.Id, 1);
        SetWorldBlock(chunk, ox, oy, oz, cx - 2, vaultY + 2, cz + 2, GameData.BTorch.Id, 2);
        SetWorldBlock(chunk, ox, oy, oz, cx + 2, vaultY + 2, cz + 2, GameData.BTorch.Id, 1);

        // Подземная ловушка: 3x3 TNT под полом и нажимная плита в центре
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                SetWorldBlock(chunk, ox, oy, oz, cx + dx, vaultY - 1, cz + dz, GameData.BTNT.Id);
            }
        }
        SetWorldBlock(chunk, ox, oy, oz, cx, vaultY, cz, GameData.BPressurePlate.Id);

        // 4 сундука у стен комнаты (на полу, но не загораживают нажимную плиту)
        SetWorldBlock(chunk, ox, oy, oz, cx + 2, vaultY + 1, cz, GameData.BChest.Id);
        SetWorldBlock(chunk, ox, oy, oz, cx - 2, vaultY + 1, cz, GameData.BChest.Id);
        SetWorldBlock(chunk, ox, oy, oz, cx, vaultY + 1, cz + 2, GameData.BChest.Id);
        SetWorldBlock(chunk, ox, oy, oz, cx, vaultY + 1, cz - 2, GameData.BChest.Id);
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
                        float glowNoise = _coalNoise.Fractal(wx * 0.08f, wy * 0.08f, wz * 0.08f, 2, 0.5f);
                        if (glowNoise > 0.72f) blockId = GameData.BGlowstone.Id;
                    }

                    // Аутентичные Адские крепости (Nether Fortress)
                    int fortSectorX = (int)MathF.Floor(wx / 160f);
                    int fortSectorZ = (int)MathF.Floor(wz / 160f);
                    int fortHash = Math.Abs(fortSectorX * 73856093 ^ fortSectorZ * 83492791 ^ Seed ^ 0x5EEDF087);
                    if (fortHash % 100 < 55) { // 55% секторов содержат крепость
                        int fcX = fortSectorX * 160 + 80;
                        int fcZ = fortSectorZ * 160 + 80;
                        int relX = wx - fcX;
                        int relZ = wz - fcZ;

                        // Главный мост (Север-Юг) и перекресток (Запад-Восток)
                        bool isNorthSouthBridge = Math.Abs(relX) <= 2 && Math.Abs(relZ) <= 50;
                        bool isEastWestBridge = Math.Abs(relZ) <= 2 && Math.Abs(relX) <= 50;
                        bool isFortressCore = isNorthSouthBridge || isEastWestBridge;

                        // Балкон со спавнером ифритов (Blaze Spawner Room на восточном конце)
                        bool isSpawnerBalcony = Math.Abs(relX - 44) <= 4 && Math.Abs(relZ) <= 4;
                        // Комната с песком душ и адским наростом (на западном конце)
                        bool isWartRoom = Math.Abs(relX - (-44)) <= 4 && Math.Abs(relZ) <= 4;

                        if (isFortressCore || isSpawnerBalcony || isWartRoom) {
                            if (wy == 46) {
                                // Пол моста / балкона / комнаты
                                if (isWartRoom && Math.Abs(relX - (-44)) <= 2 && Math.Abs(relZ) <= 2) {
                                    blockId = GameData.BSoulSand.Id; // грядка песка душ в центре комнаты
                                } else {
                                    blockId = GameData.BNetherBrick.Id;
                                }
                            } else if (wy == 47) {
                                if (isSpawnerBalcony) {
                                    // Ограждение балкона
                                    if (Math.Abs(relX - 44) == 4 || Math.Abs(relZ) == 4) blockId = GameData.BNetherBrick.Id;
                                    else if (relX == 44 && relZ == 0) blockId = GameData.BNetherBrick.Id; // Постамент
                                    else blockId = 0;
                                } else if (isWartRoom) {
                                    // Стены комнаты адского нароста с сундуком
                                    if (Math.Abs(relX - (-44)) == 4 || Math.Abs(relZ) == 4) blockId = GameData.BNetherBrick.Id;
                                    else if (relX == -44 && relZ == 3) blockId = GameData.BChest.Id;
                                    else blockId = 0;
                                } else {
                                    // Перила моста
                                    bool isEdge = (isNorthSouthBridge && Math.Abs(relX) == 2) || (isEastWestBridge && Math.Abs(relZ) == 2);
                                    if (isEdge) blockId = GameData.BNetherBrick.Id;
                                    else blockId = 0;
                                }
                            } else if (wy == 48) {
                                // Единственный спавнер ифритов на постаменте в центре комнаты
                                if (isSpawnerBalcony && relX == 44 && relZ == 0) blockId = GameData.BMobSpawner.Id;
                                else if (isWartRoom && (Math.Abs(relX - (-44)) == 4 || Math.Abs(relZ) == 4)) blockId = GameData.BNetherBrick.Id;
                                else blockId = 0;
                            } else if (wy >= 49 && wy <= 53) {
                                blockId = 0; // Проход над мостом
                            } else if (wy < 46 && wy >= 31) {
                                // Массивные опорные колонны моста до лавы
                                bool isPillar = (isNorthSouthBridge && Math.Abs(relZ) % 18 <= 2 && Math.Abs(relX) <= 2) ||
                                                (isEastWestBridge && Math.Abs(relX) % 18 <= 2 && Math.Abs(relZ) <= 2) ||
                                                (isSpawnerBalcony && (Math.Abs(relX - 44) == 3 || Math.Abs(relZ) == 3)) ||
                                                (isWartRoom && (Math.Abs(relX - (-44)) == 3 || Math.Abs(relZ) == 3));
                                if (isPillar) blockId = GameData.BNetherBrick.Id;
                            }
                        }
                    }

                    chunk.SetVoxel(idx, MakeVoxel(blockId));
                }
            }
        }
    }

    /// <summary>Высота твердой поверхности центрального острова Энда в мировых координатах (для спавна).</summary>
    public int EndSurfaceHeight(int wx, int wz) {
        float distC = MathF.Sqrt(wx * wx + wz * wz);
        if (distC >= 60f) return -1; // вне главного острова (пустота)
        // Почти плоская арена: лёгкий наклон к краю, без холма в центре,
        // чтобы Слизень Края мог свободно перемещаться.
        float top = 56f + MathF.Max(0f, (50f - distC) * 0.05f);
        return (int)MathF.Floor(top);
    }

    /// <summary>
    /// Генерация измерения Энд:
    /// - Центральный остров из эндового камня (купол).
    /// - 10 обсидиановых колонн по кольцу с эндер-кристаллами на вершинах.
    /// - Парящие островки из эндового камня вокруг.
    /// - Пустота (void) везде остальном.
    /// </summary>
    public void GenerateEndChunk(Chunk chunk, int ox, int oy, int oz) {
        const int pillarCount = 10;
        const float pillarRadius = 31f;

        // Детерминированные высоты колонн взаимозависимы от мира, но не меняются при перерисовании чанка
        var pillarHeights = new int[pillarCount];
        for (int i = 0; i < pillarCount; i++) {
            pillarHeights[i] = 56 + (Math.Abs((Seed + i * 53) * 40503 ^ (i * 1777)) % 36); // 56..92
        }

        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                int wx = ox + lx, wz = oz + lz;
                float distC = MathF.Sqrt(wx * wx + wz * wz);
                float islandTop = EndSurfaceHeight(wx, wz); // -1 вне главного острова

                // Парящий островок (детерминированная сетка — крупные острова, дальше от центра)
                bool onIsland = false;
                float isleTop = -1f;
                int chorusH = 0;   // высота хорус-дерева (только на побочных островах)
                if (distC >= 170f) {
                    int ciX = (int)MathF.Floor((wx + 100f) / 200f);
                    int ciZ = (int)MathF.Floor((wz + 100f) / 200f);
                    int cX = ciX * 200 - 100, cZ = ciZ * 200 - 100;
                    int hc = Math.Abs(ciX * 73856093 ^ ciZ * 83492791 ^ Seed * 92821);
                    if (hc % 100 < 55) {
                        float fdx = wx - cX, fdz = wz - cZ;
                        float fr = MathF.Sqrt(fdx * fdx + fdz * fdz);
                        float radius = 28 + (hc / 53) % 21; // 28..48 — крупные островки
                        if (fr < radius) {
                            onIsland = true;
                            isleTop = 68f + (hc / 11) % 46; // 68..113
                            // Хорус-деревья редко и ближе к центру островка (не сплошной массой)
                            if (fr < radius * 0.4f && (hc / 7) % 100 < 12) {
                                chorusH = 2 + (hc / 13) % 4; // 2..5 блоков ствола
                            }
                        }
                    }
                }

                for (int ly = 0; ly < Chunk.SizeY; ly++) {
                    int wy = oy + ly;
                    ushort blockId = 0;

                    if (islandTop >= 0f && wy <= (int)MathF.Floor(islandTop) && wy >= (int)MathF.Floor(islandTop) - 9) {
                        blockId = GameData.BEndStone.Id;
                    } else if (onIsland && wy <= (int)MathF.Floor(isleTop) && wy >= (int)MathF.Floor(isleTop) - 9) {
                        blockId = GameData.BEndStone.Id;
                    } else if (onIsland && chorusH > 0 && wy >= (int)MathF.Floor(isleTop) + 1 && wy <= (int)MathF.Floor(isleTop) + chorusH) {
                        // Ствол хоруса (нормальное дерево) на побочном острове
                        blockId = GameData.BChorusPlant.Id;
                    } else if (onIsland && chorusH > 0 && wy == (int)MathF.Floor(isleTop) + chorusH + 1) {
                        // Цветок хоруса на верхушке
                        blockId = GameData.BChorusFlower.Id;
                    }

                    chunk.SetVoxel(Chunk.Index(lx, ly, lz), MakeVoxel(blockId));
                }
            }
        }

        // Толстые обсидиановые колонны 2×2 с эндер-кристаллами (тяжело забраться)
        for (int i = 0; i < pillarCount; i++) {
            float ang = i * MathF.Tau / pillarCount;
            int px = (int)MathF.Round(pillarRadius * MathF.Cos(ang));
            int pz = (int)MathF.Round(pillarRadius * MathF.Sin(ang));
            int baseY = EndSurfaceHeight(px, pz);
            int topY = pillarHeights[i];
            for (int dx = 0; dx <= 1; dx++) {
                for (int dz = 0; dz <= 1; dz++) {
                    for (int wy = baseY; wy <= topY; wy++) {
                        SetWorldBlock(chunk, ox, oy, oz, px + dx, wy, pz + dz, GameData.BObsidianPillar.Id);
                    }
                }
            }
            // Кристалл в углу 2×2 на вершине; остальные клетки — воздух
            SetWorldBlock(chunk, ox, oy, oz, px, topY + 1, pz, GameData.BEnderCrystal.Id);
            SetWorldBlock(chunk, ox, oy, oz, px + 1, topY + 1, pz, 0);
            SetWorldBlock(chunk, ox, oy, oz, px, topY + 1, pz + 1, 0);
            SetWorldBlock(chunk, ox, oy, oz, px + 1, topY + 1, pz + 1, 0);
            SetWorldBlock(chunk, ox, oy, oz, px, topY + 2, pz, 0);
        }

        // Забытый Обелиск Края с древним секретным рецептом Ключа Бездны на побочном острове (+100, +100)
        int altarX = 100, altarZ = 100;
        int altarSurf = 72;
        for (int dx = -2; dx <= 2; dx++) {
            for (int dz = -2; dz <= 2; dz++) {
                SetWorldBlock(chunk, ox, oy, oz, altarX + dx, altarSurf, altarZ + dz, GameData.BObsidian.Id);
            }
        }
        SetWorldBlock(chunk, ox, oy, oz, altarX - 2, altarSurf + 1, altarZ - 2, GameData.BObsidianPillar.Id);
        SetWorldBlock(chunk, ox, oy, oz, altarX - 2, altarSurf + 2, altarZ - 2, GameData.BObsidianPillar.Id);
        SetWorldBlock(chunk, ox, oy, oz, altarX - 2, altarSurf + 3, altarZ - 2, GameData.BTorch.Id, 2);

        SetWorldBlock(chunk, ox, oy, oz, altarX + 2, altarSurf + 1, altarZ - 2, GameData.BObsidianPillar.Id);
        SetWorldBlock(chunk, ox, oy, oz, altarX + 2, altarSurf + 2, altarZ - 2, GameData.BObsidianPillar.Id);
        SetWorldBlock(chunk, ox, oy, oz, altarX + 2, altarSurf + 3, altarZ - 2, GameData.BTorch.Id, 1);

        SetWorldBlock(chunk, ox, oy, oz, altarX - 2, altarSurf + 1, altarZ + 2, GameData.BObsidianPillar.Id);
        SetWorldBlock(chunk, ox, oy, oz, altarX - 2, altarSurf + 2, altarZ + 2, GameData.BObsidianPillar.Id);
        SetWorldBlock(chunk, ox, oy, oz, altarX - 2, altarSurf + 3, altarZ + 2, GameData.BTorch.Id, 2);

        SetWorldBlock(chunk, ox, oy, oz, altarX + 2, altarSurf + 1, altarZ + 2, GameData.BObsidianPillar.Id);
        SetWorldBlock(chunk, ox, oy, oz, altarX + 2, altarSurf + 2, altarZ + 2, GameData.BObsidianPillar.Id);
        SetWorldBlock(chunk, ox, oy, oz, altarX + 2, altarSurf + 3, altarZ + 2, GameData.BTorch.Id, 1);

        // В центре алтаря — святилище древних
        SetWorldBlock(chunk, ox, oy, oz, altarX, altarSurf + 1, altarZ, GameData.BWorkbench.Id);
    }

    /// <summary>
    /// Тайное измерение «Бездна»: колоссальный монолитный пол из бедрока в бездонной пустоте
    /// под Эндом. В центре — древний алтарь и Врата Бездны.
    /// Попасть сюда можно, совершив смертоносный прыжок в пустоту Энда с золотыми яблоками и тотемами.
    /// </summary>
    public void GenerateVoidChunk(Chunk chunk, int ox, int oy, int oz) {
        const int arenaRadius = 45;
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                int wx = ox + lx, wz = oz + lz;
                float dist = MathF.Sqrt(wx * wx + wz * wz);
                for (int ly = 0; ly < Chunk.SizeY; ly++) {
                    int wy = oy + ly;
                    ushort blockId = 0;

                    // Монолитный пол из коренной породы (Бедрока)
                    if (dist <= arenaRadius) {
                        if (wy == 9) blockId = GameData.BBedrock.Id;
                        else if (wy == 10) blockId = GameData.BBedrock.Id;
                        else if (wy == 11) {
                            // Обсидиановые узоры и руны по кругу арены
                            blockId = (MathF.Abs(dist - 20f) < 1.2f || MathF.Abs(dist - 35f) < 1.2f || (Math.Abs(wx) <= 1 || Math.Abs(wz) <= 1))
                                ? GameData.BObsidian.Id : GameData.BBedrock.Id;
                        }
                    }

                    // Алтарь-врата в центре арены
                    if (MathF.Abs(wx) <= 2 && MathF.Abs(wz) <= 2 && wy == 12) blockId = GameData.BObsidian.Id;
                    if (wx == 0 && wz == 0 && wy == 13) blockId = GameData.BVoidGate.Id;

                    chunk.SetVoxel(Chunk.Index(lx, ly, lz), MakeVoxel(blockId));
                }
            }
        }

        // Обсидиановые монолиты по краям арены бедрока
        const int pillarCount = 8;
        for (int p = 0; p < pillarCount; p++) {
            float ang = p * MathF.Tau / pillarCount;
            int px = (int)MathF.Round(34f * MathF.Cos(ang));
            int pz = (int)MathF.Round(34f * MathF.Sin(ang));
            for (int y = 11; y <= 20; y++) {
                SetWorldBlock(chunk, ox, oy, oz, px, y, pz, GameData.BObsidianPillar.Id);
            }
            SetWorldBlock(chunk, ox, oy, oz, px, 21, pz, GameData.BTorch.Id, 2);
        }
    }

    public static VoxelData MakeVoxel(ushort blockId, byte facing = 0) {
        if (blockId == 0) return VoxelData.Air;
        var b = GameData.GetBlock(blockId);
        var flags = VoxelFlags.None;
        if (b.IsSolid) flags |= VoxelFlags.Solid;
        return new VoxelData {
            TypeId = blockId,
            Flags = flags,
            SubGridLayerMask = facing,
        };
    }
}

