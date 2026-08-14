namespace VoxelFrame.Game;

/// <summary>
/// Детерминированный гладкий шум Перлина (2D и 3D) с фрактальным наложением октав.
/// Минимальное потребление памяти (таблица перестановок) и быстрая генерация по сиду.
/// </summary>
public sealed class Noise {
    private readonly byte[] _p = new byte[512];

    public Noise(int seed) {
        var rng = new Random(seed);
        byte[] permutation = new byte[256];
        for (int i = 0; i < 256; i++) permutation[i] = (byte)i;
        for (int i = 255; i > 0; i--) {
            int j = rng.Next(i + 1);
            (permutation[i], permutation[j]) = (permutation[j], permutation[i]);
        }
        for (int i = 0; i < 256; i++) {
            _p[i] = permutation[i];
            _p[256 + i] = permutation[i];
        }
    }

    private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static float Lerp(float t, float a, float b) => a + t * (b - a);

    private static float Grad(int hash, float x, float z) {
        int h = hash & 7;
        float u = h < 4 ? x : z;
        float v = h < 4 ? z : x;
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    private static float Grad(int hash, float x, float y, float z) {
        int h = hash & 15;
        float u = h < 8 ? x : y;
        float v = h < 4 ? y : (h == 12 || h == 14 ? x : z);
        return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
    }

    /// <summary>2D шум в диапазоне [0, 1].</summary>
    public float Get(float x, float z) {
        int xi = (int)MathF.Floor(x) & 255;
        int zi = (int)MathF.Floor(z) & 255;

        float xf = x - MathF.Floor(x);
        float zf = z - MathF.Floor(z);

        float u = Fade(xf);
        float v = Fade(zf);

        int aa = _p[_p[xi] + zi];
        int ab = _p[_p[xi] + zi + 1];
        int ba = _p[_p[xi + 1] + zi];
        int bb = _p[_p[xi + 1] + zi + 1];

        float x1 = Lerp(u, Grad(aa, xf, zf), Grad(ba, xf - 1, zf));
        float x2 = Lerp(u, Grad(ab, xf, zf - 1), Grad(bb, xf - 1, zf - 1));

        float val = Lerp(v, x1, x2);
        return Math.Clamp((val + 1f) * 0.5f, 0f, 1f);
    }

    /// <summary>3D шум в диапазоне [0, 1].</summary>
    public float Get(float x, float y, float z) {
        int xi = (int)MathF.Floor(x) & 255;
        int yi = (int)MathF.Floor(y) & 255;
        int zi = (int)MathF.Floor(z) & 255;

        float xf = x - MathF.Floor(x);
        float yf = y - MathF.Floor(y);
        float zf = z - MathF.Floor(z);

        float u = Fade(xf);
        float v = Fade(yf);
        float w = Fade(zf);

        int a = _p[xi] + yi;
        int aa = _p[a] + zi;
        int ab = _p[a + 1] + zi;
        int b = _p[xi + 1] + yi;
        int ba = _p[b] + zi;
        int bb = _p[b + 1] + zi;

        float x1 = Lerp(u, Grad(_p[aa], xf, yf, zf), Grad(_p[ba], xf - 1, yf, zf));
        float x2 = Lerp(u, Grad(_p[ab], xf, yf - 1, zf), Grad(_p[bb], xf - 1, yf - 1, zf));
        float y1 = Lerp(v, x1, x2);

        float x3 = Lerp(u, Grad(_p[aa + 1], xf, yf, zf - 1), Grad(_p[ba + 1], xf - 1, yf, zf - 1));
        float x4 = Lerp(u, Grad(_p[ab + 1], xf, yf - 1, zf - 1), Grad(_p[bb + 1], xf - 1, yf - 1, zf - 1));
        float y2 = Lerp(v, x3, x4);

        float val = Lerp(w, y1, y2);
        return Math.Clamp((val + 1f) * 0.5f, 0f, 1f);
    }

    /// <summary>2D фрактальный шум из нескольких октав.</summary>
    public float Fractal(float x, float z, int octaves, float persistence = 0.5f) {
        float sum = 0f, amp = 1f, total = 0f, freq = 1f;
        for (int o = 0; o < octaves; o++) {
            sum += Get(x * freq, z * freq) * amp;
            total += amp;
            amp *= persistence;
            freq *= 2f;
        }
        return sum / total;
    }

    /// <summary>3D фрактальный шум из нескольких октав.</summary>
    public float Fractal(float x, float y, float z, int octaves, float persistence = 0.5f) {
        float sum = 0f, amp = 1f, total = 0f, freq = 1f;
        for (int o = 0; o < octaves; o++) {
            sum += Get(x * freq, y * freq, z * freq) * amp;
            total += amp;
            amp *= persistence;
            freq *= 2f;
        }
        return sum / total;
    }
}
