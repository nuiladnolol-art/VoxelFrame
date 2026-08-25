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
    public bool IsGrounded;

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

        // Гигантский прыжок к игроку (раз в ~1.6-3 сек)
        if (LeapCooldown <= 0f) {
            LeapCooldown = 1.6f + (float)_random.NextDouble() * 1.4f;
            if (dist > 1.0f) {
                var dir = Vector3.Normalize(new Vector3(toPlayer.X, 0f, toPlayer.Z));
                Velocity.X = dir.X * 16f;
                Velocity.Z = dir.Z * 16f;
                Velocity.Y = 13f;
                SoundSystem.PlaySplash();
            } else if (dist > 0.01f) {
                Velocity.X = 0f; Velocity.Z = 0f; Velocity.Y = 8f;
            }
        }

        // Ограничиваем босса вблизи центрального острова, если игрок рядом с ним
        var toCenter = _islandCenter - Position;
        if (toCenter.Length() > 70f) {
            var dirCenter = Vector3.Normalize(new Vector3(toCenter.X, 0.0f, toCenter.Z));
            Velocity.X += dirCenter.X * 18f * dt;
            Velocity.Z += dirCenter.Z * 18f * dt;
        }

        // Удар при контакте
        if (dist < HalfSizeXZ + 1.2f && AttackCooldown <= 0f) {
            AttackCooldown = 1.4f;
            player.ApplyDamage(12f, session, Position);
            session.AddMessage("Слизень Края обрушился на вас! -12 HP");
            SoundSystem.PlayCreeperHiss();
        }

        var half = new Vector3(HalfSizeXZ, HalfSizeY, HalfSizeXZ);
        IsGrounded = Collision.Move(world, ref Position, half, ref Velocity, dt);
        if (IsGrounded && Velocity.Y < 0f) Velocity.Y = 0f;
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
