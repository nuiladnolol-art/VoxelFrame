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
    public int MasterSeed;

    // Титры при выходе из Энда после победы над боссом
    public float CreditsTimer;

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

    // Сон и смена времени
    public bool IsSleeping;
    public float SleepProgress;
    public Vec3i BedPosition;

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

    public static GameSession NewGame(int seed, bool headless) {
        var session = new GameSession(headless) {
            World = new GameWorld(seed),
            DayNight = new DayNightCycle(),
            Player = new Player(),
            MasterSeed = seed,
        };
        // Спавн: площадка 3×3 на поверхности у (0,0). Мгновенная плавная загрузка без фризов
        int target = session.World.Generator.SurfaceHeight(0, 0);
        session.World.EnsureLoadedAroundSync(new Vector3(0.5f, target + 1.9f, 0.5f), 1);
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                int sh = session.World.Generator.SurfaceHeight(dx, dz);
                var w = new Vec3i(dx, 0, dz);
                if (sh > target) {
                    for (int y = target + 1; y <= sh; y++) {
                        var v = session.World.GetVoxel(new Vec3i(dx, y, dz));
                        if (v.TypeId != 0) session.World.RemoveBlock(new Vec3i(dx, y, dz));
                    }
                } else if (sh < target) {
                    for (int y = sh + 1; y <= target; y++)
                        session.World.PlacePlacedBlock(new Vec3i(dx, y, dz), GameData.BDirt);
                }
                // Крона дерева, попавшего в площадку, не должна висеть в воздухе.
                for (int y = target + 1; y <= target + 8; y++) {
                    var w2 = new Vec3i(dx, y, dz);
                    if (session.World.GetVoxel(w2).TypeId == GameData.BLeaves.Id)
                        session.World.RemoveBlock(w2);
                }
            }
        }
        session.World.SpawnBlock = new Vec3i(0, target, 0);
        session.Player.Position = new Vector3(0.5f, target + 1.9f, 0.5f);
        return session;
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
                NetherWorld = new GameWorld(World.Seed ^ 0x1337BEEF) { Dimension = Dimension.Nether };
            }
            World = NetherWorld;
            int targetX = (int)MathF.Floor(Player.Position.X / 8f);
            int targetZ = (int)MathF.Floor(Player.Position.Z / 8f);
            int targetY = 56;

            World.EnsureLoadedAroundSync(new Vector3(targetX, targetY, targetZ), 2);
            EnsureSafePortalPlatform(World, targetX, targetY, targetZ, GameData.BNetherPortal);

            Player.Position = new Vector3(targetX + 0.5f, targetY + 1.0f, targetZ + 0.5f);
            Player.Velocity = Vector3.Zero;
            Player.PortalTimer = -2.5f;
            AddMessage("Вы вошли в Нижний мир!");
            // Повелитель Ада встречает игрока при первом входе (роняет Адский артефакт)
            World.SpawnMiniBoss(HostileType.NetherLord, new Vector3(targetX + 6.5f, targetY + 1.0f, targetZ + 0.5f));
        } else if (targetDim == Dimension.End) {
            if (EndWorld == null) {
                EndWorld = new GameWorld(World.Seed ^ 0x2E1D0FF) { Dimension = Dimension.End };
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
            AddMessage("Вы вошли в Энд!");
        } else {
            bool endBossDefeated = fromDim == Dimension.End && World.EndBossDefeated;
            World = OverworldWorld ?? new GameWorld(World.Seed) { Dimension = Dimension.Overworld };

            if (fromDim == Dimension.End) {
                // Выход из Энда: просто возвращаем на точку спавна, БЕЗ портала в Обычном мире
                World.EnsureLoadedAroundSync(new Vector3(World.SpawnBlock.X, World.SpawnBlock.Y, World.SpawnBlock.Z), 2);
                Player.Position = World.GetSafeRespawnPosition(World.SpawnBlock);
                Player.Velocity = Vector3.Zero;
                Player.PortalTimer = -2.5f;
                AddMessage("Вы вернулись в Обычный мир!");
                // После победы над Слизнем Края — показываем титры
                if (endBossDefeated) {
                    Ui = UiState.Credits;
                    CreditsTimer = 32f;
                }
            } else {
                // Возврат из Нижнего мира: координаты портала (как в Minecraft)
                int targetX = (int)MathF.Floor(Player.Position.X * 8f);
                int targetZ = (int)MathF.Floor(Player.Position.Z * 8f);

                World.EnsureLoadedAroundSync(new Vector3(targetX, 64f, targetZ), 2);
                int surfaceY = World.Generator.SurfaceHeight(targetX, targetZ);
                if (surfaceY <= 0) surfaceY = 64;
                int targetY = surfaceY + 1;

                EnsureSafePortalPlatform(World, targetX, targetY, targetZ, GameData.BNetherPortal);

                Player.Position = new Vector3(targetX + 0.5f, targetY + 1.0f, targetZ + 0.5f);
                Player.Velocity = Vector3.Zero;
                Player.PortalTimer = -2.5f;
                AddMessage("Вы вернулись в Обычный мир!");
            }
        }
    }

    /// <summary>Переход в Бездну (тайное измерение под Эндом) при выживании в пустоте.</summary>
    public void EnterVoid() {
        if (VoidWorld == null) {
            VoidWorld = new GameWorld(World.Seed ^ 0x4E19D2B) { Dimension = Dimension.Void };
        }
        World = VoidWorld;
        const int platformY = 21;
        World.EnsureLoadedAroundSync(new Vector3(0.5f, platformY, 0.5f), 2);
        Player.Position = new Vector3(0.5f, platformY + 0.5f, 0.5f);
        Player.Velocity = Vector3.Zero;
        Player.PortalTimer = -2.5f;
        AddMessage("Вы провалились в Бездну. В центре — Врата, ждущие Ключ...");
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

    /// <summary>Награда за открытие Врат Бездны: мощный лут.</summary>
    public void GiveVoidReward() {
        var inv = Player.Inventory;
        void Give(ItemDefinition def, int qty) { if (qty > 0) inv.TryInsert(GameData.NewItem(def), qty); }
        Give(GameData.DiamondItem, 12);
        Give(GameData.GoldIngotItem, 24);
        Give(GameData.TotemItem, 2);
        Give(GameData.EnchantedBookItem, 2);
        Give(GameData.GoldenAppleItem, 4);
        Give(GameData.ObsidianItem, 32);
        AddMessage("Врата Бездны открылись! Перед вами сокровище забытого измерения.");
        AddMessage("Вы познали тайну, скрытую под Эндом.");
    }

    private static void EnsureSafePortalPlatform(GameWorld world, int px, int py, int pz, BlockType portalBlock) {
        // Создаём надежную платформу 5х5 под ногами игрока
        for (int dx = -2; dx <= 2; dx++) {
            for (int dz = -2; dz <= 2; dz++) {
                world.PlacePlacedBlock(new Vec3i(px + dx, py - 1, pz + dz), GameData.BObsidian);
                for (int dy = 0; dy <= 4; dy++) {
                    world.RemoveBlock(new Vec3i(px + dx, py + dy, pz + dz));
                }
            }
        }
        // Строим 4x5 рамку портала из обсидиана с портальными блоками внутри
        for (int dx = -1; dx <= 2; dx++) {
            world.PlacePlacedBlock(new Vec3i(px + dx, py - 1, pz), GameData.BObsidian);
            world.PlacePlacedBlock(new Vec3i(px + dx, py + 3, pz), GameData.BObsidian);
        }
        for (int dy = 0; dy <= 2; dy++) {
            world.PlacePlacedBlock(new Vec3i(px - 1, py + dy, pz), GameData.BObsidian);
            world.PlacePlacedBlock(new Vec3i(px + 2, py + dy, pz), GameData.BObsidian);
            world.PlacePlacedBlock(new Vec3i(px, py + dy, pz), portalBlock);
            world.PlacePlacedBlock(new Vec3i(px + 1, py + dy, pz), portalBlock);
        }
    }

    /// <summary>Надёжная площадка для точки входа в Энд: обсидиановый пол без портала.</summary>
    private static void EnsureSafeEndSpawnPad(GameWorld world, int px, int py, int pz) {
        for (int dx = -2; dx <= 2; dx++) {
            for (int dz = -2; dz <= 2; dz++) {
                world.PlacePlacedBlock(new Vec3i(px + dx, py - 1, pz + dz), GameData.BObsidian);
                for (int dy = 0; dy <= 4; dy++) {
                    world.RemoveBlock(new Vec3i(px + dx, py + dy, pz + dz));
                }
            }
        }
    }

    public void Tick(float dt, in PlayerInput input) {
        TotalPlaySeconds += dt;
        for (int i = 0; i < _messages.Count; i++)
            _messages[i] = (_messages[i].Text, _messages[i].Age + dt);
        _messages.RemoveAll(m => m.Age > 6f);

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
            var shake = Player.ScreenShake > 0.001f
                ? new Vector3(
                    (Random.Shared.NextSingle() - 0.5f) * Player.ScreenShake,
                    (Random.Shared.NextSingle() - 0.5f) * Player.ScreenShake,
                    (Random.Shared.NextSingle() - 0.5f) * Player.ScreenShake)
                : Vector3.Zero;
            Camera.Position = Player.Eye + new Vector3(0f, Player.BobOffset, 0f) + shake;
            Camera.Target = Player.Eye + new Vector3(0f, Player.BobOffset, 0f) + Player.Forward + shake;
            Camera.FovY = 70f + Player.SprintFovProgress * 10f;

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
            else if (input.OpenCrafting) Ui = UiState.Crafting;
            else if (input.Pause) Ui = UiState.Paused;
        } else if (Ui != UiState.Chat && Ui != UiState.Death) {
            if (input.OpenInventory || input.OpenCrafting || input.Pause) {
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
            return;
        }

        string[] parts = cmd.Substring(1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return;

        string c = parts[0].ToLowerInvariant();
        switch (c) {
            case "gamemode":
            case "gm":
                if (parts.Length < 2) {
                    AddChatMessage("Использование: /gamemode <survival|creative|0|1|s|c>", Color.Yellow);
                    return;
                }
                string modeStr = parts[1].ToLowerInvariant();
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
                if (parts.Length < 3) {
                    AddChatMessage("Использование: /gamerule keepInventory <true|false>", Color.Yellow);
                    return;
                }
                if (parts[1].Equals("keepInventory", StringComparison.OrdinalIgnoreCase)) {
                    if (bool.TryParse(parts[2], out bool val)) {
                        KeepInventory = val;
                        AddChatMessage($"Игровое правило keepInventory установлено в {val}", Color.Green);
                    } else {
                        AddChatMessage("Значение должно быть true или false", Color.Red);
                    }
                } else {
                    AddChatMessage($"Неизвестное правило: {parts[1]}", Color.Red);
                }
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
                    AddChatMessage("Или: /locate structure <village|stronghold|portal|dungeon|pyramid|mineshaft>", Color.Yellow);
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

            case "help":
            case "?":
                AddChatMessage("=== Доступные команды ===", Color.Gold);
                AddChatMessage("/gamemode <survival|creative|s|c|0|1> — сменить режим", Color.White);
                AddChatMessage("/gamerule keepInventory <true|false> — сохранение инвентаря", Color.White);
                AddChatMessage("/time set <day|night|число> — сменить время", Color.White);
                AddChatMessage("/weather <clear|rain|thunder> — изменить погоду", Color.White);
                AddChatMessage("/locate <biome|structure> <название> — найти биом/структуру", Color.White);
                AddChatMessage("/clear — очистить инвентарь", Color.White);
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



