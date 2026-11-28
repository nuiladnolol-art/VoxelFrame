using System.Numerics;
using Raylib_cs;
using Voxalith.Core;
using Voxalith.Core.Inventory;
using Voxalith.Core.World;

namespace Voxalith.Game;
public sealed partial class Player {
    private void UpdateMovement(float dt, in PlayerInput input, GameWorld world, GameSession session, bool isFrozen) {
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

        float feetY = Position.Y - HalfExtents.Y + 0.05f;
        var feetCell = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(feetY), (int)MathF.Floor(Position.Z));
        var feetBlock = world.GetVoxel(feetCell);
        var waistVoxel = world.GetVoxel(new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y), (int)MathF.Floor(Position.Z)));
        var eyeVoxel = world.GetVoxel(new Vec3i((int)MathF.Floor(Eye.X), (int)MathF.Floor(Eye.Y), (int)MathF.Floor(Eye.Z)));
        bool inWeb = feetBlock.TypeId == GameData.BWeb.Id || waistVoxel.TypeId == GameData.BWeb.Id || eyeVoxel.TypeId == GameData.BWeb.Id;
        bool inWater = feetBlock.TypeId == GameData.BWater.Id || waistVoxel.TypeId == GameData.BWater.Id || eyeVoxel.TypeId == GameData.BWater.Id;
        bool inLava = feetBlock.TypeId == GameData.BLava.Id || waistVoxel.TypeId == GameData.BLava.Id || eyeVoxel.TypeId == GameData.BLava.Id;
        bool inFire = world.Fire.Burning.ContainsKey(feetCell) || world.Fire.Campfires.Contains(feetCell);

        if (inFire && !inWater && session.GameMode != GameMode.Creative) {
            FireTicks = MathF.Max(FireTicks, 5.0f);
        }

        float sinYaw = MathF.Sin(Yaw);
        float cosYaw = MathF.Cos(Yaw);
        var forwardH = new Vector3(sinYaw, 0f, cosYaw);
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
            if (!_wasInWater) {
                SoundSystem.PlaySplashAt(Position);
                world.SpawnSplash(Position + new Vector3(0f, 0.1f, 0f), 12);
            }
            FireTicks = 0f; // Вода мгновенно тушит огонь
            speed *= 0.65f;
            float targetVx = wish.X * speed;
            float targetVz = wish.Z * speed;
            Velocity.X += (targetVx - Velocity.X) * MathF.Min(1f, 10f * dt);
            Velocity.Z += (targetVz - Velocity.Z) * MathF.Min(1f, 10f * dt);
            if (inJump) {
                bool nearSurface = eyeVoxel.TypeId != GameData.BWater.Id;
                Velocity.Y = nearSurface ? JumpSpeed * 1.05f : 3.8f;
                world.SpawnSplash(Position + new Vector3(0f, 0.2f, 0f), 2);
            } else {
                Velocity.Y -= 5f * dt;
                if (Velocity.Y < -2.5f) Velocity.Y = -2.5f; // мягкое погружение без камня на дно
            }
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
            bool canSneak = inCrouch && (OnGround || Collision.HasGroundBelow(world, Position, HalfExtents, false));
            OnGround = Collision.Move(world, ref Position, HalfExtents, ref Velocity, dt, canSneak);
        }

        if (OnGround && Velocity.Y < 0f) Velocity.Y = 0f;

        _wasInWater = inWater;

        // Пыль при приземлении и предотвращение случайного вытаптывания грядок
        if (!wasOnGround && OnGround && !inWater && !inLava) {
            var groundCell = new Vec3i(feetCell.X, feetCell.Y - 1, feetCell.Z);
            var groundBlock = world.GetVoxel(groundCell);
            ushort landBlockId = groundBlock.TypeId != 0 ? groundBlock.TypeId : feetBlock.TypeId;
            world.SpawnBlockDust(Position - new Vector3(0f, HalfExtents.Y, 0f), landBlockId, 6);
            world.SpawnDust(Position - new Vector3(0f, HalfExtents.Y, 0f), 3);

            // Вытаптывание грядки ТОЛЬКО при падении с высоты > 1.2м (обычная ходьба, бег и присед безопасны)
            float landFallDist = HighestYInAir - Position.Y;
            if (groundBlock.TypeId == GameData.BFarmland.Id && landFallDist > 1.2f) {
                world.SetBlock(groundCell, GameData.BDirt.Id);
                SoundSystem.PlayDig(GameData.BDirt.Id);
                var cropPos = new Vec3i(feetCell.X, feetCell.Y, feetCell.Z);
                var cropVox = world.GetVoxel(cropPos);
                if (cropVox.TypeId == GameData.BWheatCrop.Id || cropVox.TypeId == GameData.BCarrotCrop.Id || cropVox.TypeId == GameData.BPotatoCrop.Id) {
                    var cBlk = GameData.GetBlock(cropVox.TypeId);
                    world.RemoveBlock(cropPos);
                    world.SpawnPickup(cBlk.DropItemId != 0 ? cBlk.DropItemId : GameData.WheatSeedsItem.Id, 1, cropPos);
                }
            }
        }

        // Звуки шагов по типу материала и пыль при спринте.
        // Условие — по нажатым клавишам (а не по скорости): если игрок отпустил движение,
        // шаги прекращаются сразу, даже если скорость ещё не обнулилась до нуля.
        bool stepMoving = input.MoveX != 0f || input.MoveZ != 0f;
        if (inWater && stepMoving) {
            StepSoundTimer -= dt * 1.0f;
            if (StepSoundTimer <= 0f) {
                StepSoundTimer = 0.42f;
                SoundSystem.PlayStep(GameData.BWater.Id);
                world.SpawnSplash(Position + new Vector3(0f, 0.1f, 0f), 4);
            }
        } else if (OnGround && stepMoving && !inWater && !inLava && !input.Crouch) {
            StepSoundTimer -= dt * (IsSprinting ? 1.5f : 1.0f);
            if (StepSoundTimer <= 0f) {
                StepSoundTimer = 0.38f;
                var groundBlock = world.GetVoxel(new Vec3i(feetCell.X, feetCell.Y - 1, feetCell.Z));
                ushort stepBlockId = groundBlock.TypeId != 0 ? groundBlock.TypeId : feetBlock.TypeId;
                SoundSystem.PlayStep(stepBlockId);
                if (IsSprinting) {
                    world.SpawnDust(Position - new Vector3(0f, HalfExtents.Y, 0f), 2);
                    world.SpawnBlockDust(Position - new Vector3(0f, HalfExtents.Y, 0f), stepBlockId, 2);
                }
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
                float fallDmg = MathF.Ceiling(fallDist - 3f);
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
                    ApplyDamage(2.0f, session, cause: CauseVoid);
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
                ApplyDamage(MaxHealth * 2f, session, cause: CauseVoid);
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

        // Урон от удушья и выталкивание при застревании в непрозрачных блоках (голова/глаза)
        if (session.GameMode != GameMode.Creative) {
            var eyeCell = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Eye.Y), (int)MathF.Floor(Position.Z));
            var headCell = new Vec3i((int)MathF.Floor(Position.X), (int)MathF.Floor(Position.Y + HalfExtents.Y - 0.05f), (int)MathF.Floor(Position.Z));

            bool eyeSuffocating = world.IsOpaqueAt(eyeCell) && world.IsSolidAt(eyeCell);
            bool headSuffocating = world.IsOpaqueAt(headCell) && world.IsSolidAt(headCell);

            if (eyeSuffocating || headSuffocating) {
                StuckTimer += dt;
                if (StuckTimer >= 0.5f) {
                    StuckTimer = 0f;
                    ApplyDamage(1f, session);
                    session.AddMessage("Вы задыхаетесь в блоках!");

                    // Мягкое горизонтальное выталкивание в сторону ближайшего открытого блока
                    var offsets = new (int dx, int dz)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
                    Vector3 pushDir = Vector3.Zero;
                    foreach (var (dx, dz) in offsets) {
                        var neighbor = headCell + new Vec3i(dx, 0, dz);
                        if (!world.IsSolidAt(neighbor)) {
                            pushDir = new Vector3(dx, 0f, dz);
                            break;
                        }
                    }
                    if (pushDir != Vector3.Zero) {
                        Position += pushDir * 0.1f;
                        Velocity += pushDir * 0.5f;
                    } else {
                        // Если по бокам всё замуровано, но строго сверху есть свободное пространство (например, осыпался гравий) — выталкиваем вверх
                        var aboveHead = headCell + new Vec3i(0, 1, 0);
                        if (!world.IsSolidAt(aboveHead)) {
                            Position.Y += 0.2f;
                            Velocity.Y = 0f;
                        }
                    }
                }
            } else {
                StuckTimer = 0f;
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
    }
}
