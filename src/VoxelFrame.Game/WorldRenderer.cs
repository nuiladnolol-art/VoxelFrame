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
        void main() {
            fragTexCoord = vertexTexCoord;
            fragColor = vertexColor;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string FragmentShaderSrc = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform float skyFactor;
        uniform float sunAngle;
        out vec4 finalColor;
        void main() {
            vec4 texelColor = texture(texture0, fragTexCoord);
            if (texelColor.a < 0.5) discard;

            float sun = fragColor.r;       // Солнечный свет [0..1]
            float block = fragColor.g;     // Блочный (факельный) свет [0..1]
            float faceDir = fragColor.b;   // Направленное затенение граней [0.55..1.0]

            // Нелинейная кривая освещенности Minecraft (Alpha / Modern)
            float sunCurve = pow(sun * max(skyFactor, 0.04), 1.35);
            float blockCurve = pow(block, 1.25);

            // Теплый уютный оттенок факельного света + чистый дневной свет
            vec3 torchLight = vec3(1.0, 0.82, 0.55) * blockCurve;
            vec3 sunLight = vec3(0.96, 0.96, 1.0) * sunCurve;
            vec3 ambient = vec3(0.12, 0.12, 0.16);

            vec3 totalLight = (sunLight + torchLight + ambient) * faceDir;
            totalLight = clamp(totalLight, 0.08, 1.0);

            finalColor = texelColor * colDiffuse * vec4(totalLight, 1.0);
        }
        """;

    private int _skyFactorLoc = -1;
    private int _sunAngleLoc = -1;

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
        }
        _materialReady = true;
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

        unsafe {
            Rlgl.EnableBackfaceCulling();
            foreach (var gc in _world.Chunks) {
                if (!gc.MeshUploaded) continue;
                foreach (var m in gc.Meshes)
                    Raylib.DrawMesh(m, _material, Matrix4x4.Identity);
            }
        }
    }

    // ── Небо ─────────────────────────────────────────────────────────────────

    public void DrawSky() {
        float f = _session.DayNight.SkyFactor;
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();

        var top = LerpColor(C(8, 10, 26, 255), C(92, 150, 240, 255), f);
        var bottom = LerpColor(C(16, 18, 40, 255), C(178, 208, 244, 255), f);
        Raylib.DrawRectangleGradientV(0, 0, w, h, top, bottom);

        // Звёзды (ночью).
        if (f < 0.6f) {
            float alpha = (0.6f - f) / 0.6f;
            int starCount = 130;
            for (int i = 0; i < starCount; i++) {
                var rng = new Random(i * 7919 + 17);
                float sx = rng.NextSingle() * w;
                float sy = rng.NextSingle() * h * 0.55f;
                byte a = (byte)(alpha * (90 + rng.Next(120)));
                Raylib.DrawPixel((int)sx, (int)sy, C(255, 255, 240, (int)a));
            }
        }

        // Квадратные Солнце и Луна (Minecraft Alpha style)
        float u = _session.DayNight.TimeOfDay;
        float sunAngle = 2f * MathF.PI * (u - 0.25f);
        var sunDir = new Vector2(MathF.Cos(sunAngle), MathF.Sin(sunAngle));
        DrawCelestial(sunDir, f, C(255, 255, 230, 255), w, h, true);
        DrawCelestial(-sunDir, f, C(235, 240, 255, 255), w, h, false);
    }

    private static void DrawCelestial(Vector2 dir, float skyFactor, Color color, int w, int h, bool isSun) {
        if (dir.Y < -0.08f) return;
        float visible = Math.Clamp(dir.Y * 2.5f, 0f, 1f);
        float x = w / 2f + dir.X * w * 0.44f;
        float y = h * 0.72f - dir.Y * h * 0.52f;
        byte a = (byte)(visible * 255 * (0.4f + 0.6f * skyFactor));
        color.A = a;
        float sz = MathF.Min(w, h) * (isSun ? 0.085f : 0.065f);
        Raylib.DrawRectangle((int)(x - sz / 2f), (int)(y - sz / 2f), (int)sz, (int)sz, color);
        if (isSun) {
            Raylib.DrawRectangleLines((int)(x - sz / 2f), (int)(y - sz / 2f), (int)sz, (int)sz, C(255, 240, 160, (int)(a * 0.7f)));
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

    private static Color C(int r, int g, int b, int a = 255) => new((byte)r, (byte)g, (byte)b, (byte)a);

    private static Color LerpColor(Color a, Color b, float t) => C(
        (int)MathF.Round(a.R + (b.R - a.R) * t),
        (int)MathF.Round(a.G + (b.G - a.G) * t),
        (int)MathF.Round(a.B + (b.B - a.B) * t),
        255);

    // ── Декор: факелы, костры, пламя, подсветка ──────────────────────────────

    public void DrawDecorations(float dt) {
        unsafe {
            foreach (var pos in _world.DecorPositions) {
                var v = _world.GetVoxel(pos);
                if (v.TypeId != GameData.BTorch.Id) continue;
                byte tile = (byte)TextureAtlas.TTorch;
                var src = new Rectangle(
                    tile % TextureAtlas.Cols * TextureAtlas.TilePx,
                    tile / TextureAtlas.Cols * TextureAtlas.TilePx,
                    TextureAtlas.TilePx, TextureAtlas.TilePx);
                var p = new Vector3(pos.X + 0.5f, pos.Y + 0.5f, pos.Z + 0.5f);
                var size = new Vector2(0.4f, 0.4f);
                Raylib.DrawBillboardRec(_session.Camera, TextureAtlas.Atlas, src, p, size, Color.White);
                // Пламя над телом.
                var flamePos = new Vector3(p.X, p.Y + 0.35f, p.Z);
                DrawFlame(flamePos, 0.3f, dt);
            }
            foreach (var pos in _world.Fire.Burning.Keys) {
                DrawFlame(new Vector3(pos.X + 0.5f, pos.Y + 0.85f, pos.Z + 0.5f), 0.5f, dt);
            }
        }
        if (_session.HasTarget) {
            var t = _session.TargetBlock;
            var p = new Vector3(t.X + 0.5f, t.Y + 0.5f, t.Z + 0.5f);
            Raylib.DrawCubeWires(p, 1.005f, 1.005f, 1.005f, Color.Black);
        }
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

    public void DrawEntities(Camera3D camera) {
        float time = (float)Raylib.GetTime();
        // 1. Draw Item Pickups as Billboards
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
            
            Raylib.DrawBillboardRec(camera, TextureAtlas.Atlas, src, pos, new Vector2(0.4f, 0.4f), Color.White);
        }

        // 2. Draw Animals (Pig, Cow, Sheep) with 3D model & animations
        foreach (var a in _world.Animals) {
            if (!a.Alive) continue;
            DrawSoftShadow(a.Position - new Vector3(0f, a.HalfSizeY, 0f), a.HalfSizeX + 0.05f);

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
                var pigPink = a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(240, 160, 160, 255);
                var snoutColor = a.HurtTime > 0f ? new Color(220, 60, 60, 255) : new Color(220, 120, 130, 255);
                var pigOutline = new Color(40, 20, 20, 180);

                // Body (bottom at -0.15, top at +0.40)
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
                Raylib.DrawCube(pigHead + new Vector3(-0.16f, 0.08f, 0.23f), 0.06f, 0.06f, 0.01f, Color.Black);
                Raylib.DrawCube(pigHead + new Vector3(0.16f, 0.08f, 0.23f), 0.06f, 0.06f, 0.01f, Color.Black);

                // 4 Legs (height 0.35, bottom at -0.45, center Y = -0.275)
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
                var cowWhite = a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(240, 240, 240, 255);
                var cowBlack = a.HurtTime > 0f ? new Color(180, 40, 40, 255) : new Color(55, 45, 40, 255);
                var snoutColor = a.HurtTime > 0f ? new Color(200, 70, 70, 255) : new Color(175, 140, 140, 255);
                var hornColor = new Color(210, 210, 205, 255);
                var udderPink = new Color(245, 170, 180, 255);
                var cowOutline = new Color(30, 20, 15, 180);

                // Body (height 0.60, center Y = +0.15)
                Raylib.DrawCube(new Vector3(0f, 0.15f, 0f), 0.75f, 0.60f, 1.05f, cowWhite);
                Raylib.DrawCubeWires(new Vector3(0f, 0.15f, 0f), 0.752f, 0.602f, 1.052f, cowOutline);
                // Black patches on body
                Raylib.DrawCube(new Vector3(-0.20f, 0.22f, 0.15f), 0.38f, 0.35f, 0.45f, cowBlack);
                Raylib.DrawCube(new Vector3(0.20f, 0.12f, -0.20f), 0.38f, 0.38f, 0.45f, cowBlack);

                // Udder (center Y = -0.10, Z = -0.25)
                Raylib.DrawCube(new Vector3(0f, -0.10f, -0.25f), 0.20f, 0.12f, 0.22f, udderPink);

                // Head (center Y = +0.40, Z = +0.65)
                var headPos = new Vector3(0f, 0.40f, 0.65f);
                Raylib.DrawCube(headPos, 0.45f, 0.45f, 0.45f, cowBlack);
                Raylib.DrawCubeWires(headPos, 0.452f, 0.452f, 0.452f, cowOutline);

                // Snout
                Raylib.DrawCube(headPos + new Vector3(0f, -0.08f, 0.26f), 0.32f, 0.20f, 0.14f, snoutColor);

                // Horns
                Raylib.DrawCube(headPos + new Vector3(-0.22f, 0.24f, -0.05f), 0.08f, 0.16f, 0.08f, hornColor);
                Raylib.DrawCube(headPos + new Vector3(0.22f, 0.24f, -0.05f), 0.08f, 0.16f, 0.08f, hornColor);

                // Eyes
                Raylib.DrawCube(headPos + new Vector3(-0.18f, 0.06f, 0.23f), 0.06f, 0.06f, 0.01f, Color.Black);
                Raylib.DrawCube(headPos + new Vector3(0.18f, 0.06f, 0.23f), 0.06f, 0.06f, 0.01f, Color.Black);

                // 4 Legs (height 0.55, bottom at -0.65, center Y = -0.375)
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
                var woolWhite = a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(235, 235, 235, 255);
                var skinTan = a.HurtTime > 0f ? new Color(210, 80, 80, 255) : new Color(225, 205, 185, 255);
                var sheepOutline = new Color(50, 50, 50, 180);

                // Fluffy Wool Body (height 0.65, center Y = +0.10)
                Raylib.DrawCube(new Vector3(0f, 0.10f, 0f), 0.80f, 0.65f, 0.95f, woolWhite);
                Raylib.DrawCubeWires(new Vector3(0f, 0.10f, 0f), 0.802f, 0.652f, 0.952f, sheepOutline);

                // Head (center Y = +0.28, Z = +0.58)
                var headPos = new Vector3(0f, 0.28f, 0.58f);
                Raylib.DrawCube(headPos, 0.35f, 0.38f, 0.42f, skinTan);
                Raylib.DrawCubeWires(headPos, 0.352f, 0.382f, 0.422f, sheepOutline);
                // Wool fleece hat on top of head
                Raylib.DrawCube(headPos + new Vector3(0f, 0.18f, -0.05f), 0.36f, 0.14f, 0.34f, woolWhite);

                // Eyes
                Raylib.DrawCube(headPos + new Vector3(-0.14f, 0.04f, 0.215f), 0.05f, 0.05f, 0.01f, Color.Black);
                Raylib.DrawCube(headPos + new Vector3(0.14f, 0.04f, 0.215f), 0.05f, 0.05f, 0.01f, Color.Black);

                // 4 Legs (height 0.45, bottom at -0.55, center Y = -0.325)
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

        // 3. Draw Hostile Mobs (Zombie, Creeper, Skeleton, Spider) with exact ground contact & animations
        foreach (var h in _world.HostileMobs) {
            if (!h.Alive) continue;
            DrawSoftShadow(h.Position - new Vector3(0f, h.HalfSizeY, 0f), 0.45f);

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
                // ── ZOMBIE MODEL (HalfSizeY = 0.85, bottom at -0.85) ───────
                var skinColor = h.HurtTime > 0f ? new Color(220, 60, 60, 255) : new Color(45, 125, 45, 255);
                var shirtColor = h.HurtTime > 0f ? new Color(180, 50, 50, 255) : new Color(40, 140, 160, 255);
                var pantsColor = h.HurtTime > 0f ? new Color(140, 40, 40, 255) : new Color(40, 40, 110, 255);
                var outlineColor = new Color(20, 20, 20, 180);

                // Body (center Y = +0.15, height 0.70, top = +0.50, bottom = -0.20)
                Raylib.DrawCube(new Vector3(0f, 0.15f, 0f), 0.6f, 0.70f, 0.35f, shirtColor);
                Raylib.DrawCubeWires(new Vector3(0f, 0.15f, 0f), 0.602f, 0.702f, 0.352f, outlineColor);

                // Head (center Y = +0.74, height 0.48, bottom = +0.50)
                var headPos = new Vector3(0f, 0.74f, 0f);
                Raylib.DrawCube(headPos, 0.48f, 0.48f, 0.48f, skinColor);
                Raylib.DrawCubeWires(headPos, 0.482f, 0.482f, 0.482f, outlineColor);

                Raylib.DrawCube(headPos + new Vector3(-0.12f, 0.04f, 0.245f), 0.09f, 0.08f, 0.01f, Color.Black);
                Raylib.DrawCube(headPos + new Vector3(0.12f, 0.04f, 0.245f), 0.09f, 0.08f, 0.01f, Color.Black);
                Raylib.DrawCube(headPos + new Vector3(0f, -0.1f, 0.245f), 0.16f, 0.06f, 0.01f, Color.Black);

                // Arms (raised forward, center Y = +0.35)
                var leftArm = new Vector3(-0.42f, 0.35f, 0.25f + MathF.Sin(time * 4f) * 0.03f);
                var rightArm = new Vector3(0.42f, 0.35f, 0.25f - MathF.Sin(time * 4f) * 0.03f);
                Raylib.DrawCube(leftArm, 0.2f, 0.2f, 0.55f, skinColor);
                Raylib.DrawCubeWires(leftArm, 0.202f, 0.202f, 0.552f, outlineColor);
                Raylib.DrawCube(rightArm, 0.2f, 0.2f, 0.55f, skinColor);
                Raylib.DrawCubeWires(rightArm, 0.202f, 0.202f, 0.552f, outlineColor);

                // Legs (height 0.65, bottom = -0.85, center Y = -0.525)
                var leftLeg = new Vector3(-0.16f, -0.525f, walkSwing * 0.3f);
                var rightLeg = new Vector3(0.16f, -0.525f, -walkSwing * 0.3f);
                Raylib.DrawCube(leftLeg, 0.22f, 0.65f, 0.22f, pantsColor);
                Raylib.DrawCubeWires(leftLeg, 0.222f, 0.652f, 0.222f, outlineColor);
                Raylib.DrawCube(rightLeg, 0.22f, 0.65f, 0.22f, pantsColor);
                Raylib.DrawCubeWires(rightLeg, 0.222f, 0.652f, 0.222f, outlineColor);

            } else if (h.Type == HostileType.Skeleton) {
                // ── SKELETON MODEL (HalfSizeY = 0.85, bottom at -0.85) ─────
                var boneColor = h.HurtTime > 0f ? new Color(220, 60, 60, 255) : new Color(205, 205, 205, 255);
                var ribColor = h.HurtTime > 0f ? new Color(180, 40, 40, 255) : new Color(130, 130, 130, 255);
                var bowColor = new Color(125, 80, 45, 255);
                var stringColor = new Color(225, 225, 225, 255);
                var outlineColor = new Color(30, 30, 30, 180);

                // Body (center Y = +0.15, height 0.70)
                Raylib.DrawCube(new Vector3(0f, 0.15f, 0f), 0.45f, 0.70f, 0.22f, boneColor);
                Raylib.DrawCubeWires(new Vector3(0f, 0.15f, 0f), 0.452f, 0.702f, 0.222f, outlineColor);

                // Ribcage ribs (тёмные рёбра на груди)
                Raylib.DrawCube(new Vector3(0f, 0.28f, 0.115f), 0.36f, 0.05f, 0.01f, ribColor);
                Raylib.DrawCube(new Vector3(0f, 0.18f, 0.115f), 0.36f, 0.05f, 0.01f, ribColor);
                Raylib.DrawCube(new Vector3(0f, 0.08f, 0.115f), 0.36f, 0.05f, 0.01f, ribColor);

                // Head (center Y = +0.72)
                var headPos = new Vector3(0f, 0.72f, 0f);
                Raylib.DrawCube(headPos, 0.44f, 0.44f, 0.44f, boneColor);
                Raylib.DrawCubeWires(headPos, 0.442f, 0.442f, 0.442f, outlineColor);

                // Skull dark eyes and nose
                Raylib.DrawCube(headPos + new Vector3(-0.10f, 0.04f, 0.225f), 0.09f, 0.09f, 0.01f, Color.Black);
                Raylib.DrawCube(headPos + new Vector3(0.10f, 0.04f, 0.225f), 0.09f, 0.09f, 0.01f, Color.Black);
                Raylib.DrawCube(headPos + new Vector3(0f, -0.09f, 0.225f), 0.12f, 0.05f, 0.01f, Color.Black);

                // Arms (держат лук перед собой)
                var leftArm = new Vector3(-0.30f, 0.35f, 0.22f);
                var rightArm = new Vector3(0.30f, 0.35f, 0.22f);
                Raylib.DrawCube(leftArm, 0.12f, 0.12f, 0.50f, boneColor);
                Raylib.DrawCube(rightArm, 0.12f, 0.12f, 0.50f, boneColor);

                // Деревянный лук в руках скелета
                var bowPos = new Vector3(0.15f, 0.35f, 0.48f);
                Raylib.DrawCube(bowPos, 0.06f, 0.55f, 0.06f, bowColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, 0.24f, -0.06f), 0.05f, 0.12f, 0.06f, bowColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, -0.24f, -0.06f), 0.05f, 0.12f, 0.06f, bowColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, 0f, -0.08f), 0.02f, 0.52f, 0.02f, stringColor);

                // Legs (height 0.65, bottom = -0.85, center Y = -0.525)
                var leftLeg = new Vector3(-0.12f, -0.525f, walkSwing * 0.3f);
                var rightLeg = new Vector3(0.12f, -0.525f, -walkSwing * 0.3f);
                Raylib.DrawCube(leftLeg, 0.14f, 0.65f, 0.14f, boneColor);
                Raylib.DrawCube(rightLeg, 0.14f, 0.65f, 0.14f, boneColor);

            } else if (h.Type == HostileType.Spider) {
                // ── SPIDER MODEL (HalfSizeY = 0.40, bottom at -0.40) ───────
                var spiderColor = h.HurtTime > 0f ? new Color(220, 50, 50, 255) : new Color(35, 25, 20, 255);
                var eyeRed = new Color(220, 20, 20, 255);

                // Body (Head + Abdomen)
                var headPos = new Vector3(0f, -0.05f, 0.30f);
                Raylib.DrawCube(headPos, 0.45f, 0.35f, 0.40f, spiderColor);
                var abdomenPos = new Vector3(0f, 0.05f, -0.30f);
                Raylib.DrawCube(abdomenPos, 0.65f, 0.50f, 0.65f, spiderColor);

                // Glowing Red Eyes
                Raylib.DrawCube(headPos + new Vector3(-0.12f, 0.02f, 0.205f), 0.06f, 0.06f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(0.12f, 0.02f, 0.205f), 0.06f, 0.06f, 0.01f, eyeRed);

                // 8 Legs (extending down to -0.40)
                for (int i = 0; i < 4; i++) {
                    float zOff = (i - 1.5f) * 0.22f;
                    float legSwing = MathF.Sin(time * 12f + i * 1.2f) * 0.15f;
                    var legL = new Vector3(-0.45f, -0.22f + legSwing, zOff);
                    var legR = new Vector3(0.45f, -0.22f - legSwing, zOff);
                    Raylib.DrawCube(legL, 0.55f, 0.08f, 0.08f, spiderColor);
                    Raylib.DrawCube(legR, 0.55f, 0.08f, 0.08f, spiderColor);
                }

            } else if (h.Type == HostileType.Creeper) {
                // ── CREEPER MODEL (HalfSizeY = 0.75, bottom at -0.75) ──────
                bool isFlashing = h.FuseTimer > 0f && (int)(h.FuseTimer * 12f) % 2 == 0;
                var creeperGreen = isFlashing ? Color.White : (h.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(30, 185, 55, 255));
                var outlineColor = new Color(15, 60, 20, 180);

                float fuseScale = 1.0f + MathF.Min(0.25f, h.FuseTimer * 0.20f);
                Rlgl.Scalef(fuseScale, fuseScale, fuseScale);

                // Body (height 0.65, center Y = -0.025)
                Raylib.DrawCube(new Vector3(0f, -0.025f, 0f), 0.5f, 0.65f, 0.35f, creeperGreen);
                Raylib.DrawCubeWires(new Vector3(0f, -0.025f, 0f), 0.502f, 0.652f, 0.352f, outlineColor);

                // Head (center Y = +0.54)
                var headPos = new Vector3(0f, 0.54f, 0f);
                Raylib.DrawCube(headPos, 0.48f, 0.48f, 0.48f, creeperGreen);
                Raylib.DrawCubeWires(headPos, 0.482f, 0.482f, 0.482f, outlineColor);

                Raylib.DrawCube(headPos + new Vector3(-0.11f, 0.06f, 0.245f), 0.10f, 0.10f, 0.01f, Color.Black);
                Raylib.DrawCube(headPos + new Vector3(0.11f, 0.06f, 0.245f), 0.10f, 0.10f, 0.01f, Color.Black);
                Raylib.DrawCube(headPos + new Vector3(0f, -0.06f, 0.245f), 0.10f, 0.12f, 0.01f, Color.Black);
                Raylib.DrawCube(headPos + new Vector3(-0.07f, -0.12f, 0.245f), 0.06f, 0.12f, 0.01f, Color.Black);
                Raylib.DrawCube(headPos + new Vector3(0.07f, -0.12f, 0.245f), 0.06f, 0.12f, 0.01f, Color.Black);

                // 4 Legs (height 0.40, bottom = -0.75, center Y = -0.55)
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

        // 4. Draw Flying Arrows (стрелы скелетов)
        foreach (var arr in _world.Arrows) {
            if (!arr.Alive) continue;
            var fwd = arr.Velocity.LengthSquared() > 0.01f ? Vector3.Normalize(arr.Velocity) : Vector3.UnitZ;
            Raylib.DrawCube(arr.Position, 0.06f, 0.06f, 0.55f, new Color(175, 140, 95, 255));
            Raylib.DrawCube(arr.Position + fwd * 0.26f, 0.10f, 0.10f, 0.10f, new Color(190, 190, 190, 255));
            Raylib.DrawCube(arr.Position - fwd * 0.24f, 0.12f, 0.12f, 0.12f, new Color(245, 245, 245, 255));
        }

        // 5. Falling blocks (collapse debris) — как кубы с цветом блока.
        foreach (var f in _world.FallingBlocks) {
            if (!f.Alive) continue;
            var tint = BlockTint(f.Block.Id);
            Raylib.DrawCube(f.Position, 1.0f, 1.0f, 1.0f, tint);
            Raylib.DrawCubeWires(f.Position, 1.002f, 1.002f, 1.002f, new Color(20, 20, 25, 180));
        }
    }

    private void DrawSoftShadow(Vector3 pos, float baseRadius) {
        float groundY = pos.Y;
        int px = (int)MathF.Floor(pos.X);
        int pz = (int)MathF.Floor(pos.Z);
        for (int y = (int)MathF.Floor(pos.Y); y >= 0; y--) {
            if (_world.IsSolidAt(new Vec3i(px, y, pz))) {
                groundY = y + 1.01f;
                break;
            }
        }
        
        float dist = pos.Y - groundY;
        if (dist > 6f || dist < -0.5f) return;
        
        float scale = Math.Clamp(1.0f - dist / 6.0f, 0f, 1f);
        float radius = baseRadius * scale;
        if (radius < 0.05f) return;
        
        Rlgl.DisableBackfaceCulling();
        
        Raylib.DrawCircle3D(new Vector3(pos.X, groundY, pos.Z), radius * 1.0f, new Vector3(1, 0, 0), 90f, new Color((byte)0, (byte)0, (byte)0, (byte)(35 * scale)));
        Raylib.DrawCircle3D(new Vector3(pos.X, groundY, pos.Z), radius * 0.7f, new Vector3(1, 0, 0), 90f, new Color((byte)0, (byte)0, (byte)0, (byte)(55 * scale)));
        Raylib.DrawCircle3D(new Vector3(pos.X, groundY, pos.Z), radius * 0.4f, new Vector3(1, 0, 0), 90f, new Color((byte)0, (byte)0, (byte)0, (byte)(80 * scale)));
        
        Rlgl.EnableBackfaceCulling();
    }

    private static Color BlockTint(ushort blockId) => blockId switch {
        var id when id == GameData.BDirt.Id => new Color(134, 96, 58, 255),
        var id when id == GameData.BGrass.Id => new Color(122, 92, 58, 255),
        var id when id == GameData.BStone.Id => new Color(128, 128, 132, 255),
        var id when id == GameData.BLog.Id => new Color(98, 70, 42, 255),
        var id when id == GameData.BPlanks.Id => new Color(168, 132, 82, 255),
        var id when id == GameData.BLeaves.Id => new Color(52, 118, 40, 255),
        var id when id == GameData.BCoalOre.Id => new Color(106, 106, 110, 255),
        _ => new Color(150, 150, 155, 255),
    };

    public void Dispose() {
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
