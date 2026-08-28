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

        // Болото: очень влажно, умеренно-тёплый климат (тёмная вода, слаймы ночью)
        if (humid > 0.80f && temp < 0.70f) {
            return BiomeType.Swamp;
        }
        // Пустыня: жарко и сухо
        if (temp > 0.70f && humid < 0.38f) {
            return BiomeType.Desert; // Жаркий и сухой биом пустыни (~11% суши)
        }
        // Саванна: тепло, не очень влажно (сухие жёлтые равнины с редкими деревьями)
        if (temp > 0.40f && temp < 0.75f && humid < 0.50f) {
            return BiomeType.Savanna;
        }
        // Лес: влажный лесистый
        if (humid > 0.50f) {
            return BiomeType.Forest; // Влажный лесистый биом
        }
        return BiomeType.Plains; // Умеренные просторные равнины
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
                        } else if (isSwamp) {
                            type = GameData.BDirt.Id; // Болото: топкая грязевая поверхность
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

                // Болотные лужи: в низинах болота мелкая тёмная вода вместо грязи
                if (biome == BiomeType.Swamp && surface >= SeaLevel && surface <= SeaLevel + 2) {
                    int surfLy = surface - oy;
                    if (surfLy >= 0 && surfLy < Chunk.SizeY) {
                        var wIdx = Chunk.Index(lx, surfLy, lz);
                        if (chunk.Get(wIdx).TypeId == GameData.BDirt.Id) {
                            var waterVox = MakeVoxel(GameData.BWater.Id);
                            chunk.SetVoxel(wIdx, in waterVox);
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

        // Пустынные пирамиды и храмы
        PlaceDesertPyramids(chunk, ox, oy, oz);

        // 3D-жилы руд
        PlaceOreVeins(chunk, ox, oy, oz);

        // Деревья на поверхности
        PlaceTrees(chunk, ox, oz);

        // Растительность (2D трава)
        PlaceFoliage(chunk, ox, oz);

        // Редкие деревни и разрушенные порталы
        if (chunk.Origin.Y == 1 || chunk.Origin.Y == 2) {
            PlaceVillages(chunk, ox, oz);
            PlaceRuinedPortals(chunk, ox, oy, oz);
            PlaceEndStrongholds(chunk, ox, oy, oz);
        }
    }

    // ── Поиск биомов и структур (для команды /locate) ──────────────────────────

    /// <summary>Ищет ближайший биом заданного типа вокруг точки (спираль с шагом 16).</summary>
    public Vector3? FindNearestBiome(Vector3 from, BiomeType target) {
        int px = (int)MathF.Floor(from.X), pz = (int)MathF.Floor(from.Z);
        for (int ring = 0; ring <= 220; ring++) {  // до ~3520 блоков
            int r = ring * 16;
            for (int i = -ring; i <= ring; i++) {
                if (GetBiome(px + i * 16, 50, pz + r) == target)
                    return new Vector3(px + i * 16 + 0.5f, MathF.Max(SeaLevel + 1, SurfaceHeight(px + i * 16, pz + r)), pz + r + 0.5f);
                if (GetBiome(px + i * 16, 50, pz - r) == target)
                    return new Vector3(px + i * 16 + 0.5f, MathF.Max(SeaLevel + 1, SurfaceHeight(px + i * 16, pz - r)), pz - r + 0.5f);
                if (GetBiome(px + r, 50, pz + i * 16) == target)
                    return new Vector3(px + r + 0.5f, MathF.Max(SeaLevel + 1, SurfaceHeight(px + r, pz + i * 16)), pz + i * 16 + 0.5f);
                if (GetBiome(px - r, 50, pz + i * 16) == target)
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
        const int sector = 448; // 14 чанков
        int psx = (int)MathF.Floor((float)px / sector), psz = (int)MathF.Floor((float)pz / sector);
        Vector3? best = null; float bestDist = float.MaxValue;
        for (int dx = -8; dx <= 8; dx++) for (int dz = -8; dz <= 8; dz++) {
            int sx = psx + dx, sz = psz + dz;
            float seed = _mineshaftNoise.Get(sx * 31.73f + 123.456f, sz * 31.73f + 654.321f);
            if (seed < 0.30f) continue;
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
            const int radius = 16;
            if (cx + radius < ox || cx - radius > ox + Chunk.SizeX - 1) continue;
            if (cz + radius < oz || cz - radius > oz + Chunk.SizeZ - 1) continue;
            PlaceEndStronghold(chunk, ox, oy, oz, cx, cz);
        }
    }

    /// <summary>
    /// Подземная крепость Энда: большая портальная камера в толще камня,
    /// кольцо 4×4 рамок, боковые сокровищницы и входная шахта с поверхности.
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

        // 8. Заметный маркер на поверхности над крепостью — чтобы её легко было найти
        int markerY = surface;
        SetWorldBlock(chunk, ox, oy, oz, cWX, markerY, cWZ, GameData.BCobblestone.Id);
        SetWorldBlock(chunk, ox, oy, oz, cWX + 1, markerY, cWZ, GameData.BCobblestone.Id);
        SetWorldBlock(chunk, ox, oy, oz, cWX, markerY, cWZ + 1, GameData.BCobblestone.Id);
        SetWorldBlock(chunk, ox, oy, oz, cWX + 1, markerY, cWZ + 1, GameData.BCobblestone.Id);
        SetWorldBlock(chunk, ox, oy, oz, cWX, markerY + 1, cWZ, GameData.BTorch.Id, 3);
        SetWorldBlock(chunk, ox, oy, oz, cWX + 1, markerY + 1, cWZ, GameData.BTorch.Id, 3);
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
                        if (isForest || isPlains || isSavanna) {
                            // Оптимизированная естественная плотность 2D-травы (кластерами)
                            float chance = isSavanna ? 0.30f : isPlains ? 0.22f : 0.12f;
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
    /// Генерация пещер:
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
                bool isRavine = ravineDist < 1.1f;

                for (int ly = 0; ly < Chunk.SizeY; ly++) {
                    int wy = oy + ly;
                    if (wy <= 3) continue; // коренная порода
                    if (wy > surface + 2) continue; // воздух

                    int idx = Chunk.Index(lx, ly, lz);
                    ushort cur = chunk.Get(idx).TypeId;
                    if (cur == 0 || cur == GameData.BBedrock.Id) continue;

                    // 1. Cheese Caves (Просторные гроты и залы)
                    float cheese = _caveCheese.Fractal(wx * 0.012f, wy * 0.018f + 100f, wz * 0.012f, 3, 0.5f);
                    bool isCheese = (wy < 36 ? cheese > 0.50f : cheese > 0.58f) && wy < surface - 3;

                    // 2. Spaghetti Caves (Узкие извилистые туннели)
                    float sp1 = _caveSpaghetti1.Get(wx * 0.022f + wy * 0.014f, wz * 0.022f);
                    float sp2 = _caveSpaghetti2.Get(wx * 0.022f, wz * 0.022f + wy * 0.014f + 500f);
                    bool isSpaghetti = (sp1 * sp1 + sp2 * sp2) < 0.018f && wy < surface - 2;

                    // 3. Noodle Caves (Узкие вертикальные расщелины)
                    float noodle = _caveNoodle.Get(wx * 0.015f + 2000f, wz * 0.015f + wy * 0.028f);
                    bool isNoodle = MathF.Abs(noodle) < 0.018f && wy > 6 && wy < surface - 2;

                    // 4. Каньон / Разлом (глубокий вертикальный разрез)
                    bool inRavine = isRavine && wy >= 9 && wy <= surface - 4;

                    // Выходы пещер и разломов на поверхность
                    bool surfaceBreach = wy >= surface - 3 && (isSpaghetti || isNoodle || inRavine) && cheese > 0.48f;

                    if (isCheese || isSpaghetti || isNoodle || inRavine || surfaceBreach) {
                        ushort replaceWith;
                        if (wy <= 8) {
                            replaceWith = GameData.BLava.Id; // Подземные лавовые озера на самом дне (Y <= 8)
                        } else if (wy <= SeaLevel && wy > surface - 4 && cur == GameData.BWater.Id) {
                            replaceWith = GameData.BWater.Id;
                        } else {
                            replaceWith = 0; // Чистый воздух в пещерах (без паразитного обсидиана)
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

                    // Уголь (Coal) — компактные сбалансированные жилы (3-8 блоков) на высотах Y=4..80
                    float coalN = _oreNoise.Fractal(wx * 0.20f, wy * 0.20f, wz * 0.20f, 2, 0.5f);
                    if (coalN > 0.77f) {
                        var v = MakeVoxel(GameData.BCoalOre.Id);
                        chunk.SetVoxel(idx, in v);
                        continue;
                    }

                    // Железо (Iron) — жилы на высотах Y=4..60
                    if (wy <= 60) {
                        float ironN = _ironNoise.Fractal(wx * 0.22f, wy * 0.22f + 300f, wz * 0.22f, 2, 0.5f);
                        if (ironN > 0.72f) {
                            var v = MakeVoxel(GameData.BIronOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }

                    // Золото (Gold) — жилы на глубине Y=2..34 (и до 65 в столовых горах/пустыне)
                    var biome = GetBiome(wx, BaseHeight, wz);
                    int maxGoldY = (biome == BiomeType.Desert) ? 65 : 34;
                    if (wy <= maxGoldY) {
                        float goldN = _goldNoise.Fractal(wx * 0.22f + 700f, wy * 0.22f, wz * 0.22f, 2, 0.5f);
                        if (goldN > 0.80f) {
                            var v = MakeVoxel(GameData.BGoldOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }

                    // Редстоун (Redstone) — жилы на глубине Y=1..20
                    if (wy <= 20) {
                        float redN = _oreNoise.Fractal(wx * 0.24f + 1200f, wy * 0.24f, wz * 0.24f, 2, 0.5f);
                        if (redN > 0.82f) {
                            var v = MakeVoxel(GameData.BRedstoneOre.Id);
                            chunk.SetVoxel(idx, in v);
                            continue;
                        }
                    }

                    // Алмазы (Diamond) — жилы на глубине Y=1..16 (чем глубже, тем больше)
                    if (wy <= 16) {
                        float diaN = _diamondNoise.Fractal(wx * 0.24f + 5000f, wy * 0.24f, wz * 0.24f, 2, 0.5f);
                        float threshold = wy <= 8 ? 0.79f : 0.82f;
                        if (diaN > threshold) {
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

                        var biome = GetBiome(wx, BaseHeight, wz);
                        if (biome == BiomeType.Plains) {
                            if (n < 0.88f) continue; // Редкие одиночные деревья на равнинах
                        } else if (biome == BiomeType.Savanna) {
                            if (n < 0.78f) continue; // Саванна: редкие раскидистые деревья
                        } else if (biome != BiomeType.Forest && biome != BiomeType.Swamp) {
                            continue; // Лес и болото — полная плотность деревьев
                        }

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
        // Размер сектора деревни: 14×14 чанков = 448×448 блоков (деревни стали реже)
        const int villageSectorChunks = 14;
        const int villageSectorBlocks = villageSectorChunks * Chunk.SizeX;

        int chunkX = ox / Chunk.SizeX;
        int chunkZ = oz / Chunk.SizeZ;

        // Определяем сектор деревни для этого чанка
        int sectorX = (int)MathF.Floor((float)chunkX / villageSectorChunks);
        int sectorZ = (int)MathF.Floor((float)chunkZ / villageSectorChunks);

        // Генерируем позицию деревни в секторе через шум
        var rng = new Random(_seed ^ (sectorX * 73856093) ^ (sectorZ * 19349663));
        float villageSeed = _mineshaftNoise.Get(sectorX * 31.73f + 123.456f, sectorZ * 31.73f + 654.321f);
        if (villageSeed < 0.30f) return; // ~30% секторов получают деревни

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
            float dist = rng.Next(10, 32);
            int houseX = villageWX + (int)(MathF.Cos(angle) * dist);
            int houseZ = villageWZ + (int)(MathF.Sin(angle) * dist);
            PlaceVillageHouse(chunk, ox, oz, houseX, houseZ, h, rng);

            // Фонарный столб возле дома
            int lampX = villageWX + (int)(MathF.Cos(angle) * (dist * 0.6f));
            int lampZ = villageWZ + (int)(MathF.Sin(angle) * (dist * 0.6f));
            PlaceVillageLampPost(chunk, ox, oz, lampX, lampZ);
        }

        // Дорога-гравий между домами (по центру)
        PlaceVillageRoad(chunk, ox, oz, villageWX, villageWZ, rng);
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

        SetVillageBlock(chunk, ox, oz, lampWX, surface, lampWZ, GameData.BCobblestone.Id);
        SetVillageBlock(chunk, ox, oz, lampWX, surface + 1, lampWZ, GameData.BLog.Id);
        SetVillageBlock(chunk, ox, oz, lampWX, surface + 2, lampWZ, GameData.BLog.Id);
        SetVillageBlock(chunk, ox, oz, lampWX, surface + 3, lampWZ, GameData.BLog.Id);
        SetVillageBlock(chunk, ox, oz, lampWX, surface + 3, lampWZ + 1, GameData.BTorch.Id, 4);
    }

    private void PlaceVillageHouse(Chunk chunk, int ox, int oz, int houseWX, int houseWZ, int houseIdx, Random rng) {
        int surface = SurfaceHeight(houseWX, houseWZ);
        if (surface < SeaLevel + 2) return;

        int houseType = houseIdx % 5; // 0 = Коттедж, 1 = Кузница, 2 = Ферма, 3 = Башня/Церковь, 4 = Хижина

        if (houseType == 1) {
            // ── 1. Кузница селения (Blacksmith Forge) 7x5 ──
            const int W = 7, D = 5, H = 4;
            for (int dz = 0; dz < D; dz++) {
                for (int dx = 0; dx < W; dx++) {
                    int wx = houseWX + dx - W / 2;
                    int wz = houseWZ + dz - D / 2;
                    int wy = surface;

                    for (int under = 0; under <= 3; under++)
                        SetVillageBlock(chunk, ox, oz, wx, wy - under, wz, GameData.BCobblestone.Id);

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
            return;
        }

        if (houseType == 2) {
            // ── 2. Фермерский дом (Farmhouse) + Большая грядка с пшеницей ──
            const int W = 6, D = 5, H = 4;
            for (int dz = 0; dz < D; dz++) {
                for (int dx = 0; dx < W; dx++) {
                    int wx = houseWX + dx - W / 2;
                    int wz = houseWZ + dz - D / 2;
                    int wy = surface;

                    for (int under = 0; under <= 3; under++)
                        SetVillageBlock(chunk, ox, oz, wx, wy - under, wz, GameData.BCobblestone.Id);

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
                            SetVillageBlock(chunk, ox, oz, gwx, gwy + 1, gwz, GameData.BWheatCrop.Id);
                        }
                    }
                }
            }
            return;
        }

        if (houseType == 3) {
            // ── 3. Сторожевая башня / Церковь (Watchtower / Church) 5x5x7 ──
            const int W = 5, D = 5, H = 7;
            for (int dz = 0; dz < D; dz++) {
                for (int dx = 0; dx < W; dx++) {
                    int wx = houseWX + dx - W / 2;
                    int wz = houseWZ + dz - D / 2;
                    int wy = surface;

                    for (int under = 0; under <= 3; under++)
                        SetVillageBlock(chunk, ox, oz, wx, wy - under, wz, GameData.BCobblestone.Id);

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
            return;
        }

        // ── 0 и 4. Жилой коттедж и Уютная хижина со стеклами, дверьми и кроватями ──
        int houseW = (houseType == 0) ? 6 : 5;
        int houseD = (houseType == 0) ? 6 : 5;
        const int houseH = 4;

        for (int dz = 0; dz < houseD; dz++) {
            for (int dx = 0; dx < houseW; dx++) {
                int wx = houseWX + dx - houseW / 2;
                int wz = houseWZ + dz - houseD / 2;
                int wy = surface;

                for (int under = 0; under <= 3; under++)
                    SetVillageBlock(chunk, ox, oz, wx, wy - under, wz, GameData.BCobblestone.Id);

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

        // Комната 7x7x5 с мшистым булыжником
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

                        if (isFortressCore || isSpawnerBalcony) {
                            if (wy == 46) {
                                // Пол моста / балкона
                                blockId = GameData.BNetherBrick.Id;
                            } else if (wy == 47) {
                                if (isSpawnerBalcony) {
                                    // Ограждение балкона
                                    if (Math.Abs(relX - 44) == 4 || Math.Abs(relZ) == 4) blockId = GameData.BNetherBrick.Id;
                                    else if (relX == 44 && relZ == 0) blockId = GameData.BNetherBrick.Id; // Постамент
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
                                else blockId = 0;
                            } else if (wy >= 49 && wy <= 53) {
                                blockId = 0; // Проход над мостом
                            } else if (wy < 46 && wy >= 31) {
                                // Массивные опорные колонны моста до лавы
                                bool isPillar = (isNorthSouthBridge && Math.Abs(relZ) % 18 <= 2 && Math.Abs(relX) <= 2) ||
                                                (isEastWestBridge && Math.Abs(relX) % 18 <= 2 && Math.Abs(relZ) <= 2) ||
                                                (isSpawnerBalcony && (Math.Abs(relX - 44) == 3 || Math.Abs(relZ) == 3));
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
    }

    /// <summary>
    /// Тайное измерение «Бездна»: парящая тёмная обсидиановая платформа в пустоте
    /// под Эндом. В центре — алтарь-врата, куда вставляется Ключ Бездны.
    /// Попасть сюда можно, выжив при падении в пустоту Энда.
    /// </summary>
    public void GenerateVoidChunk(Chunk chunk, int ox, int oy, int oz) {
        const int platformRadius = 26;
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                int wx = ox + lx, wz = oz + lz;
                float dist = MathF.Sqrt(wx * wx + wz * wz);
                for (int ly = 0; ly < Chunk.SizeY; ly++) {
                    int wy = oy + ly;
                    ushort blockId = 0;

                    // Тёмная платформа: сверху ровная, края сходят на конус в пустоту
                    if (dist <= platformRadius) {
                        float bottom = 14f + dist * 0.22f;
                        if (wy >= bottom && wy <= 20) {
                            blockId = (wy == 20) ? GameData.BEndStone.Id : GameData.BObsidian.Id;
                        }
                    }

                    // Алтарь-врата в центре платформы (постамент + светящиеся врата)
                    if (MathF.Abs(wx) <= 2 && MathF.Abs(wz) <= 2 && wy >= 21 && wy <= 22) blockId = GameData.BObsidian.Id;
                    if (wx == 0 && wz == 0 && wy == 23) blockId = GameData.BVoidGate.Id;

                    chunk.SetVoxel(Chunk.Index(lx, ly, lz), MakeVoxel(blockId));
                }
            }
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

