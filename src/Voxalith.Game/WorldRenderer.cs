using System.Numerics;
using Raylib_cs;
using Voxalith.Core;
using Voxalith.Core.World;

namespace Voxalith.Game;

/// <summary>
/// Рендер мира: меши чанков (пересборка по очереди), небо с солнцем и
/// звёздами, декор (факелы, костры, пламя), подсветка целевого блока.
/// </summary>
public sealed class WorldRenderer : IDisposable {
    private readonly GameSession _session;
    private GameWorld _world => _session.World;
    private Dimension _lastDimension = Dimension.Overworld;
    private readonly List<GameChunk> _rebuildQueue = new();
    private readonly HashSet<GameChunk> _rebuildSet = new();
    private readonly List<GameChunk> _drainBuf = new();
    private GameWorld? _hookedWorld;
    private Material _material;
    private bool _materialReady;

    private static readonly Random ParticleRng = new(2026);

    // ── Шейдер мира ─────────────────────────────────────────────────────────
    // Альфа цвета вершины — код материала (ChunkMesher.Mat). Освещение: полусферический
    // свет неба + направленный свет солнца/луны с картой теней + факельный свет; вода
    // с волновыми нормалями, френелевским отражением неба и бликом солнца; листва и трава
    // качаются на ветру; лава/огонь/светокамень светятся в HDR (подхватывает bloom).
    private const string VertexShaderSrc = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec3 vertexNormal;
        in vec4 vertexColor;
        uniform mat4 mvp;
        uniform float time;
        uniform float windStrength;
        out vec2 fragTexCoord;
        out vec4 fragColor;
        out vec3 fragWorldPos;
        out vec3 fragNormal;
        flat out int fragMat;
        void main() {
            vec3 p = vertexPosition;
            int mat = int(vertexColor.a * 255.0 + 0.5);
            if (windStrength > 0.0) {
                if (mat == 254) {
                    // Листва: смещение — функция позиции вершины, поэтому общие вершины
                    // соседних блоков двигаются одинаково и щелей не появляется.
                    float ph = p.x * 0.45 + p.z * 0.37 + p.y * 0.21;
                    p.x += sin(time * 1.7 + ph) * 0.028 * windStrength;
                    p.z += cos(time * 1.3 + ph * 1.27) * 0.028 * windStrength;
                } else if (mat == 253 && fract(p.y) > 0.5) {
                    // Трава/цветы: качается только верх стебля
                    float ph = floor(p.x) * 0.9 + floor(p.z) * 1.37;
                    float sway = sin(time * 2.1 + ph) * 0.6 + sin(time * 3.7 + ph * 2.3) * 0.25;
                    p.x += sway * 0.07 * windStrength;
                    p.z += cos(time * 1.9 + ph * 0.7) * 0.05 * windStrength;
                }
            }
            fragTexCoord = vertexTexCoord;
            fragColor = vertexColor;
            fragWorldPos = p;
            fragNormal = vertexNormal;
            fragMat = mat;
            gl_Position = mvp * vec4(p, 1.0);
        }
        """;

    private const string FragmentShaderSrc = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        in vec3 fragWorldPos;
        in vec3 fragNormal;
        flat in int fragMat;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform float time;
        uniform int dimension;
        uniform vec3 lightDir;
        uniform vec3 lightColor;
        uniform vec3 sunDir;
        uniform vec3 ambientTop;
        uniform vec3 ambientBottom;
        uniform vec3 skyZenith;
        uniform vec3 skyHorizon;
        uniform vec3 sunsetColor;
        uniform float sunsetAmount;
        uniform float nightFactor;
        uniform float overcast;
        uniform vec3 playerLightPos;
        uniform float playerLightRadius;
        uniform vec3 creeperLightPos;
        uniform float creeperLightRadius;
        uniform vec3 cameraPos;
        uniform vec3 fogColor;
        uniform float fogStart;
        uniform float fogEnd;
        uniform int fogMode;          // 0 = атмосферный (цвет неба по направлению), 1 = плоский
        uniform int shadowsOn;
        uniform sampler2D shadowMap;
        uniform mat4 lightVP;
        uniform float shadowTexel;
        uniform float hdr;            // 1 — кадр идёт в HDR-буфер пост-обработки
        out vec4 finalColor;

        const int M_LEAVES = 254;
        const int M_PLANT = 253;
        const int M_WATER = 252;
        const int M_LAVA = 251;
        const int M_PORTAL = 250;
        const int M_GLASS = 249;
        const int M_FIRE = 248;
        const int M_EMISSIVE = 247;

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
            // Ореол рассеяния вокруг солнца (Ми) — тот же, что в шейдере неба
            float mu = max(dot(d, sunDir), 0.0);
            vec3 glowCol = mix(vec3(1.0, 0.92, 0.75), sunsetColor, sunsetAmount);
            c += glowCol * (pow(mu, 8.0) * 0.22 + pow(mu, 64.0) * 0.35) * (1.0 - nightFactor) * (1.0 - overcast * 0.8);
            return c;
        }

        float shadowTap(vec2 uv, float z) {
            return z > texture(shadowMap, uv).r ? 0.0 : 1.0;
        }

        // PCF 3×3 с билинейным весом (4×4 выборки): мягкий край без «лесенки» текселей
        float sampleShadow(vec3 wpos, vec3 n, float ndl) {
            vec3 p = wpos + n * (shadowTexel * (0.20 + 0.40 * (1.0 - ndl)));
            vec4 lp = lightVP * vec4(p, 1.0);
            vec3 c = lp.xyz / lp.w * 0.5 + 0.5;
            if (c.x <= 0.0 || c.x >= 1.0 || c.y <= 0.0 || c.y >= 1.0 || c.z >= 1.0) return 1.0;
            float z = c.z - max(0.00015 * (1.0 - ndl), 0.00004);
            vec2 size = vec2(textureSize(shadowMap, 0));
            vec2 inv = 1.0 / size;
            vec2 st = c.xy * size - 0.5;
            vec2 base = floor(st);
            vec2 f = st - base;
            float s[16];
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                    s[y * 4 + x] = shadowTap((base + vec2(float(x) - 0.5, float(y) - 0.5)) * inv, z);
            float sum = 0.0;
            for (int y = 0; y < 3; y++) {
                for (int x = 0; x < 3; x++) {
                    float a = mix(s[y * 4 + x], s[y * 4 + x + 1], f.x);
                    float b = mix(s[(y + 1) * 4 + x], s[(y + 1) * 4 + x + 1], f.x);
                    sum += mix(a, b, f.y);
                }
            }
            float edge = max(abs(c.x - 0.5), abs(c.y - 0.5)) * 2.0;
            return mix(sum / 9.0, 1.0, smoothstep(0.82, 1.0, edge));
        }

        // Нормаль водной поверхности: сумма направленных волн (аналитический градиент)
        vec3 waterNormal(vec2 p, float t) {
            vec2 g = vec2(0.0);
            vec2 d1 = vec2(0.80, 0.60);  float a1 = dot(d1, p) * 1.3 + t * 1.6;  g += d1 * cos(a1) * 0.055;
            vec2 d2 = vec2(-0.45, 0.89); float a2 = dot(d2, p) * 2.1 + t * 2.2;  g += d2 * cos(a2) * 0.035;
            vec2 d3 = vec2(0.97, -0.24); float a3 = dot(d3, p) * 3.7 + t * 2.9;  g += d3 * cos(a3) * 0.020;
            vec2 d4 = vec2(-0.71, -0.70); float a4 = dot(d4, p) * 6.3 + t * 3.8; g += d4 * cos(a4) * 0.012;
            return normalize(vec3(-g.x, 1.0, -g.y));
        }

        void main() {
            vec4 texelColor = texture(texture0, fragTexCoord);
            if (texelColor.a < 0.05) discard;

            float sky = fragColor.r;       // чистый свет неба [0..1]
            float block = fragColor.g;     // блочный (факельный) свет [0..1]
            float ao = fragColor.b;        // запечённая окклюзия углов (AO) [0..1]
            vec3 n = normalize(fragNormal);
            float faceShade = n.y > 0.5 ? 1.0 : (n.y < -0.5 ? 0.55 : (abs(n.x) > 0.5 ? 0.82 : 0.68));
            vec3 toCam = cameraPos - fragWorldPos;
            float dist = length(toCam);
            vec3 V = toCam / max(dist, 1e-4);

            // Динамический свет от факела в руке и раздувающегося бабахера (квадратичный спад)
            if (playerLightRadius > 0.1) {
                float k = clamp(1.0 - distance(fragWorldPos, playerLightPos) / playerLightRadius, 0.0, 1.0);
                block = max(block, k * (14.0 / 15.0));
            }
            if (creeperLightRadius > 0.1) {
                float k = clamp(1.0 - distance(fragWorldPos, creeperLightPos) / creeperLightRadius, 0.0, 1.0);
                block = max(block, k);
            }

            bool isPlant = fragMat == M_PLANT || fragMat == M_FIRE;
            bool foliage = isPlant || fragMat == M_LEAVES;

            // ── Рассеянный свет неба (полусфера) ──
            float skyL = pow(sky, 1.4);
            float hemi = isPlant ? 0.85 : (n.y * 0.2 + 0.8);
            vec3 ambient = mix(ambientBottom, ambientTop, hemi);
            if (dimension == 0) ambient *= skyL;
            // AO модулирует рассеянный свет неба, сохраняя контраст в углах
            ambient *= ao;
            // Лёгкая классическая разница сторон X/Z — блоки остаются «читаемыми»
            ambient *= mix(1.0, faceShade, 0.35);

            // ── Прямой свет солнца / луны ──
            vec3 L = normalize(lightDir);
            float ndl = isPlant ? (0.55 + 0.45 * max(L.y, 0.0)) : max(dot(n, L), 0.0);
            float outdoor = smoothstep(0.45, 0.92, sky);
            float shadow = 1.0;
            if (shadowsOn == 1 && ndl > 0.0 && outdoor > 0.0) shadow = sampleShadow(fragWorldPos, isPlant ? vec3(0.0, 1.0, 0.0) : n, ndl);
            vec3 direct = lightColor * ndl * shadow * outdoor;
            // Отражённый от освещённой земли свет на стенах (bounce), смягчает теневые стороны
            if (dimension == 0 && n.y < 0.5) direct += lightColor * (0.16 * max(L.y, 0.0)) * skyL;
            // Просвечивание листвы и травы на контровом свете
            if (foliage) {
                float back = isPlant ? 0.35 : max(dot(-n, L), 0.0) * 0.45;
                direct += lightColor * back * shadow * outdoor * 0.6;
            }

            // ── Факелы ──
            vec3 torchColor = vec3(1.0, 0.74, 0.42);
            if (dimension == 1) torchColor = vec3(1.0, 0.66, 0.36);
            else if (dimension == 3) torchColor = vec3(0.85, 0.62, 1.0);
            vec3 torch = torchColor * pow(block, 1.5) * 1.3 * mix(0.75, 1.0, ao);

            // Минимальная подсветка пещер (различимость рельефа в темноте без факелов)
            vec3 caveFloor = vec3(0.065, 0.070, 0.085);
            vec3 totalLight = ambient + direct + torch + caveFloor;

            vec3 albedo = texelColor.rgb * colDiffuse.rgb;
            vec3 color = albedo * totalLight;
            float alpha = texelColor.a;

            // ── Светящиеся материалы (HDR → bloom) ──
            float emissiveBoost = mix(1.0, 1.8, hdr);
            if (fragMat == M_LAVA) {
                float pulse = 0.85 + 0.15 * sin(time * 1.4 + fragWorldPos.x * 0.6 + fragWorldPos.z * 0.45);
                color = albedo * 1.25 * pulse * emissiveBoost;
            } else if (fragMat == M_FIRE || fragMat == M_EMISSIVE) {
                color = albedo * 1.15 * emissiveBoost;
            } else if (fragMat == M_PORTAL) {
                float pulse = 0.8 + 0.2 * sin(time * 2.3 + fragWorldPos.y * 1.7);
                color = mix(color, albedo * 1.3 * emissiveBoost, 0.7 * pulse);
            }

            // ── Вода: отражение неба по Френелю + блик солнца ──
            if (fragMat == M_WATER && n.y > 0.5) {
                vec3 wn = waterNormal(fragWorldPos.xz, time);
                float viewAbove = step(fragWorldPos.y, cameraPos.y);
                float ndv = clamp(dot(wn, V), 0.0, 1.0);
                float fres = 0.02 + 0.98 * pow(1.0 - ndv, 5.0);
                vec3 R = reflect(-V, wn);
                R.y = abs(R.y);
                vec3 refl = skyColor(R);
                if (dimension != 0) refl = fogColor;
                float reflAmt = fres * viewAbove * (0.25 + 0.75 * outdoor);
                vec3 deep = albedo * totalLight * vec3(0.55, 0.80, 0.95);
                color = mix(deep, refl * (0.55 + 0.45 * outdoor), clamp(reflAmt, 0.0, 0.92));
                float spec = pow(max(dot(R, L), 0.0), 220.0);
                color += lightColor * spec * shadow * outdoor * viewAbove * mix(1.5, 3.0, hdr);
                alpha = mix(alpha, 1.0, clamp(fres * 0.85 * viewAbove, 0.0, 0.85));
            } else if (fragMat == M_GLASS) {
                vec3 R = reflect(-V, n);
                float fres = 0.04 + 0.96 * pow(1.0 - clamp(dot(n, V), 0.0, 1.0), 5.0);
                color += skyColor(R) * fres * 0.35 * outdoor;
            }

            if (hdr < 0.5) color = min(color, vec3(1.0));

            // ── Туман и воздушная перспектива ──
            float lin = clamp((dist - fogStart) / max(fogEnd - fogStart, 0.01), 0.0, 1.0);
            float fogF = lin * lin * (3.0 - 2.0 * lin);
            vec3 fogCol = fogColor;
            if (fogMode == 0) {
                fogCol = skyColor(-V);
                float haze = 1.0 - exp(-dist * (0.0016 + overcast * 0.004));
                fogF = max(fogF, haze * 0.28);
            }
            finalColor = vec4(mix(color, fogCol, fogF), alpha);
        }
        """;

    private readonly Dictionary<string, int> _locs = new();
    private int Loc(string name) => _locs.TryGetValue(name, out int l) ? l : -1;
    private static readonly string[] UniformNames = {
        "time", "windStrength", "dimension", "lightDir", "lightColor", "sunDir", "ambientTop", "ambientBottom",
        "skyZenith", "skyHorizon", "sunsetColor", "sunsetAmount", "nightFactor", "overcast",
        "playerLightPos", "playerLightRadius", "creeperLightPos", "creeperLightRadius", "cameraPos",
        "fogColor", "fogStart", "fogEnd", "fogMode", "shadowsOn", "shadowMap", "lightVP", "shadowTexel", "hdr",
    };
    private const int ShadowTextureSlot = 10;
    private readonly ShadowMapper _shadows = new();
    private bool _shadowBound;

    // Запрос полной пересборки мешей (смена мягкого освещения и т.п.)
    private static volatile bool _fullRemeshRequested;
    public static void RequestFullRemesh() => _fullRemeshRequested = true;

    // Шейдер неба (полноэкранный градиент по направлению взгляда)
    private Shader _skyShader;
    private bool _skyShaderReady;
    private readonly Dictionary<string, int> _skyLocs = new();
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
        public byte AmbientType; // 0=None, 1=Firefly, 2=Leaf, 3=Ember, 4=Bubble
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
            foreach (var name in UniformNames) _locs[name] = Raylib.GetShaderLocation(shader, name);
        }
        LoadSkyShader();
        _materialReady = true;

        HookWorldEvents(_world);
    }

    private void HookWorldEvents(GameWorld world) {
        UnhookWorldEvents();
        _hookedWorld = world;
        world.OnBlockRemoved += SpawnBlockParticles;
        world.OnDustSpawned += SpawnDustParticles;
        world.OnCritSpawned += SpawnCritParticles;
        world.OnEatParticlesSpawned += SpawnEatParticles;
        world.OnHealBeamSpawned += SpawnHealBeamParticles;
        world.OnSplashSpawned += SpawnSplashParticles;
        world.OnBlockDustSpawned += SpawnBlockDustParticles;
    }

    private void UnhookWorldEvents() {
        if (_hookedWorld == null) return;
        _hookedWorld.OnBlockRemoved -= SpawnBlockParticles;
        _hookedWorld.OnDustSpawned -= SpawnDustParticles;
        _hookedWorld.OnCritSpawned -= SpawnCritParticles;
        _hookedWorld.OnEatParticlesSpawned -= SpawnEatParticles;
        _hookedWorld.OnHealBeamSpawned -= SpawnHealBeamParticles;
        _hookedWorld.OnSplashSpawned -= SpawnSplashParticles;
        _hookedWorld.OnBlockDustSpawned -= SpawnBlockDustParticles;
        _hookedWorld = null;
    }

    public int MeshQueueCount => _rebuildQueue.Count;

    public void ProcessMeshQueue() {
        bool fullRemesh = _fullRemeshRequested;
        if (fullRemesh) _fullRemeshRequested = false;
        if (_lastDimension != _world.Dimension || fullRemesh) {
            _lastDimension = _world.Dimension;
            _rebuildQueue.Clear();
            _rebuildSet.Clear();
            _drainBuf.Clear();
            HookWorldEvents(_world);
            foreach (var gc in _world.Chunks) {
                gc.MeshDirty = true;
                if (_rebuildSet.Add(gc)) _rebuildQueue.Add(gc);
            }
        }

        _world.CollectMeshDirty(_drainBuf);
        if (_drainBuf.Count > 0) {
            foreach (var gc in _drainBuf) {
                if (_rebuildSet.Add(gc)) _rebuildQueue.Add(gc);
            }
            _drainBuf.Clear();
            var playerPos = _session.Player.Position;
            int pcx = (int)MathF.Floor(playerPos.X / Chunk.SizeX);
            int pcz = (int)MathF.Floor(playerPos.Z / Chunk.SizeZ);
            // Сортируем по убыванию расстояния: ближайшие окажутся в конце,
            // что позволяет извлекать их за O(1) через RemoveAt(_rebuildQueue.Count - 1).
            _rebuildQueue.Sort((a, b) => {
                int distA = Math.Abs(a.Coord.X - pcx) + Math.Abs(a.Coord.Z - pcz) + Math.Abs(a.Coord.Y - 1);
                int distB = Math.Abs(b.Coord.X - pcx) + Math.Abs(b.Coord.Z - pcz) + Math.Abs(b.Coord.Y - 1);
                return distB.CompareTo(distA);
            });
        }
        int budget = _session.Ui == UiState.Loading ? 16 : 4;
        while (_rebuildQueue.Count > 0 && budget-- > 0) {
            int lastIdx = _rebuildQueue.Count - 1;
            var gc = _rebuildQueue[lastIdx];
            _rebuildQueue.RemoveAt(lastIdx);
            _rebuildSet.Remove(gc);
            // Чанк мог быть выгружен или заменён (ресинк от сервера) между постановкой
            // в очередь и обработкой: сборка меша для отсоединённого объекта утекала в VRAM.
            if (!ReferenceEquals(_world.TryGetChunk(gc.Coord), gc)) continue;
            gc.UnloadMesh();
            var (opaque, trans) = ChunkMesher.Build(gc, _world);
            gc.Meshes.AddRange(opaque);
            gc.TranslucentMeshes.AddRange(trans);
            gc.MeshUploaded = gc.Meshes.Count > 0 || gc.TranslucentMeshes.Count > 0;
            if (!gc.MeshUploaded) gc.UnloadMesh();
        }
    }

    private void SetF(string n, float v) { int l = Loc(n); if (l != -1) Raylib.SetShaderValue(_material.Shader, l, v, ShaderUniformDataType.Float); }
    private void SetI(string n, int v) { int l = Loc(n); if (l != -1) Raylib.SetShaderValue(_material.Shader, l, v, ShaderUniformDataType.Int); }
    private void SetV3(string n, Vector3 v) { int l = Loc(n); if (l != -1) Raylib.SetShaderValue(_material.Shader, l, v, ShaderUniformDataType.Vec3); }

    /// <summary>Обновить модель атмосферы на кадр (вызывать до неба, теней и мира).</summary>
    public void UpdateAtmosphere(float dt) {
        Atmosphere.Update(_session.DayNight.TimeOfDay, _session.Weather, _world.Dimension, dt);
    }

    /// <summary>
    /// Проход теней. Вызывается ДО начала кадра (BeginDrawing/BeginScene):
    /// raylib не умеет вложенные render target-ы.
    /// </summary>
    public void RenderShadowPass() {
        if (!_materialReady) return;
        try {
            _shadows.Render(_world, _session.Camera.Position);
        } catch {
            SaveSystem.ShadowQuality = 0; // не роняем игру из-за драйвера — просто без теней
        }
    }

    private void BindShadowMap() {
        _shadowBound = false;
        int loc = Loc("shadowMap");
        bool on = _shadows.Active && loc != -1;
        SetI("shadowsOn", on ? 1 : 0);
        if (!on) return;
        Raylib.SetShaderValue(_material.Shader, loc, ShadowTextureSlot, ShaderUniformDataType.Int);
        int vp = Loc("lightVP");
        if (vp != -1) Raylib.SetShaderValueMatrix(_material.Shader, vp, _shadows.LightViewProj);
        SetF("shadowTexel", _shadows.TexelWorld);
        Rlgl.ActiveTextureSlot(ShadowTextureSlot);
        Rlgl.EnableTexture(_shadows.DepthTextureId);
        Rlgl.ActiveTextureSlot(0);
        _shadowBound = true;
    }

    private void UnbindShadowMap() {
        if (!_shadowBound) return;
        Rlgl.ActiveTextureSlot(ShadowTextureSlot);
        Rlgl.DisableTexture();
        Rlgl.ActiveTextureSlot(0);
        _shadowBound = false;
    }

    public void DrawWorld() {
        if (!_materialReady) return;

        SetF("time", (float)_session.TotalPlaySeconds);
        SetF("windStrength", SaveSystem.GraphicsQuality == SaveSystem.GraphicsPreset.Fast ? 0f : 1f + Atmosphere.Overcast * 1.6f);
        SetI("dimension", (int)_world.Dimension);
        SetV3("lightDir", Atmosphere.LightDir);
        SetV3("lightColor", Atmosphere.LightColor);
        SetV3("sunDir", Atmosphere.SunDir);
        SetV3("ambientTop", Atmosphere.AmbientTop);
        SetV3("ambientBottom", Atmosphere.AmbientBottom);
        SetV3("skyZenith", Atmosphere.Zenith);
        SetV3("skyHorizon", Atmosphere.Horizon);
        SetV3("sunsetColor", Atmosphere.SunsetColor);
        SetF("sunsetAmount", Atmosphere.SunsetAmount);
        SetF("nightFactor", Atmosphere.Night);
        SetF("overcast", Atmosphere.Overcast);
        SetF("hdr", PostProcessing.SceneIsHdr ? 1f : 0f);

        bool holdingTorch = _session.Player.SelectedEntry?.Item.Definition.Id == GameData.TorchItem.Id || _session.Player.OffhandItem?.Id == GameData.TorchItem.Id;
        SetV3("playerLightPos", _session.Player.Eye);
        SetF("playerLightRadius", (SaveSystem.DynamicLighting && holdingTorch) ? 14.0f : 0f);

        // Динамический свет раздувающегося белого крипера перед взрывом
        Vector3 creeperLightPos = Vector3.Zero;
        float creeperLightRadius = 0f;
        if (SaveSystem.DynamicLighting) {
            foreach (var h in _world.HostileMobs) {
                if (h.Type == HostileType.Creeper && h.FuseTimer > 0f && h.Alive) {
                    float rad = 6.0f + (h.FuseTimer / 1.3f) * 6.5f; // Свечение от 6 до 12.5 блоков
                    if (rad > creeperLightRadius) {
                        creeperLightRadius = rad;
                        creeperLightPos = h.Position + new Vector3(0f, 0.5f, 0f);
                    }
                }
            }
        }
        SetV3("creeperLightPos", creeperLightPos);
        SetF("creeperLightRadius", creeperLightRadius);

        var camPos = _session.Camera.Position;
        SetV3("cameraPos", camPos);

        // Подводный / лавовый / атмосферный туман
        var camCell = new Vec3i((int)MathF.Floor(camPos.X), (int)MathF.Floor(camPos.Y), (int)MathF.Floor(camPos.Z));
        var camVoxel = _world.GetVoxel(camCell);
        bool isWater = camVoxel.TypeId == GameData.BWater.Id;
        bool isLava = camVoxel.TypeId == GameData.BLava.Id;

        Vector3 fogVec;
        float fogStart, fogEnd;
        int fogMode = 1;
        if (isWater) {
            fogVec = new Vector3(0.04f, 0.16f, 0.36f) * MathF.Max(0.25f, 1f - Atmosphere.Night * 0.75f); // Глубокий лазурно-синий подводный туман
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
            if (_world.Dimension == Dimension.Overworld) fogMode = 0;
        }
        SetV3("fogColor", fogVec);
        SetF("fogStart", fogStart);
        SetF("fogEnd", fogEnd);
        SetI("fogMode", fogMode);

        BindShadowMap();

        unsafe {
            Rlgl.EnableBackfaceCulling();
            var playerPos = _session.Player.Position;
            int pcx = (int)MathF.Floor(playerPos.X / Chunk.SizeX);
            int pcz = (int)MathF.Floor(playerPos.Z / Chunk.SizeZ);
            int rDist = SaveSystem.RenderDistanceSetting;
            int maxDistSq = (rDist + 1) * (rDist + 1);

            const float chunkRadius = 27.8f; // Радиус описанной сферы для 32×32×32 чанка
            var camDiff = _session.Camera.Target - _session.Camera.Position;
            var camFwd = camDiff.LengthSquared() > 0.0001f ? Vector3.Normalize(camDiff) : _session.Player.Forward;
            float currentFov = _session.Camera.FovY;
            float aspect = (float)Raylib.GetScreenWidth() / Math.Max(1, Raylib.GetScreenHeight());
            float halfFovRad = currentFov * 0.5f * (MathF.PI / 180f);
            float tanFov = MathF.Tan(halfFovRad) * MathF.Max(1.0f, aspect) * 1.55f; // Динамический запас конуса под любой FOV и ультраширокие мониторы

            _translucentChunks.Clear();
            foreach (var gc in _world.Chunks) {
                int dx = gc.Coord.X - pcx;
                int dz = gc.Coord.Z - pcz;
                int dSq = dx * dx + dz * dz;

                // Меши далёких чанков выгружает UnloadFarChunks вместе с самим чанком
                // (renderDistance+2). Раньше здесь стояла отдельная выгрузка меша с
                // меньшим радиусом: чанк оставался в мире без меша и больше никогда
                // не пересобирался — навсегда невидимые дыры в мире.

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
            Rlgl.DisableBackfaceCulling();
        }
    }

    public void DrawWorldOpaque() => DrawWorld();

    /// <summary>
    /// Отрисовка полупрозрачного мира (вода, стекло, порталы, растительность).
    /// Вызывается ПОСЛЕ отрисовки сущностей и декораций (факелов), чтобы вода и порталы
    /// накладывались поверх них прозрачным слоем с корректным Z-тестом (без просвета сквозь воду).
    /// </summary>
    public void DrawWorldTranslucent() {
        // Карта теней остаётся привязанной к слоту после непрозрачного прохода
        // (её читает и вода); отвязываем в конце в любом случае.
        if (!_materialReady || _translucentChunks.Count == 0) { UnbindShadowMap(); return; }
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
        UnbindShadowMap();
    }

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
            var camDiff = _session.Camera.Target - _session.Camera.Position;
            var camFwd = camDiff.LengthSquared() > 0.0001f ? Vector3.Normalize(camDiff) : _session.Player.Forward;
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
                float szY = 1.005f;
                if (GameData.IsSlab(v.TypeId)) {
                    p.Y = t.Y + 0.25f;
                    szY = 0.505f;
                }
                Raylib.DrawCubeWires(p, 1.005f, szY, 1.005f, Color.Black);

                // Визуальные стадии разрушения блока (трещины)
                if (_session.Player.BreakProgress > 0f && _session.Player.BreakDuration > 0f) {
                    float ratio = Math.Clamp(_session.Player.BreakProgress / _session.Player.BreakDuration, 0f, 1f);
                    DrawBlockDamageOverlay(p, ratio);
                }
            }
        }
        UpdateAmbientParticles(dt);
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

    private static readonly (Vector2 A, Vector2 B)[] CrackPattern = new (Vector2, Vector2)[] {
        (new Vector2(-0.08f, -0.06f), new Vector2(0.04f, 0.08f)),
        (new Vector2(0.04f, 0.08f), new Vector2(0.12f, -0.04f)),
        (new Vector2(-0.08f, -0.06f), new Vector2(-0.14f, 0.05f)),
        (new Vector2(0.04f, 0.08f), new Vector2(0.02f, 0.28f)),
        (new Vector2(0.02f, 0.28f), new Vector2(-0.18f, 0.38f)),
        (new Vector2(0.12f, -0.04f), new Vector2(0.32f, -0.15f)),
        (new Vector2(-0.14f, 0.05f), new Vector2(-0.35f, -0.12f)),
        (new Vector2(-0.08f, -0.06f), new Vector2(0.02f, -0.32f)),
        (new Vector2(0.02f, 0.28f), new Vector2(0.24f, 0.44f)),
        (new Vector2(-0.18f, 0.38f), new Vector2(-0.42f, 0.42f)),
        (new Vector2(0.32f, -0.15f), new Vector2(0.44f, 0.12f)),
        (new Vector2(0.32f, -0.15f), new Vector2(0.42f, -0.40f)),
        (new Vector2(-0.35f, -0.12f), new Vector2(-0.45f, 0.18f)),
        (new Vector2(-0.35f, -0.12f), new Vector2(-0.38f, -0.42f)),
        (new Vector2(0.02f, -0.32f), new Vector2(0.18f, -0.45f)),
        (new Vector2(0.02f, -0.32f), new Vector2(-0.22f, -0.44f)),
        (new Vector2(-0.14f, 0.05f), new Vector2(-0.12f, 0.25f)),
        (new Vector2(0.12f, -0.04f), new Vector2(0.15f, 0.18f)),
        (new Vector2(-0.08f, -0.06f), new Vector2(-0.25f, -0.22f)),
        (new Vector2(0.04f, 0.08f), new Vector2(0.25f, 0.22f)),
        (new Vector2(0.24f, 0.44f), new Vector2(0.40f, 0.35f)),
        (new Vector2(-0.38f, -0.42f), new Vector2(-0.15f, -0.45f)),
        (new Vector2(0.42f, -0.40f), new Vector2(0.25f, -0.42f)),
        (new Vector2(-0.42f, 0.42f), new Vector2(-0.45f, -0.10f)),
        (new Vector2(0.44f, 0.12f), new Vector2(0.10f, 0.45f)),
        (new Vector2(-0.22f, -0.44f), new Vector2(-0.42f, -0.30f)),
        (new Vector2(0.18f, -0.45f), new Vector2(0.40f, -0.25f)),
        (new Vector2(-0.25f, -0.22f), new Vector2(-0.35f, 0.05f))
    };

    /// <summary>Отрисовка стадий трещин (разрушения блока) на всех 6 гранях.</summary>
    private static void DrawBlockDamageOverlay(Vector3 pos, float ratio) {
        int count = Math.Clamp((int)(ratio * CrackPattern.Length), 2, CrackPattern.Length);
        float offset = 0.503f;

        Rlgl.SetTexture(0);
        Rlgl.EnableDepthTest();
        Rlgl.Color4ub(15, 15, 15, 230);
        Rlgl.Begin((int)DrawMode.Lines);

        for (int i = 0; i < count; i++) {
            var (a, b) = CrackPattern[i];
            // +Z
            Rlgl.Vertex3f(pos.X + a.X, pos.Y + a.Y, pos.Z + offset);
            Rlgl.Vertex3f(pos.X + b.X, pos.Y + b.Y, pos.Z + offset);
            // -Z
            Rlgl.Vertex3f(pos.X - a.X, pos.Y + a.Y, pos.Z - offset);
            Rlgl.Vertex3f(pos.X - b.X, pos.Y + b.Y, pos.Z - offset);
            // +X
            Rlgl.Vertex3f(pos.X + offset, pos.Y + a.Y, pos.Z + a.X);
            Rlgl.Vertex3f(pos.X + offset, pos.Y + b.Y, pos.Z + b.X);
            // -X
            Rlgl.Vertex3f(pos.X - offset, pos.Y + a.Y, pos.Z - a.X);
            Rlgl.Vertex3f(pos.X - offset, pos.Y + b.Y, pos.Z - b.X);
            // +Y
            Rlgl.Vertex3f(pos.X + a.X, pos.Y + offset, pos.Z + a.Y);
            Rlgl.Vertex3f(pos.X + b.X, pos.Y + offset, pos.Z + b.Y);
            // -Y
            Rlgl.Vertex3f(pos.X + a.X, pos.Y - offset, pos.Z - a.Y);
            Rlgl.Vertex3f(pos.X + b.X, pos.Y - offset, pos.Z - b.Y);
        }
        Rlgl.End();

        // Затемняющий полупрозрачный куб стадии поломки
        byte alpha = (byte)(15 + ratio * 65);
        Raylib.DrawCube(pos, 1.002f, 1.002f, 1.002f, new Color((byte)10, (byte)10, (byte)10, alpha));
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
        // Цвет тумана = цвет горизонта из модели атмосферы (как у неба и шейдера мира).
        var c = Atmosphere.Horizon;
        return new Color((byte)Math.Clamp((int)(c.X * 255f), 0, 255), (byte)Math.Clamp((int)(c.Y * 255f), 0, 255),
            (byte)Math.Clamp((int)(c.Z * 255f), 0, 255), (byte)255);
    }

    public float GetFogFactor(Vector3 pos) {
        float dist = Vector3.Distance(_session.Camera.Position, pos);
        float maxRenderDist = SaveSystem.RenderDistanceSetting * (float)Chunk.SizeX;
        bool nether = _world.Dimension == Dimension.Nether;
        bool end = _world.Dimension == Dimension.End;
        bool voidDim = _world.Dimension == Dimension.Void;
        float fogStart = nether ? 20.0f : end ? 40.0f : voidDim ? 35.0f : MathF.Max(15.0f, maxRenderDist * 0.60f);
        float fogEnd = nether ? 68.0f : end ? 140.0f : voidDim ? 110.0f : MathF.Max(28.0f, maxRenderDist * 0.96f);
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

    private static Color GetBlockMaterialColor(ushort blockId) {
        if (blockId == GameData.BGrass.Id) return new Color(95, 170, 60, 255);
        if (blockId == GameData.BDirt.Id || blockId == GameData.BFarmland.Id) return new Color(134, 96, 67, 255);
        if (blockId == GameData.BStone.Id || blockId == GameData.BBedrock.Id) return new Color(128, 128, 128, 255);
        if (blockId == GameData.BCobblestone.Id || blockId == GameData.BMossyCobblestone.Id) return new Color(110, 110, 115, 255);
        if (blockId == GameData.BLog.Id) return new Color(103, 76, 45, 255);
        if (blockId == GameData.BPlanks.Id || blockId == GameData.BDoorLower.Id || blockId == GameData.BDoorUpper.Id) return new Color(185, 148, 90, 255);
        if (blockId == GameData.BLeaves.Id || blockId == GameData.BSapling.Id) return new Color(50, 140, 45, 255);
        if (blockId == GameData.BSand.Id || blockId == GameData.BChiseledSandstone.Id) return new Color(225, 210, 145, 255);
        if (blockId == GameData.BGravel.Id) return new Color(140, 135, 135, 255);
        if (blockId == GameData.BGlass.Id) return new Color(210, 240, 255, 180);
        if (blockId == GameData.BWater.Id) return new Color(60, 120, 235, 200);
        if (blockId == GameData.BLava.Id || blockId == GameData.BFire.Id) return new Color(255, 110, 20, 255);
        if (blockId == GameData.BCoalOre.Id) return new Color(45, 45, 45, 255);
        if (blockId == GameData.BIronOre.Id) return new Color(190, 165, 145, 255);
        if (blockId == GameData.BGoldOre.Id) return new Color(245, 215, 50, 255);
        if (blockId == GameData.BDiamondOre.Id) return new Color(85, 230, 245, 255);
        if (blockId == GameData.BObsidian.Id || blockId == GameData.BObsidianPillar.Id) return new Color(35, 25, 50, 255);
        if (blockId == GameData.BWorkbench.Id) return new Color(150, 115, 70, 255);
        if (blockId == GameData.BFurnace.Id) return new Color(105, 105, 110, 255);
        if (blockId == GameData.BChest.Id) return new Color(165, 115, 55, 255);
        if (blockId == GameData.BBed.Id || blockId == GameData.BBedHead.Id) return new Color(210, 45, 45, 255);
        if (blockId == GameData.BTNT.Id) return new Color(220, 50, 40, 255);
        if (blockId == GameData.BNetherrack.Id || blockId == GameData.BNetherBrick.Id) return new Color(125, 45, 45, 255);
        if (blockId == GameData.BSoulSand.Id) return new Color(85, 60, 50, 255);
        if (blockId == GameData.BGlowstone.Id) return new Color(245, 210, 100, 255);
        if (blockId == GameData.BNetherQuartzOre.Id) return new Color(230, 225, 220, 255);
        if (blockId == GameData.BEndStone.Id) return new Color(225, 228, 170, 255);
        if (blockId == GameData.BEndPortalFrame.Id) return new Color(60, 100, 85, 255);
        if (blockId == GameData.BEnderCrystal.Id) return new Color(220, 120, 255, 255);
        if (blockId == GameData.BChorusPlant.Id || blockId == GameData.BChorusFlower.Id) return new Color(175, 125, 175, 255);
        if (blockId == GameData.BWheatCrop.Id || blockId == GameData.BCarrotCrop.Id || blockId == GameData.BPotatoCrop.Id || blockId == GameData.BTallGrass.Id)
            return new Color(110, 180, 55, 255);
        if (blockId == GameData.BRedFlower.Id) return new Color(235, 45, 45, 255);
        if (blockId == GameData.BYellowFlower.Id) return new Color(250, 225, 45, 255);
        if (blockId == GameData.BJukebox.Id) return new Color(135, 95, 60, 255);
        return new Color(145, 145, 150, 255);
    }

    private void DrawHeldItemModel(int itemId, Vector3 itemPos, Vector3 light, Vector3 worldPos, bool isOffhand = false) {
        if (itemId <= 0) return;

        // 1. Блок в руке
        if (GameData.TryGetBlock((ushort)itemId, out var bDef) || (GameData.TryGetBlockByItem((ushort)itemId, out var bDef2) && (bDef = bDef2) != null)) {
            if (bDef.Id == GameData.BTorch.Id) {
                Raylib.DrawCube(itemPos, 0.05f, 0.26f, 0.05f, ShadeColor(new Color(135, 95, 55, 255), light, worldPos));
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.12f, 0f), 0.07f, 0.08f, 0.07f, new Color(255, 220, 70, 255));
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.13f, 0f), 0.04f, 0.06f, 0.04f, new Color(255, 110, 20, 255));
            } else {
                Color bCol = GetBlockMaterialColor(bDef.Id);
                float bSz = isOffhand ? 0.16f : 0.22f;
                Raylib.DrawCube(itemPos, bSz, bSz, bSz, ShadeColor(bCol, light, worldPos));
            }
            return;
        }

        // 2. Инструменты, оружие и предметы
        if (GameData.Items.TryGetValue((ushort)itemId, out var idef)) {
            Color matCol = idef.Name switch {
                _ when idef.Name.Contains("Алмаз") => new Color(85, 230, 245, 255),
                _ when idef.Name.Contains("Золот") => new Color(245, 215, 50, 255),
                _ when idef.Name.Contains("Желез") => new Color(220, 220, 225, 255),
                _ when idef.Name.Contains("Камен") => new Color(130, 130, 135, 255),
                _ => new Color(165, 125, 75, 255)
            };
            Color handleCol = ShadeColor(new Color(125, 85, 45, 255), light, worldPos);
            Color headCol = ShadeColor(matCol, light, worldPos);

            if (idef.Name.Contains("меч")) {
                Raylib.DrawCube(itemPos - new Vector3(0f, 0.08f, 0f), 0.04f, 0.14f, 0.04f, handleCol);
                Raylib.DrawCube(itemPos, 0.14f, 0.03f, 0.06f, headCol);
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.18f, 0f), 0.07f, 0.34f, 0.03f, headCol);
            } else if (idef.Name.Contains("кирка")) {
                Raylib.DrawCube(itemPos, 0.04f, 0.38f, 0.04f, handleCol);
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.17f, 0f), 0.24f, 0.06f, 0.06f, headCol);
            } else if (idef.Name.Contains("топор")) {
                Raylib.DrawCube(itemPos, 0.04f, 0.38f, 0.04f, handleCol);
                Raylib.DrawCube(itemPos + new Vector3(0.07f, 0.14f, 0f), 0.12f, 0.12f, 0.05f, headCol);
            } else if (idef.Name.Contains("лопата")) {
                Raylib.DrawCube(itemPos, 0.04f, 0.38f, 0.04f, handleCol);
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.17f, 0f), 0.11f, 0.12f, 0.03f, headCol);
            } else if (idef.Name.Contains("мотыга")) {
                Raylib.DrawCube(itemPos, 0.04f, 0.38f, 0.04f, handleCol);
                Raylib.DrawCube(itemPos + new Vector3(0.06f, 0.17f, 0f), 0.12f, 0.05f, 0.04f, headCol);
            } else if (itemId == GameData.BowItem.Id) {
                Raylib.DrawCube(itemPos, 0.05f, 0.38f, 0.08f, handleCol);
                Raylib.DrawCube(itemPos + new Vector3(0f, 0f, 0.04f), 0.02f, 0.36f, 0.01f, ShadeColor(Color.White, light, worldPos));
            } else if (itemId == GameData.TorchItem.Id) {
                Raylib.DrawCube(itemPos, 0.05f, 0.26f, 0.05f, handleCol);
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.12f, 0f), 0.07f, 0.08f, 0.07f, new Color(255, 220, 70, 255));
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.13f, 0f), 0.04f, 0.06f, 0.04f, new Color(255, 110, 20, 255));
            } else if (itemId == GameData.AppleItem.Id || itemId == GameData.GoldenAppleItem.Id) {
                Color aCol = itemId == GameData.GoldenAppleItem.Id ? new Color(255, 215, 0, 255) : new Color(225, 45, 40, 255);
                Raylib.DrawCube(itemPos, 0.13f, 0.13f, 0.13f, ShadeColor(aCol, light, worldPos));
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.08f, 0f), 0.02f, 0.04f, 0.02f, handleCol);
            } else if (itemId == GameData.BreadItem.Id) {
                Raylib.DrawCube(itemPos, 0.11f, 0.09f, 0.22f, ShadeColor(new Color(205, 145, 75, 255), light, worldPos));
            } else if (itemId == GameData.RawBeefItem.Id || itemId == GameData.CookedBeefItem.Id || itemId == GameData.RawPorkItem.Id || itemId == GameData.CookedPorkItem.Id) {
                Color mCol = (itemId == GameData.CookedBeefItem.Id || itemId == GameData.CookedPorkItem.Id) ? new Color(145, 75, 50, 255) : new Color(215, 95, 95, 255);
                Raylib.DrawCube(itemPos, 0.14f, 0.07f, 0.18f, ShadeColor(mCol, light, worldPos));
            } else if (itemId == GameData.EnderPearlItem.Id || itemId == GameData.EyeOfEnderItem.Id) {
                Color pCol = itemId == GameData.EyeOfEnderItem.Id ? new Color(75, 205, 145, 255) : new Color(35, 135, 125, 255);
                Raylib.DrawCube(itemPos, 0.12f, 0.12f, 0.12f, ShadeColor(pCol, light, worldPos));
            } else if (itemId == GameData.TotemItem.Id) {
                Raylib.DrawCube(itemPos, 0.09f, 0.18f, 0.06f, new Color(255, 215, 40, 255));
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.04f, 0f), 0.18f, 0.05f, 0.04f, new Color(110, 225, 140, 255));
            } else if (itemId == GameData.IronIngotItem.Id || itemId == GameData.GoldIngotItem.Id) {
                Color ingCol = itemId == GameData.GoldIngotItem.Id ? new Color(250, 220, 50, 255) : new Color(225, 225, 230, 255);
                Raylib.DrawCube(itemPos, 0.09f, 0.06f, 0.18f, ShadeColor(ingCol, light, worldPos));
            } else {
                Raylib.DrawCube(itemPos, 0.11f, 0.22f, 0.11f, ShadeColor(matCol, light, worldPos));
            }
        }
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
        Rlgl.Rotatef(yaw * 180f / MathF.PI, 0f, 1f, 0f);

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
                DrawHeldItemModel(offhandItemId, offPos, light, pos, isOffhand: true);
            }
        }

        // Правая рука (качается при ходьбе + наносит удар/копает)
        var rArmPos = new Vector3(0.33f, 0.02f + armPunch * 0.2f, -walkSwing * 0.3f + armPunch * 0.4f);
        Raylib.DrawCube(rArmPos, 0.18f, 0.65f, 0.18f, skinColor);
        Raylib.DrawCube(rArmPos + new Vector3(0f, 0.18f, 0f), 0.182f, 0.30f, 0.182f, shirtColor);

        // 3D предмет в правой руке
        if (selectedItemId > 0) {
            var itemPos = rArmPos + new Vector3(0f, -0.22f, 0.20f + armPunch * 0.2f);
            DrawHeldItemModel(selectedItemId, itemPos, light, pos, isOffhand: false);
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

        // 3. Draw Hostile Mobs (Zombie, Creeper, Skeleton, Spider, Enderman, Blaze, Mini-Bosses)
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

            } else if (h.Type == HostileType.Creeper) {
                // ── CREEPER MODEL (4 шагающие ноги, камуфляж, раздувание) ──
                bool isFlashing = h.FuseTimer > 0f && (int)(h.FuseTimer * 12f) % 2 == 0;
                var baseCreeper = isFlashing ? Color.White : (h.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(30, 185, 55, 255));
                var creeperGreen = isFlashing ? ShadeColor(Color.White, light, hPos) : ShadeColor(baseCreeper, light, hPos);
                var outlineColor = ShadeColor(new Color(15, 60, 20, 180), light, hPos);

                float fuseScale = 1.0f + MathF.Min(0.28f, h.FuseTimer * 0.22f);
                Rlgl.Scalef(fuseScale, fuseScale, fuseScale);

                // Body
                Raylib.DrawCube(new Vector3(0f, -0.025f, 0f), 0.5f, 0.65f, 0.35f, creeperGreen);
                Raylib.DrawCubeWires(new Vector3(0f, -0.025f, 0f), 0.502f, 0.652f, 0.352f, outlineColor);

                // Head
                var headPos = new Vector3(0f, 0.54f, 0f);
                Raylib.DrawCube(headPos, 0.48f, 0.48f, 0.48f, creeperGreen);
                Raylib.DrawCubeWires(headPos, 0.482f, 0.482f, 0.482f, outlineColor);

                // Каноничное лицо крипера: черные глаза, переносица и опущенный рот
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
                Raylib.DrawCube(new Vector3(-0.16f, -0.52f, 0.18f + legSwing), 0.18f, 0.36f, 0.18f, creeperGreen);
                Raylib.DrawCube(new Vector3(0.16f, -0.52f, 0.18f - legSwing), 0.18f, 0.36f, 0.18f, creeperGreen);
                Raylib.DrawCube(new Vector3(-0.16f, -0.52f, -0.18f - legSwing), 0.18f, 0.36f, 0.18f, creeperGreen);
                Raylib.DrawCube(new Vector3(0.16f, -0.52f, -0.18f + legSwing), 0.18f, 0.36f, 0.18f, creeperGreen);

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

    private float _ambientTimer;

    private void UpdateAmbientParticles(float dt) {
        if (SaveSystem.ParticlesMode == 0) return;
        _ambientTimer -= dt;
        if (_ambientTimer > 0f) return;
        _ambientTimer = SaveSystem.ParticlesMode == 1 ? 0.45f : 0.22f;

        var pPos = _session.Player.Position;
        int px = (int)MathF.Floor(pPos.X);
        int py = (int)MathF.Floor(pPos.Y);
        int pz = (int)MathF.Floor(pPos.Z);

        var eyeVox = _world.GetVoxel(new Vec3i((int)MathF.Floor(_session.Player.Eye.X), (int)MathF.Floor(_session.Player.Eye.Y), (int)MathF.Floor(_session.Player.Eye.Z)));
        bool inWater = eyeVox.TypeId == GameData.BWater.Id;

        // 1. Подводные пузырьки воздуха
        if (inWater) {
            float ox = (ParticleRng.NextSingle() - 0.5f) * 3.5f;
            float oy = -0.5f + ParticleRng.NextSingle() * 1.5f;
            float oz = (ParticleRng.NextSingle() - 0.5f) * 3.5f;
            _particles.Add(new VoxelParticle {
                Position = _session.Player.Eye + new Vector3(ox, oy, oz),
                Velocity = new Vector3(0f, 1.4f, 0f),
                Color = new Color(200, 235, 255, 170),
                Size = 0.05f + ParticleRng.NextSingle() * 0.04f,
                Lifetime = 1.2f + ParticleRng.NextSingle() * 0.8f,
                MaxLifetime = 2.0f,
                AmbientType = 4
            });
            return;
        }

        // 2. Нижний мир — восходящие адские искры
        if (_world.Dimension == Dimension.Nether) {
            float ox = (ParticleRng.NextSingle() - 0.5f) * 16.0f;
            float oy = -2.0f + ParticleRng.NextSingle() * 8.0f;
            float oz = (ParticleRng.NextSingle() - 0.5f) * 16.0f;
            _particles.Add(new VoxelParticle {
                Position = pPos + new Vector3(ox, oy, oz),
                Velocity = new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.5f, 1.0f + ParticleRng.NextSingle() * 1.5f, (ParticleRng.NextSingle() - 0.5f) * 0.5f),
                Color = ParticleRng.NextDouble() < 0.6 ? new Color(255, 150, 30, 240) : new Color(255, 60, 20, 220),
                Size = 0.06f + ParticleRng.NextSingle() * 0.05f,
                Lifetime = 1.4f + ParticleRng.NextSingle() * 1.0f,
                MaxLifetime = 2.4f,
                AmbientType = 3
            });
            return;
        }

        // 3. Обычный мир: Ночные светлячки
        if (_world.Dimension == Dimension.Overworld && _session.DayNight.SkyFactor < 0.22f) {
            float ox = (ParticleRng.NextSingle() - 0.5f) * 20.0f;
            float oz = (ParticleRng.NextSingle() - 0.5f) * 20.0f;
            int gX = px + (int)ox, gZ = pz + (int)oz;
            int surfY = _world.Generator.SurfaceHeight(gX, gZ);
            float spawnY = surfY + 0.8f + ParticleRng.NextSingle() * 2.2f;
            _particles.Add(new VoxelParticle {
                Position = new Vector3(gX + ParticleRng.NextSingle(), spawnY, gZ + ParticleRng.NextSingle()),
                Velocity = new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.4f, (ParticleRng.NextSingle() - 0.5f) * 0.3f, (ParticleRng.NextSingle() - 0.5f) * 0.4f),
                Color = new Color(210, 255, 90, 220),
                Size = 0.07f + ParticleRng.NextSingle() * 0.04f,
                Lifetime = 2.5f + ParticleRng.NextSingle() * 2.0f,
                MaxLifetime = 4.5f,
                AmbientType = 1
            });
        }

        // 4. Обычный мир: падающие листья с крон деревьев
        if (_world.Dimension == Dimension.Overworld) {
            int rx = px + ParticleRng.Next(-8, 9);
            int rz = pz + ParticleRng.Next(-8, 9);
            int ry = py + ParticleRng.Next(-1, 8);
            var checkVox = _world.GetVoxel(new Vec3i(rx, ry, rz));
            if (checkVox.TypeId == GameData.BLeaves.Id) {
                _particles.Add(new VoxelParticle {
                    Position = new Vector3(rx + ParticleRng.NextSingle(), ry - 0.1f, rz + ParticleRng.NextSingle()),
                    Velocity = new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.6f, -0.35f, (ParticleRng.NextSingle() - 0.5f) * 0.6f),
                    Color = ParticleRng.NextDouble() < 0.7 ? new Color(55, 140, 42, 230) : new Color(190, 135, 30, 230),
                    Size = 0.08f + ParticleRng.NextSingle() * 0.04f,
                    Lifetime = 3.0f + ParticleRng.NextSingle() * 2.0f,
                    MaxLifetime = 5.0f,
                    AmbientType = 2
                });
            }
        }
    }

    public void DrawParticles(float dt) {
        for (int i = _particles.Count - 1; i >= 0; i--) {
            var p = _particles[i];
            p.Lifetime -= dt;
            if (p.Lifetime <= 0f) {
                // Swap-remove: RemoveAt сдвигал весь хвост списка на каждую
                // просроченную частицу (O(n²) при взрыве с сотнями частиц).
                int last = _particles.Count - 1;
                _particles[i] = _particles[last];
                _particles.RemoveAt(last);
                continue;
            }

            if (p.IsHealBeam) {
                // Искра летит к цели по прямой с лёгкой волной
                var jitter = new Vector3(
                    MathF.Sin(p.MaxLifetime * 23f + i) * 0.15f, 0f,
                    MathF.Cos(p.MaxLifetime * 19f + i) * 0.15f);
                p.Position = Vector3.Lerp(p.Position, p.Target, 4.5f * dt) + jitter * dt;
            } else if (p.AmbientType == 1) {
                // Firefly (светлячок): плавное хаотическое парение без гравитации
                p.Position += p.Velocity * dt;
                p.Velocity.X += (ParticleRng.NextSingle() - 0.5f) * 1.5f * dt;
                p.Velocity.Y += (ParticleRng.NextSingle() - 0.5f) * 1.5f * dt;
                p.Velocity.Z += (ParticleRng.NextSingle() - 0.5f) * 1.5f * dt;
                p.Velocity *= MathF.Exp(-1.2f * dt);
            } else if (p.AmbientType == 2) {
                // Leaf (падающий лист): медленный планирующий спуск с покачиванием
                p.Position += p.Velocity * dt;
                p.Position.X += MathF.Sin(p.Lifetime * 5.0f + i) * 0.4f * dt;
                p.Position.Z += MathF.Cos(p.Lifetime * 4.0f + i) * 0.4f * dt;
                p.Velocity.Y = Math.Clamp(p.Velocity.Y - 1.2f * dt, -0.65f, 0.5f);
                p.Velocity.X *= MathF.Exp(-1.0f * dt);
                p.Velocity.Z *= MathF.Exp(-1.0f * dt);
            } else if (p.AmbientType == 3) {
                // Nether ember (искра): подъем вверх с турбулентностью
                p.Position += p.Velocity * dt;
                p.Position.X += MathF.Sin(p.Lifetime * 6.0f + i) * 0.25f * dt;
                p.Position.Z += MathF.Cos(p.Lifetime * 5.0f + i) * 0.25f * dt;
                p.Velocity.Y += 0.8f * dt;
                p.Velocity.X *= MathF.Exp(-1.0f * dt);
                p.Velocity.Z *= MathF.Exp(-1.0f * dt);
            } else if (p.AmbientType == 4) {
                // Underwater bubble (пузырек воздуха): всплытие
                p.Position += p.Velocity * dt;
                p.Velocity.Y = 1.6f;
                p.Position.X += MathF.Sin(p.Lifetime * 8.0f + i) * 0.15f * dt;
                p.Position.Z += MathF.Cos(p.Lifetime * 8.0f + i) * 0.15f * dt;
            } else {
                p.Position += p.Velocity * dt;
                p.Velocity.Y -= 16f * dt;
                p.Velocity.X *= MathF.Exp(-2.0f * dt);
                p.Velocity.Z *= MathF.Exp(-2.0f * dt);
            }
            _particles[i] = p;

            float alphaRatio = Math.Clamp(p.Lifetime / p.MaxLifetime, 0f, 1f);
            byte alpha = (byte)(p.Color.A * alphaRatio);
            if (p.AmbientType == 1) {
                float pulse = 0.6f + 0.4f * MathF.Sin(p.Lifetime * 8f);
                alpha = (byte)(alpha * pulse);
            }
            var col = new Color(p.Color.R, p.Color.G, p.Color.B, alpha);

            if (p.IsCrit || p.AmbientType == 1 || p.AmbientType == 3) {
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

    public void SpawnSplashParticles(Vector3 pos, int count = 8) {
        int actualCount = SaveSystem.ParticlesMode == 0 ? count / 2 : count;
        for (int i = 0; i < actualCount; i++) {
            float angle = ParticleRng.NextSingle() * MathF.Tau;
            float r = 0.8f + ParticleRng.NextSingle() * 2.2f;
            float vx = MathF.Cos(angle) * r;
            float vy = 1.6f + ParticleRng.NextSingle() * 2.2f;
            float vz = MathF.Sin(angle) * r;
            float size = 0.06f + ParticleRng.NextSingle() * 0.05f;
            float life = 0.35f + ParticleRng.NextSingle() * 0.25f;
            _particles.Add(new VoxelParticle {
                Position = pos + new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.3f, 0.05f, (ParticleRng.NextSingle() - 0.5f) * 0.3f),
                Velocity = new Vector3(vx, vy, vz),
                Color = new Color(185, 225, 255, 220),
                Size = size,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = false
            });
        }
    }

    public void SpawnBlockDustParticles(Vector3 pos, ushort blockId, int count = 6) {
        int actualCount = SaveSystem.ParticlesMode == 0 ? count / 2 : count;
        var baseColor = BlockTint(blockId);
        for (int i = 0; i < actualCount; i++) {
            float vx = (ParticleRng.NextSingle() - 0.5f) * 1.6f;
            float vy = 0.6f + ParticleRng.NextSingle() * 1.4f;
            float vz = (ParticleRng.NextSingle() - 0.5f) * 1.6f;
            int d = ParticleRng.Next(-16, 17);
            var col = new Color(
                (byte)Math.Clamp(baseColor.R + d, 0, 255),
                (byte)Math.Clamp(baseColor.G + d, 0, 255),
                (byte)Math.Clamp(baseColor.B + d, 0, 255),
                (byte)255);
            float size = 0.07f + ParticleRng.NextSingle() * 0.06f;
            float life = 0.35f + ParticleRng.NextSingle() * 0.25f;
            _particles.Add(new VoxelParticle {
                Position = pos + new Vector3((ParticleRng.NextSingle() - 0.5f) * 0.4f, 0.05f, (ParticleRng.NextSingle() - 0.5f) * 0.4f),
                Velocity = new Vector3(vx, vy, vz),
                Color = col,
                Size = size,
                Lifetime = life,
                MaxLifetime = life,
                IsCrit = false
            });
        }
    }

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
        UnhookWorldEvents();
        _shadows.Dispose();
        if (_skyShaderReady) { Raylib.UnloadShader(_skyShader); _skyShaderReady = false; }
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
