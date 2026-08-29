using System;
using System.Numerics;
using Raylib_cs;

namespace VoxelFrame.Game;

/// <summary>
/// Пост-обработка: весь кадр рисуется в RenderTexture, затем проходит финальный шейдер:
/// виньетка, тёплый «золотой час» (рассвет/закат) и лёгкий bloom.
/// Отключается целиком в настройках графики; файлы assets/ не затрагиваются.
/// </summary>
public static class PostProcessing {
    private static Shader _shader;
    private static RenderTexture2D _rt;
    private static int _w, _h;
    private static bool _ready;
    private static bool _failed;

    // uniform-локации
    private static int _vignetteLoc = -1;
    private static int _vignetteStrengthLoc = -1;
    private static int _goldenLoc = -1;
    private static int _bloomLoc = -1;

    private static GameSession? _session;
    private static bool _inScene;

    public static bool Enabled => !_failed && SaveSystem.PostFxMode > 0;

    public static void Ensure() {
        if (_ready || _failed) return;
        try {
            _shader = Raylib.LoadShaderFromMemory(null, FragmentShaderSrc);
            if (!Raylib.IsShaderValid(_shader)) { _failed = true; return; }
            _vignetteLoc = Raylib.GetShaderLocation(_shader, "vignette");
            _vignetteStrengthLoc = Raylib.GetShaderLocation(_shader, "vignetteStrength");
            _goldenLoc = Raylib.GetShaderLocation(_shader, "golden");
            _bloomLoc = Raylib.GetShaderLocation(_shader, "bloom");
            _ready = true;
        } catch {
            _failed = true;
        }
    }

    /// <summary>Начало кадра. Возвращает true, если сцену нужно рисовать в RenderTexture
    /// (в этом случае вызывающий код НЕ вызывает Raylib.BeginDrawing() до EndScene).</summary>
    public static bool BeginScene(GameSession session) {
        _session = session;
        if (!Enabled) return false;
        Ensure();
        if (!_ready) return false;

        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        if (w <= 0 || h <= 0) return false; // минимизированное окно — рисуем напрямую
        if (_w != w || _h != h || _rt.Texture.Id == 0) {
            if (_rt.Texture.Id != 0) Raylib.UnloadRenderTexture(_rt);
            _rt = Raylib.LoadRenderTexture(w, h);
            if (_rt.Texture.Id == 0) { _failed = true; return false; }
            _w = w; _h = h;
        }
        Raylib.BeginTextureMode(_rt);
        _inScene = true;
        return true;
    }

    /// <summary>Конец кадра: композитим RenderTexture на экран финальным шейдером.</summary>
    public static void EndScene() {
        if (!_inScene) return;
        _inScene = false;

        Raylib.EndTextureMode();
        Raylib.BeginDrawing();

        var src = new Rectangle(0, 0, _w, -_h); // переворот Y


        if (_vignetteLoc != -1) Raylib.SetShaderValue(_shader, _vignetteLoc, SaveSystem.PostFxVignette ? 1f : 0f, ShaderUniformDataType.Float);
        if (_vignetteStrengthLoc != -1) Raylib.SetShaderValue(_shader, _vignetteStrengthLoc, SaveSystem.PostFxVignetteStrength / 100f, ShaderUniformDataType.Float);
        float golden = ComputeGoldenFactor();
        if (_goldenLoc != -1) Raylib.SetShaderValue(_shader, _goldenLoc, golden, ShaderUniformDataType.Float);
        if (_bloomLoc != -1) Raylib.SetShaderValue(_shader, _bloomLoc, SaveSystem.PostFxBloom ? 1f : 0f, ShaderUniformDataType.Float);

        Raylib.BeginShaderMode(_shader);
        Raylib.DrawTextureRec(_rt.Texture, src, Vector2.Zero, Color.White);
        Raylib.EndShaderMode();
        // НЕ EndDrawing — вызывающий код продолжает рисовать UI уже на экране.
    }

    /// <summary>Сила «золотого часа» 0..1: рассвет (0.25) и закат (0.75), днём и ночью 0.</summary>
    private static float ComputeGoldenFactor() {
        if (!SaveSystem.PostFxGoldenHour) return 0f;
        float tod = _session?.DayNight?.TimeOfDay ?? 0.5f;
        float d1 = MathF.Abs(tod - 0.25f);   // рассвет
        float d2 = MathF.Abs(tod - 0.75f);   // закат
        float d = MathF.Min(d1, d2);
        return Math.Clamp(1f - d / 0.09f, 0f, 1f);
    }

    public static void Unload() {
        if (_ready) {
            Raylib.UnloadShader(_shader);
            if (_rt.Texture.Id != 0) Raylib.UnloadRenderTexture(_rt);
            _ready = false;
        }
    }

    private const string FragmentShaderSrc = """
        #version 330
        in vec2 fragTexCoord;
        out vec4 fragColor;
        uniform sampler2D texture0;
        uniform float vignette;          // 0/1
        uniform float vignetteStrength;  // 0..0.6
        uniform float golden;            // 0..1 сила золотого часа
        uniform float bloom;             // 0/1

        void main() {
            vec4 c = texture(texture0, fragTexCoord);

            // Золотой час: тёплый оттенок, лёгкое затемнение теней
            if (golden > 0.001) {
                vec3 warm = vec3(1.10, 0.94, 0.78);
                c.rgb = mix(c.rgb, c.rgb * warm, golden * 0.85);
                c.rgb += vec3(0.05, 0.02, -0.01) * golden;
                float lum = dot(c.rgb, vec3(0.299, 0.587, 0.114));
                c.rgb *= 1.0 - golden * 0.12 * (1.0 - smoothstep(0.0, 0.35, lum));
            }

            // Bloom: «свечение» ярких участков через широкую выборку соседей
            if (bloom > 0.5) {
                vec2 ts = vec2(1.0) / vec2(textureSize(texture0, 0));
                vec3 bright = vec3(0.0);
                const int R = 3;
                for (int y = -R; y <= R; y++) {
                    for (int x = -R; x <= R; x++) {
                        vec2 o = vec2(float(x), float(y)) * ts * 2.0;
                        vec3 s = texture(texture0, clamp(fragTexCoord + o, vec2(0.0), vec2(1.0))).rgb;
                        float l = dot(s, vec3(0.299, 0.587, 0.114));
                        bright += s * smoothstep(0.62, 0.95, l);
                    }
                }
                bright /= float((2 * R + 1) * (2 * R + 1));
                c.rgb += bright * 0.35;
                // Мягкое тонирование бликов
                c.rgb = mix(c.rgb, min(c.rgb, vec3(1.6)), 0.25);
            }

            // Виньетка: затемнение углов
            if (vignette > 0.5) {
                float d = distance(fragTexCoord, vec2(0.5)) / 0.7071;
                float v = smoothstep(0.55, 1.05, d);
                c.rgb *= 1.0 - v * vignetteStrength;
            }

            fragColor = c;
        }
        """;
}
