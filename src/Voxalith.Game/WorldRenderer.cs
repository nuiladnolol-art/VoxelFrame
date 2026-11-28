using System.Numerics;
using Raylib_cs;
using Voxalith.Core;
using Voxalith.Core.World;

namespace Voxalith.Game;

/// <summary>
/// Рендер мира: меши чанков (пересборка по очереди), небо с солнцем и
/// звёздами, декор (факелы, костры, пламя), подсветка целевого блока.
/// </summary>
public sealed partial class WorldRenderer : IDisposable {
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
    private readonly List<(VoxelParticle Particle, Color Color)> _additiveParticlesBuf = new();
    private readonly List<GameChunk> _chunksBuf = new();
    private readonly List<(GameChunk Chunk, int DistSq)> _translucentChunks = new();

    private enum HeldItemShape : byte { Default, Sword, Pickaxe, Axe, Shovel, Hoe }
    private readonly record struct HeldItemVisual(Color MaterialColor, HeldItemShape Shape);
    private static readonly Dictionary<ushort, HeldItemVisual> _heldItemVisualCache = new();

    private static HeldItemVisual GetHeldItemVisual(Voxalith.Core.Inventory.ItemDefinition idef) {
        if (_heldItemVisualCache.TryGetValue(idef.Id, out var cached)) return cached;
        Color matCol = idef.Name switch {
            _ when idef.Name.Contains("Алмаз") => new Color(85, 230, 245, 255),
            _ when idef.Name.Contains("Золот") => new Color(245, 215, 50, 255),
            _ when idef.Name.Contains("Желез") => new Color(220, 220, 225, 255),
            _ when idef.Name.Contains("Камен") => new Color(130, 130, 135, 255),
            _ => new Color(165, 125, 75, 255)
        };
        HeldItemShape shape = idef.Name switch {
            _ when idef.Name.Contains("меч") => HeldItemShape.Sword,
            _ when idef.Name.Contains("кирка") => HeldItemShape.Pickaxe,
            _ when idef.Name.Contains("топор") => HeldItemShape.Axe,
            _ when idef.Name.Contains("лопата") => HeldItemShape.Shovel,
            _ when idef.Name.Contains("мотыга") => HeldItemShape.Hoe,
            _ => HeldItemShape.Default
        };
        cached = new HeldItemVisual(matCol, shape);
        _heldItemVisualCache[idef.Id] = cached;
        return cached;
    }

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
            _world.CopyChunksTo(_chunksBuf);
            foreach (var gc in _chunksBuf) {
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
            _world.CopyChunksTo(_chunksBuf);
            foreach (var gc in _chunksBuf) {
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

    // ── Декор: факелы, костры, пламя, подсветка ──────────────────────────────

    private readonly List<(Vector3 Pos, float Size)> _flameBatch = new();

    public void DrawDecorations(float dt) {
        _flameBatch.Clear();
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
                byte torchFacing = v.SubGridLayerMask;
                var woodCol = ShadeColor(new Color(130, 90, 48, 255), light, p);
                var headCol = ShadeColor(new Color(55, 42, 38, 255), light, p);

                Rlgl.PushMatrix();
                Vector3 flamePos;

                if (torchFacing == 1) {
                    Rlgl.Translatef(pos.X + 0.82f, pos.Y + 0.32f, pos.Z + 0.5f);
                    Rlgl.Rotatef(28f, 0f, 0f, 1f);
                    flamePos = new Vector3(pos.X + 0.62f, pos.Y + 0.65f, pos.Z + 0.5f);
                } else if (torchFacing == 2) {
                    Rlgl.Translatef(pos.X + 0.18f, pos.Y + 0.32f, pos.Z + 0.5f);
                    Rlgl.Rotatef(-28f, 0f, 0f, 1f);
                    flamePos = new Vector3(pos.X + 0.38f, pos.Y + 0.65f, pos.Z + 0.5f);
                } else if (torchFacing == 3) {
                    Rlgl.Translatef(pos.X + 0.5f, pos.Y + 0.32f, pos.Z + 0.82f);
                    Rlgl.Rotatef(-28f, 1f, 0f, 0f);
                    flamePos = new Vector3(pos.X + 0.5f, pos.Y + 0.65f, pos.Z + 0.62f);
                } else if (torchFacing == 4) {
                    Rlgl.Translatef(pos.X + 0.5f, pos.Y + 0.32f, pos.Z + 0.18f);
                    Rlgl.Rotatef(28f, 1f, 0f, 0f);
                    flamePos = new Vector3(pos.X + 0.5f, pos.Y + 0.65f, pos.Z + 0.38f);
                } else {
                    Rlgl.Translatef(pos.X + 0.5f, pos.Y + 0.25f, pos.Z + 0.5f);
                    flamePos = new Vector3(pos.X + 0.5f, pos.Y + 0.52f, pos.Z + 0.5f);
                }

                Raylib.DrawCube(Vector3.Zero, 0.10f, 0.50f, 0.10f, woodCol);
                Raylib.DrawCube(new Vector3(0f, 0.22f, 0f), 0.12f, 0.10f, 0.12f, headCol);
                Rlgl.PopMatrix();

                _flameBatch.Add((flamePos, 0.24f));
            } else if (v.TypeId == GameData.BEndPortalFrame.Id && (v.SubGridLayerMask & 1) != 0) {
                var gemPos = new Vector3(pos.X + 0.5f, pos.Y + 1.14f, pos.Z + 0.5f);
                var gemCol = ShadeColor(new Color(120, 235, 140, 255), light, p);
                Raylib.DrawCube(gemPos, 0.46f, 0.30f, 0.46f, gemCol);
            }
        }
        foreach (var pos in _world.Fire.Burning.Keys) {
            var fp = new Vector3(pos.X + 0.5f, pos.Y + 0.85f, pos.Z + 0.5f);
            if (Vector3.DistanceSquared(fp, camPos) <= maxDistSq) {
                _flameBatch.Add((fp, 0.5f));
            }
        }
        if (_flameBatch.Count > 0) {
            Raylib.BeginBlendMode(BlendMode.Additive);
            for (int i = 0; i < _flameBatch.Count; i++) {
                DrawFlame(_flameBatch[i].Pos, _flameBatch[i].Size);
            }
            Raylib.EndBlendMode();
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

    /// <summary>Пламя: пара аддитивных кубиков с дрожанием (вызывается внутри общего BeginBlendMode(Additive)).</summary>
    private static void DrawFlame(Vector3 pos, float size) {
        float jx = (ParticleRng.NextSingle() - 0.5f) * 0.12f;
        float jz = (ParticleRng.NextSingle() - 0.5f) * 0.12f;
        float pulse = 0.85f + 0.15f * MathF.Sin((float)(ParticleRng.NextDouble() * MathF.Tau));
        var inner = C(255, 180, 60, 200);
        var outer = C(255, 90, 20, 140);
        Raylib.DrawCube(pos + new Vector3(jx, 0.1f, jz), size * 0.5f * pulse, size * pulse, size * 0.5f * pulse, outer);
        Raylib.DrawCube(pos + new Vector3(-jx * 0.5f, 0.16f, -jz * 0.5f), size * 0.3f * pulse, size * 0.55f * pulse, size * 0.3f * pulse, inner);
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
