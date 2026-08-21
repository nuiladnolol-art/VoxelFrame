using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Рендер мира: меши чанков (пересборка по очереди), небо с солнцем и
/// звёздами, декор (факелы, костры, пламя), подсветка целевого блока.
/// </summary>
public sealed class WorldRenderer : IDisposable {
    private readonly GameSession _session;
    private readonly GameWorld _world;
    private readonly List<GameChunk> _rebuildQueue = new();
    private Material _material;
    private bool _materialReady;

    private static readonly Random ParticleRng = new(2026);

    private const string VertexShaderSrc = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec4 vertexColor;
        uniform mat4 mvp;
        out vec2 fragTexCoord;
        out vec4 fragColor;
        out vec3 fragWorldPos;
        void main() {
            fragTexCoord = vertexTexCoord;
            fragColor = vertexColor;
            fragWorldPos = vertexPosition;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string FragmentShaderSrc = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        in vec3 fragWorldPos;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform float skyFactor;
        uniform float sunAngle;
        uniform vec3 playerLightPos;
        uniform float playerLightRadius;
        uniform vec3 creeperLightPos;
        uniform float creeperLightRadius;
        uniform vec3 cameraPos;
        uniform vec3 fogColor;
        uniform float fogStart;
        uniform float fogEnd;
        out vec4 finalColor;
        void main() {
            vec4 texelColor = texture(texture0, fragTexCoord);
            if (texelColor.a < 0.05) discard;

            float sun = fragColor.r;       // Солнечный свет [0..1]
            float block = fragColor.g;     // Блочный (факельный) свет [0..1]
            float faceDir = fragColor.b;   // Направленное затенение граней [0.55..1.0]

            // Динамический свет от факела в руке игрока
            if (playerLightRadius > 0.1) {
                float d = distance(fragWorldPos, playerLightPos);
                if (d < playerLightRadius) {
                    float handLight = clamp(1.0 - (d / playerLightRadius), 0.0, 1.0);
                    block = max(block, handLight * 0.95);
                }
            }

            // Динамический свет от раздувающегося белого крипера
            if (creeperLightRadius > 0.1) {
                float d = distance(fragWorldPos, creeperLightPos);
                if (d < creeperLightRadius) {
                    float creepLight = clamp(1.0 - (d / creeperLightRadius), 0.0, 1.0);
                    block = max(block, creepLight * 1.0);
                }
            }

            // Нелинейная кривая освещенности Minecraft (Alpha / Modern)
            float sunCurve = pow(sun * max(skyFactor, 0.04), 1.35);
            float blockCurve = pow(block, 1.25);

            // Теплый уютный оттенок факельного света + чистый дневной свет
            vec3 torchLight = vec3(1.0, 0.82, 0.55) * blockCurve;
            vec3 sunLight = vec3(0.96, 0.96, 1.0) * sunCurve;
            vec3 ambient = vec3(0.12, 0.12, 0.16);

            vec3 totalLight = (sunLight + torchLight + ambient) * faceDir;
            totalLight = clamp(totalLight, 0.08, 1.0);

            vec4 lightedColor = texelColor * colDiffuse * vec4(totalLight, 1.0);

            // Плавный дистанционный туман горизонта (убирает рябь, контурные линии и резкую границу мира)
            float dist = distance(fragWorldPos, cameraPos);
            float fogFactor = clamp((dist - fogStart) / (fogEnd - fogStart), 0.0, 1.0);
            fogFactor = pow(fogFactor, 1.35);

            finalColor = vec4(mix(lightedColor.rgb, fogColor, fogFactor), lightedColor.a);
        }
        """;

    private int _skyFactorLoc = -1;
    private int _sunAngleLoc = -1;
    private int _playerLightPosLoc = -1;
    private int _playerLightRadiusLoc = -1;
    private int _creeperLightPosLoc = -1;
    private int _creeperLightRadiusLoc = -1;
    private int _cameraPosLoc = -1;
    private int _fogColorLoc = -1;
    private int _fogStartLoc = -1;
    private int _fogEndLoc = -1;
    private static readonly Vector3[] StarPositions = GenerateStarField(160);

    public struct VoxelParticle {
        public Vector3 Position;
        public Vector3 Velocity;
        public Color Color;
        public float Size;
        public float Lifetime;
        public float MaxLifetime;
        public bool IsCrit;
    }

    private readonly List<VoxelParticle> _particles = new();

    private static Vector3[] GenerateStarField(int count) {
        var stars = new Vector3[count];
        var rng = new Random(7919);
        for (int i = 0; i < count; i++) {
            float u = rng.NextSingle() * 2f - 1f;
            float theta = rng.NextSingle() * MathF.Tau;
            float r = MathF.Sqrt(MathF.Max(0f, 1f - u * u));
            float y = MathF.Abs(u) * 0.92f + 0.08f;
            stars[i] = Vector3.Normalize(new Vector3(r * MathF.Cos(theta), y, r * MathF.Sin(theta)));
        }
        return stars;
    }

    public WorldRenderer(GameSession session) {
        _session = session;
        _world = session.World;
        _material = Raylib.LoadMaterialDefault();
        unsafe {
            _material.Maps[(int)MaterialMapIndex.Albedo].Texture = TextureAtlas.Atlas;
        }
        var shader = Raylib.LoadShaderFromMemory(VertexShaderSrc, FragmentShaderSrc);
        if (Raylib.IsShaderValid(shader)) {
            _material.Shader = shader;
            _skyFactorLoc = Raylib.GetShaderLocation(shader, "skyFactor");
            _sunAngleLoc = Raylib.GetShaderLocation(shader, "sunAngle");
            _playerLightPosLoc = Raylib.GetShaderLocation(shader, "playerLightPos");
            _playerLightRadiusLoc = Raylib.GetShaderLocation(shader, "playerLightRadius");
            _creeperLightPosLoc = Raylib.GetShaderLocation(shader, "creeperLightPos");
            _creeperLightRadiusLoc = Raylib.GetShaderLocation(shader, "creeperLightRadius");
            _cameraPosLoc = Raylib.GetShaderLocation(shader, "cameraPos");
            _fogColorLoc = Raylib.GetShaderLocation(shader, "fogColor");
            _fogStartLoc = Raylib.GetShaderLocation(shader, "fogStart");
            _fogEndLoc = Raylib.GetShaderLocation(shader, "fogEnd");
        }
        _materialReady = true;

        _world.OnBlockRemoved += SpawnBlockParticles;
        _world.OnDustSpawned += SpawnDustParticles;
        _world.OnCritSpawned += SpawnCritParticles;
    }

    public void ProcessMeshQueue() {
        foreach (var gc in _world.DrainMeshDirty()) {
            _rebuildQueue.Remove(gc);
            _rebuildQueue.Insert(0, gc);
        }
        int budget = 3;
        while (_rebuildQueue.Count > 0 && budget-- > 0) {
            var gc = _rebuildQueue[0];
            _rebuildQueue.RemoveAt(0);
            gc.UnloadMesh();
            gc.Meshes.AddRange(ChunkMesher.Build(gc, _world));
            gc.MeshUploaded = gc.Meshes.Count > 0;
            if (!gc.MeshUploaded) gc.UnloadMesh();
        }
    }

    public void DrawWorld() {
        if (!_materialReady) return;

        // Фактор неба — uniform шейдера
        if (_skyFactorLoc != -1) {
            float sky = _session.DayNight.SkyFactor;
            unsafe { Raylib.SetShaderValue(_material.Shader, _skyFactorLoc, &sky, ShaderUniformDataType.Float); }
        }
        if (_sunAngleLoc != -1) {
            float angle = _session.DayNight.TimeOfDay * MathF.PI * 2f;
            unsafe { Raylib.SetShaderValue(_material.Shader, _sunAngleLoc, &angle, ShaderUniformDataType.Float); }
        }

        bool holdingTorch = _session.Player.SelectedEntry?.Item.Definition.Id == GameData.TorchItem.Id || _session.Player.OffhandItem?.Id == GameData.TorchItem.Id;
        Vector3 lightPos = _session.Player.Eye;
        float lightRadius = holdingTorch ? 14.0f : 0f;
        if (_playerLightPosLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _playerLightPosLoc, &lightPos, ShaderUniformDataType.Vec3); }
        }
        if (_playerLightRadiusLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _playerLightRadiusLoc, &lightRadius, ShaderUniformDataType.Float); }
        }

        // Динамический свет раздувающегося белого крипера перед взрывом
        Vector3 creeperLightPos = Vector3.Zero;
        float creeperLightRadius = 0f;
        foreach (var h in _world.HostileMobs) {
            if (h.Type == HostileType.Creeper && h.FuseTimer > 0f && h.Alive) {
                float rad = 6.0f + (h.FuseTimer / 1.3f) * 6.5f; // Свечение от 6 до 12.5 блоков
                if (rad > creeperLightRadius) {
                    creeperLightRadius = rad;
                    creeperLightPos = h.Position + new Vector3(0f, 0.5f, 0f);
                }
            }
        }

        if (_creeperLightPosLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _creeperLightPosLoc, &creeperLightPos, ShaderUniformDataType.Vec3); }
        }
        if (_creeperLightRadiusLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _creeperLightRadiusLoc, &creeperLightRadius, ShaderUniformDataType.Float); }
        }

        if (_cameraPosLoc != -1) {
            var camPos = _session.Camera.Position;
            unsafe { Raylib.SetShaderValue(_material.Shader, _cameraPosLoc, &camPos, ShaderUniformDataType.Vec3); }
        }
        if (_fogColorLoc != -1) {
            Color fogC = GetFogColor();
            var fogVec = new Vector3(fogC.R / 255f, fogC.G / 255f, fogC.B / 255f);
            unsafe { Raylib.SetShaderValue(_material.Shader, _fogColorLoc, &fogVec, ShaderUniformDataType.Vec3); }
        }
        float maxRenderDist = SaveSystem.RenderDistanceSetting * 16.0f;
        float fogStart = (_world.Dimension == Dimension.Nether) ? 20.0f : MathF.Max(25.0f, maxRenderDist * 0.55f);
        float fogEnd = (_world.Dimension == Dimension.Nether) ? 68.0f : MathF.Max(40.0f, maxRenderDist * 0.95f);
        if (_fogStartLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _fogStartLoc, &fogStart, ShaderUniformDataType.Float); }
        }
        if (_fogEndLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _fogEndLoc, &fogEnd, ShaderUniformDataType.Float); }
        }

        unsafe {
            Rlgl.EnableBackfaceCulling();
            foreach (var gc in _world.Chunks) {
                if (!gc.MeshUploaded) continue;
                foreach (var m in gc.Meshes)
                    Raylib.DrawMesh(m, _material, Matrix4x4.Identity);
            }
        }
    }

    // ── Небо и Погода ────────────────────────────────────────────────────────

    /// <summary>Фоновый градиент неба (2D купол).</summary>
    public void DrawSky() {
        if (_world.Dimension == Dimension.Nether) {
            Raylib.ClearBackground(new Color(45, 10, 10, 255));
            return;
        }
        float f = _session.DayNight.SkyFactor;
        if (_session.Weather != WeatherType.Clear) {
            f *= 0.45f; // Пасмурное грозовое небо
        }
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();

        var top = LerpColor(C(8, 10, 26, 255), C(92, 150, 240, 255), f);
        var bottom = LerpColor(C(16, 18, 40, 255), C(178, 208, 244, 255), f);
        Raylib.DrawRectangleGradientV(0, 0, w, h, top, bottom);
    }

    /// <summary>3D Небесные светила (Солнце, Луна, звёзды в мировом пространстве).</summary>
    public void Draw3DSky(Camera3D camera) {
        if (_world.Dimension == Dimension.Nether) return;

        float f = _session.DayNight.SkyFactor;
        if (_session.Weather != WeatherType.Clear) f *= 0.45f;
        float u = _session.DayNight.TimeOfDay;
        float sunAngle = 2f * MathF.PI * (u - 0.25f);

        // Направление орбиты светил в мировом 3D-пространстве (Восток -> Зенит -> Запад)
        Vector3 celestialDir = Vector3.Normalize(new Vector3(MathF.Cos(sunAngle), MathF.Sin(sunAngle), 0.22f));
        float dist = 170f;

        // 1. Звёзды в 3D (видны ночью и в сумерках)
        if (f < 0.65f) {
            float starAlpha = Math.Clamp((0.65f - f) / 0.65f, 0f, 1f);
            byte a = (byte)(starAlpha * 220);
            Color starColor = C(245, 245, 255, (int)a);
            foreach (var s in StarPositions) {
                Vector3 starWorld = camera.Position + s * dist;
                Raylib.DrawCube(starWorld, 1.3f, 1.3f, 1.3f, starColor);
            }
        }

        // 2. 3D Солнце
        Vector3 sunPos = camera.Position + celestialDir * dist;
        if (celestialDir.Y > -0.22f) {
            float sunA = Math.Clamp((celestialDir.Y + 0.22f) * 3f, 0f, 1f);
            Color sunColor = C(255, 255, 220, (int)(sunA * 255));
            Color sunGlow = C(255, 215, 80, (int)(sunA * 180));
            Raylib.DrawCube(sunPos, 26f, 26f, 26f, sunColor);
            Raylib.DrawCubeWires(sunPos, 26.6f, 26.6f, 26.6f, sunGlow);
        }

        // 3. 3D Луна
        Vector3 moonPos = camera.Position - celestialDir * dist;
        if (-celestialDir.Y > -0.22f) {
            float moonA = Math.Clamp((-celestialDir.Y + 0.22f) * 3f, 0f, 1f);
            Color moonColor = C(230, 235, 255, (int)(moonA * 255));
            Color moonGlow = C(150, 180, 240, (int)(moonA * 150));
            Raylib.DrawCube(moonPos, 20f, 20f, 20f, moonColor);
            Raylib.DrawCubeWires(moonPos, 20.5f, 20.5f, 20.5f, moonGlow);
        }
    }

    /// <summary>Воксельные облака в небе (Alpha 1.2.6 style, оптимизированные).</summary>
    public void DrawClouds(Camera3D camera) {
        float time = (float)Raylib.GetTime();
        float cloudY = 108f;
        var pPos = _session.Player.Position;

        int step = 20;
        bool fancy = SaveSystem.FancyGraphics;
        int gridR = fancy ? 12 : 8; // Круговой радиус вместо тяжёлой квадратной матрицы
        int baseX = (int)MathF.Floor(pPos.X / step) * step;
        int baseZ = (int)MathF.Floor(pPos.Z / step) * step;

        float wind = time * 2.5f;
        var cloudColor = new Color(255, 255, 255, fancy ? 190 : 160);
        var cloudBottom = new Color(205, 215, 230, 190);

        for (int x = -gridR; x <= gridR; x++) {
            for (int z = -gridR; z <= gridR; z++) {
                if (x * x + z * z > gridR * gridR) continue;

                float wx = baseX + x * step;
                float wz = baseZ + z * step;

                // Быстрый сглаженный шум
                float nx = (wx + wind) * 0.004f;
                float nz = wz * 0.004f;
                float n = MathF.Sin(nx * 3.1415f) * MathF.Cos(nz * 3.1415f)
                        + MathF.Sin((nx + nz) * 2.2f) * 0.45f;

                if (n > 0.12f) {
                    var cPos = new Vector3(wx + step * 0.5f, cloudY, wz + step * 0.5f);
                    if (fancy) {
                        Raylib.DrawCube(cPos, step, 3.2f, step, cloudColor);
                        Raylib.DrawCube(cPos - new Vector3(0f, 1.6f, 0f), step, 0.1f, step, cloudBottom);
                    } else {
                        // Быстрый режим (Fast Graphics) — плоский один слой
                        Raylib.DrawCube(cPos, step, 0.8f, step, cloudColor);
                    }
                }
            }
        }
    }

    /// <summary>Осадки (дождь, снегопад, гроза с молниями).</summary>
    public void DrawWeather(Camera3D camera) {
        if (_session.Weather == WeatherType.Clear || _world.Dimension != Dimension.Overworld) return;

        var pPos = _session.Player.Position;
        int px = (int)MathF.Floor(pPos.X), py = (int)MathF.Floor(pPos.Y), pz = (int)MathF.Floor(pPos.Z);
        bool isSnow = py >= 85; // Снег на горных вершинах
        float time = (float)Raylib.GetTime();

        var rainColor = new Color(130, 160, 240, 160);
        var snowColor = new Color(245, 245, 255, 200);

        int count = 64;
        for (int i = 0; i < count; i++) {
            float offX = ((i * 17 + (int)(time * 12f)) % 32) - 16;
            float offZ = ((i * 23 + (int)(time * 15f)) % 32) - 16;
            float fall = (time * (isSnow ? 5f : 24f) + i * 1.5f) % 20f;
            float offY = 14f - fall;

            var dropPos = camera.Position + new Vector3(offX, offY, offZ);
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

    // ── Декор: факелы, костры, пламя, подсветка ──────────────────────────────

    public void DrawDecorations(float dt) {
        unsafe {
            var camPos = _session.Camera.Position;
            var camFwd = _session.Player.Forward;
            const float maxDistSq = 48f * 48f; // Высокая производительность: отсечение удаленной растительности

            foreach (var pos in _world.DecorPositions) {
                var p = new Vector3(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
                float distSq = Vector3.DistanceSquared(p, camPos);
                if (distSq > maxDistSq) continue;

                // Отсечение объектов позади камеры
                if (distSq > 16f) {
                    var dir = p - camPos;
                    if (Vector3.Dot(dir, camFwd) < -0.2f) continue;
                }

                var v = _world.GetVoxel(pos);
                if (v.TypeId == 0) continue;

                var light = GetLightFactor(p);

                if (v.TypeId == GameData.BTorch.Id) {
                    byte torchFacing = v.SubGridLayerMask; // 0=Floor, 1=West wall (+X block), 2=East wall (-X block), 3=North wall (+Z block), 4=South wall (-Z block)
                    var woodCol = new Color(130, 90, 48, 255);
                    var headCol = new Color(50, 42, 38, 255);

                    Vector3 stickPos;
                    Vector3 stickSize;
                    Vector3 flamePos;

                    if (torchFacing == 1) {
                        // Прикреплен к блоку на западе (+X): факел наклонен в сторону -X
                        stickPos = new Vector3(pos.X + 0.25f, pos.Y + 0.38f, pos.Z + 0.5f);
                        stickSize = new Vector3(0.12f, 0.48f, 0.12f);
                        flamePos = new Vector3(pos.X + 0.30f, pos.Y + 0.58f, pos.Z + 0.5f);
                        Raylib.DrawCube(stickPos, stickSize.X, stickSize.Y, stickSize.Z, woodCol);
                        Raylib.DrawCube(new Vector3(flamePos.X, flamePos.Y - 0.05f, flamePos.Z), 0.14f, 0.12f, 0.14f, headCol);
                    } else if (torchFacing == 2) {
                        // Прикреплен к блоку на востоке (-X): факел наклонен в сторону +X
                        stickPos = new Vector3(pos.X + 0.75f, pos.Y + 0.38f, pos.Z + 0.5f);
                        stickSize = new Vector3(0.12f, 0.48f, 0.12f);
                        flamePos = new Vector3(pos.X + 0.70f, pos.Y + 0.58f, pos.Z + 0.5f);
                        Raylib.DrawCube(stickPos, stickSize.X, stickSize.Y, stickSize.Z, woodCol);
                        Raylib.DrawCube(new Vector3(flamePos.X, flamePos.Y - 0.05f, flamePos.Z), 0.14f, 0.12f, 0.14f, headCol);
                    } else if (torchFacing == 3) {
                        // Прикреплен к блоку на севере (+Z): факел наклонен в сторону -Z
                        stickPos = new Vector3(pos.X + 0.5f, pos.Y + 0.38f, pos.Z + 0.25f);
                        stickSize = new Vector3(0.12f, 0.48f, 0.12f);
                        flamePos = new Vector3(pos.X + 0.5f, pos.Y + 0.58f, pos.Z + 0.30f);
                        Raylib.DrawCube(stickPos, stickSize.X, stickSize.Y, stickSize.Z, woodCol);
                        Raylib.DrawCube(new Vector3(flamePos.X, flamePos.Y - 0.05f, flamePos.Z), 0.14f, 0.12f, 0.14f, headCol);
                    } else if (torchFacing == 4) {
                        // Прикреплен к блоку на юге (-Z): факел наклонен в сторону +Z
                        stickPos = new Vector3(pos.X + 0.5f, pos.Y + 0.38f, pos.Z + 0.75f);
                        stickSize = new Vector3(0.12f, 0.48f, 0.12f);
                        flamePos = new Vector3(pos.X + 0.5f, pos.Y + 0.58f, pos.Z + 0.70f);
                        Raylib.DrawCube(stickPos, stickSize.X, stickSize.Y, stickSize.Z, woodCol);
                        Raylib.DrawCube(new Vector3(flamePos.X, flamePos.Y - 0.05f, flamePos.Z), 0.14f, 0.12f, 0.14f, headCol);
                    } else {
                        // Стоит прямо на полу
                        stickPos = new Vector3(pos.X + 0.5f, pos.Y + 0.25f, pos.Z + 0.5f);
                        stickSize = new Vector3(0.12f, 0.50f, 0.12f);
                        flamePos = new Vector3(pos.X + 0.5f, pos.Y + 0.50f, pos.Z + 0.5f);
                        Raylib.DrawCube(stickPos, stickSize.X, stickSize.Y, stickSize.Z, woodCol);
                        Raylib.DrawCube(new Vector3(flamePos.X, flamePos.Y - 0.05f, flamePos.Z), 0.14f, 0.12f, 0.14f, headCol);
                    }
                    DrawFlame(flamePos, 0.22f, dt);
                } else if (v.TypeId == GameData.BWheatCrop.Id) {
                    int stage = Math.Clamp((int)v.SubGridLayerMask, 0, 3);
                    byte tile = (byte)(TextureAtlas.TWheatCrop0 + stage);
                    var src = new Rectangle(
                        tile % TextureAtlas.Cols * TextureAtlas.TilePx,
                        tile / TextureAtlas.Cols * TextureAtlas.TilePx,
                        TextureAtlas.TilePx, TextureAtlas.TilePx);
                    var size = new Vector2(0.85f, 0.85f);
                    var cropPos = new Vector3(pos.X + 0.5f, pos.Y + 0.425f, pos.Z + 0.5f);
                    Color tint = ShadeColor(Color.White, light, cropPos);
                    Raylib.DrawBillboardRec(_session.Camera, TextureAtlas.Atlas, src, cropPos, size, tint);
                } else if (v.TypeId == GameData.BTallGrass.Id) {
                    byte tile = (byte)TextureAtlas.TTallGrass;
                    var src = new Rectangle(
                        tile % TextureAtlas.Cols * TextureAtlas.TilePx,
                        tile / TextureAtlas.Cols * TextureAtlas.TilePx,
                        TextureAtlas.TilePx, TextureAtlas.TilePx);
                    var size = new Vector2(0.9f, 0.9f);
                    var grassPos = new Vector3(pos.X + 0.5f, pos.Y + 0.45f, pos.Z + 0.5f);
                    Color tint = ShadeColor(Color.White, light, grassPos);
                    Raylib.DrawBillboardRec(_session.Camera, TextureAtlas.Atlas, src, grassPos, size, tint);
                }
            }
            foreach (var pos in _world.Fire.Burning.Keys) {
                var fp = new Vector3(pos.X + 0.5f, pos.Y + 0.85f, pos.Z + 0.5f);
                if (Vector3.DistanceSquared(fp, camPos) <= maxDistSq) {
                    DrawFlame(fp, 0.5f, dt);
                }
            }
        }
        if (_session.HasTarget) {
            var t = _session.TargetBlock;
            var p = new Vector3(t.X + 0.5f, t.Y + 0.5f, t.Z + 0.5f);
            Raylib.DrawCubeWires(p, 1.005f, 1.005f, 1.005f, Color.Black);
        }
        DrawParticles(dt);
    }

    /// <summary>Пламя: пара аддитивных кубиков с дрожанием.</summary>
    private static void DrawFlame(Vector3 pos, float size, float dt) {
        float jx = (ParticleRng.NextSingle() - 0.5f) * 0.12f;
        float jz = (ParticleRng.NextSingle() - 0.5f) * 0.12f;
        float pulse = 0.85f + 0.15f * MathF.Sin((float)(ParticleRng.NextDouble() * MathF.Tau));
        Raylib.BeginBlendMode(BlendMode.Additive);
        var inner = C(255, 180, 60, 200);
        var outer = C(255, 90, 20, 140);
        Raylib.DrawCube(pos + new Vector3(jx, 0.1f, jz), size * 0.5f * pulse, size * pulse, size * 0.5f * pulse, outer);
        Raylib.DrawCube(pos + new Vector3(-jx * 0.5f, 0.16f, -jz * 0.5f), size * 0.3f * pulse, size * 0.55f * pulse, size * 0.3f * pulse, inner);
        Raylib.EndBlendMode();
    }

    private Vector3 GetLightFactor(Vector3 pos) {
        var cell = new Vec3i((int)MathF.Floor(pos.X), (int)MathF.Floor(pos.Y), (int)MathF.Floor(pos.Z));
        byte sun = _world.GetSunLight(cell);
        byte block = _world.GetBlockLight(cell);
        float skyFactor = _session.DayNight.SkyFactor;

        // Динамический свет факела в руке игрока
        bool holdingTorch = _session.Player.SelectedEntry?.Item.Definition.Id == GameData.TorchItem.Id || _session.Player.OffhandItem?.Id == GameData.TorchItem.Id;
        if (holdingTorch) {
            float d = Vector3.Distance(pos, _session.Player.Eye);
            if (d < 12.5f) {
                byte dynBlock = (byte)(14 * (1.0f - d / 12.5f));
                if (dynBlock > block) block = dynBlock;
            }
        }

        float sunCurve = MathF.Pow(sun / 15f, 1.4f) * skyFactor;
        float blockCurve = MathF.Pow(block / 15f, 1.4f);

        Vector3 torchLight = new Vector3(1.0f, 0.82f, 0.55f) * blockCurve;
        Vector3 sunLight = new Vector3(0.96f, 0.96f, 1.0f) * sunCurve;
        Vector3 ambient = new Vector3(0.12f, 0.12f, 0.14f);

        Vector3 total = sunLight + torchLight + ambient;
        return new Vector3(
            Math.Clamp(total.X, 0.10f, 1.0f),
            Math.Clamp(total.Y, 0.10f, 1.0f),
            Math.Clamp(total.Z, 0.10f, 1.0f)
        );
    }

    public Color GetFogColor() {
        float f = _session.DayNight.SkyFactor;
        if (_session.Weather != WeatherType.Clear) f *= 0.45f;
        return (_world.Dimension == Dimension.Nether)
            ? new Color(45, 10, 10, 255)
            : LerpColor(C(16, 18, 40, 255), C(178, 208, 244, 255), f);
    }

    public float GetFogFactor(Vector3 pos) {
        float dist = Vector3.Distance(_session.Camera.Position, pos);
        float maxRenderDist = SaveSystem.RenderDistanceSetting * 16.0f;
        float fogStart = (_world.Dimension == Dimension.Nether) ? 20.0f : MathF.Max(25.0f, maxRenderDist * 0.55f);
        float fogEnd = (_world.Dimension == Dimension.Nether) ? 68.0f : MathF.Max(40.0f, maxRenderDist * 0.95f);
        float fogFactor = Math.Clamp((dist - fogStart) / (fogEnd - fogStart), 0.0f, 1.0f);
        return MathF.Pow(fogFactor, 1.35f);
    }

    private static Color ShadeColor(Color baseColor, Vector3 light) =>
        new(
            (byte)Math.Clamp((int)(baseColor.R * light.X), 0, 255),
            (byte)Math.Clamp((int)(baseColor.G * light.Y), 0, 255),
            (byte)Math.Clamp((int)(baseColor.B * light.Z), 0, 255),
            baseColor.A
        );

    private Color ShadeColor(Color baseColor, Vector3 light, Vector3 pos) {
        var lit = ShadeColor(baseColor, light);
        float fogFactor = GetFogFactor(pos);
        if (fogFactor > 0.001f) {
            var fog = GetFogColor();
            return LerpColor(lit, fog, fogFactor);
        }
        return lit;
    }

    public void DrawEntities(Camera3D camera) {
        float time = (float)Raylib.GetTime();
        // 1. Draw Item Pickups as Billboards with ambient lighting and fog
        foreach (var p in _world.Pickups) {
            if (p.Quantity <= 0) continue;
            
            DrawSoftShadow(p.Position, 0.22f);
            
            float bob = MathF.Sin(p.BobPhase + (float)Raylib.GetTime() * 3f) * 0.12f;
            var pos = p.Position + new Vector3(0f, bob + 0.25f, 0f);
            byte tile = TextureAtlas.ItemTile(p.Definition.Id);
            var src = new Rectangle(
                tile % TextureAtlas.Cols * TextureAtlas.TilePx,
                tile / TextureAtlas.Cols * TextureAtlas.TilePx,
                TextureAtlas.TilePx, TextureAtlas.TilePx);
            
            var light = GetLightFactor(p.Position);
            Color tint = ShadeColor(Color.White, light, p.Position);
            Raylib.DrawBillboardRec(camera, TextureAtlas.Atlas, src, pos, new Vector2(0.4f, 0.4f), tint);
        }

        // 2. Draw Animals (Pig, Cow, Sheep) with 3D model, animations and fog
        foreach (var a in _world.Animals) {
            if (!a.Alive) continue;
            DrawSoftShadow(a.Position - new Vector3(0f, a.HalfSizeY, 0f), a.HalfSizeX + 0.05f);

            var light = GetLightFactor(a.Position);
            var aPos = a.Position;

            Vector3 fwd = new(0f, 0f, 1f);
            if (a.Velocity.LengthSquared() > 0.01f) {
                var dirH = Vector2.Normalize(new Vector2(a.Velocity.X, a.Velocity.Z));
                fwd = new Vector3(dirH.X, 0f, dirH.Y);
            }

            float angleDegrees = MathF.Atan2(fwd.X, fwd.Z) * 180f / MathF.PI;
            bool isMoving = new Vector2(a.Velocity.X, a.Velocity.Z).LengthSquared() > 0.01f;
            float walkSwing = isMoving ? MathF.Sin(time * 9f) * 0.35f : 0f;

            Rlgl.PushMatrix();
            Rlgl.Translatef(a.Position.X, a.Position.Y, a.Position.Z);
            Rlgl.Rotatef(angleDegrees, 0f, 1f, 0f);

            if (a.HurtTime > 0f) {
                float roll = MathF.Sin(a.HurtTime * MathF.PI * 10f) * 12f;
                Rlgl.Rotatef(roll, 0f, 0f, 1f);
            }

            if (a.Type == AnimalType.Pig) {
                // ── PIG MODEL ─────────────────────────────────────────────
                var pigPink = ShadeColor(a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(240, 160, 160, 255), light, aPos);
                var snoutColor = ShadeColor(a.HurtTime > 0f ? new Color(220, 60, 60, 255) : new Color(220, 120, 130, 255), light, aPos);
                var pigOutline = ShadeColor(new Color(40, 20, 20, 180), light, aPos);

                // Body
                Raylib.DrawCube(new Vector3(0f, 0.125f, 0f), 0.65f, 0.55f, 0.85f, pigPink);
                Raylib.DrawCubeWires(new Vector3(0f, 0.125f, 0f), 0.652f, 0.552f, 0.852f, pigOutline);

                // Head
                var pigHead = new Vector3(0f, 0.25f, 0.55f);
                Raylib.DrawCube(pigHead, 0.45f, 0.45f, 0.45f, pigPink);
                Raylib.DrawCubeWires(pigHead, 0.452f, 0.452f, 0.452f, pigOutline);

                // Snout
                var pigSnout = pigHead + new Vector3(0f, -0.05f, 0.26f);
                Raylib.DrawCube(pigSnout, 0.22f, 0.14f, 0.10f, snoutColor);

                // Eyes
                Raylib.DrawCube(pigHead + new Vector3(-0.16f, 0.08f, 0.23f), 0.06f, 0.06f, 0.01f, ShadeColor(Color.Black, light, aPos));
                Raylib.DrawCube(pigHead + new Vector3(0.16f, 0.08f, 0.23f), 0.06f, 0.06f, 0.01f, ShadeColor(Color.Black, light, aPos));

                // 4 Legs
                float pLegW = 0.18f, pLegH = 0.35f, pLegD = 0.18f;
                var pFL = new Vector3(-0.20f, -0.275f, 0.25f + walkSwing * 0.2f);
                var pFR = new Vector3(0.20f, -0.275f, 0.25f - walkSwing * 0.2f);
                var pBL = new Vector3(-0.20f, -0.275f, -0.25f - walkSwing * 0.2f);
                var pBR = new Vector3(0.20f, -0.275f, -0.25f + walkSwing * 0.2f);

                Raylib.DrawCube(pFL, pLegW, pLegH, pLegD, pigPink);
                Raylib.DrawCube(pFR, pLegW, pLegH, pLegD, pigPink);
                Raylib.DrawCube(pBL, pLegW, pLegH, pLegD, pigPink);
                Raylib.DrawCube(pBR, pLegW, pLegH, pLegD, pigPink);

            } else if (a.Type == AnimalType.Cow) {
                // ── COW MODEL ─────────────────────────────────────────────
                var cowWhite = ShadeColor(a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(240, 240, 240, 255), light, aPos);
                var cowBlack = ShadeColor(a.HurtTime > 0f ? new Color(180, 40, 40, 255) : new Color(55, 45, 40, 255), light, aPos);
                var snoutColor = ShadeColor(a.HurtTime > 0f ? new Color(200, 70, 70, 255) : new Color(175, 140, 140, 255), light, aPos);
                var hornColor = ShadeColor(new Color(210, 210, 205, 255), light, aPos);
                var udderPink = ShadeColor(new Color(245, 170, 180, 255), light, aPos);
                var cowOutline = ShadeColor(new Color(30, 20, 15, 180), light, aPos);

                // Body
                Raylib.DrawCube(new Vector3(0f, 0.15f, 0f), 0.75f, 0.60f, 1.05f, cowWhite);
                Raylib.DrawCubeWires(new Vector3(0f, 0.15f, 0f), 0.752f, 0.602f, 1.052f, cowOutline);
                Raylib.DrawCube(new Vector3(-0.20f, 0.22f, 0.15f), 0.38f, 0.35f, 0.45f, cowBlack);
                Raylib.DrawCube(new Vector3(0.20f, 0.12f, -0.20f), 0.38f, 0.38f, 0.45f, cowBlack);

                // Udder
                Raylib.DrawCube(new Vector3(0f, -0.10f, -0.25f), 0.20f, 0.12f, 0.22f, udderPink);

                // Head
                var headPos = new Vector3(0f, 0.40f, 0.65f);
                Raylib.DrawCube(headPos, 0.45f, 0.45f, 0.45f, cowBlack);
                Raylib.DrawCubeWires(headPos, 0.452f, 0.452f, 0.452f, cowOutline);

                // Snout
                Raylib.DrawCube(headPos + new Vector3(0f, -0.08f, 0.26f), 0.32f, 0.20f, 0.14f, snoutColor);

                // Horns
                Raylib.DrawCube(headPos + new Vector3(-0.22f, 0.24f, -0.05f), 0.08f, 0.16f, 0.08f, hornColor);
                Raylib.DrawCube(headPos + new Vector3(0.22f, 0.24f, -0.05f), 0.08f, 0.16f, 0.08f, hornColor);

                // Eyes
                Raylib.DrawCube(headPos + new Vector3(-0.18f, 0.06f, 0.23f), 0.06f, 0.06f, 0.01f, ShadeColor(Color.Black, light, aPos));
                Raylib.DrawCube(headPos + new Vector3(0.18f, 0.06f, 0.23f), 0.06f, 0.06f, 0.01f, ShadeColor(Color.Black, light, aPos));

                // 4 Legs
                float cLegW = 0.20f, cLegH = 0.55f, cLegD = 0.20f;
                var cFL = new Vector3(-0.24f, -0.375f, 0.32f + walkSwing * 0.2f);
                var cFR = new Vector3(0.24f, -0.375f, 0.32f - walkSwing * 0.2f);
                var cBL = new Vector3(-0.24f, -0.375f, -0.32f - walkSwing * 0.2f);
                var cBR = new Vector3(0.24f, -0.375f, -0.32f + walkSwing * 0.2f);

                Raylib.DrawCube(cFL, cLegW, cLegH, cLegD, cowBlack);
                Raylib.DrawCube(cFR, cLegW, cLegH, cLegD, cowBlack);
                Raylib.DrawCube(cBL, cLegW, cLegH, cLegD, cowBlack);
                Raylib.DrawCube(cBR, cLegW, cLegH, cLegD, cowBlack);

            } else if (a.Type == AnimalType.Sheep) {
                // ── SHEEP MODEL ───────────────────────────────────────────
                var woolWhite = ShadeColor(a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(235, 235, 235, 255), light, aPos);
                var skinTan = ShadeColor(a.HurtTime > 0f ? new Color(210, 80, 80, 255) : new Color(225, 205, 185, 255), light, aPos);
                var sheepOutline = ShadeColor(new Color(50, 50, 50, 180), light, aPos);

                // Fluffy Wool Body
                Raylib.DrawCube(new Vector3(0f, 0.10f, 0f), 0.80f, 0.65f, 0.95f, woolWhite);
                Raylib.DrawCubeWires(new Vector3(0f, 0.10f, 0f), 0.802f, 0.652f, 0.952f, sheepOutline);

                // Head
                var headPos = new Vector3(0f, 0.28f, 0.58f);
                Raylib.DrawCube(headPos, 0.35f, 0.38f, 0.42f, skinTan);
                Raylib.DrawCubeWires(headPos, 0.352f, 0.382f, 0.422f, sheepOutline);
                Raylib.DrawCube(headPos + new Vector3(0f, 0.18f, -0.05f), 0.36f, 0.14f, 0.34f, woolWhite);

                // Eyes
                Raylib.DrawCube(headPos + new Vector3(-0.14f, 0.04f, 0.215f), 0.05f, 0.05f, 0.01f, ShadeColor(Color.Black, light, aPos));
                Raylib.DrawCube(headPos + new Vector3(0.14f, 0.04f, 0.215f), 0.05f, 0.05f, 0.01f, ShadeColor(Color.Black, light, aPos));

                // 4 Legs
                float sLegW = 0.16f, sLegH = 0.45f, sLegD = 0.16f;
                var sFL = new Vector3(-0.22f, -0.325f, 0.28f + walkSwing * 0.2f);
                var sFR = new Vector3(0.22f, -0.325f, 0.28f - walkSwing * 0.2f);
                var sBL = new Vector3(-0.22f, -0.325f, -0.28f - walkSwing * 0.2f);
                var sBR = new Vector3(0.22f, -0.325f, -0.28f + walkSwing * 0.2f);

                Raylib.DrawCube(sFL, sLegW, sLegH, sLegD, skinTan);
                Raylib.DrawCube(sFR, sLegW, sLegH, sLegD, skinTan);
                Raylib.DrawCube(sBL, sLegW, sLegH, sLegD, skinTan);
                Raylib.DrawCube(sBR, sLegW, sLegH, sLegD, skinTan);
            }

            Rlgl.PopMatrix();
        }

        // 3. Draw Hostile Mobs (Zombie, Creeper, Skeleton, Spider) with light and fog shading
        foreach (var h in _world.HostileMobs) {
            if (!h.Alive) continue;
            DrawSoftShadow(h.Position - new Vector3(0f, h.HalfSizeY, 0f), 0.45f);

            var light = GetLightFactor(h.Position);
            var hPos = h.Position;

            Vector3 fwd = new(0f, 0f, 1f);
            if (h.Velocity.LengthSquared() > 0.01f) {
                var dirH = Vector2.Normalize(new Vector2(h.Velocity.X, h.Velocity.Z));
                fwd = new Vector3(dirH.X, 0f, dirH.Y);
            }

            float angleDegrees = MathF.Atan2(fwd.X, fwd.Z) * 180f / MathF.PI;
            bool isMoving = new Vector2(h.Velocity.X, h.Velocity.Z).LengthSquared() > 0.01f;
            float walkSwing = isMoving ? MathF.Sin(time * 8f) * 0.4f : 0f;

            Rlgl.PushMatrix();
            Rlgl.Translatef(h.Position.X, h.Position.Y, h.Position.Z);
            Rlgl.Rotatef(angleDegrees, 0f, 1f, 0f);

            if (h.HurtTime > 0f) {
                float roll = MathF.Sin(h.HurtTime * MathF.PI * 10f) * 12f;
                Rlgl.Rotatef(roll, 0f, 0f, 1f);
            }

            if (h.Type == HostileType.Zombie) {
                // ── ZOMBIE MODEL ──────────────────────────────────────────
                var skinColor = ShadeColor(h.HurtTime > 0f ? new Color(220, 60, 60, 255) : new Color(45, 125, 45, 255), light, hPos);
                var shirtColor = ShadeColor(h.HurtTime > 0f ? new Color(180, 50, 50, 255) : new Color(40, 140, 160, 255), light, hPos);
                var pantsColor = ShadeColor(h.HurtTime > 0f ? new Color(140, 40, 40, 255) : new Color(40, 40, 110, 255), light, hPos);
                var outlineColor = ShadeColor(new Color(20, 20, 20, 180), light, hPos);

                // Body
                Raylib.DrawCube(new Vector3(0f, 0.15f, 0f), 0.6f, 0.70f, 0.35f, shirtColor);
                Raylib.DrawCubeWires(new Vector3(0f, 0.15f, 0f), 0.602f, 0.702f, 0.352f, outlineColor);

                // Head
                var headPos = new Vector3(0f, 0.74f, 0f);
                Raylib.DrawCube(headPos, 0.48f, 0.48f, 0.48f, skinColor);
                Raylib.DrawCubeWires(headPos, 0.482f, 0.482f, 0.482f, outlineColor);

                Raylib.DrawCube(headPos + new Vector3(-0.12f, 0.04f, 0.245f), 0.09f, 0.08f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0.12f, 0.04f, 0.245f), 0.09f, 0.08f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0f, -0.1f, 0.245f), 0.16f, 0.06f, 0.01f, ShadeColor(Color.Black, light, hPos));

                // Arms
                var leftArm = new Vector3(-0.42f, 0.35f, 0.25f + MathF.Sin(time * 4f) * 0.03f);
                var rightArm = new Vector3(0.42f, 0.35f, 0.25f - MathF.Sin(time * 4f) * 0.03f);
                Raylib.DrawCube(leftArm, 0.2f, 0.2f, 0.55f, skinColor);
                Raylib.DrawCubeWires(leftArm, 0.202f, 0.202f, 0.552f, outlineColor);
                Raylib.DrawCube(rightArm, 0.2f, 0.2f, 0.55f, skinColor);
                Raylib.DrawCubeWires(rightArm, 0.202f, 0.202f, 0.552f, outlineColor);

                // Legs
                var leftLeg = new Vector3(-0.16f, -0.525f, walkSwing * 0.3f);
                var rightLeg = new Vector3(0.16f, -0.525f, -walkSwing * 0.3f);
                Raylib.DrawCube(leftLeg, 0.22f, 0.65f, 0.22f, pantsColor);
                Raylib.DrawCubeWires(leftLeg, 0.222f, 0.652f, 0.222f, outlineColor);
                Raylib.DrawCube(rightLeg, 0.22f, 0.65f, 0.22f, pantsColor);
                Raylib.DrawCubeWires(rightLeg, 0.222f, 0.652f, 0.222f, outlineColor);

            } else if (h.Type == HostileType.Skeleton) {
                // ── SKELETON MODEL ────────────────────────────────────────
                var boneColor = ShadeColor(h.HurtTime > 0f ? new Color(220, 60, 60, 255) : new Color(205, 205, 205, 255), light, hPos);
                var ribColor = ShadeColor(h.HurtTime > 0f ? new Color(180, 40, 40, 255) : new Color(130, 130, 130, 255), light, hPos);
                var bowColor = ShadeColor(new Color(125, 80, 45, 255), light, hPos);
                var stringColor = ShadeColor(new Color(225, 225, 225, 255), light, hPos);
                var outlineColor = ShadeColor(new Color(30, 30, 30, 180), light, hPos);

                // Body
                Raylib.DrawCube(new Vector3(0f, 0.15f, 0f), 0.45f, 0.70f, 0.22f, boneColor);
                Raylib.DrawCubeWires(new Vector3(0f, 0.15f, 0f), 0.452f, 0.702f, 0.222f, outlineColor);

                // Ribcage ribs
                Raylib.DrawCube(new Vector3(0f, 0.28f, 0.115f), 0.36f, 0.05f, 0.01f, ribColor);
                Raylib.DrawCube(new Vector3(0f, 0.18f, 0.115f), 0.36f, 0.05f, 0.01f, ribColor);
                Raylib.DrawCube(new Vector3(0f, 0.08f, 0.115f), 0.36f, 0.05f, 0.01f, ribColor);

                // Head
                var headPos = new Vector3(0f, 0.72f, 0f);
                Raylib.DrawCube(headPos, 0.44f, 0.44f, 0.44f, boneColor);
                Raylib.DrawCubeWires(headPos, 0.442f, 0.442f, 0.442f, outlineColor);

                // Skull eyes
                Raylib.DrawCube(headPos + new Vector3(-0.10f, 0.04f, 0.225f), 0.09f, 0.09f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0.10f, 0.04f, 0.225f), 0.09f, 0.09f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0f, -0.09f, 0.225f), 0.12f, 0.05f, 0.01f, ShadeColor(Color.Black, light, hPos));

                // Arms
                var leftArm = new Vector3(-0.30f, 0.35f, 0.22f);
                var rightArm = new Vector3(0.30f, 0.35f, 0.22f);
                Raylib.DrawCube(leftArm, 0.12f, 0.12f, 0.50f, boneColor);
                Raylib.DrawCube(rightArm, 0.12f, 0.12f, 0.50f, boneColor);

                // Bow
                var bowPos = new Vector3(0.15f, 0.35f, 0.48f);
                Raylib.DrawCube(bowPos, 0.06f, 0.55f, 0.06f, bowColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, 0.24f, -0.06f), 0.05f, 0.12f, 0.06f, bowColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, -0.24f, -0.06f), 0.05f, 0.12f, 0.06f, bowColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, 0f, -0.08f), 0.02f, 0.52f, 0.02f, stringColor);

                // Legs
                var leftLeg = new Vector3(-0.12f, -0.525f, walkSwing * 0.3f);
                var rightLeg = new Vector3(0.12f, -0.525f, -walkSwing * 0.3f);
                Raylib.DrawCube(leftLeg, 0.14f, 0.65f, 0.14f, boneColor);
                Raylib.DrawCube(rightLeg, 0.14f, 0.65f, 0.14f, boneColor);

            } else if (h.Type == HostileType.Spider) {
                // ── SPIDER MODEL ──────────────────────────────────────────
                var spiderColor = ShadeColor(h.HurtTime > 0f ? new Color(220, 50, 50, 255) : new Color(35, 25, 20, 255), light, hPos);
                var eyeRed = ShadeColor(new Color(220, 20, 20, 255), Vector3.One, hPos); // Глаза паука

                // Body
                var headPos = new Vector3(0f, -0.05f, 0.30f);
                Raylib.DrawCube(headPos, 0.45f, 0.35f, 0.40f, spiderColor);
                var abdomenPos = new Vector3(0f, 0.05f, -0.30f);
                Raylib.DrawCube(abdomenPos, 0.65f, 0.50f, 0.65f, spiderColor);

                // Glowing Red Eyes
                Raylib.DrawCube(headPos + new Vector3(-0.12f, 0.02f, 0.205f), 0.06f, 0.06f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(0.12f, 0.02f, 0.205f), 0.06f, 0.06f, 0.01f, eyeRed);

                // 8 Legs
                for (int i = 0; i < 4; i++) {
                    float zOff = (i - 1.5f) * 0.22f;
                    float legSwing = MathF.Sin(time * 12f + i * 1.2f) * 0.15f;
                    var legL = new Vector3(-0.45f, -0.22f + legSwing, zOff);
                    var legR = new Vector3(0.45f, -0.22f - legSwing, zOff);
                    Raylib.DrawCube(legL, 0.55f, 0.08f, 0.08f, spiderColor);
                    Raylib.DrawCube(legR, 0.55f, 0.08f, 0.08f, spiderColor);
                }

            } else if (h.Type == HostileType.Creeper) {
                // ── CREEPER MODEL ─────────────────────────────────────────
                bool isFlashing = h.FuseTimer > 0f && (int)(h.FuseTimer * 12f) % 2 == 0;
                var baseCreeper = isFlashing ? Color.White : (h.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(30, 185, 55, 255));
                var creeperGreen = isFlashing ? ShadeColor(Color.White, light, hPos) : ShadeColor(baseCreeper, light, hPos);
                var outlineColor = ShadeColor(new Color(15, 60, 20, 180), light, hPos);

                float fuseScale = 1.0f + MathF.Min(0.25f, h.FuseTimer * 0.20f);
                Rlgl.Scalef(fuseScale, fuseScale, fuseScale);

                // Body
                Raylib.DrawCube(new Vector3(0f, -0.025f, 0f), 0.5f, 0.65f, 0.35f, creeperGreen);
                Raylib.DrawCubeWires(new Vector3(0f, -0.025f, 0f), 0.502f, 0.652f, 0.352f, outlineColor);

                // Head
                var headPos = new Vector3(0f, 0.54f, 0f);
                Raylib.DrawCube(headPos, 0.48f, 0.48f, 0.48f, creeperGreen);
                Raylib.DrawCubeWires(headPos, 0.482f, 0.482f, 0.482f, outlineColor);

                Raylib.DrawCube(headPos + new Vector3(-0.11f, 0.06f, 0.245f), 0.10f, 0.10f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0.11f, 0.06f, 0.245f), 0.10f, 0.10f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0f, -0.06f, 0.245f), 0.10f, 0.12f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(-0.07f, -0.12f, 0.245f), 0.06f, 0.12f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0.07f, -0.12f, 0.245f), 0.06f, 0.12f, 0.01f, ShadeColor(Color.Black, light, hPos));

                // 4 Legs
                var legFL = new Vector3(-0.16f, -0.55f, 0.22f + walkSwing * 0.25f);
                var legFR = new Vector3(0.16f, -0.55f, 0.22f - walkSwing * 0.25f);
                var legBL = new Vector3(-0.16f, -0.55f, -0.22f - walkSwing * 0.25f);
                var legBR = new Vector3(0.16f, -0.55f, -0.22f + walkSwing * 0.25f);

                float legW = 0.18f, legH = 0.40f, legD = 0.18f;
                Raylib.DrawCube(legFL, legW, legH, legD, creeperGreen);
                Raylib.DrawCubeWires(legFL, legW + 0.002f, legH + 0.002f, legD + 0.002f, outlineColor);
                Raylib.DrawCube(legFR, legW, legH, legD, creeperGreen);
                Raylib.DrawCubeWires(legFR, legW + 0.002f, legH + 0.002f, legD + 0.002f, outlineColor);
                Raylib.DrawCube(legBL, legW, legH, legD, creeperGreen);
                Raylib.DrawCubeWires(legBL, legW + 0.002f, legH + 0.002f, legD + 0.002f, outlineColor);
                Raylib.DrawCube(legBR, legW, legH, legD, creeperGreen);
                Raylib.DrawCubeWires(legBR, legW + 0.002f, legH + 0.002f, legD + 0.002f, outlineColor);
            }

            Rlgl.PopMatrix();
        }

        // 4. Draw Flying Arrows (стрелы скелетов) with lighting and fog
        foreach (var arr in _world.Arrows) {
            if (!arr.Alive) continue;
            var arrLight = GetLightFactor(arr.Position);
            var fwd = arr.Velocity.LengthSquared() > 0.01f ? Vector3.Normalize(arr.Velocity) : Vector3.UnitZ;
            Raylib.DrawCube(arr.Position, 0.06f, 0.06f, 0.55f, ShadeColor(new Color(175, 140, 95, 255), arrLight, arr.Position));
            Raylib.DrawCube(arr.Position + fwd * 0.26f, 0.10f, 0.10f, 0.10f, ShadeColor(new Color(190, 190, 190, 255), arrLight, arr.Position));
            Raylib.DrawCube(arr.Position - fwd * 0.24f, 0.12f, 0.12f, 0.12f, ShadeColor(new Color(245, 245, 245, 255), arrLight, arr.Position));
        }

        // 5. Falling blocks with lighting and fog
        foreach (var f in _world.FallingBlocks) {
            if (!f.Alive) continue;
            var fallLight = GetLightFactor(f.Position);
            var tint = ShadeColor(BlockTint(f.Block.Id), fallLight, f.Position);
            Raylib.DrawCube(f.Position, 1.0f, 1.0f, 1.0f, tint);
            Raylib.DrawCubeWires(f.Position, 1.002f, 1.002f, 1.002f, ShadeColor(new Color(20, 20, 25, 180), fallLight, f.Position));
        }
    }

    private void DrawSoftShadow(Vector3 pos, float baseRadius) {
        float fogFactor = GetFogFactor(pos);
        if (fogFactor >= 0.95f) return;
        float maxDistSq = (SaveSystem.RenderDistanceSetting * 16f) * (SaveSystem.RenderDistanceSetting * 16f);
        if (Vector3.DistanceSquared(pos, _session.Camera.Position) > maxDistSq) return;

        float groundY = pos.Y;
        int px = (int)MathF.Floor(pos.X);
        int pz = (int)MathF.Floor(pos.Z);
        int startY = (int)MathF.Floor(pos.Y);
        for (int y = startY; y >= startY - 3; y--) {
            if (_world.IsSolidAt(new Vec3i(px, y, pz))) {
                groundY = y + 1.01f;
                break;
            }
        }
        
        float dist = pos.Y - groundY;
        if (dist > 3.5f || dist < -0.2f) return;
        
        float scale = Math.Clamp(1.0f - dist / 3.5f, 0f, 1f);
        float radius = baseRadius * scale;
        if (radius < 0.05f) return;
        
        byte alpha = (byte)(70 * scale * (1f - fogFactor));
        if (alpha < 2) return;
        Raylib.DrawCircle3D(new Vector3(pos.X, groundY, pos.Z), radius, new Vector3(1, 0, 0), 90f, new Color((byte)0, (byte)0, (byte)0, alpha));
    }

    public void SpawnBlockParticles(Vec3i pos, ushort blockId) {
        Color baseCol = BlockTint(blockId);
        var center = new Vector3(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
        for (int i = 0; i < 16; i++) {
            float ox = (ParticleRng.NextSingle() - 0.5f) * 0.7f;
            float oy = (ParticleRng.NextSingle() - 0.5f) * 0.7f;
            float oz = (ParticleRng.NextSingle() - 0.5f) * 0.7f;

            float vx = (ParticleRng.NextSingle() - 0.5f) * 3.8f;
            float vy = 1.2f + ParticleRng.NextSingle() * 3.2f;
            float vz = (ParticleRng.NextSingle() - 0.5f) * 3.8f;

            int d = ParticleRng.Next(-22, 23);
            var col = new Color(
                (byte)Math.Clamp(baseCol.R + d, 0, 255),
                (byte)Math.Clamp(baseCol.G + d, 0, 255),
                (byte)Math.Clamp(baseCol.B + d, 0, 255),
                (byte)255);

            float size = 0.08f + ParticleRng.NextSingle() * 0.08f;
            float life = 0.50f + ParticleRng.NextSingle() * 0.45f;
            _particles.Add(new VoxelParticle {
                Position = center + new Vector3(ox, oy, oz),
                Velocity = new Vector3(vx, vy, vz),
                Color = col,
                Size = size,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = false
            });
        }
    }

    public void SpawnCritParticles(Vector3 pos, int count = 14) {
        for (int i = 0; i < count; i++) {
            float angle = ParticleRng.NextSingle() * MathF.Tau;
            float speed = 2.0f + ParticleRng.NextSingle() * 3.0f;
            float vx = MathF.Cos(angle) * speed;
            float vy = 1.5f + ParticleRng.NextSingle() * 2.5f;
            float vz = MathF.Sin(angle) * speed;

            var col = ParticleRng.NextDouble() < 0.5 ? new Color(255, 220, 60, 255) : new Color(255, 140, 30, 255);
            float size = 0.12f + ParticleRng.NextSingle() * 0.08f;
            float life = 0.40f + ParticleRng.NextSingle() * 0.30f;
            _particles.Add(new VoxelParticle {
                Position = pos + new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.3f, (ParticleRng.NextSingle() - 0.5f) * 0.3f, (ParticleRng.NextSingle() - 0.5f) * 0.3f),
                Velocity = new Vector3(vx, vy, vz),
                Color = col,
                Size = size,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = true
            });
        }
    }

    public void SpawnDustParticles(Vector3 pos, int count = 4) {
        for (int i = 0; i < count; i++) {
            float vx = (ParticleRng.NextSingle() - 0.5f) * 1.5f;
            float vy = 0.5f + ParticleRng.NextSingle() * 1.2f;
            float vz = (ParticleRng.NextSingle() - 0.5f) * 1.5f;
            float size = 0.08f + ParticleRng.NextSingle() * 0.06f;
            float life = 0.35f + ParticleRng.NextSingle() * 0.25f;
            _particles.Add(new VoxelParticle {
                Position = pos + new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.4f, 0.05f, (ParticleRng.NextSingle() - 0.5f) * 0.4f),
                Velocity = new Vector3(vx, vy, vz),
                Color = new Color(180, 160, 140, 180),
                Size = size,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = false
            });
        }
    }

    public void DrawParticles(float dt) {
        for (int i = _particles.Count - 1; i >= 0; i--) {
            var p = _particles[i];
            p.Lifetime -= dt;
            if (p.Lifetime <= 0f) {
                _particles.RemoveAt(i);
                continue;
            }

            p.Position += p.Velocity * dt;
            p.Velocity.Y -= 16f * dt;
            p.Velocity.X *= MathF.Exp(-2.0f * dt);
            p.Velocity.Z *= MathF.Exp(-2.0f * dt);
            _particles[i] = p;

            float alphaRatio = Math.Clamp(p.Lifetime / p.MaxLifetime, 0f, 1f);
            var col = new Color(p.Color.R, p.Color.G, p.Color.B, (byte)(p.Color.A * alphaRatio));

            if (p.IsCrit) {
                Raylib.BeginBlendMode(BlendMode.Additive);
                Raylib.DrawCube(p.Position, p.Size, p.Size, p.Size, col);
                Raylib.EndBlendMode();
            } else {
                var light = GetLightFactor(p.Position);
                Raylib.DrawCube(p.Position, p.Size, p.Size, p.Size, ShadeColor(col, light));
            }
        }
    }

    private static Color BlockTint(ushort blockId) => blockId switch {
        var id when id == GameData.BDirt.Id => new Color(134, 96, 58, 255),
        var id when id == GameData.BGrass.Id => new Color(122, 92, 58, 255),
        var id when id == GameData.BStone.Id || id == GameData.BCobblestone.Id => new Color(128, 128, 132, 255),
        var id when id == GameData.BLog.Id => new Color(98, 70, 42, 255),
        var id when id == GameData.BPlanks.Id => new Color(168, 132, 82, 255),
        var id when id == GameData.BLeaves.Id => new Color(52, 118, 40, 255),
        var id when id == GameData.BCoalOre.Id => new Color(80, 80, 85, 255),
        var id when id == GameData.BIronOre.Id => new Color(185, 145, 115, 255),
        var id when id == GameData.BGoldOre.Id => new Color(245, 215, 60, 255),
        var id when id == GameData.BDiamondOre.Id => new Color(90, 230, 240, 255),
        var id when id == GameData.BRedstoneOre.Id => new Color(230, 40, 30, 255),
        var id when id == GameData.BSand.Id => new Color(220, 210, 155, 255),
        var id when id == GameData.BGravel.Id => new Color(130, 125, 125, 255),
        var id when id == GameData.BWater.Id => new Color(50, 100, 220, 200),
        var id when id == GameData.BLava.Id => new Color(245, 90, 20, 255),
        _ => new Color(150, 150, 155, 255),
    };

    public void Dispose() {
        _world.OnBlockRemoved -= SpawnBlockParticles;
        _world.OnDustSpawned -= SpawnDustParticles;
        _world.OnCritSpawned -= SpawnCritParticles;
        if (_materialReady) {
            unsafe {
                _material.Maps[(int)MaterialMapIndex.Albedo].Texture = default;
            }
            Raylib.UnloadShader(_material.Shader);
            Raylib.UnloadMaterial(_material);
        }
        _materialReady = false;
    }
}
