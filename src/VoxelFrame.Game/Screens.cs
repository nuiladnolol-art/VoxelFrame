using VoxelFrame.Core;
using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.Materials;

namespace VoxelFrame.Game;

public enum MenuAction { None, NewGame, Continue, Exit }
public enum PauseAction { None, Resume, SaveAndExit, Settings }

/// <summary>Экраны: главное меню, пауза, инвентарь, крафт. Мышь — прямоугольные кнопки.</summary>
public static partial class Screens {
    private static readonly Color Bg = new(28, 32, 40, 255);
    private static readonly Color Panel = new(198, 198, 198, 255); // Классический светло-серый GUI (#C6C6C6)
    private static readonly Color PanelLightBorder = new(255, 255, 255, 255); // Белый блик (верх/лево)
    private static readonly Color PanelDarkBorder = new(85, 85, 85, 255); // Тень (низ/право)
    private static readonly Color TextDark = new(64, 64, 64, 255); // Темно-серый текст (#404040)
    private static readonly Color Btn = new(140, 140, 140, 255);
    private static readonly Color BtnHover = new(170, 170, 170, 255);
    private static readonly Color BtnDisabled = new(110, 110, 110, 255);

    public static string MenuError = "";
    public static bool InWorldSelectScreen = false;
    public static bool InCreateWorldScreen = false;
    public static int SelectedWorldListIndex = 0;
    public static string WorldNameInput = "Новый мир";
    public static string WorldSeedInput = "";
    public static int ActiveTextInputField = 0; // 0 = none, 1 = name, 2 = seed
    public static int CustomWorldSeed = 0;

    public static bool InSettingsScreen = false;
    public static bool InControlsScreen = false;
    public static bool InGraphicsScreen = false;
    public static bool InAudioScreen = false;
    public static bool InGameplayScreen = false;
    public static bool SettingsOpenedFromGame = false;
    public static int ActiveRebindIndex = -1;

    private static readonly string[] BindLabels = {
        "Вперед",
        "Назад",
        "Влево",
        "Вправо",
        "Прыжок",
        "Красться",
        "Бег (Спринт)",
        "Выбросить",
        "Инвентарь",
        "Крафт",
        "Пауза"
    };

    private static KeyboardKey GetBindKey(int idx) => idx switch {
        0 => KeyBinds.Forward,
        1 => KeyBinds.Backward,
        2 => KeyBinds.Left,
        3 => KeyBinds.Right,
        4 => KeyBinds.Jump,
        5 => KeyBinds.Crouch,
        6 => KeyBinds.Sprint,
        7 => KeyBinds.Drop,
        8 => KeyBinds.Inventory,
        9 => KeyBinds.Crafting,
        10 => KeyBinds.Pause,
        _ => KeyboardKey.Null
    };

    private static void SetBindKey(int idx, KeyboardKey key) {
        switch (idx) {
            case 0: KeyBinds.Forward = key; break;
            case 1: KeyBinds.Backward = key; break;
            case 2: KeyBinds.Left = key; break;
            case 3: KeyBinds.Right = key; break;
            case 4: KeyBinds.Jump = key; break;
            case 5: KeyBinds.Crouch = key; break;
            case 6: KeyBinds.Sprint = key; break;
            case 7: KeyBinds.Drop = key; break;
            case 8: KeyBinds.Inventory = key; break;
            case 9: KeyBinds.Crafting = key; break;
            case 10: KeyBinds.Pause = key; break;
        }
    }

    // ── Главное меню ─────────────────────────────────────────────────────────

    public static MenuAction DrawMenu(float dt) {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        
        int tileSize = 64;
        for (int x = 0; x < w; x += tileSize) {
            for (int y = 0; y < h; y += tileSize) {
                bool isEven = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                Raylib.DrawRectangle(x, y, tileSize, tileSize, isEven ? new Color(42, 34, 30, 255) : new Color(34, 27, 24, 255));
            }
        }

        var action = MenuAction.None;

        if (InCreateWorldScreen) {
            Fonts.DrawCentered("СОЗДАНИЕ МИРА", w / 2f, h * 0.10f, 40f, new Color(255, 220, 120, 255));

            float boxW = 380f;
            float cx = w / 2f - boxW / 2f;

            // Название мира
            Fonts.Draw("Название мира:", cx, h * 0.22f, 18f, new Color(200, 200, 200, 255));
            var nameRec = new Rectangle(cx, h * 0.26f, boxW, 42f);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {
                if (Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), nameRec)) ActiveTextInputField = 1;
            }
            Raylib.DrawRectangleRec(nameRec, ActiveTextInputField == 1 ? new Color(60, 65, 80, 255) : new Color(40, 45, 55, 255));
            Raylib.DrawRectangleLinesEx(nameRec, 1.5f, ActiveTextInputField == 1 ? new Color(255, 220, 120, 255) : new Color(90, 100, 120, 255));
            Fonts.Draw(WorldNameInput + (ActiveTextInputField == 1 && ((int)(Raylib.GetTime() * 2) % 2 == 0) ? "_" : ""), cx + 10f, h * 0.26f + 11f, 18f, Color.White);

            // Режим игры (Выживание)
            Fonts.Draw("Режим игры: Выживание", cx, h * 0.36f, 18f, new Color(200, 200, 200, 255));
            Fonts.Draw("Поиск ресурсов, крафт, опасные мобы и шкала здоровья.", cx, h * 0.40f, 13f, new Color(150, 160, 170, 255));

            // Сид генерации
            Fonts.Draw("Сид для генератора мира (оставьте пустым для случайного):", cx, h * 0.48f, 16f, new Color(200, 200, 200, 255));
            var seedRec = new Rectangle(cx, h * 0.52f, boxW, 42f);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {
                if (Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), seedRec)) ActiveTextInputField = 2;
                else if (!Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), nameRec)) ActiveTextInputField = 0;
            }
            Raylib.DrawRectangleRec(seedRec, ActiveTextInputField == 2 ? new Color(60, 65, 80, 255) : new Color(40, 45, 55, 255));
            Raylib.DrawRectangleLinesEx(seedRec, 1.5f, ActiveTextInputField == 2 ? new Color(255, 220, 120, 255) : new Color(90, 100, 120, 255));
            Fonts.Draw(WorldSeedInput + (ActiveTextInputField == 2 && ((int)(Raylib.GetTime() * 2) % 2 == 0) ? "_" : ""), cx + 10f, h * 0.52f + 11f, 18f, Color.White);

            // Обработка ввода с клавиатуры
            int key = Raylib.GetCharPressed();
            while (key > 0) {
                if (key >= 32 && key <= 126 || key >= 1040 && key <= 1103) {
                    if (ActiveTextInputField == 1 && WorldNameInput.Length < 24) WorldNameInput += (char)key;
                    else if (ActiveTextInputField == 2 && WorldSeedInput.Length < 32) WorldSeedInput += (char)key;
                }
                key = Raylib.GetCharPressed();
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Backspace)) {
                if (ActiveTextInputField == 1 && WorldNameInput.Length > 0) WorldNameInput = WorldNameInput[..^1];
                else if (ActiveTextInputField == 2 && WorldSeedInput.Length > 0) WorldSeedInput = WorldSeedInput[..^1];
            }

            // Кнопки Создать и Отмена
            float btnY = h * 0.72f;
            if (Button(w / 2f - 195f, btnY, 190f, 46f, "Создать новый мир", true)) {
                string name = string.IsNullOrWhiteSpace(WorldNameInput) ? "Новый мир" : WorldNameInput.Trim();
                SaveSystem.CurrentWorldPath = SaveSystem.CreateWorldSavePath(name);
                CustomWorldSeed = GameData.ParseSeed(WorldSeedInput);
                action = MenuAction.NewGame;
                InCreateWorldScreen = false;
                InWorldSelectScreen = false;
            }
            if (Button(w / 2f + 5f, btnY, 190f, 46f, "Отмена", true)) {
                InCreateWorldScreen = false;
            }
        } else if (InWorldSelectScreen) {
            Fonts.DrawCentered("ВЫБОР МИРА", w / 2f, h * 0.08f, 40f, new Color(255, 220, 120, 255));

            var worlds = SaveSystem.GetAllWorlds();
            if (SelectedWorldListIndex >= worlds.Count) SelectedWorldListIndex = Math.Max(0, worlds.Count - 1);

            float listY = h * 0.16f;
            float listH = h * 0.58f;
            float cardW = MathF.Min(560f, w - 80f);
            float cardH = 64f;
            float cardX = w / 2f - cardW / 2f;

            if (worlds.Count == 0) {
                Fonts.DrawCentered("Нет созданных миров. Нажмите 'Создать новый мир'", w / 2f, h * 0.40f, 20f, new Color(180, 180, 180, 255));
            } else {
                for (int i = 0; i < worlds.Count && i < 5; i++) {
                    var wi = worlds[i];
                    float cy = listY + i * (cardH + 8f);
                    var cardRec = new Rectangle(cardX, cy, cardW, cardH);
                    bool isSelected = (i == SelectedWorldListIndex);

                    if (Raylib.IsMouseButtonPressed(MouseButton.Left) && Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), cardRec)) {
                        SelectedWorldListIndex = i;
                    }

                    Raylib.DrawRectangleRec(cardRec, isSelected ? new Color(60, 68, 85, 240) : new Color(36, 42, 54, 200));
                    Raylib.DrawRectangleLinesEx(cardRec, isSelected ? 2f : 1f, isSelected ? new Color(255, 220, 120, 255) : new Color(65, 75, 95, 255));

                    Fonts.Draw(wi.Name, cardX + 12f, cy + 8f, 20f, Color.White);
                    string dateStr = $"{wi.LastPlayed:dd.MM.yyyy HH:mm}";
                    string sizeStr = $"{wi.SizeBytes / 1024} KB";
                    string seedStr = wi.Seed != 0 ? $" (Сид: {wi.Seed})" : "";
                    Fonts.Draw($"{dateStr} • {sizeStr}{seedStr}", cardX + 12f, cy + 34f, 14f, new Color(160, 175, 195, 255));
                }
            }

            // Нижняя панель действий
            float bY1 = h * 0.78f;
            float bY2 = h * 0.86f;
            float bW = 180f;

            bool hasSelection = worlds.Count > 0 && SelectedWorldListIndex >= 0 && SelectedWorldListIndex < worlds.Count;
            if (Button(w / 2f - bW - 6f, bY1, bW, 44f, "Играть в мире", hasSelection)) {
                SaveSystem.CurrentWorldPath = worlds[SelectedWorldListIndex].FilePath;
                action = MenuAction.Continue;
                InWorldSelectScreen = false;
            }
            if (Button(w / 2f + 6f, bY1, bW, 44f, "Создать новый мир", true)) {
                InCreateWorldScreen = true;
                WorldNameInput = "Новый мир";
                WorldSeedInput = "";
                ActiveTextInputField = 0;
            }

            if (Button(w / 2f - bW - 6f, bY2, bW, 44f, "Удалить", hasSelection)) {
                SaveSystem.DeleteSave(worlds[SelectedWorldListIndex].FilePath);
                if (SelectedWorldListIndex >= worlds.Count - 1) SelectedWorldListIndex = Math.Max(0, worlds.Count - 2);
            }
            if (Button(w / 2f + 6f, bY2, bW, 44f, "Отмена", true)) {
                InWorldSelectScreen = false;
            }
        } else {
            Fonts.DrawCentered("VOXELFRAME", w / 2f + 4f, h * 0.16f + 4f, 72f, new Color(50, 45, 35, 255));
            Fonts.DrawCentered("VOXELFRAME", w / 2f, h * 0.16f, 72f, new Color(255, 220, 120, 255));

            float cy = h * 0.35f;
            if (Button(w / 2f - 140f, cy, 280f, 46f, "Одиночная игра", true)) {
                InWorldSelectScreen = true;
            }
            if (Button(w / 2f - 140f, cy + 52f, 280f, 46f, "Сетевая игра", false)) { }

            if (Button(w / 2f - 140f, cy + 104f, 138f, 46f, "Настройки...", true)) {
                InSettingsScreen = true;
                SettingsOpenedFromGame = false;
            }
            if (Button(w / 2f + 2f, cy + 104f, 138f, 46f, "Выход", true)) action = MenuAction.Exit;

            if (MenuError.Length > 0)
                Fonts.DrawCentered(MenuError, w / 2f, h * 0.68f, 18f, new Color(255, 120, 120, 255));

            Fonts.Draw("VoxelFrame Alpha 0.9.4", 10f, h - 25f, 14f, new Color(200, 200, 200, 180));
            Fonts.Draw("SenStol Studio", w - 180f, h - 25f, 14f, new Color(200, 200, 200, 180));
        }

        return action;
    }

    // ── Настройки и Управление ───────────────────────────────────────────────

    public static void DrawSettings() {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        
        int tileSize = 64;
        for (int x = 0; x < w; x += tileSize) {
            for (int y = 0; y < h; y += tileSize) {
                bool isEven = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                Raylib.DrawRectangle(x, y, tileSize, tileSize, isEven ? new Color(42, 34, 30, 255) : new Color(34, 27, 24, 255));
            }
        }

        Fonts.DrawCentered("НАСТРОЙКИ", w / 2f, h * 0.18f, 44f, new Color(255, 220, 120, 255));

        float cy = h * 0.34f;
        float btnW = 210f;
        float btnH = 46f;
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

        if (Button(leftX, cy + btnH + gapY, btnW, btnH, "Игровой процесс...", true)) {
            InGameplayScreen = true;
        }
        if (Button(rightX, cy + btnH + gapY, btnW, btnH, "Управление...", true)) {
            InControlsScreen = true;
        }

        if (Button(w / 2f - 140f, cy + (btnH + gapY) * 2 + 28f, 280f, 46f, "Готово", true)) {
            InSettingsScreen = false;
        }
    }

        private static bool Slider(float x, float y, float w, float h, string text, float value, float min, float max, out float newValue) {
        newValue = value;
        var mouse = Raylib.GetMousePosition();
        var rect = new Rectangle(x, y, w, h);
        bool hovered = Raylib.CheckCollisionPointRec(mouse, rect);
        bool dragging = hovered && Raylib.IsMouseButtonDown(MouseButton.Left);

        if (dragging) {
            float ratio = Math.Clamp((mouse.X - x) / w, 0f, 1f);
            newValue = min + ratio * (max - min);
        }

        // Фон кнопки-слайдера
        Raylib.DrawRectangle((int)x, (int)y, (int)w, (int)h, new Color(38, 38, 38, 255));
        Raylib.DrawRectangleLinesEx(rect, 2f, hovered ? new Color(160, 160, 160, 255) : new Color(60, 60, 60, 255));

        // Полоса заполнения прогресса
        float currentRatio = Math.Clamp((value - min) / (max - min), 0f, 1f);
        int fillW = (int)((w - 4f) * currentRatio);
        if (fillW > 0) {
            Raylib.DrawRectangle((int)x + 2, (int)y + 2, fillW, (int)h - 4, new Color(75, 95, 125, 200));
        }

        // Бегунок-разделитель
        float thumbX = x + 2f + currentRatio * (w - 12f);
        var thumbRect = new Rectangle(thumbX, y + 2f, 8f, h - 4f);
        Raylib.DrawRectangleRec(thumbRect, new Color(255, 220, 120, 255));

        // Текст поверх слайдера
        Fonts.DrawCentered(text, x + w / 2f, y + (h - 18f) / 2f, 18f, Color.White);

        return dragging;
    }

    public static void DrawGraphics() {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        
        int tileSize = 64;
        for (int x = 0; x < w; x += tileSize) {
            for (int y = 0; y < h; y += tileSize) {
                bool isEven = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                Raylib.DrawRectangle(x, y, tileSize, tileSize, isEven ? new Color(42, 34, 30, 255) : new Color(34, 27, 24, 255));
            }
        }

        Fonts.DrawCentered("НАСТРОЙКИ ГРАФИКИ", w / 2f, h * 0.10f, 44f, new Color(255, 220, 120, 255));

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
            if (SaveSystem.GraphicsQuality == SaveSystem.GraphicsPreset.Fast) {
                SaveSystem.CloudsMode = 1;
                SaveSystem.ParticlesMode = 0;
                SaveSystem.DynamicLighting = false;
                SaveSystem.EntityShadows = false;
            } else if (SaveSystem.GraphicsQuality == SaveSystem.GraphicsPreset.Fancy) {
                SaveSystem.CloudsMode = 2;
                SaveSystem.ParticlesMode = 2;
                SaveSystem.DynamicLighting = true;
                SaveSystem.EntityShadows = true;
            } else {
                SaveSystem.CloudsMode = 2;
                SaveSystem.ParticlesMode = 2;
                SaveSystem.DynamicLighting = true;
                SaveSystem.EntityShadows = true;
            }
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

        // 4. Тени сущностей
        string shadowsText = SaveSystem.EntityShadows ? "Тени мобов: Включены" : "Тени мобов: Выключены";
        if (Button(leftX, startY + (rowH + gapY) * 3f, colW, rowH, shadowsText, true)) {
            SaveSystem.EntityShadows = !SaveSystem.EntityShadows;
        }

        // Правая колонка
        // 1. Режим экрана
        bool isFs = Raylib.IsWindowState(ConfigFlags.UndecoratedWindow) || Raylib.IsWindowFullscreen();
        string fsText = isFs ? "Экран: Полноэкранный (в окне)" : "Экран: Оконный";
        if (Button(rightX, startY, colW, rowH, fsText, true)) {
            Raylib.ToggleBorderlessWindowed();
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

        // Кнопка Готово
        if (Button(w / 2f - 140f, h * 0.85f, 280f, 46f, "Готово", true)) {
            InGraphicsScreen = false;
            SaveSystem.SaveSettings();
        }
    }

    public static void DrawAudio() {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        
        int tileSize = 64;
        for (int x = 0; x < w; x += tileSize) {
            for (int y = 0; y < h; y += tileSize) {
                bool isEven = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                Raylib.DrawRectangle(x, y, tileSize, tileSize, isEven ? new Color(42, 34, 30, 255) : new Color(34, 27, 24, 255));
            }
        }

        Fonts.DrawCentered("НАСТРОЙКИ ЗВУКА", w / 2f, h * 0.15f, 44f, new Color(255, 220, 120, 255));

        float cy = h * 0.36f;
        string volText = SaveSystem.SoundVolume > 0 ? $"Общая громкость: {SaveSystem.SoundVolume}%" : "Общая громкость: Выкл";
        if (Slider(w / 2f - 180f, cy, 360f, 46f, volText, SaveSystem.SoundVolume, 0f, 100f, out float newVol)) {
            SaveSystem.SoundVolume = Math.Clamp((int)MathF.Round(newVol), 0, 100);
            if (SaveSystem.SoundVolume > 0) {
                Raylib.SetMasterVolume(SaveSystem.SoundVolume / 100f);
            } else {
                Raylib.SetMasterVolume(0f);
            }
        }

        if (Button(w / 2f - 140f, h * 0.82f, 280f, 46f, "Готово", true)) {
            InAudioScreen = false;
            SaveSystem.SaveSettings();
        }
    }

    public static void DrawGameplay() {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        
        int tileSize = 64;
        for (int x = 0; x < w; x += tileSize) {
            for (int y = 0; y < h; y += tileSize) {
                bool isEven = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                Raylib.DrawRectangle(x, y, tileSize, tileSize, isEven ? new Color(42, 34, 30, 255) : new Color(34, 27, 24, 255));
            }
        }

        Fonts.DrawCentered("ИГРОВОЙ ПРОЦЕСС", w / 2f, h * 0.15f, 44f, new Color(255, 220, 120, 255));

        float cy = h * 0.36f;
        Button(w / 2f - 180f, cy, 360f, 46f, "Режим игры: Классическое выживание", false);
        Button(w / 2f - 180f, cy + 56f, 360f, 46f, "Потеря вещей при смерти: Включена", false);

        if (Button(w / 2f - 140f, h * 0.82f, 280f, 46f, "Готово", true)) {
            InGameplayScreen = false;
            SaveSystem.SaveSettings();
        }
    }

    public static void DrawControls() {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        
        int tileSize = 64;
        for (int x = 0; x < w; x += tileSize) {
            for (int y = 0; y < h; y += tileSize) {
                bool isEven = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                Raylib.DrawRectangle(x, y, tileSize, tileSize, isEven ? new Color(42, 34, 30, 255) : new Color(34, 27, 24, 255));
            }
        }

        if (ActiveRebindIndex != -1) {
            int pressed = Raylib.GetKeyPressed();
            if (pressed != 0) {
                SetBindKey(ActiveRebindIndex, (KeyboardKey)pressed);
                ActiveRebindIndex = -1;
                while (Raylib.GetKeyPressed() != 0) {}
            }
        }

        Fonts.DrawCentered("НАСТРОЙКИ УПРАВЛЕНИЯ", w / 2f, h * 0.10f, 44f, new Color(255, 220, 120, 255));

        float startY = h * 0.18f;
        float rowH = 42f;
        float colW = 340f;
        
        for (int i = 0; i < BindLabels.Length; i++) {
            int col = i % 2;
            int row = i / 2;
            
            float cx = (col == 0) ? (w / 2f - colW - 10f) : (w / 2f + 10f);
            float cy = startY + row * (rowH + 8f);
            
            Fonts.Draw($"{BindLabels[i]}:", cx, cy + 10f, 20f, Color.White);
            
            string keyName = (ActiveRebindIndex == i) ? "> ??? <" : KeyBinds.GetName(GetBindKey(i));
            if (Button(cx + 160f, cy, 160f, rowH, keyName, true)) {
                ActiveRebindIndex = i;
            }
        }

        float bottomY = h * 0.85f;
        if (Button(w / 2f - 210f, bottomY, 200f, 44f, "Сбросить по умолч.", true)) {
            ActiveRebindIndex = -1;
            KeyBinds.ResetToDefaults();
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
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        Raylib.DrawRectangle(0, 0, w, h, new Color(0, 0, 0, 150));
        Fonts.DrawCentered("ПАУЗА", w / 2f, h * 0.16f, 44f, Color.White);

        var action = PauseAction.None;
        float cy = h * 0.28f;
        if (Button(w / 2f - 140f, cy, 280f, 46f, "Продолжить (ESC)", true)) action = PauseAction.Resume;
        if (Button(w / 2f - 140f, cy + 58f, 280f, 46f, "Настройки...", true)) action = PauseAction.Settings;
        if (Button(w / 2f - 140f, cy + 116f, 280f, 46f, "Сохранить и выйти в меню", true)) action = PauseAction.SaveAndExit;
        return action;
    }

    // ── Экран смерти ─────────────────────────────────────────────────────────

    public enum DeathAction { None, Respawn, MainMenu }

    public static DeathAction DrawDeath(GameSession session) {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        // Красный градиент смерти
        Raylib.DrawRectangleGradientV(0, 0, w, h, new Color(170, 20, 20, 170), new Color(40, 5, 5, 235));

        Fonts.DrawCentered("ВЫ ПОГИБЛИ!", w / 2f, h * 0.25f, 54f, new Color(255, 60, 60, 255));

        int totalSec = (int)session.TotalPlaySeconds;
        int mins = totalSec / 60;
        int secs = totalSec % 60;
        Fonts.DrawCentered($"Время выживания: {mins} мин {secs:D2} сек", w / 2f, h * 0.38f, 24f, new Color(230, 230, 230, 255));

        var action = DeathAction.None;
        float cy = h * 0.52f;
        if (Button(w / 2f - 140f, cy, 280f, 48f, "Возродиться", true)) {
            action = DeathAction.Respawn;
        }
        if (Button(w / 2f - 140f, cy + 64f, 280f, 48f, "Главное меню", true)) {
            action = DeathAction.MainMenu;
        }

        return action;
    }

    // ── Экран загрузки ───────────────────────────────────────────────────────

    public static void DrawLoading(GameSession session) {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        Raylib.ClearBackground(new Color(10, 12, 20, 255));

        Fonts.DrawCentered("ЗАГРУЗКА МИРА", w / 2f, h * 0.35f, 48f, new Color(255, 220, 120, 255));

        // Прогресс-бар
        float barW = 400f, barH = 24f;
        float barX = w / 2f - barW / 2f, barY = h * 0.5f;
        Raylib.DrawRectangleRec(new Rectangle(barX, barY, barW, barH), new Color(40, 44, 52, 255));
        float progress = session.LoadTotal > 0 ? (float)session.LoadDone / session.LoadTotal : 0f;
        Raylib.DrawRectangleRec(new Rectangle(barX, barY, barW * progress, barH), new Color(100, 180, 100, 255));
        Raylib.DrawRectangleLinesEx(new Rectangle(barX, barY, barW, barH), 2f, new Color(80, 84, 92, 255));

        string pct = $"{(int)(progress * 100)}%";
        float tw = Fonts.Measure(pct, 18f);
        Fonts.Draw(pct, w / 2f - tw / 2f, barY + 4f, 18f, Color.White);

        Fonts.DrawCentered($"Чанков: {session.LoadDone}/{session.LoadTotal}", w / 2f, barY + 40f, 16f, new Color(170, 176, 190, 255));
    }

    /// <summary>Титры после победы над Слизнем Края и выхода из Энда (прокрутка снизу вверх).</summary>
    public static void DrawCredits(GameSession session) {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        Raylib.ClearBackground(Color.Black);

        string[] lines = {
            "VoxelFrame",
            "",
            "Спасибо, что прошли игру!",
            "",
            "Вы победили Слизня Края",
            "и освободили измерение Энд.",
            "",
            "Мир ждёт новых приключений",
            "и великих построек.",
            "",
            "Играйте дальше!",
        };

        float elapsed = 32f - session.CreditsTimer;
        float y = h + 60f - elapsed * 55f;
        const float lineH = 46f;
        for (int i = 0; i < lines.Length; i++) {
            float ly = y + i * lineH;
            if (ly < -50f || ly > h + 50f) continue;
            bool isTitle = i == 0;
            float size = isTitle ? 52f : 30f;
            float mw = Fonts.Measure(lines[i], size);
            Color col = isTitle ? new Color(255, 220, 120, 255) : new Color(220, 225, 235, 255);
            Fonts.DrawShadowed(lines[i], w / 2f - mw / 2f, ly, size, col);
        }
    }

    // ── Инвентарь (сетка слотов, предмет за курсором) ─────────────────────────

    private static readonly Color SlotBg = new(139, 139, 139, 255); // Инсет слота (#8B8B8B)
    private static readonly Color SlotBorder = new(55, 55, 55, 255); // Тень слота (#373737)
    private static readonly Color SlotHover = new(220, 220, 220, 160);
    private static readonly Color SlotSelected = new(255, 255, 255, 255);

    /// Предмет, который игрок держит «за курсором».
    private static ItemEntry? Held;

    public static void Reset() {
        Held = null;
    }

    /// Возврат предмета из руки и сеток крафта в инвентарь (при закрытии окна).
    public static void ReturnHeld(GameSession session) {
        var inv = session.Player.Inventory;
        if (Held.HasValue && Held.Value.Quantity > 0) {
            var held = Held.Value;
            if (!inv.TryInsert(held.Item, held.Quantity)) {
                var dropPos = session.Player.Position + new System.Numerics.Vector3(0f, 1.2f, 0f) + session.Player.Forward * 0.4f;
                var pickup = new ItemPickup(held.Item, held.Quantity, dropPos) {
                    PickupDelay = 0.8f,
                    Velocity = session.Player.Forward * 2.0f + new System.Numerics.Vector3(0f, 1.5f, 0f)
                };
                session.World.Pickups.Add(pickup);
            }
            Held = null;
        }
        for (int i = 0; i < 4; i++) {
            if (PersonalGrid[i].HasValue && PersonalGrid[i]!.Value.Quantity > 0) {
                var it = PersonalGrid[i]!.Value;
                if (!inv.TryInsert(it.Item, it.Quantity)) {
                    var dropPos = session.Player.Position + new System.Numerics.Vector3(0f, 1.2f, 0f) + session.Player.Forward * 0.4f;
                    var pickup = new ItemPickup(it.Item, it.Quantity, dropPos) {
                        PickupDelay = 0.8f,
                        Velocity = session.Player.Forward * 2.0f + new System.Numerics.Vector3(0f, 1.5f, 0f)
                    };
                    session.World.Pickups.Add(pickup);
                }
                PersonalGrid[i] = null;
            }
        }
        for (int i = 0; i < 9; i++) {
            if (WorkbenchGrid[i].HasValue && WorkbenchGrid[i]!.Value.Quantity > 0) {
                var it = WorkbenchGrid[i]!.Value;
                if (!inv.TryInsert(it.Item, it.Quantity)) {
                    var dropPos = session.Player.Position + new System.Numerics.Vector3(0f, 1.2f, 0f) + session.Player.Forward * 0.4f;
                    var pickup = new ItemPickup(it.Item, it.Quantity, dropPos) {
                        PickupDelay = 0.8f,
                        Velocity = session.Player.Forward * 2.0f + new System.Numerics.Vector3(0f, 1.5f, 0f)
                    };
                    session.World.Pickups.Add(pickup);
                }
                WorkbenchGrid[i] = null;
            }
        }
    }

    public static void ReturnHeld(VoxelFrame.Core.Inventory.Container inv) {
        if (Held.HasValue && Held.Value.Quantity > 0) {
            var held = Held.Value;
            if (inv.TryInsert(held.Item, held.Quantity)) Held = null;
        }
    }

    public static void DrawInventory(GameSession session) {
        if (session.GameMode == GameMode.Creative) {
            DrawCreativeMenu(session);
            return;
        }

        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        var inv = session.Player.Inventory;

        int cols = 9, hotbarRows = 1, mainRows = 3;
        const int slot = 52, gap = 4;
        int gridW = cols * slot + (cols - 1) * gap;
        int panelW = gridW + 280;
        int panelH = (mainRows + hotbarRows) * slot + mainRows * gap + 90;
        float px = w / 2f - panelW / 2f, py = (h - panelH) / 2f;
        DrawPanel(px, py, panelW, panelH);

        float gridX = px + 16f;
        float gridY = py + 38f;
        float hotbarY = gridY + mainRows * (slot + gap) + 12f;

        // Заголовки
        Fonts.Draw("Инвентарь", gridX, py + 14f, 16f, TextDark);
        Fonts.Draw("Хотбар", gridX, hotbarY - 18f, 14f, TextDark);

        // Сетка: 3 ряда основного хранилища (записи 9..) + ряд хотбара (0..8).
        for (int row = 0; row < mainRows; row++) {
            for (int col = 0; col < cols; col++) {
                int idx = 9 + row * cols + col;
                DrawSlot(session, inv, gridX + col * (slot + gap), gridY + row * (slot + gap), idx, false);
            }
        }
        for (int col = 0; col < cols; col++)
            DrawSlot(session, inv, gridX + col * (slot + gap), hotbarY, col, col == session.Player.SelectedSlot);

        // Слот второй руки (справа от хотбара в окне инвентаря)
        float offhandX = px + gridW + 30f;
        float offhandY = hotbarY;
        var offhandRec = new Rectangle(offhandX, offhandY, slot, slot);
        bool offhandHover = Raylib.CheckCollisionPointRec(Raylib.GetMousePosition(), offhandRec);
        
        Raylib.DrawRectangleRec(offhandRec, SlotBg);
        Raylib.DrawRectangle((int)offhandX, (int)offhandY, slot, 2, SlotBorder);
        Raylib.DrawRectangle((int)offhandX, (int)offhandY, 2, slot, SlotBorder);
        Raylib.DrawRectangle((int)offhandX, (int)offhandY + slot - 2, slot, 2, PanelLightBorder);
        Raylib.DrawRectangle((int)offhandX + slot - 2, (int)offhandY, 2, slot, PanelLightBorder);
        if (offhandHover) Raylib.DrawRectangleRec(offhandRec, SlotHover);

        Fonts.Draw("2-я рука [F]", offhandX, offhandY - 18f, 14f, TextDark);

        if (session.Player.OffhandEntry != null) {
            Hud.DrawItemIcon(session.Player.OffhandEntry.Value.Item.Definition, offhandRec, 0.75f);
            if (session.Player.OffhandEntry.Value.Quantity > 1) {
                Fonts.DrawShadowed($"×{session.Player.OffhandEntry.Value.Quantity}", offhandX + 3f, offhandY + slot - 18f, 14f, Color.White);
            }
        }

        // Панель крафта справа.
        DrawCraftPanel(session, px + gridW + 30f, py + 14f, panelW - gridW - 46f, panelH - 90f);

        // Предмет за курсором.
        if (Held.HasValue && Held.Value.Quantity > 0) {
            var held = Held.Value;
            var mouse = Raylib.GetMousePosition();
            Hud.DrawItemIcon(held.Item.Definition, new Rectangle(mouse.X - 14f, mouse.Y - 14f, 28f, 28f), 1f);
            if (held.Quantity > 1) {
                Fonts.Draw($"×{held.Quantity}", mouse.X - 14f, mouse.Y + 8f, 15f, Color.White);
            }
        }

        HandleHeldInput(session, inv);
        DrawTooltip(session, inv);
    }

    private static void DrawTooltip(GameSession session, VoxelFrame.Core.Inventory.Container inv) {
        if (Held.HasValue && Held.Value.Quantity > 0) return;

        var mouse = Raylib.GetMousePosition();
        const int slot = 52, gap = 4;
        int cols = 9, mainRows = 3;
        int gridW = cols * slot + (cols - 1) * gap;
        int panelH = (mainRows + 1) * slot + mainRows * gap + 90;
        int panelW = gridW + 280;
        float px = Raylib.GetScreenWidth() / 2f - panelW / 2f;
        float py = (Raylib.GetScreenHeight() - panelH) / 2f;
        float gridX = px + 16f;
        float gridY = py + 38f;
        float hotbarY = gridY + mainRows * (slot + gap) + 12f;

        VoxelFrame.Core.Inventory.ItemEntry? hoveredEntry = null;

        for (int row = 0; row < mainRows; row++) {
            for (int col = 0; col < cols; col++) {
                int idx = 9 + row * cols + col;
                var rect = new Rectangle(gridX + col * (slot + gap), gridY + row * (slot + gap), slot, slot);
                if (Raylib.CheckCollisionPointRec(mouse, rect)) {
                    hoveredEntry = inv.Slots[idx];
                }
            }
        }
        for (int col = 0; col < cols; col++) {
            var rect = new Rectangle(gridX + col * (slot + gap), hotbarY, slot, slot);
            if (Raylib.CheckCollisionPointRec(mouse, rect)) {
                hoveredEntry = inv.Slots[col];
            }
        }

        // Вторая рука (Offhand slot)
        float offhandX = px + gridW + 30f;
        float offhandY = hotbarY;
        if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(offhandX, offhandY, slot, slot))) {
            hoveredEntry = session.Player.OffhandEntry;
        }

        if (hoveredEntry != null) {
            DrawItemTooltip(hoveredEntry.Value.Item.Definition, mouse);
        }
    }

    private static void DrawSlot(GameSession session, VoxelFrame.Core.Inventory.Container inv, float x, float y, int idx, bool hotbarSelected) {
        var rect = new Rectangle(x, y, 52, 52);
        var mouse = Raylib.GetMousePosition();
        bool hovered = Raylib.CheckCollisionPointRec(mouse, rect);

        Raylib.DrawRectangleRec(rect, SlotBg);
        Raylib.DrawRectangle((int)x, (int)y, 52, 2, SlotBorder);
        Raylib.DrawRectangle((int)x, (int)y, 2, 52, SlotBorder);
        Raylib.DrawRectangle((int)x, (int)y + 50, 52, 2, PanelLightBorder);
        Raylib.DrawRectangle((int)x + 50, (int)y, 2, 52, PanelLightBorder);

        if (hovered) Raylib.DrawRectangleRec(rect, SlotHover);
        if (hotbarSelected) Raylib.DrawRectangleLinesEx(rect, 2.5f, SlotSelected);

        if (idx >= 0 && idx < inv.Slots.Length) {
            var entry = inv.Slots[idx];
            if (entry != null) {
                Hud.DrawItemIcon(entry.Value.Item.Definition, new Rectangle(x + 3f, y + 3f, 46f, 46f), 1f);
                if (entry.Value.Quantity > 1) {
                    Fonts.DrawShadowed($"×{entry.Value.Quantity}", x + 4f, y + 32f, 15f, Color.White);
                }
            }
        }
        _ = session;
    }

    private static void HandleHeldInput(GameSession session, VoxelFrame.Core.Inventory.Container inv) {
        var mouse = Raylib.GetMousePosition();
        const int slot = 52, gap = 4;
        int cols = 9, mainRows = 3;
        int gridW = cols * slot + (cols - 1) * gap;
        int panelH = mainRows * slot + (mainRows - 1) * gap + slot + 120;
        int panelW = gridW + 270;
        float px = Raylib.GetScreenWidth() / 2f - panelW / 2f;
        float py = (Raylib.GetScreenHeight() - panelH) / 2f;
        float gridX = px + 16f;

        if (Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right)) {
            bool right = Raylib.IsMouseButtonPressed(MouseButton.Right);
            bool slotHit = false;
            for (int row = 0; row < mainRows; row++) {
                for (int col = 0; col < cols; col++) {
                    int idx = 9 + row * cols + col;
                    if (SlotClicked(session, inv, gridX + col * (slot + gap), py + 76f + row * (slot + gap), idx, right)) {
                        slotHit = true;
                        break;
                    }
                }
                if (slotHit) break;
            }
            if (!slotHit) {
                float hotbarY = py + 76f + mainRows * (slot + gap);
                for (int col = 0; col < cols; col++) {
                    if (SlotClicked(session, inv, gridX + col * (slot + gap), hotbarY, col, right)) {
                        slotHit = true;
                        break;
                    }
                }

                // Клик по слоту второй руки
                var offhandRec = new Rectangle(px + gridW + 30f, hotbarY, slot, slot);
                if (Raylib.CheckCollisionPointRec(mouse, offhandRec)) {
                    slotHit = true;
                    if (right) {
                        if (Held.HasValue && Held.Value.Quantity > 0) {
                            var held = Held.Value;
                            if (session.Player.OffhandEntry == null) {
                                session.Player.OffhandEntry = held with { Quantity = 1 };
                                Held = held.Quantity > 1 ? held with { Quantity = held.Quantity - 1 } : null;
                            } else if (session.Player.OffhandEntry.Value.Item.Definition.Id == held.Item.Definition.Id && session.Player.OffhandEntry.Value.Quantity < held.Item.Definition.MaxStack) {
                                session.Player.OffhandEntry = session.Player.OffhandEntry.Value with { Quantity = session.Player.OffhandEntry.Value.Quantity + 1 };
                                Held = held.Quantity > 1 ? held with { Quantity = held.Quantity - 1 } : null;
                            }
                        } else if (session.Player.OffhandEntry != null) {
                            int take = (session.Player.OffhandEntry.Value.Quantity + 1) / 2;
                            Held = session.Player.OffhandEntry.Value with { Quantity = take };
                            session.Player.OffhandEntry = session.Player.OffhandEntry.Value with { Quantity = session.Player.OffhandEntry.Value.Quantity - take };
                            if (session.Player.OffhandEntry.Value.Quantity <= 0) session.Player.OffhandEntry = null;
                        }
                    } else {
                        // Left click swap
                        if (!Held.HasValue || Held.Value.Quantity <= 0) {
                            if (session.Player.OffhandEntry != null) {
                                Held = session.Player.OffhandEntry;
                                session.Player.OffhandEntry = null;
                            }
                        } else {
                            var held = Held.Value;
                            if (session.Player.OffhandEntry == null) {
                                session.Player.OffhandEntry = held;
                                Held = null;
                            } else if (session.Player.OffhandEntry.Value.Item.Definition.Id == held.Item.Definition.Id) {
                                int add = Math.Min(session.Player.OffhandEntry.Value.Item.Definition.MaxStack - session.Player.OffhandEntry.Value.Quantity, held.Quantity);
                                session.Player.OffhandEntry = session.Player.OffhandEntry.Value with { Quantity = session.Player.OffhandEntry.Value.Quantity + add };
                                Held = held.Quantity > add ? held with { Quantity = held.Quantity - add } : null;
                            } else {
                                var tmp = session.Player.OffhandEntry;
                                session.Player.OffhandEntry = held;
                                Held = tmp;
                            }
                        }
                    }
                    SoundSystem.PlayPop();
                }
            }

            // Клик за пределами окна инвентаря с предметом в руке — выбросить в мир!
            if (!slotHit && Held.HasValue && Held.Value.Quantity > 0) {
                if (!Raylib.CheckCollisionPointRec(mouse, new Rectangle(px, py, panelW, panelH))) {
                    var dropPos = session.Player.Eye + session.Player.Forward * 0.5f;
                    int dropCount = right ? 1 : Held.Value.Quantity;
                    var pickup = new ItemPickup(Held.Value.Item, dropCount, dropPos) {
                        PickupDelay = 1.2f,
                        Velocity = session.Player.Forward * 4.5f + new Vector3(0f, 2.0f, 0f)
                    };
                    session.World.Pickups.Add(pickup);
                    if (right && Held.Value.Quantity > 1) {
                        Held = Held.Value with { Quantity = Held.Value.Quantity - 1 };
                    } else {
                        Held = null;
                    }
                    return;
                }
            }
        }

        // Выбрасывание предмета клавишей Q при наведении на слот
        if (Raylib.IsKeyPressed(KeyboardKey.Q)) {
            for (int row = 0; row < mainRows; row++) {
                for (int col = 0; col < cols; col++) {
                    int idx = 9 + row * cols + col;
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + col * (slot + gap), py + 76f + row * (slot + gap), 52, 52))) {
                        var slotEntry = inv.Slots[idx];
                        if (slotEntry.HasValue && slotEntry.Value.Quantity > 0) {
                            var dropPos = session.Player.Eye + session.Player.Forward * 0.5f;
                            var pickup = new ItemPickup(slotEntry.Value.Item, 1, dropPos) {
                                PickupDelay = 1.2f,
                                Velocity = session.Player.Forward * 4.5f + new Vector3(0f, 2.0f, 0f)
                            };
                            session.World.Pickups.Add(pickup);
                            if (slotEntry.Value.Quantity > 1) {
                                inv.Slots[idx] = slotEntry.Value with { Quantity = slotEntry.Value.Quantity - 1 };
                            } else {
                                inv.RemoveAt(idx);
                            }
                            return;
                        }
                    }
                }
            }
            float hotbarY = py + 76f + mainRows * (slot + gap);
            for (int col = 0; col < cols; col++) {
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + col * (slot + gap), hotbarY, 52, 52))) {
                    int idx = col;
                    var slotEntry = inv.Slots[idx];
                    if (slotEntry.HasValue && slotEntry.Value.Quantity > 0) {
                        var dropPos = session.Player.Eye + session.Player.Forward * 0.5f;
                        var pickup = new ItemPickup(slotEntry.Value.Item, 1, dropPos) {
                            PickupDelay = 1.2f,
                            Velocity = session.Player.Forward * 4.5f + new Vector3(0f, 2.0f, 0f)
                        };
                        session.World.Pickups.Add(pickup);
                        if (slotEntry.Value.Quantity > 1) {
                            inv.Slots[idx] = slotEntry.Value with { Quantity = slotEntry.Value.Quantity - 1 };
                        } else {
                            inv.RemoveAt(idx);
                        }
                        return;
                    }
                }
            }
        }

        // Swapping with hotbar using 1-9 keys when hovering slots
        int hotbarKey = -1;
        for (int k = 0; k < 9; k++) {
            if (Raylib.IsKeyPressed(KeyboardKey.One + k)) {
                hotbarKey = k;
                break;
            }
        }
        if (hotbarKey >= 0) {
            for (int row = 0; row < mainRows; row++) {
                for (int col = 0; col < cols; col++) {
                    int idx = 9 + row * cols + col;
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + col * (slot + gap), py + 76f + row * (slot + gap), 52, 52))) {
                        var tmp = inv.Slots[idx];
                        inv.Slots[idx] = inv.Slots[hotbarKey];
                        inv.Slots[hotbarKey] = tmp;
                        return;
                    }
                }
            }
            float hotbarY = py + 76f + mainRows * (slot + gap);
            for (int col = 0; col < cols; col++) {
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + col * (slot + gap), hotbarY, 52, 52))) {
                    int idx = col;
                    var tmp = inv.Slots[idx];
                    inv.Slots[idx] = inv.Slots[hotbarKey];
                    inv.Slots[hotbarKey] = tmp;
                    return;
                }
            }
        }
    }

    private static bool SlotClicked(GameSession session, VoxelFrame.Core.Inventory.Container inv,
                                    float x, float y, int idx, bool rightClick) {
        var rect = new Rectangle(x, y, 52, 52);
        var mouse = Raylib.GetMousePosition();
        if (!Raylib.CheckCollisionPointRec(mouse, rect)) return false;

        var entryInSlot = inv.Slots[idx];
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        if (shift && !rightClick && entryInSlot != null) {
            // Shift-Click: быстрое перемещение между хотбаром (0..8) и основным инвентарем (9..35)
            var item = entryInSlot.Value;
            int targetStart = idx < 9 ? 9 : 0;
            int targetEnd = idx < 9 ? 36 : 9;
            inv.RemoveAt(idx);
            int rem = item.Quantity;
            for (int t = targetStart; t < targetEnd && rem > 0; t++) {
                var te = inv.Slots[t];
                if (te != null && te.Value.Item.Definition == item.Item.Definition && te.Value.Quantity < te.Value.Item.Definition.MaxStack) {
                    int add = Math.Min(te.Value.Item.Definition.MaxStack - te.Value.Quantity, rem);
                    inv.InsertAt(t, te.Value with { Quantity = te.Value.Quantity + add });
                    rem -= add;
                }
            }
            for (int t = targetStart; t < targetEnd && rem > 0; t++) {
                if (inv.Slots[t] == null) {
                    int add = Math.Min(item.Item.Definition.MaxStack, rem);
                    inv.InsertAt(t, new ItemEntry(item.Item, add));
                    rem -= add;
                }
            }
            if (rem > 0) {
                inv.InsertAt(idx, item with { Quantity = rem });
            }
            return true;
        }

        if (rightClick) {
            if (Held.HasValue && Held.Value.Quantity > 0) {
                var held = Held.Value;
                // Place 1 from hand
                if (entryInSlot == null) {
                    inv.InsertAt(idx, new ItemEntry(held.Item, 1));
                    Held = held with { Quantity = held.Quantity - 1 };
                    if (Held.Value.Quantity <= 0) Held = null;
                } else if (entryInSlot.Value.Item.Definition == held.Item.Definition) {
                    int current = entryInSlot.Value.Quantity;
                    if (current < entryInSlot.Value.Item.Definition.MaxStack) {
                        inv.InsertAt(idx, entryInSlot.Value with { Quantity = current + 1 });
                        Held = held with { Quantity = held.Quantity - 1 };
                        if (Held.Value.Quantity <= 0) Held = null;
                    }
                }
            } else if (entryInSlot != null) {
                // Take half from slot
                int qty = entryInSlot.Value.Quantity;
                if (qty > 0) {
                    int take = (qty + 1) / 2;
                    Held = entryInSlot.Value with { Quantity = take };
                    inv.RemoveAt(idx);
                    if (qty - take > 0) {
                        inv.InsertAt(idx, entryInSlot.Value with { Quantity = qty - take });
                    }
                }
            }
            return true;
        }

        // Left click
        if (!Held.HasValue || Held.Value.Quantity <= 0) {
            // Take all
            if (entryInSlot != null) {
                Held = entryInSlot;
                inv.RemoveAt(idx);
            }
        } else {
            var heldItem = Held.Value;
            if (entryInSlot == null) {
                // Place all
                inv.InsertAt(idx, heldItem);
                Held = null;
            } else if (entryInSlot.Value.Item.Definition == heldItem.Item.Definition) {
                // Merge all up to 64
                int current = entryInSlot.Value.Quantity;
                if (current < entryInSlot.Value.Item.Definition.MaxStack) {
                    int add = Math.Min(entryInSlot.Value.Item.Definition.MaxStack - current, heldItem.Quantity);
                    inv.InsertAt(idx, entryInSlot.Value with { Quantity = current + add });
                    if (heldItem.Quantity - add > 0) {
                        Held = heldItem with { Quantity = heldItem.Quantity - add };
                    } else {
                        Held = null;
                    }
                }
            } else {
                // Swap items
                inv.RemoveAt(idx);
                inv.InsertAt(idx, heldItem);
                Held = entryInSlot;
            }
        }
        return true;
    }

    // ── Крафт-сетки ──────────────────────────────────────────────────────────

    // Личный 2×2 крафт (в инвентаре)
    private static readonly ItemEntry?[] PersonalGrid = new ItemEntry?[4];
    // 3×3 верстак
    private static readonly ItemEntry?[] WorkbenchGrid = new ItemEntry?[9];

    public static void ClearCraftingGrids() {
        for (int i = 0; i < 4; i++) PersonalGrid[i] = null;
        for (int i = 0; i < 9; i++) WorkbenchGrid[i] = null;
    }

    public static void DrawCrafting(GameSession session) {
        DrawInventory(session);
    }

    // ── Личный 2×2 крафт в инвентаре ─────────────────────────────────────────
    private static void DrawCraftPanel(GameSession session, float panelX, float panelY, float panelW, float panelH) {
        const int slotSz = 44, gap = 4;
        var mouse = Raylib.GetMousePosition();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool rightClick = Raylib.IsMouseButtonPressed(MouseButton.Right);

        // Заголовок
        Fonts.Draw("Создание", panelX + 8f, panelY, 16f, TextDark);

        // 2×2 сетка ингредиентов
        float gridX = panelX + 8f;
        float gridY = panelY + 24f;
        for (int r = 0; r < 2; r++) {
            for (int c = 0; c < 2; c++) {
                int idx = r * 2 + c;
                float sx = gridX + c * (slotSz + gap);
                float sy = gridY + r * (slotSz + gap);
                DrawCraftSlot(PersonalGrid, idx, sx, sy, slotSz, mouse, leftClick, rightClick);
            }
        }

        // Стрелка
        float arrowX = gridX + 2 * (slotSz + gap) + 6f;
        float arrowMidY = gridY + (slotSz + gap) / 2f + slotSz / 2f - 8f;
        Fonts.Draw("→", arrowX, arrowMidY, 22f, new Color(80, 80, 80, 255));

        // Слот результата
        float resultX = arrowX + 26f;
        float resultY = gridY + (slotSz + gap) / 2f - slotSz / 2f + 2f;

        // Вычисляем результат 2×2 → expand to 3×3
        var grid3 = new ItemDefinition?[9];
        grid3[0] = PersonalGrid[0]?.Item.Definition; grid3[1] = PersonalGrid[1]?.Item.Definition;
        grid3[3] = PersonalGrid[2]?.Item.Definition; grid3[4] = PersonalGrid[3]?.Item.Definition;
        string key2x2 = GameData.NormalizeGrid(grid3);
        bool hasResult = GameData.ShapeRecipes.TryGetValue(key2x2, out var craftResult2);

        var resultRect = new Rectangle(resultX, resultY, slotSz, slotSz);
        bool resultHovered = Raylib.CheckCollisionPointRec(mouse, resultRect);
        Color resultBg = hasResult ? (resultHovered ? new Color(80, 110, 80, 255) : new Color(52, 68, 52, 255)) : SlotBg;
        Raylib.DrawRectangleRounded(resultRect, 0.12f, 6, resultBg);
        Raylib.DrawRectangleRoundedLinesEx(resultRect, 0.12f, 6, 1.5f, hasResult ? new Color(100, 200, 100, 255) : SlotBorder);

        if (hasResult) {
            Hud.DrawItemIcon(craftResult2.Item, new Rectangle(resultX + 3f, resultY + 3f, slotSz - 6f, slotSz - 6f), 1f);
            if (craftResult2.Count > 1)
                Fonts.DrawShadowed($"×{craftResult2.Count}", resultX + 3f, resultY + slotSz - 16f, 14f, Color.White);

            if (leftClick && resultHovered) {
                bool canTake = false;
                if (!Held.HasValue || Held.Value.Quantity <= 0) {
                    Held = new ItemEntry(GameData.NewItem(craftResult2.Item), craftResult2.Count);
                    canTake = true;
                } else if (Held.Value.Item.Definition.Id == craftResult2.Item.Id && Held.Value.Quantity + craftResult2.Count <= 64) {
                    Held = Held.Value with { Quantity = Held.Value.Quantity + craftResult2.Count };
                    canTake = true;
                }

                if (canTake) {
                    for (int i = 0; i < 4; i++) {
                        if (PersonalGrid[i].HasValue && PersonalGrid[i]!.Value.Quantity > 0) {
                            int rem = PersonalGrid[i]!.Value.Quantity - 1;
                            PersonalGrid[i] = rem > 0 ? PersonalGrid[i]!.Value with { Quantity = rem } : null;
                        }
                    }
                    session.AddMessage($"Создано: {craftResult2.Item.Name}");
                }
            }
        }

        // Книга рецептов в инвентаре (нижняя половина панели крафта)
        float bookY = gridY + 2 * (slotSz + gap) + 6f;
        DrawRecipeBookSection(session, panelX + 4f, bookY, panelW - 8f, panelH - (bookY - panelY) - 4f, PersonalGrid, false);
    }

    // ── Книга рецептов ───────────────────────────────────────────────────────

    public record struct RecipeBookEntry(
        string Name,
        ItemDefinition Output,
        int Count,
        (ItemDefinition Item, int Count)[] Ingredients,
        ItemDefinition?[]? Shape,
        bool Needs3x3,
        bool IsSmelt = false);

    private static int _recipeScroll = 0;

    private static readonly RecipeBookEntry[] AllRecipes = new RecipeBookEntry[] {
        // Базовые
        new("Доски (4 шт)", GameData.PlankItem, 4, new[] { (GameData.LogItem, 1) }, new ItemDefinition?[] { GameData.LogItem, null, null, null }, false),
        new("Палки (4 шт)", GameData.StickItem, 4, new[] { (GameData.PlankItem, 2) }, new ItemDefinition?[] { GameData.PlankItem, null, GameData.PlankItem, null }, false),
        new("Верстак", GameData.WorkbenchItem, 1, new[] { (GameData.PlankItem, 4) }, new ItemDefinition?[] { GameData.PlankItem, GameData.PlankItem, GameData.PlankItem, GameData.PlankItem }, false),
        new("Факелы (4 шт)", GameData.TorchItem, 4, new[] { (GameData.CoalItem, 1), (GameData.StickItem, 1) }, new ItemDefinition?[] { GameData.CoalItem, null, GameData.StickItem, null }, false),
        new("Сундук", GameData.ChestItem, 1, new[] { (GameData.PlankItem, 8) }, new ItemDefinition?[] { GameData.PlankItem, GameData.PlankItem, GameData.PlankItem, GameData.PlankItem, null, GameData.PlankItem, GameData.PlankItem, GameData.PlankItem, GameData.PlankItem }, true),
        new("Печка", GameData.FurnaceItem, 1, new[] { (GameData.CobblestoneItem, 8) }, new ItemDefinition?[] { GameData.CobblestoneItem, GameData.CobblestoneItem, GameData.CobblestoneItem, GameData.CobblestoneItem, null, GameData.CobblestoneItem, GameData.CobblestoneItem, GameData.CobblestoneItem, GameData.CobblestoneItem }, true),
        new("Кровать", GameData.BedItem, 1, new[] { (GameData.WhiteWoolItem, 3), (GameData.PlankItem, 3) }, new ItemDefinition?[] { GameData.WhiteWoolItem, GameData.WhiteWoolItem, GameData.WhiteWoolItem, GameData.PlankItem, GameData.PlankItem, GameData.PlankItem, null, null, null }, true),

        // Пища и выживание
        new("Хлеб", GameData.BreadItem, 1, new[] { (GameData.WheatItem, 3) }, new ItemDefinition?[] { GameData.WheatItem, GameData.WheatItem, GameData.WheatItem, null, null, null, null, null, null }, true),
        new("Костная мука (3 шт)", GameData.BoneMealItem, 3, new[] { (GameData.BoneItem, 1) }, new ItemDefinition?[] { GameData.BoneItem, null, null, null }, false),
        new("Опилки (4 шт)", GameData.SawdustItem, 4, new[] { (GameData.LogItem, 1) }, new ItemDefinition?[] { GameData.LogItem, null, null, null }, false),
        new("Каша из опилок", GameData.SawdustPorridgeItem, 1, new[] { (GameData.SawdustItem, 2), (GameData.PlankItem, 1), (GameData.WheatSeedsItem, 1) }, new ItemDefinition?[] { GameData.SawdustItem, GameData.SawdustItem, GameData.PlankItem, GameData.WheatSeedsItem }, false),
        new("Тотем бессмертия", GameData.TotemItem, 1, new[] { (GameData.BoneItem, 7), (GameData.GoldIngotItem, 1) }, new ItemDefinition?[] { GameData.BoneItem, GameData.GoldIngotItem, GameData.BoneItem, GameData.BoneItem, GameData.BoneItem, GameData.BoneItem, null, GameData.BoneItem, null }, true),

        // Инструменты: Кирки
        new("Деревянная кирка", GameData.WoodPickaxeItem, 1, new[] { (GameData.PlankItem, 3), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.PlankItem, GameData.PlankItem, GameData.PlankItem, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Каменная кирка", GameData.StonePickaxeItem, 1, new[] { (GameData.CobblestoneItem, 3), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.CobblestoneItem, GameData.CobblestoneItem, GameData.CobblestoneItem, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Железная кирка", GameData.IronPickaxeItem, 1, new[] { (GameData.IronIngotItem, 3), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.IronIngotItem, GameData.IronIngotItem, GameData.IronIngotItem, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Алмазная кирка", GameData.DiamondPickaxeItem, 1, new[] { (GameData.DiamondItem, 3), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.DiamondItem, GameData.DiamondItem, GameData.DiamondItem, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Золотая кирка", GameData.GoldPickaxeItem, 1, new[] { (GameData.GoldIngotItem, 3), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.GoldIngotItem, GameData.GoldIngotItem, GameData.GoldIngotItem, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),

        // Топоры
        new("Деревянный топор", GameData.WoodAxeItem, 1, new[] { (GameData.PlankItem, 3), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.PlankItem, GameData.PlankItem, null, GameData.PlankItem, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Каменный топор", GameData.StoneAxeItem, 1, new[] { (GameData.CobblestoneItem, 3), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.CobblestoneItem, GameData.CobblestoneItem, null, GameData.CobblestoneItem, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Железный топор", GameData.IronAxeItem, 1, new[] { (GameData.IronIngotItem, 3), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.IronIngotItem, GameData.IronIngotItem, null, GameData.IronIngotItem, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Алмазный топор", GameData.DiamondAxeItem, 1, new[] { (GameData.DiamondItem, 3), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.DiamondItem, GameData.DiamondItem, null, GameData.DiamondItem, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Золотой топор", GameData.GoldAxeItem, 1, new[] { (GameData.GoldIngotItem, 3), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.GoldIngotItem, GameData.GoldIngotItem, null, GameData.GoldIngotItem, GameData.StickItem, null, null, GameData.StickItem, null }, true),

        // Мечи
        new("Деревянный меч", GameData.WoodSwordItem, 1, new[] { (GameData.PlankItem, 2), (GameData.StickItem, 1) }, new ItemDefinition?[] { null, GameData.PlankItem, null, null, GameData.PlankItem, null, null, GameData.StickItem, null }, true),
        new("Каменный меч", GameData.StoneSwordItem, 1, new[] { (GameData.CobblestoneItem, 2), (GameData.StickItem, 1) }, new ItemDefinition?[] { null, GameData.CobblestoneItem, null, null, GameData.CobblestoneItem, null, null, GameData.StickItem, null }, true),
        new("Железный меч", GameData.IronSwordItem, 1, new[] { (GameData.IronIngotItem, 2), (GameData.StickItem, 1) }, new ItemDefinition?[] { null, GameData.IronIngotItem, null, null, GameData.IronIngotItem, null, null, GameData.StickItem, null }, true),
        new("Алмазный меч", GameData.DiamondSwordItem, 1, new[] { (GameData.DiamondItem, 2), (GameData.StickItem, 1) }, new ItemDefinition?[] { null, GameData.DiamondItem, null, null, GameData.DiamondItem, null, null, GameData.StickItem, null }, true),
        new("Золотой меч", GameData.GoldSwordItem, 1, new[] { (GameData.GoldIngotItem, 2), (GameData.StickItem, 1) }, new ItemDefinition?[] { null, GameData.GoldIngotItem, null, null, GameData.GoldIngotItem, null, null, GameData.StickItem, null }, true),

        // Лопаты
        new("Деревянная лопата", GameData.WoodShovelItem, 1, new[] { (GameData.PlankItem, 1), (GameData.StickItem, 2) }, new ItemDefinition?[] { null, GameData.PlankItem, null, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Каменная лопата", GameData.StoneShovelItem, 1, new[] { (GameData.CobblestoneItem, 1), (GameData.StickItem, 2) }, new ItemDefinition?[] { null, GameData.CobblestoneItem, null, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Железная лопата", GameData.IronShovelItem, 1, new[] { (GameData.IronIngotItem, 1), (GameData.StickItem, 2) }, new ItemDefinition?[] { null, GameData.IronIngotItem, null, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Алмазная лопата", GameData.DiamondShovelItem, 1, new[] { (GameData.DiamondItem, 1), (GameData.StickItem, 2) }, new ItemDefinition?[] { null, GameData.DiamondItem, null, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Золотая лопата", GameData.GoldShovelItem, 1, new[] { (GameData.GoldIngotItem, 1), (GameData.StickItem, 2) }, new ItemDefinition?[] { null, GameData.GoldIngotItem, null, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),

        // Оружие, охота и снаряжение
        new("Кремень", GameData.FlintItem, 1, new[] { (GameData.GravelItem, 3) }, new ItemDefinition?[] { GameData.GravelItem, GameData.GravelItem, GameData.GravelItem, null, null, null, null, null, null }, true),
        new("Огниво", GameData.FlintAndSteelItem, 1, new[] { (GameData.IronIngotItem, 1), (GameData.FlintItem, 1) }, new ItemDefinition?[] { GameData.IronIngotItem, null, null, null, GameData.FlintItem, null, null, null, null }, false),
        new("Ведро", GameData.BucketItem, 1, new[] { (GameData.IronIngotItem, 3) }, new ItemDefinition?[] { GameData.IronIngotItem, null, GameData.IronIngotItem, null, GameData.IronIngotItem, null, null, null, null }, true),
        new("Лук", GameData.BowItem, 1, new[] { (GameData.StickItem, 3), (GameData.StringItem, 3) }, new ItemDefinition?[] { null, GameData.StickItem, GameData.StringItem, GameData.StickItem, null, GameData.StringItem, null, GameData.StickItem, GameData.StringItem }, true),
        new("Стрелы (4 шт)", GameData.ArrowItem, 4, new[] { (GameData.FlintItem, 1), (GameData.StickItem, 1), (GameData.FeatherItem, 1) }, new ItemDefinition?[] { GameData.FlintItem, null, null, GameData.StickItem, null, null, GameData.FeatherItem, null, null }, true),
        new("Щит", GameData.ShieldItem, 1, new[] { (GameData.IronIngotItem, 1), (GameData.PlankItem, 6) }, new ItemDefinition?[] { GameData.PlankItem, GameData.IronIngotItem, GameData.PlankItem, GameData.PlankItem, GameData.PlankItem, GameData.PlankItem, null, GameData.PlankItem, null }, true),
        new("Динамит", GameData.TNTItem, 1, new[] { (GameData.GunpowderItem, 5), (GameData.SandItem, 4) }, new ItemDefinition?[] { GameData.GunpowderItem, GameData.SandItem, GameData.GunpowderItem, GameData.SandItem, GameData.GunpowderItem, GameData.SandItem, GameData.GunpowderItem, GameData.SandItem, GameData.GunpowderItem }, true),

        // Мотыги
        new("Деревянная мотыга", GameData.WoodHoeItem, 1, new[] { (GameData.PlankItem, 2), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.PlankItem, GameData.PlankItem, null, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Каменная мотыга", GameData.StoneHoeItem, 1, new[] { (GameData.CobblestoneItem, 2), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.CobblestoneItem, GameData.CobblestoneItem, null, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Железная мотыга", GameData.IronHoeItem, 1, new[] { (GameData.IronIngotItem, 2), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.IronIngotItem, GameData.IronIngotItem, null, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Алмазная мотыга", GameData.DiamondHoeItem, 1, new[] { (GameData.DiamondItem, 2), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.DiamondItem, GameData.DiamondItem, null, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),
        new("Золотая мотыга", GameData.GoldHoeItem, 1, new[] { (GameData.GoldIngotItem, 2), (GameData.StickItem, 2) }, new ItemDefinition?[] { GameData.GoldIngotItem, GameData.GoldIngotItem, null, null, GameData.StickItem, null, null, GameData.StickItem, null }, true),

        // Прочие блоки и предметы
        new("Шерсть", GameData.WhiteWoolItem, 1, new[] { (GameData.StringItem, 4) }, new ItemDefinition?[] { GameData.StringItem, GameData.StringItem, GameData.StringItem, GameData.StringItem }, false),
        new("Деревянная дверь (3 шт)", GameData.DoorItem, 3, new[] { (GameData.PlankItem, 6) }, new ItemDefinition?[] { GameData.PlankItem, GameData.PlankItem, null, GameData.PlankItem, GameData.PlankItem, null, GameData.PlankItem, GameData.PlankItem, null }, true),
        new("Золотое яблоко", GameData.GoldenAppleItem, 1, new[] { (GameData.GoldIngotItem, 8), (GameData.AppleItem, 1) }, new ItemDefinition?[] { GameData.GoldIngotItem, GameData.GoldIngotItem, GameData.GoldIngotItem, GameData.GoldIngotItem, GameData.AppleItem, GameData.GoldIngotItem, GameData.GoldIngotItem, GameData.GoldIngotItem, GameData.GoldIngotItem }, true),
        new("Светокамень", GameData.GlowstoneItem, 1, new[] { (GameData.GlowstoneDustItem, 4) }, new ItemDefinition?[] { GameData.GlowstoneDustItem, GameData.GlowstoneDustItem, GameData.GlowstoneDustItem, GameData.GlowstoneDustItem }, false),
        new("Рельсы (16 шт)", GameData.RailItem, 16, new[] { (GameData.IronIngotItem, 6), (GameData.StickItem, 1) }, new ItemDefinition?[] { GameData.IronIngotItem, null, GameData.IronIngotItem, GameData.IronIngotItem, GameData.StickItem, GameData.IronIngotItem, GameData.IronIngotItem, null, GameData.IronIngotItem }, true),
        new("Факелы (4 шт, древесный уголь)", GameData.TorchItem, 4, new[] { (GameData.CharcoalItem, 1), (GameData.StickItem, 1) }, new ItemDefinition?[] { GameData.CharcoalItem, null, GameData.StickItem, null }, false),
        new("Порох ифрита (2 шт)", GameData.BlazePowderItem, 2, new[] { (GameData.BlazeRodItem, 1) }, new ItemDefinition?[] { GameData.BlazeRodItem, null, null, null }, false),
        new("Око Эндера", GameData.EyeOfEnderItem, 1, new[] { (GameData.BlazePowderItem, 1), (GameData.EnderPearlItem, 1) }, new ItemDefinition?[] { GameData.BlazePowderItem, null, GameData.EnderPearlItem, null }, false),

        // Плавка в печке
        new("Железный слиток", GameData.IronIngotItem, 1, new[] { (GameData.IronOreItem, 1) }, null, false, IsSmelt: true),
        new("Золотой слиток", GameData.GoldIngotItem, 1, new[] { (GameData.GoldOreItem, 1) }, null, false, IsSmelt: true),
        new("Стекло", GameData.GlassItem, 1, new[] { (GameData.SandItem, 1) }, null, false, IsSmelt: true),
        new("Уголь", GameData.CoalItem, 1, new[] { (GameData.CoalOreItem, 1) }, null, false, IsSmelt: true),
        new("Камень", GameData.StoneItem, 1, new[] { (GameData.CobblestoneItem, 1) }, null, false, IsSmelt: true),
        new("Древесный уголь", GameData.CharcoalItem, 1, new[] { (GameData.LogItem, 1) }, null, false, IsSmelt: true),
        new("Жареная свинина", GameData.CookedPorkItem, 1, new[] { (GameData.RawPorkItem, 1) }, null, false, IsSmelt: true),
        new("Жареный стейк", GameData.CookedBeefItem, 1, new[] { (GameData.RawBeefItem, 1) }, null, false, IsSmelt: true),
        new("Жареная баранина", GameData.CookedMuttonItem, 1, new[] { (GameData.RawMuttonItem, 1) }, null, false, IsSmelt: true),
    };

    private static void DrawRecipeBookSection(GameSession session, float rx, float ry, float rw, float rh, ItemEntry?[] targetGrid, bool is3x3) {
        var mouse = Raylib.GetMousePosition();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        var inv = session.Player.Inventory;

        var bookRect = new Rectangle(rx, ry, rw, rh);
        Raylib.DrawRectangleRounded(bookRect, 0.08f, 6, new Color(20, 26, 32, 230));
        Raylib.DrawRectangleRoundedLinesEx(bookRect, 0.08f, 6, 1.5f, new Color(40, 140, 60, 220));

        Fonts.Draw("📖 Книга рецептов", rx + 8f, ry + 6f, 15f, new Color(100, 230, 120, 255));

        // Скролл колесом мыши
        if (Raylib.CheckCollisionPointRec(mouse, bookRect)) {
            int wheel = (int)Raylib.GetMouseWheelMove();
            if (wheel != 0) {
                _recipeScroll = Math.Clamp(_recipeScroll - wheel, 0, Math.Max(0, AllRecipes.Length - 3));
            }
        }

        float itemH = 42f;
        float itemY = ry + 28f;
        int maxVisible = (int)((rh - 32f) / itemH);

        for (int i = _recipeScroll; i < Math.Min(AllRecipes.Length, _recipeScroll + maxVisible); i++) {
            var r = AllRecipes[i];
            bool canCraft = true;
            foreach (var (reqItem, reqCount) in r.Ingredients) {
                if (inv.CountOf(reqItem) < reqCount) {
                    canCraft = false;
                    break;
                }
            }
            if (r.Needs3x3 && !is3x3) canCraft = false;

            var itemRec = new Rectangle(rx + 5f, itemY, rw - 10f, itemH - 3f);
            bool hov = Raylib.CheckCollisionPointRec(mouse, itemRec);

            Color bgCol = hov ? (canCraft ? new Color(40, 80, 50, 240) : new Color(60, 40, 40, 220))
                            : (canCraft ? new Color(30, 55, 38, 200) : new Color(30, 32, 38, 160));
            Raylib.DrawRectangleRounded(itemRec, 0.15f, 4, bgCol);
            Raylib.DrawRectangleRoundedLinesEx(itemRec, 0.15f, 4, 1f, canCraft ? new Color(80, 180, 90, 200) : new Color(60, 65, 75, 180));

            // Иконка
            Hud.DrawItemIcon(r.Output, new Rectangle(itemRec.X + 3f, itemRec.Y + 3f, 32f, 32f), 0.95f);
            if (r.Count > 1) {
                Fonts.DrawShadowed($"×{r.Count}", itemRec.X + 18f, itemRec.Y + 18f, 11f, Color.White);
            }

            // Название
            Color nameCol = canCraft ? Color.White : new Color(160, 165, 175, 200);
            Fonts.Draw(r.Name, itemRec.X + 38f, itemRec.Y + 4f, 13f, nameCol);

            // Стоимость или тег верстака / печки
            string costText = r.IsSmelt ? "[Плавка в печке]"
                : r.Needs3x3 && !is3x3 ? "[Верстак 3×3]"
                : string.Join(" + ", r.Ingredients.Select(ing => $"{ing.Count}×{ing.Item.Name[..Math.Min(5, ing.Item.Name.Length)]}"));
            Color costCol = canCraft ? new Color(160, 230, 170, 220) : new Color(200, 120, 120, 200);
            Fonts.Draw(costText, itemRec.X + 38f, itemRec.Y + 22f, 11f, costCol);

            if (hov && leftClick) {
                if (r.IsSmelt) {
                    var fuel = r.Ingredients[0].Item;
                    session.AddMessage($"Плавка: поместите {fuel.Name} в верхнюю ячейку печки, а топливо (уголь, бревно и т.п.) — в нижнюю.");
                } else if (r.Needs3x3 && !is3x3) {
                    session.AddMessage("Этот предмет можно скрафтить только на верстаке 3×3!");
                } else if (!canCraft) {
                    session.AddMessage($"Не хватает ингредиентов для создания: {r.Name}");
                } else {
                    AutoFillRecipe(session, r, targetGrid, is3x3);
                }
            }

            itemY += itemH;
        }
    }

    private static void AutoFillRecipe(GameSession session, RecipeBookEntry recipe, ItemEntry?[] grid, bool is3x3) {
        var inv = session.Player.Inventory;
        int gridSize = is3x3 ? 3 : 2;

        var shape = recipe.Shape;
        if (shape == null) return; // рецепты плавки в сетку крафта не раскладываются
        int shapeW = recipe.Needs3x3 ? 3 : 2;
        int shapeH = recipe.Needs3x3 ? 3 : 2;

        // Найдём bounding box непустых ячеек в shape
        int minR = shapeH, maxR = -1, minC = shapeW, maxC = -1;
        for (int r = 0; r < shapeH; r++) {
            for (int c = 0; c < shapeW; c++) {
                if (r * shapeW + c < shape.Length && shape[r * shapeW + c] != null) {
                    if (r < minR) minR = r;
                    if (r > maxR) maxR = r;
                    if (c < minC) minC = c;
                    if (c > maxC) maxC = c;
                }
            }
        }

        if (maxR < 0) return; // пустой шаблон

        int bbH = maxR - minR + 1;
        int bbW = maxC - minC + 1;

        if (!is3x3 && (bbW > 2 || bbH > 2 || recipe.Needs3x3)) {
            session.AddMessage("Этот рецепт требует Верстак 3×3!");
            return;
        }

        // Проверяем наличие ингредиентов
        foreach (var (reqItem, reqCount) in recipe.Ingredients) {
            if (inv.CountOf(reqItem) < reqCount) {
                session.AddMessage($"Не хватает: {reqItem.Name} ({inv.CountOf(reqItem)}/{reqCount})");
                return;
            }
        }

        // Возвращаем текущие предметы из сетки в инвентарь
        for (int i = 0; i < grid.Length; i++) {
            if (grid[i].HasValue && grid[i]!.Value.Quantity > 0) {
                inv.TryInsert(grid[i]!.Value.Item, grid[i]!.Value.Quantity);
                grid[i] = null;
            }
        }

        // Выровнять по верхнему левому углу целевой сетки
        bool placed = false;
        for (int r = 0; r < bbH && r < gridSize; r++) {
            for (int c = 0; c < bbW && c < gridSize; c++) {
                int shapeIdx = (minR + r) * shapeW + (minC + c);
                if (shapeIdx >= shape.Length) continue;
                var needed = shape[shapeIdx];
                if (needed != null) {
                    int gridIdx = r * gridSize + c;
                    if (gridIdx < grid.Length && inv.TryRemove(needed, 1)) {
                        grid[gridIdx] = new ItemEntry(GameData.NewItem(needed), 1);
                        placed = true;
                    }
                }
            }
        }

        if (placed) {
            SoundSystem.PlayPop();
            session.AddMessage($"Выложен рецепт: {recipe.Name}");
        }
    }

    // ── 3×3 экран верстака ───────────────────────────────────────────────────

    public static void DrawWorkbench(GameSession session) {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        var inv = session.Player.Inventory;
        var mouse = Raylib.GetMousePosition();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool rightClick = Raylib.IsMouseButtonPressed(MouseButton.Right);

        const int slotSz = 48, gap = 4;
        int gridW3 = 3 * slotSz + 2 * gap;
        int panelW = gridW3 + 340;
        int invGridW = 9 * 52 + 8 * 4;
        panelW = Math.Max(panelW, invGridW + 32);
        int panelH = 3 * slotSz + 2 * gap + 4 * 52 + 3 * 4 + 130;

        float px = w / 2f - panelW / 2f, py = (h - panelH) / 2f;
        DrawPanel(px, py, panelW, panelH);

        Fonts.DrawCentered("ВЕРСТАК", w / 2f, py + 8f, 26f, TextDark);
        Fonts.DrawCentered("3×3 крафт · ЛКМ — взять/положить · ПКМ — положить 1", w / 2f, py + 36f, 14f, new Color(90, 90, 90, 255));

        float gridX = px + 20f;
        float gridY = py + 62f;

        // 3×3 сетка
        for (int r = 0; r < 3; r++) {
            for (int c = 0; c < 3; c++) {
                int idx = r * 3 + c;
                float sx = gridX + c * (slotSz + gap);
                float sy = gridY + r * (slotSz + gap);
                DrawCraftSlot(WorkbenchGrid, idx, sx, sy, slotSz, mouse, leftClick, rightClick);
            }
        }

        // Стрелка + результат
        float arrowX = gridX + gridW3 + 10f;
        float arrowY = gridY + slotSz + gap + slotSz / 2f - 10f;
        Fonts.Draw("→", arrowX, arrowY, 28f, new Color(200, 200, 200, 255));

        float resultX = arrowX + 36f;
        float resultY = gridY + slotSz + gap;
        var gridDef3 = new ItemDefinition?[9];
        for (int i = 0; i < 9; i++) gridDef3[i] = WorkbenchGrid[i]?.Item.Definition;
        string key3 = GameData.NormalizeGrid(gridDef3);
        bool hasResult = GameData.ShapeRecipes.TryGetValue(key3, out var craftResult3);

        var resultRect = new Rectangle(resultX, resultY, slotSz, slotSz);
        bool resultHov = Raylib.CheckCollisionPointRec(mouse, resultRect);
        Color resBg = hasResult ? (resultHov ? new Color(80, 110, 80, 255) : new Color(52, 68, 52, 255)) : SlotBg;
        Raylib.DrawRectangleRounded(resultRect, 0.12f, 6, resBg);
        Raylib.DrawRectangleRoundedLinesEx(resultRect, 0.12f, 6, 1.5f, hasResult ? new Color(100, 200, 100, 255) : SlotBorder);

        if (hasResult) {
            Hud.DrawItemIcon(craftResult3.Item, new Rectangle(resultX + 3f, resultY + 3f, slotSz - 6f, slotSz - 6f), 1f);
            if (craftResult3.Count > 1)
                Fonts.DrawShadowed($"×{craftResult3.Count}", resultX + 3f, resultY + slotSz - 16f, 14f, Color.White);

            if (leftClick && resultHov) {
                bool canTake = false;
                if (!Held.HasValue || Held.Value.Quantity <= 0) {
                    Held = new ItemEntry(GameData.NewItem(craftResult3.Item), craftResult3.Count);
                    canTake = true;
                } else if (Held.Value.Item.Definition.Id == craftResult3.Item.Id && Held.Value.Quantity + craftResult3.Count <= 64) {
                    Held = Held.Value with { Quantity = Held.Value.Quantity + craftResult3.Count };
                    canTake = true;
                }

                if (canTake) {
                    for (int i = 0; i < 9; i++) {
                        if (WorkbenchGrid[i].HasValue && WorkbenchGrid[i]!.Value.Quantity > 0) {
                            int rem = WorkbenchGrid[i]!.Value.Quantity - 1;
                            WorkbenchGrid[i] = rem > 0 ? WorkbenchGrid[i]!.Value with { Quantity = rem } : null;
                        }
                    }
                    session.AddMessage($"Создано: {craftResult3.Item.Name}");
                }
            }
        }

        // Книга рецептов верстака (справа от 3×3 сетки)
        float bookX = resultX + slotSz + 14f;
        float bookW = px + panelW - bookX - 14f;
        float bookH = 3 * (slotSz + gap) + 12f;
        DrawRecipeBookSection(session, bookX, gridY - 6f, bookW, bookH, WorkbenchGrid, true);

        // Инвентарь снизу
        float invY = gridY + 3 * (slotSz + gap) + 20f;
        float invX = px + 16f;
        for (int row = 0; row < 3; row++) {
            for (int col = 0; col < 9; col++) {
                int idx = 9 + row * 9 + col;
                DrawSlot(session, inv, invX + col * 56f, invY + row * 56f, idx, false);
            }
        }
        float hotY = invY + 3 * 56f + 4f;
        for (int col = 0; col < 9; col++)
            DrawSlot(session, inv, invX + col * 56f, hotY, col, col == session.Player.SelectedSlot);

        // Предмет за курсором
        DrawHeldItem();
        HandleWorkbenchInput(session, inv, invX, invY, hotY);
        DrawWorkbenchTooltip(session, inv, invX, invY, hotY, gridX, gridY, slotSz, gap);
    }

    private static void HandleWorkbenchInput(GameSession session, VoxelFrame.Core.Inventory.Container inv,
                                              float invX, float invY, float hotY) {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left) && !Raylib.IsMouseButtonPressed(MouseButton.Right)) return;
        bool right = Raylib.IsMouseButtonPressed(MouseButton.Right);
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 9; col++)
                if (SlotClicked(session, inv, invX + col * 56f, invY + row * 56f, 9 + row * 9 + col, right)) return;
        for (int col = 0; col < 9; col++)
            if (SlotClicked(session, inv, invX + col * 56f, hotY, col, right)) return;
    }

    private static void DrawWorkbenchTooltip(GameSession session, VoxelFrame.Core.Inventory.Container inv,
                                              float invX, float invY, float hotY,
                                              float gridX, float gridY, int slotSz, int gap) {
        if (Held.HasValue && Held.Value.Quantity > 0) return;
        var mouse = Raylib.GetMousePosition();
        ItemDefinition? def = null;
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + c*(slotSz+gap), gridY + r*(slotSz+gap), slotSz, slotSz)))
                    def = WorkbenchGrid[r*3+c]?.Item.Definition;
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 9; col++) {
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(invX + col*56f, invY + row*56f, 52, 52))) {
                    var e = inv.Slots[9 + row*9 + col];
                    if (e.HasValue) def = e.Value.Item.Definition;
                }
            }
        for (int col = 0; col < 9; col++) {
            if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(invX + col*56f, hotY, 52, 52))) {
                var e = inv.Slots[col];
                if (e.HasValue) def = e.Value.Item.Definition;
            }
        }
        if (def != null) DrawItemTooltip(def, mouse);
    }

    // ── Экран печки (Автономная фоновая плавка) ───────────────────────────────

    public static void DrawFurnaceUI(GameSession session) {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        var inv = session.Player.Inventory;
        var mouse = Raylib.GetMousePosition();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool rightClick = Raylib.IsMouseButtonPressed(MouseButton.Right);

        var furnace = session.World.GetOrCreateFurnace(session.ActiveFurnacePos);

        const int slotSz = 52;
        int panelW = 9 * 56 + 32;
        int panelH = 4 * 56 + 180;
        float px = w / 2f - panelW / 2f, py = (h - panelH) / 2f;
        DrawPanel(px, py, panelW, panelH);

        Fonts.DrawCentered("ПЕЧКА", w / 2f, py + 8f, 26f, TextDark);
        Fonts.DrawCentered("Сырьё (вверху) + Топливо (внизу) → Результат (справа)", w / 2f, py + 36f, 13f, new Color(90, 90, 90, 255));

        float centerX = w / 2f;
        float topY = py + 65f;

        // 1. Слот сырья (Input)
        float inSlotX = centerX - slotSz - 35f;
        var inRect = new Rectangle(inSlotX, topY, slotSz, slotSz);
        bool inHov = Raylib.CheckCollisionPointRec(mouse, inRect);
        Raylib.DrawRectangleRounded(inRect, 0.12f, 6, inHov ? SlotHover : SlotBg);
        Raylib.DrawRectangleRoundedLinesEx(inRect, 0.12f, 6, 1.5f, SlotBorder);
        if (furnace.Input.HasValue && furnace.Input.Value.Quantity > 0) {
            var item = furnace.Input.Value;
            Hud.DrawItemIcon(item.Item.Definition, new Rectangle(inSlotX + 3f, topY + 3f, slotSz - 6f, slotSz - 6f), 1f);
            if (item.Quantity > 1) Fonts.DrawShadowed($"×{item.Quantity}", inSlotX + 4f, topY + slotSz - 16f, 14f, Color.White);
        }
        Fonts.DrawCentered("Сырьё", inSlotX + slotSz/2f, topY - 14f, 12f, new Color(200, 200, 200, 255));

        // 2. Слот топлива (Fuel)
        float fuelSlotY = topY + slotSz + 24f;
        var fuelRect = new Rectangle(inSlotX, fuelSlotY, slotSz, slotSz);
        bool fuelHov = Raylib.CheckCollisionPointRec(mouse, fuelRect);
        Raylib.DrawRectangleRounded(fuelRect, 0.12f, 6, fuelHov ? SlotHover : SlotBg);
        Raylib.DrawRectangleRoundedLinesEx(fuelRect, 0.12f, 6, 1.5f, SlotBorder);
        if (furnace.Fuel.HasValue && furnace.Fuel.Value.Quantity > 0) {
            var fuel = furnace.Fuel.Value;
            Hud.DrawItemIcon(fuel.Item.Definition, new Rectangle(inSlotX + 3f, fuelSlotY + 3f, slotSz - 6f, slotSz - 6f), 1f);
            if (fuel.Quantity > 1) Fonts.DrawShadowed($"×{fuel.Quantity}", inSlotX + 4f, fuelSlotY + slotSz - 16f, 14f, Color.White);
        }
        Fonts.DrawCentered("Топливо", inSlotX + slotSz/2f, fuelSlotY + slotSz + 4f, 12f, new Color(200, 200, 200, 255));

        // 3. Анимированное пламя (Flame)
        float flameRatio = furnace.MaxFuelTimer > 0f ? Math.Clamp(furnace.FuelTimer / furnace.MaxFuelTimer, 0f, 1f) : 0f;
        float flameY = topY + slotSz + 2f;
        Fonts.DrawCentered("🔥", inSlotX + slotSz / 2f, flameY, 20f, flameRatio > 0f ? new Color(255, 140, 30, (int)(180 + 75 * flameRatio)) : new Color(60, 60, 60, 180));

        // 4. Стрелка прогресса плавки
        float arrowX = centerX - 10f;
        float arrowY = topY + (slotSz + 24f) / 2f;
        Fonts.Draw("→", arrowX, arrowY, 28f, new Color(200, 200, 200, 255));
        float cookRatio = Math.Clamp(furnace.SmeltTimer / 8f, 0f, 1f);
        if (cookRatio > 0f) {
            Raylib.DrawRectangle((int)arrowX, (int)arrowY + 28, (int)(28f * cookRatio), 4, new Color(255, 200, 50, 255));
        }

        // 5. Слот результата (Output)
        float outSlotX = centerX + 35f;
        float outSlotY = topY + (slotSz + 24f) / 2f - slotSz / 2f + 8f;
        var outRect = new Rectangle(outSlotX, outSlotY, slotSz, slotSz);
        bool outHov = Raylib.CheckCollisionPointRec(mouse, outRect);
        Raylib.DrawRectangleRounded(outRect, 0.12f, 6, outHov ? SlotHover : (furnace.Output != null ? new Color(52, 68, 52, 255) : SlotBg));
        Raylib.DrawRectangleRoundedLinesEx(outRect, 0.12f, 6, 1.5f, furnace.Output != null ? new Color(100, 200, 100, 255) : SlotBorder);
        Fonts.DrawCentered("Результат", outSlotX + slotSz/2f, outSlotY - 14f, 12f, new Color(200, 200, 200, 255));

        if (furnace.Output.HasValue && furnace.Output.Value.Quantity > 0) {
            var outp = furnace.Output.Value;
            Hud.DrawItemIcon(outp.Item.Definition, new Rectangle(outSlotX + 3f, outSlotY + 3f, slotSz - 6f, slotSz - 6f), 1f);
            if (outp.Quantity > 1) Fonts.DrawShadowed($"×{outp.Quantity}", outSlotX + 4f, outSlotY + slotSz - 16f, 14f, Color.White);
        }

        // Взаимодействие со слотом сырья
        if ((leftClick || rightClick) && inHov) {
            HandleFurnaceSlotClick(ref furnace.Input, leftClick, rightClick, id => GameData.SmeltingRecipes.ContainsKey(id));
        }

        // Взаимодействие со слотом топлива
        if ((leftClick || rightClick) && fuelHov) {
            HandleFurnaceSlotClick(ref furnace.Fuel, leftClick, rightClick, id => id == GameData.CoalItem.Id || id == GameData.CharcoalItem.Id || id == GameData.LogItem.Id || id == GameData.PlankItem.Id || id == GameData.StickItem.Id);
        }

        // Взаимодействие со слотом результата (только забирать)
        if (leftClick && outHov && furnace.Output.HasValue && furnace.Output.Value.Quantity > 0) {
            var outp = furnace.Output.Value;
            if (!Held.HasValue || Held.Value.Quantity <= 0) {
                Held = outp;
                furnace.Output = null;
            } else if (Held.Value.Item.Definition.Id == outp.Item.Definition.Id && Held.Value.Quantity + outp.Quantity <= 64) {
                Held = Held.Value with { Quantity = Held.Value.Quantity + outp.Quantity };
                furnace.Output = null;
            }
        }

        // Сетка инвентаря снизу
        float invY = fuelSlotY + slotSz + 25f;
        float invX = px + 16f;
        for (int row = 0; row < 3; row++) {
            for (int col = 0; col < 9; col++) {
                int idx = 9 + row * 9 + col;
                DrawSlot(session, inv, invX + col * 56f, invY + row * 56f, idx, false);
            }
        }
        float hotY = invY + 3 * 56f + 4f;
        for (int col = 0; col < 9; col++)
            DrawSlot(session, inv, invX + col * 56f, hotY, col, col == session.Player.SelectedSlot);

        DrawHeldItem();
        HandleWorkbenchInput(session, inv, invX, invY, hotY);

        if (!Held.HasValue || Held.Value.Quantity <= 0) {
            ItemDefinition? fDef = null;
            if (inHov && furnace.Input.HasValue) { fDef = furnace.Input.Value.Item.Definition; }
            else if (fuelHov && furnace.Fuel.HasValue) { fDef = furnace.Fuel.Value.Item.Definition; }
            else if (outHov && furnace.Output.HasValue) { fDef = furnace.Output.Value.Item.Definition; }
            else {
                for (int row = 0; row < 3; row++) {
                    for (int col = 0; col < 9; col++) {
                        if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(invX + col * 56f, invY + row * 56f, 52, 52))) {
                            var e = inv.Slots[9 + row * 9 + col];
                            if (e.HasValue) { fDef = e.Value.Item.Definition; }
                        }
                    }
                }
                for (int col = 0; col < 9; col++) {
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(invX + col * 56f, hotY, 52, 52))) {
                        var e = inv.Slots[col];
                        if (e.HasValue) { fDef = e.Value.Item.Definition; }
                    }
                }
            }
            if (fDef != null) DrawItemTooltip(fDef, mouse);
        }
    }

    private static void HandleFurnaceSlotClick(ref ItemEntry? slotItem, bool leftClick, bool rightClick, Func<ushort, bool> filter) {
        if (rightClick) {
            if (Held.HasValue && Held.Value.Quantity > 0) {
                if (filter(Held.Value.Item.Definition.Id)) {
                    var held = Held.Value;
                    if (!slotItem.HasValue) {
                        slotItem = new ItemEntry(held.Item, 1);
                        Held = held with { Quantity = held.Quantity - 1 };
                        if (Held.Value.Quantity <= 0) Held = null;
                    } else if (slotItem.Value.Item.Definition == held.Item.Definition && slotItem.Value.Quantity < slotItem.Value.Item.Definition.MaxStack) {
                        slotItem = slotItem.Value with { Quantity = slotItem.Value.Quantity + 1 };
                        Held = held with { Quantity = held.Quantity - 1 };
                        if (Held.Value.Quantity <= 0) Held = null;
                    }
                }
            } else if (slotItem.HasValue && slotItem.Value.Quantity > 0) {
                int qty = slotItem.Value.Quantity;
                int take = (qty + 1) / 2;
                Held = slotItem.Value with { Quantity = take };
                if (qty - take > 0) slotItem = slotItem.Value with { Quantity = qty - take };
                else slotItem = null;
            }
        } else if (leftClick) {
            if (!Held.HasValue || Held.Value.Quantity <= 0) {
                if (slotItem.HasValue) {
                    Held = slotItem;
                    slotItem = null;
                }
            } else if (filter(Held.Value.Item.Definition.Id)) {
                var held = Held.Value;
                if (!slotItem.HasValue) {
                    slotItem = held;
                    Held = null;
                } else if (slotItem.Value.Item.Definition == held.Item.Definition) {
                    int current = slotItem.Value.Quantity;
                    if (current < slotItem.Value.Item.Definition.MaxStack) {
                        int add = Math.Min(slotItem.Value.Item.Definition.MaxStack - current, held.Quantity);
                        slotItem = slotItem.Value with { Quantity = current + add };
                        if (held.Quantity - add > 0) Held = held with { Quantity = held.Quantity - add };
                        else Held = null;
                    }
                } else {
                    var tmp = slotItem;
                    slotItem = held;
                    Held = tmp;
                }
            }
        }
    }

    // ── Экран сундука (27 слотов сундука + 36 слотов игрока) ─────────────────

    public static void DrawChestUI(GameSession session) {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        var pInv = session.Player.Inventory;
        var chestInv = session.World.GetOrCreateChest(session.ActiveChestPos);
        var mouse = Raylib.GetMousePosition();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool rightClick = Raylib.IsMouseButtonPressed(MouseButton.Right);

        const int slotSz = 52, gap = 4;
        int gridW = 9 * slotSz + 8 * gap;
        int panelW = gridW + 32;
        int panelH = 3 * slotSz + 2 * gap + 24 + 4 * slotSz + 3 * gap + 100;

        float px = w / 2f - panelW / 2f, py = (h - panelH) / 2f;
        DrawPanel(px, py, panelW, panelH);

        Fonts.DrawCentered("СУНДУК", w / 2f, py + 8f, 24f, TextDark);
        Fonts.DrawCentered("Хранилище предметов · ЛКМ / ПКМ", w / 2f, py + 34f, 13f, new Color(90, 90, 90, 255));

        float chestY = py + 56f;
        float chestX = px + 16f;

        // 3×9 слотов сундука
        for (int r = 0; r < 3; r++) {
            for (int c = 0; c < 9; c++) {
                int idx = r * 9 + c;
                float sx = chestX + c * (slotSz + gap);
                float sy = chestY + r * (slotSz + gap);
                var rect = new Rectangle(sx, sy, slotSz, slotSz);
                bool hov = Raylib.CheckCollisionPointRec(mouse, rect);

                Raylib.DrawRectangleRounded(rect, 0.12f, 6, hov ? SlotHover : SlotBg);
                Raylib.DrawRectangleRoundedLinesEx(rect, 0.12f, 6, 1.5f, SlotBorder);

                var entry = chestInv.Slots[idx];
                if (entry.HasValue && entry.Value.Quantity > 0) {
                    Hud.DrawItemIcon(entry.Value.Item.Definition, new Rectangle(sx + 3f, sy + 3f, slotSz - 6f, slotSz - 6f), 1f);
                    if (entry.Value.Quantity > 1)
                        Fonts.DrawShadowed($"×{entry.Value.Quantity}", sx + 4f, sy + slotSz - 16f, 14f, Color.White);
                }

                if ((leftClick || rightClick) && hov) {
                    HandleContainerSlotClick(chestInv, idx, leftClick, rightClick, pInv);
                }
            }
        }

        // Инвентарь игрока снизу
        float invY = chestY + 3 * (slotSz + gap) + 20f;
        float invX = px + 16f;
        Fonts.Draw("Инвентарь", invX, invY - 16f, 13f, new Color(200, 200, 200, 255));

        for (int row = 0; row < 3; row++) {
            for (int col = 0; col < 9; col++) {
                int idx = 9 + row * 9 + col;
                DrawSlot(session, pInv, invX + col * (slotSz + gap), invY + row * (slotSz + gap), idx, false);
            }
        }
        float hotY = invY + 3 * (slotSz + gap) + 6f;
        for (int col = 0; col < 9; col++)
            DrawSlot(session, pInv, invX + col * (slotSz + gap), hotY, col, col == session.Player.SelectedSlot);

        DrawHeldItem();

        if (leftClick || rightClick) {
            for (int row = 0; row < 3; row++) {
                for (int col = 0; col < 9; col++) {
                    float sx = invX + col * (slotSz + gap);
                    float sy = invY + row * (slotSz + gap);
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(sx, sy, slotSz, slotSz))) {
                        HandleContainerSlotClick(pInv, 9 + row * 9 + col, leftClick, rightClick, chestInv);
                        return;
                    }
                }
            }
            for (int col = 0; col < 9; col++) {
                float sx = invX + col * (slotSz + gap);
                float sy = hotY;
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(sx, sy, slotSz, slotSz))) {
                    HandleContainerSlotClick(pInv, col, leftClick, rightClick, chestInv);
                    return;
                }
            }
        }

        if (!Held.HasValue || Held.Value.Quantity <= 0) {
            ItemDefinition? cDef = null;
            for (int r = 0; r < 3; r++) {
                for (int c = 0; c < 9; c++) {
                    int idx = r * 9 + c;
                    float sx = chestX + c * (slotSz + gap);
                    float sy = chestY + r * (slotSz + gap);
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(sx, sy, slotSz, slotSz))) {
                        var e = chestInv.Slots[idx];
                        if (e.HasValue) { cDef = e.Value.Item.Definition; }
                    }
                }
            }
            for (int row = 0; row < 3; row++) {
                for (int col = 0; col < 9; col++) {
                    int idx = 9 + row * 9 + col;
                    float sx = invX + col * (slotSz + gap);
                    float sy = invY + row * (slotSz + gap);
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(sx, sy, slotSz, slotSz))) {
                        var e = pInv.Slots[idx];
                        if (e.HasValue) { cDef = e.Value.Item.Definition; }
                    }
                }
            }
            for (int col = 0; col < 9; col++) {
                float sx = invX + col * (slotSz + gap);
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(sx, hotY, slotSz, slotSz))) {
                    var e = pInv.Slots[col];
                    if (e.HasValue) { cDef = e.Value.Item.Definition; }
                }
            }
            if (cDef != null) DrawItemTooltip(cDef, mouse);
        }
    }

    private static void HandleContainerSlotClick(Container inv, int slotIdx, bool leftClick, bool rightClick, Container? targetTransferInv = null) {
        var slotItem = inv.Slots[slotIdx];

        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        if (shift && !rightClick && slotItem != null && targetTransferInv != null) {
            var item = slotItem.Value;
            int inserted = 0;
            // 1. Сначала объединяем с существующими одинаковыми стаками
            for (int i = 0; i < targetTransferInv.Slots.Length; i++) {
                var s = targetTransferInv.Slots[i];
                if (s.HasValue && s.Value.Item.Definition.Id == item.Item.Definition.Id && s.Value.Quantity < s.Value.Item.Definition.MaxStack) {
                    int space = s.Value.Item.Definition.MaxStack - s.Value.Quantity;
                    int add = Math.Min(space, item.Quantity - inserted);
                    targetTransferInv.InsertAt(i, s.Value with { Quantity = s.Value.Quantity + add });
                    inserted += add;
                    if (inserted >= item.Quantity) break;
                }
            }
            // 2. Остаток помещаем в первые свободные пустые слоты
            if (inserted < item.Quantity) {
                for (int i = 0; i < targetTransferInv.Slots.Length; i++) {
                    if (!targetTransferInv.Slots[i].HasValue) {
                        int add = Math.Min(item.Item.Definition.MaxStack, item.Quantity - inserted);
                        targetTransferInv.InsertAt(i, new ItemEntry(item.Item, add));
                        inserted += add;
                        if (inserted >= item.Quantity) break;
                    }
                }
            }
            if (inserted >= item.Quantity) {
                inv.RemoveAt(slotIdx);
                SoundSystem.PlayPop();
            } else if (inserted > 0) {
                inv.InsertAt(slotIdx, item with { Quantity = item.Quantity - inserted });
                SoundSystem.PlayPop();
            }
            return;
        }

        if (rightClick) {
            if (Held.HasValue && Held.Value.Quantity > 0) {
                var held = Held.Value;
                if (!slotItem.HasValue) {
                    inv.InsertAt(slotIdx, new ItemEntry(held.Item, 1));
                    Held = held with { Quantity = held.Quantity - 1 };
                    if (Held.Value.Quantity <= 0) Held = null;
                } else if (slotItem.Value.Item.Definition == held.Item.Definition && slotItem.Value.Quantity < slotItem.Value.Item.Definition.MaxStack) {
                    inv.InsertAt(slotIdx, slotItem.Value with { Quantity = slotItem.Value.Quantity + 1 });
                    Held = held with { Quantity = held.Quantity - 1 };
                    if (Held.Value.Quantity <= 0) Held = null;
                }
            } else if (slotItem.HasValue && slotItem.Value.Quantity > 0) {
                int qty = slotItem.Value.Quantity;
                int take = (qty + 1) / 2;
                Held = slotItem.Value with { Quantity = take };
                if (qty - take > 0) inv.InsertAt(slotIdx, slotItem.Value with { Quantity = qty - take });
                else inv.RemoveAt(slotIdx);
            }
        } else if (leftClick) {
            if (!Held.HasValue || Held.Value.Quantity <= 0) {
                if (slotItem.HasValue) {
                    Held = slotItem;
                    inv.RemoveAt(slotIdx);
                }
            } else {
                var held = Held.Value;
                if (!slotItem.HasValue) {
                    inv.InsertAt(slotIdx, held);
                    Held = null;
                } else if (slotItem.Value.Item.Definition == held.Item.Definition) {
                    int current = slotItem.Value.Quantity;
                    if (current < slotItem.Value.Item.Definition.MaxStack) {
                        int add = Math.Min(slotItem.Value.Item.Definition.MaxStack - current, held.Quantity);
                        inv.InsertAt(slotIdx, slotItem.Value with { Quantity = current + add });
                        if (held.Quantity - add > 0) Held = held with { Quantity = held.Quantity - add };
                        else Held = null;
                    }
                } else {
                    inv.InsertAt(slotIdx, held);
                    Held = slotItem;
                }
            }
        }
    }

    // ── Вспомогательный слот крафт-сетки ─────────────────────────────────────

    private static void DrawCraftSlot(ItemEntry?[] grid, int idx, float x, float y, int sz,
                                       System.Numerics.Vector2 mouse, bool leftClick, bool rightClick) {
        var rect = new Rectangle(x, y, sz, sz);
        bool hov = Raylib.CheckCollisionPointRec(mouse, rect);
        Raylib.DrawRectangleRounded(rect, 0.12f, 6, hov ? SlotHover : SlotBg);
        Raylib.DrawRectangleRoundedLinesEx(rect, 0.12f, 6, 2f, hov && Held != null ? SlotSelected : SlotBorder);

        var slotItem = grid[idx];
        if (slotItem.HasValue && slotItem.Value.Quantity > 0) {
            var item = slotItem.Value;
            Hud.DrawItemIcon(item.Item.Definition, new Rectangle(x + 3f, y + 3f, sz - 6f, sz - 6f), 1f);
            if (item.Quantity > 1) {
                Fonts.DrawShadowed($"×{item.Quantity}", x + 4f, y + sz - 18f, 14f, Color.White);
            }
        }

        if (hov && (leftClick || rightClick)) {
            if (rightClick) {
                if (Held.HasValue && Held.Value.Quantity > 0) {
                    var held = Held.Value;
                    if (!slotItem.HasValue) {
                        grid[idx] = new ItemEntry(held.Item, 1);
                        Held = held with { Quantity = held.Quantity - 1 };
                        if (Held.Value.Quantity <= 0) Held = null;
                    } else if (slotItem.Value.Item.Definition == held.Item.Definition && slotItem.Value.Quantity < slotItem.Value.Item.Definition.MaxStack) {
                        grid[idx] = slotItem.Value with { Quantity = slotItem.Value.Quantity + 1 };
                        Held = held with { Quantity = held.Quantity - 1 };
                        if (Held.Value.Quantity <= 0) Held = null;
                    }
                } else if (slotItem.HasValue && slotItem.Value.Quantity > 0) {
                    int qty = slotItem.Value.Quantity;
                    int take = (qty + 1) / 2;
                    Held = slotItem.Value with { Quantity = take };
                    if (qty - take > 0) {
                        grid[idx] = slotItem.Value with { Quantity = qty - take };
                    } else {
                        grid[idx] = null;
                    }
                }
            } else if (leftClick) {
                if (!Held.HasValue || Held.Value.Quantity <= 0) {
                    if (slotItem.HasValue) {
                        Held = slotItem;
                        grid[idx] = null;
                    }
                } else {
                    var held = Held.Value;
                    if (!slotItem.HasValue) {
                        grid[idx] = held;
                        Held = null;
                    } else if (slotItem.Value.Item.Definition == held.Item.Definition) {
                        int current = slotItem.Value.Quantity;
                        if (current < slotItem.Value.Item.Definition.MaxStack) {
                            int add = Math.Min(slotItem.Value.Item.Definition.MaxStack - current, held.Quantity);
                            grid[idx] = slotItem.Value with { Quantity = current + add };
                            if (held.Quantity - add > 0) {
                                Held = held with { Quantity = held.Quantity - add };
                            } else {
                                Held = null;
                            }
                        }
                    } else {
                        var tmp = slotItem;
                        grid[idx] = held;
                        Held = tmp;
                    }
                }
            }
        }
    }



    private static void DrawHeldItem() {
        if (!Held.HasValue || Held.Value.Quantity <= 0) return;
        var held = Held.Value;
        var mouse = Raylib.GetMousePosition();
        Hud.DrawItemIcon(held.Item.Definition, new Rectangle(mouse.X - 14f, mouse.Y - 14f, 28f, 28f), 1f);
        if (held.Quantity > 1)
            Fonts.Draw($"×{held.Quantity}", mouse.X - 14f, mouse.Y + 8f, 15f, Color.White);
    }

    public static void DrawItemTooltip(ItemDefinition def, System.Numerics.Vector2 mouse) {
        string title = def.Name;
        ushort itemId = def.Id;

        string subtext = "";
        if (GameData.FoodValue.TryGetValue(itemId, out float food)) {
            subtext = $"Пища: +{food} HP";
        } else if (GameData.GetToolTier(itemId) > 0) {
            subtext = $"Урон: {GameData.GetWeaponDamage(itemId)} HP";
        }

        float w = MathF.Max(Fonts.Measure(title, 16f), Fonts.Measure(subtext, 14f)) + 28f;
        float h = string.IsNullOrEmpty(subtext) ? 32f : 52f;

        float tx = mouse.X + 14f;
        float ty = mouse.Y - 14f;
        
        if (tx + w > Raylib.GetScreenWidth() - 8f) tx = mouse.X - w - 8f;
        if (ty + h > Raylib.GetScreenHeight() - 8f) ty = Raylib.GetScreenHeight() - h - 8f;
        if (ty < 8f) ty = 8f;
        if (tx < 8f) tx = 8f;

        var bg = new Color(16, 12, 28, 255);
        var border = new Color(85, 55, 145, 255);
        
        Raylib.DrawRectangleRounded(new Rectangle(tx, ty, w, h), 0.15f, 6, bg);
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(tx, ty, w, h), 0.15f, 6, 2f, border);

        Fonts.DrawShadowed(title, tx + 12f, ty + 7f, 16f, new Color(255, 220, 100, 255));
        if (!string.IsNullOrEmpty(subtext)) {
            Fonts.Draw(subtext, tx + 12f, ty + 28f, 14f, new Color(180, 190, 210, 255));
        }
    }

    private static void DrawNameTooltip(string name, System.Numerics.Vector2 mouse) {
        float tw = Fonts.Measure(name, 16f) + 16f;
        float th = 28f;
        float tx = mouse.X + 12f, ty = mouse.Y - 12f;
        if (tx + tw > Raylib.GetScreenWidth()) tx = mouse.X - tw - 8f;
        if (ty + th > Raylib.GetScreenHeight()) ty = Raylib.GetScreenHeight() - th - 8f;
        if (ty < 8f) ty = 8f;
        Raylib.DrawRectangleRec(new Rectangle(tx, ty, tw, th), new Color(16, 8, 24, 240));
        Raylib.DrawRectangleLinesEx(new Rectangle(tx, ty, tw, th), 1.5f, new Color(42, 16, 76, 255));
        Fonts.Draw(name, tx + 8f, ty + 6f, 16f, Color.White);
    }

    private static bool CampfireNearby(GameSession session) {
        var p = session.Player.Position;
        foreach (var camp in session.World.Fire.Campfires) {
            float dx = p.X - (camp.X + 0.5f), dz = p.Z - (camp.Z + 0.5f);
            if (dx * dx + dz * dz < 25f) return true;
        }
        return false;
    }



    // ── Общие элементы ───────────────────────────────────────────────────────

    private static bool Button(float x, float y, float width, float height, string label, bool enabled) {
        var rect = new Rectangle(x, y, width, height);
        var mouse = Raylib.GetMousePosition();
        bool hovered = Raylib.CheckCollisionPointRec(mouse, rect);
        
        Color faceColor = !enabled 
            ? new Color(74, 74, 74, 255) 
            : (hovered ? new Color(140, 150, 190, 255) : new Color(110, 110, 110, 255));
            
        Color textColor = !enabled 
            ? new Color(160, 160, 160, 255) 
            : (hovered ? new Color(255, 250, 160, 255) : Color.White);

        Raylib.DrawRectangleRec(rect, faceColor);
        
        if (enabled) {
            Color highlight = hovered ? new Color(200, 210, 255, 255) : new Color(160, 160, 160, 255);
            Color shadow = hovered ? new Color(80, 90, 130, 255) : new Color(60, 60, 60, 255);
            Raylib.DrawRectangle((int)x, (int)y, (int)width, 2, highlight);
            Raylib.DrawRectangle((int)x, (int)y, 2, (int)height, highlight);
            Raylib.DrawRectangle((int)x, (int)(y + height - 2), (int)width, 2, shadow);
            Raylib.DrawRectangle((int)(x + width - 2), (int)y, 2, (int)height, shadow);
        }
        
        Color outline = !enabled ? new Color(40, 40, 40, 255) : (hovered ? new Color(255, 220, 120, 255) : Color.Black);
        Raylib.DrawRectangleLinesEx(rect, 2f, outline);
        
        Fonts.DrawCentered(label, x + width / 2f, y + height / 2f - 9f, 20f, textColor);
        return enabled && hovered && Raylib.IsMouseButtonPressed(MouseButton.Left);
    }

    private static void DrawPanel(float x, float y, float width, float height) {
        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(0, 0, 0, 140));
        var rect = new Rectangle(x, y, width, height);
        Raylib.DrawRectangleRec(rect, Panel);
        Raylib.DrawRectangleLinesEx(rect, 2f, Color.Black);
        // Верхний и левый белый блик
        Raylib.DrawRectangle((int)x + 2, (int)y + 2, (int)width - 4, 3, PanelLightBorder);
        Raylib.DrawRectangle((int)x + 2, (int)y + 2, 3, (int)height - 4, PanelLightBorder);
        // Нижний и правый темно-серый скос (тень)
        Raylib.DrawRectangle((int)x + 2, (int)(y + height) - 5, (int)width - 4, 3, PanelDarkBorder);
        Raylib.DrawRectangle((int)(x + width) - 5, (int)y + 2, 3, (int)height - 4, PanelDarkBorder);
    }
}





