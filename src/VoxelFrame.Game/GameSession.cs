using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

public enum UiState { Playing, Inventory, Crafting, Paused, Workbench, Furnace, Chest, Loading, Death, Chat, Credits }
public enum Dimension { Overworld, Nether, End, Void }
public enum WeatherType { Clear, Rain, Thunder }
public enum GameMode { Survival, Creative }

/// <summary>
/// Игровая сессия: мир + игрок + время суток + UI-состояние + сообщения.
/// Вся логика чистая (без Raylib) — работает в headless-режиме для тестов.
/// </summary>
public sealed class GameSession {

    public GameWorld World = null!;
    public Player Player = null!;
    public DayNightCycle DayNight = null!;
    public Camera3D Camera;
    public UiState Ui;
    public bool Headless;

    public GameMode GameMode = GameMode.Survival;
    public bool KeepInventory = false;
    public bool CheatsEnabled = false;
    public int MasterSeed;

    // Титры при выходе из Энда / Бездны после победы над боссом
    public float CreditsTimer;
    public int CreditsType = 1; // 1 = End Slime (таинственный финал), 2 = True Void Slime (истинный триумф)
    /// <summary>После этих титров — сохранить игру и выйти в главное меню (истинный финал).</summary>
    public bool CreditsLeadToMenu;

    // Кинематографичные заголовки и субтитры (Cinema Titles)
    public string? CurrentTitle;
    public string? CurrentSubtitle;
    public float TitleTimer;
    public float TitleDuration = 4.5f;
    public Color TitleColor = Color.White;
    public Color SubtitleColor = Color.LightGray;

    public void ShowTitle(string title, string subtitle = "", float duration = 4.5f, Color? titleColor = null, Color? subColor = null) {
        CurrentTitle = title;
        CurrentSubtitle = subtitle;
        TitleDuration = MathF.Max(1.0f, duration);
        TitleTimer = TitleDuration;
        TitleColor = titleColor ?? new Color(255, 225, 120, 255);
        SubtitleColor = subColor ?? new Color(235, 235, 245, 255);
    }

    // Actionbar над хотбаром (статусные уведомления, трек пластинки и т.д.)
    public string? ActionbarText;
    public float ActionbarTimer;
    public float ActionbarDuration = 4.0f;
    public Color ActionbarColor = Color.White;

    public void ShowActionbar(string text, float duration = 4.0f, Color? color = null) {
        ActionbarText = text;
        ActionbarDuration = MathF.Max(3.0f, duration);
        ActionbarTimer = ActionbarDuration;
        ActionbarColor = color ?? new Color(255, 240, 100, 255);
    }

    // Чат и команды
    public string ChatInput = "";
    public readonly List<(string Text, Color Col, float Time)> ChatLog = new();

    public void AddChatMessage(string text, Color? col = null) {
        ChatLog.Add((text, col ?? Color.White, 12f));
        if (ChatLog.Count > 100) ChatLog.RemoveAt(0);
    }

    public Dimension Dimension => World.Dimension;
    public WeatherType Weather = WeatherType.Clear;
    public float WeatherTimer = 300f;
    public float ThunderTimer = 0f;
    public float AmbientSoundTimer = 25f;
    public float MusicTimer = 180f;

    // Автосохранение: интервал в секундах игрового времени
    public const float AutosaveInterval = 180f;
    public float AutosaveTimer = AutosaveInterval;

    public GameWorld? NetherWorld;
    public GameWorld? OverworldWorld;
    public GameWorld? EndWorld;
    public GameWorld? VoidWorld;   // Бездна — тайное измерение под Эндом (не сохраняется, генерируется заново)

    public Vec3i TargetBlock;
    public Vec3i PlaceCell;
    public Vec3i ActiveFurnacePos;
    public Vec3i ActiveChestPos;
    public bool HasTarget;
    public float TotalPlaySeconds;

    public bool IsSleeping;
    public float SleepProgress;
    public Vec3i BedPosition;

    public string LastDeathCause = "Неизвестная причина";
    public Vector3 LastDeathPos;

    private Vector3 _lastChunkLoadPos = new(float.MaxValue, 0, 0);

    // Очередь загрузки чанков (для экрана загрузки)
    private Queue<Vec3i>? _loadQueue;
    public int LoadTotal, LoadDone;
    public bool IsLoading => _loadQueue != null;

    private readonly List<(string Text, float Age)> _messages = new();

    public GameSession(bool headless) {
        Headless = headless;
        Camera = new Camera3D {
            Position = new Vector3(0f, 40f, 0f),
            Target = new Vector3(0f, 40f, 1f),
            Up = Vector3.UnitY,
            FovY = 70f,
            Projection = CameraProjection.Perspective,
        };
    }

    public IReadOnlyList<(string Text, float Age)> Messages => _messages;

    public void AddMessage(string text) {
        _messages.Insert(0, (text, 0f));
        if (_messages.Count > 5) _messages.RemoveAt(_messages.Count - 1);
    }

    public void StartSleep(Vec3i bedPos) {
        IsSleeping = true;
        SleepProgress = 0f;
        BedPosition = bedPos;
        World.SpawnBlock = bedPos;
    }

    public void RespawnPlayer() {
        if (World.Dimension != Dimension.Overworld) {
            World = OverworldWorld ?? new GameWorld(World.Seed) { Dimension = Dimension.Overworld };
        }
        Player.Health = Player.MaxHealth;
        Player.Hunger = Player.MaxHunger;
        Player.Saturation = 5f;
        Player.Exhaustion = 0f;
        Player.StarveTimer = 0f;
        Player.Velocity = Vector3.Zero;
        Player.HighestYInAir = 0f;
        Player.FireTicks = 0f;
        Player.AirSupply = 10f;
        Player.StuckTimer = 0f;
        Player.PortalTimer = 0f;

        var spawnBlock = BedPosition == default ? World.SpawnBlock : BedPosition;
        World.EnsureLoadedAroundSync(new Vector3(spawnBlock.X, spawnBlock.Y, spawnBlock.Z), 2);
        Player.Position = World.GetSafeRespawnPosition(spawnBlock);
        Ui = UiState.Playing;
        AddMessage("Вы возродились на точке спавна");
    }

    // ── Создание / загрузка ──────────────────────────────────────────────────

    public int CameraPerspective = 0; // 0 = 1st person, 1 = 3rd person back, 2 = 3rd person front

    public static GameSession NewGame(int seed, bool headless) {
        var session = new GameSession(headless) {
            World = new GameWorld(seed),
            DayNight = new DayNightCycle(),
            Player = new Player { Name = !string.IsNullOrEmpty(Screens.PlayerNick) ? Screens.PlayerNick : "Player" },
            MasterSeed = seed,
        };

        // Точка спавна зависит от сида: ищем сушу спиралью от (0,0). Чистый Перлин в узлах
        // решётки вырождается в константу, поэтому фикс-спавн (0,0) всегда давал одну и ту же
        // плоскую точку (Саванна, Y=42) в любом мире.
        Vec3i spawn = FindSpawnPoint(session.World.Generator, seed);

        // Спавн: площадка 3×3 на поверхности. Мгновенная плавная загрузка без фризов
        int target = session.World.Generator.SurfaceHeight(spawn.X, spawn.Z);
        session.World.EnsureLoadedAroundSync(new Vector3(spawn.X + 0.5f, target + 1.9f, spawn.Z + 0.5f), 1);
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                int wx = spawn.X + dx, wz = spawn.Z + dz;
                int sh = session.World.Generator.SurfaceHeight(wx, wz);
                var w = new Vec3i(wx, 0, wz);
                if (sh > target) {
                    for (int y = target + 1; y <= sh; y++) {
                        var v = session.World.GetVoxel(new Vec3i(wx, y, wz));
                        if (v.TypeId != 0) session.World.RemoveBlock(new Vec3i(wx, y, wz));
                    }
                } else if (sh < target) {
                    for (int y = sh + 1; y <= target; y++)
                        session.World.PlacePlacedBlock(new Vec3i(wx, y, wz), GameData.BDirt);
                }
                // Крона дерева, попавшего в площадку, не должна висеть в воздухе.
                for (int y = target + 1; y <= target + 8; y++) {
                    var w2 = new Vec3i(wx, y, wz);
                    if (session.World.GetVoxel(w2).TypeId == GameData.BLeaves.Id)
                        session.World.RemoveBlock(w2);
                }
            }
        }
        session.World.SpawnBlock = new Vec3i(spawn.X, target, spawn.Z);
        session.Player.Position = new Vector3(spawn.X + 0.5f, target + 1.9f, spawn.Z + 0.5f);
        return session;
    }

    /// <summary>Детерминированный поиск точки спавна по сиду: суша над уровнем моря,
    /// не вода, не пляж и не деревня. Стартуем в 1500–2600 блоках от (0,0) в сид-зависимом
    /// направлении: узел перлин-решётки в начале координат зажимает климат в линейный пандус
    /// (ячейка климата ~625 блоков), поэтому в радиусе пары сотен блоков биом в каждом мире
    /// был один и тот же (Саванна).</summary>
    private static Vec3i FindSpawnPoint(WorldGenerator gen, int seed) {
        var rng = new Random(seed ^ 0x5F3759DF);
        double angle = rng.NextDouble() * Math.Tau;
        int startR = rng.Next(1500, 2601);
        var center = new Vec3i(
            (int)MathF.Round((float)Math.Cos(angle) * startR),
            0,
            (int)MathF.Round((float)Math.Sin(angle) * startR));

        const int step = 13;
        for (int ring = 0; ring <= 120; ring++) {   // спираль вокруг стартовой точки, до ~1560 блоков
            for (int dx = -ring; dx <= ring; dx++) {
                for (int dz = -ring; dz <= ring; dz++) {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != ring) continue; // только периметр кольца
                    int wx = center.X + dx * step;
                    int wz = center.Z + dz * step;
                    if (IsGoodSpawnColumn(gen, wx, wz)) return new Vec3i(wx, 0, wz);
                }
            }
        }
        // Фоллбэк: старый центр (лучше, чем не найти ничего).
        return new Vec3i(0, 0, 0);
    }

    private static bool IsGoodSpawnColumn(WorldGenerator gen, int wx, int wz) {
        var biome = gen.GetBiome(wx, WorldGenerator.BaseHeight, wz);
        if (biome is BiomeType.Ocean or BiomeType.River or BiomeType.Beach or BiomeType.Mineshaft) return false;
        if (gen.IsInVillage(wx, wz)) return false; // площадка 3×3 не должна срезать угол дома
        int surface = gen.SurfaceHeight(wx, wz);
        if (surface <= WorldGenerator.SeaLevel + 1) return false; // почти уровень моря — может подтопить площадку
        return true;
    }

    // ── Тик ──────────────────────────────────────────────────────────────────

    public void SwitchDimension(Dimension targetDim) {
        if (World.Dimension == targetDim) return;
        var fromDim = World.Dimension;

        // Сохраняем текущий мир в соответствующее поле
        switch (World.Dimension) {
            case Dimension.Overworld: OverworldWorld = World; break;
            case Dimension.Nether: NetherWorld = World; break;
            case Dimension.End: EndWorld = World; break;
        }

        if (targetDim == Dimension.Nether) {
            if (NetherWorld == null) {
                NetherWorld = new GameWorld(MasterSeed ^ 0x1337BEEF) { Dimension = Dimension.Nether };
            }
            World = NetherWorld;
            int targetX = (int)MathF.Floor(Player.Position.X / 8f);
            int targetZ = (int)MathF.Floor(Player.Position.Z / 8f);
            int targetY = (int)MathF.Floor(Player.Position.Y);

            // Поиск существующего портала в радиусе 32 блоков
            var existingPortal = FindNearestPortal(World, targetX, targetY, targetZ, 32, GameData.BNetherPortal.Id);
            if (existingPortal.HasValue) {
                var p = existingPortal.Value;
                Player.Position = new Vector3(p.X + 0.5f, p.Y + 0.1f, p.Z + 0.5f);
            } else {
                var spawnPos = FindSafeNetherPortalSpawn(World, targetX, targetY, targetZ);
                EnsureSafePortalPlatform(World, spawnPos.X, spawnPos.Y, spawnPos.Z, GameData.BNetherPortal);
                Player.Position = new Vector3(spawnPos.X + 0.5f, spawnPos.Y + 1.0f, spawnPos.Z + 0.5f);
                World.SpawnMiniBoss(HostileType.NetherLord, new Vector3(spawnPos.X + 6.5f, spawnPos.Y + 1.0f, spawnPos.Z + 0.5f));
            }

            Player.Velocity = Vector3.Zero;
            Player.PortalTimer = -2.5f;
            Player.PortalLocked = true;
            AddMessage("Вы вошли в Нижний мир!");
        } else if (targetDim == Dimension.End) {
            if (EndWorld == null) {
                EndWorld = new GameWorld(MasterSeed ^ 0x2E1D0FF) { Dimension = Dimension.End };
            }
            World = EndWorld;
            // Парящая платформа у края главного острова (воздушная часть),
            // чтобы видеть плоскую арену с боссом и колоннами.
            int islandTop = World.Generator.EndSurfaceHeight(0, 0);
            if (islandTop <= 0) islandTop = 60;
            const int padX = 0, padZ = 65;   // чуть за краем острова (радиус 60)
            int targetY = islandTop + 1;

            World.EnsureLoadedAroundSync(new Vector3(padX, targetY, padZ), 2);
            // В Энде никакого портала на входе: выход откроется только после победы над Слизнем Края.
            EnsureSafeEndSpawnPad(World, padX, targetY, padZ);

            Player.Position = new Vector3(padX + 0.5f, targetY + 1.0f, padZ + 0.5f);
            Player.Velocity = Vector3.Zero;
            Player.PortalTimer = -2.5f;
            Player.PortalLocked = true;
            AddMessage("Вы вошли в Энд!");
        } else {
            bool endBossDefeated = fromDim == Dimension.End && World.EndBossDefeated;
            // Истинный финал: выход из Бездны после победы над Истинным Слизнем — титры и главное меню.
            bool trueVictory = fromDim == Dimension.Void && World.TrueVoidBossDefeated;
            World = OverworldWorld ?? new GameWorld(MasterSeed) { Dimension = Dimension.Overworld };

            if (trueVictory) {
                // Портал Триумфа уводит домой: финальные титры, затем главное меню.
                World.EnsureLoadedAroundSync(new Vector3(World.SpawnBlock.X, World.SpawnBlock.Y, World.SpawnBlock.Z), 2);
                Player.Position = World.GetSafeRespawnPosition(World.SpawnBlock);
                Player.Velocity = Vector3.Zero;
                Player.PortalTimer = -2.5f;
                Player.PortalLocked = true;
                AddMessage("Портал Триумфа переносит вас домой...");
                Ui = UiState.Credits;
                CreditsType = 2;
                CreditsLeadToMenu = true;
                CreditsTimer = 32f;
            } else if (fromDim == Dimension.End) {
                // Выход из Энда: просто возвращаем на точку спавна, БЕЗ портала в Обычном мире
                World.EnsureLoadedAroundSync(new Vector3(World.SpawnBlock.X, World.SpawnBlock.Y, World.SpawnBlock.Z), 2);
                Player.Position = World.GetSafeRespawnPosition(World.SpawnBlock);
                Player.Velocity = Vector3.Zero;
                Player.PortalTimer = -2.5f;
                Player.PortalLocked = true;
                AddMessage("Вы вернулись в Обычный мир!");
                // После победы над Слизнем Края — показываем титры
                if (endBossDefeated) {
                    Ui = UiState.Credits;
                    CreditsTimer = 32f;
                }
            } else {
                // Возврат из Нижнего мира: координаты портала (как в Minecraft X*8, Z*8)
                int targetX = (int)MathF.Floor(Player.Position.X * 8f);
                int targetZ = (int)MathF.Floor(Player.Position.Z * 8f);
                int targetY = (int)MathF.Floor(Player.Position.Y);

                // Поиск существующего портала в радиусе 128 блоков
                var existingPortal = FindNearestPortal(World, targetX, targetY, targetZ, 128, GameData.BNetherPortal.Id);
                if (existingPortal.HasValue) {
                    var p = existingPortal.Value;
                    Player.Position = new Vector3(p.X + 0.5f, p.Y + 0.1f, p.Z + 0.5f);
                } else {
                    World.EnsureLoadedAroundSync(new Vector3(targetX, 64f, targetZ), 2);
                    int surfaceY = World.Generator.SurfaceHeight(targetX, targetZ);
                    if (surfaceY <= 0) surfaceY = 64;
                    int createY = surfaceY + 1;

                    EnsureSafePortalPlatform(World, targetX, createY, targetZ, GameData.BNetherPortal);
                    Player.Position = new Vector3(targetX + 0.5f, createY + 1.0f, targetZ + 0.5f);
                }

                Player.Velocity = Vector3.Zero;
                Player.PortalTimer = -2.5f;
                Player.PortalLocked = true;
                AddMessage("Вы вернулись в Обычный мир!");
            }
        }
        Player.Dimension = World.Dimension;
    }

    public GameWorld GetWorld(Dimension dim) {
        if (dim == Dimension.Nether) {
            if (NetherWorld == null) NetherWorld = new GameWorld(MasterSeed ^ 0x1337BEEF) { Dimension = Dimension.Nether };
            return NetherWorld;
        }
        if (dim == Dimension.End) {
            if (EndWorld == null) EndWorld = new GameWorld(MasterSeed ^ 0x2E1D0FF) { Dimension = Dimension.End };
            return EndWorld;
        }
        if (dim == Dimension.Void) {
            if (VoidWorld == null) VoidWorld = new GameWorld(MasterSeed ^ 0x4E19D2B) { Dimension = Dimension.Void };
            return VoidWorld;
        }
        if (OverworldWorld == null && World.Dimension == Dimension.Overworld) return World;
        return OverworldWorld ?? World;
    }

    /// <summary>Открыт ли древний обелиск знаний на побочном острове Энда.</summary>
    public bool EndLoreDiscovered;

    /// <summary>Переход в Бездну (тайное измерение под Эндом) при выживании в пустоте.</summary>
    public void EnterVoid() {
        if (VoidWorld == null) {
            VoidWorld = new GameWorld(World.Seed ^ 0x4E19D2B) { Dimension = Dimension.Void };
        }
        World = VoidWorld;
        Player.Dimension = Dimension.Void;
        const int bedrockFloorY = 12;
        World.EnsureLoadedAroundSync(new Vector3(0.5f, bedrockFloorY, -14f), 2);
        Player.Position = new Vector3(0.5f, bedrockFloorY + 0.5f, -14f);
        Player.Velocity = Vector3.Zero;
        Player.PortalTimer = -2.5f;
        SoundSystem.PlayThunder();
        ShowTitle("БЕЗДНА", "Вы пробили пелену Пустоты и достигли Дна Реальности", 5.0f, new Color(180, 60, 255, 255), new Color(230, 210, 255, 255));
        AddMessage("§5Вы пробили смертоносную пелену Пустоты и упали на монолитный пол из бедрока!");
        AddMessage("§dВ полумраке перед вами возвышаются Врата Бездны и Алтарь...");
    }

    /// <summary>Возврат из Бездны в Энд.</summary>
    public void ReturnFromVoid() {
        World = EndWorld ?? new GameWorld(World.Seed) { Dimension = Dimension.End };
        var islandTop = World.Generator.EndSurfaceHeight(0, 0);
        if (islandTop <= 0) islandTop = 60;
        World.EnsureLoadedAroundSync(new Vector3(0.5f, islandTop + 2, 0.5f), 2);
        Player.Position = new Vector3(0.5f, islandTop + 2f, 0.5f);
        Player.Velocity = Vector3.Zero;
        Player.PortalTimer = -2.5f;
        AddMessage("Вы выбрались из Бездны обратно в Энд.");
    }

    /// <summary>Активация алтаря Бездны: ловушка, диалог и пробуждение Истинного Слизня Края.</summary>
    public void TriggerVoidAltarEncounter() {
        if (World.VoidAltarTriggered) return;
        World.VoidAltarTriggered = true;
        World.VoidBossIntroStep = 1;
        World.VoidBossIntroTimer = 0f;

        // Растворяем алтарь/врата во тьме с громом и частицами
        World.RemoveBlock(new Vec3i(0, 13, 0));
        World.RemoveBlock(new Vec3i(0, 12, 0));
        World.SpawnCrit(new Vector3(0.5f, 13f, 0.5f), 45);
        SoundSystem.PlayThunder();
        SoundSystem.PlayExplosion();

        ShowTitle("ВРАТА БЕЗДНЫ РАЗРУШЕНЫ", "«Ха-ха-ха... Ты правда думал, что победил меня наверху?..»", 4.0f, new Color(220, 60, 255, 255), new Color(255, 210, 255, 255));
        AddMessage("§dВы вставляете Ключ Бездны в алтарь...");
        AddMessage("§4Сокровищница растворяется во тьме! Пол из бедрока содрогается!");
        AddMessage("§5[Истинный Слизень]: Ха-ха-ха... Ты правда думал, что победил меня наверху, смертный?.. ");
    }

    public static Vec3i? FindNearestPortal(GameWorld world, int targetX, int targetY, int targetZ, int horizontalRadius, ushort portalBlockId) {
        int chunkRadius = (horizontalRadius / Chunk.SizeX) + 1;
        world.EnsureLoadedAroundSync(new Vector3(targetX, targetY, targetZ), chunkRadius);

        Vec3i? bestPos = null;
        float bestDistSq = float.MaxValue;

        int minChunkX = (targetX - horizontalRadius) >> 5;
        int maxChunkX = (targetX + horizontalRadius) >> 5;
        int minChunkZ = (targetZ - horizontalRadius) >> 5;
        int maxChunkZ = (targetZ + horizontalRadius) >> 5;

        for (int cx = minChunkX; cx <= maxChunkX; cx++) {
            for (int cz = minChunkZ; cz <= maxChunkZ; cz++) {
                for (int cy = 0; cy < 4; cy++) {
                    var cc = new Vec3i(cx, cy, cz);
                    var gc = world.TryGetChunk(cc);
                    if (gc == null) continue;

                    for (int lx = 0; lx < Chunk.SizeX; lx++) {
                        int wx = (cx << 5) + lx;
                        int dx = wx - targetX;
                        for (int lz = 0; lz < Chunk.SizeZ; lz++) {
                            int wz = (cz << 5) + lz;
                            int dz = wz - targetZ;
                            int distH2 = dx * dx + dz * dz;
                            if (distH2 > horizontalRadius * horizontalRadius) continue;

                            for (int ly = 0; ly < Chunk.SizeY; ly++) {
                                if (gc.Chunk.Get(lx, ly, lz).TypeId == portalBlockId) {
                                    int wy = (cy << 5) + ly;
                                    int dy = wy - targetY;
                                    float totalDistSq = distH2 + dy * dy;
                                    if (totalDistSq < bestDistSq) {
                                        bestDistSq = totalDistSq;
                                        bestPos = new Vec3i(wx, wy, wz);
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        if (bestPos.HasValue) {
            var p = bestPos.Value;
            while (p.Y > 1 && world.GetVoxel(new Vec3i(p.X, p.Y - 1, p.Z)).TypeId == portalBlockId) {
                p = new Vec3i(p.X, p.Y - 1, p.Z);
            }
            return p;
        }

        return null;
    }

    private static Vec3i FindSafeNetherPortalSpawn(GameWorld world, int targetX, int targetY, int targetZ) {
        int chunkRadius = 2;
        world.EnsureLoadedAroundSync(new Vector3(targetX, targetY, targetZ), chunkRadius);

        for (int r = 0; r <= 24; r += 4) {
            for (int dx = -r; dx <= r; dx += 4) {
                for (int dz = -r; dz <= r; dz += 4) {
                    int cx = targetX + dx;
                    int cz = targetZ + dz;
                    for (int y = 35; y <= 85; y++) {
                        if (world.IsSolidAt(new Vec3i(cx, y - 1, cz)) &&
                            !world.IsSolidAt(new Vec3i(cx, y, cz)) &&
                            !world.IsSolidAt(new Vec3i(cx, y + 1, cz)) &&
                            !world.IsSolidAt(new Vec3i(cx, y + 2, cz)) &&
                            world.GetVoxel(new Vec3i(cx, y, cz)).TypeId != GameData.BLava.Id) {
                            return new Vec3i(cx, y, cz);
                        }
                    }
                }
            }
        }
        return new Vec3i(targetX, 56, targetZ);
    }

    private static void EnsureSafePortalPlatform(GameWorld world, int px, int py, int pz, BlockType portalBlock) {
        // Создаём надежную платформу 5х5 под ногами игрока
        for (int dx = -2; dx <= 2; dx++) {
            for (int dz = -2; dz <= 2; dz++) {
                var floorPos = new Vec3i(px + dx, py - 1, pz + dz);
                world.PlacePlacedBlock(floorPos, GameData.BObsidian);
                SyncBlockChange(floorPos, GameData.BObsidian.Id, 0, false, (byte)world.Dimension);
                for (int dy = 0; dy <= 4; dy++) {
                    var airPos = new Vec3i(px + dx, py + dy, pz + dz);
                    world.RemoveBlock(airPos);
                    SyncBlockChange(airPos, 0, 0, true, (byte)world.Dimension);
                }
            }
        }
        // Строим 4x5 рамку портала из обсидиана с портальными блоками внутри
        for (int dx = -1; dx <= 2; dx++) {
            var b1 = new Vec3i(px + dx, py - 1, pz);
            var b2 = new Vec3i(px + dx, py + 3, pz);
            world.PlacePlacedBlock(b1, GameData.BObsidian);
            world.PlacePlacedBlock(b2, GameData.BObsidian);
            SyncBlockChange(b1, GameData.BObsidian.Id, 0, false, (byte)world.Dimension);
            SyncBlockChange(b2, GameData.BObsidian.Id, 0, false, (byte)world.Dimension);
        }
        for (int dy = 0; dy <= 2; dy++) {
            var side1 = new Vec3i(px - 1, py + dy, pz);
            var side2 = new Vec3i(px + 2, py + dy, pz);
            var port1 = new Vec3i(px, py + dy, pz);
            var port2 = new Vec3i(px + 1, py + dy, pz);
            world.PlacePlacedBlock(side1, GameData.BObsidian);
            world.PlacePlacedBlock(side2, GameData.BObsidian);
            world.PlacePlacedBlock(port1, portalBlock);
            world.PlacePlacedBlock(port2, portalBlock);
            SyncBlockChange(side1, GameData.BObsidian.Id, 0, false, (byte)world.Dimension);
            SyncBlockChange(side2, GameData.BObsidian.Id, 0, false, (byte)world.Dimension);
            SyncBlockChange(port1, portalBlock.Id, 0, false, (byte)world.Dimension);
            SyncBlockChange(port2, portalBlock.Id, 0, false, (byte)world.Dimension);
        }
    }

    private static void SyncBlockChange(Vec3i pos, ushort typeId, byte mask, bool isBreak, byte dimension) {
        GameServer.Active?.BroadcastHostBlockChange(pos.X, pos.Y, pos.Z, typeId, mask, isBreak);
        GameClient.Active?.SendBlockChange(pos.X, pos.Y, pos.Z, typeId, mask, isBreak, dimension);
    }

    /// <summary>Надёжная площадка для точки входа в Энд: обсидиановый пол без портала.</summary>
    private static void EnsureSafeEndSpawnPad(GameWorld world, int px, int py, int pz) {
        for (int dx = -2; dx <= 2; dx++) {
            for (int dz = -2; dz <= 2; dz++) {
                var floorPos = new Vec3i(px + dx, py - 1, pz + dz);
                world.PlacePlacedBlock(floorPos, GameData.BObsidian);
                SyncBlockChange(floorPos, GameData.BObsidian.Id, 0, false, (byte)world.Dimension);
                for (int dy = 0; dy <= 4; dy++) {
                    var airPos = new Vec3i(px + dx, py + dy, pz + dz);
                    world.RemoveBlock(airPos);
                    SyncBlockChange(airPos, 0, 0, true, (byte)world.Dimension);
                }
            }
        }
    }

    public void Tick(float dt, in PlayerInput input) {
        TotalPlaySeconds += dt;
        for (int i = 0; i < _messages.Count; i++)
            _messages[i] = (_messages[i].Text, _messages[i].Age + dt);
        _messages.RemoveAll(m => m.Age > 6f);
        if (TitleTimer > 0f) TitleTimer = MathF.Max(0f, TitleTimer - dt);
        if (ActionbarTimer > 0f) ActionbarTimer = MathF.Max(0f, ActionbarTimer - dt);

        if (Ui == UiState.Paused || Ui == UiState.Death || Ui == UiState.Credits) return;

        // Погодный цикл (дождь, гроза, ясная погода)
        if (World.Dimension == Dimension.Overworld) {
            WeatherTimer -= dt;
            if (WeatherTimer <= 0f) {
                Weather = Weather == WeatherType.Clear
                    ? (Random.Shared.NextDouble() < 0.35 ? WeatherType.Thunder : WeatherType.Rain)
                    : WeatherType.Clear;
                WeatherTimer = Weather == WeatherType.Clear ? 300f + Random.Shared.Next(300) : 180f + Random.Shared.Next(120);
                AddMessage(Weather == WeatherType.Thunder ? "Началась гроза!" : Weather == WeatherType.Rain ? "Пошел дождь..." : "Небо прояснилось.");
            }

            if (Weather == WeatherType.Thunder) {
                ThunderTimer -= dt;
                if (ThunderTimer <= 0f) {
                    SoundSystem.PlayThunder();
                    ThunderTimer = 14f + Random.Shared.NextSingle() * 22f;
                }
            }

            // Фоновая музыка на рассвете и закате
            MusicTimer -= dt;
            if (MusicTimer <= 0f) {
                SoundSystem.PlayBackgroundMusic();
                MusicTimer = 300f + Random.Shared.NextSingle() * 240f;
            }
        }

        if (IsSleeping) {
            SleepProgress += dt;
            // Во время пика затемнения (1.0 секунда сна) переводим время на рассвет 6:00 (TimeOfDay = 0.25f)
            if (SleepProgress >= 1.0f && DayNight.TimeOfDay != 0.25f) {
                DayNight.TimeOfDay = 0.25f;
                if (Weather != WeatherType.Clear) {
                    Weather = WeatherType.Clear;
                    WeatherTimer = 300f + Random.Shared.Next(300);
                }
            }
            if (SleepProgress >= 2.0f) {
                DayNight.TimeOfDay = 0.25f;
                IsSleeping = false;
                Player.Health = MathF.Min(Player.MaxHealth, Player.Health + 4f);
                AddMessage("Вы проснулись. Точка возрождения установлена.");
            }
        }

        if (Ui == UiState.Playing) {
            if (!IsSleeping) {
                Player.Update(dt, input, World, this);
            }

            if (Raylib.IsKeyPressed(KeyboardKey.F5)) {
                CameraPerspective = (CameraPerspective + 1) % 3;
                string modeName = CameraPerspective == 0 ? "Вид от первого лица" : CameraPerspective == 1 ? "Вид от третьего лица (сзади)" : "Вид от третьего лица (спереди)";
                AddMessage(modeName);
            }

            var shake = Player.ScreenShake > 0.001f
                ? new Vector3(
                    (Random.Shared.NextSingle() - 0.5f) * Player.ScreenShake,
                    (Random.Shared.NextSingle() - 0.5f) * Player.ScreenShake,
                    (Random.Shared.NextSingle() - 0.5f) * Player.ScreenShake)
                : Vector3.Zero;

            Vector3 eyePos = Player.Eye + new Vector3(0f, Player.BobOffset, 0f);
            if (CameraPerspective == 0) {
                Camera.Position = eyePos + shake;
                Camera.Target = eyePos + Player.Forward + shake;
            } else if (CameraPerspective == 1) {
                // Вид от 3-го лица сзади
                Vector3 backDir = -Player.Forward;
                Camera.Position = eyePos + backDir * 3.5f + shake;
                Camera.Target = eyePos + shake;
            } else {
                // Вид от 3-го лица спереди
                Vector3 frontDir = Player.Forward;
                Camera.Position = eyePos + frontDir * 3.5f + shake;
                Camera.Target = eyePos + shake;
            }
            Camera.FovY = SaveSystem.FovSetting + Player.SprintFovProgress * 10f;

            // Обновление координат слушателя звука для 3D аудио
            SoundSystem.ListenerPosition = Player.Eye;
            SoundSystem.ListenerForward = Player.Forward;
            var fwdH = new Vector3(Player.Forward.X, 0f, Player.Forward.Z);
            SoundSystem.ListenerRight = fwdH.LengthSquared() > 0.001f 
                ? Vector3.Normalize(new Vector3(-fwdH.Z, 0f, fwdH.X)) 
                : new Vector3(1f, 0f, 0f);

            // Наклон камеры при получении урона (Hurt Tilt)
            if (Player.HurtTimer > 0f) {
                float tiltFactor = MathF.Sin(Player.HurtTimer / 0.5f * MathF.PI);
                float tiltAngle = tiltFactor * (Player.HurtDirection * 0.12f); // ~7 градусов
                var rot = Matrix4x4.CreateFromAxisAngle(Vector3.Normalize(Player.Forward), tiltAngle);
                Camera.Up = Vector3.Transform(Vector3.UnitY, rot);
            } else {
                Camera.Up = Vector3.UnitY;
            }
            if (input.OpenInventory) Ui = UiState.Inventory;
            else if (input.Pause) Ui = UiState.Paused;
        } else if (Ui != UiState.Chat && Ui != UiState.Death) {
            if (input.OpenInventory || input.Pause) {
                Screens.ReturnHeld(this);   // предмет «из руки» и предметы крафта вернуть в инвентарь
                Ui = UiState.Playing;
            }
        }

        World.EnsureLoadedAround(Player.Position);

        World.Tick(dt, Player);
        World.TickPickups(dt, Player);
        World.TickHostileMobs(dt, Player, this);
        var endIslandTop = World.Generator.EndSurfaceHeight(0, 0);
        var endIslandCenter = new Vector3(0.5f, endIslandTop, 0.5f);
        World.TickEndCrystals(dt, Player, this);
        World.TickEndSlime(dt, Player, this, endIslandCenter, endIslandTop);
        World.TickTrueVoidBoss(dt, Player, this);

        DayNight.Tick(dt);
        // Фактор неба — uniform шейдера, меши не пересобираются при смене дня.

        for (int i = 0; i < ChatLog.Count; i++) {
            var item = ChatLog[i];
            if (item.Time > 0f) {
                ChatLog[i] = (item.Text, item.Col, item.Time - dt);
            }
        }
    }

    // ── Смерть и возрождение ────────────────────────────────────────────────

    public void DiePlayer(string message = "Вы погибли!") {
        if (Ui == UiState.Death) return;
        Screens.ReturnHeld(this);
        LastDeathPos = Player.Position;
        if (string.IsNullOrEmpty(LastDeathCause) || LastDeathCause == "Неизвестная причина") {
            LastDeathCause = message;
        }
        if (!KeepInventory) {
            // Дроп всех предметов из инвентаря на месте гибели с разлетом и задержкой подбора 2.5с
            var dropPos = Player.Position + new Vector3(0f, 0.5f, 0f);
            var rng = Random.Shared;
            for (int i = 0; i < Player.Inventory.Capacity; i++) {
                var slot = Player.Inventory.Slots[i];
                if (slot != null && slot.Value.Quantity > 0) {
                    float angle = rng.NextSingle() * MathF.Tau;
                    float speed = 1.2f + rng.NextSingle() * 2.0f;
                    var pickup = new ItemPickup(slot.Value.Item, slot.Value.Quantity, dropPos) {
                        PickupDelay = 2.5f,
                        Velocity = new Vector3(MathF.Cos(angle) * speed, 3.5f + rng.NextSingle() * 2.0f, MathF.Sin(angle) * speed)
                    };
                    World.Pickups.Add(pickup);
                }
            }
            if (Player.OffhandEntry != null) {
                float angle = rng.NextSingle() * MathF.Tau;
                float speed = 1.2f + rng.NextSingle() * 2.0f;
                var pickup = new ItemPickup(Player.OffhandEntry.Value.Item, Player.OffhandEntry.Value.Quantity, dropPos) {
                    PickupDelay = 2.5f,
                    Velocity = new Vector3(MathF.Cos(angle) * speed, 3.5f + rng.NextSingle() * 2.0f, MathF.Sin(angle) * speed)
                };
                World.Pickups.Add(pickup);
                Player.OffhandEntry = null;
            }
            // Дроп брони
            for (int i = 0; i < 4; i++) {
                if (Player.Armor[i] is { } ae && ae.Quantity > 0) {
                    float angle = rng.NextSingle() * MathF.Tau;
                    float speed = 1.2f + rng.NextSingle() * 2.0f;
                    var pickup = new ItemPickup(ae.Item, ae.Quantity, dropPos) {
                        PickupDelay = 2.5f,
                        Velocity = new Vector3(MathF.Cos(angle) * speed, 3.5f + rng.NextSingle() * 2.0f, MathF.Sin(angle) * speed)
                    };
                    World.Pickups.Add(pickup);
                    Player.Armor[i] = null;
                }
            }
            Player.Inventory.Clear();
        }
        Player.Health = 0f;
        Player.Velocity = Vector3.Zero;
        Player.FireTicks = 0f;
        Ui = UiState.Death;
        AddMessage(message);
    }

    public void ExecuteChatCommand(string cmd) {
        cmd = cmd.Trim();
        if (string.IsNullOrEmpty(cmd)) return;

        AddChatMessage("> " + cmd, new Color(220, 220, 220, 255));

        if (!cmd.StartsWith("/")) {
            GameClient.Active?.SendChatMessage(cmd);
            GameServer.Active?.BroadcastHostChat(Player.Name, cmd);
            return;
        }

        string[] parts = cmd.Substring(1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string c = parts[0].ToLowerInvariant();

        // Команды, доступные всегда без читов
        if (c is "help" or "?" or "seed") {
            if (c is "seed") {
                AddChatMessage($"Сид мира: {World.Seed}", Color.Gold);
                return;
            }
            AddChatMessage("=== Доступные команды ===", Color.Gold);
            AddChatMessage("/gamemode <survival|creative|s|c|0|1> — сменить режим", Color.White);
            AddChatMessage("/gamerule keepInventory <true|false> — сохранение инвентаря", Color.White);
            AddChatMessage("/give <предмет|id> [кол-во] — выдать предмет", Color.White);
            AddChatMessage("/tp <игрок> ИЛИ <x> <y> <z> — телепортация", Color.White);
            AddChatMessage("/time set <day|night|число> — сменить время суток", Color.White);
            AddChatMessage("/weather <clear|rain|thunder> — изменить погоду", Color.White);
            AddChatMessage("/locate <biome|structure> <название> — найти биом/структуру", Color.White);
            AddChatMessage("/kill — самоуничтожение", Color.White);
            AddChatMessage("/clear — очистить инвентарь", Color.White);
            AddChatMessage("/seed — узнать сид текущего мира", Color.White);
            return;
        }

        bool isClient = GameClient.Active != null;
        if (isClient && (c is "gamerule" or "keepinventory" or "time" or "weather")) {
            AddChatMessage("У вас нет прав администратора для изменения правил и параметров сервера.", Color.Red);
            return;
        }

        // Проверка прав на использование читов в этом мире
        if (!CheatsEnabled) {
            string hint = isClient ? "Читы отключены создателем сервера!" : "Читы отключены в этом мире! Включите их через Меню паузы (Esc) -> «Открыть для сети...»";
            AddChatMessage(hint, Color.Red);
            return;
        }

        switch (c) {
            case "gamemode":
            case "gm":
            case "gmc":
            case "gms":
                string modeStr = c == "gmc" ? "creative" : (c == "gms" ? "survival" : (parts.Length > 1 ? parts[1].ToLowerInvariant() : ""));
                if (string.IsNullOrEmpty(modeStr)) {
                    AddChatMessage("Использование: /gamemode <survival|creative|0|1|s|c>", Color.Yellow);
                    return;
                }
                if (modeStr is "creative" or "c" or "1") {
                    GameMode = GameMode.Creative;
                    Player.Health = Player.MaxHealth;
                    Player.Hunger = 20f;
                    AddChatMessage("Установлен режим игры: Творческий (Creative)", Color.Green);
                } else if (modeStr is "survival" or "s" or "0") {
                    GameMode = GameMode.Survival;
                    Player.IsFlying = false;
                    AddChatMessage("Установлен режим игры: Выживание (Survival)", Color.Green);
                } else {
                    AddChatMessage($"Неизвестный режим игры: {parts[1]}", Color.Red);
                }
                break;

            case "gamerule":
            case "keepinventory":
                if (c == "keepinventory") {
                    if (parts.Length >= 2 && bool.TryParse(parts[1], out bool kv)) {
                        KeepInventory = kv;
                        AddChatMessage($"Игровое правило keepInventory установлено в {kv}", Color.Green);
                    } else {
                        KeepInventory = !KeepInventory;
                        AddChatMessage($"Игровое правило keepInventory установлено в {KeepInventory}", Color.Green);
                    }
                    GameServer.Active?.BroadcastGameRuleSync("keepInventory", KeepInventory);
                    break;
                }
                if (parts.Length < 3) {
                    AddChatMessage("Использование: /gamerule keepInventory <true|false>", Color.Yellow);
                    return;
                }
                if (parts[1].Equals("keepInventory", StringComparison.OrdinalIgnoreCase)) {
                    if (bool.TryParse(parts[2], out bool val)) {
                        KeepInventory = val;
                        AddChatMessage($"Игровое правило keepInventory установлено в {val}", Color.Green);
                        GameServer.Active?.BroadcastGameRuleSync("keepInventory", KeepInventory);
                    } else {
                        AddChatMessage("Значение должно быть true или false", Color.Red);
                    }
                } else {
                    AddChatMessage($"Неизвестное правило: {parts[1]}", Color.Red);
                }
                break;

            case "give":
                if (parts.Length < 2) {
                    AddChatMessage("Использование: /give <предмет|id> [кол-во]", Color.Yellow);
                    return;
                }
                int count = 1;
                if (parts.Length >= 3 && int.TryParse(parts[2], out int cParsed)) {
                    count = Math.Clamp(cParsed, 1, 6400);
                }
                string query = parts[1].ToLowerInvariant().Replace("_", " ");
                ItemDefinition? foundDef = null;

                if (int.TryParse(parts[1], out int itemId) && GameData.Items.TryGetValue((ushort)itemId, out var itemById)) {
                    foundDef = itemById;
                } else {
                    foreach (var kvp in GameData.Items) {
                        if (kvp.Value.Name.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                            kvp.Value.Name.ToLowerInvariant().Contains(query)) {
                            foundDef = kvp.Value;
                            break;
                        }
                    }
                }

                if (foundDef == null) {
                    AddChatMessage($"Предмет не найден: {parts[1]}", Color.Red);
                } else {
                    int remaining = count;
                    while (remaining > 0) {
                        int stack = Math.Min(remaining, foundDef.MaxStack);
                        var itemInst = GameData.NewItem(foundDef);
                        if (!Player.Inventory.TryInsert(itemInst, stack)) {
                            World.Pickups.Add(new ItemPickup(itemInst, stack, Player.Position));
                        }
                        remaining -= stack;
                    }
                    AddChatMessage($"Выдано {count} шт. «{foundDef.Name}»", Color.Green);
                }
                break;

            case "tp":
            case "teleport":
                // Формат 1: /tp <targetPlayer> — телепортироваться к игроку
                if (parts.Length == 2) {
                    string targetName = parts[1];
                    Vector3? targetPos = null;

                    // Если мы хост сервера — ищем среди подключенных клиентов
                    if (GameServer.Active != null) {
                        foreach (var client in GameServer.Active.Clients) {
                            if (client.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase)) {
                                targetPos = client.Position;
                                break;
                            }
                        }
                    }
                    // Если мы клиент — ищем среди удаленных игроков
                    if (GameClient.Active != null) {
                        foreach (var rp in GameClient.Active.RemotePlayers) {
                            if (rp.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase)) {
                                targetPos = rp.Position;
                                break;
                            }
                        }
                    }

                    if (targetPos.HasValue) {
                        Player.Position = targetPos.Value;
                        Player.Velocity = Vector3.Zero;
                        AddChatMessage($"Телепортирован к игроку {targetName} (X:{targetPos.Value.X:F1} Y:{targetPos.Value.Y:F1} Z:{targetPos.Value.Z:F1})", Color.Green);
                    } else {
                        AddChatMessage($"Игрок '{targetName}' не найден в сети.", Color.Red);
                    }
                    return;
                }

                // Формат 2: /tp <who> <toWhom> — телепортировать одного игрока к другому (на сервере)
                if (parts.Length == 3) {
                    string who = parts[1];
                    string toWhom = parts[2];
                    Vector3? toPos = null;

                    if (toWhom.Equals(Player.Name, StringComparison.OrdinalIgnoreCase) || toWhom.Equals("host", StringComparison.OrdinalIgnoreCase) || toWhom.Equals("me", StringComparison.OrdinalIgnoreCase)) {
                        toPos = Player.Position;
                    } else if (GameServer.Active != null) {
                        foreach (var client in GameServer.Active.Clients) {
                            if (client.Name.Equals(toWhom, StringComparison.OrdinalIgnoreCase)) {
                                toPos = client.Position;
                                break;
                            }
                        }
                    }

                    if (!toPos.HasValue) {
                        AddChatMessage($"Целевой игрок '{toWhom}' не найден.", Color.Red);
                        return;
                    }

                    if (who.Equals(Player.Name, StringComparison.OrdinalIgnoreCase) || who.Equals("me", StringComparison.OrdinalIgnoreCase)) {
                        Player.Position = toPos.Value;
                        Player.Velocity = Vector3.Zero;
                        AddChatMessage($"Телепортирован к игроку {toWhom}", Color.Green);
                    } else if (GameServer.Active != null) {
                        if (GameServer.Active.TeleportClientByName(who, toPos.Value)) {
                            AddChatMessage($"Игрок {who} телепортирован к {toWhom}", Color.Green);
                        } else {
                            AddChatMessage($"Игрок '{who}' не найден.", Color.Red);
                        }
                    } else {
                        AddChatMessage("Телепортация других игроков доступна только хосту сервера.", Color.Red);
                    }
                    return;
                }

                // Формат 3: /tp <who> <x> <y> <z> — хост телепортирует игрока на координаты
                if (parts.Length == 5 && float.TryParse(parts[2], out float px) && float.TryParse(parts[3], out float py) && float.TryParse(parts[4], out float pz)) {
                    string who = parts[1];
                    var dest = new Vector3(px, py, pz);
                    if (who.Equals(Player.Name, StringComparison.OrdinalIgnoreCase) || who.Equals("me", StringComparison.OrdinalIgnoreCase)) {
                        Player.Position = dest;
                        Player.Velocity = Vector3.Zero;
                        AddChatMessage($"Телепортирован на координаты X:{px:F1} Y:{py:F1} Z:{pz:F1}", Color.Green);
                    } else if (GameServer.Active != null) {
                        if (GameServer.Active.TeleportClientByName(who, dest)) {
                            AddChatMessage($"Игрок {who} телепортирован на X:{px:F1} Y:{py:F1} Z:{pz:F1}", Color.Green);
                        } else {
                            AddChatMessage($"Игрок '{who}' не найден.", Color.Red);
                        }
                    }
                    return;
                }

                // Формат 4: /tp <x> <y> <z> — телепортация себя по координатам
                if (parts.Length < 4 || !float.TryParse(parts[1], out float tpx) ||
                    !float.TryParse(parts[2], out float tpy) || !float.TryParse(parts[3], out float tpz)) {
                    AddChatMessage("Использование: /tp <игрок> ИЛИ /tp <x> <y> <z> ИЛИ /tp <кто> <к_кому>", Color.Yellow);
                    return;
                }
                Player.Position = new Vector3(tpx, tpy, tpz);
                Player.Velocity = Vector3.Zero;
                AddChatMessage($"Телепортирован на координаты X:{tpx:F1} Y:{tpy:F1} Z:{tpz:F1}", Color.Green);
                break;

            case "kill":
                Player.Health = 0f;
                LastDeathCause = "пал жертвой консольной команды";
                DiePlayer("пал жертвой консольной команды");
                AddChatMessage("Игрок самоуничтожен", Color.Red);
                break;

            case "time":
                if (parts.Length >= 3 && parts[1].Equals("set", StringComparison.OrdinalIgnoreCase)) {
                    string t = parts[2].ToLowerInvariant();
                    if (t is "day" or "1000") {
                        DayNight.TimeOfDay = 0.35f;
                        AddChatMessage("Установлено время: День", Color.Green);
                    } else if (t is "night" or "13000") {
                        DayNight.TimeOfDay = 0.85f;
                        AddChatMessage("Установлено время: Ночь", Color.Green);
                    } else if (float.TryParse(parts[2], out float val)) {
                        DayNight.TimeOfDay = Math.Clamp(val / 24000f, 0f, 1f);
                        AddChatMessage($"Время установлено в {val}", Color.Green);
                    }
                } else {
                    AddChatMessage("Использование: /time set <day|night|число>", Color.Yellow);
                }
                break;

            case "weather":
                if (parts.Length >= 2) {
                    string w = parts[1].ToLowerInvariant();
                    if (w is "clear" or "sun") {
                        Weather = WeatherType.Clear;
                        WeatherTimer = 600f;
                        AddChatMessage("Установлена ясная погода", Color.Green);
                    } else if (w is "rain") {
                        Weather = WeatherType.Rain;
                        WeatherTimer = 600f;
                        AddChatMessage("Установлена дождливая погода", Color.Green);
                    } else if (w is "thunder") {
                        Weather = WeatherType.Thunder;
                        WeatherTimer = 600f;
                        AddChatMessage("Установлена грозовая погода", Color.Green);
                    }
                } else {
                    AddChatMessage("Использование: /weather <clear|rain|thunder>", Color.Yellow);
                }
                break;

            case "clear":
                Player.Inventory.Clear();
                Player.OffhandEntry = null;
                AddChatMessage("Инвентарь игрока очищен", Color.Green);
                break;

            case "locate":
                if (parts.Length < 3) {
                    AddChatMessage("Использование: /locate biome <savanna|swamp|desert|forest|plains|ocean|river|beach>", Color.Yellow);
                    AddChatMessage("Или: /locate structure <village|stronghold|portal|dungeon|pyramid|chapel|vault|mineshaft>", Color.Yellow);
                    break;
                }
                if (parts[1].Equals("biome", StringComparison.OrdinalIgnoreCase)) {
                    BiomeType? targetBiome = parts[2].ToLowerInvariant() switch {
                        "savanna" => BiomeType.Savanna,
                        "swamp" => BiomeType.Swamp,
                        "desert" => BiomeType.Desert,
                        "forest" or "woods" => BiomeType.Forest,
                        "plains" or "plain" => BiomeType.Plains,
                        "ocean" => BiomeType.Ocean,
                        "river" => BiomeType.River,
                        "beach" => BiomeType.Beach,
                        _ => null,
                    };
                    if (targetBiome == null) {
                        AddChatMessage($"Неизвестный биом: {parts[2]}", Color.Red);
                        break;
                    }
                    var biomePos = World.Generator.FindNearestBiome(Player.Position, targetBiome.Value);
                    if (biomePos == null) {
                        AddChatMessage("Биом не найден в радиусе поиска", Color.Red);
                    } else {
                        float dist = Vector3.Distance(Player.Position, biomePos.Value);
                        AddChatMessage($"Биом «{WorldGenerator.GetBiomeName(targetBiome.Value)}» найден: X={biomePos.Value.X:F0}, Y={biomePos.Value.Y:F0}, Z={biomePos.Value.Z:F0} (дистанция {dist:F0} блоков)", Color.Green);
                    }
                } else if (parts[1].Equals("structure", StringComparison.OrdinalIgnoreCase)) {
                    string sName = parts[2].ToLowerInvariant();
                    var structPos = World.Generator.FindNearestStructure(Player.Position, sName);
                    if (structPos == null) {
                        AddChatMessage("Структура не найдена в радиусе поиска", Color.Red);
                    } else {
                        float dist = Vector3.Distance(Player.Position, structPos.Value);
                        AddChatMessage($"Структура «{sName}» найдена: X={structPos.Value.X:F0}, Y={structPos.Value.Y:F0}, Z={structPos.Value.Z:F0} (дистанция {dist:F0} блоков)", Color.Green);
                    }
                } else {
                    AddChatMessage($"Неизвестный тип локации: {parts[1]}", Color.Red);
                }
                break;

            default:
                AddChatMessage($"Неизвестная команда: {parts[0]}. Введите /help для справки.", Color.Red);
                break;
        }
    }

    // ── Сохранение ───────────────────────────────────────────────────────────

    public void SaveTo(string path) => SaveSystem.Save(this, path);

    // ── Постепенная загрузка чанков ──────────────────────────────────────────

    /// <summary>Начинает постепенную загрузку чанков вокруг позиции.</summary>
    public void StartLoading(Vector3 position, int radius = GameWorld.RenderDistance) {
        Ui = UiState.Loading;
        var pc = Core.World.Chunk.CoordOf(new Core.Vec3i(
            (int)MathF.Floor(position.X),
            (int)MathF.Floor(position.Y),
            (int)MathF.Floor(position.Z)));
        _loadQueue = new Queue<Vec3i>();
        for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
                for (int dy = -1; dy <= 2; dy++)
                    _loadQueue.Enqueue(new Vec3i(pc.X + dx, pc.Y + dy, pc.Z + dz));
        LoadTotal = _loadQueue.Count;
        LoadDone = 0;
    }

    /// <summary>Загружает N чанков за кадр. Возвращает true когда загрузка завершена.</summary>
    public bool TickLoading(int chunksPerFrame = 8) {
        if (_loadQueue == null) return true;
        for (int i = 0; i < chunksPerFrame && _loadQueue.Count > 0; i++) {
            World.GetOrCreateChunk(_loadQueue.Dequeue());
            LoadDone++;
        }
        if (_loadQueue.Count == 0) {
            _loadQueue = null;
            Ui = UiState.Playing;
            return true;
        }
        return false;
    }
}



