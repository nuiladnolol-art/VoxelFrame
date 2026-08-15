using Raylib_cs;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

internal static class Program {
    public const int WindowW = 1280, WindowH = 720;
    public static string PlayerName = "Player";

    private static int Main(string[] args) {
        if (args.Contains("--smoke")) return SmokeTest.Run();

        string? autoshotFile = null;
        int autoshotFrames = 0;
        for (int i = 0; i < args.Length - 2; i++) {
            if (args[i] == "--autoshot") {
                autoshotFile = args[i + 1];
                autoshotFrames = int.Parse(args[i + 2]);
            }
        }

        bool startFullscreen = false;
        int launchWidth = 1280, launchHeight = 720;
        try {
            for (int i = 0; i < args.Length; i++) {
                if (args[i] == "--username" && i + 1 < args.Length) PlayerName = args[i + 1];
                if (args[i] == "--fullscreen") startFullscreen = true;
                if (args[i] == "--width" && i + 1 < args.Length) int.TryParse(args[i + 1], out launchWidth);
                if (args[i] == "--height" && i + 1 < args.Length) int.TryParse(args[i + 1], out launchHeight);
            }
            // Fallback: \u0447\u0438\u0442\u0430\u0435\u043c launcher.json, \u0435\u0441\u043b\u0438 \u0430\u0440\u0433\u0443\u043c\u0435\u043d\u0442\u044b \u043d\u0435 \u043f\u0435\u0440\u0435\u0434\u0430\u043d\u044b
            if (!args.Any(a => a == "--username" || a == "--fullscreen")) {
                string configPath = Path.Combine(SaveSystem.SaveDirectory, "launcher.json");
                if (File.Exists(configPath)) {
                    string text = File.ReadAllText(configPath);
                    if (text.Contains("\"Fullscreen\": true")) startFullscreen = true;
                    if (text.Contains("\"Username\":")) {
                        int s2 = text.IndexOf("\"Username\":") + 11;
                        int e2 = text.IndexOf(",", s2);
                        if (e2 == -1) e2 = text.IndexOf("}", s2);
                        string nm = text.Substring(s2, e2 - s2).Replace("\"", "").Trim();
                        if (!string.IsNullOrEmpty(nm)) PlayerName = nm;
                    }
                }
            }
        } catch { }

        Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow);
        Raylib.InitWindow(launchWidth, launchHeight, "VoxelFrame — выживание");
        try {
            string iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "icon.png");
            if (!File.Exists(iconPath)) iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "assets", "icon.png");
            if (File.Exists(iconPath)) {
                var iconImg = Raylib.LoadImage(iconPath);
                if (iconImg.Width > 0) {
                    Raylib.SetWindowIcon(iconImg);
                    unsafe { Raylib.UnloadImage(iconImg); }
                }
            }
        } catch { }
        if (startFullscreen) {
            Raylib.ToggleBorderlessWindowed();
        }
        Raylib.SetTargetFPS(60);
        Raylib.SetExitKey(KeyboardKey.Null);   // ESC обрабатывается игрой

        SaveSystem.LoadSettings();
        SoundSystem.Initialize();
        TextureAtlas.Load();
        Fonts.Load();
        RegisterTiles();

        GameSession? session = null;
        WorldRenderer? renderer = null;
        int frames = 0;
        bool cursorCaptured = false;
        float pauseDebounce = 0f;

        if (autoshotFile != null) {
            session = GameSession.NewGame(12345, headless: false);
            renderer = new WorldRenderer(session);
        }

        while (!Raylib.WindowShouldClose()) {
            float dt = MathF.Min(Raylib.GetFrameTime(), 0.1f);
            if (pauseDebounce > 0f) pauseDebounce -= dt;

            if (session == null) {
                if (cursorCaptured) {
                    Raylib.EnableCursor();
                    cursorCaptured = false;
                }
                Raylib.BeginDrawing();
                MenuAction action = MenuAction.None;
                if (Screens.InSettingsScreen) {
                    if (Screens.InGraphicsScreen) Screens.DrawGraphics();
                    else if (Screens.InAudioScreen) Screens.DrawAudio();
                    else if (Screens.InGameplayScreen) Screens.DrawGameplay();
                    else if (Screens.InControlsScreen) Screens.DrawControls();
                    else Screens.DrawSettings();
                    Raylib.EndDrawing();
                    if (Raylib.IsKeyPressed(KeyBinds.Pause)) {
                        if (Screens.InGraphicsScreen) { Screens.InGraphicsScreen = false; SaveSystem.SaveSettings(); }
                        else if (Screens.InAudioScreen) { Screens.InAudioScreen = false; SaveSystem.SaveSettings(); }
                        else if (Screens.InGameplayScreen) { Screens.InGameplayScreen = false; SaveSystem.SaveSettings(); }
                        else if (Screens.InControlsScreen) { Screens.InControlsScreen = false; SaveSystem.SaveSettings(); }
                        else Screens.InSettingsScreen = false;
                    }
                } else {
                    action = Screens.DrawMenu(dt);
                    Raylib.EndDrawing();
                }

                if (action == MenuAction.NewGame) {
                    Screens.Reset();
                    session = GameSession.NewGame(new Random().Next(), headless: false);
                    renderer = new WorldRenderer(session);
                    session.Ui = UiState.Loading;
                    session.LoadTotal = 1;
                    session.LoadDone = 0;
                    session.AddMessage($"Привет, {PlayerName}!");
                } else if (action == MenuAction.Continue) {
                    try {
                        Screens.Reset();
                        session = SaveSystem.Load(SaveSystem.SavePath, headless: false);
                        renderer = new WorldRenderer(session);
                        session.Ui = UiState.Loading;
                        session.LoadTotal = 1;
                        session.LoadDone = 0;
                        session.AddMessage($"С возвращением, {PlayerName}!");
                    } catch (Exception ex) {
                        Screens.MenuError = $"Не удалось загрузить: {ex.Message}";
                        session = null;
                    }
                } else if (action == MenuAction.Exit) {
                    break;
                }
                continue;
            }

            // Экран загрузки: рисуем + строим меши
            if (session.Ui == UiState.Loading) {
                // Строим меши пока не появится хотя бы один видимый чанк
                renderer!.ProcessMeshQueue();
                session.LoadDone = Math.Min(session.LoadDone + 3, session.LoadTotal);
                Raylib.BeginDrawing();
                Screens.DrawLoading(session);
                Raylib.EndDrawing();
                // После построения нескольких мешей — переходим в игру
                if (session.LoadDone >= session.LoadTotal) {
                    session.Ui = UiState.Playing;
                }
                continue;
            }

            if (session.Ui == UiState.Paused) {
                if (cursorCaptured) {
                    Raylib.EnableCursor();
                    cursorCaptured = false;
                }
                Raylib.BeginDrawing();
                
                if (Screens.InSettingsScreen) {
                    if (Screens.InGraphicsScreen) Screens.DrawGraphics();
                    else if (Screens.InAudioScreen) Screens.DrawAudio();
                    else if (Screens.InGameplayScreen) Screens.DrawGameplay();
                    else if (Screens.InControlsScreen) Screens.DrawControls();
                    else Screens.DrawSettings();
                    Raylib.EndDrawing();
                    if (pauseDebounce <= 0f && Raylib.IsKeyPressed(KeyBinds.Pause)) {
                        pauseDebounce = 0.25f;
                        if (Screens.InGraphicsScreen) { Screens.InGraphicsScreen = false; SaveSystem.SaveSettings(); }
                        else if (Screens.InAudioScreen) { Screens.InAudioScreen = false; SaveSystem.SaveSettings(); }
                        else if (Screens.InGameplayScreen) { Screens.InGameplayScreen = false; SaveSystem.SaveSettings(); }
                        else if (Screens.InControlsScreen) { Screens.InControlsScreen = false; SaveSystem.SaveSettings(); }
                        else Screens.InSettingsScreen = false;
                    }
                    continue;
                }
                
                Raylib.ClearBackground(new Color(10, 12, 20, 255));
                renderer!.ProcessMeshQueue();
                renderer.DrawSky();
                Raylib.BeginMode3D(session.Camera);
                renderer.Draw3DSky(session.Camera);
                renderer.DrawWorld();
                renderer.DrawDecorations(dt);
                renderer.DrawEntities(session.Camera);
                Raylib.EndMode3D();

                var pauseAction = Screens.DrawPause(session);
                Raylib.EndDrawing();

                if ((pauseDebounce <= 0f && Raylib.IsKeyPressed(KeyBinds.Pause)) || pauseAction == PauseAction.Resume) {
                    session.Ui = UiState.Playing;
                    pauseDebounce = 0.25f;
                } else if (pauseAction == PauseAction.Settings) {
                    Screens.InSettingsScreen = true;
                    Screens.SettingsOpenedFromGame = true;
                    pauseDebounce = 0.25f;
                } else if (pauseAction == PauseAction.SaveAndExit) {
                    session.SaveTo(SaveSystem.SavePath);
                    session.Ui = UiState.Playing;
                    session = null;
                    // Не вызываем Dispose() — это вызывает зависание OpenGL.
                    // Рендерер будет освобождён при закрытии окна или сборке мусора.
                    renderer = null;
                }
                continue;
            }

            var input = ReadInput(session.Ui == UiState.Playing, pauseDebounce);
            if (input.Pause) pauseDebounce = 0.25f;
            session.Tick(dt, input);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(10, 12, 20, 255));

            renderer!.ProcessMeshQueue();
            renderer.DrawSky();
            Raylib.BeginMode3D(session.Camera);
            renderer.Draw3DSky(session.Camera);
            renderer.DrawWorld();
            renderer.DrawClouds(session.Camera);
            renderer.DrawDecorations(dt);
            renderer.DrawEntities(session.Camera);
            Raylib.EndMode3D();

            // Защита от X-Ray (если камера внутри непрозрачного блока — черная заглушка)
            var camCell = new VoxelFrame.Core.Vec3i((int)MathF.Floor(session.Camera.Position.X), (int)MathF.Floor(session.Camera.Position.Y), (int)MathF.Floor(session.Camera.Position.Z));
            if (session.World.IsOpaqueAt(camCell)) {
                Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(16, 14, 13, 255));
            }

            switch (session.Ui) {
                case UiState.Playing:
                    // Курсор прячем только при фокусе окна: иначе он «зависает»
                    // отдельно от прицела, а мышь не вращает камеру.
                    if (Raylib.IsWindowFocused() && !cursorCaptured) {
                        Raylib.DisableCursor();
                        cursorCaptured = true;
                    } else if (!Raylib.IsWindowFocused() && cursorCaptured) {
                        Raylib.EnableCursor();
                        cursorCaptured = false;
                    }
                    Hud.Draw(session, dt);
                    break;
                case UiState.Death:
                    if (cursorCaptured) {
                        Raylib.EnableCursor();
                        cursorCaptured = false;
                    }
                    var deathAction = Screens.DrawDeath(session);
                    if (deathAction == Screens.DeathAction.Respawn) {
                        session.RespawnPlayer();
                    } else if (deathAction == Screens.DeathAction.MainMenu) {
                        session.SaveTo(SaveSystem.SavePath);
                        session.Ui = UiState.Playing;
                        session = null;
                        renderer = null;
                    }
                    break;
                case UiState.Inventory:
                    if (cursorCaptured) {
                        Raylib.EnableCursor();
                        cursorCaptured = false;
                    }
                    Screens.DrawInventory(session);
                    break;
                case UiState.Crafting:
                    if (cursorCaptured) {
                        Raylib.EnableCursor();
                        cursorCaptured = false;
                    }
                    Screens.DrawCrafting(session);
                    break;
                case UiState.Workbench:
                    if (cursorCaptured) {
                        Raylib.EnableCursor();
                        cursorCaptured = false;
                    }
                    Screens.DrawWorkbench(session);
                    break;
                case UiState.Furnace:
                    if (cursorCaptured) {
                        Raylib.EnableCursor();
                        cursorCaptured = false;
                    }
                    Screens.DrawFurnaceUI(session);
                    break;
                case UiState.Chest:
                    if (cursorCaptured) {
                        Raylib.EnableCursor();
                        cursorCaptured = false;
                    }
                    Screens.DrawChestUI(session);
                    break;
            }
            Raylib.EndDrawing();

            frames++;
            if (autoshotFile != null && frames >= autoshotFrames) {
                Raylib.TakeScreenshot(autoshotFile);
                break;
            }
        }

        // Не вызываем Dispose() на renderer/world — это вызывает зависание
        // OpenGL при завершении. Ресурсы освобождаются ОС при закрытии процесса.
        SaveSystem.SaveSettings();
        SoundSystem.Shutdown();
        TextureAtlas.Unload();
        Fonts.Unload();
        Raylib.CloseWindow();
        return 0;
    }

    private static PlayerInput ReadInput(bool cursorCaptured, float pauseDebounce) {
        var input = new PlayerInput {
            MoveX = (Raylib.IsKeyDown(KeyBinds.Right) ? 1f : 0f) - (Raylib.IsKeyDown(KeyBinds.Left) ? 1f : 0f),
            MoveZ = (Raylib.IsKeyDown(KeyBinds.Forward) ? 1f : 0f) - (Raylib.IsKeyDown(KeyBinds.Backward) ? 1f : 0f),
            Jump = Raylib.IsKeyDown(KeyBinds.Jump),
            Crouch = Raylib.IsKeyDown(KeyBinds.Crouch),
            Sprint = Raylib.IsKeyDown(KeyBinds.Sprint) || Raylib.IsKeyDown(KeyboardKey.LeftControl),
            Drop = Raylib.IsKeyPressed(KeyBinds.Drop),
            AttackHeld = Raylib.IsMouseButtonDown(MouseButton.Left),
            UsePressed = Raylib.IsMouseButtonPressed(MouseButton.Right),
            UseHeld = Raylib.IsMouseButtonDown(MouseButton.Right),
            OpenInventory = Raylib.IsKeyPressed(KeyBinds.Inventory),
            OpenCrafting = Raylib.IsKeyPressed(KeyBinds.Crafting),
            Pause = pauseDebounce <= 0f && Raylib.IsKeyPressed(KeyBinds.Pause),
            Scroll = (int)Raylib.GetMouseWheelMove(),
            HotbarSlot = HotbarKey(),
        };
        if (cursorCaptured) {
            var delta = Raylib.GetMouseDelta();
            input.MouseDX = delta.X;
            input.MouseDY = delta.Y;
        }
        return input;
    }

    private static int HotbarKey() {
        for (int k = 0; k < 9; k++)
            if (Raylib.IsKeyPressed(KeyboardKey.One + k)) return k;
        return -1;
    }

    /// <summary>Связывание блоков и предметов с тайлами атласа.</summary>
    public static void RegisterTiles() {
        TextureAtlas.SetBlockTiles(GameData.BGrass.Id, TextureAtlas.TGrassTop, TextureAtlas.TGrassSide, TextureAtlas.TDirt);
        TextureAtlas.SetBlockTiles(GameData.BDirt.Id, TextureAtlas.TDirt, TextureAtlas.TDirt, TextureAtlas.TDirt);
        TextureAtlas.SetBlockTiles(GameData.BStone.Id, TextureAtlas.TStone, TextureAtlas.TStone, TextureAtlas.TStone);
        TextureAtlas.SetBlockTiles(GameData.BLog.Id, TextureAtlas.TLogTop, TextureAtlas.TLogSide, TextureAtlas.TLogTop);
        TextureAtlas.SetBlockTiles(GameData.BLeaves.Id, TextureAtlas.TLeaves, TextureAtlas.TLeaves, TextureAtlas.TLeaves);
        TextureAtlas.SetBlockTiles(GameData.BPlanks.Id, TextureAtlas.TPlanks, TextureAtlas.TPlanks, TextureAtlas.TPlanks);
        TextureAtlas.SetBlockTiles(GameData.BCoalOre.Id, TextureAtlas.TCoalOre, TextureAtlas.TCoalOre, TextureAtlas.TCoalOre);
        TextureAtlas.SetBlockTiles(GameData.BBedrock.Id, TextureAtlas.TBedrock, TextureAtlas.TBedrock, TextureAtlas.TBedrock);
        TextureAtlas.SetBlockTiles(GameData.BIronOre.Id,  TextureAtlas.TIronOre,    TextureAtlas.TIronOre,    TextureAtlas.TIronOre);
        TextureAtlas.SetBlockTiles(GameData.BGrass.Id, TextureAtlas.TGrassTop, TextureAtlas.TGrassSide, TextureAtlas.TDirt);
        TextureAtlas.SetBlockTiles(GameData.BDirt.Id, TextureAtlas.TDirt, TextureAtlas.TDirt, TextureAtlas.TDirt);
        TextureAtlas.SetBlockTiles(GameData.BStone.Id, TextureAtlas.TStone, TextureAtlas.TStone, TextureAtlas.TStone);
        TextureAtlas.SetBlockTiles(GameData.BLog.Id, TextureAtlas.TLogTop, TextureAtlas.TLogSide, TextureAtlas.TLogTop);
        TextureAtlas.SetBlockTiles(GameData.BLeaves.Id, TextureAtlas.TLeaves, TextureAtlas.TLeaves, TextureAtlas.TLeaves);
        TextureAtlas.SetBlockTiles(GameData.BPlanks.Id, TextureAtlas.TPlanks, TextureAtlas.TPlanks, TextureAtlas.TPlanks);
        TextureAtlas.SetBlockTiles(GameData.BCoalOre.Id, TextureAtlas.TCoalOre, TextureAtlas.TCoalOre, TextureAtlas.TCoalOre);
        TextureAtlas.SetBlockTiles(GameData.BTorch.Id, TextureAtlas.TTorch, TextureAtlas.TTorch, TextureAtlas.TTorch);
        TextureAtlas.SetBlockTiles(GameData.BBedrock.Id, TextureAtlas.TBedrock, TextureAtlas.TBedrock, TextureAtlas.TBedrock);
        TextureAtlas.SetBlockTiles(GameData.BIronOre.Id, TextureAtlas.TIronOre, TextureAtlas.TIronOre, TextureAtlas.TIronOre);
        TextureAtlas.SetBlockFaces(GameData.BWorkbench.Id, TextureAtlas.TLogSide, TextureAtlas.TLogSide, TextureAtlas.TWorkbench, TextureAtlas.TPlanks, TextureAtlas.TLogSide, TextureAtlas.TLogSide);
        TextureAtlas.SetBlockFaces(GameData.BFurnace.Id, TextureAtlas.TStone, TextureAtlas.TStone, TextureAtlas.TStone, TextureAtlas.TStone, TextureAtlas.TFurnace, TextureAtlas.TStone);
        TextureAtlas.SetBlockTiles(GameData.BCobblestone.Id, TextureAtlas.TCobblestone, TextureAtlas.TCobblestone, TextureAtlas.TCobblestone);
        TextureAtlas.SetBlockTiles(GameData.BSand.Id, TextureAtlas.TSand, TextureAtlas.TSand, TextureAtlas.TSand);
        TextureAtlas.SetBlockTiles(GameData.BGravel.Id, TextureAtlas.TGravel, TextureAtlas.TGravel, TextureAtlas.TGravel);
        TextureAtlas.SetBlockTiles(GameData.BGlass.Id, TextureAtlas.TGlass, TextureAtlas.TGlass, TextureAtlas.TGlass);
        TextureAtlas.SetBlockTiles(GameData.BWater.Id, TextureAtlas.TWater, TextureAtlas.TWater, TextureAtlas.TWater);
        TextureAtlas.SetBlockTiles(GameData.BLava.Id, TextureAtlas.TLava, TextureAtlas.TLava, TextureAtlas.TLava);
        TextureAtlas.SetBlockTiles(GameData.BGoldOre.Id, TextureAtlas.TGoldOre, TextureAtlas.TGoldOre, TextureAtlas.TGoldOre);
        TextureAtlas.SetBlockTiles(GameData.BDiamondOre.Id, TextureAtlas.TDiamondOre, TextureAtlas.TDiamondOre, TextureAtlas.TDiamondOre);
        TextureAtlas.SetBlockTiles(GameData.BRedstoneOre.Id, TextureAtlas.TRedstoneOre, TextureAtlas.TRedstoneOre, TextureAtlas.TRedstoneOre);
        TextureAtlas.SetBlockTiles(GameData.BObsidian.Id, TextureAtlas.TObsidian, TextureAtlas.TObsidian, TextureAtlas.TObsidian);

        TextureAtlas.SetItemTile(GameData.DirtItem.Id, TextureAtlas.TDirt);
        TextureAtlas.SetItemTile(GameData.StoneItem.Id, TextureAtlas.TStone);
        TextureAtlas.SetItemTile(GameData.LogItem.Id, TextureAtlas.TLogSide);
        TextureAtlas.SetItemTile(GameData.PlankItem.Id, TextureAtlas.TPlanks);
        TextureAtlas.SetItemTile(GameData.StickItem.Id, TextureAtlas.TSticks);
        TextureAtlas.SetItemTile(GameData.CoalItem.Id, TextureAtlas.TCoal);
        TextureAtlas.SetItemTile(GameData.CoalOreItem.Id, TextureAtlas.TCoalOre);
        TextureAtlas.SetItemTile(GameData.TorchItem.Id, TextureAtlas.TTorch);
        TextureAtlas.SetItemTile(GameData.AppleItem.Id, TextureAtlas.TApple);
        TextureAtlas.SetItemTile(GameData.RawPorkItem.Id, TextureAtlas.TPorkRaw);
        TextureAtlas.SetItemTile(GameData.CookedPorkItem.Id, TextureAtlas.TPorkCooked);
        TextureAtlas.SetItemTile(GameData.IronOreItem.Id, TextureAtlas.TIronOre);
        TextureAtlas.SetItemTile(GameData.IronIngotItem.Id, TextureAtlas.TIronIngot);
        TextureAtlas.SetItemTile(GameData.WorkbenchItem.Id, TextureAtlas.TWorkbench);
        TextureAtlas.SetItemTile(GameData.FurnaceItem.Id, TextureAtlas.TFurnace);
        TextureAtlas.SetItemTile(GameData.BreadItem.Id, TextureAtlas.TBread);
        TextureAtlas.SetItemTile(GameData.GoldOreItem.Id, TextureAtlas.TGoldOre);
        TextureAtlas.SetItemTile(GameData.GoldIngotItem.Id, TextureAtlas.TGoldIngot);
        TextureAtlas.SetItemTile(GameData.DiamondItem.Id, TextureAtlas.TDiamond);
        TextureAtlas.SetItemTile(GameData.DiamondOreItem.Id, TextureAtlas.TDiamondOre);
        TextureAtlas.SetItemTile(GameData.RedstoneItem.Id, TextureAtlas.TRedstoneDust);
        TextureAtlas.SetItemTile(GameData.SandItem.Id, TextureAtlas.TSand);
        TextureAtlas.SetItemTile(GameData.GravelItem.Id, TextureAtlas.TGravel);
        TextureAtlas.SetItemTile(GameData.CobblestoneItem.Id, TextureAtlas.TCobblestone);
        TextureAtlas.SetItemTile(GameData.GlassItem.Id, TextureAtlas.TGlass);
        TextureAtlas.SetItemTile(GameData.ObsidianItem.Id, TextureAtlas.TObsidian);

        // 16 отдельныx картинок под ВСЕ инструменты из 4 материалов!
        TextureAtlas.SetItemTile(GameData.WoodPickaxeItem.Id, TextureAtlas.TPickaxeWood);
        TextureAtlas.SetItemTile(GameData.StonePickaxeItem.Id, TextureAtlas.TPickaxeStone);
        TextureAtlas.SetItemTile(GameData.IronPickaxeItem.Id, TextureAtlas.TPickaxeIron);
        TextureAtlas.SetItemTile(GameData.DiamondPickaxeItem.Id, TextureAtlas.TPickaxeDiamond);

        TextureAtlas.SetItemTile(GameData.WoodAxeItem.Id, TextureAtlas.TAxeWood);
        TextureAtlas.SetItemTile(GameData.StoneAxeItem.Id, TextureAtlas.TAxeStone);
        TextureAtlas.SetItemTile(GameData.IronAxeItem.Id, TextureAtlas.TAxeIron);
        TextureAtlas.SetItemTile(GameData.DiamondAxeItem.Id, TextureAtlas.TAxeDiamond);

        TextureAtlas.SetItemTile(GameData.WoodSwordItem.Id, TextureAtlas.TSwordWood);
        TextureAtlas.SetItemTile(GameData.StoneSwordItem.Id, TextureAtlas.TSwordStone);
        TextureAtlas.SetItemTile(GameData.IronSwordItem.Id, TextureAtlas.TSwordIron);
        TextureAtlas.SetItemTile(GameData.DiamondSwordItem.Id, TextureAtlas.TSwordDiamond);

        TextureAtlas.SetItemTile(GameData.WoodShovelItem.Id, TextureAtlas.TShovelWood);
        TextureAtlas.SetItemTile(GameData.StoneShovelItem.Id, TextureAtlas.TShovelStone);
        TextureAtlas.SetItemTile(GameData.IronShovelItem.Id, TextureAtlas.TShovelIron);
        TextureAtlas.SetItemTile(GameData.DiamondShovelItem.Id, TextureAtlas.TShovelDiamond);

        // Дроп мобов и новые предметы
        TextureAtlas.SetItemTile(GameData.FeatherItem.Id, TextureAtlas.TFeather);
        TextureAtlas.SetItemTile(GameData.GunpowderItem.Id, TextureAtlas.TGunpowder);
        TextureAtlas.SetItemTile(GameData.StringItem.Id, TextureAtlas.TString);
        TextureAtlas.SetItemTile(GameData.ArrowItem.Id, TextureAtlas.TArrow);
        TextureAtlas.SetItemTile(GameData.BoneItem.Id, TextureAtlas.TBone);
        TextureAtlas.SetItemTile(GameData.CharcoalItem.Id, TextureAtlas.TCharcoal);
        TextureAtlas.SetBlockFaces(GameData.BChest.Id, TextureAtlas.TChestSide, TextureAtlas.TChestSide, TextureAtlas.TChestTop, TextureAtlas.TChestTop, TextureAtlas.TChestFront, TextureAtlas.TChestSide);
        TextureAtlas.SetBlockFaces(GameData.BBed.Id, TextureAtlas.TBedSide, TextureAtlas.TBedSide, TextureAtlas.TBedTop, TextureAtlas.TPlanks, TextureAtlas.TBedEnd, TextureAtlas.TBedEnd);
        TextureAtlas.SetBlockFaces(GameData.BBedHead.Id, TextureAtlas.TBedSide, TextureAtlas.TBedSide, TextureAtlas.TBedTop, TextureAtlas.TPlanks, TextureAtlas.TBedEnd, TextureAtlas.TBedEnd);

        TextureAtlas.SetItemTile(GameData.RawBeefItem.Id, TextureAtlas.TRawBeef);
        TextureAtlas.SetItemTile(GameData.CookedBeefItem.Id, TextureAtlas.TCookedBeef);
        TextureAtlas.SetItemTile(GameData.LeatherItem.Id, TextureAtlas.TLeather);
        TextureAtlas.SetItemTile(GameData.WhiteWoolItem.Id, TextureAtlas.TWool);
        TextureAtlas.SetItemTile(GameData.ChestItem.Id, TextureAtlas.TChestFront);
        TextureAtlas.SetItemTile(GameData.BedItem.Id, TextureAtlas.TBedTop);
        TextureAtlas.SetItemTile(GameData.RottenFleshItem.Id, TextureAtlas.TRottenFlesh);
    }
}
