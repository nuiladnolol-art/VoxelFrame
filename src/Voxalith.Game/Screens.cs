using Voxalith.Core;
using System.Numerics;
using Raylib_cs;
using Voxalith.Core.Inventory;
using Voxalith.Core.Materials;

namespace Voxalith.Game;

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

    private static readonly (GameData.CraftCategory Cat, string Label)[] RecipeCategories = {
        (GameData.CraftCategory.All, "Все"),
        (GameData.CraftCategory.Weapons, "Оружие"),
        (GameData.CraftCategory.Tools, "Инстр."),
        (GameData.CraftCategory.Armor, "Броня"),
        (GameData.CraftCategory.Blocks, "Блоки"),
        (GameData.CraftCategory.Food, "Еда"),
        (GameData.CraftCategory.Materials, "Ресурсы")
    };
    private static readonly List<GameData.CraftRecipe> _recipeBookList = new();

    public static string MenuError = "";
    public static bool InWorldSelectScreen = false;
    public static bool InCreateWorldScreen = false;
    public static int SelectedWorldListIndex = 0;
    public static int WorldListScrollOffset = 0;
    public static bool ConfirmDeleteWorld = false;
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

    private static List<SaveSystem.WorldSaveInfo>? _cachedWorlds;
    public static void InvalidateWorldsCache() => _cachedWorlds = null;
    public static List<SaveSystem.WorldSaveInfo> GetCachedWorlds() {
        if (_cachedWorlds == null) _cachedWorlds = SaveSystem.GetAllWorlds();
        return _cachedWorlds;
    }

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
        "Пауза",
        "Вторая рука",
        "Вид камеры",
        "Инфо F3",
        "Чат",
        "Список игроков"
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
        10 => KeyBinds.SwapHands,
        11 => KeyBinds.TogglePerspective,
        12 => KeyBinds.ToggleDebug,
        13 => KeyBinds.Chat,
        14 => KeyBinds.PlayerList,
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
            case 10: KeyBinds.SwapHands = key; break;
            case 11: KeyBinds.TogglePerspective = key; break;
            case 12: KeyBinds.ToggleDebug = key; break;
            case 13: KeyBinds.Chat = key; break;
            case 14: KeyBinds.PlayerList = key; break;
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

        // Глубокий градиентный темный фон из темы
        var theme = UiTheme.Current;
        Raylib.DrawRectangleGradientV(0, 0, w, h, theme.Background, theme.BackgroundDark);

        // Воксельная сетка из квадратов
        int tileSize = 64;
        for (int x = 0; x < w; x += tileSize) {
            for (int y = 0; y < h; y += tileSize) {
                bool isEven = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                if (isEven) {
                    Raylib.DrawRectangle(x, y, tileSize, tileSize, new Color(28, 34, 48, 35));
                }
            }
        }

        // Атмосферные частицы
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

        // Виньетка
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
        "Осторожно: споровики не спят!",
        "Кастомный воксельный движок!",
        "С любовью от SenStol Studio!",
        "Ходоки боятся утреннего солнца!",
        "Дерево само себя не срубит!",
        "Скрафти верстак первым делом!",
        "Болотный страж ждет во тьме...",
        "Ночь темна и полна споровиков!",
        "Где мой факел?!",
        "Костная мука творит чудеса!",
        "Морковь полезна для зрения!",
        "Жареная картошечка в печи!",
        "60 кадров в секунду на чистом C#!",
        "Бесконечные процедурные миры!",
        "Никакой Java, только .NET 10!",
        "Воксели правят миром!",
        "Не забудь поставить кровать!",
        "Победи Слизня Края — спаси Аструм!",
        "Свинки обожают морковь!",
        "Шерсть для кровати добыта!",
        "Книга рецептов всегда под рукой!",
        "Слушай шаги за спиной...",
        "Алмазная кирка — лучший друг!",
        "Не смотри в глаза Астральному Сталкеру!",
        "Лава течет на самом дне!",
        "Огороды цветут и пахнут!",
        "Посади дуб около дома!",
        "Секретные данжи ждут тебя!",
        "Улучшай броню вовремя!",
        "Споровик уже шипит сзади...",
        "Уютный домик из дубовых досок!",
        "Печка греет, хлеб печется!",
        "Золотые яблоки спасают жизнь!",
        "Костяные Лучники стреляют метко!",
        "Врата в Инфернум открыты!",
        "Пепельные Ифриты не любят воду!",
        "Зачаруй свой меч на остроту!",
        "Генерация чанков на лету!",
        "Никаких фризов и лагов!",
        "Построй свой замок мечты!",
        "Рецепты крафта на любой вкус!",
        "Спасибо, что играешь в Voxalith!",
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
                if (key >= 32 && key <= 126 || key >= 1040 && key <= 1103 || key == 1025 || key == 1105) {
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
                InvalidateWorldsCache();
                InCreateWorldScreen = false;
                InWorldSelectScreen = false;
            }
            if (Button(w / 2f + 5f, btnY, 210f, 48f, "Отмена", true)) {
                InCreateWorldScreen = false;
            }
        } else if (InWorldSelectScreen) {
            Fonts.DrawTitle3D("ВЫБОР МИРА", w / 2f, h * 0.08f, 44f);

            var worlds = GetCachedWorlds();
            if (worlds.Count > 0) {
                float wheel = Raylib.GetMouseWheelMove();
                if (wheel > 0) WorldListScrollOffset = Math.Max(0, WorldListScrollOffset - 1);
                else if (wheel < 0) WorldListScrollOffset = Math.Min(Math.Max(0, worlds.Count - 5), WorldListScrollOffset + 1);
                WorldListScrollOffset = Math.Clamp(WorldListScrollOffset, 0, Math.Max(0, worlds.Count - 5));
            } else {
                WorldListScrollOffset = 0;
            }
            if (SelectedWorldListIndex >= worlds.Count) SelectedWorldListIndex = Math.Max(0, worlds.Count - 1);

            float listY = h * 0.16f;
            float cardW = MathF.Min(600f, w - 80f);
            float cardH = 68f;
            float cardX = w / 2f - cardW / 2f;

            if (worlds.Count == 0) {
                Fonts.DrawCenteredShadowed("Нет созданных миров. Нажмите 'Создать новый мир'", w / 2f, h * 0.40f, 20f, new Color(180, 190, 205, 255));
            } else {
                for (int slot = 0; slot < 5 && (slot + WorldListScrollOffset) < worlds.Count; slot++) {
                    int i = slot + WorldListScrollOffset;
                    var wi = worlds[i];
                    float cy = listY + slot * (cardH + 10f);
                    var cardRec = new Rectangle(cardX, cy, cardW, cardH);
                    bool isSelected = (i == SelectedWorldListIndex);
                    bool cardHover = Raylib.CheckCollisionPointRec(Ui.Mouse(), cardRec);

                    if (Raylib.IsMouseButtonPressed(MouseButton.Left) && cardHover) {
                        if (SelectedWorldListIndex != i) ConfirmDeleteWorld = false;
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

                if (worlds.Count > 5) {
                    Fonts.DrawCentered($"Показаны {WorldListScrollOffset + 1}..{Math.Min(worlds.Count, WorldListScrollOffset + 5)} из {worlds.Count} (прокрутка колёсиком мыши)", w / 2f, listY + 5 * (cardH + 10f) + 6f, 14f, new Color(170, 195, 230, 220));
                }
            }

            // Нижняя панель действий
            float bY1 = h * 0.78f;
            float bY2 = h * 0.86f;
            float bW = 200f;

            bool hasSelection = worlds.Count > 0 && SelectedWorldListIndex >= 0 && SelectedWorldListIndex < worlds.Count;
            if (Button(w / 2f - bW - 8f, bY1, bW, 46f, "Играть в мире", hasSelection)) {
                ConfirmDeleteWorld = false;
                SaveSystem.CurrentWorldPath = worlds[SelectedWorldListIndex].FilePath;
                action = MenuAction.Continue;
                InWorldSelectScreen = false;
            }
            if (Button(w / 2f + 8f, bY1, bW, 46f, "Создать новый мир", true)) {
                ConfirmDeleteWorld = false;
                InCreateWorldScreen = true;
                WorldNameInput = "Новый мир";
                WorldSeedInput = "";
                ActiveTextInputField = 0;
            }

            string deleteLabel = ConfirmDeleteWorld ? "Точно удалить?" : "Удалить";
            if (Button(w / 2f - bW - 8f, bY2, bW, 46f, deleteLabel, hasSelection)) {
                if (!ConfirmDeleteWorld) {
                    ConfirmDeleteWorld = true;
                } else {
                    ConfirmDeleteWorld = false;
                    SaveSystem.DeleteSave(worlds[SelectedWorldListIndex].FilePath);
                    InvalidateWorldsCache();
                    if (SelectedWorldListIndex >= worlds.Count - 1) SelectedWorldListIndex = Math.Max(0, worlds.Count - 2);
                }
            }
            if (Button(w / 2f + 8f, bY2, bW, 46f, "Отмена", true)) {
                ConfirmDeleteWorld = false;
                InWorldSelectScreen = false;
            }
        } else {
            // ── ЛОГОТИП ──────────────────────────────────────────────────────
            Fonts.DrawTitle3D("VOXALITH", w / 2f, h * 0.15f, 76f);
            DrawSplashText(w / 2f + 210f, h * 0.15f + 56f);

            // ── КНОПКИ МЕНЮ ──────────────────────────────────────────────────
            float cy = h * 0.36f;
            float btnW = 360f;
            float btnH = 48f;
            float gapY = 12f;

            if (Button(w / 2f - btnW / 2f, cy, btnW, btnH, "Одиночная игра", true)) {
                InvalidateWorldsCache();
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
            var verRec = new Rectangle(14f, h - 34f, 195f, 24f);
            Raylib.DrawRectangleRec(verRec, new Color(20, 24, 34, 180));
            Raylib.DrawRectangleLinesEx(verRec, 1f, new Color(50, 60, 80, 180));
            Fonts.Draw($"Voxalith {Program.Version}", 22f, h - 30f, 14f, new Color(190, 205, 230, 220));

            // Правый копирайт
            var copyRec = new Rectangle(w - 200f, h - 34f, 186f, 24f);
            Raylib.DrawRectangleRec(copyRec, new Color(20, 24, 34, 180));
            Raylib.DrawRectangleLinesEx(copyRec, 1f, new Color(50, 60, 80, 180));
            Fonts.DrawCentered("SenStol Studio • 2026", w - 107f, h - 30f, 14f, new Color(190, 205, 230, 220));
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
            "Voxalith: ИСТИННЫЙ ФИНАЛ",
            "",
            "--- ВЕЛИКИЙ ТРИУМФ ---",
            "",
            "Истинный Слизень Края повержен!",
            "Бездна очищена от древней тьмы.",
            "",
            "Все измерения — Терра, Инфернум и Аструм —",
            "навеки обрели покой и безопасность.",
            "",
            "Ты одолел всех стражей, собрал все реликвии",
            "и покорил само Дно Реальности.",
            "",
            "Ты — Истинная Легенда Voxalith!",
            "",
            "Спасибо за невероятное прохождение!",
            "Твой бесконечный мир ждёт тебя.",
        } : new[] {
            "Voxalith: ТЕНЬ КРАЯ",
            "",
            "Слизень Края повержен... но так ли это?",
            "",
            "Острова Аструма хранят древнюю тайну,",
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

        const string skipHint = "[ESC / ПРОБЕЛ] — Пропустить титры";
        float skipW = Fonts.Measure(skipHint, 18f);
        Fonts.DrawShadowed(skipHint, MathF.Max(12f, w - skipW - 18f), h - 30, 18f, new Color(160, 160, 180, 180));
    }

    // ── Общие элементы ───────────────────────────────────────────────────────

    private static bool Button(float x, float y, float width, float height, string label, bool enabled) {
        var rect = new Rectangle(x, y, width, height);
        var mouse = Ui.Mouse();
        bool hovered = Raylib.CheckCollisionPointRec(mouse, rect);
        bool clicked = enabled && hovered && Raylib.IsMouseButtonPressed(MouseButton.Left);
        if (clicked) SoundSystem.PlayPop();

        // 1. Фон кнопки из темы
        var theme = UiTheme.Current;
        Color topColor = !enabled 
            ? theme.ButtonDisabled 
            : (hovered ? theme.ButtonHover : theme.ButtonNormal);
        Color botColor = !enabled 
            ? theme.ButtonDisabled 
            : (hovered ? new Color((byte)Math.Max(0, theme.ButtonHover.R - 15), (byte)Math.Max(0, theme.ButtonHover.G - 15), (byte)Math.Max(0, theme.ButtonHover.B - 15), (byte)255)
                       : new Color((byte)Math.Max(0, theme.ButtonNormal.R - 15), (byte)Math.Max(0, theme.ButtonNormal.G - 15), (byte)Math.Max(0, theme.ButtonNormal.B - 15), (byte)255));

        Raylib.DrawRectangleGradientV((int)x, (int)y, (int)width, (int)height, topColor, botColor);

        // 2. Внутренний легкий световой блик в верхней половине
        if (enabled) {
            Raylib.DrawRectangle((int)x + 2, (int)y + 2, (int)width - 4, (int)(height * 0.45f), new Color(255, 255, 255, hovered ? 25 : 12));
        }

        // 3. 3D-фаски
        if (enabled) {
            Color highlight = hovered ? theme.PanelBorderLight : new Color(255, 255, 255, 60);
            Color shadow = new Color(0, 0, 0, 100);
            
            Raylib.DrawRectangle((int)x + 1, (int)y + 1, (int)width - 2, 2, highlight);
            Raylib.DrawRectangle((int)x + 1, (int)y + 1, 2, (int)height - 2, highlight);
            Raylib.DrawRectangle((int)x + 1, (int)(y + height - 3), (int)width - 2, 2, shadow);
            Raylib.DrawRectangle((int)(x + width - 3), (int)y + 1, 2, (int)height - 2, shadow);
        } else {
            Raylib.DrawRectangle((int)x + 1, (int)y + 1, (int)width - 2, 2, new Color(80, 85, 95, 120));
            Raylib.DrawRectangle((int)x + 1, (int)y + 1, 2, (int)height - 2, new Color(80, 85, 95, 120));
            Raylib.DrawRectangle((int)x + 1, (int)(y + height - 3), (int)width - 2, 2, new Color(25, 28, 35, 180));
            Raylib.DrawRectangle((int)(x + width - 3), (int)y + 1, 2, (int)height - 2, new Color(25, 28, 35, 180));
        }

        // 4. Внешний контур
        Color outline = !enabled 
            ? new Color(25, 28, 35, 255) 
            : (hovered ? theme.AccentPrimary : theme.ButtonBorder);
        Raylib.DrawRectangleLinesEx(rect, hovered ? 2f : 1.5f, outline);

        // 5. Текст кнопки
        Color textColor = !enabled 
            ? new Color(125, 130, 140, 255) 
            : (hovered ? theme.TextAccent : theme.TextPrimary);

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





