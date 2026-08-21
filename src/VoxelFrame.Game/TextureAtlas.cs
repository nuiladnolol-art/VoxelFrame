using Raylib_cs;

namespace VoxelFrame.Game;

/// <summary>
/// Процедурный текстурный атлас: 8×4 тайлов по 16×16 пикселей.
/// Никаких файлов-ассетов — текстуры генерируются из палитры + шум.
/// </summary>
public static class TextureAtlas {
    public const int TilePx = 16;
    public const int Cols = 8, Rows = 16;
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
                     TBedTop = 69, TBedSide = 70, TBedEnd = 71,
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
                     TNetherQuartzOre = 114, TNetherBrick = 115, TNetherPortal = 116;

    public record struct BlockFaceTiles(byte PosX, byte NegX, byte PosY, byte NegY, byte PosZ, byte NegZ);

    private static Texture2D _atlas;
    private static readonly Dictionary<ushort, BlockFaceTiles> _blockTiles = new();
    private static readonly Dictionary<ushort, byte> _itemTiles = new();
    private static bool _ready;

    public static string TextureDirectory {
        get {
            string[] probeStarts = { Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory };
            foreach (var start in probeStarts) {
                var dir = new DirectoryInfo(start);
                while (dir != null) {
                    string candidate = Path.Combine(dir.FullName, "assets", "textures");
                    if (Directory.Exists(candidate) && (File.Exists(Path.Combine(dir.FullName, "VoxelGame.sln")) || File.Exists(Path.Combine(candidate, "blocks", "stone.png")))) {
                        return candidate;
                    }
                    dir = dir.Parent;
                }
            }
            foreach (var start in probeStarts) {
                var dir = new DirectoryInfo(start);
                while (dir != null) {
                    string candidate = Path.Combine(dir.FullName, "assets", "textures");
                    if (Directory.Exists(candidate)) {
                        return candidate;
                    }
                    dir = dir.Parent;
                }
            }
            return Path.Combine(Directory.GetCurrentDirectory(), "assets", "textures");
        }
    }

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
                if (IsCompletelyBlackImage(existing)) shouldWrite = true;
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
        _ready = true;
    }

    public static Image GenerateProceduralImage() {
        var image = Raylib.GenImageColor(AtlasW, AtlasH, new Color(0, 0, 0, 0));

        var palette = new (Color Base, bool Grain)[Cols * Rows];
        palette[TGrassTop] = (new Color(96, 158, 62, 255), true);
        palette[TGrassSide] = (new Color(122, 92, 58, 255), true);
        palette[TDirt] = (new Color(134, 96, 58, 255), true);
        palette[TStone] = (new Color(128, 128, 132, 255), true);
        palette[TLogSide] = (new Color(98, 70, 42, 255), true);
        palette[TLogTop] = (new Color(150, 118, 76, 255), false);
        palette[TLeaves] = (new Color(52, 118, 40, 255), true);
        palette[TPlanks] = (new Color(168, 132, 82, 255), false);
        palette[TCoalOre] = (new Color(106, 106, 110, 255), true);
        palette[TTorch] = (new Color(176, 138, 74, 255), true);
        palette[TBedrock] = (new Color(48, 44, 40, 255), true);
        palette[TApple] = (new Color(206, 44, 44, 255), false);
        palette[TPorkRaw] = (new Color(222, 140, 140, 255), true);
        palette[TPorkCooked] = (new Color(150, 92, 58, 255), true);
        palette[TSticks] = (new Color(130, 96, 52, 255), false);
        palette[TCoal] = (new Color(44, 44, 48, 255), true);
        palette[TCharcoal] = (new Color(36, 32, 30, 255), true);
        palette[TRawBeef] = (new Color(190, 40, 40, 255), true);
        palette[TCookedBeef] = (new Color(110, 55, 30, 255), true);
        palette[TLeather] = (new Color(175, 110, 60, 255), true);
        palette[TWool] = (new Color(230, 230, 230, 255), true);
        palette[TFeather] = (new Color(230, 230, 240, 255), false);
        palette[TGunpowder] = (new Color(60, 60, 65, 255), true);
        palette[TString] = (new Color(220, 220, 220, 255), false);
        palette[TArrow] = (new Color(140, 100, 60, 255), false);
        palette[TBone] = (new Color(225, 225, 215, 255), false);
        palette[THeart] = (new Color(220, 20, 40, 255), false);
        palette[TFood] = (new Color(160, 90, 40, 255), false);
        palette[THeartEmpty] = (new Color(60, 60, 60, 255), false);
        palette[TFoodEmpty] = (new Color(60, 60, 60, 255), false);
        palette[TIronOre] = (new Color(130, 115, 100, 255), true);
        palette[TWorkbench] = (new Color(142, 108, 62, 255), false);
        palette[TFurnace] = (new Color(100, 96, 92, 255), true);
        palette[TIronIngot] = (new Color(210, 210, 215, 255), false);
        palette[TBread] = (new Color(196, 150, 70, 255), false);

        palette[TCobblestone] = (new Color(110, 110, 115, 255), true);
        palette[TSand] = (new Color(220, 210, 155, 255), true);
        palette[TGravel] = (new Color(125, 120, 120, 255), true);
        palette[TGlass] = (new Color(210, 240, 255, 160), false);
        palette[TWater] = (new Color(40, 80, 220, 180), true);
        palette[TLava] = (new Color(240, 90, 15, 255), true);
        palette[TGoldOre] = (new Color(130, 125, 110, 255), true);
        palette[TDiamondOre] = (new Color(120, 125, 130, 255), true);
        palette[TRedstoneOre] = (new Color(130, 115, 115, 255), true);
        palette[TObsidian] = (new Color(25, 18, 38, 255), true);
        palette[TGoldIngot] = (new Color(245, 215, 50, 255), false);
        palette[TDiamond] = (new Color(90, 230, 240, 255), false);
        palette[TRedstoneDust] = (new Color(220, 30, 20, 255), false);
        palette[TFlint] = (new Color(50, 50, 55, 255), false);
        palette[TClay] = (new Color(160, 165, 175, 255), true);
        palette[TChestTop] = (new Color(150, 105, 55, 255), true);
        palette[TChestSide] = (new Color(150, 105, 55, 255), true);
        palette[TChestFront] = (new Color(150, 105, 55, 255), true);
        palette[TBedTop] = (new Color(210, 40, 40, 255), true);
        palette[TBedSide] = (new Color(210, 40, 40, 255), true);
        palette[TBedEnd] = (new Color(210, 40, 40, 255), true);
        palette[TRottenFlesh] = (new Color(150, 75, 45, 255), true);
        palette[TWheat] = (new Color(225, 190, 60, 255), true);
        palette[TWheatSeeds] = (new Color(160, 180, 80, 255), false);
        palette[TFarmland] = (new Color(85, 55, 30, 255), true);
        palette[TWheatCrop0] = (new Color(80, 180, 40, 255), false);
        palette[TWheatCrop1] = (new Color(100, 200, 50, 255), false);
        palette[TWheatCrop2] = (new Color(170, 190, 50, 255), false);
        palette[TWheatCrop3] = (new Color(225, 190, 50, 255), false);
        palette[THoeWood] = (new Color(140, 95, 55, 255), false);
        palette[THoeStone] = (new Color(150, 150, 150, 255), false);
        palette[THoeIron] = (new Color(220, 220, 220, 255), false);
        palette[THoeDiamond] = (new Color(90, 230, 240, 255), false);
        palette[TTallGrass] = (new Color(90, 175, 45, 255), false);
        palette[TRawMutton] = (new Color(215, 65, 75, 255), true);
        palette[TCookedMutton] = (new Color(145, 75, 38, 255), true);
        palette[TBow] = (new Color(130, 96, 52, 255), false);
        palette[TShield] = (new Color(160, 120, 70, 255), false);
        palette[TFlintAndSteel] = (new Color(200, 200, 210, 255), false);
        palette[TGoldenApple] = (new Color(255, 215, 30, 255), false);
        palette[TSaddle] = (new Color(140, 80, 40, 255), false);
        palette[TEnchantedBook] = (new Color(150, 60, 180, 255), false);
        palette[TMusicDisc] = (new Color(30, 30, 35, 255), false);
        palette[TNetherQuartz] = (new Color(235, 230, 225, 255), false);
        palette[TBlazeRod] = (new Color(255, 170, 20, 255), false);
        palette[TGlowstoneDust] = (new Color(255, 220, 60, 255), false);
        palette[TTNT] = (new Color(210, 40, 30, 255), true);
        palette[TTNTSide] = (new Color(210, 40, 30, 255), true);
        palette[TTNTTop] = (new Color(210, 40, 30, 255), true);
        palette[TTNTBottom] = (new Color(210, 40, 30, 255), true);
        palette[TMossyCobble] = (new Color(90, 120, 90, 255), true);
        palette[TMobSpawner] = (new Color(35, 45, 60, 255), false);
        palette[TWeb] = (new Color(240, 240, 245, 180), false);
        palette[TRail] = (new Color(180, 160, 130, 255), false);
        palette[TPressurePlate] = (new Color(120, 120, 125, 255), true);
        palette[TChiseledSandstone] = (new Color(215, 205, 150, 255), true);
        palette[TNetherrack] = (new Color(130, 35, 35, 255), true);
        palette[TSoulSand] = (new Color(85, 65, 55, 255), true);
        palette[TGlowstone] = (new Color(245, 205, 95, 255), true);
        palette[TNetherQuartzOre] = (new Color(135, 45, 45, 255), true);
        palette[TNetherBrick] = (new Color(55, 25, 30, 255), true);
        palette[TNetherPortal] = (new Color(120, 30, 210, 200), true);

        var rng = new Random(20260812);
        for (int tile = 0; tile < palette.Length; tile++) {
            var (baseColor, grain) = palette[tile];
            if (baseColor.A == 0 && tile != TLeaves && tile != TGlass && tile != TWater) {
                // Если базовый цвет нулевой, проверяем кастомные отрисовки
            }
            for (int py = 0; py < TilePx; py++) {
                for (int px = 0; px < TilePx; px++) {
                    int r = baseColor.R, g = baseColor.G, b = baseColor.B;
                    byte a = baseColor.A > 0 ? baseColor.A : (byte)255;
                    
                    if (grain) {
                        int d = rng.Next(-28, 29);
                        r = Math.Clamp(r + d, 0, 255);
                        g = Math.Clamp(g + d, 0, 255);
                        b = Math.Clamp(b + d, 0, 255);
                    }
                    
                    if (tile == TApple) {
                        bool insideApple = (px - 8) * (px - 8) + (py - 9) * (py - 9) <= 20;
                        bool isStem = px == 8 && py >= 2 && py <= 4;
                        if (insideApple) {
                            r = 206; g = 44; b = 44;
                        } else if (isStem) {
                            r = 100; g = 70; b = 30;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TSticks) {
                        bool insideStick = Math.Abs(px + py - 15) <= 1 && px >= 2 && px <= 13;
                        if (insideStick) {
                            r = 130; g = 96; b = 52;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TCoal) {
                        bool insideCoal = (px - 8) * (px - 8) + (py - 8) * (py - 8) <= 18 + (px * 3 + py * 7) % 5;
                        if (insideCoal) {
                            r = 44; g = 44; b = 48;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TPorkRaw) {
                        bool insidePork = (px - 8) * (px - 8) * 0.8f + (py - 8) * (py - 8) * 1.2f <= 18 + (px + py) % 4;
                        bool isFat = insidePork && (px - py == 2 || px - py == 3);
                        if (isFat) {
                            r = 255; g = 230; b = 230;
                        } else if (insidePork) {
                            r = 222; g = 140; b = 140;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TPorkCooked) {
                        bool insidePork = (px - 8) * (px - 8) * 0.8f + (py - 8) * (py - 8) * 1.2f <= 18 + (px + py) % 4;
                        bool isFat = insidePork && (px - py == 2 || px - py == 3);
                        if (isFat) {
                            r = 230; g = 200; b = 180;
                        } else if (insidePork) {
                            r = 150; g = 92; b = 58;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TWater) {
                        int wave = ((px * 3 + py * 5 + (px ^ py)) % 7);
                        r = 38 + wave * 4;
                        g = 85 + wave * 9;
                        b = 215 + wave * 5;
                        a = 160; // Полупрозрачная вода по канону
                    } else if (tile == TLava) {
                        int lavaWave = ((px * 2 + py * 7) % 6);
                        r = 235 + lavaWave * 3;
                        g = 70 + lavaWave * 15;
                        b = 10;
                        a = 255;
                    } else if (tile == TChestTop || tile == TChestSide) {
                        bool isBorder = px <= 1 || px >= 14 || py <= 1 || py >= 14;
                        if (isBorder) { r = 85; g = 50; b = 25; a = 255; }
                        else { r = 150 + (px % 3) * 5; g = 105 + (px % 3) * 4; b = 55; a = 255; }
                    } else if (tile == TChestFront) {
                        bool isBorder = px <= 1 || px >= 14 || py <= 1 || py >= 14;
                        bool isLock = px >= 7 && px <= 8 && py >= 5 && py <= 8;
                        if (isLock) { r = 220; g = 220; b = 225; a = 255; }
                        else if (isBorder) { r = 85; g = 50; b = 25; a = 255; }
                        else { r = 150 + (px % 3) * 5; g = 105 + (px % 3) * 4; b = 55; a = 255; }
                    } else if (tile == TBedTop) {
                        bool isPillow = py <= 5 && px >= 2 && px <= 13;
                        if (isPillow) { r = 245; g = 245; b = 250; a = 255; }
                        else { r = 210 + (px % 2) * 10; g = 35; b = 35; a = 255; }
                    } else if (tile == TBedSide) {
                        if (py >= 12) { r = 120; g = 80; b = 40; a = 255; }
                        else if (px <= 5 && py <= 5) { r = 245; g = 245; b = 250; a = 255; }
                        else { r = 210; g = 35; b = 35; a = 255; }
                    } else if (tile == TBedEnd) {
                        if (py >= 12) { r = 120; g = 80; b = 40; a = 255; }
                        else { r = 210; g = 35; b = 35; a = 255; }
                    } else if (tile == TTorch) {
                        bool isStick = px >= 7 && px <= 8 && py >= 6 && py <= 14;
                        bool isFlame = px >= 6 && px <= 9 && py >= 1 && py <= 5;
                        if (isStick) {
                            r = 130; g = 96; b = 52;
                        } else if (isFlame) {
                            r = 255; g = 120 + (py * 25) % 135; b = 0;
                        } else {
                            a = 0;
                        }
                    } else if (tile == THeart) {
                        byte[,] heartMap = new byte[16, 16] {
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,1,1,1,0,0,0,0,0,1,1,1,0,0,0},
                            {0,1,2,2,2,1,0,0,0,1,2,2,2,1,0,0},
                            {0,1,3,2,2,2,1,0,1,2,2,2,2,1,0,0},
                            {0,1,2,2,2,2,2,1,2,2,2,2,2,1,0,0},
                            {0,0,1,2,2,2,2,2,2,2,2,2,1,0,0,0},
                            {0,0,0,1,2,2,2,2,2,2,2,1,0,0,0,0},
                            {0,0,0,0,1,2,2,2,2,2,1,0,0,0,0,0},
                            {0,0,0,0,0,1,2,2,2,1,0,0,0,0,0,0},
                            {0,0,0,0,0,0,1,2,1,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0}
                        };
                        byte val = heartMap[py, px];
                        if (val == 1) { r = 40; g = 10; b = 10; a = 255; }
                        else if (val == 2) { r = 220; g = 20; b = 40; a = 255; }
                        else if (val == 3) { r = 255; g = 140; b = 160; a = 255; }
                        else { a = 0; }
                    } else if (tile == TFood) {
                        byte[,] foodMap = new byte[16, 16] {
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,1,1,1,1,0,0,0,0},
                            {0,0,0,0,0,0,0,1,2,2,2,2,1,0,0,0},
                            {0,0,0,0,0,0,1,2,2,2,2,2,2,1,0,0},
                            {0,0,0,0,0,1,2,2,2,2,2,2,2,1,0,0},
                            {0,0,0,0,0,1,2,2,2,2,2,2,1,0,0,0},
                            {0,0,0,0,1,2,2,2,2,2,1,1,0,0,0,0},
                            {0,0,0,1,2,2,2,2,1,1,0,0,0,0,0,0},
                            {0,0,0,1,2,2,1,1,0,0,0,0,0,0,0,0},
                            {0,0,1,1,1,1,0,0,0,0,0,0,0,0,0,0},
                            {0,1,3,3,1,0,0,0,0,0,0,0,0,0,0,0},
                            {1,3,3,1,0,0,0,0,0,0,0,0,0,0,0,0},
                            {1,3,1,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0}
                        };
                        byte val = foodMap[py, px];
                        if (val == 1) { r = 40; g = 25; b = 15; a = 255; }
                        else if (val == 2) { r = 160; g = 90; b = 40; a = 255; }
                        else if (val == 3) { r = 240; g = 230; b = 220; a = 255; }
                        else { a = 0; }
                    } else if (tile == THeartEmpty) {
                        byte[,] heartMap = new byte[16, 16] {
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,1,1,1,0,0,0,0,0,1,1,1,0,0,0},
                            {0,1,2,2,2,1,0,0,0,1,2,2,2,1,0,0},
                            {0,1,2,2,2,2,1,0,1,2,2,2,2,1,0,0},
                            {0,1,2,2,2,2,2,1,2,2,2,2,2,1,0,0},
                            {0,0,1,2,2,2,2,2,2,2,2,2,1,0,0,0},
                            {0,0,0,1,2,2,2,2,2,2,2,1,0,0,0,0},
                            {0,0,0,0,1,2,2,2,2,2,1,0,0,0,0,0},
                            {0,0,0,0,0,1,2,2,2,1,0,0,0,0,0,0},
                            {0,0,0,0,0,0,1,2,1,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,1,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0}
                        };
                        byte val = heartMap[py, px];
                        if (val == 1) { r = 30; g = 30; b = 30; a = 255; }
                        else if (val == 2) { r = 60; g = 60; b = 60; a = 180; }
                        else { a = 0; }
                    } else if (tile == TFoodEmpty) {
                        byte[,] foodMap = new byte[16, 16] {
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,1,1,1,1,0,0,0,0},
                            {0,0,0,0,0,0,0,1,2,2,2,2,1,0,0,0},
                            {0,0,0,0,0,0,1,2,2,2,2,2,2,1,0,0},
                            {0,0,0,0,0,1,2,2,2,2,2,2,2,1,0,0},
                            {0,0,0,0,0,1,2,2,2,2,2,2,1,0,0,0},
                            {0,0,0,0,1,2,2,2,2,2,1,1,0,0,0,0},
                            {0,0,0,1,2,2,2,2,1,1,0,0,0,0,0,0},
                            {0,0,0,1,2,2,1,1,0,0,0,0,0,0,0,0},
                            {0,0,1,1,1,1,0,0,0,0,0,0,0,0,0,0},
                            {0,1,3,3,1,0,0,0,0,0,0,0,0,0,0,0},
                            {1,3,3,1,0,0,0,0,0,0,0,0,0,0,0,0},
                            {1,3,1,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0}
                        };
                        byte val = foodMap[py, px];
                        if (val == 1) { r = 30; g = 30; b = 30; a = 255; }
                        else if (val == 2) { r = 60; g = 60; b = 60; a = 180; }
                        else if (val == 3) { r = 80; g = 80; b = 80; a = 180; }
                        else { a = 0; }
                    } else if (tile == TIronOre) {
                        // Каменный фон с ржавыми пятнами железа
                        int d = rng.Next(-18, 19);
                        r = Math.Clamp(128 + d, 0, 255);
                        g = Math.Clamp(118 + d, 0, 255);
                        b = Math.Clamp(108 + d, 0, 255);
                        if ((px * 5 + py * 7) % 7 == 0 || (px * 3 + py * 11) % 9 == 1) {
                            r = Math.Clamp(r + 65, 0, 255);
                            g = Math.Clamp(g - 15, 0, 255);
                            b = Math.Clamp(b - 25, 0, 255);
                        }
                    } else if (tile == TWorkbench) {
                        // Топ верстака: Четкая резная 3x3 сетка крафта на дубовых досках
                        int d = rng.Next(-8, 9);
                        r = Math.Clamp(150 + d, 0, 255);
                        g = Math.Clamp(115 + d, 0, 255);
                        b = Math.Clamp(68 + d, 0, 255);
                        // Внешняя рамка верстака
                        bool isBorder = px == 0 || px == 15 || py == 0 || py == 15;
                        // Сетка 3x3: деления на координатах 5 и 10
                        bool isGridLine = px == 5 || px == 10 || py == 5 || py == 10;
                        if (isBorder || isGridLine) {
                            r = Math.Clamp(r - 55, 0, 255);
                            g = Math.Clamp(g - 45, 0, 255);
                            b = Math.Clamp(b - 30, 0, 255);
                        } else {
                            // Внутренние ячейки сетки крафта: небольшое затенение сверху/слева ячеек для 3D рельефа
                            bool isInnerSlotBorder = (px % 5 == 1) || (py % 5 == 1);
                            if (isInnerSlotBorder) {
                                r = Math.Clamp(r - 25, 0, 255);
                                g = Math.Clamp(g - 20, 0, 255);
                                b = Math.Clamp(b - 12, 0, 255);
                            }
                        }
                    } else if (tile == TBoneMeal) {
                        // Костная мука: белая пыль / мешочек
                        bool inBag = (px >= 4 && px <= 11 && py >= 5 && py <= 13) || (px >= 6 && px <= 9 && py >= 3 && py <= 4);
                        if (inBag) {
                            int d = rng.Next(-15, 16);
                            r = Math.Clamp(235 + d, 0, 255);
                            g = Math.Clamp(235 + d, 0, 255);
                            b = Math.Clamp(225 + d, 0, 255);
                            if (py == 5 || py == 13 || px == 4 || px == 11) {
                                r = 180; g = 180; b = 170;
                            }
                            a = 255;
                        } else { a = 0; }
                    } else if (tile == TSawdust) {
                        // Опилки: кучка деревянных стружек
                        bool inPile = (py >= 8 && py <= 13 && px >= 3 && px <= 12 && (13 - py) * 2 >= Math.Abs(px - 7.5f));
                        if (inPile) {
                            int d = rng.Next(-20, 21);
                            r = Math.Clamp(190 + d, 0, 255);
                            g = Math.Clamp(145 + d, 0, 255);
                            b = Math.Clamp(75 + d, 0, 255);
                            a = 255;
                        } else { a = 0; }
                    } else if (tile == TSawdustPorridge) {
                        // Каша из опилок: деревянная миска с золотистой сытной кашей и ложкой
                        bool inBowl = (py >= 9 && py <= 14 && px >= 2 && px <= 13 && py - 9 >= Math.Abs(px - 7.5f) * 0.7f);
                        bool inFood = (py >= 6 && py <= 9 && px >= 3 && px <= 12);
                        bool inSpoon = (px == 11 && py >= 3 && py <= 8) || (px == 10 && py == 7);
                        if (inSpoon) {
                            r = 160; g = 110; b = 60; a = 255;
                        } else if (inFood) {
                            int d = rng.Next(-15, 16);
                            r = Math.Clamp(210 + d, 0, 255);
                            g = Math.Clamp(170 + d, 0, 255);
                            b = Math.Clamp(95 + d, 0, 255);
                            a = 255;
                        } else if (inBowl) {
                            r = 120; g = 80; b = 40; a = 255;
                        } else { a = 0; }
                    } else if (tile == TTotem) {
                        // Тотем бессмертия: золотой человечек с зелеными глазами
                        bool isHead = px >= 5 && px <= 10 && py >= 2 && py <= 6;
                        bool isWings = (px >= 2 && px <= 13 && py >= 6 && py <= 9);
                        bool isBody = px >= 5 && px <= 10 && py >= 7 && py <= 14;
                        bool isEyes = (px == 6 || px == 9) && py == 4;
                        if (isEyes) {
                            r = 40; g = 230; b = 80; a = 255;
                        } else if (isHead || isWings || isBody) {
                            int d = rng.Next(-15, 16);
                            r = Math.Clamp(240 + d, 0, 255);
                            g = Math.Clamp(195 + d, 0, 255);
                            b = Math.Clamp(40 + d, 0, 255);
                            if (px == 5 || px == 10 || py == 2 || py == 14) {
                                r = 180; g = 140; b = 25;
                            }
                            a = 255;
                        } else { a = 0; }
                    } else if (tile == TFurnace) {
                        // Камень с тёмным жерлом
                        int d = rng.Next(-16, 17);
                        r = Math.Clamp(100 + d, 0, 255);
                        g = Math.Clamp(96 + d, 0, 255);
                        b = Math.Clamp(92 + d, 0, 255);
                        bool opening = px >= 4 && px <= 11 && py >= 7 && py <= 13;
                        bool glow = opening && (px + py) % 3 == 0;
                        if (glow) { r = 200; g = 100; b = 20; }
                        else if (opening) { r = 22; g = 18; b = 14; }
                    } else if (tile == TIronIngot) {
                        // Серебристый слиток
                        bool ingot = py >= 4 && py <= 11 && px >= 3 && px <= 12;
                        bool shine = ingot && px >= 4 && px <= 6 && py >= 5 && py <= 7;
                        if (shine) { r = 240; g = 240; b = 245; a = 255; }
                        else if (ingot) { r = 185; g = 185; b = 192; a = 255; }
                        else { a = 0; }
                    } else if (tile == TGoldIngot) {
                        // Золотой слиток
                        bool ingot = py >= 4 && py <= 11 && px >= 3 && px <= 12;
                        bool shine = ingot && px >= 4 && px <= 6 && py >= 5 && py <= 7;
                        if (shine) { r = 255; g = 245; b = 130; a = 255; }
                        else if (ingot) { r = 245; g = 195; b = 35; a = 255; }
                        else { a = 0; }
                    } else if (tile == TDiamond) {
                        // Драгоценный алмаз
                        bool inDiamond = Math.Abs(px - 8) + Math.Abs(py - 8) <= 5 && py >= 3 && py <= 12;
                        bool shine = inDiamond && px >= 6 && px <= 7 && py >= 5 && py <= 7;
                        if (shine) { r = 220; g = 255; b = 255; a = 255; }
                        else if (inDiamond) { r = 75; g = 225; b = 235; a = 255; }
                        else { a = 0; }
                    } else if (tile == TRedstoneDust) {
                        // Кучка редстоун пыли
                        bool inDust = (px - 8) * (px - 8) + (py - 10) * (py - 10) <= 14 && py >= 6;
                        bool sparkle = (px == 6 && py == 8) || (px == 10 && py == 9);
                        if (sparkle) { r = 255; g = 120; b = 100; a = 255; }
                        else if (inDust) { r = 205 + (px * 3 + py * 7) % 35; g = 25; b = 15; a = 255; }
                        else { a = 0; }
                    } else if (tile == TFlint) {
                        // Кремень
                        bool inFlint = px >= 4 && px <= 12 && py >= 3 && py <= 12 && (px + py >= 9) && (px - py <= 5);
                        bool edge = inFlint && (px == 4 || py == 3 || px + py == 9);
                        if (edge) { r = 90; g = 90; b = 98; a = 255; }
                        else if (inFlint) { r = 50 + (px * 5 + py * 3) % 15; g = 50 + (px * 5 + py * 3) % 15; b = 56; a = 255; }
                        else { a = 0; }
                    } else if (tile == TClay) {
                        // Глина
                        bool inClay = (px - 8) * (px - 8) + (py - 8) * (py - 8) <= 18 + (px * 5 + py * 3) % 4;
                        if (inClay) { r = 160 + (px * 7 + py * 5) % 20; g = 165 + (px * 7 + py * 5) % 20; b = 175; a = 255; }
                        else { a = 0; }
                    } else if (tile >= TPickaxeWood && tile <= TShovelDiamond) {
                        // 16 уникальных тайлов для ВСЕХ инструментов!
                        int toolType = (tile - TPickaxeWood) / 4; // 0 = Pickaxe, 1 = Sword, 2 = Axe, 3 = Shovel
                        int material = (tile - TPickaxeWood) % 4; // 0 = Wood, 1 = Stone, 2 = Iron, 3 = Diamond

                        (int mr, int mg, int mb) = material switch {
                            0 => (140, 104, 60),  // Дерево
                            1 => (148, 148, 152), // Камень
                            2 => (220, 220, 226), // Железо
                            _ => (70, 220, 230)   // Алмаз
                        };

                        bool isStick = (px == 15 - py) && (py >= 6);
                        bool isHead = false;

                        if (toolType == 0) { // Кирка (дуговая головка)
                            isHead = py <= 6 && px <= 11 && Math.Abs(px + py - 8) <= 3;
                        } else if (toolType == 1) { // Меч (лезвие + гарда + рукоять)
                            bool blade = (px == py || px == py + 1) && py >= 3 && py <= 12;
                            bool guard = (px + py == 20) && Math.Abs(px - py) <= 3;
                            isStick = (px == py) && py >= 12;
                            isHead = blade || guard;
                        } else if (toolType == 2) { // Топор (Г-образное лезвие)
                            isHead = px >= 3 && px <= 9 && py >= 2 && py <= 7 && (px <= 6 || py <= 5);
                        } else if (toolType == 3) { // Лопата (прямоугольное штыковое лезвие)
                            isHead = px >= 2 && px <= 7 && py >= 2 && py <= 7;
                        }

                        if (isHead) { r = mr; g = mg; b = mb; a = 255; }
                        else if (isStick) { r = 130; g = 96; b = 52; a = 255; }
                        else { a = 0; }
                    } else if (tile == TBread) {
                        // Хлеб: овальная форма, корочка сверху
                        float cx = 8f, cy = 9f;
                        float dx = (px - cx) / 7f, dy = (py - cy) / 5f;
                        bool inBread = dx * dx + dy * dy <= 1f;
                        bool crust = py <= 5 && dx * dx + dy * dy <= 1.1f;
                        if (inBread || crust) {
                            r = crust ? 170 : 196;
                            g = crust ? 120 : 150;
                            b = crust ? 50 : 70;
                            a = 255;
                        } else { a = 0; }
                    } else if (tile == TCharcoal) {
                        bool insideCoal = (px - 8) * (px - 8) + (py - 8) * (py - 8) <= 17 + (px * 3 + py * 7) % 5;
                        bool grainLine = (px + py) % 4 == 0;
                        if (insideCoal) {
                            r = grainLine ? 30 : 42;
                            g = grainLine ? 26 : 38;
                            b = grainLine ? 24 : 36;
                            a = 255;
                        } else { a = 0; }
                    } else if (tile == TRawBeef) {
                        bool insideSteak = (px - 8) * (px - 8) * 0.9f + (py - 8) * (py - 8) * 1.1f <= 20;
                        bool isFatEdge = insideSteak && (px <= 4 || (px >= 11 && py <= 6));
                        if (isFatEdge) {
                            r = 245; g = 240; b = 230; a = 255;
                        } else if (insideSteak) {
                            r = 185 + (px * 7 + py * 3) % 25; g = 35; b = 35; a = 255;
                        } else { a = 0; }
                    } else if (tile == TCookedBeef) {
                        bool insideSteak = (px - 8) * (px - 8) * 0.9f + (py - 8) * (py - 8) * 1.1f <= 20;
                        bool isGrillMark = insideSteak && ((px + py) % 4 == 0);
                        if (isGrillMark) {
                            r = 65; g = 30; b = 15; a = 255;
                        } else if (insideSteak) {
                            r = 120 + (px * 3 + py * 5) % 25; g = 60; b = 32; a = 255;
                        } else { a = 0; }
                    } else if (tile == TLeather) {
                        bool inHide = Math.Abs(px - 8) + Math.Abs(py - 8) <= 6 || (px >= 4 && px <= 12 && py >= 4 && py <= 12);
                        if (inHide) {
                            r = 175; g = 110; b = 60; a = 255;
                        } else { a = 0; }
                    } else if (tile == TWool) {
                        bool inWool = (px - 8) * (px - 8) + (py - 8) * (py - 8) <= 22;
                        if (inWool) {
                            int shade = 230 + (px * 5 + py * 7) % 25;
                            r = shade; g = shade; b = shade; a = 255;
                        } else { a = 0; }
                    } else if (tile == TFeather) {
                        bool inFeather = Math.Abs(px - py) <= 2 && px >= 2 && px <= 13 && py >= 2 && py <= 13;
                        bool shaft = px == py;
                        if (shaft) { r = 240; g = 240; b = 245; a = 255; }
                        else if (inFeather) { r = 220; g = 220; b = 230; a = 255; }
                        else { a = 0; }
                    } else if (tile == TGunpowder) {
                        bool inPouch = (px - 8) * (px - 8) + (py - 9) * (py - 9) <= 16;
                        if (inPouch) { int shade = 55 + (px * 3 + py * 7) % 20; r = shade; g = shade; b = shade + 4; a = 255; }
                        else { a = 0; }
                    } else if (tile == TString) {
                        bool inString = (px >= 4 && px <= 12 && py >= 4 && py <= 12) && (px == 4 || px == 12 || py == 4 || py == 12 || px == py);
                        if (inString) { r = 220; g = 220; b = 220; a = 255; }
                        else { a = 0; }
                    } else if (tile == TArrow) {
                        bool inArrow = px == 15 - py && px >= 2 && px <= 13;
                        bool tip = px <= 4 && py >= 11;
                        bool feather = px >= 11 && py <= 4;
                        if (tip) { r = 160; g = 160; b = 165; a = 255; }
                        else if (feather) { r = 230; g = 230; b = 240; a = 255; }
                        else if (inArrow) { r = 130; g = 96; b = 52; a = 255; }
                        else { a = 0; }
                    } else if (tile == TBone) {
                        bool inBone = (Math.Abs(px + py - 15) <= 1 && px >= 3 && px <= 12) ||
                                      ((px == 3 || px == 4) && (py == 11 || py == 12)) ||
                                      ((px == 11 || px == 12) && (py == 3 || py == 4));
                        if (inBone) { r = 230; g = 230; b = 220; a = 255; }
                        else { a = 0; }
                    } else if (tile == TRottenFlesh) {
                        bool inMeat = px >= 3 && px <= 12 && py >= 4 && py <= 12;
                        bool rottenSpot = (px == 5 && py == 6) || (px == 9 && py == 8) || (px == 7 && py == 10) || (px == 10 && py == 5);
                        bool bonePart = px <= 4 && py >= 11;
                        if (bonePart) { r = 220; g = 220; b = 210; a = 255; }
                        else if (rottenSpot) { r = 70; g = 100; b = 40; a = 255; }
                        else if (inMeat) { r = 140 + (px % 3) * 8; g = 60 + (py % 3) * 6; b = 40; a = 255; }
                        else { a = 0; }
                    } else if (tile == TWheat) {
                        // Сноп пшеницы
                        bool inSheaf = (px >= 4 && px <= 11 && py >= 3 && py <= 13) &&
                                       !(py < 6 && (px == 4 || px == 11));
                        bool isTie = inSheaf && (py == 8 || py == 9);
                        if (isTie) {
                            r = 160; g = 50; b = 30; a = 255; // Красная лента
                        } else if (inSheaf) {
                            int shade = (px * 3 + py * 7) % 25;
                            r = 220 + shade; g = 185 + shade; b = 55; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TWheatSeeds) {
                        // Зерна/семена пшеницы
                        bool inSeed1 = (px >= 4 && px <= 6 && py >= 9 && py <= 12);
                        bool inSeed2 = (px >= 8 && px <= 10 && py >= 6 && py <= 10);
                        bool inSeed3 = (px >= 6 && px <= 8 && py >= 11 && py <= 13);
                        if (inSeed1 || inSeed2 || inSeed3) {
                            r = 150 + (px * 7) % 30; g = 175 + (py * 5) % 30; b = 75; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TFarmland) {
                        // Грядка: бороздки влажной вспаханной земли
                        bool isGroove = py % 4 == 0;
                        if (isGroove) {
                            r = 60; g = 40; b = 20; a = 255;
                        } else {
                            int d = (px * 3 + py * 7) % 20;
                            r = 95 + d; g = 65 + d; b = 35; a = 255;
                        }
                    } else if (tile >= TWheatCrop0 && tile <= TWheatCrop3) {
                        // Посевы пшеницы (4 стадии роста: 0..3)
                        int stage = tile - TWheatCrop0;
                        int minH = 14 - (stage * 3 + 2); // 0: 12..14, 1: 9..14, 2: 6..14, 3: 3..14
                        bool inStem = (px == 4 || px == 7 || px == 11) && py >= minH && py <= 14;
                        bool inLeaf = stage >= 1 && ((px == 3 || px == 5 || px == 8 || px == 10 || px == 12) && py >= minH + 1 && py <= 14);
                        bool inWheatHead = stage >= 3 && py >= 2 && py <= 6 && (px >= 3 && px <= 12 && (px % 3 != 0));
                        if (inWheatHead) {
                            r = 180 + (px + py) % 15; g = 190 + (px * 3) % 20; b = 50; a = 255;
                        } else if (inStem || inLeaf) {
                            r = 85 + (stage * 30); g = 180 + (stage * 10); b = 45; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile >= THoeWood && tile <= THoeDiamond) {
                        // Мотыга
                        int mat = tile - THoeWood;
                        Color matCol = mat == 0 ? new Color(140, 95, 55, 255)
                                     : mat == 1 ? new Color(150, 150, 150, 255)
                                     : mat == 2 ? new Color(220, 220, 220, 255)
                                     : new Color(90, 230, 240, 255);
                        bool isHandle = (px + py >= 14 && px + py <= 16 && px >= 3 && py >= 3 && px <= 13 && py <= 13);
                        bool isBladeTop = (py >= 2 && py <= 4 && px >= 4 && px <= 12);
                        bool isBladeHook = (px >= 4 && px <= 6 && py >= 4 && py <= 7);
                        if (isBladeTop || isBladeHook) {
                            r = matCol.R; g = matCol.G; b = matCol.B; a = 255;
                        } else if (isHandle) {
                            r = 130; g = 90; b = 45; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TTallGrass) {
                        // Трава: реалистичные лепестки и стебельки травы (Cross-billboard)
                        bool isGrassBlade = false;
                        if (py >= 4 && (px == 3 || px == 4) && py >= 6) isGrassBlade = true;
                        if (py >= 2 && (px == 6 || px == 7 || px == 8)) isGrassBlade = true;
                        if (py >= 5 && (px == 10 || px == 11)) isGrassBlade = true;
                        if (py >= 3 && px == 13 && py >= 7) isGrassBlade = true;
                        if (py >= 8 && (px == 2 || px == 5 || px == 9 || px == 12 || px == 14)) isGrassBlade = true;
                        if (py >= 13 && px >= 2 && px <= 14) isGrassBlade = true;

                        if (isGrassBlade) {
                            int shade = (px * 7 + py * 11) % 40;
                            r = 70 + shade;
                            g = 150 + shade;
                            b = 35 + shade / 2;
                            a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TRawMutton) {
                        bool insideChop = (px - 7) * (px - 7) * 1.0f + (py - 7) * (py - 7) * 1.2f <= 18;
                        bool boneStick = (px >= 10 && px <= 13 && py >= 10 && py <= 13 && Math.Abs(px - py) <= 1);
                        if (boneStick) {
                            r = 240; g = 238; b = 225; a = 255;
                        } else if (insideChop) {
                            bool fatLine = (px <= 4 || (py <= 4 && px <= 8));
                            if (fatLine) { r = 245; g = 235; b = 230; a = 255; }
                            else { r = 205 + (px * 5 + py * 7) % 25; g = 65; b = 75; a = 255; }
                        } else { a = 0; }
                    } else if (tile == TCookedMutton) {
                        bool insideChop = (px - 7) * (px - 7) * 1.0f + (py - 7) * (py - 7) * 1.2f <= 18;
                        bool boneStick = (px >= 10 && px <= 13 && py >= 10 && py <= 13 && Math.Abs(px - py) <= 1);
                        if (boneStick) {
                            r = 235; g = 230; b = 215; a = 255;
                        } else if (insideChop) {
                            bool crust = (px <= 4 || py <= 4 || (px + py) % 4 == 0);
                            if (crust) { r = 90; g = 45; b = 20; a = 255; }
                            else { r = 145 + (px * 3 + py * 7) % 25; g = 75; b = 38; a = 255; }
                        } else { a = 0; }
                    } else if (tile == TBow) {
                        bool woodBow = (px == 3 && py >= 3 && py <= 12) || (py == 2 && px >= 4 && px <= 9) || (py == 13 && px >= 4 && px <= 9) || (px == 10 && (py == 3 || py == 12));
                        bool bowString = (px == 11 && py >= 3 && py <= 12);
                        if (woodBow) { r = 120 + (px * 4) % 20; g = 80; b = 40; a = 255; }
                        else if (bowString) { r = 240; g = 240; b = 245; a = 255; }
                        else { a = 0; }
                    } else if (tile == TShield) {
                        bool border = px >= 3 && px <= 12 && py >= 2 && py <= 14 && (py <= 9 || Math.Abs(px - 7.5f) * 1.8f <= (14 - py) + 2);
                        bool metalRim = border && (px == 3 || px == 12 || py == 2 || py == 14 || (py >= 10 && (px <= 4 || px >= 11)));
                        bool metalBoss = (px >= 7 && px <= 8 && py >= 7 && py <= 8);
                        if (metalRim || metalBoss) { r = 210; g = 215; b = 225; a = 255; }
                        else if (border) { r = 150 + (py % 3) * 15; g = 110; b = 65; a = 255; }
                        else { a = 0; }
                    } else if (tile == TGoldenApple) {
                        bool insideApple = (px - 7.5f) * (px - 7.5f) + (py - 8.5f) * (py - 8.5f) <= 24 && py >= 4 && py <= 13;
                        bool stem = (px == 7 || px == 8) && (py >= 2 && py <= 4);
                        if (stem) { r = 100; g = 70; b = 30; a = 255; }
                        else if (insideApple) {
                            bool highlight = (px >= 5 && px <= 7 && py >= 5 && py <= 7);
                            r = highlight ? 255 : 235; g = highlight ? 245 : 195; b = highlight ? 120 : 25; a = 255;
                        } else { a = 0; }
                    } else if (tile == TTNT || tile == TTNTSide) {
                        bool whiteBand = py >= 6 && py <= 9;
                        if (whiteBand) { r = 240; g = 240; b = 240; a = 255; }
                        else {
                            bool redGap = (px % 4 == 0);
                            r = redGap ? 160 : 220; g = 30; b = 25; a = 255;
                        }
                    } else if (tile == TMossyCobble) {
                        bool isMoss = ((px * 13 + py * 7) % 7 < 3) || ((px + py) % 5 == 0 && py > 6);
                        if (isMoss) { r = 60 + (px * 3) % 25; g = 135 + (py * 5) % 30; b = 45; a = 255; }
                        else { r = 110 + (px * 4 + py * 7) % 20; g = r; b = r + 5; a = 255; }
                    } else if (tile == TMobSpawner) {
                        bool bar = (px <= 1 || px >= 14 || py <= 1 || py >= 14 || px == 5 || px == 10 || py == 5 || py == 10);
                        if (bar) { r = 40; g = 45; b = 55; a = 255; }
                        else {
                            bool flame = (px >= 6 && px <= 9 && py >= 6 && py <= 9);
                            if (flame) { r = 240; g = 140; b = 30; a = 255; }
                            else { a = 0; }
                        }
                    } else if (tile == TWeb) {
                        bool strand = (px == py || px + py == 15 || px == 7 || py == 7 || (px % 4 == 0 && py % 4 == 0));
                        if (strand) { r = 245; g = 245; b = 250; a = 220; }
                        else { a = 0; }
                    } else if (tile == TRail) {
                        bool tie = (py % 4 == 0);
                        bool ironRail = (px == 3 || px == 12);
                        if (ironRail) { r = 200; g = 200; b = 210; a = 255; }
                        else if (tie) { r = 130; g = 95; b = 55; a = 255; }
                        else { a = 0; }
                    } else if (tile == TNetherPortal) {
                        int wave = (int)(MathF.Sin(px * 0.8f + py * 0.6f) * 40f);
                        r = Math.Clamp(130 + wave, 60, 220);
                        g = Math.Clamp(30 + wave / 2, 10, 90);
                        b = Math.Clamp(230 + wave, 140, 255);
                        a = 210;
                    } else {
                        if (tile == TPlanks && (py == 4 || py == 11)) { r -= 40; g -= 30; b -= 20; }
                        if (tile == TLogSide && py % 5 == 4) { r -= 30; g -= 24; b -= 14; }
                        if (tile == TLeaves && (px * 3 + py * 7) % 5 == 0) {
                            a = 0;
                        }
                        if (px == 0 || py == 0) { r = r * 4 / 5; g = g * 4 / 5; b = b * 4 / 5; }
                    }

                    unsafe {
                        Raylib.ImageDrawPixel(ref image, tile % Cols * TilePx + px, tile / Cols * TilePx + py,
                            new Color((byte)r, (byte)g, (byte)b, a));
                    }
                }
            }
        }

        return image;
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

    /// <summary>UV-прямоугольник тайла в атласе [0..1].</summary>
    public static Rectangle TileUv(byte tile) => new(
        tile % Cols * TilePx / (float)AtlasW,
        tile / Cols * TilePx / (float)AtlasH,
        TilePx / (float)AtlasW,
        TilePx / (float)AtlasH);
}
