using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Освещение. Солнечный свет — строгая тень по карте поверхности (без
/// распространения). Блочный свет (факелы, костры) — BFS с затуханием 1 на
/// блок внутри чанка; через границы свет попадает из соседних чанков.
/// </summary>
public static class LightEngine {
    private static readonly Vec3i[] Dirs6 = {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    /// <summary>
    /// Солнечный свет: колонки с открытым небом получают 15, дальше свет
    /// распространяется BFS с затуханием 1 на блок — затекает в пещеры,
    /// под навесы и к подножию обрывов. Оптимизации: уровневые корзины
    /// (клетка получает финальное значение с первого визита), кэш соседних
    /// секций и «крыша» колонки из готовых карт Surface вместо обхода мира.
    /// </summary>
    private static readonly Queue<int>[] SunBuckets = CreateSunBuckets();
    private static readonly GameChunk?[] FaceNeighbors = new GameChunk?[6];
    private static readonly GameChunk?[] ColumnSections = new GameChunk?[6];

    private static Queue<int>[] CreateSunBuckets() {
        var b = new Queue<int>[14];
        for (int i = 0; i < b.Length; i++) b[i] = new Queue<int>();
        return b;
    }

    // Быстрые таблицы свойств блоков: GameData.GetBlock (словарь) в горячем
    // пути света вызывался сотни тысяч раз за один пересчёт — это и
    // просаживало FPS. Заменены на индекс по массиву.
    private static bool[]? _opaqueTable;
    private static byte[]? _lightLevelTable;

    private static bool BlocksLight(in VoxelData v) {
        if (v.TypeId == 0) return false;
        var t = _opaqueTable;
        if (t == null) {
            t = new bool[256];
            foreach (var b in GameData.Blocks) t[b.Id] = b.IsOpaque;
            _opaqueTable = t;
        }
        return t[v.TypeId];
    }

    private static byte EmittedLight(ushort typeId) {
        var t = _lightLevelTable;
        if (t == null) {
            t = new byte[256];
            foreach (var b in GameData.Blocks) t[b.Id] = b.LightLevel;
            _lightLevelTable = t;
        }
        return t[typeId];
    }

    // Диагностика: VF_LIGHT_PROFILE=1 включает усреднённые тайминги фаз
    // (вывод раз в 200 вызовов). В обычной игре выключен и ничего не стоит.
    private static readonly bool Profile = Environment.GetEnvironmentVariable("VF_LIGHT_PROFILE") == "1";
    private static double _pSky, _pSeed, _pBorder, _pSpread;
    private static int _profileCalls;
    private static readonly System.Diagnostics.Stopwatch? ProfSw = Profile ? new() : null;

    public static void RecomputeSun(GameChunk gc, GameWorld world) {
        Array.Clear(gc.SunLight);
        foreach (var q in SunBuckets) q.Clear();

        var coord = gc.Coord;
        for (int i = 0; i < 6; i++)
            FaceNeighbors[i] = world.TryGetChunk(new Vec3i(coord.X + Dirs6[i].X, coord.Y + Dirs6[i].Y, coord.Z + Dirs6[i].Z));
        // Секции вертикальной колонки: индекс s ↔ dy = s - 3 (диапазон -2..3).
        for (int s = 0; s < 6; s++)
            ColumnSections[s] = s == 3 ? gc : world.TryGetChunk(new Vec3i(coord.X, coord.Y + (s - 3), coord.Z));

        ProfSw?.Restart();

        int maxRoof = int.MinValue;
        // 1. Прямой свет по колонкам. Карты Surface секций всегда актуальны:
        //    каждая правка блока обновляет свою секцию через OnBlockChanged.
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                int roof = int.MinValue;
                for (int s = 0; s < 6; s++) {
                    var sec = ColumnSections[s];
                    if (sec == null) continue;
                    int top = sec.Surface[sec.SurfaceIndex(lx, lz)];
                    if (top != int.MinValue && top > roof) roof = top;
                }
                if (roof > maxRoof) maxRoof = roof;
                int baseY = coord.Y * Chunk.SizeY;
                for (int ly = Chunk.SizeY - 1; ly >= 0; ly--) {
                    var v = gc.Chunk.Get(lx, ly, lz);
                    if (BlocksLight(v)) break;
                    if (roof != int.MinValue && baseY + ly < roof) break;
                    gc.SunLight[Chunk.Index(lx, ly, lz)] = 15;
                }
            }
        }
        if (ProfSw != null) { _pSky += ProfSw.ElapsedTicks; ProfSw.Restart(); }

        // 2. Затравки: клетки неба рядом с затемнёнными прозрачными клетками.
        var frontier = SunBuckets[13];
        // Тёмных прозрачных клеток выше самой высокой крыши чанка не бывает,
        // значит и затравки искать там нечего — режем верх полосы сканирования.
        int bandTop = maxRoof == int.MinValue
            ? -1
            : Math.Min(Chunk.SizeY - 1, maxRoof - coord.Y * Chunk.SizeY + 1);
        for (int lx = 0; lx < Chunk.SizeX && bandTop >= 0; lx++)
            for (int lz = 0; lz < Chunk.SizeZ; lz++)
                for (int ly = 0; ly <= bandTop; ly++) {
                    if (gc.SunLight[Chunk.Index(lx, ly, lz)] != 15) continue;
                    foreach (var d in Dirs6) {
                        int nx = lx + d.X, ny = ly + d.Y, nz = lz + d.Z;
                        if (nx < 0 || nx >= Chunk.SizeX || ny < 0 || ny >= Chunk.SizeY || nz < 0 || nz >= Chunk.SizeZ) continue;
                        int nIdx = Chunk.Index(nx, ny, nz);
                        var nv = gc.Chunk.Get(nIdx);
                        if (BlocksLight(nv) || gc.SunLight[nIdx] >= 14) continue;
                        frontier.Enqueue(nIdx);
                    }
                }
        if (ProfSw != null) { _pSeed += ProfSw.ElapsedTicks; ProfSw.Restart(); }

        SeedSunBorders(gc);
        if (ProfSw != null) { _pBorder += ProfSw.ElapsedTicks; ProfSw.Restart(); }

        // 3. Спуск по корзинам: уровни обрабатываются от больших к меньшим,
        //    поэтому первое присвоение клетке — окончательное.
        for (int level = 14; level >= 2; level--) {
            var q = SunBuckets[level - 1];
            int next = level - 1;
            while (q.Count > 0) {
                int idx = q.Dequeue();
                if (gc.SunLight[idx] >= level) continue;
                gc.SunLight[idx] = (byte)level;

                // Index(x,y,z) = (x*32 + z)*32 + y ⇒ обратная распаковка сдвигами.
                int x = idx >> 10, z = (idx >> 5) & 31, y = idx & 31;
                foreach (var d in Dirs6) {
                    int nx = x + d.X, ny = y + d.Y, nz = z + d.Z;
                    if (nx < 0 || nx >= Chunk.SizeX || ny < 0 || ny >= Chunk.SizeY || nz < 0 || nz >= Chunk.SizeZ) continue;
                    int nIdx = Chunk.Index(nx, ny, nz);
                    if (gc.SunLight[nIdx] >= next) continue;
                    var nv = gc.Chunk.Get(nIdx);
                    if (BlocksLight(nv)) continue;
                    SunBuckets[next - 1].Enqueue(nIdx);
                }
            }
        }
        if (ProfSw != null) {
            _pSpread += ProfSw.ElapsedTicks;
            if (++_profileCalls >= 200) {
                double f = 1000.0 / System.Diagnostics.Stopwatch.Frequency / _profileCalls;
                Console.WriteLine($"[light-prof] sky={_pSky * f:F3}ms seed={_pSeed * f:F3}ms border={_pBorder * f:F3}ms spread={_pSpread * f:F3}ms");
                _pSky = _pSeed = _pBorder = _pSpread = 0;
                _profileCalls = 0;
            }
        }
    }
    /// <summary>Подмешивает солнечный свет соседних чанков на границах.</summary>
    private static void SeedSunBorders(GameChunk gc) {
        // Грань X-: вход через локальный x=0, чанк слева отдаёт свой x=31.
        for (int ly = 0; ly < Chunk.SizeY; ly++)
            for (int lz = 0; lz < Chunk.SizeZ; lz++)
                BorderSeed(FaceNeighbors[1], gc, 0, ly, lz, Chunk.Index(Chunk.SizeX - 1, ly, lz));

        // Грань X+: вход через x=31, чанк справа отдаёт x=0.
        for (int ly = 0; ly < Chunk.SizeY; ly++)
            for (int lz = 0; lz < Chunk.SizeZ; lz++)
                BorderSeed(FaceNeighbors[0], gc, Chunk.SizeX - 1, ly, lz, Chunk.Index(0, ly, lz));

        // Грань Y-: вход через y=0, секция ниже отдаёт y=31.
        for (int lx = 0; lx < Chunk.SizeX; lx++)
            for (int lz = 0; lz < Chunk.SizeZ; lz++)
                BorderSeed(FaceNeighbors[3], gc, lx, 0, lz, Chunk.Index(lx, Chunk.SizeY - 1, lz));

        // Грань Y+: вход через y=31, секция выше отдаёт y=0.
        for (int lx = 0; lx < Chunk.SizeX; lx++)
            for (int lz = 0; lz < Chunk.SizeZ; lz++)
                BorderSeed(FaceNeighbors[2], gc, lx, Chunk.SizeY - 1, lz, Chunk.Index(lx, 0, lz));

        // Грань Z-: вход через z=0, чанк сзади отдаёт z=31.
        for (int ly = 0; ly < Chunk.SizeY; ly++)
            for (int lx = 0; lx < Chunk.SizeX; lx++)
                BorderSeed(FaceNeighbors[5], gc, lx, ly, 0, Chunk.Index(lx, ly, Chunk.SizeZ - 1));

        // Грань Z+: вход через z=31, чанк спереди отдаёт z=0.
        for (int ly = 0; ly < Chunk.SizeY; ly++)
            for (int lx = 0; lx < Chunk.SizeX; lx++)
                BorderSeed(FaceNeighbors[4], gc, lx, ly, Chunk.SizeZ - 1, Chunk.Index(lx, ly, 0));
    }

    private static void BorderSeed(GameChunk? n, GameChunk gc, int lx, int ly, int lz, int nIdx) {
        if (n == null) return;   // нет соседа — не сеем, иначе белые стены по краю мира
        int incoming = n.SunLight[nIdx] - 1;
        if (incoming <= 1) return;
        int idx = Chunk.Index(lx, ly, lz);
        var v = gc.Chunk.Get(idx);
        if (BlocksLight(v)) return;
        if (incoming <= gc.SunLight[idx]) return;
        SunBuckets[incoming - 1].Enqueue(idx);
    }

    /// <summary>Пересчитывает блочный свет чанка (источники + свет соседей на границе).</summary>
    public static void RecomputeBlock(GameChunk gc, GameWorld world) {
        Array.Clear(gc.BlockLight);

        var queue = new Queue<(int X, int Y, int Z, byte Level)>();
        // Источники внутри чанка.
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int ly = 0; ly < Chunk.SizeY; ly++) {
                for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                    int idx = Chunk.Index(lx, ly, lz);
                    ushort type = gc.Chunk.Get(idx).TypeId;
                    if (type == 0) continue;
                    byte level = EmittedLight(type);
                    if (level > 0) {
                        gc.BlockLight[idx] = level;
                        queue.Enqueue((lx, ly, lz, level));
                    }
                }
            }
        }
        // Свет соседних чанков на границах (затухает на 1 при входе).
        int ox = gc.Coord.X * Chunk.SizeX, oy = gc.Coord.Y * Chunk.SizeY, oz = gc.Coord.Z * Chunk.SizeZ;
        for (int ly = 0; ly < Chunk.SizeY; ly++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                Seed(world, gc, queue, 0, ly, lz, new Vec3i(ox - 1, oy + ly, oz + lz));
                Seed(world, gc, queue, Chunk.SizeX - 1, ly, lz, new Vec3i(ox + Chunk.SizeX, oy + ly, oz + lz));
            }
        }
        for (int ly = 0; ly < Chunk.SizeY; ly++) {
            for (int lx = 0; lx < Chunk.SizeX; lx++) {
                Seed(world, gc, queue, lx, ly, 0, new Vec3i(ox + lx, oy + ly, oz - 1));
                Seed(world, gc, queue, lx, ly, Chunk.SizeZ - 1, new Vec3i(ox + lx, oy + ly, oz + Chunk.SizeZ));
            }
        }
        for (int lx = 0; lx < Chunk.SizeX; lx++) {
            for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                Seed(world, gc, queue, lx, 0, lz, new Vec3i(ox + lx, oy - 1, oz + lz));
                Seed(world, gc, queue, lx, Chunk.SizeY - 1, lz, new Vec3i(ox + lx, oy + Chunk.SizeY, oz + lz));
            }
        }

        // BFS с затуханием.
        while (queue.Count > 0) {
            var (x, y, z, level) = queue.Dequeue();
            if (level <= 1) continue;
            foreach (var d in Dirs6) {
                int nx = x + d.X, ny = y + d.Y, nz = z + d.Z;
                if (nx < 0 || nx >= Chunk.SizeX || ny < 0 || ny >= Chunk.SizeY || nz < 0 || nz >= Chunk.SizeZ) continue;
                int idx = Chunk.Index(nx, ny, nz);
                if (gc.BlockLight[idx] >= level - 1) continue;
                var v = gc.Chunk.Get(idx);
                if (BlocksLight(v)) continue; // свет не проходит
                gc.BlockLight[idx] = (byte)(level - 1);
                queue.Enqueue((nx, ny, nz, (byte)(level - 1)));
            }
        }
    }

    /// <summary>Подмешивает свет из соседнего чанка в приграничную ячейку.</summary>
    private static void Seed(GameWorld world, GameChunk gc, Queue<(int, int, int, byte)> queue,
                             int lx, int ly, int lz, Vec3i outsideWorld) {
        byte incoming = world.GetBlockLight(outsideWorld);
        if (incoming <= 1) return;
        int idx = Chunk.Index(lx, ly, lz);
        var v = gc.Chunk.Get(idx);
        if (BlocksLight(v)) return;
        if (incoming - 1 > gc.BlockLight[idx]) {
            gc.BlockLight[idx] = (byte)(incoming - 1);
            queue.Enqueue((lx, ly, lz, (byte)(incoming - 1)));
        }
    }
}
