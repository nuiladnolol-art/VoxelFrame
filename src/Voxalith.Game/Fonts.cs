using System.Numerics;
using Raylib_cs;

namespace Voxalith.Game;

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
        string cwd = Directory.GetCurrentDirectory();

        // 1. Собираем корни для поиска шрифта
        var searchRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            baseDir,
            cwd
        };
        try {
            if (!string.IsNullOrEmpty(AppContext.BaseDirectory))
                searchRoots.Add(AppContext.BaseDirectory);
        } catch { }

        // Поднимаемся вверх на 5 уровней для поиска в корне репозитория при отладке
        var snapshot = searchRoots.ToList();
        foreach (var root in snapshot) {
            try {
                var dir = new DirectoryInfo(root);
                for (int i = 0; i < 5 && dir?.Parent != null; i++) {
                    dir = dir.Parent;
                    searchRoots.Add(dir.FullName);
                }
            } catch { }
        }

        // 2. Приоритетный поиск: только аутентичные пиксельные шрифты игры
        var candidatePaths = new List<string>();
        foreach (var root in searchRoots) {
            candidatePaths.Add(Path.Combine(root, "assets", "fonts", "main.ttf"));
            candidatePaths.Add(Path.Combine(root, "assets", "fonts", "pixel.ttf"));
            candidatePaths.Add(Path.Combine(root, "assets", "fonts", "minecraft.ttf"));
            candidatePaths.Add(Path.Combine(root, "fonts", "main.ttf"));
            candidatePaths.Add(Path.Combine(root, "fonts", "pixel.ttf"));
            candidatePaths.Add(Path.Combine(root, "fonts", "minecraft.ttf"));
        }

        foreach (var path in candidatePaths) {
            if (!File.Exists(path)) continue;
            try {
                // Загружаем в разрешении 32px: базовый размер для пиксельного шрифта
                _font = Raylib.LoadFontEx(path, 32, codepoints, codepoints.Length);
                if (_font.GlyphCount > 100) {
                    // Пиксельный шрифт ОБЯЗАН использовать Point фильтрацию (четкие несглаженные пиксели)
                    Raylib.SetTextureFilter(_font.Texture, TextureFilter.Point);
                    Cyrillic = true;
                    try { Console.WriteLine($"[Fonts] Пиксельный шрифт успешно загружен: {path}"); } catch { }
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
        set.Add(0x266A); // ♪
        set.Add(0x266B); // ♫
        set.Add(0x25C0); // ◀
        set.Add(0x25B2); // ▲
        set.Add(0x25BC); // ▼

        var list = set.ToList();
        list.Sort();
        return list.ToArray();
    }

    public static string StripFormatting(string text) {
        if (string.IsNullOrEmpty(text) || !text.Contains('§')) return text;
        var sb = new System.Text.StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++) {
            if (text[i] == '§' && i + 1 < text.Length) {
                i++; // Пропускаем '§' и код форматирования
            } else {
                sb.Append(text[i]);
            }
        }
        return sb.ToString();
    }

    public static Color GetMinecraftColor(char code, Color fallback) {
        int a = (int)fallback.A;
        return code switch {
            '0' => new Color(0, 0, 0, a),
            '1' => new Color(0, 0, 170, a),
            '2' => new Color(0, 170, 0, a),
            '3' => new Color(0, 170, 170, a),
            '4' => new Color(170, 0, 0, a),
            '5' => new Color(170, 0, 170, a),
            '6' => new Color(255, 170, 0, a),
            '7' => new Color(170, 170, 170, a),
            '8' => new Color(85, 85, 85, a),
            '9' => new Color(85, 85, 255, a),
            'a' or 'A' => new Color(85, 255, 85, a),
            'b' or 'B' => new Color(85, 255, 255, a),
            'c' or 'C' => new Color(255, 85, 85, a),
            'd' or 'D' => new Color(255, 85, 255, a),
            'e' or 'E' => new Color(255, 255, 85, a),
            'f' or 'F' => new Color(255, 255, 255, a),
            _ => fallback
        };
    }

    public static float Measure(string text, float size) {
        if (string.IsNullOrEmpty(text)) return 0f;
        string clean = StripFormatting(text);
        if (!Cyrillic) return PixelFont.Measure(clean, size);
        return Raylib.MeasureTextEx(_font, clean, size, 1f).X;
    }

    public static void Draw(string text, float x, float y, float size, Color color) {
        if (string.IsNullOrEmpty(text)) return;
        if (!Cyrillic) {
            PixelFont.Draw(StripFormatting(text), x, y, size, color);
            return;
        }

        if (!text.Contains('§')) {
            Raylib.DrawTextEx(_font, text, new Vector2(x, y), size, 1f, color);
            return;
        }

        // Рендерим текст с учетом цветовых тегов Minecraft (§)
        float curX = x;
        Color curColor = color;
        var sb = new System.Text.StringBuilder();

        void Flush() {
            if (sb.Length > 0) {
                string chunk = sb.ToString();
                Raylib.DrawTextEx(_font, chunk, new Vector2(curX, y), size, 1f, curColor);
                curX += Raylib.MeasureTextEx(_font, chunk, size, 1f).X;
                sb.Clear();
            }
        }

        for (int i = 0; i < text.Length; i++) {
            if (text[i] == '§' && i + 1 < text.Length) {
                Flush();
                i++;
                char code = text[i];
                curColor = GetMinecraftColor(code, color);
            } else {
                sb.Append(text[i]);
            }
        }
        Flush();
    }

    public static void DrawShadowed(string text, float x, float y, float size, Color color, float shadowDist = 2f) {
        if (string.IsNullOrEmpty(text)) return;
        if (!Cyrillic) {
            string cleanTxt = StripFormatting(text);
            PixelFont.Draw(cleanTxt, x + shadowDist, y + shadowDist, size, new Color(0, 0, 0, (int)(color.A * 180 / 255)));
            PixelFont.Draw(cleanTxt, x, y, size, color);
            return;
        }

        // 1. Тень (чистый текст без тегов разметки)
        string clean = StripFormatting(text);
        int shadowA = (int)(color.A * 190 / 255);
        Raylib.DrawTextEx(_font, clean, new Vector2(x + shadowDist, y + shadowDist), size, 1f, new Color(0, 0, 0, shadowA));

        // 2. Лицевой цветной слой
        Draw(text, x, y, size, color);
    }

    public static void DrawOutlined(string text, float x, float y, float size, Color color, Color outlineColor, float outlineWidth = 1.5f) {
        if (string.IsNullOrEmpty(text)) return;
        string clean = StripFormatting(text);
        if (!Cyrillic) {
            PixelFont.DrawShadowed(clean, x, y, size, color);
            return;
        }
        for (float ox = -outlineWidth; ox <= outlineWidth; ox += outlineWidth) {
            for (float oy = -outlineWidth; oy <= outlineWidth; oy += outlineWidth) {
                if (ox != 0 || oy != 0) {
                    Raylib.DrawTextEx(_font, clean, new Vector2(x + ox, y + oy), size, 1f, outlineColor);
                }
            }
        }
        Draw(text, x, y, size, color);
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
        string clean = StripFormatting(text);
        if (!Cyrillic) {
            PixelFont.DrawCentered(clean, cx + 2f, y + 2f, size, new Color(20, 15, 10, 220));
            PixelFont.DrawCentered(clean, cx, y, size, new Color(255, 240, 140, 255));
            return;
        }
        float w = Measure(clean, size);
        float x = cx - w / 2f;

        // 1. Глубокая тень внизу
        for (int d = 6; d >= 3; d--) {
            Raylib.DrawTextEx(_font, clean, new Vector2(x + d, y + d), size, 1f, new Color(20, 15, 10, 220));
        }

        // 2. Темно-бронзовый нижний 3D-скос (экструзия)
        for (int d = 2; d >= 1; d--) {
            Raylib.DrawTextEx(_font, clean, new Vector2(x, y + d), size, 1f, new Color(130, 80, 15, 255));
        }

        // 3. Черная контурная подложка
        for (int dx = -2; dx <= 2; dx++) {
            for (int dy = -2; dy <= 2; dy++) {
                if (Math.Abs(dx) + Math.Abs(dy) > 2) continue;
                Raylib.DrawTextEx(_font, clean, new Vector2(x + dx, y + dy), size, 1f, new Color(30, 22, 12, 255));
            }
        }

        // 4. Золотой градиентный лицевой слой (с верхним сиянием)
        Raylib.DrawTextEx(_font, clean, new Vector2(x, y + 1f), size, 1f, new Color(255, 190, 50, 255));
        Raylib.DrawTextEx(_font, clean, new Vector2(x, y), size, 1f, new Color(255, 240, 140, 255));
    }
}
