using System.Numerics;
using Raylib_cs;

namespace VoxelFrame.Game;

/// <summary>
/// Шрифт с полной поддержкой кириллицы и спецсимволов:
/// загружает чистый TTF из Windows Fonts с билинейной фильтрацией,
/// либо переключается на пиксельный растровый движок без знаков вопроса.
/// </summary>
public static class Fonts {
    private static Font _font;
    private static bool _loaded;
    public static bool Cyrillic { get; private set; }

    public static void Load() {
        if (_loaded) return;
        PixelFont.Load(); // Гарантированный fallback

        var codepoints = BuildCodepoints();
        foreach (var path in new[] {
            @"C:\Windows\Fonts\segoeui.ttf",
            @"C:\Windows\Fonts\arial.ttf",
            @"C:\Windows\Fonts\tahoma.ttf",
            @"C:\Windows\Fonts\consola.ttf",
            @"C:\Windows\Fonts\calibri.ttf",
        }) {
            if (!File.Exists(path)) continue;
            try {
                _font = Raylib.LoadFontEx(path, 32, codepoints, codepoints.Length);
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

    public static void DrawShadowed(string text, float x, float y, float size, Color color) {
        if (string.IsNullOrEmpty(text)) return;
        if (!Cyrillic) {
            PixelFont.Draw(text, x + 1.5f, y + 1.5f, size, Color.Black);
            PixelFont.Draw(text, x, y, size, color);
            return;
        }
        Raylib.DrawTextEx(_font, text, new Vector2(x + 1.5f, y + 1.5f), size, 1f, Color.Black);
        Raylib.DrawTextEx(_font, text, new Vector2(x, y), size, 1f, color);
    }

    public static void DrawCentered(string text, float cx, float y, float size, Color color) {
        if (string.IsNullOrEmpty(text)) return;
        float w = Measure(text, size);
        Draw(text, cx - w / 2f, y, size, color);
    }
}
