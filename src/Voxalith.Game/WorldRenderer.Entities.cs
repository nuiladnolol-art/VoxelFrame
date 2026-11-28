using System.Numerics;
using Raylib_cs;
using Voxalith.Core;
using Voxalith.Core.World;

namespace Voxalith.Game;
public sealed partial class WorldRenderer {
    private void DrawRemotePlayers(Camera3D camera, float time) {
        // Если мы клиент — рисуем игроков, полученных от сервера
        if (GameClient.Active != null) {
            foreach (var rp in GameClient.Active.RemotePlayers) {
                if (rp.Dimension != _session.World.Dimension) continue;
                DrawSinglePlayerModel(camera, time, rp.Name, rp.Position, rp.Yaw, rp.Pitch, rp.IsMoving, rp.IsSneaking, rp.Health, rp.SelectedItemId, rp.OffhandItemId, rp.HelmetId, rp.ChestplateId, rp.LeggingsId, rp.BootsId, rp.ArmSwingTimer, rp.HurtTimer, rp.SkinName, rp.IsBlocking);
            }
        }
        // Если мы сервер/хост — рисуем подключившихся клиентов
        else if (GameServer.Active != null) {
            foreach (var cl in GameServer.Active.Clients) {
                if (cl.Dimension != _session.World.Dimension) continue;
                DrawSinglePlayerModel(camera, time, cl.Name, cl.Position, cl.Yaw, cl.Pitch, cl.IsMoving, cl.IsSneaking, cl.Health, cl.SelectedItemId, cl.OffhandItemId, cl.HelmetId, cl.ChestplateId, cl.LeggingsId, cl.BootsId, cl.ArmSwingTimer, cl.HurtTimer, cl.SkinName, cl.IsBlocking);
            }
        }
    }

    private static Color? GetArmorMaterialColor(int itemId) {
        if (itemId <= 0) return null;
        if (itemId == GameData.LeatherHelmetItem.Id || itemId == GameData.LeatherChestplateItem.Id || itemId == GameData.LeatherLeggingsItem.Id || itemId == GameData.LeatherBootsItem.Id)
            return new Color(160, 95, 55, 255); // Кожа
        if (itemId == GameData.IronHelmetItem.Id || itemId == GameData.IronChestplateItem.Id || itemId == GameData.IronLeggingsItem.Id || itemId == GameData.IronBootsItem.Id)
            return new Color(220, 220, 225, 255); // Железо
        if (itemId == GameData.DiamondHelmetItem.Id || itemId == GameData.DiamondChestplateItem.Id || itemId == GameData.DiamondLeggingsItem.Id || itemId == GameData.DiamondBootsItem.Id)
            return new Color(75, 225, 235, 255); // Алмаз
        return null;
    }

    private static Color GetBlockMaterialColor(ushort blockId) {
        if (blockId == GameData.BGrass.Id) return new Color(95, 170, 60, 255);
        if (blockId == GameData.BDirt.Id || blockId == GameData.BFarmland.Id) return new Color(134, 96, 67, 255);
        if (blockId == GameData.BStone.Id || blockId == GameData.BBedrock.Id) return new Color(128, 128, 128, 255);
        if (blockId == GameData.BCobblestone.Id || blockId == GameData.BMossyCobblestone.Id) return new Color(110, 110, 115, 255);
        if (blockId == GameData.BLog.Id) return new Color(103, 76, 45, 255);
        if (blockId == GameData.BPlanks.Id || blockId == GameData.BDoorLower.Id || blockId == GameData.BDoorUpper.Id) return new Color(185, 148, 90, 255);
        if (blockId == GameData.BLeaves.Id || blockId == GameData.BSapling.Id) return new Color(50, 140, 45, 255);
        if (blockId == GameData.BSand.Id || blockId == GameData.BChiseledSandstone.Id) return new Color(225, 210, 145, 255);
        if (blockId == GameData.BGravel.Id) return new Color(140, 135, 135, 255);
        if (blockId == GameData.BGlass.Id) return new Color(210, 240, 255, 180);
        if (blockId == GameData.BWater.Id) return new Color(60, 120, 235, 200);
        if (blockId == GameData.BLava.Id || blockId == GameData.BFire.Id) return new Color(255, 110, 20, 255);
        if (blockId == GameData.BCoalOre.Id) return new Color(45, 45, 45, 255);
        if (blockId == GameData.BIronOre.Id) return new Color(190, 165, 145, 255);
        if (blockId == GameData.BGoldOre.Id) return new Color(245, 215, 50, 255);
        if (blockId == GameData.BDiamondOre.Id) return new Color(85, 230, 245, 255);
        if (blockId == GameData.BObsidian.Id || blockId == GameData.BObsidianPillar.Id) return new Color(35, 25, 50, 255);
        if (blockId == GameData.BWorkbench.Id) return new Color(150, 115, 70, 255);
        if (blockId == GameData.BFurnace.Id) return new Color(105, 105, 110, 255);
        if (blockId == GameData.BChest.Id) return new Color(165, 115, 55, 255);
        if (blockId == GameData.BBed.Id || blockId == GameData.BBedHead.Id) return new Color(210, 45, 45, 255);
        if (blockId == GameData.BTNT.Id) return new Color(220, 50, 40, 255);
        if (blockId == GameData.BNetherrack.Id || blockId == GameData.BNetherBrick.Id) return new Color(125, 45, 45, 255);
        if (blockId == GameData.BSoulSand.Id) return new Color(85, 60, 50, 255);
        if (blockId == GameData.BGlowstone.Id) return new Color(245, 210, 100, 255);
        if (blockId == GameData.BNetherQuartzOre.Id) return new Color(230, 225, 220, 255);
        if (blockId == GameData.BEndStone.Id) return new Color(225, 228, 170, 255);
        if (blockId == GameData.BEndPortalFrame.Id) return new Color(60, 100, 85, 255);
        if (blockId == GameData.BEnderCrystal.Id) return new Color(220, 120, 255, 255);
        if (blockId == GameData.BChorusPlant.Id || blockId == GameData.BChorusFlower.Id) return new Color(175, 125, 175, 255);
        if (blockId == GameData.BWheatCrop.Id || blockId == GameData.BCarrotCrop.Id || blockId == GameData.BPotatoCrop.Id || blockId == GameData.BTallGrass.Id)
            return new Color(110, 180, 55, 255);
        if (blockId == GameData.BRedFlower.Id) return new Color(235, 45, 45, 255);
        if (blockId == GameData.BYellowFlower.Id) return new Color(250, 225, 45, 255);
        if (blockId == GameData.BJukebox.Id) return new Color(135, 95, 60, 255);
        return new Color(145, 145, 150, 255);
    }

    private void DrawHeldItemModel(int itemId, Vector3 itemPos, Vector3 light, Vector3 worldPos, bool isOffhand = false) {
        if (itemId <= 0) return;

        // 1. Блок в руке
        if (GameData.TryGetBlock((ushort)itemId, out var bDef) || (GameData.TryGetBlockByItem((ushort)itemId, out var bDef2) && (bDef = bDef2) != null)) {
            if (bDef.Id == GameData.BTorch.Id) {
                Raylib.DrawCube(itemPos, 0.05f, 0.26f, 0.05f, ShadeColor(new Color(135, 95, 55, 255), light, worldPos));
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.12f, 0f), 0.07f, 0.08f, 0.07f, new Color(255, 220, 70, 255));
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.13f, 0f), 0.04f, 0.06f, 0.04f, new Color(255, 110, 20, 255));
            } else {
                Color bCol = GetBlockMaterialColor(bDef.Id);
                float bSz = isOffhand ? 0.16f : 0.22f;
                Raylib.DrawCube(itemPos, bSz, bSz, bSz, ShadeColor(bCol, light, worldPos));
            }
            return;
        }

        // 2. Инструменты, оружие и предметы
        if (GameData.Items.TryGetValue((ushort)itemId, out var idef)) {
            var vis = GetHeldItemVisual(idef);
            Color handleCol = ShadeColor(new Color(125, 85, 45, 255), light, worldPos);
            Color headCol = ShadeColor(vis.MaterialColor, light, worldPos);

            if (vis.Shape == HeldItemShape.Sword) {
                Raylib.DrawCube(itemPos - new Vector3(0f, 0.08f, 0f), 0.04f, 0.14f, 0.04f, handleCol);
                Raylib.DrawCube(itemPos, 0.14f, 0.03f, 0.06f, headCol);
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.18f, 0f), 0.07f, 0.34f, 0.03f, headCol);
            } else if (vis.Shape == HeldItemShape.Pickaxe) {
                Raylib.DrawCube(itemPos, 0.04f, 0.38f, 0.04f, handleCol);
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.17f, 0f), 0.24f, 0.06f, 0.06f, headCol);
            } else if (vis.Shape == HeldItemShape.Axe) {
                Raylib.DrawCube(itemPos, 0.04f, 0.38f, 0.04f, handleCol);
                Raylib.DrawCube(itemPos + new Vector3(0.07f, 0.14f, 0f), 0.12f, 0.12f, 0.05f, headCol);
            } else if (vis.Shape == HeldItemShape.Shovel) {
                Raylib.DrawCube(itemPos, 0.04f, 0.38f, 0.04f, handleCol);
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.17f, 0f), 0.11f, 0.12f, 0.03f, headCol);
            } else if (vis.Shape == HeldItemShape.Hoe) {
                Raylib.DrawCube(itemPos, 0.04f, 0.38f, 0.04f, handleCol);
                Raylib.DrawCube(itemPos + new Vector3(0.06f, 0.17f, 0f), 0.12f, 0.05f, 0.04f, headCol);
            } else if (itemId == GameData.BowItem.Id) {
                Raylib.DrawCube(itemPos, 0.05f, 0.38f, 0.08f, handleCol);
                Raylib.DrawCube(itemPos + new Vector3(0f, 0f, 0.04f), 0.02f, 0.36f, 0.01f, ShadeColor(Color.White, light, worldPos));
            } else if (itemId == GameData.TorchItem.Id) {
                Raylib.DrawCube(itemPos, 0.05f, 0.26f, 0.05f, handleCol);
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.12f, 0f), 0.07f, 0.08f, 0.07f, new Color(255, 220, 70, 255));
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.13f, 0f), 0.04f, 0.06f, 0.04f, new Color(255, 110, 20, 255));
            } else if (itemId == GameData.AppleItem.Id || itemId == GameData.GoldenAppleItem.Id) {
                Color aCol = itemId == GameData.GoldenAppleItem.Id ? new Color(255, 215, 0, 255) : new Color(225, 45, 40, 255);
                Raylib.DrawCube(itemPos, 0.13f, 0.13f, 0.13f, ShadeColor(aCol, light, worldPos));
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.08f, 0f), 0.02f, 0.04f, 0.02f, handleCol);
            } else if (itemId == GameData.BreadItem.Id) {
                Raylib.DrawCube(itemPos, 0.11f, 0.09f, 0.22f, ShadeColor(new Color(205, 145, 75, 255), light, worldPos));
            } else if (itemId == GameData.RawBeefItem.Id || itemId == GameData.CookedBeefItem.Id || itemId == GameData.RawPorkItem.Id || itemId == GameData.CookedPorkItem.Id) {
                Color mCol = (itemId == GameData.CookedBeefItem.Id || itemId == GameData.CookedPorkItem.Id) ? new Color(145, 75, 50, 255) : new Color(215, 95, 95, 255);
                Raylib.DrawCube(itemPos, 0.14f, 0.07f, 0.18f, ShadeColor(mCol, light, worldPos));
            } else if (itemId == GameData.EnderPearlItem.Id || itemId == GameData.EyeOfEnderItem.Id) {
                Color pCol = itemId == GameData.EyeOfEnderItem.Id ? new Color(75, 205, 145, 255) : new Color(35, 135, 125, 255);
                Raylib.DrawCube(itemPos, 0.12f, 0.12f, 0.12f, ShadeColor(pCol, light, worldPos));
            } else if (itemId == GameData.TotemItem.Id) {
                Raylib.DrawCube(itemPos, 0.09f, 0.18f, 0.06f, new Color(255, 215, 40, 255));
                Raylib.DrawCube(itemPos + new Vector3(0f, 0.04f, 0f), 0.18f, 0.05f, 0.04f, new Color(110, 225, 140, 255));
            } else if (itemId == GameData.IronIngotItem.Id || itemId == GameData.GoldIngotItem.Id) {
                Color ingCol = itemId == GameData.GoldIngotItem.Id ? new Color(250, 220, 50, 255) : new Color(225, 225, 230, 255);
                Raylib.DrawCube(itemPos, 0.09f, 0.06f, 0.18f, ShadeColor(ingCol, light, worldPos));
            } else {
                Raylib.DrawCube(itemPos, 0.11f, 0.22f, 0.11f, headCol);
            }
        }
    }

    private void DrawSinglePlayerModel(Camera3D camera, float time, string name, Vector3 pos, float yaw, float pitch, bool isMoving, bool isSneaking, float health, int selectedItemId, int offhandItemId, int helmetId, int chestId, int legsId, int bootsId, float armSwingTimer, float hurtTimer, string skinName, bool isBlocking = false) {
        DrawSoftShadow(pos - new Vector3(0f, 0.9f, 0f), 0.45f);
        var light = GetLightFactor(pos);
        var skinDef = SkinSystem.GetSkin(skinName);

        float walkSwing = isMoving ? MathF.Sin(time * 10f) * 0.45f : 0f;
        float armPunch = armSwingTimer > 0f ? MathF.Sin(armSwingTimer * MathF.PI) * 0.8f : 0f;

        var skinColor = ShadeColor(hurtTimer > 0f ? new Color(240, 80, 80, 255) : skinDef.SkinColor, light, pos);
        var hairColor = ShadeColor(skinDef.HairColor, light, pos);
        var shirtColor = ShadeColor(hurtTimer > 0f ? new Color(200, 60, 60, 255) : skinDef.ShirtColor, light, pos);
        var pantsColor = ShadeColor(hurtTimer > 0f ? new Color(160, 40, 40, 255) : skinDef.PantsColor, light, pos);
        var shoeColor = ShadeColor(skinDef.ShoeColor, light, pos);
        var eyeColor = ShadeColor(skinDef.EyeColor, Vector3.One, pos);
        var eyeWhite = ShadeColor(Color.White, light, pos);
        var detailColor = ShadeColor(skinDef.DetailColor, light, pos);

        Rlgl.PushMatrix();
        Rlgl.Translatef(pos.X, pos.Y, pos.Z);
        Rlgl.Rotatef(yaw * 180f / MathF.PI, 0f, 1f, 0f);

        if (isSneaking) {
            Rlgl.Translatef(0f, -0.15f, 0f);
        }

        // 1. Туловище
        Raylib.DrawCube(new Vector3(0f, 0f, 0f), 0.45f, 0.65f, 0.24f, shirtColor);
        // Нагрудник (броня)
        if (GetArmorMaterialColor(chestId) is { } cColor) {
            Raylib.DrawCube(new Vector3(0f, 0f, 0f), 0.48f, 0.67f, 0.27f, ShadeColor(cColor, light, pos));
        } else {
            // Вырез на шее (кожа)
            Raylib.DrawCube(new Vector3(0f, 0.26f, 0.121f), 0.12f, 0.12f, 0.01f, skinColor);
            // Детали одежды (пояс / ремень / эмблема)
            Raylib.DrawCube(new Vector3(0f, -0.28f, 0.121f), 0.452f, 0.08f, 0.01f, detailColor);
        }

        // 2. Голова с наклоном Pitch
        Rlgl.PushMatrix();
        Rlgl.Translatef(0f, 0.52f, 0f);
        Rlgl.Rotatef(-pitch * 180f / MathF.PI, 1f, 0f, 0f);

        Raylib.DrawCube(Vector3.Zero, 0.42f, 0.42f, 0.42f, skinColor);
        // Шлем (броня) или Волосы
        if (GetArmorMaterialColor(helmetId) is { } hColor) {
            Raylib.DrawCube(new Vector3(0f, 0.06f, 0f), 0.46f, 0.46f, 0.46f, ShadeColor(hColor, light, pos));
        } else {
            // Волосы
            Raylib.DrawCube(new Vector3(0f, 0.16f, 0f), 0.43f, 0.12f, 0.43f, hairColor);
            Raylib.DrawCube(new Vector3(0f, 0.05f, -0.16f), 0.43f, 0.22f, 0.12f, hairColor);
        }
        // Глаза (белок + зрачок цвета скина)
        Raylib.DrawCube(new Vector3(-0.11f, 0.02f, 0.215f), 0.08f, 0.06f, 0.01f, eyeWhite);
        Raylib.DrawCube(new Vector3(0.11f, 0.02f, 0.215f), 0.08f, 0.06f, 0.01f, eyeWhite);
        Raylib.DrawCube(new Vector3(-0.09f, 0.02f, 0.217f), 0.04f, 0.06f, 0.01f, eyeColor);
        Raylib.DrawCube(new Vector3(0.09f, 0.02f, 0.217f), 0.04f, 0.06f, 0.01f, eyeColor);
        // Нос
        Raylib.DrawCube(new Vector3(0f, -0.04f, 0.215f), 0.08f, 0.05f, 0.01f, detailColor);
        Rlgl.PopMatrix();

        // 3. Руки
        // Левая рука (качается при ходьбе или блокирует щитом)
        var lArmPos = isBlocking ? new Vector3(-0.20f, 0.12f, 0.28f) : new Vector3(-0.33f, 0.02f, walkSwing * 0.3f);
        Raylib.DrawCube(lArmPos, 0.18f, 0.65f, 0.18f, skinColor);
        Raylib.DrawCube(lArmPos + new Vector3(0f, 0.18f, 0f), 0.182f, 0.30f, 0.182f, shirtColor);

        // Предмет / Щит во второй руке (слева)
        if (offhandItemId > 0) {
            if (offhandItemId == GameData.ShieldItem.Id) {
                var shieldPos = isBlocking ? new Vector3(-0.06f, 0.10f, 0.42f) : (lArmPos + new Vector3(0f, -0.05f, 0.16f));
                Raylib.DrawCube(shieldPos, 0.36f, 0.48f, 0.06f, ShadeColor(new Color(155, 140, 115, 255), light, pos));
                Raylib.DrawCube(shieldPos + new Vector3(0f, 0f, 0.035f), 0.10f, 0.10f, 0.02f, ShadeColor(new Color(200, 200, 205, 255), light, pos));
            } else {
                var offPos = lArmPos + new Vector3(0f, -0.22f, 0.20f);
                DrawHeldItemModel(offhandItemId, offPos, light, pos, isOffhand: true);
            }
        }

        // Правая рука (качается при ходьбе + наносит удар/копает)
        var rArmPos = new Vector3(0.33f, 0.02f + armPunch * 0.2f, -walkSwing * 0.3f + armPunch * 0.4f);
        Raylib.DrawCube(rArmPos, 0.18f, 0.65f, 0.18f, skinColor);
        Raylib.DrawCube(rArmPos + new Vector3(0f, 0.18f, 0f), 0.182f, 0.30f, 0.182f, shirtColor);

        // 3D предмет в правой руке
        if (selectedItemId > 0) {
            var itemPos = rArmPos + new Vector3(0f, -0.22f, 0.20f + armPunch * 0.2f);
            DrawHeldItemModel(selectedItemId, itemPos, light, pos, isOffhand: false);
        }

        // 4. Ноги
        var lLegPos = new Vector3(-0.12f, -0.62f, -walkSwing * 0.35f);
        var rLegPos = new Vector3(0.12f, -0.62f, walkSwing * 0.35f);

        Raylib.DrawCube(lLegPos, 0.20f, 0.65f, 0.20f, pantsColor);
        Raylib.DrawCube(rLegPos, 0.20f, 0.65f, 0.20f, pantsColor);

        // Поножи (броня)
        if (GetArmorMaterialColor(legsId) is { } lColor) {
            Raylib.DrawCube(lLegPos, 0.22f, 0.55f, 0.22f, ShadeColor(lColor, light, pos));
            Raylib.DrawCube(rLegPos, 0.22f, 0.55f, 0.22f, ShadeColor(lColor, light, pos));
        }

        // Ботинки
        if (GetArmorMaterialColor(bootsId) is { } bColor) {
            Raylib.DrawCube(lLegPos - new Vector3(0f, 0.24f, -0.02f), 0.222f, 0.17f, 0.26f, ShadeColor(bColor, light, pos));
            Raylib.DrawCube(rLegPos - new Vector3(0f, 0.24f, -0.02f), 0.222f, 0.17f, 0.26f, ShadeColor(bColor, light, pos));
        } else {
            Raylib.DrawCube(lLegPos - new Vector3(0f, 0.25f, -0.02f), 0.202f, 0.15f, 0.24f, shoeColor);
            Raylib.DrawCube(rLegPos - new Vector3(0f, 0.25f, -0.02f), 0.202f, 0.15f, 0.24f, shoeColor);
        }

        Rlgl.PopMatrix();
    }

    public void DrawRemotePlayerNameTags(Camera3D camera) {
        if (GameClient.Active != null) {
            foreach (var rp in GameClient.Active.RemotePlayers) {
                if (rp.Dimension != _session.World.Dimension) continue;
                DrawPlayerNameTag2D(camera, rp.Name, rp.Position, rp.IsSneaking, rp.Health);
            }
        } else if (GameServer.Active != null) {
            foreach (var cl in GameServer.Active.Clients) {
                if (cl.Dimension != _session.World.Dimension) continue;
                DrawPlayerNameTag2D(camera, cl.Name, cl.Position, cl.IsSneaking, cl.Health);
            }
        }
    }

    private void DrawPlayerNameTag2D(Camera3D camera, string name, Vector3 pos, bool isSneaking, float health) {
        var namePos = pos + new Vector3(0f, isSneaking ? 1.05f : 1.25f, 0f);
        float dist = Vector3.Distance(camera.Position, namePos);
        if (dist < 0.8f || dist > 50f) return;

        var toTarget = namePos - camera.Position;
        var camFwd = Vector3.Normalize(camera.Target - camera.Position);
        if (Vector3.Dot(toTarget, camFwd) <= 0.1f) return;

        var sp = Raylib.GetWorldToScreen(namePos, camera);
        if (sp.X < -100 || sp.X > Raylib.GetScreenWidth() + 100 || sp.Y < -50 || sp.Y > Raylib.GetScreenHeight() + 50) return;

        float scale = Math.Clamp(1.0f - (dist / 65f), 0.55f, 1.0f);
        float fontSize = 13.5f * scale;
        float nameW = Fonts.Measure(name, fontSize);
        float tagH = 18f * scale;
        float tagW = nameW + 14f * scale;

        var tagRect = new Rectangle(sp.X - tagW / 2f, sp.Y - tagH / 2f, tagW, tagH);
        Raylib.DrawRectangleRounded(tagRect, 0.25f, 4, isSneaking ? new Color(15, 18, 26, 110) : new Color(15, 18, 26, 210));
        Fonts.DrawCentered(name, sp.X, sp.Y - 6f * scale, fontSize, isSneaking ? new Color(200, 205, 215, 140) : Color.White);

        // Полоска здоровья
        float hpPct = Math.Clamp(health / 20f, 0f, 1f);
        float barW = Math.Max(nameW, 32f * scale);
        float barH = 3.5f * scale;
        var hpBg = new Rectangle(sp.X - barW / 2f, sp.Y + tagH / 2f + 2f * scale, barW, barH);
        Raylib.DrawRectangleRounded(hpBg, 0.4f, 2, new Color(30, 30, 30, 180));
        if (hpPct > 0f) {
            Color hpCol = hpPct > 0.5f ? new Color(60, 225, 75, 240) : (hpPct > 0.25f ? new Color(240, 205, 45, 240) : new Color(240, 60, 50, 240));
            Raylib.DrawRectangleRounded(new Rectangle(hpBg.X, hpBg.Y, barW * hpPct, barH), 0.4f, 2, hpCol);
        }
    }

    public void DrawEntities(Camera3D camera) {
        float time = (float)Raylib.GetTime();

        // 1. Draw Remote Players (Multiplayer: Steve 3D Model, Head Pitch/Yaw, Item in Hand, 3D Name Tag)
        DrawRemotePlayers(camera, time);

        // Draw Local Player in 3rd person view (F5)
        if (_session.CameraPerspective != 0 && _session.Player.Health > 0f) {
            var p = _session.Player;
            int selectedItemId = p.SelectedItem?.Id ?? 0;
            int offhandItemId = p.OffhandItem?.Id ?? 0;
            int helmetId = p.Armor[0]?.Item.Definition.Id ?? 0;
            int chestId = p.Armor[1]?.Item.Definition.Id ?? 0;
            int legsId = p.Armor[2]?.Item.Definition.Id ?? 0;
            int bootsId = p.Armor[3]?.Item.Definition.Id ?? 0;
            DrawSinglePlayerModel(camera, time, p.Name, p.Position, p.Yaw, p.Pitch, p.IsMoving, p.IsCrouching, p.Health, selectedItemId, offhandItemId, helmetId, chestId, legsId, bootsId, p.SwingMarker, p.HurtTimer, p.SkinName, p.IsBlocking);
        }

        // 2. Draw Animals (Pig, Cow, Sheep) with 3D model, animations and fog
        foreach (var a in _world.Animals) {
            if (!a.Alive) continue;
            DrawSoftShadow(a.Position - new Vector3(0f, a.HalfSizeY, 0f), a.HalfSizeX + 0.05f);

            var light = GetLightFactor(a.Position);
            var aPos = a.Position;

            Vector3 fwd = new(0f, 0f, 1f);
            if (a.Velocity.LengthSquared() > 0.01f) {
                var dirH = Vector2.Normalize(new Vector2(a.Velocity.X, a.Velocity.Z));
                fwd = new Vector3(dirH.X, 0f, dirH.Y);
            }

            float angleDegrees = MathF.Atan2(fwd.X, fwd.Z) * 180f / MathF.PI;
            bool isMoving = new Vector2(a.Velocity.X, a.Velocity.Z).LengthSquared() > 0.01f;
            float walkSwing = isMoving ? MathF.Sin(time * 9f) * 0.35f : 0f;

            Rlgl.PushMatrix();
            Rlgl.Translatef(a.Position.X, a.Position.Y, a.Position.Z);
            Rlgl.Rotatef(angleDegrees, 0f, 1f, 0f);

            if (a.IsBaby) {
                Rlgl.Scalef(0.55f, 0.55f, 0.55f);
            }

            if (a.HurtTime > 0f) {
                float roll = MathF.Sin(a.HurtTime * MathF.PI * 10f) * 12f;
                Rlgl.Rotatef(roll, 0f, 0f, 1f);
            }

            if (a.Type == AnimalType.Pig) {
                // ── PIG MODEL (с 3D-ушками и виляющим хвостиком) ──────────────
                var pigPink = ShadeColor(a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(240, 165, 165, 255), light, aPos);
                var snoutColor = ShadeColor(a.HurtTime > 0f ? new Color(200, 60, 60, 255) : new Color(220, 130, 130, 255), light, aPos);

                // Body
                Raylib.DrawCube(new Vector3(0f, 0.125f, 0f), 0.65f, 0.55f, 0.85f, pigPink);

                // Head
                var pigHead = new Vector3(0f, 0.25f, 0.55f);
                Raylib.DrawCube(pigHead, 0.45f, 0.45f, 0.45f, pigPink);

                // Ears (3D ушки)
                Raylib.DrawCube(pigHead + new Vector3(-0.24f, 0.16f, 0.05f), 0.08f, 0.12f, 0.10f, pigPink);
                Raylib.DrawCube(pigHead + new Vector3(0.24f, 0.16f, 0.05f), 0.08f, 0.12f, 0.10f, pigPink);

                // Snout (рыло с ноздрями)
                var pigSnout = pigHead + new Vector3(0f, -0.05f, 0.26f);
                Raylib.DrawCube(pigSnout, 0.22f, 0.14f, 0.10f, snoutColor);
                Raylib.DrawCube(pigSnout + new Vector3(-0.05f, 0f, 0.055f), 0.04f, 0.05f, 0.01f, ShadeColor(Color.Black, light, aPos));
                Raylib.DrawCube(pigSnout + new Vector3(0.05f, 0f, 0.055f), 0.04f, 0.05f, 0.01f, ShadeColor(Color.Black, light, aPos));

                // Eyes
                Raylib.DrawCube(pigHead + new Vector3(-0.16f, 0.08f, 0.23f), 0.06f, 0.06f, 0.01f, ShadeColor(Color.Black, light, aPos));
                Raylib.DrawCube(pigHead + new Vector3(0.16f, 0.08f, 0.23f), 0.06f, 0.06f, 0.01f, ShadeColor(Color.Black, light, aPos));

                // Хвостик крючком
                float tailWag = MathF.Sin(time * 12f) * 0.08f;
                Raylib.DrawCube(new Vector3(tailWag, 0.25f, -0.46f), 0.06f, 0.06f, 0.12f, pigPink);

                // 4 Legs
                float pLegW = 0.18f, pLegH = 0.35f, pLegD = 0.18f;
                var pFL = new Vector3(-0.20f, -0.275f, 0.25f + walkSwing * 0.2f);
                var pFR = new Vector3(0.20f, -0.275f, 0.25f - walkSwing * 0.2f);
                var pBL = new Vector3(-0.20f, -0.275f, -0.25f - walkSwing * 0.2f);
                var pBR = new Vector3(0.20f, -0.275f, -0.25f + walkSwing * 0.2f);

                Raylib.DrawCube(pFL, pLegW, pLegH, pLegD, pigPink);
                Raylib.DrawCube(pFR, pLegW, pLegH, pLegD, pigPink);
                Raylib.DrawCube(pBL, pLegW, pLegH, pLegD, pigPink);
                Raylib.DrawCube(pBR, pLegW, pLegH, pLegD, pigPink);

            } else if (a.Type == AnimalType.Cow) {
                // ── COW MODEL (чистая воксельная корова без Z-fighting и мерцания) ──
                var cowBrown = ShadeColor(a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(68, 52, 40, 255), light, aPos);
                var cowWhite = ShadeColor(a.HurtTime > 0f ? new Color(220, 110, 110, 255) : new Color(230, 230, 230, 255), light, aPos);
                var snoutColor = ShadeColor(a.HurtTime > 0f ? new Color(200, 70, 70, 255) : new Color(185, 150, 150, 255), light, aPos);
                var hornColor = ShadeColor(new Color(220, 220, 215, 255), light, aPos);
                var udderPink = ShadeColor(new Color(245, 170, 180, 255), light, aPos);
                var hoofColor = ShadeColor(new Color(36, 28, 22, 255), light, aPos);

                // Body (Основное тело коровы)
                Raylib.DrawCube(new Vector3(0f, 0.15f, 0f), 0.75f, 0.60f, 1.05f, cowBrown);

                // Белое брюхо/пятно снизу и по бокам с четким выступом
                Raylib.DrawCube(new Vector3(0f, 0.08f, -0.05f), 0.76f, 0.32f, 0.55f, cowWhite);

                // Udder
                Raylib.DrawCube(new Vector3(0f, -0.10f, -0.25f), 0.20f, 0.12f, 0.22f, udderPink);

                // Head
                var headPos = new Vector3(0f, 0.40f, 0.65f);
                Raylib.DrawCube(headPos, 0.45f, 0.45f, 0.45f, cowBrown);

                // Snout
                Raylib.DrawCube(headPos + new Vector3(0f, -0.08f, 0.26f), 0.32f, 0.20f, 0.14f, snoutColor);

                // Horns
                Raylib.DrawCube(headPos + new Vector3(-0.22f, 0.24f, -0.05f), 0.08f, 0.16f, 0.08f, hornColor);
                Raylib.DrawCube(headPos + new Vector3(0.22f, 0.24f, -0.05f), 0.08f, 0.16f, 0.08f, hornColor);

                // Eyes
                Raylib.DrawCube(headPos + new Vector3(-0.18f, 0.06f, 0.23f), 0.06f, 0.06f, 0.01f, ShadeColor(Color.Black, light, aPos));
                Raylib.DrawCube(headPos + new Vector3(0.18f, 0.06f, 0.23f), 0.06f, 0.06f, 0.01f, ShadeColor(Color.Black, light, aPos));

                // 4 Legs with Hooves
                float cLegW = 0.20f, cLegH = 0.55f, cLegD = 0.20f;
                var cFL = new Vector3(-0.24f, -0.375f, 0.32f + walkSwing * 0.2f);
                var cFR = new Vector3(0.24f, -0.375f, 0.32f - walkSwing * 0.2f);
                var cBL = new Vector3(-0.24f, -0.375f, -0.32f - walkSwing * 0.2f);
                var cBR = new Vector3(0.24f, -0.375f, -0.32f + walkSwing * 0.2f);

                Raylib.DrawCube(cFL, cLegW, cLegH, cLegD, cowBrown);
                Raylib.DrawCube(cFR, cLegW, cLegH, cLegD, cowBrown);
                Raylib.DrawCube(cBL, cLegW, cLegH, cLegD, cowBrown);
                Raylib.DrawCube(cBR, cLegW, cLegH, cLegD, cowBrown);

                // Копыта
                Raylib.DrawCube(cFL - new Vector3(0f, 0.22f, 0f), cLegW + 0.01f, 0.12f, cLegD + 0.01f, hoofColor);
                Raylib.DrawCube(cFR - new Vector3(0f, 0.22f, 0f), cLegW + 0.01f, 0.12f, cLegD + 0.01f, hoofColor);
                Raylib.DrawCube(cBL - new Vector3(0f, 0.22f, 0f), cLegW + 0.01f, 0.12f, cLegD + 0.01f, hoofColor);
                Raylib.DrawCube(cBR - new Vector3(0f, 0.22f, 0f), cLegW + 0.01f, 0.12f, cLegD + 0.01f, hoofColor);

            } else if (a.Type == AnimalType.Sheep) {
                // ── SHEEP MODEL ───────────────────────────────────────────
                var woolWhite = ShadeColor(a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(235, 235, 235, 255), light, aPos);
                var skinTan = ShadeColor(a.HurtTime > 0f ? new Color(210, 80, 80, 255) : new Color(225, 205, 185, 255), light, aPos);

                // Fluffy Wool Body
                Raylib.DrawCube(new Vector3(0f, 0.10f, 0f), 0.80f, 0.65f, 0.95f, woolWhite);

                // Head
                var headPos = new Vector3(0f, 0.28f, 0.58f);
                Raylib.DrawCube(headPos, 0.35f, 0.38f, 0.42f, skinTan);
                Raylib.DrawCube(headPos + new Vector3(0f, 0.18f, -0.05f), 0.36f, 0.14f, 0.34f, woolWhite);

                // Eyes
                Raylib.DrawCube(headPos + new Vector3(-0.14f, 0.04f, 0.215f), 0.05f, 0.05f, 0.01f, ShadeColor(Color.Black, light, aPos));
                Raylib.DrawCube(headPos + new Vector3(0.14f, 0.04f, 0.215f), 0.05f, 0.05f, 0.01f, ShadeColor(Color.Black, light, aPos));

                // 4 Legs
                float sLegW = 0.16f, sLegH = 0.45f, sLegD = 0.16f;
                var sFL = new Vector3(-0.22f, -0.325f, 0.28f + walkSwing * 0.2f);
                var sFR = new Vector3(0.22f, -0.325f, 0.28f - walkSwing * 0.2f);
                var sBL = new Vector3(-0.22f, -0.325f, -0.28f - walkSwing * 0.2f);
                var sBR = new Vector3(0.22f, -0.325f, -0.28f + walkSwing * 0.2f);

                Raylib.DrawCube(sFL, sLegW, sLegH, sLegD, skinTan);
                Raylib.DrawCube(sFR, sLegW, sLegH, sLegD, skinTan);
                Raylib.DrawCube(sBL, sLegW, sLegH, sLegD, skinTan);
                Raylib.DrawCube(sBR, sLegW, sLegH, sLegD, skinTan);

            } else if (a.Type == AnimalType.Chicken) {
                // ── CHICKEN MODEL (Курица с машущими крыльями, клювом и гребешком) ──
                var featherWhite = ShadeColor(a.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(245, 245, 248, 255), light, aPos);
                var beakYellow = ShadeColor(new Color(245, 185, 30, 255), light, aPos);
                var wattleRed = ShadeColor(new Color(225, 40, 40, 255), light, aPos);
                var legYellow = ShadeColor(new Color(235, 175, 25, 255), light, aPos);

                // Взмахи крыльев при падении или ходьбе
                bool isFalling = a.Velocity.Y < -0.2f;
                float wingFlap = isFalling ? MathF.Sin(time * 28f) * 0.45f : (isMoving ? MathF.Sin(time * 12f) * 0.18f : 0f);

                // Body (Тело)
                Raylib.DrawCube(new Vector3(0f, 0.05f, 0f), 0.42f, 0.35f, 0.48f, featherWhite);

                // Head (Голова с легким покачиванием)
                float headBob = isMoving ? MathF.Sin(time * 9f) * 0.05f : 0f;
                var headPos = new Vector3(0f, 0.28f, 0.22f + headBob);
                Raylib.DrawCube(headPos, 0.24f, 0.28f, 0.24f, featherWhite);

                // Comb (Красный гребешок на голове)
                Raylib.DrawCube(headPos + new Vector3(0f, 0.17f, 0f), 0.06f, 0.08f, 0.18f, wattleRed);

                // Beak (Желтый клюв)
                var beakPos = headPos + new Vector3(0f, 0.02f, 0.15f);
                Raylib.DrawCube(beakPos, 0.12f, 0.08f, 0.10f, beakYellow);

                // Wattle (Красная бородка под клювом)
                Raylib.DrawCube(headPos + new Vector3(0f, -0.09f, 0.13f), 0.08f, 0.10f, 0.06f, wattleRed);

                // Eyes (Черные глазки)
                Raylib.DrawCube(headPos + new Vector3(-0.125f, 0.05f, 0.06f), 0.01f, 0.04f, 0.04f, ShadeColor(Color.Black, light, aPos));
                Raylib.DrawCube(headPos + new Vector3(0.125f, 0.05f, 0.06f), 0.01f, 0.04f, 0.04f, ShadeColor(Color.Black, light, aPos));

                // Wings (Крылья по бокам с анимацией махания)
                var leftWing = new Vector3(-0.23f - MathF.Abs(wingFlap) * 0.15f, 0.08f + MathF.Abs(wingFlap) * 0.12f, 0f);
                var rightWing = new Vector3(0.23f + MathF.Abs(wingFlap) * 0.15f, 0.08f + MathF.Abs(wingFlap) * 0.12f, 0f);
                Raylib.DrawCube(leftWing, 0.06f, 0.26f, 0.36f, featherWhite);
                Raylib.DrawCube(rightWing, 0.06f, 0.26f, 0.36f, featherWhite);

                // 2 Legs (Тонкие желтые лапки)
                float legSwing = walkSwing * 0.4f;
                var legL = new Vector3(-0.10f, -0.22f, legSwing);
                var legR = new Vector3(0.10f, -0.22f, -legSwing);
                Raylib.DrawCube(legL, 0.06f, 0.22f, 0.06f, legYellow);
                Raylib.DrawCube(legR, 0.06f, 0.22f, 0.06f, legYellow);
                // Лапки снизу (пальцы)
                Raylib.DrawCube(legL + new Vector3(0f, -0.10f, 0.04f), 0.10f, 0.02f, 0.12f, legYellow);
                Raylib.DrawCube(legR + new Vector3(0f, -0.10f, 0.04f), 0.10f, 0.02f, 0.12f, legYellow);
            }

            Rlgl.PopMatrix();

            if (a.LoveTimer > 0f) {
                float heartY = a.Position.Y + a.HalfSizeY + 0.35f + MathF.Sin(time * 5f + a.Position.X) * 0.08f;
                var heartPos = new Vector3(a.Position.X, heartY, a.Position.Z);
                var heartSrc = TextureAtlas.TilePixelRect(TextureAtlas.THeartParticle);
                Raylib.DrawBillboardRec(camera, TextureAtlas.Atlas, heartSrc, heartPos, new Vector2(0.4f, 0.4f), Color.White);
            }
        }

        // 3. Draw Hostile Mobs (Zombie, Creeper, Skeleton, Spider, Enderman, Blaze, Mini-Bosses)
        foreach (var h in _world.HostileMobs) {
            if (!h.Alive) continue;
            DrawSoftShadow(h.Position - new Vector3(0f, h.HalfSizeY, 0f), 0.45f);

            var light = GetLightFactor(h.Position);
            var hPos = h.Position;

            Vector3 fwd = new(0f, 0f, 1f);
            if (h.Velocity.LengthSquared() > 0.01f) {
                var dirH = Vector2.Normalize(new Vector2(h.Velocity.X, h.Velocity.Z));
                fwd = new Vector3(dirH.X, 0f, dirH.Y);
            }

            float angleDegrees = MathF.Atan2(fwd.X, fwd.Z) * 180f / MathF.PI;
            bool isMoving = new Vector2(h.Velocity.X, h.Velocity.Z).LengthSquared() > 0.01f;
            float walkSwing = isMoving ? MathF.Sin(time * 8f) * 0.4f : 0f;

            Rlgl.PushMatrix();
            Rlgl.Translatef(h.Position.X, h.Position.Y, h.Position.Z);
            Rlgl.Rotatef(angleDegrees, 0f, 1f, 0f);

            if (h.HurtTime > 0f) {
                float roll = MathF.Sin(h.HurtTime * MathF.PI * 10f) * 12f;
                Rlgl.Rotatef(roll, 0f, 0f, 1f);
            }

            if (h.Type == HostileType.Zombie) {
                // ── ZOMBIE MODEL ──────────────────────────────────────────
                var skinColor = ShadeColor(h.HurtTime > 0f ? new Color(220, 60, 60, 255) : new Color(45, 125, 45, 255), light, hPos);
                var shirtColor = ShadeColor(h.HurtTime > 0f ? new Color(180, 50, 50, 255) : new Color(40, 140, 160, 255), light, hPos);
                var pantsColor = ShadeColor(h.HurtTime > 0f ? new Color(140, 40, 40, 255) : new Color(40, 40, 110, 255), light, hPos);
                var outlineColor = ShadeColor(new Color(20, 20, 20, 180), light, hPos);

                // Body
                Raylib.DrawCube(new Vector3(0f, 0.15f, 0f), 0.6f, 0.70f, 0.35f, shirtColor);
                Raylib.DrawCubeWires(new Vector3(0f, 0.15f, 0f), 0.602f, 0.702f, 0.352f, outlineColor);

                // Head
                var headPos = new Vector3(0f, 0.74f, 0f);
                Raylib.DrawCube(headPos, 0.48f, 0.48f, 0.48f, skinColor);
                Raylib.DrawCubeWires(headPos, 0.482f, 0.482f, 0.482f, outlineColor);

                Raylib.DrawCube(headPos + new Vector3(-0.12f, 0.04f, 0.245f), 0.09f, 0.08f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0.12f, 0.04f, 0.245f), 0.09f, 0.08f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0f, -0.1f, 0.245f), 0.16f, 0.06f, 0.01f, ShadeColor(Color.Black, light, hPos));

                // Arms (вытянуты вперед)
                var leftArm = new Vector3(-0.42f, 0.35f, 0.25f + MathF.Sin(time * 4f) * 0.03f);
                var rightArm = new Vector3(0.42f, 0.35f, 0.25f - MathF.Sin(time * 4f) * 0.03f);
                Raylib.DrawCube(leftArm, 0.2f, 0.2f, 0.55f, skinColor);
                Raylib.DrawCubeWires(leftArm, 0.202f, 0.202f, 0.552f, outlineColor);
                Raylib.DrawCube(rightArm, 0.2f, 0.2f, 0.55f, skinColor);
                Raylib.DrawCubeWires(rightArm, 0.202f, 0.202f, 0.552f, outlineColor);

                // Legs
                var leftLeg = new Vector3(-0.16f, -0.525f, walkSwing * 0.3f);
                var rightLeg = new Vector3(0.16f, -0.525f, -walkSwing * 0.3f);
                Raylib.DrawCube(leftLeg, 0.22f, 0.65f, 0.22f, pantsColor);
                Raylib.DrawCubeWires(leftLeg, 0.222f, 0.652f, 0.222f, outlineColor);
                Raylib.DrawCube(rightLeg, 0.22f, 0.65f, 0.22f, pantsColor);
                Raylib.DrawCubeWires(rightLeg, 0.222f, 0.652f, 0.222f, outlineColor);

            } else if (h.Type == HostileType.Skeleton) {
                // ── SKELETON MODEL (3D ребра и лук со стрелой) ──────────────
                var boneColor = ShadeColor(h.HurtTime > 0f ? new Color(220, 60, 60, 255) : new Color(205, 205, 205, 255), light, hPos);
                var ribColor = ShadeColor(h.HurtTime > 0f ? new Color(180, 40, 40, 255) : new Color(130, 130, 130, 255), light, hPos);
                var bowColor = ShadeColor(new Color(125, 80, 45, 255), light, hPos);
                var stringColor = ShadeColor(new Color(225, 225, 225, 255), light, hPos);
                var arrowColor = ShadeColor(new Color(230, 230, 230, 255), light, hPos);
                var outlineColor = ShadeColor(new Color(30, 30, 30, 180), light, hPos);

                // Body (Позвоночник и грудная клетка)
                Raylib.DrawCube(new Vector3(0f, 0.15f, 0f), 0.45f, 0.70f, 0.22f, boneColor);
                Raylib.DrawCubeWires(new Vector3(0f, 0.15f, 0f), 0.452f, 0.702f, 0.222f, outlineColor);

                // 3D ребра
                Raylib.DrawCube(new Vector3(0f, 0.28f, 0.115f), 0.38f, 0.05f, 0.02f, ribColor);
                Raylib.DrawCube(new Vector3(0f, 0.18f, 0.115f), 0.38f, 0.05f, 0.02f, ribColor);
                Raylib.DrawCube(new Vector3(0f, 0.08f, 0.115f), 0.38f, 0.05f, 0.02f, ribColor);

                // Head
                var headPos = new Vector3(0f, 0.72f, 0f);
                Raylib.DrawCube(headPos, 0.44f, 0.44f, 0.44f, boneColor);
                Raylib.DrawCubeWires(headPos, 0.442f, 0.442f, 0.442f, outlineColor);

                // Skull eyes & nose cavity
                Raylib.DrawCube(headPos + new Vector3(-0.10f, 0.04f, 0.225f), 0.09f, 0.09f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0.10f, 0.04f, 0.225f), 0.09f, 0.09f, 0.01f, ShadeColor(Color.Black, light, hPos));
                Raylib.DrawCube(headPos + new Vector3(0f, -0.09f, 0.225f), 0.12f, 0.05f, 0.01f, ShadeColor(Color.Black, light, hPos));

                // Arms
                var leftArm = new Vector3(-0.30f, 0.35f, 0.22f);
                var rightArm = new Vector3(0.30f, 0.35f, 0.22f);
                Raylib.DrawCube(leftArm, 0.12f, 0.12f, 0.50f, boneColor);
                Raylib.DrawCube(rightArm, 0.12f, 0.12f, 0.50f, boneColor);

                // 3D Bow with Arrow
                var bowPos = new Vector3(0.15f, 0.35f, 0.48f);
                Raylib.DrawCube(bowPos, 0.06f, 0.55f, 0.06f, bowColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, 0.24f, -0.06f), 0.05f, 0.12f, 0.06f, bowColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, -0.24f, -0.06f), 0.05f, 0.12f, 0.06f, bowColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, 0f, -0.08f), 0.02f, 0.52f, 0.02f, stringColor);
                Raylib.DrawCube(bowPos + new Vector3(0f, 0f, 0.05f), 0.03f, 0.03f, 0.45f, arrowColor);

                // Legs
                var leftLeg = new Vector3(-0.12f, -0.525f, walkSwing * 0.3f);
                var rightLeg = new Vector3(0.12f, -0.525f, -walkSwing * 0.3f);
                Raylib.DrawCube(leftLeg, 0.14f, 0.65f, 0.14f, boneColor);
                Raylib.DrawCube(rightLeg, 0.14f, 0.65f, 0.14f, boneColor);

            } else if (h.Type == HostileType.Spider) {
                // ── SPIDER MODEL (8 суставчатых анимированных лап + 8 светящихся глаз) ──
                var spiderColor = ShadeColor(h.HurtTime > 0f ? new Color(220, 50, 50, 255) : new Color(35, 25, 20, 255), light, hPos);
                var spiderDark = ShadeColor(new Color(22, 16, 14, 255), light, hPos);
                var eyeRed = ShadeColor(new Color(235, 20, 20, 255), Vector3.One, hPos);

                // Body
                var headPos = new Vector3(0f, -0.05f, 0.30f);
                Raylib.DrawCube(headPos, 0.45f, 0.35f, 0.40f, spiderColor);
                var abdomenPos = new Vector3(0f, 0.05f, -0.30f);
                Raylib.DrawCube(abdomenPos, 0.65f, 0.50f, 0.65f, spiderDark);
                // Красный знак на спине
                Raylib.DrawCube(abdomenPos + new Vector3(0f, 0.26f, 0f), 0.18f, 0.02f, 0.22f, eyeRed);

                // 8 Glowing Red Eyes (Кластер из 8 глаз)
                Raylib.DrawCube(headPos + new Vector3(-0.14f, 0.05f, 0.205f), 0.05f, 0.05f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(-0.06f, 0.05f, 0.205f), 0.05f, 0.05f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(0.06f, 0.05f, 0.205f), 0.05f, 0.05f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(0.14f, 0.05f, 0.205f), 0.05f, 0.05f, 0.01f, eyeRed);

                Raylib.DrawCube(headPos + new Vector3(-0.10f, -0.03f, 0.205f), 0.04f, 0.04f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(-0.03f, -0.03f, 0.205f), 0.04f, 0.04f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(0.03f, -0.03f, 0.205f), 0.04f, 0.04f, 0.01f, eyeRed);
                Raylib.DrawCube(headPos + new Vector3(0.10f, -0.03f, 0.205f), 0.04f, 0.04f, 0.01f, eyeRed);

                // 8 Суставчатых лап
                for (int i = 0; i < 4; i++) {
                    float zOff = (i - 1.5f) * 0.22f;
                    float legSwing = MathF.Sin(time * 12f + i * 1.2f) * 0.16f;
                    var legL = new Vector3(-0.45f, -0.20f + legSwing, zOff);
                    var legR = new Vector3(0.45f, -0.20f - legSwing, zOff);
                    Raylib.DrawCube(legL, 0.55f, 0.08f, 0.08f, spiderColor);
                    Raylib.DrawCube(legR, 0.55f, 0.08f, 0.08f, spiderColor);
                }

            } else if (h.Type == HostileType.Creeper) {
                // ── CREEPER MODEL (4 шагающие ноги, камуфляж, раздувание) ──
                bool isFlashing = h.FuseTimer > 0f && (int)(h.FuseTimer * 12f) % 2 == 0;
                var baseCreeper = isFlashing ? Color.White : (h.HurtTime > 0f ? new Color(240, 80, 80, 255) : new Color(30, 185, 55, 255));
                var creeperGreen = isFlashing ? ShadeColor(Color.White, light, hPos) : ShadeColor(baseCreeper, light, hPos);
                var outlineColor = ShadeColor(new Color(15, 60, 20, 180), light, hPos);

                float fuseScale = 1.0f + MathF.Min(0.28f, h.FuseTimer * 0.22f);
                Rlgl.Scalef(fuseScale, fuseScale, fuseScale);

                // Body
                Raylib.DrawCube(new Vector3(0f, -0.025f, 0f), 0.5f, 0.65f, 0.35f, creeperGreen);
                Raylib.DrawCubeWires(new Vector3(0f, -0.025f, 0f), 0.502f, 0.652f, 0.352f, outlineColor);

                // Head
                var headPos = new Vector3(0f, 0.54f, 0f);
                Raylib.DrawCube(headPos, 0.48f, 0.48f, 0.48f, creeperGreen);
                Raylib.DrawCubeWires(headPos, 0.482f, 0.482f, 0.482f, outlineColor);

                // Каноничное лицо крипера: черные глаза, переносица и опущенный рот
                var faceBlack = isFlashing ? ShadeColor(Color.White, light, hPos) : ShadeColor(new Color(15, 25, 15, 255), light, hPos);
                // Глаза
                Raylib.DrawCube(headPos + new Vector3(-0.10f, 0.07f, 0.245f), 0.08f, 0.08f, 0.015f, faceBlack);
                Raylib.DrawCube(headPos + new Vector3(0.10f, 0.07f, 0.245f), 0.08f, 0.08f, 0.015f, faceBlack);
                // Переносица
                Raylib.DrawCube(headPos + new Vector3(0f, 0.01f, 0.245f), 0.06f, 0.06f, 0.015f, faceBlack);
                // Рот (перевернутая дуга с уголками вниз)
                Raylib.DrawCube(headPos + new Vector3(0f, -0.04f, 0.245f), 0.16f, 0.05f, 0.015f, faceBlack);
                Raylib.DrawCube(headPos + new Vector3(-0.08f, -0.09f, 0.245f), 0.06f, 0.07f, 0.015f, faceBlack);
                Raylib.DrawCube(headPos + new Vector3(0.08f, -0.09f, 0.245f), 0.06f, 0.07f, 0.015f, faceBlack);

                // 4 шагающие ноги
                float legSwing = walkSwing * 0.3f;
                Raylib.DrawCube(new Vector3(-0.16f, -0.52f, 0.18f + legSwing), 0.18f, 0.36f, 0.18f, creeperGreen);
                Raylib.DrawCube(new Vector3(0.16f, -0.52f, 0.18f - legSwing), 0.18f, 0.36f, 0.18f, creeperGreen);
                Raylib.DrawCube(new Vector3(-0.16f, -0.52f, -0.18f - legSwing), 0.18f, 0.36f, 0.18f, creeperGreen);
                Raylib.DrawCube(new Vector3(0.16f, -0.52f, -0.18f + legSwing), 0.18f, 0.36f, 0.18f, creeperGreen);

            } else if (h.Type == HostileType.ZombiePigman) {
                // ── ZOMBIE PIGMAN (СВИНОЗОМБИ) MODEL ───────────────────────
                var pigSkin = ShadeColor(h.HurtTime > 0f ? new Color(240, 70, 70, 255) : new Color(230, 150, 160, 255), light, hPos);
                var rotGreen = ShadeColor(h.HurtTime > 0f ? new Color(200, 50, 50, 255) : new Color(60, 110, 50, 255), light, hPos);
                var goldSword = ShadeColor(new Color(255, 215, 30, 255), light, hPos);
                var outlineColor = ShadeColor(new Color(30, 20, 20, 180), light, hPos);

                // Body
                Raylib.DrawCube(new Vector3(-0.15f, 0.15f, 0f), 0.3f, 0.70f, 0.35f, pigSkin);
                Raylib.DrawCube(new Vector3(0.15f, 0.15f, 0f), 0.3f, 0.70f, 0.35f, rotGreen);
                Raylib.DrawCubeWires(new Vector3(0f, 0.15f, 0f), 0.602f, 0.702f, 0.352f, outlineColor);

                // Head
                var headPos = new Vector3(0f, 0.74f, 0f);
                Raylib.DrawCube(headPos + new Vector3(-0.12f, 0f, 0f), 0.24f, 0.48f, 0.48f, pigSkin);
                Raylib.DrawCube(headPos + new Vector3(0.12f, 0f, 0f), 0.24f, 0.48f, 0.48f, rotGreen);
                Raylib.DrawCubeWires(headPos, 0.482f, 0.482f, 0.482f, outlineColor);

                // Пятачок
                Raylib.DrawCube(headPos + new Vector3(0f, -0.06f, 0.26f), 0.22f, 0.14f, 0.08f, pigSkin);

                // Руки с золотым мечом
                var leftArm = new Vector3(-0.42f, 0.35f, 0.25f);
                var rightArm = new Vector3(0.42f, 0.35f, 0.25f);
                Raylib.DrawCube(leftArm, 0.2f, 0.2f, 0.55f, pigSkin);
                Raylib.DrawCube(rightArm, 0.2f, 0.2f, 0.55f, rotGreen);

                var swordPos = rightArm + new Vector3(0f, 0.10f, 0.28f);
                Raylib.DrawCube(swordPos, 0.06f, 0.55f, 0.06f, goldSword);

                // Ноги
                Raylib.DrawCube(new Vector3(-0.16f, -0.525f, walkSwing * 0.3f), 0.22f, 0.65f, 0.22f, pigSkin);
                Raylib.DrawCube(new Vector3(0.16f, -0.525f, -walkSwing * 0.3f), 0.22f, 0.65f, 0.22f, rotGreen);

            } else if (h.Type == HostileType.Blaze) {
                // ── BLAZE (ИФРИТ) MODEL (12 огненных парящих стержней) ─────
                var blazeYellow = ShadeColor(h.HurtTime > 0f ? new Color(255, 100, 100, 255) : new Color(255, 210, 40, 255), Vector3.One, hPos);
                var blazeOrange = ShadeColor(h.HurtTime > 0f ? new Color(255, 60, 60, 255) : new Color(240, 110, 20, 255), Vector3.One, hPos);
                var blazeDark = ShadeColor(new Color(90, 30, 10, 255), Vector3.One, hPos);

                float bob = MathF.Sin(time * 3f) * 0.10f;
                var headPos = new Vector3(0f, 0.35f + bob, 0f);
                Raylib.DrawCube(headPos, 0.46f, 0.46f, 0.46f, blazeYellow);
                Raylib.DrawCube(headPos + new Vector3(-0.11f, 0.04f, 0.235f), 0.08f, 0.08f, 0.01f, blazeDark);
                Raylib.DrawCube(headPos + new Vector3(0.11f, 0.04f, 0.235f), 0.08f, 0.08f, 0.01f, blazeDark);

                // 3 яруса стержней
                float rot1 = time * 2.5f, rot2 = -time * 2.0f + 0.5f, rot3 = time * 1.8f + 1.2f;
                for (int i = 0; i < 4; i++) {
                    float a1 = rot1 + i * (MathF.Tau / 4f);
                    Raylib.DrawCube(new Vector3(MathF.Cos(a1) * 0.45f, 0.22f + bob, MathF.Sin(a1) * 0.45f), 0.10f, 0.38f, 0.10f, blazeOrange);
                    float a2 = rot2 + i * (MathF.Tau / 4f);
                    Raylib.DrawCube(new Vector3(MathF.Cos(a2) * 0.40f, -0.15f + bob, MathF.Sin(a2) * 0.40f), 0.10f, 0.38f, 0.10f, blazeOrange);
                    float a3 = rot3 + i * (MathF.Tau / 4f);
                    Raylib.DrawCube(new Vector3(MathF.Cos(a3) * 0.30f, -0.50f + bob, MathF.Sin(a3) * 0.30f), 0.10f, 0.38f, 0.10f, blazeOrange);
                }

            } else if (h.Type == HostileType.Enderman) {
                // ── ENDERMAN MODEL (высокий, тёмно-пурпурный, светящиеся глаза) ─
                var skinColor = ShadeColor(h.HurtTime > 0f ? new Color(80, 20, 90, 255) : new Color(28, 18, 40, 255), light, hPos);
                var headColor = ShadeColor(h.HurtTime > 0f ? new Color(80, 20, 90, 255) : new Color(22, 14, 32, 255), light, hPos);
                var eyeColor = ShadeColor(new Color(200, 90, 255, 255), Vector3.One, hPos);

                // Ноги
                Raylib.DrawCube(new Vector3(-0.13f, -0.68f, walkSwing * 0.3f), 0.16f, 0.95f, 0.16f, skinColor);
                Raylib.DrawCube(new Vector3(0.13f, -0.68f, -walkSwing * 0.3f), 0.16f, 0.95f, 0.16f, skinColor);

                // Туловище
                Raylib.DrawCube(new Vector3(0f, 0.18f, 0f), 0.42f, 0.72f, 0.24f, skinColor);

                // Руки
                var leftArm = new Vector3(-0.31f, 0.06f, walkSwing * 0.3f);
                var rightArm = new Vector3(0.31f, 0.06f, -walkSwing * 0.3f);
                Raylib.DrawCube(leftArm, 0.12f, 1.05f, 0.12f, skinColor);
                Raylib.DrawCube(rightArm, 0.12f, 1.05f, 0.12f, skinColor);

                // Голова + глаза
                var headPos = new Vector3(0f, 0.92f, 0f);
                Raylib.DrawCube(headPos, 0.42f, 0.44f, 0.42f, headColor);
                Raylib.DrawCube(headPos + new Vector3(-0.11f, 0.06f, 0.215f), 0.08f, 0.08f, 0.012f, eyeColor);
                Raylib.DrawCube(headPos + new Vector3(0.11f, 0.06f, 0.215f), 0.08f, 0.08f, 0.012f, eyeColor);

            } else if (h.Type == HostileType.NetherLord) {
                // ── МИНИ-БОСС: Повелитель Ада ─────────────────────────────
                var demonSkin = ShadeColor(h.HurtTime > 0f ? new Color(220, 60, 50, 255) : new Color(120, 30, 20, 255), light, hPos);
                var demonHot = ShadeColor(h.HurtTime > 0f ? new Color(255, 150, 60, 255) : new Color(210, 90, 40, 255), Vector3.One, hPos);
                var demonEye = ShadeColor(new Color(255, 210, 80, 255), Vector3.One, hPos);

                Raylib.DrawCube(new Vector3(-0.20f, -0.95f, walkSwing * 0.3f), 0.24f, 0.70f, 0.24f, demonSkin);
                Raylib.DrawCube(new Vector3(0.20f, -0.95f, -walkSwing * 0.3f), 0.24f, 0.70f, 0.24f, demonSkin);
                Raylib.DrawCube(new Vector3(0f, -0.20f, 0f), 0.55f, 0.90f, 0.35f, demonSkin);

                // Голова с рогами
                var dHead = new Vector3(0f, 0.55f, 0f);
                Raylib.DrawCube(dHead, 0.40f, 0.40f, 0.40f, demonSkin);
                Raylib.DrawCube(dHead + new Vector3(-0.14f, 0.26f, 0f), 0.06f, 0.20f, 0.06f, demonHot);
                Raylib.DrawCube(dHead + new Vector3(0.14f, 0.26f, 0f), 0.06f, 0.20f, 0.06f, demonHot);
                Raylib.DrawCube(dHead + new Vector3(-0.10f, 0.05f, 0.205f), 0.07f, 0.06f, 0.015f, demonEye);
                Raylib.DrawCube(dHead + new Vector3(0.10f, 0.05f, 0.205f), 0.07f, 0.06f, 0.015f, demonEye);

                // Огненные угли вокруг босса
                for (int i = 0; i < 4; i++) {
                    float a = time * 2f + i * 1.57f;
                    var ember = new Vector3(MathF.Cos(a) * 0.55f, -0.4f + MathF.Sin(time * 3f + i) * 0.3f, MathF.Sin(a) * 0.55f);
                    Raylib.DrawCube(ember, 0.08f, 0.08f, 0.08f, demonHot);
                }

            } else if (h.Type == HostileType.SwampGuardian) {
                // ── МИНИ-БОСС: Болотный страж (токсичные наросты и грибы) ───
                var swampBody = ShadeColor(h.HurtTime > 0f ? new Color(150, 220, 90, 255) : new Color(70, 130, 60, 255), light, hPos);
                var swampDark = ShadeColor(h.HurtTime > 0f ? new Color(80, 140, 50, 255) : new Color(35, 75, 35, 255), light, hPos);
                var swampEye = ShadeColor(new Color(255, 240, 150, 255), Vector3.One, hPos);
                float squish = 1f + MathF.Sin(time * 4f) * 0.08f;

                Raylib.DrawCube(new Vector3(0f, -0.1f, 0f), 0.9f, 0.55f * squish, 0.9f, swampBody);
                Raylib.DrawCube(new Vector3(0f, 0.32f, 0f), 0.8f, 0.42f * squish, 0.8f, swampBody);

                // Грибы на спине
                Raylib.DrawCube(new Vector3(-0.25f, 0.54f, -0.2f), 0.18f, 0.18f, 0.18f, new Color(220, 40, 40, 255));
                Raylib.DrawCube(new Vector3(0.25f, 0.52f, -0.15f), 0.15f, 0.15f, 0.15f, new Color(160, 110, 60, 255));

                Raylib.DrawCube(new Vector3(-0.2f, 0.42f, 0.41f), 0.12f, 0.09f, 0.02f, swampEye);
                Raylib.DrawCube(new Vector3(0.2f, 0.42f, 0.41f), 0.12f, 0.09f, 0.02f, swampEye);

            } else if (h.Type == HostileType.DesertGuardian) {
                // ── МИНИ-БОСС: Страж пустыни (золотой фараон-голем) ────────
                var golemSand = ShadeColor(h.HurtTime > 0f ? new Color(240, 200, 130, 255) : new Color(210, 190, 140, 255), light, hPos);
                var golemGold = ShadeColor(h.HurtTime > 0f ? new Color(255, 230, 90, 255) : new Color(200, 170, 60, 255), light, hPos);
                var golemEye = ShadeColor(new Color(255, 90, 40, 255), Vector3.One, hPos);
                var outline3 = ShadeColor(new Color(90, 70, 30, 200), light, hPos);

                Raylib.DrawCube(new Vector3(-0.25f, -0.55f, walkSwing * 0.3f), 0.3f, 0.5f, 0.3f, golemSand);
                Raylib.DrawCube(new Vector3(0.25f, -0.55f, -walkSwing * 0.3f), 0.3f, 0.5f, 0.3f, golemSand);
                Raylib.DrawCube(new Vector3(0f, 0.0f, 0f), 0.7f, 0.8f, 0.45f, golemSand);
                Raylib.DrawCube(new Vector3(0f, 0.05f, 0.23f), 0.3f, 0.25f, 0.03f, golemGold);

                var gHead = new Vector3(0f, 0.65f, 0f);
                Raylib.DrawCube(gHead, 0.5f, 0.5f, 0.5f, golemSand);
                Raylib.DrawCubeWires(gHead, 0.502f, 0.502f, 0.502f, outline3);
                Raylib.DrawCube(gHead + new Vector3(0f, 0.28f, 0f), 0.36f, 0.08f, 0.36f, golemGold);
                // Глаза
                Raylib.DrawCube(gHead + new Vector3(-0.12f, 0.04f, 0.26f), 0.08f, 0.08f, 0.02f, golemEye);
                Raylib.DrawCube(gHead + new Vector3(0.12f, 0.04f, 0.26f), 0.08f, 0.08f, 0.02f, golemEye);
            }

            Rlgl.PopMatrix();
        }

        // 3.1 Draw End Slime boss (Слизень Края)
        if (_world.EndBoss is { Alive: true } boss) {
            var bl = GetLightFactor(boss.Position);
            float squish = 1f + MathF.Sin(time * 6f) * 0.12f;
            float bodyW = EndSlime.HalfSizeXZ * 2f;
            float bodyH = EndSlime.HalfSizeY * 2f * squish;
            var baseCenter = boss.Position - new Vector3(0f, EndSlime.HalfSizeY, 0f);
            var bodyPos = baseCenter + new Vector3(0f, bodyH * 0.5f, 0f);
            bool hurt = boss.HurtTime > 0f;
            var green = hurt ? new Color(220, 70, 70, 235) : new Color(70, 160, 140, 240);
            var greenDark = hurt ? new Color(170, 40, 40, 235) : new Color(45, 110, 95, 240);
            var eyeColor = ShadeColor(new Color(200, 90, 255, 255), bl, boss.Position);

            // Во время отдыха (окно уязвимости) босс «сдувается» и желтеет — видно окно атаки
            if (boss.IsResting) {
                squish *= 0.82f;
                bodyH = EndSlime.HalfSizeY * 2f * squish;
                green = new Color(150, 175, 70, 240);
                greenDark = new Color(105, 125, 45, 240);
            }

            DrawSoftShadow(baseCenter, bodyW * 0.6f);

            // Внешняя оболочка
            Raylib.DrawCube(bodyPos, bodyW, bodyH, bodyW, ShadeColor(green, bl, boss.Position));
            // Внутренний гель (полупрозрачнее, светлее)
            Raylib.DrawCube(bodyPos, bodyW * 0.72f, bodyH * 0.72f, bodyW * 0.72f, ShadeColor(greenDark, bl, boss.Position));

            // Фиолетовые глаза на лицевой стороне
            float eyeY = boss.Position.Y + EndSlime.HalfSizeY * 0.15f;
            float eyeFz = bodyW * 0.55f;
            Raylib.DrawCube(boss.Position + new Vector3(-bodyW * 0.25f, 0.25f, eyeFz), 0.5f, 0.5f, 0.12f, eyeColor);
            Raylib.DrawCube(boss.Position + new Vector3(bodyW * 0.25f, 0.25f, eyeFz), 0.5f, 0.5f, 0.12f, eyeColor);
        }

        // 3.1.5 Draw True End Slime boss (Истинный Слизень Края в Бездне)
        if (_world.TrueVoidBoss is { Alive: true } tBoss) {
            var bl = GetLightFactor(tBoss.Position);
            float squish = 1f + MathF.Sin(time * 7f) * 0.15f;
            float bodyW = TrueEndSlime.HalfSizeXZ * 2f;
            float bodyH = TrueEndSlime.HalfSizeY * 2f * squish;
            var baseCenter = tBoss.Position - new Vector3(0f, TrueEndSlime.HalfSizeY, 0f);
            var bodyPos = baseCenter + new Vector3(0f, bodyH * 0.5f, 0f);
            bool hurt = tBoss.HurtTime > 0f;
            var voidColor = hurt ? new Color(255, 60, 60, 245) : new Color(75, 20, 110, 245);
            var voidCore = hurt ? new Color(220, 30, 30, 250) : new Color(140, 30, 180, 250);
            var eyeColor = ShadeColor(new Color(255, 30, 50, 255), bl, tBoss.Position);

            DrawSoftShadow(baseCenter, bodyW * 0.7f);

            // Внешняя тёмная оболочка Бездны
            Raylib.DrawCube(bodyPos, bodyW, bodyH, bodyW, ShadeColor(voidColor, bl, tBoss.Position));
            // Внутреннее пульсирующее ядро
            Raylib.DrawCube(bodyPos, bodyW * 0.75f, bodyH * 0.75f, bodyW * 0.75f, ShadeColor(voidCore, bl, tBoss.Position));

            // Зловещие светящиеся красные глаза
            float eyeFz = bodyW * 0.55f;
            Raylib.DrawCube(tBoss.Position + new Vector3(-bodyW * 0.25f, 0.35f, eyeFz), 0.6f, 0.6f, 0.15f, eyeColor);
            Raylib.DrawCube(tBoss.Position + new Vector3(bodyW * 0.25f, 0.35f, eyeFz), 0.6f, 0.6f, 0.15f, eyeColor);
        }

        // 3.2 End Crystals (сущности — парят, светятся, взрываются от касания)
        foreach (var cry in _world.EndCrystals) {
            if (!cry.Alive) continue;
            float bob = MathF.Sin(time * 2.2f + cry.BobPhase) * 0.07f;
            var cPos = cry.Position + new Vector3(0f, bob, 0f);
            float rot = time * 1.4f + cry.BobPhase;
            var cl = GetLightFactor(cPos);

            // Свечение ядра (аддитивное)
            Raylib.BeginBlendMode(BlendMode.Additive);
            Raylib.DrawCube(cPos, 0.7f, 0.7f, 0.7f, new Color(200, 255, 235, 80));
            Raylib.EndBlendMode();

            // Заметный вращающийся кристалл-«алмаз» из кубов (как в Minecraft)
            Rlgl.PushMatrix();
            Rlgl.Translatef(cPos.X, cPos.Y, cPos.Z);
            Rlgl.Rotatef(rot * 180f / MathF.PI, 0f, 1f, 0f);
            var crystalCol = ShadeColor(new Color(215, 245, 255, 240), cl, cPos);
            var crystalDark = ShadeColor(new Color(140, 185, 210, 240), cl, cPos);
            var crystalCore = ShadeColor(new Color(255, 255, 255, 255), cl, cPos);

            Raylib.DrawCube(Vector3.Zero, 0.46f, 0.46f, 0.46f, crystalCore);
            Raylib.DrawCube(new Vector3(0f, 0.50f, 0f), 0.30f, 0.32f, 0.30f, crystalCol);
            Raylib.DrawCube(new Vector3(0f, -0.50f, 0f), 0.30f, 0.32f, 0.30f, crystalCol);
            Raylib.DrawCube(new Vector3(0.50f, 0f, 0f), 0.32f, 0.30f, 0.30f, crystalCol);
            Raylib.DrawCube(new Vector3(-0.50f, 0f, 0f), 0.32f, 0.30f, 0.30f, crystalCol);
            Raylib.DrawCube(new Vector3(0f, 0f, 0.50f), 0.30f, 0.30f, 0.32f, crystalDark);
            Raylib.DrawCube(new Vector3(0f, 0f, -0.50f), 0.30f, 0.30f, 0.32f, crystalDark);

            // Вращающееся кольцо из 4 кубов
            float ringRot = rot * 1.7f;
            for (int i = 0; i < 4; i++) {
                float a = ringRot + i * (MathF.PI / 2f);
                var ringPos = new Vector3(MathF.Cos(a) * 0.62f, 0f, MathF.Sin(a) * 0.62f);
                Raylib.DrawCube(ringPos, 0.26f, 0.12f, 0.26f, crystalCol);
            }
            Rlgl.PopMatrix();
        }

        // 3.3 Телеграф ударной волны Слизня Края: пульсирующее красное кольцо на земле
        if (_world.EndBoss is { Alive: true, IsDying: false } slamBoss && slamBoss.SlamWarningTimer > 0f) {
            float t = slamBoss.SlamWarningTimer / 0.6f;                 // 1 → 0
            byte alpha = (byte)(150 + 80 * MathF.Sin(time * 30f));      // быстрый пульс
            float radius = EndSlime.HalfSizeXZ + 3.2f + (1f - t) * 1.2f;
            var ground = FindGroundBelow(slamBoss.Position);
            if (ground.HasValue) {
                var gc = ground.Value;
                Raylib.DrawCircle3D(new Vector3(gc.X, gc.Y + 0.03f, gc.Z), radius, new Vector3(1, 0, 0), 90f,
                    new Color((byte)255, (byte)60, (byte)30, alpha));
                Raylib.DrawCircle3D(new Vector3(gc.X, gc.Y + 0.03f, gc.Z), radius * 0.62f, new Vector3(1, 0, 0), 90f,
                    new Color((byte)255, (byte)120, (byte)40, (byte)(alpha * 0.6f)));
            }
        }

        // 3.4 Телеграф сингулярности Истинного Слизня: фиолетовое сжимающееся кольцо вокруг игрока
        if (_world.TrueVoidBoss is { Alive: true, IsDying: false } tSlam && tSlam.SingularityWarningTimer > 0f) {
            float t = tSlam.SingularityWarningTimer / 0.6f;             // 1 → 0
            float radius = 4.5f * t + 1.2f;                             // сжимается к игроку
            byte alpha = (byte)(140 + 80 * MathF.Sin(time * 30f));
            var ground = FindGroundBelow(_session.Player.Position);
            if (ground.HasValue) {
                var gp = ground.Value;
                Raylib.DrawCircle3D(new Vector3(gp.X, gp.Y + 0.03f, gp.Z), radius, new Vector3(1, 0, 0), 90f,
                    new Color((byte)220, (byte)40, (byte)255, alpha));
                Raylib.DrawCircle3D(new Vector3(gp.X, gp.Y + 0.03f, gp.Z), radius * 0.55f, new Vector3(1, 0, 0), 90f,
                    new Color((byte)255, (byte)100, (byte)255, (byte)(alpha * 0.6f)));
            }
        }

        // 4. Draw Flying Arrows / Жемчуг / Око Эндера with lighting and fog
        foreach (var arr in _world.Arrows) {
            if (!arr.Alive) continue;
            var arrLight = GetLightFactor(arr.Position);
            var fwd = arr.Velocity.LengthSquared() > 0.01f ? Vector3.Normalize(arr.Velocity) : Vector3.UnitZ;
            Color bodyCol, tipCol, backCol;
            if (arr.IsSlimeSpit) {
                bodyCol = new Color(90, 200, 70, 255);    // плевок Слизня Края: тёмно-зелёный
                tipCol = new Color(160, 255, 120, 255);
                backCol = new Color(40, 110, 30, 255);
            } else if (arr.IsEnderPearl) {
                bodyCol = new Color(40, 200, 150, 255);   // жемчужно-зелёный
                tipCol = new Color(150, 255, 210, 255);
                backCol = new Color(20, 110, 85, 255);
            } else if (arr.IsEyeOfEnder) {
                bodyCol = new Color(200, 60, 60, 255);    // око: красное тело, янтарное яблоко
                tipCol = new Color(255, 200, 60, 255);
                backCol = new Color(110, 25, 20, 255);
            } else {
                bodyCol = new Color(175, 140, 95, 255);
                tipCol = new Color(190, 190, 190, 255);
                backCol = new Color(245, 245, 245, 255);
            }
            Raylib.DrawCube(arr.Position, 0.08f, 0.08f, 0.55f, ShadeColor(bodyCol, arrLight, arr.Position));
            Raylib.DrawCube(arr.Position + fwd * 0.26f, 0.12f, 0.12f, 0.12f, ShadeColor(tipCol, arrLight, arr.Position));
            Raylib.DrawCube(arr.Position - fwd * 0.24f, 0.14f, 0.14f, 0.14f, ShadeColor(backCol, arrLight, arr.Position));
        }

        // 5. Falling blocks with lighting and fog
        foreach (var f in _world.FallingBlocks) {
            if (!f.Alive) continue;
            var fallLight = GetLightFactor(f.Position);
            var tint = ShadeColor(BlockTint(f.Block.Id), fallLight, f.Position);
            Raylib.DrawCube(f.Position, 1.0f, 1.0f, 1.0f, tint);
            Raylib.DrawCubeWires(f.Position, 1.002f, 1.002f, 1.002f, ShadeColor(new Color(20, 20, 25, 180), fallLight, f.Position));
        }

    }

    /// <summary>
    /// Выброшенные предметы (billboard'ы) — рисуются ПОСЛЕДНИМИ в кадре (после неба,
    /// облаков и всех сущностей). Включён depth-тест: непрозрачные пиксели корректно
    /// закрывают то, что позади, а прозрачные смешиваются с ним (моб/облако видны
    /// сквозь прозрачную часть предмета), потому что ничто не рисуется после них.
    /// </summary>
    public void DrawPickups(Camera3D camera) {
        Rlgl.EnableDepthTest();
        foreach (var p in _world.Pickups) {
            if (p.Quantity <= 0) continue;

            DrawSoftShadow(p.Position, 0.22f);

            float bob = MathF.Sin(p.BobPhase + (float)Raylib.GetTime() * 3f) * 0.12f;
            var pos = p.Position + new Vector3(0f, bob + 0.25f, 0f);
            byte tile = TextureAtlas.ItemTile(p.Definition.Id);
            var src = TextureAtlas.TilePixelRect(tile);

            var light = GetLightFactor(p.Position);
            Color tint = ShadeColor(Color.White, light, p.Position);
            Raylib.DrawBillboardRec(camera, TextureAtlas.Atlas, src, pos, new Vector2(0.4f, 0.4f), tint);
        }
    }

    /// <summary>Ищет твёрдую землю под точкой (до 6 блоков вниз); null — если не нашли.</summary>
    private Vector3? FindGroundBelow(Vector3 pos) {
        int px = (int)MathF.Floor(pos.X);
        int pz = (int)MathF.Floor(pos.Z);
        int startY = (int)MathF.Floor(pos.Y);
        for (int y = startY; y >= startY - 6; y--) {
            if (_world.IsSolidAt(new Vec3i(px, y, pz))) {
                return new Vector3(pos.X, y + 1.0f, pos.Z);
            }
        }
        return null;
    }

    private void DrawSoftShadow(Vector3 pos, float baseRadius) {
        if (!SaveSystem.EntityShadows) return;
        float fogFactor = GetFogFactor(pos);
        if (fogFactor >= 0.95f) return;
        float maxDistSq = (SaveSystem.RenderDistanceSetting * 16f) * (SaveSystem.RenderDistanceSetting * 16f);
        if (Vector3.DistanceSquared(pos, _session.Camera.Position) > maxDistSq) return;

        float groundY = pos.Y;
        int px = (int)MathF.Floor(pos.X);
        int pz = (int)MathF.Floor(pos.Z);
        int startY = (int)MathF.Floor(pos.Y);
        for (int y = startY; y >= startY - 3; y--) {
            if (_world.IsSolidAt(new Vec3i(px, y, pz))) {
                groundY = y + 1.01f;
                break;
            }
        }
        
        float dist = pos.Y - groundY;
        if (dist > 3.5f || dist < -0.2f) return;
        
        float scale = Math.Clamp(1.0f - dist / 3.5f, 0f, 1f);
        float radius = baseRadius * scale;
        if (radius < 0.05f) return;
        
        byte alpha = (byte)(70 * scale * (1f - fogFactor));
        if (alpha < 2) return;
        Raylib.DrawCircle3D(new Vector3(pos.X, groundY, pos.Z), radius, new Vector3(1, 0, 0), 90f, new Color((byte)0, (byte)0, (byte)0, alpha));
    }
}
