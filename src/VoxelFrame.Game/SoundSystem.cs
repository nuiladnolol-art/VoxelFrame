using System.IO;
using Raylib_cs;

namespace VoxelFrame.Game;

/// <summary>
/// Процедурная звуковая система на основе встроенного генератора WAV-байтов в память.
/// Создает 8-битные и 16-битные ретро-звуки шагов, копания, ударов и установки блоков без внешних файлов.
/// </summary>
public static class SoundSystem {
    private static bool _audioReady;
    private static Sound _stepSound;
    private static Sound _digSound;
    private static Sound _hitSound;
    private static Sound _placeSound;
    private static Sound _eatSound;
    private static Sound _splashSound;
    private static Sound _popSound;

    public static void Initialize() {
        if (_audioReady) return;
        try {
            Raylib.InitAudioDevice();
            if (Raylib.IsAudioDeviceReady()) {
                _stepSound = LoadProceduralSound(CreateNoiseWav(44100 / 14, 0.25f));
                _digSound = LoadProceduralSound(CreateNoiseWav(44100 / 8, 0.5f));
                _hitSound = LoadProceduralSound(CreateToneWav(44100 / 6, 120f, 60f, 0.6f));
                _placeSound = LoadProceduralSound(CreateToneWav(44100 / 10, 400f, 250f, 0.35f));
                _eatSound = LoadProceduralSound(CreateToneWav(44100 / 8, 280f, 420f, 0.45f));
                _splashSound = LoadProceduralSound(CreateNoiseWav(44100 / 5, 0.4f));
                _popSound = LoadProceduralSound(CreateToneWav(44100 / 16, 550f, 850f, 0.30f));
                _audioReady = true;
            }
        } catch {
            _audioReady = false;
        }
    }

    public static void PlayStep() {
        if (_audioReady && SaveSystem.SoundVolume > 0) {
            Raylib.SetMasterVolume(SaveSystem.SoundVolume / 100f);
            Raylib.PlaySound(_stepSound);
        }
    }

    public static void PlayDig() {
        if (_audioReady && SaveSystem.SoundVolume > 0) {
            Raylib.SetMasterVolume(SaveSystem.SoundVolume / 100f);
            Raylib.PlaySound(_digSound);
        }
    }

    public static void PlayHit() {
        if (_audioReady && SaveSystem.SoundVolume > 0) {
            Raylib.SetMasterVolume(SaveSystem.SoundVolume / 100f);
            Raylib.PlaySound(_hitSound);
        }
    }

    public static void PlayPlace() {
        if (_audioReady && SaveSystem.SoundVolume > 0) {
            Raylib.SetMasterVolume(SaveSystem.SoundVolume / 100f);
            Raylib.PlaySound(_placeSound);
        }
    }

    public static void PlayEat() {
        if (_audioReady && SaveSystem.SoundVolume > 0) {
            Raylib.SetMasterVolume(SaveSystem.SoundVolume / 100f);
            Raylib.PlaySound(_eatSound);
        }
    }

    public static void PlaySplash() {
        if (_audioReady && SaveSystem.SoundVolume > 0) {
            Raylib.SetMasterVolume(SaveSystem.SoundVolume / 100f);
            Raylib.PlaySound(_splashSound);
        }
    }

    public static void PlayPop() {
        if (_audioReady && SaveSystem.SoundVolume > 0) {
            Raylib.SetMasterVolume(SaveSystem.SoundVolume / 100f);
            Raylib.PlaySound(_popSound);
        }
    }

    private static unsafe Sound LoadProceduralSound(byte[] wavBytes) {
        fixed (byte* ptr = wavBytes)
        fixed (byte* ext = ".wav"u8) {
            Wave wave = Raylib.LoadWaveFromMemory((sbyte*)ext, ptr, wavBytes.Length);
            Sound sound = Raylib.LoadSoundFromWave(wave);
            Raylib.UnloadWave(wave);
            return sound;
        }
    }

    private static byte[] CreateNoiseWav(int sampleCount, float volume) {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        int sampleRate = 44100;
        short bitsPerSample = 16;
        short channels = 1;
        int subchunk2Size = sampleCount * channels * (bitsPerSample / 8);

        // WAV Header
        bw.Write("RIFF"u8);
        bw.Write(36 + subchunk2Size);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16); // Subchunk1Size
        bw.Write((short)1); // PCM
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * (bitsPerSample / 8));
        bw.Write((short)(channels * (bitsPerSample / 8)));
        bw.Write(bitsPerSample);
        bw.Write("data"u8);
        bw.Write(subchunk2Size);

        var rng = new Random(42);
        for (int i = 0; i < sampleCount; i++) {
            float env = 1.0f - (float)i / sampleCount;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            short sample = (short)(noise * env * 32767f * volume);
            bw.Write(sample);
        }

        return ms.ToArray();
    }

    private static byte[] CreateToneWav(int sampleCount, float startFreq, float endFreq, float volume) {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        int sampleRate = 44100;
        short bitsPerSample = 16;
        short channels = 1;
        int subchunk2Size = sampleCount * channels * (bitsPerSample / 8);

        bw.Write("RIFF"u8);
        bw.Write(36 + subchunk2Size);
        bw.Write("WAVE"u8);
        bw.Write("fmt "u8);
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(channels);
        bw.Write(sampleRate);
        bw.Write(sampleRate * channels * (bitsPerSample / 8));
        bw.Write((short)(channels * (bitsPerSample / 8)));
        bw.Write(bitsPerSample);
        bw.Write("data"u8);
        bw.Write(subchunk2Size);

        float phase = 0f;
        for (int i = 0; i < sampleCount; i++) {
            float t = (float)i / sampleCount;
            float freq = startFreq + (endFreq - startFreq) * t;
            phase += 2f * MathF.PI * freq / sampleRate;
            float env = MathF.Sin((1f - t) * MathF.PI * 0.5f);
            short sample = (short)(MathF.Sin(phase) * env * 32767f * volume);
            bw.Write(sample);
        }

        return ms.ToArray();
    }

    public static void Shutdown() {
        if (_audioReady) {
            Raylib.UnloadSound(_stepSound);
            Raylib.UnloadSound(_digSound);
            Raylib.UnloadSound(_hitSound);
            Raylib.UnloadSound(_placeSound);
            Raylib.CloseAudioDevice();
            _audioReady = false;
        }
    }
}
