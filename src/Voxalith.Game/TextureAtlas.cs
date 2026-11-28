using Raylib_cs;

namespace Voxalith.Game;

/// <summary>
/// Процедурный текстурный атлас: 8×4 тайлов по 16×16 пикселей.
/// Никаких файлов-ассетов — текстуры генерируются из палитры + шум.
/// </summary>
public static partial class TextureAtlas {
    public const int TilePx = 16;
    public const int Cols = 8, Rows = 28;
    public const int AtlasW = Cols * TilePx, AtlasH = Rows * TilePx;

    public const int TGrassTop = 0, TGrassSide = 1, TDirt = 2, TStone = 3,
                     TLogSide = 4, TLogTop = 5, TLeaves = 6, TPlanks = 7,
                     TCoalOre = 8, TTorch = 9, TIronOre = 10, TGoldOre = 11,
                     TDiamondOre = 12, TRedstoneOre = 13, TBedrock = 14, TCobblestone = 15,
                     TSand = 16, TGravel = 17, TGlass = 18, TWater = 19,
                     TLava = 20, TObsidian = 21, TWorkbench = 22, TFurnace = 23,
                     TApple = 24, TPorkRaw = 25, TPorkCooked = 26, TBread = 27,
                     TSticks = 28, TCoal = 29, TIronIngot = 30, TGoldIngot = 31,
                     TDiamond = 32, TRedstoneDust = 33, TFlint = 34, TClay = 35,
                     THeart = 36, THeartEmpty = 37, TFood = 38, TFoodEmpty = 39,
                     // 16 индивидуальных тайлов под разные инструменты и материалы
                     TPickaxeWood = 40, TPickaxeStone = 41, TPickaxeIron = 42, TPickaxeDiamond = 43,
                     TSwordWood = 44, TSwordStone = 45, TSwordIron = 46, TSwordDiamond = 47,
                     TAxeWood = 48, TAxeStone = 49, TAxeIron = 50, TAxeDiamond = 51,
                     TShovelWood = 52, TShovelStone = 53, TShovelIron = 54, TShovelDiamond = 55,
                     TFeather = 56, TGunpowder = 57, TString = 58, TArrow = 59, TBone = 60,
                     TCharcoal = 61, TRawBeef = 62, TCookedBeef = 63, TLeather = 64, TWool = 65,
                     TChestTop = 66, TChestSide = 67, TChestFront = 68,
                     TBedHeadTop = 69, TBedFootTop = 70, TBedSide = 71, TBedEnd = 120,
                     TBedTop = 69, // backward compatibility
                     TRottenFlesh = 72, TWheat = 73, TWheatSeeds = 74,
                     TFarmland = 75, TWheatCrop0 = 76, TWheatCrop1 = 77, TWheatCrop2 = 78, TWheatCrop3 = 79,
                     THoeWood = 80, THoeStone = 81, THoeIron = 82, THoeDiamond = 83,
                     TTallGrass = 84, TBoneMeal = 85, TSawdust = 86, TSawdustPorridge = 87, TTotem = 88,
                     TRawMutton = 89, TCookedMutton = 90,
                     TBow = 91, TShield = 92, TFlintAndSteel = 93, TGoldenApple = 94, TSaddle = 95,
                     TEnchantedBook = 96, TMusicDisc = 97, TNetherQuartz = 98, TBlazeRod = 99, TGlowstoneDust = 100,
                     TTNT = 101, TTNTSide = 102, TTNTTop = 103, TTNTBottom = 104,
                     TMossyCobble = 105, TMobSpawner = 106, TWeb = 107, TRail = 108, TPressurePlate = 109,
                     TChiseledSandstone = 110, TNetherrack = 111, TSoulSand = 112, TGlowstone = 113,
                     TNetherQuartzOre = 114, TNetherBrick = 115, TNetherPortal = 116,
                     TDoorLower = 117, TDoorUpper = 118, TDoorItem = 119,
                     TBucket = 121, TWaterBucket = 122, TLavaBucket = 123,
                     TEndStone = 124, TEndPortalFrame = 125, TEndPortal = 126, TEnderCrystal = 127,
                     TChorusPlant = 128, TChorusFlower = 129,
                     TEnderPearl = 130, TEyeOfEnder = 131, TBlazePowder = 132, TChorusFruit = 133, TEndSlime = 134,
                     TNetherArtifact = 135, TSwampArtifact = 136, TDesertArtifact = 137, TVoidKey = 138,
                     TVoidGate = 139,
                     TPickaxeGold = 140, TAxeGold = 141, TSwordGold = 142, TShovelGold = 143, THoeGold = 144,
                     TLeavesPlains = 148, TLeavesSavanna = 149, TLeavesSwamp = 150, TWaterSwamp = 151,
                     TGrassTopPlains = 152, TGrassSidePlains = 153, TTallGrassPlains = 154,
                     TGrassTopSavanna = 155, TGrassSideSavanna = 156, TTallGrassSavanna = 157,
                     TGrassTopSwamp = 158, TGrassSideSwamp = 159, TTallGrassSwamp = 160,
                     TLeatherHelmet = 161, TLeatherChestplate = 162, TLeatherLeggings = 163, TLeatherBoots = 164,
                     TIronHelmet = 165, TIronChestplate = 166, TIronLeggings = 167, TIronBoots = 168,
                     TDiamondHelmet = 169, TDiamondChestplate = 170, TDiamondLeggings = 171, TDiamondBoots = 172,
                     TArmorIcon = 173, TArmorIconHalf = 174, TArmorIconEmpty = 175,
                     TSapling = 176, TRedFlower = 177, TYellowFlower = 178,
                     TCarrot = 179, TPotato = 180, TBakedPotato = 181,
                     TCarrotCrop0 = 182, TCarrotCrop1 = 183, TCarrotCrop2 = 184, TCarrotCrop3 = 185,
                     TPotatoCrop0 = 186, TPotatoCrop1 = 187, TPotatoCrop2 = 188, TPotatoCrop3 = 189,
                     THeartParticle = 190, TFire = 191,
                     TRawChicken = 192, TCookedChicken = 193, TEgg = 194,
                     TJukeboxTop = 195, TJukeboxSide = 196;

    public record struct BlockFaceTiles(byte PosX, byte NegX, byte PosY, byte NegY, byte PosZ, byte NegZ);

    private static Texture2D _atlas;
    private static readonly Dictionary<ushort, BlockFaceTiles> _blockTiles = new();
    private static readonly Dictionary<ushort, byte> _itemTiles = new();
    private static bool _ready;

    public static string TextureDirectory =>
        AssetLocator.FindAssetSubdir(
            "textures",
            (root, candidate) =>
                File.Exists(Path.Combine(root, "Voxalith.sln")) ||
                File.Exists(Path.Combine(root, "Voxalith.slnx")) ||
                File.Exists(Path.Combine(candidate, "blocks", "stone.png")))
        ?? Path.Combine(Directory.GetCurrentDirectory(), "assets", "textures");

    public static readonly Dictionary<int, string> TileFiles = new() {
        // Блоки (0..23)
        [TGrassTop] = "blocks/grass_top.png",
        [TGrassSide] = "blocks/grass_side.png",
        [TDirt] = "blocks/dirt.png",
        [TStone] = "blocks/stone.png",
        [TLogSide] = "blocks/log_side.png",
        [TLogTop] = "blocks/log_top.png",
        [TLeaves] = "blocks/leaves.png",
        [TPlanks] = "blocks/planks.png",
        [TCoalOre] = "blocks/coal_ore.png",
        [TTorch] = "blocks/torch.png",
        [TIronOre] = "blocks/iron_ore.png",
        [TGoldOre] = "blocks/gold_ore.png",
        [TDiamondOre] = "blocks/diamond_ore.png",
        [TRedstoneOre] = "blocks/redstone_ore.png",
        [TBedrock] = "blocks/bedrock.png",
        [TCobblestone] = "blocks/cobblestone.png",
        [TSand] = "blocks/sand.png",
        [TGravel] = "blocks/gravel.png",
        [TGlass] = "blocks/glass.png",
        [TWater] = "blocks/water.png",
        [TLava] = "blocks/lava.png",
        [TObsidian] = "blocks/obsidian.png",
        [TWorkbench] = "blocks/workbench.png",
        [TFurnace] = "blocks/furnace.png",

        // Предметы и ресурсы (24..35)
        [TApple] = "items/apple.png",
        [TPorkRaw] = "items/porkchop_raw.png",
        [TPorkCooked] = "items/porkchop_cooked.png",
        [TBread] = "items/bread.png",
        [TSticks] = "items/stick.png",
        [TCoal] = "items/coal.png",
        [TIronIngot] = "items/iron_ingot.png",
        [TGoldIngot] = "items/gold_ingot.png",
        [TDiamond] = "items/diamond.png",
        [TRedstoneDust] = "items/redstone.png",
        [TFlint] = "items/flint.png",
        [TClay] = "items/clay.png",

        // GUI (36..39)
        [THeart] = "gui/heart.png",
        [THeartEmpty] = "gui/heart_empty.png",
        [TFood] = "gui/food.png",
        [TFoodEmpty] = "gui/food_empty.png",

        // Инструменты (40..55)
        [TPickaxeWood] = "items/wooden_pickaxe.png",
        [TPickaxeStone] = "items/stone_pickaxe.png",
        [TPickaxeIron] = "items/iron_pickaxe.png",
        [TPickaxeDiamond] = "items/diamond_pickaxe.png",
        [TSwordWood] = "items/wooden_sword.png",
        [TSwordStone] = "items/stone_sword.png",
        [TSwordIron] = "items/iron_sword.png",
        [TSwordDiamond] = "items/diamond_sword.png",
        [TAxeWood] = "items/wooden_axe.png",
        [TAxeStone] = "items/stone_axe.png",
        [TAxeIron] = "items/iron_axe.png",
        [TAxeDiamond] = "items/diamond_axe.png",
        [TShovelWood] = "items/wooden_shovel.png",
        [TShovelStone] = "items/stone_shovel.png",
        [TShovelIron] = "items/iron_shovel.png",
        [TShovelDiamond] = "items/diamond_shovel.png",

        // Дропы и новые предметы
        [TFeather] = "items/feather.png",
        [TGunpowder] = "items/gunpowder.png",
        [TString] = "items/string.png",
        [TArrow] = "items/arrow.png",
        [TBone] = "items/bone.png",
        [TCharcoal] = "items/charcoal.png",
        [TRawBeef] = "items/beef_raw.png",
        [TCookedBeef] = "items/beef_cooked.png",
        [TLeather] = "items/leather.png",
        [TWool] = "items/wool.png",
        [TRottenFlesh] = "items/rotten_flesh.png",
        [TWheat] = "items/wheat.png",
        [TWheatSeeds] = "items/seeds.png",
        [TTallGrass] = "blocks/tallgrass.png",
        [TBoneMeal] = "items/bonemeal.png",
        [TSawdust] = "items/sawdust.png",
        [TSawdustPorridge] = "items/sawdust_porridge.png",
        [TTotem] = "items/totem.png",
        [TRawMutton] = "items/mutton_raw.png",
        [TCookedMutton] = "items/mutton_cooked.png",
        [TBow] = "items/bow.png",
        [TShield] = "items/shield.png",
        [TFlintAndSteel] = "items/flint_and_steel.png",
        [TGoldenApple] = "items/apple_golden.png",
        [TSaddle] = "items/saddle.png",
        [TEnchantedBook] = "items/book_enchanted.png",
        [TMusicDisc] = "items/record_13.png",
        [TNetherQuartz] = "items/quartz.png",
        [TBlazeRod] = "items/blaze_rod.png",
        [TGlowstoneDust] = "items/glowstone_dust.png",
        [TTNT] = "blocks/tnt.png",
        [TTNTSide] = "blocks/tnt_side.png",
        [TTNTTop] = "blocks/tnt_top.png",
        [TTNTBottom] = "blocks/tnt_bottom.png",
        [TMossyCobble] = "blocks/cobblestone_mossy.png",
        [TMobSpawner] = "blocks/mob_spawner.png",
        [TWeb] = "blocks/web.png",
        [TRail] = "blocks/rail.png",
        [TPressurePlate] = "blocks/pressure_plate.png",
        [TChiseledSandstone] = "blocks/sandstone_chiseled.png",
        [TNetherrack] = "blocks/netherrack.png",
        [TSoulSand] = "blocks/soul_sand.png",
        [TGlowstone] = "blocks/glowstone.png",
        [TNetherQuartzOre] = "blocks/quartz_ore.png",
        [TNetherBrick] = "blocks/nether_brick.png",
        [TNetherPortal] = "blocks/portal.png",
        [TBucket] = "items/bucket.png",
        [TWaterBucket] = "items/bucket_water.png",
        [TLavaBucket] = "items/bucket_lava.png",
        // Энд (124..134)
        [TEndStone] = "blocks/end_stone.png",
        [TEndPortalFrame] = "blocks/end_portal_frame.png",
        [TEndPortal] = "blocks/end_portal.png",
        [TEnderCrystal] = "blocks/ender_crystal.png",
        [TChorusPlant] = "blocks/chorus_plant.png",
        [TChorusFlower] = "blocks/chorus_flower.png",
        [TEnderPearl] = "items/ender_pearl.png",
        [TEyeOfEnder] = "items/eye_of_ender.png",
        [TBlazePowder] = "items/blaze_powder.png",
        [TChorusFruit] = "items/chorus_fruit.png",
        [TEndSlime] = "items/end_slime.png",
        // Блоки без файлов ранее (сундук, кровать, грядка, посевы, двери, врата)
        [TChestTop] = "blocks/chest_top.png",
        [TChestSide] = "blocks/chest_side.png",
        [TChestFront] = "blocks/chest_front.png",
        [TBedHeadTop] = "blocks/bed_head_top.png",
        [TBedFootTop] = "blocks/bed_foot_top.png",
        [TBedSide] = "blocks/bed_side.png",
        [TBedEnd] = "blocks/bed_end.png",
        [TFarmland] = "blocks/farmland.png",
        [TWheatCrop0] = "blocks/wheat_crop_0.png",
        [TWheatCrop1] = "blocks/wheat_crop_1.png",
        [TWheatCrop2] = "blocks/wheat_crop_2.png",
        [TWheatCrop3] = "blocks/wheat_crop_3.png",
        [TDoorLower] = "blocks/door_lower.png",
        [TDoorUpper] = "blocks/door_upper.png",
        [TDoorItem] = "items/door.png",
        [TVoidGate] = "blocks/void_gate.png",
        // Мотыги и золотые инструменты
        [THoeWood] = "items/wooden_hoe.png",
        [THoeStone] = "items/stone_hoe.png",
        [THoeIron] = "items/iron_hoe.png",
        [THoeDiamond] = "items/diamond_hoe.png",
        [TPickaxeGold] = "items/golden_pickaxe.png",
        [TAxeGold] = "items/golden_axe.png",
        [TSwordGold] = "items/golden_sword.png",
        [TShovelGold] = "items/golden_shovel.png",
        [THoeGold] = "items/golden_hoe.png",
        // Артефакты и ключ
        [TNetherArtifact] = "items/nether_artifact.png",
        [TSwampArtifact] = "items/swamp_artifact.png",
        [TDesertArtifact] = "items/desert_artifact.png",
        [TVoidKey] = "items/void_key.png",
        [TFire] = "blocks/fire.png",
        [TSapling] = "blocks/sapling.png",
        [TRedFlower] = "blocks/flower_red.png",
        [TYellowFlower] = "blocks/flower_yellow.png",
        [TRawChicken] = "items/chicken_raw.png",
        [TCookedChicken] = "items/chicken_cooked.png",
        [TEgg] = "items/egg.png",
        [TJukeboxTop] = "blocks/jukebox_top.png",
        [TJukeboxSide] = "blocks/jukebox_side.png",
        // Биомные цвета листвы и воды
        [TLeavesPlains] = "blocks/leaves_plains.png",
        [TLeavesSavanna] = "blocks/leaves_savanna.png",
        [TLeavesSwamp] = "blocks/leaves_swamp.png",
        [TWaterSwamp] = "blocks/water_swamp.png",
        // Биомные цвета травы
        [TGrassTopPlains] = "blocks/grass_top_plains.png",
        [TGrassSidePlains] = "blocks/grass_side_plains.png",
        [TTallGrassPlains] = "blocks/tallgrass_plains.png",
        [TGrassTopSavanna] = "blocks/grass_top_savanna.png",
        [TGrassSideSavanna] = "blocks/grass_side_savanna.png",
        [TTallGrassSavanna] = "blocks/tallgrass_savanna.png",
        [TGrassTopSwamp] = "blocks/grass_top_swamp.png",
        [TGrassSideSwamp] = "blocks/grass_side_swamp.png",
        [TTallGrassSwamp] = "blocks/tallgrass_swamp.png",
        // Броня (Кожаная, Железная, Алмазная)
        [TLeatherHelmet] = "items/leather_helmet.png",
        [TLeatherChestplate] = "items/leather_chestplate.png",
        [TLeatherLeggings] = "items/leather_leggings.png",
        [TLeatherBoots] = "items/leather_boots.png",
        [TIronHelmet] = "items/iron_helmet.png",
        [TIronChestplate] = "items/iron_chestplate.png",
        [TIronLeggings] = "items/iron_leggings.png",
        [TIronBoots] = "items/iron_boots.png",
        [TDiamondHelmet] = "items/diamond_helmet.png",
        [TDiamondChestplate] = "items/diamond_chestplate.png",
        [TDiamondLeggings] = "items/diamond_leggings.png",
        [TDiamondBoots] = "items/diamond_boots.png",
        // GUI иконки
        [TArmorIcon] = "gui/armor.png",
        [TArmorIconHalf] = "gui/armor_half.png",
        [TArmorIconEmpty] = "gui/armor_empty.png",
        [THeartParticle] = "gui/heart_particle.png",
        // Культуры и корнеплоды
        [TCarrot] = "items/carrot.png",
        [TPotato] = "items/potato.png",
        [TBakedPotato] = "items/potato_baked.png",
        [TCarrotCrop0] = "blocks/carrot_crop_0.png",
        [TCarrotCrop1] = "blocks/carrot_crop_1.png",
        [TCarrotCrop2] = "blocks/carrot_crop_2.png",
        [TCarrotCrop3] = "blocks/carrot_crop_3.png",
        [TPotatoCrop0] = "blocks/potato_crop_0.png",
        [TPotatoCrop1] = "blocks/potato_crop_1.png",
        [TPotatoCrop2] = "blocks/potato_crop_2.png",
        [TPotatoCrop3] = "blocks/potato_crop_3.png",
    };

    public static Texture2D Atlas => _atlas;
    public static bool Ready => _ready;

    public static bool IsCompletelyBlackImage(Image img) {
        if (img.Width <= 0 || img.Height <= 0) return true;
        int nonTransparent = 0;
        for (int py = 0; py < img.Height; py++) {
            for (int px = 0; px < img.Width; px++) {
                var c = Raylib.GetImageColor(img, px, py);
                if (c.A > 10) {
                    nonTransparent++;
                    if (c.R > 0 || c.G > 0 || c.B > 0) {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    public static void GenerateDefaultTextures(bool forceOverwrite = false) {
        var masterImage = GenerateProceduralImage();
        foreach (var (tile, relPath) in TileFiles) {
            string fullPath = Path.Combine(TextureDirectory, relPath);
            bool shouldWrite = forceOverwrite || !File.Exists(fullPath);
            if (!shouldWrite && File.Exists(fullPath)) {
                var existing = Raylib.LoadImage(fullPath);
                if (IsCompletelyBlackImage(existing) || tile == TWater || tile == TLava) shouldWrite = true;
                unsafe { Raylib.UnloadImage(existing); }
            }
            if (shouldWrite) {
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                int sx = tile % Cols * TilePx;
                int sy = tile / Cols * TilePx;
                var tileImage = Raylib.ImageFromImage(masterImage, new Rectangle(sx, sy, TilePx, TilePx));
                Raylib.ExportImage(tileImage, fullPath);
                unsafe { Raylib.UnloadImage(tileImage); }
            }
        }
        unsafe { Raylib.UnloadImage(masterImage); }
    }

    public static void GenerateAtlasFile() {
        GenerateDefaultTextures(forceOverwrite: false);
    }

    public static void Load() {
        if (_ready) return;

        GenerateDefaultTextures(forceOverwrite: false);

        var atlasImage = Raylib.GenImageColor(AtlasW, AtlasH, new Color(0, 0, 0, 0));
        var fallbackProcedural = GenerateProceduralImage();

        for (int tile = 0; tile < Cols * Rows; tile++) {
            int dx = tile % Cols * TilePx;
            int dy = tile / Cols * TilePx;

            if (TileFiles.TryGetValue(tile, out var relPath)) {
                string fullPath = Path.Combine(TextureDirectory, relPath);
                if (File.Exists(fullPath)) {
                    var tileImage = Raylib.LoadImage(fullPath);
                    if (tileImage.Width > 0 && tileImage.Height > 0) {
                        if (tileImage.Width != TilePx || tileImage.Height != TilePx) {
                            Raylib.ImageResize(ref tileImage, TilePx, TilePx);
                        }
                        if (!IsCompletelyBlackImage(tileImage)) {
                            for (int py = 0; py < TilePx; py++) {
                                for (int px = 0; px < TilePx; px++) {
                                    var col = Raylib.GetImageColor(tileImage, px, py);
                                    if (tile == TWater) col.A = 155;
                                    unsafe {
                                        Raylib.ImageDrawPixel(ref atlasImage, dx + px, dy + py, col);
                                    }
                                }
                            }
                            unsafe { Raylib.UnloadImage(tileImage); }
                            continue;
                        }
                    }
                    unsafe { Raylib.UnloadImage(tileImage); }
                }
            }

            // Если файла нет или он был полностью черным — копируем из процедурного фолбека
            for (int py = 0; py < TilePx; py++) {
                for (int px = 0; px < TilePx; px++) {
                    var col = Raylib.GetImageColor(fallbackProcedural, dx + px, dy + py);
                    unsafe {
                        Raylib.ImageDrawPixel(ref atlasImage, dx + px, dy + py, col);
                    }
                }
            }
        }

        unsafe { Raylib.UnloadImage(fallbackProcedural); }

        _atlas = Raylib.LoadTextureFromImage(atlasImage);
        unsafe { Raylib.UnloadImage(atlasImage); }
        Raylib.GenTextureMipmaps(ref _atlas);
        Raylib.SetTextureFilter(_atlas, TextureFilter.Anisotropic4X);
        _ready = true;
    }


    public static void Unload() {
        if (_ready) {
            Raylib.UnloadTexture(_atlas);
            _ready = false;
        }
    }

    public static void SetBlockTiles(ushort blockId, byte top, byte side, byte bottom) =>
        _blockTiles[blockId] = new BlockFaceTiles(side, side, top, bottom, side, side);

    public static void SetBlockFaces(ushort blockId, byte posX, byte negX, byte top, byte bottom, byte posZ, byte negZ) =>
        _blockTiles[blockId] = new BlockFaceTiles(posX, negX, top, bottom, posZ, negZ);

    public static void SetItemTile(ushort itemId, byte tile) => _itemTiles[itemId] = tile;

    public static BlockFaceTiles BlockTiles(ushort blockId) =>
        _blockTiles.TryGetValue(blockId, out var t) ? t : new BlockFaceTiles((byte)TDirt, (byte)TDirt, (byte)TDirt, (byte)TDirt, (byte)TDirt, (byte)TDirt);

    public static byte ItemTile(ushort itemId) => _itemTiles.TryGetValue(itemId, out var t) ? t : (byte)TDirt;

    /// <summary>UV-прямоугольник тайла в атласе [0..1], утоплен на пол-текселя внутрь тайла.
    /// Без этого на границах тайлов из-за точности интерполяции подтягиваются пиксели
    /// соседних текстур — тонкая полоска справа/сверху («кровотечение атласа»).</summary>
    public static Rectangle TileUv(byte tile) {
        float du = 0.5f / AtlasW;   // пол-текселя в U
        float dv = 0.5f / AtlasH;   // пол-текселя в V
        return new Rectangle(
            tile % Cols * TilePx / (float)AtlasW + du,
            tile / Cols * TilePx / (float)AtlasH + dv,
            TilePx / (float)AtlasW - 2f * du,
            TilePx / (float)AtlasH - 2f * dv);
    }

    /// <summary>Прямоугольник тайла в пикселях атласа, утоплен на inset пикселей от границ
    /// (для DrawTexturePro с пиксельными rect — та же защита от кровотечения атласа).</summary>
    public static Rectangle TilePixelRect(byte tile, float inset = 0.5f) => new(
        tile % Cols * TilePx + inset,
        tile / Cols * TilePx + inset,
        TilePx - 2f * inset,
        TilePx - 2f * inset);
}
