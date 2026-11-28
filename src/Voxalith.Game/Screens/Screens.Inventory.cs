using Voxalith.Core;
using System.Numerics;
using Raylib_cs;
using Voxalith.Core.Inventory;
using Voxalith.Core.Materials;

namespace Voxalith.Game;
public static partial class Screens {
    // ── Инвентарь (сетка слотов, предмет за курсором) ─────────────────────────

    private static readonly Color SlotBg = new(139, 139, 139, 255); // Инсет слота (#8B8B8B)
    private static readonly Color SlotBorder = new(55, 55, 55, 255); // Тень слота (#373737)
    private static readonly Color SlotHover = new(220, 220, 220, 160);
    private static readonly Color SlotSelected = new(255, 255, 255, 255);

    /// Предмет, который игрок держит «за курсором».
    private static ItemEntry? Held;

    public static void Reset() {
        Held = null;
        // Сетки крафта — статическое состояние: без очистки их содержимое
        // переезжало в следующий мир (дубли предметов).
        ClearCraftingGrids();
    }

    /// Возврат предмета из руки и сеток крафта в инвентарь (при закрытии окна).
    public static void ReturnHeld(GameSession session) {
        var inv = session.Player.Inventory;
        if (Held.HasValue && Held.Value.Quantity > 0) {
            var held = Held.Value;
            if (!inv.TryInsert(held.Item, held.Quantity)) {
                var dropPos = session.Player.Position + new System.Numerics.Vector3(0f, 1.2f, 0f) + session.Player.Forward * 0.4f;
                var pickup = new ItemPickup(held.Item, held.Quantity, dropPos) {
                    PickupDelay = 0.8f,
                    Velocity = session.Player.Forward * 2.0f + new System.Numerics.Vector3(0f, 1.5f, 0f)
                };
                session.World.Pickups.Add(pickup);
                GameClient.Active?.SendDropItem(held.Item.Definition.Id, held.Quantity, held.Item.Durability);
            }
            Held = null;
        }
        for (int i = 0; i < 4; i++) {
            if (PersonalGrid[i].HasValue && PersonalGrid[i]!.Value.Quantity > 0) {
                var it = PersonalGrid[i]!.Value;
                if (!inv.TryInsert(it.Item, it.Quantity)) {
                    var dropPos = session.Player.Position + new System.Numerics.Vector3(0f, 1.2f, 0f) + session.Player.Forward * 0.4f;
                    var pickup = new ItemPickup(it.Item, it.Quantity, dropPos) {
                        PickupDelay = 0.8f,
                        Velocity = session.Player.Forward * 2.0f + new System.Numerics.Vector3(0f, 1.5f, 0f)
                    };
                    session.World.Pickups.Add(pickup);
                    GameClient.Active?.SendDropItem(it.Item.Definition.Id, it.Quantity, it.Item.Durability);
                }
                PersonalGrid[i] = null;
            }
        }
        for (int i = 0; i < 9; i++) {
            if (WorkbenchGrid[i].HasValue && WorkbenchGrid[i]!.Value.Quantity > 0) {
                var it = WorkbenchGrid[i]!.Value;
                if (!inv.TryInsert(it.Item, it.Quantity)) {
                    var dropPos = session.Player.Position + new System.Numerics.Vector3(0f, 1.2f, 0f) + session.Player.Forward * 0.4f;
                    var pickup = new ItemPickup(it.Item, it.Quantity, dropPos) {
                        PickupDelay = 0.8f,
                        Velocity = session.Player.Forward * 2.0f + new System.Numerics.Vector3(0f, 1.5f, 0f)
                    };
                    session.World.Pickups.Add(pickup);
                    GameClient.Active?.SendDropItem(it.Item.Definition.Id, it.Quantity, it.Item.Durability);
                }
                WorkbenchGrid[i] = null;
            }
        }
    }

    public static void ReturnHeld(Voxalith.Core.Inventory.Container inv) {
        if (Held.HasValue && Held.Value.Quantity > 0) {
            var held = Held.Value;
            if (inv.TryInsert(held.Item, held.Quantity)) Held = null;
        }
    }

    public static void DrawInventory(GameSession session) {
        if (session.GameMode == GameMode.Creative) {
            DrawCreativeMenu(session);
            return;
        }

        int w = Ui.Vw, h = Ui.Vh;
        var inv = session.Player.Inventory;
        var mouse = Ui.Mouse();

        int cols = 9, mainRows = 3;
        const int slot = 52, gap = 4;
        int gridW = cols * slot + (cols - 1) * gap; // 500px
        int panelW = 1020;
        int panelH = 340;
        float px = MathF.Max(4f, (w - panelW) / 2f), py = MathF.Max(4f, (h - panelH) / 2f);
        DrawPanel(px, py, panelW, panelH);

        // 1. Слоты экипировки брони слева (Шлем, Нагрудник, Поножи, Ботинки)
        float armorX = px + 18f;
        float armorY = py + 34f;
        Fonts.Draw("Броня", armorX, py + 14f, 15f, TextDark);

        string[] armorLabels = { "Шлем", "Нагрудник", "Поножи", "Ботинки" };
        for (int a = 0; a < 4; a++) {
            float ay = armorY + a * (slot + gap);
            var aRect = new Rectangle(armorX, ay, slot, slot);
            bool aHov = Raylib.CheckCollisionPointRec(mouse, aRect);

            Raylib.DrawRectangleRec(aRect, SlotBg);
            Raylib.DrawRectangle((int)armorX, (int)ay, slot, 2, SlotBorder);
            Raylib.DrawRectangle((int)armorX, (int)ay, 2, slot, SlotBorder);
            Raylib.DrawRectangle((int)armorX, (int)ay + slot - 2, slot, 2, PanelLightBorder);
            Raylib.DrawRectangle((int)armorX + slot - 2, (int)ay, 2, slot, PanelLightBorder);
            if (aHov) Raylib.DrawRectangleRec(aRect, SlotHover);

            if (session.Player.Armor[a] is { } ae && ae.Quantity > 0) {
                Hud.DrawItemIcon(ae.Item.Definition, new Rectangle(armorX + 3f, ay + 3f, slot - 6f, slot - 6f), 1f);
                Hud.DrawItemDurability(ae.Item, aRect);
            } else {
                Fonts.DrawCentered(armorLabels[a], armorX + slot / 2f, ay + slot / 2f - 6f, 11f, new Color(130, 140, 155, 130));
            }
        }

        // 2. Слот второй руки (справа от ботинок или под ними)
        float offhandX = armorX;
        float offhandY = armorY + 4 * (slot + gap) + 6f;
        var offhandRec = new Rectangle(offhandX, offhandY, slot, slot);
        bool offhandHover = Raylib.CheckCollisionPointRec(mouse, offhandRec);
        
        Raylib.DrawRectangleRec(offhandRec, SlotBg);
        Raylib.DrawRectangle((int)offhandX, (int)offhandY, slot, 2, SlotBorder);
        Raylib.DrawRectangle((int)offhandX, (int)offhandY, 2, slot, SlotBorder);
        Raylib.DrawRectangle((int)offhandX, (int)offhandY + slot - 2, slot, 2, PanelLightBorder);
        Raylib.DrawRectangle((int)offhandX + slot - 2, (int)offhandY, 2, slot, PanelLightBorder);
        if (offhandHover) Raylib.DrawRectangleRec(offhandRec, SlotHover);

        if (session.Player.OffhandEntry != null) {
            Hud.DrawItemIcon(session.Player.OffhandEntry.Value.Item.Definition, offhandRec, 0.75f);
            if (session.Player.OffhandEntry.Value.Quantity > 1) {
                Fonts.DrawShadowed($"×{session.Player.OffhandEntry.Value.Quantity}", offhandX + 3f, offhandY + slot - 18f, 14f, Color.White);
            }
            Hud.DrawItemDurability(session.Player.OffhandEntry.Value.Item, offhandRec);
        } else {
            Fonts.DrawCentered("2-я рука", offhandX + slot / 2f, offhandY + slot / 2f - 6f, 10f, new Color(130, 140, 155, 130));
        }

        // 3. Сетка основного инвентаря
        float gridX = armorX + slot + 16f;
        float gridY = py + 34f;
        float hotbarY = gridY + mainRows * (slot + gap) + 12f;

        // Заголовки
        Fonts.Draw("Инвентарь", gridX, py + 14f, 15f, TextDark);
        Fonts.Draw("Панель быстрого доступа", gridX, hotbarY - 16f, 13f, TextDark);

        // Сетка: 3 ряда основного хранилища (записи 9..) + ряд хотбара (0..8).
        for (int row = 0; row < mainRows; row++) {
            for (int col = 0; col < cols; col++) {
                int idx = 9 + row * cols + col;
                DrawSlot(session, inv, gridX + col * (slot + gap), gridY + row * (slot + gap), idx, false);
            }
        }
        for (int col = 0; col < cols; col++)
            DrawSlot(session, inv, gridX + col * (slot + gap), hotbarY, col, col == session.Player.SelectedSlot);

        // 4. Панель крафта и книга рецептов справа
        float rightX = gridX + gridW + 18f;
        float rightW = px + panelW - rightX - 16f;
        DrawCraftPanel(session, rightX, py + 14f, rightW, panelH - 28f);

        // Предмет за курсором
        if (Held.HasValue && Held.Value.Quantity > 0) {
            var held = Held.Value;
            Hud.DrawItemIcon(held.Item.Definition, new Rectangle(mouse.X - 14f, mouse.Y - 14f, 28f, 28f), 1f);
            if (held.Quantity > 1) {
                Fonts.Draw($"×{held.Quantity}", mouse.X - 14f, mouse.Y + 8f, 15f, Color.White);
            }
        }

        HandleHeldInput(session, inv);
        DrawTooltip(session, inv);
    }

    private static void DrawTooltip(GameSession session, Voxalith.Core.Inventory.Container inv) {
        if (Held.HasValue && Held.Value.Quantity > 0) return;

        var mouse = Ui.Mouse();
        const int slot = 52, gap = 4;
        int cols = 9, mainRows = 3;
        int panelW = 1020;
        int panelH = 340;
        float px = MathF.Max(4f, (Ui.Vw - panelW) / 2f);
        float py = MathF.Max(4f, (Ui.Vh - panelH) / 2f);

        float armorX = px + 18f;
        float armorY = py + 34f;
        float gridX = armorX + slot + 16f;
        float gridY = py + 34f;
        float hotbarY = gridY + mainRows * (slot + gap) + 12f;
        float offhandX = armorX;
        float offhandY = armorY + 4 * (slot + gap) + 6f;

        Voxalith.Core.Inventory.ItemEntry? hoveredEntry = null;

        // Броня
        for (int a = 0; a < 4; a++) {
            if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(armorX, armorY + a * (slot + gap), slot, slot))) {
                hoveredEntry = session.Player.Armor[a];
            }
        }

        // Вторая рука
        if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(offhandX, offhandY, slot, slot))) {
            hoveredEntry = session.Player.OffhandEntry;
        }

        // Инвентарь
        for (int row = 0; row < mainRows; row++) {
            for (int col = 0; col < cols; col++) {
                int idx = 9 + row * cols + col;
                var rect = new Rectangle(gridX + col * (slot + gap), gridY + row * (slot + gap), slot, slot);
                if (Raylib.CheckCollisionPointRec(mouse, rect)) {
                    hoveredEntry = inv.Slots[idx];
                }
            }
        }
        for (int col = 0; col < cols; col++) {
            var rect = new Rectangle(gridX + col * (slot + gap), hotbarY, slot, slot);
            if (Raylib.CheckCollisionPointRec(mouse, rect)) {
                hoveredEntry = inv.Slots[col];
            }
        }

        if (hoveredEntry != null) {
            DrawItemTooltip(hoveredEntry.Value.Item.Definition, mouse);
        }
    }

    private static void DrawSlot(GameSession session, Voxalith.Core.Inventory.Container inv, float x, float y, int idx, bool hotbarSelected) {
        var rect = new Rectangle(x, y, 52, 52);
        var mouse = Ui.Mouse();
        bool hovered = Raylib.CheckCollisionPointRec(mouse, rect);

        Raylib.DrawRectangleRec(rect, SlotBg);
        Raylib.DrawRectangle((int)x, (int)y, 52, 2, SlotBorder);
        Raylib.DrawRectangle((int)x, (int)y, 2, 52, SlotBorder);
        Raylib.DrawRectangle((int)x, (int)y + 50, 52, 2, PanelLightBorder);
        Raylib.DrawRectangle((int)x + 50, (int)y, 2, 52, PanelLightBorder);

        if (hovered) Raylib.DrawRectangleRec(rect, SlotHover);
        if (hotbarSelected) Raylib.DrawRectangleLinesEx(rect, 2.5f, SlotSelected);

        if (idx >= 0 && idx < inv.Slots.Length) {
            var entry = inv.Slots[idx];
            if (entry != null) {
                Hud.DrawItemIcon(entry.Value.Item.Definition, new Rectangle(x + 3f, y + 3f, 46f, 46f), 1f);
                if (entry.Value.Quantity > 1) {
                    Fonts.DrawShadowed(Hud.FormatStackQty(entry.Value.Quantity), x + 4f, y + 32f, 15f, Color.White);
                }
                Hud.DrawItemDurability(entry.Value.Item, rect);
            }
        }
        _ = session;
    }

    private static void HandleHeldInput(GameSession session, Voxalith.Core.Inventory.Container inv) {
        var mouse = Ui.Mouse();
        const int slot = 52, gap = 4;
        int cols = 9, mainRows = 3;
        int panelW = 1020;
        int panelH = 340;
        float px = MathF.Max(4f, (Ui.Vw - panelW) / 2f);
        float py = MathF.Max(4f, (Ui.Vh - panelH) / 2f);

        float armorX = px + 18f;
        float armorY = py + 34f;
        float gridX = armorX + slot + 16f;
        float gridY = py + 34f;
        float hotbarY = gridY + mainRows * (slot + gap) + 12f;
        float offhandX = armorX;
        float offhandY = armorY + 4 * (slot + gap) + 6f;

        if (Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right)) {
            bool right = Raylib.IsMouseButtonPressed(MouseButton.Right);
            bool slotHit = false;

            // Клик по слотам брони
            for (int a = 0; a < 4; a++) {
                var aRect = new Rectangle(armorX, armorY + a * (slot + gap), slot, slot);
                if (Raylib.CheckCollisionPointRec(mouse, aRect)) {
                    slotHit = true;
                    if (Held.HasValue && Held.Value.Quantity > 0) {
                        var held = Held.Value;
                        var heldArmorType = GameData.GetArmorType(held.Item.Definition.Id);
                        if (heldArmorType.HasValue && (int)heldArmorType.Value == a) {
                            var oldArmor = session.Player.Armor[a];
                            session.Player.Armor[a] = held;
                            Held = oldArmor;
                            SoundSystem.PlayPop();
                        }
                    } else if (session.Player.Armor[a] != null) {
                        Held = session.Player.Armor[a];
                        session.Player.Armor[a] = null;
                        SoundSystem.PlayPop();
                    }
                    return;
                }
            }

            // Клик по слоту второй руки
            var offhandRec = new Rectangle(offhandX, offhandY, slot, slot);
            if (Raylib.CheckCollisionPointRec(mouse, offhandRec)) {
                slotHit = true;
                if (right) {
                    if (Held.HasValue && Held.Value.Quantity > 0) {
                        var held = Held.Value;
                        if (session.Player.OffhandEntry == null) {
                            session.Player.OffhandEntry = held with { Quantity = 1 };
                            Held = held.Quantity > 1 ? held with { Quantity = held.Quantity - 1 } : null;
                        } else if (session.Player.OffhandEntry.Value.Item.Definition.Id == held.Item.Definition.Id && session.Player.OffhandEntry.Value.Quantity < held.Item.Definition.MaxStack) {
                            session.Player.OffhandEntry = session.Player.OffhandEntry.Value with { Quantity = session.Player.OffhandEntry.Value.Quantity + 1 };
                            Held = held.Quantity > 1 ? held with { Quantity = held.Quantity - 1 } : null;
                        }
                    } else if (session.Player.OffhandEntry != null) {
                        int take = (session.Player.OffhandEntry.Value.Quantity + 1) / 2;
                        Held = session.Player.OffhandEntry.Value with { Quantity = take };
                        session.Player.OffhandEntry = session.Player.OffhandEntry.Value with { Quantity = session.Player.OffhandEntry.Value.Quantity - take };
                        if (session.Player.OffhandEntry.Value.Quantity <= 0) session.Player.OffhandEntry = null;
                    }
                } else {
                    if (!Held.HasValue || Held.Value.Quantity <= 0) {
                        if (session.Player.OffhandEntry != null) {
                            Held = session.Player.OffhandEntry;
                            session.Player.OffhandEntry = null;
                        }
                    } else {
                        var held = Held.Value;
                        if (session.Player.OffhandEntry == null) {
                            session.Player.OffhandEntry = held;
                            Held = null;
                        } else if (session.Player.OffhandEntry.Value.Item.Definition.Id == held.Item.Definition.Id) {
                            int add = Math.Min(session.Player.OffhandEntry.Value.Item.Definition.MaxStack - session.Player.OffhandEntry.Value.Quantity, held.Quantity);
                            session.Player.OffhandEntry = session.Player.OffhandEntry.Value with { Quantity = session.Player.OffhandEntry.Value.Quantity + add };
                            Held = held.Quantity > add ? held with { Quantity = held.Quantity - add } : null;
                        } else {
                            var tmp = session.Player.OffhandEntry;
                            session.Player.OffhandEntry = held;
                            Held = tmp;
                        }
                    }
                }
                SoundSystem.PlayPop();
                return;
            }

            // Клик по основному инвентарю
            for (int row = 0; row < mainRows; row++) {
                for (int col = 0; col < cols; col++) {
                    int idx = 9 + row * cols + col;
                    if (SlotClicked(session, inv, gridX + col * (slot + gap), gridY + row * (slot + gap), idx, right)) {
                        slotHit = true;
                        break;
                    }
                }
                if (slotHit) break;
            }
            if (!slotHit) {
                for (int col = 0; col < cols; col++) {
                    if (SlotClicked(session, inv, gridX + col * (slot + gap), hotbarY, col, right)) {
                        slotHit = true;
                        break;
                    }
                }
            }

            // Клик за пределами окна инвентаря с предметом в руке — выбросить в мир!
            if (!slotHit && Held.HasValue && Held.Value.Quantity > 0) {
                if (!Raylib.CheckCollisionPointRec(mouse, new Rectangle(px, py, panelW, panelH))) {
                    var dropPos = session.Player.Eye + session.Player.Forward * 0.5f;
                    int dropCount = right ? 1 : Held.Value.Quantity;
                    var pickup = new ItemPickup(Held.Value.Item, dropCount, dropPos) {
                        PickupDelay = 1.2f,
                        Velocity = session.Player.Forward * 4.5f + new Vector3(0f, 2.0f, 0f)
                    };
                    session.World.Pickups.Add(pickup);
                    GameClient.Active?.SendDropItem(Held.Value.Item.Definition.Id, dropCount, Held.Value.Item.Durability);
                    if (right && Held.Value.Quantity > 1) {
                        Held = Held.Value with { Quantity = Held.Value.Quantity - 1 };
                    } else {
                        Held = null;
                    }
                    return;
                }
            }
        }

        // Выбрасывание предмета назначенной клавишей (KeyBinds.Drop) при наведении на слот
        if (Raylib.IsKeyPressed(KeyBinds.Drop)) {
            bool ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
            for (int row = 0; row < mainRows; row++) {
                for (int col = 0; col < cols; col++) {
                    int idx = 9 + row * cols + col;
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + col * (slot + gap), gridY + row * (slot + gap), 52, 52))) {
                        var slotEntry = inv.Slots[idx];
                        if (slotEntry.HasValue && slotEntry.Value.Quantity > 0) {
                            var dropPos = session.Player.Eye + session.Player.Forward * 0.5f;
                            int dropCount = ctrl ? slotEntry.Value.Quantity : 1;
                            var pickup = new ItemPickup(slotEntry.Value.Item, dropCount, dropPos) {
                                PickupDelay = 1.2f,
                                Velocity = session.Player.Forward * 4.5f + new Vector3(0f, 2.0f, 0f)
                            };
                            session.World.Pickups.Add(pickup);
                            SoundSystem.PlayPop();
                            if (!ctrl && slotEntry.Value.Quantity > 1) {
                                inv.Slots[idx] = slotEntry.Value with { Quantity = slotEntry.Value.Quantity - 1 };
                            } else {
                                inv.RemoveAt(idx);
                            }
                            return;
                        }
                    }
                }
            }
            for (int col = 0; col < cols; col++) {
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + col * (slot + gap), hotbarY, 52, 52))) {
                    int idx = col;
                    var slotEntry = inv.Slots[idx];
                    if (slotEntry.HasValue && slotEntry.Value.Quantity > 0) {
                        var dropPos = session.Player.Eye + session.Player.Forward * 0.5f;
                        int dropCount = ctrl ? slotEntry.Value.Quantity : 1;
                        var pickup = new ItemPickup(slotEntry.Value.Item, dropCount, dropPos) {
                            PickupDelay = 1.2f,
                            Velocity = session.Player.Forward * 4.5f + new Vector3(0f, 2.0f, 0f)
                        };
                        session.World.Pickups.Add(pickup);
                        SoundSystem.PlayPop();
                        if (!ctrl && slotEntry.Value.Quantity > 1) {
                            inv.Slots[idx] = slotEntry.Value with { Quantity = slotEntry.Value.Quantity - 1 };
                        } else {
                            inv.RemoveAt(idx);
                        }
                        return;
                    }
                }
            }
        }

        // Swapping with hotbar using 1-9 keys when hovering slots
        int hotbarKey = -1;
        for (int k = 0; k < 9; k++) {
            if (Raylib.IsKeyPressed(KeyboardKey.One + k)) {
                hotbarKey = k;
                break;
            }
        }
        if (hotbarKey >= 0) {
            for (int row = 0; row < mainRows; row++) {
                for (int col = 0; col < cols; col++) {
                    int idx = 9 + row * cols + col;
                    if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + col * (slot + gap), gridY + row * (slot + gap), 52, 52))) {
                        var tmp = inv.Slots[idx];
                        inv.Slots[idx] = inv.Slots[hotbarKey];
                        inv.Slots[hotbarKey] = tmp;
                        return;
                    }
                }
            }
            for (int col = 0; col < cols; col++) {
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + col * (slot + gap), hotbarY, 52, 52))) {
                    int idx = col;
                    var tmp = inv.Slots[idx];
                    inv.Slots[idx] = inv.Slots[hotbarKey];
                    inv.Slots[hotbarKey] = tmp;
                    return;
                }
            }
        }
    }

    private static double _lastSlotClickTime = 0;
    private static int _lastSlotClickIndex = -1;

    private static bool SlotClicked(GameSession session, Voxalith.Core.Inventory.Container inv,
                                    float x, float y, int idx, bool rightClick) {
        var rect = new Rectangle(x, y, 52, 52);
        var mouse = Ui.Mouse();
        if (!Raylib.CheckCollisionPointRec(mouse, rect)) return false;

        // Двойной клик ЛКМ: сбор всех одинаковых предметов из инвентаря в курсор (до MaxStack)
        if (!rightClick) {
            double now = Raylib.GetTime();
            bool isDoubleClick = (now - _lastSlotClickTime < 0.35) && (_lastSlotClickIndex == idx);
            _lastSlotClickTime = now;
            _lastSlotClickIndex = idx;

            if (isDoubleClick && Held.HasValue && Held.Value.Quantity < Held.Value.Item.Definition.MaxStack) {
                var heldDef = Held.Value.Item.Definition;
                int maxStack = heldDef.MaxStack;
                for (int i = 0; i < inv.Slots.Length && Held.Value.Quantity < maxStack; i++) {
                    var slotItem = inv.Slots[i];
                    if (slotItem.HasValue && slotItem.Value.Item.Definition == heldDef) {
                        int canTake = Math.Min(maxStack - Held.Value.Quantity, slotItem.Value.Quantity);
                        Held = Held.Value with { Quantity = Held.Value.Quantity + canTake };
                        if (slotItem.Value.Quantity - canTake <= 0) {
                            inv.RemoveAt(i);
                        } else {
                            inv.InsertAt(i, slotItem.Value with { Quantity = slotItem.Value.Quantity - canTake });
                        }
                    }
                }
                SoundSystem.PlayPop();
                return true;
            }
        }

        var entryInSlot = inv.Slots[idx];
        bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        if (shift && !rightClick && entryInSlot != null) {
            var item = entryInSlot.Value;
            var armorType = GameData.GetArmorType(item.Item.Definition.Id);
            if (armorType.HasValue) {
                int aSlot = (int)armorType.Value;
                if (session.Player.Armor[aSlot] == null) {
                    session.Player.Armor[aSlot] = item;
                    inv.RemoveAt(idx);
                    SoundSystem.PlayPop();
                    return true;
                }
            }

            // Shift-Click: быстрое перемещение между хотбаром (0..8) и основным инвентарем (9..35)
            int targetStart = idx < 9 ? 9 : 0;
            int targetEnd = idx < 9 ? 36 : 9;
            inv.RemoveAt(idx);
            int rem = item.Quantity;
            for (int t = targetStart; t < targetEnd && rem > 0; t++) {
                var te = inv.Slots[t];
                if (te != null && te.Value.Item.Definition == item.Item.Definition && te.Value.Quantity < te.Value.Item.Definition.MaxStack) {
                    int add = Math.Min(te.Value.Item.Definition.MaxStack - te.Value.Quantity, rem);
                    inv.InsertAt(t, te.Value with { Quantity = te.Value.Quantity + add });
                    rem -= add;
                }
            }
            for (int t = targetStart; t < targetEnd && rem > 0; t++) {
                if (inv.Slots[t] == null) {
                    int add = Math.Min(item.Item.Definition.MaxStack, rem);
                    inv.InsertAt(t, new ItemEntry(item.Item, add));
                    rem -= add;
                }
            }
            if (rem > 0) {
                inv.InsertAt(idx, item with { Quantity = rem });
            }
            return true;
        }

        if (rightClick) {
            if (Held.HasValue && Held.Value.Quantity > 0) {
                var held = Held.Value;
                // Place 1 from hand
                if (entryInSlot == null) {
                    inv.InsertAt(idx, new ItemEntry(held.Item, 1));
                    Held = held with { Quantity = held.Quantity - 1 };
                    if (Held.Value.Quantity <= 0) Held = null;
                } else if (entryInSlot.Value.Item.Definition == held.Item.Definition) {
                    int current = entryInSlot.Value.Quantity;
                    if (current < entryInSlot.Value.Item.Definition.MaxStack) {
                        inv.InsertAt(idx, entryInSlot.Value with { Quantity = current + 1 });
                        Held = held with { Quantity = held.Quantity - 1 };
                        if (Held.Value.Quantity <= 0) Held = null;
                    }
                }
            } else if (entryInSlot != null) {
                // Take half from slot
                int qty = entryInSlot.Value.Quantity;
                if (qty > 0) {
                    int take = (qty + 1) / 2;
                    Held = entryInSlot.Value with { Quantity = take };
                    inv.RemoveAt(idx);
                    if (qty - take > 0) {
                        inv.InsertAt(idx, entryInSlot.Value with { Quantity = qty - take });
                    }
                }
            }
            return true;
        }

        // Left click
        if (!Held.HasValue || Held.Value.Quantity <= 0) {
            // Take all
            if (entryInSlot != null) {
                Held = entryInSlot;
                inv.RemoveAt(idx);
            }
        } else {
            var heldItem = Held.Value;
            if (entryInSlot == null) {
                // Place all
                inv.InsertAt(idx, heldItem);
                Held = null;
            } else if (entryInSlot.Value.Item.Definition == heldItem.Item.Definition) {
                // Merge all up to MaxStack
                int current = entryInSlot.Value.Quantity;
                if (current < entryInSlot.Value.Item.Definition.MaxStack) {
                    int add = Math.Min(entryInSlot.Value.Item.Definition.MaxStack - current, heldItem.Quantity);
                    inv.InsertAt(idx, entryInSlot.Value with { Quantity = current + add });
                    if (heldItem.Quantity - add > 0) {
                        Held = heldItem with { Quantity = heldItem.Quantity - add };
                    } else {
                        Held = null;
                    }
                } else {
                    // Swap items if stack is already full / max stack == 1
                    inv.RemoveAt(idx);
                    inv.InsertAt(idx, heldItem);
                    Held = entryInSlot;
                }
            } else {
                // Swap items
                inv.RemoveAt(idx);
                inv.InsertAt(idx, heldItem);
                Held = entryInSlot;
            }
        }
        return true;
    }

    // ── Крафт-сетки ──────────────────────────────────────────────────────────

    // Личный 2×2 крафт (в инвентаре)
    private static readonly ItemEntry?[] PersonalGrid = new ItemEntry?[4];
    // 3×3 верстак
    private static readonly ItemEntry?[] WorkbenchGrid = new ItemEntry?[9];

    public static void ClearCraftingGrids() {
        for (int i = 0; i < 4; i++) PersonalGrid[i] = null;
        for (int i = 0; i < 9; i++) WorkbenchGrid[i] = null;
    }

    public static void DrawCrafting(GameSession session) {
        DrawInventory(session);
    }

    // ── Личный 2×2 крафт в инвентаре ─────────────────────────────────────────
    private static void DrawCraftPanel(GameSession session, float panelX, float panelY, float panelW, float panelH) {
        const int slotSz = 44, gap = 4;
        var mouse = Ui.Mouse();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool rightClick = Raylib.IsMouseButtonPressed(MouseButton.Right);

        // Заголовок
        Fonts.Draw("Создание", panelX + 8f, panelY, 16f, TextDark);

        // 2×2 сетка ингредиентов
        float gridX = panelX + 8f;
        float gridY = panelY + 24f;
        for (int r = 0; r < 2; r++) {
            for (int c = 0; c < 2; c++) {
                int idx = r * 2 + c;
                float sx = gridX + c * (slotSz + gap);
                float sy = gridY + r * (slotSz + gap);
                DrawCraftSlot(PersonalGrid, idx, sx, sy, slotSz, mouse, leftClick, rightClick);
            }
        }

        // Стрелка
        float arrowX = gridX + 2 * (slotSz + gap) + 6f;
        float arrowMidY = gridY + (slotSz + gap) / 2f + slotSz / 2f - 8f;
        Fonts.Draw("→", arrowX, arrowMidY, 22f, new Color(80, 80, 80, 255));

        // Слот результата
        float resultX = arrowX + 26f;
        float resultY = gridY + (slotSz + gap) / 2f - slotSz / 2f + 2f;

        // Вычисляем результат 2×2 → expand to 3×3
        var grid3 = new ItemDefinition?[9];
        grid3[0] = PersonalGrid[0]?.Item.Definition; grid3[1] = PersonalGrid[1]?.Item.Definition;
        grid3[3] = PersonalGrid[2]?.Item.Definition; grid3[4] = PersonalGrid[3]?.Item.Definition;
        string key2x2 = GameData.NormalizeGrid(grid3);
        bool hasResult = GameData.ShapeRecipes.TryGetValue(key2x2, out var craftResult2);

        var resultRect = new Rectangle(resultX, resultY, slotSz, slotSz);
        bool resultHovered = Raylib.CheckCollisionPointRec(mouse, resultRect);
        Color resultBg = hasResult ? (resultHovered ? new Color(80, 110, 80, 255) : new Color(52, 68, 52, 255)) : SlotBg;
        Raylib.DrawRectangleRounded(resultRect, 0.12f, 6, resultBg);
        Raylib.DrawRectangleRoundedLinesEx(resultRect, 0.12f, 6, 1.5f, hasResult ? new Color(100, 200, 100, 255) : SlotBorder);

        if (hasResult) {
            Hud.DrawItemIcon(craftResult2.Item, new Rectangle(resultX + 3f, resultY + 3f, slotSz - 6f, slotSz - 6f), 1f);
            if (craftResult2.Count > 1)
                Fonts.DrawShadowed($"×{craftResult2.Count}", resultX + 3f, resultY + slotSz - 16f, 14f, Color.White);

            if (leftClick && resultHovered) {
                bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
                var inv = session.Player.Inventory;
                if (shift) {
                    int craftedCount = 0;
                    while (true) {
                        var g3 = new ItemDefinition?[9];
                        g3[0] = PersonalGrid[0]?.Item.Definition; g3[1] = PersonalGrid[1]?.Item.Definition;
                        g3[3] = PersonalGrid[2]?.Item.Definition; g3[4] = PersonalGrid[3]?.Item.Definition;
                        string curKey = GameData.NormalizeGrid(g3);
                        if (!GameData.ShapeRecipes.TryGetValue(curKey, out var currentRecipe) || currentRecipe.Item.Id != craftResult2.Item.Id) break;

                        var inst = GameData.NewItem(currentRecipe.Item);
                        if (!inv.TryInsert(inst, currentRecipe.Count)) break;

                        for (int i = 0; i < 4; i++) {
                            if (PersonalGrid[i].HasValue && PersonalGrid[i]!.Value.Quantity > 0) {
                                int rem = PersonalGrid[i]!.Value.Quantity - 1;
                                PersonalGrid[i] = rem > 0 ? PersonalGrid[i]!.Value with { Quantity = rem } : null;
                            }
                        }
                        craftedCount += currentRecipe.Count;
                    }
                    if (craftedCount > 0) {
                        session.AddMessage($"Создано: {craftResult2.Item.Name} ×{craftedCount}");
                        SoundSystem.PlayPop();
                    }
                } else {
                    bool canTake = false;
                    if (!Held.HasValue || Held.Value.Quantity <= 0) {
                        Held = new ItemEntry(GameData.NewItem(craftResult2.Item), craftResult2.Count);
                        canTake = true;
                    } else if (Held.Value.Item.Definition.Id == craftResult2.Item.Id && Held.Value.Quantity + craftResult2.Count <= craftResult2.Item.MaxStack) {
                        Held = Held.Value with { Quantity = Held.Value.Quantity + craftResult2.Count };
                        canTake = true;
                    }

                    if (canTake) {
                        for (int i = 0; i < 4; i++) {
                            if (PersonalGrid[i].HasValue && PersonalGrid[i]!.Value.Quantity > 0) {
                                int rem = PersonalGrid[i]!.Value.Quantity - 1;
                                PersonalGrid[i] = rem > 0 ? PersonalGrid[i]!.Value with { Quantity = rem } : null;
                            }
                        }
                        session.AddMessage($"Создано: {craftResult2.Item.Name}");
                        SoundSystem.PlayPop();
                    }
                }
            }
        }

        // Книга рецептов в инвентаре (нижняя половина панели крафта)
        float bookY = gridY + 2 * (slotSz + gap) + 6f;
        DrawRecipeBookGrid(session, panelX + 4f, bookY, panelW - 8f, panelH - (bookY - panelY) - 4f, PersonalGrid, false);
    }

    // ── Книга рецептов нового поколения ──────────────────────────────────────
    public static string RecipeSearch = "";
    public static bool RecipeSearchActive = false;
    public static GameData.CraftCategory RecipeCategory = GameData.CraftCategory.All;
    public static bool RecipeOnlyCraftable = false;
    public static int RecipePage = 0;

    private static void DrawRecipeBookGrid(GameSession session, float rx, float ry, float rw, float rh, ItemEntry?[] targetGrid, bool is3x3) {
        var mouse = Ui.Mouse();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool rightClick = Raylib.IsMouseButtonPressed(MouseButton.Right);
        bool shiftHeld = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
        var inv = session.Player.Inventory;

        var bookRect = new Rectangle(rx, ry, rw, rh);
        Raylib.DrawRectangleRounded(bookRect, 0.04f, 6, new Color(42, 48, 60, 240));
        Raylib.DrawRectangleRoundedLinesEx(bookRect, 0.04f, 6, 1.5f, new Color(75, 85, 105, 230));

        // Заголовок
        Fonts.Draw("Книга рецептов", rx + 8f, ry + 4f, 15f, new Color(240, 245, 255, 255));

        // Строка поиска и переключатель "Только доступные"
        float searchY = ry + 24f;
        float btnW = 92f;
        float searchW = rw - btnW - 18f;
        var searchRect = new Rectangle(rx + 6f, searchY, searchW, 22f);
        var craftableBtnRect = new Rectangle(rx + rw - btnW - 6f, searchY, btnW, 22f);

        bool searchHov = Raylib.CheckCollisionPointRec(mouse, searchRect);
        if (leftClick) {
            RecipeSearchActive = searchHov;
        }

        if (RecipeSearchActive) {
            int charCode = Raylib.GetCharPressed();
            while (charCode > 0) {
                if (charCode >= 32 && RecipeSearch.Length < 25) {
                    RecipeSearch += (char)charCode;
                    RecipePage = 0;
                }
                charCode = Raylib.GetCharPressed();
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && RecipeSearch.Length > 0) {
                RecipeSearch = RecipeSearch[..^1];
                RecipePage = 0;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.Enter)) {
                RecipeSearchActive = false;
            }
        }

        // Отрисовка поля поиска
        Raylib.DrawRectangleRounded(searchRect, 0.15f, 4, RecipeSearchActive ? new Color(55, 65, 82, 255) : new Color(30, 35, 45, 230));
        Raylib.DrawRectangleRoundedLinesEx(searchRect, 0.15f, 4, 1f, RecipeSearchActive ? new Color(120, 180, 255, 255) : new Color(65, 75, 95, 200));

        string displayText = string.IsNullOrEmpty(RecipeSearch) ? (RecipeSearchActive ? "" : "Поиск...") : RecipeSearch;
        Color searchTextColor = string.IsNullOrEmpty(RecipeSearch) ? new Color(140, 150, 165, 180) : Color.White;
        Fonts.Draw(displayText, searchRect.X + 6f, searchRect.Y + 3f, 12f, searchTextColor);

        // Кнопка очистки поиска если текст введен
        if (!string.IsNullOrEmpty(RecipeSearch)) {
            var clearRect = new Rectangle(searchRect.X + searchRect.Width - 18f, searchRect.Y + 2f, 16f, 18f);
            if (Raylib.CheckCollisionPointRec(mouse, clearRect)) {
                Fonts.Draw("x", clearRect.X + 4f, clearRect.Y + 1f, 12f, new Color(255, 120, 120, 255));
                if (leftClick) {
                    RecipeSearch = "";
                    RecipePage = 0;
                }
            } else {
                Fonts.Draw("x", clearRect.X + 4f, clearRect.Y + 1f, 12f, new Color(180, 190, 205, 200));
            }
        }

        // Кнопка переключения "Только доступные"
        bool btnHov = Raylib.CheckCollisionPointRec(mouse, craftableBtnRect);
        Color btnBg = RecipeOnlyCraftable ? (btnHov ? new Color(55, 135, 75, 255) : new Color(40, 110, 55, 240))
                                          : (btnHov ? new Color(65, 72, 88, 230) : new Color(45, 50, 62, 200));
        Raylib.DrawRectangleRounded(craftableBtnRect, 0.15f, 4, btnBg);
        Raylib.DrawRectangleRoundedLinesEx(craftableBtnRect, 0.15f, 4, 1f, RecipeOnlyCraftable ? new Color(100, 230, 120, 255) : new Color(80, 90, 110, 200));
        Fonts.DrawCentered(RecipeOnlyCraftable ? "Доступно" : "Все", craftableBtnRect.X + craftableBtnRect.Width / 2f, craftableBtnRect.Y + 3f, 11f, RecipeOnlyCraftable ? Color.White : new Color(190, 200, 215, 220));
        if (btnHov && leftClick) {
            RecipeOnlyCraftable = !RecipeOnlyCraftable;
            RecipePage = 0;
        }

        // Вкладки категорий (равномерное распределение по ширине без вылезания)
        float tabY = searchY + 26f;
        var categories = RecipeCategories;
        float tabH = 19f;
        float totalTabGap = (categories.Length - 1) * 3f;
        float tabW = (rw - 12f - totalTabGap) / categories.Length;
        for (int i = 0; i < categories.Length; i++) {
            var (cat, label) = categories[i];
            float tx = rx + 6f + i * (tabW + 3f);
            var tabRect = new Rectangle(tx, tabY, tabW, tabH);
            bool isSel = RecipeCategory == cat;
            bool tabHov = Raylib.CheckCollisionPointRec(mouse, tabRect);

            Color tabBg = isSel ? new Color(50, 125, 70, 255) : (tabHov ? new Color(65, 75, 95, 230) : new Color(40, 46, 58, 200));
            Raylib.DrawRectangleRounded(tabRect, 0.2f, 4, tabBg);
            Raylib.DrawRectangleRoundedLinesEx(tabRect, 0.2f, 4, 1f, isSel ? new Color(100, 225, 120, 255) : new Color(60, 70, 90, 180));
            Fonts.DrawCentered(label, tx + tabW / 2f, tabY + 2f, 10.5f, isSel ? Color.White : (tabHov ? new Color(230, 235, 245, 255) : new Color(160, 170, 185, 210)));

            if (tabHov && leftClick) {
                RecipeCategory = cat;
                RecipePage = 0;
            }
        }

        // Фильтрация рецептов
        var list = _recipeBookList;
        list.Clear();
        foreach (var r in GameData.CraftRecipes) {
            if (r.IsSmelt) continue;
            if (r.Needs3x3 && !is3x3) continue;
            if (RecipeCategory != GameData.CraftCategory.All && r.Category != RecipeCategory) continue;
            if (!string.IsNullOrWhiteSpace(RecipeSearch)) {
                if (!r.Name.Contains(RecipeSearch, StringComparison.OrdinalIgnoreCase) &&
                    !r.Output.Name.Contains(RecipeSearch, StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            if (RecipeOnlyCraftable) {
                bool canCraft = true;
                foreach (var (reqItem, reqCount) in r.Ingredients) {
                    if (inv.CountOf(reqItem) < reqCount) { canCraft = false; break; }
                }
                if (!canCraft) continue;
            }
            list.Add(r);
        }

        // Сетка иконок
        float gridTopY = tabY + tabH + 6f;
        float bottomBarH = 22f;
        float gridAreaH = (ry + rh - bottomBarH - 4f) - gridTopY;
        const float slotSz = 38f, gap = 4f;
        int cols = Math.Max(1, (int)((rw - 12f + gap) / (slotSz + gap)));
        int rows = Math.Max(1, (int)((gridAreaH + gap) / (slotSz + gap)));
        int pageSize = cols * rows;
        int totalPages = Math.Max(1, (list.Count + pageSize - 1) / pageSize);

        if (Raylib.CheckCollisionPointRec(mouse, bookRect)) {
            int wheel = (int)Raylib.GetMouseWheelMove();
            if (wheel != 0) {
                RecipePage = Math.Clamp(RecipePage - wheel, 0, totalPages - 1);
            }
        }
        RecipePage = Math.Clamp(RecipePage, 0, totalPages - 1);

        GameData.CraftRecipe? hoveredRecipe = null;
        float startGridX = rx + (rw - (cols * slotSz + (cols - 1) * gap)) / 2f;

        for (int i = 0; i < pageSize; i++) {
            int rIdx = RecipePage * pageSize + i;
            int r = i / cols;
            int c = i % cols;
            float sx = startGridX + c * (slotSz + gap);
            float sy = gridTopY + r * (slotSz + gap);
            var slotRect = new Rectangle(sx, sy, slotSz, slotSz);

            if (rIdx < list.Count) {
                var recipe = list[rIdx];
                bool canCraft = true;
                foreach (var (reqItem, reqCount) in recipe.Ingredients) {
                    if (inv.CountOf(reqItem) < reqCount) { canCraft = false; break; }
                }

                bool hov = Raylib.CheckCollisionPointRec(mouse, slotRect);
                Color bg = canCraft ? (hov ? new Color(50, 105, 60, 255) : new Color(34, 70, 42, 230))
                                    : (hov ? new Color(60, 68, 82, 235) : new Color(34, 38, 48, 200));
                Color border = canCraft ? new Color(90, 220, 110, 245) : new Color(60, 68, 82, 210);

                Raylib.DrawRectangleRounded(slotRect, 0.15f, 4, bg);
                Raylib.DrawRectangleRoundedLinesEx(slotRect, 0.15f, 4, 1.2f, border);

                Hud.DrawItemIcon(recipe.Output, new Rectangle(sx + 3f, sy + 3f, slotSz - 6f, slotSz - 6f), 1f);
                if (recipe.Count > 1) {
                    Fonts.DrawShadowed($"×{recipe.Count}", sx + 16f, sy + slotSz - 14f, 11f, Color.White);
                }

                if (hov) {
                    hoveredRecipe = recipe;
                    if (leftClick) {
                        if (shiftHeld) {
                            QuickCraftStack(session, recipe, is3x3);
                        } else {
                            QuickCraftOnce(session, recipe, is3x3);
                        }
                    } else if (rightClick) {
                        AutoFillRecipe(session, recipe, targetGrid, is3x3);
                    }
                }
            } else {
                // Пустой слот в сетке
                Raylib.DrawRectangleRounded(slotRect, 0.15f, 4, new Color(28, 32, 40, 140));
                Raylib.DrawRectangleRoundedLinesEx(slotRect, 0.15f, 4, 1f, new Color(45, 52, 65, 140));
            }
        }

        // Нижняя панель навигации (Пагинация)
        float navY = ry + rh - 22f;
        var prevRect = new Rectangle(rx + 8f, navY, 26f, 18f);
        var nextRect = new Rectangle(rx + rw - 34f, navY, 26f, 18f);
        bool prevHov = Raylib.CheckCollisionPointRec(mouse, prevRect);
        bool nextHov = Raylib.CheckCollisionPointRec(mouse, nextRect);

        Raylib.DrawRectangleRounded(prevRect, 0.2f, 4, prevHov ? new Color(65, 75, 95, 255) : new Color(42, 48, 62, 200));
        Fonts.DrawCentered("<", prevRect.X + 13f, prevRect.Y + 2f, 12f, RecipePage > 0 ? Color.White : new Color(130, 135, 145, 150));
        if (prevHov && leftClick && RecipePage > 0) RecipePage--;

        Raylib.DrawRectangleRounded(nextRect, 0.2f, 4, nextHov ? new Color(65, 75, 95, 255) : new Color(42, 48, 62, 200));
        Fonts.DrawCentered(">", nextRect.X + 13f, nextRect.Y + 2f, 12f, RecipePage < totalPages - 1 ? Color.White : new Color(130, 135, 145, 150));
        if (nextHov && leftClick && RecipePage < totalPages - 1) RecipePage++;

        string pageInfo = $"Стр. {RecipePage + 1}/{totalPages} ({list.Count} рец.)";
        Fonts.DrawCentered(pageInfo, rx + rw / 2f, navY + 2f, 11f, new Color(180, 190, 205, 230));

        // Отрисовка подробного тултипа рецепта
        if (hoveredRecipe != null && (!Held.HasValue || Held.Value.Quantity <= 0)) {
            DrawRecipeTooltip(session, hoveredRecipe, mouse);
        }
    }

    private static void DrawRecipeTooltip(GameSession session, GameData.CraftRecipe recipe, Vector2 mouse) {
        var inv = session.Player.Inventory;
        var lines = new List<(string Text, Color Col)>();
        lines.Add(($"{recipe.Output.Name} ×{recipe.Count}", new Color(255, 235, 120, 255)));

        string catName = recipe.Category switch {
            GameData.CraftCategory.Weapons => "Оружие",
            GameData.CraftCategory.Tools => "Инструмент",
            GameData.CraftCategory.Armor => "Доспехи",
            GameData.CraftCategory.Blocks => "Блок",
            GameData.CraftCategory.Food => "Еда",
            GameData.CraftCategory.Materials => "Ресурс",
            _ => "Предмет"
        };
        lines.Add(($"[{catName}]", new Color(140, 200, 255, 220)));

        lines.Add(("Ингредиенты:", new Color(200, 200, 210, 255)));
        foreach (var (ing, count) in recipe.Ingredients) {
            int have = inv.CountOf(ing);
            bool ok = have >= count;
            Color ingCol = ok ? new Color(110, 230, 120, 255) : new Color(255, 110, 100, 255);
            lines.Add(($" • {ing.Name}: {have}/{count}", ingCol));
        }

        lines.Add(("ЛКМ — быстрый крафт (1 шт.)", new Color(170, 225, 185, 220)));
        lines.Add(("Shift+ЛКМ — скрафтить стак", new Color(255, 215, 100, 220)));
        lines.Add(("ПКМ — выложить в сетку", new Color(170, 175, 185, 200)));

        float maxW = 0f;
        foreach (var (t, _) in lines) maxW = MathF.Max(maxW, Fonts.Measure(t, 12f));
        float boxW = maxW + 16f;
        float boxH = lines.Count * 17f + 10f;
        float bx = Math.Min(mouse.X + 14f, Ui.Vw - boxW - 8f);
        float by = Math.Min(mouse.Y + 14f, Ui.Vh - boxH - 8f);

        Raylib.DrawRectangleRounded(new Rectangle(bx, by, boxW, boxH), 0.12f, 4, new Color(15, 18, 24, 250));
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(bx, by, boxW, boxH), 0.12f, 4, 1.2f, new Color(50, 140, 70, 230));

        float curY = by + 5f;
        foreach (var (t, c) in lines) {
            Fonts.Draw(t, bx + 8f, curY, 12f, c);
            curY += 17f;
        }
    }

    private static void QuickCraftOnce(GameSession session, GameData.CraftRecipe recipe, bool is3x3) {
        if (recipe.Needs3x3 && !is3x3) {
            session.AddMessage("Этот рецепт требует Верстак 3×3!");
            return;
        }
        var inv = session.Player.Inventory;
        foreach (var (item, count) in recipe.Ingredients) {
            if (inv.CountOf(item) < count) {
                session.AddMessage($"Не хватает ингредиентов для «{recipe.Name}»!");
                return;
            }
        }

        foreach (var (ing, count) in recipe.Ingredients) {
            inv.TryRemove(ing, count);
        }

        var resItem = GameData.NewItem(recipe.Output);
        if (!inv.TryInsert(resItem, recipe.Count)) {
            var p = session.Player.Position + new Vector3(0f, 0.5f, 0f);
            session.World.Pickups.Add(new ItemPickup(resItem, recipe.Count, p) {
                PickupDelay = 0.5f,
                Velocity = session.Player.Forward * 2.5f + Vector3.UnitY * 2.0f
            });
        }

        GameClient.Active?.SendInventoryAction(InventoryActionType.Craft, GameData.CraftRecipes.IndexOf(recipe), 0, 1);
        GameClient.Active?.SendInventoryUpdate(session.Player, (byte)session.World.Dimension);

        SoundSystem.PlayPop();
        session.AddMessage($"Скрафчено: {recipe.Output.Name} ×{recipe.Count}");
    }

    private static void QuickCraftStack(GameSession session, GameData.CraftRecipe recipe, bool is3x3) {
        if (recipe.Needs3x3 && !is3x3) {
            session.AddMessage("Этот рецепт требует Верстак 3×3!");
            return;
        }
        var inv = session.Player.Inventory;
        int maxPerStack = Math.Max(1, recipe.Output.MaxStack);
        int maxCrafts = maxPerStack == 1 ? 1 : Math.Max(1, maxPerStack / recipe.Count);
        foreach (var (item, count) in recipe.Ingredients) {
            int have = inv.CountOf(item);
            if (count > 0) maxCrafts = Math.Min(maxCrafts, have / count);
        }
        if (maxCrafts <= 0) {
            session.AddMessage($"Не хватает ингредиентов для «{recipe.Name}»!");
            return;
        }

        int crafted = 0;
        for (int c = 0; c < maxCrafts; c++) {
            var item = GameData.NewItem(recipe.Output);
            bool ok = true;
            foreach (var (ing, count) in recipe.Ingredients) {
                if (!inv.TryRemove(ing, count)) { ok = false; break; }
            }
            if (!ok) break;

            if (!inv.TryInsert(item, recipe.Count)) {
                // Если нет места в инвентаре, дропаем на землю
                var p = session.Player.Position + new Vector3(0f, 0.5f, 0f);
                session.World.Pickups.Add(new ItemPickup(item, recipe.Count, p) {
                    PickupDelay = 0.5f,
                    Velocity = session.Player.Forward * 2.5f + Vector3.UnitY * 2.0f
                });
            }
            crafted += 1;
        }
        
        GameClient.Active?.SendInventoryAction(InventoryActionType.Craft, GameData.CraftRecipes.IndexOf(recipe), 0, crafted);
        GameClient.Active?.SendInventoryUpdate(session.Player, (byte)session.World.Dimension);

        if (crafted > 0) {
            SoundSystem.PlayPop();
            session.AddMessage($"Скрафчено: {recipe.Output.Name} ×{crafted}");
        }
    }

    private static void AutoFillRecipe(GameSession session, GameData.CraftRecipe recipe, ItemEntry?[] grid, bool is3x3) {
        var inv = session.Player.Inventory;
        int gridSize = is3x3 ? 3 : 2;

        var shape = recipe.Shape;
        if (shape == null) return; // рецепты плавки в сетку крафта не раскладываются
        const int shapeW = 3;
        const int shapeH = 3;

        // Найдём bounding box непустых ячеек в shape 3×3
        int minR = shapeH, maxR = -1, minC = shapeW, maxC = -1;
        for (int r = 0; r < shapeH; r++) {
            for (int c = 0; c < shapeW; c++) {
                int idx = r * shapeW + c;
                if (idx < shape.Length && shape[idx] != null) {
                    if (r < minR) minR = r;
                    if (r > maxR) maxR = r;
                    if (c < minC) minC = c;
                    if (c > maxC) maxC = c;
                }
            }
        }

        if (maxR < 0) return; // пустой шаблон

        int bbH = maxR - minR + 1;
        int bbW = maxC - minC + 1;

        if (!is3x3 && (bbW > 2 || bbH > 2 || recipe.Needs3x3)) {
            session.AddMessage("Этот рецепт требует Верстак 3×3!");
            return;
        }

        // Возвращаем текущие предметы из сетки в инвентарь или выбрасываем
        for (int i = 0; i < grid.Length; i++) {
            if (grid[i].HasValue && grid[i]!.Value.Quantity > 0) {
                if (!inv.TryInsert(grid[i]!.Value.Item, grid[i]!.Value.Quantity)) {
                    var p = session.Player.Position + new Vector3(0f, 0.5f, 0f);
                    session.World.Pickups.Add(new ItemPickup(grid[i]!.Value.Item, grid[i]!.Value.Quantity, p) {
                        PickupDelay = 0.5f,
                        Velocity = session.Player.Forward * 1.5f + Vector3.UnitY * 1.5f
                    });
                }
                grid[i] = null;
            }
        }

        // Проверяем наличие ингредиентов
        foreach (var (reqItem, reqCount) in recipe.Ingredients) {
            if (inv.CountOf(reqItem) < reqCount) {
                session.AddMessage($"Не хватает: {reqItem.Name} ({inv.CountOf(reqItem)}/{reqCount})");
                return;
            }
        }

        // Выровнять по верхнему левому углу целевой сетки
        bool placed = false;
        for (int r = 0; r < bbH && r < gridSize; r++) {
            for (int c = 0; c < bbW && c < gridSize; c++) {
                int shapeIdx = (minR + r) * shapeW + (minC + c);
                if (shapeIdx >= shape.Length) continue;
                var needed = shape[shapeIdx];
                if (needed != null) {
                    int gridIdx = r * gridSize + c;
                    if (gridIdx < grid.Length && inv.TryRemove(needed, 1)) {
                        grid[gridIdx] = new ItemEntry(GameData.NewItem(needed), 1);
                        placed = true;
                    }
                }
            }
        }

        if (placed) {
            SoundSystem.PlayPop();
            session.AddMessage($"Выложен рецепт: {recipe.Name}");
        }
    }

    // ── 3×3 экран верстака ───────────────────────────────────────────────────

    public static void DrawWorkbench(GameSession session) {
        int w = Ui.Vw, h = Ui.Vh;
        var inv = session.Player.Inventory;
        var mouse = Ui.Mouse();
        bool leftClick = Raylib.IsMouseButtonPressed(MouseButton.Left);
        bool rightClick = Raylib.IsMouseButtonPressed(MouseButton.Right);

        const int slotSz = 48, gap = 4;
        int gridW3 = 3 * slotSz + 2 * gap;
        int panelW = 920;
        int panelH = 3 * slotSz + 2 * gap + 4 * 52 + 3 * 4 + 140;

        float px = MathF.Max(4f, w / 2f - panelW / 2f), py = MathF.Max(4f, (h - panelH) / 2f);
        DrawPanel(px, py, panelW, panelH);

        Fonts.DrawCentered("ВЕРСТАК", w / 2f, py + 8f, 26f, TextDark);
        Fonts.DrawCentered("3×3 крафт · ЛКМ — взять/положить · ПКМ — положить 1", w / 2f, py + 36f, 14f, new Color(90, 90, 90, 255));

        float gridX = px + 24f;
        float gridY = py + 62f;

        // 3×3 сетка
        for (int r = 0; r < 3; r++) {
            for (int c = 0; c < 3; c++) {
                int idx = r * 3 + c;
                float sx = gridX + c * (slotSz + gap);
                float sy = gridY + r * (slotSz + gap);
                DrawCraftSlot(WorkbenchGrid, idx, sx, sy, slotSz, mouse, leftClick, rightClick);
            }
        }

        // Стрелка + результат
        float arrowX = gridX + gridW3 + 10f;
        float arrowY = gridY + slotSz + gap + slotSz / 2f - 10f;
        Fonts.Draw("→", arrowX, arrowY, 28f, new Color(200, 200, 200, 255));

        float resultX = arrowX + 36f;
        float resultY = gridY + slotSz + gap;
        var gridDef3 = new ItemDefinition?[9];
        for (int i = 0; i < 9; i++) gridDef3[i] = WorkbenchGrid[i]?.Item.Definition;
        string key3 = GameData.NormalizeGrid(gridDef3);
        bool hasResult = GameData.ShapeRecipes.TryGetValue(key3, out var craftResult3);

        var resultRect = new Rectangle(resultX, resultY, slotSz, slotSz);
        bool resultHov = Raylib.CheckCollisionPointRec(mouse, resultRect);
        Color resBg = hasResult ? (resultHov ? new Color(80, 110, 80, 255) : new Color(52, 68, 52, 255)) : SlotBg;
        Raylib.DrawRectangleRounded(resultRect, 0.12f, 6, resBg);
        Raylib.DrawRectangleRoundedLinesEx(resultRect, 0.12f, 6, 1.5f, hasResult ? new Color(100, 200, 100, 255) : SlotBorder);

        if (hasResult) {
            Hud.DrawItemIcon(craftResult3.Item, new Rectangle(resultX + 3f, resultY + 3f, slotSz - 6f, slotSz - 6f), 1f);
            if (craftResult3.Count > 1)
                Fonts.DrawShadowed($"×{craftResult3.Count}", resultX + 3f, resultY + slotSz - 16f, 14f, Color.White);

            if (leftClick && resultHov) {
                bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
                if (shift) {
                    int craftedCount = 0;
                    while (true) {
                        var g3 = new ItemDefinition?[9];
                        for (int i = 0; i < 9; i++) g3[i] = WorkbenchGrid[i]?.Item.Definition;
                        string curKey = GameData.NormalizeGrid(g3);
                        if (!GameData.ShapeRecipes.TryGetValue(curKey, out var currentRecipe) || currentRecipe.Item.Id != craftResult3.Item.Id) break;

                        var inst = GameData.NewItem(currentRecipe.Item);
                        if (!inv.TryInsert(inst, currentRecipe.Count)) break;

                        for (int i = 0; i < 9; i++) {
                            if (WorkbenchGrid[i].HasValue && WorkbenchGrid[i]!.Value.Quantity > 0) {
                                int rem = WorkbenchGrid[i]!.Value.Quantity - 1;
                                WorkbenchGrid[i] = rem > 0 ? WorkbenchGrid[i]!.Value with { Quantity = rem } : null;
                            }
                        }
                        craftedCount += currentRecipe.Count;
                    }
                    if (craftedCount > 0) {
                        session.AddMessage($"Создано: {craftResult3.Item.Name} ×{craftedCount}");
                        SoundSystem.PlayPop();
                    }
                } else {
                    bool canTake = false;
                    if (!Held.HasValue || Held.Value.Quantity <= 0) {
                        Held = new ItemEntry(GameData.NewItem(craftResult3.Item), craftResult3.Count);
                        canTake = true;
                    } else if (Held.Value.Item.Definition.Id == craftResult3.Item.Id && Held.Value.Quantity + craftResult3.Count <= craftResult3.Item.MaxStack) {
                        Held = Held.Value with { Quantity = Held.Value.Quantity + craftResult3.Count };
                        canTake = true;
                    }

                    if (canTake) {
                        for (int i = 0; i < 9; i++) {
                            if (WorkbenchGrid[i].HasValue && WorkbenchGrid[i]!.Value.Quantity > 0) {
                                int rem = WorkbenchGrid[i]!.Value.Quantity - 1;
                                WorkbenchGrid[i] = rem > 0 ? WorkbenchGrid[i]!.Value with { Quantity = rem } : null;
                            }
                        }
                        session.AddMessage($"Создано: {craftResult3.Item.Name}");
                        SoundSystem.PlayPop();
                    }
                }
            }
        }

        // Книга рецептов верстака (справа от 3×3 сетки)
        float bookX = resultX + slotSz + 18f;
        float bookW = px + panelW - bookX - 18f;
        float bookH = 3 * (slotSz + gap) + 24f;
        DrawRecipeBookGrid(session, bookX, gridY - 8f, bookW, bookH, WorkbenchGrid, true);

        // Инвентарь снизу
        float invY = gridY + 3 * (slotSz + gap) + 20f;
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

        // Предмет за курсором
        DrawHeldItem();
        HandleWorkbenchInput(session, inv, invX, invY, hotY);
        DrawWorkbenchTooltip(session, inv, invX, invY, hotY, gridX, gridY, slotSz, gap);
    }

    private static void HandleWorkbenchInput(GameSession session, Voxalith.Core.Inventory.Container inv,
                                              float invX, float invY, float hotY) {
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left) && !Raylib.IsMouseButtonPressed(MouseButton.Right)) return;
        bool right = Raylib.IsMouseButtonPressed(MouseButton.Right);
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 9; col++)
                if (SlotClicked(session, inv, invX + col * 56f, invY + row * 56f, 9 + row * 9 + col, right)) return;
        for (int col = 0; col < 9; col++)
            if (SlotClicked(session, inv, invX + col * 56f, hotY, col, right)) return;
    }

    private static void DrawWorkbenchTooltip(GameSession session, Voxalith.Core.Inventory.Container inv,
                                              float invX, float invY, float hotY,
                                              float gridX, float gridY, int slotSz, int gap) {
        if (Held.HasValue && Held.Value.Quantity > 0) return;
        var mouse = Ui.Mouse();
        ItemDefinition? def = null;
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(gridX + c*(slotSz+gap), gridY + r*(slotSz+gap), slotSz, slotSz)))
                    def = WorkbenchGrid[r*3+c]?.Item.Definition;
        for (int row = 0; row < 3; row++)
            for (int col = 0; col < 9; col++) {
                if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(invX + col*56f, invY + row*56f, 52, 52))) {
                    var e = inv.Slots[9 + row*9 + col];
                    if (e.HasValue) def = e.Value.Item.Definition;
                }
            }
        for (int col = 0; col < 9; col++) {
            if (Raylib.CheckCollisionPointRec(mouse, new Rectangle(invX + col*56f, hotY, 52, 52))) {
                var e = inv.Slots[col];
                if (e.HasValue) def = e.Value.Item.Definition;
            }
        }
        if (def != null) DrawItemTooltip(def, mouse);
    }

    // ── Вспомогательный слот крафт-сетки ─────────────────────────────────────

    private static void DrawCraftSlot(ItemEntry?[] grid, int idx, float x, float y, int sz,
                                       System.Numerics.Vector2 mouse, bool leftClick, bool rightClick) {
        var rect = new Rectangle(x, y, sz, sz);
        bool hov = Raylib.CheckCollisionPointRec(mouse, rect);
        Raylib.DrawRectangleRounded(rect, 0.12f, 6, hov ? SlotHover : SlotBg);
        Raylib.DrawRectangleRoundedLinesEx(rect, 0.12f, 6, 2f, hov && Held != null ? SlotSelected : SlotBorder);

        var slotItem = grid[idx];
        if (slotItem.HasValue && slotItem.Value.Quantity > 0) {
            var item = slotItem.Value;
            Hud.DrawItemIcon(item.Item.Definition, new Rectangle(x + 3f, y + 3f, sz - 6f, sz - 6f), 1f);
            if (item.Quantity > 1) {
                Fonts.DrawShadowed($"×{item.Quantity}", x + 4f, y + sz - 18f, 14f, Color.White);
            }
        }

        if (hov && (leftClick || rightClick)) {
            if (rightClick) {
                if (Held.HasValue && Held.Value.Quantity > 0) {
                    var held = Held.Value;
                    if (!slotItem.HasValue) {
                        grid[idx] = new ItemEntry(held.Item, 1);
                        Held = held with { Quantity = held.Quantity - 1 };
                        if (Held.Value.Quantity <= 0) Held = null;
                    } else if (slotItem.Value.Item.Definition == held.Item.Definition && slotItem.Value.Quantity < slotItem.Value.Item.Definition.MaxStack) {
                        grid[idx] = slotItem.Value with { Quantity = slotItem.Value.Quantity + 1 };
                        Held = held with { Quantity = held.Quantity - 1 };
                        if (Held.Value.Quantity <= 0) Held = null;
                    }
                } else if (slotItem.HasValue && slotItem.Value.Quantity > 0) {
                    int qty = slotItem.Value.Quantity;
                    int take = (qty + 1) / 2;
                    Held = slotItem.Value with { Quantity = take };
                    if (qty - take > 0) {
                        grid[idx] = slotItem.Value with { Quantity = qty - take };
                    } else {
                        grid[idx] = null;
                    }
                }
            } else if (leftClick) {
                if (!Held.HasValue || Held.Value.Quantity <= 0) {
                    if (slotItem.HasValue) {
                        Held = slotItem;
                        grid[idx] = null;
                    }
                } else {
                    var held = Held.Value;
                    if (!slotItem.HasValue) {
                        grid[idx] = held;
                        Held = null;
                    } else if (slotItem.Value.Item.Definition == held.Item.Definition) {
                        int current = slotItem.Value.Quantity;
                        if (current < slotItem.Value.Item.Definition.MaxStack) {
                            int add = Math.Min(slotItem.Value.Item.Definition.MaxStack - current, held.Quantity);
                            grid[idx] = slotItem.Value with { Quantity = current + add };
                            if (held.Quantity - add > 0) {
                                Held = held with { Quantity = held.Quantity - add };
                            } else {
                                Held = null;
                            }
                        }
                    } else {
                        var tmp = slotItem;
                        grid[idx] = held;
                        Held = tmp;
                    }
                }
            }
        }
    }



    private static void DrawHeldItem() {
        if (!Held.HasValue || Held.Value.Quantity <= 0) return;
        var held = Held.Value;
        var mouse = Ui.Mouse();
        var heldRec = new Rectangle(mouse.X - 14f, mouse.Y - 14f, 28f, 28f);
        Hud.DrawItemIcon(held.Item.Definition, heldRec, 1f);
        if (held.Quantity > 1)
            Fonts.Draw($"×{held.Quantity}", mouse.X - 14f, mouse.Y + 8f, 15f, Color.White);
        Hud.DrawItemDurability(held.Item, heldRec);
    }

    public static void DrawItemTooltip(ItemInstance item, System.Numerics.Vector2 mouse) {
        DrawItemTooltip(item.Definition, mouse, item.Durability);
    }

    public static void DrawItemTooltip(ItemDefinition def, System.Numerics.Vector2 mouse, int currentDurability = -1) {
        string title = def.Name;
        ushort itemId = def.Id;

        string subtext1 = "";
        string subtext2 = "";

        if (GameData.FoodValue.TryGetValue(itemId, out float food)) {
            subtext1 = $"Восстанавливает: +{food} сытости";
        } else if (GameData.GetArmorPoints(itemId) > 0) {
            int pts = GameData.GetArmorPoints(itemId);
            int maxDur = GameData.GetMaxArmorDurability(itemId);
            int cur = currentDurability >= 0 ? currentDurability : maxDur;
            subtext1 = $"+{pts} Очков брони";
            subtext2 = $"Прочность: {cur} / {maxDur}";
        } else if (GameData.GetToolTier(itemId) > 0) {
            float dmg = GameData.GetWeaponDamage(itemId);
            float cd = GameData.GetWeaponCooldown(itemId);
            float spd = cd > 0 ? 1f / cd : 4f;
            int maxDur = GameData.GetMaxToolDurability(itemId);
            int cur = currentDurability >= 0 ? currentDurability : maxDur;
            subtext1 = $"Урон: {dmg} HP · Скорость: {spd:0.#}";
            subtext2 = $"Прочность: {cur} / {maxDur}";
        } else if (GameData.TryGetBlockByItem(itemId, out var blk) && blk != null) {
            subtext1 = blk.IsSolid ? "Твёрдый строительный блок" : "Декоративный объект";
        }

        // Цвета редкости предмета
        Color titleCol = itemId switch {
            var id when id == GameData.GoldenAppleItem.Id || id == GameData.TotemItem.Id || id == GameData.GoldSwordItem.Id => new Color(255, 220, 70, 255),
            var id when id == GameData.DiamondSwordItem.Id || id == GameData.DiamondPickaxeItem.Id || id == GameData.DiamondAxeItem.Id || id == GameData.DiamondShovelItem.Id || id == GameData.DiamondHoeItem.Id ||
                        id == GameData.DiamondHelmetItem.Id || id == GameData.DiamondChestplateItem.Id || id == GameData.DiamondLeggingsItem.Id || id == GameData.DiamondBootsItem.Id => new Color(100, 235, 255, 255),
            var id when id == GameData.MusicDiscItem.Id || id == GameData.DesertArtifactItem.Id || id == GameData.SwampArtifactItem.Id || id == GameData.NetherArtifactItem.Id || id == GameData.VoidKeyItem.Id || id == GameData.NetherTotemItem.Id || id == GameData.DesertTotemItem.Id || id == GameData.SwampTotemItem.Id => new Color(220, 130, 255, 255),
            var id when id == GameData.IronSwordItem.Id || id == GameData.IronPickaxeItem.Id || id == GameData.IronAxeItem.Id || id == GameData.IronShovelItem.Id || id == GameData.IronHoeItem.Id ||
                        id == GameData.IronHelmetItem.Id || id == GameData.IronChestplateItem.Id || id == GameData.IronLeggingsItem.Id || id == GameData.IronBootsItem.Id => new Color(220, 230, 240, 255),
            _ => new Color(255, 255, 255, 255)
        };

        float w = MathF.Max(Fonts.Measure(title, 16f), MathF.Max(Fonts.Measure(subtext1, 13f), Fonts.Measure(subtext2, 13f))) + 24f;
        float h = string.IsNullOrEmpty(subtext1) ? 32f : string.IsNullOrEmpty(subtext2) ? 52f : 68f;

        float tx = mouse.X + 14f;
        float ty = mouse.Y - 14f;
        
        if (tx + w > Ui.Vw - 8f) tx = mouse.X - w - 8f;
        if (ty + h > Ui.Vh - 8f) ty = Ui.Vh - h - 8f;
        if (ty < 8f) ty = 8f;
        if (tx < 8f) tx = 8f;

        var bg = new Color(16, 12, 28, 245);
        var border = new Color(85, 55, 145, 255);
        var innerBorder = new Color(45, 25, 80, 255);
        
        Raylib.DrawRectangleRounded(new Rectangle(tx, ty, w, h), 0.12f, 6, bg);
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(tx, ty, w, h), 0.12f, 6, 2f, border);
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(tx + 2, ty + 2, w - 4, h - 4), 0.10f, 6, 1f, innerBorder);

        Fonts.DrawShadowed(title, tx + 10f, ty + 7f, 16f, titleCol);
        if (!string.IsNullOrEmpty(subtext1)) {
            Fonts.Draw(subtext1, tx + 10f, ty + 28f, 13f, new Color(175, 185, 210, 255));
        }
        if (!string.IsNullOrEmpty(subtext2)) {
            Fonts.Draw(subtext2, tx + 10f, ty + 46f, 13f, new Color(125, 215, 130, 255));
        }
    }

    private static void DrawNameTooltip(string name, System.Numerics.Vector2 mouse) {
        float tw = Fonts.Measure(name, 16f) + 16f;
        float th = 28f;
        float tx = mouse.X + 12f, ty = mouse.Y - 12f;
        if (tx + tw > Ui.Vw) tx = mouse.X - tw - 8f;
        if (ty + th > Ui.Vh) ty = Ui.Vh - th - 8f;
        if (ty < 8f) ty = 8f;
        Raylib.DrawRectangleRec(new Rectangle(tx, ty, tw, th), new Color(16, 8, 24, 240));
        Raylib.DrawRectangleLinesEx(new Rectangle(tx, ty, tw, th), 1.5f, new Color(42, 16, 76, 255));
        Fonts.Draw(name, tx + 8f, ty + 6f, 16f, Color.White);
    }

    private static bool CampfireNearby(GameSession session) {
        var p = session.Player.Position;
        foreach (var camp in session.World.Fire.Campfires) {
            float dx = p.X - (camp.X + 0.5f), dz = p.Z - (camp.Z + 0.5f);
            if (dx * dx + dz * dz < 25f) return true;
        }
        return false;
    }
}
