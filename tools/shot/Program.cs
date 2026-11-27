using System.Numerics;
using Raylib_cs;
using Voxalith.Core;
using Voxalith.Core.World;
using Voxalith.Game;

// Детерминированный рендер-харнесс: строит мир по фиксированному сиду,
// ставит камеру/время суток/погоду из аргументов и сохраняет PNG.
// Используется для проверки графики «до/после» без ручного запуска игры.

string outDir = args.Length > 0 ? args[0] : "shots";
Directory.CreateDirectory(outDir);

int seed = 12345;
int width = 1280, height = 720;
foreach (var a in args) {
    if (a.StartsWith("--seed=")) int.TryParse(a[7..], out seed);
    if (a.StartsWith("--w=")) int.TryParse(a[4..], out width);
    if (a.StartsWith("--h=")) int.TryParse(a[4..], out height);
}

// Настройки графики — из окружения харнесса, не из профиля пользователя.
SaveSystem.PostFxMode = 1;
SaveSystem.PostFxVignette = true;
SaveSystem.PostFxVignetteStrength = 35;
SaveSystem.PostFxGoldenHour = true;
SaveSystem.PostFxBloom = true;
SaveSystem.DynamicLighting = true;
SaveSystem.EntityShadows = true;
SaveSystem.GraphicsQuality = SaveSystem.GraphicsPreset.Fancy;
SaveSystem.RenderDistanceSetting = 8;
SaveSystem.FovSetting = 70;

Raylib.SetConfigFlags(ConfigFlags.Msaa4xHint | ConfigFlags.HiddenWindow);
Raylib.InitWindow(width, height, "Shot");
Raylib.SetTargetFPS(60);
Raylib.SetExitKey(KeyboardKey.Null);

TextureAtlas.Load();
Fonts.Load();
Voxalith.Game.Program.RegisterTiles();
SkyTextures.Load();

var session = GameSession.NewGame(seed, headless: false);
var world = session.World;
var renderer = new WorldRenderer(session);
var spawn = world.SpawnBlock;

// Догружаем окрестности и строим все меши сразу — иначе кадр будет неполным.
var center = session.Player.Position;
world.EnsureLoadedAroundSync(center, 8);
world.RelightAroundSync(new Vec3i(
    (int)MathF.Floor(center.X / Chunk.SizeX), 1,
    (int)MathF.Floor(center.Z / Chunk.SizeZ)), 10);

foreach (var gc in world.Chunks) {
    gc.RecomputeAllSurfaces();
    LightEngine.RecomputeSun(gc, world);
    LightEngine.RecomputeBlock(gc, world);
    gc.UnloadMesh();
    var (opq, trans) = ChunkMesher.Build(gc, world);
    gc.Meshes.AddRange(opq);
    gc.TranslucentMeshes.AddRange(trans);
    gc.MeshUploaded = gc.Meshes.Count > 0 || gc.TranslucentMeshes.Count > 0;
}
Console.WriteLine($"chunks={world.Chunks.Count} spawn=({spawn.X},{spawn.Y},{spawn.Z})");

// ── Сцены ────────────────────────────────────────────────────────────────────
// Каждая сцена: имя файла, время суток, погода, смещение/направление камеры,
// измерение, опционально предмет в руке.
var scenes = new List<(string Name, float Tod, WeatherType Weather, Vector3 CamPos, Vector3 CamTarget, Dimension Dim, int HeldItem)> {
    ("01_day_spawn",   0.50f, WeatherType.Clear, default, default, Dimension.Overworld, 0),
    ("02_sunset",      0.745f, WeatherType.Clear, default, default, Dimension.Overworld, 0),
    ("03_night",       0.00f, WeatherType.Clear, default, default, Dimension.Overworld, 0),
    ("04_rain",        0.50f, WeatherType.Rain,   default, default, Dimension.Overworld, 0),
    ("05_aerial",      0.42f, WeatherType.Clear, default, default, Dimension.Overworld, 0),
    ("06_ground_low",  0.50f, WeatherType.Clear, default, default, Dimension.Overworld, 0),
};

float surf = world.Generator.SurfaceHeight(spawn.X, spawn.Z);
Vector3 sp = new(spawn.X + 0.5f, surf + 1.9f, spawn.Z + 0.5f);

foreach (var sc in scenes) {
    if (sc.Dim != world.Dimension) {
        try { session.SwitchDimension(sc.Dim); } catch (Exception ex) { Console.WriteLine($"dim switch failed: {ex.Message}"); continue; }
    }
    session.DayNight.TimeOfDay = sc.Tod;
    session.Weather = sc.Weather;

    var cam = new Camera3D { Up = Vector3.UnitY, FovY = SaveSystem.FovSetting, Projection = CameraProjection.Perspective };
    switch (sc.Name) {
        case "05_aerial":
            cam.Position = sp + new Vector3(-26f, 22f, -26f);
            cam.Target = sp + new Vector3(0f, -6f, 0f);
            break;
        case "06_ground_low":
            cam.Position = sp + new Vector3(3.2f, -1.2f, 3.2f);
            cam.Target = sp + new Vector3(-2f, -0.6f, -3f);
            break;
        default:
            cam.Position = sp + new Vector3(-8f, 3.5f, -8f);
            cam.Target = sp + new Vector3(2f, 0f, 2f);
            break;
    }
    session.Camera = cam;

    // Прогрев: мир/шейдеры/пост-обработка инициализируются на первых кадрах.
    for (int i = 0; i < 6; i++) DrawFrame(session, renderer, cam, sc.Dim);

    Raylib.TakeScreenshot(Path.Combine(outDir, sc.Name + ".png"));
    for (int i = 0; i < 3; i++) DrawFrame(session, renderer, cam, sc.Dim);
    Console.WriteLine("shot: " + sc.Name);
}

// ── Меню и инвентарь (оформление UI) ─────────────────────────────────────────
session.Ui = UiState.Playing;
for (int i = 0; i < 4; i++) DrawFrame(session, renderer, session.Camera, session.Dimension);
Raylib.BeginDrawing();
Raylib.ClearBackground(new Color(10, 12, 20, 255));
Ui.Begin();
Screens.DrawInventory(session);
Ui.End();
Raylib.EndDrawing();
Raylib.TakeScreenshot(Path.Combine(outDir, "10_inventory.png"));
Raylib.EndDrawing();
Console.WriteLine("shot: 10_inventory");

renderer.Dispose();
world.Dispose();
TextureAtlas.Unload();
Fonts.Unload();
SkyTextures.Unload();
PostProcessing.Unload();
Raylib.CloseWindow();
return 0;

static void DrawFrame(GameSession session, WorldRenderer renderer, Camera3D cam, Dimension dim) {
    bool post = PostProcessing.BeginScene(session);
    if (!post) Raylib.BeginDrawing();
    Raylib.ClearBackground(new Color(10, 12, 20, 255));
    session.Camera = cam;
    renderer.DrawSky();
    Raylib.BeginMode3D(cam);
    renderer.Draw3DSky(cam);
    renderer.DrawWorldOpaque();
    renderer.DrawEntities(cam);
    renderer.DrawDecorations(1f / 60f);
    renderer.DrawPickups(cam);
    renderer.DrawWorldTranslucent();
    renderer.DrawClouds(cam);
    renderer.DrawWeather(cam);
    Raylib.EndMode3D();
    PostProcessing.EndScene();
    if (!post) { }
    Raylib.EndDrawing();
}
