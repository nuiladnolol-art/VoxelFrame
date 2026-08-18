namespace VoxelFrame.Game;

/// <summary>
/// Цикл дня и ночи. Сутки — 8 минут. Фактор неба: 1.0 днём, 0.08 ночью,
/// плавные переходы на рассвете и закате.
/// </summary>
public sealed class DayNightCycle {
    public const float CycleSeconds = 480f;     // полные сутки
    public const float StartTime = 0.35f;       // старт ~8:24 утра

    /// <summary>Время суток 0..1 (0 = полночь, 0.25 = 6:00, 0.5 = полдень, 0.75 = 18:00).</summary>
    public float TimeOfDay;

    public DayNightCycle(float timeOfDay = StartTime) => TimeOfDay = timeOfDay;

    public void Tick(float dt) {
        TimeOfDay = (TimeOfDay + dt / CycleSeconds) % 1f;
    }

    /// <summary>Яркость солнца: 1.0 днём → 0.08 ночью.</summary>
    public float SkyFactor {
        get {
            float sun = MathF.Sin(2f * MathF.PI * (TimeOfDay - 0.25f));
            sun = MathF.Max(0f, sun);
            sun = MathF.Pow(sun, 0.65f);
            return 0.08f + 0.92f * sun;
        }
    }

    public bool IsDay => SkyFactor > 0.5f;

    /// <summary>Квант яркости (0..15) — при смене перестраиваются меши освещения.</summary>
    public int SkyStep => (int)(SkyFactor * 15.999f);

    public string ClockText {
        get {
            float hours = TimeOfDay * 24f;
            int h = (int)hours;
            int m = (int)((hours - h) * 60f);
            return $"{h:00}:{m:00}";
        }
    }
}
