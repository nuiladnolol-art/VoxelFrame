using System.Numerics;
using Raylib_cs;
using Voxalith.Core;
using Voxalith.Core.World;

namespace Voxalith.Game;
public sealed partial class WorldRenderer {
    // ── Небо и Погода ────────────────────────────────────────────────────────

    private const string SkyFragmentSrc = """
        #version 330
        in vec2 fragTexCoord;
        uniform vec3 camFwd;
        uniform vec3 camRight;
        uniform vec3 camUp;
        uniform float tanHalfFov;
        uniform float aspect;
        uniform vec3 skyZenith;
        uniform vec3 skyHorizon;
        uniform vec3 sunsetColor;
        uniform float sunsetAmount;
        uniform vec3 sunDir;
        uniform float nightFactor;
        uniform float overcast;
        uniform int dimension;
        uniform float time;
        out vec4 finalColor;

        float hash13(vec3 p) {
            p = fract(p * 0.1031);
            p += dot(p, p.zyx + 31.32);
            return fract((p.x + p.y) * p.z);
        }
        float cloudNoise3(vec3 p) {
            vec3 i = floor(p), f = fract(p);
            f = f * f * (3.0 - 2.0 * f);
            float n000 = hash13(i), n100 = hash13(i + vec3(1,0,0)), n010 = hash13(i + vec3(0,1,0)), n110 = hash13(i + vec3(1,1,0));
            float n001 = hash13(i + vec3(0,0,1)), n101 = hash13(i + vec3(1,0,1)), n011 = hash13(i + vec3(0,1,1)), n111 = hash13(i + vec3(1,1,1));
            return mix(mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y),
                       mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y), f.z);
        }
        float fbm(vec3 p) {
            float s = 0.0, a = 0.5;
            for (int i = 0; i < 5; i++) { s += cloudNoise3(p) * a; p = p * 2.03 + vec3(1.7, 9.2, 3.1); a *= 0.5; }
            return s;
        }

        vec3 skyColor(vec3 d) {
            float h = clamp(d.y, -1.0, 1.0);
            vec3 c = mix(skyHorizon, skyZenith, (1.0 - exp(-max(h, 0.0) * 2.6)) / 0.9257);
            if (sunsetAmount > 0.0) {
                vec2 fd = normalize(d.xz + vec2(1e-4, 0.0));
                vec2 fs = normalize(sunDir.xz + vec2(1e-4, 0.0));
                float toward = dot(fd, fs) * 0.5 + 0.5;
                float band = pow(1.0 - abs(h), 6.0);
                c = mix(c, sunsetColor, sunsetAmount * band * (0.25 + 0.75 * toward * toward));
            }
            float mu = max(dot(d, sunDir), 0.0);
            vec3 glowCol = mix(vec3(1.0, 0.92, 0.75), sunsetColor, sunsetAmount);
            c += glowCol * (pow(mu, 8.0) * 0.22 + pow(mu, 64.0) * 0.35) * (1.0 - nightFactor) * (1.0 - overcast * 0.8);
            return c;
        }

        void main() {
            vec2 ndc = fragTexCoord * 2.0 - 1.0;
            vec3 d = normalize(camFwd + camRight * ndc.x * tanHalfFov * aspect + camUp * ndc.y * tanHalfFov);
            vec3 c;
            if (dimension == 0) {
                c = skyColor(d);
                // Ниже горизонта — мягкая дымка (начинается ниже, чем кончается туман мира)
                c = mix(c, skyHorizon * 0.55, smoothstep(-0.15, -0.6, d.y));
                // Едва заметный Млечный путь ночью
                if (nightFactor > 0.01) {
                    float band = exp(-pow(dot(d, normalize(vec3(0.3, 0.2, 1.0))) * 3.2, 2.0));
                    float neb = fbm(d * 4.0) * band;
                    c += vec3(0.10, 0.11, 0.17) * neb * nightFactor * (1.0 - overcast) * smoothstep(-0.05, 0.25, d.y);
                }
            } else if (dimension == 1) {
                // Нижний мир: раскалённая дымка, клубящаяся у «пола»
                float n = fbm(d * 2.5 + vec3(0.0, time * 0.03, 0.0));
                c = mix(vec3(0.16, 0.035, 0.025), vec3(0.34, 0.09, 0.04), n);
                c = mix(c, vec3(0.45, 0.14, 0.05), smoothstep(0.1, -0.6, d.y) * 0.5);
            } else if (dimension == 2) {
                // Энд: тёмная пустота с пурпурной туманностью и холодными звёздами
                float n = fbm(d * 3.0 + vec3(time * 0.01));
                c = mix(vec3(0.02, 0.012, 0.04), vec3(0.16, 0.08, 0.26), pow(n, 2.2) * 1.6);
                vec3 sp = floor(d * 220.0);
                float st = step(0.9975, hash13(sp));
                c += vec3(0.75, 0.70, 0.95) * st * (0.6 + 0.4 * sin(time * 2.0 + hash13(sp + 7.0) * 30.0));
            } else {
                // Бездна: почти чёрное фиолетовое ничто с медленными всполохами
                float n = fbm(d * 2.0 + vec3(0.0, -time * 0.02, 0.0));
                c = mix(vec3(0.015, 0.006, 0.03), vec3(0.09, 0.03, 0.15), n * n);
            }
            // Дизеринг против полос на плавных градиентах
            c += (hash13(vec3(gl_FragCoord.xy, time)) - 0.5) / 255.0;
            finalColor = vec4(c, 1.0);
        }
        """;

    private static readonly string[] SkyUniformNames = {
        "camFwd", "camRight", "camUp", "tanHalfFov", "aspect", "skyZenith", "skyHorizon",
        "sunsetColor", "sunsetAmount", "sunDir", "nightFactor", "overcast", "dimension", "time",
    };

    private void LoadSkyShader() {
        try {
            var sh = Raylib.LoadShaderFromMemory(null, SkyFragmentSrc);
            if (!Raylib.IsShaderValid(sh)) return;
            _skyShader = sh;
            foreach (var n in SkyUniformNames) _skyLocs[n] = Raylib.GetShaderLocation(sh, n);
            _skyShaderReady = true;
        } catch { _skyShaderReady = false; }
    }

    private void SkyF(string n, float v) { if (_skyLocs.TryGetValue(n, out int l) && l != -1) Raylib.SetShaderValue(_skyShader, l, v, ShaderUniformDataType.Float); }
    private void SkyI(string n, int v) { if (_skyLocs.TryGetValue(n, out int l) && l != -1) Raylib.SetShaderValue(_skyShader, l, v, ShaderUniformDataType.Int); }
    private void SkyV3(string n, Vector3 v) { if (_skyLocs.TryGetValue(n, out int l) && l != -1) Raylib.SetShaderValue(_skyShader, l, v, ShaderUniformDataType.Vec3); }

    /// <summary>
    /// Небо: полноэкранный шейдер считает цвет по НАПРАВЛЕНИЮ взгляда (раньше был
    /// плоский 2D-градиент, не реагировавший на наклон камеры). Горизонт совпадает с туманом.
    /// </summary>
    public void DrawSky() {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        if (!_skyShaderReady) {
            var hz = Atmosphere.Horizon; var zn = Atmosphere.Zenith;
            Raylib.DrawRectangleGradientV(0, 0, w, h,
                new Color((byte)(zn.X * 255), (byte)(zn.Y * 255), (byte)(zn.Z * 255), (byte)255),
                new Color((byte)(hz.X * 255), (byte)(hz.Y * 255), (byte)(hz.Z * 255), (byte)255));
            return;
        }
        var cam = _session.Camera;
        var fwd = cam.Target - cam.Position;
        fwd = fwd.LengthSquared() > 1e-6f ? Vector3.Normalize(fwd) : _session.Player.Forward;
        var right = Vector3.Cross(fwd, cam.Up);
        right = right.LengthSquared() > 1e-6f ? Vector3.Normalize(right) : Vector3.UnitX;
        var upv = Vector3.Cross(right, fwd);

        SkyV3("camFwd", fwd);
        SkyV3("camRight", right);
        SkyV3("camUp", upv);
        SkyF("tanHalfFov", MathF.Tan(cam.FovY * 0.5f * MathF.PI / 180f));
        SkyF("aspect", (float)w / Math.Max(1, h));
        SkyV3("skyZenith", Atmosphere.Zenith);
        SkyV3("skyHorizon", Atmosphere.Horizon);
        SkyV3("sunsetColor", Atmosphere.SunsetColor);
        SkyF("sunsetAmount", Atmosphere.SunsetAmount);
        SkyV3("sunDir", Atmosphere.SunDir);
        SkyF("nightFactor", Atmosphere.Night);
        SkyF("overcast", Atmosphere.Overcast);
        SkyI("dimension", (int)_world.Dimension);
        SkyF("time", (float)(Raylib.GetTime() % 10000.0));

        // Полноэкранный квад с UV (v вверх), чтобы шейдер знал направление каждого пикселя
        Raylib.BeginShaderMode(_skyShader);
        var tex = Raylib.GetShapesTexture();
        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin((int)DrawMode.Quads);
        Rlgl.Color4ub(255, 255, 255, 255);
        Rlgl.TexCoord2f(0f, 1f); Rlgl.Vertex2f(0f, 0f);
        Rlgl.TexCoord2f(0f, 0f); Rlgl.Vertex2f(0f, h);
        Rlgl.TexCoord2f(1f, 0f); Rlgl.Vertex2f(w, h);
        Rlgl.TexCoord2f(1f, 1f); Rlgl.Vertex2f(w, 0f);
        Rlgl.End();
        Rlgl.SetTexture(0);
        Raylib.EndShaderMode();
    }

    /// <summary>3D Небесные светила (Солнце, Луна, звёзды — билборды без записи в Z-буфер, не режущие облака).</summary>
    public void Draw3DSky(Camera3D camera) {
        if (_world.Dimension == Dimension.Nether) return;
        if (_world.Dimension == Dimension.End) return; // в Энде нет солнца/луны — только пустота
        if (_world.Dimension == Dimension.Void) return; // в Бездне нет солнца/луны
        if (!SkyTextures.Ready) SkyTextures.Load();

        float f = _session.DayNight.SkyFactor;
        if (_session.Weather != WeatherType.Clear) f *= 0.45f;
        float u = _session.DayNight.TimeOfDay;
        float sunAngle = 2f * MathF.PI * (u - 0.25f);

        // Направление орбиты светил в мировом 3D-пространстве (Восток -> Зенит -> Запад)
        Vector3 celestialDir = Vector3.Normalize(new Vector3(MathF.Cos(sunAngle), MathF.Sin(sunAngle), 0.22f));
        float dist = 170f;
        float time = (float)Raylib.GetTime();

        // Базис камеры для идеальных билбордов
        Vector3 camFwd = Vector3.Normalize(camera.Target - camera.Position);
        Vector3 camRight = Vector3.Normalize(Vector3.Cross(camFwd, camera.Up));
        Vector3 camUp = Vector3.Cross(camRight, camFwd);

        // Светила находятся на бесконечности: отключаем depth test и depth write, чтобы не вырезать дыр в облаках
        Rlgl.DisableDepthTest();
        Rlgl.DisableDepthMask();

        // 1. Звёзды — мягко мерцают (единый батч)
        if (f < 0.65f) {
            float starAlpha = Math.Clamp((0.65f - f) / 0.65f, 0f, 1f);
            var starTex = SkyTextures.Star;
            float u0 = 0f, v0 = 0f, u1 = 1f, v1 = 1f;

            Rlgl.SetTexture(starTex.Id);
            Rlgl.Begin((int)DrawMode.Quads);
            for (int i = 0; i < StarPositions.Length; i++) {
                var s = StarPositions[i];
                float twinkle = MathF.Sin(time * 2.5f + i * 1.7f) * 0.25f + 0.75f;
                float size = 0.9f + (i % 4) * 0.35f;
                byte a = (byte)(starAlpha * (190 + (i % 3) * 22) * twinkle);
                Vector3 starWorld = camera.Position + s * dist;
                Vector3 rx = camRight * (size * 0.5f);
                Vector3 ry = camUp * (size * 0.5f);

                Vector3 p0 = starWorld - rx - ry;
                Vector3 p1 = starWorld + rx - ry;
                Vector3 p2 = starWorld + rx + ry;
                Vector3 p3 = starWorld - rx + ry;

                Rlgl.Color4ub(245, 245, 255, a);
                Rlgl.TexCoord2f(u0, v1); Rlgl.Vertex3f(p0.X, p0.Y, p0.Z);
                Rlgl.TexCoord2f(u1, v1); Rlgl.Vertex3f(p1.X, p1.Y, p1.Z);
                Rlgl.TexCoord2f(u1, v0); Rlgl.Vertex3f(p2.X, p2.Y, p2.Z);
                Rlgl.TexCoord2f(u0, v0); Rlgl.Vertex3f(p3.X, p3.Y, p3.Z);
            }
            Rlgl.End();
            Rlgl.SetTexture(0);
        }

        // 2. Солнце с короной
        Vector3 sunPos = camera.Position + celestialDir * dist;
        if (celestialDir.Y > -0.22f) {
            float sunA = Math.Clamp((celestialDir.Y + 0.22f) * 3f, 0f, 1f);
            DrawCelestialBillboard(SkyTextures.Sun, new Rectangle(0, 0, 128, 128),
                sunPos, camRight, camUp, 46f, 46f, C(255, 255, 255, (int)(sunA * 255)));
        }

        // 3. Луна с фазами (атлас 8 фаз) и лунным ореолом
        Vector3 moonPos = camera.Position - celestialDir * dist;
        if (-celestialDir.Y > -0.22f) {
            float moonA = Math.Clamp((-celestialDir.Y + 0.22f) * 3f, 0f, 1f);
            int dayIndex = (int)MathF.Floor(_session.TotalPlaySeconds / DayNightCycle.CycleSeconds) % SkyTextures.PhaseCount;
            int phase = ((dayIndex + 4) % SkyTextures.PhaseCount + SkyTextures.PhaseCount) % SkyTextures.PhaseCount;
            var src = new Rectangle(phase * SkyTextures.PhasePx, 0, SkyTextures.PhasePx, SkyTextures.PhasePx);
            if (SaveSystem.FancyGraphics) {
                DrawCelestialBillboard(SkyTextures.Sun, new Rectangle(0, 0, 128, 128),
                    moonPos, camRight, camUp, 34f, 34f, C(120, 160, 235, (int)(moonA * 70)));
            }
            DrawCelestialBillboard(SkyTextures.MoonPhaseAtlas, src,
                moonPos, camRight, camUp, 26f, 26f, C(255, 255, 255, (int)(moonA * 255)));
        }

        Rlgl.EnableDepthTest();
        Rlgl.EnableDepthMask();
    }

    private static void DrawCelestialBillboard(Texture2D tex, Rectangle srcRec, Vector3 center, Vector3 camRight, Vector3 camUp, float sizeX, float sizeY, Color col) {
        float hx = sizeX * 0.5f;
        float hy = sizeY * 0.5f;
        Vector3 p0 = center - camRight * hx - camUp * hy;
        Vector3 p1 = center + camRight * hx - camUp * hy;
        Vector3 p2 = center + camRight * hx + camUp * hy;
        Vector3 p3 = center - camRight * hx + camUp * hy;

        float u0 = srcRec.X / Math.Max(1, tex.Width);
        float v0 = srcRec.Y / Math.Max(1, tex.Height);
        float u1 = (srcRec.X + srcRec.Width) / Math.Max(1, tex.Width);
        float v1 = (srcRec.Y + srcRec.Height) / Math.Max(1, tex.Height);

        Rlgl.SetTexture(tex.Id);
        Rlgl.Begin((int)DrawMode.Quads);
        Rlgl.Color4ub(col.R, col.G, col.B, col.A);

        Rlgl.TexCoord2f(u0, v1); Rlgl.Vertex3f(p0.X, p0.Y, p0.Z);
        Rlgl.TexCoord2f(u1, v1); Rlgl.Vertex3f(p1.X, p1.Y, p1.Z);
        Rlgl.TexCoord2f(u1, v0); Rlgl.Vertex3f(p2.X, p2.Y, p2.Z);
        Rlgl.TexCoord2f(u0, v0); Rlgl.Vertex3f(p3.X, p3.Y, p3.Z);

        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    /// <summary>
    /// Воксельные объёмные облака (без внутренних стенок). Только в Обычном мире.
    /// Сетка ячеек привязана к ветру: облака плавно дрейфуют, а не «перещёлкиваются»
    /// по 16 блоков; освещение берётся из модели атмосферы (закатный подсвет, ночь),
    /// по краю радиуса облака растворяются в цвете неба вместо резкого обрыва.
    /// </summary>
    public void DrawClouds(Camera3D camera) {
        if (SaveSystem.CloudsMode == 0) return;
        if (_world.Dimension != Dimension.Overworld) return; // в Энде и Незере облаков нет

        float time = (float)Raylib.GetTime();
        const float cloudY = 112f;
        var pPos = _session.Player.Position;

        const int step = 16;
        bool fancy = SaveSystem.CloudsMode == 2;
        int gridR = fancy ? (SaveSystem.GraphicsQuality == SaveSystem.GraphicsPreset.Fabulous ? 16 : 12) : 8;
        int size = gridR * 2 + 1;

        float wind = time * 2.2f;
        int windCells = (int)MathF.Floor(wind / step);
        float windFrac = wind - windCells * step;
        int baseCellX = (int)MathF.Floor((pPos.X + windFrac) / step);
        int baseCellZ = (int)MathF.Floor(pPos.Z / step);

        float height = fancy ? 4.0f : 0.8f;
        float y0 = cloudY;
        float y1 = cloudY + height;

        // Освещение облаков из модели атмосферы: рассеянный свет неба + прямой свет + закат
        var lit = Atmosphere.AmbientTop * 1.05f + Atmosphere.LightColor * 0.95f + new Vector3(0.05f);
        lit = Vector3.Lerp(lit, lit * 0.6f + Atmosphere.SunsetColor * 0.75f, Atmosphere.SunsetAmount * 0.6f);
        lit = Vector3.Min(lit, Vector3.One);
        var shade = lit * new Vector3(0.80f, 0.82f, 0.88f);
        byte alpha = (byte)(fancy ? 220 : 185);
        float rWorld = gridR * step;
        var horizon = Atmosphere.Horizon;

        // 1. Маска плотности облаков (в координатах «сетки ветра»)
        Span<bool> grid = stackalloc bool[size * size];
        for (int gx = 0; gx < size; gx++) {
            int x = gx - gridR;
            for (int gz = 0; gz < size; gz++) {
                int z = gz - gridR;
                if (x * x + z * z > gridR * gridR) { grid[gx * size + gz] = false; continue; }
                float sx = (baseCellX + x + windCells) * (float)step;
                float sz = (baseCellZ + z) * (float)step;
                float nx = sx * 0.0035f;
                float nz = sz * 0.0035f;
                float n = MathF.Sin(nx * 3.14159f) * MathF.Cos(nz * 3.14159f)
                        + MathF.Sin((nx + nz) * 2.1f) * 0.42f;
                grid[gx * size + gz] = n > 0.16f - Atmosphere.Overcast * 0.35f; // в непогоду облаков больше
            }
        }

        var shapesTex = Raylib.GetShapesTexture();
        var shapesRect = Raylib.GetShapesTextureRectangle();
        float u = (shapesRect.X + shapesRect.Width * 0.5f) / Math.Max(1, shapesTex.Width);
        float v = (shapesRect.Y + shapesRect.Height * 0.5f) / Math.Max(1, shapesTex.Height);

        void Quad(Color c, Vector3 a, Vector3 b, Vector3 d, Vector3 e) {
            Rlgl.Color4ub(c.R, c.G, c.B, c.A);
            Rlgl.TexCoord2f(u, v); Rlgl.Vertex3f(a.X, a.Y, a.Z);
            Rlgl.TexCoord2f(u, v); Rlgl.Vertex3f(b.X, b.Y, b.Z);
            Rlgl.TexCoord2f(u, v); Rlgl.Vertex3f(d.X, d.Y, d.Z);
            Rlgl.TexCoord2f(u, v); Rlgl.Vertex3f(e.X, e.Y, e.Z);
        }
        static Color ToColor(Vector3 c, float a) => new(
            (byte)Math.Clamp((int)(c.X * 255f), 0, 255), (byte)Math.Clamp((int)(c.Y * 255f), 0, 255),
            (byte)Math.Clamp((int)(c.Z * 255f), 0, 255), (byte)Math.Clamp((int)a, 0, 255));

        Rlgl.SetTexture(shapesTex.Id);
        Rlgl.Begin((int)DrawMode.Quads);

        for (int gx = 0; gx < size; gx++) {
            int x = gx - gridR;
            float x0 = (baseCellX + x) * (float)step - windFrac;
            float x1 = x0 + step;
            for (int gz = 0; gz < size; gz++) {
                if (!grid[gx * size + gz]) continue;
                int z = gz - gridR;
                float z0 = (baseCellZ + z) * (float)step;
                float z1 = z0 + step;

                // Растворение к краю радиуса: и прозрачность, и цвет уходят в горизонт
                float cdx = (x0 + x1) * 0.5f - pPos.X, cdz = (z0 + z1) * 0.5f - pPos.Z;
                float d = MathF.Sqrt(cdx * cdx + cdz * cdz) / rWorld;
                float fade = Math.Clamp((d - 0.55f) / 0.45f, 0f, 1f);
                fade = fade * fade * (3f - 2f * fade);
                float a = alpha * (1f - fade);
                if (a < 2f) continue;
                var top = Vector3.Lerp(lit, horizon, fade * 0.7f);
                var bottom = Vector3.Lerp(shade * 0.82f, horizon, fade * 0.7f);
                var sideZ = Vector3.Lerp(shade * 0.92f, horizon, fade * 0.7f);
                var sideX = Vector3.Lerp(shade, horizon, fade * 0.7f);

                bool hasN = gz > 0 && grid[gx * size + (gz - 1)];
                bool hasS = gz < size - 1 && grid[gx * size + (gz + 1)];
                bool hasW = gx > 0 && grid[(gx - 1) * size + gz];
                bool hasE = gx < size - 1 && grid[(gx + 1) * size + gz];

                // Низ (-Y) и верх (+Y)
                Quad(ToColor(bottom, a), new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1));
                if (fancy || camera.Position.Y > y0)
                    Quad(ToColor(top, a), new(x0, y1, z1), new(x1, y1, z1), new(x1, y1, z0), new(x0, y1, z0));

                if (!fancy) continue;
                // Боковые грани — только на границе облака
                if (!hasN) Quad(ToColor(sideZ, a), new(x0, y1, z0), new(x1, y1, z0), new(x1, y0, z0), new(x0, y0, z0));
                if (!hasS) Quad(ToColor(sideZ, a), new(x1, y1, z1), new(x0, y1, z1), new(x0, y0, z1), new(x1, y0, z1));
                if (!hasW) Quad(ToColor(sideX, a), new(x0, y1, z1), new(x0, y1, z0), new(x0, y0, z0), new(x0, y0, z1));
                if (!hasE) Quad(ToColor(sideX, a), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1), new(x1, y0, z0));
            }
        }

        Rlgl.End();
        Rlgl.SetTexture(0);
    }

    /// <summary>Осадки (дождь, снегопад, гроза с молниями).</summary>
    public void DrawWeather(Camera3D camera) {
        if (_session.Weather == WeatherType.Clear || _world.Dimension != Dimension.Overworld) return;

        var pPos = _session.Player.Position;
        int px = (int)MathF.Floor(pPos.X), py = (int)MathF.Floor(pPos.Y), pz = (int)MathF.Floor(pPos.Z);
        int playerSurface = _world.Generator.SurfaceHeight(px, pz);
        // Если игрок находится глубоко в пещере под землей, осадки перед глазами не рисуются
        if (py < playerSurface - 3 && _world.IsSolidAt(new Vec3i(px, py + 2, pz))) return;

        bool isSnow = py >= 85; // Снег на горных вершинах
        float time = (float)Raylib.GetTime();

        var rainColor = new Color(130, 160, 240, 160);
        var snowColor = new Color(245, 245, 255, 200);

        int count = SaveSystem.ParticlesMode == 0 ? 16 : (SaveSystem.ParticlesMode == 1 ? 36 : 64);
        for (int i = 0; i < count; i++) {
            float offX = ((i * 17 + (int)(time * 12f)) % 32) - 16;
            float offZ = ((i * 23 + (int)(time * 15f)) % 32) - 16;
            float fall = (time * (isSnow ? 5f : 24f) + i * 1.5f) % 20f;
            float offY = 14f - fall;

            var dropPos = camera.Position + new Vector3(offX, offY, offZ);
            int dx = (int)MathF.Floor(dropPos.X), dy = (int)MathF.Floor(dropPos.Y), dz = (int)MathF.Floor(dropPos.Z);
            // Читаем кэшированную поверхность загруженного чанка вместо тяжёлого 10-октавного шума на каждую каплю.
            int surf = _world.GetColumnSurfaceHeight(dx, dz);
            if (surf == int.MinValue) surf = playerSurface;
            if (dy < surf) continue; // Капли не проходят сквозь землю и крыши

            if (isSnow) {
                Raylib.DrawCube(dropPos, 0.18f, 0.18f, 0.18f, snowColor);
            } else {
                Raylib.DrawLine3D(dropPos, dropPos - new Vector3(0.1f, 0.9f, 0.1f), rainColor);
            }
        }
    }

    private static Color C(int r, int g, int b, int a = 255) => new((byte)r, (byte)g, (byte)b, (byte)a);

    private static Color LerpColor(Color a, Color b, float t) => C(
        (int)MathF.Round(a.R + (b.R - a.R) * t),
        (int)MathF.Round(a.G + (b.G - a.G) * t),
        (int)MathF.Round(a.B + (b.B - a.B) * t),
        255);
}
