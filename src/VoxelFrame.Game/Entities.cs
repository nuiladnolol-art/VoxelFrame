using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>ААBB-коллизия с твёрдыми блоками: движение по осям по отдельности.</summary>
public static class Collision {
    public static bool IntersectsSolid(GameWorld world, Vector3 min, Vector3 max) {
        int x0 = (int)MathF.Floor(min.X), x1 = (int)MathF.Floor(max.X);
        int y0 = (int)MathF.Floor(min.Y), y1 = (int)MathF.Floor(max.Y);
        int z0 = (int)MathF.Floor(min.Z), z1 = (int)MathF.Floor(max.Z);
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                for (int z = z0; z <= z1; z++)
                    if (world.IsSolidAt(new Vec3i(x, y, z))) return true;
        return false;
    }

    /// <summary>Сдвигает тело с учётом коллизий; возвращает признак «стоит на земле».</summary>
    public static bool Move(GameWorld world, ref Vector3 pos, Vector3 half, ref Vector3 vel, float dt, bool sneaking = false) {
        bool onGround = false;

        // Автоматическое выталкивание вверх при застревании внутри твердого блока
        if (IntersectsSolid(world, pos - half, pos + half)) {
            for (int step = 0; step < 24; step++) {
                pos.Y += 0.05f;
                if (!IntersectsSolid(world, pos - half, pos + half)) {
                    vel.Y = 0f;
                    break;
                }
            }
        }

        // Если крадемся (Shift) — не даем упасть с края блока
        if (sneaking) {
            float testX = pos.X + vel.X * dt;
            if (!IntersectsSolid(world, new Vector3(testX, pos.Y - 0.1f, pos.Z) - half, new Vector3(testX, pos.Y - 0.1f, pos.Z) + half)) {
                vel.X = 0f;
            }
            float testZ = pos.Z + vel.Z * dt;
            if (!IntersectsSolid(world, new Vector3(pos.X, pos.Y - 0.1f, testZ) - half, new Vector3(pos.X, pos.Y - 0.1f, testZ) + half)) {
                vel.Z = 0f;
            }
        }

        pos.X += vel.X * dt;
        if (IntersectsSolid(world, pos - half, pos + half)) {
            pos.X -= vel.X * dt;
            vel.X = 0f;
        }
        pos.Z += vel.Z * dt;
        if (IntersectsSolid(world, pos - half, pos + half)) {
            pos.Z -= vel.Z * dt;
            vel.Z = 0f;
        }
        pos.Y += vel.Y * dt;
        if (IntersectsSolid(world, pos - half, pos + half)) {
            if (vel.Y < 0f) onGround = true;
            pos.Y -= vel.Y * dt;
            vel.Y = 0f;
        }
        return onGround;
    }
}

/// <summary>Предмет в мире. Не исчезает — масса сохраняется.</summary>
public sealed class ItemPickup {
    public ItemDefinition Definition;
    public int Quantity;
    public Vector3 Position;
    public float BobPhase;
    public Vector3 Velocity;
    public float PickupDelay = 0.3f;

    public ItemPickup(ItemDefinition definition, int quantity, Vector3 position) {
        Definition = definition;
        Quantity = quantity;
        Position = position;
        BobPhase = (float)(position.X * 13.7 + position.Z * 7.3);
        
        var rng = new Random();
        float vx = ((float)rng.NextDouble() - 0.5f) * 1.5f;
        float vy = ((float)rng.NextDouble() * 1.5f) + 1.0f;
        float vz = ((float)rng.NextDouble() - 0.5f) * 1.5f;
        Velocity = new Vector3(vx, vy, vz);
    }

    /// <summary>Притяжение к игроку и сбор в инвентарь (с задержкой подбора).</summary>
    public void Tick(float dt, GameWorld world, Player player) {
        var cell = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
        var vox = world.GetVoxel(cell);
        if (vox.TypeId == GameData.BLava.Id || world.Fire.Burning.ContainsKey(cell)) {
            Quantity = 0; // Предмет сгорает в лаве или огне!
            return;
        }

        if (PickupDelay > 0f) PickupDelay -= dt;

        var to = player.Position - Position;
        float dist = to.Length();
        bool isAlive = player.Health > 0f;
        bool canFitAny = isAlive && player.Inventory.HasSpaceFor(Definition, 1);
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
                if (player.Inventory.TryInsert(GameData.NewItem(Definition), fit)) {
                    Quantity -= fit;
                    SoundSystem.PlayPop();
                    break;
                }
                fit--;
            }
        }
    }
}

public enum AnimalType { Pig, Cow, Sheep }

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

    private static readonly Random _random = new();

    public const float HalfSize = 0.45f;
    public float HalfSizeX => 0.45f;
    public float HalfSizeY => Type switch {
        AnimalType.Cow => 0.65f,
        AnimalType.Sheep => 0.55f,
        _ => 0.45f // Pig
    };
    public float HalfSizeZ => 0.45f;

    public Animal() {
        Health = 10f;
    }

    public Animal(AnimalType type, Vector3 position) {
        Type = type;
        Position = position;
        Health = type switch {
            AnimalType.Sheep => 8f,
            AnimalType.Cow => 10f,
            _ => 10f
        };
    }

    public void Die(GameWorld world, GameSession session) {
        Alive = false;
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
                world.SpawnPickup(GameData.WhiteWoolItem.Id, _random.Next(1, 4), pos);
                break;
        }
    }

    public void Tick(float dt, GameWorld world, Player? player = null) {
        if (!Alive) return;
        if (Position.Y < FallingBlock.VoidY) { Alive = false; return; }
        
        HurtTime -= dt;
        FleeTimer -= dt;

        var feetPos = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y - HalfSizeY + 0.1f), (int)MathF.Floor(Position.Z));
        if (world.GetVoxel(feetPos).TypeId == GameData.BLava.Id) {
            Health -= 8f * dt;
            HurtTime = 0.3f;
            if (Health <= 0f) { Alive = false; return; }
        }

        // Привлечение животного едой в руках игрока (яблоки, хлеб)
        if (player != null && FleeTimer <= 0f) {
            ushort heldId = player.SelectedEntry?.Item.Definition.Id ?? 0;
            bool isFood = heldId == GameData.AppleItem.Id || heldId == GameData.BreadItem.Id;
            float dist = Vector3.Distance(Position, player.Position);
            if (isFood && dist < 8.5f && dist > 1.5f) {
                var toP = player.Position - Position;
                WanderDir = Vector2.Normalize(new Vector2(toP.X, toP.Z));
                WanderTimer = 0.8f;
            }
        }

        WanderTimer -= dt;
        if (WanderTimer <= 0f && FleeTimer <= 0f) {
            WanderTimer = 2.5f + (float)_random.NextDouble() * 4.5f;
            if (_random.NextDouble() < 0.25) {
                WanderDir = Vector2.Zero;
            } else {
                float angle = (float)_random.NextDouble() * MathF.Tau;
                WanderDir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }
        }
        
        float speed = FleeTimer > 0f ? 2.8f : 1.1f;
        Velocity.X = WanderDir.X * speed;
        Velocity.Z = WanderDir.Y * speed;
        // Проверка прыжка на 1 блок вверх при препятствии
        if (WanderDir != Vector2.Zero) {
            int aheadX = (int)MathF.Floor(Position.X + WanderDir.X * (HalfSizeX + 0.35f));
            int aheadZ = (int)MathF.Floor(Position.Z + WanderDir.Y * (HalfSizeZ + 0.35f));
            var aheadFoot = new Vec3i(aheadX, feetPos.Y, aheadZ);
            var aheadHead = new Vec3i(aheadX, feetPos.Y + 1, aheadZ);
            var currentHead = feetPos + new Vec3i(0, 1, 0);

            if (world.IsSolidAt(aheadFoot) && !world.IsSolidAt(aheadHead) && !world.IsSolidAt(currentHead) && MathF.Abs(Velocity.Y) < 0.1f) {
                Velocity.Y = 7.5f;
            } else if (world.IsSolidAt(aheadFoot) && world.IsSolidAt(aheadHead)) {
                // Стена: повернуть в другую сторону
                float angle = (float)_random.NextDouble() * MathF.Tau;
                WanderDir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
            }
        }

        Velocity.Y -= 22f * dt;

        bool grounded = Collision.Move(world, ref Position, new Vector3(HalfSizeX, HalfSizeY, HalfSizeZ), ref Velocity, dt);
        if (grounded && Velocity.Y < 0f) {
            Velocity.Y = 0f;
        }
    }
}
