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
        var feetPos = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y - HalfSizeY + 0.1f), (int)MathF.Floor(Position.Z));
        var feetVox = world.GetVoxel(feetPos);
        if (feetVox.TypeId == GameData.BLava.Id) {
            Health -= 8f * dt;
            HurtTime = 0.3f;
            if (Health <= 0f) {
                Die(world, session);
                return;
            }
        }

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
        if (dist > 64f) { Alive = false; return; } // Деспавн

        Vector3 moveDir = Vector3.Zero;
        float speed = 2.0f;

        if (dist < 20f) { // Агр на игрока
            var dir = Vector3.Normalize(new Vector3(toPlayer.X, 0f, toPlayer.Z));
            moveDir = dir;

            if (Type == HostileType.Spider) {
                speed = 3.4f;
                // Паук лазает по стенам
                var aheadWall = new Vec3i((int)MathF.Floor(Position.X + dir.X * 0.7f), (int)MathF.Floor(Position.Y + 0.5f), (int)MathF.Floor(Position.Z + dir.Z * 0.7f));
                if (world.IsSolidAt(aheadWall)) {
                    Velocity.Y = 4.5f;
                }
            } else if (Type == HostileType.Creeper) {
                speed = 2.4f;
                var mobCenter = Position + new Vector3(0f, 0.45f, 0f);
                var playerCenter = player.Position + new Vector3(0f, 0.60f, 0f);
                bool canSeePlayer = HasLineOfSight(world, mobCenter, playerCenter) || HasLineOfSight(world, mobCenter, player.Eye);
                // Начинает шипеть и взводиться на расстоянии до 3.8 блоков (не вплотную)
                if (dist < 3.8f && canSeePlayer) {
                    speed = 0.4f; // Замедляется при раздувании
                    FuseTimer += dt;
                    if (FuseTimer >= 1.3f) {
                        Explode(world, session);
                        Alive = false;
                        return;
                    }
                } else if (dist > 5.5f || !canSeePlayer) {
                    FuseTimer = MathF.Max(0f, FuseTimer - dt * 1.5f);
                }
            } else if (Type == HostileType.Skeleton) {
                if (dist < 7.5f) {
                    moveDir = -dir; // Отступает назад при приближении
                    speed = 2.0f;
                } else if (dist <= 14f) {
                    // Тактическое боковое смещение (стрейф)
                    var strafe = Vector3.Cross(dir, Vector3.UnitY);
                    moveDir = Vector3.Normalize(dir * 0.3f + strafe * 0.7f);
                    speed = 1.6f;
                } else {
                    speed = 2.2f;
                }
            } else {
                speed = 2.3f;
            }

            // Умный прыжок и обход препятствий
            int aheadX = (int)MathF.Floor(Position.X + moveDir.X * (HalfSizeX + 0.35f));
            int aheadZ = (int)MathF.Floor(Position.Z + moveDir.Z * (HalfSizeZ + 0.35f));
            var aheadFoot = new Vec3i(aheadX, feetPos.Y, aheadZ);
            var aheadHead = new Vec3i(aheadX, feetPos.Y + 1, aheadZ);
            var currentHead = feetPos + new Vec3i(0, 1, 0);

            if (world.IsSolidAt(aheadFoot)) {
                if (!world.IsSolidAt(aheadHead) && !world.IsSolidAt(currentHead) && MathF.Abs(Velocity.Y) < 0.1f) {
                    Velocity.Y = 8.5f; // Прыжок на 1 блок вверх
                } else if (world.IsSolidAt(aheadHead)) {
                    // Стена впереди: проверяем боковые направления для обхода (+45° / -45°)
                    var leftDir = new Vector3(moveDir.Z, 0f, -moveDir.X);
                    var rightDir = new Vector3(-moveDir.Z, 0f, moveDir.X);
                    var leftCell = new Vec3i((int)MathF.Floor(Position.X + leftDir.X * 0.6f), feetPos.Y, (int)MathF.Floor(Position.Z + leftDir.Z * 0.6f));
                    var rightCell = new Vec3i((int)MathF.Floor(Position.X + rightDir.X * 0.6f), feetPos.Y, (int)MathF.Floor(Position.Z + rightDir.Z * 0.6f));

                    if (!world.IsSolidAt(leftCell)) moveDir = Vector3.Normalize(moveDir + leftDir);
                    else if (!world.IsSolidAt(rightCell)) moveDir = Vector3.Normalize(moveDir + rightDir);
                }
            }

            // Атака Зомби и Паука
            if ((Type == HostileType.Zombie || Type == HostileType.Spider) && dist < 1.8f && AttackCooldown <= 0f) {
                var mobCenter = Position + new Vector3(0f, 0.35f, 0f);
                var playerCenter = player.Position + new Vector3(0f, 0.60f, 0f);
                if (HasLineOfSight(world, mobCenter, playerCenter) || HasLineOfSight(world, mobCenter, player.Eye)) {
                    AttackCooldown = 1.0f;
                    float dmg = Type == HostileType.Spider ? 3f : 4f;
                    player.Health = MathF.Max(0f, player.Health - dmg);
                    SoundSystem.PlayHit();
                }
            }

            // Атака Скелета
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
                }
            }
        } else {
            // Плавное блуждание
            if (_random.NextDouble() < 0.02) {
                if (_random.NextDouble() < 0.30) {
                    WanderDir = Vector2.Zero; // Пауза
                } else {
                    float angle = (float)_random.NextDouble() * MathF.Tau;
                    WanderDir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                }
            }
            moveDir = new Vector3(WanderDir.X, 0f, WanderDir.Y);
            speed = 1.1f;
        }

        Velocity.X = moveDir.X * speed;
        Velocity.Z = moveDir.Z * speed;
        Velocity.Y -= 22f * dt;

        bool grounded = Collision.Move(world, ref Position, new Vector3(HalfSizeX, HalfSizeY, HalfSizeZ), ref Velocity, dt);
        if (grounded && Velocity.Y < 0f) Velocity.Y = 0f;
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
                world.SpawnPickup(GameData.RottenFleshItem.Id, _random.Next(1, 3), pos);
                break;
            case HostileType.Creeper:
                world.SpawnPickup(GameData.GunpowderItem.Id, _random.Next(1, 3), pos);
                break;
            case HostileType.Skeleton:
                world.SpawnPickup(GameData.ArrowItem.Id, _random.Next(1, 3), pos);
                world.SpawnPickup(GameData.BoneItem.Id, 1, pos);
                break;
            case HostileType.Spider:
                world.SpawnPickup(GameData.StringItem.Id, _random.Next(1, 3), pos);
                break;
        }
    }

    private void Explode(GameWorld world, GameSession session) {
        var center = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
        session.AddMessage("КРИПЕР ВЗОРВАЛСЯ!");
        SoundSystem.PlayExplosion();

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
