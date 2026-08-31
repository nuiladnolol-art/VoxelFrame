using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>ААBB-коллизия с твёрдыми блоками: движение по осям по отдельности.</summary>
public static class Collision {
    public static bool IntersectsSolid(GameWorld world, Vector3 min, Vector3 max, bool ignoreDoors = false) {
        const float eps = 0.001f;
        int x0 = (int)MathF.Floor(min.X + eps), x1 = (int)MathF.Floor(max.X - eps);
        int y0 = (int)MathF.Floor(min.Y + eps), y1 = (int)MathF.Floor(max.Y - eps);
        int z0 = (int)MathF.Floor(min.Z + eps), z1 = (int)MathF.Floor(max.Z - eps);
        for (int x = x0; x <= x1; x++) {
            for (int y = y0; y <= y1; y++) {
                for (int z = z0; z <= z1; z++) {
                    var cell = new Vec3i(x, y, z);
                    var v = world.GetVoxel(cell);
                    if (v.TypeId == 0) continue;
                    if ((v.Flags & VoxelFlags.Solid) == 0) continue;

                    if (GameData.IsDoor(v.TypeId)) {
                        if (ignoreDoors) continue;
                        // Открытая дверь проходима для всех, закрытая — преграда
                        bool isOpen = (v.SubGridLayerMask & 8) != 0;
                        if (isOpen) continue;
                    }

                    // Получаем точные границы AABB блока
                    GetBlockAabb(v, x, y, z, out float bx0, out float by0, out float bz0, out float bx1, out float by1, out float bz1);

                    // Проверяем реальное пересечение AABB сущности [min, max] и AABB блока [b0, b1]
                    if (max.X > bx0 + eps && min.X < bx1 - eps &&
                        max.Y > by0 + eps && min.Y < by1 - eps &&
                        max.Z > bz0 + eps && min.Z < bz1 - eps) {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private static void GetBlockAabb(in VoxelData v, int x, int y, int z, out float bx0, out float by0, out float bz0, out float bx1, out float by1, out float bz1) {
        bx0 = x; by0 = y; bz0 = z;
        bx1 = x + 1f; by1 = y + 1f; bz1 = z + 1f;
        ushort typeId = v.TypeId;

        if (typeId == GameData.BBed.Id || typeId == GameData.BBedHead.Id) {
            by1 = y + 0.5625f; // 9/16 блока
        } else if (typeId == GameData.BFarmland.Id) {
            by1 = y + 0.9375f; // 15/16 блока
        } else if (typeId == GameData.BChest.Id) {
            bx0 = x + 0.0625f; bx1 = x + 0.9375f;
            by1 = y + 0.875f;  // 14/16 блока
            bz0 = z + 0.0625f; bz1 = z + 0.9375f;
        } else if (GameData.IsDoor(typeId)) {
            // Тонкая дверная панель 3/16 — совпадает с визуалом в ChunkMesher
            byte facing = (byte)(v.SubGridLayerMask & 3);
            const float T = 0.1875f;
            switch (facing) {
                case 0: bz0 = z + (1f - T); bz1 = z + 1f; break;
                case 1: bx0 = x; bx1 = x + T; break;
                case 2: bz0 = z; bz1 = z + T; break;
                default: bx0 = x + (1f - T); bx1 = x + 1f; break;
            }
        }
    }

    /// <summary>Сдвигает тело с учётом коллизий; возвращает признак «стоит на земле».</summary>
    public static bool Move(GameWorld world, ref Vector3 pos, Vector3 half, ref Vector3 vel, float dt, bool sneaking = false, float stepHeight = 0.56f, bool ignoreDoors = false) {
        bool onGround = false;

        // 1. Если крадемся (Shift) на краю блока
        if (sneaking && vel.Y <= 0.01f) {
            float testX = pos.X + vel.X * dt;
            float testZ = pos.Z + vel.Z * dt;
            bool groundX = IntersectsSolid(world, new Vector3(testX, pos.Y - 0.1f, pos.Z) - half, new Vector3(testX, pos.Y - 0.1f, pos.Z) + half, ignoreDoors);
            if (!groundX) vel.X = 0f;
            bool groundZ = IntersectsSolid(world, new Vector3(pos.X, pos.Y - 0.1f, testZ) - half, new Vector3(pos.X, pos.Y - 0.1f, testZ) + half, ignoreDoors);
            if (!groundZ) vel.Z = 0f;
        }

        // 2. Движение по X (с аккуратным подъемом на полублоки)
        if (vel.X != 0f) {
            pos.X += vel.X * dt;
            if (IntersectsSolid(world, pos - half, pos + half, ignoreDoors)) {
                bool stepped = false;
                if (stepHeight > 0f && vel.Y <= 0.05f) {
                    for (float dy = 0.1f; dy <= stepHeight; dy += 0.1f) {
                        var steppedPos = new Vector3(pos.X, pos.Y + dy, pos.Z);
                        if (!IntersectsSolid(world, steppedPos - half, steppedPos + half, ignoreDoors)) {
                            if (IntersectsSolid(world, new Vector3(steppedPos.X, steppedPos.Y - 0.15f, steppedPos.Z) - half, new Vector3(steppedPos.X, steppedPos.Y - 0.01f, steppedPos.Z) + half, ignoreDoors)) {
                                pos.Y += dy;
                                stepped = true;
                                break;
                            }
                        }
                    }
                }
                if (!stepped) {
                    pos.X -= vel.X * dt;
                    vel.X = 0f;
                }
            }
        }

        // 3. Движение по Z (с аккуратным подъемом на полублоки)
        if (vel.Z != 0f) {
            pos.Z += vel.Z * dt;
            if (IntersectsSolid(world, pos - half, pos + half, ignoreDoors)) {
                bool stepped = false;
                if (stepHeight > 0f && vel.Y <= 0.05f) {
                    for (float dy = 0.1f; dy <= stepHeight; dy += 0.1f) {
                        var steppedPos = new Vector3(pos.X, pos.Y + dy, pos.Z);
                        if (!IntersectsSolid(world, steppedPos - half, steppedPos + half, ignoreDoors)) {
                            if (IntersectsSolid(world, new Vector3(steppedPos.X, steppedPos.Y - 0.15f, steppedPos.Z) - half, new Vector3(steppedPos.X, steppedPos.Y - 0.01f, steppedPos.Z) + half, ignoreDoors)) {
                                pos.Y += dy;
                                stepped = true;
                                break;
                            }
                        }
                    }
                }
                if (!stepped) {
                    pos.Z -= vel.Z * dt;
                    vel.Z = 0f;
                }
            }
        }

        // 4. Движение по Y
        pos.Y += vel.Y * dt;
        if (IntersectsSolid(world, pos - half, pos + half, ignoreDoors)) {
            if (vel.Y < 0f) onGround = true;
            pos.Y -= vel.Y * dt;
            vel.Y = 0f;
        }

        // 5. Проверка касания земли чуть ниже стоп
        if (!onGround && vel.Y <= 0f) {
            if (IntersectsSolid(world, new Vector3(pos.X, pos.Y - 0.05f, pos.Z) - half, new Vector3(pos.X, pos.Y - 0.01f, pos.Z) + half, ignoreDoors)) {
                onGround = true;
            }
        }

        return onGround;
    }
}

/// <summary>Предмет в мире. Не исчезает — масса сохраняется.</summary>
public sealed class ItemPickup {
    public ItemInstance Item;
    public ItemDefinition Definition => Item.Definition;
    public int Quantity;
    public Vector3 Position;
    public float BobPhase;
    public Vector3 Velocity;
    public float PickupDelay = 0.3f;
    public float Age = 0f;

    public ItemPickup(ItemInstance item, int quantity, Vector3 position) {
        Item = item;
        Quantity = quantity;
        Position = position;
        BobPhase = (float)(position.X * 13.7 + position.Z * 7.3);
        
        var rng = new Random();
        float vx = ((float)rng.NextDouble() - 0.5f) * 1.5f;
        float vy = ((float)rng.NextDouble() * 1.5f) + 1.0f;
        float vz = ((float)rng.NextDouble() - 0.5f) * 1.5f;
        Velocity = new Vector3(vx, vy, vz);
    }

    /// <summary>Притяжение к игроку и сбор в инвентарь (с задержкой подбора и деспавном через 5 мин).</summary>
    public void Tick(float dt, GameWorld world, Player player) {
        Age += dt;
        if (Age >= 300f) {
            Quantity = 0; // Испарение / деспавн предметов, пролежавших на земле > 5 минут
            return;
        }

        var cell = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
        var belowCell = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y - 0.25f), (int)MathF.Floor(Position.Z));
        var vox = world.GetVoxel(cell);
        var belowVox = world.GetVoxel(belowCell);
        if (vox.TypeId == GameData.BLava.Id || belowVox.TypeId == GameData.BLava.Id ||
            world.Fire.Burning.ContainsKey(cell) || world.Fire.Burning.ContainsKey(belowCell)) {
            Quantity = 0; // Предмет сгорает в лаве или огне!
            SoundSystem.PlaySplash();
            return;
        }

        if (PickupDelay > 0f) PickupDelay -= dt;

        var to = player.Position - Position;
        float dist = to.Length();
        bool isAlive = player.Health > 0f;
        bool canFitAny = isAlive && player.Inventory.HasSpaceFor(Item.Definition, 1);
        if (PickupDelay <= 0f && canFitAny && dist < 3.2f && dist > 0.001f) {
            Position += to / dist * MathF.Min(5f * dt, dist);
            Velocity = Vector3.Zero;
        } else {
            Velocity.Y -= 20.0f * dt;
            Velocity.X *= MathF.Exp(-2f * dt);
            Velocity.Z *= MathF.Exp(-2f * dt);
            var half = new Vector3(0.15f, 0.15f, 0.15f);
            Collision.Move(world, ref Position, half, ref Velocity, dt);
        }
        if (isAlive && PickupDelay <= 0f && dist < 1.5f) {
            int fit = Quantity;
            while (fit > 0) {
                if (player.Inventory.TryInsert(Item, fit)) {
                    Quantity -= fit;
                    SoundSystem.PlayPop();
                    break;
                }
                fit--;
            }
        }
    }
}

public enum AnimalType { Pig, Cow, Sheep, Chicken }

public sealed class Animal {
    public AnimalType Type;
    public Vector3 Position;
    public Vector3 Velocity;
    public float Health = 10f;
    public bool Alive = true;
    public float HurtTime;
    public float WanderTimer;
    public Vector2 WanderDir;
    public float FleeTimer;

    // Размножение (Breeding), возраст и яйца
    public float LoveTimer;
    public float BreedCooldown;
    public bool IsBaby;
    public float BabyAgeTimer = 180f;
    public float EggLayTimer;

    private static readonly Random _random = new();

    public const float HalfSize = 0.45f;
    public float HalfSizeX => IsBaby ? 0.15f : (Type == AnimalType.Chicken ? 0.22f : 0.45f);
    public float HalfSizeY => (Type switch {
        AnimalType.Cow => 0.65f,
        AnimalType.Sheep => 0.55f,
        AnimalType.Chicken => 0.35f,
        _ => 0.45f // Pig
    }) * (IsBaby ? 0.55f : 1.0f);
    public float HalfSizeZ => IsBaby ? 0.15f : (Type == AnimalType.Chicken ? 0.22f : 0.45f);

    public Animal() {
        Health = 10f;
    }

    public Animal(AnimalType type, Vector3 position) {
        Type = type;
        Position = position;
        Health = type switch {
            AnimalType.Sheep => 8f,
            AnimalType.Cow => 10f,
            AnimalType.Chicken => 4f,
            _ => 10f
        };
        if (type == AnimalType.Chicken) {
            EggLayTimer = 180f + (float)_random.NextDouble() * 300f;
        }
    }

    public bool LikesFood(ushort itemId) => Type switch {
        AnimalType.Cow or AnimalType.Sheep => itemId == GameData.WheatItem.Id,
        AnimalType.Pig => itemId == GameData.CarrotItem.Id || itemId == GameData.PotatoItem.Id || itemId == GameData.AppleItem.Id || itemId == GameData.BreadItem.Id,
        AnimalType.Chicken => itemId == GameData.WheatSeedsItem.Id,
        _ => false
    };

    public void TakeDamage(float damage, GameWorld world, GameSession? session = null) {
        if (!Alive) return;
        Health -= damage;
        HurtTime = 0.4f;
        FleeTimer = 3.0f;
        if (Health <= 0f) {
            Alive = false;
            if (IsBaby) return; // С детенышей нет дропа
            var pos = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
            switch (Type) {
                case AnimalType.Pig:
                    world.SpawnPickup(GameData.RawPorkItem.Id, _random.Next(1, 4), pos);
                    break;
                case AnimalType.Cow:
                    world.SpawnPickup(GameData.RawBeefItem.Id, _random.Next(1, 4), pos);
                    if (_random.NextDouble() < 0.60) {
                        world.SpawnPickup(GameData.LeatherItem.Id, _random.Next(1, 3), pos);
                    }
                    break;
                case AnimalType.Sheep:
                    world.SpawnPickup(GameData.WhiteWoolItem.Id, 1, pos);
                    world.SpawnPickup(GameData.RawMuttonItem.Id, _random.Next(1, 3), pos);
                    break;
                case AnimalType.Chicken:
                    world.SpawnPickup(GameData.RawChickenItem.Id, 1, pos);
                    world.SpawnPickup(GameData.FeatherItem.Id, _random.Next(1, 3), pos);
                    break;
            }
        }
    }

    public void Die(GameWorld world, GameSession session) {
        TakeDamage(999f, world, session);
    }

    public void Tick(float dt, GameWorld world, Player? player = null, GameSession? session = null) {
        if (!Alive) return;
        if (Position.Y < FallingBlock.VoidY) { Alive = false; return; }
        
        HurtTime -= dt;
        FleeTimer -= dt;
        if (LoveTimer > 0f) LoveTimer -= dt;
        if (BreedCooldown > 0f) BreedCooldown -= dt;

        if (IsBaby) {
            BabyAgeTimer -= dt;
            if (BabyAgeTimer <= 0f) {
                IsBaby = false;
            }
        } else if (Type == AnimalType.Chicken) {
            EggLayTimer -= dt;
            if (EggLayTimer <= 0f) {
                EggLayTimer = 180f + (float)_random.NextDouble() * 300f;
                var eggPos = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
                world.SpawnPickup(GameData.EggItem.Id, 1, eggPos);
                SoundSystem.PlayPop();
            }
        }

        var feetPos = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y - HalfSizeY + 0.1f), (int)MathF.Floor(Position.Z));
        if (world.GetVoxel(feetPos).TypeId == GameData.BLava.Id) {
            Health -= 8f * dt;
            HurtTime = 0.3f;
            if (Health <= 0f) { Alive = false; return; }
        }

        // Поиск партнера для спаривания в режиме любви
        bool headingToMate = false;
        if (LoveTimer > 0f && !IsBaby && BreedCooldown <= 0f && FleeTimer <= 0f) {
            Animal? bestMate = null;
            float bestDistSq = 12f * 12f;
            foreach (var other in world.Animals) {
                if (other == this || !other.Alive || other.Type != Type || other.IsBaby || other.LoveTimer <= 0f || other.BreedCooldown > 0f) continue;
                float dsq = Vector3.DistanceSquared(Position, other.Position);
                if (dsq < bestDistSq) {
                    bestDistSq = dsq;
                    bestMate = other;
                }
            }

            if (bestMate != null) {
                headingToMate = true;
                float dist = MathF.Sqrt(bestDistSq);
                var toMate = bestMate.Position - Position;
                WanderDir = Vector2.Normalize(new Vector2(toMate.X, toMate.Z));
                WanderTimer = 0.5f;

                if (dist < 1.4f) {
                    // Рождение детеныша!
                    var babyPos = (Position + bestMate.Position) * 0.5f;
                    var baby = new Animal(Type, babyPos) { IsBaby = true, BabyAgeTimer = 180f };
                    world.Animals.Add(baby);

                    LoveTimer = 0f;
                    bestMate.LoveTimer = 0f;
                    BreedCooldown = 180f;
                    bestMate.BreedCooldown = 180f;

                    SoundSystem.PlayPop();
                    session?.AddMessage("Родилось маленькое животное!");
                }
            }
        }

        // Привлечение животного любимой едой в руках игрока
        if (!headingToMate && player != null && FleeTimer <= 0f) {
            ushort heldId = player.SelectedEntry?.Item.Definition.Id ?? 0;
            bool isFood = LikesFood(heldId);
            float dist = Vector3.Distance(Position, player.Position);
            if (isFood && dist < 10.0f && dist > 1.8f) {
                var toP = player.Position - Position;
                WanderDir = Vector2.Normalize(new Vector2(toP.X, toP.Z));
                WanderTimer = 0.8f;
            }
        }

        WanderTimer -= dt;
        if (WanderTimer <= 0f && FleeTimer <= 0f && !headingToMate) {
            WanderTimer = 2.5f + (float)_random.NextDouble() * 4.5f;
            if (_random.NextDouble() < 0.25) {
                WanderDir = Vector2.Zero;
            } else {
                float angle = (float)_random.NextDouble() * MathF.Tau;
                WanderDir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }
        }
        
        float speed = FleeTimer > 0f ? 2.8f : headingToMate ? 1.4f : 1.1f;
        Velocity.X = WanderDir.X * speed;
        Velocity.Z = WanderDir.Y * speed;
        // Проверка прыжка на 1 блок вверх при препятствии
        if (WanderDir != Vector2.Zero) {
            int aheadX = (int)MathF.Floor(Position.X + WanderDir.X * (HalfSizeX + 0.35f));
            int aheadZ = (int)MathF.Floor(Position.Z + WanderDir.Y * (HalfSizeZ + 0.35f));
            var aheadFoot = new Vec3i(aheadX, feetPos.Y, aheadZ);
            var aheadHead = new Vec3i(aheadX, feetPos.Y + 1, aheadZ);
            var currentHead = feetPos + new Vec3i(0, 1, 0);

            // Дверь — не препятствие для животных: идут сквозь неё.
            bool isDoorAhead = GameData.IsDoor(world.GetVoxel(aheadFoot).TypeId) ||
                               GameData.IsDoor(world.GetVoxel(aheadHead).TypeId);

            if (world.IsSolidAt(aheadFoot) && !isDoorAhead && !world.IsSolidAt(aheadHead) && !world.IsSolidAt(currentHead) && MathF.Abs(Velocity.Y) < 0.1f) {
                Velocity.Y = 7.5f;
            } else if (world.IsSolidAt(aheadFoot) && !isDoorAhead && world.IsSolidAt(aheadHead)) {
                // Стена: повернуть в другую сторону
                float angle = (float)_random.NextDouble() * MathF.Tau;
                WanderDir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }
        }

        Velocity.Y -= (Type == AnimalType.Chicken ? 8f : 22f) * dt;
        if (Type == AnimalType.Chicken && Velocity.Y < -2.2f) {
            Velocity.Y = -2.2f;
        }

        bool grounded = Collision.Move(world, ref Position, new Vector3(HalfSizeX, HalfSizeY, HalfSizeZ), ref Velocity, dt, ignoreDoors: true);
        if (grounded && Velocity.Y < 0f) {
            Velocity.Y = 0f;
        }
    }
}
