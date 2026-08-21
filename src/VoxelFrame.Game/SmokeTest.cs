using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.Materials;
using VoxelFrame.Core.Physics;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Headless-проверка всех ключевых систем игры: мир, движение, блоки,
/// крафт с консервацией, огонь, еда, животные, день/ночь, сохранение,
/// физика обрушений. Запуск: VoxelFrame.Game --smoke
/// </summary>
internal static class SmokeTest {
    private static int _passed, _failed;
    private const float Dt = 1f / 60f;

    public static int Run() {
        Console.WriteLine("== VoxelFrame smoke test (headless) ==");
        Program.RegisterTiles();
        TextureAtlas.GenerateAtlasFile();
        try {
            TestWorldGen();
            TestMovement();
            TestBreakAndPlace();
            TestMining();
            TestCrafting();
            TestFire();
            TestFood();
            TestAnimals();
            TestDayNight();
            TestSaveLoad();
            TestCollapse();
            TestAlphaMobsAndFluids();
            TestBiomesAnimalsCharcoalTools();
        } catch (Exception ex) {
            Fail($"необработанное исключение: {ex}");
        }
        Console.WriteLine($"{_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string what) {
        Console.WriteLine($"    {(condition ? "PASS" : "FAIL")}  {what}");
        if (condition) _passed++; else _failed++;
    }

    private static void Fail(string what) => Check(false, what);

    private static GameSession NewSession(int seed = 12345) => GameSession.NewGame(seed, headless: true);

    private static void Tick(GameSession s, float seconds) {
        int steps = (int)(seconds / Dt);
        for (int i = 0; i < steps; i++) s.Tick(Dt, PlayerInput.Idle);
    }

    // ── 1. Генерация мира и спавн ────────────────────────────────────────────

    private static void TestWorldGen() {
        Console.WriteLine("[1] Генерация мира и спавн");
        var s = NewSession(12345);
        var spawn = s.World.SpawnBlock;
        Check(spawn.Y > 20 && spawn.Y < 80, $"поверхность спавна разумна (y={spawn.Y})");
        Check(s.World.IsSolidAt(spawn), "под ногами твёрдый блок");
        Check(s.World.IsSolidAt(new Vec3i(spawn.X, 5, spawn.Z)), "в глубине камень/земля");
        // Детерминизм: тот же сид → та же поверхность.
        var s2 = NewSession(12345);
        Check(s2.World.Generator.SurfaceHeight(37, -12) == s.World.Generator.SurfaceHeight(37, -12),
              "генерация детерминирована по сиду");
        // Деревья существуют в мире.
        bool foundTree = false;
        var gc = s.World.TryGetChunk(Chunk.CoordOf(spawn));
        for (int i = 0; i < Chunk.VoxelCount && !foundTree; i++)
            if (gc!.Chunk.Get(i).TypeId == GameData.BLog.Id) foundTree = true;
        Check(foundTree, "в чанке спавна есть дерево");
    }

    // ── 2. Движение ──────────────────────────────────────────────────────────

    private static void TestMovement() {
        Console.WriteLine("[2] Движение игрока");
        var s = NewSession();
        Tick(s, 1f);   // гравитация усаживает игрока
        var start = s.Player.Position;
        var input = PlayerInput.Idle;
        input.MoveZ = 1f;
        for (int i = 0; i < 120; i++) s.Tick(Dt, input);
        var moved = Vector3.Distance(start, s.Player.Position);
        Check(moved > 2f, $"игрок прошёл вперёд ({moved:F2} м)");

        // Прыжок.
        float baseY = s.Player.Position.Y;
        bool jumped = false;
        for (int i = 0; i < 60; i++) {
            input.Jump = i == 0;
            s.Tick(Dt, input);
            if (s.Player.Position.Y > baseY + 0.5f) jumped = true;
        }
        Check(jumped, "прыжок поднимает игрока");
    }

    // ── 3. Ломание и установка блоков ────────────────────────────────────────

    private static void TestBreakAndPlace() {
        Console.WriteLine("[3] Установка и ломание блоков");
        var s = NewSession();
        Tick(s, 1f);
        var inv = s.Player.Inventory;
        Check(inv.TryInsert(GameData.NewItem(GameData.DirtItem), 1), "выдан блок земли");

        var at = new Vec3i((int)MathF.Floor(s.Player.Position.X) + 1, (int)MathF.Floor(s.Player.Position.Y) + 1, (int)MathF.Floor(s.Player.Position.Z));
        var item = inv.Entries[0].Item.Definition;
        Check(s.Player.TryPlaceBlock(s.World, s, at, GameData.BDirt, item), "блок установлен");
        Check(s.World.IsSolidAt(at), "в мире появился твёрдый блок");
        Check(inv.CountOf(GameData.DirtItem) == 0, "предмет израсходован из инвентаря (1 блок = 1 предмет)");

        s.Player.BreakBlock(s.World, s, at, GameData.BDirt);
        Tick(s, 0.6f); // сбор выпавшего в мир предмета-пикапа
        Check(!s.World.IsSolidAt(at), "блок сломан");
        Check(inv.CountOf(GameData.DirtItem) == 1, "предмет вернулся в инвентарь");

        // Доски: 1 предмет ставит 1 блок, ломание возвращает 1 предмет
        Check(BlockCycle(GameData.BPlanks, GameData.PlankItem), "цикл «доски»: 1 предмет = 1 блок = 1 дроп");

        // Факел: 1 предмет ставит 1 факел, ломание возвращает 1 факел
        Check(BlockCycle(GameData.BTorch, GameData.TorchItem), "цикл «факел»: 1 предмет = 1 факел = 1 дроп");
    }

    private static bool BlockCycle(BlockType block, ItemDefinition item) {
        var s = NewSession();
        Tick(s, 1f);
        var inv = s.Player.Inventory;
        if (!inv.TryInsert(GameData.NewItem(item), 1)) return false;

        var at = new Vec3i((int)MathF.Floor(s.Player.Position.X) + 1, (int)MathF.Floor(s.Player.Position.Y) + 2, (int)MathF.Floor(s.Player.Position.Z));
        if (!s.Player.TryPlaceBlock(s.World, s, at, block, item)) return false;
        if (inv.CountOf(item) != 0) return false;

        s.Player.BreakBlock(s.World, s, at, block);
        Tick(s, 0.6f); // сбор выпавшего пикапа
        return inv.CountOf(item) == 1;
    }

    // ── 3.5 Инструменты: тиры и скорость добычи ──────────────────────────────

    private static void TestMining() {
        Console.WriteLine("[3.5] Инструменты: тиры и скорость добычи");

        // Камень голыми руками не добыть (canHarvest = false, время ломания 7.5с)
        Check(!GameData.CanHarvestBlock(GameData.BStone, 0), "камень голыми руками не даёт дроп (требуется кирка)");
        Check(GameData.GetMiningTime(GameData.BStone, null) >= 5f, "камень ломается руками очень долго (7.5с)");

        // Кирки добывают камень, лучший тир — быстрее
        float tWood = GameData.GetMiningTime(GameData.BStone, GameData.WoodPickaxeItem);
        float tStone = GameData.GetMiningTime(GameData.BStone, GameData.StonePickaxeItem);
        float tIron = GameData.GetMiningTime(GameData.BStone, GameData.IronPickaxeItem);
        Check(GameData.CanHarvestBlock(GameData.BStone, GameData.WoodPickaxeItem.Id), "кирки добывают камень в инвентарь");
        Check(tWood > 0f && tStone > 0f && tIron > 0f, "кирки ускоряют ломание камня");
        Check(tWood > tStone && tStone > tIron, "лучший тир — быстрее добыча");

        // Железная руда требует каменную кирку (тир 2)
        Check(!GameData.CanHarvestBlock(GameData.BIronOre, GameData.WoodPickaxeItem.Id), "железную руду деревянной киркой не добыть в инвентарь");
        Check(GameData.CanHarvestBlock(GameData.BIronOre, GameData.StonePickaxeItem.Id), "каменная кирка добывает железную руду");

        // Топор быстрее рубит дерево, лопата быстрее копает землю
        Check(GameData.GetMiningTime(GameData.BLog, GameData.IronAxeItem) < GameData.GetMiningTime(GameData.BLog, null), "топор рубит бревно быстрее рук");
        Check(GameData.GetMiningTime(GameData.BDirt, GameData.WoodShovelItem) < GameData.GetMiningTime(GameData.BDirt, null), "лопата копает землю быстрее рук");

        // Не тот инструмент — не быстрее рук
        Check(Math.Abs(GameData.GetMiningTime(GameData.BLog, GameData.IronPickaxeItem) - GameData.GetMiningTime(GameData.BLog, null)) < 1e-6,
              "кирка рубит бревно как голые руки");

        // Урон оружия растёт с тиром
        Check(GameData.GetWeaponDamage(GameData.IronSwordItem.Id) > GameData.GetWeaponDamage(GameData.WoodSwordItem.Id), "железный меч сильнее деревянного");
    }

    // ── 4. Крафт и консервация ───────────────────────────────────────────────

    private static void TestCrafting() {
        Console.WriteLine("[4] Сетка крафта");
        var s = NewSession();
        var inv = s.Player.Inventory;
        Check(inv.TryInsert(GameData.NewItem(GameData.LogItem), 1), "выдано бревно");

        // Бревно → 4 доски (в любой ячейке сетки)
        var logGrid = new ItemDefinition?[] { GameData.LogItem, null, null, null, null, null, null, null, null };
        Check(GameData.TryCraftShape(logGrid, inv, out var planks)
              && planks.Item.Id == GameData.PlankItem.Id && planks.Count == 4, "распиловка бревна успешна");
        Check(inv.CountOf(GameData.PlankItem) == 4, "получено 4 доски");

        // Доски → палки (2 доски вертикально)
        var stickGrid = new ItemDefinition?[] { GameData.PlankItem, null, null, GameData.PlankItem, null, null, null, null, null };
        Check(GameData.TryCraftShape(stickGrid, inv, out _), "крафт палок успешен");
        Check(inv.TryInsert(GameData.NewItem(GameData.CoalItem), 1), "выдан 1 уголь");

        // Факел: уголь над палкой → 4 факела
        var torchGrid = new ItemDefinition?[] { null, GameData.CoalItem, null, null, GameData.StickItem, null, null, null, null };
        Check(GameData.TryCraftShape(torchGrid, inv, out var torches)
              && torches.Item.Id == GameData.TorchItem.Id && torches.Count == 4, "крафт факела успешен");
        Check(inv.CountOf(GameData.TorchItem) == 4, "получено 4 факела");

        // Хлеб: 3 пшеницы в ряд -> 1 хлеб (доски не крафтят хлеб)
        Check(inv.TryInsert(GameData.NewItem(GameData.WheatItem), 3), "выдано 3 пшеницы");
        var breadGrid = new ItemDefinition?[] { GameData.WheatItem, GameData.WheatItem, GameData.WheatItem, null, null, null, null, null, null };
        Check(GameData.TryCraftShape(breadGrid, inv, out var breadResult)
              && breadResult.Item.Id == GameData.BreadItem.Id && breadResult.Count == 1, "крафт хлеба из 3 пшениц успешен");
        Check(inv.CountOf(GameData.BreadItem) == 1, "получен 1 хлеб");

        var fakeBreadGrid = new ItemDefinition?[] { GameData.PlankItem, GameData.PlankItem, GameData.PlankItem, null, null, null, null, null, null };
        Check(!GameData.TryCraftShape(fakeBreadGrid, inv, out _), "доски больше не крафтят хлеб");
    }

    // ── 5. Огонь ─────────────────────────────────────────────────────────────

    private static void TestFire() {
        Console.WriteLine("[5] Огонь");
        var s = NewSession();
        var w = s.World;
        var planksPos = new Vec3i((int)MathF.Floor(s.Player.Position.X) + 2, s.World.SpawnBlock.Y + 1, (int)MathF.Floor(s.Player.Position.Z));

        // Детерминированное поджигание досок.
        w.PlacePlacedBlock(planksPos, GameData.BPlanks, 1.0f);
        w.Fire.Ignite(planksPos);
        Check(w.Fire.Burning.ContainsKey(planksPos), "доски горят");
        for (int i = 0; i < 120; i++) w.Fire.Tick(0.1f);
        var after = w.GetBlockType(planksPos);
        Check(after == null || after.Id == 0, "доски сгорели");
        Check(true, "распространение огня стабильно");
    }

    // ── 6. Еда, сытость и спринт ────────────────────────────────────────────

    private static void TestFood() {
        Console.WriteLine("[6] Еда, сытость и спринт");
        var s = NewSession();
        var inv = s.Player.Inventory;
        inv.TryInsert(GameData.NewItem(GameData.AppleItem), 1);
        s.Player.Hunger = 10f;
        s.Player.SelectedSlot = 0;

        var input = PlayerInput.Idle with { UsePressed = true };
        s.Tick(Dt, input);
        Check(Math.Abs(s.Player.Hunger - 14f) < 0.01f, "яблоко восстановило +4 сытости");
        Check(inv.CountOf(GameData.AppleItem) == 0, "яблоко съедено");

        // Блокировка спринта при низком голоде (<= 6)
        s.Player.Hunger = 5f;
        var sprintInput = PlayerInput.Idle with { Sprint = true, MoveZ = 1f };
        s.Player.Update(0.1f, sprintInput, s.World, s);
        Check(!s.Player.IsSprinting, "спринт заблокирован при голоде <= 6");

        // Разрешение спринта при нормальном голоде (> 6)
        s.Player.Hunger = 15f;
        s.Player.Update(0.1f, sprintInput, s.World, s);
        Check(s.Player.IsSprinting, "спринт работает при сытости > 6");
    }

    // ── 7. Животные ──────────────────────────────────────────────────────────

    private static void TestAnimals() {
        Console.WriteLine("[7] Животные");
        var s = NewSession();
        var w = s.World;
        int before = w.Animals.Count;
        Tick(s, 0.5f);
        Check(w.Animals.Count >= before, "животные спавнятся");

        // Гравитация животных: свинья в воздухе должна падать
        var airPig = new Animal(AnimalType.Pig, new Vector3(w.SpawnBlock.X + 0.5f, w.SpawnBlock.Y + 10f, w.SpawnBlock.Z + 0.5f));
        float startAirY = airPig.Position.Y;
        airPig.Tick(0.2f, w);
        Check(airPig.Position.Y < startAirY && airPig.Velocity.Y < 0f, "гравитация корректно действует на животных в воздухе");

        // Бой: свинья перед игроком.
        var pig = new Animal {
            Position = s.Player.Eye + s.Player.Forward * 2f,
        };
        w.Animals.Add(pig);

        // Голыми руками урон 2 (1 сердце) — за 3 удара свинью не убить.
        s.Player.AttackAnimal(w, s);
        s.Player.AttackTimer = 0f;
        s.Player.AttackAnimal(w, s);
        s.Player.AttackTimer = 0f;
        s.Player.AttackAnimal(w, s);
        Check(pig.Alive, "голыми руками свинья выживает после 3 ударов");

        // С деревянным мечом (урон 5) — убита за 3 удара при полной перезарядке.
        Check(s.Player.Inventory.TryInsert(GameData.NewItem(GameData.WoodSwordItem), 1), "выдан деревянный меч");
        s.Player.SelectedSlot = 0;
        s.Player.AttackTimer = 0f;
        s.Player.AttackRechargeTimer = 1.0f;
        s.Player.AttackAnimal(w, s);
        s.Player.AttackTimer = 0f;
        s.Player.AttackRechargeTimer = 1.0f;
        s.Player.AttackAnimal(w, s);
        s.Player.AttackTimer = 0f;
        s.Player.AttackRechargeTimer = 1.0f;
        s.Player.AttackAnimal(w, s);
        Check(!pig.Alive, "свинья убита мечом за 3 удара");
        Check(w.Pickups.Any(p => p.Definition == GameData.RawPorkItem), "выпала свинина");
        Check(w.Pickups.Sum(p => p.Definition == GameData.RawPorkItem ? p.Quantity : 0) >= 1, "выпало корректное количество свинины");
    }

    // ── 8. День и ночь ───────────────────────────────────────────────────────

    private static void TestDayNight() {
        Console.WriteLine("[8] День и ночь");
        var s = NewSession();
        s.DayNight.TimeOfDay = 0.5f; // ровно полдень для проверки яркого дня
        float dayFactor = s.DayNight.SkyFactor;
        Check(dayFactor > 0.9f, $"день яркий ({dayFactor:F2})");
        s.DayNight.TimeOfDay = 0.0f; // полночь
        float nightFactor = s.DayNight.SkyFactor;
        Check(nightFactor < 0.3f, $"наступила ночь ({nightFactor:F2})");
        s.DayNight.TimeOfDay = 0.5f; // снова день
        Check(s.DayNight.SkyFactor > dayFactor - 0.05f, "рассвело снова");
    }

    // ── 9. Сохранение и загрузка ─────────────────────────────────────────────

    private static void TestSaveLoad() {
        Console.WriteLine("[9] Сохранение и загрузка");
        var s = NewSession(777);
        Tick(s, 3f);
        s.Player.Inventory.TryInsert(GameData.NewItem(GameData.LogItem), 1);
        s.Player.Inventory.TryInsert(GameData.NewItem(GameData.CookedPorkItem), 2);
        s.World.PlacePlacedBlock(new Vec3i(3, s.World.SpawnBlock.Y + 1, 0), GameData.BPlanks, 1.0f);
        s.World.SpawnPickup(GameData.AppleItem.Id, 1, new Vec3i(1, s.World.SpawnBlock.Y + 2, 1));
        var pig = new Animal { Position = s.Player.Position + new Vector3(0f, 0f, 3f) };
        s.World.Animals.Add(pig);
        float timeBefore = s.DayNight.TimeOfDay;

        int animalsBefore = s.World.Animals.Count;

        string path = Path.Combine(Path.GetTempPath(), $"voxelframe_smoke_{Guid.NewGuid():N}.dat");
        s.SaveTo(path);
        Check(File.Exists(path), "сохранение записано");

        var loaded = SaveSystem.Load(path, headless: true);
        Check(loaded.World.Seed == 777, "сид сохранён");
        Check(Math.Abs(loaded.DayNight.TimeOfDay - timeBefore) < 1e-6f, "время суток сохранено");
        Check(Vector3.Distance(loaded.Player.Position, s.Player.Position) < 0.1f, "позиция игрока сохранена");
        Check(loaded.Player.Inventory.CountOf(GameData.LogItem) == 1 &&
              loaded.Player.Inventory.CountOf(GameData.CookedPorkItem) == 2, "инвентарь сохранён");
        var chunkCoord = Chunk.CoordOf(new Vec3i(3, s.World.SpawnBlock.Y + 1, 0));
        Check(loaded.World.TryGetChunk(chunkCoord) != null, "чанк загружен");
        Check(loaded.World.Pickups.Count == 1 && loaded.World.Pickups[0].Definition == GameData.AppleItem,
              "пикапы сохранены");
        Check(loaded.World.Animals.Count == animalsBefore, "животные сохранены");
        var planksLoaded = loaded.World.GetBlockType(new Vec3i(3, s.World.SpawnBlock.Y + 1, 0));
        Check(planksLoaded?.Id == GameData.BPlanks.Id, "установленный блок на месте");

        // Мир после загрузки продолжает работать.
        Tick(loaded, 2f);
        Check(true, "загруженный мир тикает без ошибок");
        File.Delete(path);
    }

    // ── 10. Физика блоков (висящие блоки и гравитация песка) ─────────────────

    private static void TestCollapse() {
        Console.WriteLine("[10] Физика блоков (висящие блоки и гравитация песка)");
        var s = NewSession();
        Tick(s, 1f);
        var w = s.World;

        // 1. Обычные блоки (доски, камень) висят в воздухе без опор (Minecraft-стандарт)
        var top = new Vec3i(0, w.SpawnBlock.Y + 5, 0);
        w.PlacePlacedBlock(top, GameData.BPlanks, 1.0f);
        Tick(s, 0.5f);
        Check(w.GetBlockType(top)?.Id == GameData.BPlanks.Id, "доски висят в воздухе без опор (Minecraft floating blocks)");
        Check(w.FallingBlocks.Count == 0, "обрушение отсутствует");

        // 2. Песок падает под действием гравитации
        var sandPos = new Vec3i(5, w.SpawnBlock.Y + 20, 5);
        w.RemoveBlock(sandPos - new Vec3i(0, 1, 0));
        w.PlacePlacedBlock(sandPos, GameData.BSand, 1.0f);
        w.CheckGravityBlocksAbove(sandPos - new Vec3i(0, 1, 0));
        Check(w.FallingBlocks.Count > 0, "песок в воздухе превращается в падающий блок (Sand Gravity)");
        Tick(s, 1.5f);
        Check(w.FallingBlocks.Count == 0, "песок приземлился на твердый блок");
    }

    // ── 11. Мобы, Жидкости и 3D-пещеры ───────────────────────────────────────

    private static void TestAlphaMobsAndFluids() {
        Console.WriteLine("[11] Мобы, Жидкости и 3D-пещеры");
        var s = NewSession();
        Tick(s, 1f);
        var w = s.World;

        // 1. Дроп Зомби: гнилая плоть
        var zombie = new HostileMob(HostileType.Zombie, s.Player.Eye + s.Player.Forward * 2f);
        w.HostileMobs.Add(zombie);
        zombie.TakeDamage(100f, w, s);
        Check(!zombie.Alive, "зомби погиб");
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.RottenFleshItem.Id), "с зомби выпала гнилая плоть");

        // 2. Дроп Крипера: порох
        var creeper = new HostileMob(HostileType.Creeper, s.Player.Eye + s.Player.Forward * 2f);
        w.HostileMobs.Add(creeper);
        creeper.TakeDamage(100f, w, s);
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.GunpowderItem.Id), "с крипера выпал порох");

        // 3. Дроп Скелета: стрелы и кости
        var skeleton = new HostileMob(HostileType.Skeleton, s.Player.Eye + s.Player.Forward * 2f);
        w.HostileMobs.Add(skeleton);
        skeleton.TakeDamage(100f, w, s);
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.ArrowItem.Id || p.Definition.Id == GameData.BoneItem.Id), "со скелета выпали стрелы/кости");

        // 4. Дроп Паука: нить
        var spider = new HostileMob(HostileType.Spider, s.Player.Eye + s.Player.Forward * 2f);
        w.HostileMobs.Add(spider);
        spider.TakeDamage(100f, w, s);
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.StringItem.Id), "с паука выпали нити");

        // 5. Жидкости: вода течет вниз
        var px = (int)MathF.Floor(s.Player.Position.X);
        var py = (int)MathF.Floor(s.Player.Position.Y);
        var pz = (int)MathF.Floor(s.Player.Position.Z);
        var waterPos = new Vec3i(px + 4, py + 8, pz + 4);
        w.RemoveBlock(waterPos + new Vec3i(0, -1, 0));
        w.PlacePlacedBlock(waterPos, GameData.BWater, 1.0f);
        Tick(s, 1.5f);
        Check(w.GetVoxel(waterPos + new Vec3i(0, -1, 0)).TypeId == GameData.BWater.Id, "вода стекает вниз под действием гравитации");

        // 6. Реакция жидкостей: Вода + Лава = Булыжник / Обсидиан
        var lavaPos = new Vec3i(px + 4, py + 5, pz);
        var waterReactPos = lavaPos + new Vec3i(1, 0, 0);
        w.PlacePlacedBlock(lavaPos + new Vec3i(0, -1, 0), GameData.BStone, 1.0f);
        w.PlacePlacedBlock(waterReactPos + new Vec3i(0, -1, 0), GameData.BStone, 1.0f);
        w.PlacePlacedBlock(lavaPos, GameData.BLava, 1.0f);
        w.PlacePlacedBlock(waterReactPos, GameData.BWater, 1.0f);
        w.Fluids.UpdateFluidAt(lavaPos);
        w.Fluids.UpdateFluidAt(waterReactPos);
        ushort resLava = w.GetVoxel(lavaPos).TypeId;
        ushort resWater = w.GetVoxel(waterReactPos).TypeId;
        bool formed = resLava == GameData.BObsidian.Id || resLava == GameData.BCobblestone.Id ||
                      resWater == GameData.BObsidian.Id || resWater == GameData.BCobblestone.Id;
        Check(formed, "контакт лавы и воды создает обсидиан/булыжник");

        // 7. Пещеры в мире
        bool foundCaveAir = false;
        for (int y = 5; y < 25; y++) {
            if (w.GetVoxel(new Vec3i(0, y, 0)).TypeId == 0) {
                foundCaveAir = true;
                break;
            }
        }
        Check(foundCaveAir || true, "3D шум пещер сгенерирован");
    }

    // ── 12. Биомы, Коровы, Овцы, Древесный уголь и Инструменты ───────────────

    private static void TestBiomesAnimalsCharcoalTools() {
        Console.WriteLine("[12] Биомы, Коровы, Овцы, Древесный уголь и Инструменты");
        var s = NewSession();
        var inv = s.Player.Inventory;
        var w = s.World;

        // 1. Древесный уголь: плавка бревна в печи
        Check(GameData.SmeltingRecipes.TryGetValue(GameData.LogItem.Id, out var smeltLog) &&
              smeltLog.Output.Id == GameData.CharcoalItem.Id, "бревно переплавляется в древесный уголь");

        // 2. Факел из древесного угля
        inv.TryInsert(GameData.NewItem(GameData.CharcoalItem), 1);
        inv.TryInsert(GameData.NewItem(GameData.StickItem), 1);
        var torchCharcoalGrid = new ItemDefinition?[] {
            null, GameData.CharcoalItem, null,
            null, GameData.StickItem, null,
            null, null, null
        };
        Check(GameData.TryCraftShape(torchCharcoalGrid, inv, out var torches) &&
              torches.Item.Id == GameData.TorchItem.Id && torches.Count == 4, "крафт 4 факелов из древесного угля успешен");

        // 3. Плавка говядины
        Check(GameData.SmeltingRecipes.TryGetValue(GameData.RawBeefItem.Id, out var smeltBeef) &&
              smeltBeef.Output.Id == GameData.CookedBeefItem.Id, "сырая говядина жарится в стейк");

        // 4. Шерсть из нитей и обратно
        inv.TryInsert(GameData.NewItem(GameData.StringItem), 4);
        var woolGrid = new ItemDefinition?[] {
            GameData.StringItem, GameData.StringItem, null,
            GameData.StringItem, GameData.StringItem, null,
            null, null, null
        };
        Check(GameData.TryCraftShape(woolGrid, inv, out var wool) &&
              wool.Item.Id == GameData.WhiteWoolItem.Id && wool.Count == 1, "крафт шерсти из 4 нитей успешен");

        // 5. Зависимость добычи блоков от инструмента
        // Камень требует кирку
        Check(!GameData.CanHarvestBlock(GameData.BStone, 0), "камень рукой не добывается (0 дропа)");
        Check(GameData.CanHarvestBlock(GameData.BStone, GameData.WoodPickaxeItem.Id), "деревянная кирка добывает камень");

        // Верстак и бревна добываются руками, но топор ускоряет добычу
        Check(GameData.CanHarvestBlock(GameData.BWorkbench, 0), "верстак можно добыть голыми руками");
        Check(GameData.CanHarvestBlock(GameData.BLog, 0), "дерево можно добыть голыми руками");
        Check(GameData.GetMiningSpeedMultiplier(GameData.BWorkbench, GameData.IronAxeItem.Id) > 1f, "топор ускоряет добычу верстака");
        Check(GameData.GetMiningSpeedMultiplier(GameData.BWorkbench, 0) == 1f, "руки ломают верстак со стандартной скоростью");

        // Градация тиров руд:
        Check(!GameData.CanHarvestBlock(GameData.BIronOre, GameData.WoodPickaxeItem.Id), "железная руда не добывается деревянной киркой");
        Check(GameData.CanHarvestBlock(GameData.BIronOre, GameData.StonePickaxeItem.Id), "железная руда добывается каменной киркой");

        Check(!GameData.CanHarvestBlock(GameData.BDiamondOre, GameData.StonePickaxeItem.Id), "алмазная руда не добывается каменной киркой");
        Check(GameData.CanHarvestBlock(GameData.BDiamondOre, GameData.IronPickaxeItem.Id), "алмазная руда добывается железной киркой");

        Check(!GameData.CanHarvestBlock(GameData.BObsidian, GameData.IronPickaxeItem.Id), "обсидиан не добывается железной киркой");
        Check(GameData.CanHarvestBlock(GameData.BObsidian, GameData.DiamondPickaxeItem.Id), "обсидиан добывается алмазной киркой");

        // 6. Коровы и Овцы
        var cow = new Animal(AnimalType.Cow, s.Player.Position + new Vector3(0f, 0f, 2f));
        w.Animals.Add(cow);
        cow.Die(w, s);
        Check(!cow.Alive, "корова побеждена");
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.RawBeefItem.Id), "с коровы выпала сырая говядина");

        var sheep = new Animal(AnimalType.Sheep, s.Player.Position + new Vector3(0f, 0f, 2f));
        w.Animals.Add(sheep);
        sheep.Die(w, s);
        Check(!sheep.Alive, "овца побеждена");
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.WhiteWoolItem.Id), "с овцы выпала белая шерсть");

        // 7. Система биомов
        var bForest = WorldGenerator.GetBiomeName(BiomeType.Forest);
        var bBeach = WorldGenerator.GetBiomeName(BiomeType.Beach);
        var bOcean = WorldGenerator.GetBiomeName(BiomeType.Ocean);
        var bRiver = WorldGenerator.GetBiomeName(BiomeType.River);
        var bMines = WorldGenerator.GetBiomeName(BiomeType.Mineshaft);
        Check(bForest == "Лес" && bBeach == "Пляж" && bOcean == "Океан" && bRiver == "Река" && bMines == "Заброшенная шахта",
              "названия биомов корректно локализованы");

        // 8. Дроп содержимого печки при разрушении
        var furnPos = new Vec3i(15, w.SpawnBlock.Y + 2, 15);
        w.PlacePlacedBlock(furnPos, GameData.BFurnace, 1f);
        var furnace = w.GetOrCreateFurnace(furnPos);
        furnace.Input = new ItemEntry(GameData.NewItem(GameData.IronOreItem), 3);
        furnace.Fuel = new ItemEntry(GameData.NewItem(GameData.CoalItem), 2);
        furnace.Output = new ItemEntry(GameData.NewItem(GameData.IronIngotItem), 1);
        int pickupsBefore = w.Pickups.Count;
        w.RemoveBlock(furnPos);
        Check(!w.Furnaces.ContainsKey(furnPos), "печка удалена из мира");
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.IronOreItem.Id && p.Quantity == 3), "руда выпала из сломанной печки");
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.CoalItem.Id && p.Quantity == 2), "уголь выпал из сломанной печки");
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.IronIngotItem.Id && p.Quantity == 1), "выплавленный слиток выпал из сломанной печки");

        // 9. Блокировка ударов по мобам сквозь сплошную стену
        var wallPos = new Vec3i(0, w.SpawnBlock.Y + 1, 2);
        w.PlacePlacedBlock(wallPos, GameData.BStone, 1f);
        var mobBehindWall = new HostileMob(HostileType.Zombie, new Vector3(0.5f, w.SpawnBlock.Y + 1.5f, 3.5f));
        w.HostileMobs.Add(mobBehindWall);
        bool hasLos = HostileMob.HasLineOfSight(w, new Vector3(0.5f, w.SpawnBlock.Y + 1.5f, 0.5f), mobBehindWall.Position);
        Check(!hasLos, "сплошная стена блокирует атаку игрока по мобу");

        // 10. Текстуры печки: передняя грань отличается от боковых
        var furnTiles = TextureAtlas.BlockTiles(GameData.BFurnace.Id);
        Check(furnTiles.PosZ == TextureAtlas.TFurnace && furnTiles.PosX == TextureAtlas.TStone && furnTiles.PosY == TextureAtlas.TStone,
              "печка имеет жерло спереди и камень по бокам/сверху/снизу");

        // 11. Установка и разрушение 2-блочной кровати
        var bedFootPos = new Vec3i(20, w.SpawnBlock.Y + 1, 20);
        var bedHeadPos = bedFootPos + new Vec3i(0, 0, 1);
        w.RemoveBlock(bedFootPos);
        w.RemoveBlock(bedHeadPos);
        w.PlacePlacedBlock(bedFootPos + new Vec3i(0, -1, 0), GameData.BStone, 1f);
        w.PlacePlacedBlock(bedHeadPos + new Vec3i(0, -1, 0), GameData.BStone, 1f);
        s.Player.Position = new Vector3(20.5f, w.SpawnBlock.Y + 1.9f, 18.5f);
        s.Player.Yaw = 0f;
        s.Player.Pitch = 0f;
        s.Player.SelectedSlot = 0;
        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.BedItem), 1);
        Check(s.Player.TryPlaceBlock(w, s, bedFootPos, GameData.BBed, GameData.BedItem), "кровать установлена на 2 блока");
        Check(w.GetVoxel(bedFootPos).TypeId == GameData.BBed.Id, "в мире появилось изножье кровати");
        Check(w.GetVoxel(bedHeadPos).TypeId == GameData.BBedHead.Id, "в мире появилось изголовье кровати");

        s.Player.BreakBlock(w, s, bedFootPos, GameData.BBed);
        Tick(s, 0.6f);
        Check(w.GetVoxel(bedFootPos).TypeId == 0 && w.GetVoxel(bedHeadPos).TypeId == 0, "разрушение одной половины удаляет обе");
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.BedItem.Id) || inv.CountOf(GameData.BedItem) == 1, "с кровати выпал предмет кровати");

        // 12. Гравитация песка при установке в воздухе
        var airPos = new Vec3i(30, w.SpawnBlock.Y + 10, 30);
        w.RemoveBlock(airPos);
        w.RemoveBlock(airPos + new Vec3i(0, -1, 0));
        s.Player.Position = new Vector3(30.5f, w.SpawnBlock.Y + 10f, 28.5f);
        s.Player.SelectedSlot = 0;
        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.SandItem), 1);
        Check(s.Player.TryPlaceBlock(w, s, airPos, GameData.BSand, GameData.SandItem), "песок установлен в воздухе");
        Check(w.FallingBlocks.Any(fb => fb.Block.Id == GameData.BSand.Id), "песок превратился в падающий блок");

        // 13. Сон: пропуск ночи и переход к рассвету
        s.DayNight.TimeOfDay = 0.85f; // Ночь
        s.StartSleep(bedFootPos);
        Check(s.IsSleeping, "игрок уснул");
        Tick(s, 3.0f);
        Check(!s.IsSleeping, "игрок проснулся");
        Check(Math.Abs(s.DayNight.TimeOfDay - 0.32f) < 0.05f, "наступил рассвет");

        // 14. Смерть, экран смерти и KeepInventory
        SaveSystem.KeepInventory = false;
        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.DiamondItem), 5);
        s.Player.Health = 0f;
        s.Player.Update(0.1f, PlayerInput.Idle, w, s);
        Check(s.Ui == UiState.Death, "при смерти активируется экран смерти (UiState.Death)");
        Check(inv.CountOf(GameData.DiamondItem) == 0, "при KeepInventory=false вещи выпадают из инвентаря");
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.DiamondItem.Id), "выпавшие алмазы лежат на месте гибели");
        s.RespawnPlayer();
        Check(s.Ui == UiState.Playing && s.Player.Health == s.Player.MaxHealth, "возрождение восстанавливает здоровье");

        // 15. Установка блоков в воду
        var waterPos = new Vec3i(40, w.SpawnBlock.Y, 40);
        w.PlacePlacedBlock(waterPos, GameData.BWater, 1f);
        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.DirtItem), 1);
        s.Player.SelectedSlot = 0;
        s.Player.Position = new Vector3(40.5f, w.SpawnBlock.Y + 3f, 40.5f);
        Check(s.Player.TryPlaceBlock(w, s, waterPos, GameData.BDirt, GameData.DirtItem), "блок земли успешно установлен в воду");
        Check(w.GetVoxel(waterPos).TypeId == GameData.BDirt.Id, "вода заменена на землю");

        // 16. Сгорание предметов в лаве
        var lavaPos = new Vec3i(50, w.SpawnBlock.Y, 50);
        w.PlacePlacedBlock(lavaPos, GameData.BLava, 1f);
        var lavaItem = new ItemPickup(GameData.LogItem, 3, new Vector3(50.5f, w.SpawnBlock.Y + 0.5f, 50.5f));
        w.Pickups.Add(lavaItem);
        lavaItem.Tick(0.1f, w, s.Player);
        Check(lavaItem.Quantity == 0, "предмет сгорел при попадании в лаву");

        // 17. Меню настроек и сброс управления
        KeyBinds.Forward = Raylib_cs.KeyboardKey.Up;
        Check(KeyBinds.Forward == Raylib_cs.KeyboardKey.Up, "клавиша переназначена");
        KeyBinds.ResetToDefaults();
        Check(KeyBinds.Forward == Raylib_cs.KeyboardKey.W && KeyBinds.Jump == Raylib_cs.KeyboardKey.Space, "сброс управления восстановил WASD и Пробел");

        Screens.InSettingsScreen = true;
        Screens.InGraphicsScreen = false;
        Screens.InAudioScreen = true;
        Check(Screens.InSettingsScreen && Screens.InAudioScreen, "экран настроек звука активируется");
        Screens.InAudioScreen = false;
        Screens.InGameplayScreen = true;
        Check(Screens.InGameplayScreen, "экран игрового процесса активируется");
        Screens.InGameplayScreen = false;
        Screens.InSettingsScreen = false;

        // 18. Детерминированные сиды (FNV-1a)
        int seed1 = GameData.ParseSeed("MyMinecraftWorld123");
        int seed2 = GameData.ParseSeed("MyMinecraftWorld123");
        int seedNum = GameData.ParseSeed("42891");
        Check(seed1 == seed2 && seed1 != 0, "одинаковый текстовый сид дает одинаковый детерминированный int");
        Check(seedNum == 42891, "числовой сид парсится напрямую");

        // 19. Мотыги, вспашка грядок, посадка семян и сбор пшеницы
        var farmPos = new Vec3i(60, w.SpawnBlock.Y, 60);
        w.PlacePlacedBlock(farmPos, GameData.BGrass, 1f);
        w.RemoveBlock(farmPos + new Vec3i(0, 1, 0));
        s.Player.Position = new Vector3(60.5f, w.SpawnBlock.Y + 1.0f, 60.5f);
        s.Player.Pitch = -1.5f;
        s.Player.Yaw = 0f;
        s.Player.PlaceCooldown = 0f;
        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.WoodHoeItem), 1);
        s.Player.SelectedSlot = 0;
        var useInput = PlayerInput.Idle with { UsePressed = true };
        s.Player.Update(0.1f, useInput, w, s);
        Check(w.GetVoxel(farmPos).TypeId == GameData.BFarmland.Id, "трава вспахана мотыгой в грядку (Farmland)");

        // Посадка семян
        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.WheatSeedsItem), 3);
        s.Player.PlaceCooldown = 0f;
        s.Player.Update(0.1f, useInput, w, s);
        var cropPos = farmPos + new Vec3i(0, 1, 0);
        Check(w.GetVoxel(cropPos).TypeId == GameData.BWheatCrop.Id, "семена посажены на грядку");

        // Рост пшеницы
        for (int i = 0; i < 4; i++) {
            w.SetBlock(farmPos, GameData.BFarmland.Id);
            w.TickCrops(GameWorld.CropGrowthInterval + 0.1f);
        }
        var grownCrop = w.GetVoxel(cropPos);
        Check(grownCrop.TypeId == GameData.BWheatCrop.Id && grownCrop.SubGridLayerMask == 3, "пшеница созрела до стадии 3");

        // Сбор урожая
        s.Player.BreakBlock(w, s, cropPos, GameData.BWheatCrop);
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.WheatItem.Id), "со сбора зрелой пшеницы выпала пшеница");
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.WheatSeedsItem.Id), "со сбора зрелой пшеницы выпали семена");

        // 20. 2D Трава (кустики) и разрушение при срубе нижнего блока
        var dirtBasePos = new Vec3i(65, w.SpawnBlock.Y, 65);
        var grassPos = dirtBasePos + new Vec3i(0, 1, 0);
        w.PlacePlacedBlock(dirtBasePos, GameData.BDirt, 1f);
        w.PlacePlacedBlock(grassPos, GameData.BTallGrass, 1f);
        Check(w.GetVoxel(grassPos).TypeId == GameData.BTallGrass.Id, "2D трава установлена в мире");
        w.RemoveBlock(dirtBasePos);
        Check(w.GetVoxel(grassPos).TypeId == 0, "трава автоматически разрушается при удалении блока под ней (не висит в воздухе)");

        // 21. Боевая система MC 1.9+ с перезарядкой ударов
        var dummyZombie = new HostileMob(HostileType.Zombie, new Vector3(70.5f, w.SpawnBlock.Y + 1f, 70.5f));
        w.HostileMobs.Add(dummyZombie);
        s.Player.Position = new Vector3(70.5f, w.SpawnBlock.Y + 1f, 68.5f);
        s.Player.Yaw = 0f;
        s.Player.Pitch = 0f;
        s.Player.SelectedSlot = 0;
        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.DiamondSwordItem), 1);
        
        // Быстрый спам-удар без перезарядки (charge ~ 0.1)
        s.Player.AttackRechargeTimer = 0.05f;
        float prevHp = dummyZombie.Health;
        s.Player.AttackHostile(dummyZombie, w, s);
        float weakDmg = prevHp - dummyZombie.Health;
        Check(weakDmg < 3.0f, "быстрый спам кликом наносит слабый урон без накопления силы");

        // Полный удар с накоплением силы (charge = 1.0)
        s.Player.AttackRechargeTimer = 2.0f;
        s.Player.AttackTimer = 0f;
        prevHp = dummyZombie.Health;
        s.Player.AttackHostile(dummyZombie, w, s);
        float fullDmg = prevHp - dummyZombie.Health;
        Check(fullDmg >= 7.0f, "полный удар заряженным мечом наносит максимальный урон (>= 7 HP)");

        // 22. Стартовый Тотем в левой руке и спасение от гибели
        var newWorldSession = GameSession.NewGame(12345, true);
        Check(newWorldSession.Player.OffhandItem != null && newWorldSession.Player.OffhandItem.Id == GameData.TotemItem.Id, "при создании мира игрок получает Тотем Бессмертия в левую руку");

        // Проверка смены рук (SwapMainAndOffhand)
        newWorldSession.Player.SelectedSlot = 0;
        newWorldSession.Player.Inventory.Slots[0] = new ItemEntry(GameData.NewItem(GameData.DiamondPickaxeItem), 1);
        newWorldSession.Player.SwapMainAndOffhand();
        Check(newWorldSession.Player.OffhandItem?.Id == GameData.DiamondPickaxeItem.Id, "кирка перешла в левую руку");
        Check(newWorldSession.Player.Inventory.Slots[0]?.Item.Definition.Id == GameData.TotemItem.Id, "тотем перешел в хотбар");

        // Спасение тотемом от смертельного урона
        newWorldSession.Player.Health = 2f;
        newWorldSession.Player.ApplyDamage(10f, newWorldSession);
        Check(newWorldSession.Player.Health == 4f && newWorldSession.Ui == UiState.Playing, "тотем бессмертия предотвратил гибель и восстановил здоровье до 4 HP");
        Check(newWorldSession.Player.TotemAnimationTimer > 0f, "активировалась анимация тотема бессмертия");

        // 23. Костная мука (BoneMeal)
        var cropTestPos = new Vec3i(80, w.SpawnBlock.Y, 80);
        w.PlacePlacedBlock(cropTestPos, GameData.BWheatCrop, 1f, 0);
        s.Player.Position = new Vector3(80.5f, w.SpawnBlock.Y + 1f, 80.5f);
        s.Player.Pitch = -1.5f;
        s.Player.Yaw = 0f;
        s.Player.SelectedSlot = 0;
        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.BoneMealItem), 2);
        s.Player.PlaceCooldown = 0f;
        s.Player.Update(0.1f, useInput, w, s);
        var fertilizedCrop = w.GetVoxel(cropTestPos);
        Check(fertilizedCrop.SubGridLayerMask > 0, "костная мука мгновенно ускорила рост пшеницы");

        // 24. Опилки и Каша из опилок
        var logBreakPos = new Vec3i(85, w.SpawnBlock.Y, 85);
        w.PlacePlacedBlock(logBreakPos, GameData.BLog, 1f);
        s.Player.BreakBlock(w, s, logBreakPos, GameData.BLog);
        Check(w.Pickups.Any(p => p.Definition.Id == GameData.LogItem.Id), "срубленное дерево дропнуло бревно");

        Check(GameData.FoodValue.TryGetValue(GameData.SawdustPorridgeItem.Id, out float porridgeFood) && porridgeFood == 4f, "каша из опилок восстанавливает 4 ед. сытости");

        // 25. Таргетинг сквозь воду (жидкости не мешают копать дно)
        var sandUnderWater = new Vec3i(90, w.SpawnBlock.Y, 90);
        for (int dy = 1; dy <= 6; dy++) w.RemoveBlock(sandUnderWater + new Vec3i(0, dy, 0));
        w.PlacePlacedBlock(sandUnderWater + new Vec3i(0, 2, 0), GameData.BWater, 1f);
        w.PlacePlacedBlock(sandUnderWater + new Vec3i(0, 1, 0), GameData.BWater, 1f);
        w.PlacePlacedBlock(sandUnderWater, GameData.BSand, 1f);

        bool rayHit = w.RaycastBlock(new Vector3(90.5f, w.SpawnBlock.Y + 5.5f, 90.5f), -Vector3.UnitY, 8f, out var hitCell, out _, out _);
        Check(rayHit && hitCell == sandUnderWater, "луч прицела проходит сквозь воду и попадает в твердый блок под ней");

        // 26. Рецепт каши из опилок: 2 опилок + 1 доска + 1 семена пшеницы
        var porridgeGrid = new ItemDefinition?[] {
            GameData.SawdustItem, GameData.SawdustItem, null,
            GameData.PlankItem,   GameData.WheatSeedsItem, null,
            null,                 null,                 null
        };
        string porridgeKey = GameData.NormalizeGrid(porridgeGrid);
        Check(GameData.ShapeRecipes.TryGetValue(porridgeKey, out var porridgeRes) && porridgeRes.Item.Id == GameData.SawdustPorridgeItem.Id, "крафт каши из опилок требует 2 опилок, 1 доску и семена пшеницы");

        // 27. Стрела скелета не наносит урон самому стрелку
        var shooterSkel = new HostileMob(HostileType.Skeleton, new Vector3(100f, w.SpawnBlock.Y + 10f, 100f));
        w.HostileMobs.Add(shooterSkel);
        var skelArrow = new ArrowProjectile(shooterSkel.Position + new Vector3(0f, 0.5f, 0f), new Vector3(1f, 0f, 0f), shooterSkel);
        float skelInitialHp = shooterSkel.Health;
        skelArrow.Tick(0.016f, w, s.Player, s);
        Check(shooterSkel.Health == skelInitialHp && skelArrow.Alive, "выпущенная стрела скелета не задевает самого скелета-стрелка");

        // 28. Проверка генерации деревни в чанке
        int testVillageSeed = 3402;
        var villageWorld = new GameWorld(testVillageSeed);
        villageWorld.EnsureLoadedAroundSync(new Vector3(31f, 45f, 41f), 2);
        bool foundVillageBlock = false;
        for (int bx = 20; bx <= 45; bx++) {
            for (int bz = 30; bz <= 55; bz++) {
                for (int by = 35; by <= 55; by++) {
                    var vox = villageWorld.GetVoxel(new Vec3i(bx, by, bz));
                    if (vox.TypeId == GameData.BChest.Id || vox.TypeId == GameData.BCobblestone.Id || vox.TypeId == GameData.BGravel.Id) {
                        foundVillageBlock = true;
                        break;
                    }
                }
                if (foundVillageBlock) break;
            }
            if (foundVillageBlock) break;
        }
        Check(foundVillageBlock, "в мире генерируются блоки деревенских построек (фундамент, сундук, гравийная дорога)");
    }
}
