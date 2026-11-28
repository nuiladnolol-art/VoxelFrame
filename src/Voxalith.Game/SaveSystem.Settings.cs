using System.IO.Compression;
using System.Numerics;
using System.Text;
using Voxalith.Core;
using Voxalith.Core.World;
using Voxalith.Core.Inventory;

namespace Voxalith.Game;
public static partial class SaveSystem {
    // ── Настройки (JSON) ───────────────────────────────────────────────────

    public enum GraphicsPreset {
        Fast = 0,       // Быстрая: максимальный FPS, упрощенные облака/частицы, без тяжелых каркасов
        Fancy = 1,      // Красивая: сбалансированная графика (3D облака, тени, подводный туман)
        Fabulous = 2    // Ультра: максимальный визуал, плотный туман, полный набор частиц и света
    }

    public static GraphicsPreset GraphicsQuality = GraphicsPreset.Fancy;
    public static bool FancyGraphics {
        get => GraphicsQuality != GraphicsPreset.Fast;
        set => GraphicsQuality = value ? GraphicsPreset.Fancy : GraphicsPreset.Fast;
    }
    public static int CloudsMode = 2; // 0=Off, 1=Fast, 2=Fancy
    public static int ParticlesMode = 2; // 0=Minimal, 1=Decreased, 2=All
    public static bool DynamicLighting = true;
    public static bool EntityShadows = true;
    public static int SoundVolume = 100; // 0..100% (Общая громкость)
    public static int MusicVolume = 70;  // 0..100% (Музыка)
    public static int BlocksVolume = 100; // 0..100% (Блоки, шаги, копание)
    public static int CreaturesVolume = 100; // 0..100% (Мобы и животные)
    public static int WeatherVolume = 100; // 0..100% (Погода, гром, вода)
    public static int PlayerVolume = 100; // 0..100% (Игрок и интерфейс)
    public static int RenderDistanceSetting = 5; // 2..16 (как у ползунка в настройках)
    public static int MouseSensitivity = 100; // 20..200%
    public static bool InvertMouseY = false;   // Инверсия взгляда по вертикали (вверх/вниз)
    public static bool InvertMouseX = false;   // Инверсия взгляда по горизонтали (влево/вправо)
    public static int FovSetting = 70; // 50..110
    public static bool SmoothLighting = true;
    public static int UiScaleMode = 0; // 0 = Авто (по высоте окна), иначе процент 50..300
    public static int PostFxMode = 1;  // 0 = выкл, 1 = вкл (виньетка/золотой час/bloom в настройках графики)
    public static bool PostFxVignette = true;
    public static int PostFxVignetteStrength = 35; // 0..60 %
    public static bool PostFxGoldenHour = true;
    public static bool PostFxBloom = true;
    public static bool PostFxGodRays = true;   // объёмные солнечные лучи (screen-space)
    /// <summary>Тени от солнца/луны (shadow map): 0 = выкл, 1 = средние (1024), 2 = высокие (2048).</summary>
    public static int ShadowQuality = 1;

    public static string SelectedSkin = "cyan";

    public static bool SavedFullscreen;
    public static int SavedWidth;
    public static int SavedHeight;

    public static void SaveSettings() {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            var obj = new Dictionary<string, object> {
                ["Forward"] = (int)KeyBinds.Forward,
                ["Backward"] = (int)KeyBinds.Backward,
                ["Left"] = (int)KeyBinds.Left,
                ["Right"] = (int)KeyBinds.Right,
                ["Jump"] = (int)KeyBinds.Jump,
                ["Crouch"] = (int)KeyBinds.Crouch,
                ["Sprint"] = (int)KeyBinds.Sprint,
                ["Drop"] = (int)KeyBinds.Drop,
                ["Inventory"] = (int)KeyBinds.Inventory,
                ["Pause"] = (int)KeyBinds.Pause,
                ["ToggleDebug"] = (int)KeyBinds.ToggleDebug,
                ["SwapHands"] = (int)KeyBinds.SwapHands,
                ["TogglePerspective"] = (int)KeyBinds.TogglePerspective,
                ["Chat"] = (int)KeyBinds.Chat,
                ["PlayerList"] = (int)KeyBinds.PlayerList,
                ["Fullscreen"] = Raylib_cs.Raylib.IsWindowState(Raylib_cs.ConfigFlags.UndecoratedWindow) || Raylib_cs.Raylib.IsWindowFullscreen(),
                ["Width"] = Raylib_cs.Raylib.GetScreenWidth(),
                ["Height"] = Raylib_cs.Raylib.GetScreenHeight(),
                ["GraphicsQuality"] = (int)GraphicsQuality,
                ["FancyGraphics"] = FancyGraphics,
                ["CloudsMode"] = CloudsMode,
                ["ParticlesMode"] = ParticlesMode,
                ["DynamicLighting"] = DynamicLighting,
                ["EntityShadows"] = EntityShadows,
                ["SoundVolume"] = SoundVolume,
                ["MusicVolume"] = MusicVolume,
                ["BlocksVolume"] = BlocksVolume,
                ["CreaturesVolume"] = CreaturesVolume,
                ["WeatherVolume"] = WeatherVolume,
                ["PlayerVolume"] = PlayerVolume,
                ["RenderDistanceSetting"] = RenderDistanceSetting,
                ["MouseSensitivity"] = MouseSensitivity,
                ["InvertMouseY"] = InvertMouseY,
                ["InvertMouseX"] = InvertMouseX,
                ["FovSetting"] = FovSetting,
                ["SmoothLighting"] = SmoothLighting,
                ["UiScaleMode"] = UiScaleMode,
                ["PostFxMode"] = PostFxMode,
                ["PostFxVignette"] = PostFxVignette,
                ["PostFxVignetteStrength"] = PostFxVignetteStrength,
                ["PostFxGoldenHour"] = PostFxGoldenHour,
                ["PostFxBloom"] = PostFxBloom,
                ["PostFxGodRays"] = PostFxGodRays,
                ["ShadowQuality"] = ShadowQuality,
                ["PlayerNick"] = Screens.PlayerNick,
                ["SelectedSkin"] = SelectedSkin,
                ["SelectedThemeIndex"] = SelectedThemeIndex,
                ["DirectConnectIp"] = Screens.DirectConnectIp,
                ["DirectConnectPort"] = Screens.DirectConnectPort,
            };
            string json = System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            string path = SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(tmp, json);
            if (File.Exists(path)) File.Replace(tmp, path, path + ".bak");
            else File.Move(tmp, path);
        } catch (Exception ex) {
            Console.WriteLine($"[SaveSystem] Ошибка сохранения настроек: {ex.Message}");
        }
    }

    public static void LoadSettings() {
        try {
            if (!File.Exists(SettingsPath)) return;
            string json = File.ReadAllText(SettingsPath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            void R(string key, Action<int> set) { if (root.TryGetProperty(key, out var v)) set(v.GetInt32()); }
            void B(string key, Action<bool> set) { if (root.TryGetProperty(key, out var v)) set(v.GetBoolean()); }
            void S(string key, Action<string> set) { if (root.TryGetProperty(key, out var v) && v.GetString() is { } s && !string.IsNullOrWhiteSpace(s)) set(s); }
            R("Forward", v => KeyBinds.Forward = (Raylib_cs.KeyboardKey)v);
            R("Backward", v => KeyBinds.Backward = (Raylib_cs.KeyboardKey)v);
            R("Left", v => KeyBinds.Left = (Raylib_cs.KeyboardKey)v);
            R("Right", v => KeyBinds.Right = (Raylib_cs.KeyboardKey)v);
            R("Jump", v => KeyBinds.Jump = (Raylib_cs.KeyboardKey)v);
            R("Crouch", v => KeyBinds.Crouch = (Raylib_cs.KeyboardKey)v);
            R("Sprint", v => KeyBinds.Sprint = (Raylib_cs.KeyboardKey)v);
            R("Drop", v => KeyBinds.Drop = (Raylib_cs.KeyboardKey)v);
            R("Inventory", v => KeyBinds.Inventory = (Raylib_cs.KeyboardKey)v);
            R("Pause", v => KeyBinds.Pause = (Raylib_cs.KeyboardKey)v);
            R("ToggleDebug", v => KeyBinds.ToggleDebug = (Raylib_cs.KeyboardKey)v);
            R("SwapHands", v => KeyBinds.SwapHands = (Raylib_cs.KeyboardKey)v);
            R("TogglePerspective", v => KeyBinds.TogglePerspective = (Raylib_cs.KeyboardKey)v);
            R("Chat", v => KeyBinds.Chat = (Raylib_cs.KeyboardKey)v);
            R("PlayerList", v => KeyBinds.PlayerList = (Raylib_cs.KeyboardKey)v);
            B("Fullscreen", v => SavedFullscreen = v);
            R("Width", v => SavedWidth = v);
            R("Height", v => SavedHeight = v);
            R("GraphicsQuality", v => {
                GraphicsQuality = (GraphicsPreset)Math.Clamp(v, 0, 2);
            });
            B("FancyGraphics", v => {
                if (!root.TryGetProperty("GraphicsQuality", out _)) {
                    GraphicsQuality = v ? GraphicsPreset.Fancy : GraphicsPreset.Fast;
                }
            });
            R("CloudsMode", v => CloudsMode = Math.Clamp(v, 0, 2));
            R("ParticlesMode", v => ParticlesMode = Math.Clamp(v, 0, 2));
            B("DynamicLighting", v => DynamicLighting = v);
            B("EntityShadows", v => EntityShadows = v);
            R("SoundVolume", v => SoundVolume = Math.Clamp(v, 0, 100));
            R("MusicVolume", v => MusicVolume = Math.Clamp(v, 0, 100));
            R("BlocksVolume", v => BlocksVolume = Math.Clamp(v, 0, 100));
            R("CreaturesVolume", v => CreaturesVolume = Math.Clamp(v, 0, 100));
            R("WeatherVolume", v => WeatherVolume = Math.Clamp(v, 0, 100));
            R("PlayerVolume", v => PlayerVolume = Math.Clamp(v, 0, 100));
            // Ползунок ограничен 2..16; без зажима битое значение в settings.json
            // (0, отрицательное, 999) ломало стриминг чанков и туман.
            R("RenderDistanceSetting", v => RenderDistanceSetting = Math.Clamp(v, 2, 16));
            R("MouseSensitivity", v => MouseSensitivity = Math.Clamp(v, 20, 200));
            B("InvertMouseY", v => InvertMouseY = v);
            B("InvertMouseX", v => InvertMouseX = v);
            R("FovSetting", v => FovSetting = Math.Clamp(v, 50, 110));
            B("SmoothLighting", v => SmoothLighting = v);
            R("UiScaleMode", v => UiScaleMode = v == 0 ? 0 : Math.Clamp(v, 50, 300));
            R("PostFxMode", v => PostFxMode = Math.Clamp(v, 0, 1));
            B("PostFxVignette", v => PostFxVignette = v);
            R("PostFxVignetteStrength", v => PostFxVignetteStrength = Math.Clamp(v, 0, 60));
            B("PostFxGoldenHour", v => PostFxGoldenHour = v);
            B("PostFxBloom", v => PostFxBloom = v);
            B("PostFxGodRays", v => PostFxGodRays = v);
            R("ShadowQuality", v => ShadowQuality = Math.Clamp(v, 0, 2));
            S("PlayerNick", v => Screens.PlayerNick = v);
            S("SelectedSkin", v => {
                SelectedSkin = v;
                SkinSystem.SetSkin(v);
            });
            S("DirectConnectIp", v => Screens.DirectConnectIp = v);
            S("DirectConnectPort", v => Screens.DirectConnectPort = v);
            R("SelectedThemeIndex", v => SelectedThemeIndex = Math.Clamp(v, 0, 2));
        } catch (Exception ex) {
            Console.WriteLine($"[SaveSystem] Ошибка загрузки настроек: {ex.Message}");
        }
    }
}
