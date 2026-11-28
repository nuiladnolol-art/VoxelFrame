using Raylib_cs;

namespace Voxalith.Game;

public static partial class TextureAtlas {
    public static Image GenerateProceduralImage() {
        var image = Raylib.GenImageColor(AtlasW, AtlasH, new Color(0, 0, 0, 0));

        var palette = new (Color Base, bool Grain)[Cols * Rows];
        palette[TGrassTop] = (new Color(78, 155, 54, 255), true);
        palette[TGrassSide] = (new Color(120, 79, 49, 255), true);
        palette[TDirt] = (new Color(120, 79, 49, 255), true);
        palette[TStone] = (new Color(108, 114, 125, 255), true);
        palette[TLogSide] = (new Color(105, 75, 45, 255), true);
        palette[TLogTop] = (new Color(170, 128, 80, 255), false);
        palette[TLeaves] = (new Color(56, 126, 44, 255), true);
        palette[TPlanks] = (new Color(175, 132, 80, 255), false);
        palette[TCoalOre] = (new Color(108, 114, 125, 255), true);
        palette[TTorch] = (new Color(176, 138, 74, 255), true);
        palette[TBedrock] = (new Color(28, 30, 36, 255), false);
        palette[TApple] = (new Color(215, 38, 38, 255), false);
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
        palette[TIronOre] = (new Color(108, 114, 125, 255), true);
        palette[TWorkbench] = (new Color(142, 108, 62, 255), false);
        palette[TFurnace] = (new Color(100, 96, 92, 255), true);
        palette[TIronIngot] = (new Color(210, 210, 215, 255), false);
        palette[TBread] = (new Color(196, 150, 70, 255), false);

        palette[TCobblestone] = (new Color(105, 110, 118, 255), false);
        palette[TSand] = (new Color(218, 198, 134, 255), true);
        palette[TGravel] = (new Color(125, 128, 135, 255), true);
        palette[TGlass] = (new Color(180, 235, 255, 35), false);
        palette[TWater] = (new Color(40, 95, 230, 180), true);
        palette[TLava] = (new Color(240, 90, 15, 255), true);
        palette[TGoldOre] = (new Color(108, 114, 125, 255), true);
        palette[TDiamondOre] = (new Color(108, 114, 125, 255), true);
        palette[TRedstoneOre] = (new Color(108, 114, 125, 255), true);
        palette[TObsidian] = (new Color(24, 18, 36, 255), false);
        palette[TGoldIngot] = (new Color(245, 215, 50, 255), false);
        palette[TDiamond] = (new Color(90, 230, 240, 255), false);
        palette[TRedstoneDust] = (new Color(220, 30, 20, 255), false);
        palette[TFlint] = (new Color(50, 50, 55, 255), false);
        palette[TClay] = (new Color(160, 165, 175, 255), true);
        palette[TChestTop] = (new Color(150, 105, 55, 255), true);
        palette[TChestSide] = (new Color(150, 105, 55, 255), true);
        palette[TChestFront] = (new Color(150, 105, 55, 255), true);
        palette[TBedHeadTop] = (new Color(210, 40, 40, 255), true);
        palette[TBedFootTop] = (new Color(210, 40, 40, 255), true);
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
        palette[TNetherQuartzOre] = (new Color(95, 26, 32, 255), true);
        palette[TNetherBrick] = (new Color(55, 25, 30, 255), true);
        palette[TNetherPortal] = (new Color(120, 30, 210, 200), true);
        palette[TDoorLower] = (new Color(145, 105, 60, 255), false);
        palette[TDoorUpper] = (new Color(145, 105, 60, 255), false);
        palette[TDoorItem] = (new Color(145, 105, 60, 255), false);
        // Энд: блоки
        palette[TEndStone] = (new Color(224, 224, 200, 255), true);
        palette[TEndPortalFrame] = (new Color(72, 92, 62, 255), true);
        palette[TChorusPlant] = (new Color(126, 68, 140, 255), true);
        palette[TVoidGate] = (new Color(52, 30, 80, 255), true);
        palette[TChorusFlower] = (new Color(176, 112, 188, 255), true);
        // Биомные цвета листвы и болотной воды
        palette[TLeavesPlains] = (new Color(72, 152, 45, 255), true);
        palette[TLeavesSavanna] = (new Color(152, 148, 50, 255), true);
        palette[TLeavesSwamp] = (new Color(58, 86, 42, 255), true);
        palette[TWaterSwamp] = (new Color(36, 62, 46, 255), true);
        // Биомные цвета травы (блоки и растительность)
        palette[TGrassTopPlains] = (new Color(96, 178, 56, 255), true);
        palette[TGrassSidePlains] = (new Color(122, 92, 58, 255), true);
        palette[TTallGrassPlains] = (new Color(95, 185, 52, 255), false);
        palette[TGrassTopSavanna] = (new Color(162, 154, 52, 255), true);
        palette[TGrassSideSavanna] = (new Color(122, 92, 58, 255), true);
        palette[TTallGrassSavanna] = (new Color(165, 158, 55, 255), false);
        palette[TGrassTopSwamp] = (new Color(64, 88, 44, 255), true);
        palette[TGrassSideSwamp] = (new Color(122, 92, 58, 255), true);
        palette[TTallGrassSwamp] = (new Color(62, 86, 42, 255), false);
        // Саженцы, Цветы, Культуры и Частицы
        palette[TSapling] = (new Color(60, 140, 40, 255), false);
        palette[TRedFlower] = (new Color(220, 30, 35, 255), false);
        palette[TYellowFlower] = (new Color(245, 215, 30, 255), false);
        palette[TCarrot] = (new Color(245, 120, 20, 255), false);
        palette[TPotato] = (new Color(180, 140, 75, 255), false);
        palette[TBakedPotato] = (new Color(150, 95, 45, 255), false);
        palette[TCarrotCrop0] = (new Color(70, 160, 40, 255), false);
        palette[TCarrotCrop1] = (new Color(80, 180, 40, 255), false);
        palette[TCarrotCrop2] = (new Color(90, 200, 40, 255), false);
        palette[TCarrotCrop3] = (new Color(100, 210, 40, 255), false);
        palette[TPotatoCrop0] = (new Color(60, 150, 40, 255), false);
        palette[TPotatoCrop1] = (new Color(70, 170, 40, 255), false);
        palette[TPotatoCrop2] = (new Color(80, 190, 45, 255), false);
        palette[TPotatoCrop3] = (new Color(90, 200, 50, 255), false);
        palette[THeartParticle] = (new Color(235, 30, 60, 255), false);
        palette[TFire] = (new Color(255, 120, 0, 255), false);
        palette[TJukeboxTop] = (new Color(110, 75, 45, 255), false);
        palette[TJukeboxSide] = (new Color(115, 78, 48, 255), false);

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
                        // Smooth Voxel 3D Bevel lighting (Teardown / Unturned aesthetic)
                        int bevel = 0;
                        if (py == 0) bevel += 14;
                        if (px == 0) bevel += 10;
                        if (py == 15) bevel -= 16;
                        if (px == 15) bevel -= 12;
                        if (py == 1) bevel += 4;
                        if (px == 1) bevel += 3;
                        if (py == 14) bevel -= 5;
                        if (px == 14) bevel -= 4;
                        int subtle = ((px * 3 + py * 7 + tile * 11) % 5) - 2;
                        r = Math.Clamp(r + bevel + subtle, 0, 255);
                        g = Math.Clamp(g + bevel + subtle, 0, 255);
                        b = Math.Clamp(b + bevel + subtle, 0, 255);
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
                        float wave = MathF.Sin(px * 0.45f) * MathF.Cos(py * 0.45f) + MathF.Sin((px + py) * 0.35f) * 0.5f;
                        int dw = (int)(wave * 8f);
                        r = Math.Clamp(36 + dw, 25, 60);
                        g = Math.Clamp(92 + dw * 2, 70, 130);
                        b = Math.Clamp(224 + dw, 195, 255);
                        a = 180; // Чистая полупрозрачная вода
                    } else if (tile == TLava) {
                        float wave = MathF.Sin(px * 0.5f) * MathF.Cos(py * 0.5f);
                        int dw = (int)(wave * 12f);
                        r = Math.Clamp(235 + dw, 200, 255);
                        g = Math.Clamp(85 + dw * 3, 50, 140);
                        b = 15;
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
                    } else if (tile == TBedHeadTop) {
                        bool isPillow = py >= 1 && py <= 5 && px >= 2 && px <= 13;
                        bool isPillowEdge = isPillow && (py == 1 || py == 5 || px == 2 || px == 13);
                        if (isPillowEdge) { r = 215; g = 215; b = 222; a = 255; }
                        else if (isPillow) { r = 245; g = 245; b = 250; a = 255; }
                        else if (py == 6 || px == 0 || px == 15) { r = 150; g = 20; b = 20; a = 255; }
                        else {
                            bool quiltPattern = ((px + py) % 4 == 0);
                            r = (byte)(quiltPattern ? 205 : 180);
                            g = (byte)(quiltPattern ? 40 : 25);
                            b = (byte)(quiltPattern ? 40 : 25);
                            a = 255;
                        }
                    } else if (tile == TBedFootTop) {
                        if (py == 0 || py == 15 || px == 0 || px == 15) { r = 150; g = 20; b = 20; a = 255; }
                        else {
                            bool quiltPattern = ((px + py) % 4 == 0);
                            r = (byte)(quiltPattern ? 205 : 180);
                            g = (byte)(quiltPattern ? 40 : 25);
                            b = (byte)(quiltPattern ? 40 : 25);
                            a = 255;
                        }
                    } else if (tile == TBedSide) {
                        bool isLeg = (px <= 2 || px >= 13) && py >= 10;
                        bool isFrame = py >= 10;
                        if (isLeg) { r = 95; g = 60; b = 30; a = 255; } // ножки кровати
                        else if (isFrame) { r = 135; g = 92; b = 48; a = 255; } // дубовый каркас
                        else if (py == 8 || py == 9) { r = 240; g = 240; b = 245; a = 255; } // простыня
                        else { r = (byte)(185 + (px % 2) * 15); g = 30; b = 30; a = 255; } // одеяло
                    } else if (tile == TBedEnd) {
                        bool isLeg = (px <= 2 || px >= 13) && py >= 10;
                        bool isFrame = py >= 10;
                        if (isLeg) { r = 95; g = 60; b = 30; a = 255; }
                        else if (isFrame) { r = 135; g = 92; b = 48; a = 255; }
                        else if (py >= 7 && py <= 9) { r = 240; g = 240; b = 245; a = 255; }
                        else { r = 185; g = 30; b = 30; a = 255; }
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
                            {0,0,0,0,0,0,0,1,4,4,2,2,1,0,0,0},
                            {0,0,0,0,0,0,1,4,4,4,2,2,2,1,0,0},
                            {0,0,0,0,0,1,4,4,2,2,2,2,2,1,0,0},
                            {0,0,0,0,0,1,2,2,2,2,2,2,1,0,0,0},
                            {0,0,0,0,1,2,2,2,2,2,2,1,0,0,0,0},
                            {0,0,0,1,2,2,2,2,2,1,1,0,0,0,0,0},
                            {0,0,0,1,2,2,2,1,1,0,0,0,0,0,0,0},
                            {0,0,1,1,1,1,1,0,0,0,0,0,0,0,0,0},
                            {0,1,3,3,1,0,0,0,0,0,0,0,0,0,0,0},
                            {1,3,3,1,0,0,0,0,0,0,0,0,0,0,0,0},
                            {1,3,3,1,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,1,1,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0}
                        };
                        byte val = foodMap[py, px];
                        if (val == 1) { r = 45; g = 24; b = 12; a = 255; }
                        else if (val == 2) { r = 186; g = 98; b = 34; a = 255; }
                        else if (val == 3) { r = 240; g = 232; b = 218; a = 255; }
                        else if (val == 4) { r = 226; g = 142; b = 58; a = 255; }
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
                            {0,0,0,0,1,2,2,2,2,2,2,1,0,0,0,0},
                            {0,0,0,1,2,2,2,2,2,1,1,0,0,0,0,0},
                            {0,0,0,1,2,2,2,1,1,0,0,0,0,0,0,0},
                            {0,0,1,1,1,1,1,0,0,0,0,0,0,0,0,0},
                            {0,1,3,3,1,0,0,0,0,0,0,0,0,0,0,0},
                            {1,3,3,1,0,0,0,0,0,0,0,0,0,0,0,0},
                            {1,3,3,1,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,1,1,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0},
                            {0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0}
                        };
                        byte val = foodMap[py, px];
                        if (val == 1) { r = 30; g = 30; b = 30; a = 255; }
                        else if (val == 2) { r = 60; g = 60; b = 60; a = 180; }
                        else if (val == 3) { r = 80; g = 80; b = 80; a = 180; }
                        else { a = 0; }
                    } else if (tile == TDiamondOre) {
                        // Чистая сланцевая порода с яркими гранеными кристаллами алмаза
                        int distA = Math.Abs(px - 5) + Math.Abs(py - 5);
                        int distB = Math.Abs(px - 11) + Math.Abs(py - 10);
                        bool isShd = px >= 10 && px <= 11 && py >= 3 && py <= 4;
                        if (distA <= 2) {
                            if (distA == 0) { r = 255; g = 255; b = 255; }
                            else if (px <= 5 && py <= 5) { r = 0; g = 245; b = 255; }
                            else if (px > 5 && py > 5) { r = 0; g = 140; b = 180; }
                            else { r = 0; g = 210; b = 240; }
                        } else if (distB <= 2) {
                            if (distB == 0) { r = 240; g = 255; b = 255; }
                            else if (px <= 11 && py <= 10) { r = 0; g = 240; b = 255; }
                            else if (px > 11 && py > 10) { r = 0; g = 130; b = 170; }
                            else { r = 0; g = 205; b = 235; }
                        } else if (isShd) {
                            r = 0; g = 230; b = 250;
                        }
                    } else if (tile == TGoldOre) {
                        // Октаэдрические кристаллы золота в монолитном сланце
                        int distA = Math.Abs(px - 5) + Math.Abs(py - 9);
                        int distB = Math.Abs(px - 10) + Math.Abs(py - 5);
                        bool isShd = px >= 3 && px <= 4 && py >= 3 && py <= 4;
                        if (distA <= 2) {
                            if (distA == 0) { r = 255; g = 250; b = 180; }
                            else if (px <= 5 && py <= 9) { r = 255; g = 220; b = 40; }
                            else if (px > 5 && py > 9) { r = 180; g = 130; b = 20; }
                            else { r = 245; g = 195; b = 30; }
                        } else if (distB <= 2) {
                            if (distB == 0) { r = 255; g = 245; b = 160; }
                            else if (px <= 10 && py <= 5) { r = 255; g = 215; b = 35; }
                            else if (px > 10 && py > 5) { r = 175; g = 125; b = 20; }
                            else { r = 240; g = 190; b = 30; }
                        } else if (isShd) {
                            r = 245; g = 200; b = 35;
                        }
                    } else if (tile == TIronOre) {
                        // Металлические серебристо-стальные самородки в камне
                        bool inA = px >= 4 && px <= 6 && py >= 4 && py <= 6;
                        bool inB = px >= 9 && px <= 11 && py >= 8 && py <= 10;
                        bool inC = px >= 10 && px <= 11 && py >= 3 && py <= 4;
                        if (inA) {
                            if (px == 4 && py == 4) { r = 255; g = 255; b = 255; }
                            else if (px == 4 || py == 4) { r = 240; g = 240; b = 246; }
                            else if (px == 6 || py == 6) { r = 140; g = 125; b = 115; }
                            else { r = 210; g = 210; b = 220; }
                        } else if (inB) {
                            if (px == 9 && py == 8) { r = 255; g = 255; b = 255; }
                            else if (px == 9 || py == 8) { r = 235; g = 235; b = 242; }
                            else if (px == 11 || py == 10) { r = 135; g = 120; b = 110; }
                            else { r = 205; g = 205; b = 215; }
                        } else if (inC) {
                            r = 220; g = 220; b = 228;
                        }
                    } else if (tile == TCoalOre) {
                        // Глянцевые кристаллы чёрного обсидиана / угля со сколами
                        int distA = Math.Abs(px - 5) + Math.Abs(py - 5);
                        int distB = Math.Abs(px - 10) + Math.Abs(py - 9);
                        bool isShd = px >= 10 && px <= 11 && py >= 3 && py <= 4;
                        if (distA <= 2) {
                            if (distA == 0) { r = 150; g = 150; b = 165; }
                            else if (px <= 5 && py <= 5) { r = 65; g = 65; b = 75; }
                            else { r = 28; g = 28; b = 34; }
                        } else if (distB <= 2) {
                            if (distB == 0) { r = 140; g = 140; b = 155; }
                            else if (px <= 10 && py <= 9) { r = 60; g = 60; b = 70; }
                            else { r = 25; g = 25; b = 30; }
                        } else if (isShd) {
                            r = 55; g = 55; b = 65;
                        }
                    } else if (tile == TRedstoneOre) {
                        // Светящиеся призмы рубинового редстоуна с мягким ореолом
                        int distA = Math.Abs(px - 5) + Math.Abs(py - 6);
                        int distB = Math.Abs(px - 10) + Math.Abs(py - 9);
                        bool isShd = px >= 3 && px <= 4 && py >= 10 && py <= 11;
                        if (distA <= 2) {
                            if (distA == 0) { r = 255; g = 220; b = 230; }
                            else if (px <= 5 && py <= 6) { r = 255; g = 50; b = 75; }
                            else if (px > 5 && py > 6) { r = 160; g = 15; b = 30; }
                            else { r = 235; g = 30; b = 55; }
                        } else if (distB <= 2) {
                            if (distB == 0) { r = 255; g = 200; b = 215; }
                            else if (px <= 10 && py <= 9) { r = 255; g = 45; b = 70; }
                            else if (px > 10 && py > 9) { r = 155; g = 15; b = 30; }
                            else { r = 230; g = 25; b = 50; }
                        } else if (isShd) {
                            r = 245; g = 40; b = 65;
                        } else if (distA <= 4 || distB <= 4) {
                            r = Math.Clamp(r + 35, 0, 255);
                            g = Math.Clamp(g - 15, 0, 255);
                            b = Math.Clamp(b - 15, 0, 255);
                        }
                    } else if (tile == TNetherQuartzOre) {
                        // Острые призмы розово-белого кварца в инфернальном камне
                        bool inA = px >= 4 && px <= 6 && py >= 4 && py <= 7;
                        bool inB = px >= 9 && px <= 11 && py >= 7 && py <= 10;
                        bool inC = px >= 6 && px <= 7 && py >= 10 && py <= 11;
                        if (inA) {
                            if (px == 5 && py == 4) { r = 255; g = 255; b = 255; }
                            else if (px == 4 || py == 4) { r = 250; g = 238; b = 242; }
                            else if (px == 6 || py == 7) { r = 195; g = 145; b = 160; }
                            else { r = 238; g = 218; b = 225; }
                        } else if (inB) {
                            if (px == 11 && py == 7) { r = 255; g = 255; b = 255; }
                            else if (px == 9 || py == 7) { r = 248; g = 235; b = 238; }
                            else if (px == 11 || py == 10) { r = 190; g = 140; b = 155; }
                            else { r = 235; g = 215; b = 222; }
                        } else if (inC) {
                            r = 240; g = 225; b = 230;
                        }
                    } else if (tile == TCobblestone || tile == TMossyCobble) {
                        // 4 аккуратные плитки с закругленными фасками и темным швом
                        bool isMortar = px == 7 || px == 8 || py == 7 || py == 8;
                        if (isMortar) {
                            if (tile == TMossyCobble) {
                                r = 55 + ((px * 3 + py * 7) % 15);
                                g = 145 + ((px * 7 + py * 3) % 25);
                                b = 48;
                            } else {
                                r = 42; g = 44; b = 48;
                            }
                        } else {
                            int qx = px < 7 ? px : px - 9;
                            int qy = py < 7 ? py : py - 9;
                            int pBevel = (qy == 0 ? 14 : 0) + (qx == 0 ? 10 : 0) - (qy == 6 ? 16 : 0) - (qx == 6 ? 12 : 0);
                            r = Math.Clamp(105 + pBevel, 0, 255);
                            g = Math.Clamp(110 + pBevel, 0, 255);
                            b = Math.Clamp(118 + pBevel, 0, 255);
                        }
                    } else if (tile == TPlanks) {
                        // 3 гладкие деревянные доски с аккуратными стыками
                        bool isSeam = py == 4 || py == 10;
                        if (isSeam) {
                            r = 72; g = 48; b = 24;
                        } else {
                            int boardY = py <= 3 ? py : (py <= 9 ? py - 5 : py - 11);
                            int bH = py <= 3 ? 4 : (py <= 9 ? 5 : 5);
                            int bBevel = (boardY == 0 ? 14 : 0) - (boardY == bH - 1 ? 14 : 0);
                            r = Math.Clamp(175 + bBevel, 0, 255);
                            g = Math.Clamp(132 + bBevel, 0, 255);
                            b = Math.Clamp(80 + bBevel, 0, 255);
                        }
                    } else if (tile == TLogSide) {
                        // Вертикальные желобки коры дерева
                        bool isCrease = px == 5 || px == 11;
                        if (isCrease) {
                            r = 60; g = 40; b = 20;
                        } else {
                            int colX = px <= 4 ? px : (px <= 10 ? px - 6 : px - 12);
                            int cW = px <= 4 ? 5 : (px <= 10 ? 5 : 4);
                            int cBevel = (colX == 0 ? 12 : 0) - (colX == cW - 1 ? 14 : 0);
                            r = Math.Clamp(105 + cBevel, 0, 255);
                            g = Math.Clamp(75 + cBevel, 0, 255);
                            b = Math.Clamp(45 + cBevel, 0, 255);
                        }
                    } else if (tile == TLogTop) {
                        // Срез бревна: годовые кольца и сердцевина
                        bool isBark = px <= 1 || px >= 14 || py <= 1 || py >= 14;
                        if (isBark) {
                            r = 75; g = 48; b = 26;
                        } else {
                            float dist = MathF.Max(MathF.Abs(px - 7.5f), MathF.Abs(py - 7.5f));
                            if (dist <= 1.5f) {
                                r = 195; g = 150; b = 95;
                            } else if (dist >= 3.2f && dist <= 4.0f) {
                                r = 140; g = 102; b = 60;
                            } else {
                                r = 170; g = 128; b = 80;
                            }
                        }
                    } else if (tile == TBedrock) {
                        // Монолитная титановая плита с диагональным усилением и заклепками
                        bool isBorder = px <= 1 || px >= 14 || py <= 1 || py >= 14;
                        bool isRivet = (px == 2 || px == 13) && (py == 2 || py == 13);
                        bool isBrace = Math.Abs(px - py) <= 1 || Math.Abs(px + py - 15) <= 1;
                        if (isRivet) {
                            r = 205; g = 210; b = 220;
                        } else if (isBrace) {
                            bool highlight = px == py || px + py == 15;
                            r = highlight ? 65 : 48;
                            g = highlight ? 70 : 52;
                            b = highlight ? 82 : 62;
                        } else if (isBorder) {
                            bool topL = px <= 1 || py <= 1;
                            r = topL ? 46 : 18;
                            g = topL ? 48 : 20;
                            b = topL ? 56 : 24;
                        } else {
                            r = 28; g = 30; b = 36;
                        }
                    } else if (tile == TObsidian) {
                        // Тёмный обсидиановый монолит с неоновыми фиолетовыми разломами
                        bool isVein = (px + py * 2 == 12 || px * 2 - py == 8 || px + py == 22) && px >= 2 && px <= 13 && py >= 2 && py <= 13;
                        if (isVein) {
                            r = 160; g = 70; b = 245;
                        } else {
                            int bvl = (py == 0 || px == 0 ? 8 : 0) - (py == 15 || px == 15 ? 8 : 0);
                            r = Math.Clamp(24 + bvl, 0, 255);
                            g = Math.Clamp(18 + bvl, 0, 255);
                            b = Math.Clamp(36 + bvl, 0, 255);
                        }
                    } else if (tile == TGlass) {
                        // Кристально прозрачное стекло со стильными бликами
                        bool isBorder = px == 0 || px == 15 || py == 0 || py == 15;
                        bool glint1 = (px + py == 7 || px + py == 8) && px >= 2 && px <= 7;
                        bool glint2 = (px + py == 21 || px + py == 22) && px >= 9 && px <= 13;
                        if (glint1 || glint2) {
                            r = 255; g = 255; b = 255; a = 230;
                        } else if (isBorder) {
                            r = 160; g = 230; b = 255; a = 210;
                        } else {
                            r = 180; g = 235; b = 255; a = 35;
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
                    } else if (tile >= TPickaxeGold && tile <= THoeGold) {
                        // Золотые инструменты: общие формы инструментов, золотой цвет
                        int gt = tile - TPickaxeGold; // 0=кирка, 1=топор, 2=меч, 3=лопата, 4=мотыга
                        (int mr, int mg, int mb) = (245, 200, 50);
                        bool isStick = (px == 15 - py) && (py >= 6);
                        bool isHead = false;
                        switch (gt) {
                            case 0: isHead = py <= 6 && px <= 11 && Math.Abs(px + py - 8) <= 3; break;                        // кирка
                            case 1: isHead = px >= 3 && px <= 9 && py >= 2 && py <= 7 && (px <= 6 || py <= 5); break;         // топор
                            case 2: { bool blade = (px == py || px == py + 1) && py >= 3 && py <= 12;
                                      bool guard = (px + py == 20) && Math.Abs(px - py) <= 3;
                                      isStick = (px == py) && py >= 12;
                                      isHead = blade || guard; break; }                                                       // меч
                            case 3: isHead = px >= 2 && px <= 7 && py >= 2 && py <= 7; break;                                 // лопата
                            case 4: { bool bTop = py >= 2 && py <= 4 && px >= 4 && px <= 12;
                                      bool bHook = px >= 4 && px <= 6 && py >= 4 && py <= 7;
                                      isHead = bTop || bHook; break; }                                                        // мотыга
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
                    } else if (tile == TTallGrass || tile == TTallGrassPlains || tile == TTallGrassSavanna || tile == TTallGrassSwamp) {
                        // Трава: реалистичные лепестки и стебельки травы (Cross-billboard)
                        bool isGrassBlade = false;
                        if (py >= 4 && (px == 3 || px == 4) && py >= 6) isGrassBlade = true;
                        if (py >= 2 && (px == 6 || px == 7 || px == 8)) isGrassBlade = true;
                        if (py >= 5 && (px == 10 || px == 11)) isGrassBlade = true;
                        if (py >= 3 && px == 13 && py >= 7) isGrassBlade = true;
                        if (py >= 8 && (px == 2 || px == 5 || px == 9 || px == 12 || px == 14)) isGrassBlade = true;
                        if (py >= 13 && px >= 2 && px <= 14) isGrassBlade = true;

                        if (isGrassBlade) {
                            int shade = (px * 7 + py * 11) % 35;
                            var (tc, _) = palette[tile];
                            r = Math.Clamp(tc.R + shade - 15, 0, 255);
                            g = Math.Clamp(tc.G + shade - 15, 0, 255);
                            b = Math.Clamp(tc.B + shade / 2 - 8, 0, 255);
                            a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TGrassSide || tile == TGrassSidePlains || tile == TGrassSideSavanna || tile == TGrassSideSwamp) {
                        bool isGrassFringe = py <= 2 || (py == 3 && (px % 3 == 0 || px % 4 == 1));
                        if (isGrassFringe) {
                            int topTile = tile switch {
                                TGrassSidePlains => TGrassTopPlains,
                                TGrassSideSavanna => TGrassTopSavanna,
                                TGrassSideSwamp => TGrassTopSwamp,
                                _ => TGrassTop
                            };
                            var (gcCol, _) = palette[topTile];
                            int d = (px * 13 + py * 7) % 20;
                            r = Math.Clamp(gcCol.R + d - 10, 0, 255);
                            g = Math.Clamp(gcCol.G + d - 10, 0, 255);
                            b = Math.Clamp(gcCol.B + d - 10, 0, 255);
                            a = 255;
                        } else {
                            r = 122 + (px * 7 + py * 13) % 25 - 12;
                            g = 92 + (px * 7 + py * 13) % 20 - 10;
                            b = 58 + (px * 7 + py * 13) % 15 - 7;
                            a = 255;
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
                    } else if (tile == TDoorUpper) {
                        bool frame = px <= 1 || px >= 14 || py <= 1 || py >= 14;
                        bool window = ((px >= 3 && px <= 6) || (px >= 9 && px <= 12)) && (py >= 3 && py <= 10);
                        if (window) {
                            r = 170; g = 215; b = 240; a = 190;
                        } else if (frame) {
                            r = 110; g = 75; b = 40; a = 255;
                        } else {
                            r = 145 + (px % 3) * 10; g = 105; b = 60; a = 255;
                        }
                    } else if (tile == TDoorLower) {
                        bool frame = px <= 1 || px >= 14 || py <= 1 || py >= 14;
                        bool handle = (px == 12 || px == 13) && (py == 3 || py == 4);
                        if (handle) {
                            r = 40; g = 40; b = 45; a = 255;
                        } else if (frame) {
                            r = 110; g = 75; b = 40; a = 255;
                        } else {
                            r = 145 + (px % 3) * 10; g = 105; b = 60; a = 255;
                        }
                    } else if (tile == TDoorItem) {
                        if (px >= 3 && px <= 12 && py >= 1 && py <= 14) {
                            bool window = ((px >= 5 && px <= 6) || (px >= 9 && px <= 10)) && (py >= 3 && py <= 6);
                            bool handle = (px == 10 || px == 11) && (py == 8);
                            if (window) { r = 170; g = 215; b = 240; a = 230; }
                            else if (handle) { r = 40; g = 40; b = 45; a = 255; }
                            else if (px == 3 || px == 12 || py == 1 || py == 14) { r = 110; g = 75; b = 40; a = 255; }
                            else { r = 145; g = 105; b = 60; a = 255; }
                        } else {
                            a = 0;
                        }
                    } else if (tile == TBucket || tile == TWaterBucket || tile == TLavaBucket) {
                        bool isHandle = (py == 2 && (px == 7 || px == 8)) || (py == 3 && (px == 5 || px == 6 || px == 9 || px == 10));
                        int widthAtY = 12 - (py - 4) / 2;
                        int minX = 7 - widthAtY / 2;
                        int maxX = 8 + widthAtY / 2;
                        bool insideBucket = py >= 4 && py <= 13 && px >= minX && px <= maxX;
                        bool isRim = insideBucket && (px == minX || px == maxX || py == 13 || py == 4);

                        if (isHandle) {
                            r = 130; g = 130; b = 135; a = 255;
                        } else if (isRim) {
                            bool highlight = px == minX || py == 4;
                            r = highlight ? 220 : 160;
                            g = highlight ? 220 : 160;
                            b = highlight ? 230 : 170;
                            a = 255;
                        } else if (insideBucket) {
                            if (tile == TWaterBucket) {
                                r = 38 + ((px + py) % 3) * 15;
                                g = 95 + ((px + py) % 4) * 20;
                                b = 225;
                                a = 255;
                            } else if (tile == TLavaBucket) {
                                r = 245;
                                g = 80 + ((px * 2 + py) % 4) * 25;
                                b = 15;
                                a = 255;
                            } else {
                                r = 70; g = 70; b = 75; a = 255;
                            }
                        } else {
                            a = 0;
                        }
                    } else if (tile == TEndPortal) {
                        // Портал в Энд: тёмная звёздная пурпурная воронка
                        int wave = (int)(MathF.Sin(px * 0.7f + py * 0.9f) * 30f);
                        r = Math.Clamp(55 + wave, 20, 120);
                        g = Math.Clamp(25 + wave / 2, 10, 70);
                        b = Math.Clamp(95 + wave, 40, 160);
                        a = 215;
                    } else if (tile == TEnderCrystal) {
                        // Хрустальный эндер-кристалл
                        float dxc = (px - 8f) / 5.5f, dyc = (py - 8f) / 6.5f;
                        bool inGem = dxc * dxc + dyc * dyc <= 1f && py >= 2 && py <= 14;
                        bool shine = inGem && px >= 6 && px <= 7 && py >= 5 && py <= 7;
                        if (shine) { r = 230; g = 255; b = 255; a = 255; }
                        else if (inGem) { r = 150; g = 225; b = 240; a = 235; }
                        else { a = 0; }
                    } else if (tile >= TEnderPearl && tile <= TVoidKey) {
                        // Энд и артефакты: предметы с прозрачным фоном (жемчуг, око, порох, слизь, артефакты, ключ)
                        float cx = 8f, cy = 8f;
                        bool inObj = tile switch {
                            TEnderPearl => (px - cx) * (px - cx) + (py - cy) * (py - cy) <= 24,
                            TEyeOfEnder => (px - cx) * (px - cx) + (py - cy) * (py - cy) <= 26,
                            TBlazePowder => (px - cx) * (px - cx) + (py - 10) * (py - 10) <= 18 && px >= 3 && px <= 13 && py >= 6,
                            TChorusFruit => (px - cx) * (px - cx) * 0.8f + (py - cy) * (py - cy) * 1.1f <= 20,
                            TNetherArtifact => (px - cx) * (px - cx) + (py - cy) * (py - cy) <= 24,
                            TSwampArtifact => (px - cx) * (px - cx) + (py - cy) * (py - cy) <= 28,
                            TDesertArtifact => MathF.Abs(px - cx) + MathF.Abs(py - cy) <= 7,
                            TVoidKey => (px - cx) * (px - cx) + (py - 3) * (py - 3) <= 9
                                        || (px >= 6 && px <= 10 && py >= 3 && py <= 14)
                                        || (px >= 10 && px <= 13 && (py == 10 || py == 13)),
                            _ => (px - cx) * (px - cx) + (py - cy) * (py - cy) <= 30,
                        };
                        if (inObj) {
                            (int mr, int mg, int mb) = tile switch {
                                TEnderPearl => (40, 180, 185),
                                TEyeOfEnder => (35, 175, 150),
                                TBlazePowder => (235, 150, 40),
                                TChorusFruit => (155, 85, 165),
                                TNetherArtifact => (205, 70, 40),
                                TSwampArtifact => (65, 145, 75),
                                TDesertArtifact => (215, 175, 70),
                                _ => (90, 60, 140),
                            };
                            r = mr; g = mg; b = mb; a = 255;
                            if (tile == TEyeOfEnder) {
                                bool pupil = (px - 8) * (px - 8) + (py - 8) * (py - 8) <= 4;
                                if (pupil) { r = 220; g = 235; b = 255; }
                            } else if (tile == TEndSlime) {
                                bool shine = (px - 8) * (px - 8) + (py - 6) * (py - 6) <= 4;
                                if (shine) { r = 120; g = 95; b = 140; }
                            } else if (tile == TNetherArtifact) {
                                bool hot = (px - 8) * (px - 8) + (py - 8) * (py - 8) <= 5;
                                if (hot) { r = 255; g = 200; b = 90; }
                            } else if (tile == TSwampArtifact) {
                                bool slime = (px - 8) * (px - 8) + (py - 6) * (py - 6) <= 4;
                                if (slime) { r = 130; g = 215; b = 130; }
                            } else if (tile == TDesertArtifact) {
                                bool glint = (px - 7) * (px - 7) + (py - 6) * (py - 6) <= 3;
                                if (glint) { r = 255; g = 240; b = 170; }
                            } else if (tile == TVoidKey) {
                                bool glow = (px - 8) * (px - 8) + (py - 3) * (py - 3) <= 4;
                                if (glow) { r = 200; g = 170; b = 255; }
                            }
                        } else {
                            a = 0;
                        }
                    } else if (tile >= TLeatherHelmet && tile <= TDiamondBoots) {
                        // Броня: кожаная, железная, алмазная
                        int tier = (tile - TLeatherHelmet) / 4; // 0 = Leather, 1 = Iron, 2 = Diamond
                        int piece = (tile - TLeatherHelmet) % 4; // 0 = Helmet, 1 = Chestplate, 2 = Leggings, 3 = Boots

                        (int ar, int ag, int ab) = tier switch {
                            0 => (160, 102, 54),  // Кожа (коричневый)
                            1 => (220, 222, 230), // Железо (серебристо-стальной)
                            _ => (75, 225, 235)   // Алмаз (бирюзовый)
                        };

                        bool inArmor = false;
                        if (piece == 0) {
                            // Шлем: куполообразный свод + боковые пластины
                            inArmor = px >= 4 && px <= 11 && py >= 4 && py <= 11 && (py >= 6 || (px >= 5 && px <= 10)) && !(py >= 9 && px >= 6 && px <= 9);
                        } else if (piece == 1) {
                            // Нагрудник: плечи, шея, корпус
                            inArmor = px >= 3 && px <= 12 && py >= 3 && py <= 13 && !(py <= 5 && px >= 6 && px <= 9) && !(py >= 12 && (px <= 3 || px >= 12));
                        } else if (piece == 2) {
                            // Поножи: пояс и две штанины
                            inArmor = px >= 4 && px <= 11 && py >= 4 && py <= 13 && !(py >= 7 && px >= 7 && px <= 8);
                        } else if (piece == 3) {
                            // Ботинки: два отдельных ботинка с подошвой
                            inArmor = ((px >= 3 && px <= 6) || (px >= 9 && px <= 12)) && py >= 7 && py <= 13 && (py >= 11 || (px >= 4 && px <= 6) || (px >= 9 && px <= 11));
                        }

                        if (inArmor) {
                            bool highlight = px == 5 && py <= 7;
                            bool shadow = px == 11 || py == 13;
                            if (highlight) { r = Math.Min(255, ar + 35); g = Math.Min(255, ag + 35); b = Math.Min(255, ab + 35); }
                            else if (shadow) { r = ar * 3 / 4; g = ag * 3 / 4; b = ab * 3 / 4; }
                            else { r = ar; g = ag; b = ab; }
                            a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile >= TArmorIcon && tile <= TArmorIconEmpty) {
                        // Значок брони для HUD (щиток)
                        float cx = 8f;
                        bool inShield = px >= 3 && px <= 12 && py >= 3 && py <= 13 && (py <= 8 || MathF.Abs(px - cx) <= (14 - py));
                        bool border = inShield && (px == 3 || px == 12 || py == 3 || MathF.Abs(px - cx) >= (13 - py));

                        if (inShield) {
                            if (tile == TArmorIconEmpty) {
                                r = border ? 50 : 30; g = border ? 50 : 30; b = border ? 55 : 35;
                            } else if (tile == TArmorIconHalf) {
                                if (px < 8) {
                                    r = border ? 240 : 200; g = border ? 240 : 200; b = border ? 250 : 210;
                                } else {
                                    r = border ? 60 : 35; g = border ? 60 : 35; b = border ? 65 : 40;
                                }
                            } else {
                                r = border ? 245 : 210; g = border ? 245 : 210; b = border ? 255 : 220;
                            }
                            a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TSapling) {
                        // Саженец: коричневый стволик снизу, зеленая крона сверху
                        bool stem = (px == 7 || px == 8) && py >= 8 && py <= 14;
                        bool leaves = (px >= 4 && px <= 11 && py >= 3 && py <= 8 && !((px == 4 || px == 11) && (py == 3 || py == 8)));
                        if (stem) {
                            r = 120; g = 80; b = 40; a = 255;
                        } else if (leaves) {
                            r = 60 + ((px + py) % 3) * 15; g = 150 + ((px * 3) % 25); b = 40; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TRedFlower) {
                        // Мак: зеленый стебель и красные лепестки с темным центром
                        bool stem = (px == 7 || px == 8) && py >= 7 && py <= 14;
                        bool flower = (px >= 5 && px <= 10 && py >= 2 && py <= 7);
                        bool center = (px >= 7 && px <= 8 && py >= 4 && py <= 5);
                        if (center) {
                            r = 40; g = 20; b = 20; a = 255;
                        } else if (flower) {
                            r = 220 + ((px + py) % 2) * 20; g = 30; b = 35; a = 255;
                        } else if (stem) {
                            r = 50; g = 140; b = 35; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TYellowFlower) {
                        // Одуванчик: зеленый стебель и желтые лепестки
                        bool stem = (px == 7 || px == 8) && py >= 7 && py <= 14;
                        bool flower = (px >= 5 && px <= 10 && py >= 3 && py <= 7);
                        if (flower) {
                            r = 250; g = 215 + ((px + py) % 2) * 25; b = 25; a = 255;
                        } else if (stem) {
                            r = 50; g = 140; b = 35; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TCarrot) {
                        // Морковь: оранжевый конус вниз + зеленая ботва
                        bool greens = (px >= 6 && px <= 9 && py >= 2 && py <= 5);
                        bool carrotBody = (px >= 6 && px <= 9 && py >= 6 && py <= 8) ||
                                          (px >= 7 && px <= 8 && py >= 9 && py <= 12) ||
                                          (px == 7 && py == 13);
                        if (greens) {
                            r = 50; g = 160; b = 40; a = 255;
                        } else if (carrotBody) {
                            r = 240 + (px % 2) * 15; g = 120 + (py % 2) * 15; b = 20; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TPotato) {
                        // Картофель: овальный клубень
                        bool potato = (px >= 4 && px <= 11 && py >= 5 && py <= 11 && !(px == 4 && py == 5) && !(px == 11 && py == 11));
                        bool spot = (px == 6 && py == 7) || (px == 9 && py == 9);
                        if (spot) {
                            r = 130; g = 95; b = 50; a = 255;
                        } else if (potato) {
                            r = 185 + (px % 3) * 10; g = 145 + (py % 3) * 8; b = 80; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TBakedPotato) {
                        // Печеный картофель: поджаристая корочка + горячая сердцевина
                        bool potato = (px >= 4 && px <= 11 && py >= 5 && py <= 11);
                        bool center = (px >= 6 && px <= 9 && py >= 7 && py <= 9);
                        if (center) {
                            r = 255; g = 230; b = 90; a = 255;
                        } else if (potato) {
                            r = 145 + (px % 2) * 15; g = 85 + (py % 2) * 10; b = 40; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile >= TCarrotCrop0 && tile <= TCarrotCrop3) {
                        // Стадии роста моркови 0..3
                        int stage = tile - TCarrotCrop0;
                        int topY = stage switch { 0 => 11, 1 => 8, 2 => 5, _ => 2 };
                        bool greens = (px >= 6 - stage && px <= 9 + stage && py >= topY && py <= 13);
                        bool carrotRoot = stage == 3 && px >= 7 && px <= 8 && py >= 12 && py <= 14;
                        if (carrotRoot) {
                            r = 245; g = 120; b = 20; a = 255;
                        } else if (greens && ((px * 3 + py * 7) % 2 == 0 || py == topY)) {
                            r = 50 + stage * 10; g = 150 + stage * 20; b = 35; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile >= TPotatoCrop0 && tile <= TPotatoCrop3) {
                        // Стадии роста картофеля 0..3
                        int stage = tile - TPotatoCrop0;
                        int topY = stage switch { 0 => 11, 1 => 8, 2 => 5, _ => 2 };
                        bool leaves = (px >= 5 - stage && px <= 10 + stage && py >= topY && py <= 14);
                        bool flower = stage == 3 && ((px == 6 && py == 3) || (px == 9 && py == 4));
                        if (flower) {
                            r = 240; g = 240; b = 250; a = 255;
                        } else if (leaves && ((px * 5 + py * 3) % 2 == 0 || py >= 12)) {
                            r = 40 + stage * 8; g = 135 + stage * 15; b = 40; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == THeartParticle) {
                        // Сердечко для режима любви
                        bool inHeart = (py >= 4 && py <= 6 && ((px >= 4 && px <= 6) || (px >= 9 && px <= 11))) ||
                                       (py == 7 && px >= 4 && px <= 11) ||
                                       (py == 8 && px >= 5 && px <= 10) ||
                                       (py == 9 && px >= 6 && px <= 9) ||
                                       (py == 10 && px >= 7 && px <= 8);
                        if (inHeart) {
                            r = 235; g = 30; b = 60; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TRawChicken) {
                        // Куриная ножка (сырая)
                        bool bone = (px >= 10 && px <= 13 && py >= 10 && py <= 13 && Math.Abs(px - py) <= 1);
                        bool meat = (px - 6) * (px - 6) * 1.1f + (py - 6) * (py - 6) * 0.9f <= 16;
                        if (bone) {
                            r = 240; g = 235; b = 225; a = 255;
                        } else if (meat) {
                            r = 235 + (px * 3 + py * 7) % 20; g = 170 + (px * 5) % 20; b = 160; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TCookedChicken) {
                        // Куриная ножка (жареная)
                        bool bone = (px >= 10 && px <= 13 && py >= 10 && py <= 13 && Math.Abs(px - py) <= 1);
                        bool meat = (px - 6) * (px - 6) * 1.1f + (py - 6) * (py - 6) * 0.9f <= 16;
                        if (bone) {
                            r = 240; g = 235; b = 225; a = 255;
                        } else if (meat) {
                            bool crust = (px <= 4 || py <= 4 || (px + py) % 3 == 0);
                            r = crust ? 140 : 185; g = crust ? 70 : 110; b = crust ? 25 : 40; a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TEgg) {
                        // Яйцо: гладкий белый овал
                        float ex = (px - 8f) / 4.5f, ey = (py - 8.5f) / 6.0f;
                        bool inEgg = (ex * ex + ey * ey <= 1.0f) && (py >= 3 && py <= 14);
                        if (inEgg) {
                            bool shine = px >= 6 && px <= 7 && py >= 5 && py <= 7;
                            r = shine ? 255 : 235 + (px % 2) * 10;
                            g = shine ? 255 : 230 + (py % 2) * 10;
                            b = shine ? 250 : 215;
                            a = 255;
                        } else {
                            a = 0;
                        }
                    } else if (tile == TFire) {
                        // Анимированное процедурное пламя огня
                        bool isFlame = (px >= 2 && px <= 13 && py >= 1 && py <= 15 && ((15 - py) * 0.75f >= Math.Abs(px - 7.5f) - 1.8f));
                        if (isFlame) {
                            int flameGrad = 15 - py;
                            if (flameGrad < 4) {
                                r = 255; g = 230 + (px % 2) * 25; b = 70;
                            } else if (flameGrad < 9) {
                                r = 255; g = 140 + (px % 3) * 20; b = 10;
                            } else {
                                r = 220 + (px % 2) * 35; g = 40 + (py % 2) * 20; b = 0;
                            }
                            a = (byte)(py <= 2 ? 180 : 255);
                        } else {
                            a = 0;
                        }
                    } else if (tile == TJukeboxTop) {
                        // Верх проигрывателя: полированное дубовое дерево, виниловый стол + игла
                        bool isBorder = px == 0 || px == 15 || py == 0 || py == 15;
                        float discDistSq = (px - 7.5f) * (px - 7.5f) + (py - 7.5f) * (py - 7.5f);
                        bool isDisc = discDistSq <= 24f;
                        bool isRim = discDistSq <= 27f && discDistSq > 21f;
                        bool isCenter = discDistSq <= 3.5f;
                        bool isToneArm = (px >= 10 && px <= 13 && py >= 2 && py <= 5 && Math.Abs(px + py - 15) <= 1);
                        if (isCenter) {
                            r = 235; g = 190; b = 40; a = 255; // Золотой шпиндель
                        } else if (isRim) {
                            r = 180; g = 130; b = 45; a = 255; // Латунный обод
                        } else if (isDisc) {
                            int groove = ((int)MathF.Sqrt(discDistSq)) % 2;
                            r = groove == 0 ? 32 : 22; g = groove == 0 ? 32 : 22; b = groove == 0 ? 36 : 26; a = 255; // Черный винил
                        } else if (isToneArm) {
                            r = 240; g = 205; b = 70; a = 255; // Золотой тонарм
                        } else if (isBorder) {
                            r = 75; g = 50; b = 30; a = 255;
                        } else {
                            r = 120 + (px % 2) * 10; g = 80 + (py % 2) * 8; b = 45; a = 255;
                        }
                    } else if (tile == TJukeboxSide) {
                        // Боковая сторона проигрывателя: дубовый корпус + декоративная латунная решетка
                        bool isBorder = px == 0 || px == 15 || py == 0 || py == 15;
                        bool isCorner = (px <= 2 || px >= 13) && (py <= 2 || py >= 13);
                        bool isSpeakerGrille = px >= 4 && px <= 11 && py >= 4 && py <= 11;
                        bool isDiamondPatt = isSpeakerGrille && ((px + py) % 2 == 0);
                        if (isCorner) {
                            r = 210; g = 175; b = 50; a = 255; // Латунные уголки
                        } else if (isSpeakerGrille) {
                            if (isDiamondPatt) {
                                r = 165; g = 125; b = 40; a = 255; // Тканевая решетка / ромбы
                            } else {
                                r = 45; g = 30; b = 18; a = 255;
                            }
                        } else if (isBorder) {
                            r = 70; g = 45; b = 25; a = 255;
                        } else {
                            r = 115 + (px % 3) * 8; g = 75 + (py % 3) * 6; b = 42; a = 255;
                        }
                    } else {
                        if (tile == TLeaves || tile == TLeavesPlains || tile == TLeavesSavanna || tile == TLeavesSwamp) {
                            // Органическая текстура листвы без перфорации и муара
                            int leafNoise = (px * 13 + py * 29) % 7;
                            if (leafNoise == 0) { r += 16; g += 22; b += 12; }
                            else if (leafNoise == 1) { r -= 18; g -= 24; b -= 14; }
                            else if (leafNoise == 2) { r -= 10; g -= 14; b -= 8; }
                        }
                        if (tile == TVoidGate) {
                            // Врата Бездны: тёмный камень со светящейся фиолетовой руной
                            bool rune = (MathF.Abs(px - 8) + MathF.Abs(py - 8) <= 4) && ((px + py) % 2 == 0);
                            bool glow = (px - 8) * (px - 8) + (py - 8) * (py - 8) <= 12;
                            if (rune) { r = 190; g = 150; b = 255; }
                            else if (glow) { r += 30; g += 20; b += 45; }
                        }
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
}
