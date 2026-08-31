using System;
using System.Collections.Generic;
using System.IO;
using Raylib_cs;

namespace VoxelFrame.Game;

public enum SoundCategory {
    Master,
    Music,
    Blocks,
    Creatures,
    Weather,
    Player
}

/// <summary>
/// Процедурная и файловая звуковая система с поддержкой категорий громкости
/// и фоновой музыки (Background Music / BGM).
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
    private static Sound _babakherHissSound;

    // Интерактив, визуал и атмосфера
    private static Sound _eatSound;
    private static Sound _splashSound;
    private static Sound _popSound;
    private static Sound _chestSound;
    private static Sound _doorOpenSound;
    private static Sound _doorCloseSound;
    private static Sound _dupePoliceSound;
    private static Sound _totemSound;
    private static Sound _thunderSound;

    // Фоновая музыка (BGM)
    private static readonly List<string> _musicFiles = new();
    private static Music _currentMusic;
    private static bool _musicLoaded;
    private static bool _musicPlaying;
    private static float _musicPauseTimer = 1.0f; // небольшая пауза перед стартом первого трека
    private static int _currentMusicIndex = -1;
    private static readonly Random _musicRng = new();

    // Музыкальная пластинка (Disc Music)
    private static Music _discMusic;
    private static bool _discLoaded;
    private static bool _discPlaying;

    public static bool IsDiscPlaying => _discLoaded && _discPlaying && Raylib.IsMusicStreamPlaying(_discMusic);

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
                _babakherHissSound = LoadProceduralSound(CreateNoiseWav(44100 * 3 / 2, 0.85f, highPass: true));

                // Интерактив и атмосфера
                _eatSound = LoadProceduralSound(CreateToneWav(44100 / 8, 280f, 420f, 0.45f));
                _splashSound = LoadProceduralSound(CreateSplashWav(44100 / 4, 0.50f));
                _popSound = LoadProceduralSound(CreateToneWav(44100 / 16, 550f, 850f, 0.30f));
                _chestSound = LoadProceduralSound(CreateToneWav(44100 / 7, 360f, 480f, 0.35f));
                _doorOpenSound = LoadProceduralSound(CreateToneWav(44100 / 6, 220f, 360f, 0.45f));
                _doorCloseSound = LoadProceduralSound(CreateToneWav(44100 / 6, 360f, 180f, 0.45f));
                _dupePoliceSound = LoadProceduralSound(CreateToneWav(44100 / 4, 880f, 220f, 0.65f));
                _thunderSound = LoadProceduralSound(CreateThunderWav(44100 * 3, 0.95f));

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

                // Поиск музыкальных файлов
                DiscoverMusicFiles();

                _audioReady = true;
            }
        } catch {
            _audioReady = false;
        }
    }

    private static void DiscoverMusicFiles() {
        _musicFiles.Clear();
        string[] probeStarts = { Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory };
        foreach (var start in probeStarts) {
            var dir = new DirectoryInfo(start);
            while (dir != null) {
                string musicDir = Path.Combine(dir.FullName, "assets", "music");
                if (Directory.Exists(musicDir)) {
                    foreach (var f in Directory.GetFiles(musicDir)) {
                        string name = Path.GetFileName(f).ToLowerInvariant();
                        if (name.StartsWith("disc_") || name.StartsWith("record_")) continue; // Пластинки исключаем из обычной фоновой музыки
                        string ext = Path.GetExtension(f).ToLowerInvariant();
                        if (ext is ".mp3" or ".ogg" or ".wav" or ".flac") {
                            if (!_musicFiles.Contains(f)) _musicFiles.Add(f);
                        }
                    }
                }
                dir = dir.Parent;
            }
        }
    }

    public static float GetCategoryVolume(SoundCategory cat) {
        float master = SaveSystem.SoundVolume / 100f;
        if (master <= 0.001f) return 0f;

        float catFactor = cat switch {
            SoundCategory.Master => 1.0f,
            SoundCategory.Music => SaveSystem.MusicVolume / 100f,
            SoundCategory.Blocks => SaveSystem.BlocksVolume / 100f,
            SoundCategory.Creatures => SaveSystem.CreaturesVolume / 100f,
            SoundCategory.Weather => SaveSystem.WeatherVolume / 100f,
            SoundCategory.Player => SaveSystem.PlayerVolume / 100f,
            _ => 1.0f
        };
        return Math.Clamp(master * catFactor, 0f, 1f);
    }

    private static string? FindSoundFile(string fileName) {
        string[] probeStarts = { Directory.GetCurrentDirectory(), AppDomain.CurrentDomain.BaseDirectory };
        foreach (var start in probeStarts) {
            var dir = new DirectoryInfo(start);
            while (dir != null) {
                string candidate = Path.Combine(dir.FullName, "assets", "sounds", fileName);
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(dir.FullName, "assets", "music", fileName);
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }
        return null;
    }

    public static System.Numerics.Vector3 ListenerPosition;
    public static System.Numerics.Vector3 ListenerForward = new(0f, 0f, 1f);
    public static System.Numerics.Vector3 ListenerRight = new(1f, 0f, 0f);

    private static void Play(Sound s, SoundCategory cat = SoundCategory.Player, float pitch = 1.0f) {
        if (!_audioReady) return;
        float vol = GetCategoryVolume(cat);
        if (vol <= 0.001f) return;

        Raylib.SetSoundVolume(s, vol);
        Raylib.SetSoundPitch(s, pitch);
        Raylib.SetSoundPan(s, 0.5f);
        Raylib.PlaySound(s);
    }

    /// <summary>
    /// Воспроизведение звука в 3D пространстве с учетом расстояния до слушателя (падающая громкость)
    /// и направления источника (стерео-панорамирование влево/вправо).
    /// </summary>
    public static void Play3D(Sound s, System.Numerics.Vector3 soundPos, SoundCategory cat = SoundCategory.Player, float pitch = 1.0f, float maxDistance = 24f, float baseVol = 1.0f) {
        if (!_audioReady) return;

        float dist = System.Numerics.Vector3.Distance(soundPos, ListenerPosition);
        if (dist > maxDistance) return; // Звук за пределами радиуса слышимости — полная тишина

        float distFrac = Math.Clamp(1.0f - (dist / maxDistance), 0f, 1f);
        float falloff = distFrac * distFrac; // Плавное квадратичное затухание

        float categoryVol = GetCategoryVolume(cat);
        float finalVol = categoryVol * baseVol * falloff;
        if (finalVol <= 0.002f) return;

        // Расчет панорамирования стерео (0.0 = слева, 0.5 = центр, 1.0 = справа)
        float pan = 0.5f;
        if (dist > 0.3f) {
            var soundDir = System.Numerics.Vector3.Normalize(soundPos - ListenerPosition);
            float rightDot = System.Numerics.Vector3.Dot(ListenerRight, soundDir);
            pan = Math.Clamp(0.5f + rightDot * 0.42f, 0.08f, 0.92f);
        }

        Raylib.SetSoundVolume(s, finalVol);
        Raylib.SetSoundPitch(s, pitch);
        Raylib.SetSoundPan(s, pan);
        Raylib.PlaySound(s);
    }

    public static void PlayStep(ushort blockId = 0) => PlayStepAt(ListenerPosition, blockId);
    public static void PlayStepAt(System.Numerics.Vector3 pos, ushort blockId = 0) {
        float p = 0.9f + (float)Random.Shared.NextDouble() * 0.2f;
        Sound snd = (blockId == GameData.BStone.Id || blockId == GameData.BCobblestone.Id || blockId == GameData.BObsidian.Id ||
            blockId == GameData.BCoalOre.Id || blockId == GameData.BIronOre.Id || blockId == GameData.BGoldOre.Id ||
            blockId == GameData.BDiamondOre.Id || blockId == GameData.BMossyCobblestone.Id ||
            blockId == GameData.BNetherrack.Id || blockId == GameData.BNetherBrick.Id || blockId == GameData.BNetherQuartzOre.Id ||
            blockId == GameData.BChiseledSandstone.Id || blockId == GameData.BFurnace.Id) ? _stepStone :
            (blockId == GameData.BLog.Id || blockId == GameData.BPlanks.Id || blockId == GameData.BWorkbench.Id || blockId == GameData.BChest.Id) ? _stepWood :
            (blockId == GameData.BGravel.Id) ? _stepGravel :
            (blockId == GameData.BSand.Id || blockId == GameData.BSoulSand.Id) ? _stepSand :
            (blockId == GameData.BWater.Id || blockId == GameData.BLava.Id) ? _stepWater : _stepGrass;

        Play3D(snd, pos, SoundCategory.Blocks, p, maxDistance: 16f, baseVol: 0.85f);
    }

    public static void PlayDig(ushort blockId = 0) => PlayDigAt(ListenerPosition, blockId);
    public static void PlayDigAt(System.Numerics.Vector3 pos, ushort blockId = 0) {
        float p = 0.9f + (float)Random.Shared.NextDouble() * 0.2f;
        Sound snd = (blockId == GameData.BStone.Id || blockId == GameData.BCobblestone.Id || blockId == GameData.BObsidian.Id ||
            blockId == GameData.BCoalOre.Id || blockId == GameData.BIronOre.Id || blockId == GameData.BGoldOre.Id ||
            blockId == GameData.BDiamondOre.Id || blockId == GameData.BMossyCobblestone.Id ||
            blockId == GameData.BNetherrack.Id || blockId == GameData.BNetherBrick.Id || blockId == GameData.BNetherQuartzOre.Id) ? _digStone :
            (blockId == GameData.BLog.Id || blockId == GameData.BPlanks.Id || blockId == GameData.BWorkbench.Id || blockId == GameData.BChest.Id) ? _digWood :
            (blockId == GameData.BSand.Id || blockId == GameData.BGravel.Id || blockId == GameData.BSoulSand.Id) ? _digSand : _digGrass;

        Play3D(snd, pos, SoundCategory.Blocks, p, maxDistance: 20f, baseVol: 0.9f);
    }

    public static void PlayPlace() => PlayPlaceAt(ListenerPosition);
    public static void PlayPlaceAt(System.Numerics.Vector3 pos) => Play3D(_placeSound, pos, SoundCategory.Blocks, 0.95f + (float)Random.Shared.NextDouble() * 0.1f, maxDistance: 20f);

    public static void PlayHit() => PlayHitAt(ListenerPosition);
    public static void PlayHitAt(System.Numerics.Vector3 pos) => Play3D(_hitSound, pos, SoundCategory.Player, 0.9f + (float)Random.Shared.NextDouble() * 0.2f, maxDistance: 22f);

    public static void PlayPlayerHurt() => Play(_hitSound, SoundCategory.Player, 0.85f);
    public static void PlayStrongAttack() => Play(_critSound, SoundCategory.Player, 1.15f);
    public static void PlayWeakAttack() => Play(_hitSound, SoundCategory.Player, 1.30f);
    public static void PlayCrit() => Play(_critSound, SoundCategory.Player, 1.0f + (float)Random.Shared.NextDouble() * 0.15f);

    public static void PlayBreakTool() => PlayBreakToolAt(ListenerPosition);
    public static void PlayBreakToolAt(System.Numerics.Vector3 pos) => Play3D(_breakToolSound, pos, SoundCategory.Blocks, 1.0f, maxDistance: 20f);

    public static void PlayBowShoot() => PlayBowShootAt(ListenerPosition);
    public static void PlayBowShootAt(System.Numerics.Vector3 pos) => Play3D(_placeSound, pos, SoundCategory.Player, 1.4f, maxDistance: 22f);

    public static void PlayArrowHit() => PlayArrowHitAt(ListenerPosition);
    public static void PlayArrowHitAt(System.Numerics.Vector3 pos) => Play3D(_arrowHitSound, pos, SoundCategory.Player, 1.0f, maxDistance: 24f);

    private static double _lastExplodeTime;
    public static void PlayExplosion() => PlayExplosionAt(ListenerPosition);
    public static void PlayExplosionAt(System.Numerics.Vector3 pos) {
        double now = Raylib.GetTime();
        if (now - _lastExplodeTime < 0.1) return;
        _lastExplodeTime = now;
        Play3D(_explosionSound, pos, SoundCategory.Creatures, 1.0f, maxDistance: 36f, baseVol: 1.2f);
    }

    public static void PlayShieldBlock() => Play(_shieldBlockSound, SoundCategory.Player, 0.95f + (float)Random.Shared.NextDouble() * 0.1f);
    public static void PlayBabakherHiss() => PlayBabakherHissAt(ListenerPosition);
    public static void PlayBabakherHissAt(System.Numerics.Vector3 pos) => Play3D(_babakherHissSound, pos, SoundCategory.Creatures, 1.0f, maxDistance: 22f);

    public static void PlayCaveAmbiance() { /* Заменено фоновой музыкой */ }
    public static void PlayThunder() => Play(_thunderSound, SoundCategory.Weather, 0.9f + (float)Random.Shared.NextDouble() * 0.2f);
    public static void PlayEat() => PlayEatAt(ListenerPosition);
    public static void PlayEatAt(System.Numerics.Vector3 pos) => Play3D(_eatSound, pos, SoundCategory.Player, 0.9f + (float)Random.Shared.NextDouble() * 0.2f, maxDistance: 16f);

    public static void PlaySplash() => PlaySplashAt(ListenerPosition);
    public static void PlaySplashAt(System.Numerics.Vector3 pos) => Play3D(_splashSound, pos, SoundCategory.Weather, 1.0f, maxDistance: 20f);

    public static void PlayPop() => PlayPopAt(ListenerPosition);
    public static void PlayPopAt(System.Numerics.Vector3 pos) => Play3D(_popSound, pos, SoundCategory.Player, 0.95f + (float)Random.Shared.NextDouble() * 0.1f, maxDistance: 18f);

    public static void PlayChest() => PlayChestAt(ListenerPosition);
    public static void PlayChestAt(System.Numerics.Vector3 pos) => Play3D(_chestSound, pos, SoundCategory.Player, 1.0f, maxDistance: 18f);

    public static void PlayDoorOpen() => PlayDoorOpenAt(ListenerPosition);
    public static void PlayDoorOpenAt(System.Numerics.Vector3 pos) => Play3D(_doorOpenSound, pos, SoundCategory.Blocks, 0.95f + (float)Random.Shared.NextDouble() * 0.1f, maxDistance: 20f);

    public static void PlayDoorClose() => PlayDoorCloseAt(ListenerPosition);
    public static void PlayDoorCloseAt(System.Numerics.Vector3 pos) => Play3D(_doorCloseSound, pos, SoundCategory.Blocks, 0.95f + (float)Random.Shared.NextDouble() * 0.1f, maxDistance: 20f);

    public static void PlayDupePolice() => Play(_dupePoliceSound, SoundCategory.Player, 1.0f);
    public static void PlayTotem() => Play(_totemSound, SoundCategory.Player, 1.0f);
    public static void StopTotem() {
        if (_audioReady) Raylib.StopSound(_totemSound);
    }
    public static void PlayFertilize() => PlayFertilizeAt(ListenerPosition);
    public static void PlayFertilizeAt(System.Numerics.Vector3 pos) => Play3D(_placeSound, pos, SoundCategory.Blocks, 1.2f, maxDistance: 18f);

    // ── Музыкальная пластинка (Music Disc) ───────────────────────────────────

    public static bool ToggleDisc(string fileName = "disc_circus.mp3") {
        if (IsDiscPlaying) {
            StopDisc();
            return false;
        } else {
            return PlayDisc(fileName);
        }
    }

    public static bool PlayDisc(string fileName = "disc_circus.mp3") {
        if (!_audioReady) return false;
        try {
            string? discPath = FindSoundFile(fileName) 
                            ?? FindSoundFile("disc_circus.mp3")
                            ?? FindSoundFile("record_13.mp3");

            if (discPath != null && File.Exists(discPath)) {
                if (_discLoaded) {
                    Raylib.StopMusicStream(_discMusic);
                    Raylib.UnloadMusicStream(_discMusic);
                    _discLoaded = false;
                    _discPlaying = false;
                }
                _discMusic = Raylib.LoadMusicStream(discPath);
                if (_discMusic.FrameCount > 0) {
                    _discLoaded = true;
                    _discPlaying = true;
                    // Ставим фоновую музыку на паузу во время пластинки
                    if (_musicLoaded && _musicPlaying) {
                        Raylib.PauseMusicStream(_currentMusic);
                    }
                    Raylib.SetMusicVolume(_discMusic, GetCategoryVolume(SoundCategory.Music));
                    Raylib.PlayMusicStream(_discMusic);
                    return true;
                }
            }
        } catch {
            _discLoaded = false;
            _discPlaying = false;
        }
        return false;
    }

    public static void StopDisc() {
        try {
            if (_discLoaded) {
                Raylib.StopMusicStream(_discMusic);
                Raylib.UnloadMusicStream(_discMusic);
            }
        } catch {
            // Игнорируем возможные ошибки аудио-потока
        } finally {
            _discLoaded = false;
            _discPlaying = false;
        }
    }

    // ── Фоновая музыка (Background Music Streamer) ───────────────────────────

    public static void UpdateMusic(float dt) {
        if (!_audioReady) return;

        float musicVol = GetCategoryVolume(SoundCategory.Music);

        // 1. Если играет пластинка — обновляем её и глушим фоновый BGM
        if (_discLoaded && _discPlaying) {
            if (musicVol <= 0.001f) {
                Raylib.PauseMusicStream(_discMusic);
            } else {
                if (!Raylib.IsMusicStreamPlaying(_discMusic)) {
                    Raylib.ResumeMusicStream(_discMusic);
                }
                Raylib.SetMusicVolume(_discMusic, musicVol);
                Raylib.UpdateMusicStream(_discMusic);
            }

            if (!Raylib.IsMusicStreamPlaying(_discMusic)) {
                // Пластинка завершила воспроизведение
                StopDisc();
            } else {
                if (_musicLoaded && _musicPlaying) {
                    Raylib.PauseMusicStream(_currentMusic);
                }
                return;
            }
        }

        // 2. Фоновый стрим BGM
        if (musicVol <= 0.001f || _musicFiles.Count == 0) {
            if (_musicPlaying && _musicLoaded) {
                Raylib.PauseMusicStream(_currentMusic);
                _musicPlaying = false;
            }
            return;
        }

        if (_musicLoaded) {
            Raylib.SetMusicVolume(_currentMusic, musicVol);
            Raylib.UpdateMusicStream(_currentMusic);

            if (!Raylib.IsMusicStreamPlaying(_currentMusic)) {
                // Трек завершился или был на паузе
                if (_musicPlaying) {
                    // Трек только что доиграл до конца
                    _musicPlaying = false;
                    _musicPauseTimer = 15f + (float)_musicRng.NextDouble() * 25f; // Пауза 15-40 сек перед следующим треком
                } else {
                    _musicPauseTimer -= dt;
                    if (_musicPauseTimer <= 0f) {
                        PlayNextTrack();
                    }
                }
            } else {
                _musicPlaying = true;
            }
        } else {
            _musicPauseTimer -= dt;
            if (_musicPauseTimer <= 0f) {
                PlayNextTrack();
            }
        }
    }

    private static void PlayNextTrack() {
        if (_musicFiles.Count == 0) return;

        try {
            if (_musicLoaded) {
                Raylib.StopMusicStream(_currentMusic);
                Raylib.UnloadMusicStream(_currentMusic);
                _musicLoaded = false;
                _musicPlaying = false;
            }

            // Выбираем следующий случайный трек
            int nextIdx = _musicRng.Next(0, _musicFiles.Count);
            if (nextIdx == _currentMusicIndex && _musicFiles.Count > 1) {
                nextIdx = (nextIdx + 1) % _musicFiles.Count;
            }
            _currentMusicIndex = nextIdx;

            string trackPath = _musicFiles[_currentMusicIndex];
            if (File.Exists(trackPath)) {
                _currentMusic = Raylib.LoadMusicStream(trackPath);
                if (_currentMusic.FrameCount > 0) {
                    _musicLoaded = true;
                    Raylib.SetMusicVolume(_currentMusic, GetCategoryVolume(SoundCategory.Music));
                    Raylib.PlayMusicStream(_currentMusic);
                    _musicPlaying = true;
                }
            }
        } catch {
            _musicLoaded = false;
            _musicPlaying = false;
            _musicPauseTimer = 10f;
        }
    }

    public static void PlayBackgroundMusic() {
        // Вызывается для гарантированного запуска фоновой музыки
        if (!_musicLoaded || !_musicPlaying) {
            _musicPauseTimer = 0f;
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

    private static byte[] CreateThunderWav(int sampleCount, float volume) {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteWavHeader(bw, sampleCount);

        var rng = new Random(54321);
        int sampleRate = 44100;
        float lowFilter = 0f;
        float lowFilter2 = 0f;
        for (int i = 0; i < sampleCount; i++) {
            float t = (float)i / sampleCount;
            // Удар молнии в начале + мощный раскатистый рокот с реверберацией
            float strike = MathF.Exp(-t * 18f) * (float)(rng.NextDouble() * 2.0 - 1.0) * 0.8f;
            float rumbleNoise = (float)(rng.NextDouble() * 2.0 - 1.0);
            
            // 2-полюсный НЧ-фильтр для создания глубокого басового рокота 45-80 Гц
            float freq = 55f + MathF.Sin(t * 12f) * 20f;
            float rc = 1.0f / (2f * MathF.PI * freq);
            float dtSample = 1.0f / sampleRate;
            float alpha = dtSample / (rc + dtSample);
            lowFilter += alpha * (rumbleNoise - lowFilter);
            lowFilter2 += alpha * (lowFilter - lowFilter2);

            // Огибающая громкости грома с несколькими эхо-волнами
            float echoWaves = 1.0f + 0.5f * MathF.Sin(t * 15f) * MathF.Exp(-t * 2.5f);
            float env = MathF.Pow(1.0f - t, 1.8f) * echoWaves;

            float sampleVal = (strike + lowFilter2 * 3.5f) * env * volume;
            short sample = (short)Math.Clamp((int)(sampleVal * 32767f), -32768, 32767);
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

            if (_musicLoaded) {
                Raylib.StopMusicStream(_currentMusic);
                Raylib.UnloadMusicStream(_currentMusic);
                _musicLoaded = false;
            }

            StopDisc();

            Raylib.CloseAudioDevice();
            _audioReady = false;
        }
    }
}
