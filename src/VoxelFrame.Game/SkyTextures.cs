using Raylib_cs;

namespace VoxelFrame.Game;

/// <summary>
/// Процедурные текстуры неба: звёзды, солнце с короной, луна с фазами и кратерами.
/// Генерируются один раз при старте в памяти; файлы assets/ не затрагиваются.
/// </summary>
public static class SkyTextures {
    private static Texture2D _star;
    private static Texture2D _sun;
    private static Texture2D _moon;
    private static Texture2D _moonPhase; // атлас 8 фаз в полосе
    private static bool _ready;

    public static Texture2D Star => _star;
    public static Texture2D Sun => _sun;
    public static Texture2D Moon => _moon;
    public static Texture2D MoonPhaseAtlas => _moonPhase;
    public static bool Ready => _ready;

    /// <summary>Атлас фаз луны: 8 фаз по горизонтали, каждая PhasePx×PhasePx.</summary>
    public const int PhaseCount = 8;
    public const int PhasePx = 64;

    public static void Load() {
        if (_ready) return;

        // ── Звезда: 32×32, мягкое ядро + 4 луча (крест) ──
        var star = Raylib.GenImageColor(32, 32, new Color(0, 0, 0, 0));
        for (int y = 0; y < 32; y++) {
            for (int x = 0; x < 32; x++) {
                float dx = x - 15.5f, dy = y - 15.5f;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                float core = MathF.Max(0f, 1f - d / 4.5f);
                float rayX = MathF.Max(0f, 1f - MathF.Abs(dy) / 1.1f) * MathF.Max(0f, 1f - d / 14f);
                float rayY = MathF.Max(0f, 1f - MathF.Abs(dx) / 1.1f) * MathF.Max(0f, 1f - d / 14f);
                float intensity = MathF.Max(core, MathF.Max(rayX, rayY) * 0.8f);
                byte a = (byte)(255 * Math.Clamp(intensity, 0f, 1f));
                Raylib.ImageDrawPixel(ref star, x, y, new Color(255, 255, 255, (int)a));
            }
        }
        _star = Raylib.LoadTextureFromImage(star);
        Raylib.SetTextureFilter(_star, TextureFilter.Bilinear);
        Raylib.UnloadImage(star);

        // ── Солнце: 128×128, горячее ядро с плавной короной ──
        var sun = Raylib.GenImageColor(128, 128, new Color(0, 0, 0, 0));
        for (int y = 0; y < 128; y++) {
            for (int x = 0; x < 128; x++) {
                float dx = x - 63.5f, dy = y - 63.5f;
                float d = MathF.Sqrt(dx * dx + dy * dy) / 63.5f;
                Color c;
                if (d < 0.30f) {
                    c = new Color(255, 252, 230, 255); // чистое ядро
                } else if (d < 0.40f) {
                    float t = (d - 0.30f) / 0.10f;
                    c = Lerp(new Color(255, 252, 230, 255), new Color(255, 224, 130, 240), t);
                } else {
                    float t = Math.Clamp((d - 0.40f) / 0.60f, 0f, 1f);
                    float falloff = (1f - t) * (1f - t);
                    c = new Color(255, (int)(byte)(205 - t * 40), (int)(byte)(90 - t * 50), (int)(byte)(200 * falloff));
                }
                if (c.A > 0) Raylib.ImageDrawPixel(ref sun, x, y, c);
            }
        }
        _sun = Raylib.LoadTextureFromImage(sun);
        Raylib.SetTextureFilter(_sun, TextureFilter.Bilinear);
        Raylib.UnloadImage(sun);

        // ── Луна (полная): 64×64, диск с кратерами и лёгкой тенью по краю ──
        var moon = Raylib.GenImageColor(64, 64, new Color(0, 0, 0, 0));
        DrawMoonDisk(ref moon, 64);
        _moon = Raylib.LoadTextureFromImage(moon);
        Raylib.SetTextureFilter(_moon, TextureFilter.Bilinear);
        Raylib.UnloadImage(moon);

        // ── Атлас фаз луны: 8 кадров 64×64 в ряд ──
        var phases = Raylib.GenImageColor(PhaseCount * PhasePx, PhasePx, new Color(0, 0, 0, 0));
        for (int p = 0; p < PhaseCount; p++) {
            // Каждый кадр: полная луна, поверх — «теневой» диск-маска,
            // смещающийся в зависимости от фазы (0 = новолуние, 4 = полнолуние).
            int ox = p * PhasePx;
            DrawMoonDisk(ref phases, PhasePx, ox);

            if (p != 4) { // 4 — полнолуние, без тени
                // Фаза: смещение тени слева направо. Новолуние (0) — луна полностью закрыта.
                float shift = (p / (float)(PhaseCount - 1)) * 2f - 1f; // -1..+1
                float shadowOffset = shift * PhasePx * 0.92f;
                for (int y = 0; y < PhasePx; y++) {
                    for (int x = 0; x < PhasePx; x++) {
                        var c = Raylib.GetImageColor(phases, ox + x, y);
                        if (c.A == 0) continue;
                        // Тень — эллипс, центр смещён от центра диска
                        float cx = PhasePx / 2f - 0.5f + shadowOffset;
                        float r = PhasePx / 2f - 2f;
                        float ddx = x - cx, ddy = y - (PhasePx / 2f - 0.5f);
                        bool inShadow = (ddx * ddx + ddy * ddy) <= r * r;
                        if (inShadow) {
                            Raylib.ImageDrawPixel(ref phases, ox + x, y, new Color(0, 0, 0, 0));
                        }
                    }
                }
            }
        }
        _moonPhase = Raylib.LoadTextureFromImage(phases);
        Raylib.SetTextureFilter(_moonPhase, TextureFilter.Bilinear);
        Raylib.UnloadImage(phases);

        _ready = true;
    }

    private static void DrawMoonDisk(ref Image img, int size, int offsetX = 0) {
        float cx = size / 2f - 0.5f + offsetX, cy = size / 2f - 0.5f;
        float r = size / 2f - 2f;
        // Детерминированные кратеры (позиции в долях диска)
        var craters = new (float Nx, float Ny, float Nr)[] {
            (-0.30f, -0.22f, 0.16f), (0.25f, -0.30f, 0.11f), (0.05f, 0.15f, 0.20f),
            (0.40f, 0.25f, 0.09f), (-0.15f, 0.42f, 0.12f), (-0.45f, 0.10f, 0.08f),
        };
        for (int y = 0; y < size; y++) {
            for (int x = 0; x < size; x++) {
                float dx = x - cx, dy = y - cy;
                float d = MathF.Sqrt(dx * dx + dy * dy);
                if (d > r) continue;
                // Базовый цвет: светлый центр, чуть темнее к краю
                float edge = d / r;
                byte baseCol = (byte)(242 - edge * 26);
                // Терминальная тень по правому краю диска
                float shade = MathF.Max(0f, (dx / r) * 0.5f) + MathF.Max(0f, (dy / r) * 0.12f);
                byte col = (byte)(baseCol * (1f - shade * 0.55f));
                // Кратеры: тёмное дно и светлый ободок
                float shadeMul = 1f;
                foreach (var (nx, ny, nr) in craters) {
                    float cdx = dx / r - nx, cdy = dy / r - ny;
                    float cd = MathF.Sqrt(cdx * cdx + cdy * cdy) / nr;
                    if (cd < 1f) {
                        shadeMul *= 0.55f + 0.45f * cd; // тёмное дно
                        if (cd > 0.82f) shadeMul *= 1.35f; // светлый ободок
                    }
                }
                col = (byte)Math.Clamp(col * shadeMul, 0f, 255f);
                Raylib.ImageDrawPixel(ref img, x, y, new Color((int)col, (int)col, (int)Math.Min(255, col + 8), 255));
            }
        }
    }

    private static Color Lerp(Color a, Color b, float t) {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t),
            (byte)(a.A + (b.A - a.A) * t));
    }

    public static void Unload() {
        if (!_ready) return;
        Raylib.UnloadTexture(_star);
        Raylib.UnloadTexture(_sun);
        Raylib.UnloadTexture(_moon);
        Raylib.UnloadTexture(_moonPhase);
        _ready = false;
    }
}
