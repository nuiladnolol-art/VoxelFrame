using Voxalith.Core;
using System.Numerics;
using Raylib_cs;
using Voxalith.Core.Inventory;
using Voxalith.Core.Materials;

namespace Voxalith.Game;
public static partial class Screens {
    // ── Настройки и Управление ───────────────────────────────────────────────

    public static void DrawSettings() {
        int w = Ui.Vw, h = Ui.Vh;
        DrawMenuBackground(Raylib.GetFrameTime());

        Fonts.DrawTitle3D("НАСТРОЙКИ", w / 2f, h * 0.16f, 48f);

        float cy = h * 0.34f;
        float btnW = 300f;
        float btnH = 48f;
        float gapX = 20f;
        float gapY = 16f;

        float leftX = w / 2f - btnW - gapX / 2f;
        float rightX = w / 2f + gapX / 2f;

        if (Button(leftX, cy, btnW, btnH, "Настройки графики...", true)) {
            InGraphicsScreen = true;
        }
        if (Button(rightX, cy, btnW, btnH, "Настройки звука...", true)) {
            InAudioScreen = true;
        }

        if (Button(leftX, cy + btnH + gapY, btnW, btnH, "Управление...", true)) {
            InControlsScreen = true;
            ActiveRebindIndex = -1;
        }

        // Выбор цвета персонажа
        string skinText = $"Цвет игрока: {SkinSystem.CurrentSkin.DisplayName}";
        if (Button(rightX, cy + btnH + gapY, btnW, btnH, skinText, true)) {
            SkinSystem.NextSkin();
        }

        // Выбор темы интерфейса (Theme Engine)
        string themeText = $"Тема: {UiTheme.Current.Name}";
        if (Button(leftX, cy + (btnH + gapY) * 2, btnW, btnH, themeText, true)) {
            SaveSystem.SelectedThemeIndex = (SaveSystem.SelectedThemeIndex + 1) % UiTheme.AllThemes.Length;
            SaveSystem.SaveSettings();
        }

        // Масштаб интерфейса: авто по высоте окна или фиксированный процент
        string scaleText = SaveSystem.UiScaleMode switch {
            0 => "Масштаб: Авто",
            _ => $"Масштаб: {SaveSystem.UiScaleMode}%",
        };
        if (Button(rightX, cy + (btnH + gapY) * 2, btnW, btnH, scaleText, true)) {
            int[] steps = { 0, 75, 100, 125, 150, 200 };
            int idx = Array.IndexOf(steps, SaveSystem.UiScaleMode);
            SaveSystem.UiScaleMode = steps[(idx + 1) % steps.Length];
            SaveSystem.SaveSettings();
        }

        if (Button(w / 2f - btnW / 2f, cy + (btnH + gapY) * 3, btnW, btnH, "Готово", true) || Raylib.IsKeyPressed(KeyboardKey.Escape)) {
            InSettingsScreen = false;
        }
    }

    private static bool Slider(float x, float y, float w, float h, string text, float value, float min, float max, out float newValue) {
        newValue = value;
        var mouse = Ui.Mouse();
        var rect = new Rectangle(x, y, w, h);
        bool hovered = Raylib.CheckCollisionPointRec(mouse, rect);
        bool dragging = hovered && Raylib.IsMouseButtonDown(MouseButton.Left);

        if (dragging) {
            float ratio = Math.Clamp((mouse.X - x) / w, 0f, 1f);
            newValue = min + ratio * (max - min);
        }

        // Фон слайдера в классическом стиле светлых воксельных кнопок
        Color topColor = hovered ? new Color(175, 185, 205, 255) : new Color(145, 145, 145, 255);
        Color botColor = hovered ? new Color(145, 155, 175, 255) : new Color(115, 115, 115, 255);
        Raylib.DrawRectangleGradientV((int)x, (int)y, (int)w, (int)h, topColor, botColor);

        // Полоса заполнения прогресса
        float currentRatio = Math.Clamp((value - min) / (max - min), 0f, 1f);
        int fillW = (int)((w - 4f) * currentRatio);
        if (fillW > 0) {
            Raylib.DrawRectangleGradientV((int)x + 2, (int)y + 2, fillW, (int)h - 4, new Color(100, 150, 220, 160), new Color(70, 115, 180, 180));
        }

        // 3D-фаски
        Raylib.DrawRectangle((int)x, (int)y, (int)w, 2, new Color(255, 255, 255, hovered ? 180 : 120));
        Raylib.DrawRectangle((int)x, (int)y, 2, (int)h, new Color(255, 255, 255, hovered ? 180 : 120));
        Raylib.DrawRectangle((int)x, (int)(y + h - 2), (int)w, 2, new Color(45, 45, 45, 200));
        Raylib.DrawRectangle((int)(x + w - 2), (int)y, 2, (int)h, new Color(45, 45, 45, 200));
        Raylib.DrawRectangleLinesEx(rect, 1.5f, hovered ? new Color(255, 220, 80, 255) : new Color(55, 55, 55, 255));

        // Бегунок
        float thumbX = x + 2f + currentRatio * (w - 14f);
        var thumbRect = new Rectangle(thumbX, y + 2f, 10f, h - 4f);
        Raylib.DrawRectangleRec(thumbRect, hovered ? new Color(255, 235, 120, 255) : new Color(240, 240, 240, 255));
        Raylib.DrawRectangleLinesEx(thumbRect, 1f, new Color(40, 40, 40, 255));

        // Текст поверх слайдера
        Fonts.DrawCenteredShadowed(text, x + w / 2f, y + (h - 18f) / 2f, 18f, Color.White, 1.5f);

        return dragging;
    }

    public static void DrawGraphics() {
        int w = Ui.Vw, h = Ui.Vh;
        DrawMenuBackground(Raylib.GetFrameTime());

        Fonts.DrawTitle3D("НАСТРОЙКИ ГРАФИКИ", w / 2f, h * 0.10f, 44f);

        float colW = 340f;
        float rowH = 44f;
        float gapY = 12f;
        float startY = h * 0.18f;

        float leftX = w / 2f - colW - 10f;
        float rightX = w / 2f + 10f;

        // Левая колонка
        // 1. Графика (Пресет)
        string gfxPresetText = SaveSystem.GraphicsQuality switch {
            SaveSystem.GraphicsPreset.Fast => "Графика: Быстрая (Fast)",
            SaveSystem.GraphicsPreset.Fancy => "Графика: Красивая (Fancy)",
            SaveSystem.GraphicsPreset.Fabulous => "Графика: Ультра (Fabulous)",
            _ => "Графика: Красивая"
        };
        if (Button(leftX, startY, colW, rowH, gfxPresetText, true)) {
            SaveSystem.GraphicsQuality = (SaveSystem.GraphicsPreset)(((int)SaveSystem.GraphicsQuality + 1) % 3);
            bool oldSmooth = SaveSystem.SmoothLighting;
            if (SaveSystem.GraphicsQuality == SaveSystem.GraphicsPreset.Fast) {
                SaveSystem.CloudsMode = 1;
                SaveSystem.ParticlesMode = 0;
                SaveSystem.DynamicLighting = false;
                SaveSystem.EntityShadows = false;
                SaveSystem.SmoothLighting = false;
                SaveSystem.ShadowQuality = 0;
                SaveSystem.PostFxGodRays = false;
            } else {
                bool ultra = SaveSystem.GraphicsQuality == SaveSystem.GraphicsPreset.Fabulous;
                SaveSystem.CloudsMode = 2;
                SaveSystem.ParticlesMode = 2;
                SaveSystem.DynamicLighting = true;
                SaveSystem.EntityShadows = true;
                SaveSystem.SmoothLighting = true;
                SaveSystem.ShadowQuality = ultra ? 2 : 1;
                SaveSystem.PostFxGodRays = true;
            }
            if (oldSmooth != SaveSystem.SmoothLighting) WorldRenderer.RequestFullRemesh();
        }

        // 2. Облака
        string cloudsText = SaveSystem.CloudsMode switch {
            0 => "Облака: Выключены",
            1 => "Облака: Быстрые (2D)",
            _ => "Облака: Объёмные (3D)"
        };
        if (Button(leftX, startY + (rowH + gapY), colW, rowH, cloudsText, true)) {
            SaveSystem.CloudsMode = (SaveSystem.CloudsMode + 1) % 3;
        }

        // 3. Частицы
        string particlesText = SaveSystem.ParticlesMode switch {
            0 => "Частицы: Минимум (FPS+)",
            1 => "Частицы: Уменьшенные",
            _ => "Частицы: Все"
        };
        if (Button(leftX, startY + (rowH + gapY) * 2f, colW, rowH, particlesText, true)) {
            SaveSystem.ParticlesMode = (SaveSystem.ParticlesMode + 1) % 3;
        }

        // 4. Мягкое освещение (Smooth Lighting). Раньше кнопка переключала FancyGraphics,
        // т.е. сбрасывала весь пресет (Ультра → Быстрая), а сохранённый SmoothLighting
        // нигде не читался. Теперь — отдельная опция с пересборкой мешей.
        string smoothLightText = SaveSystem.SmoothLighting ? "Мягкий свет: Вкл" : "Мягкий свет: Выкл (Быстро)";
        if (Button(leftX, startY + (rowH + gapY) * 3f, colW, rowH, smoothLightText, true)) {
            SaveSystem.SmoothLighting = !SaveSystem.SmoothLighting;
            WorldRenderer.RequestFullRemesh();
        }

        // 5. Тени от солнца и луны
        string shadowText = SaveSystem.ShadowQuality switch {
            0 => "Тени: Выкл (FPS+)",
            1 => "Тени: Средние",
            _ => "Тени: Высокие"
        };
        if (Button(leftX, startY + (rowH + gapY) * 4f, colW, rowH, shadowText, true)) {
            SaveSystem.ShadowQuality = (SaveSystem.ShadowQuality + 1) % 3;
        }

        // Правая колонка
        // 1. Поле зрения (FOV)
        string fovText = $"Поле зрения (FOV): {SaveSystem.FovSetting}°";
        if (Slider(rightX, startY, colW, rowH, fovText, SaveSystem.FovSetting, 50f, 110f, out float newFov)) {
            SaveSystem.FovSetting = Math.Clamp((int)MathF.Round(newFov), 50, 110);
        }

        // 2. Динамическое освещение в руке
        string dynLightText = SaveSystem.DynamicLighting ? "Динамический свет: Вкл" : "Динамический свет: Выкл";
        if (Button(rightX, startY + (rowH + gapY), colW, rowH, dynLightText, true)) {
            SaveSystem.DynamicLighting = !SaveSystem.DynamicLighting;
        }

        // 3. Слайдер дальности прорисовки
        int rd = SaveSystem.RenderDistanceSetting;
        string chunkWord = (rd % 10 == 1 && rd % 100 != 11) ? "чанк" : (rd % 10 >= 2 && rd % 10 <= 4 && (rd % 100 < 10 || rd % 100 >= 20)) ? "чанка" : "чанков";
        string distText = $"Дальность: {rd} {chunkWord}";
        if (Slider(rightX, startY + (rowH + gapY) * 2f, colW, rowH, distText, SaveSystem.RenderDistanceSetting, 2f, 16f, out float newDist)) {
            SaveSystem.RenderDistanceSetting = Math.Clamp((int)MathF.Round(newDist), 2, 16);
        }

        // 4. Режим экрана
        bool isFs = Raylib.IsWindowState(ConfigFlags.UndecoratedWindow) || Raylib.IsWindowFullscreen();
        string fsText = isFs ? "Экран: Полноэкранный" : "Экран: Оконный";
        if (Button(rightX, startY + (rowH + gapY) * 3f, colW, rowH, fsText, true)) {
            Raylib.ToggleBorderlessWindowed();
        }

        // 5. Солнечные лучи (god rays)
        string raysText = SaveSystem.PostFxGodRays ? "Солнечные лучи: Вкл" : "Солнечные лучи: Выкл";
        if (Button(rightX, startY + (rowH + gapY) * 4f, colW, rowH, raysText, true)) {
            SaveSystem.PostFxGodRays = !SaveSystem.PostFxGodRays;
        }

        // 6. Кинематографичные эффекты (по центру на всю ширину)
        float centerBtnW = colW * 2f + 20f;
        float centerBtnX = w / 2f - centerBtnW / 2f;
        string postFxText = SaveSystem.PostFxMode == 0
            ? "Кино-эффекты: Выкл (FPS+)"
            : $"Кино-эффекты: Вкл (HDR, тонмаппинг, сглаживание{(SaveSystem.PostFxBloom ? ", bloom" : "")}{(SaveSystem.PostFxVignette ? ", виньетка" : "")})";
        if (Button(centerBtnX, startY + (rowH + gapY) * 5f, centerBtnW, rowH, postFxText, true)) {
            SaveSystem.PostFxMode = SaveSystem.PostFxMode == 0 ? 1 : 0;
        }

        // Кнопка Готово
        if (Button(w / 2f - 140f, h * 0.85f, 280f, 46f, "Готово", true)) {
            InGraphicsScreen = false;
            SaveSystem.SaveSettings();
        }
    }

    public static void DrawAudio() {
        int w = Ui.Vw, h = Ui.Vh;
        DrawMenuBackground(Raylib.GetFrameTime());

        Fonts.DrawTitle3D("НАСТРОЙКИ ЗВУКА", w / 2f, h * 0.10f, 44f);

        float colW = 340f;
        float rowH = 44f;
        float gapY = 14f;
        float startY = h * 0.18f;

        float leftX = w / 2f - colW - 10f;
        float rightX = w / 2f + 10f;
        float centerX = w / 2f - colW / 2f;

        // 1. Общая громкость (Master)
        string masterText = SaveSystem.SoundVolume > 0 ? $"Общая громкость: {SaveSystem.SoundVolume}%" : "Общая громкость: Выкл";
        if (Slider(centerX, startY, colW, rowH, masterText, SaveSystem.SoundVolume, 0f, 100f, out float newMaster)) {
            SaveSystem.SoundVolume = Math.Clamp((int)MathF.Round(newMaster), 0, 100);
            if (SaveSystem.SoundVolume > 0) {
                Raylib.SetMasterVolume(SaveSystem.SoundVolume / 100f);
            } else {
                Raylib.SetMasterVolume(0f);
            }
        }

        // 2. Музыка (Music / BGM) - Левая колонка
        string musicText = SaveSystem.MusicVolume > 0 ? $"Музыка (BGM): {SaveSystem.MusicVolume}%" : "Музыка (BGM): Выкл";
        if (Slider(leftX, startY + (rowH + gapY), colW, rowH, musicText, SaveSystem.MusicVolume, 0f, 100f, out float newMusic)) {
            SaveSystem.MusicVolume = Math.Clamp((int)MathF.Round(newMusic), 0, 100);
        }

        // 3. Блоки (Шаги, копание, установка) - Правая колонка
        string blocksText = SaveSystem.BlocksVolume > 0 ? $"Блоки и шаги: {SaveSystem.BlocksVolume}%" : "Блоки и шаги: Выкл";
        if (Slider(rightX, startY + (rowH + gapY), colW, rowH, blocksText, SaveSystem.BlocksVolume, 0f, 100f, out float newBlocks)) {
            SaveSystem.BlocksVolume = Math.Clamp((int)MathF.Round(newBlocks), 0, 100);
        }

        // 4. Существа (Мобы, животные, криперы) - Левая колонка
        string creaturesText = SaveSystem.CreaturesVolume > 0 ? $"Существа и мобы: {SaveSystem.CreaturesVolume}%" : "Существа: Выкл";
        if (Slider(leftX, startY + (rowH + gapY) * 2f, colW, rowH, creaturesText, SaveSystem.CreaturesVolume, 0f, 100f, out float newCreatures)) {
            SaveSystem.CreaturesVolume = Math.Clamp((int)MathF.Round(newCreatures), 0, 100);
        }

        // 5. Окружение и погода (Гром, вода, лава) - Правая колонка
        string weatherText = SaveSystem.WeatherVolume > 0 ? $"Погода и окружение: {SaveSystem.WeatherVolume}%" : "Погода: Выкл";
        if (Slider(rightX, startY + (rowH + gapY) * 2f, colW, rowH, weatherText, SaveSystem.WeatherVolume, 0f, 100f, out float newWeather)) {
            SaveSystem.WeatherVolume = Math.Clamp((int)MathF.Round(newWeather), 0, 100);
        }

        // 6. Игрок и интерфейс (Удары, поедание, клики, тотем)
        string playerText = SaveSystem.PlayerVolume > 0 ? $"Игрок и интерфейс: {SaveSystem.PlayerVolume}%" : "Игрок: Выкл";
        if (Slider(centerX, startY + (rowH + gapY) * 3f, colW, rowH, playerText, SaveSystem.PlayerVolume, 0f, 100f, out float newPlayer)) {
            SaveSystem.PlayerVolume = Math.Clamp((int)MathF.Round(newPlayer), 0, 100);
        }

        // Нижние кнопки
        float bottomY = h * 0.85f;
        if (Button(w / 2f - 210f, bottomY, 200f, 44f, "Сбросить по умолч.", true)) {
            SaveSystem.SoundVolume = 100;
            SaveSystem.MusicVolume = 70;
            SaveSystem.BlocksVolume = 100;
            SaveSystem.CreaturesVolume = 100;
            SaveSystem.WeatherVolume = 100;
            SaveSystem.PlayerVolume = 100;
            Raylib.SetMasterVolume(1.0f);
            SaveSystem.SaveSettings();
        }

        if (Button(w / 2f + 10f, bottomY, 200f, 44f, "Готово", true)) {
            InAudioScreen = false;
            SaveSystem.SaveSettings();
        }
    }

    // ── Открыть для сети (LAN / Читы) ────────────────────────────────────────
    private static GameMode _lanGameMode = GameMode.Survival;
    private static bool _lanAllowCheats = true;
    private static bool _lanKeepInventory = false;
    private static bool _lanInitialized = false;

    public static void DrawOpenToLan(GameSession session) {
        int w = Ui.Vw, h = Ui.Vh;
        Raylib.DrawRectangleGradientV(0, 0, w, h, new Color(10, 14, 22, 210), new Color(5, 7, 12, 245));

        Fonts.DrawTitle3D("ОТКРЫТЬ ДЛЯ СЕТИ", w / 2f, h * 0.14f, 48f);

        if (!_lanInitialized) {
            _lanGameMode = session.GameMode;
            _lanAllowCheats = session.CheatsEnabled;
            _lanKeepInventory = session.KeepInventory;
            _lanInitialized = true;
        }

        Fonts.DrawCenteredShadowed("Настройки мира и использование чит-команд", w / 2f, h * 0.25f, 18f, new Color(200, 215, 235, 230));

        float btnW = 420f;
        float btnH = 46f;
        float gapY = 14f;
        float cy = h * 0.32f;

        // 1. Игровой режим
        string modeText = _lanGameMode == GameMode.Creative ? "Режим игры: Творческий (Creative)" : "Режим игры: Выживание (Survival)";
        if (Button(w / 2f - btnW / 2f, cy, btnW, btnH, modeText, true)) {
            _lanGameMode = _lanGameMode == GameMode.Creative ? GameMode.Survival : GameMode.Creative;
        }

        // 2. Использование читов
        string cheatsText = _lanAllowCheats ? "Использование читов: ВКЛ" : "Использование читов: ВЫКЛ";
        if (Button(w / 2f - btnW / 2f, cy + btnH + gapY, btnW, btnH, cheatsText, true)) {
            _lanAllowCheats = !_lanAllowCheats;
        }

        // 3. Сохранение инвентаря
        string keepInvText = _lanKeepInventory ? "Сохранение инвентаря (keepInventory): ВКЛ" : "Сохранение инвентаря (keepInventory): ВЫКЛ";
        if (Button(w / 2f - btnW / 2f, cy + (btnH + gapY) * 2f, btnW, btnH, keepInvText, true)) {
            _lanKeepInventory = !_lanKeepInventory;
        }

        // Подсказка / предупреждение
        Fonts.DrawCenteredShadowed("Включение читов активирует команды /gamemode, /gamerule, /give, /time, /tp", w / 2f, h * 0.62f, 16f, new Color(255, 220, 110, 230));

        // Кнопки внизу
        float actBtnW = 250f;
        float actGapX = 20f;
        float actY = h * 0.74f;

        if (Button(w / 2f - actBtnW - actGapX / 2f, actY, actBtnW, 48f, "Открыть мир для сети", true)) {
            session.CheatsEnabled = _lanAllowCheats;
            session.GameMode = _lanGameMode;
            session.KeepInventory = _lanKeepInventory;
            if (_lanGameMode == GameMode.Creative) {
                session.Player.Health = session.Player.MaxHealth;
                session.Player.Hunger = 20f;
            }
            GameServer.Start(session, NetworkProtocol.DefaultPort);
            _lanDiscovery ??= new LanDiscovery();
            _lanDiscovery.StartBroadcaster(NetworkProtocol.DefaultPort, "LAN World", session.Player.Name, () => (GameServer.Active?.ClientCount ?? 0) + 1);

            session.AddChatMessage("Локальный мир открыт на порту 25565", Color.Green);
            if (session.CheatsEnabled) {
                session.AddChatMessage("Читы и консольные команды активированы! Введите /help для списка.", Color.Gold);
            }
            InOpenToLanScreen = false;
            _lanInitialized = false;
            session.Ui = UiState.Playing;
        }

        if (Button(w / 2f + actGapX / 2f, actY, actBtnW, 48f, "Отмена", true)) {
            InOpenToLanScreen = false;
            _lanInitialized = false;
        }
    }

    public static void DrawControls() {
        int w = Ui.Vw, h = Ui.Vh;
        DrawMenuBackground(Raylib.GetFrameTime());

        if (ActiveRebindIndex != -1) {
            int pressed = Raylib.GetKeyPressed();
            if (pressed != 0) {
                if ((KeyboardKey)pressed != KeyboardKey.Escape) {
                    SetBindKey(ActiveRebindIndex, (KeyboardKey)pressed);
                }
                ActiveRebindIndex = -1;
                while (Raylib.GetKeyPressed() != 0) {}
            }
        }

        Fonts.DrawTitle3D("НАСТРОЙКИ УПРАВЛЕНИЯ", w / 2f, h * 0.08f, 40f);

        float startY = h * 0.14f;
        float rowH = 36f;
        float rowGap = 6f;
        float colW = 340f;
        
        for (int i = 0; i < BindLabels.Length; i++) {
            int col = i % 2;
            int row = i / 2;
            
            float cx = (col == 0) ? (w / 2f - colW - 10f) : (w / 2f + 10f);
            float cy = startY + row * (rowH + rowGap);
            
            Fonts.DrawShadowed($"{BindLabels[i]}:", cx, cy + 8f, 18f, Color.White);
            
            string keyName = (ActiveRebindIndex == i) ? "> ??? <" : KeyBinds.GetName(GetBindKey(i));
            if (Button(cx + 170f, cy, 170f, rowH, keyName, true)) {
                ActiveRebindIndex = i;
            }
        }

        // Слайдер чувствительности мыши
        int totalRows = (BindLabels.Length + 1) / 2;
        float sensY = startY + totalRows * (rowH + rowGap) + 4f;
        string sensText = $"Чувствительность мыши: {SaveSystem.MouseSensitivity}%";
        if (Slider(w / 2f - colW, sensY, colW * 2f + 20f, rowH, sensText, SaveSystem.MouseSensitivity, 20f, 200f, out float newSens)) {
            SaveSystem.MouseSensitivity = Math.Clamp((int)MathF.Round(newSens), 20, 200);
        }

        // Кнопки инверсии мыши
        float invY = sensY + rowH + 6f;
        float invBtnW = (colW * 2f + 20f - 10f) / 2f;
        string invYText = $"Инверсия по Y: {(SaveSystem.InvertMouseY ? "ВКЛ" : "ВЫКЛ")}";
        if (Button(w / 2f - colW, invY, invBtnW, rowH, invYText, true)) {
            SaveSystem.InvertMouseY = !SaveSystem.InvertMouseY;
            SaveSystem.SaveSettings();
        }
        string invXText = $"Инверсия по X: {(SaveSystem.InvertMouseX ? "ВКЛ" : "ВЫКЛ")}";
        if (Button(w / 2f - colW + invBtnW + 10f, invY, invBtnW, rowH, invXText, true)) {
            SaveSystem.InvertMouseX = !SaveSystem.InvertMouseX;
            SaveSystem.SaveSettings();
        }

        float bottomY = h * 0.88f;
        if (Button(w / 2f - 210f, bottomY, 200f, 44f, "Сбросить по умолч.", true)) {
            ActiveRebindIndex = -1;
            KeyBinds.ResetToDefaults();
            SaveSystem.MouseSensitivity = 100;
            SaveSystem.InvertMouseY = false;
            SaveSystem.InvertMouseX = false;
            SaveSystem.SaveSettings();
        }

        if (Button(w / 2f + 10f, bottomY, 200f, 44f, "Готово", true)) {
            ActiveRebindIndex = -1;
            InControlsScreen = false;
            SaveSystem.SaveSettings();
        }
    }

    // ── Пауза ────────────────────────────────────────────────────────────────

    public static PauseAction DrawPause(GameSession session) {
        int w = Ui.Vw, h = Ui.Vh;
        Raylib.DrawRectangleGradientV(0, 0, w, h, new Color(10, 14, 22, 170), new Color(5, 7, 12, 220));

        bool isClient = GameClient.Active != null;
        bool isHost = GameServer.Active != null;
        bool isMultiplayer = isClient || isHost;

        string title = isMultiplayer ? (isClient ? "СЕТЕВАЯ ИГРА (КЛИЕНТ)" : "СЕТЕВАЯ ИГРА (СЕРВЕР LAN)") : "МЕНЮ ПАУЗЫ";
        Fonts.DrawTitle3D(title, w / 2f, h * 0.16f, 44f);

        var action = PauseAction.None;
        float cy = h * 0.32f;
        float btnW = 340f;
        float btnH = 48f;
        float gapY = 12f;

        if (Button(w / 2f - btnW / 2f, cy, btnW, btnH, "Вернуться в игру", true)) action = PauseAction.Resume;

        if (!isMultiplayer) {
            // Одиночная игра: можно открыть для сети
            if (Button(w / 2f - btnW / 2f, cy + btnH + gapY, btnW, btnH, "Открыть для сети...", true)) action = PauseAction.OpenToLan;
            if (Button(w / 2f - btnW / 2f, cy + (btnH + gapY) * 2f, btnW, btnH, "Настройки...", true)) action = PauseAction.Settings;
            if (Button(w / 2f - btnW / 2f, cy + (btnH + gapY) * 3f, btnW, btnH, "Сохранить и выйти в меню", true)) action = PauseAction.SaveAndExit;
        } else {
            // В сетевой игре: кнопки "Открыть для сети..." нет
            if (Button(w / 2f - btnW / 2f, cy + btnH + gapY, btnW, btnH, "Настройки...", true)) action = PauseAction.Settings;
            string exitText = isClient ? "Отключиться от сервера" : "Остановить LAN и выйти";
            if (Button(w / 2f - btnW / 2f, cy + (btnH + gapY) * 2f, btnW, btnH, exitText, true)) action = PauseAction.SaveAndExit;
        }
        return action;
    }
}
