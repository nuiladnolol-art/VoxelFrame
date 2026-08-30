using System.Numerics;
using Raylib_cs;

namespace VoxelFrame.Game;

/// <summary>
/// Шрифт с полной поддержкой кириллицы и спецсимволов:
/// загружает качественный игровой TTF с поддержкой высокого разрешения (64px),
/// либо переключается на пиксельный растровый движок.
/// </summary>
public static class Fonts {
    private static Font _font;
    private static bool _loaded;
    public static bool Cyrillic { get; private set; }

    public static void Load() {
        if (_loaded) return;
        PixelFont.Load(); // Гарантированный fallback

        var codepoints = BuildCodepoints();
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // Приоритетный поиск: локальные игровые шрифты из assets -> качественные системные шрифты
        var candidatePaths = new List<string> {
            Path.Combine(baseDir, "assets", "fonts", "main.ttf"),
            Path.Combine(baseDir, "assets", "fonts", "pixel.ttf"),
            Path.Combine(baseDir, "assets", "fonts", "minecraft.ttf"),
            @"C:\Windows\Fonts\bahnschrift.ttf", // Плотный, современный полужирный игровой шрифт DIN
            @"C:\Windows\Fonts\arialbd.ttf",    // Четкий плотный полужирный шрифт
            @"C:\Windows\Fonts\trebucbd.ttf",   // Trebuchet Bold
            @"C:\Windows\Fonts\segoeuib.ttf",   // Segoe UI Bold
            @"C:\Windows\Fonts\tahomabd.ttf",   // Tahoma Bold
            @"C:\Windows\Fonts\calibrib.ttf",   // Calibri Bold
            @"C:\Windows\Fonts\segoeui.ttf",
            @"C:\Windows\Fonts\arial.ttf"
        };

        foreach (var path in candidatePaths) {
            if (!File.Exists(path)) continue;
            try {
                // Загружаем в высоком разрешении 64px, чтобы любые размеры от 12px до 80px были четкими
                _font = Raylib.LoadFontEx(path, 64, codepoints, codepoints.Length);
                if (_font.GlyphCount > 100) {
                    Raylib.SetTextureFilter(_font.Texture, TextureFilter.Bilinear);
                    Cyrillic = true;
                    break;
                }
            } catch {
                // Try next font
            }
        }
        if (!Cyrillic) _font = Raylib.GetFontDefault();
        _loaded = true;
    }

    public static void Unload() {
        if (_loaded && Cyrillic) {
            Raylib.UnloadFont(_font);
            _loaded = false;
        }
        PixelFont.Unload();
    }

    private static int[] BuildCodepoints() {
        var set = new HashSet<int>();
        // ASCII
        for (int c = 32; c <= 126; c++) set.Add(c);
        // Latin-1 Supplement (включая ×, °, ±, «, », ©, ®)
        for (int c = 160; c <= 255; c++) set.Add(c);
        // Кириллица основная и расширенная (А-Я, а-я, Ё, ё, Ђ, Ѓ, Є, Ѕ, І, Ї, Ј, Љ, Њ, Ћ, Ќ, Ў, Џ)
        for (int c = 0x0400; c <= 0x04FF; c++) set.Add(c);
        // Пунктуация (тире, многоточие, кавычки)
        for (int c = 0x2000; c <= 0x206F; c++) set.Add(c);
        // Спецзнаки
        set.Add(0x2116); // №
        set.Add(0x2192); // →
        set.Add(0x2190); // ←
        set.Add(0x2191); // ↑
        set.Add(0x2193); // ↓
        set.Add(0x2022); // •
        set.Add(0x00D7); // ×
        set.Add(0x25B6); // ▶
        set.Add(0x2728); // ✨
        set.Add(0x2764); // ❤

        var list = set.ToList();
        list.Sort();
        return list.ToArray();
    }

    public static float Measure(string text, float size) {
        if (string.IsNullOrEmpty(text)) return 0f;
        if (!Cyrillic) return PixelFont.Measure(text, size);
        return Raylib.MeasureTextEx(_font, text, size, 1f).X;
    }

    public static void Draw(string text, float x, float y, float size, Color color) {
        if (string.IsNullOrEmpty(text)) return;
        if (!Cyrillic) {
            PixelFont.Draw(text, x, y, size, color);
            return;
        }
        Raylib.DrawTextEx(_font, text, new Vector2(x, y), size, 1f, color);
    }

    public static void DrawShadowed(string text, float x, float y, float size, Color color, float shadowDist = 2f) {
        if (string.IsNullOrEmpty(text)) return;
        if (!Cyrillic) {
            PixelFont.Draw(text, x + shadowDist, y + shadowDist, size, new Color(0, 0, 0, 180));
            PixelFont.Draw(text, x, y, size, color);
            return;
        }
        Raylib.DrawTextEx(_font, text, new Vector2(x + shadowDist, y + shadowDist), size, 1f, new Color(0, 0, 0, 190));
        Raylib.DrawTextEx(_font, text, new Vector2(x, y), size, 1f, color);
    }

    public static void DrawOutlined(string text, float x, float y, float size, Color color, Color outlineColor, float outlineWidth = 1.5f) {
        if (string.IsNullOrEmpty(text)) return;
        if (!Cyrillic) {
            PixelFont.DrawShadowed(text, x, y, size, color);
            return;
        }
        for (float ox = -outlineWidth; ox <= outlineWidth; ox += outlineWidth) {
            for (float oy = -outlineWidth; oy <= outlineWidth; oy += outlineWidth) {
                if (ox != 0 || oy != 0) {
                    Raylib.DrawTextEx(_font, text, new Vector2(x + ox, y + oy), size, 1f, outlineColor);
                }
            }
        }
        Raylib.DrawTextEx(_font, text, new Vector2(x, y), size, 1f, color);
    }

    public static void DrawCentered(string text, float cx, float y, float size, Color color) {
        if (string.IsNullOrEmpty(text)) return;
        float w = Measure(text, size);
        Draw(text, cx - w / 2f, y, size, color);
    }

    public static void DrawCenteredShadowed(string text, float cx, float y, float size, Color color, float shadowDist = 2f) {
        if (string.IsNullOrEmpty(text)) return;
        float w = Measure(text, size);
        DrawShadowed(text, cx - w / 2f, y, size, color, shadowDist);
    }

    /// <summary>
    /// Отрисовка стилизованного 3D-заголовка с многослойным объемом, золотыми фасками и глубокими тенями.
    /// </summary>
    public static void DrawTitle3D(string text, float cx, float y, float size) {
        if (string.IsNullOrEmpty(text)) return;
        float w = Measure(text, size);
        float x = cx - w / 2f;

        // 1. Глубокая тень внизу
        for (int d = 6; d >= 3; d--) {
            Raylib.DrawTextEx(_font, text, new Vector2(x + d, y + d), size, 1.5f, new Color(20, 15, 10, 220));
        }

        // 2. Темно-бронзовый нижний 3D-скос (экструзия)
        for (int d = 2; d >= 1; d--) {
            Raylib.DrawTextEx(_font, text, new Vector2(x, y + d), size, 1.5f, new Color(130, 80, 15, 255));
        }

        // 3. Черная контурная подложка
        for (int dx = -2; dx <= 2; dx++) {
            for (int dy = -2; dy <= 2; dy++) {
                if (Math.Abs(dx) + Math.Abs(dy) > 2) continue;
                Raylib.DrawTextEx(_font, text, new Vector2(x + dx, y + dy), size, 1.5f, new Color(30, 22, 12, 255));
            }
        }

        // 4. Золотой градиентный лицевой слой (с верхним сиянием)
        Raylib.DrawTextEx(_font, text, new Vector2(x, y + 1f), size, 1.5f, new Color(255, 190, 50, 255));
        Raylib.DrawTextEx(_font, text, new Vector2(x, y), size, 1.5f, new Color(255, 240, 140, 255));
    }
}
