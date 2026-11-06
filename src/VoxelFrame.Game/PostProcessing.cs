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
    private static int _underwaterLoc = -1;
    private static int _underlavaLoc = -1;

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
            _underwaterLoc = Raylib.GetShaderLocation(_shader, "underwater");
            _underlavaLoc = Raylib.GetShaderLocation(_shader, "underlava");
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

        float underwater = 0f;
        float underlava = 0f;
        if (_session?.Player != null && _session.World != null) {
            var eyeCell = new VoxelFrame.Core.Vec3i(
                (int)MathF.Floor(_session.Player.Eye.X),
                (int)MathF.Floor(_session.Player.Eye.Y),
                (int)MathF.Floor(_session.Player.Eye.Z));
            var eyeVox = _session.World.GetVoxel(eyeCell);
            if (eyeVox.TypeId == GameData.BWater.Id) underwater = 1f;
            else if (eyeVox.TypeId == GameData.BLava.Id) underlava = 1f;
        }
        if (_underwaterLoc != -1) Raylib.SetShaderValue(_shader, _underwaterLoc, underwater, ShaderUniformDataType.Float);
        if (_underlavaLoc != -1) Raylib.SetShaderValue(_shader, _underlavaLoc, underlava, ShaderUniformDataType.Float);

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
        // Статическая ссылка на сессию держала в памяти весь мир (чанки + меши)
        // после выхода в меню; сбрасываем и состояние, чтобы PostFX мог
        // восстановиться после временного сбоя без перезапуска игры.
        _session = null;
        _inScene = false;
        _failed = false;
        _w = 0;
        _h = 0;
        _rt = default;
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
        uniform float underwater;        // 0..1
        uniform float underlava;         // 0..1

        void main() {
            vec4 c = texture(texture0, fragTexCoord);

            // Мягкий и четкий подводный/лавовый оттенок (без размывающего дисторшна)
            if (underwater > 0.001) {
                vec3 waterTint = vec3(0.65, 0.85, 1.15);
                c.rgb = mix(c.rgb, c.rgb * waterTint + vec3(0.02, 0.05, 0.10), underwater * 0.40);
            } else if (underlava > 0.001) {
                vec3 lavaTint = vec3(1.35, 0.45, 0.15);
                c.rgb = mix(c.rgb, c.rgb * lavaTint + vec3(0.25, 0.05, 0.0), underlava * 0.60);
            }

            // Золотой час: тёплый оттенок, лёгкое затемнение теней
            if (golden > 0.001) {
                vec3 warm = vec3(1.10, 0.94, 0.78);
                c.rgb = mix(c.rgb, c.rgb * warm, golden * 0.85);
                c.rgb += vec3(0.05, 0.02, -0.01) * golden;
                float lum = dot(c.rgb, vec3(0.299, 0.587, 0.114));
                c.rgb *= 1.0 - golden * 0.12 * (1.0 - smoothstep(0.0, 0.35, lum));
            }

            // Bloom: быстрый и мягкий 4-tap ореол вокруг ярких источников (факелы, солнце, лава)
            if (bloom > 0.5) {
                vec2 ts = vec2(2.0) / vec2(textureSize(texture0, 0));
                vec3 s0 = texture(texture0, clamp(fragTexCoord + vec2(-ts.x, 0.0), vec2(0.0), vec2(1.0))).rgb;
                vec3 s1 = texture(texture0, clamp(fragTexCoord + vec2(ts.x, 0.0), vec2(0.0), vec2(1.0))).rgb;
                vec3 s2 = texture(texture0, clamp(fragTexCoord + vec2(0.0, -ts.y), vec2(0.0), vec2(1.0))).rgb;
                vec3 s3 = texture(texture0, clamp(fragTexCoord + vec2(0.0, ts.y), vec2(0.0), vec2(1.0))).rgb;
                vec3 avg = (s0 + s1 + s2 + s3) * 0.25;
                float l = dot(avg, vec3(0.299, 0.587, 0.114));
                if (l > 0.75) {
                    c.rgb += avg * ((l - 0.75) * 0.35);
                }
            }

            // Базовая виньетка: аккуратное затемнение углов
            if (vignette > 0.5) {
                float d = distance(fragTexCoord, vec2(0.5)) / 0.7071;
                float v = smoothstep(0.55, 1.05, d);
                c.rgb *= 1.0 - v * vignetteStrength;
            }

            fragColor = c;
        }
        """;
}
