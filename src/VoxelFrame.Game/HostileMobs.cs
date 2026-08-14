using System;
using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

public enum HostileType { Zombie, Creeper, Skeleton, Spider }

/// <summary>
/// Враждебные мобы: Zombie, Creeper, Skeleton, Spider.
/// - Спавн в темноте ночью или в пещерах.
/// - Аутентичный дроп (Зомби -> перья, Крипер -> порох, Скелет -> стрелы/кости, Паук -> нить).
/// - Зомби и скелеты горят на солнце.
/// </summary>
public sealed class HostileMob {
    public const float HalfSize = 0.45f;

    public HostileType Type;
    public Vector3 Position;
    public Vector3 Velocity;
    public float Health = 20f;
    public bool Alive = true;
    public float FuseTimer;   // для Крипера (1.5с отсчет до взрыва)
    public float AttackCooldown;
    public float HurtTime;
    public Vector2 WanderDir;
    private readonly Random _random = new();

    public float HalfSizeX => Type == HostileType.Spider ? 0.65f : 0.4f;
    public float HalfSizeY => Type == HostileType.Spider ? 0.35f : 0.85f;
    public float HalfSizeZ => Type == HostileType.Spider ? 0.65f : 0.4f;

    public HostileMob(HostileType type, Vector3 position) {
        Type = type;
        Position = position;
        Health = type switch {
            HostileType.Spider => 16f,
            HostileType.Skeleton => 20f,
            HostileType.Creeper => 20f,
            _ => 20f
        };
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

    public void Tick(float dt, GameWorld world, Player player, GameSession session) {
        if (!Alive) return;
        if (Position.Y < FallingBlock.VoidY) { Alive = false; return; }

        HurtTime -= dt;
        AttackCooldown -= dt;

        // Зомби и Скелеты горят при ярком солнечном свете днём
        var feetPos = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
        byte sun = world.GetSunLight(feetPos);
        float sky = session.DayNight.SkyFactor;
        if ((Type == HostileType.Zombie || Type == HostileType.Skeleton) && sun >= 12 && sky > 0.70f) {
            Health -= 4f * dt;
            HurtTime = 0.3f;
            if (Health <= 0f) {
                Die(world, session);
                return;
            }
        }

        var toPlayer = player.Position - Position;
        float dist = toPlayer.Length();

        Velocity.Y -= 22f * dt;
        bool grounded = Collision.Move(world, ref Position, new Vector3(HalfSizeX, HalfSizeY, HalfSizeZ), ref Velocity, dt);
        if (grounded && Velocity.Y < 0f) Velocity.Y = 0f;

        if (dist > 64f) { Alive = false; return; } // Деспавн

        if (dist < 20f) { // Агр на игрока
            var dir = Vector3.Normalize(new Vector3(toPlayer.X, 0f, toPlayer.Z));
            float speed = Type switch {
                HostileType.Spider => 3.4f,
                HostileType.Creeper => 2.3f,
                HostileType.Skeleton => (dist < 8f ? -1.5f : 2.0f), // Скелет держит дистанцию
                _ => 2.2f
            };

            Velocity.X = dir.X * speed;
            Velocity.Z = dir.Z * speed;

            // Проверка блока впереди для прыжка
            var aheadCell = new Vec3i((int)MathF.Floor(Position.X + dir.X * 0.6f), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z + dir.Z * 0.6f));
            if (world.IsSolidAt(aheadCell) && (grounded || Type == HostileType.Spider)) {
                Velocity.Y = Type == HostileType.Spider ? 9.5f : 8.5f;
            }

            // Логика Крипера (начинает шипеть и тикать только если видит игрока)
            if (Type == HostileType.Creeper) {
                var mobCenter = Position + new Vector3(0f, 0.45f, 0f);
                var playerCenter = player.Position + new Vector3(0f, 0.60f, 0f);
                bool canSeePlayer = HasLineOfSight(world, mobCenter, playerCenter) || HasLineOfSight(world, mobCenter, player.Eye);
                if (dist < 3.2f && canSeePlayer) {
                    FuseTimer += dt;
                    if (FuseTimer >= 1.5f) {
                        Explode(world, session);
                        Alive = false;
                        return;
                    }
                } else {
                    FuseTimer = MathF.Max(0f, FuseTimer - dt * 1.5f);
                }
            }

            // Атака Зомби и Паука (только при прямой видимости — блокирует урон сквозь стены и в коробке 1x1x2)
            if ((Type == HostileType.Zombie || Type == HostileType.Spider) && dist < 1.8f && AttackCooldown <= 0f) {
                var mobCenter = Position + new Vector3(0f, 0.35f, 0f);
                var playerCenter = player.Position + new Vector3(0f, 0.60f, 0f);
                if (HasLineOfSight(world, mobCenter, playerCenter) || HasLineOfSight(world, mobCenter, player.Eye)) {
                    AttackCooldown = 1.0f;
                    float dmg = Type == HostileType.Spider ? 3f : 4f;
                    player.Health = MathF.Max(0f, player.Health - dmg);
                    session.AddMessage($"{(Type == HostileType.Spider ? "Паук" : "Зомби")} нанёс урон -{dmg} HP!");
                    SoundSystem.PlayHit();
                }
            }

            // Атака Скелета (стрельба из лука с проверкой прямой видимости и визуальной стрелой)
            if (Type == HostileType.Skeleton && dist < 20f && dist > 1.8f && AttackCooldown <= 0f) {
                var eyePos = Position + new Vector3(0f, 0.65f, 0f);
                var targetPos = player.Position + new Vector3(0f, Player.EyeHeight * 0.6f, 0f);
                if (HasLineOfSight(world, eyePos, targetPos)) {
                    AttackCooldown = 2.0f;
                    var toTarget = targetPos - eyePos;
                    float toDist = toTarget.Length();
                    var arrowDir = Vector3.Normalize(toTarget);
                    var arrowVel = arrowDir * 18f + new Vector3(0f, MathF.Min(2.5f, toDist * 0.12f), 0f);
                    world.Arrows.Add(new ArrowProjectile(eyePos + arrowDir * 0.5f, arrowVel));
                    session.AddMessage("Скелет натянул тетиву и выстрелил стрелой!");
                }
            }
        } else {
            // Блуждание
            Velocity.X = WanderDir.X * 1.0f;
            Velocity.Z = WanderDir.Y * 1.0f;
            if (_random.NextDouble() < 0.02) {
                float angle = (float)_random.NextDouble() * MathF.Tau;
                WanderDir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }
        }
    }

    public static bool HasLineOfSight(GameWorld world, Vector3 from, Vector3 to) {
        var delta = to - from;
        float dist = delta.Length();
        if (dist < 0.3f) return true;
        var step = delta / dist * 0.4f;
        int steps = (int)(dist / 0.4f);
        var cur = from;
        for (int i = 0; i < steps; i++) {
            cur += step;
            var cell = new Vec3i((int)MathF.Floor(cur.X), (int)MathF.Floor(cur.Y), (int)MathF.Floor(cur.Z));
            if (world.IsSolidAt(cell)) return false;
        }
        return true;
    }

    public void Die(GameWorld world, GameSession session) {
        Alive = false;
        var pos = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
        
        switch (Type) {
            case HostileType.Zombie:
                world.SpawnPickup(GameData.FeatherItem.Id, _random.Next(1, 3), pos); // В Alpha зомби дропали перья!
                session.AddMessage("Зомби побежден — выпало перо");
                break;
            case HostileType.Creeper:
                world.SpawnPickup(GameData.GunpowderItem.Id, _random.Next(1, 3), pos);
                session.AddMessage("Крипер побежден — выпал порох");
                break;
            case HostileType.Skeleton:
                world.SpawnPickup(GameData.ArrowItem.Id, _random.Next(1, 3), pos);
                world.SpawnPickup(GameData.BoneItem.Id, 1, pos);
                session.AddMessage("Скелет побежден — выпали стрелы и кость");
                break;
            case HostileType.Spider:
                world.SpawnPickup(GameData.StringItem.Id, _random.Next(1, 3), pos);
                session.AddMessage("Паук побежден — выпала нить");
                break;
        }
    }

    private void Explode(GameWorld world, GameSession session) {
        var center = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
        session.AddMessage("КРИПЕР ВЗОРВАЛСЯ!");

        // Урон игроку в зависимости от расстояния
        float dist = Vector3.Distance(playerPos(session), Position);
        if (dist < 5.5f) {
            float dmg = (1.0f - dist / 5.5f) * 24f;
            session.Player.Health = MathF.Max(0f, session.Player.Health - dmg);
            session.AddMessage($"Взрыв нанёс урон -{dmg:F0} HP!");
            SoundSystem.PlayHit();
        }

        // Уничтожение блоков в радиусе 3 с дропом ~30%
        for (int dx = -3; dx <= 3; dx++) {
            for (int dy = -3; dy <= 3; dy++) {
                for (int dz = -3; dz <= 3; dz++) {
                    if (dx * dx + dy * dy + dz * dz <= 9) {
                        var target = center + new Vec3i(dx, dy, dz);
                        var vox = world.GetVoxel(target);
                        if (vox.TypeId != 0 && vox.TypeId != GameData.BBedrock.Id && vox.TypeId != GameData.BObsidian.Id) {
                            var b = GameData.GetBlock(vox.TypeId);
                            world.RemoveBlock(target);
                            if (b.DropItemId != 0 && _random.NextDouble() < 0.30) {
                                world.SpawnPickup(b.DropItemId, 1, target);
                            }
                        }
                    }
                }
            }
        }
    }

    private static Vector3 playerPos(GameSession session) => session.Player.Position;
}

/// <summary>
/// Летящая стрела: баллистическая траектория, столкновение с блоками и игроком.
/// </summary>
public sealed class ArrowProjectile {
    public Vector3 Position;
    public Vector3 Velocity;
    public bool Alive = true;
    public float LifeTime = 6.0f;

    public ArrowProjectile(Vector3 position, Vector3 velocity) {
        Position = position;
        Velocity = velocity;
    }

    public void Tick(float dt, GameWorld world, Player player, GameSession session) {
        if (!Alive) return;
        LifeTime -= dt;
        if (LifeTime <= 0f) { Alive = false; return; }

        Velocity.Y -= 12.0f * dt;
        var nextPos = Position + Velocity * dt;

        // Попадание в игрока
        var toPlayer = (player.Position + new Vector3(0f, Player.EyeHeight * 0.5f, 0f)) - Position;
        if (toPlayer.Length() < 0.85f) {
            Alive = false;
            player.Health = MathF.Max(0f, player.Health - 3f);
            session.AddMessage("В вас попала стрела скелета! -3 HP");
            SoundSystem.PlayHit();
            return;
        }

        // Попадание в твёрдый блок
        int bx = (int)MathF.Floor(nextPos.X);
        int by = (int)MathF.Floor(nextPos.Y);
        int bz = (int)MathF.Floor(nextPos.Z);
        if (world.IsSolidAt(new Vec3i(bx, by, bz))) {
            Alive = false;
            return;
        }

        Position = nextPos;
    }
}
