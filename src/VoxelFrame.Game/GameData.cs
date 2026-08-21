using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.Materials;

namespace VoxelFrame.Game;

/// <summary>Определение блока. Физические параметры производны от материала и объёма.</summary>
public sealed class BlockType {
    public required ushort Id { get; init; }
    public required string Name { get; init; }
    public required Material Material { get; init; }
    public bool IsSolid { get; set; } = true;         // коллизии
    public bool IsOpaque { get; set; } = true;        // блокирует солнечный свет
    public bool IsFlammable { get; set; }             // горит
    public byte LightLevel { get; set; }              // собственный свет (0..15)
    public float BurnTimeSeconds { get; set; }        // время горения
    public float LoadCapacityKN { get; set; }         // 0 = не несущий (террейн)
    public ushort DropItemId { get; set; }            // что выпадает при ломании
    public int DropItemCount { get; set; } = 1;       // сколько предметов выпадает
    public int PlaceItemCount { get; set; } = 1;      // сколько предметов нужно для установки
    public float PlaceContentVolumeM3 { get; set; } = 1f;  // объём материала в ячейке после установки
    public bool IsUnbreakable { get; set; }
    /// <summary>Это блок верстака — открывает 3×3 крафт при ПКМ.</summary>
    public bool IsWorkbench { get; set; }
    /// <summary>Это блок печки — открывает UI печки при ПКМ.</summary>
    public bool IsFurnace { get; set; }
}

/// <summary>Игровые данные: материалы, блоки, предметы, рецепты. Единый источник истины.</summary>
public static class GameData {
    // ── Материалы (плотность = источник массы, всё в СИ) ─────────────────────
    public static readonly Material Oak = new() {
        Id = 1, Name = "Дуб", DensityKgPerM3 = 760, CompressiveStrengthKPa = 8000, Category = MaterialCategory.Wood,
    };
    public static readonly Material Sawdust = new() {
        Id = 2, Name = "Опилки", DensityKgPerM3 = 760, Category = MaterialCategory.Wood, State = PhysicalState.Bulk,
    };
    public static readonly Material Stone = new() {
        Id = 3, Name = "Камень", DensityKgPerM3 = 2600, CompressiveStrengthKPa = 18000, Category = MaterialCategory.Stone,
    };
    public static readonly Material DirtM = new() {
        Id = 4, Name = "Земля", DensityKgPerM3 = 1500, Category = MaterialCategory.Soil,
    };
    public static readonly Material Coal = new() {
        Id = 5, Name = "Уголь", DensityKgPerM3 = 2600, Category = MaterialCategory.Metal,
    };
    public static readonly Material AppleM = new() {
        Id = 6, Name = "Яблоко", DensityKgPerM3 = 625, Category = MaterialCategory.Organic,
    };
    public static readonly Material Pork = new() {
        Id = 7, Name = "Свинина", DensityKgPerM3 = 667, Category = MaterialCategory.Organic,
    };
    public static readonly Material AshM = new() {
        Id = 8, Name = "Зола", DensityKgPerM3 = 400, Category = MaterialCategory.Soil, State = PhysicalState.Bulk,
    };
    public static readonly Material LeavesM = new() {
        Id = 9, Name = "Листва", DensityKgPerM3 = 250, Category = MaterialCategory.Wood,
    };
    public static readonly Material IronM = new() {
        Id = 10, Name = "Железо", DensityKgPerM3 = 7874, CompressiveStrengthKPa = 120000, Category = MaterialCategory.Metal,
    };
    public static readonly Material StoneToolM = new() {
        Id = 11, Name = "Каменный инструмент", DensityKgPerM3 = 2600, CompressiveStrengthKPa = 18000, Category = MaterialCategory.Stone,
    };
    public static readonly Material IronToolM = new() {
        Id = 12, Name = "Железный инструмент", DensityKgPerM3 = 7874, CompressiveStrengthKPa = 120000, Category = MaterialCategory.Metal,
    };
    public static readonly Material WoodToolM = new() {
        Id = 13, Name = "Деревянный инструмент", DensityKgPerM3 = 760, Category = MaterialCategory.Wood,
    };
    public static readonly Material DiamondM = new() {
        Id = 14, Name = "Алмаз", DensityKgPerM3 = 3510, CompressiveStrengthKPa = 500000, Category = MaterialCategory.Stone,
    };
    public static readonly Material GoldM = new() {
        Id = 15, Name = "Золото", DensityKgPerM3 = 19300, CompressiveStrengthKPa = 100000, Category = MaterialCategory.Metal,
    };
    public static readonly Material RedstoneM = new() {
        Id = 16, Name = "Редстоун", DensityKgPerM3 = 2200, Category = MaterialCategory.Stone,
    };
    public static readonly Material SandM = new() {
        Id = 17, Name = "Песок", DensityKgPerM3 = 1600, Category = MaterialCategory.Soil,
    };
    public static readonly Material StringM = new() {
        Id = 18, Name = "Нить", DensityKgPerM3 = 300, Category = MaterialCategory.Organic,
    };
    public static readonly Material BoneM = new() {
        Id = 19, Name = "Кость", DensityKgPerM3 = 1900, Category = MaterialCategory.Organic,
    };
    public static readonly Material BeefM = new() {
        Id = 20, Name = "Говядина", DensityKgPerM3 = 700, Category = MaterialCategory.Organic,
    };
    public static readonly Material LeatherM = new() {
        Id = 21, Name = "Кожа", DensityKgPerM3 = 860, Category = MaterialCategory.Organic,
    };
    public static readonly Material WoolM = new() {
        Id = 22, Name = "Шерсть", DensityKgPerM3 = 200, Category = MaterialCategory.Wood,
    };

    // ── Предметы ─────────────────────────────────────────────────────────────
    public static readonly ItemDefinition DirtItem = Item(1, "Земля", DirtM, 1.0);
    public static readonly ItemDefinition StoneItem = Item(2, "Камень", Stone, 1.0);
    public static readonly ItemDefinition LogItem = Item(3, "Бревно", Oak, 1.0);
    public static readonly ItemDefinition PlankItem = Item(4, "Доски", Oak, 0.4);
    public static readonly ItemDefinition StickItem = Item(6, "Палки", Oak, 0.04);
    public static readonly ItemDefinition CoalItem = Item(7, "Уголь", Coal, 0.02);
    public static readonly ItemDefinition CoalOreItem = Item(8, "Угольная руда", Stone, 1.0);
    public static readonly ItemDefinition TorchItem = Item(9, "Факел", Oak, 0.02);
    public static readonly ItemDefinition AppleItem = Item(13, "Яблоко", AppleM, 0.0004);
    public static readonly ItemDefinition RawPorkItem = Item(14, "Сырая свинина", Pork, 0.015);
    public static readonly ItemDefinition CookedPorkItem = Item(15, "Жареная свинина", Pork, 0.015);
    public static readonly ItemDefinition IronOreItem = Item(16, "Железная руда", Stone, 1.0);
    public static readonly ItemDefinition IronIngotItem = Item(17, "Железный слиток", IronM, 0.001);
    public static readonly ItemDefinition WoodPickaxeItem = Item(18, "Деревянная кирка", WoodToolM, 0.05);
    public static readonly ItemDefinition StonePickaxeItem = Item(19, "Каменная кирка", StoneToolM, 0.05);
    public static readonly ItemDefinition IronPickaxeItem = Item(20, "Железная кирка", IronToolM, 0.05);
    public static readonly ItemDefinition WoodAxeItem = Item(21, "Деревянный топор", WoodToolM, 0.05);
    public static readonly ItemDefinition StoneAxeItem = Item(22, "Каменный топор", StoneToolM, 0.05);
    public static readonly ItemDefinition IronAxeItem = Item(23, "Железный топор", IronToolM, 0.05);
    public static readonly ItemDefinition WoodSwordItem = Item(24, "Деревянный меч", WoodToolM, 0.04);
    public static readonly ItemDefinition StoneSwordItem = Item(25, "Каменный меч", StoneToolM, 0.04);
    public static readonly ItemDefinition IronSwordItem = Item(26, "Железный меч", IronToolM, 0.04);
    public static readonly ItemDefinition WoodShovelItem = Item(27, "Деревянная лопата", WoodToolM, 0.03);
    public static readonly ItemDefinition StoneShovelItem = Item(28, "Каменная лопата", StoneToolM, 0.03);
    public static readonly ItemDefinition IronShovelItem = Item(29, "Железная лопата", IronToolM, 0.03);
    public static readonly ItemDefinition WorkbenchItem = Item(30, "Верстак", Oak, 1.0);
    public static readonly ItemDefinition FurnaceItem = Item(31, "Печка", Stone, 1.0);
    public static readonly ItemDefinition BreadItem = Item(32, "Хлеб", Oak, 0.15);

    // Новые блоки и предметы MC Alpha 1
    public static readonly ItemDefinition GoldOreItem = Item(33, "Золотая руда", Stone, 1.0);
    public static readonly ItemDefinition GoldIngotItem = Item(34, "Золотой слиток", GoldM, 0.001);
    public static readonly ItemDefinition DiamondItem = Item(35, "Алмаз", DiamondM, 0.0005);
    public static readonly ItemDefinition DiamondOreItem = Item(36, "Алмазная руда", Stone, 1.0);
    public static readonly ItemDefinition RedstoneItem = Item(37, "Редстоун пыль", RedstoneM, 0.001);
    public static readonly ItemDefinition SandItem = Item(38, "Песок", SandM, 1.0);
    public static readonly ItemDefinition GravelItem = Item(39, "Гравий", Stone, 1.0);
    public static readonly ItemDefinition CobblestoneItem = Item(40, "Булыжник", Stone, 1.0);
    public static readonly ItemDefinition GlassItem = Item(41, "Стекло", Stone, 1.0);
    public static readonly ItemDefinition ObsidianItem = Item(42, "Обсидиан", DiamondM, 1.0);
    public static readonly ItemDefinition DiamondPickaxeItem = Item(43, "Алмазная кирка", DiamondM, 0.05);
    public static readonly ItemDefinition DiamondAxeItem = Item(44, "Алмазный топор", DiamondM, 0.05);
    public static readonly ItemDefinition DiamondSwordItem = Item(45, "Алмазный меч", DiamondM, 0.04);
    public static readonly ItemDefinition DiamondShovelItem = Item(46, "Алмазная лопата", DiamondM, 0.03);

    public static readonly ItemDefinition FeatherItem = Item(47, "Перо", LeavesM, 0.0001);
    public static readonly ItemDefinition GunpowderItem = Item(48, "Порох", Coal, 0.0005);
    public static readonly ItemDefinition StringItem = Item(49, "Нить", StringM, 0.0002);
    public static readonly ItemDefinition ArrowItem = Item(50, "Стрела", WoodToolM, 0.001);
    public static readonly ItemDefinition BoneItem = Item(51, "Кость", BoneM, 0.002);
    public static readonly ItemDefinition CharcoalItem = Item(52, "Древесный уголь", Coal, 0.02);
    public static readonly ItemDefinition RawBeefItem = Item(53, "Сырая говядина", BeefM, 0.02);
    public static readonly ItemDefinition CookedBeefItem = Item(54, "Жареная говядина", BeefM, 0.02);
    public static readonly ItemDefinition LeatherItem = Item(55, "Кожа", LeatherM, 0.005);
    public static readonly ItemDefinition WhiteWoolItem = Item(56, "Шерсть", WoolM, 0.02);
    public static readonly ItemDefinition ChestItem = Item(57, "Сундук", Oak, 0.05);
    public static readonly ItemDefinition BedItem = Item(58, "Кровать", Oak, 0.08);
    public static readonly ItemDefinition RottenFleshItem = Item(59, "Гнилая плоть", BeefM, 0.02);
    public static readonly ItemDefinition WoodHoeItem = Item(60, "Деревянная мотыга", WoodToolM, 0.03);
    public static readonly ItemDefinition StoneHoeItem = Item(61, "Каменная мотыга", StoneToolM, 0.03);
    public static readonly ItemDefinition IronHoeItem = Item(62, "Железная мотыга", IronToolM, 0.03);
    public static readonly ItemDefinition DiamondHoeItem = Item(63, "Алмазная мотыга", DiamondM, 0.03);
    public static readonly ItemDefinition WheatItem = Item(64, "Пшеница", LeavesM, 0.01);
    public static readonly ItemDefinition WheatSeedsItem = Item(65, "Семена пшеницы", LeavesM, 0.001);
    public static readonly ItemDefinition BoneMealItem = Item(66, "Костная мука", BoneM, 0.001);
    public static readonly ItemDefinition SawdustItem = Item(67, "Древесные опилки", Oak, 0.001);
    public static readonly ItemDefinition SawdustPorridgeItem = Item(68, "Каша из опилок", Oak, 0.05);
    public static readonly ItemDefinition TotemItem = Item(69, "Тотем бессмертия", GoldM, 0.02);
    public static readonly ItemDefinition RawMuttonItem = Item(70, "Сырая баранина", BeefM, 0.02);
    public static readonly ItemDefinition CookedMuttonItem = Item(71, "Жареная баранина", BeefM, 0.02);
    public static readonly ItemDefinition BowItem = Item(72, "Лук", Oak, 0.05);
    public static readonly ItemDefinition ShieldItem = Item(73, "Щит", Oak, 0.10);
    public static readonly ItemDefinition FlintItem = Item(74, "Кремень", Stone, 0.001);
    public static readonly ItemDefinition FlintAndSteelItem = Item(75, "Огниво", IronM, 0.005);
    public static readonly ItemDefinition GoldenAppleItem = Item(76, "Золотое яблоко", GoldM, 0.01);
    public static readonly ItemDefinition SaddleItem = Item(77, "Седло", LeatherM, 0.02);
    public static readonly ItemDefinition EnchantedBookItem = Item(78, "Зачарованная книга", LeatherM, 0.01);
    public static readonly ItemDefinition MusicDiscItem = Item(79, "Музыкальная пластинка", DiamondM, 0.01);
    public static readonly ItemDefinition NetherQuartzItem = Item(80, "Кварц", DiamondM, 0.001);
    public static readonly ItemDefinition BlazeRodItem = Item(81, "Стержень ифрита", GoldM, 0.01);
    public static readonly ItemDefinition GlowstoneDustItem = Item(82, "Светопыль", RedstoneM, 0.001);
    public static readonly ItemDefinition TNTItem = Item(83, "Динамит", SandM, 0.5);
    public static readonly ItemDefinition NetherrackItem = Item(84, "Адский камень", Stone, 1.0);
    public static readonly ItemDefinition SoulSandItem = Item(85, "Песок душ", SandM, 1.0);
    public static readonly ItemDefinition GlowstoneItem = Item(86, "Светокамень", Stone, 1.0);
    public static readonly ItemDefinition NetherQuartzOreItem = Item(87, "Кварцевая руда", Stone, 1.0);
    public static readonly ItemDefinition NetherBrickItem = Item(88, "Адский кирпич", Stone, 1.0);
    public static readonly ItemDefinition MossyCobblestoneItem = Item(89, "Замшелый булыжник", Stone, 1.0);
    public static readonly ItemDefinition ChiseledSandstoneItem = Item(90, "Резной песчаник", SandM, 1.0);
    public static readonly ItemDefinition RailItem = Item(91, "Рельсы", IronM, 0.02);

    // ── Блоки ─────────────────────────────────────────────────────────────────
    public static readonly BlockType BGrass = Block(1, "Трава", DirtM, drop: DirtItem);
    public static readonly BlockType BDirt = Block(2, "Земля", DirtM, drop: DirtItem);
    public static readonly BlockType BStone = Block(3, "Камень", Stone, drop: CobblestoneItem);
    public static readonly BlockType BLog = Block(4, "Бревно", Oak, drop: LogItem, flammable: true, burnTime: 8f);
    public static readonly BlockType BLeaves = Block(5, "Листва", LeavesM, drop: null, flammable: true, burnTime: 4f)
        .With(b => { b.IsSolid = true; b.IsOpaque = false; });
    public static readonly BlockType BPlanks = Block(6, "Доски", Oak, drop: PlankItem, flammable: true, burnTime: 7f);
    public static readonly BlockType BCoalOre = Block(7, "Угольная руда", Stone, drop: CoalItem);
    public static readonly BlockType BTorch = Block(8, "Факел", Oak, drop: TorchItem, light: 11)
        .With(b => { b.IsSolid = false; b.IsOpaque = false; });
    public static readonly BlockType BBedrock = Block(11, "Коренная порода", Stone, drop: null).With(b => b.IsUnbreakable = true);
    public static readonly BlockType BIronOre = Block(12, "Железная руда", Stone, drop: IronOreItem);
    public static readonly BlockType BWorkbench = Block(13, "Верстак", Oak, drop: WorkbenchItem, flammable: true, burnTime: 6f)
        .With(b => b.IsWorkbench = true);
    public static readonly BlockType BFurnace = Block(14, "Печка", Stone, drop: FurnaceItem)
        .With(b => b.IsFurnace = true);
    public static readonly BlockType BCobblestone = Block(15, "Булыжник", Stone, drop: CobblestoneItem);
    public static readonly BlockType BSand = Block(16, "Песок", SandM, drop: SandItem);
    public static readonly BlockType BGravel = Block(17, "Гравий", Stone, drop: GravelItem);
    public static readonly BlockType BGlass = Block(18, "Стекло", Stone, drop: null).With(b => { b.IsOpaque = false; });
    public static readonly BlockType BWater = Block(19, "Вода", DirtM, drop: null).With(b => { b.IsSolid = false; b.IsOpaque = false; });
    public static readonly BlockType BLava = Block(20, "Лава", Stone, drop: null, light: 15).With(b => { b.IsSolid = false; b.IsOpaque = true; });
    public static readonly BlockType BGoldOre = Block(21, "Золотая руда", Stone, drop: GoldOreItem);
    public static readonly BlockType BDiamondOre = Block(22, "Алмазная руда", Stone, drop: DiamondItem);
    public static readonly BlockType BRedstoneOre = Block(23, "Редстоун руда", Stone, drop: RedstoneItem);
    public static readonly BlockType BObsidian = Block(24, "Обсидиан", DiamondM, drop: ObsidianItem);
    public static readonly BlockType BChest = Block(25, "Сундук", Oak, drop: ChestItem, flammable: true, burnTime: 8f);
    public static readonly BlockType BBed = Block(26, "Кровать", Oak, drop: BedItem, flammable: true, burnTime: 6f);
    public static readonly BlockType BBedHead = Block(27, "Кровать (изголовье)", Oak, drop: BedItem, flammable: true, burnTime: 6f);
    public static readonly BlockType BFarmland = Block(28, "Грядка", DirtM, drop: DirtItem);
    public static readonly BlockType BWheatCrop = Block(29, "Посевы пшеницы", LeavesM, drop: WheatSeedsItem)
        .With(b => { b.IsSolid = false; b.IsOpaque = false; });
    public static readonly BlockType BTallGrass = Block(30, "Трава", LeavesM, drop: WheatSeedsItem)
        .With(b => { b.IsSolid = false; b.IsOpaque = false; });
    public static readonly BlockType BMossyCobblestone = Block(31, "Замшелый булыжник", Stone, drop: MossyCobblestoneItem);
    public static readonly BlockType BMobSpawner = Block(32, "Спавнер монстров", Stone, drop: null)
        .With(b => { b.IsOpaque = false; });
    public static readonly BlockType BWeb = Block(33, "Паутина", StringM, drop: StringItem)
        .With(b => { b.IsSolid = false; b.IsOpaque = false; });
    public static readonly BlockType BRail = Block(34, "Рельсы", IronM, drop: RailItem)
        .With(b => { b.IsSolid = false; b.IsOpaque = false; });
    public static readonly BlockType BPressurePlate = Block(35, "Нажимная плита", Stone, drop: StoneItem)
        .With(b => { b.IsSolid = false; b.IsOpaque = false; });
    public static readonly BlockType BTNT = Block(36, "Динамит", SandM, drop: TNTItem, flammable: true, burnTime: 0.1f);
    public static readonly BlockType BChiseledSandstone = Block(37, "Резной песчаник", SandM, drop: ChiseledSandstoneItem);
    public static readonly BlockType BNetherrack = Block(38, "Адский камень", Stone, drop: NetherrackItem, flammable: true, burnTime: 99999f);
    public static readonly BlockType BSoulSand = Block(39, "Песок душ", SandM, drop: SoulSandItem);
    public static readonly BlockType BGlowstone = Block(40, "Светокамень", Stone, drop: GlowstoneDustItem, light: 15)
        .With(b => { b.DropItemCount = 3; });
    public static readonly BlockType BNetherQuartzOre = Block(41, "Кварцевая руда", Stone, drop: NetherQuartzItem);
    public static readonly BlockType BNetherBrick = Block(42, "Адский кирпич", Stone, drop: NetherBrickItem);
    public static readonly BlockType BNetherPortal = Block(43, "Портал в Нижний мир", DiamondM, drop: null, light: 11)
        .With(b => { b.IsSolid = false; b.IsOpaque = false; b.IsUnbreakable = true; });

    public static readonly BlockType[] Blocks =
        { BGrass, BDirt, BStone, BLog, BLeaves, BPlanks, BCoalOre, BTorch, BBedrock, BIronOre, BWorkbench, BFurnace,
          BCobblestone, BSand, BGravel, BGlass, BWater, BLava, BGoldOre, BDiamondOre, BRedstoneOre, BObsidian, BChest, BBed, BBedHead,
          BFarmland, BWheatCrop, BTallGrass, BMossyCobblestone, BMobSpawner, BWeb, BRail, BPressurePlate, BTNT, BChiseledSandstone,
          BNetherrack, BSoulSand, BGlowstone, BNetherQuartzOre, BNetherBrick, BNetherPortal };


    private static readonly Dictionary<ushort, BlockType> _byId = Blocks.ToDictionary(b => b.Id);
    private static readonly Dictionary<ushort, ushort> _blockByItem =
        Blocks.Where(b => b.DropItemId != 0 && b.Id != BWheatCrop.Id && b.Id != BTallGrass.Id)
              .GroupBy(b => b.DropItemId)
              .ToDictionary(g => g.Key, g => g.First().Id); // grass/dirt share the dirt item

    /// <summary>Возвращает блок по его Id.</summary>
    public static BlockType GetBlock(ushort id) => _byId.TryGetValue(id, out var b) ? b : throw new KeyNotFoundException($"Блок #{id}");

    public static bool TryGetBlockByItem(ushort itemId, out BlockType? block) {
        if (_blockByItem.TryGetValue(itemId, out ushort bid)) {
            block = _byId[bid];
            return true;
        }
        block = null;
        return false;
    }

    /// <summary>Пища: сколько HP восстанавливает предмет.</summary>
    public static readonly Dictionary<ushort, float> FoodValue = new() {
        { AppleItem.Id, 4f },        // Яблоко: +4 HP
        { RawPorkItem.Id, 3f },      // Сырая свинина: +3 HP
        { CookedPorkItem.Id, 8f },   // Жареная свинина: +8 HP
        { BreadItem.Id, 5f },        // Хлеб: +5 HP
        { RawBeefItem.Id, 4f },      // Сырая говядина: +4 HP
        { CookedBeefItem.Id, 8f },   // Жареный стейк: +8 HP
        { RottenFleshItem.Id, 2f },  // Гнилая плоть: +2 HP
        { SawdustPorridgeItem.Id, 4f }, // Каша из опилок: +4 HP
        { RawMuttonItem.Id, 2f },    // Сырая баранина: +2 HP
        { CookedMuttonItem.Id, 6f }, // Жареная баранина: +6 HP
        { GoldenAppleItem.Id, 10f }, // Золотое яблоко: +10 HP
    };

    // ── Реестр предметов + менеджер материи (консервация) ────────────────────
    public static readonly MaterialVolumeManager Materials = new();

    public static readonly Dictionary<ushort, ItemDefinition> Items = new();
    private static ulong _nextInstanceId;

    public static ItemInstance NewItem(ItemDefinition def) => new(def, _nextInstanceId++);

    // ── Shape-based крафт ──────────────────────────────────────────────────────
    /// <summary>Ключ — нормализованный паттерн сетки 3×3. Значение — (выходной предмет, количество).</summary>
    public static readonly Dictionary<string, (ItemDefinition Item, int Count)> ShapeRecipes = new();

    // ── Рецепты плавки ────────────────────────────────────────────────────────
    /// <summary>Входной item ID → (выходной предмет, количество).</summary>
    public static readonly Dictionary<ushort, (ItemDefinition Output, int Count)> SmeltingRecipes = new();

    // ── Тир инструментов ──────────────────────────────────────────────────────
    /// <summary>Уровень инструмента: 0=руки, 1=дерево, 2=камень, 3=железо, 5=алмаз.</summary>
    public static int GetToolTier(ushort itemId) {
        if (itemId == WoodPickaxeItem.Id || itemId == WoodAxeItem.Id || itemId == WoodShovelItem.Id || itemId == WoodSwordItem.Id || itemId == WoodHoeItem.Id) return 1;
        if (itemId == StonePickaxeItem.Id || itemId == StoneAxeItem.Id || itemId == StoneShovelItem.Id || itemId == StoneSwordItem.Id || itemId == StoneHoeItem.Id) return 2;
        if (itemId == IronPickaxeItem.Id || itemId == IronAxeItem.Id || itemId == IronShovelItem.Id || itemId == IronSwordItem.Id || itemId == IronHoeItem.Id) return 3;
        if (itemId == DiamondPickaxeItem.Id || itemId == DiamondAxeItem.Id || itemId == DiamondShovelItem.Id || itemId == DiamondSwordItem.Id || itemId == DiamondHoeItem.Id) return 5;
        return 0;
    }

    public static int GetMaxToolDurability(ushort itemId) {
        if (itemId == WoodPickaxeItem.Id || itemId == WoodAxeItem.Id || itemId == WoodShovelItem.Id || itemId == WoodSwordItem.Id || itemId == WoodHoeItem.Id) return 60;
        if (itemId == StonePickaxeItem.Id || itemId == StoneAxeItem.Id || itemId == StoneShovelItem.Id || itemId == StoneSwordItem.Id || itemId == StoneHoeItem.Id) return 132;
        if (itemId == IronPickaxeItem.Id || itemId == IronAxeItem.Id || itemId == IronShovelItem.Id || itemId == IronSwordItem.Id || itemId == IronHoeItem.Id) return 251;
        if (itemId == DiamondPickaxeItem.Id || itemId == DiamondAxeItem.Id || itemId == DiamondShovelItem.Id || itemId == DiamondSwordItem.Id || itemId == DiamondHoeItem.Id) return 1562;
        return 60;
    }

    public static bool RequiresPickaxe(ushort blockId) =>
        blockId == BStone.Id || blockId == BCobblestone.Id || blockId == BCoalOre.Id ||
        blockId == BIronOre.Id || blockId == BGoldOre.Id || blockId == BDiamondOre.Id ||
        blockId == BRedstoneOre.Id || blockId == BFurnace.Id || blockId == BObsidian.Id;

    /// <summary>Минимальный тир инструмента для добычи блока. 0 = можно голыми руками.</summary>
    public static int GetRequiredTier(ushort blockId) {
        if (blockId == BStone.Id || blockId == BCoalOre.Id || blockId == BFurnace.Id || blockId == BCobblestone.Id) return 1;
        if (blockId == BIronOre.Id) return 2;
        if (blockId == BGoldOre.Id || blockId == BDiamondOre.Id || blockId == BRedstoneOre.Id) return 3;
        if (blockId == BObsidian.Id) return 5; // Алмазная кирка
        return 0;
    }

    public static bool CanHarvestBlock(BlockType b, ushort toolId) {
        if (RequiresPickaxe(b.Id)) {
            if (!IsPickaxe(toolId)) return false;
            return GetToolTier(toolId) >= GetRequiredTier(b.Id);
        }
        // Дерево, доски, верстак, земля, песок, факелы и т.д. можно добыть рукой!
        return true;
    }

    public static bool IsPickaxe(ushort itemId) =>
        itemId == WoodPickaxeItem.Id || itemId == StonePickaxeItem.Id || itemId == IronPickaxeItem.Id || itemId == DiamondPickaxeItem.Id;

    public static bool IsAxe(ushort itemId) =>
        itemId == WoodAxeItem.Id || itemId == StoneAxeItem.Id || itemId == IronAxeItem.Id || itemId == DiamondAxeItem.Id;

    public static bool IsShovel(ushort itemId) =>
        itemId == WoodShovelItem.Id || itemId == StoneShovelItem.Id || itemId == IronShovelItem.Id || itemId == DiamondShovelItem.Id;

    public static bool IsSword(ushort itemId) =>
        itemId == WoodSwordItem.Id || itemId == StoneSwordItem.Id || itemId == IronSwordItem.Id || itemId == DiamondSwordItem.Id;

    public static bool IsHoe(ushort itemId) =>
        itemId == WoodHoeItem.Id || itemId == StoneHoeItem.Id || itemId == IronHoeItem.Id || itemId == DiamondHoeItem.Id;

    /// <summary>
    /// Детерминированный парсинг сида (FNV-1a), не зависящий от рандомизации .NET процесса.
    /// Два игрока с одинаковым текстом сида гарантированно получат одинаковый мир.
    /// </summary>
    public static int ParseSeed(string seedInput) {
        if (string.IsNullOrWhiteSpace(seedInput)) return new Random().Next();
        if (int.TryParse(seedInput.Trim(), out int numericSeed)) return numericSeed;
        unchecked {
            int hash = (int)2166136261;
            foreach (char c in seedInput.Trim()) {
                hash = (hash ^ c) * 16777619;
            }
            return hash;
        }
    }

    public static float GetWeaponDamage(ushort itemId) {
        if (itemId == DiamondSwordItem.Id) return 7f;   // Алмазный меч: 7 HP (3.5 сердца)
        if (itemId == IronSwordItem.Id) return 6f;      // Железный меч: 6 HP (3 сердца)
        if (itemId == StoneSwordItem.Id) return 5f;     // Каменный меч: 5 HP (2.5 сердца)
        if (itemId == WoodSwordItem.Id) return 4f;      // Деревянный меч: 4 HP (2 сердца)
        
        if (itemId == DiamondAxeItem.Id) return 6f;     // Алмазный топор: 6 HP
        if (itemId == IronAxeItem.Id) return 5f;        // Железный топор: 5 HP
        if (itemId == StoneAxeItem.Id) return 4f;       // Каменный топор: 4 HP
        if (itemId == WoodAxeItem.Id) return 3f;        // Деревянный топор: 3 HP

        if (itemId == DiamondPickaxeItem.Id) return 5f; // Алмазная кирка: 5 HP
        if (itemId == IronPickaxeItem.Id) return 4f;    // Железная кирка: 4 HP
        if (itemId == StonePickaxeItem.Id) return 3f;   // Каменная кирка: 3 HP
        if (itemId == WoodPickaxeItem.Id) return 2f;    // Деревянная кирка: 2 HP

        if (itemId == DiamondShovelItem.Id) return 4f;  // Алмазная лопата: 4 HP
        if (itemId == IronShovelItem.Id) return 3f;     // Железная лопата: 3 HP
        if (itemId == StoneShovelItem.Id) return 2f;    // Каменная лопата: 2 HP
        if (itemId == WoodShovelItem.Id) return 1f;     // Деревянная лопата: 1 HP

        return 1f; // Рука / любой другой предмет: 1 HP (0.5 сердца)
    }

    /// <summary>
    /// Длительность перезарядки атаки оружия (Minecraft 1.9+):
    /// Мечи ~0.625с (1.6 ск/с), Топоры ~1.0с (1.0 ск/с), Кирки ~0.83с, Рука/Мотыга ~0.35с
    /// </summary>
    public static float GetWeaponCooldown(ushort itemId) {
        if (IsSword(itemId)) return 0.625f;
        if (IsAxe(itemId)) return 1.0f;
        if (IsPickaxe(itemId)) return 0.833f;
        if (IsShovel(itemId)) return 1.0f;
        if (IsHoe(itemId)) return 0.35f;
        return 0.35f;
    }

    // ── Добыча блоков ─────────────────────────────────────────────────────────

    public static float GetBlockHardness(BlockType b) {
        if (b.Id == BTorch.Id) return 0.2f;
        if (b.Id == BBed.Id || b.Id == BBedHead.Id) return 0.2f;
        if (b.Id == BWheatCrop.Id) return 0.0f;
        if (b.Id == BTallGrass.Id) return 0.0f;
        if (b.Id == BFarmland.Id) return 0.6f;
        if (b.Id == BCoalOre.Id) return 3.0f;
        if (b.Id == BIronOre.Id || b.Id == BGoldOre.Id) return 3.0f;
        if (b.Id == BDiamondOre.Id || b.Id == BRedstoneOre.Id) return 3.0f;
        if (b.Id == BObsidian.Id) return 25f;
        if (b.Id == BCobblestone.Id) return 2.0f;
        if (b.Id == BStone.Id) return 1.5f;
        if (b.Id == BSand.Id || b.Id == BGravel.Id) return 0.6f;
        if (b.Id == BGlass.Id) return 0.3f;
        if (b.Id == BLeaves.Id) return 0.2f;
        if (b.Id == BWorkbench.Id) return 2.5f;
        return b.Material.Category switch {
            MaterialCategory.Soil => 0.5f,
            MaterialCategory.Wood => 2.0f,
            MaterialCategory.Stone => 1.5f,
            MaterialCategory.Metal => 5f,
            _ => 0.5f,
        };
    }

    public static float GetMiningSpeedMultiplier(BlockType b, ushort toolId) {
        int tier = GetToolTier(toolId);
        if (tier == 0) return 1f;

        bool isWoodBlock = b.Material.Category == MaterialCategory.Wood || b.Id == BLog.Id || b.Id == BPlanks.Id || b.Id == BWorkbench.Id;
        bool isStoneBlock = b.Material.Category == MaterialCategory.Stone || b.Material.Category == MaterialCategory.Metal || RequiresPickaxe(b.Id);
        bool isSoilBlock = b.Material.Category == MaterialCategory.Soil || b.Id == BDirt.Id || b.Id == BGrass.Id || b.Id == BSand.Id || b.Id == BGravel.Id;

        if (IsPickaxe(toolId) && isStoneBlock) {
            return tier switch { 1 => 2f, 2 => 4f, 3 => 6f, 5 => 8f, _ => 1f };
        }
        if (IsAxe(toolId) && isWoodBlock) {
            return tier switch { 1 => 2f, 2 => 4f, 3 => 6f, 5 => 8f, _ => 1f };
        }
        if (IsShovel(toolId) && isSoilBlock) {
            return tier switch { 1 => 2f, 2 => 4f, 3 => 6f, 5 => 8f, _ => 1f };
        }
        if (IsSword(toolId) && b.Id == BLeaves.Id) {
            return 3f;
        }

        return 1f;
    }

    public static float GetMiningTime(BlockType b, ItemDefinition? tool) {
        ushort toolId = tool?.Id ?? 0;
        float baseHardness = GetBlockHardness(b);
        float speed = GetMiningSpeedMultiplier(b, toolId);
        bool canHarvest = CanHarvestBlock(b, toolId);

        if (!canHarvest) {
            return baseHardness * 5.0f; // Не тот инструмент или нет нужного тира: долго ломается и не дропается
        }

        if (speed > 1.0f) {
            return (baseHardness * 1.5f) / speed;
        }

        return baseHardness * 1.5f;
    }

    public static string MiningRequirementHint(BlockType b) {
        int tier = GetRequiredTier(b.Id);
        return tier switch {
            1 => $"Для «{b.Name}» нужна кирка (деревянная или лучше)",
            2 => $"Для «{b.Name}» нужна каменная кирка или лучше",
            3 => $"Для «{b.Name}» нужна железная кирка или лучше",
            5 => $"Для «{b.Name}» нужна алмазная кирка",
            _ => $"«{b.Name}» добывается любым инструментом или руками",
        };
    }

    static GameData() {
        _blockByItem[DirtItem.Id] = BDirt.Id;
        _blockByItem[SandItem.Id] = BSand.Id;
        _blockByItem[GravelItem.Id] = BGravel.Id;
        _blockByItem[CobblestoneItem.Id] = BCobblestone.Id;
        _blockByItem[GlassItem.Id] = BGlass.Id;
        _blockByItem[ObsidianItem.Id] = BObsidian.Id;
        _blockByItem[ChestItem.Id] = BChest.Id;
        _blockByItem[BedItem.Id] = BBed.Id;

        foreach (var m in new[] { Oak, Stone, DirtM, Coal, AppleM, Pork, LeavesM,
                                   IronM, StoneToolM, IronToolM, WoodToolM, DiamondM, GoldM, RedstoneM, SandM, StringM, BoneM,
                                   BeefM, LeatherM, WoolM })
            Materials.RegisterMaterial(m);
        foreach (var item in new[] {
            DirtItem, StoneItem, LogItem, PlankItem, StickItem, CoalItem,
            CoalOreItem, TorchItem, AppleItem, RawPorkItem, CookedPorkItem,
            IronOreItem, IronIngotItem,
            WoodPickaxeItem, StonePickaxeItem, IronPickaxeItem,
            WoodAxeItem, StoneAxeItem, IronAxeItem,
            WoodSwordItem, StoneSwordItem, IronSwordItem,
            WoodShovelItem, StoneShovelItem, IronShovelItem,
            WorkbenchItem, FurnaceItem, BreadItem,
            GoldOreItem, GoldIngotItem, DiamondItem, DiamondOreItem, RedstoneItem,
            SandItem, GravelItem, CobblestoneItem, GlassItem, ObsidianItem,
            DiamondPickaxeItem, DiamondAxeItem, DiamondSwordItem, DiamondShovelItem,
            FeatherItem, GunpowderItem, StringItem, ArrowItem, BoneItem,
            CharcoalItem, RawBeefItem, CookedBeefItem, LeatherItem, WhiteWoolItem,
            ChestItem, BedItem, RottenFleshItem,
            WoodHoeItem, StoneHoeItem, IronHoeItem, DiamondHoeItem,
            WheatItem, WheatSeedsItem,
            BoneMealItem, SawdustItem, SawdustPorridgeItem, TotemItem,
            RawMuttonItem, CookedMuttonItem
        }) {
            Items.Add(item.Id, item);
        }


        MaterialVolumeManager.EnableConservationChecks = false;

        // Старые list-based рецепты (для совместимости с сохранениями)
        Register("cook.pork", "Жарка свинины", 4f,
            new[] { new ItemPart(RawPorkItem, 1) }, new ItemPart(CookedPorkItem, 1));

        InitShapeRecipes();
        InitSmeltingRecipes();
    }

    // ── Shape-based рецепты ───────────────────────────────────────────────────

    private static void AddShapeRecipe(ItemDefinition?[] grid9, ItemDefinition output, int count) {
        string key = NormalizeGrid(grid9);
        if (!string.IsNullOrEmpty(key))
            ShapeRecipes[key] = (output, count);
    }

    /// <summary>
    /// Нормализует сетку 3×3: обрезает до минимального ограничивающего прямоугольника,
    /// затем кодирует как строку "id,id,...;id,id,...;...". 0 = пусто.
    /// </summary>
    public static string NormalizeGrid(ItemDefinition?[] grid9) {
        int minRow = 3, maxRow = -1, minCol = 3, maxCol = -1;
        for (int r = 0; r < 3; r++) {
            for (int c = 0; c < 3; c++) {
                if (grid9[r * 3 + c] != null) {
                    if (r < minRow) minRow = r;
                    if (r > maxRow) maxRow = r;
                    if (c < minCol) minCol = c;
                    if (c > maxCol) maxCol = c;
                }
            }
        }
        if (maxRow < 0) return string.Empty;
        var sb = new System.Text.StringBuilder();
        for (int r = minRow; r <= maxRow; r++) {
            for (int c = minCol; c <= maxCol; c++) {
                var item = grid9[r * 3 + c];
                sb.Append(item == null ? "0" : item.Id.ToString());
                sb.Append(',');
            }
            sb.Append(';');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Чистый крафт по форме сетки 3×3 (без Raylib): нормализует сетку, ищет рецепт,
    /// списывает ингредиенты из инвентаря и кладёт результат. Атомарно: если нет
    /// рецепта, не хватает ингредиентов или нет места под результат — инвентарь
    /// не меняется. Работает в headless-режиме для тестов.
    /// </summary>
    public static bool TryCraftShape(ItemDefinition?[] grid9, VoxelFrame.Core.Inventory.Container inv,
                                     out (ItemDefinition Item, int Count) result) {
        result = default;
        string key = NormalizeGrid(grid9);
        if (key.Length == 0 || !ShapeRecipes.TryGetValue(key, out result)) return false;

        // Считаем, сколько каждого типа нужно
        var needed = new Dictionary<ushort, int>();
        foreach (var item in grid9) {
            if (item == null) continue;
            needed.TryGetValue(item.Id, out int cur);
            needed[item.Id] = cur + 1;
        }
        // Проверяем наличие в инвентаре
        foreach (var (id, count) in needed) {
            if (!Items.TryGetValue(id, out var def) || inv.CountOf(def) < count) return false;
        }
        // Списываем ингредиенты
        foreach (var (id, count) in needed) {
            Items.TryGetValue(id, out var def);
            inv.TryRemove(def!, count);
        }
        // Кладём результат; если места нет — откатываем списание
        if (!inv.TryInsert(NewItem(result.Item), result.Count)) {
            foreach (var (id, count) in needed) {
                Items.TryGetValue(id, out var def);
                inv.TryInsert(NewItem(def!), count);
            }
            return false;
        }
        return true;
    }

    private static void InitShapeRecipes() {
        // Бревно → доски (1 бревно в любой ячейке)
        AddShapeRecipe(new ItemDefinition?[] { LogItem,null,null, null,null,null, null,null,null }, PlankItem, 4);

        // Доски → палки (2 доски вертикально)
        AddShapeRecipe(new ItemDefinition?[] { null,PlankItem,null, null,PlankItem,null, null,null,null }, StickItem, 4);
        AddShapeRecipe(new ItemDefinition?[] { PlankItem,null,null, PlankItem,null,null, null,null,null }, StickItem, 4);
        AddShapeRecipe(new ItemDefinition?[] { null,null,PlankItem, null,null,PlankItem, null,null,null }, StickItem, 4);

        // Факел: уголь над палкой
        AddShapeRecipe(new ItemDefinition?[] { null,CoalItem,null, null,StickItem,null, null,null,null }, TorchItem, 4);
        AddShapeRecipe(new ItemDefinition?[] { CoalItem,null,null, StickItem,null,null, null,null,null }, TorchItem, 4);

        // Верстак: 2×2 доски
        AddShapeRecipe(new ItemDefinition?[] { PlankItem,PlankItem,null, PlankItem,PlankItem,null, null,null,null }, WorkbenchItem, 1);

        // Печка: 8 булыжников вокруг пустого центра
        AddShapeRecipe(new ItemDefinition?[] { CobblestoneItem,CobblestoneItem,CobblestoneItem, CobblestoneItem,null,CobblestoneItem, CobblestoneItem,CobblestoneItem,CobblestoneItem }, FurnaceItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { StoneItem,StoneItem,StoneItem, StoneItem,null,StoneItem, StoneItem,StoneItem,StoneItem }, FurnaceItem, 1);

        // Кирки
        AddShapeRecipe(new ItemDefinition?[] { PlankItem,PlankItem,PlankItem, null,StickItem,null, null,StickItem,null }, WoodPickaxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { CobblestoneItem,CobblestoneItem,CobblestoneItem, null,StickItem,null, null,StickItem,null }, StonePickaxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { StoneItem,StoneItem,StoneItem, null,StickItem,null, null,StickItem,null }, StonePickaxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { IronIngotItem,IronIngotItem,IronIngotItem, null,StickItem,null, null,StickItem,null }, IronPickaxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { DiamondItem,DiamondItem,DiamondItem, null,StickItem,null, null,StickItem,null }, DiamondPickaxeItem, 1);

        // Топоры (левые и правые)
        AddShapeRecipe(new ItemDefinition?[] { PlankItem,PlankItem,null, PlankItem,StickItem,null, null,StickItem,null }, WoodAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,PlankItem,PlankItem, null,StickItem,PlankItem, null,StickItem,null }, WoodAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { CobblestoneItem,CobblestoneItem,null, CobblestoneItem,StickItem,null, null,StickItem,null }, StoneAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,CobblestoneItem,CobblestoneItem, null,StickItem,CobblestoneItem, null,StickItem,null }, StoneAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { StoneItem,StoneItem,null, StoneItem,StickItem,null, null,StickItem,null }, StoneAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { IronIngotItem,IronIngotItem,null, IronIngotItem,StickItem,null, null,StickItem,null }, IronAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,IronIngotItem,IronIngotItem, null,StickItem,IronIngotItem, null,StickItem,null }, IronAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { DiamondItem,DiamondItem,null, DiamondItem,StickItem,null, null,StickItem,null }, DiamondAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,DiamondItem,DiamondItem, null,StickItem,DiamondItem, null,StickItem,null }, DiamondAxeItem, 1);

        // Мечи
        AddShapeRecipe(new ItemDefinition?[] { null,PlankItem,null, null,PlankItem,null, null,StickItem,null }, WoodSwordItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,CobblestoneItem,null, null,CobblestoneItem,null, null,StickItem,null }, StoneSwordItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,StoneItem,null, null,StoneItem,null, null,StickItem,null }, StoneSwordItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,IronIngotItem,null, null,IronIngotItem,null, null,StickItem,null }, IronSwordItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,DiamondItem,null, null,DiamondItem,null, null,StickItem,null }, DiamondSwordItem, 1);

        // Лопаты
        AddShapeRecipe(new ItemDefinition?[] { null,PlankItem,null, null,StickItem,null, null,StickItem,null }, WoodShovelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,CobblestoneItem,null, null,StickItem,null, null,StickItem,null }, StoneShovelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,StoneItem,null, null,StickItem,null, null,StickItem,null }, StoneShovelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,IronIngotItem,null, null,StickItem,null, null,StickItem,null }, IronShovelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,DiamondItem,null, null,StickItem,null, null,StickItem,null }, DiamondShovelItem, 1);

        // Факел из древесного угля
        AddShapeRecipe(new ItemDefinition?[] { null,CharcoalItem,null, null,StickItem,null, null,null,null }, TorchItem, 4);
        AddShapeRecipe(new ItemDefinition?[] { CharcoalItem,null,null, StickItem,null,null, null,null,null }, TorchItem, 4);

        // Шерсть из нитей (2×2 нити)
        AddShapeRecipe(new ItemDefinition?[] { StringItem,StringItem,null, StringItem,StringItem,null, null,null,null }, WhiteWoolItem, 1);
        // Нити из шерсти (1 шерсть -> 4 нити)
        AddShapeRecipe(new ItemDefinition?[] { WhiteWoolItem,null,null, null,null,null, null,null,null }, StringItem, 4);

        // Мотыги
        AddShapeRecipe(new ItemDefinition?[] { PlankItem,PlankItem,null, null,StickItem,null, null,StickItem,null }, WoodHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,PlankItem,PlankItem, null,StickItem,null, null,StickItem,null }, WoodHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { CobblestoneItem,CobblestoneItem,null, null,StickItem,null, null,StickItem,null }, StoneHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,CobblestoneItem,CobblestoneItem, null,StickItem,null, null,StickItem,null }, StoneHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { StoneItem,StoneItem,null, null,StickItem,null, null,StickItem,null }, StoneHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { IronIngotItem,IronIngotItem,null, null,StickItem,null, null,StickItem,null }, IronHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,IronIngotItem,IronIngotItem, null,StickItem,null, null,StickItem,null }, IronHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { DiamondItem,DiamondItem,null, null,StickItem,null, null,StickItem,null }, DiamondHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,DiamondItem,DiamondItem, null,StickItem,null, null,StickItem,null }, DiamondHoeItem, 1);

        // Хлеб (3 пшеницы в ряд)
        AddShapeRecipe(new ItemDefinition?[] { WheatItem,WheatItem,WheatItem, null,null,null, null,null,null }, BreadItem, 1);

        // Кровать: 3 шерсти в верхнем ряду + 3 доски во втором ряду
        AddShapeRecipe(new ItemDefinition?[] { WhiteWoolItem,WhiteWoolItem,WhiteWoolItem, PlankItem,PlankItem,PlankItem, null,null,null }, BedItem, 1);

        // Сундук: 8 досок по периметру 3×3 (центр пустой)
        AddShapeRecipe(new ItemDefinition?[] { PlankItem,PlankItem,PlankItem, PlankItem,null,PlankItem, PlankItem,PlankItem,PlankItem }, ChestItem, 1);

        // Костная мука: 1 кость -> 3 костной муки
        AddShapeRecipe(new ItemDefinition?[] { BoneItem,null,null, null,null,null, null,null,null }, BoneMealItem, 3);

        // Опилки: 2 доски -> 4 опилок, или 1 доска + 1 палка -> 2 опилок
        AddShapeRecipe(new ItemDefinition?[] { PlankItem,PlankItem,null, null,null,null, null,null,null }, SawdustItem, 4);
        AddShapeRecipe(new ItemDefinition?[] { PlankItem,null,null, StickItem,null,null, null,null,null }, SawdustItem, 2);

        // Каша из опилок: 2 опилки + 1 доска + 1 семена пшеницы
        AddShapeRecipe(new ItemDefinition?[] { SawdustItem,SawdustItem,null, PlankItem,WheatSeedsItem,null, null,null,null }, SawdustPorridgeItem, 1);

        // Тотем бессмертия: фигурка из костей с золотым слитком в центре
        AddShapeRecipe(new ItemDefinition?[] {
            BoneItem, GoldIngotItem, BoneItem,
            BoneItem, BoneItem,      BoneItem,
            null,     BoneItem,      null
        }, TotemItem, 1);

        // Лук (Bow): 3 палки + 3 нити
        AddShapeRecipe(new ItemDefinition?[] {
            null,      StickItem, StringItem,
            StickItem, null,      StringItem,
            null,      StickItem, StringItem
        }, BowItem, 1);
        AddShapeRecipe(new ItemDefinition?[] {
            StringItem, StickItem, null,
            StringItem, null,      StickItem,
            StringItem, StickItem, null
        }, BowItem, 1);

        // Щит (Shield): 6 досок + 1 слиток железа
        AddShapeRecipe(new ItemDefinition?[] {
            PlankItem, IronIngotItem, PlankItem,
            PlankItem, PlankItem,     PlankItem,
            null,      PlankItem,     null
        }, ShieldItem, 1);

        // Огниво (Flint and Steel): 1 железо + 1 кремень
        AddShapeRecipe(new ItemDefinition?[] {
            IronIngotItem, null, null,
            null, FlintItem, null,
            null, null, null
        }, FlintAndSteelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] {
            null, IronIngotItem, null,
            null, FlintItem, null,
            null, null, null
        }, FlintAndSteelItem, 1);

        // Динамит (TNT): 5 пороха + 4 песка (крест-накрест)
        AddShapeRecipe(new ItemDefinition?[] {
            GunpowderItem, SandItem,      GunpowderItem,
            SandItem,      GunpowderItem, SandItem,
            GunpowderItem, SandItem,      GunpowderItem
        }, TNTItem, 1);

        // Золотое яблоко: яблоко + 8 золотых слитков
        AddShapeRecipe(new ItemDefinition?[] {
            GoldIngotItem, GoldIngotItem, GoldIngotItem,
            GoldIngotItem, AppleItem,     GoldIngotItem,
            GoldIngotItem, GoldIngotItem, GoldIngotItem
        }, GoldenAppleItem, 1);

        // Блок светокамня: 4 светопыли 2×2
        AddShapeRecipe(new ItemDefinition?[] {
            GlowstoneDustItem, GlowstoneDustItem, null,
            GlowstoneDustItem, GlowstoneDustItem, null,
            null,              null,              null
        }, GlowstoneItem, 1);

        // Рельсы: 6 железа + 1 палка
        AddShapeRecipe(new ItemDefinition?[] {
            IronIngotItem, null,      IronIngotItem,
            IronIngotItem, StickItem, IronIngotItem,
            IronIngotItem, null,      IronIngotItem
        }, RailItem, 16);
    }

    private static void InitSmeltingRecipes() {
        SmeltingRecipes[IronOreItem.Id] = (IronIngotItem, 1);
        SmeltingRecipes[GoldOreItem.Id] = (GoldIngotItem, 1);
        SmeltingRecipes[SandItem.Id] = (GlassItem, 1);
        SmeltingRecipes[CoalOreItem.Id] = (CoalItem, 1);
        SmeltingRecipes[RawPorkItem.Id] = (CookedPorkItem, 1);
        SmeltingRecipes[CobblestoneItem.Id] = (StoneItem, 1);
        SmeltingRecipes[LogItem.Id] = (CharcoalItem, 1);
        SmeltingRecipes[RawBeefItem.Id] = (CookedBeefItem, 1);
        SmeltingRecipes[RawMuttonItem.Id] = (CookedMuttonItem, 1);
    }

    // ── Старый список рецептов (для DrawCraftPanel legacy) ───────────────────
    private static void Register(string id, string name, float seconds, RecipePart[] inputs, params RecipePart[] outputs) {
        Materials.RegisterRecipe(new CraftingRecipe {
            Id = id, CraftTimeSeconds = seconds,
            Inputs = inputs,
            Outputs = outputs,
        });
        _recipeNames[id] = name;
    }

    public static readonly Dictionary<string, string> _recipeNames = new();
    public static string RecipeName(string id) => _recipeNames.TryGetValue(id, out var n) ? n : id;

    private static ItemDefinition Item(ushort id, string name, Material material, double volumeM3) =>
        new() { Id = id, Name = name, Material = material, VolumeM3 = volumeM3 };

    private static BlockType Block(ushort id, string name, Material material, ItemDefinition? drop,
                                   bool flammable = false, float burnTime = 0f, float capacity = 0f, byte light = 0) =>
        new() {
            Id = id, Name = name, Material = material,
            DropItemId = drop?.Id ?? 0,
            IsFlammable = flammable, BurnTimeSeconds = burnTime,
            LoadCapacityKN = capacity, LightLevel = light,
        };

    private static BlockType With(this BlockType b, Action<BlockType> configure) {
        configure(b);
        return b;
    }
}
