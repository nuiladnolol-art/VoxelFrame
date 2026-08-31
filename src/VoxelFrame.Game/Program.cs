using Raylib_cs;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

internal static class Program {
    public const int WindowW = 1280, WindowH = 720;
    public static string PlayerName = "Player";

    // Кольцевой буфер диагностики в памяти: кадр больше НЕ пишет на диск
    // (раньше было ~11 синхронных записей файла за кадр — источник статтеров).
    // Файл пишется один раз — при падении или аварийном исключении.
    private static readonly string[] _diagRing = new string[256];
    private static int _diagCount;

    private static void CrashDiag(string tag) {
        _diagRing[_diagCount++ % _diagRing.Length] = tag;
    }

    private static void FlushCrashLog(Exception? crash = null) {
        try {
            if (_diagCount == 0 && crash == null) return;
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VoxelFrame");
            Directory.CreateDirectory(dir);
            using var w = new StreamWriter(Path.Combine(dir, "crash_diag.txt"), append: true);
            w.WriteLine($"---- {DateTime.Now:yyyy-MM-dd HH:mm:ss} ----");
            if (crash != null) w.WriteLine(crash.ToString());
            int total = Math.Min(_diagCount, _diagRing.Length);
            int start = _diagCount <= _diagRing.Length ? 0 : _diagCount % _diagRing.Length;
            for (int i = 0; i < total; i++) w.WriteLine(_diagRing[(start + i) % _diagRing.Length]);
            _diagCount = 0;
        } catch { }
    }

    /// <summary>
    /// F2: скриншот в %AppData%/VoxelFrame/screenshots/ с тостом в HUD.
    /// </summary>
    private static void TakeGameScreenshot(GameSession session) {
        try {
            string dir = Path.Combine(SaveSystem.SaveDirectory, "..", "screenshots");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"screenshot_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.png");
            Raylib.TakeScreenshot(file);
            session.AddMessage("Скриншот сохранён: " + Path.GetFileName(file));
        } catch (Exception ex) {
            session.AddMessage("Не удалось сохранить скриншот: " + ex.Message);
        }
    }

    private static int Main(string[] args) {
        if (args.Contains("--smoke")) return SmokeTest.Run();
        if (args.Contains("--export-textures")) {
            // Генерирует недостающие файлы текстур в assets/ (для тайлов без файла).
            TextureAtlas.GenerateDefaultTextures(forceOverwrite: false);
            return 0;
        }
        AppDomain.CurrentDomain.UnhandledException += (_, e) => FlushCrashLog(e.ExceptionObject as Exception);

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
        if (!string.IsNullOrEmpty(PlayerName) && PlayerName != "Player") {
            Screens.PlayerNick = PlayerName;
        } else if (!string.IsNullOrEmpty(Screens.PlayerNick)) {
            PlayerName = Screens.PlayerNick;
        }
        SkinSystem.Initialize();
        SoundSystem.Initialize();
        TextureAtlas.Load();
        Fonts.Load();
        RegisterTiles();

        GameSession? session = null;
        WorldRenderer? renderer = null;
        int frames = 0;
        bool cursorCaptured = false;
        float pauseDebounce = 0f;
        float clientInvSyncTimer = 0f;

        if (autoshotFile != null) {
            session = GameSession.NewGame(12345, headless: false);
            renderer = new WorldRenderer(session);
        }

        while (!Raylib.WindowShouldClose()) {
            float dt = MathF.Min(Raylib.GetFrameTime(), 0.1f);
            SoundSystem.UpdateMusic(dt);
            if (pauseDebounce > 0f) pauseDebounce -= dt;

            if (session == null) {
                if (cursorCaptured) {
                    Raylib.EnableCursor();
                    cursorCaptured = false;
                }
                Raylib.BeginDrawing();
                MenuAction action = MenuAction.None;
                Ui.Begin();
                if (Screens.InMultiplayerScreen) {
                    Screens.DrawMultiplayer(ref session);
                    Ui.End();
                    Raylib.EndDrawing();
                    if (session != null) {
                        renderer = new WorldRenderer(session);
                        session.Ui = UiState.Loading;
                        session.LoadTotal = 32;
                        session.LoadDone = 0;
                    }
                    continue;
                } else if (Screens.InSettingsScreen) {
                    if (Screens.InGraphicsScreen) Screens.DrawGraphics();
                    else if (Screens.InAudioScreen) Screens.DrawAudio();
                    else if (Screens.InControlsScreen) Screens.DrawControls();
                    else Screens.DrawSettings();
                    Ui.End();
                    Raylib.EndDrawing();
                    if (Raylib.IsKeyPressed(KeyBinds.Pause)) {
                        if (Screens.InGraphicsScreen) { Screens.InGraphicsScreen = false; SaveSystem.SaveSettings(); }
                        else if (Screens.InAudioScreen) { Screens.InAudioScreen = false; SaveSystem.SaveSettings(); }
                        else if (Screens.InControlsScreen) { Screens.InControlsScreen = false; SaveSystem.SaveSettings(); }
                        else Screens.InSettingsScreen = false;
                    }
                } else {
                    action = Screens.DrawMenu(dt);
                    Ui.End();
                    Raylib.EndDrawing();
                }

                if (action == MenuAction.NewGame) {
                    Screens.Reset();
                    int seed = Screens.CustomWorldSeed != 0 ? Screens.CustomWorldSeed : new Random().Next();
                    session = GameSession.NewGame(seed, headless: false);
                    renderer = new WorldRenderer(session);
                    session.Ui = UiState.Loading;
                    session.LoadTotal = 32;
                    session.LoadDone = 0;
                } else if (action == MenuAction.Continue) {
                    try {
                        Screens.Reset();
                        var (loaded, fromBackup) = SaveSystem.LoadWithRecovery(SaveSystem.SavePath, headless: false);
                        session = loaded;
                        renderer = new WorldRenderer(session);
                        session.Ui = UiState.Loading;
                        session.LoadTotal = Math.Min(32, Math.Max(1, session.World.Chunks.Count));
                        session.LoadDone = 0;
                        if (fromBackup) {
                            session.AddMessage("Основной сейв был повреждён — мир восстановлен из резервной копии.");
                        }
                    } catch (Exception ex) {
                        Screens.MenuError = $"Не удалось загрузить: {ex.Message}";
                        session = null;
                    }
                } else if (action == MenuAction.Exit) {
                    break;
                }
                continue;
            }

            // Если соединение клиента с сервером было разорвано (хост закрыл сервер или упал)
            if (GameClient.Active is { WasDisconnected: true }) {
                if (cursorCaptured) {
                    Raylib.EnableCursor();
                    cursorCaptured = false;
                }
                Raylib.BeginDrawing();
                Raylib.ClearBackground(new Color(18, 20, 28, 255));
                Ui.Begin();
                int dw = Ui.Vw, dh = Ui.Vh;
                Fonts.DrawCentered("СОЕДИНЕНИЕ С СЕРВЕРОМ ПОТЕРЯНО", dw / 2f, dh * 0.35f, 26f, new Color(240, 80, 80, 255));
                Fonts.DrawCentered("Хост закрыл сервер или произошел сбой сети", dw / 2f, dh * 0.43f, 15f, new Color(180, 185, 200, 255));
                var btnRec = new Rectangle(dw / 2f - 130f, dh * 0.55f, 260f, 44f);
                var mouse = Ui.Mouse();
                bool hov = Raylib.CheckCollisionPointRec(mouse, btnRec);
                Raylib.DrawRectangleRounded(btnRec, 0.15f, 6, hov ? new Color(60, 75, 100, 255) : new Color(40, 48, 62, 220));
                Raylib.DrawRectangleRoundedLinesEx(btnRec, 0.15f, 6, 1.5f, hov ? new Color(120, 180, 255, 255) : new Color(70, 85, 110, 200));
                Fonts.DrawCentered("Главное меню", btnRec.X + btnRec.Width / 2f, btnRec.Y + 12f, 16f, Color.White);

                if (hov && Raylib.IsMouseButtonPressed(MouseButton.Left)) {
                    GameClient.Disconnect();
                    session = null;
                    renderer = null;
                }
                Ui.End();
                Raylib.EndDrawing();
                continue;
            }

            // Экран загрузки: рисуем + строим первые меши видимых чанков
            if (session.Ui == UiState.Loading) {
                renderer!.ProcessMeshQueue();
                session.LoadDone = Math.Min(session.LoadDone + 4, session.LoadTotal);
                if (!PostProcessing.BeginScene(session)) {
                    Raylib.BeginDrawing();
                }
                Raylib.ClearBackground(new Color(10, 12, 20, 255));
                Ui.Begin();
                Screens.DrawLoading(session);
                Ui.End();
                PostProcessing.EndScene();
                Raylib.EndDrawing();
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
                if (!PostProcessing.BeginScene(session)) {
                    Raylib.BeginDrawing();
                }

                if (Screens.InSettingsScreen) {
                    Ui.Begin();
                    if (Screens.InGraphicsScreen) Screens.DrawGraphics();
                    else if (Screens.InAudioScreen) Screens.DrawAudio();
                    else if (Screens.InControlsScreen) Screens.DrawControls();
                    else Screens.DrawSettings();
                    Ui.End();
                    PostProcessing.EndScene();
                    Raylib.EndDrawing();
                    if (pauseDebounce <= 0f && Raylib.IsKeyPressed(KeyBinds.Pause)) {
                        pauseDebounce = 0.25f;
                        if (Screens.InGraphicsScreen) { Screens.InGraphicsScreen = false; SaveSystem.SaveSettings(); }
                        else if (Screens.InAudioScreen) { Screens.InAudioScreen = false; SaveSystem.SaveSettings(); }
                        else if (Screens.InControlsScreen) { Screens.InControlsScreen = false; SaveSystem.SaveSettings(); }
                        else Screens.InSettingsScreen = false;
                    }
                    continue;
                }

                if (Screens.InOpenToLanScreen) {
                    if (GameClient.Active != null) {
                        Screens.InOpenToLanScreen = false;
                    } else {
                        Ui.Begin();
                        Screens.DrawOpenToLan(session);
                        Ui.End();
                        PostProcessing.EndScene();
                        Raylib.EndDrawing();
                        if (pauseDebounce <= 0f && Raylib.IsKeyPressed(KeyBinds.Pause)) {
                            pauseDebounce = 0.25f;
                            Screens.InOpenToLanScreen = false;
                        }
                        continue;
                    }
                }

                // В сетевой игре мир и другие игроки продолжают обновляться в реальном времени даже в меню паузы
                if (GameClient.Active != null || GameServer.Active != null) {
                    session.Tick(dt, PlayerInput.Idle);
                    GameClient.Active?.UpdateRemotePlayers(dt);
                }
                
                Raylib.ClearBackground(new Color(10, 12, 20, 255));
                renderer!.ProcessMeshQueue();
                renderer.DrawSky();
                Raylib.BeginMode3D(session.Camera);
                renderer.Draw3DSky(session.Camera);
                renderer.DrawWorldOpaque();
                renderer.DrawEntities(session.Camera);
                renderer.DrawDecorations(dt);
                renderer.DrawPickups(session.Camera);
                renderer.DrawWorldTranslucent();
                Raylib.EndMode3D();

                // Пост-обработка до паузы-UI
                PostProcessing.EndScene();

                Ui.Begin();
                var pauseAction = Screens.DrawPause(session);
                Ui.End();
                Raylib.EndDrawing();

                if ((pauseDebounce <= 0f && Raylib.IsKeyPressed(KeyBinds.Pause)) || pauseAction == PauseAction.Resume) {
                    session.Ui = UiState.Playing;
                    pauseDebounce = 0.25f;
                } else if (pauseAction == PauseAction.OpenToLan) {
                    if (GameClient.Active == null) {
                        Screens.InOpenToLanScreen = true;
                        pauseDebounce = 0.25f;
                    }
                } else if (pauseAction == PauseAction.Settings) {
                    Screens.InSettingsScreen = true;
                    Screens.SettingsOpenedFromGame = true;
                    pauseDebounce = 0.25f;
                } else if (pauseAction == PauseAction.SaveAndExit) {
                    SoundSystem.StopTotem();
                    SoundSystem.StopDisc();
                    bool wasClient = GameClient.Active != null;
                    GameClient.Disconnect(session.Player);
                    GameServer.Stop();
                    if (!wasClient) {
                        session.SaveTo(SaveSystem.SavePath);
                    }
                    session.Ui = UiState.Playing;
                    session = null;
                    renderer = null;
                }
                continue;
            }

            if (Raylib.IsKeyPressed(KeyBinds.ToggleDebug)) {
                Hud.ShowDebugInfo = !Hud.ShowDebugInfo;
            }

            var input = ReadInput(session.Ui, pauseDebounce);
            if (input.Pause) pauseDebounce = 0.25f;
            CrashDiag("01_pre_tick");
            session.Tick(dt, input);
            CrashDiag("02_post_tick");

            // Сетевой обмен: обновление позиций игроков и отправка локального состояния
            GameClient.Active?.UpdateRemotePlayers(dt);
            GameServer.Active?.Update(dt);
            if (GameClient.Active != null) {
                GameClient.Active.SendMovement(session.Player.Position, session.Player.Yaw, session.Player.Pitch, session.Player.IsMoving, session.Player.IsCrouching, session.Player.IsFlying, session.Player.Health);
                clientInvSyncTimer += dt;
                if (clientInvSyncTimer >= 2.0f) {
                    clientInvSyncTimer = 0f;
                    GameClient.Active.SendInventoryUpdate(session.Player);
                }
            }
            if (GameServer.Active != null) {
                GameServer.Active.BroadcastHostMovement(session.Player.Position, session.Player.Yaw, session.Player.Pitch, session.Player.IsMoving, session.Player.IsCrouching, session.Player.IsFlying, session.Player.Health);
            }

            // Автосейв раз в AutosaveInterval секунд игрового времени.
            session.AutosaveTimer -= dt;
            if (session.AutosaveTimer <= 0f) {
                session.AutosaveTimer = GameSession.AutosaveInterval;
                try {
                    session.SaveTo(SaveSystem.SavePath);
                    GameServer.Active?.SaveAllPlayers();
                } catch { /* повторим на следующем тике */ }
            }

            if (!PostProcessing.BeginScene(session)) {
                Raylib.BeginDrawing();
            }
            Raylib.ClearBackground(new Color(10, 12, 20, 255));

            CrashDiag("03_pre_mesh");
            renderer!.ProcessMeshQueue();
            CrashDiag("04_post_mesh");
            renderer.DrawSky();
            Raylib.BeginMode3D(session.Camera);
            renderer.Draw3DSky(session.Camera);
            CrashDiag("05_pre_drawworld");
            renderer.DrawWorldOpaque();
            CrashDiag("06_post_drawworld");
            renderer.DrawEntities(session.Camera);
            CrashDiag("07_pre_decos");
            renderer.DrawDecorations(dt);
            CrashDiag("08_post_decos");
            renderer.DrawPickups(session.Camera);
            renderer.DrawWorldTranslucent();
            renderer.DrawClouds(session.Camera);
            renderer.DrawWeather(session.Camera);
            Raylib.EndMode3D();
            CrashDiag("09_post_endmode3d");

            // Пост-обработка: композит сцены с виньеткой/цветокором ДО отрисовки UI
            PostProcessing.EndScene();

            // Отрисовка 2D-никнеймов игроков над головами
            renderer.DrawRemotePlayerNameTags(session.Camera);

            // Защита от X-Ray (если камера внутри непрозрачного блока — черная заглушка)
            var camCell = new VoxelFrame.Core.Vec3i((int)MathF.Floor(session.Camera.Position.X), (int)MathF.Floor(session.Camera.Position.Y), (int)MathF.Floor(session.Camera.Position.Z));
            if (session.World.IsOpaqueAt(camCell)) {
                Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(16, 14, 13, 255));
            }

            // UI рисуется в виртуальных координатах (масштаб интерфейса)
            Ui.Begin();
            switch (session.Ui) {
                case UiState.Credits:
                    if (cursorCaptured) {
                        Raylib.EnableCursor();
                        cursorCaptured = false;
                    }
                    session.CreditsTimer -= dt;
                    Screens.DrawCredits(session);
                    if (session.CreditsTimer <= 0f || Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.Space) || Raylib.IsKeyPressed(KeyboardKey.Enter)) {
                        if (session.CreditsLeadToMenu) {
                            // Истинный финал: титры просмотрены — сохраняем и выходим в главное меню.
                            SoundSystem.StopTotem();
                            SoundSystem.StopDisc();
                            session.SaveTo(SaveSystem.SavePath);
                            session.Ui = UiState.Playing;
                            session = null;
                            renderer = null;
                        } else {
                            session.Ui = UiState.Playing;
                        }
                    }
                    break;
                case UiState.Playing:
                    if (Raylib.IsKeyPressed(KeyboardKey.T)) {
                        session.Ui = UiState.Chat;
                        session.ChatInput = "";
                        if (cursorCaptured) { Raylib.EnableCursor(); cursorCaptured = false; }
                    } else if (Raylib.IsKeyPressed(KeyboardKey.Slash)) {
                        session.Ui = UiState.Chat;
                        session.ChatInput = "/";
                        if (cursorCaptured) { Raylib.EnableCursor(); cursorCaptured = false; }
                    } else if (Raylib.IsKeyPressed(KeyboardKey.F2)) {
                        TakeGameScreenshot(session);
                    }

                    // Курсор прячем только при фокусе окна: иначе он «зависает»
                    // отдельно от прицела, а мышь не вращает камеру.
                    if (session.Ui == UiState.Playing) {
                        if (Raylib.IsWindowFocused() && !cursorCaptured) {
                            Raylib.DisableCursor();
                            cursorCaptured = true;
                        } else if (!Raylib.IsWindowFocused()) {
                            // Автопауза при потере фокуса (Alt-Tab)
                            session.Ui = UiState.Paused;
                            if (cursorCaptured) {
                                Raylib.EnableCursor();
                                cursorCaptured = false;
                            }
                        }
                    }
                    CrashDiag("10_pre_hud");
                    Hud.Draw(session, dt);
                    CrashDiag("11_post_hud");
                    break;
                case UiState.Chat:
                    if (cursorCaptured) {
                        Raylib.EnableCursor();
                        cursorCaptured = false;
                    }
                    int charCode = Raylib.GetCharPressed();
                    while (charCode > 0) {
                        if (charCode >= 32 && session.ChatInput.Length < 120) {
                            session.ChatInput += (char)charCode;
                        }
                        charCode = Raylib.GetCharPressed();
                    }
                    if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && session.ChatInput.Length > 0) {
                        session.ChatInput = session.ChatInput[..^1];
                    }
                    if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.KpEnter)) {
                        session.ExecuteChatCommand(session.ChatInput);
                        session.ChatInput = "";
                        session.Ui = UiState.Playing;
                    } else if (Raylib.IsKeyPressed(KeyboardKey.Escape)) {
                        session.ChatInput = "";
                        session.Ui = UiState.Playing;
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
                        SoundSystem.StopTotem();
                        SoundSystem.StopDisc();
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
            Ui.End();
            Raylib.EndDrawing();

            frames++;
            if (autoshotFile != null && frames >= autoshotFrames) {
                Raylib.TakeScreenshot(autoshotFile);
                break;
            }
        }

        // Сохранение при закрытии окна/выходе: текущее измерение (в т.ч. Энд) и
        // позиция игрока не теряются, даже если закрыть окно крестиком без паузы.
        if (session != null && session.World != null) {
            try { session.SaveTo(SaveSystem.SavePath); } catch { /* не критично */ }
        }

        // Освобождаем GPU-ресурсы ДО CloseWindow: контекст OpenGL ещё текущий,
        // удаление шейдеров и мешей корректно. Раньше Dispose пропускали из-за
        // зависания — ресурсы текли до смерти процесса.
        try { renderer?.Dispose(); renderer = null; } catch (Exception ex) { FlushCrashLog(ex); }
        try { session?.World?.Dispose(); session = null; } catch (Exception ex) { FlushCrashLog(ex); }
        SaveSystem.SaveSettings();
        SoundSystem.Shutdown();
        TextureAtlas.Unload();
        Fonts.Unload();
        PostProcessing.Unload();
        Raylib.CloseWindow();
        return 0;
    }

    private static float _lastForwardPressTime = -10f;
    private static bool _doubleTapSprint = false;

    private static PlayerInput ReadInput(UiState uiState, float pauseDebounce) {
        if (uiState == UiState.Chat) {
            return new PlayerInput();
        }

        if (uiState != UiState.Playing) {
            bool suppressInventoryKey = Screens.RecipeSearchActive || Screens.InMultiplayerScreen;
            return new PlayerInput {
                OpenInventory = !suppressInventoryKey && Raylib.IsKeyPressed(KeyBinds.Inventory),
                Pause = pauseDebounce <= 0f && Raylib.IsKeyPressed(KeyBinds.Pause) && !Screens.RecipeSearchActive,
            };
        }

        float now = (float)Raylib.GetTime();
        if (Raylib.IsKeyPressed(KeyBinds.Forward)) {
            if (now - _lastForwardPressTime < 0.28f) {
                _doubleTapSprint = true;
            }
            _lastForwardPressTime = now;
        }
        if (!Raylib.IsKeyDown(KeyBinds.Forward)) {
            _doubleTapSprint = false;
        }

        var input = new PlayerInput {
            MoveX = (Raylib.IsKeyDown(KeyBinds.Right) ? 1f : 0f) - (Raylib.IsKeyDown(KeyBinds.Left) ? 1f : 0f),
            MoveZ = (Raylib.IsKeyDown(KeyBinds.Forward) ? 1f : 0f) - (Raylib.IsKeyDown(KeyBinds.Backward) ? 1f : 0f),
            Jump = Raylib.IsKeyDown(KeyBinds.Jump),
            JumpPressed = Raylib.IsKeyPressed(KeyBinds.Jump),
            Crouch = Raylib.IsKeyDown(KeyBinds.Crouch),
            Sprint = Raylib.IsKeyDown(KeyBinds.Sprint) || Raylib.IsKeyDown(KeyboardKey.LeftControl) || _doubleTapSprint,
            Drop = Raylib.IsKeyPressed(KeyBinds.Drop),
            AttackHeld = Raylib.IsMouseButtonDown(MouseButton.Left),
            AttackPressed = Raylib.IsMouseButtonPressed(MouseButton.Left),
            UsePressed = Raylib.IsMouseButtonPressed(MouseButton.Right),
            UseHeld = Raylib.IsMouseButtonDown(MouseButton.Right),
            OpenInventory = Raylib.IsKeyPressed(KeyBinds.Inventory),
            Pause = pauseDebounce <= 0f && Raylib.IsKeyPressed(KeyBinds.Pause),
            Scroll = (int)Raylib.GetMouseWheelMove(),
            HotbarSlot = HotbarKey(),
        };
        var delta = Raylib.GetMouseDelta();
        float sens = SaveSystem.MouseSensitivity / 100f;
        input.MouseDX = delta.X * sens;
        input.MouseDY = delta.Y * sens;
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
        TextureAtlas.SetBlockTiles(GameData.BTorch.Id, TextureAtlas.TTorch, TextureAtlas.TTorch, TextureAtlas.TTorch);
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
        TextureAtlas.SetBlockTiles(GameData.BObsidian.Id, TextureAtlas.TObsidian, TextureAtlas.TObsidian, TextureAtlas.TObsidian);
        TextureAtlas.SetBlockTiles(GameData.BSapling.Id, TextureAtlas.TSapling, TextureAtlas.TSapling, TextureAtlas.TSapling);
        TextureAtlas.SetBlockTiles(GameData.BRedFlower.Id, TextureAtlas.TRedFlower, TextureAtlas.TRedFlower, TextureAtlas.TRedFlower);
        TextureAtlas.SetBlockTiles(GameData.BYellowFlower.Id, TextureAtlas.TYellowFlower, TextureAtlas.TYellowFlower, TextureAtlas.TYellowFlower);
        TextureAtlas.SetBlockTiles(GameData.BCarrotCrop.Id, TextureAtlas.TCarrotCrop0, TextureAtlas.TCarrotCrop0, TextureAtlas.TCarrotCrop0);
        TextureAtlas.SetBlockTiles(GameData.BPotatoCrop.Id, TextureAtlas.TPotatoCrop0, TextureAtlas.TPotatoCrop0, TextureAtlas.TPotatoCrop0);

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
        TextureAtlas.SetItemTile(GameData.OakSaplingItem.Id, TextureAtlas.TSapling);
        TextureAtlas.SetItemTile(GameData.RedFlowerItem.Id, TextureAtlas.TRedFlower);
        TextureAtlas.SetItemTile(GameData.YellowFlowerItem.Id, TextureAtlas.TYellowFlower);
        TextureAtlas.SetItemTile(GameData.CarrotItem.Id, TextureAtlas.TCarrot);
        TextureAtlas.SetItemTile(GameData.PotatoItem.Id, TextureAtlas.TPotato);
        TextureAtlas.SetItemTile(GameData.BakedPotatoItem.Id, TextureAtlas.TBakedPotato);
        TextureAtlas.SetItemTile(GameData.RawChickenItem.Id, TextureAtlas.TRawChicken);
        TextureAtlas.SetItemTile(GameData.CookedChickenItem.Id, TextureAtlas.TCookedChicken);
        TextureAtlas.SetItemTile(GameData.EggItem.Id, TextureAtlas.TEgg);
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
        TextureAtlas.SetItemTile(GameData.GoldPickaxeItem.Id, TextureAtlas.TPickaxeGold);
        TextureAtlas.SetItemTile(GameData.GoldAxeItem.Id, TextureAtlas.TAxeGold);
        TextureAtlas.SetItemTile(GameData.GoldSwordItem.Id, TextureAtlas.TSwordGold);
        TextureAtlas.SetItemTile(GameData.GoldShovelItem.Id, TextureAtlas.TShovelGold);
        TextureAtlas.SetItemTile(GameData.GoldHoeItem.Id, TextureAtlas.THoeGold);

        // Дроп мобов и новые предметы
        TextureAtlas.SetItemTile(GameData.FeatherItem.Id, TextureAtlas.TFeather);
        TextureAtlas.SetItemTile(GameData.GunpowderItem.Id, TextureAtlas.TGunpowder);
        TextureAtlas.SetItemTile(GameData.StringItem.Id, TextureAtlas.TString);
        TextureAtlas.SetItemTile(GameData.ArrowItem.Id, TextureAtlas.TArrow);
        TextureAtlas.SetItemTile(GameData.BoneItem.Id, TextureAtlas.TBone);
        TextureAtlas.SetItemTile(GameData.CharcoalItem.Id, TextureAtlas.TCharcoal);
        TextureAtlas.SetBlockFaces(GameData.BChest.Id, TextureAtlas.TChestSide, TextureAtlas.TChestSide, TextureAtlas.TChestTop, TextureAtlas.TChestTop, TextureAtlas.TChestFront, TextureAtlas.TChestSide);
        TextureAtlas.SetBlockFaces(GameData.BBed.Id, TextureAtlas.TBedSide, TextureAtlas.TBedSide, TextureAtlas.TBedFootTop, TextureAtlas.TPlanks, TextureAtlas.TBedEnd, TextureAtlas.TBedEnd);
        TextureAtlas.SetBlockFaces(GameData.BBedHead.Id, TextureAtlas.TBedSide, TextureAtlas.TBedSide, TextureAtlas.TBedHeadTop, TextureAtlas.TPlanks, TextureAtlas.TBedEnd, TextureAtlas.TBedEnd);

        TextureAtlas.SetItemTile(GameData.RawBeefItem.Id, TextureAtlas.TRawBeef);
        TextureAtlas.SetItemTile(GameData.CookedBeefItem.Id, TextureAtlas.TCookedBeef);
        TextureAtlas.SetItemTile(GameData.LeatherItem.Id, TextureAtlas.TLeather);
        TextureAtlas.SetItemTile(GameData.WhiteWoolItem.Id, TextureAtlas.TWool);
        TextureAtlas.SetItemTile(GameData.ChestItem.Id, TextureAtlas.TChestFront);
        TextureAtlas.SetItemTile(GameData.BedItem.Id, TextureAtlas.TBedHeadTop);
        TextureAtlas.SetItemTile(GameData.RottenFleshItem.Id, TextureAtlas.TRottenFlesh);
        TextureAtlas.SetItemTile(GameData.WheatItem.Id, TextureAtlas.TWheat);
        TextureAtlas.SetItemTile(GameData.WheatSeedsItem.Id, TextureAtlas.TWheatSeeds);
        TextureAtlas.SetItemTile(GameData.WoodHoeItem.Id, TextureAtlas.THoeWood);
        TextureAtlas.SetItemTile(GameData.StoneHoeItem.Id, TextureAtlas.THoeStone);
        TextureAtlas.SetItemTile(GameData.IronHoeItem.Id, TextureAtlas.THoeIron);
        TextureAtlas.SetItemTile(GameData.DiamondHoeItem.Id, TextureAtlas.THoeDiamond);
        TextureAtlas.SetItemTile(GameData.BoneMealItem.Id, TextureAtlas.TBoneMeal);
        TextureAtlas.SetItemTile(GameData.SawdustItem.Id, TextureAtlas.TSawdust);
        TextureAtlas.SetItemTile(GameData.SawdustPorridgeItem.Id, TextureAtlas.TSawdustPorridge);
        TextureAtlas.SetItemTile(GameData.TotemItem.Id, TextureAtlas.TTotem);
        TextureAtlas.SetItemTile(GameData.RawMuttonItem.Id, TextureAtlas.TRawMutton);
        TextureAtlas.SetItemTile(GameData.CookedMuttonItem.Id, TextureAtlas.TCookedMutton);
        TextureAtlas.SetItemTile(GameData.BowItem.Id, TextureAtlas.TBow);
        TextureAtlas.SetItemTile(GameData.ShieldItem.Id, TextureAtlas.TShield);
        TextureAtlas.SetItemTile(GameData.FlintItem.Id, TextureAtlas.TFlint);
        TextureAtlas.SetItemTile(GameData.FlintAndSteelItem.Id, TextureAtlas.TFlintAndSteel);
        TextureAtlas.SetItemTile(GameData.GoldenAppleItem.Id, TextureAtlas.TGoldenApple);
        TextureAtlas.SetItemTile(GameData.MusicDiscItem.Id, TextureAtlas.TMusicDisc);
        TextureAtlas.SetItemTile(GameData.NetherQuartzItem.Id, TextureAtlas.TNetherQuartz);
        TextureAtlas.SetItemTile(GameData.BlazeRodItem.Id, TextureAtlas.TBlazeRod);
        TextureAtlas.SetItemTile(GameData.GlowstoneDustItem.Id, TextureAtlas.TGlowstoneDust);
        TextureAtlas.SetItemTile(GameData.TNTItem.Id, TextureAtlas.TTNTSide);
        TextureAtlas.SetItemTile(GameData.NetherrackItem.Id, TextureAtlas.TNetherrack);
        TextureAtlas.SetItemTile(GameData.SoulSandItem.Id, TextureAtlas.TSoulSand);
        TextureAtlas.SetItemTile(GameData.GlowstoneItem.Id, TextureAtlas.TGlowstone);
        TextureAtlas.SetItemTile(GameData.NetherQuartzOreItem.Id, TextureAtlas.TNetherQuartzOre);
        TextureAtlas.SetItemTile(GameData.NetherBrickItem.Id, TextureAtlas.TNetherBrick);
        TextureAtlas.SetItemTile(GameData.DoorItem.Id, TextureAtlas.TDoorItem);
        TextureAtlas.SetBlockFaces(GameData.BDoorLower.Id, TextureAtlas.TDoorLower, TextureAtlas.TDoorLower, TextureAtlas.TPlanks, TextureAtlas.TPlanks, TextureAtlas.TDoorLower, TextureAtlas.TDoorLower);
        TextureAtlas.SetBlockFaces(GameData.BDoorUpper.Id, TextureAtlas.TDoorUpper, TextureAtlas.TDoorUpper, TextureAtlas.TPlanks, TextureAtlas.TPlanks, TextureAtlas.TDoorUpper, TextureAtlas.TDoorUpper);
        TextureAtlas.SetItemTile(GameData.MossyCobblestoneItem.Id, TextureAtlas.TMossyCobble);
        TextureAtlas.SetItemTile(GameData.ChiseledSandstoneItem.Id, TextureAtlas.TChiseledSandstone);
        TextureAtlas.SetItemTile(GameData.RailItem.Id, TextureAtlas.TRail);
        TextureAtlas.SetItemTile(GameData.BucketItem.Id, TextureAtlas.TBucket);
        TextureAtlas.SetItemTile(GameData.WaterBucketItem.Id, TextureAtlas.TWaterBucket);
        TextureAtlas.SetItemTile(GameData.LavaBucketItem.Id, TextureAtlas.TLavaBucket);
        TextureAtlas.SetItemTile(GameData.JukeboxItem.Id, TextureAtlas.TJukeboxSide);
        TextureAtlas.SetBlockFaces(GameData.BJukebox.Id, TextureAtlas.TJukeboxSide, TextureAtlas.TJukeboxSide, TextureAtlas.TJukeboxTop, TextureAtlas.TPlanks, TextureAtlas.TJukeboxSide, TextureAtlas.TJukeboxSide);

        TextureAtlas.SetBlockTiles(GameData.BFire.Id, TextureAtlas.TFire, TextureAtlas.TFire, TextureAtlas.TFire);
        TextureAtlas.SetBlockTiles(GameData.BFarmland.Id, TextureAtlas.TFarmland, TextureAtlas.TDirt, TextureAtlas.TDirt);
        TextureAtlas.SetBlockTiles(GameData.BTallGrass.Id, TextureAtlas.TTallGrass, TextureAtlas.TTallGrass, TextureAtlas.TTallGrass);
        TextureAtlas.SetBlockTiles(GameData.BMossyCobblestone.Id, TextureAtlas.TMossyCobble, TextureAtlas.TMossyCobble, TextureAtlas.TMossyCobble);
        TextureAtlas.SetBlockTiles(GameData.BMobSpawner.Id, TextureAtlas.TMobSpawner, TextureAtlas.TMobSpawner, TextureAtlas.TMobSpawner);
        TextureAtlas.SetBlockTiles(GameData.BWeb.Id, TextureAtlas.TWeb, TextureAtlas.TWeb, TextureAtlas.TWeb);
        TextureAtlas.SetBlockTiles(GameData.BRail.Id, TextureAtlas.TRail, TextureAtlas.TRail, TextureAtlas.TRail);
        TextureAtlas.SetBlockTiles(GameData.BPressurePlate.Id, TextureAtlas.TPressurePlate, TextureAtlas.TPressurePlate, TextureAtlas.TPressurePlate);
        TextureAtlas.SetBlockFaces(GameData.BTNT.Id, TextureAtlas.TTNTSide, TextureAtlas.TTNTSide, TextureAtlas.TTNTTop, TextureAtlas.TTNTBottom, TextureAtlas.TTNTSide, TextureAtlas.TTNTSide);
        TextureAtlas.SetBlockTiles(GameData.BChiseledSandstone.Id, TextureAtlas.TChiseledSandstone, TextureAtlas.TChiseledSandstone, TextureAtlas.TChiseledSandstone);
        TextureAtlas.SetBlockTiles(GameData.BNetherrack.Id, TextureAtlas.TNetherrack, TextureAtlas.TNetherrack, TextureAtlas.TNetherrack);
        TextureAtlas.SetBlockTiles(GameData.BSoulSand.Id, TextureAtlas.TSoulSand, TextureAtlas.TSoulSand, TextureAtlas.TSoulSand);
        TextureAtlas.SetBlockTiles(GameData.BGlowstone.Id, TextureAtlas.TGlowstone, TextureAtlas.TGlowstone, TextureAtlas.TGlowstone);
        TextureAtlas.SetBlockTiles(GameData.BNetherQuartzOre.Id, TextureAtlas.TNetherQuartzOre, TextureAtlas.TNetherQuartzOre, TextureAtlas.TNetherQuartzOre);
        TextureAtlas.SetBlockTiles(GameData.BNetherBrick.Id, TextureAtlas.TNetherBrick, TextureAtlas.TNetherBrick, TextureAtlas.TNetherBrick);
        TextureAtlas.SetBlockTiles(GameData.BNetherPortal.Id, TextureAtlas.TNetherPortal, TextureAtlas.TNetherPortal, TextureAtlas.TNetherPortal);

        // Энд: блоки и предметы
        TextureAtlas.SetBlockTiles(GameData.BEndStone.Id, TextureAtlas.TEndStone, TextureAtlas.TEndStone, TextureAtlas.TEndStone);
        TextureAtlas.SetBlockTiles(GameData.BEndPortalFrame.Id, TextureAtlas.TEndPortalFrame, TextureAtlas.TEndPortalFrame, TextureAtlas.TEndPortalFrame);
        TextureAtlas.SetBlockTiles(GameData.BEndPortal.Id, TextureAtlas.TEndPortal, TextureAtlas.TEndPortal, TextureAtlas.TEndPortal);
        TextureAtlas.SetBlockTiles(GameData.BObsidianPillar.Id, TextureAtlas.TObsidian, TextureAtlas.TObsidian, TextureAtlas.TObsidian);
        TextureAtlas.SetBlockTiles(GameData.BEnderCrystal.Id, TextureAtlas.TEnderCrystal, TextureAtlas.TEnderCrystal, TextureAtlas.TEnderCrystal);
        TextureAtlas.SetBlockTiles(GameData.BChorusPlant.Id, TextureAtlas.TChorusPlant, TextureAtlas.TChorusPlant, TextureAtlas.TChorusPlant);
        TextureAtlas.SetBlockTiles(GameData.BChorusFlower.Id, TextureAtlas.TChorusFlower, TextureAtlas.TChorusFlower, TextureAtlas.TChorusFlower);
        TextureAtlas.SetBlockTiles(GameData.BVoidGate.Id, TextureAtlas.TVoidGate, TextureAtlas.TVoidGate, TextureAtlas.TVoidGate);
        TextureAtlas.SetItemTile(GameData.EnderPearlItem.Id, TextureAtlas.TEnderPearl);
        TextureAtlas.SetItemTile(GameData.EyeOfEnderItem.Id, TextureAtlas.TEyeOfEnder);
        TextureAtlas.SetItemTile(GameData.BlazePowderItem.Id, TextureAtlas.TBlazePowder);
        TextureAtlas.SetItemTile(GameData.ChorusFruitItem.Id, TextureAtlas.TChorusFruit);
        TextureAtlas.SetItemTile(GameData.EndSlimeItem.Id, TextureAtlas.TEndSlime);
        TextureAtlas.SetItemTile(GameData.EndStoneItem.Id, TextureAtlas.TEndStone);
        TextureAtlas.SetItemTile(GameData.EndPortalFrameItem.Id, TextureAtlas.TEndPortalFrame);
        TextureAtlas.SetItemTile(GameData.EnderCrystalItem.Id, TextureAtlas.TEnderCrystal);
        TextureAtlas.SetItemTile(GameData.NetherArtifactItem.Id, TextureAtlas.TNetherArtifact);
        TextureAtlas.SetItemTile(GameData.SwampArtifactItem.Id, TextureAtlas.TSwampArtifact);
        TextureAtlas.SetItemTile(GameData.DesertArtifactItem.Id, TextureAtlas.TDesertArtifact);
        TextureAtlas.SetItemTile(GameData.VoidKeyItem.Id, TextureAtlas.TVoidKey);
        TextureAtlas.SetItemTile(GameData.NetherTotemItem.Id, TextureAtlas.TNetherArtifact);
        TextureAtlas.SetItemTile(GameData.SwampTotemItem.Id, TextureAtlas.TSwampArtifact);
        TextureAtlas.SetItemTile(GameData.DesertTotemItem.Id, TextureAtlas.TDesertArtifact);

        // Броня
        TextureAtlas.SetItemTile(GameData.LeatherHelmetItem.Id, TextureAtlas.TLeatherHelmet);
        TextureAtlas.SetItemTile(GameData.LeatherChestplateItem.Id, TextureAtlas.TLeatherChestplate);
        TextureAtlas.SetItemTile(GameData.LeatherLeggingsItem.Id, TextureAtlas.TLeatherLeggings);
        TextureAtlas.SetItemTile(GameData.LeatherBootsItem.Id, TextureAtlas.TLeatherBoots);

        TextureAtlas.SetItemTile(GameData.IronHelmetItem.Id, TextureAtlas.TIronHelmet);
        TextureAtlas.SetItemTile(GameData.IronChestplateItem.Id, TextureAtlas.TIronChestplate);
        TextureAtlas.SetItemTile(GameData.IronLeggingsItem.Id, TextureAtlas.TIronLeggings);
        TextureAtlas.SetItemTile(GameData.IronBootsItem.Id, TextureAtlas.TIronBoots);

        TextureAtlas.SetItemTile(GameData.DiamondHelmetItem.Id, TextureAtlas.TDiamondHelmet);
        TextureAtlas.SetItemTile(GameData.DiamondChestplateItem.Id, TextureAtlas.TDiamondChestplate);
        TextureAtlas.SetItemTile(GameData.DiamondLeggingsItem.Id, TextureAtlas.TDiamondLeggings);
        TextureAtlas.SetItemTile(GameData.DiamondBootsItem.Id, TextureAtlas.TDiamondBoots);
    }
}
