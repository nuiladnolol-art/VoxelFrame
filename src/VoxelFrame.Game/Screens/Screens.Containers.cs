using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.Materials;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

public static partial class Screens {
    // ── Экран печки (Автономная фоновая плавка) ───────────────────────────────

    public static void DrawFurnaceUI(GameSession session) {
        int w = Ui.Vw, h = Ui.Vh;
        var inv = session.Player.Inventory;
        var mouse = Ui.Mouse();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool rightClick = Raylib.IsMouseButtonPressed(MouseButton.Right);

        var furnace = session.World.GetOrCreateFurnace(session.ActiveFurnacePos);

        const int slotSz = 52;
        int panelW = 9 * 56 + 32;
        int panelH = 4 * 56 + 180;
        float px = w / 2f - panelW / 2f, py = (h - panelH) / 2f;
        DrawPanel(px, py, panelW, panelH);

        Fonts.DrawCentered("ПЕЧКА", w / 2f, py + 8f, 26f, TextDark);
        Fonts.DrawCentered("Сырьё (вверху) + Топливо (внизу) → Результат (справа)", w / 2f, py + 36f, 13f, new Color(90, 90, 90, 255));

        float centerX = w / 2f;
        float topY = py + 65f;

        // 1. Слот сырья (Input)
        float inSlotX = centerX - slotSz - 35f;
        var inRect = new Rectangle(inSlotX, topY, slotSz, slotSz);
        bool inHov = Raylib.CheckCollisionPointRec(mouse, inRect);
        Raylib.DrawRectangleRounded(inRect, 0.12f, 6, inHov ? SlotHover : SlotBg);
        Raylib.DrawRectangleRoundedLinesEx(inRect, 0.12f, 6, 1.5f, SlotBorder);
        if (furnace.Input.HasValue && furnace.Input.Value.Quantity > 0) {
            var item = furnace.Input.Value;
            Hud.DrawItemIcon(item.Item.Definition, new Rectangle(inSlotX + 3f, topY + 3f, slotSz - 6f, slotSz - 6f), 1f);
            if (item.Quantity > 1) Fonts.DrawShadowed($"×{item.Quantity}", inSlotX + 4f, topY + slotSz - 16f, 14f, Color.White);
        }
        Fonts.DrawCentered("Сырьё", inSlotX + slotSz/2f, topY - 14f, 12f, new Color(200, 200, 200, 255));

        // 2. Слот топлива (Fuel)
        float fuelSlotY = topY + slotSz + 24f;
        var fuelRect = new Rectangle(inSlotX, fuelSlotY, slotSz, slotSz);
        bool fuelHov = Raylib.CheckCollisionPointRec(mouse, fuelRect);
        Raylib.DrawRectangleRounded(fuelRect, 0.12f, 6, fuelHov ? SlotHover : SlotBg);
        Raylib.DrawRectangleRoundedLinesEx(fuelRect, 0.12f, 6, 1.5f, SlotBorder);
        if (furnace.Fuel.HasValue && furnace.Fuel.Value.Quantity > 0) {
            var fuel = furnace.Fuel.Value;
            Hud.DrawItemIcon(fuel.Item.Definition, new Rectangle(inSlotX + 3f, fuelSlotY + 3f, slotSz - 6f, slotSz - 6f), 1f);
            if (fuel.Quantity > 1) Fonts.DrawShadowed($"×{fuel.Quantity}", inSlotX + 4f, fuelSlotY + slotSz - 16f, 14f, Color.White);
        }
        Fonts.DrawCentered("Топливо", inSlotX + slotSz/2f, fuelSlotY + slotSz + 4f, 12f, new Color(200, 200, 200, 255));

        // 3. Анимированное пламя (Flame)
        float flameRatio = furnace.MaxFuelTimer > 0f ? Math.Clamp(furnace.FuelTimer / furnace.MaxFuelTimer, 0f, 1f) : 0f;
        float flameY = topY + slotSz + 2f;
        Fonts.DrawCentered("🔥", inSlotX + slotSz / 2f, flameY, 20f, flameRatio > 0f ? new Color(255, 140, 30, (int)(180 + 75 * flameRatio)) : new Color(60, 60, 60, 180));

        // 4. Стрелка прогресса плавки
        float arrowX = centerX - 10f;
        float arrowY = topY + (slotSz + 24f) / 2f;
        Fonts.Draw("→", arrowX, arrowY, 28f, new Color(200, 200, 200, 255));
        float cookRatio = Math.Clamp(furnace.SmeltTimer / 8f, 0f, 1f);
        if (cookRatio > 0f) {
            Raylib.DrawRectangle((int)arrowX, (int)arrowY + 28, (int)(28f * cookRatio), 4, new Color(255, 200, 50, 255));
        }

        // 5. Слот результата (Output)
        float outSlotX = centerX + 35f;
        float outSlotY = topY + (slotSz + 24f) / 2f - slotSz / 2f + 8f;
        var outRect = new Rectangle(outSlotX, outSlotY, slotSz, slotSz);
        bool outHov = Raylib.CheckCollisionPointRec(mouse, outRect);
        Raylib.DrawRectangleRounded(outRect, 0.12f, 6, outHov ? SlotHover : (furnace.Output != null ? new Color(52, 68, 52, 255) : SlotBg));
        Raylib.DrawRectangleRoundedLinesEx(outRect, 0.12f, 6, 1.5f, furnace.Output != null ? new Color(100, 200, 100, 255) : SlotBorder);
        Fonts.DrawCentered("Результат", outSlotX + slotSz/2f, outSlotY - 14f, 12f, new Color(200, 200, 200, 255));

        if (furnace.Output.HasValue && furnace.Output.Value.Quantity > 0) {
            var outp = furnace.Output.Value;
            Hud.DrawItemIcon(outp.Item.Definition, new Rectangle(outSlotX + 3f, outSlotY + 3f, slotSz - 6f, slotSz - 6f), 1f);
            if (outp.Quantity > 1) Fonts.DrawShadowed($"×{outp.Quantity}", outSlotX + 4f, outSlotY + slotSz - 16f, 14f, Color.White);
        }

        // Взаимодействие со слотом сырья
        if ((leftClick || rightClick) && inHov) {
            HandleFurnaceSlotClick(ref furnace.Input, leftClick, rightClick, id => GameData.SmeltingRecipes.ContainsKey(id));
            NotifyFurnaceChanged(session, furnace);
        }

        // Взаимодействие со слотом топлива
        if ((leftClick || rightClick) && fuelHov) {
            HandleFurnaceSlotClick(ref furnace.Fuel, leftClick, rightClick, id => GameData.IsFuel(id));
            NotifyFurnaceChanged(session, furnace);
        }

        // Взаимодействие со слотом результата (только забирать)
        if (leftClick && outHov && furnace.Output.HasValue && furnace.Output.Value.Quantity > 0) {
            var outp = furnace.Output.Value;
            if (!Held.HasValue || Held.Value.Quantity <= 0) {
                Held = outp;
                furnace.Output = null;
            } else if (Held.Value.Item.Definition.Id == outp.Item.Definition.Id && Held.Value.Quantity + outp.Quantity <= 64) {
                Held = Held.Value with { Quantity = Held.Value.Quantity + outp.Quantity };
                furnace.Output = null;
            }
            NotifyFurnaceChanged(session, furnace);
        }

        // Сетка инвентаря снизу
        float invY = fuelSlotY + slotSz + 25f;
        float invX = px + 16f;
        for (int row = 0; row < 3; row++) {
            for (int col = 0; col < 9; col++) {
                int idx = 9 + row * 9 + col;
                DrawSlot(session, inv, invX + col * 56f, invY + row * 56f, idx, false);
            }
        }
        float hotY = invY + 3 * 56f + 4f;
        for (int col = 0; col < 9; col++)
            DrawSlot(session, inv, invX + col * 56f, hotY, col, col == session.Player.SelectedSlot);

        DrawHeldItem();
        HandleWorkbenchInput(session, inv, invX, invY, hotY);

        if (!Held.HasValue || Held.Value.Quantity <= 0) {
            ItemDefinition? fDef = null;
            if (inHov && furnace.Input.HasValue) { fDef = furnace.Input.Value.Item.Definition; }
            else if (fuelHov && furnace.Fuel.HasValue) { fDef = furnace.Fuel.Value.Item.Definition; }
            else if (outHov && furnace.Output.HasValue) { fDef = furnace.Output.Value.Item.Definition; }
            else {
                for (int row = 0; row < 3; row++) {
                    for (int col = 0; col < 9; col++) {
                        if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(invX + col * 56f, invY + row * 56f, 52, 52))) {
                            var e = inv.Slots[9 + row * 9 + col];
                            if (e.HasValue) { fDef = e.Value.Item.Definition; }
                        }
                    }
                }
                for (int col = 0; col < 9; col++) {
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(invX + col * 56f, hotY, 52, 52))) {
                        var e = inv.Slots[col];
                        if (e.HasValue) { fDef = e.Value.Item.Definition; }
                    }
                }
            }
            if (fDef != null) DrawItemTooltip(fDef, mouse);
        }
    }

    private static void HandleFurnaceSlotClick(ref ItemEntry? slotItem, bool leftClick, bool rightClick, Func<ushort, bool> filter) {
        if (rightClick) {
            if (Held.HasValue && Held.Value.Quantity > 0) {
                if (filter(Held.Value.Item.Definition.Id)) {
                    var held = Held.Value;
                    if (!slotItem.HasValue) {
                        slotItem = new ItemEntry(held.Item, 1);
                        Held = held with { Quantity = held.Quantity - 1 };
                        if (Held.Value.Quantity <= 0) Held = null;
                    } else if (slotItem.Value.Item.Definition == held.Item.Definition && slotItem.Value.Quantity < slotItem.Value.Item.Definition.MaxStack) {
                        slotItem = slotItem.Value with { Quantity = slotItem.Value.Quantity + 1 };
                        Held = held with { Quantity = held.Quantity - 1 };
                        if (Held.Value.Quantity <= 0) Held = null;
                    }
                }
            } else if (slotItem.HasValue && slotItem.Value.Quantity > 0) {
                int qty = slotItem.Value.Quantity;
                int take = (qty + 1) / 2;
                Held = slotItem.Value with { Quantity = take };
                if (qty - take > 0) slotItem = slotItem.Value with { Quantity = qty - take };
                else slotItem = null;
            }
        } else if (leftClick) {
            if (!Held.HasValue || Held.Value.Quantity <= 0) {
                if (slotItem.HasValue) {
                    Held = slotItem;
                    slotItem = null;
                }
            } else if (filter(Held.Value.Item.Definition.Id)) {
                var held = Held.Value;
                if (!slotItem.HasValue) {
                    slotItem = held;
                    Held = null;
                } else if (slotItem.Value.Item.Definition == held.Item.Definition) {
                    int current = slotItem.Value.Quantity;
                    if (current < slotItem.Value.Item.Definition.MaxStack) {
                        int add = Math.Min(slotItem.Value.Item.Definition.MaxStack - current, held.Quantity);
                        slotItem = slotItem.Value with { Quantity = current + add };
                        if (held.Quantity - add > 0) Held = held with { Quantity = held.Quantity - add };
                        else Held = null;
                    }
                } else {
                    var tmp = slotItem;
                    slotItem = held;
                    Held = tmp;
                }
            }
        }
    }

    private static void NotifyFurnaceChanged(GameSession session, FurnaceData furnace) {
        if (GameClient.Active != null) GameClient.Active.SendFurnaceSync(session.ActiveFurnacePos, furnace, (byte)session.World.Dimension);
        if (GameServer.Active != null) GameServer.Active.BroadcastFurnaceSync(session.ActiveFurnacePos, furnace, (byte)session.World.Dimension);
    }

    private static void NotifyChestChanged(GameSession session, Container chest) {
        if (GameClient.Active != null) GameClient.Active.SendChestSync(session.ActiveChestPos, chest, (byte)session.World.Dimension);
        if (GameServer.Active != null) GameServer.Active.BroadcastChestSync(session.ActiveChestPos, chest);
    }

    // ── Экран сундука (27 слотов сундука + 36 слотов игрока) ─────────────────

    public static void DrawChestUI(GameSession session) {
        int w = Ui.Vw, h = Ui.Vh;
        var pInv = session.Player.Inventory;
        var chestInv = session.World.GetOrCreateChest(session.ActiveChestPos);
        var mouse = Ui.Mouse();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool rightClick = Raylib.IsMouseButtonPressed(MouseButton.Right);

        const int slotSz = 52, gap = 4;
        int gridW = 9 * slotSz + 8 * gap;
        int panelW = gridW + 32;
        int panelH = 3 * slotSz + 2 * gap + 24 + 4 * slotSz + 3 * gap + 100;

        float px = w / 2f - panelW / 2f, py = (h - panelH) / 2f;
        DrawPanel(px, py, panelW, panelH);

        Fonts.DrawCentered("СУНДУК", w / 2f, py + 8f, 24f, TextDark);
        Fonts.DrawCentered("Хранилище предметов · ЛКМ / ПКМ", w / 2f, py + 34f, 13f, new Color(90, 90, 90, 255));

        // Кнопки быстрой сортировки и быстрого складывания
        float btnSortW = 104f, btnSortH = 22f;
        var sortBtnRect = new Rectangle(px + panelW - 16f - btnSortW, py + 28f, btnSortW, btnSortH);
        var stackBtnRect = new Rectangle(sortBtnRect.X - 110f, py + 28f, 106f, btnSortH);

        bool sortHov = Raylib.CheckCollisionPointRec(mouse, sortBtnRect);
        bool stackHov = Raylib.CheckCollisionPointRec(mouse, stackBtnRect);

        Raylib.DrawRectangleRounded(sortBtnRect, 0.2f, 4, sortHov ? new Color(60, 75, 95, 255) : new Color(40, 48, 62, 220));
        Raylib.DrawRectangleRoundedLinesEx(sortBtnRect, 0.2f, 4, 1f, sortHov ? new Color(120, 180, 255, 255) : new Color(70, 85, 110, 200));
        Fonts.DrawCentered("Сортировка", sortBtnRect.X + btnSortW / 2f, sortBtnRect.Y + 4f, 12f, sortHov ? Color.White : new Color(200, 215, 235, 220));

        Raylib.DrawRectangleRounded(stackBtnRect, 0.2f, 4, stackHov ? new Color(50, 100, 70, 255) : new Color(32, 65, 45, 220));
        Raylib.DrawRectangleRoundedLinesEx(stackBtnRect, 0.2f, 4, 1f, stackHov ? new Color(100, 220, 130, 255) : new Color(60, 120, 80, 200));
        Fonts.DrawCentered("Сложить всё", stackBtnRect.X + stackBtnRect.Width / 2f, stackBtnRect.Y + 4f, 12f, stackHov ? Color.White : new Color(200, 240, 210, 220));

        if (leftClick) {
            if (sortHov) {
                SortContainer(chestInv);
                session.AddMessage("Сундук отсортирован!");
                NotifyChestChanged(session, chestInv);
            } else if (stackHov) {
                QuickStackToChest(pInv, chestInv, session);
                NotifyChestChanged(session, chestInv);
            }
        }

        float chestY = py + 56f;
        float chestX = px + 16f;

        // 3×9 слотов сундука
        for (int r = 0; r < 3; r++) {
            for (int c = 0; c < 9; c++) {
                int idx = r * 9 + c;
                float sx = chestX + c * (slotSz + gap);
                float sy = chestY + r * (slotSz + gap);
                var rect = new Rectangle(sx, sy, slotSz, slotSz);
                bool hov = Raylib.CheckCollisionPointRec(mouse, rect);

                Raylib.DrawRectangleRounded(rect, 0.12f, 6, hov ? SlotHover : SlotBg);
                Raylib.DrawRectangleRoundedLinesEx(rect, 0.12f, 6, 1.5f, SlotBorder);

                var entry = chestInv.Slots[idx];
                if (entry.HasValue && entry.Value.Quantity > 0) {
                    Hud.DrawItemIcon(entry.Value.Item.Definition, new Rectangle(sx + 3f, sy + 3f, slotSz - 6f, slotSz - 6f), 1f);
                    if (entry.Value.Quantity > 1)
                        Fonts.DrawShadowed($"×{entry.Value.Quantity}", sx + 4f, sy + slotSz - 16f, 14f, Color.White);
                    Hud.DrawItemDurability(entry.Value.Item, rect);
                }

                if ((leftClick || rightClick) && hov) {
                    HandleContainerSlotClick(chestInv, idx, leftClick, rightClick, pInv);
                    NotifyChestChanged(session, chestInv);
                }
            }
        }

        // Инвентарь игрока снизу
        float invY = chestY + 3 * (slotSz + gap) + 20f;
        float invX = px + 16f;
        Fonts.Draw("Инвентарь", invX, invY - 16f, 13f, new Color(200, 200, 200, 255));

        for (int row = 0; row < 3; row++) {
            for (int col = 0; col < 9; col++) {
                int idx = 9 + row * 9 + col;
                DrawSlot(session, pInv, invX + col * (slotSz + gap), invY + row * (slotSz + gap), idx, false);
            }
        }
        float hotY = invY + 3 * (slotSz + gap) + 6f;
        for (int col = 0; col < 9; col++)
            DrawSlot(session, pInv, invX + col * (slotSz + gap), hotY, col, col == session.Player.SelectedSlot);

        DrawHeldItem();

        if (leftClick || rightClick) {
            for (int row = 0; row < 3; row++) {
                for (int col = 0; col < 9; col++) {
                    float sx = invX + col * (slotSz + gap);
                    float sy = invY + row * (slotSz + gap);
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(sx, sy, slotSz, slotSz))) {
                        HandleContainerSlotClick(pInv, 9 + row * 9 + col, leftClick, rightClick, chestInv);
                        NotifyChestChanged(session, chestInv);
                        return;
                    }
                }
            }
            for (int col = 0; col < 9; col++) {
                float sx = invX + col * (slotSz + gap);
                float sy = hotY;
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(sx, sy, slotSz, slotSz))) {
                    HandleContainerSlotClick(pInv, col, leftClick, rightClick, chestInv);
                    NotifyChestChanged(session, chestInv);
                    return;
                }
            }
        }

        if (!Held.HasValue || Held.Value.Quantity <= 0) {
            ItemDefinition? cDef = null;
            for (int r = 0; r < 3; r++) {
                for (int c = 0; c < 9; c++) {
                    int idx = r * 9 + c;
                    float sx = chestX + c * (slotSz + gap);
                    float sy = chestY + r * (slotSz + gap);
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(sx, sy, slotSz, slotSz))) {
                        var e = chestInv.Slots[idx];
                        if (e.HasValue) { cDef = e.Value.Item.Definition; }
                    }
                }
            }
            for (int row = 0; row < 3; row++) {
                for (int col = 0; col < 9; col++) {
                    int idx = 9 + row * 9 + col;
                    float sx = invX + col * (slotSz + gap);
                    float sy = invY + row * (slotSz + gap);
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(sx, sy, slotSz, slotSz))) {
                        var e = pInv.Slots[idx];
                        if (e.HasValue) { cDef = e.Value.Item.Definition; }
                    }
                }
            }
            for (int col = 0; col < 9; col++) {
                float sx = invX + col * (slotSz + gap);
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(sx, hotY, slotSz, slotSz))) {
                    var e = pInv.Slots[col];
                    if (e.HasValue) { cDef = e.Value.Item.Definition; }
                }
            }
            if (cDef != null) DrawItemTooltip(cDef, mouse);
        }
    }

    private static void SortContainer(VoxelFrame.Core.Inventory.Container container) {
        var items = new List<ItemEntry>();
        int limit = Math.Min(27, container.Capacity);
        for (int i = 0; i < limit; i++) {
            if (container.Slots[i] is { } entry && entry.Quantity > 0) {
                items.Add(entry);
                container.Slots[i] = null;
            }
        }
        var merged = new List<ItemEntry>();
        foreach (var entry in items.OrderBy(e => e.Item.Definition.Id)) {
            int toAdd = entry.Quantity;
            for (int j = 0; j < merged.Count && toAdd > 0; j++) {
                if (merged[j].Item.Definition.Id == entry.Item.Definition.Id && merged[j].Quantity < merged[j].Item.Definition.MaxStack) {
                    int space = merged[j].Item.Definition.MaxStack - merged[j].Quantity;
                    int add = Math.Min(space, toAdd);
                    merged[j] = merged[j] with { Quantity = merged[j].Quantity + add };
                    toAdd -= add;
                }
            }
            if (toAdd > 0) {
                merged.Add(entry with { Quantity = toAdd });
            }
        }
        for (int i = 0; i < merged.Count && i < limit; i++) {
            container.InsertAt(i, merged[i]);
        }
        SoundSystem.PlayPop();
    }

    private static void QuickStackToChest(VoxelFrame.Core.Inventory.Container pInv, VoxelFrame.Core.Inventory.Container chestInv, GameSession session) {
        int moved = 0;
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        int startSlot = shift ? 0 : 9;
        int chestCapacity = Math.Min(27, chestInv.Capacity);

        for (int i = startSlot; i < pInv.Capacity; i++) {
            if (pInv.Slots[i] is not { } pe || pe.Quantity <= 0) continue;
            var item = pe.Item;
            int remaining = pe.Quantity;

            for (int ci = 0; ci < chestCapacity && remaining > 0; ci++) {
                if (chestInv.Slots[ci] is { } cs && cs.Item.Definition.Id == item.Definition.Id && cs.Quantity < cs.Item.Definition.MaxStack) {
                    int space = cs.Item.Definition.MaxStack - cs.Quantity;
                    int add = Math.Min(space, remaining);
                    chestInv.InsertAt(ci, cs with { Quantity = cs.Quantity + add });
                    remaining -= add;
                    moved += add;
                }
            }

            for (int ci = 0; ci < chestCapacity && remaining > 0; ci++) {
                if (chestInv.Slots[ci] == null) {
                    int add = Math.Min(item.Definition.MaxStack, remaining);
                    chestInv.InsertAt(ci, new ItemEntry(item, add));
                    remaining -= add;
                    moved += add;
                }
            }

            if (remaining <= 0) {
                pInv.RemoveAt(i);
            } else if (remaining < pe.Quantity) {
                pInv.InsertAt(i, pe with { Quantity = remaining });
            }
        }

        if (moved > 0) {
            SoundSystem.PlayPop();
            session.AddMessage($"Сложено в сундук: {moved} предм.");
        } else {
            session.AddMessage("Нет предметов для перемещения!");
        }
    }

    private static void HandleContainerSlotClick(Container inv, int slotIdx, bool leftClick, bool rightClick, Container? targetTransferInv = null) {
        var slotItem = inv.Slots[slotIdx];

        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        if (shift && !rightClick && slotItem != null && targetTransferInv != null) {
            var item = slotItem.Value;
            int inserted = 0;
            for (int i = 0; i < targetTransferInv.Slots.Length; i++) {
                var s = targetTransferInv.Slots[i];
                if (s.HasValue && s.Value.Item.Definition.Id == item.Item.Definition.Id && s.Value.Quantity < s.Value.Item.Definition.MaxStack) {
                    int space = s.Value.Item.Definition.MaxStack - s.Value.Quantity;
                    int add = Math.Min(space, item.Quantity - inserted);
                    targetTransferInv.InsertAt(i, s.Value with { Quantity = s.Value.Quantity + add });
                    inserted += add;
                    if (inserted >= item.Quantity) break;
                }
            }
            if (inserted < item.Quantity) {
                for (int i = 0; i < targetTransferInv.Slots.Length; i++) {
                    if (!targetTransferInv.Slots[i].HasValue) {
                        int add = Math.Min(item.Item.Definition.MaxStack, item.Quantity - inserted);
                        targetTransferInv.InsertAt(i, new ItemEntry(item.Item, add));
                        inserted += add;
                        if (inserted >= item.Quantity) break;
                    }
                }
            }
            if (inserted >= item.Quantity) {
                inv.RemoveAt(slotIdx);
                SoundSystem.PlayPop();
            } else if (inserted > 0) {
                inv.InsertAt(slotIdx, item with { Quantity = item.Quantity - inserted });
                SoundSystem.PlayPop();
            }
            return;
        }

        if (rightClick) {
            if (Held.HasValue && Held.Value.Quantity > 0) {
                var held = Held.Value;
                if (!slotItem.HasValue) {
                    inv.InsertAt(slotIdx, new ItemEntry(held.Item, 1));
                    Held = held with { Quantity = held.Quantity - 1 };
                    if (Held.Value.Quantity <= 0) Held = null;
                } else if (slotItem.Value.Item.Definition == held.Item.Definition && slotItem.Value.Quantity < slotItem.Value.Item.Definition.MaxStack) {
                    inv.InsertAt(slotIdx, slotItem.Value with { Quantity = slotItem.Value.Quantity + 1 });
                    Held = held with { Quantity = held.Quantity - 1 };
                    if (Held.Value.Quantity <= 0) Held = null;
                }
            } else if (slotItem.HasValue && slotItem.Value.Quantity > 0) {
                int qty = slotItem.Value.Quantity;
                int take = (qty + 1) / 2;
                Held = slotItem.Value with { Quantity = take };
                if (qty - take > 0) inv.InsertAt(slotIdx, slotItem.Value with { Quantity = qty - take });
                else inv.RemoveAt(slotIdx);
            }
        } else if (leftClick) {
            if (!Held.HasValue || Held.Value.Quantity <= 0) {
                if (slotItem.HasValue) {
                    Held = slotItem;
                    inv.RemoveAt(slotIdx);
                }
            } else {
                var held = Held.Value;
                if (!slotItem.HasValue) {
                    inv.InsertAt(slotIdx, held);
                    Held = null;
                } else if (slotItem.Value.Item.Definition == held.Item.Definition) {
                    int current = slotItem.Value.Quantity;
                    if (current < slotItem.Value.Item.Definition.MaxStack) {
                        int add = Math.Min(slotItem.Value.Item.Definition.MaxStack - current, held.Quantity);
                        inv.InsertAt(slotIdx, slotItem.Value with { Quantity = current + add });
                        if (held.Quantity - add > 0) Held = held with { Quantity = held.Quantity - add };
                        else Held = null;
                    }
                } else {
                    inv.InsertAt(slotIdx, held);
                    Held = slotItem;
                }
            }
        }
    }
}
