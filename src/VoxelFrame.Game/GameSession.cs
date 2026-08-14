using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

public enum UiState { Playing, Inventory, Crafting, Paused, Workbench, Furnace, Loading }

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

    public Vec3i TargetBlock;
    public Vec3i PlaceCell;
    public Vec3i ActiveFurnacePos;
    public bool HasTarget;
    public float TotalPlaySeconds;

    // Очередь загрузки чанков (для экрана загрузки)
    private Queue<Vec3i>? _loadQueue;
    public int LoadTotal, LoadDone;
    public bool IsLoading => _loadQueue != null;

    private readonly List<(string Text, float Age)> _messages = new();
    private float _physicsAccumulator;

    public GameSession(bool headless) {
        Headless = headless;
        Camera = new Camera3D {
            FovY = 70f,
            Projection = CameraProjection.Perspective,
            Up = Vector3.UnitY,
        };
    }

    public IReadOnlyList<(string Text, float Age)> Messages => _messages;

    public void AddMessage(string text) {
        _messages.Insert(0, (text, 0f));
        if (_messages.Count > 5) _messages.RemoveAt(_messages.Count - 1);
    }

    public void RespawnPlayer() {
        Player.Health = 20f;
        Player.Velocity = Vector3.Zero;
        Player.Position = new Vector3(World.SpawnBlock.X + 0.5f, World.SpawnBlock.Y + 2.1f, World.SpawnBlock.Z + 0.5f);
        AddMessage("Вы возродились на точке спавна");
    }

    // ── Создание / загрузка ──────────────────────────────────────────────────

    public static GameSession NewGame(int seed, bool headless) {
        var session = new GameSession(headless) {
            World = new GameWorld(seed),
            DayNight = new DayNightCycle(),
            Player = new Player(),
        };
        // Спавн: площадка 3×3 на поверхности у (0,0).
        int target = session.World.Generator.SurfaceHeight(0, 0);
        session.World.EnsureLoadedAround(new Vector3(0.5f, target + 1.9f, 0.5f));
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

    public void Tick(float dt, in PlayerInput input) {
        TotalPlaySeconds += dt;
        for (int i = 0; i < _messages.Count; i++)
            _messages[i] = (_messages[i].Text, _messages[i].Age + dt);
        _messages.RemoveAll(m => m.Age > 6f);

        if (Ui == UiState.Paused) return;

        if (Ui == UiState.Playing) {
            Player.Update(dt, input, World, this);
            Camera.Position = Player.Eye + new Vector3(0f, Player.BobOffset, 0f);
            Camera.Target = Player.Eye + new Vector3(0f, Player.BobOffset, 0f) + Player.Forward;
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
        World.Tick(dt);
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
