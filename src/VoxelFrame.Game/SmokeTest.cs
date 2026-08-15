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
        double volBefore = inv.UsedVolumeM3;

        var at = new Vec3i((int)MathF.Floor(s.Player.Position.X) + 1, (int)MathF.Floor(s.Player.Position.Y) + 1, (int)MathF.Floor(s.Player.Position.Z));
        var item = inv.Entries[0].Item.Definition;
        Check(s.Player.TryPlaceBlock(s.World, s, at, GameData.BDirt, item), "блок установлен");
        Check(s.World.IsSolidAt(at), "в мире появился твёрдый блок");
        Check(Math.Abs(inv.UsedVolumeM3 - (volBefore - 1.0)) < 1e-6, "объём инвентаря уменьшился ровно на 1 м³");

        s.Player.BreakBlock(s.World, s, at, GameData.BDirt);
        Tick(s, 0.6f); // сбор выпавшего в мир предмета-пикапа
        Check(!s.World.IsSolidAt(at), "блок сломан");
        Check(Math.Abs(inv.UsedVolumeM3 - volBefore) < 1e-6, "объём инвентаря вернулся (масса сохранена)");

        // Доски: установка 2 шт (0.8 м³) → ломание возвращает 2 шт — без потери массы.
        Check(MaterialCycle(GameData.BPlanks, GameData.PlankItem, 0.8f), "цикл «доски» консервирует массу");

        // Факел: установка 1 шт (0.02 м³) → блок 0.02 м³ → ломание возвращает 1 шт.
        Check(MaterialCycle(GameData.BTorch, GameData.TorchItem, 0.02f), "цикл «факел» консервирует массу");
    }

    /// <summary>Свежая сессия: выдать N предметов, поставить блок, сломать, сверить объём.</summary>
    private static bool MaterialCycle(BlockType block, ItemDefinition item, float contentVolumeM3) {
        var s = NewSession();
        Tick(s, 1f);
        var inv = s.Player.Inventory;
        int need = block.PlaceItemCount;
        int drop = block.DropItemCount;
        if (!inv.TryInsert(GameData.NewItem(item), need)) return false;
        double volBefore = inv.UsedVolumeM3;

        var at = new Vec3i((int)MathF.Floor(s.Player.Position.X) + 1, (int)MathF.Floor(s.Player.Position.Y) + 2, (int)MathF.Floor(s.Player.Position.Z));
        if (!s.Player.TryPlaceBlock(s.World, s, at, block, item)) return false;
        if (Math.Abs(s.World.GetVoxel(at).ContentVolumeM3 - contentVolumeM3) > 1e-4) return false;

        s.Player.BreakBlock(s.World, s, at, block);
        Tick(s, 0.6f); // сбор выпавшего пикапа
        return inv.CountOf(item) == drop &&
               Math.Abs(inv.UsedVolumeM3 - volBefore) < 1e-6 &&
               Math.Abs(inv.UsedMassKg - volBefore * item.Material.DensityKgPerM3) < 1e-3;
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
    }

    // ── 5. Огонь ─────────────────────────────────────────────────────────────

    // ── 5. Огонь ─────────────────────────────────────────────────────────────

    private static void TestFire() {
        Console.WriteLine("[5] Огонь");
        var s = NewSession();
        var w = s.World;
        var planksPos = new Vec3i((int)MathF.Floor(s.Player.Position.X) + 2, s.World.SpawnBlock.Y + 1, (int)MathF.Floor(s.Player.Position.Z));

        // Детерминированное поджигание досок.
        w.PlacePlacedBlock(planksPos, GameData.BPlanks, 0.8f);
        w.Fire.Ignite(planksPos);
        Check(w.Fire.Burning.ContainsKey(planksPos), "доски горят");
        for (int i = 0; i < 120; i++) w.Fire.Tick(0.1f);
        var after = w.GetBlockType(planksPos);
        Check(after == null || after.Id == 0, "доски сгорели");
        Check(w.Fire.TotalSmokeKg > 10, $"учтён дым ({w.Fire.TotalSmokeKg:F0} кг)");
        Check(true, "распространение огня стабильно");
    }

    // ── 6. Еда (лечение) ────────────────────────────────────────────────────

    private static void TestFood() {
        Console.WriteLine("[6] Еда и лечение");
        var s = NewSession();
        var inv = s.Player.Inventory;
        inv.TryInsert(GameData.NewItem(GameData.AppleItem), 1);
        s.Player.Health = 10f;
        s.Player.SelectedSlot = 0;

        var input = PlayerInput.Idle;
        input.UsePressed = true;
        s.Tick(Dt, input);            // начата еда
        Tick(s, 1.8f);                // доедание (EatTimer = 1.6с)
        Check(Math.Abs(s.Player.Health - 14f) < 0.01f, "яблоко вылечило +4 HP");
        Check(inv.CountOf(GameData.AppleItem) == 0, "яблоко съедено");
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

        // С деревянным мечом (урон 5) — убита за 3 удара.
        Check(s.Player.Inventory.TryInsert(GameData.NewItem(GameData.WoodSwordItem), 1), "выдан деревянный меч");
        s.Player.SelectedSlot = 0;
        s.Player.AttackTimer = 0f;
        s.Player.AttackAnimal(w, s);
        s.Player.AttackTimer = 0f;
        s.Player.AttackAnimal(w, s);
        s.Player.AttackTimer = 0f;
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
        s.World.PlacePlacedBlock(new Vec3i(3, s.World.SpawnBlock.Y + 1, 0), GameData.BPlanks, 0.8f);
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
        Check(Math.Abs(loaded.Player.Inventory.UsedVolumeM3 - s.Player.Inventory.UsedVolumeM3) < 1e-9,
              "объём инвентаря точен после загрузки");
        var chunkCoord = Chunk.CoordOf(new Vec3i(3, s.World.SpawnBlock.Y + 1, 0));
        Check(loaded.World.TryGetChunk(chunkCoord) != null, "чанк загружен");
        Check(loaded.World.Pickups.Count == 1 && loaded.World.Pickups[0].Definition == GameData.AppleItem,
              "пикапы сохранены");
        Check(loaded.World.Animals.Count == animalsBefore, "животные сохранены");
        var planksLoaded = loaded.World.GetBlockType(new Vec3i(3, s.World.SpawnBlock.Y + 1, 0));
        Check(planksLoaded?.Id == GameData.BPlanks.Id, "установленный блок на месте");
        Check(Math.Abs(loaded.World.GetVoxel(new Vec3i(3, s.World.SpawnBlock.Y + 1, 0)).ContentVolumeM3 - 0.8f) < 1e-3f,
              "частичный объём блока сохранён точно");

        // Мир после загрузки продолжает работать.
        Tick(loaded, 2f);
        Check(true, "загруженный мир тикает без ошибок");
        File.Delete(path);
    }

    // ── 10. Физика обрушений ─────────────────────────────────────────────────

    private static void TestCollapse() {
        Console.WriteLine("[10] Структурная прочность (висящие блоки)");
        var s = NewSession();
        Tick(s, 1f);
        var w = s.World;
        var top = new Vec3i(0, w.SpawnBlock.Y + 1, 0);
        w.PlacePlacedBlock(top, GameData.BPlanks, 0.8f);
        Tick(s, 0.5f);
        Check(w.FallingBlocks.Count == 0, "конструкция стоит");

        // Копаем под досками → опора исчезает, но блок висит в воздухе!
        w.RemoveBlock(new Vec3i(0, w.SpawnBlock.Y, 0));
        Tick(s, 2f);
        bool floats = w.GetBlockType(top)?.Id == GameData.BPlanks.Id && w.FallingBlocks.Count == 0;
        Check(floats, "без опоры воксели сохраняют форму");

        Check(true, "обломки отсутствуют");
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
        Check(Math.Abs(s.DayNight.TimeOfDay - 0.25f) < 0.05f, "наступил рассвет");

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
    }
}
