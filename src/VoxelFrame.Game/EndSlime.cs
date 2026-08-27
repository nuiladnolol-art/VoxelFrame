using System;
using System.Numerics;
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
    public const float MaxHealth = 200f;
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
        HurtTime -= dt;
        AttackCooldown -= dt;
        LeapCooldown -= dt;

        // Самовосстановление, пока живы эндер-кристаллы (каждый лечит ~1.5 HP/сек)
        int crystals = world.CountAliveEndCrystals();
        if (crystals > 0) {
            HealTimer += dt;
            if (HealTimer >= 1f) {
                HealTimer = 0f;
                Health = MathF.Min(MaxHealth, Health + crystals * 1.5f);
            }
        }

        var toPlayer = player.Position - Position;
        float dist = toPlayer.Length();

        // Босс просыпается, когда игрок приближается
        if (!Awake) {
            if (dist < 42f) {
                Awake = true;
                SoundSystem.PlayThunder();
                session.AddMessage("Слизень Края пробуждается!");
            } else {
                return;
            }
        }

        // Гравитация
        Velocity.Y -= 26f * dt;

        int phase = Phase;

        // Гигантский прыжок к игроку (в фазе 3 — чаще и мощнее)
        float leapCd = phase == 3 ? 0.9f : phase == 2 ? 1.3f : 1.7f;
        float leapSpeed = phase == 3 ? 20f : 16f;
        if (LeapCooldown <= 0f) {
            LeapCooldown = leapCd + (float)_random.NextDouble() * 1.1f;
            if (dist > 1.0f) {
                var dir = Vector3.Normalize(new Vector3(toPlayer.X, 0f, toPlayer.Z));
                Velocity.X = dir.X * leapSpeed;
                Velocity.Z = dir.Z * leapSpeed;
                Velocity.Y = phase == 3 ? 15f : 13f;
                SoundSystem.PlaySplash();
            } else if (dist > 0.01f) {
                Velocity.X = 0f; Velocity.Z = 0f; Velocity.Y = 8f;
            }
        }

        // Фаза 2+: плевок снарядом (зелёный «слизневый шар»)
        if (phase >= 2 && SpitCooldown <= 0f && dist > 4f && dist < 34f) {
            SpitCooldown = phase == 3 ? 1.8f : 2.8f;
            var aim = Vector3.Normalize(toPlayer + new Vector3(0f, 0.8f, 0f));
            world.Arrows.Add(new ArrowProjectile(Position + new Vector3(0f, HalfSizeY, 0f), aim * 15f) {
                IsSlimeSpit = true, Damage = 5.95f   // −33%, затем ещё −15%
            });
            SoundSystem.PlayBowShoot();
        }

        // Фаза 3: ударная волна при контакте с землёй рядом с игроком
        if (phase == 3 && SlamCooldown <= 0f && dist < 9f && IsGrounded) {
            SlamCooldown = 5.5f;
            float slamDmg = 5.95f * (1f - dist / 9f);   // −33%, затем ещё −15%
            player.ApplyDamage(slamDmg, session, Position);
            session.AddMessage($"Ударная волна Слизня Края! -{slamDmg:F0} HP");
            var kb = Vector3.Normalize(new Vector3(toPlayer.X, 0f, toPlayer.Z)) * 8f;
            player.Velocity += new Vector3(kb.X, 4f, kb.Z);
            world.SpawnCrit(Position + new Vector3(0f, HalfSizeY, 0f), 22);
            SoundSystem.PlayExplosion();
        }

        // Фаза 3: призыв эндэрменов
        if (phase == 3 && SummonCooldown <= 0f) {
            SummonCooldown = 8f;
            for (int i = 0; i < 2; i++) {
                var sp = Position + new Vector3((_random.NextSingle() - 0.5f) * 8f, 0.5f, (_random.NextSingle() - 0.5f) * 8f);
                world.HostileMobs.Add(new HostileMob(HostileType.Enderman, sp));
            }
            session.AddMessage("Слизень Края призывает эндэрменов!");
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

        // Удар при контакте (урон растёт с фазой; −33%, затем ещё −15%)
        float contactDmg = phase == 3 ? 10.2f : phase == 2 ? 7.65f : 5.95f;
        if (dist < HalfSizeXZ + 1.2f && AttackCooldown <= 0f) {
            AttackCooldown = 1.2f;
            player.ApplyDamage(contactDmg, session, Position);
            session.AddMessage($"Слизень Края обрушился на вас! -{contactDmg:F0} HP");
            SoundSystem.PlayCreeperHiss();
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
        if (!Alive) return;
        Health -= damage;
        HurtTime = 0.4f;
        SoundSystem.PlayHit();
        if (Health <= 0f) {
            Die(world, session);
        }
    }

    private void Die(GameWorld world, GameSession session) {
        Alive = false;
        world.EndBossDefeated = true;
        var pos = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
        world.SpawnPickup(GameData.EndSlimeItem.Id, 1, pos);
        world.SpawnPickup(GameData.EnderPearlItem.Id, _random.Next(4, 9), pos);
        world.SpawnPickup(GameData.TotemItem.Id, 1, pos); // органичный источник тотема — награда за победу над боссом
        SoundSystem.PlayExplosion();
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
