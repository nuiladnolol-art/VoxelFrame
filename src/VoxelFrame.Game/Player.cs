using System.Numerics;
using Raylib_cs;
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
    public Dimension Dimension = Dimension.Overworld;
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
    public float EatingTimer;
    public float EatingSoundTimer;
    public float AttackTimer;
    /// <summary>Метка замаха (ЛКМ) — нужна для удара по эндер-кристаллу, который не ловится лучом.</summary>
    public float SwingMarker;
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
    public float VoidDamageTimer;
    /// <summary>Урон от застревания в блоках.</summary>
    public float StuckTimer;
    public const float MaxHunger = 20f;
    public float Hunger = 20f;
    public float Saturation = 5f;
    public float Exhaustion = 0f;
    public float StarveTimer = 0f;
    private ItemEntry? _offhandEntry;
    public ItemEntry? OffhandEntry {
        get => _offhandEntry;
        set => _offhandEntry = (value.HasValue && value.Value.Quantity > 0) ? value : null;
    }
    public ItemDefinition? OffhandItem {
        get => _offhandEntry?.Item.Definition;
        set {
            if (value != null) {
                if (_offhandEntry == null || _offhandEntry.Value.Item.Definition != value) {
                    _offhandEntry = new ItemEntry(GameData.NewItem(value), _offhandEntry?.Quantity ?? 1);
                }
            } else {
                _offhandEntry = null;
            }
        }
    }
    public int OffhandCount {
        get => _offhandEntry?.Quantity ?? 0;
        set {
            if (value <= 0) {
                _offhandEntry = null;
            } else if (_offhandEntry != null) {
                _offhandEntry = _offhandEntry.Value with { Quantity = value };
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
    public bool PortalLocked { get; set; }   // Блокирует повторный вход, пока игрок не покинет порталы
    public float VoidFallTimer;              // Сколько игрок выживает в пустоте (путь в Бездну)
    public string Name { get; set; } = "Steve";
    public bool IsMoving => Velocity.X * Velocity.X + Velocity.Z * Velocity.Z > 0.01f;
    public bool IsCrouching => CurrentEyeHeight < EyeHeight - 0.1f;
    public bool IsFlying { get; set; }
    public float SpaceDoubleTapTimer { get; set; }
    public readonly ItemEntry?[] Armor = new ItemEntry?[4]; // 0=Helmet, 1=Chestplate, 2=Leggings, 3=Boots
    public float BreakGraceTimer;

    public int GetTotalArmorPoints() {
        int pts = 0;
        for (int i = 0; i < 4; i++) {
            if (Armor[i] is { } ae && ae.Quantity > 0) {
                pts += GameData.GetArmorPoints(ae.Item.Definition.Id);
            }
        }
        return pts;
    }

    public void ApplyDamage(float amount, GameSession session, Vector3? attackerPos = null, string cause = "") {
        if (session.GameMode == GameMode.Creative) return; // В Творческом режиме игрок бессмертен
        if (amount <= 0f) return;
        if (InvulnerabilityTimer > 0f) return;

        if (!string.IsNullOrEmpty(cause)) {
            session.LastDeathCause = cause;
        } else if (attackerPos.HasValue) {
            session.LastDeathCause = "Атака врага";
        }
        session.LastDeathPos = Position;

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

        // Поглощение урона броней (кроме пустоты и истощения от голода)
        int armorPts = GetTotalArmorPoints();
        if (armorPts > 0 && cause != "Падение в Бездну" && cause != "Истощение от голода" && cause != "Голод") {
            float reduction = Math.Clamp(armorPts * 0.035f, 0.05f, 0.75f);
            amount *= (1.0f - reduction);

            // Изнашивание надетой брони
            for (int i = 0; i < 4; i++) {
                if (Armor[i] is { } ae && ae.Quantity > 0) {
                    var inst = ae.Item;
                    inst.Durability--;
                    if (inst.Durability <= 0) {
                        session.AddMessage($"Броня «{ae.Item.Definition.Name}» сломалась!");
                        SoundSystem.PlayBreakTool();
                        Armor[i] = null;
                    } else {
                        Armor[i] = ae with { Item = inst };
                    }
                }
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
        var mainEntry = Inventory.Slots[SelectedSlot];
        var offhandEntry = _offhandEntry;

        // Очищаем текущий слот хотбара
        if (mainEntry != null) {
            Inventory.RemoveAt(SelectedSlot);
        }

        // Переносим предмет в левую руку (сохраняя точный ItemInstance и его Durability)
        _offhandEntry = mainEntry;

        // Переносим бывший предмет из левой руки в выбранный слот хотбара (сохраняя точный ItemInstance)
        if (offhandEntry != null) {
            Inventory.InsertAt(SelectedSlot, offhandEntry.Value);
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

    /// <summary>Телепорт игрока (жемчуг Эндера): ставим над первой твёрдой опорой, сбрасываем скорость.</summary>
    public void TeleportTo(Vector3 target, GameWorld world) {
        int tx = (int)MathF.Floor(target.X);
        int tz = (int)MathF.Floor(target.Z);
        int topY = (int)MathF.Floor(target.Y);
        for (int wy = topY + 1; wy >= topY - 16 && wy >= 2; wy--) {
            var belowPos = new Vec3i(tx, wy - 1, tz);
            var footPos = new Vec3i(tx, wy, tz);
            var headPos = new Vec3i(tx, wy + 1, tz);
            var belowVox = world.GetVoxel(belowPos);
            if (belowVox.TypeId == 0 || belowVox.TypeId == GameData.BLava.Id || belowVox.TypeId == GameData.BWater.Id) continue;
            var belowBlock = GameData.GetBlock(belowVox.TypeId);
            if (!belowBlock.IsSolid) continue;
            bool footFree = !world.IsSolidAt(footPos);
            bool headFree = !world.IsSolidAt(headPos);
            if (footFree && headFree) {
                Position = new Vector3(tx + 0.5f, wy + 0.95f, tz + 0.5f);
                Velocity = Vector3.Zero;
                OnGround = true;
                return;
            }
        }
        // Опора не нашлась (жемчуг улетел в пустоту) — просто переносим в точку приземления.
        Position = new Vector3(target.X, MathF.Max(2f, target.Y), target.Z);
        Velocity = Vector3.Zero;
    }

    /// <summary>Случайный телепорт плода хоруса: пытаемся сдвинуться в радиусе ~8 блоков (как в Minecraft).</summary>
    private void TeleportRandomly(GameWorld world) {
        for (int attempt = 0; attempt < 32; attempt++) {
            float dx = (Random.Shared.NextSingle() - 0.5f) * 16f;
            float dy = (Random.Shared.NextSingle() - 0.5f) * 8f;
            float dz = (Random.Shared.NextSingle() - 0.5f) * 16f;
            var target = Position + new Vector3(dx, dy, dz);
            TeleportTo(target, world);
            if (Vector3.DistanceSquared(Position, target) > 1.0f) return; // реально переместились
        }
    }

    /// <summary>Бросок Жемчуга Эндера: дуга вперёд, при падении на блок — телепорт.</summary>
    private void ThrowEnderPearl(GameWorld world, GameSession session) {
        world.Arrows.Add(new ArrowProjectile(Eye + Forward * 0.4f, Forward * 20f, null) {
            IsEnderPearl = true,
            LifeTime = 20f
        });
        SoundSystem.PlayBowShoot();
    }

    /// <summary>Бросок Ока Эндера: летит к ближайшей крепости, затем падает и возвращается пикапом.</summary>
    private void ThrowEyeOfEnder(GameWorld world, GameSession session) {
        Vector3 vel;
        if (world.FindNearestEndStronghold(Eye) is Vector3 target) {
            Vector3 diff = target + new Vector3(0f, 2f, 0f) - Eye;
            Vector3 dir = diff.LengthSquared() > 0.0001f ? Vector3.Normalize(diff) : Forward;
            vel = dir * 16f + Vector3.UnitY * 6f;
            session.AddMessage("Око Эндера указывает на ближайшую крепость!");
        } else {
            vel = Forward * 12f + Vector3.UnitY * 6f;
            session.AddMessage("Око Эндера кружится... поблизости нет крепости.");
        }
        world.Arrows.Add(new ArrowProjectile(Eye + Forward * 0.4f, vel, null) { IsEyeOfEnder = true, LifeTime = 12f });
        SoundSystem.PlayBowShoot();
    }

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
            // Показываем название предмета пару секунд над хотбаром (+ прочность для инструментов)
            SlotToastTimer = 2f;
            if (SelectedEntry is { } se && GameData.GetToolTier(se.Item.Definition.Id) > 0) {
                SlotToastText = $"{se.Item.Definition.Name} ({se.Item.Durability}/{GameData.GetMaxToolDurability(se.Item.Definition.Id)})";
            } else {
                SlotToastText = SelectedItem?.Name ?? "";
            }
            int itemId = SelectedItem?.Id ?? 0;
            GameClient.Active?.SendAction(PlayerActionType.ItemChange, itemId);
            GameServer.Active?.BroadcastHostAction(PlayerActionType.ItemChange, itemId);
        }

        // Быстрая смена предмета между основной и второй рукой по клавише F
        if (Raylib_cs.Raylib.IsKeyPressed(Raylib_cs.KeyboardKey.F)) {
            SwapMainAndOffhand();
            int itemId = SelectedItem?.Id ?? 0;
            GameClient.Active?.SendAction(PlayerActionType.ItemChange, itemId);
            GameServer.Active?.BroadcastHostAction(PlayerActionType.ItemChange, itemId);
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
            Exhaustion += dt * 0.06f;
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
                    Exhaustion += IsSprinting ? 0.08f : 0.02f;
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

        // Звуки шагов по типу материала и пыль при спринте.
        // Условие — по нажатым клавишам (а не по скорости): если игрок отпустил движение,
        // шаги прекращаются сразу, даже если скорость ещё не обнулилась до нуля.
        bool stepMoving = input.MoveX != 0f || input.MoveZ != 0f;
        if (OnGround && stepMoving && !inWater && !inLava && !input.Crouch) {
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

        // Падение в пустоту ниже предела.
        // В Энде это путь в Бездну: пустота жжёт, но если выжить (хил/тотем) —
        // проходишь порог и падаешь на платформу с Вратами. В самой Бездне — возврат.
        if (Position.Y < FallingBlock.VoidY) {
            HighestYInAir = Position.Y;
            var dim = session.World.Dimension;
            if (dim == Dimension.End || dim == Dimension.Void) {
                VoidFallTimer += dt;
                VoidDamageTimer -= dt;
                // Смертоносный урон Пустоты: 2.0 HP (1 сердце) каждые 0.333с = ровно 6.0 HP (3 сердца в секунду)
                if (VoidDamageTimer <= 0f) {
                    VoidDamageTimer = 0.333f;
                    InvulnerabilityTimer = 0f;
                    ApplyDamage(2.0f, session);
                }
                if (VoidFallTimer > 1.0f && VoidFallTimer < 1.4f) {
                    session.ShowTitle("СМЕРТОНОСНАЯ ПУСТОТА", "Используйте Золотые яблоки и Тотемы бессмертия!", 3.5f, new Color(255, 40, 70, 255));
                }

                if (dim == Dimension.Void) {
                    // В измерении Бездны при падении за край арены возвращаем на монолитный пол
                    if (VoidFallTimer >= 0.8f) {
                        VoidFallTimer = 0f;
                        VoidDamageTimer = 0f;
                        Position = new Vector3(0.5f, 13.5f, -14f);
                        Velocity = Vector3.Zero;
                        session.ShowTitle("ТЕМНОЕ ПРИТЯЖЕНИЕ", "Гравитация Бездны вернула вас на арену!", 3.0f, new Color(180, 60, 255, 255));
                        SoundSystem.PlayThunder();
                    }
                } else if (VoidFallTimer >= 5.0f) {
                    VoidFallTimer = 0f;
                    VoidDamageTimer = 0f;
                    session.EnterVoid();
                }
            } else {
                ApplyDamage(MaxHealth * 2f, session);
                session.AddMessage("Вы упали в пустоту!");
            }
        } else {
            VoidFallTimer = 0f;
            VoidDamageTimer = 0f;
        }

        // Проверка обнаружения древнего Обелиска Края на побочном острове (+100, +100)
        if (session.World.Dimension == Dimension.End && !session.EndLoreDiscovered) {
            var toLoreAltar = Position - new Vector3(100.5f, 73f, 100.5f);
            if (toLoreAltar.Length() < 6.5f) {
                session.EndLoreDiscovered = true;
                session.ShowTitle("ЗАБЫТЫЙ ОБЕЛИСК КРАЯ", "«Скуй Ключ Бездны из Слизи Края и трёх реликвий...»", 6.0f, new Color(255, 215, 80, 255), new Color(255, 240, 180, 255));
                session.AddMessage("§6[Забытый Обелиск Края]:");
                session.AddMessage("§e«Соедини Слизь Края с тремя реликвиями (Ад, Болото, Пустыня), чтобы сковать Ключ Бездны...»");
                session.AddMessage("§c«Но путь в Бездну лежит сквозь смертоносное падение в Пустоту (~3 сердца в секунду). Лишь вкусивший Золотые яблоки и запасшийся Тотемами достигнет пола из бедрока...»");
                SoundSystem.PlayThunder();
            }
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

        // 1. Атака сущностей (игроки/мобы/животные/босс/истинный босс) требует дискретного клика ЛКМ (AttackPressed)
        Animal? targetedAnimal = null;
        HostileMob? targetedHostile = null;
        EndSlime? targetedBoss = null;
        TrueEndSlime? targetedTrueBoss = null;
        int targetedPlayerId = -1;
        float bestEntityDist = float.MaxValue;
        const float EntityAttackReach = 3.2f;

        // PvP: Проверка попадания по другим игрокам в сетевой игре
        if (GameClient.Active != null) {
            foreach (var rp in GameClient.Active.RemotePlayers) {
                if (rp.Dimension != session.World.Dimension) continue;
                var pMin = rp.Position - new Vector3(0.35f, 0.9f, 0.35f);
                var pMax = rp.Position + new Vector3(0.35f, 0.9f, 0.35f);
                if (RayAabb(Eye, Forward, pMin, pMax, out float pt) && pt < bestEntityDist && pt <= EntityAttackReach) {
                    bestEntityDist = pt;
                    targetedPlayerId = rp.Id;
                    targetedAnimal = null;
                    targetedHostile = null;
                    targetedBoss = null;
                    targetedTrueBoss = null;
                }
            }
        } else if (GameServer.Active != null) {
            foreach (var cl in GameServer.Active.Clients) {
                if (cl.Dimension != session.World.Dimension) continue;
                var pMin = cl.Position - new Vector3(0.35f, 0.9f, 0.35f);
                var pMax = cl.Position + new Vector3(0.35f, 0.9f, 0.35f);
                if (RayAabb(Eye, Forward, pMin, pMax, out float pt) && pt < bestEntityDist && pt <= EntityAttackReach) {
                    bestEntityDist = pt;
                    targetedPlayerId = cl.Id;
                    targetedAnimal = null;
                    targetedHostile = null;
                    targetedBoss = null;
                    targetedTrueBoss = null;
                }
            }
        }

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
                    targetedPlayerId = -1;
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
                    targetedTrueBoss = null;
                    targetedPlayerId = -1;
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
                    targetedTrueBoss = null;
                    targetedPlayerId = -1;
                }
            }
        }

        // Истинный босс Бездны (если жив)
        if (world.TrueVoidBoss is { Alive: true } tBoss) {
            var bMin = tBoss.Position - new Vector3(TrueEndSlime.HalfSizeXZ, TrueEndSlime.HalfSizeY, TrueEndSlime.HalfSizeXZ);
            var bMax = tBoss.Position + new Vector3(TrueEndSlime.HalfSizeXZ, TrueEndSlime.HalfSizeY, TrueEndSlime.HalfSizeXZ);
            if (RayAabb(Eye, Forward, bMin, bMax, out float tbt) && tbt < bestEntityDist && tbt <= EntityAttackReach + 1.0f) {
                var hitPoint = Eye + Forward * MathF.Max(0.1f, tbt - 0.05f);
                if (HostileMob.HasLineOfSight(world, Eye, hitPoint)) {
                    bestEntityDist = tbt;
                    targetedTrueBoss = tBoss;
                    targetedBoss = null;
                    targetedAnimal = null;
                    targetedHostile = null;
                    targetedPlayerId = -1;
                }
            }
        }

        // Метка замаха: ЛКМ всегда создаёт «свинг» (даже по воздуху) — для удара по эндер-кристаллу.
        SwingMarker = MathF.Max(0f, SwingMarker - dt);
        if (input.AttackPressed) {
            SwingMarker = AttackCooldown;
            int itemId = SelectedItem?.Id ?? 0;
            GameClient.Active?.SendAction(PlayerActionType.SwingArm, itemId);
            GameServer.Active?.BroadcastHostAction(PlayerActionType.SwingArm, itemId);
        }

        if (input.AttackPressed && (targetedPlayerId != -1 || targetedAnimal != null || targetedHostile != null || targetedBoss != null || targetedTrueBoss != null) && bestEntityDist <= EntityAttackReach + 1.0f) {
            BreakTarget = new Vec3i(int.MinValue, int.MinValue, int.MinValue);
            BreakProgress = 0f;
            BreakDuration = 0f;
            if (targetedPlayerId != -1) AttackRemotePlayer(targetedPlayerId, session);
            else if (targetedAnimal != null) AttackAnimal(world, session);
            else if (targetedHostile != null) AttackHostile(targetedHostile, world, session);
            else if (targetedBoss != null) AttackBoss(targetedBoss, world, session);
            else if (targetedTrueBoss != null) AttackTrueBoss(targetedTrueBoss, world, session);
        } else if ((input.AttackHeld || input.AttackPressed) && (targetedPlayerId == -1 && targetedAnimal == null && targetedHostile == null && targetedBoss == null && targetedTrueBoss == null)) {
            // 2. Ломание блоков (в Креативе мгновенно любые блоки, включая бедрок, в Выживании по времени)
            if (hasTarget && GameData.TryGetBlock(world.GetVoxel(hit).TypeId, out var targetBlock)) {
                if (session.GameMode == GameMode.Creative) {
                    BreakBlock(world, session, hit, targetBlock);
                    BreakProgress = 0f;
                    BreakDuration = 0f;
                    BreakTarget = new Vec3i(int.MinValue, int.MinValue, int.MinValue);
                } else if (!targetBlock.IsUnbreakable) {
                    if (hit != BreakTarget) {
                        if (BreakGraceTimer > 0f && BreakTarget.X != int.MinValue) {
                            BreakGraceTimer -= dt;
                        } else {
                            BreakTarget = hit;
                            BreakProgress = 0f;
                            BreakGraceTimer = 0.25f;
                            ushort tId = SelectedItem?.Id ?? 0;
                            if (!GameData.CanHarvestBlock(targetBlock, tId) && GameData.GetRequiredTier(targetBlock.Id) > 0) {
                                session.AddMessage("Внимание: нужен более прочный инструмент для добычи этого блока!");
                            }
                        }
                    } else {
                        BreakGraceTimer = 0.25f;
                    }

                    if (hit == BreakTarget || BreakGraceTimer > 0f) {
                        var targetBlk = hit == BreakTarget ? targetBlock : (GameData.TryGetBlock(world.GetVoxel(BreakTarget).TypeId, out var b) ? b : targetBlock);
                        float breakTime = GameData.GetMiningTime(targetBlk, SelectedItem);
                        BreakDuration = breakTime;
                        BreakProgress += dt;
                        if (BreakProgress >= breakTime) {
                            BreakBlock(world, session, BreakTarget, targetBlk);
                            BreakProgress = 0f;
                            BreakDuration = 0f;
                            BreakGraceTimer = 0f;
                            BreakTarget = new Vec3i(int.MinValue, int.MinValue, int.MinValue);
                        }
                    }
                }
            } else {
                if (BreakGraceTimer > 0f) {
                    BreakGraceTimer -= dt;
                } else {
                    BreakTarget = new Vec3i(int.MinValue, int.MinValue, int.MinValue);
                    BreakProgress = 0f;
                    BreakDuration = 0f;
                }
            }
        } else {
            if (BreakGraceTimer > 0f) {
                BreakGraceTimer -= dt;
            } else {
                BreakTarget = new Vec3i(int.MinValue, int.MinValue, int.MinValue);
                BreakProgress = 0f;
                BreakDuration = 0f;
            }
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

        // Поедание пищи с задержкой 1.6 сек при удержании ПКМ (как в Minecraft)
        var heldFoodItem = SelectedEntry?.Item.Definition;
        if (heldFoodItem != null && GameData.FoodValue.TryGetValue(heldFoodItem.Id, out float foodVal)) {
            bool canEatFood = Hunger < MaxHunger || heldFoodItem.Id == GameData.GoldenAppleItem.Id;
            if ((input.UseHeld || input.UsePressed) && canEatFood) {
                EatingTimer += dt;
                EatingSoundTimer += dt;
                var foodColor = GetFoodParticleColor(heldFoodItem);
                var eatPos = Eye - new Vector3(0f, 0.35f, 0f) + Forward * 0.35f;

                if (EatingSoundTimer >= 0.22f) {
                    EatingSoundTimer = 0f;
                    SoundSystem.PlayEat();
                    world.SpawnEatParticles(eatPos, foodColor, 4);
                }

                if (EatingTimer >= 1.6f) {
                    EatingTimer = 0f;
                    if (TryConsumeSelected(heldFoodItem, 1)) {
                        Hunger = MathF.Min(MaxHunger, Hunger + foodVal);
                        Saturation = MathF.Min(Hunger, Saturation + foodVal * 0.6f);
                        world.SpawnEatParticles(eatPos, foodColor, 10);
                        if (heldFoodItem.Id == GameData.GoldenAppleItem.Id) {
                            Health = MathF.Min(MaxHealth, Health + 4f);
                            InvulnerabilityTimer = 1.5f;
                            session.ShowTitle("ЗОЛОТОЕ ЯБЛОКО", "+4 HP и благословение защиты!", 2.5f, new Color(255, 215, 0, 255));
                        } else if (heldFoodItem.Id == GameData.RottenFleshItem.Id) {
                            if (Random.Shared.NextDouble() < 0.80) {
                                Exhaustion += 12.0f;
                                session.AddMessage("Несвежая пища вызвала приступ голода!");
                            }
                        } else if (heldFoodItem.Id == GameData.ChorusFruitItem.Id) {
                            TeleportRandomly(world);
                            session.ShowTitle("ПЛОД ХОРУСА", "Пространственное смещение!", 2.0f, new Color(210, 120, 255, 255));
                        }
                        SoundSystem.PlayEat();
                    }
                }
            } else {
                EatingTimer = 0f;
                EatingSoundTimer = 0f;
            }
        } else {
            EatingTimer = 0f;
            EatingSoundTimer = 0f;
        }

        // Использование: установка блока или быстрое взаимодействие.
        PlaceCooldown -= dt;
        bool wantUse = input.UsePressed || (input.UseHeld && PlaceCooldown <= 0f);
        if (wantUse) {
            PlaceCooldown = 0.25f;

            // Проверка кормления животных (Размножение / Режим любви)
            if (input.UsePressed) {
                var origin = Eye;
                var dir = Forward;
                Animal? bestAnimal = null;
                float bestAnimalDist = float.MaxValue;
                foreach (var a in world.Animals) {
                    if (!a.Alive) continue;
                    var min = a.Position - new Vector3(a.HalfSizeX, a.HalfSizeY, a.HalfSizeZ);
                    var max = a.Position + new Vector3(a.HalfSizeX, a.HalfSizeY, a.HalfSizeZ);
                    if (RayAabb(origin, dir, min, max, out float t) && t < bestAnimalDist) {
                        bestAnimalDist = t;
                        bestAnimal = a;
                    }
                }

                if (bestAnimal != null && bestAnimalDist <= 3.5f) {
                    var heldDef = SelectedEntry?.Item.Definition;
                    if (heldDef != null && bestAnimal.LikesFood(heldDef.Id)) {
                        if (!bestAnimal.IsBaby && bestAnimal.BreedCooldown <= 0f && bestAnimal.LoveTimer <= 0f) {
                            if (TryConsumeSelected(heldDef, 1, session)) {
                                bestAnimal.LoveTimer = 25f;
                                SoundSystem.PlayPop();
                                session.AddMessage("Животное довольно и ищет пару!");
                                wantUse = false;
                            }
                        } else if (bestAnimal.IsBaby) {
                            if (TryConsumeSelected(heldDef, 1, session)) {
                                bestAnimal.BabyAgeTimer -= 25f;
                                SoundSystem.PlayPop();
                                session.AddMessage("Детеныш растет быстрее!");
                                wantUse = false;
                            }
                        }
                    }
                }
            }

            // Проверка ПКМ на верстак / печку / сундук / кровать
            if (wantUse && input.UsePressed && session.HasTarget) {
                var targetVox = world.GetVoxel(session.TargetBlock);
                if (targetVox.TypeId == GameData.BWorkbench.Id) {
                    session.Ui = UiState.Workbench;
                    wantUse = false;
                } else if (targetVox.TypeId == GameData.BFurnace.Id) {
                    session.ActiveFurnacePos = session.TargetBlock;
                    session.Ui = UiState.Furnace;
                    wantUse = false;
                } else if (targetVox.TypeId == GameData.BVoidGate.Id) {
                    // Врата Бездны / Алтарь: вставить Ключ Бездны (только в измерении Бездны)
                    var heldVoidKey = SelectedEntry?.Item.Definition;
                    if (heldVoidKey != null && heldVoidKey.Id == GameData.VoidKeyItem.Id && session.World.Dimension == Dimension.Void) {
                        if (!world.VoidAltarTriggered && TryConsumeSelected(heldVoidKey, 1, session)) {
                            session.TriggerVoidAltarEncounter();
                            wantUse = false;
                        }
                    } else if (session.World.Dimension == Dimension.Void && !world.VoidAltarTriggered) {
                        session.AddMessage("Врата запечатаны древней силой. Нужен Ключ Бездны...");
                        wantUse = false;
                    }
                } else if (targetVox.TypeId == GameData.BChest.Id) {
                    session.ActiveChestPos = session.TargetBlock;
                    session.Ui = UiState.Chest;
                    SoundSystem.PlayChest();
                    wantUse = false;
                } else if (targetVox.TypeId == GameData.BJukebox.Id) {
                    wantUse = false;
                    bool hasDiscInside = targetVox.SubGridLayerMask == 1;
                    if (hasDiscInside) {
                        // Извлекаем пластинку из проигрывателя
                        targetVox.SubGridLayerMask = 0;
                        world.SetVoxelRaw(session.TargetBlock, in targetVox);
                        SoundSystem.StopDisc();
                        world.SpawnPickup(GameData.MusicDiscItem.Id, 1, session.TargetBlock);
                        session.ShowActionbar("§7♪ Воспроизведение пластинки остановлено ♪", 3.5f);
                    } else {
                        // Если в руках пластинка — вставляем её в проигрыватель
                        var held = SelectedEntry?.Item.Definition;
                        if (held != null && held.Id == GameData.MusicDiscItem.Id) {
                            if (session.GameMode == GameMode.Creative || TryConsumeSelected(held, 1, session)) {
                                targetVox.SubGridLayerMask = 1;
                                world.SetVoxelRaw(session.TargetBlock, in targetVox);
                                SoundSystem.PlayDisc("disc_circus");
                                world.SpawnCrit(new Vector3(session.TargetBlock.X + 0.5f, session.TargetBlock.Y + 1.1f, session.TargetBlock.Z + 0.5f), 10);
                                session.ShowActionbar("§e♪ Сейчас играет: The Amazing Digital Circus - The One Who's Running the Show [8-Bit Remix] ♪", 5.0f);
                            }
                        }
                    }
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
                    GameClient.Active?.SendBlockChange(lowerPos.X, lowerPos.Y, lowerPos.Z, lVox.TypeId, newMask, isBreak: false, (byte)session.World.Dimension);
                    GameClient.Active?.SendBlockChange(upperPos.X, upperPos.Y, upperPos.Z, uVox.TypeId, newMask, isBreak: false, (byte)session.World.Dimension);
                    GameServer.Active?.BroadcastHostBlockChange(lowerPos.X, lowerPos.Y, lowerPos.Z, lVox.TypeId, newMask, isBreak: false);
                    GameServer.Active?.BroadcastHostBlockChange(upperPos.X, upperPos.Y, upperPos.Z, uVox.TypeId, newMask, isBreak: false);
                    if (isOpen) SoundSystem.PlayDoorClose();
                    else SoundSystem.PlayDoorOpen();
                    wantUse = false;
                } else if (targetVox.TypeId == GameData.BBed.Id || targetVox.TypeId == GameData.BBedHead.Id) {
                    if (world.Dimension != Dimension.Overworld) {
                        // В Нижнем мире и в Энде сон вызывает энергетический взрыв!
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
                            DamageSelectedTool(session); // мотыга изнашивается при вспашке

                            wantUse = false;
                        }
                    }
                } else if (item.Id == GameData.WheatSeedsItem.Id || item.Id == GameData.CarrotItem.Id || item.Id == GameData.PotatoItem.Id) {
                    wantUse = false;
                    // Посадка семян пшеницы / моркови / картофеля на вспаханную грядку (Farmland)
                    if (session.HasTarget) {
                        var targetVox = world.GetVoxel(session.TargetBlock);
                        if (targetVox.TypeId == GameData.BFarmland.Id) {
                            var cropPos = session.TargetBlock + new Vec3i(0, 1, 0);
                            var cropVox = world.GetVoxel(cropPos);
                            if (cropVox.TypeId == 0) {
                                if (TryConsumeSelected(item, 1, session)) {
                                    var planted = item.Id == GameData.WheatSeedsItem.Id ? GameData.BWheatCrop :
                                                  item.Id == GameData.CarrotItem.Id ? GameData.BCarrotCrop : GameData.BPotatoCrop;
                                    world.PlacePlacedBlock(cropPos, planted, 0);
                                    SoundSystem.PlayDig(GameData.BGrass.Id);
                                }
                            }
                        }
                    }
                } else if (item.Id == GameData.OakSaplingItem.Id || item.Id == GameData.RedFlowerItem.Id || item.Id == GameData.YellowFlowerItem.Id) {
                    wantUse = false;
                    // Посадка саженца дуба / мака / одуванчика на траву или землю
                    if (session.HasTarget) {
                        var targetVox = world.GetVoxel(session.TargetBlock);
                        if (targetVox.TypeId == GameData.BGrass.Id || targetVox.TypeId == GameData.BDirt.Id) {
                            var plantPos = session.TargetBlock + new Vec3i(0, 1, 0);
                            var plantVox = world.GetVoxel(plantPos);
                            if (plantVox.TypeId == 0) {
                                if (TryConsumeSelected(item, 1, session)) {
                                    var planted = item.Id == GameData.OakSaplingItem.Id ? GameData.BSapling :
                                                  item.Id == GameData.RedFlowerItem.Id ? GameData.BRedFlower : GameData.BYellowFlower;
                                    world.PlacePlacedBlock(plantPos, planted, 0);
                                    SoundSystem.PlayDig(GameData.BGrass.Id);
                                }
                            }
                        }
                    }
                } else if (item.Id == GameData.BoneMealItem.Id) {
                    wantUse = false;
                    if (session.HasTarget) {
                        var targetVox = world.GetVoxel(session.TargetBlock);
                        if (targetVox.TypeId == GameData.BWheatCrop.Id || targetVox.TypeId == GameData.BCarrotCrop.Id || targetVox.TypeId == GameData.BPotatoCrop.Id) {
                            int curStage = targetVox.SubGridLayerMask; // 0..3
                            if (curStage < 3) {
                                if (TryConsumeSelected(item, 1, session)) {
                                    byte nextStage = (byte)Math.Min(3, curStage + 1 + Random.Shared.Next(2));
                                    var blk = targetVox.TypeId == GameData.BWheatCrop.Id ? GameData.BWheatCrop :
                                              targetVox.TypeId == GameData.BCarrotCrop.Id ? GameData.BCarrotCrop : GameData.BPotatoCrop;
                                    world.PlacePlacedBlock(session.TargetBlock, blk, nextStage);
                                    SoundSystem.PlayFertilize();
                                    session.AddMessage("Культура удобрена и подросла!");
                                }
                            }
                        } else if (targetVox.TypeId == GameData.BSapling.Id) {
                            if (TryConsumeSelected(item, 1, session)) {
                                SoundSystem.PlayFertilize();
                                world.GrowTree(session.TargetBlock);
                                session.AddMessage("Саженец вырос в дерево!");
                            }
                        } else if (targetVox.TypeId == GameData.BGrass.Id) {
                            if (TryConsumeSelected(item, 1, session)) {
                                SoundSystem.PlayFertilize();
                                for (int dx = -2; dx <= 2; dx++) {
                                    for (int dz = -2; dz <= 2; dz++) {
                                        if (Math.Abs(dx) + Math.Abs(dz) > 3) continue;
                                        var posBelow = session.TargetBlock + new Vec3i(dx, 0, dz);
                                        var posAbove = posBelow + new Vec3i(0, 1, 0);
                                        if (world.GetVoxel(posBelow).TypeId == GameData.BGrass.Id && world.GetVoxel(posAbove).TypeId == 0) {
                                            double roll = Random.Shared.NextDouble();
                                            if (roll < 0.45) {
                                                world.PlacePlacedBlock(posAbove, GameData.BTallGrass, 0);
                                            } else if (roll < 0.65) {
                                                world.PlacePlacedBlock(posAbove, GameData.BRedFlower, 0);
                                            } else if (roll < 0.85) {
                                                world.PlacePlacedBlock(posAbove, GameData.BYellowFlower, 0);
                                            }
                                        }
                                    }
                                }
                                session.AddMessage("Поляна расцвела густой травой и цветами!");
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
                                    world.PlacePlacedBlock(placeCell, GameData.BFire);
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
                        bool isFull = false; // набрать можно только полный (источниковый) блок

                        if (session.HasTarget) {
                            var tv = world.GetVoxel(session.TargetBlock);
                            if (tv.TypeId == GameData.BWater.Id || tv.TypeId == GameData.BLava.Id) {
                                fluidPos = session.TargetBlock;
                                foundFluid = true;
                                isWater = tv.TypeId == GameData.BWater.Id;
                                isFull = tv.SubGridLayerMask == 0;
                            } else {
                                var pv = world.GetVoxel(session.PlaceCell);
                                if (pv.TypeId == GameData.BWater.Id || pv.TypeId == GameData.BLava.Id) {
                                    fluidPos = session.PlaceCell;
                                    foundFluid = true;
                                    isWater = pv.TypeId == GameData.BWater.Id;
                                    isFull = pv.SubGridLayerMask == 0;
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
                                    isFull = sv.SubGridLayerMask == 0;
                                    break;
                                }
                            }
                        }

                        if (foundFluid) {
                            if (!isFull) {
                                // Проточная/падающая жидкость не набирается — нужен полный блок
                                session.AddMessage(isWater ? "Нужен полный блок воды!" : "Нужен полный блок лавы!");
                            } else {
                                world.RemoveBlock(fluidPos);
                                SoundSystem.PlaySplash();

                                if (session.GameMode != GameMode.Creative) {
                                    Inventory.RemoveAt(SelectedSlot);
                                    Inventory.InsertAt(SelectedSlot, new ItemEntry(GameData.NewItem(isWater ? GameData.WaterBucketItem : GameData.LavaBucketItem), 1));
                                }
                                session.AddMessage(isWater ? "Ведро наполнено водой" : "Ведро наполнено лавой");
                            }
                            wantUse = false;
                        }
                    } else if (item.Id == GameData.WaterBucketItem.Id || item.Id == GameData.LavaBucketItem.Id) {
                        if (world.Dimension != Dimension.Overworld) {
                            // В Энде и Нижнем мире жидкости из вёдер не выливаются (иначе каскад в пустоте)
                            session.AddMessage("В этом измерении нельзя выливать жидкости из ведра.");
                            wantUse = false;
                        } else {
                            Vec3i placeAt = session.HasTarget ? session.PlaceCell : new Vec3i((int)MathF.Floor(eye.X + Forward.X * 2f), (int)MathF.Floor(eye.Y + Forward.Y * 2f), (int)MathF.Floor(eye.Z + Forward.Z * 2f));
                            if (session.HasTarget && (world.GetVoxel(session.TargetBlock).TypeId == 0 || !world.IsSolidAt(session.TargetBlock))) placeAt = session.TargetBlock;
                            world.PlacePlacedBlock(placeAt, item.Id == GameData.WaterBucketItem.Id ? GameData.BWater : GameData.BLava, 0);
                            SoundSystem.PlaySplash();
                            if (session.GameMode != GameMode.Creative) {
                                Inventory.RemoveAt(SelectedSlot);
                                Inventory.InsertAt(SelectedSlot, new ItemEntry(GameData.NewItem(GameData.BucketItem), 1));
                            }
                            session.AddMessage(item.Id == GameData.WaterBucketItem.Id ? "Вода вылита из ведра" : "Лава вылита из ведра");
                            wantUse = false;
                        }
                    } else if (GameData.FoodValue.ContainsKey(item.Id)) {
                        wantUse = false;
                    } else if (item.Id == GameData.EyeOfEnderItem.Id) {
                        // Око Эндера: ПКМ по рамке — вставка глаза; ПКМ по воздуху/не-рамке — бросок к крепости
                        wantUse = false;
                        if (input.UsePressed) {
                            bool isFrame = session.HasTarget &&
                                           world.GetVoxel(session.TargetBlock).TypeId == GameData.BEndPortalFrame.Id;
                            if (isFrame) {
                                var targetVox = world.GetVoxel(session.TargetBlock);
                                if ((targetVox.SubGridLayerMask & 1) == 0) {
                                    if (TryConsumeSelected(item, 1)) {
                                        targetVox.SubGridLayerMask |= 1;
                                        world.SetVoxelRaw(session.TargetBlock, in targetVox);
                                        SoundSystem.PlayPlace();
                                        if (TryActivateEndPortal(world, session.TargetBlock)) {
                                            session.AddMessage("Портал в Энд открыт!");
                                            SoundSystem.PlayThunder();
                                        }
                                        // Без лишнего сообщения «вставлено в рамку» — глаз и так виден.
                                    }
                                }
                                // Без сообщения «уже содержит око» — это заметно и без подсказки.
                            } else {
                                // Бросок Ока Эндера в сторону ближайшей крепости Энда
                                if (session.GameMode == GameMode.Creative || TryConsumeSelected(item, 1)) {
                                    ThrowEyeOfEnder(world, session);
                                }
                            }
                        }
                    } else if (item.Id == GameData.EnderPearlItem.Id) {
                        // Жемчуг Эндера: ПКМ — бросить, при приземлении телепортирует игрока
                        wantUse = false;
                        if (input.UsePressed) {
                            if (session.GameMode == GameMode.Creative || TryConsumeSelected(item, 1)) {
                                ThrowEnderPearl(world, session);
                            }
                        }
                    } else if (item.Id == GameData.NetherTotemItem.Id) {
                        // Тотем Пламени: ПКМ — призыв Владыки Незера в Аду
                        wantUse = false;
                        if (input.UsePressed) {
                            if (world.Dimension == Dimension.Nether) {
                                if (session.GameMode == GameMode.Creative || TryConsumeSelected(item, 1)) {
                                    var spawnPos = Position + Forward * 5.0f + new Vector3(0f, 1.0f, 0f);
                                    world.HostileMobs.Add(new HostileMob(HostileType.NetherLord, spawnPos));
                                    world.NetherBossSpawned = true;
                                    SoundSystem.PlayThunder();
                                    SoundSystem.PlayBabakherHiss();
                                    world.SpawnCrit(spawnPos, 40);
                                    session.AddMessage("§cТотем Пламени вспыхивает! Владыка Незера восстаёт из адского пламени!");
                                }
                            } else {
                                session.AddMessage("§cТотем Пламени можно использовать только в Нижнем мире (Незере)!");
                            }
                        }
                    } else if (item.Id == GameData.DesertTotemItem.Id) {
                        // Тотем Песков: ПКМ — призыв Стража Пустыни в Пустыне
                        wantUse = false;
                        if (input.UsePressed) {
                            var b = world.Generator.GetBiome((int)Position.X, 50, (int)Position.Z);
                            if (b == BiomeType.Desert) {
                                if (session.GameMode == GameMode.Creative || TryConsumeSelected(item, 1)) {
                                    var spawnPos = Position + Forward * 5.0f + new Vector3(0f, 1.0f, 0f);
                                    world.HostileMobs.Add(new HostileMob(HostileType.DesertGuardian, spawnPos));
                                    world.DesertBossSpawned = true;
                                    SoundSystem.PlayThunder();
                                    SoundSystem.PlayBabakherHiss();
                                    world.SpawnCrit(spawnPos, 40);
                                    session.AddMessage("§eТотем Песков пробуждает древнюю силу! Страж Пустыни восстаёт из барханов!");
                                }
                            } else {
                                session.AddMessage("§eТотем Песков можно использовать только в биоме Пустыни!");
                            }
                        }
                    } else if (item.Id == GameData.SwampTotemItem.Id) {
                        // Тотем Топей: ПКМ — призыв Болотного Стража в Болоте
                        wantUse = false;
                        if (input.UsePressed) {
                            var b = world.Generator.GetBiome((int)Position.X, 50, (int)Position.Z);
                            if (b == BiomeType.Swamp) {
                                if (session.GameMode == GameMode.Creative || TryConsumeSelected(item, 1)) {
                                    var spawnPos = Position + Forward * 5.0f + new Vector3(0f, 1.0f, 0f);
                                    world.HostileMobs.Add(new HostileMob(HostileType.SwampGuardian, spawnPos));
                                    world.SwampBossSpawned = true;
                                    SoundSystem.PlayThunder();
                                    SoundSystem.PlayBabakherHiss();
                                    world.SpawnCrit(spawnPos, 40);
                                    session.AddMessage("§2Тотем Топей взывает к тьме! Болотный Страж восстаёт из трясины!");
                                }
                            } else {
                                session.AddMessage("§2Тотем Топей можно использовать только в биоме Болота!");
                            }
                        }
                    } else if (item.Id == GameData.MusicDiscItem.Id) {
                        wantUse = false;
                        if (input.UsePressed) {
                            session.AddMessage("§7Для воспроизведения пластинки нужен проигрыватель!");
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
        bool inNetherPortal = IsInAnyPortal(world, feetCell, GameData.BNetherPortal.Id);
        bool inEndPortal = IsInAnyPortal(world, feetCell, GameData.BEndPortal.Id);

        if (inNetherPortal || inEndPortal) {
            if (PortalLocked) {
                // Мы в портале назначения: не телепортируем обратно, пока игрок стоит в нём.
            } else if (PortalTimer >= 0f) {
                PortalTimer += dt;
                if (PortalTimer >= 0.5f) {
                    Dimension target = inEndPortal
                        ? (session.World.Dimension == Dimension.Overworld ? Dimension.End : Dimension.Overworld)
                        : (session.World.Dimension == Dimension.Overworld ? Dimension.Nether : Dimension.Overworld);
                    session.SwitchDimension(target);
                    PortalTimer = -2.0f; // Задержка перед повторным входом
                    PortalLocked = true; // Пока игрок не покинет порталы — не пускаем обратно
                }
            } else {
                PortalTimer += dt;
            }
        } else {
            PortalTimer = MathF.Max(0f, PortalTimer - dt * 2f);
            PortalLocked = false;       // Игрок вышел из портала — снимаем блокировку
        }

        TickVitals(dt, session);
    }

    /// <summary>
    /// Проверяет, стоит ли игрок внутри портала: сканируем 3×3 по X/Z на уровне ног и глаз.
    /// Так вход срабатывает даже без идеальной центровки относительно проёма.
    /// </summary>
    private static bool IsInAnyPortal(GameWorld world, Vec3i feetCell, ushort portalId) {
        for (int dy = 0; dy <= 1; dy++) {
            int y = feetCell.Y + dy;
            for (int dx = -1; dx <= 1; dx++) {
                for (int dz = -1; dz <= 1; dz++) {
                    if (world.GetVoxel(new Vec3i(feetCell.X + dx, y, feetCell.Z + dz)).TypeId == portalId) {
                        return true;
                    }
                }
            }
        }
        return false;
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
                Exhaustion += 3.0f;
            }
        } else if (Hunger >= 18f && Health < MaxHealth) {
            HealthRegenTimer += dt;
            if (HealthRegenTimer >= 4.0f) {
                HealthRegenTimer = 0f;
                Health = MathF.Min(MaxHealth, Health + 1f);
                Exhaustion += 3.0f;
            }
        } else {
            HealthRegenTimer = 0f;
        }

        // Урон от голода при Hunger <= 0
        if (Hunger <= 0f) {
            StarveTimer += dt;
            if (StarveTimer >= 4.0f) {
                StarveTimer = 0f;
                ApplyDamage(1f, session, cause: "Истощение от голода");
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
        if (block.Id == GameData.BJukebox.Id) {
            SoundSystem.StopDisc();
        }
        world.RemoveBlock(pos);
        SoundSystem.PlayDig(block.Id);
        GameClient.Active?.SendBlockChange(pos.X, pos.Y, pos.Z, 0, 0, isBreak: true, (byte)session.World.Dimension);
        GameServer.Active?.BroadcastHostBlockChange(pos.X, pos.Y, pos.Z, 0, 0, isBreak: true);

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

        // Износ инструмента при ломании блока в Выживании
        DamageSelectedTool(session);

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
            } else if (block.Id == GameData.BCarrotCrop.Id) {
                int stage = oldVox.SubGridLayerMask;
                if (stage >= 3) {
                    int count = DropRng.Next(2, 5); // 2..4 моркови
                    world.SpawnPickup(GameData.CarrotItem.Id, count, pos);
                } else {
                    world.SpawnPickup(GameData.CarrotItem.Id, 1, pos);
                }
            } else if (block.Id == GameData.BPotatoCrop.Id) {
                int stage = oldVox.SubGridLayerMask;
                if (stage >= 3) {
                    int count = DropRng.Next(2, 5); // 2..4 картофеля
                    world.SpawnPickup(GameData.PotatoItem.Id, count, pos);
                } else {
                    world.SpawnPickup(GameData.PotatoItem.Id, 1, pos);
                }
            } else if (block.Id == GameData.BTallGrass.Id) {
                double roll = DropRng.NextDouble();
                if (roll < 0.25) {
                    world.SpawnPickup(GameData.WheatSeedsItem.Id, 1, pos);
                } else if (roll < 0.33) {
                    world.SpawnPickup(GameData.CarrotItem.Id, 1, pos);
                } else if (roll < 0.41) {
                    world.SpawnPickup(GameData.PotatoItem.Id, 1, pos);
                }
            } else if (block.Id == GameData.BLeaves.Id) {
                double roll = DropRng.NextDouble();
                if (roll < 0.15) {
                    world.SpawnPickup(GameData.OakSaplingItem.Id, 1, pos); // Саженец дуба (15%)!
                } else if (roll < 0.20) {
                    world.SpawnPickup(GameData.AppleItem.Id, 1, pos);
                } else if (roll < 0.35) {
                    world.SpawnPickup(GameData.StickItem.Id, 1, pos);
                }
            } else if (block.DropItemId != 0 && GameData.Items.TryGetValue(block.DropItemId, out var drop)) {
                if (block.Id == GameData.BGravel.Id && DropRng.NextDouble() < 0.25) {
                    world.SpawnPickup(GameData.FlintItem.Id, 1, pos);
                } else {
                    world.SpawnPickup(drop.Id, dropCount, pos);
                }
                // При рубке бревна иногда высыпаются древесные опилки
                if (block.Id == GameData.BLog.Id && DropRng.NextDouble() < 0.35) {
                    world.SpawnPickup(GameData.SawdustItem.Id, 1, pos);
                }
            }

            // При разрушении проигрывателя с пластинкой — извлекаем пластинку и останавливаем трек
            if (block.Id == GameData.BJukebox.Id && oldVox.SubGridLayerMask == 1) {
                world.SpawnPickup(GameData.MusicDiscItem.Id, 1, pos);
                SoundSystem.StopDisc();
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

    /// <summary>Снимает 1 прочность с инструмента/оружия в выбранном слоте; ломает при нуле.</summary>
    private void DamageSelectedTool(GameSession session) {
        if (session.GameMode == GameMode.Creative) return;
        var entry = Inventory.Slots[SelectedSlot];
        if (entry == null) return;
        var def = entry.Value.Item.Definition;
        if (GameData.GetToolTier(def.Id) <= 0) return; // не инструмент
        int dur = entry.Value.Item.Durability - 1;
        if (dur <= 0) {
            Inventory.RemoveAt(SelectedSlot);
            session.AddMessage($"Инструмент «{def.Name}» сломался!");
            SoundSystem.PlayBreakTool();
        } else {
            entry.Value.Item.Durability = dur;
        }
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
        var pmin = Position - HalfExtents;
        var pmax = Position + HalfExtents;

        // Если блок твердый (или кровать, или дверь) — проверяем, чтобы он не ставился внутрь игрока
        bool isSolidPlacement = block.IsSolid || block.Id == GameData.BBed.Id || item.Id == GameData.DoorItem.Id;
        if (isSolidPlacement) {
            var min = new Vector3(cell.X, cell.Y, cell.Z);
            var max = new Vector3(cell.X + 1f, cell.Y + 1f, cell.Z + 1f);
            if (min.X < pmax.X && max.X > pmin.X && min.Y < pmax.Y && max.Y > pmin.Y && min.Z < pmax.Z && max.Z > pmin.Z)
                return false;
        }

        // Вычисляем ориентацию блока (facing: 0..3) по направлению взгляда игрока
        byte facing = 0;
        Vec3i forwardH;
        if (MathF.Abs(Forward.X) > MathF.Abs(Forward.Z)) {
            if (Forward.X > 0) { facing = 3; forwardH = new Vec3i(1, 0, 0); }
            else { facing = 1; forwardH = new Vec3i(-1, 0, 0); }
        } else {
            if (Forward.Z > 0) { facing = 2; forwardH = new Vec3i(0, 0, 1); }
            else { facing = 0; forwardH = new Vec3i(0, 0, -1); }
        }

        // Специальная установка 2-блочной двери (низ + верх)
        if (item.Id == GameData.DoorItem.Id) {
            var above = cell + new Vec3i(0, 1, 0);
            if (world.IsSolidAt(above) || world.GetVoxel(above).TypeId != 0) return false;
            if (!world.IsSolidAt(cell + new Vec3i(0, -1, 0))) return false;

            // Проверка коллизии верхней половины двери с игроком
            var aboveMin = new Vector3(above.X, above.Y, above.Z);
            var aboveMax = new Vector3(above.X + 1f, above.Y + 1f, above.Z + 1f);
            if (aboveMin.X < pmax.X && aboveMax.X > pmin.X && aboveMin.Y < pmax.Y && aboveMax.Y > pmin.Y && aboveMin.Z < pmax.Z && aboveMax.Z > pmin.Z)
                return false;

            if (TryConsumeSelected(item, 1, session)) {
                world.PlacePlacedBlock(cell, GameData.BDoorLower, facing);
                world.PlacePlacedBlock(above, GameData.BDoorUpper, facing);
                SoundSystem.PlayPlace();
                GameClient.Active?.SendBlockChange(cell.X, cell.Y, cell.Z, GameData.BDoorLower.Id, facing, isBreak: false, (byte)session.World.Dimension);
                GameClient.Active?.SendBlockChange(above.X, above.Y, above.Z, GameData.BDoorUpper.Id, facing, isBreak: false, (byte)session.World.Dimension);
                GameServer.Active?.BroadcastHostBlockChange(cell.X, cell.Y, cell.Z, GameData.BDoorLower.Id, facing, isBreak: false);
                GameServer.Active?.BroadcastHostBlockChange(above.X, above.Y, above.Z, GameData.BDoorUpper.Id, facing, isBreak: false);
                return true;
            }
            return false;
        }

        // Специальная установка 2-блочной кровати (изножье + изголовье)
        if (block.Id == GameData.BBed.Id) {
            // Кровать ставится только на верхнюю грань блока (горизонтально)
            if (session.TargetBlock.Y >= cell.Y) return false;

            var headCell = cell + forwardH;
            var exFoot = world.GetVoxel(cell);
            var exHead = world.GetVoxel(headCell);
            // Обе клетки кровати должны быть свободным воздухом
            if (exFoot.TypeId != 0 || exHead.TypeId != 0)
                return false;
            // Оба опорных блока снизу должны быть твёрдыми
            if (!world.IsSolidAt(cell + new Vec3i(0, -1, 0)) || !world.IsSolidAt(headCell + new Vec3i(0, -1, 0)))
                return false;

            // Проверка коллизии изголовья кровати с игроком
            var headMin = new Vector3(headCell.X, headCell.Y, headCell.Z);
            var headMax = new Vector3(headCell.X + 1f, headCell.Y + 1f, headCell.Z + 1f);
            if (headMin.X < pmax.X && headMax.X > pmin.X && headMin.Y < pmax.Y && headMax.Y > pmin.Y && headMin.Z < pmax.Z && headMax.Z > pmin.Z)
                return false;

            if (TryConsumeSelected(item, 1, session)) {
                world.PlacePlacedBlock(cell, GameData.BBed, facing);
                world.PlacePlacedBlock(headCell, GameData.BBedHead, facing);
                SoundSystem.PlayPlace();
                GameClient.Active?.SendBlockChange(cell.X, cell.Y, cell.Z, GameData.BBed.Id, facing, isBreak: false, (byte)session.World.Dimension);
                GameClient.Active?.SendBlockChange(headCell.X, headCell.Y, headCell.Z, GameData.BBedHead.Id, facing, isBreak: false, (byte)session.World.Dimension);
                GameServer.Active?.BroadcastHostBlockChange(cell.X, cell.Y, cell.Z, GameData.BBed.Id, facing, isBreak: false);
                GameServer.Active?.BroadcastHostBlockChange(headCell.X, headCell.Y, headCell.Z, GameData.BBedHead.Id, facing, isBreak: false);
                return true;
            }
            return false;
        }

        // 3D Факел: установка на пол (facing=0) или на стену (facing=1..4)
        if (block.Id == GameData.BTorch.Id) {
            var hitNormal = cell - session.TargetBlock;
            byte torchFacing = 0;
            if (hitNormal.X == 1) torchFacing = 2;      // Стена на -X от факела (клик по правой грани)
            else if (hitNormal.X == -1) torchFacing = 1; // Стена на +X от факела (клик по левой грани)
            else if (hitNormal.Z == 1) torchFacing = 4;  // Стена на -Z от факела (клик по дальней грани)
            else if (hitNormal.Z == -1) torchFacing = 3; // Стена на +Z от факела (клик по ближней грани)

            if (TryConsumeSelected(item, 1, session)) {
                world.PlacePlacedBlock(cell, block, torchFacing);
                SoundSystem.PlayPlace();
                GameClient.Active?.SendBlockChange(cell.X, cell.Y, cell.Z, block.Id, torchFacing, isBreak: false, (byte)session.World.Dimension);
                GameServer.Active?.BroadcastHostBlockChange(cell.X, cell.Y, cell.Z, block.Id, torchFacing, isBreak: false);
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

        // Рамка портала Энда: бит 0 (SubGridLayerMask & 1) — это «глаз», его нельзя
        // ставить при установке блока, иначе в свежей рамке «сам появится глаз».
        if (block.Id == GameData.BEndPortalFrame.Id) facing &= 0xFE;

        if (TryConsumeSelected(item, 1, session)) {
            world.PlacePlacedBlock(cell, block, facing);
            SoundSystem.PlayPlace();
            GameClient.Active?.SendBlockChange(cell.X, cell.Y, cell.Z, block.Id, facing, isBreak: false, (byte)session.World.Dimension);
            GameServer.Active?.BroadcastHostBlockChange(cell.X, cell.Y, cell.Z, block.Id, facing, isBreak: false);
            return true;
        }
        return false;
    }

    // ── Бой ──────────────────────────────────────────────────────────────────

    public void AttackRemotePlayer(int targetId, GameSession session) {
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
        }

        if (GameClient.Active != null) {
            GameClient.Active.SendHit(targetId, dmg);
        } else if (GameServer.Active != null) {
            GameServer.Active.BroadcastHostHit(targetId, dmg);
        }

        if (isStrong) SoundSystem.PlayStrongAttack();
        else SoundSystem.PlayWeakAttack();
    }

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
        DamageSelectedTool(session); // оружие изнашивается при ударе по животному
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

        // Круговой срез мечом (Sweeping Edge) при полном заряде атаки на земле
        bool isSword = toolId == GameData.WoodSwordItem.Id || toolId == GameData.StoneSwordItem.Id ||
                       toolId == GameData.IronSwordItem.Id || toolId == GameData.GoldSwordItem.Id ||
                       toolId == GameData.DiamondSwordItem.Id;
        if (isSword && isStrong && OnGround && !isCrit) {
            float sweepRadius = 2.8f;
            foreach (var other in world.HostileMobs) {
                if (other == mob || other.Health <= 0f) continue;
                float d = Vector3.Distance(Position, other.Position);
                if (d <= sweepRadius) {
                    var toOther = Vector3.Normalize(other.Position - Position);
                    float dot = Vector3.Dot(Forward, toOther);
                    if (dot > 0.25f) { // В конусе перед игроком
                        other.Health -= dmg * 0.5f;
                        other.HurtTime = 0.35f;
                        var sPush = Vector2.Normalize(new Vector2(toOther.X, toOther.Z));
                        other.Velocity += new Vector3(sPush.X * 4.0f, 1.5f, sPush.Y * 4.0f);
                        world.SpawnCrit(other.Position + new Vector3(0f, 0.6f, 0f), 5);
                        if (other.Health <= 0f) other.Die(world, session);
                    }
                }
            }
            world.SpawnCrit(Position + Forward * 1.5f + Vector3.UnitY * 0.6f, 8);
        }

        if (mob.Health <= 0f) {
            mob.Die(world, session);
        }
        DamageSelectedTool(session); // оружие изнашивается при ударе по мобу
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
        DamageSelectedTool(session); // оружие изнашивается при ударе по боссу
    }

    public void AttackTrueBoss(TrueEndSlime boss, GameWorld world, GameSession session) {
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
            session.AddMessage("Критический удар по Истинному Слизню! ×1.5");
            world.SpawnCrit(boss.Position + new Vector3(0f, TrueEndSlime.HalfSizeY, 0f), 22);
        }

        boss.TakeDamage(dmg, world, session);

        if (isStrong) SoundSystem.PlayStrongAttack();
        else SoundSystem.PlayWeakAttack();
        DamageSelectedTool(session);
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
    /// <summary>Цвет частиц поедания под конкретный вид еды.</summary>
    public static Color GetFoodParticleColor(ItemDefinition item) => item.Id switch {
        var id when id == GameData.AppleItem.Id => new Color(220, 35, 35, 255),
        var id when id == GameData.GoldenAppleItem.Id => new Color(255, 215, 30, 255),
        var id when id == GameData.BreadItem.Id => new Color(196, 150, 70, 255),
        var id when id == GameData.RawPorkItem.Id => new Color(230, 140, 140, 255),
        var id when id == GameData.CookedPorkItem.Id => new Color(160, 95, 60, 255),
        var id when id == GameData.RawBeefItem.Id => new Color(190, 45, 45, 255),
        var id when id == GameData.CookedBeefItem.Id => new Color(115, 60, 35, 255),
        var id when id == GameData.RawMuttonItem.Id => new Color(215, 65, 75, 255),
        var id when id == GameData.CookedMuttonItem.Id => new Color(145, 75, 40, 255),
        var id when id == GameData.RawChickenItem.Id => new Color(235, 175, 160, 255),
        var id when id == GameData.CookedChickenItem.Id => new Color(185, 110, 40, 255),
        var id when id == GameData.CarrotItem.Id => new Color(245, 120, 20, 255),
        var id when id == GameData.PotatoItem.Id => new Color(200, 165, 95, 255),
        var id when id == GameData.BakedPotatoItem.Id => new Color(185, 120, 50, 255),
        var id when id == GameData.RottenFleshItem.Id => new Color(125, 85, 40, 255),
        var id when id == GameData.ChorusFruitItem.Id => new Color(185, 100, 210, 255),
        var id when id == GameData.SawdustPorridgeItem.Id => new Color(210, 170, 95, 255),
        _ => new Color(200, 170, 100, 255)
    };
}
