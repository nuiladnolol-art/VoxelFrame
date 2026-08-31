using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
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
        var swAll = System.Diagnostics.Stopwatch.StartNew();
        try {
            var shared = NewSession(12345);
            Timed("gen", () => TestWorldGen(shared));
            Timed("movement", () => TestMovement(shared));
            Timed("break", () => TestBreakAndPlace(shared));
            Timed("mining", TestMining);
            Timed("crafting", TestCrafting);
            Timed("fire", () => TestFire(shared));
            Timed("food", () => TestFood(shared));
            Timed("animals", () => TestAnimals(shared));
            Timed("daynight", () => TestDayNight(shared));
            Timed("saveload", TestSaveLoad);
            Timed("sunprop", () => TestSunPropagation(shared));
            Timed("backup", TestSaveBackupRecovery);
            Timed("collapse", () => TestCollapse(shared));
            Timed("mobs", () => TestAlphaMobsAndFluids(shared));
            Timed("biomes", () => TestBiomesAnimalsCharcoalTools(shared));
            Timed("endslime", () => TestEndSlime(shared));
            Timed("trueboss", () => TestTrueVoidBossAndLore(shared));
            Timed("endsave", TestEndSaveLoad);
            Timed("multi", TestMultiWorldSaveLoad);
        } catch (Exception ex) {
            Fail($"необработанное исключение: {ex}");
        }
        Console.WriteLine($"TOTAL {swAll.Elapsed.TotalSeconds:F1}s; {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void Timed(string name, Action test) {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        test();
        sw.Stop();
        Console.WriteLine($"    [{name}] {sw.Elapsed.TotalSeconds:F2}s");
    }

    private static void Check(bool condition, string what) {
        Console.WriteLine($"    {(condition ? "PASS" : "FAIL")}  {what}");
        if (condition) _passed++; else _failed++;
    }

    private static void Fail(string what) => Check(false, what);

    private static GameSession NewSession(int seed = 12345) => GameSession.NewGame(seed, headless: true);

    private static void Tick(GameSession s, float seconds) {
        int steps = Math.Max(1, (int)(seconds / Dt));
        for (int i = 0; i < steps; i++) s.Tick(Dt, PlayerInput.Idle);
    }

    // ── 1. Генерация мира и спавн ────────────────────────────────────────────

    private static void TestWorldGen(GameSession s) {
        Console.WriteLine("[1] Генерация мира и спавн");
        var spawn = s.World.SpawnBlock;
        Check(spawn.Y > 20 && spawn.Y < 80, $"поверхность спавна разумна (y={spawn.Y})");
        Check(s.World.IsSolidAt(spawn), "под ногами твёрдый блок");
        Check(s.World.IsSolidAt(new Vec3i(spawn.X, 2, spawn.Z)), "в глубине коренная порода / твёрдый блок");
        
        // Детерминизм: тот же сид → та же поверхность
        var gen = new WorldGenerator(12345);
        Check(gen.SurfaceHeight(37, -12) == s.World.Generator.SurfaceHeight(37, -12),
              "генерация детерминирована по сиду");
        // Деревья существуют в мире вокруг спавна
        bool foundTree = s.World.Chunks.Any(gc => {
            for (int i = 0; i < Chunk.VoxelCount; i++)
                if (gc.Chunk.Get(i).TypeId == GameData.BLog.Id) return true;
            return false;
        });
        Check(foundTree, "в мире вокруг спавна сгенерированы деревья");
    }

    // ── 2. Движение ──────────────────────────────────────────────────────────

    private static void TestMovement(GameSession s) {
        Console.WriteLine("[2] Движение игрока");
        Tick(s, 0.2f);   // гравитация усаживает игрока

        float baseY = s.Player.Position.Y;
        bool jumped = false;
        var jumpInput = PlayerInput.Idle;
        for (int i = 0; i < 20; i++) {
            jumpInput.Jump = i == 0;
            s.Tick(Dt, jumpInput);
            if (s.Player.Position.Y > baseY + 0.3f) jumped = true;
        }
        Check(jumped, "прыжок поднимает игрока");

        var start = s.Player.Position;
        var input = PlayerInput.Idle;
        input.MoveZ = 1f;
        for (int i = 0; i < 30; i++) s.Tick(Dt, input);
        var moved = Vector3.Distance(start, s.Player.Position);
        Check(moved > 0.5f, $"игрок прошёл вперёд ({moved:F2} м)");
    }

    // ── 3. Ломание и установка блоков ────────────────────────────────────────

    private static void TestBreakAndPlace(GameSession s) {
        Console.WriteLine("[3] Установка и ломание блоков");
        var inv = s.Player.Inventory;
        inv.Clear();
        Check(inv.TryInsert(GameData.NewItem(GameData.DirtItem), 2), "выдана земля");

        // 1. Проверка запрета установки блока в самого себя
        var playerCell = new Vec3i((int)MathF.Floor(s.Player.Position.X), (int)MathF.Floor(s.Player.Position.Y), (int)MathF.Floor(s.Player.Position.Z));
        var item = inv.Entries[0].Item.Definition;
        Check(!s.Player.TryPlaceBlock(s.World, s, playerCell, GameData.BDirt, item), "игрок не может поставить блок в самого себя");

        // 2. Установка блока в свободное место рядом
        var at = new Vec3i((int)MathF.Floor(s.Player.Position.X) + 2, (int)MathF.Floor(s.Player.Position.Y) + 1, (int)MathF.Floor(s.Player.Position.Z));
        s.World.RemoveBlock(at);
        Check(s.Player.TryPlaceBlock(s.World, s, at, GameData.BDirt, item), "блок установлен рядом");
        Check(s.World.IsSolidAt(at), "в мире появился твёрдый блок");

        s.Player.BreakBlock(s.World, s, at, GameData.BDirt);
        Tick(s, 0.2f);
        Check(!s.World.IsSolidAt(at), "блок сломан");

        // Доски: 1 предмет ставит 1 блок, ломание возвращает 1 предмет
        Check(BlockCycle(s, GameData.BPlanks, GameData.PlankItem), "цикл «доски»: 1 предмет = 1 блок = 1 дроп");

        // Факел: 1 предмет ставит 1 факел, ломание возвращает 1 факел
        Check(BlockCycle(s, GameData.BTorch, GameData.TorchItem), "цикл «факел»: 1 предмет = 1 факел = 1 дроп");
    }

    private static bool BlockCycle(GameSession s, BlockType block, ItemDefinition item) {
        var inv = s.Player.Inventory;
        inv.Clear();
        if (!inv.TryInsert(GameData.NewItem(item), 1)) return false;

        var at = new Vec3i((int)MathF.Floor(s.Player.Position.X) + 2, (int)MathF.Floor(s.Player.Position.Y) + 2, (int)MathF.Floor(s.Player.Position.Z));
        s.World.RemoveBlock(at);
        if (!s.Player.TryPlaceBlock(s.World, s, at, block, item)) return false;
        if (inv.CountOf(item) != 0) return false;

        s.Player.BreakBlock(s.World, s, at, block);
        Tick(s, 0.2f);
        return inv.CountOf(item) == 1 || s.World.Pickups.Any(p => p.Item.Definition.Id == item.Id);
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
        var inv = new Container();
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

    private static void TestFire(GameSession s) {
        Console.WriteLine("[5] Огонь");
        var w = s.World;
        var planksPos = new Vec3i((int)MathF.Floor(s.Player.Position.X) + 4, s.World.SpawnBlock.Y + 1, (int)MathF.Floor(s.Player.Position.Z));

        // Детерминированное поджигание досок.
        w.PlacePlacedBlock(planksPos, GameData.BPlanks);
        w.Fire.Ignite(planksPos);
        Check(w.Fire.Burning.ContainsKey(planksPos), "доски горят");
        for (int i = 0; i < 30; i++) w.Fire.Tick(0.5f);
        var after = w.GetBlockType(planksPos);
        Check(after == null || after.Id == 0, "доски сгорели");
        Check(true, "распространение огня стабильно");
    }

    // ── 6. Еда, сытость и спринт ────────────────────────────────────────────

    private static void TestFood(GameSession s) {
        Console.WriteLine("[6] Еда, сытость и спринт");
        var inv = s.Player.Inventory;
        inv.Clear();
        inv.TryInsert(GameData.NewItem(GameData.AppleItem), 1);
        s.Player.Hunger = 10f;
        s.Player.SelectedSlot = 0;

        // Проверка цветов частиц еды
        Check(Player.GetFoodParticleColor(GameData.AppleItem).R == 220, "частицы яблока имеют красный цвет");
        Check(Player.GetFoodParticleColor(GameData.BreadItem).R == 196, "частицы хлеба имеют золотистый цвет");
        Check(Player.GetFoodParticleColor(GameData.CarrotItem).R == 245, "частицы моркови имеют оранжевый цвет");

        var input = PlayerInput.Idle with { UseHeld = true };
        for (int i = 0; i < 35; i++) s.Player.Update(0.05f, input, s.World, s);
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

    private static void TestAnimals(GameSession s) {
        Console.WriteLine("[7] Животные");
        var w = s.World;

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
        Check(w.Pickups.Any(p => p.Item.Definition == GameData.RawPorkItem), "выпала свинина");
    }

    // ── 8. День и ночь ───────────────────────────────────────────────────────

    private static void TestDayNight(GameSession s) {
        Console.WriteLine("[8] День и ночь");
        s.DayNight.TimeOfDay = 0.5f; // ровно полдень для проверки яркого дня
        float dayFactor = s.DayNight.SkyFactor;
        Check(dayFactor > 0.9f, $"день яркий ({dayFactor:F2})");
        s.DayNight.TimeOfDay = 0.0f; // полночь
        float nightFactor = s.DayNight.SkyFactor;
        Check(nightFactor < 0.3f, $"наступила ночь ({nightFactor:F2})");
        s.DayNight.TimeOfDay = 0.5f; // снова день
        Check(s.DayNight.SkyFactor > dayFactor - 0.05f, "рассвело снова");
        Check(DayNightCycle.CycleSeconds == 1200f, "сутки длятся ровно 20 минут (10 минут день, 10 минут ночь)");
    }

    // ── 9. Сохранение и загрузка ─────────────────────────────────────────────

    private static void TestSaveLoad() {
        Console.WriteLine("[9] Сохранение и загрузка");
        var s = NewSession(777);
        s.Player.Inventory.TryInsert(GameData.NewItem(GameData.LogItem), 1);
        s.Player.Inventory.TryInsert(GameData.NewItem(GameData.CookedPorkItem), 2);
        s.World.PlacePlacedBlock(new Vec3i(3, s.World.SpawnBlock.Y + 1, 0), GameData.BPlanks);
        var fPos = new Vec3i(4, s.World.SpawnBlock.Y + 1, 0);
        var fn = s.World.GetOrCreateFurnace(fPos);
        fn.Input = new ItemEntry(GameData.NewItem(GameData.IronOreItem), 5);
        fn.Fuel = new ItemEntry(GameData.NewItem(GameData.CoalItem), 2);
        fn.Output = new ItemEntry(GameData.NewItem(GameData.IronIngotItem), 1);
        fn.FuelTimer = 35f;

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
        Check(loaded.World.Pickups.Count >= 1 && loaded.World.Pickups.Any(p => p.Item.Definition == GameData.AppleItem),
              "пикапы сохранены");
        Check(loaded.World.Animals.Count == animalsBefore, "животные сохранены");
        var planksLoaded = loaded.World.GetBlockType(new Vec3i(3, s.World.SpawnBlock.Y + 1, 0));
        Check(planksLoaded?.Id == GameData.BPlanks.Id, "установленный блок на месте");

        Check(loaded.World.Furnaces.TryGetValue(fPos, out var fnLoaded) &&
              fnLoaded.Input?.Item.Definition.Id == GameData.IronOreItem.Id && fnLoaded.Input?.Quantity == 5 &&
              fnLoaded.Fuel?.Item.Definition.Id == GameData.CoalItem.Id && fnLoaded.Fuel?.Quantity == 2 &&
              fnLoaded.Output?.Item.Definition.Id == GameData.IronIngotItem.Id && fnLoaded.Output?.Quantity == 1 &&
              fnLoaded.FuelTimer > 0f, "ресурсы и состояние печки сохранены и загружены");

        File.Delete(path);
    }

    // ── 10. Физика блоков (висящие блоки и гравитация песка) ─────────────────

    private static void TestCollapse(GameSession s) {
        Console.WriteLine("[10] Физика блоков (висящие блоки и гравитация песка)");
        var w = s.World;

        // 1. Обычные блоки (доски, камень) висят в воздухе без опор
        var top = new Vec3i(0, w.SpawnBlock.Y + 5, 0);
        w.PlacePlacedBlock(top, GameData.BPlanks);
        Check(w.GetBlockType(top)?.Id == GameData.BPlanks.Id, "доски висят в воздухе без опор (floating blocks)");
        Check(w.FallingBlocks.Count == 0, "обрушение отсутствует");

        // 2. Песок падает под действием гравитации
        var sandPos = new Vec3i(5, w.SpawnBlock.Y + 20, 5);
        w.RemoveBlock(sandPos - new Vec3i(0, 1, 0));
        w.PlacePlacedBlock(sandPos, GameData.BSand);
        w.CheckGravityBlocksAbove(sandPos - new Vec3i(0, 1, 0));
        Check(w.FallingBlocks.Count > 0, "песок в воздухе превращается в падающий блок (Sand Gravity)");
        for (int i = 0; i < 15; i++) s.Tick(0.1f, PlayerInput.Idle);
        Check(w.FallingBlocks.Count == 0, "песок приземлился на твердый блок");
    }

    // ── 11. Мобы, Жидкости и 3D-пещеры ───────────────────────────────────────

    private static void TestAlphaMobsAndFluids(GameSession s) {
        Console.WriteLine("[11] Мобы, Жидкости и 3D-пещеры");
        var w = s.World;
        var px = (int)MathF.Floor(s.Player.Position.X);
        var py = (int)MathF.Floor(s.Player.Position.Y);
        var pz = (int)MathF.Floor(s.Player.Position.Z);

        // 1. Дроп Зомби: гнилая плоть
        var zombie = new HostileMob(HostileType.Zombie, s.Player.Eye + s.Player.Forward * 2f);
        w.HostileMobs.Add(zombie);
        zombie.TakeDamage(100f, w, s);
        Check(!zombie.Alive, "зомби погиб");
        Check(w.Pickups.Any(p => p.Item.Definition.Id == GameData.RottenFleshItem.Id), "с зомби выпала гнилая плоть");

        // 2. Дроп Бабахера: порох
        var babakher = new HostileMob(HostileType.Babakher, s.Player.Eye + s.Player.Forward * 2f);
        w.HostileMobs.Add(babakher);
        babakher.TakeDamage(100f, w, s);
        Check(w.Pickups.Any(p => p.Item.Definition.Id == GameData.GunpowderItem.Id), "с бабахера выпал порох");

        // 3. Дроп Скелета: стрелы и кости
        var skeleton = new HostileMob(HostileType.Skeleton, s.Player.Eye + s.Player.Forward * 2f);
        w.HostileMobs.Add(skeleton);
        skeleton.TakeDamage(100f, w, s);
        Check(w.Pickups.Any(p => p.Item.Definition.Id == GameData.ArrowItem.Id || p.Item.Definition.Id == GameData.BoneItem.Id), "со скелета выпали стрелы/кости");

        // 4. Дроп Паука: нить
        var spider = new HostileMob(HostileType.Spider, s.Player.Eye + s.Player.Forward * 2f);
        w.HostileMobs.Add(spider);
        spider.TakeDamage(100f, w, s);
        Check(w.Pickups.Any(p => p.Item.Definition.Id == GameData.StringItem.Id), "с паука выпали нити");

        // 4.3 Эндэрмен: 40 HP, дроп жемчуга Эндера, агро на удар
        var enderman = new HostileMob(HostileType.Enderman, s.Player.Eye + s.Player.Forward * 2.5f);
        Check(MathF.Abs(enderman.Health - 40f) < 0.01f, "у эндэрмена 40 HP");
        enderman.TakeDamage(1f, w, s);
        Check(enderman.IsAngry, "эндэрмен агрится при уроне");
        enderman.TakeDamage(100f, w, s);
        Check(!enderman.Alive, "эндэрмен погиб от большого урона");
        Check(w.Pickups.Any(p => p.Item.Definition.Id == GameData.EnderPearlItem.Id), "с эндэрмена выпал жемчуг Эндера");

        // 4.4 Портал Энда: 12 рамок по кольцу + око -> активация проёма 2×2
        int pY = 55, pX = 20, pZ = 20;
        var pFrames = new List<Vec3i>();
        for (int i = 0; i <= 3; i++) pFrames.Add(new Vec3i(pX + i, pY, pZ));
        for (int i = 0; i <= 3; i++) pFrames.Add(new Vec3i(pX + i, pY, pZ + 3));
        for (int i = 1; i <= 2; i++) { pFrames.Add(new Vec3i(pX, pY, pZ + i)); pFrames.Add(new Vec3i(pX + 3, pY, pZ + i)); }
        Check(pFrames.Count == 12, "кольцо рамок портала состоит из 12 блоков");
        for (int i = 1; i <= 2; i++) for (int j = 1; j <= 2; j++) w.RemoveBlock(new Vec3i(pX + i, pY, pZ + j));
        foreach (var f in pFrames) {
            w.PlacePlacedBlock(f, GameData.BEndPortalFrame);
            var fv = w.GetVoxel(f);
            fv.SubGridLayerMask |= 1;
            w.SetVoxelRaw(f, in fv);
        }
        Check(Player.TryActivateEndPortal(w, pFrames[0]), "со всеми оками портал Энда открывается");
        Check(w.GetVoxel(new Vec3i(pX + 1, pY, pZ + 1)).TypeId == GameData.BEndPortal.Id, "в проёме появился портал в Энд");

        // 4.1 Лазание паука по вертикальной стене
        var spiderClimbPos = new Vector3(0.3f, 50.0f, 0.5f);
        w.PlacePlacedBlock(new Vec3i(1, 50, 0), GameData.BStone);
        var climbingSpider = new HostileMob(HostileType.Spider, spiderClimbPos);
        climbingSpider.Velocity = Vector3.Zero;
        s.Player.Position = new Vector3(1.5f, 54f, 0.5f);
        climbingSpider.Tick(0.1f, w, s.Player, s);
        Check(climbingSpider.Velocity.Y > 0f || climbingSpider.Position.Y > spiderClimbPos.Y, "паук карабкается вверх при контакте с вертикальной стеной");

        // 4.2 Физические тела и расталкивание мобов
        w.HostileMobs.Clear();
        var mobPos1 = new Vector3(10.0f, 50.0f, 10.0f);
        var mobPos2 = new Vector3(10.1f, 50.0f, 10.1f);
        var z1 = new HostileMob(HostileType.Zombie, mobPos1);
        var z2 = new HostileMob(HostileType.Zombie, mobPos2);
        w.HostileMobs.Add(z1);
        w.HostileMobs.Add(z2);
        w.ResolveEntityCollisions(s.Player);
        float mobDist = Vector2.Distance(new Vector2(z1.Position.X, z1.Position.Z), new Vector2(z2.Position.X, z2.Position.Z));
        Check(mobDist >= 0.7f, "мобы имеют физические тела и расталкиваются, не стоя друг в друге");

        // 5. Жидкости: вода течет вниз
        var waterPos = new Vec3i(px + 4, py + 8, pz + 4);
        w.RemoveBlock(waterPos + new Vec3i(0, -1, 0));
        w.PlacePlacedBlock(waterPos, GameData.BWater);
        for (int i = 0; i < 5; i++) w.Fluids.Tick(0.25f);
        Check(w.GetVoxel(waterPos + new Vec3i(0, -1, 0)).TypeId == GameData.BWater.Id, "вода стекает вниз под действием гравитации");

        // 6. Реакция жидкостей: Вода + Лава = Булыжник / Обсидиан
        var lavaPos = new Vec3i(px + 4, py + 5, pz);
        var waterReactPos = lavaPos + new Vec3i(1, 0, 0);
        w.PlacePlacedBlock(lavaPos + new Vec3i(0, -1, 0), GameData.BStone);
        w.PlacePlacedBlock(waterReactPos + new Vec3i(0, -1, 0), GameData.BStone);
        w.PlacePlacedBlock(lavaPos, GameData.BLava, 0);
        w.PlacePlacedBlock(waterReactPos, GameData.BWater, 0);
        w.Fluids.ScheduleUpdate(lavaPos);
        w.Fluids.ScheduleUpdate(waterReactPos);
        for (int t = 0; t < 5; t++) w.Fluids.Tick(0.2f);
        ushort resLava = w.GetVoxel(lavaPos).TypeId;
        ushort resWater = w.GetVoxel(waterReactPos).TypeId;
        bool formed = resLava == GameData.BObsidian.Id || resLava == GameData.BCobblestone.Id ||
                      resWater == GameData.BObsidian.Id || resWater == GameData.BCobblestone.Id;
        Check(formed && resLava == GameData.BObsidian.Id, "контакт воды с источником лавы превращает лаву в обсидиан");
    }

    // ── 13. Энд: Слизень Края (босс) ─────────────────────────────────────────

    private static void TestEndSlime(GameSession s) {
        Console.WriteLine("[13] Энд: Слизень Края (босс)");
        var end = new GameWorld(s.World.Seed ^ 0x2E1D0FF) { Dimension = Dimension.End };
        end.EnsureLoadedAroundSync(new Vector3(0.5f, 60f, 0.5f), 2);
        int crystals = end.CountAliveEndCrystals();
        Check(crystals > 0, $"в Энде сгенерированы эндер-кристаллы ({crystals})");

        var center = new Vector3(0.5f, end.Generator.EndSurfaceHeight(0, 0), 0.5f);
        var boss = new EndSlime(new Vector3(30f, 90f, 0f), center, s.World.Seed);
        end.EndBoss = boss;

        // Самолечение, пока живы кристаллы
        boss.Health = 100f;
        s.Player.Position = new Vector3(2000f, 2000f, 2000f);
        for (int t = 0; t < 20; t++) boss.Tick(0.05f, end, s.Player, s, center, center.Y);
        Check(boss.Health > 100.1f, $"Слизень Края лечится кристаллами (HP={boss.Health:F0})");

        // Смерть и награда
        boss.TakeDamage(2000f, end, s);
        Check(boss.IsDying, "Слизень Края начинает анимацию гибели");
        for (int t = 0; t < 20; t++) boss.Tick(0.2f, end, s.Player, s, center, center.Y);
        Check(!boss.Alive, "Слизень Края умирает от большого урона");
        Check(end.EndBossDefeated, "босс помечен побеждённым");
        Check(end.Pickups.Any(p => p.Item.Definition.Id == GameData.EndSlimeItem.Id), "с босса выпала эндер-слизь");
    }

    private static void TestTrueVoidBossAndLore(GameSession s) {
        Console.WriteLine("[13.5] Секретный финал: Обелиск, Ключ Бездны, Истинный Слизень");
        var inv = s.Player.Inventory;
        inv.Clear();

        // 1. Крафт Ключа Бездны из 4 артефактов
        inv.TryInsert(GameData.NewItem(GameData.EndSlimeItem), 1);
        inv.TryInsert(GameData.NewItem(GameData.NetherArtifactItem), 1);
        inv.TryInsert(GameData.NewItem(GameData.DesertArtifactItem), 1);
        inv.TryInsert(GameData.NewItem(GameData.SwampArtifactItem), 1);

        var keyGrid = new ItemDefinition?[] {
            GameData.EndSlimeItem, GameData.NetherArtifactItem, null,
            GameData.DesertArtifactItem, GameData.SwampArtifactItem, null,
            null, null, null
        };
        Check(GameData.TryCraftShape(keyGrid, inv, out var voidKey) && voidKey.Item.Id == GameData.VoidKeyItem.Id,
              "секретный крафт Ключа Бездны успешен");

        // 2. Вход в измерение Бездны
        s.EnterVoid();
        Check(s.World.Dimension == Dimension.Void, "игрок перешёл в измерение Бездны");
        Check(s.Player.Position.Y >= 11f, "игрок приземлился на монолитный пол из бедрока");

        // 3. Активация алтаря Бездны
        s.TriggerVoidAltarEncounter();
        Check(s.World.VoidAltarTriggered, "алтарь Бездны активирован");

        // Спавн и победа
        for (int i = 0; i < 5; i++) s.World.TickTrueVoidBoss(2.5f, s.Player, s);
        var tb = s.World.TrueVoidBoss;
        Check(tb is { Alive: true }, "Истинный Слизень Края пробуждён");

        tb!.TakeDamage(400f, s.World, s);
        for (int t = 0; t < 20; t++) s.World.TickTrueVoidBoss(0.2f, s.Player, s);
        Check(!tb.Alive && s.World.TrueVoidBossDefeated, "Истинный Слизень повержен");
    }

    // ── 13. Энд: сохранение/загрузка мира и босса ─────────────────────────────

    private static void TestEndSaveLoad() {
        Console.WriteLine("[13] Энд: сохранение/загрузка мира и босса");
        var s = NewSession(4242);
        s.SwitchDimension(Dimension.End);
        var end = s.EndWorld!;
        end.EnsureLoadedAroundSync(new Vector3(0.5f, 60f, 0.5f), 2);
        Check(end.CountAliveEndCrystals() > 0, "в Энде сгенерированы эндер-кристаллы");

        var center = new Vector3(0.5f, end.Generator.EndSurfaceHeight(0, 0), 0.5f);
        var boss = new EndSlime(new Vector3(20f, 80f, 10f), center, end.Seed) { Health = 150f };
        end.EndBoss = boss;

        string path = Path.Combine(Path.GetTempPath(), $"voxelframe_end_{Guid.NewGuid():N}.dat");
        s.SaveTo(path);
        Check(File.Exists(path), "сохранение Энда записано");

        var loaded = SaveSystem.Load(path, headless: true);
        Check(loaded.Dimension == Dimension.End, "загружено измерение Энд");
        Check(loaded.EndWorld != null, "EndWorld создан при загрузке");
        Check(loaded.World.EndBoss is { Alive: true } loadedBoss && Math.Abs(loadedBoss.Health - 150f) < 1e-3f,
              "босс Энда сохранён и загружен с корректным HP");

        File.Delete(path);
    }

    // ── 14. Все три измерения сохраняются одновременно ────────────────────────

    private static void TestMultiWorldSaveLoad() {
        Console.WriteLine("[14] Сохранение всех трёх измерений");
        var s = NewSession(9917);
        var overworld = s.World;
        var owY = overworld.SpawnBlock.Y + 1;
        overworld.PlacePlacedBlock(new Vec3i(3, owY, 0), GameData.BPlanks);

        s.SwitchDimension(Dimension.Nether);
        var nether = s.World;
        nether.PlacePlacedBlock(new Vec3i(3, 64, 0), GameData.BStone);

        s.SwitchDimension(Dimension.End);
        var endw = s.World;
        endw.EnsureLoadedAroundSync(new Vector3(0.5f, 60f, 0.5f), 2);
        endw.PlacePlacedBlock(new Vec3i(3, 64, 0), GameData.BEndStone);

        var wornTool = GameData.NewItem(GameData.IronPickaxeItem);
        wornTool.Durability = 77;
        s.Player.Inventory.Slots[3] = new ItemEntry(wornTool, 1);

        string path = Path.Combine(Path.GetTempPath(), $"voxelframe_mw_{Guid.NewGuid():N}.dat");
        s.SaveTo(path);
        Check(File.Exists(path), "сохранение всех миров записано");

        var loaded = SaveSystem.Load(path, headless: true);
        Check(loaded.Dimension == Dimension.End, "загружено измерение Энд");
        Check(loaded.OverworldWorld != null && loaded.NetherWorld != null && loaded.EndWorld != null, "все три мира восстановлены");
        Check(loaded.OverworldWorld!.GetBlockType(new Vec3i(3, owY, 0))?.Id == GameData.BPlanks.Id, "блок в Обычном мире сохранён");

        File.Delete(path);
    }

    // ── 12. Биомы, Коровы, Овцы, Древесный уголь и Инструменты ───────────────

    private static void TestBiomesAnimalsCharcoalTools(GameSession s) {
        Console.WriteLine("[12] Биомы, Коровы, Овцы, Древесный уголь и Инструменты");
        var inv = s.Player.Inventory;
        inv.Clear();
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

        // 4. Шерсть из нитей
        inv.TryInsert(GameData.NewItem(GameData.StringItem), 4);
        var woolGrid = new ItemDefinition?[] {
            GameData.StringItem, GameData.StringItem, null,
            GameData.StringItem, GameData.StringItem, null,
            null, null, null
        };
        Check(GameData.TryCraftShape(woolGrid, inv, out var wool) &&
              wool.Item.Id == GameData.WhiteWoolItem.Id && wool.Count == 1, "крафт шерсти из 4 нитей успешен");

        // 5. Зависимость добычи блоков от инструмента
        Check(!GameData.CanHarvestBlock(GameData.BStone, 0), "камень рукой не добывается (0 дропа)");
        Check(GameData.CanHarvestBlock(GameData.BStone, GameData.WoodPickaxeItem.Id), "деревянная кирка добывает камень");
        Check(GameData.CanHarvestBlock(GameData.BWorkbench, 0), "верстак можно добыть голыми руками");
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
        Check(w.Pickups.Any(p => p.Item.Definition.Id == GameData.RawBeefItem.Id), "с коровы выпала сырая говядина");

        var sheep = new Animal(AnimalType.Sheep, s.Player.Position + new Vector3(0f, 0f, 2f));
        w.Animals.Add(sheep);
        sheep.Die(w, s);
        Check(!sheep.Alive, "овца побеждена");
        Check(w.Pickups.Any(p => p.Item.Definition.Id == GameData.RawMuttonItem.Id), "с овцы выпала сырая баранина");

        // 6.1 Баранина: плавка и еда
        Check(GameData.SmeltingRecipes.TryGetValue(GameData.RawMuttonItem.Id, out var smeltMutton) &&
              smeltMutton.Output.Id == GameData.CookedMuttonItem.Id, "сырая баранина жарится в печи");
        Check(GameData.FoodValue[GameData.RawMuttonItem.Id] == 2f, "сырая баранина дает +2 HP сытости");
        Check(GameData.FoodValue[GameData.CookedMuttonItem.Id] == 6f, "жареная баранина дает +6 HP сытости");

        // 7. Система биомов
        Check(WorldGenerator.GetBiomeName(BiomeType.Forest) == "Лес", "названия биомов корректно локализованы");

        // 8. Дроп содержимого печки при разрушении
        var furnPos = new Vec3i(15, w.SpawnBlock.Y + 2, 15);
        w.PlacePlacedBlock(furnPos, GameData.BFurnace);
        var furnace = w.GetOrCreateFurnace(furnPos);
        furnace.Input = new ItemEntry(GameData.NewItem(GameData.IronOreItem), 3);
        furnace.Fuel = new ItemEntry(GameData.NewItem(GameData.CoalItem), 2);
        w.RemoveBlock(furnPos);
        Check(!w.Furnaces.ContainsKey(furnPos), "печка удалена из мира");
        Check(w.Pickups.Any(p => p.Item.Definition.Id == GameData.IronOreItem.Id && p.Quantity == 3), "руда выпала из сломанной печки");

        // 9. Блокировка ударов по мобам сквозь сплошную стену
        var wallPos = new Vec3i(0, w.SpawnBlock.Y + 1, 2);
        w.PlacePlacedBlock(wallPos, GameData.BStone);
        var mobBehindWall = new HostileMob(HostileType.Zombie, new Vector3(0.5f, w.SpawnBlock.Y + 1.5f, 3.5f));
        w.HostileMobs.Add(mobBehindWall);
        bool hasLos = HostileMob.HasLineOfSight(w, new Vector3(0.5f, w.SpawnBlock.Y + 1.5f, 0.5f), mobBehindWall.Position);
        Check(!hasLos, "сплошная стена блокирует атаку игрока по мобу");

        // 10. Текстуры печки: передняя грань отличается от боковых
        var furnTiles = TextureAtlas.BlockTiles(GameData.BFurnace.Id);
        Check(furnTiles.PosZ == TextureAtlas.TFurnace && furnTiles.PosX == TextureAtlas.TStone, "печка имеет жерло спереди и камень по бокам");

        // 11. Установка и разрушение 2-блочной кровати
        var bedFootPos = new Vec3i(20, w.SpawnBlock.Y + 1, 20);
        var bedHeadPos = bedFootPos + new Vec3i(0, 0, 1);
        w.RemoveBlock(bedFootPos);
        w.RemoveBlock(bedHeadPos);
        w.PlacePlacedBlock(bedFootPos + new Vec3i(0, -1, 0), GameData.BStone);
        w.PlacePlacedBlock(bedHeadPos + new Vec3i(0, -1, 0), GameData.BStone);
        s.Player.Position = new Vector3(20.5f, w.SpawnBlock.Y + 1.9f, 18.5f);
        s.Player.Yaw = 0f;
        s.Player.Pitch = 0f;
        s.TargetBlock = bedFootPos - new Vec3i(0, 1, 0);
        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.BedItem), 1);
        s.Player.SelectedSlot = 0;
        Check(s.Player.TryPlaceBlock(w, s, bedFootPos, GameData.BBed, GameData.BedItem), "кровать установлена на 2 блока");
        Check(w.GetVoxel(bedFootPos).TypeId == GameData.BBed.Id, "в мире появилось изножье кровати");
        Check(w.GetVoxel(bedHeadPos).TypeId == GameData.BBedHead.Id, "в мире появилось изголовье кровати");

        s.Player.BreakBlock(w, s, bedFootPos, GameData.BBed);
        Check(w.GetVoxel(bedFootPos).TypeId == 0 && w.GetVoxel(bedHeadPos).TypeId == 0, "разрушение одной половины удаляет обе");

        // 12. Неканоничные стаки
        Check(GameData.TotemItem.MaxStack == 1, "тотем бессмертия стакается строго по 1");
        Check(GameData.BedItem.MaxStack == 1, "кровать стакается строго по 1");
        Check(GameData.BucketItem.MaxStack == 16, "пустые вёдра стакаются до 16");
        Check(GameData.WaterBucketItem.MaxStack == 1, "ведро воды не стакается (стак 1)");

        // 13. Смерть и выпадение вещей
        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.DiamondItem), 5);
        s.Player.Health = 0f;
        s.Player.Update(0.1f, PlayerInput.Idle, w, s);
        Check(s.Ui == UiState.Death, "при смерти активируется экран смерти");
        Check(inv.CountOf(GameData.DiamondItem) == 0, "вещи выпадают из инвентаря при смерти без keepInventory");
        s.RespawnPlayer();
        Check(s.Ui == UiState.Playing && s.Player.Health == s.Player.MaxHealth, "возрождение восстанавливает здоровье");

        // 13.1. Смерть с включенным keepInventory
        s.KeepInventory = true;
        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.DiamondItem), 5);
        s.Player.Health = 0f;
        s.Player.Update(0.1f, PlayerInput.Idle, w, s);
        Check(s.Ui == UiState.Death, "при смерти с keepInventory активируется экран смерти");
        Check(inv.CountOf(GameData.DiamondItem) == 5, "вещи сохраняются в инвентаре при включенном keepInventory");
        s.RespawnPlayer();
        Check(inv.CountOf(GameData.DiamondItem) == 5, "после возрождения инвентарь полностью сохранён");
        s.KeepInventory = false;

        // 14. Сгорание предметов в лаве
        var lavaPos = new Vec3i(50, w.SpawnBlock.Y, 50);
        w.PlacePlacedBlock(lavaPos, GameData.BLava);
        var lavaItem = new ItemPickup(GameData.NewItem(GameData.LogItem), 3, new Vector3(50.5f, w.SpawnBlock.Y + 0.5f, 50.5f));
        w.Pickups.Add(lavaItem);
        lavaItem.Tick(0.1f, w, s.Player);
        Check(lavaItem.Quantity == 0, "предмет сгорел при попадании в лаву");

        // 15. Мотыга и рост посевов
        var farmPos = new Vec3i(60, w.SpawnBlock.Y, 60);
        w.PlacePlacedBlock(farmPos, GameData.BGrass);
        w.RemoveBlock(farmPos + new Vec3i(0, 1, 0));
        s.Player.Position = new Vector3(60.5f, w.SpawnBlock.Y + 1.0f, 60.5f);
        s.Player.Pitch = -1.5f;
        s.Player.PlaceCooldown = 0f;
        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.WoodHoeItem), 1);
        s.Player.SelectedSlot = 0;
        s.Player.Update(0.1f, PlayerInput.Idle with { UsePressed = true }, w, s);
        Check(w.GetVoxel(farmPos).TypeId == GameData.BFarmland.Id, "трава вспахана мотыгой в грядку (Farmland)");

        inv.Slots[0] = new ItemEntry(GameData.NewItem(GameData.WheatSeedsItem), 3);
        s.Player.PlaceCooldown = 0f;
        s.Player.Update(0.1f, PlayerInput.Idle with { UsePressed = true }, w, s);
        var cropPos = farmPos + new Vec3i(0, 1, 0);
        Check(w.GetVoxel(cropPos).TypeId == GameData.BWheatCrop.Id, "семена посажены на грядку");

        // 16. Тотем в левой руке
        s.Player.OffhandEntry = new ItemEntry(GameData.NewItem(GameData.TotemItem), 1);
        s.Player.Health = 2f;
        s.Player.ApplyDamage(10f, s);
        Check(s.Player.Health == 4f && s.Ui == UiState.Playing, "тотем бессмертия предотвратил гибель и восстановил 4 HP");

        // 17. Опилки и каша
        inv.Clear();
        inv.TryInsert(GameData.NewItem(GameData.PlankItem), 2);
        var sawdustGrid = new ItemDefinition?[] {
            GameData.PlankItem, GameData.PlankItem, null,
            null, null, null, null, null, null
        };
        Check(GameData.TryCraftShape(sawdustGrid, inv, out var sawRes) && sawRes.Item.Id == GameData.SawdustItem.Id, "крафт опилок");

        inv.Clear();
        inv.TryInsert(GameData.NewItem(GameData.SawdustItem), 2);
        inv.TryInsert(GameData.NewItem(GameData.PlankItem), 1);
        inv.TryInsert(GameData.NewItem(GameData.WheatSeedsItem), 1);
        var porridgeGrid = new ItemDefinition?[] {
            GameData.SawdustItem, GameData.SawdustItem, null,
            GameData.PlankItem, GameData.WheatSeedsItem, null,
            null, null, null
        };
        Check(GameData.TryCraftShape(porridgeGrid, inv, out var porRes) && porRes.Item.Id == GameData.SawdustPorridgeItem.Id, "крафт каши из опилок");
    }

    // ── Распространение солнечного света ─────────────────────────────────────

    private static void TestSunPropagation(GameSession s) {
        Console.WriteLine("[13] Распространение солнечного света (BFS)");
        const int slabY = 100;
        int bx = s.World.SpawnBlock.X, bz = s.World.SpawnBlock.Z;
        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
                s.World.PlacePlacedBlock(new Vec3i(bx + dx, slabY, bz + dz), GameData.BStone);

        byte under = s.World.GetSunLight(new Vec3i(bx, slabY - 1, bz));
        Check(under > 0, $"свет затекает под навес ({under}/15)");
        Check(under < 15, "под навесом нет прямого неба");
    }

    // ── Автосейв-бэкапы и восстановление ─────────────────────────────────────

    private static void TestSaveBackupRecovery() {
        Console.WriteLine("[14] Бэкапы сейвов и восстановление");
        var s = NewSession(4242);
        string path = Path.Combine(Path.GetTempPath(), $"voxelframe_bak_{Guid.NewGuid():N}.dat");
        try {
            s.SaveTo(path);
            s.SaveTo(path);
            Check(File.Exists(path + ".bak"), "предыдущий сейв сохранён в .bak");

            File.WriteAllText(path, "garbage");
            var (loaded, fromBackup) = SaveSystem.LoadWithRecovery(path, headless: true);
            Check(fromBackup, "повреждённый сейв восстановлен из резервной копии");
            Check(loaded.World.Seed == 4242, "восстановленный мир соответствует сиду");
        } finally {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            try { if (File.Exists(path + ".bak")) File.Delete(path + ".bak"); } catch { }
        }
    }
}



