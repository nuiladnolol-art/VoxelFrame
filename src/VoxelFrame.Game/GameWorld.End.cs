using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

// ── Энд: босс (Слизень Края) и эндер-кристаллы ─────────────────────────────
public sealed partial class GameWorld {
    /// <summary>Босс измерения — гигантский Слизень Края.</summary>
    public EndSlime? EndBoss;

    /// <summary>Повержен ли босс (персистентно).</summary>
    public bool EndBossDefeated;

    // ── Мини-боссы артефактов: встречаются один раз за мир ──────────────────
    public bool NetherBossSpawned;
    public bool SwampBossSpawned;
    public bool DesertBossSpawned;

    /// <summary>Спавнит мини-босса в точке, если он ещё не встречался в этом мире.</summary>
    public void SpawnMiniBoss(HostileType type, Vector3 pos) {
        if (type == HostileType.NetherLord) {
            if (NetherBossSpawned) return;
            NetherBossSpawned = true;
        } else if (type == HostileType.SwampGuardian) {
            if (SwampBossSpawned) return;
            SwampBossSpawned = true;
        } else if (type == HostileType.DesertGuardian) {
            if (DesertBossSpawned) return;
            DesertBossSpawned = true;
        } else return;
        HostileMobs.Add(new HostileMob(type, pos));
        SoundSystem.PlayBabakherHiss();
    }

    /// <summary>Позиции всех когда-либо сгенерированных эндер-кристаллов (для подсчёта живых).</summary>
    private readonly HashSet<Vec3i> _endCrystals = new();

    /// <summary>Эндер-кристаллы как сущности (парят над якорем, взрываются от касания).</summary>
    public readonly List<EndCrystalEntity> EndCrystals = new();

    /// <summary>Регистрирует эндер-кристаллы в свежесозданном чанке Энда и спавнит сущности.</summary>
    private void ScanEndCrystals(GameChunk gc) {
        for (int x = 0; x < Chunk.SizeX; x++) {
            for (int z = 0; z < Chunk.SizeZ; z++) {
                for (int y = 0; y < Chunk.SizeY; y++) {
                    if (gc.Chunk.Get(x, y, z).TypeId == GameData.BEnderCrystal.Id) {
                        int wx = gc.Coord.X * Chunk.SizeX + x;
                        int wy = gc.Coord.Y * Chunk.SizeY + y;
                        int wz = gc.Coord.Z * Chunk.SizeZ + z;
                        var pos = new Vec3i(wx, wy, wz);
                        _endCrystals.Add(pos);
                        TrySpawnCrystalEntityAt(pos);
                    }
                }
            }
        }
    }

    /// <summary>Спавнит сущность-кристалл над якорным блоком, если её ещё нет.</summary>
    public void TrySpawnCrystalEntityAt(Vec3i pos) {
        if (Dimension != Dimension.End) return;
        foreach (var c in EndCrystals) {
            if (c.Anchor == pos) return;
        }
        EndCrystals.Add(new EndCrystalEntity(pos));
    }

    /// <summary>Тик всех кристаллов Энда: касание/снаряд → взрыв; мёртвые убираем.</summary>
    public void TickEndCrystals(float dt, Player player, GameSession session) {
        for (int i = EndCrystals.Count - 1; i >= 0; i--) {
            var c = EndCrystals[i];
            if (!c.Alive) { EndCrystals.RemoveAt(i); continue; }
            c.Tick(dt, this, player, session);
            if (!c.Alive) EndCrystals.RemoveAt(i);
        }
    }

    /// <summary>
    /// Число живых эндер-кристаллов (пока хоть один жив — Слизень Края лечится).
    /// Медленно пробегаем по набору позиций и проверяем, что блок остался кристаллом.
    /// </summary>
    public int CountAliveEndCrystals() {
        int count = 0;
        foreach (var p in _endCrystals) {
            if (GetVoxel(p).TypeId == GameData.BEnderCrystal.Id) count++;
        }
        return count;
    }

    /// <summary>Финальный тайный босс — Истинный Слизень Края (в Бездне).</summary>
    public TrueEndSlime? TrueVoidBoss;
    public bool TrueVoidBossDefeated;
    public bool VoidAltarTriggered;
    public int VoidBossIntroStep;
    public float VoidBossIntroTimer;

    /// <summary>Тик босса и спавн его при первом входе в измерение.</summary>
    public void TickEndSlime(float dt, Player player, GameSession session, Vector3 islandCenter, float islandTopY) {
        if (Dimension != Dimension.End || EndBossDefeated) return;

        if (EndBoss == null) {
            // Босс спавнится близко к точке входа и на твёрдом куполе, а не в пустоте далеко вверху
            EndBoss = new EndSlime(islandCenter + new Vector3(10f, 3f, 0f), islandCenter, Seed ^ 0x5A1E5);
        }
        EndBoss.Tick(dt, this, player, session, islandCenter, islandTopY);
    }

    /// <summary>Тик Истинного Слизня Края и катсцены пробуждения в Бездне.</summary>
    public void TickTrueVoidBoss(float dt, Player player, GameSession session) {
        if (Dimension != Dimension.Void || TrueVoidBossDefeated) return;

        // Катсцена пробуждения босса при активации алтаря
        if (VoidBossIntroStep > 0 && VoidBossIntroStep <= 4) {
            VoidBossIntroTimer += dt;
            if (VoidBossIntroTimer >= 2.4f) {
                VoidBossIntroTimer = 0f;
                VoidBossIntroStep++;

                if (VoidBossIntroStep == 2) {
                    session.ShowTitle("ИСТИННЫЙ ВЛАДЫКА", "«Наверху была лишь моя жалкая тень, запертая на острове!»", 3.2f, new Color(200, 60, 255, 255), new Color(245, 210, 255, 255));
                    session.AddMessage("§5[Истинный Слизень]: Наверху была лишь моя жалкая тень, заточённая в клетке острова!");
                    SoundSystem.PlaySplash();
                } else if (VoidBossIntroStep == 3) {
                    session.ShowTitle("ПЕЧАТИ СНЯТЫ", "«Ты сам принёс мне Ключ Бездны и снял печати с моей истинной мощи!»", 3.2f, new Color(220, 40, 180, 255), new Color(255, 180, 220, 255));
                    session.AddMessage("§5[Истинный Слизень]: Ты сам принёс мне Ключ Бездны и снял печати с моей истинной мощи!");
                    SoundSystem.PlayBabakherHiss();
                } else if (VoidBossIntroStep == 4) {
                    session.ShowTitle("ИСТИННЫЙ СЛИЗЕНЬ КРАЯ", "«Я — ИСТИННЫЙ СЛИЗЕНЬ КРАЯ! УЗРИ СВОЮ ПОГИБЕЛЬ!»", 5.0f, new Color(255, 30, 30, 255), new Color(255, 210, 80, 255));
                    session.AddMessage("§4[Истинный Слизень]: Я — ИСТИННЫЙ СЛИЗЕНЬ КРАЯ! УЗРИ СВОЮ ПОГИБЕЛЬ!");
                    SoundSystem.PlayThunder();
                    TrueVoidBoss = new TrueEndSlime(new Vector3(0.5f, 13f, 8.5f), new Vector3(0.5f, 11f, 0.5f), Seed ^ 0x901DF00);
                }
            }
        }

        TrueVoidBoss?.Tick(dt, this, player, session);
    }
}
