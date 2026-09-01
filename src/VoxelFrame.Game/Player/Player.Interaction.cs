using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

public sealed partial class Player {
    private static readonly Random DropRng = new();

    // ── Ломание блоков ───────────────────────────────────────────────────────

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
                    world.SpawnPickup(GameData.WheatItem.Id, 1, pos);
                    int seedCount = DropRng.Next(1, 4);
                    world.SpawnPickup(GameData.WheatSeedsItem.Id, seedCount, pos);
                } else {
                    world.SpawnPickup(GameData.WheatSeedsItem.Id, 1, pos);
                }
            } else if (block.Id == GameData.BCarrotCrop.Id) {
                int stage = oldVox.SubGridLayerMask;
                if (stage >= 3) {
                    int count = DropRng.Next(2, 5);
                    world.SpawnPickup(GameData.CarrotItem.Id, count, pos);
                } else {
                    world.SpawnPickup(GameData.CarrotItem.Id, 1, pos);
                }
            } else if (block.Id == GameData.BPotatoCrop.Id) {
                int stage = oldVox.SubGridLayerMask;
                if (stage >= 3) {
                    int count = DropRng.Next(2, 5);
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
                    world.SpawnPickup(GameData.OakSaplingItem.Id, 1, pos);
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
                if (block.Id == GameData.BLog.Id && DropRng.NextDouble() < 0.35) {
                    world.SpawnPickup(GameData.SawdustItem.Id, 1, pos);
                }
            }
        }
    }

    // ── Установка блоков ─────────────────────────────────────────────────────

    public bool TryConsumeSelected(ItemDefinition item, int qty = 1, GameSession? session = null) {
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

        byte facing = 0;
        Vec3i forwardH;
        if (MathF.Abs(Forward.X) > MathF.Abs(Forward.Z)) {
            if (Forward.X > 0) { facing = 3; forwardH = new Vec3i(1, 0, 0); }
            else { facing = 1; forwardH = new Vec3i(-1, 0, 0); }
        } else {
            if (Forward.Z > 0) { facing = 2; forwardH = new Vec3i(0, 0, 1); }
            else { facing = 0; forwardH = new Vec3i(0, 0, -1); }
        }

        if (item.Id == GameData.DoorItem.Id) {
            var above = cell + new Vec3i(0, 1, 0);
            if (world.IsSolidAt(above) || world.GetVoxel(above).TypeId != 0) return false;
            if (!world.IsSolidAt(cell + new Vec3i(0, -1, 0))) return false;

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

        if (block.Id == GameData.BBed.Id) {
            if (session.TargetBlock.Y >= cell.Y) return false;

            var headCell = cell + forwardH;
            var exFoot = world.GetVoxel(cell);
            var exHead = world.GetVoxel(headCell);
            if (exFoot.TypeId != 0 || exHead.TypeId != 0)
                return false;
            if (!world.IsSolidAt(cell + new Vec3i(0, -1, 0)) || !world.IsSolidAt(headCell + new Vec3i(0, -1, 0)))
                return false;

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

        if (block.Id == GameData.BTorch.Id) {
            var hitNormal = cell - session.TargetBlock;
            byte torchFacing = 0;
            if (hitNormal.X == 1) torchFacing = 2;
            else if (hitNormal.X == -1) torchFacing = 1;
            else if (hitNormal.Z == 1) torchFacing = 4;
            else if (hitNormal.Z == -1) torchFacing = 3;

            if (TryConsumeSelected(item, 1, session)) {
                world.PlacePlacedBlock(cell, block, torchFacing);
                SoundSystem.PlayPlace();
                GameClient.Active?.SendBlockChange(cell.X, cell.Y, cell.Z, block.Id, torchFacing, isBreak: false, (byte)session.World.Dimension);
                GameServer.Active?.BroadcastHostBlockChange(cell.X, cell.Y, cell.Z, block.Id, torchFacing, isBreak: false);
                return true;
            }
            return false;
        }

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

    // ── Порталы ──────────────────────────────────────────────────────────────

    public static bool IsInAnyPortal(GameWorld world, Vector3 playerPos, ushort portalId) {
        int minX = (int)MathF.Floor(playerPos.X - 0.3f);
        int maxX = (int)MathF.Floor(playerPos.X + 0.3f);
        int minY = (int)MathF.Floor(playerPos.Y);
        int maxY = (int)MathF.Floor(playerPos.Y + 1.7f);
        int minZ = (int)MathF.Floor(playerPos.Z - 0.3f);
        int maxZ = (int)MathF.Floor(playerPos.Z + 0.3f);

        for (int y = minY; y <= maxY; y++) {
            for (int x = minX; x <= maxX; x++) {
                for (int z = minZ; z <= maxZ; z++) {
                    if (world.GetVoxel(new Vec3i(x, y, z)).TypeId == portalId) {
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
                            var p = new Vec3i(ix, minY + y, iz);
                            world.PlacePlacedBlock(p, GameData.BNetherPortal);
                            GameClient.Active?.SendBlockChange(p.X, p.Y, p.Z, GameData.BNetherPortal.Id, 0, false, (byte)world.Dimension);
                            GameServer.Active?.BroadcastHostBlockChange(p.X, p.Y, p.Z, GameData.BNetherPortal.Id, 0, false);
                        }
                    }
                    return true;
                }
            }
        }
        return false;
    }

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
        if (maxX - minX != 3 || maxZ - minZ != 3) return false;

        foreach (var f in frames) {
            if ((world.GetVoxel(f).SubGridLayerMask & 1) == 0) return false;
        }

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
}
