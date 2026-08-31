using VoxelFrame.Core;
using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.Materials;

namespace VoxelFrame.Game;

public enum MenuAction { None, NewGame, Continue, Exit }
public enum PauseAction { None, Resume, SaveAndExit, Settings, OpenToLan }

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
    public static bool InOpenToLanScreen = false;
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
        9 => KeyBinds.Pause,
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
            case 9: KeyBinds.Pause = key; break;
        }
    }

    // ── Частицы и Splash Text меню ──────────────────────────────────────────

    private struct MenuParticle {
        public Vector2 Pos;
        public Vector2 Vel;
        public float Size;
        public float Alpha;
        public Color Tint;
    }

    private static MenuParticle[]? _particles;

    private static void InitMenuParticles() {
        if (_particles != null) return;
        var rng = new Random(1337);
        _particles = new MenuParticle[40];
        for (int i = 0; i < _particles.Length; i++) {
            _particles[i] = new MenuParticle {
                Pos = new Vector2(rng.Next(0, 1920), rng.Next(0, 1080)),
                Vel = new Vector2((float)(rng.NextDouble() - 0.5) * 16f, -12f - (float)rng.NextDouble() * 22f),
                Size = 2f + (float)rng.NextDouble() * 3.5f,
                Alpha = 0.3f + (float)rng.NextDouble() * 0.5f,
                Tint = rng.NextDouble() < 0.65 ? new Color(255, 200, 75, 255) : new Color(90, 180, 255, 255)
            };
        }
    }

    public static void DrawMenuBackground(float dt) {
        int w = Ui.Vw, h = Ui.Vh;
        InitMenuParticles();

        // 1. Глубокий градиентный темный фон (от космического индиго к темному сланцу)
        Raylib.DrawRectangleGradientV(0, 0, w, h, new Color(16, 20, 30, 255), new Color(10, 12, 18, 255));

        // 2. Стильная тонкая воксельная сетка с мягким затемнением
        int tileSize = 64;
        for (int x = 0; x < w; x += tileSize) {
            for (int y = 0; y < h; y += tileSize) {
                bool isEven = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                if (isEven) {
                    Raylib.DrawRectangle(x, y, tileSize, tileSize, new Color(28, 34, 48, 35));
                }
            }
        }

        // 3. Атмосферные частицы / золотистые и лазурные искры
        float time = (float)Raylib.GetTime();
        if (_particles != null) {
            for (int i = 0; i < _particles.Length; i++) {
                ref var p = ref _particles[i];
                p.Pos.X += (p.Vel.X + MathF.Sin(time * 1.5f + i) * 8f) * dt;
                p.Pos.Y += p.Vel.Y * dt;
                if (p.Pos.Y < -10f) p.Pos.Y = h + 10f;
                if (p.Pos.X < -10f) p.Pos.X = w + 10f;
                else if (p.Pos.X > w + 10f) p.Pos.X = -10f;

                float flicker = 0.7f + 0.3f * MathF.Sin(time * 3f + i * 1.3f);
                Color c = new(p.Tint.R, p.Tint.G, p.Tint.B, (byte)(p.Alpha * flicker * 255));
                Raylib.DrawRectangle((int)p.Pos.X, (int)p.Pos.Y, (int)p.Size, (int)p.Size, c);
            }
        }

        // 4. Мягкая виньетка по краям экрана
        Raylib.DrawRectangleGradientV(0, 0, w, 140, new Color(0, 0, 0, 180), new Color(0, 0, 0, 0));
        Raylib.DrawRectangleGradientV(0, h - 160, w, 160, new Color(0, 0, 0, 0), new Color(0, 0, 0, 210));
    }

    private static readonly string[] SplashTexts = {
        "100% Pure C# & Raylib!",
        "Теперь без редстоуна!",
        "Алмазы спасены из лавы!",
        "3D-пещеры без лавы!",
        "Не копай прямо под себя!",
        "Покорми овечку пшеницей!",
        "Осторожно: криперы не спят!",
        "Кастомный воксельный движок!",
        "С любовью от SenStol Studio!",
        "Зомби боятся утреннего солнца!",
        "Дерево само себя не срубит!",
        "Скрафти верстак первым делом!",
        "Болотный страж ждет во тьме...",
        "Ночь темна и полна криперов!",
        "Где мой факел?!",
        "Костная мука творит чудеса!",
        "Морковь полезна для зрения!",
        "Жареная картошечка в печи!",
        "60 кадров в секунду на чистом C#!",
        "Бесконечные процедурные миры!",
        "Никакой Java, только .NET 10!",
        "Воксели правят миром!",
        "Не забудь поставить кровать!",
        "Убей дракона — спаси Энд!",
        "Свинки обожают морковь!",
        "Шерсть для кровати добыта!",
        "Книга рецептов всегда под рукой!",
        "Слушай шаги за спиной...",
        "Алмазная кирка — лучший друг!",
        "Не смотри в глаза Эндермену!",
        "Лава течет на самом дне!",
        "Огороды цветут и пахнут!",
        "Посади дуб около дома!",
        "Секретные данжи ждут тебя!",
        "Улучшай броню вовремя!",
        "Крипер уже шипит сзади...",
        "Уютный домик из дубовых досок!",
        "Печка греет, хлеб печется!",
        "Золотые яблоки спасают жизнь!",
        "Скелеты стреляют метко!",
        "Портал в Нижний мир готов!",
        "Ифриты не любят воду!",
        "Зачаруй свой меч на остроту!",
        "Генерация чанков на лету!",
        "Никаких фризов и лагов!",
        "Построй свой замок мечты!",
        "Рецепты крафта на любой вкус!",
        "Спасибо, что играешь в VoxelFrame!",
        "Сделано с душой!",
        "Приятной игры!"
    };
    private static int _splashIndex = -1;

    private static void DrawSplashText(float x, float y) {
        if (_splashIndex < 0) {
            _splashIndex = Random.Shared.Next(SplashTexts.Length);
        }
        string splash = SplashTexts[_splashIndex];
        float time = (float)Raylib.GetTime();
        float pulse = 1.0f + MathF.Sin(time * 4.5f) * 0.06f;
        float baseSize = splash.Length > 28 ? 14f : splash.Length > 20 ? 16f : 18f;
        float fontSize = baseSize * pulse;

        Rlgl.PushMatrix();
        Rlgl.Translatef(x, y, 0f);
        Rlgl.Rotatef(-14f, 0f, 0f, 1f);
        Fonts.DrawShadowed(splash, 0f, 0f, fontSize, new Color(255, 245, 60, 255), 2f);
        Rlgl.PopMatrix();
    }

    // ── Главное меню ─────────────────────────────────────────────────────────

    public static MenuAction DrawMenu(float dt) {
        int w = Ui.Vw, h = Ui.Vh;
        DrawMenuBackground(dt);

        var action = MenuAction.None;

        if (InCreateWorldScreen) {
            Fonts.DrawTitle3D("СОЗДАНИЕ МИРА", w / 2f, h * 0.10f, 44f);

            float boxW = 420f;
            float cx = w / 2f - boxW / 2f;

            // Название мира
            Fonts.DrawShadowed("Название мира:", cx, h * 0.22f, 18f, new Color(220, 225, 235, 255));
            var nameRec = new Rectangle(cx, h * 0.26f, boxW, 44f);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {
                if (Raylib.CheckCollisionPointRec(Ui.Mouse(), nameRec)) ActiveTextInputField = 1;
            }
            Raylib.DrawRectangleRec(nameRec, ActiveTextInputField == 1 ? new Color(42, 50, 68, 255) : new Color(28, 34, 46, 255));
            Raylib.DrawRectangleLinesEx(nameRec, 1.5f, ActiveTextInputField == 1 ? new Color(255, 215, 80, 255) : new Color(60, 72, 95, 255));
            Fonts.Draw(WorldNameInput + (ActiveTextInputField == 1 && ((int)(Raylib.GetTime() * 2) % 2 == 0) ? "_" : ""), cx + 12f, h * 0.26f + 12f, 18f, Color.White);

            // Режим игры (Выживание)
            Fonts.DrawShadowed("Режим игры: Выживание", cx, h * 0.37f, 18f, new Color(220, 225, 235, 255));
            Fonts.Draw("Добыча ресурсов, крафт, опасные мобы и исследование 3D-мира.", cx, h * 0.41f, 14f, new Color(150, 165, 185, 255));

            // Сид генерации
            Fonts.DrawShadowed("Сид генератора мира (оставьте пустым для случайного):", cx, h * 0.49f, 16f, new Color(220, 225, 235, 255));
            var seedRec = new Rectangle(cx, h * 0.53f, boxW, 44f);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {
                if (Raylib.CheckCollisionPointRec(Ui.Mouse(), seedRec)) ActiveTextInputField = 2;
                else if (!Raylib.CheckCollisionPointRec(Ui.Mouse(), nameRec)) ActiveTextInputField = 0;
            }
            Raylib.DrawRectangleRec(seedRec, ActiveTextInputField == 2 ? new Color(42, 50, 68, 255) : new Color(28, 34, 46, 255));
            Raylib.DrawRectangleLinesEx(seedRec, 1.5f, ActiveTextInputField == 2 ? new Color(255, 215, 80, 255) : new Color(60, 72, 95, 255));
            Fonts.Draw(WorldSeedInput + (ActiveTextInputField == 2 && ((int)(Raylib.GetTime() * 2) % 2 == 0) ? "_" : ""), cx + 12f, h * 0.53f + 12f, 18f, Color.White);

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
            float btnY = h * 0.74f;
            if (Button(w / 2f - 215f, btnY, 210f, 48f, "Создать новый мир", true)) {
                string name = string.IsNullOrWhiteSpace(WorldNameInput) ? "Новый мир" : WorldNameInput.Trim();
                SaveSystem.CurrentWorldPath = SaveSystem.CreateWorldSavePath(name);
                CustomWorldSeed = GameData.ParseSeed(WorldSeedInput);
                action = MenuAction.NewGame;
                InCreateWorldScreen = false;
                InWorldSelectScreen = false;
            }
            if (Button(w / 2f + 5f, btnY, 210f, 48f, "Отмена", true)) {
                InCreateWorldScreen = false;
            }
        } else if (InWorldSelectScreen) {
            Fonts.DrawTitle3D("ВЫБОР МИРА", w / 2f, h * 0.08f, 44f);

            var worlds = SaveSystem.GetAllWorlds();
            if (SelectedWorldListIndex >= worlds.Count) SelectedWorldListIndex = Math.Max(0, worlds.Count - 1);

            float listY = h * 0.16f;
            float cardW = MathF.Min(600f, w - 80f);
            float cardH = 68f;
            float cardX = w / 2f - cardW / 2f;

            if (worlds.Count == 0) {
                Fonts.DrawCenteredShadowed("Нет созданных миров. Нажмите 'Создать новый мир'", w / 2f, h * 0.40f, 20f, new Color(180, 190, 205, 255));
            } else {
                for (int i = 0; i < worlds.Count && i < 5; i++) {
                    var wi = worlds[i];
                    float cy = listY + i * (cardH + 10f);
                    var cardRec = new Rectangle(cardX, cy, cardW, cardH);
                    bool isSelected = (i == SelectedWorldListIndex);
                    bool cardHover = Raylib.CheckCollisionPointRec(Ui.Mouse(), cardRec);

                    if (Raylib.IsMouseButtonPressed(MouseButton.Left) && cardHover) {
                        SelectedWorldListIndex = i;
                    }

                    // Фон карточки
                    Color cBg = isSelected ? new Color(55, 66, 92, 245) : (cardHover ? new Color(42, 50, 70, 220) : new Color(30, 36, 50, 200));
                    Color cBorder = isSelected ? new Color(255, 215, 80, 255) : (cardHover ? new Color(110, 135, 175, 255) : new Color(55, 65, 85, 255));

                    Raylib.DrawRectangleRec(cardRec, cBg);
                    Raylib.DrawRectangleLinesEx(cardRec, isSelected ? 2f : 1.5f, cBorder);

                    Fonts.DrawShadowed(wi.Name, cardX + 16f, cy + 10f, 22f, isSelected ? new Color(255, 245, 180, 255) : Color.White);
                    string dateStr = $"{wi.LastPlayed:dd.MM.yyyy HH:mm}";
                    string sizeStr = $"{wi.SizeBytes / 1024} KB";
                    string seedStr = wi.Seed != 0 ? $" • Сид: {wi.Seed}" : "";
                    Fonts.Draw($"Выживание • {dateStr} • {sizeStr}{seedStr}", cardX + 16f, cy + 38f, 14f, new Color(160, 180, 210, 255));
                }
            }

            // Нижняя панель действий
            float bY1 = h * 0.78f;
            float bY2 = h * 0.86f;
            float bW = 200f;

            bool hasSelection = worlds.Count > 0 && SelectedWorldListIndex >= 0 && SelectedWorldListIndex < worlds.Count;
            if (Button(w / 2f - bW - 8f, bY1, bW, 46f, "Играть в мире", hasSelection)) {
                SaveSystem.CurrentWorldPath = worlds[SelectedWorldListIndex].FilePath;
                action = MenuAction.Continue;
                InWorldSelectScreen = false;
            }
            if (Button(w / 2f + 8f, bY1, bW, 46f, "Создать новый мир", true)) {
                InCreateWorldScreen = true;
                WorldNameInput = "Новый мир";
                WorldSeedInput = "";
                ActiveTextInputField = 0;
            }

            if (Button(w / 2f - bW - 8f, bY2, bW, 46f, "Удалить", hasSelection)) {
                SaveSystem.DeleteSave(worlds[SelectedWorldListIndex].FilePath);
                if (SelectedWorldListIndex >= worlds.Count - 1) SelectedWorldListIndex = Math.Max(0, worlds.Count - 2);
            }
            if (Button(w / 2f + 8f, bY2, bW, 46f, "Отмена", true)) {
                InWorldSelectScreen = false;
            }
        } else {
            // ── ЛОГОТИП ──────────────────────────────────────────────────────
            Fonts.DrawTitle3D("VOXELFRAME", w / 2f, h * 0.15f, 76f);
            DrawSplashText(w / 2f + 210f, h * 0.15f + 56f);

            // ── КНОПКИ МЕНЮ ──────────────────────────────────────────────────
            float cy = h * 0.36f;
            float btnW = 360f;
            float btnH = 48f;
            float gapY = 12f;

            if (Button(w / 2f - btnW / 2f, cy, btnW, btnH, "Одиночная игра", true)) {
                InWorldSelectScreen = true;
            }
            if (Button(w / 2f - btnW / 2f, cy + btnH + gapY, btnW, btnH, "Сетевая игра", true)) {
                OpenMultiplayerMenu();
            }

            float halfW = (btnW - 12f) / 2f;
            if (Button(w / 2f - btnW / 2f, cy + (btnH + gapY) * 2, halfW, btnH, "Настройки...", true)) {
                InSettingsScreen = true;
                SettingsOpenedFromGame = false;
            }
            if (Button(w / 2f - btnW / 2f + halfW + 12f, cy + (btnH + gapY) * 2, halfW, btnH, "Выход", true)) {
                action = MenuAction.Exit;
            }

            if (MenuError.Length > 0)
                Fonts.DrawCenteredShadowed(MenuError, w / 2f, h * 0.72f, 18f, new Color(255, 120, 120, 255));

            // ── ПОДВАЛ (FOOTER) ───────────────────────────────────────────────
            // Левый бейдж версии
            var verRec = new Rectangle(14f, h - 34f, 190f, 24f);
            Raylib.DrawRectangleRec(verRec, new Color(20, 24, 34, 180));
            Raylib.DrawRectangleLinesEx(verRec, 1f, new Color(50, 60, 80, 180));
            Fonts.Draw("VoxelFrame 1.0.0-pre2", 22f, h - 30f, 14f, new Color(190, 205, 230, 220));

            // Правый копирайт
            var copyRec = new Rectangle(w - 200f, h - 34f, 186f, 24f);
            Raylib.DrawRectangleRec(copyRec, new Color(20, 24, 34, 180));
            Raylib.DrawRectangleLinesEx(copyRec, 1f, new Color(50, 60, 80, 180));
            Fonts.DrawCentered("SenStol Studio • 2026", w - 107f, h - 30f, 14f, new Color(190, 205, 230, 220));
        }

        return action;
    }

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
        }

        // Выбор скина персонажа
        string skinText = $"Скин: {SkinSystem.CurrentSkin.DisplayName}";
        if (Button(rightX, cy + btnH + gapY, btnW, btnH, skinText, true)) {
            SkinSystem.NextSkin();
        }

        // Масштаб интерфейса: авто по высоте окна или фиксированный процент
        string scaleText = SaveSystem.UiScaleMode switch {
            0 => "Масштаб интерфейса: Авто",
            _ => $"Масштаб: {SaveSystem.UiScaleMode}%",
        };
        if (Button(leftX, cy + (btnH + gapY) * 2, btnW, btnH, scaleText, true)) {
            int[] steps = { 0, 75, 100, 125, 150, 200 };
            int idx = Array.IndexOf(steps, SaveSystem.UiScaleMode);
            SaveSystem.UiScaleMode = steps[(idx + 1) % steps.Length];
        }

        if (Button(rightX, cy + (btnH + gapY) * 2, btnW, btnH, "Готово", true) || Raylib.IsKeyPressed(KeyboardKey.Escape)) {
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
            if (SaveSystem.GraphicsQuality == SaveSystem.GraphicsPreset.Fast) {
                SaveSystem.CloudsMode = 1;
                SaveSystem.ParticlesMode = 0;
                SaveSystem.DynamicLighting = false;
                SaveSystem.EntityShadows = false;
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

        // 4. Мягкое освещение (Smooth Lighting)
        string smoothLightText = SaveSystem.FancyGraphics ? "Мягкий свет: Вкл" : "Мягкий свет: Выкл (Быстро)";
        if (Button(leftX, startY + (rowH + gapY) * 3f, colW, rowH, smoothLightText, true)) {
            SaveSystem.FancyGraphics = !SaveSystem.FancyGraphics;
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

        // 5. Кинематографичные эффекты (по центру на всю ширину)
        float centerBtnW = colW * 2f + 20f;
        float centerBtnX = w / 2f - centerBtnW / 2f;
        string postFxText = SaveSystem.PostFxMode == 0
            ? "Кино-эффекты: Выкл (FPS+)"
            : $"Кино-эффекты: Вкл ({(SaveSystem.PostFxVignette ? "виньетка" : "без виньетки")}{(SaveSystem.PostFxBloom ? ", bloom" : "")}{(SaveSystem.PostFxGoldenHour ? ", закат" : "")})";
        if (Button(centerBtnX, startY + (rowH + gapY) * 4f, centerBtnW, rowH, postFxText, true)) {
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
                SetBindKey(ActiveRebindIndex, (KeyboardKey)pressed);
                ActiveRebindIndex = -1;
                while (Raylib.GetKeyPressed() != 0) {}
            }
        }

        Fonts.DrawTitle3D("НАСТРОЙКИ УПРАВЛЕНИЯ", w / 2f, h * 0.10f, 44f);

        float startY = h * 0.18f;
        float rowH = 42f;
        float colW = 340f;
        
        for (int i = 0; i < BindLabels.Length; i++) {
            int col = i % 2;
            int row = i / 2;
            
            float cx = (col == 0) ? (w / 2f - colW - 10f) : (w / 2f + 10f);
            float cy = startY + row * (rowH + 8f);
            
            Fonts.DrawShadowed($"{BindLabels[i]}:", cx, cy + 10f, 20f, Color.White);
            
            string keyName = (ActiveRebindIndex == i) ? "> ??? <" : KeyBinds.GetName(GetBindKey(i));
            if (Button(cx + 160f, cy, 160f, rowH, keyName, true)) {
                ActiveRebindIndex = i;
            }
        }

        // Слайдер чувствительности мыши
        float sensY = startY + 6 * (rowH + 8f);
        string sensText = $"Чувствительность мыши: {SaveSystem.MouseSensitivity}%";
        if (Slider(w / 2f - colW, sensY, colW * 2f + 20f, rowH, sensText, SaveSystem.MouseSensitivity, 20f, 200f, out float newSens)) {
            SaveSystem.MouseSensitivity = Math.Clamp((int)MathF.Round(newSens), 20, 200);
        }

        float bottomY = h * 0.85f;
        if (Button(w / 2f - 210f, bottomY, 200f, 44f, "Сбросить по умолч.", true)) {
            ActiveRebindIndex = -1;
            KeyBinds.ResetToDefaults();
            SaveSystem.MouseSensitivity = 100;
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

    // ── Экран смерти ─────────────────────────────────────────────────────────

    public enum DeathAction { None, Respawn, MainMenu }

    public static DeathAction DrawDeath(GameSession session) {
        int w = Ui.Vw, h = Ui.Vh;
        // Красный градиент смерти
        Raylib.DrawRectangleGradientV(0, 0, w, h, new Color(120, 15, 15, 190), new Color(30, 4, 4, 245));

        Fonts.DrawTitle3D("ВЫ ПОГИБЛИ!", w / 2f, h * 0.18f, 56f);

        Fonts.DrawCenteredShadowed($"Причина: {session.LastDeathCause}", w / 2f, h * 0.32f, 22f, new Color(255, 215, 90, 255));
        Fonts.DrawCenteredShadowed($"Место гибели: X: {(int)session.LastDeathPos.X}, Y: {(int)session.LastDeathPos.Y}, Z: {(int)session.LastDeathPos.Z}", w / 2f, h * 0.37f, 18f, new Color(150, 225, 255, 230));

        int totalSec = (int)session.TotalPlaySeconds;
        int mins = totalSec / 60;
        int secs = totalSec % 60;
        Fonts.DrawCenteredShadowed($"Время выживания: {mins} мин {secs:D2} сек", w / 2f, h * 0.42f, 18f, new Color(210, 215, 225, 220));

        var action = DeathAction.None;
        float cy = h * 0.54f;
        float btnW = 320f;
        float btnH = 48f;
        float gapY = 12f;

        if (Button(w / 2f - btnW / 2f, cy, btnW, btnH, "Возродиться", true)) {
            action = DeathAction.Respawn;
        }
        if (Button(w / 2f - btnW / 2f, cy + btnH + gapY, btnW, btnH, "Главное меню", true)) {
            action = DeathAction.MainMenu;
        }

        return action;
    }

    // ── Экран загрузки ───────────────────────────────────────────────────────

    public static void DrawLoading(GameSession session) {
        int w = Ui.Vw, h = Ui.Vh;
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

    /// <summary>Титры: таинственные (после Слизня Края) или истинный финал (после Истинного Слизня).</summary>
    public static void DrawCredits(GameSession session) {
        int w = Ui.Vw, h = Ui.Vh;
        Raylib.ClearBackground(new Color(6, 4, 10, 255));

        string[] lines = session.CreditsType == 2 ? new[] {
            "VoxelFrame: ИСТИННЫЙ ФИНАЛ",
            "",
            "--- ВЕЛИКИЙ ТРИУМФ ---",
            "",
            "Истинный Слизень Края повержен!",
            "Бездна очищена от древней тьмы.",
            "",
            "Все три измерения — Обычный мир, Незер и Энд —",
            "навеки обрели покой и безопасность.",
            "",
            "Ты одолел всех стражей, собрал все реликвии",
            "и покорил само Дно Реальности.",
            "",
            "Ты — Истинная Легенда VoxelFrame!",
            "",
            "Спасибо за невероятное прохождение!",
            "Твой бесконечный мир ждёт тебя.",
        } : new[] {
            "VoxelFrame: ТЕНЬ КРАЯ",
            "",
            "Слизень Края повержен... но так ли это?",
            "",
            "Острова Энда хранят древнюю тайну,",
            "забытую за гранью веков.",
            "",
            "Вдали на побочных островах спит Забытый Обелиск...",
            "Тот, кто соединит Слизь с тремя реликвиями миров,",
            "сковав Ключ Бездны...",
            "",
            "...и выдержит смертоносный спуск в Пустоту,",
            "найдёт то, что скрывается глубже самого дна реальности.",
            "",
            "Это не конец истории.",
            "Настоящий владыка Пустоты ещё наблюдает из Бездны...",
        };

        float elapsed = 32f - session.CreditsTimer;
        float y = h + 60f - elapsed * 52f;
        const float lineH = 46f;
        for (int i = 0; i < lines.Length; i++) {
            float ly = y + i * lineH;
            if (ly < -50f || ly > h + 50f) continue;
            bool isTitle = i == 0;
            bool isSub = i == 2;
            float size = isTitle ? 48f : isSub ? 32f : 24f;
            float mw = Fonts.Measure(lines[i], size);
            Color col = isTitle ? (session.CreditsType == 2 ? new Color(255, 220, 90, 255) : new Color(190, 120, 255, 255))
                      : isSub ? (session.CreditsType == 2 ? new Color(255, 240, 160, 255) : new Color(220, 180, 255, 255))
                      : new Color(225, 230, 240, 255);
            Fonts.DrawShadowed(lines[i], w / 2f - mw / 2f, ly, size, col);
        }

        Fonts.DrawShadowed("[ESC / ПРОБЕЛ] — Пропустить титры", w - 310, h - 30, 18f, new Color(160, 160, 180, 180));
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

        int w = Ui.Vw, h = Ui.Vh;
        var inv = session.Player.Inventory;
        var mouse = Ui.Mouse();

        int cols = 9, mainRows = 3;
        const int slot = 52, gap = 4;
        int gridW = cols * slot + (cols - 1) * gap; // 500px
        int panelW = 1020;
        int panelH = 340;
        float px = w / 2f - panelW / 2f, py = (h - panelH) / 2f;
        DrawPanel(px, py, panelW, panelH);

        // 1. Слоты экипировки брони слева (Шлем, Нагрудник, Поножи, Ботинки)
        float armorX = px + 18f;
        float armorY = py + 34f;
        Fonts.Draw("Броня", armorX, py + 14f, 15f, TextDark);

        string[] armorLabels = { "Шлем", "Нагрудник", "Поножи", "Ботинки" };
        for (int a = 0; a < 4; a++) {
            float ay = armorY + a * (slot + gap);
            var aRect = new Rectangle(armorX, ay, slot, slot);
            bool aHov = Raylib.CheckCollisionPointRec(mouse, aRect);

            Raylib.DrawRectangleRec(aRect, SlotBg);
            Raylib.DrawRectangle((int)armorX, (int)ay, slot, 2, SlotBorder);
            Raylib.DrawRectangle((int)armorX, (int)ay, 2, slot, SlotBorder);
            Raylib.DrawRectangle((int)armorX, (int)ay + slot - 2, slot, 2, PanelLightBorder);
            Raylib.DrawRectangle((int)armorX + slot - 2, (int)ay, 2, slot, PanelLightBorder);
            if (aHov) Raylib.DrawRectangleRec(aRect, SlotHover);

            if (session.Player.Armor[a] is { } ae && ae.Quantity > 0) {
                Hud.DrawItemIcon(ae.Item.Definition, new Rectangle(armorX + 3f, ay + 3f, slot - 6f, slot - 6f), 1f);
                Hud.DrawItemDurability(ae.Item, aRect);
            } else {
                Fonts.DrawCentered(armorLabels[a], armorX + slot / 2f, ay + slot / 2f - 6f, 11f, new Color(130, 140, 155, 130));
            }
        }

        // 2. Слот второй руки (справа от ботинок или под ними)
        float offhandX = armorX;
        float offhandY = armorY + 4 * (slot + gap) + 6f;
        var offhandRec = new Rectangle(offhandX, offhandY, slot, slot);
        bool offhandHover = Raylib.CheckCollisionPointRec(mouse, offhandRec);
        
        Raylib.DrawRectangleRec(offhandRec, SlotBg);
        Raylib.DrawRectangle((int)offhandX, (int)offhandY, slot, 2, SlotBorder);
        Raylib.DrawRectangle((int)offhandX, (int)offhandY, 2, slot, SlotBorder);
        Raylib.DrawRectangle((int)offhandX, (int)offhandY + slot - 2, slot, 2, PanelLightBorder);
        Raylib.DrawRectangle((int)offhandX + slot - 2, (int)offhandY, 2, slot, PanelLightBorder);
        if (offhandHover) Raylib.DrawRectangleRec(offhandRec, SlotHover);

        if (session.Player.OffhandEntry != null) {
            Hud.DrawItemIcon(session.Player.OffhandEntry.Value.Item.Definition, offhandRec, 0.75f);
            if (session.Player.OffhandEntry.Value.Quantity > 1) {
                Fonts.DrawShadowed($"×{session.Player.OffhandEntry.Value.Quantity}", offhandX + 3f, offhandY + slot - 18f, 14f, Color.White);
            }
            Hud.DrawItemDurability(session.Player.OffhandEntry.Value.Item, offhandRec);
        } else {
            Fonts.DrawCentered("2-я рука", offhandX + slot / 2f, offhandY + slot / 2f - 6f, 10f, new Color(130, 140, 155, 130));
        }

        // 3. Сетка основного инвентаря
        float gridX = armorX + slot + 16f;
        float gridY = py + 34f;
        float hotbarY = gridY + mainRows * (slot + gap) + 12f;

        // Заголовки
        Fonts.Draw("Инвентарь", gridX, py + 14f, 15f, TextDark);
        Fonts.Draw("Панель быстрого доступа", gridX, hotbarY - 16f, 13f, TextDark);

        // Сетка: 3 ряда основного хранилища (записи 9..) + ряд хотбара (0..8).
        for (int row = 0; row < mainRows; row++) {
            for (int col = 0; col < cols; col++) {
                int idx = 9 + row * cols + col;
                DrawSlot(session, inv, gridX + col * (slot + gap), gridY + row * (slot + gap), idx, false);
            }
        }
        for (int col = 0; col < cols; col++)
            DrawSlot(session, inv, gridX + col * (slot + gap), hotbarY, col, col == session.Player.SelectedSlot);

        // 4. Панель крафта и книга рецептов справа
        float rightX = gridX + gridW + 18f;
        float rightW = px + panelW - rightX - 16f;
        DrawCraftPanel(session, rightX, py + 14f, rightW, panelH - 28f);

        // Предмет за курсором
        if (Held.HasValue && Held.Value.Quantity > 0) {
            var held = Held.Value;
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

        var mouse = Ui.Mouse();
        const int slot = 52, gap = 4;
        int cols = 9, mainRows = 3;
        int panelW = 1020;
        int panelH = 340;
        float px = Ui.Vw / 2f - panelW / 2f;
        float py = (Ui.Vh - panelH) / 2f;

        float armorX = px + 18f;
        float armorY = py + 34f;
        float gridX = armorX + slot + 16f;
        float gridY = py + 34f;
        float hotbarY = gridY + mainRows * (slot + gap) + 12f;
        float offhandX = armorX;
        float offhandY = armorY + 4 * (slot + gap) + 6f;

        VoxelFrame.Core.Inventory.ItemEntry? hoveredEntry = null;

        // Броня
        for (int a = 0; a < 4; a++) {
            if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(armorX, armorY + a * (slot + gap), slot, slot))) {
                hoveredEntry = session.Player.Armor[a];
            }
        }

        // Вторая рука
        if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(offhandX, offhandY, slot, slot))) {
            hoveredEntry = session.Player.OffhandEntry;
        }

        // Инвентарь
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

        if (hoveredEntry != null) {
            DrawItemTooltip(hoveredEntry.Value.Item.Definition, mouse);
        }
    }

    private static void DrawSlot(GameSession session, VoxelFrame.Core.Inventory.Container inv, float x, float y, int idx, bool hotbarSelected) {
        var rect = new Rectangle(x, y, 52, 52);
        var mouse = Ui.Mouse();
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
                Hud.DrawItemDurability(entry.Value.Item, rect);
            }
        }
        _ = session;
    }

    private static void HandleHeldInput(GameSession session, VoxelFrame.Core.Inventory.Container inv) {
        var mouse = Ui.Mouse();
        const int slot = 52, gap = 4;
        int cols = 9, mainRows = 3;
        int panelW = 1020;
        int panelH = 340;
        float px = Ui.Vw / 2f - panelW / 2f;
        float py = (Ui.Vh - panelH) / 2f;

        float armorX = px + 18f;
        float armorY = py + 34f;
        float gridX = armorX + slot + 16f;
        float gridY = py + 34f;
        float hotbarY = gridY + mainRows * (slot + gap) + 12f;
        float offhandX = armorX;
        float offhandY = armorY + 4 * (slot + gap) + 6f;

        if (Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right)) {
            bool right = Raylib.IsMouseButtonPressed(MouseButton.Right);
            bool slotHit = false;

            // Клик по слотам брони
            for (int a = 0; a < 4; a++) {
                var aRect = new Rectangle(armorX, armorY + a * (slot + gap), slot, slot);
                if (Raylib.CheckCollisionPointRec(mouse, aRect)) {
                    slotHit = true;
                    if (Held.HasValue && Held.Value.Quantity > 0) {
                        var held = Held.Value;
                        var heldArmorType = GameData.GetArmorType(held.Item.Definition.Id);
                        if (heldArmorType.HasValue && (int)heldArmorType.Value == a) {
                            var oldArmor = session.Player.Armor[a];
                            session.Player.Armor[a] = held;
                            Held = oldArmor;
                            SoundSystem.PlayPop();
                        }
                    } else if (session.Player.Armor[a] != null) {
                        Held = session.Player.Armor[a];
                        session.Player.Armor[a] = null;
                        SoundSystem.PlayPop();
                    }
                    return;
                }
            }

            // Клик по слоту второй руки
            var offhandRec = new Rectangle(offhandX, offhandY, slot, slot);
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
                return;
            }

            // Клик по основному инвентарю
            for (int row = 0; row < mainRows; row++) {
                for (int col = 0; col < cols; col++) {
                    int idx = 9 + row * cols + col;
                    if (SlotClicked(session, inv, gridX + col * (slot + gap), gridY + row * (slot + gap), idx, right)) {
                        slotHit = true;
                        break;
                    }
                }
                if (slotHit) break;
            }
            if (!slotHit) {
                for (int col = 0; col < cols; col++) {
                    if (SlotClicked(session, inv, gridX + col * (slot + gap), hotbarY, col, right)) {
                        slotHit = true;
                        break;
                    }
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

        // Выбрасывание предмета клавишей Q / Ctrl+Q при наведении на слот
        if (Raylib.IsKeyPressed(KeyboardKey.Q)) {
            bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            for (int row = 0; row < mainRows; row++) {
                for (int col = 0; col < cols; col++) {
                    int idx = 9 + row * cols + col;
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + col * (slot + gap), gridY + row * (slot + gap), 52, 52))) {
                        var slotEntry = inv.Slots[idx];
                        if (slotEntry.HasValue && slotEntry.Value.Quantity > 0) {
                            var dropPos = session.Player.Eye + session.Player.Forward * 0.5f;
                            int dropCount = ctrl ? slotEntry.Value.Quantity : 1;
                            var pickup = new ItemPickup(slotEntry.Value.Item, dropCount, dropPos) {
                                PickupDelay = 1.2f,
                                Velocity = session.Player.Forward * 4.5f + new Vector3(0f, 2.0f, 0f)
                            };
                            session.World.Pickups.Add(pickup);
                            SoundSystem.PlayPop();
                            if (!ctrl && slotEntry.Value.Quantity > 1) {
                                inv.Slots[idx] = slotEntry.Value with { Quantity = slotEntry.Value.Quantity - 1 };
                            } else {
                                inv.RemoveAt(idx);
                            }
                            return;
                        }
                    }
                }
            }
            for (int col = 0; col < cols; col++) {
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + col * (slot + gap), hotbarY, 52, 52))) {
                    int idx = col;
                    var slotEntry = inv.Slots[idx];
                    if (slotEntry.HasValue && slotEntry.Value.Quantity > 0) {
                        var dropPos = session.Player.Eye + session.Player.Forward * 0.5f;
                        int dropCount = ctrl ? slotEntry.Value.Quantity : 1;
                        var pickup = new ItemPickup(slotEntry.Value.Item, dropCount, dropPos) {
                            PickupDelay = 1.2f,
                            Velocity = session.Player.Forward * 4.5f + new Vector3(0f, 2.0f, 0f)
                        };
                        session.World.Pickups.Add(pickup);
                        SoundSystem.PlayPop();
                        if (!ctrl && slotEntry.Value.Quantity > 1) {
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
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + col * (slot + gap), gridY + row * (slot + gap), 52, 52))) {
                        var tmp = inv.Slots[idx];
                        inv.Slots[idx] = inv.Slots[hotbarKey];
                        inv.Slots[hotbarKey] = tmp;
                        return;
                    }
                }
            }
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
        var mouse = Ui.Mouse();
        if (!Raylib.CheckCollisionPointRec(mouse, rect)) return false;

        var entryInSlot = inv.Slots[idx];
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        if (shift && !rightClick && entryInSlot != null) {
            var item = entryInSlot.Value;
            var armorType = GameData.GetArmorType(item.Item.Definition.Id);
            if (armorType.HasValue) {
                int aSlot = (int)armorType.Value;
                if (session.Player.Armor[aSlot] == null) {
                    session.Player.Armor[aSlot] = item;
                    inv.RemoveAt(idx);
                    SoundSystem.PlayPop();
                    return true;
                }
            }

            // Shift-Click: быстрое перемещение между хотбаром (0..8) и основным инвентарем (9..35)
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
        var mouse = Ui.Mouse();
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
        DrawRecipeBookGrid(session, panelX + 4f, bookY, panelW - 8f, panelH - (bookY - panelY) - 4f, PersonalGrid, false);
    }

    // ── Книга рецептов нового поколения ──────────────────────────────────────
    public static string RecipeSearch = "";
    public static bool RecipeSearchActive = false;
    public static GameData.CraftCategory RecipeCategory = GameData.CraftCategory.All;
    public static bool RecipeOnlyCraftable = false;
    public static int RecipePage = 0;

    private static void DrawRecipeBookGrid(GameSession session, float rx, float ry, float rw, float rh, ItemEntry?[] targetGrid, bool is3x3) {
        var mouse = Ui.Mouse();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool shiftHeld = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        var inv = session.Player.Inventory;

        var bookRect = new Rectangle(rx, ry, rw, rh);
        Raylib.DrawRectangleRounded(bookRect, 0.04f, 6, new Color(42, 48, 60, 240));
        Raylib.DrawRectangleRoundedLinesEx(bookRect, 0.04f, 6, 1.5f, new Color(75, 85, 105, 230));

        // Заголовок
        Fonts.Draw("Книга рецептов", rx + 8f, ry + 4f, 15f, new Color(240, 245, 255, 255));

        // Строка поиска и переключатель "Только доступные"
        float searchY = ry + 24f;
        float btnW = 92f;
        float searchW = rw - btnW - 18f;
        var searchRect = new Rectangle(rx + 6f, searchY, searchW, 22f);
        var craftableBtnRect = new Rectangle(rx + rw - btnW - 6f, searchY, btnW, 22f);

        bool searchHov = Raylib.CheckCollisionPointRec(mouse, searchRect);
        if (leftClick) {
            RecipeSearchActive = searchHov;
        }

        if (RecipeSearchActive) {
            int charCode = Raylib.GetCharPressed();
            while (charCode > 0) {
                if (charCode >= 32 && RecipeSearch.Length < 25) {
                    RecipeSearch += (char)charCode;
                    RecipePage = 0;
                }
                charCode = Raylib.GetCharPressed();
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && RecipeSearch.Length > 0) {
                RecipeSearch = RecipeSearch[..^1];
                RecipePage = 0;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.Enter)) {
                RecipeSearchActive = false;
            }
        }

        // Отрисовка поля поиска
        Raylib.DrawRectangleRounded(searchRect, 0.15f, 4, RecipeSearchActive ? new Color(55, 65, 82, 255) : new Color(30, 35, 45, 230));
        Raylib.DrawRectangleRoundedLinesEx(searchRect, 0.15f, 4, 1f, RecipeSearchActive ? new Color(120, 180, 255, 255) : new Color(65, 75, 95, 200));

        string displayText = string.IsNullOrEmpty(RecipeSearch) ? (RecipeSearchActive ? "" : "Поиск...") : RecipeSearch;
        Color searchTextColor = string.IsNullOrEmpty(RecipeSearch) ? new Color(140, 150, 165, 180) : Color.White;
        Fonts.Draw(displayText, searchRect.X + 6f, searchRect.Y + 3f, 12f, searchTextColor);

        // Кнопка очистки поиска если текст введен
        if (!string.IsNullOrEmpty(RecipeSearch)) {
            var clearRect = new Rectangle(searchRect.X + searchRect.Width - 18f, searchRect.Y + 2f, 16f, 18f);
            if (Raylib.CheckCollisionPointRec(mouse, clearRect)) {
                Fonts.Draw("x", clearRect.X + 4f, clearRect.Y + 1f, 12f, new Color(255, 120, 120, 255));
                if (leftClick) {
                    RecipeSearch = "";
                    RecipePage = 0;
                }
            } else {
                Fonts.Draw("x", clearRect.X + 4f, clearRect.Y + 1f, 12f, new Color(180, 190, 205, 200));
            }
        }

        // Кнопка переключения "Только доступные"
        bool btnHov = Raylib.CheckCollisionPointRec(mouse, craftableBtnRect);
        Color btnBg = RecipeOnlyCraftable ? (btnHov ? new Color(55, 135, 75, 255) : new Color(40, 110, 55, 240))
                                          : (btnHov ? new Color(65, 72, 88, 230) : new Color(45, 50, 62, 200));
        Raylib.DrawRectangleRounded(craftableBtnRect, 0.15f, 4, btnBg);
        Raylib.DrawRectangleRoundedLinesEx(craftableBtnRect, 0.15f, 4, 1f, RecipeOnlyCraftable ? new Color(100, 230, 120, 255) : new Color(80, 90, 110, 200));
        Fonts.DrawCentered(RecipeOnlyCraftable ? "Доступно" : "Все", craftableBtnRect.X + craftableBtnRect.Width / 2f, craftableBtnRect.Y + 3f, 11f, RecipeOnlyCraftable ? Color.White : new Color(190, 200, 215, 220));
        if (btnHov && leftClick) {
            RecipeOnlyCraftable = !RecipeOnlyCraftable;
            RecipePage = 0;
        }

        // Вкладки категорий (равномерное распределение по ширине без вылезания)
        float tabY = searchY + 26f;
        var categories = new[] {
            (GameData.CraftCategory.All, "Все"),
            (GameData.CraftCategory.Weapons, "Оружие"),
            (GameData.CraftCategory.Tools, "Инстр."),
            (GameData.CraftCategory.Armor, "Броня"),
            (GameData.CraftCategory.Blocks, "Блоки"),
            (GameData.CraftCategory.Food, "Еда"),
            (GameData.CraftCategory.Materials, "Ресурсы")
        };
        float tabH = 19f;
        float totalTabGap = (categories.Length - 1) * 3f;
        float tabW = (rw - 12f - totalTabGap) / categories.Length;
        for (int i = 0; i < categories.Length; i++) {
            var (cat, label) = categories[i];
            float tx = rx + 6f + i * (tabW + 3f);
            var tabRect = new Rectangle(tx, tabY, tabW, tabH);
            bool isSel = RecipeCategory == cat;
            bool tabHov = Raylib.CheckCollisionPointRec(mouse, tabRect);

            Color tabBg = isSel ? new Color(50, 125, 70, 255) : (tabHov ? new Color(65, 75, 95, 230) : new Color(40, 46, 58, 200));
            Raylib.DrawRectangleRounded(tabRect, 0.2f, 4, tabBg);
            Raylib.DrawRectangleRoundedLinesEx(tabRect, 0.2f, 4, 1f, isSel ? new Color(100, 225, 120, 255) : new Color(60, 70, 90, 180));
            Fonts.DrawCentered(label, tx + tabW / 2f, tabY + 2f, 10.5f, isSel ? Color.White : (tabHov ? new Color(230, 235, 245, 255) : new Color(160, 170, 185, 210)));

            if (tabHov && leftClick) {
                RecipeCategory = cat;
                RecipePage = 0;
            }
        }

        // Фильтрация рецептов
        var list = new List<GameData.CraftRecipe>();
        foreach (var r in GameData.CraftRecipes) {
            if (r.IsSmelt) continue;
            if (r.Needs3x3 && !is3x3) continue;
            if (RecipeCategory != GameData.CraftCategory.All && r.Category != RecipeCategory) continue;
            if (!string.IsNullOrWhiteSpace(RecipeSearch)) {
                if (!r.Name.Contains(RecipeSearch, StringComparison.OrdinalIgnoreCase) &&
                    !r.Output.Name.Contains(RecipeSearch, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            if (RecipeOnlyCraftable) {
                bool canCraft = true;
                foreach (var (reqItem, reqCount) in r.Ingredients) {
                    if (inv.CountOf(reqItem) < reqCount) { canCraft = false; break; }
                }
                if (!canCraft) continue;
            }
            list.Add(r);
        }

        // Сетка иконок
        float gridTopY = tabY + tabH + 6f;
        float bottomBarH = 22f;
        float gridAreaH = (ry + rh - bottomBarH - 4f) - gridTopY;
        const float slotSz = 38f, gap = 4f;
        int cols = Math.Max(1, (int)((rw - 12f + gap) / (slotSz + gap)));
        int rows = Math.Max(1, (int)((gridAreaH + gap) / (slotSz + gap)));
        int pageSize = cols * rows;
        int totalPages = Math.Max(1, (list.Count + pageSize - 1) / pageSize);

        if (Raylib.CheckCollisionPointRec(mouse, bookRect)) {
            int wheel = (int)Raylib.GetMouseWheelMove();
            if (wheel != 0) {
                RecipePage = Math.Clamp(RecipePage - wheel, 0, totalPages - 1);
            }
        }
        RecipePage = Math.Clamp(RecipePage, 0, totalPages - 1);

        GameData.CraftRecipe? hoveredRecipe = null;
        float startGridX = rx + (rw - (cols * slotSz + (cols - 1) * gap)) / 2f;

        for (int i = 0; i < pageSize; i++) {
            int rIdx = RecipePage * pageSize + i;
            int r = i / cols;
            int c = i % cols;
            float sx = startGridX + c * (slotSz + gap);
            float sy = gridTopY + r * (slotSz + gap);
            var slotRect = new Rectangle(sx, sy, slotSz, slotSz);

            if (rIdx < list.Count) {
                var recipe = list[rIdx];
                bool canCraft = true;
                foreach (var (reqItem, reqCount) in recipe.Ingredients) {
                    if (inv.CountOf(reqItem) < reqCount) { canCraft = false; break; }
                }

                bool hov = Raylib.CheckCollisionPointRec(mouse, slotRect);
                Color bg = canCraft ? (hov ? new Color(50, 105, 60, 255) : new Color(34, 70, 42, 230))
                                    : (hov ? new Color(60, 68, 82, 235) : new Color(34, 38, 48, 200));
                Color border = canCraft ? new Color(90, 220, 110, 245) : new Color(60, 68, 82, 210);

                Raylib.DrawRectangleRounded(slotRect, 0.15f, 4, bg);
                Raylib.DrawRectangleRoundedLinesEx(slotRect, 0.15f, 4, 1.2f, border);

                Hud.DrawItemIcon(recipe.Output, new Rectangle(sx + 3f, sy + 3f, slotSz - 6f, slotSz - 6f), 1f);
                if (recipe.Count > 1) {
                    Fonts.DrawShadowed($"×{recipe.Count}", sx + 16f, sy + slotSz - 14f, 11f, Color.White);
                }

                if (hov) {
                    hoveredRecipe = recipe;
                    if (leftClick) {
                        if (shiftHeld) {
                            QuickCraftStack(session, recipe, is3x3);
                        } else {
                            AutoFillRecipe(session, recipe, targetGrid, is3x3);
                        }
                    }
                }
            } else {
                // Пустой слот в сетке
                Raylib.DrawRectangleRounded(slotRect, 0.15f, 4, new Color(28, 32, 40, 140));
                Raylib.DrawRectangleRoundedLinesEx(slotRect, 0.15f, 4, 1f, new Color(45, 52, 65, 140));
            }
        }

        // Нижняя панель навигации (Пагинация)
        float navY = ry + rh - 22f;
        var prevRect = new Rectangle(rx + 8f, navY, 26f, 18f);
        var nextRect = new Rectangle(rx + rw - 34f, navY, 26f, 18f);
        bool prevHov = Raylib.CheckCollisionPointRec(mouse, prevRect);
        bool nextHov = Raylib.CheckCollisionPointRec(mouse, nextRect);

        Raylib.DrawRectangleRounded(prevRect, 0.2f, 4, prevHov ? new Color(65, 75, 95, 255) : new Color(42, 48, 62, 200));
        Fonts.DrawCentered("<", prevRect.X + 13f, prevRect.Y + 2f, 12f, RecipePage > 0 ? Color.White : new Color(130, 135, 145, 150));
        if (prevHov && leftClick && RecipePage > 0) RecipePage--;

        Raylib.DrawRectangleRounded(nextRect, 0.2f, 4, nextHov ? new Color(65, 75, 95, 255) : new Color(42, 48, 62, 200));
        Fonts.DrawCentered(">", nextRect.X + 13f, nextRect.Y + 2f, 12f, RecipePage < totalPages - 1 ? Color.White : new Color(130, 135, 145, 150));
        if (nextHov && leftClick && RecipePage < totalPages - 1) RecipePage++;

        string pageInfo = $"Стр. {RecipePage + 1}/{totalPages} ({list.Count} рец.)";
        Fonts.DrawCentered(pageInfo, rx + rw / 2f, navY + 2f, 11f, new Color(180, 190, 205, 230));

        // Отрисовка подробного тултипа рецепта
        if (hoveredRecipe != null && (!Held.HasValue || Held.Value.Quantity <= 0)) {
            DrawRecipeTooltip(session, hoveredRecipe, mouse);
        }
    }

    private static void DrawRecipeTooltip(GameSession session, GameData.CraftRecipe recipe, Vector2 mouse) {
        var inv = session.Player.Inventory;
        var lines = new List<(string Text, Color Col)>();
        lines.Add(($"{recipe.Output.Name} ×{recipe.Count}", new Color(255, 235, 120, 255)));

        string catName = recipe.Category switch {
            GameData.CraftCategory.Weapons => "Оружие",
            GameData.CraftCategory.Tools => "Инструмент",
            GameData.CraftCategory.Armor => "Доспехи",
            GameData.CraftCategory.Blocks => "Блок",
            GameData.CraftCategory.Food => "Еда",
            GameData.CraftCategory.Materials => "Ресурс",
            _ => "Предмет"
        };
        lines.Add(($"[{catName}]", new Color(140, 200, 255, 220)));

        lines.Add(("Ингредиенты:", new Color(200, 200, 210, 255)));
        foreach (var (ing, count) in recipe.Ingredients) {
            int have = inv.CountOf(ing);
            bool ok = have >= count;
            Color ingCol = ok ? new Color(110, 230, 120, 255) : new Color(255, 110, 100, 255);
            lines.Add(($" • {ing.Name}: {have}/{count}", ingCol));
        }

        lines.Add(("ЛКМ — выложить в сетку", new Color(170, 175, 185, 200)));
        lines.Add(("Shift+ЛКМ — скрафтить стак", new Color(130, 215, 150, 220)));

        float maxW = 0f;
        foreach (var (t, _) in lines) maxW = MathF.Max(maxW, Fonts.Measure(t, 12f));
        float boxW = maxW + 16f;
        float boxH = lines.Count * 17f + 10f;
        float bx = Math.Min(mouse.X + 14f, Ui.Vw - boxW - 8f);
        float by = Math.Min(mouse.Y + 14f, Ui.Vh - boxH - 8f);

        Raylib.DrawRectangleRounded(new Rectangle(bx, by, boxW, boxH), 0.12f, 4, new Color(15, 18, 24, 250));
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(bx, by, boxW, boxH), 0.12f, 4, 1.2f, new Color(50, 140, 70, 230));

        float curY = by + 5f;
        foreach (var (t, c) in lines) {
            Fonts.Draw(t, bx + 8f, curY, 12f, c);
            curY += 17f;
        }
    }

    private static void QuickCraftStack(GameSession session, GameData.CraftRecipe recipe, bool is3x3) {
        if (recipe.Needs3x3 && !is3x3) {
            session.AddMessage("Этот рецепт требует Верстак 3×3!");
            return;
        }
        var inv = session.Player.Inventory;
        int maxPerStack = Math.Max(1, recipe.Output.MaxStack);
        int maxCrafts = maxPerStack == 1 ? 1 : Math.Max(1, maxPerStack / recipe.Count);
        foreach (var (item, count) in recipe.Ingredients) {
            int have = inv.CountOf(item);
            if (count > 0) maxCrafts = Math.Min(maxCrafts, have / count);
        }
        if (maxCrafts <= 0) {
            session.AddMessage($"Не хватает ингредиентов для «{recipe.Name}»!");
            return;
        }

        int crafted = 0;
        for (int c = 0; c < maxCrafts; c++) {
            var item = GameData.NewItem(recipe.Output);
            bool ok = true;
            foreach (var (ing, count) in recipe.Ingredients) {
                if (!inv.TryRemove(ing, count)) { ok = false; break; }
            }
            if (!ok) break;

            if (!inv.TryInsert(item, recipe.Count)) {
                // Если нет места в инвентаре, дропаем на землю
                var p = session.Player.Position + new Vector3(0f, 0.5f, 0f);
                session.World.Pickups.Add(new ItemPickup(item, recipe.Count, p) {
                    PickupDelay = 0.5f,
                    Velocity = session.Player.Forward * 2.5f + Vector3.UnitY * 2.0f
                });
            }
            crafted += recipe.Count;
        }

        if (crafted > 0) {
            SoundSystem.PlayPop();
            session.AddMessage($"Скрафчено: {recipe.Output.Name} ×{crafted}");
        }
    }

    private static void AutoFillRecipe(GameSession session, GameData.CraftRecipe recipe, ItemEntry?[] grid, bool is3x3) {
        var inv = session.Player.Inventory;
        int gridSize = is3x3 ? 3 : 2;

        var shape = recipe.Shape;
        if (shape == null) return; // рецепты плавки в сетку крафта не раскладываются
        const int shapeW = 3;
        const int shapeH = 3;

        // Найдём bounding box непустых ячеек в shape 3×3
        int minR = shapeH, maxR = -1, minC = shapeW, maxC = -1;
        for (int r = 0; r < shapeH; r++) {
            for (int c = 0; c < shapeW; c++) {
                int idx = r * shapeW + c;
                if (idx < shape.Length && shape[idx] != null) {
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

        // Возвращаем текущие предметы из сетки в инвентарь или выбрасываем
        for (int i = 0; i < grid.Length; i++) {
            if (grid[i].HasValue && grid[i]!.Value.Quantity > 0) {
                if (!inv.TryInsert(grid[i]!.Value.Item, grid[i]!.Value.Quantity)) {
                    var p = session.Player.Position + new Vector3(0f, 0.5f, 0f);
                    session.World.Pickups.Add(new ItemPickup(grid[i]!.Value.Item, grid[i]!.Value.Quantity, p) {
                        PickupDelay = 0.5f,
                        Velocity = session.Player.Forward * 1.5f + Vector3.UnitY * 1.5f
                    });
                }
                grid[i] = null;
            }
        }

        // Проверяем наличие ингредиентов
        foreach (var (reqItem, reqCount) in recipe.Ingredients) {
            if (inv.CountOf(reqItem) < reqCount) {
                session.AddMessage($"Не хватает: {reqItem.Name} ({inv.CountOf(reqItem)}/{reqCount})");
                return;
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
        int w = Ui.Vw, h = Ui.Vh;
        var inv = session.Player.Inventory;
        var mouse = Ui.Mouse();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool rightClick = Raylib.IsMouseButtonPressed(MouseButton.Right);

        const int slotSz = 48, gap = 4;
        int gridW3 = 3 * slotSz + 2 * gap;
        int panelW = 920;
        int panelH = 3 * slotSz + 2 * gap + 4 * 52 + 3 * 4 + 140;

        float px = w / 2f - panelW / 2f, py = (h - panelH) / 2f;
        DrawPanel(px, py, panelW, panelH);

        Fonts.DrawCentered("ВЕРСТАК", w / 2f, py + 8f, 26f, TextDark);
        Fonts.DrawCentered("3×3 крафт · ЛКМ — взять/положить · ПКМ — положить 1", w / 2f, py + 36f, 14f, new Color(90, 90, 90, 255));

        float gridX = px + 24f;
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
        float bookX = resultX + slotSz + 18f;
        float bookW = px + panelW - bookX - 18f;
        float bookH = 3 * (slotSz + gap) + 24f;
        DrawRecipeBookGrid(session, bookX, gridY - 8f, bookW, bookH, WorkbenchGrid, true);

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
        var mouse = Ui.Mouse();
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
        int w = Ui.Vw, h = Ui.Vh;
        var inv = session.Player.Inventory;
        var mouse = Ui.Mouse();
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

    private static void NotifyChestChanged(GameSession session, Container chest) {
        if (GameClient.Active != null) GameClient.Active.SendChestSync(session.ActiveChestPos, chest);
        if (GameServer.Active != null) GameServer.Active.BroadcastChestSync(session.ActiveChestPos, chest);
    }

    // ── Экран сундука (27 слотов сундука + 36 слотов игрока) ─────────────────

    public static void DrawChestUI(GameSession session) {
        int w = Ui.Vw, h = Ui.Vh;
        var pInv = session.Player.Inventory;
        var chestInv = session.World.GetOrCreateChest(session.ActiveChestPos);
        var mouse = Ui.Mouse();
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

        // Кнопки быстрой сортировки и быстрого складывания
        float btnSortW = 104f, btnSortH = 22f;
        var sortBtnRect = new Rectangle(px + panelW - 16f - btnSortW, py + 28f, btnSortW, btnSortH);
        var stackBtnRect = new Rectangle(sortBtnRect.X - 110f, py + 28f, 106f, btnSortH);

        bool sortHov = Raylib.CheckCollisionPointRec(mouse, sortBtnRect);
        bool stackHov = Raylib.CheckCollisionPointRec(mouse, stackBtnRect);

        Raylib.DrawRectangleRounded(sortBtnRect, 0.2f, 4, sortHov ? new Color(60, 75, 95, 255) : new Color(40, 48, 62, 220));
        Raylib.DrawRectangleRoundedLinesEx(sortBtnRect, 0.2f, 4, 1f, sortHov ? new Color(120, 180, 255, 255) : new Color(70, 85, 110, 200));
        Fonts.DrawCentered("Сортировка", sortBtnRect.X + btnSortW / 2f, sortBtnRect.Y + 4f, 12f, sortHov ? Color.White : new Color(200, 215, 235, 220));

        Raylib.DrawRectangleRounded(stackBtnRect, 0.2f, 4, stackHov ? new Color(50, 100, 70, 255) : new Color(32, 65, 45, 220));
        Raylib.DrawRectangleRoundedLinesEx(stackBtnRect, 0.2f, 4, 1f, stackHov ? new Color(100, 220, 130, 255) : new Color(60, 120, 80, 200));
        Fonts.DrawCentered("Сложить всё", stackBtnRect.X + stackBtnRect.Width / 2f, stackBtnRect.Y + 4f, 12f, stackHov ? Color.White : new Color(200, 240, 210, 220));

        if (leftClick) {
            if (sortHov) {
                SortContainer(chestInv);
                session.AddMessage("Сундук отсортирован!");
                NotifyChestChanged(session, chestInv);
            } else if (stackHov) {
                QuickStackToChest(pInv, chestInv, session);
                NotifyChestChanged(session, chestInv);
            }
        }

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
                    Hud.DrawItemDurability(entry.Value.Item, rect);
                }

                if ((leftClick || rightClick) && hov) {
                    HandleContainerSlotClick(chestInv, idx, leftClick, rightClick, pInv);
                    NotifyChestChanged(session, chestInv);
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
                        NotifyChestChanged(session, chestInv);
                        return;
                    }
                }
            }
            for (int col = 0; col < 9; col++) {
                float sx = invX + col * (slotSz + gap);
                float sy = hotY;
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(sx, sy, slotSz, slotSz))) {
                    HandleContainerSlotClick(pInv, col, leftClick, rightClick, chestInv);
                    NotifyChestChanged(session, chestInv);
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

    private static void SortContainer(VoxelFrame.Core.Inventory.Container container) {
        var items = new List<ItemEntry>();
        int limit = Math.Min(27, container.Capacity);
        for (int i = 0; i < limit; i++) {
            if (container.Slots[i] is { } entry && entry.Quantity > 0) {
                items.Add(entry);
                container.Slots[i] = null;
            }
        }
        // Уплотняем одинаковые предметы до MaxStack
        var merged = new List<ItemEntry>();
        foreach (var entry in items.OrderBy(e => e.Item.Definition.Id)) {
            int toAdd = entry.Quantity;
            for (int j = 0; j < merged.Count && toAdd > 0; j++) {
                if (merged[j].Item.Definition.Id == entry.Item.Definition.Id && merged[j].Quantity < merged[j].Item.Definition.MaxStack) {
                    int space = merged[j].Item.Definition.MaxStack - merged[j].Quantity;
                    int add = Math.Min(space, toAdd);
                    merged[j] = merged[j] with { Quantity = merged[j].Quantity + add };
                    toAdd -= add;
                }
            }
            if (toAdd > 0) {
                merged.Add(entry with { Quantity = toAdd });
            }
        }
        for (int i = 0; i < merged.Count && i < limit; i++) {
            container.InsertAt(i, merged[i]);
        }
        SoundSystem.PlayPop();
    }

    private static void QuickStackToChest(VoxelFrame.Core.Inventory.Container pInv, VoxelFrame.Core.Inventory.Container chestInv, GameSession session) {
        int moved = 0;
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        int startSlot = shift ? 0 : 9; // без Shift не трогаем хотбар
        int chestCapacity = Math.Min(27, chestInv.Capacity);

        for (int i = startSlot; i < pInv.Capacity; i++) {
            if (pInv.Slots[i] is not { } pe || pe.Quantity <= 0) continue;
            var item = pe.Item;
            int remaining = pe.Quantity;

            // 1. Сначала объединяем с существующими стаками в сундуке (0..26)
            for (int ci = 0; ci < chestCapacity && remaining > 0; ci++) {
                if (chestInv.Slots[ci] is { } cs && cs.Item.Definition.Id == item.Definition.Id && cs.Quantity < cs.Item.Definition.MaxStack) {
                    int space = cs.Item.Definition.MaxStack - cs.Quantity;
                    int add = Math.Min(space, remaining);
                    chestInv.InsertAt(ci, cs with { Quantity = cs.Quantity + add });
                    remaining -= add;
                    moved += add;
                }
            }

            // 2. Затем кладем остаток в первые свободные пустые слоты сундука (0..26)
            for (int ci = 0; ci < chestCapacity && remaining > 0; ci++) {
                if (chestInv.Slots[ci] == null) {
                    int add = Math.Min(item.Definition.MaxStack, remaining);
                    chestInv.InsertAt(ci, new ItemEntry(item, add));
                    remaining -= add;
                    moved += add;
                }
            }

            if (remaining <= 0) {
                pInv.RemoveAt(i);
            } else if (remaining < pe.Quantity) {
                pInv.InsertAt(i, pe with { Quantity = remaining });
            }
        }

        if (moved > 0) {
            SoundSystem.PlayPop();
            session.AddMessage($"Сложено в сундук: {moved} предм.");
        } else {
            session.AddMessage("Нет предметов для перемещения!");
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
        var mouse = Ui.Mouse();
        var heldRec = new Rectangle(mouse.X - 14f, mouse.Y - 14f, 28f, 28f);
        Hud.DrawItemIcon(held.Item.Definition, heldRec, 1f);
        if (held.Quantity > 1)
            Fonts.Draw($"×{held.Quantity}", mouse.X - 14f, mouse.Y + 8f, 15f, Color.White);
        Hud.DrawItemDurability(held.Item, heldRec);
    }

    public static void DrawItemTooltip(ItemInstance item, System.Numerics.Vector2 mouse) {
        DrawItemTooltip(item.Definition, mouse, item.Durability);
    }

    public static void DrawItemTooltip(ItemDefinition def, System.Numerics.Vector2 mouse, int currentDurability = -1) {
        string title = def.Name;
        ushort itemId = def.Id;

        string subtext1 = "";
        string subtext2 = "";

        if (GameData.FoodValue.TryGetValue(itemId, out float food)) {
            subtext1 = $"Восстанавливает: +{food} сытости";
        } else if (GameData.GetArmorPoints(itemId) > 0) {
            int pts = GameData.GetArmorPoints(itemId);
            int maxDur = GameData.GetMaxArmorDurability(itemId);
            int cur = currentDurability >= 0 ? currentDurability : maxDur;
            subtext1 = $"+{pts} Очков брони";
            subtext2 = $"Прочность: {cur} / {maxDur}";
        } else if (GameData.GetToolTier(itemId) > 0) {
            float dmg = GameData.GetWeaponDamage(itemId);
            float cd = GameData.GetWeaponCooldown(itemId);
            float spd = cd > 0 ? 1f / cd : 4f;
            int maxDur = GameData.GetMaxToolDurability(itemId);
            int cur = currentDurability >= 0 ? currentDurability : maxDur;
            subtext1 = $"Урон: {dmg} HP · Скорость: {spd:0.#}";
            subtext2 = $"Прочность: {cur} / {maxDur}";
        } else if (GameData.TryGetBlockByItem(itemId, out var blk) && blk != null) {
            subtext1 = blk.IsSolid ? "Твёрдый строительный блок" : "Декоративный объект";
        }

        // Цвета редкости предмета
        Color titleCol = itemId switch {
            var id when id == GameData.GoldenAppleItem.Id || id == GameData.TotemItem.Id || id == GameData.GoldSwordItem.Id => new Color(255, 220, 70, 255),
            var id when id == GameData.DiamondSwordItem.Id || id == GameData.DiamondPickaxeItem.Id || id == GameData.DiamondAxeItem.Id || id == GameData.DiamondShovelItem.Id || id == GameData.DiamondHoeItem.Id ||
                        id == GameData.DiamondHelmetItem.Id || id == GameData.DiamondChestplateItem.Id || id == GameData.DiamondLeggingsItem.Id || id == GameData.DiamondBootsItem.Id => new Color(100, 235, 255, 255),
            var id when id == GameData.MusicDiscItem.Id || id == GameData.DesertArtifactItem.Id || id == GameData.SwampArtifactItem.Id || id == GameData.NetherArtifactItem.Id || id == GameData.VoidKeyItem.Id || id == GameData.NetherTotemItem.Id || id == GameData.DesertTotemItem.Id || id == GameData.SwampTotemItem.Id => new Color(220, 130, 255, 255),
            var id when id == GameData.IronSwordItem.Id || id == GameData.IronPickaxeItem.Id || id == GameData.IronAxeItem.Id || id == GameData.IronShovelItem.Id || id == GameData.IronHoeItem.Id ||
                        id == GameData.IronHelmetItem.Id || id == GameData.IronChestplateItem.Id || id == GameData.IronLeggingsItem.Id || id == GameData.IronBootsItem.Id => new Color(220, 230, 240, 255),
            _ => new Color(255, 255, 255, 255)
        };

        float w = MathF.Max(Fonts.Measure(title, 16f), MathF.Max(Fonts.Measure(subtext1, 13f), Fonts.Measure(subtext2, 13f))) + 24f;
        float h = string.IsNullOrEmpty(subtext1) ? 32f : string.IsNullOrEmpty(subtext2) ? 52f : 68f;

        float tx = mouse.X + 14f;
        float ty = mouse.Y - 14f;
        
        if (tx + w > Ui.Vw - 8f) tx = mouse.X - w - 8f;
        if (ty + h > Ui.Vh - 8f) ty = Ui.Vh - h - 8f;
        if (ty < 8f) ty = 8f;
        if (tx < 8f) tx = 8f;

        var bg = new Color(16, 12, 28, 245);
        var border = new Color(85, 55, 145, 255);
        var innerBorder = new Color(45, 25, 80, 255);
        
        Raylib.DrawRectangleRounded(new Rectangle(tx, ty, w, h), 0.12f, 6, bg);
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(tx, ty, w, h), 0.12f, 6, 2f, border);
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(tx + 2, ty + 2, w - 4, h - 4), 0.10f, 6, 1f, innerBorder);

        Fonts.DrawShadowed(title, tx + 10f, ty + 7f, 16f, titleCol);
        if (!string.IsNullOrEmpty(subtext1)) {
            Fonts.Draw(subtext1, tx + 10f, ty + 28f, 13f, new Color(175, 185, 210, 255));
        }
        if (!string.IsNullOrEmpty(subtext2)) {
            Fonts.Draw(subtext2, tx + 10f, ty + 46f, 13f, new Color(125, 215, 130, 255));
        }
    }

    private static void DrawNameTooltip(string name, System.Numerics.Vector2 mouse) {
        float tw = Fonts.Measure(name, 16f) + 16f;
        float th = 28f;
        float tx = mouse.X + 12f, ty = mouse.Y - 12f;
        if (tx + tw > Ui.Vw) tx = mouse.X - tw - 8f;
        if (ty + th > Ui.Vh) ty = Ui.Vh - th - 8f;
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
        var mouse = Ui.Mouse();
        bool hovered = Raylib.CheckCollisionPointRec(mouse, rect);
        bool clicked = enabled && hovered && Raylib.IsMouseButtonPressed(MouseButton.Left);
        if (clicked) SoundSystem.PlayPop();

        // 1. Классический светлый/средне-серый воксельный фон кнопки (#969696 / #808080)
        Color topColor = !enabled 
            ? new Color(95, 98, 105, 230) 
            : (hovered ? new Color(175, 185, 205, 255) : new Color(145, 145, 145, 255));
        Color botColor = !enabled 
            ? new Color(75, 78, 85, 230) 
            : (hovered ? new Color(145, 155, 175, 255) : new Color(115, 115, 115, 255));

        Raylib.DrawRectangleGradientV((int)x, (int)y, (int)width, (int)height, topColor, botColor);

        // 2. Внутренний легкий световой блик в верхней половине
        if (enabled) {
            Raylib.DrawRectangle((int)x + 2, (int)y + 2, (int)width - 4, (int)(height * 0.45f), new Color(255, 255, 255, hovered ? 35 : 20));
        }

        // 3. 3D-фаски (белый блик сверху/слева, глубокая тень снизу/справа)
        if (enabled) {
            Color highlight = hovered ? new Color(255, 255, 255, 255) : new Color(220, 220, 220, 255);
            Color shadow = hovered ? new Color(60, 70, 90, 255) : new Color(55, 55, 55, 255);
            
            Raylib.DrawRectangle((int)x + 1, (int)y + 1, (int)width - 2, 2, highlight);
            Raylib.DrawRectangle((int)x + 1, (int)y + 1, 2, (int)height - 2, highlight);
            Raylib.DrawRectangle((int)x + 1, (int)(y + height - 3), (int)width - 2, 2, shadow);
            Raylib.DrawRectangle((int)(x + width - 3), (int)y + 1, 2, (int)height - 2, shadow);
        } else {
            Raylib.DrawRectangle((int)x + 1, (int)y + 1, (int)width - 2, 2, new Color(120, 125, 135, 200));
            Raylib.DrawRectangle((int)x + 1, (int)y + 1, 2, (int)height - 2, new Color(120, 125, 135, 200));
            Raylib.DrawRectangle((int)x + 1, (int)(y + height - 3), (int)width - 2, 2, new Color(45, 48, 55, 255));
            Raylib.DrawRectangle((int)(x + width - 3), (int)y + 1, 2, (int)height - 2, new Color(45, 48, 55, 255));
        }

        // 4. Внешний контур
        Color outline = !enabled 
            ? new Color(35, 38, 45, 255) 
            : (hovered ? new Color(255, 220, 100, 255) : new Color(25, 25, 25, 255));
        Raylib.DrawRectangleLinesEx(rect, hovered ? 2f : 1.5f, outline);

        // 5. Текст кнопки
        Color textColor = !enabled 
            ? new Color(165, 170, 180, 255) 
            : (hovered ? new Color(255, 255, 200, 255) : Color.White);

        float textY = y + (height - 18f) / 2f;

        // Особый бейдж для «Сетевой игры» если заблокирована
        if (!enabled && label.Contains("Сетевая")) {
            float badgeW = 94f, badgeH = 20f;
            float bx = x + width - badgeW - 10f, by = y + (height - badgeH) / 2f;
            
            // Центрируем текст «Сетевая игра» левее бейджа, чтобы они не накладывались
            float labelCenterX = x + (width - badgeW - 14f) / 2f;
            Fonts.DrawCenteredShadowed(label, labelCenterX, textY, 18f, textColor, 1.5f);

            Raylib.DrawRectangleRec(new Rectangle(bx, by, badgeW, badgeH), new Color(30, 35, 45, 240));
            Raylib.DrawRectangleLinesEx(new Rectangle(bx, by, badgeW, badgeH), 1f, new Color(220, 180, 70, 240));
            Fonts.Draw("В разработке", bx + 6f, by + 3f, 11f, new Color(255, 215, 90, 255));
        } else {
            Fonts.DrawCenteredShadowed(label, x + width / 2f, textY, 18f, textColor, 1.5f);
        }

        return clicked;
    }

    private static void DrawPanel(float x, float y, float width, float height) {
        Raylib.DrawRectangle(0, 0, Ui.Vw, Ui.Vh, new Color(0, 0, 0, 140));
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





