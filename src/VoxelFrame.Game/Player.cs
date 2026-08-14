using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Игрок: движение с коллизиями, камера (yaw/pitch), ломание/установка блоков,
/// еда для лечения, здоровье и регенерация.
/// </summary>
public sealed class Player {
    public const float WalkSpeed = 4.3f;
    public const float SprintSpeed = 6.2f;
    public const float JumpSpeed = 8.0f;
    public const float Gravity = 25f;
    public const float EyeHeight = 0.72f;
    public const float Reach = 6f;
    public const float AttackCooldown = 0.4f;

    public static readonly Vector3 HalfExtents = new(0.3f, 0.9f, 0.3f);

    public float CurrentEyeHeight = EyeHeight;
    public Vector3 Position;
    public Vector3 Velocity;
    public float Yaw;
    public float Pitch;
    public bool OnGround;
    public float Health = 20f;           // макс. 20
    public float MaxHealth = 20f;
    public readonly Container Inventory = new(1000000.0, 1000000.0);
    public int SelectedSlot;
    public float HealthRegenTimer;
    public float BreakProgress;
    public Vec3i BreakTarget = new(int.MinValue, int.MinValue, int.MinValue);
    public float BreakDuration;
    public float SlotToastTimer;
    public string SlotToastText = "";
    public float EatTimer;
    public float AttackTimer;
    public float BobTimer;
    public float BobOffset;
    public float PlaceCooldown;
    public float HighestYInAir;
    public float AirSupply = 10f;
    /// <summary>Урон от застревания в блоках.</summary>
    public float StuckTimer;

    public Vector3 Forward => new(
        MathF.Cos(Pitch) * MathF.Sin(Yaw),
        MathF.Sin(Pitch),
        MathF.Cos(Pitch) * MathF.Cos(Yaw));

    public Vector3 Eye => Position + new Vector3(0f, CurrentEyeHeight, 0f);

    public ItemEntry? SelectedEntry =>
        SelectedSlot >= 0 && SelectedSlot < 9
            ? Inventory.Slots[SelectedSlot]
            : null;

    public ItemDefinition? SelectedItem => SelectedEntry?.Item.Definition;

    public void Update(float dt, in PlayerInput input, GameWorld world, GameSession session) {
        // Взгляд.
        const float sensitivity = 0.0022f;
        Yaw -= input.MouseDX * sensitivity;
        Pitch -= input.MouseDY * sensitivity;
        Pitch = Math.Clamp(Pitch, -1.55f, 1.55f);
        int prevSlot = SelectedSlot;
        if (input.Scroll != 0) {
            SelectedSlot = (SelectedSlot - input.Scroll) % 9;
            if (SelectedSlot < 0) SelectedSlot += 9;
        }
        if (input.HotbarSlot >= 0) SelectedSlot = input.HotbarSlot;
        if (SelectedSlot != prevSlot) {
            // Показываем название предмета пару секунд над хотбаром
            SlotToastTimer = 2f;
            SlotToastText = SelectedItem?.Name ?? "";
        }
        SlotToastTimer = MathF.Max(0f, SlotToastTimer - dt);

        // Движение (ходьба, приседание, плавание).
        float targetEyeHeight = EyeHeight;
        float speed = WalkSpeed;
        if (input.Crouch) {
            targetEyeHeight = 0.35f;
            speed = WalkSpeed * 0.35f;
        }
        CurrentEyeHeight += (targetEyeHeight - CurrentEyeHeight) * MathF.Min(1f, dt * 15f);

        var feetBlock = world.GetVoxel(new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z)));
        var eyeVoxel = world.GetVoxel(new Vec3i((int)MathF.Floor(Eye.X), (int)MathF.Floor(Eye.Y), (int)MathF.Floor(Eye.Z)));
        bool inWater = feetBlock.TypeId == GameData.BWater.Id || eyeVoxel.TypeId == GameData.BWater.Id;

        var forwardH = new Vector3(Forward.X, 0f, Forward.Z);
        if (forwardH.LengthSquared() > 0.001f) forwardH = Vector3.Normalize(forwardH);
        var right = Vector3.Cross(forwardH, Vector3.UnitY);
        var wish = right * input.MoveX + forwardH * input.MoveZ;
        if (wish.LengthSquared() > 1f) wish = Vector3.Normalize(wish);

        if (inWater) {
            speed *= 0.65f;
            Velocity.X = wish.X * speed;
            Velocity.Z = wish.Z * speed;
            Velocity.Y -= 6f * dt; // уменьшенная гравитация в воде
            if (input.Jump) {
                Velocity.Y = 4.0f; // всплытие
            }
            Velocity.X *= MathF.Exp(-2.5f * dt);
            Velocity.Z *= MathF.Exp(-2.5f * dt);
            HighestYInAir = Position.Y; // вода гасит урон от падения
            OnGround = Collision.Move(world, ref Position, HalfExtents, ref Velocity, dt, false);
        } else {
            Velocity.X = wish.X * speed;
            Velocity.Z = wish.Z * speed;
            if (input.Jump && OnGround) Velocity.Y = JumpSpeed;
            Velocity.Y -= Gravity * dt;
            OnGround = Collision.Move(world, ref Position, HalfExtents, ref Velocity, dt, input.Crouch && OnGround);
        }

        if (OnGround && Velocity.Y < 0f) Velocity.Y = 0f;

        // Звуки шагов при ходьбе по земле
        if (OnGround && (Velocity.X != 0f || Velocity.Z != 0f) && !input.Crouch) {
            if (BobTimer % MathF.PI < 0.2f) {
                SoundSystem.PlayStep();
            }
        }

        // Урон от падения
        if (!OnGround && !inWater) {
            if (Position.Y > HighestYInAir) HighestYInAir = Position.Y;
        } else if (OnGround) {
            float fallDist = HighestYInAir - Position.Y;
            if (fallDist > 3.5f) {
                float fallDmg = MathF.Floor((fallDist - 3f) * 2f);
                Health = MathF.Max(0f, Health - fallDmg);
                session.AddMessage($"Урон от падения: -{fallDmg} HP");
                SoundSystem.PlayHit();
            }
            HighestYInAir = Position.Y;
        }

        // Проверка удушья под водой
        if (eyeVoxel.TypeId == GameData.BWater.Id) {
            AirSupply -= dt;
            if (AirSupply <= 0f) {
                AirSupply = 0f;
                Health = MathF.Max(0f, Health - dt * 3f);
                session.AddMessage("Вы тонете!");
                SoundSystem.PlayHit();
            }
        } else {
            AirSupply = 10f;
        }

        // Урон и мягкое выталкивание при застревании в блоках
        var feet = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
        if (world.IsSolidAt(feet)) {
            StuckTimer += dt;
            if (StuckTimer >= 0.5f) {
                StuckTimer = 0f;
                Health = MathF.Max(0f, Health - 1f);
                session.AddMessage("Вы застряли в блоках!");
                SoundSystem.PlayHit();
                Position.Y += 0.25f;
                Velocity.Y = 0f;
            }
        } else {
            StuckTimer = 0f;
        }

        // Head bobbing logic.
        if (OnGround && (input.MoveX != 0f || input.MoveZ != 0f) && !input.Crouch) {
            float bobSpeed = input.Sprint ? SprintSpeed * 2.2f : WalkSpeed * 2.2f;
            BobTimer += dt * bobSpeed;
            float bobAmp = input.Sprint ? 0.08f : 0.04f;
            BobOffset = MathF.Sin(BobTimer) * bobAmp;
        } else {
            BobTimer = 0f;
            BobOffset += (0f - BobOffset) * MathF.Min(1f, dt * 8f);
        }

        // Луч прицеливания
        var eye = Eye + new Vector3(0f, BobOffset, 0f);
        bool hasTarget = world.RaycastBlock(eye, Forward, Reach, out var hit, out var placeCell, out _);
        session.HasTarget = hasTarget;
        session.TargetBlock = hit;
        session.PlaceCell = placeCell;

        // Ломание / атака.
        AttackTimer -= dt;
        if (input.AttackHeld) {
            Animal? targetedAnimal = null;
            HostileMob? targetedHostile = null;
            float bestDist = float.MaxValue;

            foreach (var a in world.Animals) {
                if (!a.Alive) continue;
                var min = a.Position - new Vector3(Animal.HalfSize, Animal.HalfSize, Animal.HalfSize);
                var max = a.Position + new Vector3(Animal.HalfSize, Animal.HalfSize, Animal.HalfSize);
                if (RayAabb(Eye, Forward, min, max, out float t) && t < bestDist && t <= 3.5f) {
                    var hitPoint = Eye + Forward * MathF.Max(0.1f, t - 0.05f);
                    if (HostileMob.HasLineOfSight(world, Eye, hitPoint)) {
                        bestDist = t;
                        targetedAnimal = a;
                        targetedHostile = null;
                    }
                }
            }

            foreach (var m in world.HostileMobs) {
                if (!m.Alive) continue;
                float halfX = m.Type == HostileType.Spider ? 0.65f : 0.45f;
                float halfY = m.Type == HostileType.Spider ? 0.35f : 0.85f;
                float halfZ = m.Type == HostileType.Spider ? 0.65f : 0.45f;
                var min = m.Position - new Vector3(halfX, halfY, halfZ);
                var max = m.Position + new Vector3(halfX, halfY, halfZ);
                if (RayAabb(Eye, Forward, min, max, out float t) && t < bestDist && t <= 3.5f) {
                    var hitPoint = Eye + Forward * MathF.Max(0.1f, t - 0.05f);
                    if (HostileMob.HasLineOfSight(world, Eye, hitPoint)) {
                        bestDist = t;
                        targetedHostile = m;
                        targetedAnimal = null;
                    }
                }
            }

            if ((targetedAnimal != null || targetedHostile != null) && bestDist <= 3.5f) {
                BreakTarget = new Vec3i(int.MinValue, int.MinValue, int.MinValue);
                BreakProgress = 0f;
                BreakDuration = 0f;
                if (targetedAnimal != null) AttackAnimal(world, session);
                else if (targetedHostile != null) AttackHostile(targetedHostile, world, session);
            } else if (hasTarget && GameData.GetBlock(world.GetVoxel(hit).TypeId) is { IsUnbreakable: false } targetBlock) {
                if (hit != BreakTarget) { BreakTarget = hit; BreakProgress = 0f; }
                float breakTime = GameData.GetMiningTime(targetBlock, SelectedItem);
                BreakDuration = breakTime;
                BreakProgress += dt;
                if (BreakProgress >= breakTime) {
                    BreakBlock(world, session, hit, targetBlock);
                    BreakProgress = 0f;
                    BreakDuration = 0f;
                    BreakTarget = new Vec3i(int.MinValue, int.MinValue, int.MinValue);
                }
            } else {
                BreakTarget = new Vec3i(int.MinValue, int.MinValue, int.MinValue);
                BreakProgress = 0f;
                BreakDuration = 0f;
            }
        } else {
            BreakTarget = new Vec3i(int.MinValue, int.MinValue, int.MinValue);
            BreakProgress = 0f;
            BreakDuration = 0f;
        }

        if (input.Drop) {
            var entry = SelectedEntry;
            if (entry != null) {
                Inventory.RemoveAt(SelectedSlot);
                if (entry.Value.Quantity > 1) {
                    Inventory.InsertAt(SelectedSlot, entry.Value with { Quantity = entry.Value.Quantity - 1 });
                }
                var dropPos = Eye + Forward * 0.8f;
                world.Pickups.Add(new ItemPickup(entry.Value.Item.Definition, 1, dropPos));
                if (world.Pickups.Count > 0) {
                    var p = world.Pickups[^1];
                    p.Velocity = Forward * 4.5f + new Vector3(0f, 2.0f, 0f);
                }
                session.AddMessage($"Выброшено: {entry.Value.Item.Definition.Name}");
            }
        }

        // Использование: установка блока или быстрое поедание (Alpha style).
        PlaceCooldown -= dt;
        bool wantUse = input.UsePressed || (input.UseHeld && PlaceCooldown <= 0f);
        if (wantUse) {
            PlaceCooldown = 0.25f;
            // Проверка ПКМ на верстак / печку
            if (input.UsePressed && session.HasTarget) {
                var targetVox = world.GetVoxel(session.TargetBlock);
                if (targetVox.TypeId == GameData.BWorkbench.Id) {
                    session.Ui = UiState.Workbench;
                    wantUse = false;
                } else if (targetVox.TypeId == GameData.BFurnace.Id) {
                    session.ActiveFurnacePos = session.TargetBlock;
                    session.Ui = UiState.Furnace;
                    wantUse = false;
                }
            }
            if (wantUse && SelectedItem is { } item) {
                if (GameData.FoodValue.TryGetValue(item.Id, out float heal)) {
                    if (input.UsePressed && Health < MaxHealth) {
                        if (TryConsumeSelected(item, 1)) {
                            Health = MathF.Min(MaxHealth, Health + heal);
                            session.AddMessage($"Съедено: {item.Name} (+{heal:0} HP)");
                            SoundSystem.PlayEat();
                        }
                    }
                } else if (GameData.TryGetBlockByItem(item.Id, out var block)) {
                    TryPlaceBlock(world, session, placeCell, block!, item);
                }
            }
        }

        TickVitals(dt, session);
    }

    private void TickVitals(float dt, GameSession session) {
        if (Health <= 0f) session.RespawnPlayer();
    }

    // ── Ломание блоков ───────────────────────────────────────────────────────

    private static readonly Random DropRng = new();

    public void BreakBlock(GameWorld world, GameSession session, Vec3i pos, BlockType block) {
        world.RemoveBlock(pos);
        SoundSystem.PlayDig();

        // Проверка песка/гравия выше — падение от гравитации!
        var abovePos = pos + new Vec3i(0, 1, 0);
        var aboveVoxel = world.GetVoxel(abovePos);
        if (aboveVoxel.TypeId == GameData.BSand.Id || aboveVoxel.TypeId == GameData.BGravel.Id) {
            var aboveBlock = GameData.GetBlock(aboveVoxel.TypeId);
            world.RemoveBlock(abovePos);
            world.FallingBlocks.Add(new FallingBlock(aboveBlock, new Vector3(abovePos.X + 0.5f, abovePos.Y + 0.5f, abovePos.Z + 0.5f)));
        }

        // Износ прочности инструмента
        var entry = SelectedEntry;
        ushort toolId = entry?.Item.Definition.Id ?? 0;
        if (entry != null && GameData.GetToolTier(toolId) > 0) {
            var inst = entry.Value.Item;
            int maxDur = GameData.GetMaxToolDurability(inst.Definition.Id);
            inst.Condition -= 1.0 / maxDur;
            if (inst.Condition <= 0) {
                Inventory.RemoveAt(SelectedSlot);
                session.AddMessage($"Инструмент {inst.Definition.Name} сломался!");
            }
        }

        // Проверяем, может ли текущий инструмент добыть этот блок
        bool canHarvest = GameData.CanHarvestBlock(block, toolId);

        if (canHarvest) {
            int dropCount = block.DropItemCount;
            if (block.DropItemId != 0 && GameData.Items.TryGetValue(block.DropItemId, out var drop)) {
                if (!Inventory.TryInsert(GameData.NewItem(drop), dropCount)) {
                    world.SpawnPickup(drop.Id, dropCount, pos);
                    session.AddMessage("Инвентарь полон — предмет упал на землю");
                }
            } else if (block.Id == GameData.BLeaves.Id) {
                double roll = DropRng.NextDouble();
                ItemDefinition? leafDrop = roll < 0.12 ? GameData.AppleItem : roll < 0.30 ? GameData.StickItem : null;
                if (leafDrop != null) {
                    if (!Inventory.TryInsert(GameData.NewItem(leafDrop), 1)) {
                        world.SpawnPickup(leafDrop.Id, 1, pos);
                    } else {
                        session.AddMessage($"Выпало с листвы: {leafDrop.Name}");
                    }
                }
            }
        }
    }

    // ── Установка блоков ─────────────────────────────────────────────────────

    private bool TryConsumeSelected(ItemDefinition item, int qty = 1) {
        var entry = Inventory.Slots[SelectedSlot];
        if (entry != null && entry.Value.Item.Definition == item && qty > 0) {
            int currentQty = entry.Value.Quantity;
            if (currentQty >= qty) {
                if (currentQty == qty) {
                    Inventory.RemoveAt(SelectedSlot);
                } else {
                    Inventory.InsertAt(SelectedSlot, entry.Value with { Quantity = currentQty - qty });
                }
                return true;
            }
        }
        return false;
    }

    public bool TryPlaceBlock(GameWorld world, GameSession session, Vec3i cell, BlockType block, ItemDefinition item) {
        var existing = world.GetVoxel(cell);
        if (existing.TypeId != 0) {
            var eb = GameData.GetBlock(existing.TypeId);
            if (eb.IsSolid || eb.IsOpaque) return false;
        }
        var center = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        var min = center - new Vector3(0.25f, 0.25f, 0.25f);
        var max = center + new Vector3(0.25f, 0.25f, 0.25f);
        var pmin = Position - HalfExtents;
        var pmax = Position + HalfExtents;
        if (min.X < pmax.X && max.X > pmin.X && min.Y < pmax.Y && max.Y > pmin.Y && min.Z < pmax.Z && max.Z > pmin.Z)
            return false;

        int need = block.PlaceItemCount;
        if (TryConsumeSelected(item, need)) {
            world.PlacePlacedBlock(cell, block, block.PlaceContentVolumeM3);
            SoundSystem.PlayPlace();
            return true;
        }
        return false;
    }

    // ── Бой ──────────────────────────────────────────────────────────────────

    public void AttackAnimal(GameWorld world, GameSession session) {
        if (AttackTimer > 0f) return;
        var origin = Eye;
        var dir = Forward;
        Animal? best = null;
        float bestDist = float.MaxValue;
        foreach (var a in world.Animals) {
            if (!a.Alive) continue;
            var min = a.Position - new Vector3(a.HalfSizeX, a.HalfSizeY, a.HalfSizeZ);
            var max = a.Position + new Vector3(a.HalfSizeX, a.HalfSizeY, a.HalfSizeZ);
            if (RayAabb(origin, dir, min, max, out float t) && t < bestDist) {
                bestDist = t;
                best = a;
            }
        }
        if (best == null || bestDist > 3.5f) return;
        AttackTimer = AttackCooldown;
        best.Health -= GameData.GetWeaponDamage(SelectedItem?.Id ?? 0);
        best.HurtTime = 0.5f;
        best.FleeTimer = 2.0f;
        var push = best.Position - Position;
        if (push.LengthSquared() > 0.001f) {
            var pushH = Vector2.Normalize(new Vector2(push.X, push.Z));
            best.Velocity += new Vector3(pushH.X * 5.0f, 3.5f, pushH.Y * 5.0f);
            best.WanderDir = pushH;
        } else {
            best.Velocity += new Vector3(0f, 3.5f, 0f);
        }
        SoundSystem.PlayHit();
        if (best.Health <= 0f) {
            best.Die(world, session);
        }
    }

    public void AttackHostile(HostileMob mob, GameWorld world, GameSession session) {
        if (AttackTimer > 0f) return;
        AttackTimer = AttackCooldown;
        mob.Health -= GameData.GetWeaponDamage(SelectedItem?.Id ?? 0);
        mob.HurtTime = 0.4f;
        var push = mob.Position - Position;
        if (push.LengthSquared() > 0.001f) {
            var pushH = Vector2.Normalize(new Vector2(push.X, push.Z));
            mob.Velocity += new Vector3(pushH.X * 6.0f, 3.0f, pushH.Y * 6.0f);
        }
        SoundSystem.PlayHit();
        if (mob.Health <= 0f) {
            mob.Die(world, session);
        }
    }

    /// <summary>Пересечение луча с AABB (метод слэбов).</summary>
    public static bool RayAabb(Vector3 o, Vector3 d, Vector3 min, Vector3 max, out float t) {
        t = 0f;
        float tmin = 0f, tmax = float.MaxValue;
        for (int axis = 0; axis < 3; axis++) {
            float od = axis == 0 ? d.X : axis == 1 ? d.Y : d.Z;
            float oo = axis == 0 ? o.X : axis == 1 ? o.Y : o.Z;
            float mn = axis == 0 ? min.X : axis == 1 ? min.Y : min.Z;
            float mx = axis == 0 ? max.X : axis == 1 ? max.Y : max.Z;
            if (MathF.Abs(od) < 1e-9f) {
                if (oo < mn || oo > mx) return false;
                continue;
            }
            float inv = 1f / od;
            float t1 = (mn - oo) * inv, t2 = (mx - oo) * inv;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = MathF.Max(tmin, t1);
            tmax = MathF.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        t = tmin;
        return true;
    }
}
