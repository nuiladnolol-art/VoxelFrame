using System;
using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Пост-обработка (кино-режим). Сцена рисуется в HDR-буфер (RGBA16F + текстура глубины), затем:
///   1. Bloom — цепочка уменьшений/увеличений (dual filter) с мягким порогом: светятся лава,
///      факелы, солнце, блики на воде.
///   2. Солнечные лучи — радиальное размытие от солнца по маске неба из буфера глубины
///      (облака, горы и листва дают «объёмные» тени-лучи).
///   3. Композит: экспозиция, тонмаппинг с мягким «плечом», цветокоррекция (контраст,
///      насыщенность, закат, ночь), подводное искажение, виньетка, дизеринг.
///   4. FXAA — сглаживание краёв (MSAA окна не действует на render target).
/// Любой сбой ресурсов GPU → тихий откат на прямой рендер без эффектов.
/// </summary>
public static class PostProcessing {
    private struct Target {
        public RenderTexture2D Rt;
        public int W, H;
        public bool Valid => Rt.Id != 0;
    }

    private static Target _scene;          // HDR цвет + глубина-текстура
    private static Target _ldr;            // результат композита (вход FXAA)
    private static Target _rays;           // солнечные лучи, 1/2 разрешения
    private const int BloomLevels = 5;
    private static readonly Target[] _down = new Target[BloomLevels];
    private static readonly Target[] _up = new Target[BloomLevels - 1];

    private static Shader _prefilter, _downSh, _upSh, _raysSh, _composite, _fxaa;
    private static readonly Dictionary<(uint, string), int> _locCache = new();

    // PIXELFORMAT_UNCOMPRESSED_R16G16B16A16 = 13 есть в нативном raylib 5.x,
    // но отсутствует в enum Raylib-cs 7.0. Если драйвер не примет half-float —
    // откатываемся на float32, затем на 8 бит.
    private static PixelFormat HdrFormat = (PixelFormat)13;

    private static int _w, _h;
    private static bool _ready;
    private static bool _failed;
    private static bool _hdr;
    private static GameSession? _session;
    private static bool _inScene;

    public static bool Enabled => !_failed && SaveSystem.PostFxMode > 0;
    /// <summary>Текущий кадр сцены рисуется в HDR-буфер (шейдер мира может отдавать значения &gt; 1).</summary>
    public static bool SceneIsHdr => _inScene && _hdr;

    // ── Шейдеры ────────────────────────────────────────────────────────────

    private const string PrefilterFs = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;
        uniform vec2 texel;       // размер текселя ИСТОЧНИКА
        uniform float threshold;
        out vec4 finalColor;
        float luma(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }
        vec3 pick(vec2 uv) {
            vec3 c = texture(texture0, uv).rgb;
            // мягкий порог (soft knee) + вес Кариса против «светлячков»
            float l = luma(c);
            float knee = threshold * 0.5;
            float soft = clamp(l - threshold + knee, 0.0, 2.0 * knee);
            soft = soft * soft / (4.0 * knee + 1e-4);
            float contrib = max(soft, l - threshold) / max(l, 1e-4);
            return c * contrib / (1.0 + l);
        }
        void main() {
            vec2 o = texel;
            vec3 c = pick(fragTexCoord + vec2(-o.x, -o.y)) + pick(fragTexCoord + vec2(o.x, -o.y))
                   + pick(fragTexCoord + vec2(-o.x, o.y)) + pick(fragTexCoord + vec2(o.x, o.y));
            finalColor = vec4(c * 0.25, 1.0);
        }
        """;

    private const string DownFs = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;
        uniform vec2 texel;
        out vec4 finalColor;
        void main() {
            vec2 o = texel * 0.5;
            vec3 s = texture(texture0, fragTexCoord).rgb * 4.0;
            s += texture(texture0, fragTexCoord - o).rgb;
            s += texture(texture0, fragTexCoord + o).rgb;
            s += texture(texture0, fragTexCoord + vec2(o.x, -o.y)).rgb;
            s += texture(texture0, fragTexCoord - vec2(o.x, -o.y)).rgb;
            finalColor = vec4(s / 8.0, 1.0);
        }
        """;

    private const string UpFs = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;   // меньший уровень (увеличиваем)
        uniform sampler2D texture1;   // текущий уровень цепочки уменьшения
        uniform vec2 texel;           // тексель texture0
        out vec4 finalColor;
        void main() {
            vec2 o = texel * 0.5;
            vec3 s = vec3(0.0);
            s += texture(texture0, fragTexCoord + vec2(-o.x * 2.0, 0.0)).rgb;
            s += texture(texture0, fragTexCoord + vec2(-o.x, o.y)).rgb * 2.0;
            s += texture(texture0, fragTexCoord + vec2(0.0, o.y * 2.0)).rgb;
            s += texture(texture0, fragTexCoord + vec2(o.x, o.y)).rgb * 2.0;
            s += texture(texture0, fragTexCoord + vec2(o.x * 2.0, 0.0)).rgb;
            s += texture(texture0, fragTexCoord + vec2(o.x, -o.y)).rgb * 2.0;
            s += texture(texture0, fragTexCoord + vec2(0.0, -o.y * 2.0)).rgb;
            s += texture(texture0, fragTexCoord + vec2(-o.x, -o.y)).rgb * 2.0;
            finalColor = vec4(s / 12.0 + texture(texture1, fragTexCoord).rgb, 1.0);
        }
        """;

    private const string RaysFs = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;   // HDR-сцена
        uniform sampler2D depthTex;   // глубина сцены (1.0 = небо)
        uniform vec2 sunUv;
        uniform float aspect;
        out vec4 finalColor;
        const int N = 40;
        void main() {
            vec2 uv = fragTexCoord;
            vec2 delta = (sunUv - uv) / float(N) * 0.92;
            float jitter = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453);
            uv += delta * jitter;
            vec3 sum = vec3(0.0);
            float decay = 1.0;
            for (int i = 0; i < N; i++) {
                float sky = step(0.99999, texture(depthTex, uv).r);
                vec2 dd = (uv - sunUv) * vec2(aspect, 1.0);
                float near = exp(-dot(dd, dd) * 9.0);
                sum += texture(texture0, uv).rgb * sky * near * decay;
                decay *= 0.965;
                uv += delta;
            }
            finalColor = vec4(sum / float(N), 1.0);
        }
        """;

    private const string CompositeFs = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;   // HDR-сцена
        uniform sampler2D bloomTex;
        uniform sampler2D raysTex;
        uniform float bloomStrength;
        uniform float raysStrength;
        uniform vec3 raysColor;
        uniform float exposure;
        uniform float golden;
        uniform float night;
        uniform float vignette;
        uniform float vignetteStrength;
        uniform float underwater;
        uniform float underlava;
        uniform float time;
        out vec4 finalColor;

        float luma(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }
        // Тонмаппинг с линейным участком до 0.62 и мягким плечом: обычные цвета игры
        // не меняются, а HDR-яркость (лава, блики, солнце) плавно сжимается без «выжигания».
        vec3 shoulder(vec3 x) {
            const float k = 0.62;
            vec3 over = max(x - k, 0.0);
            return min(x, vec3(k)) + (1.0 - k) * (1.0 - exp(-over / (1.0 - k)));
        }

        void main() {
            vec2 uv = fragTexCoord;
            if (underwater > 0.001) {
                uv += vec2(sin(uv.y * 28.0 + time * 2.1), cos(uv.x * 23.0 + time * 1.7)) * 0.0022 * underwater;
            } else if (underlava > 0.001) {
                uv += vec2(sin(uv.y * 18.0 + time * 3.0), cos(uv.x * 15.0 + time * 2.4)) * 0.004 * underlava;
            }
            vec3 c = texture(texture0, uv).rgb;
            c += texture(bloomTex, uv).rgb * bloomStrength;
            c += texture(raysTex, uv).rgb * raysColor * raysStrength;

            if (underwater > 0.001) {
                c = mix(c, c * vec3(0.55, 0.82, 1.10) + vec3(0.0, 0.03, 0.07), underwater * 0.5);
            } else if (underlava > 0.001) {
                c = mix(c, c * vec3(1.35, 0.45, 0.15) + vec3(0.25, 0.05, 0.0), underlava * 0.6);
            }

            c *= exposure;
            c = shoulder(c);

            // Цветокоррекция: лёгкий S-контраст и насыщенность, ночью — холоднее и бледнее
            float l = luma(c);
            float sat = 1.10 - 0.30 * night;
            c = max(mix(vec3(l), c, sat), 0.0);
            c = mix(c, c * c * (3.0 - 2.0 * c), 0.22);
            c = mix(c, c * vec3(0.90, 0.97, 1.12), night * 0.45);

            // Золотой час: тёплый сдвиг, светлые участки — медовые
            if (golden > 0.001) {
                c = mix(c, c * vec3(1.08, 0.96, 0.82), golden * 0.7);
                c += vec3(0.035, 0.015, -0.01) * golden * smoothstep(0.3, 0.9, l);
            }

            if (vignette > 0.5) {
                float d = distance(fragTexCoord, vec2(0.5)) / 0.7071;
                c *= 1.0 - smoothstep(0.45, 1.05, d) * vignetteStrength;
            }

            // Дизеринг: убирает полосы на градиентах неба и тумана
            float n = fract(sin(dot(gl_FragCoord.xy + fract(time) * 61.0, vec2(12.9898, 78.233))) * 43758.5453);
            c += (n - 0.5) / 255.0;
            finalColor = vec4(clamp(c, 0.0, 1.0), 1.0);
        }
        """;

    // FXAA (облегчённая версия алгоритма Тимоти Лоттса)
    private const string FxaaFs = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;
        uniform vec2 texel;
        out vec4 finalColor;
        float luma(vec3 c) { return dot(c, vec3(0.299, 0.587, 0.114)); }
        void main() {
            vec2 uv = fragTexCoord;
            vec3 rgbM = texture(texture0, uv).rgb;
            float lM = luma(rgbM);
            float lNW = luma(texture(texture0, uv + vec2(-1.0, -1.0) * texel).rgb);
            float lNE = luma(texture(texture0, uv + vec2( 1.0, -1.0) * texel).rgb);
            float lSW = luma(texture(texture0, uv + vec2(-1.0,  1.0) * texel).rgb);
            float lSE = luma(texture(texture0, uv + vec2( 1.0,  1.0) * texel).rgb);
            float lMin = min(lM, min(min(lNW, lNE), min(lSW, lSE)));
            float lMax = max(lM, max(max(lNW, lNE), max(lSW, lSE)));
            if (lMax - lMin < max(0.0312, lMax * 0.125)) { finalColor = vec4(rgbM, 1.0); return; }
            vec2 dir = vec2(-((lNW + lNE) - (lSW + lSE)), ((lNW + lSW) - (lNE + lSE)));
            float reduce = max((lNW + lNE + lSW + lSE) * 0.25 * 0.125, 1.0 / 128.0);
            float rcp = 1.0 / (min(abs(dir.x), abs(dir.y)) + reduce);
            dir = clamp(dir * rcp, vec2(-8.0), vec2(8.0)) * texel;
            vec3 a = 0.5 * (texture(texture0, uv + dir * (1.0 / 3.0 - 0.5)).rgb + texture(texture0, uv + dir * (2.0 / 3.0 - 0.5)).rgb);
            vec3 b = a * 0.5 + 0.25 * (texture(texture0, uv + dir * -0.5).rgb + texture(texture0, uv + dir * 0.5).rgb);
            float lB = luma(b);
            finalColor = vec4((lB < lMin || lB > lMax) ? a : b, 1.0);
        }
        """;

    // ── Ресурсы ────────────────────────────────────────────────────────────

    private static Shader LoadFs(string src) {
        var sh = Raylib.LoadShaderFromMemory(null, src);
        if (!Raylib.IsShaderValid(sh)) throw new InvalidOperationException("post shader");
        return sh;
    }

    public static void Ensure() {
        if (_ready || _failed) return;
        try {
            _prefilter = LoadFs(PrefilterFs);
            _downSh = LoadFs(DownFs);
            _upSh = LoadFs(UpFs);
            _raysSh = LoadFs(RaysFs);
            _composite = LoadFs(CompositeFs);
            _fxaa = LoadFs(FxaaFs);
            _ready = true;
        } catch {
            UnloadShaders();
            _failed = true;
        }
    }

    private static int L(Shader sh, string name) {
        var key = (sh.Id, name);
        if (!_locCache.TryGetValue(key, out int loc)) {
            loc = Raylib.GetShaderLocation(sh, name);
            _locCache[key] = loc;
        }
        return loc;
    }
    private static void F(Shader sh, string n, float v) { int l = L(sh, n); if (l != -1) Raylib.SetShaderValue(sh, l, v, ShaderUniformDataType.Float); }
    private static void V2(Shader sh, string n, Vector2 v) { int l = L(sh, n); if (l != -1) Raylib.SetShaderValue(sh, l, v, ShaderUniformDataType.Vec2); }
    private static void V3(Shader sh, string n, Vector3 v) { int l = L(sh, n); if (l != -1) Raylib.SetShaderValue(sh, l, v, ShaderUniformDataType.Vec3); }
    private static void Tex(Shader sh, string n, Texture2D t) { int l = L(sh, n); if (l != -1) Raylib.SetShaderValueTexture(sh, l, t); }

    /// <summary>Render target с цветом (HDR или 8 бит) и, опционально, текстурой глубины.</summary>
    private static Target CreateTarget(int w, int h, bool hdr, bool depthTexture) {
        w = Math.Max(1, w); h = Math.Max(1, h);
        uint fbo = Rlgl.LoadFramebuffer();
        if (fbo == 0) return default;
        Rlgl.EnableFramebuffer(fbo);
        var fmt = hdr ? HdrFormat : PixelFormat.UncompressedR8G8B8A8;
        uint color;
        unsafe { color = Rlgl.LoadTexture(null, w, h, fmt, 1); }
        uint depth = 0;
        if (depthTexture) depth = Rlgl.LoadTextureDepth(w, h, false);
        if (color != 0) Rlgl.FramebufferAttach(fbo, color, FramebufferAttachType.ColorChannel0, FramebufferAttachTextureType.Texture2D, 0);
        if (depth != 0) Rlgl.FramebufferAttach(fbo, depth, FramebufferAttachType.Depth, FramebufferAttachTextureType.Texture2D, 0);
        bool ok = color != 0 && (!depthTexture || depth != 0) && Rlgl.FramebufferComplete(fbo);
        Rlgl.DisableFramebuffer();
        if (!ok) {
            Rlgl.UnloadFramebuffer(fbo);
            if (color != 0) Rlgl.UnloadTexture(color);
            if (depth != 0) Rlgl.UnloadTexture(depth);
            return default;
        }
        var t = new Target {
            W = w, H = h,
            Rt = new RenderTexture2D {
                Id = fbo,
                Texture = new Texture2D { Id = color, Width = w, Height = h, Mipmaps = 1, Format = fmt },
                Depth = new Texture2D { Id = depth, Width = w, Height = h, Mipmaps = 1, Format = PixelFormat.UncompressedGrayscale },
            }
        };
        Raylib.SetTextureFilter(t.Rt.Texture, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(t.Rt.Texture, TextureWrap.Clamp);
        if (depth != 0) {
            Raylib.SetTextureFilter(t.Rt.Depth, TextureFilter.Point);
            Raylib.SetTextureWrap(t.Rt.Depth, TextureWrap.Clamp);
        }
        return t;
    }

    private static void Release(ref Target t) {
        if (t.Rt.Id != 0) {
            Rlgl.UnloadFramebuffer(t.Rt.Id);
            if (t.Rt.Texture.Id != 0) Rlgl.UnloadTexture(t.Rt.Texture.Id);
            if (t.Rt.Depth.Id != 0) Rlgl.UnloadTexture(t.Rt.Depth.Id);
        }
        t = default;
    }

    private static void ReleaseTargets() {
        Release(ref _scene);
        Release(ref _ldr);
        Release(ref _rays);
        for (int i = 0; i < _down.Length; i++) Release(ref _down[i]);
        for (int i = 0; i < _up.Length; i++) Release(ref _up[i]);
        _w = _h = 0;
    }

    private static bool EnsureTargets(int w, int h) {
        if (_w == w && _h == h && _scene.Valid) return true;
        ReleaseTargets();
        _hdr = true;
        _scene = CreateTarget(w, h, true, true);
        if (!_scene.Valid && HdrFormat != PixelFormat.UncompressedR32G32B32A32) {
            HdrFormat = PixelFormat.UncompressedR32G32B32A32;
            _scene = CreateTarget(w, h, true, true);
        }
        if (!_scene.Valid) { _hdr = false; _scene = CreateTarget(w, h, false, true); }
        if (!_scene.Valid) return false;
        _ldr = CreateTarget(w, h, false, false);
        _rays = CreateTarget(w / 2, h / 2, _hdr, false);
        int bw = w, bh = h;
        for (int i = 0; i < BloomLevels; i++) {
            bw = Math.Max(1, bw / 2); bh = Math.Max(1, bh / 2);
            _down[i] = CreateTarget(bw, bh, _hdr, false);
            if (i < BloomLevels - 1) _up[i] = CreateTarget(bw, bh, _hdr, false);
        }
        bool ok = _ldr.Valid && _rays.Valid;
        for (int i = 0; i < BloomLevels; i++) ok &= _down[i].Valid;
        for (int i = 0; i < _up.Length; i++) ok &= _up[i].Valid;
        if (!ok) { ReleaseTargets(); return false; }
        _w = w; _h = h;
        return true;
    }

    // ── Кадр ───────────────────────────────────────────────────────────────

    /// <summary>Начало кадра. Возвращает true, если сцену нужно рисовать в буфер пост-обработки
    /// (в этом случае вызывающий код НЕ вызывает Raylib.BeginDrawing() до EndScene).</summary>
    public static bool BeginScene(GameSession session) {
        _session = session;
        if (!Enabled) return false;
        Ensure();
        if (!_ready) return false;

        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        if (w <= 0 || h <= 0) return false; // свёрнутое окно — рисуем напрямую
        if (!EnsureTargets(w, h)) { _failed = true; return false; }
        Raylib.BeginTextureMode(_scene.Rt);
        _inScene = true;
        return true;
    }

    private static Rectangle SrcRect(in Target t) => new(0, 0, t.W, -t.H);

    /// <summary>Полноэкранный проход: шейдер читает src (texture0) и пишет в dst.</summary>
    private static void Pass(Shader sh, in Target src, in Target dst, Action? setup = null) {
        Raylib.BeginTextureMode(dst.Rt);
        Raylib.BeginShaderMode(sh);
        setup?.Invoke();
        Raylib.DrawTexturePro(src.Rt.Texture, SrcRect(src), new Rectangle(0, 0, dst.W, dst.H), Vector2.Zero, 0f, Color.White);
        Raylib.EndShaderMode();
        Raylib.EndTextureMode();
    }

    /// <summary>Конец кадра: эффекты и вывод на экран. После вызова UI рисуется уже поверх.</summary>
    public static void EndScene() {
        if (!_inScene) return;
        _inScene = false;
        Raylib.EndTextureMode();

        bool bloomOn = SaveSystem.PostFxBloom;
        float time = (float)(Raylib.GetTime() % 1000.0);

        // 1. Bloom
        if (bloomOn) {
            Pass(_prefilter, _scene, _down[0], () => {
                V2(_prefilter, "texel", new Vector2(1f / _scene.W, 1f / _scene.H));
                F(_prefilter, "threshold", _hdr ? 1.05f : 0.85f);
            });
            for (int i = 1; i < BloomLevels; i++) {
                var src = _down[i - 1];
                Pass(_downSh, src, _down[i], () => V2(_downSh, "texel", new Vector2(1f / src.W, 1f / src.H)));
            }
            for (int i = BloomLevels - 2; i >= 0; i--) {
                var lower = i == BloomLevels - 2 ? _down[BloomLevels - 1] : _up[i + 1];
                var cur = _down[i];
                Pass(_upSh, lower, _up[i], () => {
                    V2(_upSh, "texel", new Vector2(1f / lower.W, 1f / lower.H));
                    Tex(_upSh, "texture1", cur.Rt.Texture);
                });
            }
        }

        // 2. Солнечные лучи
        float raysStrength = 0f;
        var raysColor = Vector3.One;
        if (SaveSystem.PostFxGodRays && _session != null && _session.World != null
            && _session.World.Dimension == Dimension.Overworld && Atmosphere.SunVisibility > 0.01f) {
            var cam = _session.Camera;
            var fwd = cam.Target - cam.Position;
            if (fwd.LengthSquared() > 1e-6f) {
                fwd = Vector3.Normalize(fwd);
                float facing = Vector3.Dot(fwd, Atmosphere.SunDir);
                if (facing > 0.05f) {
                    var sp = Raylib.GetWorldToScreenEx(cam.Position + Atmosphere.SunDir * 100f, cam, _w, _h);
                    var sunUv = new Vector2(sp.X / _w, 1f - sp.Y / _h);
                    float off = MathF.Max(MathF.Max(-sunUv.X, sunUv.X - 1f), MathF.Max(-sunUv.Y, sunUv.Y - 1f));
                    float onScreen = Math.Clamp(1f - off / 0.6f, 0f, 1f);
                    raysStrength = Atmosphere.SunVisibility * onScreen * Math.Clamp((facing - 0.05f) * 3f, 0f, 1f) * 0.9f;
                    if (raysStrength > 0.005f) {
                        raysColor = Vector3.Lerp(new Vector3(1.0f, 0.93f, 0.80f), new Vector3(1.0f, 0.62f, 0.32f), Atmosphere.SunsetAmount);
                        float aspect = (float)_w / Math.Max(1, _h);
                        Pass(_raysSh, _scene, _rays, () => {
                            Tex(_raysSh, "depthTex", _scene.Rt.Depth);
                            V2(_raysSh, "sunUv", sunUv);
                            F(_raysSh, "aspect", aspect);
                        });
                    }
                }
            }
        }

        // 3. Композит в LDR
        float underwater = 0f, underlava = 0f;
        if (_session?.Player != null && _session.World != null) {
            var eye = _session.Camera.Position;
            var eyeVox = _session.World.GetVoxel(new VoxelFrame.Core.Vec3i(
                (int)MathF.Floor(eye.X), (int)MathF.Floor(eye.Y), (int)MathF.Floor(eye.Z)));
            if (eyeVox.TypeId == GameData.BWater.Id) underwater = 1f;
            else if (eyeVox.TypeId == GameData.BLava.Id) underlava = 1f;
        }
        float golden = SaveSystem.PostFxGoldenHour ? Atmosphere.SunsetAmount : 0f;
        float rs = raysStrength;
        Pass(_composite, _scene, _ldr, () => {
            Tex(_composite, "bloomTex", bloomOn ? _up[0].Rt.Texture : _down[0].Rt.Texture);
            Tex(_composite, "raysTex", _rays.Rt.Texture);
            F(_composite, "bloomStrength", bloomOn ? 0.55f : 0f);
            F(_composite, "raysStrength", rs);
            V3(_composite, "raysColor", raysColor);
            F(_composite, "exposure", 1.0f + Atmosphere.Night * 0.25f);
            F(_composite, "golden", golden);
            F(_composite, "night", Atmosphere.Night);
            F(_composite, "vignette", SaveSystem.PostFxVignette ? 1f : 0f);
            F(_composite, "vignetteStrength", SaveSystem.PostFxVignetteStrength / 100f);
            F(_composite, "underwater", underwater);
            F(_composite, "underlava", underlava);
            F(_composite, "time", time);
        });

        // 4. FXAA на экран
        Raylib.BeginDrawing();
        Raylib.BeginShaderMode(_fxaa);
        V2(_fxaa, "texel", new Vector2(1f / _ldr.W, 1f / _ldr.H));
        Raylib.DrawTexturePro(_ldr.Rt.Texture, SrcRect(_ldr), new Rectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight()), Vector2.Zero, 0f, Color.White);
        Raylib.EndShaderMode();
        // НЕ EndDrawing — вызывающий код продолжает рисовать UI уже на экране.
    }

    private static void UnloadShaders() {
        foreach (var sh in new[] { _prefilter, _downSh, _upSh, _raysSh, _composite, _fxaa })
            if (sh.Id != 0 && Raylib.IsShaderValid(sh)) Raylib.UnloadShader(sh);
        _prefilter = _downSh = _upSh = _raysSh = _composite = _fxaa = default;
        _locCache.Clear();
    }

    public static void Unload() {
        if (_ready) {
            UnloadShaders();
            _ready = false;
        }
        ReleaseTargets();
        // Статическая ссылка на сессию держала в памяти весь мир (чанки + меши)
        // после выхода в меню; сбрасываем и состояние, чтобы PostFX мог
        // восстановиться после временного сбоя без перезапуска игры.
        _session = null;
        _inScene = false;
        _failed = false;
    }
}
