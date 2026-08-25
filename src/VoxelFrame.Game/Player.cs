using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Игрок: движение с коллизиями, камера (yaw/pitch), ломание/установка блоков,
/// еда для лечения, здоровье и регенерация.
/// </summary>
public sealed partial class Player {
    public const float WalkSpeed = 4.3f;
    public const float JumpSpeed = 8.0f;
    public const float Gravity = 25f;
    public const float EyeHeight = 0.72f;
    public const float Reach = 4.5f;
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
    public readonly Container Inventory = new();
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
    public float StepSoundTimer;
    public float PlaceCooldown;
    public float HighestYInAir;
    public float AirSupply = 15f;
    public float FireTicks;
    public float LavaBurnTimer;
    public float FireBurnTimer;
    public float DrownDamageTimer;
    /// <summary>Урон от застревания в блоках.</summary>
    public float StuckTimer;
    public const float MaxHunger = 20f;
    public float Hunger = 20f;
    public float Saturation = 5f;
    public float Exhaustion = 0f;
    public float StarveTimer = 0f;
    public ItemDefinition? OffhandItem;
    public int OffhandCount;
    public ItemEntry? OffhandEntry {
        get => OffhandItem != null && OffhandCount > 0 ? new ItemEntry(GameData.NewItem(OffhandItem), OffhandCount) : null;
        set {
            if (value.HasValue && value.Value.Quantity > 0) {
                OffhandItem = value.Value.Item.Definition;
                OffhandCount = value.Value.Quantity;
            } else {
                OffhandItem = null;
                OffhandCount = 0;
            }
        }
    }
    public float TotemAnimationTimer;
    public bool IsSprinting { get; private set; }
    public float SprintFovProgress { get; private set; }
    public float ScreenShake { get; set; }
    public float AttackRechargeTimer = 1.0f;
    public float HurtTimer { get; set; }
    public float HurtDirection { get; set; } = 1f;
    public float InvulnerabilityTimer { get; set; }
    public float TotemFreezeTimer { get; set; }
    public float BowCharge { get; set; }
    public bool IsBlocking { get; set; }
    public float PortalTimer { get; set; }
    public bool IsFlying { get; set; }
    public float SpaceDoubleTapTimer { get; set; }

    public void ApplyDamage(float amount, GameSession session, Vector3? attackerPos = null) {
        if (session.GameMode == GameMode.Creative) return; // В Творческом режиме игрок бессмертен
        if (amount <= 0f) return;
        if (InvulnerabilityTimer > 0f) return;

        // Блокирование урона щитом
        bool hasShield = (OffhandItem != null && OffhandItem.Id == GameData.ShieldItem.Id) || (SelectedItem != null && SelectedItem.Id == GameData.ShieldItem.Id);
        if (hasShield && IsBlocking && attackerPos.HasValue) {
            var diff = attackerPos.Value - Position;
            var toAttacker = diff.LengthSquared() > 0.001f ? Vector3.Normalize(diff) : Forward;
            float dot = Vector3.Dot(new Vector3(Forward.X, 0f, Forward.Z), new Vector3(toAttacker.X, 0f, toAttacker.Z));
            if (dot > 0.15f) {
                SoundSystem.PlayShieldBlock();
                ScreenShake = MathF.Min(0.2f, ScreenShake + 0.1f);
                session.AddMessage("Удар заблокирован щитом!");
                return;
            }
        }

        // Проверка тотема бессмертия при смертельном уроне (только в основной или второй руке)
        if (Health - amount <= 0f) {
            bool hasTotem = false;
            if (OffhandItem != null && OffhandItem.Id == GameData.TotemItem.Id && OffhandCount > 0) {
                if (OffhandCount <= 1) {
                    OffhandItem = null;
                    OffhandCount = 0;
                } else {
                    OffhandCount--;
                }
                hasTotem = true;
            } else if (SelectedItem != null && SelectedItem.Id == GameData.TotemItem.Id) {
                var entry = SelectedEntry;
                if (entry != null && entry.Value.Quantity > 0) {
                    if (entry.Value.Quantity <= 1) {
                        Inventory.RemoveAt(SelectedSlot);
                    } else {
                        Inventory.InsertAt(SelectedSlot, entry.Value with { Quantity = entry.Value.Quantity - 1 });
                    }
                    hasTotem = true;
                }
            }

            if (hasTotem) {
                Health = 4.0f; // 2 сердца спасения
                FireTicks = 0f;
                Hunger = MathF.Max(Hunger, 12f);
                InvulnerabilityTimer = 2.0f; // 2 секунды неуязвимости (40 тиков)
                TotemFreezeTimer = 0f;       // Без стана игрока
                TotemAnimationTimer = 2.5f;
                ScreenShake = 0.5f;
                HurtTimer = 0.6f;
                SoundSystem.PlayTotem();
                session.AddMessage("Тотем бессмертия спас вашу жизнь!");
                return;
            }
        }

        InvulnerabilityTimer = 0.4f;
        HurtTimer = 0.5f;
        ScreenShake = MathF.Min(0.5f, ScreenShake + 0.25f);
        Health = MathF.Max(0f, Health - amount);

        if (attackerPos.HasValue) {
            var diff = attackerPos.Value - Position;
            var right = Vector3.Cross(Forward, Vector3.UnitY);
            HurtDirection = Vector3.Dot(diff, right) >= 0 ? 1f : -1f;
        } else {
            HurtDirection = (new Random().Next(0, 2) == 0) ? 1f : -1f;
        }

        SoundSystem.PlayPlayerHurt();

        if (Health <= 0f) {
            session.DiePlayer();
        }
    }

    public void SwapMainAndOffhand() {
        if (SelectedSlot < 0 || SelectedSlot >= 9) return;
        var mainSlot = Inventory.Slots[SelectedSlot];
        var oldMainItem = mainSlot?.Item.Definition;
        int oldMainCount = mainSlot?.Quantity ?? 0;

        var oldOffhandItem = OffhandItem;
        int oldOffhandCount = OffhandCount;

        // Очищаем текущий слот хотбара
        if (mainSlot != null && oldMainItem != null) {
            Inventory.RemoveAt(SelectedSlot);
        }

        // Переносим предмет в левую руку
        OffhandItem = oldMainItem;
        OffhandCount = oldMainCount;

        // Переносим бывший предмет из левой руки в выбранный слот хотбара
        if (oldOffhandItem != null && oldOffhandCount > 0) {
            Inventory.InsertAt(SelectedSlot, new ItemEntry(GameData.NewItem(oldOffhandItem), oldOffhandCount));
        }
        SoundSystem.PlayPop();
    }

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
        if (float.IsNaN(Position.X) || float.IsNaN(Position.Y) || float.IsNaN(Position.Z) ||
            float.IsInfinity(Position.X) || float.IsInfinity(Position.Y) || float.IsInfinity(Position.Z)) {
            Position = world.GetSafeRespawnPosition(world.SpawnBlock);
            Velocity = Vector3.Zero;
        }
        if (float.IsNaN(Velocity.X) || float.IsNaN(Velocity.Y) || float.IsNaN(Velocity.Z)) {
            Velocity = Vector3.Zero;
        }
        if (float.IsNaN(Yaw)) Yaw = 0f;
        if (float.IsNaN(Pitch)) Pitch = 0f;

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

        // Быстрая смена предмета между основной и второй рукой по клавише F
        if (Raylib_cs.Raylib.IsKeyPressed(Raylib_cs.KeyboardKey.F)) {
            SwapMainAndOffhand();
        }

        SlotToastTimer = MathF.Max(0f, SlotToastTimer - dt);
        ScreenShake = MathF.Max(0f, ScreenShake - dt * 3.5f);
        if (TotemAnimationTimer > 0f) TotemAnimationTimer = MathF.Max(0f, TotemAnimationTimer - dt);
        
        bool isFrozen = TotemFreezeTimer > 0f;
        if (isFrozen) {
            TotemFreezeTimer = MathF.Max(0f, TotemFreezeTimer - dt);
        }

        float inMoveX = isFrozen ? 0f : input.MoveX;
        float inMoveZ = isFrozen ? 0f : input.MoveZ;
        bool inJump = !isFrozen && input.Jump;
        bool inSprint = !isFrozen && input.Sprint;
        bool inCrouch = !isFrozen && input.Crouch;

        // Двойное нажатие пробела для активации/деактивации полёта в Творческом режиме
        if (input.JumpPressed) {
            if (session.GameMode == GameMode.Creative) {
                if (SpaceDoubleTapTimer > 0f) {
                    IsFlying = !IsFlying;
                    SpaceDoubleTapTimer = 0f;
                    Velocity = Vector3.Zero;
                } else {
                    SpaceDoubleTapTimer = 0.35f;
                }
            }
        }
        if (SpaceDoubleTapTimer > 0f) SpaceDoubleTapTimer -= dt;

        if (session.GameMode == GameMode.Creative) {
            Health = MaxHealth;
            Hunger = 20f;
            AirSupply = 10f;
            FireTicks = 0f;
        }

        // Движение (каноничная ходьба, спринт, приседание, плавание, полёт).
        float targetEyeHeight = EyeHeight;
        float speed = WalkSpeed;
        bool canSprint = Hunger > 6f || session.GameMode == GameMode.Creative;
        bool isDrawingBow = SelectedItem != null && SelectedItem.Id == GameData.BowItem.Id && input.UseHeld;
        if (isDrawingBow) {
            speed = WalkSpeed * 0.25f;
            IsSprinting = false;
            SprintFovProgress = MathF.Max(0f, SprintFovProgress - dt * 5f);
        } else if (inCrouch) {
            targetEyeHeight = 0.35f;
            speed = WalkSpeed * 0.35f;
            IsSprinting = false;
            SprintFovProgress = MathF.Max(0f, SprintFovProgress - dt * 5f);
        } else if (inSprint && canSprint && inMoveZ > 0.1f) {
            IsSprinting = true;
            speed = WalkSpeed * 1.35f;
            SprintFovProgress = MathF.Min(1f, SprintFovProgress + dt * 4f);
            Exhaustion += dt * 0.15f;
        } else {
            IsSprinting = false;
            SprintFovProgress = MathF.Max(0f, SprintFovProgress - dt * 5f);
        }
        CurrentEyeHeight += (targetEyeHeight - CurrentEyeHeight) * MathF.Min(1f, dt * 15f);

        var feetCell = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
        var feetBlock = world.GetVoxel(feetCell);
        var eyeVoxel = world.GetVoxel(new Vec3i((int)MathF.Floor(Eye.X), (int)MathF.Floor(Eye.Y), (int)MathF.Floor(Eye.Z)));
        bool inWeb = feetBlock.TypeId == GameData.BWeb.Id || eyeVoxel.TypeId == GameData.BWeb.Id;
        bool inWater = feetBlock.TypeId == GameData.BWater.Id || eyeVoxel.TypeId == GameData.BWater.Id;
        bool inLava = feetBlock.TypeId == GameData.BLava.Id || eyeVoxel.TypeId == GameData.BLava.Id;
        bool inFire = world.Fire.Burning.ContainsKey(feetCell) || world.Fire.Campfires.Contains(feetCell);

        if (inFire && !inWater && session.GameMode != GameMode.Creative) {
            FireTicks = MathF.Max(FireTicks, 5.0f);
        }

        var forwardH = new Vector3(Forward.X, 0f, Forward.Z);
        if (forwardH.LengthSquared() > 0.001f) forwardH = Vector3.Normalize(forwardH);
        var right = Vector3.Cross(forwardH, Vector3.UnitY);
        var wish = right * inMoveX + forwardH * inMoveZ;
        if (wish.LengthSquared() > 1f) wish = Vector3.Normalize(wish);

        bool wasOnGround = OnGround;

        if (IsFlying && session.GameMode == GameMode.Creative) {
            // Плавный свободный полёт в Креативе с ускорением и демпфированием
            float flySpeed = WalkSpeed * (inSprint ? 3.0f : 1.8f);
            float targetVx = wish.X * flySpeed;
            float targetVz = wish.Z * flySpeed;
            float targetVy = (inJump ? flySpeed * 0.95f : 0f) - (inCrouch ? flySpeed * 0.95f : 0f);

            float flyAccel = 18f;
            Velocity.X += (targetVx - Velocity.X) * MathF.Min(1f, flyAccel * dt);
            Velocity.Z += (targetVz - Velocity.Z) * MathF.Min(1f, flyAccel * dt);
            Velocity.Y += (targetVy - Velocity.Y) * MathF.Min(1f, flyAccel * dt);

            BobTimer = 0f;
            BobOffset = 0f;
            HighestYInAir = Position.Y;
            OnGround = Collision.Move(world, ref Position, HalfExtents, ref Velocity, dt, false);
        } else if (inWater) {
            FireTicks = 0f; // Вода мгновенно тушит огонь
            speed *= 0.65f;
            Velocity.X = wish.X * speed;
            Velocity.Z = wish.Z * speed;
            Velocity.Y -= 6f * dt; // уменьшенная гравитация в воде
            if (inJump) {
                bool nearSurface = eyeVoxel.TypeId != GameData.BWater.Id;
                // Импульс 1.15x позволяет свободно выпрыгивать на берег высотой 1 блок
                Velocity.Y = nearSurface ? JumpSpeed * 1.15f : 4.8f;
            }
            Velocity.X *= MathF.Exp(-2.5f * dt);
            Velocity.Z *= MathF.Exp(-2.5f * dt);
            HighestYInAir = Position.Y; // вода полностью гасит урон от падения
            OnGround = Collision.Move(world, ref Position, HalfExtents, ref Velocity, dt, false);
        } else if (inLava) {
            if (session.GameMode != GameMode.Creative) {
                FireTicks = MathF.Max(FireTicks, 15.0f); // Лава поджигает на 15 секунд
                LavaBurnTimer -= dt;
                if (LavaBurnTimer <= 0f) {
                    LavaBurnTimer = 0.5f; // Урон лавы каждые 0.5с
                    InvulnerabilityTimer = 0f;
                    ApplyDamage(4.0f, session); // 4 HP (2 целых сердца) за тик лавы!
                }
            }

            speed *= 0.55f;
            Velocity.X = wish.X * speed;
            Velocity.Z = wish.Z * speed;
            Velocity.Y -= 5f * dt;
            if (inJump) {
                bool nearSurface = eyeVoxel.TypeId != GameData.BLava.Id;
                // Мощный импульс выпрыгивания из лавы на берег (1.25x JumpSpeed)
                Velocity.Y = nearSurface ? JumpSpeed * 1.25f : 4.8f;
            }
            Velocity.X *= MathF.Exp(-2.5f * dt);
            Velocity.Z *= MathF.Exp(-2.5f * dt);
            HighestYInAir = Position.Y; // лава также полностью гасит урон от падения
            OnGround = Collision.Move(world, ref Position, HalfExtents, ref Velocity, dt, false);
        } else {
            LavaBurnTimer = 0f;
            if (inWeb) {
                speed *= 0.20f;
                HighestYInAir = Position.Y;
            }
            float targetVx = wish.X * speed;
            float targetVz = wish.Z * speed;

            if (OnGround) {
                // Плавное ускорение и торможение на земле (Friction)
                float accel = (wish.LengthSquared() > 0.01f) ? 20f : 26f;
                Velocity.X += (targetVx - Velocity.X) * MathF.Min(1f, accel * dt);
                Velocity.Z += (targetVz - Velocity.Z) * MathF.Min(1f, accel * dt);

                if (inJump) {
                    Velocity.Y = JumpSpeed;
                    if (IsSprinting && wish.LengthSquared() > 0.01f) {
                        // Дополнительный импульс прыжка с разбега (Sprint Jump boost)
                        Velocity.X += wish.X * 1.8f;
                        Velocity.Z += wish.Z * 1.8f;
                    }
                    Exhaustion += IsSprinting ? 0.2f : 0.05f;
                }
            } else {
                // Управление в воздухе (Air control) с сохранением импульса
                float airAccel = 8f;
                Velocity.X += (targetVx - Velocity.X) * MathF.Min(1f, airAccel * dt);
                Velocity.Z += (targetVz - Velocity.Z) * MathF.Min(1f, airAccel * dt);
                Velocity.X *= MathF.Exp(-0.8f * dt); // Сопротивление воздуха
                Velocity.Z *= MathF.Exp(-0.8f * dt);
            }

            Velocity.Y -= Gravity * dt;
            OnGround = Collision.Move(world, ref Position, HalfExtents, ref Velocity, dt, inCrouch && OnGround);
        }

        if (OnGround && Velocity.Y < 0f) Velocity.Y = 0f;

        // Пыль при приземлении
        if (!wasOnGround && OnGround && !inWater && !inLava) {
            world.SpawnDust(Position - new Vector3(0f, HalfExtents.Y, 0f), 5);
        }

        // Звуки шагов по типу материала и пыль при спринте
        if (OnGround && (Velocity.X != 0f || Velocity.Z != 0f) && !input.Crouch) {
            StepSoundTimer -= dt * (IsSprinting ? 1.5f : 1.0f);
            if (StepSoundTimer <= 0f) {
                StepSoundTimer = 0.38f;
                var groundBlock = world.GetVoxel(new Vec3i(feetCell.X, feetCell.Y - 1, feetCell.Z));
                ushort stepBlockId = groundBlock.TypeId != 0 ? groundBlock.TypeId : feetBlock.TypeId;
                SoundSystem.PlayStep(stepBlockId);
                if (IsSprinting) world.SpawnDust(Position - new Vector3(0f, HalfExtents.Y, 0f), 2);
            }
        } else {
            StepSoundTimer = 0.1f;
        }

        // Урон от падения (в воде и лаве урон ВСЕГДА сбрасывается)
        if (inWater || inLava) {
            HighestYInAir = Position.Y;
        } else if (!OnGround) {
            if (Position.Y > HighestYInAir) HighestYInAir = Position.Y;
        } else if (OnGround) {
            float fallDist = HighestYInAir - Position.Y;
            if (fallDist > 3.0f) {
                float fallDmg = MathF.Floor(fallDist - 3f);
                if (fallDmg > 0f) {
                    ApplyDamage(fallDmg, session);
                    session.AddMessage($"Урон от падения: -{fallDmg} HP");
                }
            }
            HighestYInAir = Position.Y;
        }

        // Проверка удушья под водой (дискретный урон 2 HP в секунду)
        if (eyeVoxel.TypeId == GameData.BWater.Id) {
            AirSupply -= dt;
            if (AirSupply <= 0f) {
                AirSupply = 0f;
                DrownDamageTimer -= dt;
                if (DrownDamageTimer <= 0f) {
                    DrownDamageTimer = 1.0f;
                    InvulnerabilityTimer = 0f;
                    ApplyDamage(2.0f, session);
                    session.AddMessage("Вы тонете!");
                }
            }
        } else {
            AirSupply = MathF.Min(15f, AirSupply + dt * 5f);
            DrownDamageTimer = 0f;
        }

        // Урон и мягкое выталкивание при застревании в блоках
        var feet = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z));
        if (world.IsSolidAt(feet)) {
            StuckTimer += dt;
            if (StuckTimer >= 0.5f) {
                StuckTimer = 0f;
                ApplyDamage(1f, session);
                session.AddMessage("Вы застряли в блоках!");
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

        // Луч прицеливания (если в руке пустое ведро — таргетируем воду и лаву!)
        var eye = Eye + new Vector3(0f, BobOffset, 0f);
        bool holdingBucket = SelectedItem != null && SelectedItem.Id == GameData.BucketItem.Id;
        float blockReach = session.GameMode == GameMode.Creative ? 5.0f : Reach;
        bool hasTarget = world.RaycastBlock(eye, Forward, blockReach, out var hit, out var placeCell, out _, hitFluids: holdingBucket);
        session.HasTarget = hasTarget;
        session.TargetBlock = hit;
        session.PlaceCell = placeCell;

        // Таймеры боя и урона
        AttackTimer -= dt;
        AttackRechargeTimer += dt;
        if (HurtTimer > 0f) HurtTimer = MathF.Max(0f, HurtTimer - dt);
        if (InvulnerabilityTimer > 0f) InvulnerabilityTimer = MathF.Max(0f, InvulnerabilityTimer - dt);

        // 1. Атака сущностей (мобы/животные/босс) требует дискретного клика ЛКМ (AttackPressed)
        Animal? targetedAnimal = null;
        HostileMob? targetedHostile = null;
        EndSlime? targetedBoss = null;
        float bestEntityDist = float.MaxValue;
        const float EntityAttackReach = 3.0f;

        foreach (var a in world.Animals) {
            if (!a.Alive) continue;
            var min = a.Position - new Vector3(Animal.HalfSize, Animal.HalfSize, Animal.HalfSize);
            var max = a.Position + new Vector3(Animal.HalfSize, Animal.HalfSize, Animal.HalfSize);
            if (RayAabb(Eye, Forward, min, max, out float t) && t < bestEntityDist && t <= EntityAttackReach) {
                var hitPoint = Eye + Forward * MathF.Max(0.1f, t - 0.05f);
                if (HostileMob.HasLineOfSight(world, Eye, hitPoint)) {
                    bestEntityDist = t;
                    targetedAnimal = a;
                    targetedHostile = null;
                }
            }
        }

        foreach (var m in world.HostileMobs) {
            if (!m.Alive) continue;
            var mHalf = HostileMob.GetHalfSize(m.Type);
            var min = m.Position - mHalf;
            var max = m.Position + mHalf;
            if (RayAabb(Eye, Forward, min, max, out float t) && t < bestEntityDist && t <= EntityAttackReach) {
                var hitPoint = Eye + Forward * MathF.Max(0.1f, t - 0.05f);
                if (HostileMob.HasLineOfSight(world, Eye, hitPoint)) {
                    bestEntityDist = t;
                    targetedHostile = m;
                    targetedAnimal = null;
                    targetedBoss = null;
                }
            }
        }

        // Босс-слизень (если жив)
        if (world.EndBoss is { Alive: true } boss) {
            var bMin = boss.Position - new Vector3(EndSlime.HalfSizeXZ, EndSlime.HalfSizeY, EndSlime.HalfSizeXZ);
            var bMax = boss.Position + new Vector3(EndSlime.HalfSizeXZ, EndSlime.HalfSizeY, EndSlime.HalfSizeXZ);
            if (RayAabb(Eye, Forward, bMin, bMax, out float bt) && bt < bestEntityDist && bt <= EntityAttackReach) {
                var hitPoint = Eye + Forward * MathF.Max(0.1f, bt - 0.05f);
                if (HostileMob.HasLineOfSight(world, Eye, hitPoint)) {
                    bestEntityDist = bt;
                    targetedBoss = boss;
                    targetedAnimal = null;
                    targetedHostile = null;
                }
            }
        }

        if (input.AttackPressed && (targetedAnimal != null || targetedHostile != null || targetedBoss != null) && bestEntityDist <= EntityAttackReach) {
            BreakTarget = new Vec3i(int.MinValue, int.MinValue, int.MinValue);
            BreakProgress = 0f;
            BreakDuration = 0f;
            if (targetedAnimal != null) AttackAnimal(world, session);
            else if (targetedHostile != null) AttackHostile(targetedHostile, world, session);
            else if (targetedBoss != null) AttackBoss(targetedBoss, world, session);
        } else if ((input.AttackHeld || input.AttackPressed) && (targetedAnimal == null && targetedHostile == null && targetedBoss == null)) {
            // 2. Ломание блоков (в Креативе мгновенно любые блоки, включая бедрок, в Выживании по времени)
            if (hasTarget && GameData.GetBlock(world.GetVoxel(hit).TypeId) is { } targetBlock) {
                if (session.GameMode == GameMode.Creative) {
                    BreakBlock(world, session, hit, targetBlock);
                    BreakProgress = 0f;
                    BreakDuration = 0f;
                    BreakTarget = new Vec3i(int.MinValue, int.MinValue, int.MinValue);
                } else if (!targetBlock.IsUnbreakable) {
                    if (hit != BreakTarget) {
                        BreakTarget = hit;
                        BreakProgress = 0f;
                        ushort tId = SelectedItem?.Id ?? 0;
                        if (!GameData.CanHarvestBlock(targetBlock, tId) && GameData.GetRequiredTier(targetBlock.Id) > 0) {
                            session.AddMessage("Внимание: нужен более прочный инструмент для добычи этого блока!");
                        }
                    }
                    float breakTime = GameData.GetMiningTime(targetBlock, SelectedItem);
                    BreakDuration = breakTime;
                    BreakProgress += dt;
                    if (BreakProgress >= breakTime) {
                        BreakBlock(world, session, hit, targetBlock);
                        BreakProgress = 0f;
                        BreakDuration = 0f;
                        BreakTarget = new Vec3i(int.MinValue, int.MinValue, int.MinValue);
                    }
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
                var pickup = new ItemPickup(entry.Value.Item, 1, dropPos) {
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
                    SoundSystem.PlayChest();
                    wantUse = false;
                } else if (GameData.IsDoor(targetVox.TypeId)) {
                    var lowerPos = targetVox.TypeId == GameData.BDoorLower.Id ? session.TargetBlock : session.TargetBlock + new Vec3i(0, -1, 0);
                    var upperPos = lowerPos + new Vec3i(0, 1, 0);
                    var lVox = world.GetVoxel(lowerPos);
                    var uVox = world.GetVoxel(upperPos);
                    bool isOpen = (lVox.SubGridLayerMask & 8) != 0;
                    byte newMask = (byte)(lVox.SubGridLayerMask ^ 8);
                    lVox.SubGridLayerMask = newMask;
                    uVox.SubGridLayerMask = newMask;
                    world.SetVoxelRaw(lowerPos, in lVox);
                    world.SetVoxelRaw(upperPos, in uVox);
                    if (isOpen) SoundSystem.PlayDoorClose();
                    else SoundSystem.PlayDoorOpen();
                    wantUse = false;
                } else if (targetVox.TypeId == GameData.BBed.Id || targetVox.TypeId == GameData.BBedHead.Id) {
                    if (world.Dimension == Dimension.Nether) {
                        // В Нижнем мире сон вызывает энергетический взрыв!
                        world.RemoveBlock(session.TargetBlock);
                        GameWorld.CreateExplosion(new Vector3(session.TargetBlock.X + 0.5f, session.TargetBlock.Y + 0.5f, session.TargetBlock.Z + 0.5f), 5.0f, 30f, session);
                        wantUse = false;
                        return;
                    }

                    float tod = session.DayNight.TimeOfDay;
                    // Спать можно ночью (>= 12541 тиков, tod >= 0.75f или tod <= 0.25f) или во время грозы
                    bool isNightOrStorm = tod >= 0.75f || tod <= 0.25f || session.Weather == WeatherType.Thunder;

                    bool hostileNearby = false;
                    foreach (var m in world.HostileMobs) {
                        if (m.Alive && Vector3.Distance(m.Position, Position) < 8.0f) {
                            hostileNearby = true;
                            break;
                        }
                    }

                    if (hostileNearby) {
                        session.AddMessage("Вы не можете спать: рядом бродят монстры!");
                    } else if (isNightOrStorm) {
                        session.StartSleep(session.TargetBlock);
                    } else {
                        session.AddMessage("Вы можете спать только ночью или во время грозы");
                    }
                    wantUse = false;
                }
            }
            if (wantUse && SelectedItem is { } item) {
                // Вспахивание земли/травы мотыгой в грядку (Farmland)
                if (GameData.IsHoe(item.Id) && session.HasTarget) {
                    var targetVox = world.GetVoxel(session.TargetBlock);
                    if (targetVox.TypeId == GameData.BGrass.Id || targetVox.TypeId == GameData.BDirt.Id) {
                        var above = session.TargetBlock + new Vec3i(0, 1, 0);
                        if (!world.IsSolidAt(above)) {
                            world.SetBlock(session.TargetBlock, GameData.BFarmland.Id);
                            SoundSystem.PlayDig(GameData.BDirt.Id);

                            wantUse = false;
                        }
                    }
                } else if (item.Id == GameData.WheatSeedsItem.Id) {
                    wantUse = false;
                    // Посадка семян пшеницы СТРОГО на вспаханную грядку (Farmland)
                    if (session.HasTarget) {
                        var targetVox = world.GetVoxel(session.TargetBlock);
                        if (targetVox.TypeId == GameData.BFarmland.Id) {
                            var cropPos = session.TargetBlock + new Vec3i(0, 1, 0);
                            var cropVox = world.GetVoxel(cropPos);
                            if (cropVox.TypeId == 0) {
                                if (TryConsumeSelected(item, 1)) {
                                    var planted = GameData.BWheatCrop;
                                    world.PlacePlacedBlock(cropPos, planted, 0);
                                    SoundSystem.PlayDig(GameData.BGrass.Id);
                                }
                            }
                        }
                    }
                } else if (item.Id == GameData.BoneMealItem.Id) {
                    wantUse = false;
                    if (session.HasTarget) {
                        var targetVox = world.GetVoxel(session.TargetBlock);
                        if (targetVox.TypeId == GameData.BWheatCrop.Id) {
                            int curStage = targetVox.SubGridLayerMask; // 0..3
                            if (curStage < 3) {
                                if (TryConsumeSelected(item, 1)) {
                                    byte nextStage = (byte)Math.Min(3, curStage + 1 + Random.Shared.Next(2));
                                    world.PlacePlacedBlock(session.TargetBlock, GameData.BWheatCrop, nextStage);
                                    SoundSystem.PlayFertilize();
                                    session.AddMessage("Пшеница удобрена и выросла!");
                                }
                            }
                        } else if (targetVox.TypeId == GameData.BGrass.Id) {
                            if (TryConsumeSelected(item, 1)) {
                                SoundSystem.PlayFertilize();
                                for (int dx = -2; dx <= 2; dx++) {
                                    for (int dz = -2; dz <= 2; dz++) {
                                        if (Math.Abs(dx) + Math.Abs(dz) > 3) continue;
                                        var posBelow = session.TargetBlock + new Vec3i(dx, 0, dz);
                                        var posAbove = posBelow + new Vec3i(0, 1, 0);
                                        if (world.GetVoxel(posBelow).TypeId == GameData.BGrass.Id && world.GetVoxel(posAbove).TypeId == 0) {
                                            if (Random.Shared.NextDouble() < 0.65) {
                                                world.PlacePlacedBlock(posAbove, GameData.BTallGrass, 0);
                                            }
                                        }
                                    }
                                }
                                session.AddMessage("Поляна расцвела густой травой!");
                            }
                        }
                    }
                } else if (item.Id == GameData.BowItem.Id) {
                    wantUse = false;
                } else if (item.Id == GameData.ShieldItem.Id) {
                    wantUse = false;
                } else if (item.Id == GameData.FlintAndSteelItem.Id) {
                    wantUse = false;
                    if (session.HasTarget && input.UsePressed) {
                        var targetVox = world.GetVoxel(session.TargetBlock);
                        if (targetVox.TypeId == GameData.BTNT.Id) {
                            world.RemoveBlock(session.TargetBlock);
                            GameWorld.CreateExplosion(new Vector3(session.TargetBlock.X + 0.5f, session.TargetBlock.Y + 0.5f, session.TargetBlock.Z + 0.5f), 4.2f, 26f, session);
                            SoundSystem.PlayPlace();
                        } else if (TryIgniteNetherPortal(world, session.TargetBlock, placeCell)) {
                            SoundSystem.PlayPlace();
                            session.AddMessage("Портал в Нижний мир активирован!");
                        } else {
                            var blk = world.GetBlockType(session.TargetBlock);
                            if (blk != null && blk.IsFlammable) {
                                world.Fire.Ignite(session.TargetBlock);
                                SoundSystem.PlayPlace();
                            } else {
                                var placeVox = world.GetVoxel(placeCell);
                                if (placeVox.TypeId == 0) {
                                    float dur = targetVox.TypeId == GameData.BNetherrack.Id ? 99999f : 14f;
                                    world.Fire.Burning[placeCell] = dur;
                                    world.MarkLightDirty(placeCell);
                                    SoundSystem.PlayPlace();
                                }
                            }
                        }
                    }
                }

                if (wantUse) {
                    if (item.Id == GameData.BucketItem.Id) {
                        Vec3i fluidPos = default;
                        bool foundFluid = false;
                        bool isWater = false;

                        if (session.HasTarget) {
                            var tv = world.GetVoxel(session.TargetBlock);
                            if (tv.TypeId == GameData.BWater.Id || tv.TypeId == GameData.BLava.Id) {
                                fluidPos = session.TargetBlock;
                                foundFluid = true;
                                isWater = tv.TypeId == GameData.BWater.Id;
                            } else {
                                var pv = world.GetVoxel(session.PlaceCell);
                                if (pv.TypeId == GameData.BWater.Id || pv.TypeId == GameData.BLava.Id) {
                                    fluidPos = session.PlaceCell;
                                    foundFluid = true;
                                    isWater = pv.TypeId == GameData.BWater.Id;
                                }
                            }
                        }
                        if (!foundFluid) {
                            for (float d = 0.25f; d <= Reach; d += 0.25f) {
                                var samplePos = new Vec3i((int)MathF.Floor(eye.X + Forward.X * d), (int)MathF.Floor(eye.Y + Forward.Y * d), (int)MathF.Floor(eye.Z + Forward.Z * d));
                                var sv = world.GetVoxel(samplePos);
                                if (sv.TypeId == GameData.BWater.Id || sv.TypeId == GameData.BLava.Id) {
                                    fluidPos = samplePos;
                                    foundFluid = true;
                                    isWater = sv.TypeId == GameData.BWater.Id;
                                    break;
                                }
                            }
                        }

                        if (foundFluid) {
                            world.RemoveBlock(fluidPos);
                            SoundSystem.PlaySplash();

                            if (session.GameMode != GameMode.Creative) {
                                Inventory.RemoveAt(SelectedSlot);
                                Inventory.InsertAt(SelectedSlot, new ItemEntry(GameData.NewItem(isWater ? GameData.WaterBucketItem : GameData.LavaBucketItem), 1));
                            }
                            session.AddMessage(isWater ? "Ведро наполнено водой" : "Ведро наполнено лавой");
                            wantUse = false;
                        }
                    } else if (item.Id == GameData.WaterBucketItem.Id) {
                        Vec3i placeAt = session.HasTarget ? session.PlaceCell : new Vec3i((int)MathF.Floor(eye.X + Forward.X * 2f), (int)MathF.Floor(eye.Y + Forward.Y * 2f), (int)MathF.Floor(eye.Z + Forward.Z * 2f));
                        if (session.HasTarget && (world.GetVoxel(session.TargetBlock).TypeId == 0 || !world.IsSolidAt(session.TargetBlock))) placeAt = session.TargetBlock;
                        world.PlacePlacedBlock(placeAt, GameData.BWater, 0);
                        SoundSystem.PlaySplash();
                        if (session.GameMode != GameMode.Creative) {
                            Inventory.RemoveAt(SelectedSlot);
                            Inventory.InsertAt(SelectedSlot, new ItemEntry(GameData.NewItem(GameData.BucketItem), 1));
                        }
                        session.AddMessage("Вода вылита из ведра");
                        wantUse = false;
                    } else if (item.Id == GameData.LavaBucketItem.Id) {
                        Vec3i placeAt = session.HasTarget ? session.PlaceCell : new Vec3i((int)MathF.Floor(eye.X + Forward.X * 2f), (int)MathF.Floor(eye.Y + Forward.Y * 2f), (int)MathF.Floor(eye.Z + Forward.Z * 2f));
                        if (session.HasTarget && (world.GetVoxel(session.TargetBlock).TypeId == 0 || !world.IsSolidAt(session.TargetBlock))) placeAt = session.TargetBlock;
                        world.PlacePlacedBlock(placeAt, GameData.BLava, 0);
                        SoundSystem.PlayPlace();
                        if (session.GameMode != GameMode.Creative) {
                            Inventory.RemoveAt(SelectedSlot);
                            Inventory.InsertAt(SelectedSlot, new ItemEntry(GameData.NewItem(GameData.BucketItem), 1));
                        }
                        session.AddMessage("Лава вылита из ведра");
                        wantUse = false;
                    } else if (GameData.FoodValue.TryGetValue(item.Id, out float foodVal)) {
                        bool canEatFood = Hunger < MaxHunger || item.Id == GameData.GoldenAppleItem.Id;
                        if (input.UsePressed && canEatFood) {
                            if (TryConsumeSelected(item, 1)) {
                                Hunger = MathF.Min(MaxHunger, Hunger + foodVal);
                                Saturation = MathF.Min(Hunger, Saturation + foodVal * 0.6f);
                                if (item.Id == GameData.GoldenAppleItem.Id) {
                                    Health = MathF.Min(MaxHealth, Health + 4f);
                                    InvulnerabilityTimer = 1.5f;
                                    session.AddMessage("Золотое яблоко: +4 HP и кратковременная защита!");
                                } else if (item.Id == GameData.RottenFleshItem.Id) {
                                    if (Random.Shared.NextDouble() < 0.80) {
                                        Exhaustion += 12.0f;
                                        session.AddMessage("Несвежая пища вызвала приступ голода!");
                                    }
                                }
                                SoundSystem.PlayEat();
                            }
                        }
                    } else if (item.Id == GameData.EyeOfEnderItem.Id) {
                        // Вставка Ока Эндера в рамку портала Энда
                        wantUse = false;
                        if (input.UsePressed && session.HasTarget) {
                            var targetVox = world.GetVoxel(session.TargetBlock);
                            if (targetVox.TypeId == GameData.BEndPortalFrame.Id) {
                                if ((targetVox.SubGridLayerMask & 1) == 0) {
                                    if (TryConsumeSelected(item, 1)) {
                                        targetVox.SubGridLayerMask |= 1;
                                        world.SetVoxelRaw(session.TargetBlock, in targetVox);
                                        SoundSystem.PlayPlace();
                                        if (TryActivateEndPortal(world, session.TargetBlock)) {
                                            session.AddMessage("Портал в Энд открыт!");
                                            SoundSystem.PlayThunder();
                                        } else {
                                            session.AddMessage("Око Эндера вставлено в рамку портала.");
                                        }
                                    }
                                } else {
                                    session.AddMessage("Рамка портала уже содержит око.");
                                }
                            }
                        }
                    } else if (session.HasTarget && GameData.TryGetBlockByItem(item.Id, out var block)) {
                        var targetVox = world.GetVoxel(session.TargetBlock);
                        Vec3i targetPlace = (targetVox.TypeId == GameData.BWater.Id || targetVox.TypeId == GameData.BLava.Id)
                            ? session.TargetBlock
                            : placeCell;
                        TryPlaceBlock(world, session, targetPlace, block!, item);
                    }
                }
            }
        }

        // Логика стрельбы из лука (натягивание тетивы ПКМ и выстрел при отпускании)
        bool hasBowInHand = SelectedItem != null && SelectedItem.Id == GameData.BowItem.Id;
        if (hasBowInHand) {
            if (input.UseHeld) {
                BowCharge = MathF.Min(1.0f, BowCharge + dt * 1.0f);
            } else if (BowCharge > 0.12f) {
                // Выстрел
                bool hasArrow = (OffhandItem != null && OffhandItem.Id == GameData.ArrowItem.Id && OffhandCount > 0) ||
                                Inventory.CountOf(GameData.ArrowItem) > 0;
                if (hasArrow) {
                    if (OffhandItem != null && OffhandItem.Id == GameData.ArrowItem.Id && OffhandCount > 0) {
                        OffhandCount--;
                        if (OffhandCount <= 0) OffhandItem = null;
                    } else {
                        Inventory.TryRemove(GameData.ArrowItem, 1);
                    }
                    float charge = Math.Clamp(BowCharge, 0.2f, 1.0f);
                    float arrowSpeed = 14f + charge * 24f;
                    float dmg = 3f + charge * 8f;
                    world.Arrows.Add(new ArrowProjectile(Eye + Forward * 0.4f, Forward * arrowSpeed, null) { FromPlayer = true, Damage = dmg });
                    SoundSystem.PlayBowShoot();
                } else {
                    session.AddMessage("Нет стрел!");
                }
                BowCharge = 0f;
            } else {
                BowCharge = 0f;
            }
        } else {
            BowCharge = 0f;
        }

        // Логика блокирования щитом
        bool hasShieldEquipped = (OffhandItem != null && OffhandItem.Id == GameData.ShieldItem.Id) ||
                                (SelectedItem != null && SelectedItem.Id == GameData.ShieldItem.Id);
        IsBlocking = hasShieldEquipped && input.UseHeld && !hasBowInHand;

        // Логика нахождения внутри Портала (в Нижний мир / Энд)
        bool inNetherPortal = feetBlock.TypeId == GameData.BNetherPortal.Id || eyeVoxel.TypeId == GameData.BNetherPortal.Id;
        bool inEndPortal = feetBlock.TypeId == GameData.BEndPortal.Id || eyeVoxel.TypeId == GameData.BEndPortal.Id;
        if (inNetherPortal || inEndPortal) {
            if (PortalTimer >= 0f) {
                PortalTimer += dt;
                if (PortalTimer >= 1.5f) {
                    Dimension target = inEndPortal
                        ? (session.World.Dimension == Dimension.Overworld ? Dimension.End : Dimension.Overworld)
                        : (session.World.Dimension == Dimension.Overworld ? Dimension.Nether : Dimension.Overworld);
                    session.SwitchDimension(target);
                    PortalTimer = -2.0f; // Задержка перед повторным входом
                }
            } else {
                PortalTimer += dt;
            }
        } else {
            PortalTimer = MathF.Max(0f, PortalTimer - dt * 2f);
        }

        TickVitals(dt, session);
    }

    public static bool TryIgniteNetherPortal(GameWorld world, Vec3i targetBlock, Vec3i placeCell) {
        for (int axis = 0; axis < 2; axis++) {
            for (int dx = -2; dx <= 0; dx++) {
                for (int dy = -3; dy <= 0; dy++) {
                    int minX = axis == 0 ? placeCell.X + dx : placeCell.X;
                    int minZ = axis == 1 ? placeCell.Z + dx : placeCell.Z;
                    int minY = placeCell.Y + dy;

                    bool validFrame = true;
                    for (int step = 0; step < 4; step++) {
                        int fx = axis == 0 ? minX + step : minX;
                        int fz = axis == 1 ? minZ + step : minZ;
                        if (world.GetVoxel(new Vec3i(fx, minY, fz)).TypeId != GameData.BObsidian.Id ||
                            world.GetVoxel(new Vec3i(fx, minY + 4, fz)).TypeId != GameData.BObsidian.Id) {
                            validFrame = false; break;
                        }
                    }
                    if (!validFrame) continue;

                    for (int y = 1; y <= 3; y++) {
                        int lx = axis == 0 ? minX : minX;
                        int lz = axis == 1 ? minZ : minZ;
                        int rx = axis == 0 ? minX + 3 : minX;
                        int rz = axis == 1 ? minZ + 3 : minZ;
                        if (world.GetVoxel(new Vec3i(lx, minY + y, lz)).TypeId != GameData.BObsidian.Id ||
                            world.GetVoxel(new Vec3i(rx, minY + y, rz)).TypeId != GameData.BObsidian.Id) {
                            validFrame = false; break;
                        }
                    }
                    if (!validFrame) continue;

                    for (int innerStep = 1; innerStep <= 2; innerStep++) {
                        for (int y = 1; y <= 3; y++) {
                            int ix = axis == 0 ? minX + innerStep : minX;
                            int iz = axis == 1 ? minZ + innerStep : minZ;
                            world.PlacePlacedBlock(new Vec3i(ix, minY + y, iz), GameData.BNetherPortal);
                        }
                    }
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Проверяет, собран ли портал Энда: 12 рамок по кольцу 4×4 с 2×2 проёмом,
    /// в каждой вставлено око Эндера. При активации заполняет проём порталом.
    /// </summary>
    public static bool TryActivateEndPortal(GameWorld world, Vec3i framePos) {
        int y = framePos.Y;
        var frames = new List<Vec3i>();
        for (int dx = -3; dx <= 3; dx++) {
            for (int dz = -3; dz <= 3; dz++) {
                var p = new Vec3i(framePos.X + dx, y, framePos.Z + dz);
                if (world.GetVoxel(p).TypeId == GameData.BEndPortalFrame.Id) {
                    frames.Add(p);
                }
            }
        }
        if (frames.Count != 12) return false;

        int minX = int.MaxValue, maxX = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (var f in frames) {
            if (f.X < minX) minX = f.X;
            if (f.X > maxX) maxX = f.X;
            if (f.Z < minZ) minZ = f.Z;
            if (f.Z > maxZ) maxZ = f.Z;
        }
        // Кольцо 4×4 (12 рамок): охват 3 блока по каждой оси, проём 2×2 внутри
        if (maxX - minX != 3 || maxZ - minZ != 3) return false;

        // В каждой рамке должно быть око
        foreach (var f in frames) {
            if ((world.GetVoxel(f).SubGridLayerMask & 1) == 0) return false;
        }

        // Проём 2×2 должен быть пуст (или землёй), заполняем порталом
        int ix = minX + 1, iz = minZ + 1;
        for (int dx = 0; dx <= 1; dx++) {
            for (int dz = 0; dz <= 1; dz++) {
                var p = new Vec3i(ix + dx, y, iz + dz);
                var vox = world.GetVoxel(p);
                if (vox.TypeId != 0 && vox.TypeId != GameData.BEndPortal.Id && vox.TypeId != GameData.BWater.Id && vox.TypeId != GameData.BLava.Id) {
                    return false;
                }
            }
        }
        for (int dx = 0; dx <= 1; dx++) {
            for (int dz = 0; dz <= 1; dz++) {
                var p = new Vec3i(ix + dx, y, iz + dz);
                world.PlacePlacedBlock(p, GameData.BEndPortal);
            }
        }
        return true;
    }

    private void TickVitals(float dt, GameSession session) {
        // Фоновый расход сытости и голод
        if (Exhaustion >= 4.0f) {
            Exhaustion -= 4.0f;
            if (Saturation > 0f) Saturation = MathF.Max(0f, Saturation - 1f);
            else Hunger = MathF.Max(0f, Hunger - 1f);
        }

        // Естественная регенерация здоровья при высокой сытости
        if (Hunger >= 20f && Saturation > 0f && Health < MaxHealth) {
            HealthRegenTimer += dt;
            if (HealthRegenTimer >= 0.5f) {
                HealthRegenTimer = 0f;
                Health = MathF.Min(MaxHealth, Health + 1f);
                Exhaustion += 6.0f;
            }
        } else if (Hunger >= 18f && Health < MaxHealth) {
            HealthRegenTimer += dt;
            if (HealthRegenTimer >= 4.0f) {
                HealthRegenTimer = 0f;
                Health = MathF.Min(MaxHealth, Health + 1f);
                Exhaustion += 6.0f;
            }
        } else {
            HealthRegenTimer = 0f;
        }

        // Урон от голода при Hunger <= 0
        if (Hunger <= 0f) {
            StarveTimer += dt;
            if (StarveTimer >= 4.0f) {
                StarveTimer = 0f;
                ApplyDamage(1f, session);
                session.AddMessage("Вы умираете от голода!");
            }
        } else {
            StarveTimer = 0f;
        }

        if (FireTicks > 0f) {
            FireTicks = MathF.Max(0f, FireTicks - dt);
            FireBurnTimer -= dt;
            if (FireBurnTimer <= 0f) {
                FireBurnTimer = 1.0f; // 1 HP горения в секунду
                InvulnerabilityTimer = 0f;
                ApplyDamage(1.0f, session);
            }
        } else {
            FireBurnTimer = 0f;
        }
        if (Health <= 0f) session.DiePlayer();
    }

    // ── Ломание блоков ───────────────────────────────────────────────────────

    private static readonly Random DropRng = new();

    public void BreakBlock(GameWorld world, GameSession session, Vec3i pos, BlockType block) {
        var oldVox = world.GetVoxel(pos);
        world.RemoveBlock(pos);
        SoundSystem.PlayDig(block.Id);

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

        if (session.GameMode == GameMode.Creative) {
            return; // В Творческом режиме блоки разрушаются без дропа и без износа инструмента
        }

        // Проверяем, может ли текущий инструмент добыть этот блок
        ushort toolId = SelectedEntry?.Item.Definition.Id ?? 0;
        bool canHarvest = GameData.CanHarvestBlock(block, toolId);

        if (canHarvest) {
            int dropCount = block.DropItemCount;
            if (block.Id == GameData.BWheatCrop.Id) {
                int stage = oldVox.SubGridLayerMask; // 0..3
                if (stage >= 3) {
                    // Зрелая пшеница: 1 пшеница + 1..3 семян
                    world.SpawnPickup(GameData.WheatItem.Id, 1, pos);
                    int seedCount = DropRng.Next(1, 4);
                    world.SpawnPickup(GameData.WheatSeedsItem.Id, seedCount, pos);
                } else {
                    // Недозрелая: только 1 семечко
                    world.SpawnPickup(GameData.WheatSeedsItem.Id, 1, pos);
                }
            } else if (block.Id == GameData.BTallGrass.Id) {
                if (DropRng.NextDouble() < 0.30) {
                    world.SpawnPickup(GameData.WheatSeedsItem.Id, 1, pos);
                }
            } else if (block.DropItemId != 0 && GameData.Items.TryGetValue(block.DropItemId, out var drop)) {
                if (block.Id == GameData.BGravel.Id && DropRng.NextDouble() < 0.25) {
                    world.SpawnPickup(GameData.FlintItem.Id, 1, pos);
                } else {
                    world.SpawnPickup(drop.Id, dropCount, pos);
                }
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

    private bool TryConsumeSelected(ItemDefinition item, int qty = 1, GameSession? session = null) {
        if (session != null && session.GameMode == GameMode.Creative) return true;
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

        // Специальная установка 2-блочной двери (низ + верх)
        if (item.Id == GameData.DoorItem.Id) {
            var above = cell + new Vec3i(0, 1, 0);
            if (world.IsSolidAt(above) || world.GetVoxel(above).TypeId != 0) return false;
            if (!world.IsSolidAt(cell + new Vec3i(0, -1, 0))) return false;

            if (TryConsumeSelected(item, 1, session)) {
                world.PlacePlacedBlock(cell, GameData.BDoorLower, facing);
                world.PlacePlacedBlock(above, GameData.BDoorUpper, facing);
                SoundSystem.PlayPlace();
                return true;
            }
            return false;
        }

        // Специальная установка 2-блочной кровати (изножье + изголовье)
        if (block.Id == GameData.BBed.Id) {
            var headCell = cell + forwardH;
            var exHead = world.GetVoxel(headCell);
            if (exHead.TypeId != 0 && (GameData.GetBlock(exHead.TypeId).IsSolid || GameData.GetBlock(exHead.TypeId).IsOpaque))
                return false;
            if (!world.IsSolidAt(cell + new Vec3i(0, -1, 0)) || !world.IsSolidAt(headCell + new Vec3i(0, -1, 0)))
                return false;

            if (TryConsumeSelected(item, 1, session)) {
                world.PlacePlacedBlock(cell, GameData.BBed, facing);
                world.PlacePlacedBlock(headCell, GameData.BBedHead, facing);
                SoundSystem.PlayPlace();
                return true;
            }
            return false;
        }

        // 3D Факел: установка на пол (facing=0) или на стену (facing=1..4)
        if (block.Id == GameData.BTorch.Id) {
            var hitNormal = cell - session.TargetBlock;
            byte torchFacing = 0;
            if (hitNormal.X == 1) torchFacing = 1;      // Прикреплен к стене на западе
            else if (hitNormal.X == -1) torchFacing = 2; // Прикреплен к стене на востоке
            else if (hitNormal.Z == 1) torchFacing = 3;  // Прикреплен к стене на севере
            else if (hitNormal.Z == -1) torchFacing = 4; // Прикреплен к стене на юге

            if (TryConsumeSelected(item, 1, session)) {
                world.PlacePlacedBlock(cell, block, torchFacing);
                SoundSystem.PlayPlace();
                return true;
            }
            return false;
        }

        // Гравитация песка и гравия при установке в воздухе
        if (block.Id == GameData.BSand.Id || block.Id == GameData.BGravel.Id) {
            var below = cell + new Vec3i(0, -1, 0);
            if (!world.IsSolidAt(below)) {
                if (TryConsumeSelected(item, 1, session)) {
                    world.FallingBlocks.Add(new FallingBlock(block, new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z + 0.5f)));
                    SoundSystem.PlayPlace();
                    return true;
                }
                return false;
            }
        }

        if (TryConsumeSelected(item, 1, session)) {
            world.PlacePlacedBlock(cell, block, facing);
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
        if (best == null || bestDist > 3.0f) return;

        ushort toolId = SelectedItem?.Id ?? 0;
        float weaponCd = GameData.GetWeaponCooldown(toolId);
        float charge = Math.Clamp(AttackRechargeTimer / weaponCd, 0f, 1f);
        AttackRechargeTimer = 0f;
        AttackTimer = AttackCooldown;

        bool isStrong = charge >= 0.85f;
        bool isCrit = isStrong && !OnGround && Velocity.Y < -0.2f;

        float baseDmg = GameData.GetWeaponDamage(toolId);
        float dmg = baseDmg * (0.2f + 0.8f * charge * charge);

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
            float knockback = isStrong ? 5.0f : 1.5f;
            float vertKnock = isStrong ? 3.5f : 1.0f;
            best.Velocity += new Vector3(pushH.X * knockback, vertKnock, pushH.Y * knockback);
            best.WanderDir = pushH;
        } else {
            best.Velocity += new Vector3(0f, isStrong ? 3.5f : 1.0f, 0f);
        }

        if (isStrong) SoundSystem.PlayStrongAttack();
        else SoundSystem.PlayWeakAttack();

        if (best.Health <= 0f) {
            best.Die(world, session);
        }
    }

    public void AttackHostile(HostileMob mob, GameWorld world, GameSession session) {
        if (AttackTimer > 0f) return;

        ushort toolId = SelectedItem?.Id ?? 0;
        float weaponCd = GameData.GetWeaponCooldown(toolId);
        float charge = Math.Clamp(AttackRechargeTimer / weaponCd, 0f, 1f);
        AttackRechargeTimer = 0f;
        AttackTimer = AttackCooldown;

        bool isStrong = charge >= 0.85f;
        bool isCrit = isStrong && !OnGround && Velocity.Y < -0.2f;

        float baseDmg = GameData.GetWeaponDamage(toolId);
        float dmg = baseDmg * (0.2f + 0.8f * charge * charge);

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
            float knockback = isStrong ? 6.0f : 2.0f;
            float vertKnock = isStrong ? 3.0f : 1.0f;
            mob.Velocity += new Vector3(pushH.X * knockback, vertKnock, pushH.Y * knockback);
        }

        if (isStrong) SoundSystem.PlayStrongAttack();
        else SoundSystem.PlayWeakAttack();

        if (mob.Health <= 0f) {
            mob.Die(world, session);
        }
    }

    public void AttackBoss(EndSlime boss, GameWorld world, GameSession session) {
        if (AttackTimer > 0f) return;

        ushort toolId = SelectedItem?.Id ?? 0;
        float weaponCd = GameData.GetWeaponCooldown(toolId);
        float charge = Math.Clamp(AttackRechargeTimer / weaponCd, 0f, 1f);
        AttackRechargeTimer = 0f;
        AttackTimer = AttackCooldown;

        bool isStrong = charge >= 0.85f;
        bool isCrit = isStrong && !OnGround && Velocity.Y < -0.2f;

        float baseDmg = GameData.GetWeaponDamage(toolId);
        float dmg = baseDmg * (0.2f + 0.8f * charge * charge);

        if (isCrit) {
            dmg *= 1.5f;
            session.AddMessage("Критический удар по Слизню Края! ×1.5");
            world.SpawnCrit(boss.Position + new Vector3(0f, EndSlime.HalfSizeY, 0f), 16);
        }

        boss.TakeDamage(dmg, world, session);

        if (isStrong) SoundSystem.PlayStrongAttack();
        else SoundSystem.PlayWeakAttack();
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
