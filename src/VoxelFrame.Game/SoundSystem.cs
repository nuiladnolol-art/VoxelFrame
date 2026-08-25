using System.IO;
using Raylib_cs;

namespace VoxelFrame.Game;

/// <summary>
/// Процедурная звуковая система на основе генератора WAV-байтов в память.
/// Создает богатый спектр 16-битных звуков для различных материалов блоков,
/// шагов, оружия, поломки инструментов, сундуков и взрывов без внешних файлов.
/// </summary>
public static class SoundSystem {
    private static bool _audioReady;

    // Шаги по разным поверхностям
    private static Sound _stepGrass;
    private static Sound _stepStone;
    private static Sound _stepWood;
    private static Sound _stepSand;
    private static Sound _stepGravel;
    private static Sound _stepWater;

    // Ломание и установка
    private static Sound _digGrass;
    private static Sound _digStone;
    private static Sound _digWood;
    private static Sound _digSand;
    private static Sound _placeSound;
    private static Sound _breakToolSound;

    // Бой и действия
    private static Sound _hitSound;
    private static Sound _critSound;
    private static Sound _bowShootSound;
    private static Sound _arrowHitSound;
    private static Sound _explosionSound;
    private static Sound _shieldBlockSound;
    private static Sound _creeperHissSound;

    // Интерактив, визуал и атмосфера
    private static Sound _eatSound;
    private static Sound _splashSound;
    private static Sound _popSound;
    private static Sound _chestSound;
    private static Sound _doorOpenSound;
    private static Sound _doorCloseSound;
    private static Sound _dupePoliceSound;
    private static Sound _totemSound;
    private static Sound _fertilizeSound;
    private static Sound _caveAmbianceSound;
    private static Sound _thunderSound;
    private static Sound _bgmMusic;

    public static void Initialize() {
        if (_audioReady) return;
        try {
            Raylib.InitAudioDevice();
            if (Raylib.IsAudioDeviceReady()) {
                // Шаги
                _stepGrass = LoadProceduralSound(CreateNoiseWav(44100 / 14, 0.22f, highPass: true));
                _stepStone = LoadProceduralSound(CreateToneWav(44100 / 18, 520f, 180f, 0.28f));
                _stepWood = LoadProceduralSound(CreateToneWav(44100 / 12, 240f, 120f, 0.32f));
                _stepSand = LoadProceduralSound(CreateNoiseWav(44100 / 10, 0.25f, highPass: false));
                _stepGravel = LoadProceduralSound(CreateCrunchWav(44100 / 12, 0.35f));
                _stepWater = LoadProceduralSound(CreateSplashWav(44100 / 10, 0.30f));

                // Ломание
                _digGrass = LoadProceduralSound(CreateNoiseWav(44100 / 8, 0.45f, highPass: true));
                _digStone = LoadProceduralSound(CreateToneWav(44100 / 7, 700f, 220f, 0.50f));
                _digWood = LoadProceduralSound(CreateToneWav(44100 / 8, 320f, 140f, 0.48f));
                _digSand = LoadProceduralSound(CreateNoiseWav(44100 / 7, 0.42f, highPass: false));
                _placeSound = LoadProceduralSound(CreateToneWav(44100 / 10, 420f, 260f, 0.35f));
                _breakToolSound = LoadProceduralSound(CreateCrunchWav(44100 / 4, 0.65f));

                // Боёвка
                _hitSound = LoadProceduralSound(CreateToneWav(44100 / 6, 140f, 50f, 0.60f));
                _critSound = LoadProceduralSound(CreateToneWav(44100 / 5, 880f, 440f, 0.55f));
                _bowShootSound = LoadProceduralSound(CreateToneWav(44100 / 7, 260f, 680f, 0.45f));
                _arrowHitSound = LoadProceduralSound(CreateToneWav(44100 / 16, 650f, 320f, 0.40f));
                _explosionSound = LoadProceduralSound(CreateNoiseWav(44100 / 2, 0.85f, highPass: false));
                _shieldBlockSound = LoadProceduralSound(CreateToneWav(44100 / 10, 850f, 320f, 0.65f));
                _creeperHissSound = LoadProceduralSound(CreateNoiseWav(44100 * 3 / 2, 0.85f, highPass: true));

                // Интерактив и атмосфера
                _eatSound = LoadProceduralSound(CreateToneWav(44100 / 8, 280f, 420f, 0.45f));
                _splashSound = LoadProceduralSound(CreateSplashWav(44100 / 4, 0.50f));
                _popSound = LoadProceduralSound(CreateToneWav(44100 / 16, 550f, 850f, 0.30f));
                _chestSound = LoadProceduralSound(CreateToneWav(44100 / 7, 360f, 480f, 0.35f));
                _doorOpenSound = LoadProceduralSound(CreateToneWav(44100 / 6, 220f, 360f, 0.45f));
                _doorCloseSound = LoadProceduralSound(CreateToneWav(44100 / 6, 360f, 180f, 0.45f));
                _dupePoliceSound = LoadProceduralSound(CreateToneWav(44100 / 4, 880f, 220f, 0.65f));
                _fertilizeSound = LoadProceduralSound(CreateToneWav(44100 / 6, 600f, 1200f, 0.40f));
                _caveAmbianceSound = LoadProceduralSound(CreateToneWav(44100 * 3, 110f, 75f, 0.40f));
                _thunderSound = LoadProceduralSound(CreateNoiseWav(44100 * 2, 0.90f, highPass: false));
                _bgmMusic = LoadProceduralSound(CreateToneWav(44100 * 6, 261.6f, 329.6f, 0.25f));

                // Звук тотема (MP3 / WAV файл, обрезанный ровно до 12.0 секунд)
                string? totemPath = FindSoundFile("totem.mp3") 
                                 ?? FindSoundFile("alive.mp3") 
                                 ?? FindSoundFile("totem.wav");

                if (totemPath != null && File.Exists(totemPath)) {
                    var wave = Raylib.LoadWave(totemPath);
                    if (wave.SampleCount > 0) {
                        int maxFrames = (int)(12 * wave.SampleRate);
                        if (wave.SampleCount > maxFrames) {
                            Raylib.WaveCrop(ref wave, 0, maxFrames);
                        }
                        _totemSound = Raylib.LoadSoundFromWave(wave);
                        Raylib.UnloadWave(wave);
                    } else {
                        _totemSound = Raylib.LoadSound(totemPath);
                    }
                } else {
                    _totemSound = LoadProceduralSound(CreateToneWav(44100 * 2, 580f, 880f, 0.70f));
                }

                _audioReady = true;
            }
        } catch {
            _audioReady = false;
        }
    }

    private static string? FindSoundFile(string fileName) {
        string[] probeStarts = { Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory };
        foreach (var start in probeStarts) {
            var dir = new DirectoryInfo(start);
            while (dir != null) {
                string candidate = Path.Combine(dir.FullName, "assets", "sounds", fileName);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }
        return null;
    }

    private static void Play(Sound s, float pitch = 1.0f) {
        if (!_audioReady || SaveSystem.SoundVolume <= 0) return;
        Raylib.SetMasterVolume(SaveSystem.SoundVolume / 100f);
        Raylib.SetSoundVolume(s, 1.0f);
        Raylib.SetSoundPitch(s, pitch);
        Raylib.PlaySound(s);
    }

    public static void PlayStep(ushort blockId = 0) {
        float p = 0.9f + (float)Random.Shared.NextDouble() * 0.2f;
        if (blockId == GameData.BStone.Id || blockId == GameData.BCobblestone.Id || blockId == GameData.BObsidian.Id ||
            blockId == GameData.BCoalOre.Id || blockId == GameData.BIronOre.Id || blockId == GameData.BGoldOre.Id ||
            blockId == GameData.BDiamondOre.Id || blockId == GameData.BRedstoneOre.Id || blockId == GameData.BMossyCobblestone.Id ||
            blockId == GameData.BNetherrack.Id || blockId == GameData.BNetherBrick.Id || blockId == GameData.BNetherQuartzOre.Id ||
            blockId == GameData.BChiseledSandstone.Id || blockId == GameData.BFurnace.Id) {
            Play(_stepStone, p);
        } else if (blockId == GameData.BLog.Id || blockId == GameData.BPlanks.Id || blockId == GameData.BWorkbench.Id || blockId == GameData.BChest.Id) {
            Play(_stepWood, p);
        } else if (blockId == GameData.BGravel.Id) {
            Play(_stepGravel, p);
        } else if (blockId == GameData.BSand.Id || blockId == GameData.BSoulSand.Id) {
            Play(_stepSand, p);
        } else if (blockId == GameData.BWater.Id || blockId == GameData.BLava.Id) {
            Play(_stepWater, p);
        } else {
            Play(_stepGrass, p);
        }
    }

    public static void PlayDig(ushort blockId = 0) {
        float p = 0.9f + (float)Random.Shared.NextDouble() * 0.2f;
        if (blockId == GameData.BStone.Id || blockId == GameData.BCobblestone.Id || blockId == GameData.BObsidian.Id ||
            blockId == GameData.BCoalOre.Id || blockId == GameData.BIronOre.Id || blockId == GameData.BGoldOre.Id ||
            blockId == GameData.BDiamondOre.Id || blockId == GameData.BRedstoneOre.Id || blockId == GameData.BMossyCobblestone.Id ||
            blockId == GameData.BNetherrack.Id || blockId == GameData.BNetherBrick.Id || blockId == GameData.BNetherQuartzOre.Id) {
            Play(_digStone, p);
        } else if (blockId == GameData.BLog.Id || blockId == GameData.BPlanks.Id || blockId == GameData.BWorkbench.Id || blockId == GameData.BChest.Id) {
            Play(_digWood, p);
        } else if (blockId == GameData.BSand.Id || blockId == GameData.BGravel.Id || blockId == GameData.BSoulSand.Id) {
            Play(_digSand, p);
        } else {
            Play(_digGrass, p);
        }
    }

    public static void PlayPlace() => Play(_placeSound, 0.95f + (float)Random.Shared.NextDouble() * 0.1f);
    public static void PlayHit() => Play(_hitSound, 0.9f + (float)Random.Shared.NextDouble() * 0.2f);
    public static void PlayPlayerHurt() => Play(_hitSound, 0.85f);
    public static void PlayStrongAttack() => Play(_critSound, 1.15f);
    public static void PlayWeakAttack() => Play(_hitSound, 1.30f);
    public static void PlayCrit() => Play(_critSound, 1.0f + (float)Random.Shared.NextDouble() * 0.15f);
    public static void PlayBreakTool() => Play(_breakToolSound);
    public static void PlayBowShoot() => Play(_placeSound, 1.4f); // Мягкий тихий щелчок тетивы вместо свистящего "вжух"
    public static void PlayArrowHit() => Play(_arrowHitSound);
    private static double _lastExplodeTime;
    public static void PlayExplosion() {
        double now = Raylib.GetTime();
        if (now - _lastExplodeTime < 0.1) return;
        _lastExplodeTime = now;
        Play(_explosionSound);
    }
    public static void PlayShieldBlock() => Play(_shieldBlockSound, 0.95f + (float)Random.Shared.NextDouble() * 0.1f);
    public static void PlayCreeperHiss() => Play(_creeperHissSound, 1.0f);
    public static void PlayCaveAmbiance() { /* Отключено по запросу */ }
    public static void PlayThunder() => Play(_thunderSound, 0.9f + (float)Random.Shared.NextDouble() * 0.2f);
    public static void PlayBackgroundMusic() { /* Отключено: устраняет гул и фризы аудиодрайвера */ }
    public static void PlayEat() => Play(_eatSound, 0.9f + (float)Random.Shared.NextDouble() * 0.2f);
    public static void PlaySplash() => Play(_splashSound);
    public static void PlayPop() => Play(_popSound, 0.95f + (float)Random.Shared.NextDouble() * 0.1f);
    public static void PlayChest() => Play(_chestSound);
    public static void PlayDoorOpen() => Play(_doorOpenSound, 0.95f + (float)Random.Shared.NextDouble() * 0.1f);
    public static void PlayDoorClose() => Play(_doorCloseSound, 0.95f + (float)Random.Shared.NextDouble() * 0.1f);
    public static void PlayDupePolice() => Play(_dupePoliceSound, 1.0f);
    public static void PlayTotem() => Play(_totemSound, 1.0f);
    public static void StopTotem() {
        if (_audioReady) Raylib.StopSound(_totemSound);
    }
    public static void PlayFertilize() => Play(_placeSound, 1.2f);

    private static unsafe Sound LoadProceduralSound(byte[] wavBytes) {
        fixed (byte* ptr = wavBytes)
        fixed (byte* ext = ".wav"u8) {
            Wave wave = Raylib.LoadWaveFromMemory((sbyte*)ext, ptr, wavBytes.Length);
            Sound sound = Raylib.LoadSoundFromWave(wave);
            Raylib.UnloadWave(wave);
            return sound;
        }
    }

    private static byte[] CreateNoiseWav(int sampleCount, float volume, bool highPass) {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, sampleCount);

        var rng = new Random(42);
        float last = 0f;
        for (int i = 0; i < sampleCount; i++) {
            float env = 1.0f - (float)i / sampleCount;
            float raw = (float)(rng.NextDouble() * 2.0 - 1.0);
            float noise = highPass ? (raw - last * 0.7f) : (raw * 0.5f + last * 0.5f);
            last = raw;
            short sample = (short)(noise * env * 32767f * volume);
            bw.Write(sample);
        }
        return ms.ToArray();
    }

    private static byte[] CreateToneWav(int sampleCount, float startFreq, float endFreq, float volume) {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, sampleCount);

        float phase = 0f;
        int sampleRate = 44100;
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

    private static byte[] CreateSplashWav(int sampleCount, float volume) {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, sampleCount);

        var rng = new Random(1337);
        float phase = 0f;
        int sampleRate = 44100;
        for (int i = 0; i < sampleCount; i++) {
            float t = (float)i / sampleCount;
            float env = MathF.Sin(MathF.PI * (1f - t));
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            phase += 2f * MathF.PI * (300f - 180f * t) / sampleRate;
            float wave = (noise * 0.6f + MathF.Sin(phase) * 0.4f) * env;
            short sample = (short)(wave * 32767f * volume);
            bw.Write(sample);
        }
        return ms.ToArray();
    }

    private static byte[] CreateCrunchWav(int sampleCount, float volume) {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, sampleCount);

        var rng = new Random(777);
        float phase = 0f;
        int sampleRate = 44100;
        for (int i = 0; i < sampleCount; i++) {
            float t = (float)i / sampleCount;
            float env = (1f - t) * (1f - t);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            phase += 2f * MathF.PI * (800f - 600f * t) / sampleRate;
            float wave = (noise * 0.7f + MathF.Sin(phase) * 0.3f) * env;
            short sample = (short)(wave * 32767f * volume);
            bw.Write(sample);
        }
        return ms.ToArray();
    }

    private static void WriteWavHeader(BinaryWriter bw, int sampleCount) {
        int sampleRate = 44100;
        short bitsPerSample = 16;
        short channels = 1;
        int subchunk2Size = sampleCount * channels * (bitsPerSample / 8);

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
    }

    /// <summary>
    /// Конвертирует WAV в формате IEEE Float 32-bit в стандартный PCM 16-bit,
    /// который корректно поддерживается Raylib.
    /// </summary>
    private static byte[]? ConvertFloat32ToPcm16Wav(byte[] input, ushort channels, uint sampleRate) {
        try {
            // Найти data chunk
            int dataOffset = -1, dataSize = -1;
            int pos = 12;
            while (pos + 8 <= input.Length) {
                string chunkId = System.Text.Encoding.ASCII.GetString(input, pos, 4);
                int chunkSize = System.BitConverter.ToInt32(input, pos + 4);
                if (chunkId == "data") {
                    dataOffset = pos + 8;
                    dataSize = chunkSize;
                    break;
                }
                pos += 8 + chunkSize;
            }
            if (dataOffset < 0 || dataSize <= 0) return null;

            int numSamples = dataSize / (channels * 4); // Float32 = 4 bytes per sample per channel
            // Downmix to mono если 2 канала, иначе оставляем как есть
            int outChannels = 1;
            int outSampleCount = numSamples;

            using var ms = new MemoryStream();
            using var bw = new BinaryWriter(ms);
            short bitsOut = 16;
            int byteRate = (int)(sampleRate * outChannels * bitsOut / 8);
            int blockAlign = outChannels * bitsOut / 8;
            int subchunk2Size = outSampleCount * blockAlign;
            bw.Write("RIFF"u8);
            bw.Write(36 + subchunk2Size);
            bw.Write("WAVE"u8);
            bw.Write("fmt "u8);
            bw.Write(16);
            bw.Write((short)1); // PCM
            bw.Write((short)outChannels);
            bw.Write((int)sampleRate);
            bw.Write(byteRate);
            bw.Write((short)blockAlign);
            bw.Write(bitsOut);
            bw.Write("data"u8);
            bw.Write(subchunk2Size);

            for (int i = 0; i < numSamples; i++) {
                float sample = 0f;
                for (int c = 0; c < channels; c++) {
                    float ch = System.BitConverter.ToSingle(input, dataOffset + (i * channels + c) * 4);
                    sample += ch;
                }
                sample /= channels; // Смешиваем каналы в моно
                short pcm = (short)Math.Clamp((int)(sample * 32767f), -32768, 32767);
                bw.Write(pcm);
            }
            return ms.ToArray();
        } catch {
            return null;
        }
    }

    public static void Shutdown() {
        if (_audioReady) {
            Raylib.UnloadSound(_stepGrass);
            Raylib.UnloadSound(_stepStone);
            Raylib.UnloadSound(_stepWood);
            Raylib.UnloadSound(_stepSand);
            Raylib.UnloadSound(_stepWater);

            Raylib.UnloadSound(_digGrass);
            Raylib.UnloadSound(_digStone);
            Raylib.UnloadSound(_digWood);
            Raylib.UnloadSound(_digSand);
            Raylib.UnloadSound(_placeSound);
            Raylib.UnloadSound(_breakToolSound);

            Raylib.UnloadSound(_hitSound);
            Raylib.UnloadSound(_critSound);
            Raylib.UnloadSound(_bowShootSound);
            Raylib.UnloadSound(_arrowHitSound);
            Raylib.UnloadSound(_explosionSound);

            Raylib.UnloadSound(_eatSound);
            Raylib.UnloadSound(_splashSound);
            Raylib.UnloadSound(_popSound);
            Raylib.UnloadSound(_chestSound);

            Raylib.CloseAudioDevice();
            _audioReady = false;
        }
    }
}
