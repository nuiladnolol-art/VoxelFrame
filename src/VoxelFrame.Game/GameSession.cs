using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

public enum UiState { Playing, Inventory, Crafting, Paused, Workbench, Furnace, Chest, Loading, Death }
public enum Dimension { Overworld, Nether }
public enum WeatherType { Clear, Rain, Thunder }

/// <summary>
/// Игровая сессия: мир + игрок + время суток + UI-состояние + сообщения.
/// Вся логика чистая (без Raylib) — работает в headless-режиме для тестов.
/// </summary>
public sealed class GameSession {
    public const float PhysicsTick = 1f / 20f;

    public GameWorld World = null!;
    public Player Player = null!;
    public DayNightCycle DayNight = null!;
    public Camera3D Camera;
    public UiState Ui;
    public bool Headless;

    public Dimension Dimension => World.Dimension;
    public WeatherType Weather = WeatherType.Clear;
    public float WeatherTimer = 300f;
    public float ThunderTimer = 0f;
    public float AmbientSoundTimer = 25f;
    public float MusicTimer = 180f;

    public GameWorld? NetherWorld;
    public GameWorld? OverworldWorld;

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
    private float _physicsAccumulator;

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
        Player.Position = World.GetSafeRespawnPosition(World.SpawnBlock);
        Ui = UiState.Playing;
        AddMessage("Вы возродились на точке спавна");
    }

    // ── Создание / загрузка ──────────────────────────────────────────────────

    public static GameSession NewGame(int seed, bool headless) {
        var session = new GameSession(headless) {
            World = new GameWorld(seed),
            DayNight = new DayNightCycle(),
            Player = new Player(),
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
                        session.World.PlacePlacedBlock(new Vec3i(dx, y, dz), GameData.BDirt, 1f);
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
        session.AddMessage($"Новый мир. Сид: {seed}");
        session.AddMessage("Соберите дерево: держите ЛКМ на бревне");
        return session;
    }

    // ── Тик ──────────────────────────────────────────────────────────────────

    public void SwitchDimension(Dimension targetDim) {
        if (World.Dimension == targetDim) return;
        if (targetDim == Dimension.Nether) {
            OverworldWorld = World;
            if (NetherWorld == null) {
                NetherWorld = new GameWorld(World.Seed ^ 0x1337BEEF) { Dimension = Dimension.Nether };
            }
            World = NetherWorld;
            var newPos = new Vector3(Player.Position.X / 8f, 50f, Player.Position.Z / 8f);
            World.EnsureLoadedAroundSync(newPos, 2);
            Player.Position = newPos;
            Player.Velocity = Vector3.Zero;
            AddMessage("Вы вошли в Нижний мир (Nether)!");
        } else {
            NetherWorld = World;
            World = OverworldWorld ?? new GameWorld(World.Seed) { Dimension = Dimension.Overworld };
            var newPos = new Vector3(Player.Position.X * 8f, 65f, Player.Position.Z * 8f);
            World.EnsureLoadedAroundSync(newPos, 2);
            Player.Position = newPos;
            Player.Velocity = Vector3.Zero;
            AddMessage("Вы вернулись в Обычный мир!");
        }
    }

    public void Tick(float dt, in PlayerInput input) {
        TotalPlaySeconds += dt;
        for (int i = 0; i < _messages.Count; i++)
            _messages[i] = (_messages[i].Text, _messages[i].Age + dt);
        _messages.RemoveAll(m => m.Age > 6f);

        if (Ui == UiState.Paused || Ui == UiState.Death) return;

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

            // Пещерный эмбиент в глубоких пещерах
            AmbientSoundTimer -= dt;
            if (AmbientSoundTimer <= 0f) {
                if (Player.Position.Y < 38f) {
                    SoundSystem.PlayCaveAmbiance();
                }
                AmbientSoundTimer = 35f + Random.Shared.NextSingle() * 40f;
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
            // Во время пика затемнения (1.0 секунда сна) переводим время на раннее утро 7:40 (TimeOfDay = 0.32f)
            if (SleepProgress >= 1.0f && DayNight.TimeOfDay != 0.32f) {
                DayNight.TimeOfDay = 0.32f;
            }
            if (SleepProgress >= 2.0f) {
                DayNight.TimeOfDay = 0.32f;
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

            // Minecraft Camera Hurt Tilt (наклон камеры при получении урона)
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
        } else {
            if (input.OpenInventory || input.OpenCrafting || input.Pause) {
                Screens.ReturnHeld(this);   // предмет «из руки» и предметы крафта вернуть в инвентарь
                Ui = UiState.Playing;
            }
        }

        World.EnsureLoadedAround(Player.Position);

        World.Tick(dt, Player);
        World.TickPickups(dt, Player);
        World.TickHostileMobs(dt, Player, this);
        World.ProcessSolverEvents();

        _physicsAccumulator += dt;
        while (_physicsAccumulator >= PhysicsTick) {
            _physicsAccumulator -= PhysicsTick;
            World.Physics.Tick(PhysicsTick);
            World.ProcessSolverEvents();
        }

        DayNight.Tick(dt);
        // Фактор неба — uniform шейдера, меши не пересобираются при смене дня.
    }

    // ── Смерть и возрождение ────────────────────────────────────────────────

    public void DiePlayer(string message = "Вы погибли!") {
        if (Ui == UiState.Death) return;
        Screens.ReturnHeld(this);
        // Дроп всех предметов из инвентаря на месте гибели с разлетом и задержкой подбора 2.5с
        var dropPos = Player.Position + new Vector3(0f, 0.5f, 0f);
        var rng = Random.Shared;
        for (int i = 0; i < Player.Inventory.Capacity; i++) {
            var slot = Player.Inventory.Slots[i];
            if (slot != null && slot.Value.Quantity > 0) {
                float angle = rng.NextSingle() * MathF.Tau;
                float speed = 1.2f + rng.NextSingle() * 2.0f;
                var pickup = new ItemPickup(slot.Value.Item.Definition, slot.Value.Quantity, dropPos) {
                    PickupDelay = 2.5f,
                    Velocity = new Vector3(MathF.Cos(angle) * speed, 3.5f + rng.NextSingle() * 2.0f, MathF.Sin(angle) * speed)
                };
                World.Pickups.Add(pickup);
            }
        }
        if (Player.OffhandItem != null && Player.OffhandCount > 0) {
            float angle = rng.NextSingle() * MathF.Tau;
            float speed = 1.2f + rng.NextSingle() * 2.0f;
            var pickup = new ItemPickup(Player.OffhandItem, Player.OffhandCount, dropPos) {
                PickupDelay = 2.5f,
                Velocity = new Vector3(MathF.Cos(angle) * speed, 3.5f + rng.NextSingle() * 2.0f, MathF.Sin(angle) * speed)
            };
            World.Pickups.Add(pickup);
            Player.OffhandItem = null;
            Player.OffhandCount = 0;
        }
        Player.Inventory.Clear();
        Player.Health = 0f;
        Player.Velocity = Vector3.Zero;
        Player.FireTicks = 0f;
        Ui = UiState.Death;
        AddMessage(message);
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
