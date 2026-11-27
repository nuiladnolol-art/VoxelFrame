using System.Numerics;
using Raylib_cs;
using Voxalith.Core.World;

namespace Voxalith.Game;

/// <summary>
/// Тени от солнца/луны: каждый кадр мир вокруг игрока рендерится в карту глубины
/// из ортографической камеры источника света. Шейдер мира сравнивает глубину
/// (PCF с билинейным сглаживанием) — получаются мягкие тени от деревьев, гор и построек.
/// Камера света «привязана к текселю», поэтому края теней не дрожат при ходьбе.
/// </summary>
public sealed class ShadowMapper : IDisposable {
    private const string DepthVs = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        uniform mat4 mvp;
        out vec2 fragTexCoord;
        void main() {
            fragTexCoord = vertexTexCoord;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    private const string DepthFs = """
        #version 330
        in vec2 fragTexCoord;
        uniform sampler2D texture0;
        out vec4 finalColor;
        void main() {
            if (texture(texture0, fragTexCoord).a < 0.5) discard; // прорези в листве
            finalColor = vec4(1.0);
        }
        """;

    private RenderTexture2D _rt;
    private int _size;
    private Material _material;
    private bool _materialReady;
    private bool _failed;

    /// <summary>Матрица view*proj источника света (для шейдера мира).</summary>
    public Matrix4x4 LightViewProj { get; private set; }
    /// <summary>Шейдер может читать тени в этом кадре.</summary>
    public bool Active { get; private set; }
    public uint DepthTextureId => _rt.Depth.Id;
    public int Size => _size;
    /// <summary>Половина стороны покрываемой области в блоках.</summary>
    public float HalfExtent { get; private set; } = 64f;
    /// <summary>Размер текселя карты в мировых единицах.</summary>
    public float TexelWorld => HalfExtent * 2f / Math.Max(1, _size);

    private const float LightDistance = 200f;

    private bool EnsureResources(int size) {
        if (_failed) return false;
        if (!_materialReady) {
            var shader = Raylib.LoadShaderFromMemory(DepthVs, DepthFs);
            if (!Raylib.IsShaderValid(shader)) { _failed = true; return false; }
            _material = Raylib.LoadMaterialDefault();
            _material.Shader = shader;
            unsafe { _material.Maps[(int)MaterialMapIndex.Albedo].Texture = TextureAtlas.Atlas; }
            _materialReady = true;
        }
        if (_size == size && _rt.Id != 0) return true;
        ReleaseTarget();

        uint fbo = Rlgl.LoadFramebuffer();
        if (fbo == 0) { _failed = true; return false; }
        Rlgl.EnableFramebuffer(fbo);
        // Небольшой цветовой attachment (R8) — без него FBO неполный на части драйверов GL 3.3.
        uint colorId;
        unsafe { colorId = Rlgl.LoadTexture(null, size, size, PixelFormat.UncompressedGrayscale, 1); }
        uint depthId = Rlgl.LoadTextureDepth(size, size, false);
        Rlgl.FramebufferAttach(fbo, colorId, FramebufferAttachType.ColorChannel0, FramebufferAttachTextureType.Texture2D, 0);
        Rlgl.FramebufferAttach(fbo, depthId, FramebufferAttachType.Depth, FramebufferAttachTextureType.Texture2D, 0);
        bool ok = Rlgl.FramebufferComplete(fbo);
        Rlgl.DisableFramebuffer();
        if (!ok || depthId == 0) {
            Rlgl.UnloadFramebuffer(fbo);
            if (colorId != 0) Rlgl.UnloadTexture(colorId);
            if (depthId != 0) Rlgl.UnloadTexture(depthId);
            _failed = true;
            return false;
        }
        _rt = new RenderTexture2D {
            Id = fbo,
            Texture = new Texture2D { Id = colorId, Width = size, Height = size, Mipmaps = 1, Format = PixelFormat.UncompressedGrayscale },
            Depth = new Texture2D { Id = depthId, Width = size, Height = size, Mipmaps = 1, Format = PixelFormat.UncompressedGrayscale },
        };
        // Линейная фильтрация карты не нужна (сравниваем вручную), а clamp
        // за краями даёт «свет», а не повтор теней с другой стороны карты.
        Raylib.SetTextureFilter(_rt.Depth, TextureFilter.Point);
        Raylib.SetTextureWrap(_rt.Depth, TextureWrap.Clamp);
        _size = size;
        return true;
    }

    /// <summary>
    /// Рендер карты теней. Вызывать ДО BeginDrawing/BeginTextureMode основной сцены
    /// (raylib не поддерживает вложенные render target-ы).
    /// </summary>
    public void Render(GameWorld world, Vector3 focus) {
        Active = false;
        int quality = SaveSystem.ShadowQuality;
        if (quality <= 0 || !Atmosphere.CastsShadows || world.Dimension != Dimension.Overworld) return;
        int size = quality >= 2 ? 2048 : 1024;
        HalfExtent = quality >= 2 ? 80f : 56f;
        if (!EnsureResources(size)) return;

        var l = Vector3.Normalize(Atmosphere.LightDir);
        // Стабильный базис камеры света: горизонтальная ось X всегда перпендикулярна лучу
        // и мировому вектору верха (UnitY), за исключением зенита, где берем UnitZ.
        // Это предотвращает паразитное вращение камеры теней на 90 градусов в течение суток.
        var worldUp = MathF.Abs(l.Y) > 0.98f ? Vector3.UnitZ : Vector3.UnitY;
        var vz = l;                                       // направление луча
        var vx = Vector3.Normalize(Vector3.Cross(worldUp, vz));
        var vy = Vector3.Cross(vz, vx);

        // Привязка центра к сетке текселей: тени не мерцают при движении игрока.
        float texel = TexelWorld;
        float px = Vector3.Dot(focus, vx), py = Vector3.Dot(focus, vy);
        float sx = MathF.Floor(px / texel) * texel, sy = MathF.Floor(py / texel) * texel;
        var center = focus + vx * (sx - px) + vy * (sy - py);

        var cam = new Camera3D {
            Position = center + l * LightDistance,
            Target = center,
            Up = vy,
            FovY = HalfExtent * 2f,
            Projection = CameraProjection.Orthographic,
        };

        Raylib.BeginTextureMode(_rt);
        Raylib.ClearBackground(Color.White);
        Raylib.BeginMode3D(cam);
        var view = Rlgl.GetMatrixModelview();
        var proj = Rlgl.GetMatrixProjection();
        LightViewProj = Raymath.MatrixMultiply(view, proj);

        float reach = HalfExtent + 28f; // + радиус описанной сферы чанка
        Rlgl.EnableBackfaceCulling();
        foreach (var gc in world.Chunks) {
            if (!gc.MeshUploaded) continue;
            var c = new Vector3((gc.Coord.X + 0.5f) * Chunk.SizeX, (gc.Coord.Y + 0.5f) * Chunk.SizeY, (gc.Coord.Z + 0.5f) * Chunk.SizeZ);
            var d = c - center;
            if (MathF.Abs(Vector3.Dot(d, vx)) > reach || MathF.Abs(Vector3.Dot(d, vy)) > reach) continue;
            if (Vector3.Dot(d, vz) < -LightDistance) continue; // целиком «под» картой
            if (gc.Meshes.Count > 0) {
                foreach (var m in gc.Meshes) Raylib.DrawMesh(m, _material, Matrix4x4.Identity);
            }
            if (gc.TranslucentMeshes.Count > 0) {
                foreach (var m in gc.TranslucentMeshes) Raylib.DrawMesh(m, _material, Matrix4x4.Identity);
            }
        }
        Raylib.EndMode3D();
        Raylib.EndTextureMode();
        Active = true;
    }

    private void ReleaseTarget() {
        if (_rt.Id != 0) {
            Rlgl.UnloadFramebuffer(_rt.Id);
            if (_rt.Texture.Id != 0) Rlgl.UnloadTexture(_rt.Texture.Id);
            if (_rt.Depth.Id != 0) Rlgl.UnloadTexture(_rt.Depth.Id);
        }
        _rt = default;
        _size = 0;
    }

    public void Dispose() {
        ReleaseTarget();
        if (_materialReady) {
            // UnloadMaterial выгрузил бы и общий атлас (Albedo) — отвязываем его заранее.
            unsafe { _material.Maps[(int)MaterialMapIndex.Albedo].Texture = default; }
            Raylib.UnloadMaterial(_material);
            _materialReady = false;
        }
        Active = false;
    }
}
