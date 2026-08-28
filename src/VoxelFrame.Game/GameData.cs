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
    public ushort DropItemId { get; set; }            // что выпадает при ломании
    public int DropItemCount { get; set; } = 1;       // сколько предметов выпадает
    public int PlaceItemCount { get; set; } = 1;      // сколько предметов нужно для установки
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
        Id = 1, Name = "Дуб", Category = MaterialCategory.Wood,
    };
    public static readonly Material Sawdust = new() {
        Id = 2, Name = "Опилки", Category = MaterialCategory.Wood,
    };
    public static readonly Material Stone = new() {
        Id = 3, Name = "Камень", Category = MaterialCategory.Stone,
    };
    public static readonly Material DirtM = new() {
        Id = 4, Name = "Земля", Category = MaterialCategory.Soil,
    };
    public static readonly Material Coal = new() {
        Id = 5, Name = "Уголь", Category = MaterialCategory.Stone,
    };
    public static readonly Material AppleM = new() {
        Id = 6, Name = "Яблоко", Category = MaterialCategory.Organic,
    };
    public static readonly Material Pork = new() {
        Id = 7, Name = "Свинина", Category = MaterialCategory.Organic,
    };
    public static readonly Material AshM = new() {
        Id = 8, Name = "Зола", Category = MaterialCategory.Soil,
    };
    public static readonly Material LeavesM = new() {
        Id = 9, Name = "Листва", Category = MaterialCategory.Wood,
    };
    public static readonly Material IronM = new() {
        Id = 10, Name = "Железо", Category = MaterialCategory.Metal,
    };
    public static readonly Material StoneToolM = new() {
        Id = 11, Name = "Каменный инструмент", Category = MaterialCategory.Stone,
    };
    public static readonly Material IronToolM = new() {
        Id = 12, Name = "Железный инструмент", Category = MaterialCategory.Metal,
    };
    public static readonly Material WoodToolM = new() {
        Id = 13, Name = "Деревянный инструмент", Category = MaterialCategory.Wood,
    };
    public static readonly Material DiamondM = new() {
        Id = 14, Name = "Алмаз", Category = MaterialCategory.Stone,
    };
    public static readonly Material GoldM = new() {
        Id = 15, Name = "Золото", Category = MaterialCategory.Metal,
    };
    public static readonly Material RedstoneM = new() {
        Id = 16, Name = "Редстоун", Category = MaterialCategory.Stone,
    };
    public static readonly Material SandM = new() {
        Id = 17, Name = "Песок", Category = MaterialCategory.Soil,
    };
    public static readonly Material StringM = new() {
        Id = 18, Name = "Нить", Category = MaterialCategory.Organic,
    };
    public static readonly Material BoneM = new() {
        Id = 19, Name = "Кость", Category = MaterialCategory.Organic,
    };
    public static readonly Material BeefM = new() {
        Id = 20, Name = "Говядина", Category = MaterialCategory.Organic,
    };
    public static readonly Material LeatherM = new() {
        Id = 21, Name = "Кожа", Category = MaterialCategory.Organic,
    };
    public static readonly Material WoolM = new() {
        Id = 22, Name = "Шерсть", Category = MaterialCategory.Organic,
    };
    public static readonly Material WaterM = new() {
        Id = 23, Name = "Вода", Category = MaterialCategory.Soil,
    };
    public static readonly Material LavaM = new() {
        Id = 24, Name = "Лава", Category = MaterialCategory.Stone,
    };
    public static readonly Material ObsidianM = new() {
        Id = 25, Name = "Обсидиан", Category = MaterialCategory.Stone,
    };
    public static readonly Material GlassM = new() {
        Id = 26, Name = "Стекло", Category = MaterialCategory.Stone,
    };
    public static readonly Material OrganicM = new() {
        Id = 27, Name = "Органика", Category = MaterialCategory.Organic,
    };

    // ── Предметы ─────────────────────────────────────────────────────────────
    public static readonly ItemDefinition DirtItem = Item(1, "Земля", DirtM);
    public static readonly ItemDefinition StoneItem = Item(2, "Камень", Stone);
    public static readonly ItemDefinition LogItem = Item(3, "Бревно", Oak);
    public static readonly ItemDefinition PlankItem = Item(4, "Доски", Oak);
    public static readonly ItemDefinition StickItem = Item(6, "Палки", Oak);
    public static readonly ItemDefinition CoalItem = Item(7, "Уголь", Coal);
    public static readonly ItemDefinition CoalOreItem = Item(8, "Угольная руда", Stone);
    public static readonly ItemDefinition TorchItem = Item(9, "Факел", Oak);
    public static readonly ItemDefinition AppleItem = Item(13, "Яблоко", AppleM);
    public static readonly ItemDefinition RawPorkItem = Item(14, "Сырая свинина", Pork);
    public static readonly ItemDefinition CookedPorkItem = Item(15, "Жареная свинина", Pork);
    public static readonly ItemDefinition IronOreItem = Item(16, "Железная руда", Stone);
    public static readonly ItemDefinition IronIngotItem = Item(17, "Железный слиток", IronM);
    public static readonly ItemDefinition WoodPickaxeItem = Item(18, "Деревянная кирка", WoodToolM, maxStack: 1);
    public static readonly ItemDefinition StonePickaxeItem = Item(19, "Каменная кирка", StoneToolM, maxStack: 1);
    public static readonly ItemDefinition IronPickaxeItem = Item(20, "Железная кирка", IronToolM, maxStack: 1);
    public static readonly ItemDefinition WoodAxeItem = Item(21, "Деревянный топор", WoodToolM, maxStack: 1);
    public static readonly ItemDefinition StoneAxeItem = Item(22, "Каменный топор", StoneToolM, maxStack: 1);
    public static readonly ItemDefinition IronAxeItem = Item(23, "Железный топор", IronToolM, maxStack: 1);
    public static readonly ItemDefinition WoodSwordItem = Item(24, "Деревянный меч", WoodToolM, maxStack: 1);
    public static readonly ItemDefinition StoneSwordItem = Item(25, "Каменный меч", StoneToolM, maxStack: 1);
    public static readonly ItemDefinition IronSwordItem = Item(26, "Железный меч", IronToolM, maxStack: 1);
    public static readonly ItemDefinition WoodShovelItem = Item(27, "Деревянная лопата", WoodToolM, maxStack: 1);
    public static readonly ItemDefinition StoneShovelItem = Item(28, "Каменная лопата", StoneToolM, maxStack: 1);
    public static readonly ItemDefinition IronShovelItem = Item(29, "Железная лопата", IronToolM, maxStack: 1);
    public static readonly ItemDefinition WorkbenchItem = Item(30, "Верстак", Oak);
    public static readonly ItemDefinition FurnaceItem = Item(31, "Печка", Stone);
    public static readonly ItemDefinition BreadItem = Item(32, "Хлеб", OrganicM);

    // Блоки и предметы мира
    public static readonly ItemDefinition GoldOreItem = Item(33, "Золотая руда", Stone);
    public static readonly ItemDefinition GoldIngotItem = Item(34, "Золотой слиток", GoldM);
    public static readonly ItemDefinition DiamondItem = Item(35, "Алмаз", DiamondM);
    public static readonly ItemDefinition DiamondOreItem = Item(36, "Алмазная руда", Stone);
    public static readonly ItemDefinition RedstoneItem = Item(37, "Редстоун пыль", RedstoneM);
    public static readonly ItemDefinition SandItem = Item(38, "Песок", SandM);
    public static readonly ItemDefinition GravelItem = Item(39, "Гравий", Stone);
    public static readonly ItemDefinition CobblestoneItem = Item(40, "Булыжник", Stone);
    public static readonly ItemDefinition GlassItem = Item(41, "Стекло", GlassM);
    public static readonly ItemDefinition ObsidianItem = Item(42, "Обсидиан", ObsidianM);
    public static readonly ItemDefinition DiamondPickaxeItem = Item(43, "Алмазная кирка", DiamondM, maxStack: 1);
    public static readonly ItemDefinition DiamondAxeItem = Item(44, "Алмазный топор", DiamondM, maxStack: 1);
    public static readonly ItemDefinition DiamondSwordItem = Item(45, "Алмазный меч", DiamondM, maxStack: 1);
    public static readonly ItemDefinition DiamondShovelItem = Item(46, "Алмазная лопата", DiamondM, maxStack: 1);

    public static readonly ItemDefinition FeatherItem = Item(47, "Перо", OrganicM);
    public static readonly ItemDefinition GunpowderItem = Item(48, "Порох", Coal);
    public static readonly ItemDefinition StringItem = Item(49, "Нить", StringM);
    public static readonly ItemDefinition ArrowItem = Item(50, "Стрела", WoodToolM);
    public static readonly ItemDefinition BoneItem = Item(51, "Кость", BoneM);
    public static readonly ItemDefinition CharcoalItem = Item(52, "Древесный уголь", Coal);
    public static readonly ItemDefinition RawBeefItem = Item(53, "Сырая говядина", BeefM);
    public static readonly ItemDefinition CookedBeefItem = Item(54, "Жареная говядина", BeefM);
    public static readonly ItemDefinition LeatherItem = Item(55, "Кожа", LeatherM);
    public static readonly ItemDefinition WhiteWoolItem = Item(56, "Шерсть", WoolM);
    public static readonly ItemDefinition ChestItem = Item(57, "Сундук", Oak);
    public static readonly ItemDefinition BedItem = Item(58, "Кровать", Oak, maxStack: 1);
    public static readonly ItemDefinition RottenFleshItem = Item(59, "Гнилая плоть", BeefM);
    public static readonly ItemDefinition WoodHoeItem = Item(60, "Деревянная мотыга", WoodToolM, maxStack: 1);
    public static readonly ItemDefinition StoneHoeItem = Item(61, "Каменная мотыга", StoneToolM, maxStack: 1);
    public static readonly ItemDefinition IronHoeItem = Item(62, "Железная мотыга", IronToolM, maxStack: 1);
    public static readonly ItemDefinition DiamondHoeItem = Item(63, "Алмазная мотыга", DiamondM, maxStack: 1);
    public static readonly ItemDefinition WheatItem = Item(64, "Пшеница", OrganicM);
    public static readonly ItemDefinition WheatSeedsItem = Item(65, "Семена пшеницы", OrganicM);
    public static readonly ItemDefinition BoneMealItem = Item(66, "Костная мука", BoneM);
    public static readonly ItemDefinition SawdustItem = Item(67, "Древесные опилки", Oak);
    public static readonly ItemDefinition SawdustPorridgeItem = Item(68, "Каша из опилок", Oak);
    public static readonly ItemDefinition TotemItem = Item(69, "Тотем бессмертия", GoldM, maxStack: 1);
    public static readonly ItemDefinition RawMuttonItem = Item(70, "Сырая баранина", BeefM);
    public static readonly ItemDefinition CookedMuttonItem = Item(71, "Жареная баранина", BeefM);
    public static readonly ItemDefinition BowItem = Item(72, "Лук", Oak, maxStack: 1);
    public static readonly ItemDefinition ShieldItem = Item(73, "Щит", Oak, maxStack: 1);
    public static readonly ItemDefinition FlintItem = Item(74, "Кремень", Stone);
    public static readonly ItemDefinition FlintAndSteelItem = Item(75, "Огниво", IronM, maxStack: 1);
    public static readonly ItemDefinition GoldenAppleItem = Item(76, "Золотое яблоко", GoldM);
    public static readonly ItemDefinition SaddleItem = Item(77, "Седло", LeatherM, maxStack: 1);
    public static readonly ItemDefinition EnchantedBookItem = Item(78, "Зачарованная книга", LeatherM, maxStack: 1);
    public static readonly ItemDefinition MusicDiscItem = Item(79, "Музыкальная пластинка", DiamondM, maxStack: 1);
    public static readonly ItemDefinition NetherQuartzItem = Item(80, "Кварц", DiamondM);
    public static readonly ItemDefinition BlazeRodItem = Item(81, "Стержень ифрита", GoldM);
    public static readonly ItemDefinition GlowstoneDustItem = Item(82, "Светопыль", RedstoneM);
    public static readonly ItemDefinition TNTItem = Item(83, "Динамит", SandM);
    public static readonly ItemDefinition NetherrackItem = Item(84, "Адский камень", Stone);
    public static readonly ItemDefinition SoulSandItem = Item(85, "Песок душ", SandM);
    public static readonly ItemDefinition GlowstoneItem = Item(86, "Светокамень", Stone);
    public static readonly ItemDefinition NetherQuartzOreItem = Item(87, "Кварцевая руда", Stone);
    public static readonly ItemDefinition NetherBrickItem = Item(88, "Адский кирпич", Stone);
    public static readonly ItemDefinition MossyCobblestoneItem = Item(89, "Замшелый булыжник", Stone);
    public static readonly ItemDefinition ChiseledSandstoneItem = Item(90, "Резной песчаник", SandM);
    public static readonly ItemDefinition RailItem = Item(91, "Рельсы", IronM);
    public static readonly ItemDefinition BucketItem = Item(92, "Ведро", IronM, maxStack: 16);
    public static readonly ItemDefinition WaterBucketItem = Item(93, "Ведро воды", IronM, maxStack: 1);
    public static readonly ItemDefinition LavaBucketItem = Item(94, "Ведро лавы", IronM, maxStack: 1);
    public static readonly ItemDefinition DoorItem = Item(95, "Деревянная дверь", Oak);
    public static readonly ItemDefinition GoldPickaxeItem = Item(96, "Золотая кирка", GoldM, maxStack: 1);
    public static readonly ItemDefinition GoldAxeItem = Item(97, "Золотой топор", GoldM, maxStack: 1);
    public static readonly ItemDefinition GoldSwordItem = Item(98, "Золотой меч", GoldM, maxStack: 1);
    public static readonly ItemDefinition GoldShovelItem = Item(99, "Золотая лопата", GoldM, maxStack: 1);
    public static readonly ItemDefinition GoldHoeItem = Item(100, "Золотая мотыга", GoldM, maxStack: 1);

    // Энд: предметы
    public static readonly ItemDefinition EnderPearlItem = Item(101, "Жемчуг Эндера", OrganicM);
    public static readonly ItemDefinition EyeOfEnderItem = Item(102, "Око Эндера", OrganicM);
    public static readonly ItemDefinition BlazePowderItem = Item(103, "Порох ифрита", Coal);
    public static readonly ItemDefinition ChorusFruitItem = Item(104, "Плод хоруса", OrganicM);
    public static readonly ItemDefinition EndSlimeItem = Item(105, "Эндер-слизь", ObsidianM, maxStack: 1);
    public static readonly ItemDefinition EndStoneItem = Item(106, "Эндовый камень", Stone);
    public static readonly ItemDefinition EndPortalFrameItem = Item(107, "Рамка портала Энда", ObsidianM);
    public static readonly ItemDefinition EnderCrystalItem = Item(108, "Эндер-кристалл", GlassM);

    // Артефакты мини-боссов и Ключ Бездны (собирается из всех четырёх артефактов)
    public static readonly ItemDefinition NetherArtifactItem = Item(109, "Адский артефакт", ObsidianM);
    public static readonly ItemDefinition SwampArtifactItem = Item(110, "Болотный артефакт", OrganicM);
    public static readonly ItemDefinition DesertArtifactItem = Item(111, "Пустынный артефакт", SandM);
    public static readonly ItemDefinition VoidKeyItem = Item(112, "Ключ Бездны", ObsidianM, maxStack: 1);

    // ── Блоки ─────────────────────────────────────────────────────────────────
    public static readonly BlockType BGrass = Block(1, "Трава", DirtM, drop: DirtItem);
    public static readonly BlockType BDirt = Block(2, "Земля", DirtM, drop: DirtItem);
    public static readonly BlockType BStone = Block(3, "Камень", Stone, drop: CobblestoneItem);
    public static readonly BlockType BLog = Block(4, "Бревно", Oak, drop: LogItem, flammable: true, burnTime: 8f);
    public static readonly BlockType BLeaves = Block(5, "Листва", LeavesM, drop: null, flammable: true, burnTime: 4f)
        .With(b => { b.IsSolid = true; b.IsOpaque = false; });
    public static readonly BlockType BPlanks = Block(6, "Доски", Oak, drop: PlankItem, flammable: true, burnTime: 7f);
    public static readonly BlockType BCoalOre = Block(7, "Угольная руда", Stone, drop: CoalItem);
    public static readonly BlockType BTorch = Block(8, "Факел", Oak, drop: TorchItem, light: 14)
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
    public static readonly BlockType BGlass = Block(18, "Стекло", GlassM, drop: null).With(b => { b.IsOpaque = false; });
    public static readonly BlockType BWater = Block(19, "Вода", WaterM, drop: null).With(b => { b.IsSolid = false; b.IsOpaque = false; });
    public static readonly BlockType BLava = Block(20, "Лава", LavaM, drop: null, light: 15).With(b => { b.IsSolid = false; b.IsOpaque = true; });
    public static readonly BlockType BGoldOre = Block(21, "Золотая руда", Stone, drop: GoldOreItem);
    public static readonly BlockType BDiamondOre = Block(22, "Алмазная руда", Stone, drop: DiamondItem);
    public static readonly BlockType BRedstoneOre = Block(23, "Редстоун руда", Stone, drop: RedstoneItem);
    public static readonly BlockType BObsidian = Block(24, "Обсидиан", ObsidianM, drop: ObsidianItem);
    public static readonly BlockType BChest = Block(25, "Сундук", Oak, drop: ChestItem, flammable: true, burnTime: 8f)
        .With(b => { b.IsOpaque = false; });
    public static readonly BlockType BBed = Block(26, "Кровать", Oak, drop: BedItem, flammable: true, burnTime: 6f)
        .With(b => { b.IsOpaque = false; });
    public static readonly BlockType BBedHead = Block(27, "Кровать (изголовье)", Oak, drop: BedItem, flammable: true, burnTime: 6f)
        .With(b => { b.IsOpaque = false; });
    public static readonly BlockType BFarmland = Block(28, "Грядка", DirtM, drop: DirtItem)
        .With(b => { b.IsOpaque = false; });
    public static readonly BlockType BWheatCrop = Block(29, "Посевы пшеницы", OrganicM, drop: WheatSeedsItem)
        .With(b => { b.IsSolid = false; b.IsOpaque = false; });
    public static readonly BlockType BTallGrass = Block(30, "Трава", OrganicM, drop: WheatSeedsItem)
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
    public static readonly BlockType BNetherPortal = Block(43, "Портал в Нижний мир", ObsidianM, drop: null, light: 11)
        .With(b => { b.IsSolid = false; b.IsOpaque = false; b.IsUnbreakable = true; });
    public static readonly BlockType BDoorLower = Block(44, "Деревянная дверь", Oak, drop: DoorItem, flammable: true, burnTime: 6f)
        .With(b => { b.IsOpaque = false; });
    public static readonly BlockType BDoorUpper = Block(45, "Деревянная дверь (верх)", Oak, drop: null, flammable: true, burnTime: 6f)
        .With(b => { b.IsOpaque = false; });

    // Энд: блоки
    public static readonly BlockType BEndStone = Block(46, "Эндовый камень", Stone, drop: EndStoneItem);
    public static readonly BlockType BEndPortalFrame = Block(47, "Рамка портала Энда", ObsidianM, drop: EndPortalFrameItem)
        .With(b => { b.IsOpaque = false; });
    public static readonly BlockType BEndPortal = Block(48, "Портал в Энд", ObsidianM, drop: null, light: 15)
        .With(b => { b.IsSolid = false; b.IsOpaque = false; b.IsUnbreakable = true; });
    public static readonly BlockType BObsidianPillar = Block(49, "Обсидиановая колонна", ObsidianM, drop: ObsidianItem);
    public static readonly BlockType BEnderCrystal = Block(50, "Эндер-кристалл", GlassM, drop: EnderCrystalItem)
        .With(b => { b.IsOpaque = false; b.IsSolid = false; b.IsUnbreakable = true; b.LightLevel = 12; });
    public static readonly BlockType BChorusPlant = Block(51, "Растение хоруса", Oak, drop: ChorusFruitItem, flammable: true, burnTime: 5f);
    public static readonly BlockType BChorusFlower = Block(52, "Цветок хоруса", Oak, drop: ChorusFruitItem, flammable: true, burnTime: 3f)
        .With(b => { b.IsOpaque = false; });
    public static readonly BlockType BVoidGate = Block(53, "Врата Бездны", ObsidianM, drop: null, light: 12)
        .With(b => { b.IsUnbreakable = true; b.IsOpaque = false; });

    public static readonly BlockType[] Blocks =
        { BGrass, BDirt, BStone, BLog, BLeaves, BPlanks, BCoalOre, BTorch, BBedrock, BIronOre, BWorkbench, BFurnace,
          BCobblestone, BSand, BGravel, BGlass, BWater, BLava, BGoldOre, BDiamondOre, BRedstoneOre, BObsidian, BChest, BBed, BBedHead,
          BFarmland, BWheatCrop, BTallGrass, BMossyCobblestone, BMobSpawner, BWeb, BRail, BPressurePlate, BTNT, BChiseledSandstone,
          BNetherrack, BSoulSand, BGlowstone, BNetherQuartzOre, BNetherBrick, BNetherPortal, BDoorLower, BDoorUpper,
          BEndStone, BEndPortalFrame, BEndPortal, BObsidianPillar, BEnderCrystal, BChorusPlant, BChorusFlower, BVoidGate };


    private static readonly Dictionary<ushort, BlockType> _byId = Blocks.ToDictionary(b => b.Id);
    private static readonly Dictionary<ushort, ushort> _blockByItem = new() {
        { DirtItem.Id, BDirt.Id },
        { StoneItem.Id, BStone.Id },
        { LogItem.Id, BLog.Id },
        { PlankItem.Id, BPlanks.Id },
        { TorchItem.Id, BTorch.Id },
        { CoalOreItem.Id, BCoalOre.Id },
        { IronOreItem.Id, BIronOre.Id },
        { GoldOreItem.Id, BGoldOre.Id },
        { DiamondOreItem.Id, BDiamondOre.Id },
        { WorkbenchItem.Id, BWorkbench.Id },
        { FurnaceItem.Id, BFurnace.Id },
        { CobblestoneItem.Id, BCobblestone.Id },
        { SandItem.Id, BSand.Id },
        { GravelItem.Id, BGravel.Id },
        { GlassItem.Id, BGlass.Id },
        { ObsidianItem.Id, BObsidian.Id },
        { ChestItem.Id, BChest.Id },
        { BedItem.Id, BBed.Id },
        { MossyCobblestoneItem.Id, BMossyCobblestone.Id },
        { TNTItem.Id, BTNT.Id },
        { ChiseledSandstoneItem.Id, BChiseledSandstone.Id },
        { NetherrackItem.Id, BNetherrack.Id },
        { SoulSandItem.Id, BSoulSand.Id },
        { GlowstoneItem.Id, BGlowstone.Id },
        { NetherQuartzOreItem.Id, BNetherQuartzOre.Id },
        { NetherBrickItem.Id, BNetherBrick.Id },
        { DoorItem.Id, BDoorLower.Id },
        { RailItem.Id, BRail.Id },
    };

    /// <summary>Возвращает блок по его Id.</summary>
    public static BlockType GetBlock(ushort id) => _byId.TryGetValue(id, out var b) ? b : throw new KeyNotFoundException($"Блок #{id}");

    /// <summary>Безопасно пытается получить блок по Id, не бросая исключения (воздух/пустые id → false).</summary>
    public static bool TryGetBlock(ushort id, out BlockType block) {
        if (_byId.TryGetValue(id, out var b)) { block = b; return true; }
        block = null!;
        return false;
    }

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
        { ChorusFruitItem.Id, 4f },  // Плод хоруса: +4 HP (и телепорт)
    };

    // ── Реестр предметов ───────────────────────────────────────────────────────

    public static readonly Dictionary<ushort, ItemDefinition> Items = new();
    private static ulong _nextInstanceId;

    public static ItemInstance NewItem(ItemDefinition def) {
        var inst = new ItemInstance(def, _nextInstanceId++);
        inst.Durability = GetToolTier(def.Id) > 0 ? GetMaxToolDurability(def.Id) : 0;
        return inst;
    }

    // ── Shape-based крафт ──────────────────────────────────────────────────────
    /// <summary>Ключ — нормализованный паттерн сетки 3×3. Значение — (выходной предмет, количество).</summary>
    public static readonly Dictionary<string, (ItemDefinition Item, int Count)> ShapeRecipes = new();

    // ── Рецепты плавки ────────────────────────────────────────────────────────
    /// <summary>Входной item ID → (выходной предмет, количество).</summary>
    public static readonly Dictionary<ushort, (ItemDefinition Output, int Count)> SmeltingRecipes = new();

    // ── Тир инструментов ──────────────────────────────────────────────────────
    /// <summary>Уровень инструмента: 0=руки, 1=дерево, 2=камень, 3=железо, 4=золото, 5=алмаз.</summary>
    public static int GetToolTier(ushort itemId) {
        if (itemId == WoodPickaxeItem.Id || itemId == WoodAxeItem.Id || itemId == WoodShovelItem.Id || itemId == WoodSwordItem.Id || itemId == WoodHoeItem.Id) return 1;
        if (itemId == StonePickaxeItem.Id || itemId == StoneAxeItem.Id || itemId == StoneShovelItem.Id || itemId == StoneSwordItem.Id || itemId == StoneHoeItem.Id) return 2;
        if (itemId == IronPickaxeItem.Id || itemId == IronAxeItem.Id || itemId == IronShovelItem.Id || itemId == IronSwordItem.Id || itemId == IronHoeItem.Id) return 3;
        if (itemId == GoldPickaxeItem.Id || itemId == GoldAxeItem.Id || itemId == GoldShovelItem.Id || itemId == GoldSwordItem.Id || itemId == GoldHoeItem.Id) return 4;
        if (itemId == DiamondPickaxeItem.Id || itemId == DiamondAxeItem.Id || itemId == DiamondShovelItem.Id || itemId == DiamondSwordItem.Id || itemId == DiamondHoeItem.Id) return 5;
        return 0;
    }

    public static int GetMaxToolDurability(ushort itemId) {
        if (itemId == WoodPickaxeItem.Id || itemId == WoodAxeItem.Id || itemId == WoodShovelItem.Id || itemId == WoodSwordItem.Id || itemId == WoodHoeItem.Id) return 59;
        if (itemId == StonePickaxeItem.Id || itemId == StoneAxeItem.Id || itemId == StoneShovelItem.Id || itemId == StoneSwordItem.Id || itemId == StoneHoeItem.Id) return 131;
        if (itemId == IronPickaxeItem.Id || itemId == IronAxeItem.Id || itemId == IronShovelItem.Id || itemId == IronSwordItem.Id || itemId == IronHoeItem.Id) return 250;
        if (itemId == GoldPickaxeItem.Id || itemId == GoldAxeItem.Id || itemId == GoldShovelItem.Id || itemId == GoldSwordItem.Id || itemId == GoldHoeItem.Id) return 32;
        if (itemId == DiamondPickaxeItem.Id || itemId == DiamondAxeItem.Id || itemId == DiamondShovelItem.Id || itemId == DiamondSwordItem.Id || itemId == DiamondHoeItem.Id) return 1561;
        return 59;
    }

    public static bool RequiresPickaxe(ushort blockId) =>
        blockId == BStone.Id || blockId == BCobblestone.Id || blockId == BCoalOre.Id ||
        blockId == BIronOre.Id || blockId == BGoldOre.Id || blockId == BDiamondOre.Id ||
        blockId == BRedstoneOre.Id || blockId == BFurnace.Id || blockId == BObsidian.Id ||
        blockId == BMossyCobblestone.Id || blockId == BNetherrack.Id ||
        blockId == BNetherQuartzOre.Id || blockId == BNetherBrick.Id ||
        blockId == BEndStone.Id || blockId == BEndPortalFrame.Id || blockId == BObsidianPillar.Id;

    /// <summary>Минимальный тир инструмента для добычи блока. 0 = можно голыми руками.</summary>
    public static int GetRequiredTier(ushort blockId) {
        if (blockId == BStone.Id || blockId == BCoalOre.Id || blockId == BFurnace.Id || blockId == BCobblestone.Id || blockId == BMossyCobblestone.Id || blockId == BNetherrack.Id || blockId == BNetherQuartzOre.Id || blockId == BNetherBrick.Id || blockId == BEndStone.Id || blockId == BEndPortalFrame.Id) return 1;
        if (blockId == BIronOre.Id) return 2;
        if (blockId == BGoldOre.Id || blockId == BDiamondOre.Id || blockId == BRedstoneOre.Id) return 3;
        if (blockId == BObsidian.Id || blockId == BObsidianPillar.Id) return 5; // Алмазная кирка
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
        itemId == WoodPickaxeItem.Id || itemId == StonePickaxeItem.Id || itemId == IronPickaxeItem.Id || itemId == GoldPickaxeItem.Id || itemId == DiamondPickaxeItem.Id;

    public static bool IsAxe(ushort itemId) =>
        itemId == WoodAxeItem.Id || itemId == StoneAxeItem.Id || itemId == IronAxeItem.Id || itemId == GoldAxeItem.Id || itemId == DiamondAxeItem.Id;

    public static bool IsShovel(ushort itemId) =>
        itemId == WoodShovelItem.Id || itemId == StoneShovelItem.Id || itemId == IronShovelItem.Id || itemId == GoldShovelItem.Id || itemId == DiamondShovelItem.Id;

    public static bool IsSword(ushort itemId) =>
        itemId == WoodSwordItem.Id || itemId == StoneSwordItem.Id || itemId == IronSwordItem.Id || itemId == GoldSwordItem.Id || itemId == DiamondSwordItem.Id;

    public static bool IsHoe(ushort itemId) =>
        itemId == WoodHoeItem.Id || itemId == StoneHoeItem.Id || itemId == IronHoeItem.Id || itemId == GoldHoeItem.Id || itemId == DiamondHoeItem.Id;

    public static bool IsDoor(ushort blockId) =>
        blockId == BDoorLower.Id || blockId == BDoorUpper.Id;

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
        if (itemId == WoodSwordItem.Id || itemId == GoldSwordItem.Id) return 4f; // Деревянный/Золотой меч: 4 HP (2 сердца)
        
        if (itemId == DiamondAxeItem.Id) return 9f;     // Алмазный топор: 9 HP (4.5 сердца)
        if (itemId == IronAxeItem.Id) return 9f;        // Железный топор: 9 HP (4.5 сердца)
        if (itemId == StoneAxeItem.Id) return 9f;       // Каменный топор: 9 HP (4.5 сердца)
        if (itemId == WoodAxeItem.Id || itemId == GoldAxeItem.Id) return 7f; // Деревянный/Золотой топор: 7 HP (3.5 сердца)

        if (itemId == DiamondPickaxeItem.Id) return 5f; // Алмазная кирка: 5 HP
        if (itemId == IronPickaxeItem.Id) return 4f;    // Железная кирка: 4 HP
        if (itemId == StonePickaxeItem.Id) return 3f;   // Каменная кирка: 3 HP
        if (itemId == WoodPickaxeItem.Id || itemId == GoldPickaxeItem.Id) return 2f; // Деревянная/Золотая кирка: 2 HP

        if (itemId == DiamondShovelItem.Id) return 5.5f;// Алмазная лопата: 5.5 HP
        if (itemId == IronShovelItem.Id) return 4.5f;   // Железная лопата: 4.5 HP
        if (itemId == StoneShovelItem.Id) return 3.5f;  // Каменная лопата: 3.5 HP
        if (itemId == WoodShovelItem.Id || itemId == GoldShovelItem.Id) return 2.5f; // Деревянная/Золотая лопата: 2.5 HP

        return 1f; // Рука / любой другой предмет: 1 HP (0.5 сердца)
    }

    /// <summary>
    /// Длительность перезарядки атаки оружия:
    /// Мечи ~0.625с (1.6 ск/с), Топоры (Дерево/Камень/Золото 1.25с, Железо 1.11с, Алмаз 1.0с), Кирки ~0.83с, Рука/Мотыга 0.25с (4.0 ск/с)
    /// </summary>
    public static float GetWeaponCooldown(ushort itemId) {
        if (IsSword(itemId)) return 0.625f;
        if (itemId == DiamondAxeItem.Id) return 1.0f;
        if (itemId == IronAxeItem.Id) return 1.11f;
        if (itemId == StoneAxeItem.Id || itemId == WoodAxeItem.Id || itemId == GoldAxeItem.Id) return 1.25f;
        if (IsPickaxe(itemId)) return 0.833f;
        if (IsShovel(itemId)) return 1.0f;
        if (IsHoe(itemId)) return 0.25f;
        return 0.25f;
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
        if (b.Id == BObsidian.Id || b.Id == BObsidianPillar.Id) return 50.0f;
        if (b.Id == BEndStone.Id || b.Id == BEndPortalFrame.Id) return 3.0f;
        if (b.Id == BEnderCrystal.Id) return 0.5f;
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
            return tier switch { 1 => 2f, 2 => 4f, 3 => 6f, 4 => 12f, 5 => 8f, _ => 1f };
        }
        if (IsAxe(toolId) && isWoodBlock) {
            return tier switch { 1 => 2f, 2 => 4f, 3 => 6f, 4 => 12f, 5 => 8f, _ => 1f };
        }
        if (IsShovel(toolId) && isSoilBlock) {
            return tier switch { 1 => 2f, 2 => 4f, 3 => 6f, 4 => 12f, 5 => 8f, _ => 1f };
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
            return baseHardness * 5.0f * 1.8f; // Не тот инструмент или нет нужного тира: долго ломается и не дропается
        }

        if (speed > 1.0f) {
            return (baseHardness * 1.5f * 1.8f) / speed;
        }

        return baseHardness * 1.5f * 1.8f;
    }

    public static string MiningRequirementHint(BlockType b) {
        int tier = GetRequiredTier(b.Id);
        return tier switch {
            1 => $"Для «{b.Name}» нужна кирка (деревянная или лучше)", 2 => $"Для «{b.Name}» нужна каменная кирка или лучше", 3 => $"Для «{b.Name}» нужна железная кирка или лучше", 5 => $"Для «{b.Name}» нужна алмазная кирка", _ => $"«{b.Name}» добывается любым инструментом или руками", };
    }

    static GameData() {
        _blockByItem[DirtItem.Id] = BDirt.Id;
        _blockByItem[StoneItem.Id] = BStone.Id;
        _blockByItem[LogItem.Id] = BLog.Id;
        _blockByItem[PlankItem.Id] = BPlanks.Id;
        _blockByItem[CoalOreItem.Id] = BCoalOre.Id;
        _blockByItem[TorchItem.Id] = BTorch.Id;
        _blockByItem[IronOreItem.Id] = BIronOre.Id;
        _blockByItem[GoldOreItem.Id] = BGoldOre.Id;
        _blockByItem[DiamondOreItem.Id] = BDiamondOre.Id;
        _blockByItem[SandItem.Id] = BSand.Id;
        _blockByItem[GravelItem.Id] = BGravel.Id;
        _blockByItem[CobblestoneItem.Id] = BCobblestone.Id;
        _blockByItem[GlassItem.Id] = BGlass.Id;
        _blockByItem[ObsidianItem.Id] = BObsidian.Id;
        _blockByItem[WorkbenchItem.Id] = BWorkbench.Id;
        _blockByItem[FurnaceItem.Id] = BFurnace.Id;
        _blockByItem[ChestItem.Id] = BChest.Id;
        _blockByItem[BedItem.Id] = BBed.Id;
        _blockByItem[TNTItem.Id] = BTNT.Id;
        _blockByItem[NetherrackItem.Id] = BNetherrack.Id;
        _blockByItem[SoulSandItem.Id] = BSoulSand.Id;
        _blockByItem[GlowstoneItem.Id] = BGlowstone.Id;
        _blockByItem[NetherQuartzOreItem.Id] = BNetherQuartzOre.Id;
        _blockByItem[NetherBrickItem.Id] = BNetherBrick.Id;
        _blockByItem[MossyCobblestoneItem.Id] = BMossyCobblestone.Id;
        _blockByItem[ChiseledSandstoneItem.Id] = BChiseledSandstone.Id;
        _blockByItem[RailItem.Id] = BRail.Id;
        _blockByItem[DoorItem.Id] = BDoorLower.Id;
        _blockByItem[EndStoneItem.Id] = BEndStone.Id;
        _blockByItem[EndPortalFrameItem.Id] = BEndPortalFrame.Id;
        _blockByItem[EnderCrystalItem.Id] = BEnderCrystal.Id;
        _blockByItem[ChorusFruitItem.Id] = BChorusPlant.Id;

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
            WoodHoeItem, StoneHoeItem, IronHoeItem, GoldHoeItem, DiamondHoeItem,
            GoldPickaxeItem, GoldAxeItem, GoldSwordItem, GoldShovelItem,
            WheatItem, WheatSeedsItem,
            BoneMealItem, SawdustItem, SawdustPorridgeItem, TotemItem,
            RawMuttonItem, CookedMuttonItem, BowItem, ShieldItem,
            FlintItem, FlintAndSteelItem, GoldenAppleItem, SaddleItem,
            EnchantedBookItem, MusicDiscItem, NetherQuartzItem, BlazeRodItem,
            GlowstoneDustItem, TNTItem, NetherrackItem, SoulSandItem,
            GlowstoneItem, NetherQuartzOreItem, NetherBrickItem, MossyCobblestoneItem,
            ChiseledSandstoneItem, RailItem, BucketItem, WaterBucketItem, LavaBucketItem, DoorItem,
            EnderPearlItem, EyeOfEnderItem, BlazePowderItem, ChorusFruitItem, EndSlimeItem,
            EndStoneItem, EndPortalFrameItem, EnderCrystalItem,
            NetherArtifactItem, SwampArtifactItem, DesertArtifactItem, VoidKeyItem }) {
            Items.Add(item.Id, item);
        }



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
        AddShapeRecipe(new ItemDefinition?[] {
            IronIngotItem, null, IronIngotItem,
            null, IronIngotItem, null,
            null, null, null
        }, BucketItem, 1);
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

        // Кирки
        AddShapeRecipe(new ItemDefinition?[] { PlankItem,PlankItem,PlankItem, null,StickItem,null, null,StickItem,null }, WoodPickaxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { CobblestoneItem,CobblestoneItem,CobblestoneItem, null,StickItem,null, null,StickItem,null }, StonePickaxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { IronIngotItem,IronIngotItem,IronIngotItem, null,StickItem,null, null,StickItem,null }, IronPickaxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { GoldIngotItem,GoldIngotItem,GoldIngotItem, null,StickItem,null, null,StickItem,null }, GoldPickaxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { DiamondItem,DiamondItem,DiamondItem, null,StickItem,null, null,StickItem,null }, DiamondPickaxeItem, 1);

        // Топоры (левые и правые)
        AddShapeRecipe(new ItemDefinition?[] { PlankItem,PlankItem,null, PlankItem,StickItem,null, null,StickItem,null }, WoodAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,PlankItem,PlankItem, null,StickItem,PlankItem, null,StickItem,null }, WoodAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { CobblestoneItem,CobblestoneItem,null, CobblestoneItem,StickItem,null, null,StickItem,null }, StoneAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,CobblestoneItem,CobblestoneItem, null,StickItem,CobblestoneItem, null,StickItem,null }, StoneAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { IronIngotItem,IronIngotItem,null, IronIngotItem,StickItem,null, null,StickItem,null }, IronAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,IronIngotItem,IronIngotItem, null,StickItem,IronIngotItem, null,StickItem,null }, IronAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { GoldIngotItem,GoldIngotItem,null, GoldIngotItem,StickItem,null, null,StickItem,null }, GoldAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,GoldIngotItem,GoldIngotItem, null,StickItem,GoldIngotItem, null,StickItem,null }, GoldAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { DiamondItem,DiamondItem,null, DiamondItem,StickItem,null, null,StickItem,null }, DiamondAxeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,DiamondItem,DiamondItem, null,StickItem,DiamondItem, null,StickItem,null }, DiamondAxeItem, 1);

        // Мечи
        AddShapeRecipe(new ItemDefinition?[] { null,PlankItem,null, null,PlankItem,null, null,StickItem,null }, WoodSwordItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,CobblestoneItem,null, null,CobblestoneItem,null, null,StickItem,null }, StoneSwordItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,IronIngotItem,null, null,IronIngotItem,null, null,StickItem,null }, IronSwordItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,GoldIngotItem,null, null,GoldIngotItem,null, null,StickItem,null }, GoldSwordItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,DiamondItem,null, null,DiamondItem,null, null,StickItem,null }, DiamondSwordItem, 1);

        // Лопаты
        AddShapeRecipe(new ItemDefinition?[] { null,PlankItem,null, null,StickItem,null, null,StickItem,null }, WoodShovelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,CobblestoneItem,null, null,StickItem,null, null,StickItem,null }, StoneShovelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,IronIngotItem,null, null,StickItem,null, null,StickItem,null }, IronShovelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,GoldIngotItem,null, null,StickItem,null, null,StickItem,null }, GoldShovelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,DiamondItem,null, null,StickItem,null, null,StickItem,null }, DiamondShovelItem, 1);

        // Факел из древесного угля
        AddShapeRecipe(new ItemDefinition?[] { null,CharcoalItem,null, null,StickItem,null, null,null,null }, TorchItem, 4);
        AddShapeRecipe(new ItemDefinition?[] { CharcoalItem,null,null, StickItem,null,null, null,null,null }, TorchItem, 4);

        // Шерсть из нитей (2×2 нити)
        AddShapeRecipe(new ItemDefinition?[] { StringItem,StringItem,null, StringItem,StringItem,null, null,null,null }, WhiteWoolItem, 1);

        // Мотыги
        AddShapeRecipe(new ItemDefinition?[] { PlankItem,PlankItem,null, null,StickItem,null, null,StickItem,null }, WoodHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,PlankItem,PlankItem, null,StickItem,null, null,StickItem,null }, WoodHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { CobblestoneItem,CobblestoneItem,null, null,StickItem,null, null,StickItem,null }, StoneHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,CobblestoneItem,CobblestoneItem, null,StickItem,null, null,StickItem,null }, StoneHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { IronIngotItem,IronIngotItem,null, null,StickItem,null, null,StickItem,null }, IronHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,IronIngotItem,IronIngotItem, null,StickItem,null, null,StickItem,null }, IronHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { GoldIngotItem,GoldIngotItem,null, null,StickItem,null, null,StickItem,null }, GoldHoeItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null,GoldIngotItem,GoldIngotItem, null,StickItem,null, null,StickItem,null }, GoldHoeItem, 1);
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

        // Огниво (Flint and Steel): 1 железо + 1 кремень (во всех комбинациях)
        AddShapeRecipe(new ItemDefinition?[] { IronIngotItem, null, null, null, FlintItem, null, null, null, null }, FlintAndSteelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null, IronIngotItem, null, FlintItem, null, null, null, null, null }, FlintAndSteelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { FlintItem, null, null, null, IronIngotItem, null, null, null, null }, FlintAndSteelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null, FlintItem, null, IronIngotItem, null, null, null, null, null }, FlintAndSteelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { IronIngotItem, FlintItem, null, null, null, null, null, null, null }, FlintAndSteelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { FlintItem, IronIngotItem, null, null, null, null, null, null, null }, FlintAndSteelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { IronIngotItem, null, null, FlintItem, null, null, null, null, null }, FlintAndSteelItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { FlintItem, null, null, IronIngotItem, null, null, null, null, null }, FlintAndSteelItem, 1);

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

        // Деревянная дверь: 6 досок (2 вертикальные колонки)
        AddShapeRecipe(new ItemDefinition?[] {
            PlankItem, PlankItem, null,
            PlankItem, PlankItem, null,
            PlankItem, PlankItem, null
        }, DoorItem, 3);
        AddShapeRecipe(new ItemDefinition?[] {
            null, PlankItem, PlankItem,
            null, PlankItem, PlankItem,
            null, PlankItem, PlankItem
        }, DoorItem, 3);

        // Стержень ифрита → 2 пороха ифрита
        AddShapeRecipe(new ItemDefinition?[] { BlazeRodItem, null, null, null, null, null, null, null, null }, BlazePowderItem, 2);
        AddShapeRecipe(new ItemDefinition?[] { null, BlazeRodItem, null, null, null, null, null, null, null }, BlazePowderItem, 2);
        AddShapeRecipe(new ItemDefinition?[] { null, null, null, BlazeRodItem, null, null, null, null, null }, BlazePowderItem, 2);

        // Око Эндера: порох ифрита + жемчуг Эндера
        AddShapeRecipe(new ItemDefinition?[] { BlazePowderItem, null, null, EnderPearlItem, null, null, null, null, null }, EyeOfEnderItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null, BlazePowderItem, null, null, EnderPearlItem, null, null, null, null }, EyeOfEnderItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { EnderPearlItem, null, null, BlazePowderItem, null, null, null, null, null }, EyeOfEnderItem, 1);
        AddShapeRecipe(new ItemDefinition?[] { null, EnderPearlItem, null, null, BlazePowderItem, null, null, null, null }, EyeOfEnderItem, 1);

        // Древесные опилки: 2 доски -> 4 опилок; 1 доска + 1 палка -> 2 опилок
        AddShapeRecipe(new ItemDefinition?[] { PlankItem, PlankItem, null, null, null, null, null, null, null }, SawdustItem, 4);
        AddShapeRecipe(new ItemDefinition?[] { PlankItem, null, null, StickItem, null, null, null, null, null }, SawdustItem, 2);

        // Каша из опилок: 2 опилки + 1 доска + 1 семена пшеницы
        AddShapeRecipe(new ItemDefinition?[] { SawdustItem, SawdustItem, null, PlankItem, WheatSeedsItem, null, null, null, null }, SawdustPorridgeItem, 1);

        // Ключ Бездны: четыре артефакта мини-боссов (Энд, Ад, Болото, Пустыня)
        AddShapeRecipe(new ItemDefinition?[] {
            EndSlimeItem, NetherArtifactItem, null,
            DesertArtifactItem, SwampArtifactItem, null,
            null, null, null
        }, VoidKeyItem, 1);
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


    public static readonly Dictionary<string, string> _recipeNames = new();
    public static string RecipeName(string id) => _recipeNames.TryGetValue(id, out var n) ? n : id;

    private static ItemDefinition Item(ushort id, string name, Material material, int maxStack = 64) =>
        new() { Id = id, Name = name, Material = material, MaxStack = maxStack };

    private static BlockType Block(ushort id, string name, Material material, ItemDefinition? drop,
                                   bool flammable = false, float burnTime = 0f, byte light = 0) =>
        new() {
            Id = id, Name = name, Material = material,
            DropItemId = drop?.Id ?? 0,
            IsFlammable = flammable, BurnTimeSeconds = burnTime,
            LightLevel = light,
        };

    private static BlockType With(this BlockType b, Action<BlockType> configure) {
        configure(b);
        return b;
    }
}




