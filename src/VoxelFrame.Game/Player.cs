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
    public float FireTicks;
    /// <summary>Урон от застревания в блоках.</summary>
    public float StuckTimer;
    public bool IsSprinting { get; private set; }
    public float SprintFovProgress { get; private set; }
    public float ScreenShake { get; set; }

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
        ScreenShake = MathF.Max(0f, ScreenShake - dt * 3.5f);

        // Движение (каноничная ходьба, спринт, приседание, плавание).
        float targetEyeHeight = EyeHeight;
        float speed = WalkSpeed;
        if (input.Crouch) {
            targetEyeHeight = 0.35f;
            speed = WalkSpeed * 0.35f;
            IsSprinting = false;
            SprintFovProgress = MathF.Max(0f, SprintFovProgress - dt * 5f);
        } else if (input.Sprint && input.MoveZ > 0.1f) {
            IsSprinting = true;
            speed = WalkSpeed * 1.35f;
            SprintFovProgress = MathF.Min(1f, SprintFovProgress + dt * 4f);
        } else {
            IsSprinting = false;
            SprintFovProgress = MathF.Max(0f, SprintFovProgress - dt * 5f);
        }
        CurrentEyeHeight += (targetEyeHeight - CurrentEyeHeight) * MathF.Min(1f, dt * 15f);

        var feetBlock = world.GetVoxel(new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z)));
        var eyeVoxel = world.GetVoxel(new Vec3i((int)MathF.Floor(Eye.X), (int)MathF.Floor(Eye.Y), (int)MathF.Floor(Eye.Z)));
        bool inWater = feetBlock.TypeId == GameData.BWater.Id || eyeVoxel.TypeId == GameData.BWater.Id;
        bool inLava = feetBlock.TypeId == GameData.BLava.Id || eyeVoxel.TypeId == GameData.BLava.Id;

        var forwardH = new Vector3(Forward.X, 0f, Forward.Z);
        if (forwardH.LengthSquared() > 0.001f) forwardH = Vector3.Normalize(forwardH);
        var right = Vector3.Cross(forwardH, Vector3.UnitY);
        var wish = right * input.MoveX + forwardH * input.MoveZ;
        if (wish.LengthSquared() > 1f) wish = Vector3.Normalize(wish);

        bool wasOnGround = OnGround;

        if (inWater) {
            FireTicks = 0f; // Вода мгновенно тушит огонь
            speed *= 0.65f;
            Velocity.X = wish.X * speed;
            Velocity.Z = wish.Z * speed;
            Velocity.Y -= 6f * dt; // уменьшенная гравитация в воде
            if (input.Jump) {
                bool nearSurface = eyeVoxel.TypeId != GameData.BWater.Id;
                Velocity.Y = nearSurface ? JumpSpeed * 0.85f : 4.2f;
            }
            Velocity.X *= MathF.Exp(-2.5f * dt);
            Velocity.Z *= MathF.Exp(-2.5f * dt);
            HighestYInAir = Position.Y; // вода гасит урон от падения
            OnGround = Collision.Move(world, ref Position, HalfExtents, ref Velocity, dt, false);
        } else if (inLava) {
            FireTicks = MathF.Max(FireTicks, 8.0f); // Поджигает на 8 секунд
            Health = MathF.Max(0f, Health - dt * 8.0f); // Урон от лавы
            ScreenShake = MathF.Min(0.35f, ScreenShake + dt * 0.4f);
            speed *= 0.35f;
            Velocity.X = wish.X * speed;
            Velocity.Z = wish.Z * speed;
            Velocity.Y -= 4f * dt;
            if (input.Jump) {
                Velocity.Y = 3.2f;
            }
            Velocity.X *= MathF.Exp(-4f * dt);
            Velocity.Z *= MathF.Exp(-4f * dt);
            HighestYInAir = Position.Y;
            OnGround = Collision.Move(world, ref Position, HalfExtents, ref Velocity, dt, false);
        } else {
            Velocity.X = wish.X * speed;
            Velocity.Z = wish.Z * speed;
            if (input.Jump && OnGround) Velocity.Y = JumpSpeed;
            Velocity.Y -= Gravity * dt;
            OnGround = Collision.Move(world, ref Position, HalfExtents, ref Velocity, dt, input.Crouch && OnGround);
        }

        if (OnGround && Velocity.Y < 0f) Velocity.Y = 0f;

        // Пыль при приземлении
        if (!wasOnGround && OnGround && !inWater && !inLava) {
            world.SpawnDust(Position - new Vector3(0f, HalfExtents.Y, 0f), 5);
        }

        // Звуки шагов и пыль при спринте
        if (OnGround && (Velocity.X != 0f || Velocity.Z != 0f) && !input.Crouch) {
            if (BobTimer % MathF.PI < 0.2f) {
                SoundSystem.PlayStep();
                if (IsSprinting) world.SpawnDust(Position - new Vector3(0f, HalfExtents.Y, 0f), 2);
            }
        }

        // Урон от падения
        if (!OnGround && !inWater && !inLava) {
            if (Position.Y > HighestYInAir) HighestYInAir = Position.Y;
        } else if (OnGround) {
            float fallDist = HighestYInAir - Position.Y;
            if (fallDist > 3.5f) {
                float fallDmg = MathF.Floor((fallDist - 3f) * 2f);
                Health = MathF.Max(0f, Health - fallDmg);
                ScreenShake = MathF.Min(0.35f, ScreenShake + 0.18f);
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

        // Head bobbing (классическое покачивание камеры)
        if (OnGround && (input.MoveX != 0f || input.MoveZ != 0f) && !input.Crouch) {
            BobTimer += dt * WalkSpeed * 2.2f;
            BobOffset = MathF.Sin(BobTimer) * 0.04f;
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
                var dropPos = Eye + Forward * 0.5f;
                var pickup = new ItemPickup(entry.Value.Item.Definition, 1, dropPos) {
                    PickupDelay = 1.2f,
                    Velocity = Forward * 5.0f + new Vector3(0f, 2.0f, 0f)
                };
                world.Pickups.Add(pickup);
            }
        }

        // Использование: установка блока или быстрое поедание (Alpha style).
        PlaceCooldown -= dt;
        bool wantUse = input.UsePressed || (input.UseHeld && PlaceCooldown <= 0f);
        if (wantUse) {
            PlaceCooldown = 0.25f;
            // Проверка ПКМ на верстак / печку / сундук / кровать
            if (input.UsePressed && session.HasTarget) {
                var targetVox = world.GetVoxel(session.TargetBlock);
                if (targetVox.TypeId == GameData.BWorkbench.Id) {
                    session.Ui = UiState.Workbench;
                    wantUse = false;
                } else if (targetVox.TypeId == GameData.BFurnace.Id) {
                    session.ActiveFurnacePos = session.TargetBlock;
                    session.Ui = UiState.Furnace;
                    wantUse = false;
                } else if (targetVox.TypeId == GameData.BChest.Id) {
                    session.ActiveChestPos = session.TargetBlock;
                    session.Ui = UiState.Chest;
                    wantUse = false;
                } else if (targetVox.TypeId == GameData.BBed.Id || targetVox.TypeId == GameData.BBedHead.Id) {
                    float tod = session.DayNight.TimeOfDay;
                    bool isNight = tod > 0.75f || tod < 0.22f;

                    bool hostileNearby = false;
                    foreach (var m in world.HostileMobs) {
                        if (m.Alive && Vector3.Distance(m.Position, Position) < 8.0f) {
                            hostileNearby = true;
                            break;
                        }
                    }

                    if (hostileNearby) {
                        session.AddMessage("Вы не можете спать: рядом бродят монстры!");
                    } else if (isNight) {
                        session.StartSleep(session.TargetBlock);
                    } else {
                        session.AddMessage("Вы можете спать только ночью");
                    }
                    wantUse = false;
                }
            }
            if (wantUse && SelectedItem is { } item) {
                if (GameData.FoodValue.TryGetValue(item.Id, out float heal)) {
                    if (input.UsePressed && Health < MaxHealth) {
                        if (TryConsumeSelected(item, 1)) {
                            Health = MathF.Min(MaxHealth, Health + heal);
                            SoundSystem.PlayEat();
                        }
                    }
                } else if (GameData.TryGetBlockByItem(item.Id, out var block)) {
                    var targetVox = world.GetVoxel(session.TargetBlock);
                    Vec3i targetPlace = (targetVox.TypeId == GameData.BWater.Id || targetVox.TypeId == GameData.BLava.Id)
                        ? session.TargetBlock
                        : placeCell;
                    TryPlaceBlock(world, session, targetPlace, block!, item);
                }
            }
        }

        TickVitals(dt, session);
    }

    private void TickVitals(float dt, GameSession session) {
        if (FireTicks > 0f) {
            FireTicks = MathF.Max(0f, FireTicks - dt);
            Health = MathF.Max(0f, Health - dt * 1.5f);
        }
        if (Health <= 0f) session.DiePlayer();
    }

    // ── Ломание блоков ───────────────────────────────────────────────────────

    private static readonly Random DropRng = new();

    public void BreakBlock(GameWorld world, GameSession session, Vec3i pos, BlockType block) {
        world.RemoveBlock(pos);
        SoundSystem.PlayDig();

        // Проверка песка/гравия выше — каскадное падение от гравитации!
        var curAbove = pos + new Vec3i(0, 1, 0);
        while (true) {
            var aboveVoxel = world.GetVoxel(curAbove);
            if (aboveVoxel.TypeId == GameData.BSand.Id || aboveVoxel.TypeId == GameData.BGravel.Id) {
                var aboveBlock = GameData.GetBlock(aboveVoxel.TypeId);
                world.RemoveBlock(curAbove);
                world.FallingBlocks.Add(new FallingBlock(aboveBlock, new Vector3(curAbove.X + 0.5f, curAbove.Y + 0.5f, curAbove.Z + 0.5f)));
                curAbove += new Vec3i(0, 1, 0);
            } else {
                break;
            }
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
                world.SpawnPickup(drop.Id, dropCount, pos);
            } else if (block.Id == GameData.BLeaves.Id) {
                double roll = DropRng.NextDouble();
                ItemDefinition? leafDrop = roll < 0.12 ? GameData.AppleItem : roll < 0.30 ? GameData.StickItem : null;
                if (leafDrop != null) {
                    world.SpawnPickup(leafDrop.Id, 1, pos);
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
            bool isFluid = existing.TypeId == GameData.BWater.Id || existing.TypeId == GameData.BLava.Id;
            if (!isFluid) {
                var eb = GameData.GetBlock(existing.TypeId);
                if (eb.IsSolid || eb.IsOpaque) return false;
            }
        }
        var center = new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f);
        var min = center - new Vector3(0.25f, 0.25f, 0.25f);
        var max = center + new Vector3(0.25f, 0.25f, 0.25f);
        var pmin = Position - HalfExtents;
        var pmax = Position + HalfExtents;
        if (min.X < pmax.X && max.X > pmin.X && min.Y < pmax.Y && max.Y > pmin.Y && min.Z < pmax.Z && max.Z > pmin.Z)
            return false;

        // Вычисляем ориентацию блока (facing: 0..3) по направлению взгляда игрока (передняя сторона печки смотрит на игрока)
        byte facing = 0;
        Vec3i forwardH;
        if (MathF.Abs(Forward.X) > MathF.Abs(Forward.Z)) {
            if (Forward.X > 0) { facing = 1; forwardH = new Vec3i(1, 0, 0); }
            else { facing = 3; forwardH = new Vec3i(-1, 0, 0); }
        } else {
            if (Forward.Z > 0) { facing = 2; forwardH = new Vec3i(0, 0, 1); }
            else { facing = 0; forwardH = new Vec3i(0, 0, -1); }
        }

        // Специальная установка 2-блочной кровати (изножье + изголовье)
        if (block.Id == GameData.BBed.Id) {
            var headCell = cell + forwardH;
            var exHead = world.GetVoxel(headCell);
            if (exHead.TypeId != 0 && (GameData.GetBlock(exHead.TypeId).IsSolid || GameData.GetBlock(exHead.TypeId).IsOpaque))
                return false;
            if (!world.IsSolidAt(cell + new Vec3i(0, -1, 0)) || !world.IsSolidAt(headCell + new Vec3i(0, -1, 0)))
                return false;

            if (TryConsumeSelected(item, 1)) {
                world.PlacePlacedBlock(cell, GameData.BBed, 1f, facing);
                world.PlacePlacedBlock(headCell, GameData.BBedHead, 1f, facing);
                SoundSystem.PlayPlace();
                return true;
            }
            return false;
        }

        // Гравитация песка и гравия при установке в воздухе
        if (block.Id == GameData.BSand.Id || block.Id == GameData.BGravel.Id) {
            var below = cell + new Vec3i(0, -1, 0);
            if (!world.IsSolidAt(below)) {
                if (TryConsumeSelected(item, 1)) {
                    world.FallingBlocks.Add(new FallingBlock(block, new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f)));
                    SoundSystem.PlayPlace();
                    return true;
                }
                return false;
            }
        }

        if (TryConsumeSelected(item, 1)) {
            world.PlacePlacedBlock(cell, block, 1f, facing);
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
        bool isCrit = !OnGround && Velocity.Y < -0.2f;
        float dmg = GameData.GetWeaponDamage(SelectedItem?.Id ?? 0);
        if (isCrit) {
            dmg *= 1.5f;
            session.AddMessage("Критический удар! ×1.5");
            world.SpawnCrit(best.Position + new Vector3(0f, 0.4f, 0f), 12);
        }
        best.Health -= dmg;
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
        bool isCrit = !OnGround && Velocity.Y < -0.2f;
        float dmg = GameData.GetWeaponDamage(SelectedItem?.Id ?? 0);
        if (isCrit) {
            dmg *= 1.5f;
            session.AddMessage("Критический удар! ×1.5");
            world.SpawnCrit(mob.Position + new Vector3(0f, 0.5f, 0f), 14);
        }
        mob.Health -= dmg;
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
