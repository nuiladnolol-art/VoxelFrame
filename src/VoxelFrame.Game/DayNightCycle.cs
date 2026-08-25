namespace VoxelFrame.Game;

/// <summary>
/// Цикл дня и ночи. Сутки — 20 минут: 10 минут день, 10 минут ночь.
/// Фактор неба: 1.0 днём, 0.08 ночью, плавные переходы на рассвете и закате.
/// </summary>
public sealed class DayNightCycle {
    public const float CycleSeconds = 1200f;    // полные сутки: 10 мин день + 10 мин ночь
    public const float StartTime = 0.25f;       // старт в 6:00 утра (рассвет)

    /// <summary>Время суток 0..1 (0 = полночь, 0.25 = 6:00, 0.5 = полдень, 0.75 = 18:00).</summary>
    public float TimeOfDay;

    public DayNightCycle(float timeOfDay = StartTime) => TimeOfDay = timeOfDay;

    public void Tick(float dt) {
        TimeOfDay = (TimeOfDay + dt / CycleSeconds) % 1f;
    }

    /// <summary>Яркость солнца: 1.0 днём (10 мин) → 0.08 ночью (7 мин), закат и рассвет (по ~1.5 мин).</summary>
    public float SkyFactor {
        get {
            float tod = TimeOfDay;
            if (tod >= 0.25f && tod <= 0.75f) {
                // Дневное время: стабильный солнечный свет 1.0 с плавными краями
                float edge = MathF.Min((tod - 0.25f) / 0.0625f, (0.75f - tod) / 0.0625f);
                float sun = Math.Clamp(edge, 0f, 1f);
                return 0.08f + 0.92f * MathF.Sin(sun * MathF.PI * 0.5f);
            } else {
                // Ночное время и сумерки
                float distToDay = tod < 0.25f ? (0.25f - tod) : (tod - 0.75f);
                if (distToDay < 0.0625f) {
                    float fade = 1.0f - (distToDay / 0.0625f);
                    return 0.08f + 0.92f * (fade * fade * 0.5f);
                }
                return 0.08f;
            }
        }
    }

    public bool IsDay => SkyFactor > 0.45f;

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
