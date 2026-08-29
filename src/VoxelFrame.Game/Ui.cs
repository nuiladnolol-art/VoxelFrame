using Raylib_cs;

namespace VoxelFrame.Game;

/// <summary>
/// Масштабирование интерфейса: весь UI рисуется в виртуальных координатах
/// (опорная высота 720), а матрица BeginMode2D растягивает холст под окно.
/// Экраны берут размеры из Ui.Vw/Ui.Vh и мышь из Ui.Mouse() — тогда один
/// и тот же интерфейс корректно выглядит и на 720p, и на 4K.
/// </summary>
public static class Ui {
    public const float ReferenceHeight = 720f;
    private static Camera2D _cam;
    private static int _vw = 1280, _vh = 720;

    /// <summary>Виртуальная ширина холста UI (видна экранам как обычная ширина).</summary>
    public static int Vw => _vw;

    /// <summary>Виртуальная высота холста UI (видна экранам как обычная высота).</summary>
    public static int Vh => _vh;

    /// <summary>Текущий масштаб UI (пиксель окна на виртуальный пиксель).</summary>
    public static float CurrentScale { get; private set; } = 1f;

    private static float ResolveScale() {
        int screenH = Raylib.GetScreenHeight();
        if (screenH <= 0) return 1f;
        if (SaveSystem.UiScaleMode == 0) return screenH / ReferenceHeight;
        return Math.Max(0.5f, SaveSystem.UiScaleMode / 100f);
    }

    /// <summary>Начать виртуальный холст. Всё до End() рисуется в координатах UI.</summary>
    public static void Begin() {
        int sw = Raylib.GetScreenWidth(), sh = Raylib.GetScreenHeight();
        CurrentScale = MathF.Max(ResolveScale(), 0.01f);
        _vw = Math.Max(320, (int)MathF.Ceiling(sw / CurrentScale));
        _vh = Math.Max(240, (int)MathF.Ceiling(sh / CurrentScale));
        _cam = new Camera2D {
            Target = System.Numerics.Vector2.Zero,
            Offset = System.Numerics.Vector2.Zero,
            Rotation = 0f,
            Zoom = CurrentScale,
        };
        Raylib.BeginMode2D(_cam);
    }

    /// <summary>Завершить виртуальный холст.</summary>
    public static void End() => Raylib.EndMode2D();

    /// <summary>Позиция мыши в виртуальных координатах холста UI.</summary>
    public static System.Numerics.Vector2 Mouse() {
        var p = Raylib.GetMousePosition();
        return new System.Numerics.Vector2(p.X / CurrentScale, p.Y / CurrentScale);
    }
}
