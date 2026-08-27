using System;
using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

public enum HostileType { Zombie, Creeper, Skeleton, Spider, ZombiePigman, Blaze, Enderman }

/// <summary>
/// Враждебные мобы: Zombie, Creeper, Skeleton, Spider, ZombiePigman, Blaze, Enderman.
/// - Спавн в темноте ночью, в пещерах, в Нижнем и в Энде.
/// - Аутентичный дроп (Зомби -> гнилая плоть, Крипер -> порох, Скелет -> стрелы/кости, Паук -> нить, Свинозомби -> золото/плоть, Ифрит -> стержни ифрита, Эндэрмен -> жемчуг Эндера).
/// - Зомби и скелеты горят на солнце; Свинозомби и Ифриты неуязвимы к огню/лаве; Эндэрмен повреждается водой.
/// </summary>
public sealed class HostileMob {
    public const float HalfSize = 0.45f;

    public HostileType Type;
    public Vector3 Position;
    public Vector3 Velocity;
    public float Health = 20f;
    public bool Alive = true;
    public bool IsAngry = false;
    public float FuseTimer;   // для Крипера (1.5с отсчет до взрыва)
    public float TeleportCooldown; // для Эндэрмена
    public float AttackCooldown;
    public float HurtTime;
    public Vector2 WanderDir;
    private readonly Random _random = new();

    public float HalfSizeX => GetHalfSize(Type).X;
    public float HalfSizeY => GetHalfSize(Type).Y;
    public float HalfSizeZ => GetHalfSize(Type).Z;

    /// <summary>Габариты хитбокса по типу. Эндэрмен — высокий и тонкий.</summary>
    public static Vector3 GetHalfSize(HostileType type) => type switch {
        HostileType.Spider => new Vector3(0.65f, 0.35f, 0.65f),
        HostileType.Enderman => new Vector3(0.35f, 1.15f, 0.35f),
        _ => new Vector3(0.4f, 0.85f, 0.4f)
    };

    public HostileMob(HostileType type, Vector3 position) {
        Type = type;
        Position = position;
        Health = type switch {
            HostileType.Spider => 16f,
            HostileType.Enderman => 40f, // Каноничные 40 HP
            HostileType.Skeleton => 20f,
            HostileType.Creeper => 20f,
            HostileType.ZombiePigman => 20f, // Каноничные 20 HP
            HostileType.Blaze => 20f,
            _ => 20f
        };
    }

    public void TakeDamage(float damage, GameWorld world, GameSession session) {
        if (!Alive) return;
        Health -= damage;
        HurtTime = 0.4f;
        SoundSystem.PlayHit();
        if (Type == HostileType.ZombiePigman) {
            IsAngry = true;
            foreach (var other in world.HostileMobs) {
                if (other.Alive && other.Type == HostileType.ZombiePigman && Vector3.Distance(other.Position, Position) < 32f) {
                    other.IsAngry = true;
                }
            }
        }
        if (Type == HostileType.Enderman) {
            // Эндэрмен агрегед на удар и телепортируется, раздражая соседей
            IsAngry = true;
            TryTeleport(world);
            foreach (var other in world.HostileMobs) {
                if (other.Alive && other.Type == HostileType.Enderman && Vector3.Distance(other.Position, Position) < 16f) {
                    other.IsAngry = true;
                }
            }
        }
        if (Health <= 0f) {
            Die(world, session);
        }
    }

    public void Tick(float dt, GameWorld world, Player player, GameSession session) {
        if (!Alive) return;
        if (Position.Y < FallingBlock.VoidY) { Alive = false; return; }

        HurtTime -= dt;
        AttackCooldown -= dt;

        // Зомби и Скелеты горят при ярком солнечном свете днём (если над ними нет блоков и они не в воде)
        var feetPos = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y - HalfSizeY + 0.1f), (int)MathF.Floor(Position.Z));
        var feetVox = world.GetVoxel(feetPos);
        if (feetVox.TypeId == GameData.BLava.Id && Type != HostileType.ZombiePigman && Type != HostileType.Blaze) {
            Health -= 8f * dt;
            HurtTime = 0.3f;
            if (Health <= 0f) {
                Die(world, session);
                return;
            }
        }
        // Эндэрмен повреждается водой и НЕМЕДЛЕННО телепортируется на сухое место
        if (feetVox.TypeId == GameData.BWater.Id && Type == HostileType.Enderman) {
            Health -= 3f * dt;
            HurtTime = 0.3f;
            if (TeleportCooldown <= 0f) {
                TeleportCooldown = 0.3f;
                TeleportToDrySpot(world);   // на сушу — чтобы не ходить по воде
            }
            if (Health <= 0f) {
                Die(world, session);
                return;
            }
        }

        byte sun = world.GetSunLight(feetPos);
        float sky = session.DayNight.SkyFactor;
        if ((Type == HostileType.Zombie || Type == HostileType.Skeleton) && sun >= 12 && sky > 0.70f) {
            // Проверка отсутствия блоков над головой
            int px = feetPos.X, py = feetPos.Y + 2, pz = feetPos.Z;
            bool hasCeiling = false;
            for (int y = py; y < py + 16; y++) {
                if (world.IsSolidAt(new Vec3i(px, y, pz))) { hasCeiling = true; break; }
            }
            if (!hasCeiling && feetVox.TypeId != GameData.BWater.Id) {
                Health -= 4f * dt;
                HurtTime = 0.3f;
                if (Health <= 0f) {
                    Die(world, session);
                    return;
                }
            }
        }

        TeleportCooldown -= dt;

        // Эндэрмен: агрог при взгляде игрока на его глаза (конус зрения, до 16м)
        if (Type == HostileType.Enderman && !IsAngry && session.GameMode != GameMode.Creative) {
            var mobEye = Position + new Vector3(0f, HalfSizeY * 0.85f, 0f);
            var toMob = mobEye - player.Eye;
            float lookDist = toMob.Length();
            if (lookDist > 0.01f && lookDist < 16f) {
                var dirToMob = toMob / lookDist;
                if (Vector3.Dot(player.Forward, dirToMob) > 0.92f && HasLineOfSight(world, player.Eye, mobEye)) {
                    IsAngry = true;
                    SoundSystem.PlayHit();
                }
            }
        }

        var toPlayer = player.Position - Position;
        float dist = toPlayer.Length();
        if (dist > 64f) { Alive = false; return; } // Деспавн

        Vector3 moveDir = Vector3.Zero;
        float speed = 2.0f;
        bool canTarget = session.GameMode != GameMode.Creative;
        if (Type == HostileType.ZombiePigman && !IsAngry) {
            canTarget = false;
        }
        if (Type == HostileType.Enderman && !IsAngry) {
            canTarget = false;
        }

        if (canTarget && dist < 20f) { // Агр на игрока в режиме Выживания
            var dir = Vector3.Normalize(new Vector3(toPlayer.X, 0f, toPlayer.Z));
            moveDir = dir;

            if (Type == HostileType.Spider) {
                speed = 3.4f;
                // Паук лазает по стенам
                var aheadWall = new Vec3i((int)MathF.Floor(Position.X + dir.X * 0.7f), (int)MathF.Floor(Position.Y + 0.5f), (int)MathF.Floor(Position.Z + dir.Z * 0.7f));
                if (world.IsSolidAt(aheadWall) && !GameData.IsDoor(world.GetVoxel(aheadWall).TypeId)) {
                    Velocity.Y = 4.5f;
                }
            } else if (Type == HostileType.Creeper) {
                speed = 2.4f;
                var mobEye = Position + new Vector3(0f, 0.65f, 0f);
                var playerCenter = player.Position + new Vector3(0f, 0.45f, 0f);
                var playerEye = player.Eye;
                bool canSeePlayer = HasLineOfSight(world, mobEye, playerCenter) && HasLineOfSight(world, mobEye, playerEye);
                // Начинает шипеть и взводиться на расстоянии до 3.8 блоков при прямой видимости
                if (dist < 3.8f && canSeePlayer) {
                    if (FuseTimer <= 0f) {
                        SoundSystem.PlayCreeperHiss();
                    }
                    speed = 0.35f; // Замедляется при раздувании
                    FuseTimer += dt;
                    if (FuseTimer >= 1.5f) {
                        Explode(world, session);
                        Alive = false;
                        return;
                    }
                } else if (dist > 5.0f || !canSeePlayer) {
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
            } else if (Type == HostileType.ZombiePigman) {
                speed = 2.8f; // Быстрый свинозомби
            } else if (Type == HostileType.Enderman) {
                speed = 3.315f; // эндэрмены быстрее (5.2 → 3.9 → 3.315, −25% и ещё −15%)
                // Телепорт к игроку (за спину), когда тот далеко — иначе эндэрмен бегает кругами и не догоняет
                if (dist > 5f && TeleportCooldown <= 0f) {
                    TeleportCooldown = 2.5f + (float)_random.NextDouble() * 2f;
                    TeleportNearPlayer(world, player);
                } else {
                    // Эндэрмен НЕ ходит по воде: если впереди вода — поворачиваем в сторону
                    var aheadCell = new Vec3i(
                        (int)MathF.Floor(Position.X + moveDir.X * 1.3f),
                        (int)MathF.Floor(Position.Y + 1f),
                        (int)MathF.Floor(Position.Z + moveDir.Z * 1.3f));
                    if (world.GetVoxel(aheadCell).TypeId == GameData.BWater.Id) {
                        moveDir = Vector3.Normalize(new Vector3(-moveDir.Z, 0f, moveDir.X));
                    }
                }
            } else if (Type == HostileType.Blaze) {
                speed = 1.5f;
                Velocity.Y = MathF.Sin((float)session.TotalPlaySeconds * 3.0f) * 1.2f;
            } else {
                speed = 2.3f;
            }

            // Умный прыжок и обход препятствий (для наземных мобов)
            if (Type != HostileType.Spider && Type != HostileType.Blaze) {
                int aheadX = (int)MathF.Floor(Position.X + moveDir.X * (HalfSizeX + 0.35f));
                int aheadZ = (int)MathF.Floor(Position.Z + moveDir.Z * (HalfSizeZ + 0.35f));
                var aheadFoot = new Vec3i(aheadX, feetPos.Y, aheadZ);
                var aheadHead = new Vec3i(aheadX, feetPos.Y + 1, aheadZ);
                var currentHead = feetPos + new Vec3i(0, 1, 0);

                // Дверь — не препятствие для мобов: идём сквозь неё (коллизия ignoreDoors).
                bool isDoorAhead = GameData.IsDoor(world.GetVoxel(aheadFoot).TypeId) ||
                                   GameData.IsDoor(world.GetVoxel(aheadHead).TypeId);

                if (world.IsSolidAt(aheadFoot) && !isDoorAhead) {
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
            }

            // Атака Зомби, Свинозомби и Паука в ближнем бою
            if ((Type == HostileType.Zombie || Type == HostileType.Spider || Type == HostileType.ZombiePigman) && dist < 1.8f && AttackCooldown <= 0f) {
                var mobCenter = Position + new Vector3(0f, 0.35f, 0f);
                var playerCenter = player.Position + new Vector3(0f, 0.60f, 0f);
                if (HasLineOfSight(world, mobCenter, playerCenter) || HasLineOfSight(world, mobCenter, player.Eye)) {
                    AttackCooldown = 1.0f;
                    float dmg = Type == HostileType.ZombiePigman ? 5f : Type == HostileType.Spider ? 3f : 4f;
                    player.ApplyDamage(dmg, session, Position);
                }
            }

            // Атака Эндэрмена: высокий разряд (7 HP)
            if (Type == HostileType.Enderman && dist < 1.8f && AttackCooldown <= 0f) {
                var mobCenter = Position + new Vector3(0f, 0.6f, 0f);
                var playerCenter = player.Position + new Vector3(0f, 0.60f, 0f);
                if (HasLineOfSight(world, mobCenter, playerCenter) || HasLineOfSight(world, mobCenter, player.Eye)) {
                    AttackCooldown = 1.2f;
                    player.ApplyDamage(4.76f, session, Position); // урон эндермена −20%, затем ещё −15%
                }
            }

            // Атака Скелета
            if (Type == HostileType.Skeleton && dist < 20f && dist > 1.8f && AttackCooldown <= 0f) {
                var eyePos = Position + new Vector3(0f, 0.70f, 0f);
                var targetPos = player.Position + new Vector3(0f, Player.EyeHeight * 0.6f, 0f);
                if (HasLineOfSight(world, eyePos, targetPos)) {
                    AttackCooldown = 2.0f;
                    var toTarget = targetPos - eyePos;
                    float toDist = toTarget.Length();
                    var arrowDir = Vector3.Normalize(toTarget);
                    var arrowVel = arrowDir * 18f + new Vector3(0f, MathF.Min(2.5f, toDist * 0.12f), 0f);
                    world.Arrows.Add(new ArrowProjectile(eyePos + arrowDir * 0.7f, arrowVel, this));
                    SoundSystem.PlayBowShoot();
                }
            }

            // Атака Ифрита (Blaze): огненные шары
            if (Type == HostileType.Blaze && dist < 22f && AttackCooldown <= 0f) {
                var eyePos = Position + new Vector3(0f, 0.65f, 0f);
                var targetPos = player.Position + new Vector3(0f, Player.EyeHeight * 0.5f, 0f);
                if (HasLineOfSight(world, eyePos, targetPos)) {
                    AttackCooldown = 2.5f;
                    var toTarget = targetPos - eyePos;
                    var shotDir = Vector3.Normalize(toTarget);
                    var shotVel = shotDir * 20f;
                    world.Arrows.Add(new ArrowProjectile(eyePos + shotDir * 0.6f, shotVel, this) { Damage = 5f, IsFire = true });
                    SoundSystem.PlayBowShoot();
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

        bool isSpiderClimbing = false;
        if (Type == HostileType.Spider) {
            float probeDist = HalfSizeX + 0.25f;
            for (int dx = -1; dx <= 1; dx++) {
                for (int dz = -1; dz <= 1; dz++) {
                    if (dx == 0 && dz == 0) continue;
                    var probePos = Position + new Vector3(dx * probeDist, 0f, dz * probeDist);
                    for (int dy = -1; dy <= 2; dy++) {
                        var cell = new Vec3i((int)MathF.Floor(probePos.X), (int)MathF.Floor(Position.Y + dy * 0.5f), (int)MathF.Floor(probePos.Z));
                        if (world.IsSolidAt(cell)) {
                            isSpiderClimbing = true;
                            break;
                        }
                    }
                    if (isSpiderClimbing) break;
                }
                if (isSpiderClimbing) break;
            }
        }

        Velocity.X = moveDir.X * speed;
        Velocity.Z = moveDir.Z * speed;

        // В какой жидкости находится моб (ноги)
        var mobFeetCell = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
        var mobFeetVox = world.GetVoxel(mobFeetCell);
        bool mobInWater = mobFeetVox.TypeId == GameData.BWater.Id;
        bool mobInLava = mobFeetVox.TypeId == GameData.BLava.Id;
        // Все мобы плавают в воде; свинозомби (уроженец Нижнего мира) — и в лаве
        bool mobFloats = mobInWater || (mobInLava && Type == HostileType.ZombiePigman);

        if (isSpiderClimbing) {
            Velocity.Y = 3.6f; // Лазание вверх по стене
        } else if (Type == HostileType.Blaze) {
            // Левитация ифрита
        } else if (mobFloats) {
            // Плавание: плавно тянет вверх к поверхности, не даёт утонуть
            Velocity.Y += (4.5f - Velocity.Y) * MathF.Min(1f, dt * 3f);
        } else {
            Velocity.Y -= 22f * dt;
        }

        bool grounded = Collision.Move(world, ref Position, new Vector3(HalfSizeX, HalfSizeY, HalfSizeZ), ref Velocity, dt, ignoreDoors: true);
        if (grounded && Velocity.Y < 0f) Velocity.Y = 0f;
    }

    public static bool HasLineOfSight(GameWorld world, Vector3 from, Vector3 to) {
        var delta = to - from;
        float dist = delta.Length();
        if (dist < 0.2f) return true;
        float stepLen = 0.15f;
        var step = delta / dist * stepLen;
        int steps = (int)(dist / stepLen);
        var cur = from;
        for (int i = 0; i < steps; i++) {
            cur += step;
            var cell = new Vec3i((int)MathF.Floor(cur.X), (int)MathF.Floor(cur.Y), (int)MathF.Floor(cur.Z));
            if (world.IsSolidAt(cell)) return false;
            var v = world.GetVoxel(cell);
            // Открытая дверь не загораживает обзор — мобы видят сквозь неё.
            if (v.TypeId != 0 && (GameData.GetBlock(v.TypeId).IsOpaque ||
                (GameData.IsDoor(v.TypeId) && (v.SubGridLayerMask & 8) == 0))) return false;
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
            case HostileType.ZombiePigman:
                world.SpawnPickup(GameData.RottenFleshItem.Id, _random.Next(1, 3), pos);
                if (_random.NextDouble() < 0.025) world.SpawnPickup(GameData.GoldIngotItem.Id, 1, pos); // Каноничный редкий дроп 2.5%
                break;
            case HostileType.Blaze:
                world.SpawnPickup(GameData.BlazeRodItem.Id, _random.Next(1, 3), pos);
                break;
            case HostileType.Enderman:
                world.SpawnPickup(GameData.EnderPearlItem.Id, _random.Next(1, 3), pos);
                break;
        }
    }

    /// <summary>
    /// Эндэрмен исчезает и появляется в соседней безопасной точке (пол + голова свободны).
    /// Возвращает true, если удалось телепортироваться.
    /// </summary>
    public bool TryTeleport(GameWorld world) {
        var half = GetHalfSize(Type);
        for (int attempt = 0; attempt < 12; attempt++) {
            float angle = (float)_random.NextDouble() * MathF.Tau;
            float r = 6f + (float)_random.NextDouble() * 14f;
            float dx = MathF.Cos(angle) * r;
            float dz = MathF.Sin(angle) * r;
            int tx = (int)MathF.Floor(Position.X + dx);
            int tz = (int)MathF.Floor(Position.Z + dz);
            int baseY = (int)MathF.Floor(Position.Y);

            // Ищем твёрдый пол в небольшом вертикальном диапазоне вокруг текущей высоты
            for (int dy = 0; dy <= 5; dy++) {
                int by = baseY + dy;
                var floor = new Vec3i(tx, by, tz);
                var foot = new Vec3i(tx, by + 1, tz);
                var head = new Vec3i(tx, by + 2, tz);
                if (!world.IsSolidAt(floor)) continue;
                if (world.IsSolidAt(foot) || world.IsSolidAt(head)) continue;
                var pos = new Vector3(tx + 0.5f, by + 1.0f + half.Y, tz + 0.5f);
                if (!Collision.IntersectsSolid(world, pos - half, pos + half, ignoreDoors: true)) {
                    Position = pos;
                    Velocity = Vector3.Zero;
                    return true;
                }
            }
            // И чуть ниже
            for (int dy = -1; dy >= -5; dy--) {
                int by = baseY + dy;
                var floor = new Vec3i(tx, by, tz);
                var foot = new Vec3i(tx, by + 1, tz);
                var head = new Vec3i(tx, by + 2, tz);
                if (!world.IsSolidAt(floor)) continue;
                if (world.IsSolidAt(foot) || world.IsSolidAt(head)) continue;
                var pos = new Vector3(tx + 0.5f, by + 1.0f + half.Y, tz + 0.5f);
                if (!Collision.IntersectsSolid(world, pos - half, pos + half, ignoreDoors: true)) {
                    Position = pos;
                    Velocity = Vector3.Zero;
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Телепорт эндэрмена на СУХОЕ место рядом (не в воду/лаву) — он не должен ходить по воде.</summary>
    private void TeleportToDrySpot(GameWorld world) {
        var half = GetHalfSize(Type);
        for (int attempt = 0; attempt < 24; attempt++) {
            float angle = (float)_random.NextDouble() * MathF.Tau;
            float r = 4f + (float)_random.NextDouble() * 16f;
            int tx = (int)MathF.Floor(Position.X + MathF.Cos(angle) * r);
            int tz = (int)MathF.Floor(Position.Z + MathF.Sin(angle) * r);
            int baseY = (int)MathF.Floor(Position.Y);
            for (int dy = -5; dy <= 5; dy++) {
                int by = baseY + dy;
                var floor = new Vec3i(tx, by, tz);
                var foot = new Vec3i(tx, by + 1, tz);
                var head = new Vec3i(tx, by + 2, tz);
                if (!world.IsSolidAt(floor)) continue;
                if (world.IsSolidAt(foot) || world.IsSolidAt(head)) continue;
                // Ноги должны быть на суше
                ushort footT = world.GetVoxel(foot).TypeId;
                if (footT == GameData.BWater.Id || footT == GameData.BLava.Id) continue;
                var pos = new Vector3(tx + 0.5f, by + 1.0f + half.Y, tz + 0.5f);
                if (!Collision.IntersectsSolid(world, pos - half, pos + half, ignoreDoors: true)) {
                    Position = pos;
                    Velocity = Vector3.Zero;
                    return;
                }
            }
        }
    }

    /// <summary>Телепорт эндэрмена ВБЛИЗИ игрока (2.5–5 блоков), чтобы он догонял и атаковал, а не бегал кругами.</summary>
    private void TeleportNearPlayer(GameWorld world, Player player) {
        var half = GetHalfSize(Type);
        for (int attempt = 0; attempt < 16; attempt++) {
            float angle = (float)_random.NextDouble() * MathF.Tau;
            float r = 2.5f + (float)_random.NextDouble() * 2.5f;
            float dx = MathF.Cos(angle) * r;
            float dz = MathF.Sin(angle) * r;
            int tx = (int)MathF.Floor(player.Position.X + dx);
            int tz = (int)MathF.Floor(player.Position.Z + dz);
            int baseY = (int)MathF.Floor(player.Position.Y);
            for (int dy = -3; dy <= 3; dy++) {
                int by = baseY + dy;
                var floor = new Vec3i(tx, by, tz);
                var foot = new Vec3i(tx, by + 1, tz);
                var head = new Vec3i(tx, by + 2, tz);
                if (!world.IsSolidAt(floor)) continue;
                if (world.IsSolidAt(foot) || world.IsSolidAt(head)) continue;
                ushort footT = world.GetVoxel(foot).TypeId;
                if (footT == GameData.BWater.Id || footT == GameData.BLava.Id) continue; // не в воду
                var pos = new Vector3(tx + 0.5f, by + 1.0f + half.Y, tz + 0.5f);
                if (!Collision.IntersectsSolid(world, pos - half, pos + half, ignoreDoors: true)) {
                    Position = pos;
                    Velocity = Vector3.Zero;
                    return;
                }
            }
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
            session.Player.ApplyDamage(dmg, session, Position);
            session.AddMessage($"Взрыв нанёс урон -{dmg:F0} HP!");
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
/// Летящая стрела / огненный шар: баллистическая траектория, столкновение с блоками, игроком и мобами.
/// </summary>
public sealed class ArrowProjectile {
    public Vector3 Position;
    public Vector3 Velocity;
    public bool Alive = true;
    public float LifeTime = 6.0f;
    public HostileMob? Shooter;
    public bool FromPlayer;
    public bool IsFire;
    public bool IsEnderPearl;
    public bool IsEyeOfEnder;
    public bool IsSlimeSpit;
    public float Damage = 4f;

    public ArrowProjectile(Vector3 position, Vector3 velocity, HostileMob? shooter = null) {
        Position = position;
        Velocity = velocity;
        Shooter = shooter;
    }

    public void Tick(float dt, GameWorld world, Player player, GameSession session) {
        if (!Alive) return;
        LifeTime -= dt;
        if (LifeTime <= 0f) { Alive = false; return; }

        if (!IsFire) {
            Velocity.Y -= 12.0f * dt;
        }
        var nextPos = Position + Velocity * dt;

        // Жемчуг Эндера и Око Эндера не наносят урона сущностям — отдельный блок-коллендер.
        if (IsEnderPearl || IsEyeOfEnder) {
            int pbx = (int)MathF.Floor(nextPos.X);
            int pby = (int)MathF.Floor(nextPos.Y);
            int pbz = (int)MathF.Floor(nextPos.Z);
            if (world.IsSolidAt(new Vec3i(pbx, pby, pbz))) {
                if (IsEnderPearl) {
                    player.TeleportTo(nextPos, world);
                    player.ApplyDamage(3f, session, nextPos);
                    session.AddMessage("Жемчуг Эндера телепортировал вас! -3 HP");
                } else {
                    // Око Эндера, коснувшись земли/блока, возвращается пикапом (остаётся в инвентаре)
                    world.SpawnPickup(GameData.EyeOfEnderItem.Id, 1, new Vec3i(pbx, pby, pbz));
                }
                Alive = false;
            }
            Position = nextPos;
            return;
        }

        if (FromPlayer) {
            // Стрела игрока поражает враждебных мобов
            foreach (var mob in world.HostileMobs) {
                if (!mob.Alive) continue;
                var toMob = (mob.Position + new Vector3(0f, 0.5f, 0f)) - Position;
                if (toMob.Length() < 0.85f) {
                    Alive = false;
                    mob.TakeDamage(Damage, world, session);
                    SoundSystem.PlayArrowHit();
                    return;
                }
            }
            // Стрела игрока поражает мирных животных
            foreach (var ent in world.Animals) {
                if (!ent.Alive) continue;
                var toEnt = (ent.Position + new Vector3(0f, 0.45f, 0f)) - Position;
                if (toEnt.Length() < 0.85f) {
                    Alive = false;
                    ent.TakeDamage(Damage, world);
                    SoundSystem.PlayArrowHit();
                    return;
                }
            }
            // Стрела игрока поражает Слизня Края
            if (world.EndBoss is { Alive: true } boss) {
                var bossCent = boss.Position;
                var toBoss = bossCent - Position;
                if (MathF.Abs(toBoss.X) < EndSlime.HalfSizeXZ + 0.4f &&
                    MathF.Abs(toBoss.Y) < EndSlime.HalfSizeY + 0.4f &&
                    MathF.Abs(toBoss.Z) < EndSlime.HalfSizeXZ + 0.4f) {
                    Alive = false;
                    boss.TakeDamage(Damage, world, session);
                    SoundSystem.PlayArrowHit();
                    return;
                }
            }
        } else {
            // Снаряд моба поражает игрока
            var toPlayer = (player.Position + new Vector3(0f, Player.EyeHeight * 0.5f, 0f)) - Position;
            if (toPlayer.Length() < 0.85f) {
                Alive = false;
                player.ApplyDamage(Damage, session, Position);
                if (IsFire) {
                    player.FireTicks = MathF.Max(player.FireTicks, 5.0f);
                    session.AddMessage($"Огненный шар ифрита обжёг вас! -{Damage:F0} HP");
                } else if (IsSlimeSpit) {
                    session.AddMessage($"Слизень Края плюнул в вас! -{Damage:F0} HP");
                } else {
                    session.AddMessage($"В вас попала стрела! -{Damage:F0} HP");
                }
                return;
            }

            // Попадание в других мобов
            foreach (var mob in world.HostileMobs) {
                if (!mob.Alive || mob == Shooter) continue;
                var toMob = (mob.Position + new Vector3(0f, 0.5f, 0f)) - Position;
                if (toMob.Length() < 0.75f) {
                    Alive = false;
                    mob.TakeDamage(Damage, world, session);
                    SoundSystem.PlayArrowHit();
                    return;
                }
            }
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
