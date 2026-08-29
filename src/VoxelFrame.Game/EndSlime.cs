using System;
using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Босс измерений — гигантский Слизень Края (вместо Дракона Эндера).
/// - 200 HP, крупные прыжки к игроку, ближний бой.
/// - Лечится, пока жив хоть один эндер-кристалл.
/// - Повержен только после уничтожения всех кристаллов и добивания босса.
/// </summary>
public sealed class EndSlime {
    public const float MaxHealth = 160f;
    public const float HalfSizeXZ = 1.7f;
    public const float HalfSizeY = 1.0f;
    public const float EyeHeight = 0.9f;

    public Vector3 Position;
    public Vector3 Velocity;
    public float Health = MaxHealth;
    public bool Alive = true;
    public bool Awake;
    public float HurtTime;
    public float AttackCooldown;
    public float LeapCooldown = 2f;
    public float HealTimer;
    public float SpitCooldown = 2f;
    public float SlamCooldown = 4f;
    public float SummonCooldown = 6f;
    public bool IsGrounded;
    public bool IsDying;
    public float DeathTimer;

    /// <summary>Окно уязвимости: после серии прыжков босс отдыхает и не атакует.</summary>
    public bool IsResting;
    public float RestTimer;
    public int LeapBurstCounter;

    /// <summary>Телеграф ударной волны: &gt;0 — под боссом рисуется предупреждающее кольцо.</summary>
    public float SlamWarningTimer;

    private int _notifiedPhase;
    private bool _healHintShown;
    private bool _slamHintShown;
    private bool _slamArmed;

    /// <summary>Фаза боя: 1 (обычная) → 2 (бросает снаряды) → 3 (ярость: призыв, ударная волна).</summary>
    public int Phase => Health > MaxHealth * 0.66f ? 1 : Health > MaxHealth * 0.33f ? 2 : 3;

    private readonly Random _random;
    private readonly Vector3 _islandCenter;

    public EndSlime(Vector3 position, Vector3 islandCenter, int seed) {
        Position = position;
        _islandCenter = islandCenter;
        _random = new Random(seed);
    }

    public void Tick(float dt, GameWorld world, Player player, GameSession session, Vector3 islandCenter, float islandTopY) {
        if (!Alive) return;

        // Кинематографичная смерть босса (подобно Дракону Края)
        if (IsDying) {
            DeathTimer += dt;
            Velocity = Vector3.Zero;
            Position.Y += 1.3f * dt;
            Position.X += MathF.Sin(DeathTimer * 35f) * 0.05f;
            Position.Z += MathF.Cos(DeathTimer * 35f) * 0.05f;
            world.SpawnCrit(Position + new Vector3((_random.NextSingle() - 0.5f) * 3.5f, (_random.NextSingle() - 0.5f) * 2.5f, (_random.NextSingle() - 0.5f) * 3.5f), 8);
            if ((int)(DeathTimer * 4) != (int)((DeathTimer - dt) * 4)) {
                SoundSystem.PlayBabakherHiss();
            }
            if (DeathTimer >= 3.6f) {
                Die(world, session);
            }
            return;
        }

        HurtTime -= dt;
        AttackCooldown -= dt;
        LeapCooldown -= dt;

        // Телеграф ударной волны гаснет со временем
        if (SlamWarningTimer > 0f) SlamWarningTimer -= dt;

        // Самовосстановление, пока живы эндер-кристаллы (каждый лечит ~1.5 HP/сек)
        int crystals = world.CountAliveEndCrystals();
        if (crystals > 0) {
            HealTimer += dt;
            if (HealTimer >= 1f) {
                HealTimer = 0f;
                float before = Health;
                Health = MathF.Min(MaxHealth, Health + crystals * 0.75f); // нерф: было 1.5 HP/сек за кристалл
                // Луч лечения: зелёные частицы летят от каждого живого кристалла к боссу
                if (Health > before && Awake && !IsDying) {
                    foreach (var cry in world.EndCrystals) {
                        if (!cry.Alive) continue;
                        world.SpawnHealBeam(cry.Position, Position, 3);
                    }
                    // Одноразовая подсказка: почему босс не умирает
                    if (!_healHintShown) {
                        _healHintShown = true;
                        session.AddMessage("§aСлизень Края поглощает силу кристаллов! Разрушьте их, чтобы остановить лечение!");
                        SoundSystem.PlayPop();
                    }
                }
            }
        }

        var toPlayer = player.Position - Position;
        float dist = toPlayer.Length();

        // Босс просыпается, когда игрок приближается
        if (!Awake) {
            if (dist < 42f) {
                Awake = true;
                SoundSystem.PlayThunder();
                session.ShowTitle("СЛИЗЕНЬ КРАЯ", "Древний титан пробудился!", 4.0f, new Color(120, 255, 180, 255), new Color(220, 255, 235, 255));
            } else {
                return;
            }
        }

        // Объявление фаз один раз при пересечении порога HP
        int phaseNow = Phase;
        if (phaseNow != _notifiedPhase) {
            bool woke = _notifiedPhase == 0;
            _notifiedPhase = phaseNow;
            if (!woke && Awake) {
                if (phaseNow == 2) {
                    session.ShowTitle("ФАЗА 2: ЯРОСТЬ", "Слизень Края начинает плеваться кислотой!", 2.6f,
                        new Color(255, 200, 60, 255), new Color(255, 240, 190, 255));
                    SoundSystem.PlayBabakherHiss();
                    world.SpawnCrit(Position + new Vector3(0f, HalfSizeY, 0f), 20);
                } else if (phaseNow == 3) {
                    session.ShowTitle("ФАЗА 3: ОСВОБОЖДЕНИЕ", "Ударные волны и призыв слуг — берегитесь!", 2.6f,
                        new Color(255, 90, 60, 255), new Color(255, 210, 190, 255));
                    SoundSystem.PlayThunder();
                    world.SpawnCrit(Position + new Vector3(0f, HalfSizeY, 0f), 26);
                }
            }
        }

        // Гравитация (применяется и во время отдыха, чтобы босс не завис в воздухе)
        Velocity.Y -= 26f * dt;

        // Окно уязвимости (как у Истинного Слизня): после серии прыжков босс отдыхает
        if (IsResting) {
            RestTimer -= dt;
            Velocity.X *= 0.85f;
            Velocity.Z *= 0.85f;
            if (RestTimer <= 0f) {
                IsResting = false;
                LeapBurstCounter = 0;
                LeapCooldown = 0.6f;
            }
            var halfRest = new Vector3(HalfSizeXZ, HalfSizeY, HalfSizeXZ);
            IsGrounded = Collision.Move(world, ref Position, halfRest, ref Velocity, dt, ignoreDoors: true);
            if (IsGrounded && Velocity.Y < 0f) Velocity.Y = 0f;
            SmashThroughBlocks(world);
            return;
        }

        int phase = Phase;

        // Защита от столбов (Anti-Camping): если игрок залез на высокий столб
        bool playerCampingHigh = toPlayer.Y > 3.2f;

        // Гигантский прыжок к игроку (в фазе 3 — чаще и мощнее)
        float leapCd = phase == 3 ? 0.9f : phase == 2 ? 1.3f : 1.7f;
        float leapSpeed = phase == 3 ? 20f : 16f;
        if (LeapCooldown <= 0f) {
            LeapCooldown = leapCd + (float)_random.NextDouble() * 1.0f;
            LeapBurstCounter++;
            if (playerCampingHigh) {
                // Высотный сокрушительный прыжок прямо на верхушку столба
                var dir = Vector3.Normalize(new Vector3(toPlayer.X, 0f, toPlayer.Z));
                Velocity.X = dir.X * 18f;
                Velocity.Z = dir.Z * 18f;
                Velocity.Y = MathF.Max(18f, MathF.Sqrt(2f * 26f * (toPlayer.Y + 2.5f)));
                SoundSystem.PlaySplash();
                session.ShowTitle("СОКРУШИТЕЛЬНЫЙ ПРЫЖОК", "Слизень Края сметает опору под вами!", 2.0f, new Color(100, 255, 180, 255));
            } else if (dist > 1.0f) {
                var dir = Vector3.Normalize(new Vector3(toPlayer.X, 0f, toPlayer.Z));
                Velocity.X = dir.X * leapSpeed;
                Velocity.Z = dir.Z * leapSpeed;
                Velocity.Y = phase == 3 ? 15f : 13f;
                SoundSystem.PlaySplash();
            } else if (dist > 0.01f) {
                Velocity.X = 0f; Velocity.Z = 0f; Velocity.Y = 8f;
            }

            // После серии прыжков — короткая передышка: окно уязвимости для игрока
            if (LeapBurstCounter >= (phase == 3 ? 5 : 3)) {
                IsResting = true;
                RestTimer = phase == 3 ? 1.4f : 1.9f;
                session.ShowTitle("ПЕРЕДЫШКА БОССА", "Слизень истощён — окно для атаки!", 1.8f, new Color(255, 230, 80, 255));
            }
        }

        // Фаза 2+: плевок снарядом (зелёный «слизневый шар»)
        SpitCooldown -= dt;
        if (phase >= 2 && SpitCooldown <= 0f && dist > 4f && dist < 34f) {
            SpitCooldown = phase == 3 ? 1.8f : 2.8f;
            var aim = Vector3.Normalize(toPlayer + new Vector3(0f, 0.8f, 0f));
            world.Arrows.Add(new ArrowProjectile(Position + new Vector3(0f, HalfSizeY, 0f), aim * 15f) {
                IsSlimeSpit = true, Damage = 4.0f   // нерф: было 5.95
            });
            SoundSystem.PlayBowShoot();
        }

        // Фаза 3: телеграф ударной волны — красное кольцо на земле за 0.6с до удара
        if (phase == 3 && SlamCooldown <= 0f && dist < 9f && IsGrounded) {
            SlamWarningTimer = 0.6f;
            SlamCooldown = 6.1f; // 0.6с предупреждения + 5.5с перезарядки
            _slamArmed = true;
            SoundSystem.PlayBabakherHiss();
            if (!_slamHintShown) {
                _slamHintShown = true;
                session.AddMessage("§eКрасное кольцо = ударная волна. Прыгайте в момент удара!");
            }
        }

        // Фаза 3: ударная волна после телеграфа (бьёт, если босс ещё на земле)
        if (phase == 3 && _slamArmed && SlamWarningTimer <= 0f && IsGrounded) {
            _slamArmed = false;
            float slamDmg = 4.0f * (1f - Math.Min(dist, 9f) / 9f);   // нерф: было 5.95
            player.ApplyDamage(slamDmg, session, Position);
            session.AddMessage($"Ударная волна Слизня Края! -{slamDmg:F0} HP");
            var kb = Vector3.Normalize(new Vector3(toPlayer.X, 0f, toPlayer.Z)) * 8f;
            player.Velocity += new Vector3(kb.X, 4f, kb.Z);
            world.SpawnCrit(Position + new Vector3(0f, HalfSizeY, 0f), 22);
            SoundSystem.PlayExplosion();
        }

        // Фаза 3: призыв эндэрменов (нерф: 1 слуга и реже, чтобы не захлёбываться толпой в бою)
        SummonCooldown -= dt;
        if (phase == 3 && SummonCooldown <= 0f) {
            SummonCooldown = 14f;
            var sp = Position + new Vector3((_random.NextSingle() - 0.5f) * 8f, 0.5f, (_random.NextSingle() - 0.5f) * 8f);
            world.HostileMobs.Add(new HostileMob(HostileType.Enderman, sp));
            session.AddMessage("Слизень Края призывает эндэрмена!");
        }

        // Не даём боссу улететь с острова: сильное притяжение к центру, а при вылете — возврат на арену
        var toCenter = _islandCenter - Position;
        float centerDist = toCenter.Length();
        if (centerDist > 46f) {
            var dirCenter = Vector3.Normalize(new Vector3(toCenter.X, 0.0f, toCenter.Z));
            Velocity.X += dirCenter.X * 60f * dt;
            Velocity.Z += dirCenter.Z * 60f * dt;
            if (centerDist > 80f) {
                // Улетел слишком далеко — телепортируем обратно на арену
                Position = new Vector3(_islandCenter.X + 5f, _islandCenter.Y + 3f, _islandCenter.Z + 5f);
                Velocity = Vector3.Zero;
            }
        }
        // Упал в пустоту под остров — возвращаем на арену
        if (Position.Y < _islandCenter.Y - 8f) {
            Position = new Vector3(_islandCenter.X + 5f, _islandCenter.Y + 3f, _islandCenter.Z + 5f);
            Velocity = Vector3.Zero;
        }

        // Удар при контакте (урон растёт с фазой; нерф ~-22%: было 5.95/7.65/10.2)
        float contactDmg = phase == 3 ? 8.0f : phase == 2 ? 6.0f : 4.5f;
        if (dist < HalfSizeXZ + 1.2f && AttackCooldown <= 0f) {
            AttackCooldown = 1.2f;
            player.ApplyDamage(contactDmg, session, Position);
            session.AddMessage($"Слизень Края обрушился на вас! -{contactDmg:F0} HP");
            SoundSystem.PlayBabakherHiss();
        }

        var half = new Vector3(HalfSizeXZ, HalfSizeY, HalfSizeXZ);
        IsGrounded = Collision.Move(world, ref Position, half, ref Velocity, dt, ignoreDoors: true);
        if (IsGrounded && Velocity.Y < 0f) Velocity.Y = 0f;

        // Слизень ломает блоки на своём пути (кроме обсидиана, бедрока и кристаллов)
        SmashThroughBlocks(world);
    }

    /// <summary>Разрушает твёрдые блоки в объёме тела босса, не трогая пол под ногами
    /// и защищённые блоки (обсидиан, бедрок, эндер-кристаллы).</summary>
    private void SmashThroughBlocks(GameWorld world) {
        int minX = (int)MathF.Floor(Position.X - HalfSizeXZ);
        int maxX = (int)MathF.Floor(Position.X + HalfSizeXZ);
        int minZ = (int)MathF.Floor(Position.Z - HalfSizeXZ);
        int maxZ = (int)MathF.Floor(Position.Z + HalfSizeXZ);
        int minY = (int)MathF.Floor(Position.Y - HalfSizeY);
        int maxY = (int)MathF.Floor(Position.Y + HalfSizeY);
        for (int x = minX; x <= maxX; x++) {
            for (int z = minZ; z <= maxZ; z++) {
                for (int y = minY + 1; y <= maxY; y++) { // нижний ряд (пол) не трогаем
                    var p = new Vec3i(x, y, z);
                    var v = world.GetVoxel(p);
                    if (v.TypeId == 0) continue;
                    if (v.TypeId == GameData.BObsidian.Id || v.TypeId == GameData.BObsidianPillar.Id ||
                        v.TypeId == GameData.BBedrock.Id || v.TypeId == GameData.BEnderCrystal.Id) continue;
                    var b = GameData.GetBlock(v.TypeId);
                    if (b.IsSolid) world.RemoveBlock(p);
                }
            }
        }
    }

    public void TakeDamage(float damage, GameWorld world, GameSession session) {
        if (!Alive || IsDying) return;
        Health -= damage;
        HurtTime = 0.4f;
        SoundSystem.PlayHit();
        if (Health <= 0f) {
            Health = 0f;
            IsDying = true;
            DeathTimer = 0f;
            session.ShowTitle("ТИТАН ПОВЕРЖЕН", "Энергия Края высвобождается...", 3.8f, new Color(120, 255, 180, 255));
            SoundSystem.PlayThunder();
        }
    }

    private void Die(GameWorld world, GameSession session) {
        Alive = false;
        IsDying = false;
        world.EndBossDefeated = true;
        var pos = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
        world.SpawnPickup(GameData.EndSlimeItem.Id, 1, pos);
        world.SpawnPickup(GameData.EnderPearlItem.Id, _random.Next(4, 9), pos);
        world.SpawnPickup(GameData.TotemItem.Id, 1, pos); // органичный источник тотема — награда за победу над боссом
        SoundSystem.PlayExplosion();
        session.ShowTitle("СЛИЗЕНЬ КРАЯ ПОВЕРЖЕН", "Острова Энда освобождены... Но это лишь начало.", 5.0f, new Color(120, 255, 180, 255));
        session.AddMessage("Слизень Края повержен! Энд свободен!");
        SpawnExitPortal(world);
    }

    private void SpawnExitPortal(GameWorld world) {
        int cx = (int)MathF.Floor(_islandCenter.X);
        int cy = (int)MathF.Floor(_islandCenter.Y);
        int cz = (int)MathF.Floor(_islandCenter.Z);
        // Выходной портал — небольшая площадка под ногами у центра острова
        for (int dx = -1; dx <= 1; dx++) {
            for (int dz = -1; dz <= 1; dz++) {
                world.PlacePlacedBlock(new Vec3i(cx + dx, cy, cz + dz), GameData.BEndPortal);
            }
        }
    }
}
