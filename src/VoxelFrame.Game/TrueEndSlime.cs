using System;
using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Истинный Слизень Края (True End Slime) — финальный тайный босс всей игры в измерении Бездны.
/// - 350 HP, колоссальные габариты, тёмно-пурпурная аура.
/// - 3 фазы: Прыжки с волнами Бездны -> Залпы сферами Пустоты -> Гравитационные разломы и Ярость.
/// </summary>
public enum TrueBossState { Chasing, Resting, Dying }

public sealed class TrueEndSlime {
    public const float MaxHealth = 350f;
    public const float HalfSizeXZ = 2.4f;
    public const float HalfSizeY = 1.4f;
    public const float EyeHeight = 1.2f;

    public Vector3 Position;
    public Vector3 Velocity;
    public float Health = MaxHealth;
    public bool Alive = true;
    public bool Awake = true;
    public float HurtTime;
    public float AttackCooldown;
    public float LeapCooldown = 1.2f;
    public float SpitCooldown = 2f;
    public float SlamCooldown = 4f;
    public float SingularityCooldown = 8f;
    public bool IsGrounded;
    public TrueBossState State = TrueBossState.Chasing;
    public float RestTimer;
    public int AttackBurstCounter;
    public bool IsDying;
    public float DeathTimer;

    /// <summary>Телеграф сингулярности Бездны: &gt;0 — вокруг игрока рисуется фиолетовое кольцо.</summary>
    public float SingularityWarningTimer;
    private int _notifiedPhase;

    /// <summary>Фаза боя: 1 (обычная) → 2 (сферы Бездны) → 3 (Абсолютная Ярость: сингулярность и ударные волны).</summary>
    public int Phase => Health > MaxHealth * 0.66f ? 1 : Health > MaxHealth * 0.33f ? 2 : 3;

    private readonly Random _random;
    private readonly Vector3 _arenaCenter;

    public TrueEndSlime(Vector3 position, Vector3 arenaCenter, int seed) {
        Position = position;
        _arenaCenter = arenaCenter;
        _random = new Random(seed);
    }

    public void Tick(float dt, GameWorld world, Player player, GameSession session) {
        if (!Alive) return;

        // Кинематографичная смерть босса (подобно Дракону Края)
        if (IsDying) {
            DeathTimer += dt;
            Velocity = Vector3.Zero;
            Position.Y += 1.4f * dt;
            Position.X += MathF.Sin(DeathTimer * 40f) * 0.08f;
            Position.Z += MathF.Cos(DeathTimer * 40f) * 0.08f;
            world.SpawnCrit(Position + new Vector3((_random.NextSingle() - 0.5f) * 4.5f, (_random.NextSingle() - 0.5f) * 3f, (_random.NextSingle() - 0.5f) * 4.5f), 12);
            if ((int)(DeathTimer * 4) != (int)((DeathTimer - dt) * 4)) {
                SoundSystem.PlayThunder();
            }
            if (DeathTimer >= 4.0f) {
                Die(world, session);
            }
            return;
        }

        HurtTime -= dt;
        AttackCooldown -= dt;
        LeapCooldown -= dt;
        SpitCooldown -= dt;
        SlamCooldown -= dt;
        SingularityCooldown -= dt;

        var toPlayer = player.Position - Position;
        float dist = toPlayer.Length();

        // Гравитация
        Velocity.Y -= 28f * dt;

        int phase = Phase;

        // Объявление фаз один раз при пересечении порога HP
        if (phase != _notifiedPhase) {
            bool first = _notifiedPhase == 0;
            _notifiedPhase = phase;
            if (!first) {
                if (phase == 2) {
                    session.ShowTitle("ФАЗА 2: СФЕРЫ БЕЗДНЫ", "Истинный Слизень швыряет сферы пустоты!", 2.6f,
                        new Color(200, 60, 255, 255), new Color(245, 210, 255, 255));
                    SoundSystem.PlayBabakherHiss();
                    world.SpawnCrit(Position + new Vector3(0f, HalfSizeY, 0f), 24);
                } else if (phase == 3) {
                    session.ShowTitle("ФАЗА 3: АБСОЛЮТНАЯ ЯРОСТЬ", "Сингулярности и сокрушительные волны!", 2.8f,
                        new Color(255, 30, 30, 255), new Color(255, 210, 80, 255));
                    SoundSystem.PlayThunder();
                    world.SpawnCrit(Position + new Vector3(0f, HalfSizeY, 0f), 30);
                }
            }
        }

        // Фаза передышки (Resting): босс замирает на 2 секунды после серии прыжков для окна атаки
        if (State == TrueBossState.Resting) {
            RestTimer -= dt;
            Velocity.X *= 0.85f;
            Velocity.Z *= 0.85f;
            if (RestTimer <= 0f) {
                State = TrueBossState.Chasing;
                AttackBurstCounter = 0;
                LeapCooldown = 0.6f;
            }
        } else {
            // Активное постоянное движение к игроку (не стоит на месте!)
            if (dist > 1.5f) {
                var crawlDir = Vector3.Normalize(new Vector3(toPlayer.X, 0f, toPlayer.Z));
                float crawlSpeed = phase == 3 ? 12f : 9f;
                Velocity.X += crawlDir.X * crawlSpeed * dt;
                Velocity.Z += crawlDir.Z * crawlSpeed * dt;
            }

            // Телепортация за спину при попытке игрока убежать слишком далеко
            if (dist > 26f && _random.NextDouble() < 0.05) {
                var tpTarget = player.Position - player.Forward * 3.5f + new Vector3(0f, 1f, 0f);
                if ((tpTarget - _arenaCenter).Length() < 38f) {
                    Position = tpTarget;
                    Velocity = Vector3.Zero;
                    SoundSystem.PlayThunder();
                    world.SpawnCrit(Position, 30);
                    session.ShowTitle("РАЗРЫВ ПРОСТРАНСТВА", "Бездна настигает вас!", 2.0f, new Color(220, 50, 255, 255));
                }
            }

            // Прыжки к игроку
            float leapCd = phase == 3 ? 0.7f : phase == 2 ? 1.0f : 1.3f;
            float leapSpeed = phase == 3 ? 24f : 19f;
            if (LeapCooldown <= 0f) {
                LeapCooldown = leapCd + (float)_random.NextDouble() * 0.7f;
                AttackBurstCounter++;
                if (dist > 1.2f) {
                    var dir = Vector3.Normalize(new Vector3(toPlayer.X, 0f, toPlayer.Z));
                    Velocity.X = dir.X * leapSpeed;
                    Velocity.Z = dir.Z * leapSpeed;
                    Velocity.Y = phase == 3 ? 16f : 13f;
                    SoundSystem.PlaySplash();
                } else if (dist > 0.01f) {
                    Velocity.X = 0f; Velocity.Z = 0f; Velocity.Y = 9f;
                }

                // После 3-4 яростных атак — короткая передышка
                if (AttackBurstCounter >= (phase == 3 ? 5 : 3)) {
                    State = TrueBossState.Resting;
                    RestTimer = phase == 3 ? 1.6f : 2.2f;
                    session.ShowTitle("ПЕРЕДЫШКА БОССА", "Истинный Слизень истощён — бейте!", 1.8f, new Color(255, 230, 80, 255));
                }
            }

            // Фаза 2+: Залпы сферами Бездны (Void Orbs)
            if (phase >= 2 && SpitCooldown <= 0f && dist > 3f && dist < 45f) {
                SpitCooldown = phase == 3 ? 1.3f : 2.0f;
                var aim = Vector3.Normalize(toPlayer + new Vector3(0f, 0.6f, 0f));
                world.Arrows.Add(new ArrowProjectile(Position + new Vector3(0f, HalfSizeY, 0f), aim * 18f) {
                    IsSlimeSpit = true, Damage = 7.5f
                });
                SoundSystem.PlayBowShoot();
            }

            // Фаза 3: телеграф сингулярности — 0.6с кольцо-предупреждение, затем рывок-притяжение
            if (phase == 3 && SingularityCooldown <= 0f && SingularityWarningTimer <= 0f) {
                SingularityWarningTimer = 0.6f;
                SingularityCooldown = 8.1f; // 0.6с предупреждения + 7.5с перезарядки
                SoundSystem.PlayBabakherHiss();
            }

            // Фаза 3: Гравитационная сингулярность (притягивает игрока к боссу) после телеграфа
            if (phase == 3 && SingularityWarningTimer > 0f) {
                SingularityWarningTimer -= dt;
                if (SingularityWarningTimer <= 0f) {
                    session.ShowTitle("СИНГУЛЯРНОСТЬ БЕЗДНЫ", "Искажение пространства притягивает вас!", 2.5f, new Color(220, 40, 255, 255));
                    var pullDir = Vector3.Normalize(Position - player.Position);
                    player.Velocity += pullDir * 12f + new Vector3(0f, 3f, 0f);
                    world.SpawnCrit(player.Position, 25);
                    SoundSystem.PlayThunder();
                }
            }
        }

        // Ударная волна при приземлении
        if (SlamCooldown <= 0f && dist < 12f && IsGrounded) {
            SlamCooldown = phase == 3 ? 3.5f : 5.0f;
            float slamDmg = (phase == 3 ? 12f : 8f) * (1f - dist / 12f);
            player.ApplyDamage(slamDmg, session, Position);
            session.AddMessage($"Ударная волна Истинного Слизня! -{slamDmg:F0} HP");
            var kb = Vector3.Normalize(new Vector3(toPlayer.X, 0f, toPlayer.Z)) * 10f;
            player.Velocity += new Vector3(kb.X, 5f, kb.Z);
            world.SpawnCrit(Position + new Vector3(0f, HalfSizeY, 0f), 30);
            SoundSystem.PlayExplosion();
        }

        // Удержание на арене Бездны (радиус арены ~45 блоков)
        var toCenter = _arenaCenter - Position;
        float centerDist = toCenter.Length();
        if (centerDist > 40f) {
            var dirCenter = Vector3.Normalize(new Vector3(toCenter.X, 0.0f, toCenter.Z));
            Velocity.X += dirCenter.X * 70f * dt;
            Velocity.Z += dirCenter.Z * 70f * dt;
            if (centerDist > 70f) {
                Position = new Vector3(_arenaCenter.X, _arenaCenter.Y + 4f, _arenaCenter.Z);
                Velocity = Vector3.Zero;
            }
        }
        if (Position.Y < _arenaCenter.Y - 6f) {
            Position = new Vector3(_arenaCenter.X, _arenaCenter.Y + 4f, _arenaCenter.Z);
            Velocity = Vector3.Zero;
        }

        // Контактный урон
        float contactDmg = phase == 3 ? 14f : phase == 2 ? 10f : 8f;
        if (dist < HalfSizeXZ + 1.2f && AttackCooldown <= 0f) {
            AttackCooldown = 1.0f;
            player.ApplyDamage(contactDmg, session, Position);
            session.AddMessage($"Истинный Слизень сокрушает вас! -{contactDmg:F0} HP");
            SoundSystem.PlayBabakherHiss();
        }

        var half = new Vector3(HalfSizeXZ, HalfSizeY, HalfSizeXZ);
        IsGrounded = Collision.Move(world, ref Position, half, ref Velocity, dt, ignoreDoors: true);
        if (IsGrounded && Velocity.Y < 0f) Velocity.Y = 0f;
    }

    public void TakeDamage(float amount, GameWorld world, GameSession session) {
        if (!Alive || IsDying) return;
        Health -= amount;
        HurtTime = 0.35f;
        SoundSystem.PlayPop();
        world.SpawnCrit(Position + new Vector3(0f, HalfSizeY, 0f), 15);

        if (Health <= 0f) {
            Health = 0f;
            IsDying = true;
            DeathTimer = 0f;
            session.ShowTitle("ВЛАДЫКА ПОВЕРЖЕН", "Бездна сжимается в сингулярность...", 4.2f, new Color(255, 50, 220, 255));
            SoundSystem.PlayThunder();
        }
    }

    public void Die(GameWorld world, GameSession session) {
        Alive = false;
        IsDying = false;
        Health = 0f;
        world.TrueVoidBossDefeated = true;
        session.CreditsType = 2; // Устанавливаем истинные победные титры
        SoundSystem.PlayThunder();
        SoundSystem.PlayExplosion();
        world.SpawnCrit(Position + new Vector3(0f, HalfSizeY, 0f), 60);

        session.ShowTitle("ИСТИННЫЙ ТРИУМФ", "Бездна очищена! Все миры спасены!", 6.0f, new Color(255, 215, 0, 255), new Color(255, 240, 180, 255));
        session.AddMessage("ИСТИННЫЙ СЛИЗЕНЬ КРАЯ ПОВЕРЖЕН!");
        session.AddMessage("Тьма рассеивается... В центре материализуется Портал Триумфа!");

        // Спавним Портал Триумфа на обсидиановом постаменте
        SpawnVictoryPortal(world);
    }

    private void SpawnVictoryPortal(GameWorld world) {
        int cx = (int)MathF.Round(_arenaCenter.X);
        int cy = (int)MathF.Round(_arenaCenter.Y);
        int cz = (int)MathF.Round(_arenaCenter.Z);

        // 1. Постамент из обсидиана
        for (int dx = -2; dx <= 2; dx++) {
            for (int dz = -2; dz <= 2; dz++) {
                world.PlacePlacedBlock(new Vec3i(cx + dx, cy, cz + dz), GameData.BObsidian);
            }
        }

        // 2. Портал Триумфа в центре
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                world.PlacePlacedBlock(new Vec3i(cx + dx, cy + 1, cz + dz), GameData.BEndPortal);
            }
        }
    }
}
