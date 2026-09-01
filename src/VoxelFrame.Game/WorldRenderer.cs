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
    private GameWorld _world => _session.World;
    private Dimension _lastDimension = Dimension.Overworld;
    private readonly List<GameChunk> _rebuildQueue = new();
    private readonly List<GameChunk> _drainBuf = new();
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
        uniform float time;
        uniform int dimension;
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

            // Динамический свет от факела в руке игрока (сила 14/15, как у настенного факела)
            if (playerLightRadius > 0.1) {
                float d = distance(fragWorldPos, playerLightPos);
                if (d < playerLightRadius) {
                    float handLight = clamp(1.0 - (d / playerLightRadius), 0.0, 1.0);
                    block = max(block, handLight * (14.0 / 15.0));
                }
            }

            // Динамический свет от взрывающегося бабахера
            if (creeperLightRadius > 0.1) {
                float d = distance(fragWorldPos, creeperLightPos);
                if (d < creeperLightRadius) {
                    float creepLight = clamp(1.0 - (d / creeperLightRadius), 0.0, 1.0);
                    block = max(block, creepLight * 1.0);
                }
            }

            // Нелинейная гамма-кривая освещенности
            float sunCurve = pow(sun * max(skyFactor, 0.04), 1.28);
            float blockCurve = pow(block, 1.20);

            // Суточная цветовая температура солнца / луны (Golden hour на рассвете/закате и лунное серебро ночью)
            float horizonGlow = clamp(1.0 - abs(sin(sunAngle)) * 2.8, 0.0, 1.0);
            vec3 daySunCol = vec3(1.0, 0.98, 0.92);
            vec3 goldenHourCol = vec3(1.0, 0.65, 0.38);
            vec3 nightMoonCol = vec3(0.55, 0.70, 1.0);

            vec3 sunLightColor = mix(daySunCol, goldenHourCol, horizonGlow * smoothstep(0.1, 0.6, skyFactor));
            sunLightColor = mix(nightMoonCol, sunLightColor, smoothstep(0.08, 0.25, skyFactor));

            vec3 torchColor = vec3(1.0, 0.82, 0.52);
            vec3 ambientColor = mix(vec3(0.06, 0.08, 0.14), vec3(0.14, 0.14, 0.16), skyFactor);

            if (dimension == 1) { // Nether
                ambientColor = vec3(0.20, 0.06, 0.06);
                torchColor = vec3(1.0, 0.72, 0.40);
            } else if (dimension == 2) { // End
                ambientColor = vec3(0.12, 0.08, 0.18);
                sunLightColor = vec3(0.85, 0.75, 1.0);
            }

            vec3 totalLight = (sunLightColor * sunCurve + torchColor * blockCurve + ambientColor) * faceDir;
            totalLight = clamp(totalLight, 0.06, 1.0);

            // Легкий процедурный блик/каустика на воде на верхних гранях
            if (faceDir > 0.95 && texelColor.a < 0.98) {
                float wave = sin(fragWorldPos.x * 2.5 + time * 2.0) * cos(fragWorldPos.z * 2.5 + time * 2.0);
                totalLight += vec3(0.06, 0.08, 0.10) * clamp(wave, 0.0, 1.0) * skyFactor;
            }

            vec4 lightedColor = texelColor * colDiffuse * vec4(totalLight, 1.0);

            // Плавный дистанционный туман
            float dist = distance(fragWorldPos, cameraPos);
            float fogFactor = clamp((dist - fogStart) / max(fogEnd - fogStart, 0.01), 0.0, 1.0);
            fogFactor = smoothstep(0.0, 1.0, fogFactor);

            finalColor = vec4(mix(lightedColor.rgb, fogColor, fogFactor), lightedColor.a);
        }
        """;

    private int _skyFactorLoc = -1;
    private int _sunAngleLoc = -1;
    private int _timeLoc = -1;
    private int _dimensionLoc = -1;
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
        public bool IsHealBeam; // летит к Target без гравитации
        public Vector3 Target;
    }

    private readonly List<VoxelParticle> _particles = new();
    private readonly List<(GameChunk Chunk, int DistSq)> _translucentChunks = new();

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
        _lastDimension = session.Dimension;
        _material = Raylib.LoadMaterialDefault();
        unsafe {
            _material.Maps[(int)MaterialMapIndex.Albedo].Texture = TextureAtlas.Atlas;
        }
        var shader = Raylib.LoadShaderFromMemory(VertexShaderSrc, FragmentShaderSrc);
        if (Raylib.IsShaderValid(shader)) {
            _material.Shader = shader;
            _skyFactorLoc = Raylib.GetShaderLocation(shader, "skyFactor");
            _sunAngleLoc = Raylib.GetShaderLocation(shader, "sunAngle");
            _timeLoc = Raylib.GetShaderLocation(shader, "time");
            _dimensionLoc = Raylib.GetShaderLocation(shader, "dimension");
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

        HookWorldEvents(_world);
    }

    private void HookWorldEvents(GameWorld world) {
        world.OnBlockRemoved += SpawnBlockParticles;
        world.OnDustSpawned += SpawnDustParticles;
        world.OnCritSpawned += SpawnCritParticles;
        world.OnEatParticlesSpawned += SpawnEatParticles;
        world.OnHealBeamSpawned += SpawnHealBeamParticles;
    }

    public int MeshQueueCount => _rebuildQueue.Count;

    public void ProcessMeshQueue() {
        if (_lastDimension != _world.Dimension) {
            _lastDimension = _world.Dimension;
            _rebuildQueue.Clear();
            _drainBuf.Clear();
            HookWorldEvents(_world);
            foreach (var gc in _world.Chunks) {
                gc.MeshDirty = true;
                _rebuildQueue.Add(gc);
            }
        }

        _world.CollectMeshDirty(_drainBuf);
        if (_drainBuf.Count > 0) {
            foreach (var gc in _drainBuf) {
                if (!_rebuildQueue.Contains(gc)) _rebuildQueue.Add(gc);
            }
            _drainBuf.Clear();
            var playerPos = _session.Player.Position;
            int pcx = (int)MathF.Floor(playerPos.X / Chunk.SizeX);
            int pcz = (int)MathF.Floor(playerPos.Z / Chunk.SizeZ);
            _rebuildQueue.Sort((a, b) => {
                int distA = Math.Abs(a.Coord.X - pcx) + Math.Abs(a.Coord.Z - pcz) + Math.Abs(a.Coord.Y - 1);
                int distB = Math.Abs(b.Coord.X - pcx) + Math.Abs(b.Coord.Z - pcz) + Math.Abs(b.Coord.Y - 1);
                return distA.CompareTo(distB);
            });
        }
        int budget = _session.Ui == UiState.Loading ? 16 : 4;
        while (_rebuildQueue.Count > 0 && budget-- > 0) {
            var gc = _rebuildQueue[0];
            _rebuildQueue.RemoveAt(0);
            gc.UnloadMesh();
            var (opaque, trans) = ChunkMesher.Build(gc, _world);
            gc.Meshes.AddRange(opaque);
            gc.TranslucentMeshes.AddRange(trans);
            gc.MeshUploaded = gc.Meshes.Count > 0 || gc.TranslucentMeshes.Count > 0;
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
        if (_timeLoc != -1) {
            float time = (float)_session.TotalPlaySeconds;
            unsafe { Raylib.SetShaderValue(_material.Shader, _timeLoc, &time, ShaderUniformDataType.Float); }
        }
        if (_dimensionLoc != -1) {
            int dim = (int)_world.Dimension;
            unsafe { Raylib.SetShaderValue(_material.Shader, _dimensionLoc, &dim, ShaderUniformDataType.Int); }
        }

        bool holdingTorch = _session.Player.SelectedEntry?.Item.Definition.Id == GameData.TorchItem.Id || _session.Player.OffhandItem?.Id == GameData.TorchItem.Id;
        Vector3 lightPos = _session.Player.Eye;
        float lightRadius = (SaveSystem.DynamicLighting && holdingTorch) ? 14.0f : 0f;
        if (_playerLightPosLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _playerLightPosLoc, &lightPos, ShaderUniformDataType.Vec3); }
        }
        if (_playerLightRadiusLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _playerLightRadiusLoc, &lightRadius, ShaderUniformDataType.Float); }
        }

        // Динамический свет раздувающегося белого бабахера перед взрывом
        Vector3 creeperLightPos = Vector3.Zero;
        float creeperLightRadius = 0f;
        if (SaveSystem.DynamicLighting) {
            foreach (var h in _world.HostileMobs) {
                if (h.Type == HostileType.Babakher && h.FuseTimer > 0f && h.Alive) {
                    float rad = 6.0f + (h.FuseTimer / 1.3f) * 6.5f; // Свечение от 6 до 12.5 блоков
                    if (rad > creeperLightRadius) {
                        creeperLightRadius = rad;
                        creeperLightPos = h.Position + new Vector3(0f, 0.5f, 0f);
                    }
                }
            }
        }

        if (_creeperLightPosLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _creeperLightPosLoc, &creeperLightPos, ShaderUniformDataType.Vec3); }
        }
        if (_creeperLightRadiusLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _creeperLightRadiusLoc, &creeperLightRadius, ShaderUniformDataType.Float); }
        }

        var camPos = _session.Camera.Position;
        if (_cameraPosLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _cameraPosLoc, &camPos, ShaderUniformDataType.Vec3); }
        }

        // Подводный / лавовый / атмосферный туман
        var camCell = new Vec3i((int)MathF.Floor(camPos.X), (int)MathF.Floor(camPos.Y), (int)MathF.Floor(camPos.Z));
        var camVoxel = _world.GetVoxel(camCell);
        bool isWater = camVoxel.TypeId == GameData.BWater.Id;
        bool isLava = camVoxel.TypeId == GameData.BLava.Id;

        Vector3 fogVec;
        float fogStart, fogEnd;

        if (isWater) {
            fogVec = new Vector3(0.04f, 0.16f, 0.36f); // Глубокий лазурно-синий подводный туман
            fogStart = 1.0f;
            fogEnd = (SaveSystem.GraphicsQuality == SaveSystem.GraphicsPreset.Fast) ? 12.0f : 20.0f;
        } else if (isLava) {
            fogVec = new Vector3(0.65f, 0.10f, 0.02f); // Густой раскалённо-красный туман лавы
            fogStart = 0.1f;
            fogEnd = 3.5f;
        } else {
            Color fogC = GetFogColor();
            fogVec = new Vector3(fogC.R / 255f, fogC.G / 255f, fogC.B / 255f);
            float maxRenderDist = SaveSystem.RenderDistanceSetting * (float)Chunk.SizeX;
            bool nether = _world.Dimension == Dimension.Nether;
            bool end = _world.Dimension == Dimension.End;
            fogStart = nether ? 20.0f : end ? 40.0f : MathF.Max(15.0f, maxRenderDist * 0.55f);
            fogEnd = nether ? 68.0f : end ? 140.0f : MathF.Max(28.0f, maxRenderDist * 0.96f);
        }

        if (_fogColorLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _fogColorLoc, &fogVec, ShaderUniformDataType.Vec3); }
        }
        if (_fogStartLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _fogStartLoc, &fogStart, ShaderUniformDataType.Float); }
        }
        if (_fogEndLoc != -1) {
            unsafe { Raylib.SetShaderValue(_material.Shader, _fogEndLoc, &fogEnd, ShaderUniformDataType.Float); }
        }

        unsafe {
            Rlgl.EnableBackfaceCulling();
            var playerPos = _session.Player.Position;
            int pcx = (int)MathF.Floor(playerPos.X / Chunk.SizeX);
            int pcz = (int)MathF.Floor(playerPos.Z / Chunk.SizeZ);
            int rDist = SaveSystem.RenderDistanceSetting;
            int maxDistSq = (rDist + 1) * (rDist + 1);
            int unloadDistSq = (rDist + 3) * (rDist + 3);

            const float chunkRadius = 27.8f; // Радиус описанной сферы для 32×32×32 чанка
            var camFwd = _session.Player.Forward;
            float currentFov = _session.Camera.FovY;
            float aspect = (float)Raylib.GetScreenWidth() / Math.Max(1, Raylib.GetScreenHeight());
            float halfFovRad = currentFov * 0.5f * (MathF.PI / 180f);
            float tanFov = MathF.Tan(halfFovRad) * MathF.Max(1.0f, aspect) * 1.55f; // Динамический запас конуса под любой FOV и ультраширокие мониторы

            _translucentChunks.Clear();
            foreach (var gc in _world.Chunks) {
                int dx = gc.Coord.X - pcx;
                int dz = gc.Coord.Z - pcz;
                int dSq = dx * dx + dz * dz;

                // Выгружаем меши далеких чанков из видеопамяти (VRAM cleanup)
                if (dSq > unloadDistSq && gc.MeshUploaded) {
                    gc.UnloadMesh();
                    continue;
                }

                if (!gc.MeshUploaded || dSq > maxDistSq) continue;

                // Математически точный Frustum Culling по описанной сфере чанка (без мерцания по краям)
                if (dSq > 4) {
                    var chunkCenter = new Vector3((gc.Coord.X + 0.5f) * Chunk.SizeX, (gc.Coord.Y + 0.5f) * Chunk.SizeY, (gc.Coord.Z + 0.5f) * Chunk.SizeZ);
                    var toChunk = chunkCenter - camPos;
                    float proj = Vector3.Dot(toChunk, camFwd);
                    if (proj < -chunkRadius) continue; // Чанк полностью позади камеры

                    float perpDistSq = toChunk.LengthSquared() - (proj * proj);
                    float maxConeRadius = MathF.Max(0f, proj) * tanFov + chunkRadius;
                    if (perpDistSq > maxConeRadius * maxConeRadius) continue; // Чанк вне сектора обзора
                }

                foreach (var m in gc.Meshes)
                    Raylib.DrawMesh(m, _material, Matrix4x4.Identity);

                if (gc.TranslucentMeshes.Count > 0) {
                    _translucentChunks.Add((gc, dSq));
                }
            }
        }
    }

    public void DrawWorldOpaque() => DrawWorld();

    /// <summary>
    /// Отрисовка полупрозрачного мира (вода, стекло, порталы, растительность).
    /// Вызывается ПОСЛЕ отрисовки сущностей и декораций (факелов), чтобы вода и порталы
    /// накладывались поверх них прозрачным слоем с корректным Z-тестом (без просвета сквозь воду).
    /// </summary>
    public void DrawWorldTranslucent() {
        if (!_materialReady || _translucentChunks.Count == 0) return;
        unsafe {
            // Сортировка полупрозрачных мешей (вода, лёд, стекло, порталы) от дальних к ближним
            if (_translucentChunks.Count > 1) {
                _translucentChunks.Sort(static (a, b) => b.DistSq.CompareTo(a.DistSq));
            }

            Rlgl.EnableDepthTest();
            Rlgl.DisableDepthMask();
            foreach (var (gc, _) in _translucentChunks) {
                foreach (var m in gc.TranslucentMeshes)
                    Raylib.DrawMesh(m, _material, Matrix4x4.Identity);
            }
            Rlgl.EnableDepthMask();
        }
    }

    // ── Небо и Погода ────────────────────────────────────────────────────────

    /// <summary>Фоновый градиент неба (2D купол с горизонтом и рассветными оттенками).</summary>
    public void DrawSky() {
        if (_world.Dimension == Dimension.Nether) {
            Raylib.ClearBackground(new Color(45, 10, 10, 255));
            return;
        }
        if (_world.Dimension == Dimension.End) {
            Raylib.ClearBackground(new Color(10, 6, 20, 255)); // Пурпурно-чёрная пустота Энда
            return;
        }
        float f = _session.DayNight.SkyFactor;
        if (_session.Weather != WeatherType.Clear) {
            f *= 0.45f; // Пасмурное грозовое небо
        }
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();

        // Базовые цвета неба (день/ночь)
        var top = LerpColor(C(6, 8, 22, 255), C(85, 145, 235, 255), f);
        var bottom = LerpColor(C(14, 16, 36, 255), C(175, 205, 245, 255), f);

        // Рассветный / закатный золотистый оттенок у горизонта
        float u = _session.DayNight.TimeOfDay;
        float sunAngle = 2f * MathF.PI * (u - 0.25f);
        float horizonGlow = Math.Clamp(1.0f - MathF.Abs(MathF.Sin(sunAngle)) * 2.8f, 0f, 1f);
        if (horizonGlow > 0f && _session.Weather == WeatherType.Clear) {
            var sunsetCol = C(245, 130, 65, 255);
            bottom = LerpColor(bottom, sunsetCol, horizonGlow * 0.75f);
        }

        Raylib.DrawRectangleGradientV(0, 0, w, h, top, bottom);
    }

    /// <summary>3D Небесные светила (Солнце, Луна, звёзды — билборды без записи в Z-буфер, не режущие облака).</summary>
    public void Draw3DSky(Camera3D camera) {
        if (_world.Dimension == Dimension.Nether) return;
        if (_world.Dimension == Dimension.End) return; // в Энде нет солнца/луны — только пустота
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

        // 1. Звёзды — мягко мерцают
        if (f < 0.65f) {
            float starAlpha = Math.Clamp((0.65f - f) / 0.65f, 0f, 1f);
            var starTex = SkyTextures.Star;
            var starSrc = new Rectangle(0, 0, 32, 32);
            for (int i = 0; i < StarPositions.Length; i++) {
                var s = StarPositions[i];
                float twinkle = MathF.Sin(time * 2.5f + i * 1.7f) * 0.25f + 0.75f;
                float size = 0.9f + (i % 4) * 0.35f;
                byte a = (byte)(starAlpha * (190 + (i % 3) * 22) * twinkle);
                var starColor = C(245, 245, 255, (int)a);
                Vector3 starWorld = camera.Position + s * dist;
                DrawCelestialBillboard(starTex, starSrc, starWorld, camRight, camUp, size, size, starColor);
            }
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

    /// <summary>Воксельные объемные облака (Minecraft Alpha/Modern style без внутренних стенок и z-fighting). Только в Обычном мире.</summary>
    public void DrawClouds(Camera3D camera) {
        if (SaveSystem.CloudsMode == 0) return;
        if (_world.Dimension != Dimension.Overworld) return; // в Энде и Незере облаков нет

        float time = (float)Raylib.GetTime();
        float cloudY = 112f;
        var pPos = _session.Player.Position;

        const int step = 16;
        bool fancy = SaveSystem.CloudsMode == 2;
        int gridR = fancy ? (SaveSystem.GraphicsQuality == SaveSystem.GraphicsPreset.Fabulous ? 16 : 12) : 8;
        int size = gridR * 2 + 1;
        int baseX = (int)MathF.Floor(pPos.X / step) * step;
        int baseZ = (int)MathF.Floor(pPos.Z / step) * step;

        float wind = time * 2.2f;
        float height = fancy ? 4.0f : 0.8f;
        float y0 = cloudY;
        float y1 = cloudY + height;

        // Расчет суточного освещения облаков
        float sky = _session.DayNight?.SkyFactor ?? 1f;
        byte cR = (byte)Math.Clamp((int)(245 * (0.25f + 0.75f * sky)), 45, 255);
        byte cG = (byte)Math.Clamp((int)(248 * (0.28f + 0.72f * sky)), 50, 255);
        byte cB = (byte)Math.Clamp((int)(255 * (0.35f + 0.65f * sky)), 65, 255);
        byte alpha = (byte)(fancy ? 215 : 185);

        Color colTop = new(cR, cG, cB, alpha);
        Color colBottom = new((byte)(cR * 0.78f), (byte)(cG * 0.78f), (byte)(cB * 0.80f), alpha);
        Color colSideZ = new((byte)(cR * 0.85f), (byte)(cG * 0.85f), (byte)(cB * 0.88f), alpha);
        Color colSideX = new((byte)(cR * 0.90f), (byte)(cG * 0.90f), (byte)(cB * 0.92f), alpha);

        // 1. Построение маски плотности облаков
        Span<bool> grid = stackalloc bool[size * size];
        for (int gx = 0; gx < size; gx++) {
            int x = gx - gridR;
            float wx = baseX + x * step;
            for (int gz = 0; gz < size; gz++) {
                int z = gz - gridR;
                if (x * x + z * z > gridR * gridR) {
                    grid[gx * size + gz] = false;
                    continue;
                }
                float wz = baseZ + z * step;
                float nx = (wx + wind) * 0.0035f;
                float nz = wz * 0.0035f;
                float n = MathF.Sin(nx * 3.14159f) * MathF.Cos(nz * 3.14159f)
                        + MathF.Sin((nx + nz) * 2.1f) * 0.42f;
                grid[gx * size + gz] = n > 0.16f;
            }
        }

        // 2. Отрисовка внешней оболочки облаков (только внешние видимые грани)
        var shapesTex = Raylib.GetShapesTexture();
        var shapesRect = Raylib.GetShapesTextureRectangle();
        float u = (shapesRect.X + shapesRect.Width * 0.5f) / Math.Max(1, shapesTex.Width);
        float v = (shapesRect.Y + shapesRect.Height * 0.5f) / Math.Max(1, shapesTex.Height);

        Rlgl.SetTexture(shapesTex.Id);
        Rlgl.Begin((int)DrawMode.Quads);
        Rlgl.TexCoord2f(u, v);

        for (int gx = 0; gx < size; gx++) {
            int x = gx - gridR;
            float wx = baseX + x * step;
            float x0 = wx;
            float x1 = wx + step;

            for (int gz = 0; gz < size; gz++) {
                if (!grid[gx * size + gz]) continue;

                int z = gz - gridR;
                float wz = baseZ + z * step;
                float z0 = wz;
                float z1 = wz + step;

                bool hasN = gz > 0 && grid[gx * size + (gz - 1)];
                bool hasS = gz < size - 1 && grid[gx * size + (gz + 1)];
                bool hasW = gx > 0 && grid[(gx - 1) * size + gz];
                bool hasE = gx < size - 1 && grid[(gx + 1) * size + gz];

                // Нижняя грань (-Y) — смотрит на землю
                Rlgl.Color4ub(colBottom.R, colBottom.G, colBottom.B, colBottom.A);
                Rlgl.TexCoord2f(u, v);
                Rlgl.Vertex3f(x0, y0, z0);
                Rlgl.TexCoord2f(u, v);
                Rlgl.Vertex3f(x1, y0, z0);
                Rlgl.TexCoord2f(u, v);
                Rlgl.Vertex3f(x1, y0, z1);
                Rlgl.TexCoord2f(u, v);
                Rlgl.Vertex3f(x0, y0, z1);

                // Верхняя грань (+Y) — смотрит в космос
                if (fancy || camera.Position.Y > y0) {
                    Rlgl.Color4ub(colTop.R, colTop.G, colTop.B, colTop.A);
                    Rlgl.TexCoord2f(u, v);
                    Rlgl.Vertex3f(x0, y1, z1);
                    Rlgl.TexCoord2f(u, v);
                    Rlgl.Vertex3f(x1, y1, z1);
                    Rlgl.TexCoord2f(u, v);
                    Rlgl.Vertex3f(x1, y1, z0);
                    Rlgl.TexCoord2f(u, v);
                    Rlgl.Vertex3f(x0, y1, z0);
                }

                // Боковые грани (только если соседний блок — воздух!)
                if (fancy) {
                    // Север (-Z)
                    if (!hasN) {
                        Rlgl.Color4ub(colSideZ.R, colSideZ.G, colSideZ.B, colSideZ.A);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x0, y1, z0);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x1, y1, z0);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x1, y0, z0);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x0, y0, z0);
                    }
                    // Юг (+Z)
                    if (!hasS) {
                        Rlgl.Color4ub(colSideZ.R, colSideZ.G, colSideZ.B, colSideZ.A);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x1, y1, z1);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x0, y1, z1);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x0, y0, z1);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x1, y0, z1);
                    }
                    // Запад (-X)
                    if (!hasW) {
                        Rlgl.Color4ub(colSideX.R, colSideX.G, colSideX.B, colSideX.A);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x0, y1, z1);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x0, y1, z0);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x0, y0, z0);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x0, y0, z1);
                    }
                    // Восток (+X)
                    if (!hasE) {
                        Rlgl.Color4ub(colSideX.R, colSideX.G, colSideX.B, colSideX.A);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x1, y1, z0);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x1, y1, z1);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x1, y0, z1);
                        Rlgl.TexCoord2f(u, v);
                        Rlgl.Vertex3f(x1, y0, z0);
                    }
                }
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
            int surf = _world.Generator.SurfaceHeight(dx, dz);
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
                    byte torchFacing = v.SubGridLayerMask; // 0=Floor, 1=Wall on +X (tilts -X), 2=Wall on -X (tilts +X), 3=Wall on +Z (tilts -Z), 4=Wall on -Z (tilts +Z)
                    var woodCol = ShadeColor(new Color(130, 90, 48, 255), light, p);
                    var headCol = ShadeColor(new Color(55, 42, 38, 255), light, p);

                    Rlgl.PushMatrix();
                    Vector3 flamePos;

                    if (torchFacing == 1) { // Стена справа (+X), наклон влево (-X)
                        Rlgl.Translatef(pos.X + 0.82f, pos.Y + 0.32f, pos.Z + 0.5f);
                        Rlgl.Rotatef(28f, 0f, 0f, 1f);
                        flamePos = new Vector3(pos.X + 0.62f, pos.Y + 0.65f, pos.Z + 0.5f);
                    } else if (torchFacing == 2) { // Стена слева (-X), наклон вправо (+X)
                        Rlgl.Translatef(pos.X + 0.18f, pos.Y + 0.32f, pos.Z + 0.5f);
                        Rlgl.Rotatef(-28f, 0f, 0f, 1f);
                        flamePos = new Vector3(pos.X + 0.38f, pos.Y + 0.65f, pos.Z + 0.5f);
                    } else if (torchFacing == 3) { // Стена сзади (+Z), наклон вперед (-Z)
                        Rlgl.Translatef(pos.X + 0.5f, pos.Y + 0.32f, pos.Z + 0.82f);
                        Rlgl.Rotatef(-28f, 1f, 0f, 0f);
                        flamePos = new Vector3(pos.X + 0.5f, pos.Y + 0.65f, pos.Z + 0.62f);
                    } else if (torchFacing == 4) { // Стена спереди (-Z), наклон назад (+Z)
                        Rlgl.Translatef(pos.X + 0.5f, pos.Y + 0.32f, pos.Z + 0.18f);
                        Rlgl.Rotatef(28f, 1f, 0f, 0f);
                        flamePos = new Vector3(pos.X + 0.5f, pos.Y + 0.65f, pos.Z + 0.38f);
                    } else { // На полу
                        Rlgl.Translatef(pos.X + 0.5f, pos.Y + 0.25f, pos.Z + 0.5f);
                        flamePos = new Vector3(pos.X + 0.5f, pos.Y + 0.52f, pos.Z + 0.5f);
                    }

                    // Палочка факела
                    Raylib.DrawCube(Vector3.Zero, 0.10f, 0.50f, 0.10f, woodCol);
                    // Тлеющая головка с углём
                    Raylib.DrawCube(new Vector3(0f, 0.22f, 0f), 0.12f, 0.10f, 0.12f, headCol);
                    Rlgl.PopMatrix();

                    DrawFlame(flamePos, 0.24f, dt);
                } else if (v.TypeId == GameData.BEndPortalFrame.Id && (v.SubGridLayerMask & 1) != 0) {
                    // В рамку вставлено око Эндера — рисуем зелёный самоцвет на её верхней грани
                    var gemPos = new Vector3(pos.X + 0.5f, pos.Y + 1.14f, pos.Z + 0.5f);
                    var gemCol = ShadeColor(new Color(120, 235, 140, 255), light, p);
                    Raylib.DrawCube(gemPos, 0.46f, 0.30f, 0.46f, gemCol);
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
            var v = _world.GetVoxel(t);
            if (v.TypeId != 0) {
                var p = new Vector3(t.X + 0.5f, t.Y + 0.5f, t.Z + 0.5f);
                Raylib.DrawCubeWires(p, 1.005f, 1.005f, 1.005f, Color.Black);
            }
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

        // Динамический свет факела в руке игрока (радиус и сила 14, как у настенного факела)
        bool holdingTorch = _session.Player.SelectedEntry?.Item.Definition.Id == GameData.TorchItem.Id || _session.Player.OffhandItem?.Id == GameData.TorchItem.Id;
        if (holdingTorch) {
            float d = Vector3.Distance(pos, _session.Player.Eye);
            if (d < 14.0f) {
                byte dynBlock = (byte)(14 * (1.0f - d / 14.0f));
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
            : _world.Dimension == Dimension.End
                ? new Color(24, 12, 44, 255)
                : LerpColor(C(16, 18, 40, 255), C(178, 208, 244, 255), f);
    }

    public float GetFogFactor(Vector3 pos) {
        float dist = Vector3.Distance(_session.Camera.Position, pos);
        float maxRenderDist = SaveSystem.RenderDistanceSetting * (float)Chunk.SizeX;
        bool nether = _world.Dimension == Dimension.Nether;
        bool end = _world.Dimension == Dimension.End;
        float fogStart = nether ? 20.0f : end ? 40.0f : MathF.Max(15.0f, maxRenderDist * 0.60f);
        float fogEnd = nether ? 68.0f : end ? 140.0f : MathF.Max(28.0f, maxRenderDist * 0.96f);
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

    private void DrawRemotePlayers(Camera3D camera, float time) {
        // Если мы клиент — рисуем игроков, полученных от сервера
        if (GameClient.Active != null) {
            foreach (var rp in GameClient.Active.RemotePlayers) {
                if (rp.Dimension != _session.World.Dimension) continue;
                DrawSinglePlayerModel(camera, time, rp.Name, rp.Position, rp.Yaw, rp.Pitch, rp.IsMoving, rp.IsSneaking, rp.Health, rp.SelectedItemId, rp.OffhandItemId, rp.HelmetId, rp.ChestplateId, rp.LeggingsId, rp.BootsId, rp.ArmSwingTimer, rp.HurtTimer, rp.SkinName, rp.IsBlocking);
            }
        }
        // Если мы сервер/хост — рисуем подключившихся клиентов
        else if (GameServer.Active != null) {
            foreach (var cl in GameServer.Active.Clients) {
                if (cl.Dimension != _session.World.Dimension) continue;
                DrawSinglePlayerModel(camera, time, cl.Name, cl.Position, cl.Yaw, cl.Pitch, cl.IsMoving, cl.IsSneaking, cl.Health, cl.SelectedItemId, cl.OffhandItemId, cl.HelmetId, cl.ChestplateId, cl.LeggingsId, cl.BootsId, cl.ArmSwingTimer, cl.HurtTimer, cl.SkinName, cl.IsBlocking);
            }
        }
    }

    private static Color? GetArmorMaterialColor(int itemId) {
        if (itemId <= 0) return null;
        if (itemId == GameData.LeatherHelmetItem.Id || itemId == GameData.LeatherChestplateItem.Id || itemId == GameData.LeatherLeggingsItem.Id || itemId == GameData.LeatherBootsItem.Id)
            return new Color(160, 95, 55, 255); // Кожа
        if (itemId == GameData.IronHelmetItem.Id || itemId == GameData.IronChestplateItem.Id || itemId == GameData.IronLeggingsItem.Id || itemId == GameData.IronBootsItem.Id)
            return new Color(220, 220, 225, 255); // Железо
        if (itemId == GameData.DiamondHelmetItem.Id || itemId == GameData.DiamondChestplateItem.Id || itemId == GameData.DiamondLeggingsItem.Id || itemId == GameData.DiamondBootsItem.Id)
            return new Color(75, 225, 235, 255); // Алмаз
        return null;
    }

    private void DrawSinglePlayerModel(Camera3D camera, float time, string name, Vector3 pos, float yaw, float pitch, bool isMoving, bool isSneaking, float health, int selectedItemId, int offhandItemId, int helmetId, int chestId, int legsId, int bootsId, float armSwingTimer, float hurtTimer, string skinName, bool isBlocking = false) {
        DrawSoftShadow(pos - new Vector3(0f, 0.9f, 0f), 0.45f);
        var light = GetLightFactor(pos);
        var skinDef = SkinSystem.GetSkin(skinName);

        float walkSwing = isMoving ? MathF.Sin(time * 10f) * 0.45f : 0f;
        float armPunch = armSwingTimer > 0f ? MathF.Sin(armSwingTimer * MathF.PI) * 0.8f : 0f;

        var skinColor = ShadeColor(hurtTimer > 0f ? new Color(240, 80, 80, 255) : skinDef.SkinColor, light, pos);
        var hairColor = ShadeColor(skinDef.HairColor, light, pos);
        var shirtColor = ShadeColor(hurtTimer > 0f ? new Color(200, 60, 60, 255) : skinDef.ShirtColor, light, pos);
        var pantsColor = ShadeColor(hurtTimer > 0f ? new Color(160, 40, 40, 255) : skinDef.PantsColor, light, pos);
        var shoeColor = ShadeColor(skinDef.ShoeColor, light, pos);
        var eyeColor = ShadeColor(skinDef.EyeColor, Vector3.One, pos);
        var eyeWhite = ShadeColor(Color.White, light, pos);
        var detailColor = ShadeColor(skinDef.DetailColor, light, pos);

        Rlgl.PushMatrix();
        Rlgl.Translatef(pos.X, pos.Y, pos.Z);
        Rlgl.Rotatef(yaw * 180f / MathF.PI + 180f, 0f, 1f, 0f);

        if (isSneaking) {
            Rlgl.Translatef(0f, -0.15f, 0f);
        }

        // 1. Туловище
        Raylib.DrawCube(new Vector3(0f, 0f, 0f), 0.45f, 0.65f, 0.24f, shirtColor);
        // Нагрудник (броня)
        if (GetArmorMaterialColor(chestId) is { } cColor) {
            Raylib.DrawCube(new Vector3(0f, 0f, 0f), 0.48f, 0.67f, 0.27f, ShadeColor(cColor, light, pos));
        } else {
            // Вырез на шее (кожа)
            Raylib.DrawCube(new Vector3(0f, 0.26f, 0.121f), 0.12f, 0.12f, 0.01f, skinColor);
            // Детали одежды (пояс / ремень / эмблема)
            Raylib.DrawCube(new Vector3(0f, -0.28f, 0.121f), 0.452f, 0.08f, 0.01f, detailColor);
        }

        // 2. Голова с наклоном Pitch
        Rlgl.PushMatrix();
        Rlgl.Translatef(0f, 0.52f, 0f);
        Rlgl.Rotatef(-pitch * 180f / MathF.PI, 1f, 0f, 0f);

        Raylib.DrawCube(Vector3.Zero, 0.42f, 0.42f, 0.42f, skinColor);
        // Шлем (броня) или Волосы
        if (GetArmorMaterialColor(helmetId) is { } hColor) {
            Raylib.DrawCube(new Vector3(0f, 0.06f, 0f), 0.46f, 0.46f, 0.46f, ShadeColor(hColor, light, pos));
        } else {
            // Волосы
            Raylib.DrawCube(new Vector3(0f, 0.16f, 0f), 0.43f, 0.12f, 0.43f, hairColor);
            Raylib.DrawCube(new Vector3(0f, 0.05f, -0.16f), 0.43f, 0.22f, 0.12f, hairColor);
        }
        // Глаза (белок + зрачок цвета скина)
        Raylib.DrawCube(new Vector3(-0.11f, 0.02f, 0.215f), 0.08f, 0.06f, 0.01f, eyeWhite);
        Raylib.DrawCube(new Vector3(0.11f, 0.02f, 0.215f), 0.08f, 0.06f, 0.01f, eyeWhite);
        Raylib.DrawCube(new Vector3(-0.09f, 0.02f, 0.217f), 0.04f, 0.06f, 0.01f, eyeColor);
        Raylib.DrawCube(new Vector3(0.09f, 0.02f, 0.217f), 0.04f, 0.06f, 0.01f, eyeColor);
        // Нос
        Raylib.DrawCube(new Vector3(0f, -0.04f, 0.215f), 0.08f, 0.05f, 0.01f, detailColor);
        Rlgl.PopMatrix();

        // 3. Руки
        // Левая рука (качается при ходьбе или блокирует щитом)
        var lArmPos = isBlocking ? new Vector3(-0.20f, 0.12f, 0.28f) : new Vector3(-0.33f, 0.02f, walkSwing * 0.3f);
        Raylib.DrawCube(lArmPos, 0.18f, 0.65f, 0.18f, skinColor);
        Raylib.DrawCube(lArmPos + new Vector3(0f, 0.18f, 0f), 0.182f, 0.30f, 0.182f, shirtColor);

        // Предмет / Щит во второй руке (слева)
        if (offhandItemId > 0) {
            if (offhandItemId == GameData.ShieldItem.Id) {
                var shieldPos = isBlocking ? new Vector3(-0.06f, 0.10f, 0.42f) : (lArmPos + new Vector3(0f, -0.05f, 0.16f));
                Raylib.DrawCube(shieldPos, 0.36f, 0.48f, 0.06f, ShadeColor(new Color(155, 140, 115, 255), light, pos));
                Raylib.DrawCube(shieldPos + new Vector3(0f, 0f, 0.035f), 0.10f, 0.10f, 0.02f, ShadeColor(new Color(200, 200, 205, 255), light, pos));
            } else {
                var offPos = lArmPos + new Vector3(0f, -0.22f, 0.20f);
                Raylib.DrawCube(offPos, 0.14f, 0.14f, 0.14f, ShadeColor(new Color(140, 140, 150, 255), light, pos));
            }
        }

        // Правая рука (качается при ходьбе + наносит удар/копает)
        var rArmPos = new Vector3(0.33f, 0.02f + armPunch * 0.2f, -walkSwing * 0.3f + armPunch * 0.4f);
        Raylib.DrawCube(rArmPos, 0.18f, 0.65f, 0.18f, skinColor);
        Raylib.DrawCube(rArmPos + new Vector3(0f, 0.18f, 0f), 0.182f, 0.30f, 0.182f, shirtColor);

        // 3D предмет в правой руке
        if (selectedItemId > 0) {
            var itemPos = rArmPos + new Vector3(0f, -0.22f, 0.20f + armPunch * 0.2f);
            if (GameData.TryGetBlock((ushort)selectedItemId, out var bDef) || (GameData.TryGetBlockByItem((ushort)selectedItemId, out var bDef2) && (bDef = bDef2) != null)) {
                var bCol = ShadeColor(new Color(130, 140, 155, 255), light, pos);
                Raylib.DrawCube(itemPos, 0.20f, 0.20f, 0.20f, bCol);
            } else {
                Color itemColor = selectedItemId switch {
                    _ when GameData.Items.TryGetValue((ushort)selectedItemId, out var idef) && idef.Name.Contains("Алмаз") => new Color(90, 220, 240, 255),
                    _ when GameData.Items.TryGetValue((ushort)selectedItemId, out var idef) && idef.Name.Contains("Золот") => new Color(240, 215, 60, 255),
                    _ when GameData.Items.TryGetValue((ushort)selectedItemId, out var idef) && idef.Name.Contains("Желез") => new Color(220, 220, 225, 255),
                    _ when GameData.Items.TryGetValue((ushort)selectedItemId, out var idef) && idef.Name.Contains("Камен") => new Color(130, 130, 135, 255),
                    _ => new Color(175, 135, 80, 255)
                };
                Raylib.DrawCube(itemPos, 0.09f, 0.36f, 0.09f, ShadeColor(itemColor, light, pos));
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.11f, 0f), 0.20f, 0.07f, 0.10f, ShadeColor(itemColor, light, pos));
            }
        }

        // 4. Ноги
        var lLegPos = new Vector3(-0.12f, -0.62f, -walkSwing * 0.35f);
        var rLegPos = new Vector3(0.12f, -0.62f, walkSwing * 0.35f);

        Raylib.DrawCube(lLegPos, 0.20f, 0.65f, 0.20f, pantsColor);
        Raylib.DrawCube(rLegPos, 0.20f, 0.65f, 0.20f, pantsColor);

        // Поножи (броня)
        if (GetArmorMaterialColor(legsId) is { } lColor) {
            Raylib.DrawCube(lLegPos, 0.22f, 0.55f, 0.22f, ShadeColor(lColor, light, pos));
            Raylib.DrawCube(rLegPos, 0.22f, 0.55f, 0.22f, ShadeColor(lColor, light, pos));
        }

        // Ботинки
        if (GetArmorMaterialColor(bootsId) is { } bColor) {
            Raylib.DrawCube(lLegPos - new Vector3(0f, 0.24f, -0.02f), 0.222f, 0.17f, 0.26f, ShadeColor(bColor, light, pos));
            Raylib.DrawCube(rLegPos - new Vector3(0f, 0.24f, -0.02f), 0.222f, 0.17f, 0.26f, ShadeColor(bColor, light, pos));
        } else {
            Raylib.DrawCube(lLegPos - new Vector3(0f, 0.25f, -0.02f), 0.202f, 0.15f, 0.24f, shoeColor);
            Raylib.DrawCube(rLegPos - new Vector3(0f, 0.25f, -0.02f), 0.202f, 0.15f, 0.24f, shoeColor);
        }

        Rlgl.PopMatrix();
    }

    public void DrawRemotePlayerNameTags(Camera3D camera) {
        if (GameClient.Active != null) {
            foreach (var rp in GameClient.Active.RemotePlayers) {
                if (rp.Dimension != _session.World.Dimension) continue;
                DrawPlayerNameTag2D(camera, rp.Name, rp.Position, rp.IsSneaking, rp.Health);
            }
        } else if (GameServer.Active != null) {
            foreach (var cl in GameServer.Active.Clients) {
                if (cl.Dimension != _session.World.Dimension) continue;
                DrawPlayerNameTag2D(camera, cl.Name, cl.Position, cl.IsSneaking, cl.Health);
            }
        }
    }

    private void DrawPlayerNameTag2D(Camera3D camera, string name, Vector3 pos, bool isSneaking, float health) {
        var namePos = pos + new Vector3(0f, isSneaking ? 1.05f : 1.25f, 0f);
        float dist = Vector3.Distance(camera.Position, namePos);
        if (dist < 0.8f || dist > 50f) return;

        var toTarget = namePos - camera.Position;
        var camFwd = Vector3.Normalize(camera.Target - camera.Position);
        if (Vector3.Dot(toTarget, camFwd) <= 0.1f) return;

        var sp = Raylib.GetWorldToScreen(namePos, camera);
        if (sp.X < -100 || sp.X > Raylib.GetScreenWidth() + 100 || sp.Y < -50 || sp.Y > Raylib.GetScreenHeight() + 50) return;

        float scale = Math.Clamp(1.0f - (dist / 65f), 0.55f, 1.0f);
        float fontSize = 13.5f * scale;
        float nameW = Fonts.Measure(name, fontSize);
        float tagH = 18f * scale;
        float tagW = nameW + 14f * scale;

        var tagRect = new Rectangle(sp.X - tagW / 2f, sp.Y - tagH / 2f, tagW, tagH);
        Raylib.DrawRectangleRounded(tagRect, 0.25f, 4, isSneaking ? new Color(15, 18, 26, 110) : new Color(15, 18, 26, 210));
        Fonts.DrawCentered(name, sp.X, sp.Y - 6f * scale, fontSize, isSneaking ? new Color(200, 205, 215, 140) : Color.White);

        // Полоска здоровья
        float hpPct = Math.Clamp(health / 20f, 0f, 1f);
        float barW = Math.Max(nameW, 32f * scale);
        float barH = 3.5f * scale;
        var hpBg = new Rectangle(sp.X - barW / 2f, sp.Y + tagH / 2f + 2f * scale, barW, barH);
        Raylib.DrawRectangleRounded(hpBg, 0.4f, 2, new Color(30, 30, 30, 180));
        if (hpPct > 0f) {
            Color hpCol = hpPct > 0.5f ? new Color(60, 225, 75, 240) : (hpPct > 0.25f ? new Color(240, 205, 45, 240) : new Color(240, 60, 50, 240));
            Raylib.DrawRectangleRounded(new Rectangle(hpBg.X, hpBg.Y, barW * hpPct, barH), 0.4f, 2, hpCol);
        }
    }

    public void DrawEntities(Camera3D camera) {
        float time = (float)Raylib.GetTime();

        // 1. Draw Remote Players (Multiplayer: Steve 3D Model, Head Pitch/Yaw, Item in Hand, 3D Name Tag)
        DrawRemotePlayers(camera, time);

        // Draw Local Player in 3rd person view (F5)
        if (_session.CameraPerspective != 0 && _session.Player.Health > 0f) {
            var p = _session.Player;
            int selectedItemId = p.SelectedItem?.Id ?? 0;
            int offhandItemId = p.OffhandItem?.Id ?? 0;
            int helmetId = p.Armor[0]?.Item.Definition.Id ?? 0;
            int chestId = p.Armor[1]?.Item.Definition.Id ?? 0;
            int legsId = p.Armor[2]?.Item.Definition.Id ?? 0;
            int bootsId = p.Armor[3]?.Item.Definition.Id ?? 0;
            DrawSinglePlayerModel(camera, time, p.Name, p.Position, p.Yaw, p.Pitch, p.IsMoving, p.IsCrouching, p.Health, selectedItemId, offhandItemId, helmetId, chestId, legsId, bootsId, p.SwingMarker, p.HurtTimer, p.SkinName, p.IsBlocking);
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

            if (a.IsBaby) {
                Rlgl.Scalef(0.55f, 0.55f, 0.55f);
            }

            if (a.HurtTime > 0f) {
                float roll = MathF.Sin(a.HurtTime * MathF.PI * 10f) * 12f;
                Rlgl.Rotatef(roll, 0f, 0f, 1f);
            }

            if (a.Type == AnimalType.Pig) {
                // ── PIG MODEL (с 3D-ушками и виляющим хвостиком) ──────────────
                var pigPink = ShadeColor(a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(240, 165, 165, 255), light, aPos);
                var snoutColor = ShadeColor(a.HurtTime > 0f ? new Color(200, 60, 60, 255) : new Color(220, 130, 130, 255), light, aPos);

                // Body
                Raylib.DrawCube(new Vector3(0f, 0.125f, 0f), 0.65f, 0.55f, 0.85f, pigPink);

                // Head
                var pigHead = new Vector3(0f, 0.25f, 0.55f);
                Raylib.DrawCube(pigHead, 0.45f, 0.45f, 0.45f, pigPink);

                // Ears (3D ушки)
                Raylib.DrawCube(pigHead + new Vector3(-0.24f, 0.16f, 0.05f), 0.08f, 0.12f, 0.10f, pigPink);
                Raylib.DrawCube(pigHead + new Vector3(0.24f, 0.16f, 0.05f), 0.08f, 0.12f, 0.10f, pigPink);

                // Snout (рыло с ноздрями)
                var pigSnout = pigHead + new Vector3(0f, -0.05f, 0.26f);
                Raylib.DrawCube(pigSnout, 0.22f, 0.14f, 0.10f, snoutColor);
                Raylib.DrawCube(pigSnout + new Vector3(-0.05f, 0f, 0.055f), 0.04f, 0.05f, 0.01f, ShadeColor(Color.Black, light, aPos));
                Raylib.DrawCube(pigSnout + new Vector3(0.05f, 0f, 0.055f), 0.04f, 0.05f, 0.01f, ShadeColor(Color.Black, light, aPos));

                // Eyes
                Raylib.DrawCube(pigHead + new Vector3(-0.16f, 0.08f, 0.23f), 0.06f, 0.06f, 0.01f, ShadeColor(Color.Black, light, aPos));
                Raylib.DrawCube(pigHead + new Vector3(0.16f, 0.08f, 0.23f), 0.06f, 0.06f, 0.01f, ShadeColor(Color.Black, light, aPos));

                // Хвостик крючком
                float tailWag = MathF.Sin(time * 12f) * 0.08f;
                Raylib.DrawCube(new Vector3(tailWag, 0.25f, -0.46f), 0.06f, 0.06f, 0.12f, pigPink);

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
                // ── COW MODEL (чистая воксельная корова без Z-fighting и мерцания) ──
                var cowBrown = ShadeColor(a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(68, 52, 40, 255), light, aPos);
                var cowWhite = ShadeColor(a.HurtTime > 0f ? new Color(220, 110, 110, 255) : new Color(230, 230, 230, 255), light, aPos);
                var snoutColor = ShadeColor(a.HurtTime > 0f ? new Color(200, 70, 70, 255) : new Color(185, 150, 150, 255), light, aPos);
                var hornColor = ShadeColor(new Color(220, 220, 215, 255), light, aPos);
                var udderPink = ShadeColor(new Color(245, 170, 180, 255), light, aPos);
                var hoofColor = ShadeColor(new Color(36, 28, 22, 255), light, aPos);

                // Body (Основное тело коровы)
                Raylib.DrawCube(new Vector3(0f, 0.15f, 0f), 0.75f, 0.60f, 1.05f, cowBrown);

                // Белое брюхо/пятно снизу и по бокам с четким выступом
                Raylib.DrawCube(new Vector3(0f, 0.08f, -0.05f), 0.76f, 0.32f, 0.55f, cowWhite);

                // Udder
                Raylib.DrawCube(new Vector3(0f, -0.10f, -0.25f), 0.20f, 0.12f, 0.22f, udderPink);

                // Head
                var headPos = new Vector3(0f, 0.40f, 0.65f);
                Raylib.DrawCube(headPos, 0.45f, 0.45f, 0.45f, cowBrown);

                // Snout
                Raylib.DrawCube(headPos + new Vector3(0f, -0.08f, 0.26f), 0.32f, 0.20f, 0.14f, snoutColor);

                // Horns
                Raylib.DrawCube(headPos + new Vector3(-0.22f, 0.24f, -0.05f), 0.08f, 0.16f, 0.08f, hornColor);
                Raylib.DrawCube(headPos + new Vector3(0.22f, 0.24f, -0.05f), 0.08f, 0.16f, 0.08f, hornColor);

                // Eyes
                Raylib.DrawCube(headPos + new Vector3(-0.18f, 0.06f, 0.23f), 0.06f, 0.06f, 0.01f, ShadeColor(Color.Black, light, aPos));
                Raylib.DrawCube(headPos + new Vector3(0.18f, 0.06f, 0.23f), 0.06f, 0.06f, 0.01f, ShadeColor(Color.Black, light, aPos));

                // 4 Legs with Hooves
                float cLegW = 0.20f, cLegH = 0.55f, cLegD = 0.20f;
                var cFL = new Vector3(-0.24f, -0.375f, 0.32f + walkSwing * 0.2f);
                var cFR = new Vector3(0.24f, -0.375f, 0.32f - walkSwing * 0.2f);
                var cBL = new Vector3(-0.24f, -0.375f, -0.32f - walkSwing * 0.2f);
                var cBR = new Vector3(0.24f, -0.375f, -0.32f + walkSwing * 0.2f);

                Raylib.DrawCube(cFL, cLegW, cLegH, cLegD, cowBrown);
                Raylib.DrawCube(cFR, cLegW, cLegH, cLegD, cowBrown);
                Raylib.DrawCube(cBL, cLegW, cLegH, cLegD, cowBrown);
                Raylib.DrawCube(cBR, cLegW, cLegH, cLegD, cowBrown);

                // Копыта
                Raylib.DrawCube(cFL - new Vector3(0f, 0.22f, 0f), cLegW + 0.01f, 0.12f, cLegD + 0.01f, hoofColor);
                Raylib.DrawCube(cFR - new Vector3(0f, 0.22f, 0f), cLegW + 0.01f, 0.12f, cLegD + 0.01f, hoofColor);
                Raylib.DrawCube(cBL - new Vector3(0f, 0.22f, 0f), cLegW + 0.01f, 0.12f, cLegD + 0.01f, hoofColor);
                Raylib.DrawCube(cBR - new Vector3(0f, 0.22f, 0f), cLegW + 0.01f, 0.12f, cLegD + 0.01f, hoofColor);

            } else if (a.Type == AnimalType.Sheep) {
                // ── SHEEP MODEL ───────────────────────────────────────────
                var woolWhite = ShadeColor(a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(235, 235, 235, 255), light, aPos);
                var skinTan = ShadeColor(a.HurtTime > 0f ? new Color(210, 80, 80, 255) : new Color(225, 205, 185, 255), light, aPos);

                // Fluffy Wool Body
                Raylib.DrawCube(new Vector3(0f, 0.10f, 0f), 0.80f, 0.65f, 0.95f, woolWhite);

                // Head
                var headPos = new Vector3(0f, 0.28f, 0.58f);
                Raylib.DrawCube(headPos, 0.35f, 0.38f, 0.42f, skinTan);
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

            } else if (a.Type == AnimalType.Chicken) {
                // ── CHICKEN MODEL (Курица с машущими крыльями, клювом и гребешком) ──
                var featherWhite = ShadeColor(a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(245, 245, 248, 255), light, aPos);
                var beakYellow = ShadeColor(new Color(245, 185, 30, 255), light, aPos);
                var wattleRed = ShadeColor(new Color(225, 40, 40, 255), light, aPos);
                var legYellow = ShadeColor(new Color(235, 175, 25, 255), light, aPos);

                // Взмахи крыльев при падении или ходьбе
                bool isFalling = a.Velocity.Y < -0.2f;
                float wingFlap = isFalling ? MathF.Sin(time * 28f) * 0.45f : (isMoving ? MathF.Sin(time * 12f) * 0.18f : 0f);

                // Body (Тело)
                Raylib.DrawCube(new Vector3(0f, 0.05f, 0f), 0.42f, 0.35f, 0.48f, featherWhite);

                // Head (Голова с легким покачиванием)
                float headBob = isMoving ? MathF.Sin(time * 9f) * 0.05f : 0f;
                var headPos = new Vector3(0f, 0.28f, 0.22f + headBob);
                Raylib.DrawCube(headPos, 0.24f, 0.28f, 0.24f, featherWhite);

                // Comb (Красный гребешок на голове)
                Raylib.DrawCube(headPos + new Vector3(0f, 0.17f, 0f), 0.06f, 0.08f, 0.18f, wattleRed);

                // Beak (Желтый клюв)
                var beakPos = headPos + new Vector3(0f, 0.02f, 0.15f);
                Raylib.DrawCube(beakPos, 0.12f, 0.08f, 0.10f, beakYellow);

                // Wattle (Красная бородка под клювом)
                Raylib.DrawCube(headPos + new Vector3(0f, -0.09f, 0.13f), 0.08f, 0.10f, 0.06f, wattleRed);

                // Eyes (Черные глазки)
                Raylib.DrawCube(headPos + new Vector3(-0.125f, 0.05f, 0.06f), 0.01f, 0.04f, 0.04f, ShadeColor(Color.Black, light, aPos));
                Raylib.DrawCube(headPos + new Vector3(0.125f, 0.05f, 0.06f), 0.01f, 0.04f, 0.04f, ShadeColor(Color.Black, light, aPos));

                // Wings (Крылья по бокам с анимацией махания)
                var leftWing = new Vector3(-0.23f - MathF.Abs(wingFlap) * 0.15f, 0.08f + MathF.Abs(wingFlap) * 0.12f, 0f);
                var rightWing = new Vector3(0.23f + MathF.Abs(wingFlap) * 0.15f, 0.08f + MathF.Abs(wingFlap) * 0.12f, 0f);
                Raylib.DrawCube(leftWing, 0.06f, 0.26f, 0.36f, featherWhite);
                Raylib.DrawCube(rightWing, 0.06f, 0.26f, 0.36f, featherWhite);

                // 2 Legs (Тонкие желтые лапки)
                float legSwing = walkSwing * 0.4f;
                var legL = new Vector3(-0.10f, -0.22f, legSwing);
                var legR = new Vector3(0.10f, -0.22f, -legSwing);
                Raylib.DrawCube(legL, 0.06f, 0.22f, 0.06f, legYellow);
                Raylib.DrawCube(legR, 0.06f, 0.22f, 0.06f, legYellow);
                // Лапки снизу (пальцы)
                Raylib.DrawCube(legL + new Vector3(0f, -0.10f, 0.04f), 0.10f, 0.02f, 0.12f, legYellow);
                Raylib.DrawCube(legR + new Vector3(0f, -0.10f, 0.04f), 0.10f, 0.02f, 0.12f, legYellow);
            }

            Rlgl.PopMatrix();

            if (a.LoveTimer > 0f) {
                float heartY = a.Position.Y + a.HalfSizeY + 0.35f + MathF.Sin(time * 5f + a.Position.X) * 0.08f;
                var heartPos = new Vector3(a.Position.X, heartY, a.Position.Z);
                var heartSrc = TextureAtlas.TilePixelRect(TextureAtlas.THeartParticle);
                Raylib.DrawBillboardRec(camera, TextureAtlas.Atlas, heartSrc, heartPos, new Vector2(0.4f, 0.4f), Color.White);
            }
        }

        // 3. Draw Hostile Mobs (Zombie, Babakher, Skeleton, Spider, Enderman, Blaze, Mini-Bosses)
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

                // Arms (вытянуты вперед)
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
                // ── SKELETON MODEL (3D ребра и лук со стрелой) ──────────────
                var boneColor = ShadeColor(h.HurtTime > 0f ? new Color(220, 60, 60, 255) : new Color(205, 205, 205, 255), light, hPos);
                var ribColor = ShadeColor(h.HurtTime > 0f ? new Color(180, 40, 40, 255) : new Color(130, 130, 130, 255), light, hPos);
                var bowColor = ShadeColor(new Color(125, 80, 45, 255), light, hPos);
                var stringColor = ShadeColor(new Color(225, 225, 225, 255), light, hPos);
                var arrowColor = ShadeColor(new Color(230, 230, 230, 255), light, hPos);
                var outlineColor = ShadeColor(new Color(30, 30, 30, 180), light, hPos);

                // Body (Позвоночник и грудная клетка)
                Raylib.DrawCube(new Vector3(0f, 0.15f, 0f), 0.45f, 0.70f, 0.22f, boneColor);
                Raylib.DrawCubeWires(new Vector3(0f, 0.15f, 0f), 0.452f, 0.702f, 0.222f, outlineColor);

                // 3D ребра
                Raylib.DrawCube(new Vector3(0f, 0.28f, 0.115f), 0.38f, 0.05f, 0.02f, ribColor);
                Raylib.DrawCube(new Vector3(0f, 0.18f, 0.115f), 0.38f, 0.05f, 0.02f, ribColor);
                Raylib.DrawCube(new Vector3(0f, 0.08f, 0.115f), 0.38f, 0.05f, 0.02f, ribColor);

                // Head
                var headPos = new Vector3(0f, 0.72f, 0f);
                Raylib.DrawCube(headPos, 0.44f, 0.44f, 0.44f, boneColor);
                Raylib.DrawCubeWires(headPos, 0.442f, 0.442f, 0.442f, outlineColor);

                // Skull eyes & nose cavity
                Raylib.DrawCube(headPos + new Vector3(-0.10f, 0.04f, 0.225f), 0.09f, 0.09f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0.10f, 0.04f, 0.225f), 0.09f, 0.09f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0f, -0.09f, 0.225f), 0.12f, 0.05f, 0.01f, ShadeColor(Color.Black, light, hPos));

                // Arms
                var leftArm = new Vector3(-0.30f, 0.35f, 0.22f);
                var rightArm = new Vector3(0.30f, 0.35f, 0.22f);
                Raylib.DrawCube(leftArm, 0.12f, 0.12f, 0.50f, boneColor);
                Raylib.DrawCube(rightArm, 0.12f, 0.12f, 0.50f, boneColor);

                // 3D Bow with Arrow
                var bowPos = new Vector3(0.15f, 0.35f, 0.48f);
                Raylib.DrawCube(bowPos, 0.06f, 0.55f, 0.06f, bowColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, 0.24f, -0.06f), 0.05f, 0.12f, 0.06f, bowColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, -0.24f, -0.06f), 0.05f, 0.12f, 0.06f, bowColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, 0f, -0.08f), 0.02f, 0.52f, 0.02f, stringColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, 0f, 0.05f), 0.03f, 0.03f, 0.45f, arrowColor);

                // Legs
                var leftLeg = new Vector3(-0.12f, -0.525f, walkSwing * 0.3f);
                var rightLeg = new Vector3(0.12f, -0.525f, -walkSwing * 0.3f);
                Raylib.DrawCube(leftLeg, 0.14f, 0.65f, 0.14f, boneColor);
                Raylib.DrawCube(rightLeg, 0.14f, 0.65f, 0.14f, boneColor);

            } else if (h.Type == HostileType.Spider) {
                // ── SPIDER MODEL (8 суставчатых анимированных лап + 8 светящихся глаз) ──
                var spiderColor = ShadeColor(h.HurtTime > 0f ? new Color(220, 50, 50, 255) : new Color(35, 25, 20, 255), light, hPos);
                var spiderDark = ShadeColor(new Color(22, 16, 14, 255), light, hPos);
                var eyeRed = ShadeColor(new Color(235, 20, 20, 255), Vector3.One, hPos);

                // Body
                var headPos = new Vector3(0f, -0.05f, 0.30f);
                Raylib.DrawCube(headPos, 0.45f, 0.35f, 0.40f, spiderColor);
                var abdomenPos = new Vector3(0f, 0.05f, -0.30f);
                Raylib.DrawCube(abdomenPos, 0.65f, 0.50f, 0.65f, spiderDark);
                // Красный знак на спине
                Raylib.DrawCube(abdomenPos + new Vector3(0f, 0.26f, 0f), 0.18f, 0.02f, 0.22f, eyeRed);

                // 8 Glowing Red Eyes (Кластер из 8 глаз)
                Raylib.DrawCube(headPos + new Vector3(-0.14f, 0.05f, 0.205f), 0.05f, 0.05f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(-0.06f, 0.05f, 0.205f), 0.05f, 0.05f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(0.06f, 0.05f, 0.205f), 0.05f, 0.05f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(0.14f, 0.05f, 0.205f), 0.05f, 0.05f, 0.01f, eyeRed);

                Raylib.DrawCube(headPos + new Vector3(-0.10f, -0.03f, 0.205f), 0.04f, 0.04f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(-0.03f, -0.03f, 0.205f), 0.04f, 0.04f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(0.03f, -0.03f, 0.205f), 0.04f, 0.04f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(0.10f, -0.03f, 0.205f), 0.04f, 0.04f, 0.01f, eyeRed);

                // 8 Суставчатых лап
                for (int i = 0; i < 4; i++) {
                    float zOff = (i - 1.5f) * 0.22f;
                    float legSwing = MathF.Sin(time * 12f + i * 1.2f) * 0.16f;
                    var legL = new Vector3(-0.45f, -0.20f + legSwing, zOff);
                    var legR = new Vector3(0.45f, -0.20f - legSwing, zOff);
                    Raylib.DrawCube(legL, 0.55f, 0.08f, 0.08f, spiderColor);
                    Raylib.DrawCube(legR, 0.55f, 0.08f, 0.08f, spiderColor);
                }

            } else if (h.Type == HostileType.Babakher) {
                // ── БАБАХЕР MODEL (4 шагающие ноги, камуфляж, раздувание) ──
                bool isFlashing = h.FuseTimer > 0f && (int)(h.FuseTimer * 12f) % 2 == 0;
                var baseBabakher = isFlashing ? Color.White : (h.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(30, 185, 55, 255));
                var babakherGreen = isFlashing ? ShadeColor(Color.White, light, hPos) : ShadeColor(baseBabakher, light, hPos);
                var outlineColor = ShadeColor(new Color(15, 60, 20, 180), light, hPos);

                float fuseScale = 1.0f + MathF.Min(0.28f, h.FuseTimer * 0.22f);
                Rlgl.Scalef(fuseScale, fuseScale, fuseScale);

                // Body
                Raylib.DrawCube(new Vector3(0f, -0.025f, 0f), 0.5f, 0.65f, 0.35f, babakherGreen);
                Raylib.DrawCubeWires(new Vector3(0f, -0.025f, 0f), 0.502f, 0.652f, 0.352f, outlineColor);

                // Head
                var headPos = new Vector3(0f, 0.54f, 0f);
                Raylib.DrawCube(headPos, 0.48f, 0.48f, 0.48f, babakherGreen);
                Raylib.DrawCubeWires(headPos, 0.482f, 0.482f, 0.482f, outlineColor);

                // Каноничное лицо бабахера (крипера): черные глаза, переносица и опущенный рот
                var faceBlack = isFlashing ? ShadeColor(Color.White, light, hPos) : ShadeColor(new Color(15, 25, 15, 255), light, hPos);
                // Глаза
                Raylib.DrawCube(headPos + new Vector3(-0.10f, 0.07f, 0.245f), 0.08f, 0.08f, 0.015f, faceBlack);
                Raylib.DrawCube(headPos + new Vector3(0.10f, 0.07f, 0.245f), 0.08f, 0.08f, 0.015f, faceBlack);
                // Переносица
                Raylib.DrawCube(headPos + new Vector3(0f, 0.01f, 0.245f), 0.06f, 0.06f, 0.015f, faceBlack);
                // Рот (перевернутая дуга с уголками вниз)
                Raylib.DrawCube(headPos + new Vector3(0f, -0.04f, 0.245f), 0.16f, 0.05f, 0.015f, faceBlack);
                Raylib.DrawCube(headPos + new Vector3(-0.08f, -0.09f, 0.245f), 0.06f, 0.07f, 0.015f, faceBlack);
                Raylib.DrawCube(headPos + new Vector3(0.08f, -0.09f, 0.245f), 0.06f, 0.07f, 0.015f, faceBlack);

                // 4 шагающие ноги
                float legSwing = walkSwing * 0.3f;
                Raylib.DrawCube(new Vector3(-0.16f, -0.52f, 0.18f + legSwing), 0.18f, 0.36f, 0.18f, babakherGreen);
                Raylib.DrawCube(new Vector3(0.16f, -0.52f, 0.18f - legSwing), 0.18f, 0.36f, 0.18f, babakherGreen);
                Raylib.DrawCube(new Vector3(-0.16f, -0.52f, -0.18f - legSwing), 0.18f, 0.36f, 0.18f, babakherGreen);
                Raylib.DrawCube(new Vector3(0.16f, -0.52f, -0.18f + legSwing), 0.18f, 0.36f, 0.18f, babakherGreen);

            } else if (h.Type == HostileType.ZombiePigman) {
                // ── ZOMBIE PIGMAN (СВИНОЗОМБИ) MODEL ───────────────────────
                var pigSkin = ShadeColor(h.HurtTime > 0f ? new Color(240, 70, 70, 255) : new Color(230, 150, 160, 255), light, hPos);
                var rotGreen = ShadeColor(h.HurtTime > 0f ? new Color(200, 50, 50, 255) : new Color(60, 110, 50, 255), light, hPos);
                var goldSword = ShadeColor(new Color(255, 215, 30, 255), light, hPos);
                var outlineColor = ShadeColor(new Color(30, 20, 20, 180), light, hPos);

                // Body
                Raylib.DrawCube(new Vector3(-0.15f, 0.15f, 0f), 0.3f, 0.70f, 0.35f, pigSkin);
                Raylib.DrawCube(new Vector3(0.15f, 0.15f, 0f), 0.3f, 0.70f, 0.35f, rotGreen);
                Raylib.DrawCubeWires(new Vector3(0f, 0.15f, 0f), 0.602f, 0.702f, 0.352f, outlineColor);

                // Head
                var headPos = new Vector3(0f, 0.74f, 0f);
                Raylib.DrawCube(headPos + new Vector3(-0.12f, 0f, 0f), 0.24f, 0.48f, 0.48f, pigSkin);
                Raylib.DrawCube(headPos + new Vector3(0.12f, 0f, 0f), 0.24f, 0.48f, 0.48f, rotGreen);
                Raylib.DrawCubeWires(headPos, 0.482f, 0.482f, 0.482f, outlineColor);

                // Пятачок
                Raylib.DrawCube(headPos + new Vector3(0f, -0.06f, 0.26f), 0.22f, 0.14f, 0.08f, pigSkin);

                // Руки с золотым мечом
                var leftArm = new Vector3(-0.42f, 0.35f, 0.25f);
                var rightArm = new Vector3(0.42f, 0.35f, 0.25f);
                Raylib.DrawCube(leftArm, 0.2f, 0.2f, 0.55f, pigSkin);
                Raylib.DrawCube(rightArm, 0.2f, 0.2f, 0.55f, rotGreen);

                var swordPos = rightArm + new Vector3(0f, 0.10f, 0.28f);
                Raylib.DrawCube(swordPos, 0.06f, 0.55f, 0.06f, goldSword);

                // Ноги
                Raylib.DrawCube(new Vector3(-0.16f, -0.525f, walkSwing * 0.3f), 0.22f, 0.65f, 0.22f, pigSkin);
                Raylib.DrawCube(new Vector3(0.16f, -0.525f, -walkSwing * 0.3f), 0.22f, 0.65f, 0.22f, rotGreen);

            } else if (h.Type == HostileType.Blaze) {
                // ── BLAZE (ИФРИТ) MODEL (12 огненных парящих стержней) ─────
                var blazeYellow = ShadeColor(h.HurtTime > 0f ? new Color(255, 100, 100, 255) : new Color(255, 210, 40, 255), Vector3.One, hPos);
                var blazeOrange = ShadeColor(h.HurtTime > 0f ? new Color(255, 60, 60, 255) : new Color(240, 110, 20, 255), Vector3.One, hPos);
                var blazeDark = ShadeColor(new Color(90, 30, 10, 255), Vector3.One, hPos);

                float bob = MathF.Sin(time * 3f) * 0.10f;
                var headPos = new Vector3(0f, 0.35f + bob, 0f);
                Raylib.DrawCube(headPos, 0.46f, 0.46f, 0.46f, blazeYellow);
                Raylib.DrawCube(headPos + new Vector3(-0.11f, 0.04f, 0.235f), 0.08f, 0.08f, 0.01f, blazeDark);
                Raylib.DrawCube(headPos + new Vector3(0.11f, 0.04f, 0.235f), 0.08f, 0.08f, 0.01f, blazeDark);

                // 3 яруса стержней
                float rot1 = time * 2.5f, rot2 = -time * 2.0f + 0.5f, rot3 = time * 1.8f + 1.2f;
                for (int i = 0; i < 4; i++) {
                    float a1 = rot1 + i * (MathF.Tau / 4f);
                    Raylib.DrawCube(new Vector3(MathF.Cos(a1) * 0.45f, 0.22f + bob, MathF.Sin(a1) * 0.45f), 0.10f, 0.38f, 0.10f, blazeOrange);
                    float a2 = rot2 + i * (MathF.Tau / 4f);
                    Raylib.DrawCube(new Vector3(MathF.Cos(a2) * 0.40f, -0.15f + bob, MathF.Sin(a2) * 0.40f), 0.10f, 0.38f, 0.10f, blazeOrange);
                    float a3 = rot3 + i * (MathF.Tau / 4f);
                    Raylib.DrawCube(new Vector3(MathF.Cos(a3) * 0.30f, -0.50f + bob, MathF.Sin(a3) * 0.30f), 0.10f, 0.38f, 0.10f, blazeOrange);
                }

            } else if (h.Type == HostileType.Enderman) {
                // ── ENDERMAN MODEL (высокий, тёмно-пурпурный, светящиеся глаза) ─
                var skinColor = ShadeColor(h.HurtTime > 0f ? new Color(80, 20, 90, 255) : new Color(28, 18, 40, 255), light, hPos);
                var headColor = ShadeColor(h.HurtTime > 0f ? new Color(80, 20, 90, 255) : new Color(22, 14, 32, 255), light, hPos);
                var eyeColor = ShadeColor(new Color(200, 90, 255, 255), Vector3.One, hPos);

                // Ноги
                Raylib.DrawCube(new Vector3(-0.13f, -0.68f, walkSwing * 0.3f), 0.16f, 0.95f, 0.16f, skinColor);
                Raylib.DrawCube(new Vector3(0.13f, -0.68f, -walkSwing * 0.3f), 0.16f, 0.95f, 0.16f, skinColor);

                // Туловище
                Raylib.DrawCube(new Vector3(0f, 0.18f, 0f), 0.42f, 0.72f, 0.24f, skinColor);

                // Руки
                var leftArm = new Vector3(-0.31f, 0.06f, walkSwing * 0.3f);
                var rightArm = new Vector3(0.31f, 0.06f, -walkSwing * 0.3f);
                Raylib.DrawCube(leftArm, 0.12f, 1.05f, 0.12f, skinColor);
                Raylib.DrawCube(rightArm, 0.12f, 1.05f, 0.12f, skinColor);

                // Голова + глаза
                var headPos = new Vector3(0f, 0.92f, 0f);
                Raylib.DrawCube(headPos, 0.42f, 0.44f, 0.42f, headColor);
                Raylib.DrawCube(headPos + new Vector3(-0.11f, 0.06f, 0.215f), 0.08f, 0.08f, 0.012f, eyeColor);
                Raylib.DrawCube(headPos + new Vector3(0.11f, 0.06f, 0.215f), 0.08f, 0.08f, 0.012f, eyeColor);

            } else if (h.Type == HostileType.NetherLord) {
                // ── МИНИ-БОСС: Повелитель Ада ─────────────────────────────
                var demonSkin = ShadeColor(h.HurtTime > 0f ? new Color(220, 60, 50, 255) : new Color(120, 30, 20, 255), light, hPos);
                var demonHot = ShadeColor(h.HurtTime > 0f ? new Color(255, 150, 60, 255) : new Color(210, 90, 40, 255), Vector3.One, hPos);
                var demonEye = ShadeColor(new Color(255, 210, 80, 255), Vector3.One, hPos);

                Raylib.DrawCube(new Vector3(-0.20f, -0.95f, walkSwing * 0.3f), 0.24f, 0.70f, 0.24f, demonSkin);
                Raylib.DrawCube(new Vector3(0.20f, -0.95f, -walkSwing * 0.3f), 0.24f, 0.70f, 0.24f, demonSkin);
                Raylib.DrawCube(new Vector3(0f, -0.20f, 0f), 0.55f, 0.90f, 0.35f, demonSkin);

                // Голова с рогами
                var dHead = new Vector3(0f, 0.55f, 0f);
                Raylib.DrawCube(dHead, 0.40f, 0.40f, 0.40f, demonSkin);
                Raylib.DrawCube(dHead + new Vector3(-0.14f, 0.26f, 0f), 0.06f, 0.20f, 0.06f, demonHot);
                Raylib.DrawCube(dHead + new Vector3(0.14f, 0.26f, 0f), 0.06f, 0.20f, 0.06f, demonHot);
                Raylib.DrawCube(dHead + new Vector3(-0.10f, 0.05f, 0.205f), 0.07f, 0.06f, 0.015f, demonEye);
                Raylib.DrawCube(dHead + new Vector3(0.10f, 0.05f, 0.205f), 0.07f, 0.06f, 0.015f, demonEye);

                // Огненные угли вокруг босса
                for (int i = 0; i < 4; i++) {
                    float a = time * 2f + i * 1.57f;
                    var ember = new Vector3(MathF.Cos(a) * 0.55f, -0.4f + MathF.Sin(time * 3f + i) * 0.3f, MathF.Sin(a) * 0.55f);
                    Raylib.DrawCube(ember, 0.08f, 0.08f, 0.08f, demonHot);
                }

            } else if (h.Type == HostileType.SwampGuardian) {
                // ── МИНИ-БОСС: Болотный страж (токсичные наросты и грибы) ───
                var swampBody = ShadeColor(h.HurtTime > 0f ? new Color(150, 220, 90, 255) : new Color(70, 130, 60, 255), light, hPos);
                var swampDark = ShadeColor(h.HurtTime > 0f ? new Color(80, 140, 50, 255) : new Color(35, 75, 35, 255), light, hPos);
                var swampEye = ShadeColor(new Color(255, 240, 150, 255), Vector3.One, hPos);
                float squish = 1f + MathF.Sin(time * 4f) * 0.08f;

                Raylib.DrawCube(new Vector3(0f, -0.1f, 0f), 0.9f, 0.55f * squish, 0.9f, swampBody);
                Raylib.DrawCube(new Vector3(0f, 0.32f, 0f), 0.8f, 0.42f * squish, 0.8f, swampBody);

                // Грибы на спине
                Raylib.DrawCube(new Vector3(-0.25f, 0.54f, -0.2f), 0.18f, 0.18f, 0.18f, new Color(220, 40, 40, 255));
                Raylib.DrawCube(new Vector3(0.25f, 0.52f, -0.15f), 0.15f, 0.15f, 0.15f, new Color(160, 110, 60, 255));

                Raylib.DrawCube(new Vector3(-0.2f, 0.42f, 0.41f), 0.12f, 0.09f, 0.02f, swampEye);
                Raylib.DrawCube(new Vector3(0.2f, 0.42f, 0.41f), 0.12f, 0.09f, 0.02f, swampEye);

            } else if (h.Type == HostileType.DesertGuardian) {
                // ── МИНИ-БОСС: Страж пустыни (золотой фараон-голем) ────────
                var golemSand = ShadeColor(h.HurtTime > 0f ? new Color(240, 200, 130, 255) : new Color(210, 190, 140, 255), light, hPos);
                var golemGold = ShadeColor(h.HurtTime > 0f ? new Color(255, 230, 90, 255) : new Color(200, 170, 60, 255), light, hPos);
                var golemEye = ShadeColor(new Color(255, 90, 40, 255), Vector3.One, hPos);
                var outline3 = ShadeColor(new Color(90, 70, 30, 200), light, hPos);

                Raylib.DrawCube(new Vector3(-0.25f, -0.55f, walkSwing * 0.3f), 0.3f, 0.5f, 0.3f, golemSand);
                Raylib.DrawCube(new Vector3(0.25f, -0.55f, -walkSwing * 0.3f), 0.3f, 0.5f, 0.3f, golemSand);
                Raylib.DrawCube(new Vector3(0f, 0.0f, 0f), 0.7f, 0.8f, 0.45f, golemSand);
                Raylib.DrawCube(new Vector3(0f, 0.05f, 0.23f), 0.3f, 0.25f, 0.03f, golemGold);

                var gHead = new Vector3(0f, 0.65f, 0f);
                Raylib.DrawCube(gHead, 0.5f, 0.5f, 0.5f, golemSand);
                Raylib.DrawCubeWires(gHead, 0.502f, 0.502f, 0.502f, outline3);
                Raylib.DrawCube(gHead + new Vector3(0f, 0.28f, 0f), 0.36f, 0.08f, 0.36f, golemGold);
                // Глаза
                Raylib.DrawCube(gHead + new Vector3(-0.12f, 0.04f, 0.26f), 0.08f, 0.08f, 0.02f, golemEye);
                Raylib.DrawCube(gHead + new Vector3(0.12f, 0.04f, 0.26f), 0.08f, 0.08f, 0.02f, golemEye);
            }

            Rlgl.PopMatrix();
        }

        // 3.1 Draw End Slime boss (Слизень Края)
        if (_world.EndBoss is { Alive: true } boss) {
            var bl = GetLightFactor(boss.Position);
            float squish = 1f + MathF.Sin(time * 6f) * 0.12f;
            float bodyW = EndSlime.HalfSizeXZ * 2f;
            float bodyH = EndSlime.HalfSizeY * 2f * squish;
            var baseCenter = boss.Position - new Vector3(0f, EndSlime.HalfSizeY, 0f);
            var bodyPos = baseCenter + new Vector3(0f, bodyH * 0.5f, 0f);
            bool hurt = boss.HurtTime > 0f;
            var green = hurt ? new Color(220, 70, 70, 235) : new Color(70, 160, 140, 240);
            var greenDark = hurt ? new Color(170, 40, 40, 235) : new Color(45, 110, 95, 240);
            var eyeColor = ShadeColor(new Color(200, 90, 255, 255), bl, boss.Position);

            // Во время отдыха (окно уязвимости) босс «сдувается» и желтеет — видно окно атаки
            if (boss.IsResting) {
                squish *= 0.82f;
                bodyH = EndSlime.HalfSizeY * 2f * squish;
                green = new Color(150, 175, 70, 240);
                greenDark = new Color(105, 125, 45, 240);
            }

            DrawSoftShadow(baseCenter, bodyW * 0.6f);

            // Внешняя оболочка
            Raylib.DrawCube(bodyPos, bodyW, bodyH, bodyW, ShadeColor(green, bl, boss.Position));
            // Внутренний гель (полупрозрачнее, светлее)
            Raylib.DrawCube(bodyPos, bodyW * 0.72f, bodyH * 0.72f, bodyW * 0.72f, ShadeColor(greenDark, bl, boss.Position));

            // Фиолетовые глаза на лицевой стороне
            float eyeY = boss.Position.Y + EndSlime.HalfSizeY * 0.15f;
            float eyeFz = bodyW * 0.55f;
            Raylib.DrawCube(boss.Position + new Vector3(-bodyW * 0.25f, 0.25f, eyeFz), 0.5f, 0.5f, 0.12f, eyeColor);
            Raylib.DrawCube(boss.Position + new Vector3(bodyW * 0.25f, 0.25f, eyeFz), 0.5f, 0.5f, 0.12f, eyeColor);
        }

        // 3.1.5 Draw True End Slime boss (Истинный Слизень Края в Бездне)
        if (_world.TrueVoidBoss is { Alive: true } tBoss) {
            var bl = GetLightFactor(tBoss.Position);
            float squish = 1f + MathF.Sin(time * 7f) * 0.15f;
            float bodyW = TrueEndSlime.HalfSizeXZ * 2f;
            float bodyH = TrueEndSlime.HalfSizeY * 2f * squish;
            var baseCenter = tBoss.Position - new Vector3(0f, TrueEndSlime.HalfSizeY, 0f);
            var bodyPos = baseCenter + new Vector3(0f, bodyH * 0.5f, 0f);
            bool hurt = tBoss.HurtTime > 0f;
            var voidColor = hurt ? new Color(255, 60, 60, 245) : new Color(75, 20, 110, 245);
            var voidCore = hurt ? new Color(220, 30, 30, 250) : new Color(140, 30, 180, 250);
            var eyeColor = ShadeColor(new Color(255, 30, 50, 255), bl, tBoss.Position);

            DrawSoftShadow(baseCenter, bodyW * 0.7f);

            // Внешняя тёмная оболочка Бездны
            Raylib.DrawCube(bodyPos, bodyW, bodyH, bodyW, ShadeColor(voidColor, bl, tBoss.Position));
            // Внутреннее пульсирующее ядро
            Raylib.DrawCube(bodyPos, bodyW * 0.75f, bodyH * 0.75f, bodyW * 0.75f, ShadeColor(voidCore, bl, tBoss.Position));

            // Зловещие светящиеся красные глаза
            float eyeFz = bodyW * 0.55f;
            Raylib.DrawCube(tBoss.Position + new Vector3(-bodyW * 0.25f, 0.35f, eyeFz), 0.6f, 0.6f, 0.15f, eyeColor);
            Raylib.DrawCube(tBoss.Position + new Vector3(bodyW * 0.25f, 0.35f, eyeFz), 0.6f, 0.6f, 0.15f, eyeColor);
        }

        // 3.2 End Crystals (сущности — парят, светятся, взрываются от касания)
        foreach (var cry in _world.EndCrystals) {
            if (!cry.Alive) continue;
            float bob = MathF.Sin(time * 2.2f + cry.BobPhase) * 0.07f;
            var cPos = cry.Position + new Vector3(0f, bob, 0f);
            float rot = time * 1.4f + cry.BobPhase;
            var cl = GetLightFactor(cPos);

            // Свечение ядра (аддитивное)
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawCube(cPos, 0.7f, 0.7f, 0.7f, new Color(200, 255, 235, 80));
            Raylib.EndBlendMode();

            // Заметный вращающийся кристалл-«алмаз» из кубов (как в Minecraft)
            Rlgl.PushMatrix();
            Rlgl.Translatef(cPos.X, cPos.Y, cPos.Z);
            Rlgl.Rotatef(rot * 180f / MathF.PI, 0f, 1f, 0f);
            var crystalCol = ShadeColor(new Color(215, 245, 255, 240), cl, cPos);
            var crystalDark = ShadeColor(new Color(140, 185, 210, 240), cl, cPos);
            var crystalCore = ShadeColor(new Color(255, 255, 255, 255), cl, cPos);

            Raylib.DrawCube(Vector3.Zero, 0.46f, 0.46f, 0.46f, crystalCore);
            Raylib.DrawCube(new Vector3(0f, 0.50f, 0f), 0.30f, 0.32f, 0.30f, crystalCol);
            Raylib.DrawCube(new Vector3(0f, -0.50f, 0f), 0.30f, 0.32f, 0.30f, crystalCol);
            Raylib.DrawCube(new Vector3(0.50f, 0f, 0f), 0.32f, 0.30f, 0.30f, crystalCol);
            Raylib.DrawCube(new Vector3(-0.50f, 0f, 0f), 0.32f, 0.30f, 0.30f, crystalCol);
            Raylib.DrawCube(new Vector3(0f, 0f, 0.50f), 0.30f, 0.30f, 0.32f, crystalDark);
            Raylib.DrawCube(new Vector3(0f, 0f, -0.50f), 0.30f, 0.30f, 0.32f, crystalDark);

            // Вращающееся кольцо из 4 кубов
            float ringRot = rot * 1.7f;
            for (int i = 0; i < 4; i++) {
                float a = ringRot + i * (MathF.PI / 2f);
                var ringPos = new Vector3(MathF.Cos(a) * 0.62f, 0f, MathF.Sin(a) * 0.62f);
                Raylib.DrawCube(ringPos, 0.26f, 0.12f, 0.26f, crystalCol);
            }
            Rlgl.PopMatrix();
        }

        // 3.3 Телеграф ударной волны Слизня Края: пульсирующее красное кольцо на земле
        if (_world.EndBoss is { Alive: true, IsDying: false } slamBoss && slamBoss.SlamWarningTimer > 0f) {
            float t = slamBoss.SlamWarningTimer / 0.6f;                 // 1 → 0
            byte alpha = (byte)(150 + 80 * MathF.Sin(time * 30f));      // быстрый пульс
            float radius = EndSlime.HalfSizeXZ + 3.2f + (1f - t) * 1.2f;
            var ground = FindGroundBelow(slamBoss.Position);
            if (ground.HasValue) {
                var gc = ground.Value;
                Raylib.DrawCircle3D(new Vector3(gc.X, gc.Y + 0.03f, gc.Z), radius, new Vector3(1, 0, 0), 90f,
                    new Color((byte)255, (byte)60, (byte)30, alpha));
                Raylib.DrawCircle3D(new Vector3(gc.X, gc.Y + 0.03f, gc.Z), radius * 0.62f, new Vector3(1, 0, 0), 90f,
                    new Color((byte)255, (byte)120, (byte)40, (byte)(alpha * 0.6f)));
            }
        }

        // 3.4 Телеграф сингулярности Истинного Слизня: фиолетовое сжимающееся кольцо вокруг игрока
        if (_world.TrueVoidBoss is { Alive: true, IsDying: false } tSlam && tSlam.SingularityWarningTimer > 0f) {
            float t = tSlam.SingularityWarningTimer / 0.6f;             // 1 → 0
            float radius = 4.5f * t + 1.2f;                             // сжимается к игроку
            byte alpha = (byte)(140 + 80 * MathF.Sin(time * 30f));
            var ground = FindGroundBelow(_session.Player.Position);
            if (ground.HasValue) {
                var gp = ground.Value;
                Raylib.DrawCircle3D(new Vector3(gp.X, gp.Y + 0.03f, gp.Z), radius, new Vector3(1, 0, 0), 90f,
                    new Color((byte)220, (byte)40, (byte)255, alpha));
                Raylib.DrawCircle3D(new Vector3(gp.X, gp.Y + 0.03f, gp.Z), radius * 0.55f, new Vector3(1, 0, 0), 90f,
                    new Color((byte)255, (byte)100, (byte)255, (byte)(alpha * 0.6f)));
            }
        }

        // 4. Draw Flying Arrows / Жемчуг / Око Эндера with lighting and fog
        foreach (var arr in _world.Arrows) {
            if (!arr.Alive) continue;
            var arrLight = GetLightFactor(arr.Position);
            var fwd = arr.Velocity.LengthSquared() > 0.01f ? Vector3.Normalize(arr.Velocity) : Vector3.UnitZ;
            Color bodyCol, tipCol, backCol;
            if (arr.IsSlimeSpit) {
                bodyCol = new Color(90, 200, 70, 255);    // плевок Слизня Края: тёмно-зелёный
                tipCol = new Color(160, 255, 120, 255);
                backCol = new Color(40, 110, 30, 255);
            } else if (arr.IsEnderPearl) {
                bodyCol = new Color(40, 200, 150, 255);   // жемчужно-зелёный
                tipCol = new Color(150, 255, 210, 255);
                backCol = new Color(20, 110, 85, 255);
            } else if (arr.IsEyeOfEnder) {
                bodyCol = new Color(200, 60, 60, 255);    // око: красное тело, янтарное яблоко
                tipCol = new Color(255, 200, 60, 255);
                backCol = new Color(110, 25, 20, 255);
            } else {
                bodyCol = new Color(175, 140, 95, 255);
                tipCol = new Color(190, 190, 190, 255);
                backCol = new Color(245, 245, 245, 255);
            }
            Raylib.DrawCube(arr.Position, 0.08f, 0.08f, 0.55f, ShadeColor(bodyCol, arrLight, arr.Position));
            Raylib.DrawCube(arr.Position + fwd * 0.26f, 0.12f, 0.12f, 0.12f, ShadeColor(tipCol, arrLight, arr.Position));
            Raylib.DrawCube(arr.Position - fwd * 0.24f, 0.14f, 0.14f, 0.14f, ShadeColor(backCol, arrLight, arr.Position));
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

    /// <summary>
    /// Выброшенные предметы (billboard'ы) — рисуются ПОСЛЕДНИМИ в кадре (после неба,
    /// облаков и всех сущностей). Включён depth-тест: непрозрачные пиксели корректно
    /// закрывают то, что позади, а прозрачные смешиваются с ним (моб/облако видны
    /// сквозь прозрачную часть предмета), потому что ничто не рисуется после них.
    /// </summary>
    public void DrawPickups(Camera3D camera) {
        Rlgl.EnableDepthTest();
        foreach (var p in _world.Pickups) {
            if (p.Quantity <= 0) continue;

            DrawSoftShadow(p.Position, 0.22f);

            float bob = MathF.Sin(p.BobPhase + (float)Raylib.GetTime() * 3f) * 0.12f;
            var pos = p.Position + new Vector3(0f, bob + 0.25f, 0f);
            byte tile = TextureAtlas.ItemTile(p.Definition.Id);
            var src = TextureAtlas.TilePixelRect(tile);

            var light = GetLightFactor(p.Position);
            Color tint = ShadeColor(Color.White, light, p.Position);
            Raylib.DrawBillboardRec(camera, TextureAtlas.Atlas, src, pos, new Vector2(0.4f, 0.4f), tint);
        }
    }

    /// <summary>Ищет твёрдую землю под точкой (до 6 блоков вниз); null — если не нашли.</summary>
    private Vector3? FindGroundBelow(Vector3 pos) {
        int px = (int)MathF.Floor(pos.X);
        int pz = (int)MathF.Floor(pos.Z);
        int startY = (int)MathF.Floor(pos.Y);
        for (int y = startY; y >= startY - 6; y--) {
            if (_world.IsSolidAt(new Vec3i(px, y, pz))) {
                return new Vector3(pos.X, y + 1.0f, pos.Z);
            }
        }
        return null;
    }

    private void DrawSoftShadow(Vector3 pos, float baseRadius) {
        if (!SaveSystem.EntityShadows) return;
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
        int count = SaveSystem.ParticlesMode == 0 ? 4 : (SaveSystem.ParticlesMode == 1 ? 8 : 16);
        Color baseCol = BlockTint(blockId);
        var center = new Vector3(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
        for (int i = 0; i < count; i++) {
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

    /// <summary>Луч лечения: искры летят от кристалла к боссу по прямой (без гравитации).</summary>
    public void SpawnHealBeamParticles(Vector3 from, Vector3 to, int count = 3) {
        for (int i = 0; i < count; i++) {
            var jitter = new Vector3(
                (ParticleRng.NextSingle() - 0.5f) * 0.5f,
                (ParticleRng.NextSingle() - 0.5f) * 0.5f,
                (ParticleRng.NextSingle() - 0.5f) * 0.5f);
            float life = 0.55f + ParticleRng.NextSingle() * 0.3f;
            _particles.Add(new VoxelParticle {
                Position = from + jitter,
                Velocity = Vector3.Zero,
                Color = new Color(90, 255, 160, 220),
                Size = 0.10f + ParticleRng.NextSingle() * 0.06f,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = true,
                IsHealBeam = true,
                Target = to + new Vector3(0f, EndSlime.HalfSizeY, 0f)
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

            if (p.IsHealBeam) {
                // Искра летит к цели по прямой с лёгкой волной
                var jitter = new Vector3(
                    MathF.Sin(p.MaxLifetime * 23f + i) * 0.15f, 0f,
                    MathF.Cos(p.MaxLifetime * 19f + i) * 0.15f);
                p.Position = Vector3.Lerp(p.Position, p.Target, 4.5f * dt) + jitter * dt;
            } else {
                p.Position += p.Velocity * dt;
                p.Velocity.Y -= 16f * dt;
                p.Velocity.X *= MathF.Exp(-2.0f * dt);
                p.Velocity.Z *= MathF.Exp(-2.0f * dt);
            }
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
        var id when id == GameData.BSapling.Id => new Color(60, 160, 40, 255),
        var id when id == GameData.BRedFlower.Id => new Color(220, 40, 40, 255),
        var id when id == GameData.BYellowFlower.Id => new Color(240, 210, 30, 255),
        var id when id == GameData.BSand.Id => new Color(220, 210, 155, 255),
        var id when id == GameData.BGravel.Id => new Color(130, 125, 125, 255),
        var id when id == GameData.BWater.Id => new Color(50, 100, 220, 200),
        var id when id == GameData.BLava.Id => new Color(245, 90, 20, 255),
        _ => new Color(150, 150, 155, 255),
    };

    public void SpawnEatParticles(Vector3 pos, Color color, int count = 6) {
        for (int i = 0; i < count; i++) {
            float vx = (ParticleRng.NextSingle() - 0.5f) * 1.6f;
            float vy = -0.2f + ParticleRng.NextSingle() * 1.2f;
            float vz = (ParticleRng.NextSingle() - 0.5f) * 1.6f;
            int d = ParticleRng.Next(-18, 19);
            var col = new Color(
                (byte)Math.Clamp(color.R + d, 0, 255),
                (byte)Math.Clamp(color.G + d, 0, 255),
                (byte)Math.Clamp(color.B + d, 0, 255),
                (byte)255);
            float size = 0.05f + ParticleRng.NextSingle() * 0.05f;
            float life = 0.35f + ParticleRng.NextSingle() * 0.30f;
            _particles.Add(new VoxelParticle {
                Position = pos + new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.2f, (ParticleRng.NextSingle() - 0.5f) * 0.15f, (ParticleRng.NextSingle() - 0.5f) * 0.2f),
                Velocity = new Vector3(vx, vy, vz),
                Color = col,
                Size = size,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = false
            });
        }
    }

    public void Dispose() {
        _world.OnBlockRemoved -= SpawnBlockParticles;
        _world.OnDustSpawned -= SpawnDustParticles;
        _world.OnCritSpawned -= SpawnCritParticles;
        _world.OnEatParticlesSpawned -= SpawnEatParticles;
        if (_materialReady) {
            unsafe {
                _material.Maps[(int)MaterialMapIndex.Albedo].Texture = default;
            }
            // UnloadMaterial уже выгружает шейдер материала — ручной UnloadShader
            // приводил к двойному освобождению и крашу 0xC0000374 на выходе.
            Raylib.UnloadMaterial(_material);
        }
        _materialReady = false;
    }
}
