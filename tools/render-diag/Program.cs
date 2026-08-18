using System.Diagnostics;
using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.World;
using VoxelFrame.Game;

// Рендер-диагностика: реальный кадр мира + замер FPS без пересборок мешей.
// Снимает скриншот для анализа (верхние грани должны быть зелёными — трава).

Raylib.SetConfigFlags(ConfigFlags.VSyncHint);
Raylib.InitWindow(640, 360, "RenderDiag");
Raylib.SetTargetFPS(60);
TextureAtlas.Load();
RegisterTilesForDiag();

var session = GameSession.NewGame(12345, headless: true);
var world = session.World;

// Копия Program.RegisterTiles — регистрация тайлов атласа для блоков.
static void RegisterTilesForDiag() {
    TextureAtlas.SetBlockTiles(GameData.BGrass.Id, TextureAtlas.TGrassTop, TextureAtlas.TGrassSide, TextureAtlas.TDirt);
    TextureAtlas.SetBlockTiles(GameData.BDirt.Id, TextureAtlas.TDirt, TextureAtlas.TDirt, TextureAtlas.TDirt);
    TextureAtlas.SetBlockTiles(GameData.BStone.Id, TextureAtlas.TStone, TextureAtlas.TStone, TextureAtlas.TStone);
    TextureAtlas.SetBlockTiles(GameData.BLog.Id, TextureAtlas.TLogTop, TextureAtlas.TLogSide, TextureAtlas.TLogTop);
    TextureAtlas.SetBlockTiles(GameData.BLeaves.Id, TextureAtlas.TLeaves, TextureAtlas.TLeaves, TextureAtlas.TLeaves);
    TextureAtlas.SetBlockTiles(GameData.BPlanks.Id, TextureAtlas.TPlanks, TextureAtlas.TPlanks, TextureAtlas.TPlanks);
    TextureAtlas.SetBlockTiles(GameData.BCoalOre.Id, TextureAtlas.TCoalOre, TextureAtlas.TCoalOre, TextureAtlas.TCoalOre);
    TextureAtlas.SetBlockTiles(GameData.BTorch.Id, TextureAtlas.TTorch, TextureAtlas.TTorch, TextureAtlas.TTorch);
    TextureAtlas.SetBlockTiles(GameData.BCampfire.Id, TextureAtlas.TCampfire, TextureAtlas.TCampfire, TextureAtlas.TCampfire);
    TextureAtlas.SetBlockTiles(GameData.BAsh.Id, TextureAtlas.TAsh, TextureAtlas.TAsh, TextureAtlas.TAsh);
    TextureAtlas.SetBlockTiles(GameData.BBedrock.Id, TextureAtlas.TBedrock, TextureAtlas.TBedrock, TextureAtlas.TBedrock);
}

// Построить ВСЕ меши напрямую (без очереди 3/кадр).
foreach (var gc in world.Chunks) {
    gc.RecomputeAllSurfaces();
    LightEngine.RecomputeSun(gc);
    LightEngine.RecomputeBlock(gc, world);
    gc.Meshes.AddRange(ChunkMesher.Build(gc, world));
    gc.MeshUploaded = gc.Meshes.Count > 0;
}
Console.WriteLine($"Meshes built: {world.Chunks.Count}");

// Камера над естественной травой (вне выровненной площадки спавна).
var spawn = world.SpawnBlock;
var cam = new Camera3D {
    Position = new Vector3(12.5f, spawn.Y + 6f, 12.5f - 8f),
    Target = new Vector3(12.5f, spawn.Y - 1f, 12.5f + 2f),
    Up = Vector3.UnitY,
    FovY = 70f,
    Projection = CameraProjection.Perspective,
};

// Дамп реальных данных меша спавн-чанка: ищем вершины верхних граней (+Y).
var gcSpawn = world.TryGetChunk(Chunk.CoordOf(spawn))!;
int yTopCount = 0;
var u0Counts = new Dictionary<int, int>();
unsafe {
    var m = gcSpawn.Meshes[0];
    Console.WriteLine($"Spawn chunk mesh: verts={m.VertexCount}");
    var verts = m.Vertices; var norms = m.Normals; var uvs = m.TexCoords; var cols = m.Colors;
    for (int i = 0; i < m.VertexCount; i++) {
        if (norms[i * 3 + 1] > 0.5f && verts[i * 3 + 1] > 40f) {   // +Y грани на высоте поверхности
            int u0 = (int)MathF.Round(uvs[i * 2] * 8f);   // номер тайла по U
            u0Counts[u0] = u0Counts.GetValueOrDefault(u0) + 1;
            if (yTopCount < 6 && (verts[i * 3] > 10f && verts[i * 3 + 2] > 10f)) {
                var c = cols[i * 4];
                Console.WriteLine($"  top vert {i}: pos=({verts[i*3]:F0},{verts[i*3+1]:F0},{verts[i*3+2]:F0}) uv=({uvs[i*2]:F3},{uvs[i*2+1]:F3}) shade={c}");
                yTopCount++;
            }
        }
    }
}
Console.WriteLine("Top-face tiles by U: " + string.Join(", ", u0Counts.OrderBy(k => k.Key).Select(k => $"u0={k.Key}(x{k.Value})")));
Console.WriteLine($"Surface at (12,4): {world.Generator.SurfaceHeight(12, 4)}, at (12,14): {world.Generator.SurfaceHeight(12, 14)}");
Console.WriteLine($"Surface at (11,11): {world.Generator.SurfaceHeight(11, 11)}, at (5,5): {world.Generator.SurfaceHeight(5, 5)}, at (15,20): {world.Generator.SurfaceHeight(15, 20)}");
Console.WriteLine("Column (11,11):");
for (int wy = 59; wy >= 50; wy--) {
    var v = world.GetVoxel(new Vec3i(11, wy, 11));
    Console.WriteLine($"  y={wy}: type={v.TypeId} ({(v.TypeId == 0 ? "air" : v.TypeId == GameData.BGrass.Id ? "grass" : v.TypeId == GameData.BDirt.Id ? "dirt" : v.TypeId == GameData.BStone.Id ? "stone" : "other")})");
}

var btGrass = TextureAtlas.BlockTiles(GameData.BGrass.Id);
Console.WriteLine($"Grass BlockTiles: top={btGrass.Top} side={btGrass.Side} bottom={btGrass.Bottom}");
var btStone = TextureAtlas.BlockTiles(GameData.BStone.Id);
Console.WriteLine($"Stone BlockTiles: top={btStone.Top} side={btStone.Side} bottom={btStone.Bottom}");
foreach (byte t in new byte[] { 0, 2, 3 }) {
    var r = TextureAtlas.TileUv(t);
    Console.WriteLine($"TileUv({t}) = ({r.X:F3},{r.Y:F3},{r.Width:F3},{r.Height:F3})");
}

var material = Raylib.LoadMaterialDefault();
unsafe {
    material.Maps[(int)MaterialMapIndex.Albedo].Texture = TextureAtlas.Atlas;
}
var vs = """
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
var fs = """
    #version 330
    in vec2 fragTexCoord;
    in vec4 fragColor;
    uniform sampler2D texture0;
    uniform vec4 colDiffuse;
    out vec4 finalColor;
    void main() {
        vec4 texelColor = texture(texture0, fragTexCoord);
        if (texelColor.a < 0.5) discard;
        float lum = max(max(fragColor.r, fragColor.g), 0.06);
        finalColor = texelColor * colDiffuse * vec4(lum, lum, lum, 1.0);
    }
    """;
var shader = Raylib.LoadShaderFromMemory(vs, fs);
if (Raylib.IsShaderValid(shader)) material.Shader = shader;
else Console.WriteLine("SHADER INVALID - fallback to default");

// Прогрев 30 кадров, затем замер 120 кадров.
for (int i = 0; i < 30; i++) {
    Raylib.BeginDrawing();
    Raylib.ClearBackground(new Color(10, 12, 20, 255));
    Raylib.BeginMode3D(cam);
    Rlgl.EnableBackfaceCulling();
    unsafe {
        foreach (var gc in world.Chunks)
            if (gc.MeshUploaded) foreach (var m in gc.Meshes) Raylib.DrawMesh(m, material, Matrix4x4.Identity);
    }
    Raylib.EndMode3D();
    Raylib.EndDrawing();
}

long vertSum = 0; int meshCount = 0;
foreach (var gc in world.Chunks) { if (gc.MeshUploaded) { foreach (var m in gc.Meshes) vertSum += m.VertexCount; meshCount++; } }
Console.WriteLine($"Meshes uploaded: {meshCount}, total verts: {vertSum:N0}");
Console.WriteLine($"Camera: pos={cam.Position} target={cam.Target}");
var underCam = world.GetVoxel(new Vec3i((int)MathF.Floor(cam.Position.X), (int)MathF.Floor(cam.Position.Y), (int)MathF.Floor(cam.Position.Z)));
Console.WriteLine($"Voxel at camera: type={underCam.TypeId} solid={underCam.IsSolid}");
Console.WriteLine($"Surface at (0,0)={(int)world.Generator.SurfaceHeight(0, 0)}, at (0,-8)={(int)world.Generator.SurfaceHeight(0, -8)}, at (0,2)={(int)world.Generator.SurfaceHeight(0, 2)}");

var sw = Stopwatch.StartNew();
for (int i = 0; i < 120; i++) {
    Raylib.BeginDrawing();
    Raylib.ClearBackground(new Color(10, 12, 20, 255));
    Raylib.BeginMode3D(cam);
    Rlgl.EnableBackfaceCulling();
    unsafe {
        foreach (var gc in world.Chunks)
            if (gc.MeshUploaded) foreach (var m in gc.Meshes) Raylib.DrawMesh(m, material, Matrix4x4.Identity);
    }
    Raylib.EndMode3D();
    Raylib.EndDrawing();
}
sw.Stop();
double ms = sw.Elapsed.TotalMilliseconds / 120.0;
Console.WriteLine($"Avg frame: {ms:F1} ms -> {1000.0 / ms:F1} FPS (draw-only, no rebuilds)");

Raylib.TakeScreenshot("C:/Users/arsis/OneDrive/Desktop/VoxelGame/render_diag_3d.png");
Console.WriteLine("Screenshot 3D saved");

// Оффскрин рендер в RenderTexture — обходит проблемы с дефолтным framebuffer.
// Рендер с culling и без — сравнение видимости верхних граней.
var rt = Raylib.LoadRenderTexture(640, 360);

// Камера строго сверху: если верхние грани есть, кадр будет зелёным (трава).
// Up смещён, иначе forward ∥ up — вырожденная матрица вида (ничего не рендерится).
var camTop = new Camera3D {
    Position = new Vector3(spawn.X + 0.5f, spawn.Y + 20f, spawn.Z + 0.5f),
    Target = new Vector3(spawn.X + 0.5f, spawn.Y, spawn.Z + 0.5f),
    Up = new Vector3(0f, 0f, -1f),
    FovY = 70f,
    Projection = CameraProjection.Perspective,
};

for (int pass = 0; pass < 4; pass++) {
    bool cull = (pass & 1) == 0;
    var useCam = pass < 2 ? cam : camTop;
    string label = (cull ? "CULL  " : "NOCULL") + (pass < 2 ? "   angle" : "   top  ");
    for (int i = 0; i < 2; i++) {
        Raylib.BeginTextureMode(rt);
        Raylib.ClearBackground(new Color(10, 12, 20, 255));
        Raylib.BeginMode3D(useCam);
        if (cull) Rlgl.EnableBackfaceCulling();
        else Rlgl.DisableBackfaceCulling();
        unsafe {
            foreach (var gc in world.Chunks)
                if (gc.MeshUploaded) foreach (var m in gc.Meshes) Raylib.DrawMesh(m, material, Matrix4x4.Identity);
        }
        Raylib.EndMode3D();
        Raylib.EndTextureMode();
    }

    unsafe {
        var img2 = Raylib.LoadImageFromTexture(rt.Texture);
        var cols = Raylib.LoadImageColors(img2);
        int green = 0, brown = 0, dark = 0, total = 0;
        for (int y = 0; y < 360; y += 2)
            for (int x = 0; x < 640; x += 2) {
                total++;
                var c = cols[y * 640 + x];
                if (c.G > 100 && c.G > c.R * 1.3 && c.R > 40) green++;
                else if (c.R > 60 && c.R > c.G * 1.3 && c.B < 120) brown++;
                else if (c.R < 50 && c.G < 50 && c.B < 60) dark++;
            }
        Console.WriteLine($"{label}: green={100.0 * green / total:F1}% brown={100.0 * brown / total:F1}% dark={100.0 * dark / total:F1}%");
        Raylib.UnloadImageColors(cols);
        Raylib.UnloadImage(img2);
    }
}
Raylib.UnloadRenderTexture(rt);
Console.WriteLine("Cull comparison done");

// Проверка самого механизма скриншота: яркий красный прямоугольник в 2D.
Raylib.BeginDrawing();
Raylib.ClearBackground(Color.White);
Raylib.DrawRectangle(100, 100, 200, 100, Color.Red);
Raylib.EndDrawing();
Raylib.TakeScreenshot("C:/Users/arsis/OneDrive/Desktop/VoxelGame/render_diag_2d.png");
Console.WriteLine("Screenshot 2D saved");

Raylib.UnloadMaterial(material);
Raylib.CloseWindow();
