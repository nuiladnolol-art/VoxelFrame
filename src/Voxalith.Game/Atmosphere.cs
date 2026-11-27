using System.Numerics;

namespace Voxalith.Game;

/// <summary>
/// Единая модель атмосферы на кадр: направление солнца/луны, цвет прямого света,
/// небесный (ambient) свет, цвета неба у зенита и горизонта, закатное свечение.
/// Её читают шейдер мира, шейдер неба, туман, облака и пост-обработка — поэтому
/// горизонт неба и туман совпадают по цвету, а свет на блоках согласован с солнцем.
/// Чистая математика без Raylib (можно вызывать в headless-тестах).
/// </summary>
public static class Atmosphere {
    /// <summary>Направление НА солнце (то же, что у билборда солнца в небе).</summary>
    public static Vector3 SunDir { get; private set; } = Vector3.UnitY;
    /// <summary>Направление основного источника прямого света (солнце днём, луна ночью).</summary>
    public static Vector3 LightDir { get; private set; } = Vector3.UnitY;
    /// <summary>Яркость/цвет прямого света (0 — нет прямого света).</summary>
    public static Vector3 LightColor { get; private set; }
    public static Vector3 AmbientTop { get; private set; }
    public static Vector3 AmbientBottom { get; private set; }
    public static Vector3 Zenith { get; private set; }
    public static Vector3 Horizon { get; private set; }
    /// <summary>Цвет закатного/рассветного свечения около солнца.</summary>
    public static Vector3 SunsetColor { get; private set; }
    /// <summary>Сила закатного свечения 0..1.</summary>
    public static float SunsetAmount { get; private set; }
    /// <summary>0 = день, 1 = глубокая ночь.</summary>
    public static float Night { get; private set; }
    /// <summary>Сглаженная пасмурность 0..1 (дождь/гроза).</summary>
    public static float Overcast { get; private set; }
    /// <summary>Видимость прямых солнечных лучей 0..1 (для god rays).</summary>
    public static float SunVisibility { get; private set; }
    /// <summary>Можно ли строить тени от текущего источника.</summary>
    public static bool CastsShadows { get; private set; }
    public static Dimension Dimension { get; private set; }

    private static float _overcastSmoothed;
    private static bool _initialized;

    private static float Smooth(float e0, float e1, float x) {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>Направление на солнце для времени суток 0..1 (0.25 — восход на востоке).</summary>
    public static Vector3 SunDirection(float timeOfDay) {
        float a = 2f * MathF.PI * (timeOfDay - 0.25f);
        return Vector3.Normalize(new Vector3(MathF.Cos(a), MathF.Sin(a), 0.22f));
    }

    public static void Update(float timeOfDay, WeatherType weather, Dimension dim, float dt) {
        Dimension = dim;
        float targetOvercast = weather == WeatherType.Clear ? 0f : weather == WeatherType.Rain ? 0.8f : 1f;
        if (!_initialized) { _overcastSmoothed = targetOvercast; _initialized = true; }
        // Плавная смена погоды (~4 с) вместо мгновенного «щелчка» цвета неба.
        _overcastSmoothed += (targetOvercast - _overcastSmoothed) * Math.Clamp(dt * 0.25f, 0f, 1f);
        Overcast = _overcastSmoothed;

        switch (dim) {
            case Dimension.Nether:
                SetFlat(new Vector3(0.20f, 0.055f, 0.045f), new Vector3(0.30f, 0.08f, 0.05f), new Vector3(0.16f, 0.035f, 0.03f));
                return;
            case Dimension.End:
                SetFlat(new Vector3(0.05f, 0.03f, 0.09f), new Vector3(0.10f, 0.06f, 0.17f), new Vector3(0.17f, 0.13f, 0.24f));
                LightDir = Vector3.Normalize(new Vector3(0.35f, 0.88f, 0.3f));
                LightColor = new Vector3(0.30f, 0.24f, 0.42f);
                return;
            case Dimension.Void:
                SetFlat(new Vector3(0.025f, 0.01f, 0.05f), new Vector3(0.055f, 0.025f, 0.10f), new Vector3(0.12f, 0.07f, 0.20f));
                return;
        }

        var sun = SunDirection(timeOfDay);
        SunDir = sun;
        float elev = sun.Y;

        float day = Smooth(-0.12f, 0.22f, elev);   // насколько «дневное» небо
        Night = 1f - Smooth(-0.25f, 0.05f, elev);
        float sunset = Smooth(0.42f, 0.0f, MathF.Abs(elev)) * Smooth(-0.30f, -0.02f, elev);
        SunsetAmount = sunset * (1f - Overcast * 0.85f);

        // Небо
        var zenithDay = new Vector3(0.22f, 0.45f, 0.88f);
        var horizonDay = new Vector3(0.68f, 0.80f, 0.96f);
        var zenithNight = new Vector3(0.008f, 0.012f, 0.040f);
        var horizonNight = new Vector3(0.030f, 0.040f, 0.085f);
        var zenith = Vector3.Lerp(zenithNight, zenithDay, day);
        var horizon = Vector3.Lerp(horizonNight, horizonDay, day);
        // Сумерки: зенит уходит в сиреневый, горизонт — в персиковый
        zenith = Vector3.Lerp(zenith, new Vector3(0.20f, 0.20f, 0.42f), sunset * 0.45f);
        horizon = Vector3.Lerp(horizon, new Vector3(0.95f, 0.62f, 0.45f), sunset * 0.55f);
        // Пасмурность: серое плотное небо
        float grayL = 0.62f * (0.10f + 0.90f * day);
        var gray = new Vector3(grayL * 0.92f, grayL * 0.95f, grayL);
        zenith = Vector3.Lerp(zenith, gray * 0.85f, Overcast * 0.85f);
        horizon = Vector3.Lerp(horizon, gray, Overcast * 0.85f);
        Zenith = zenith;
        Horizon = horizon;
        SunsetColor = new Vector3(1.0f, 0.46f, 0.16f);

        // Прямой свет: солнце, ниже горизонта — луна (с плавным перекрёстом без «щелчка»).
        if (elev >= 0f) {
            float k = Smooth(0.0f, 0.14f, elev);
            float warm = Smooth(0.45f, 0.02f, elev);
            var c = Vector3.Lerp(new Vector3(1.0f, 0.95f, 0.86f), new Vector3(1.0f, 0.58f, 0.30f), warm);
            LightDir = sun;
            LightColor = c * (0.64f * k * (1f - Overcast * 0.72f));
        } else {
            float k = Smooth(0.0f, 0.18f, -elev);
            LightDir = -sun;
            LightColor = new Vector3(0.14f, 0.19f, 0.32f) * (k * (1f - Overcast * 0.8f));
        }
        CastsShadows = LightColor.LengthSquared() > 0.0004f;
        SunVisibility = Smooth(-0.04f, 0.10f, elev) * (1f - Overcast);

        // Рассеянный свет неба (полусфера): сверху — цвет неба, снизу — отражённый от земли.
        var ambDay = new Vector3(0.42f, 0.47f, 0.56f);
        var ambNight = new Vector3(0.060f, 0.080f, 0.150f);
        var amb = Vector3.Lerp(ambNight, ambDay, day);
        amb = Vector3.Lerp(amb, new Vector3(0.62f, 0.50f, 0.48f) * MathF.Max(day, 0.3f), sunset * 0.35f);
        // В пасмурную погоду прямой свет слабее, а рассеянный почти не меняется
        amb *= 1f - Overcast * 0.18f;
        AmbientTop = amb;
        AmbientBottom = amb * 0.55f + new Vector3(0.05f, 0.045f, 0.03f) * day;
    }

    private static void SetFlat(Vector3 zenith, Vector3 horizon, Vector3 ambient) {
        Zenith = zenith;
        Horizon = horizon;
        SunDir = Vector3.UnitY;
        LightDir = Vector3.UnitY;
        LightColor = Vector3.Zero;
        AmbientTop = ambient;
        AmbientBottom = ambient * 0.8f;
        SunsetColor = Vector3.Zero;
        SunsetAmount = 0f;
        Night = 0f;
        SunVisibility = 0f;
        CastsShadows = false;
    }

    /// <summary>Цвет неба в направлении взгляда — та же формула, что в шейдере неба (для тумана/облаков на CPU).</summary>
    public static Vector3 SkyColor(Vector3 dir) {
        float h = Math.Clamp(dir.Y, -1f, 1f);
        float up = (1f - MathF.Exp(-MathF.Max(h, 0f) * 2.6f)) / 0.9257f;
        var c = Vector3.Lerp(Horizon, Zenith, up);
        if (SunsetAmount > 0f) {
            var flatDir = Vector2.Normalize(new Vector2(dir.X, dir.Z) + new Vector2(1e-4f, 0f));
            var flatSun = Vector2.Normalize(new Vector2(SunDir.X, SunDir.Z) + new Vector2(1e-4f, 0f));
            float toward = Vector2.Dot(flatDir, flatSun) * 0.5f + 0.5f;
            float band = MathF.Pow(1f - MathF.Abs(h), 6f);
            c = Vector3.Lerp(c, SunsetColor, SunsetAmount * band * (0.25f + 0.75f * toward * toward));
        }
        return c;
    }
}
