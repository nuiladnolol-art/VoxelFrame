using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;

namespace VoxelFrame.Game;

public static partial class Screens {
    public static int CreativeTab = 0; // 0 = Blocks, 1 = Decor, 2 = Tools, 3 = Items, 4 = All
    public static int CreativeScrollRow = 0;
    public static string CreativeSearch = "";
    public static bool CreativeSearchActive = false;

    private static readonly string[] CreativeTabNames = {
        "Блоки",
        "Декор",
        "Инструменты",
        "Материалы",
        "Все",
        "Инвентарь"
    };

    private static List<ItemDefinition> GetCreativeItems(int tab, string filter) {
        IEnumerable<ItemDefinition> items = tab switch {
            0 => GameData.Items.Values.Where(item => GameData.TryGetBlockByItem(item.Id, out var b) && b != null &&
                                                    b.Id != GameData.BTorch.Id && b.Id != GameData.BWorkbench.Id &&
                                                    b.Id != GameData.BFurnace.Id && b.Id != GameData.BChest.Id &&
                                                    b.Id != GameData.BBed.Id),
            1 => GameData.Items.Values.Where(item => item.Id == GameData.TorchItem.Id || item.Id == GameData.WorkbenchItem.Id ||
                                                    item.Id == GameData.FurnaceItem.Id || item.Id == GameData.ChestItem.Id ||
                                                    item.Id == GameData.BedItem.Id || item.Id == GameData.DoorItem.Id ||
                                                    item.Id == GameData.WheatSeedsItem.Id || item.Id == GameData.BoneMealItem.Id),
            2 => GameData.Items.Values.Where(item => GameData.GetToolTier(item.Id) > 0 || item.Id == GameData.BowItem.Id ||
                                                    item.Id == GameData.ArrowItem.Id || item.Id == GameData.ShieldItem.Id ||
                                                    item.Id == GameData.FlintAndSteelItem.Id || item.Id == GameData.BucketItem.Id ||
                                                    item.Id == GameData.WaterBucketItem.Id || item.Id == GameData.LavaBucketItem.Id ||
                                                    item.Id == GameData.TotemItem.Id),
            3 => GameData.Items.Values.Where(item => GameData.FoodValue.ContainsKey(item.Id) || item.Id == GameData.CoalItem.Id ||
                                                    item.Id == GameData.CharcoalItem.Id || item.Id == GameData.IronIngotItem.Id ||
                                                    item.Id == GameData.GoldIngotItem.Id || item.Id == GameData.DiamondItem.Id ||
                                                    item.Id == GameData.NetherQuartzItem.Id ||
                                                    item.Id == GameData.StickItem.Id || item.Id == GameData.FeatherItem.Id ||
                                                    item.Id == GameData.GunpowderItem.Id || item.Id == GameData.StringItem.Id ||
                                                    item.Id == GameData.BoneItem.Id || item.Id == GameData.FlintItem.Id ||
                                                    item.Id == GameData.LeatherItem.Id || item.Id == GameData.WhiteWoolItem.Id ||
                                                    item.Id == GameData.BlazeRodItem.Id || item.Id == GameData.GlowstoneDustItem.Id ||
                                                    item.Id == GameData.MusicDiscItem.Id || item.Id == GameData.WheatItem.Id ||
                                                    item.Id == GameData.RottenFleshItem.Id || item.Id == GameData.SawdustItem.Id ||
                                                    item.Id == GameData.SawdustPorridgeItem.Id),
            _ => GameData.Items.Values.OrderBy(i => i.Id)
        };

        if (!string.IsNullOrWhiteSpace(filter)) {
            items = items.Where(i => i.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        return items.OrderBy(i => i.Id).ToList();
    }

    /// <summary>
    /// Отрисовка полноценного Креативного меню (Creative Inventory GUI).
    /// </summary>
    public static void DrawCreativeMenu(GameSession session) {
        int w = Ui.Vw, h = Ui.Vh;
        var inv = session.Player.Inventory;
        var mouse = Ui.Mouse();

        const int cols = 9;
        const int visibleRows = 5;
        const int slot = 52, gap = 4;
        int gridW = cols * slot + (cols - 1) * gap;
        int panelW = gridW + 64;
        int panelH = (visibleRows + 1) * slot + (visibleRows + 1) * gap + 150;
        float px = (w - panelW) / 2f, py = (h - panelH) / 2f;

        DrawPanel(px, py, panelW, panelH);

        // Вкладки сверху
        float tabW = panelW / CreativeTabNames.Length;
        for (int t = 0; t < CreativeTabNames.Length; t++) {
            var tabRec = new Rectangle(px + t * tabW, py - 36f, tabW, 36f);
            bool isSelected = CreativeTab == t;
            bool tabHover = Raylib.CheckCollisionPointRec(mouse, tabRec);

            Raylib.DrawRectangleRec(tabRec, isSelected ? Panel : new Color(160, 160, 160, 255));
            Raylib.DrawRectangleLinesEx(tabRec, 2f, Color.Black);
            if (isSelected) {
                // Стираем нижнюю границу выбранной вкладки для эффекта соединения с окном
                Raylib.DrawRectangle((int)tabRec.X + 2, (int)py - 2, (int)tabRec.Width - 4, 4, Panel);
            }

            Fonts.DrawCentered(CreativeTabNames[t], tabRec.X + tabW / 2f, tabRec.Y + 10f, 13f, isSelected ? TextDark : new Color(40, 40, 40, 255));

            if (tabHover && Raylib.IsMouseButtonPressed(MouseButton.Left)) {
                CreativeTab = t;
                CreativeScrollRow = 0;
                SoundSystem.PlayPop();
            }
        }

        ItemDefinition? hoveredDef = null;
        float gridX = px + 20f;
        float gridY = py + 48f;
        float scrollbarX = gridX + cols * (slot + gap) + 6f;

        if (CreativeTab != 5) {
            // Поле поиска (справа вверху)
            float searchW = 180f, searchH = 28f;
            var searchRec = new Rectangle(px + panelW - searchW - 20f, py + 12f, searchW, searchH);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {
                CreativeSearchActive = Raylib.CheckCollisionPointRec(mouse, searchRec);
            }
            Raylib.DrawRectangleRec(searchRec, SlotBg);
            Raylib.DrawRectangleLinesEx(searchRec, 1.5f, CreativeSearchActive ? Color.Black : SlotBorder);
            string searchDisplay = string.IsNullOrEmpty(CreativeSearch) && !CreativeSearchActive ? "Поиск..." : CreativeSearch;
            Color searchColor = string.IsNullOrEmpty(CreativeSearch) && !CreativeSearchActive ? new Color(180, 180, 180, 255) : Color.White;
            Fonts.Draw(searchDisplay + (CreativeSearchActive && ((int)(Raylib.GetTime() * 2) % 2 == 0) ? "_" : ""), searchRec.X + 8f, searchRec.Y + 6f, 15f, searchColor);

            // Обработка ввода текста в поиск
            if (CreativeSearchActive) {
                int key = Raylib.GetCharPressed();
                while (key > 0) {
                    if (key >= 32) CreativeSearch += (char)key;
                    key = Raylib.GetCharPressed();
                }
                if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && CreativeSearch.Length > 0) {
                    CreativeSearch = CreativeSearch[..^1];
                }
            }

            // Заголовок
            Fonts.Draw("Креативный инвентарь", px + 20f, py + 16f, 18f, TextDark);

            // Получение предметов текущей вкладки
            var items = GetCreativeItems(CreativeTab, CreativeSearch);
            int totalRows = Math.Max(visibleRows, (items.Count + cols - 1) / cols);

            // Скролл колесиком
            float wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0) {
                CreativeScrollRow = Math.Clamp(CreativeScrollRow - (int)wheel, 0, Math.Max(0, totalRows - visibleRows));
            }

            int startIndex = CreativeScrollRow * cols;
            for (int r = 0; r < visibleRows; r++) {
                for (int c = 0; c < cols; c++) {
                    int itemIdx = startIndex + r * cols + c;
                    float sx = gridX + c * (slot + gap);
                    float sy = gridY + r * (slot + gap);
                    var slotRec = new Rectangle(sx, sy, slot, slot);
                    bool slotHover = Raylib.CheckCollisionPointRec(mouse, slotRec);

                    // Отрисовка слота
                    Raylib.DrawRectangleRec(slotRec, SlotBg);
                    Raylib.DrawRectangle((int)sx, (int)sy, slot, 2, SlotBorder);
                    Raylib.DrawRectangle((int)sx, (int)sy, 2, slot, SlotBorder);
                    Raylib.DrawRectangle((int)sx, (int)sy + slot - 2, slot, 2, PanelLightBorder);
                    Raylib.DrawRectangle((int)sx + slot - 2, (int)sy, 2, slot, PanelLightBorder);

                    if (slotHover) Raylib.DrawRectangleRec(slotRec, SlotHover);

                    if (itemIdx < items.Count) {
                        var def = items[itemIdx];
                        Hud.DrawItemIcon(def, new Rectangle(sx + 3f, sy + 3f, 46f, 46f), 1f);

                        if (slotHover) {
                            hoveredDef = def;

                            // Клик по предмету в креативной сетке
                            if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {
                                bool shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
                                int stackSize = def.MaxStack;
                                var newEntry = new ItemEntry(GameData.NewItem(def), stackSize);

                                if (shift) {
                                    inv.TryInsert(newEntry.Item, stackSize);
                                } else {
                                    Held = newEntry;
                                }
                                SoundSystem.PlayPop();
                            }
                        }
                    }
                }
            }

            // Полоса прокрутки (Scrollbar)
            float scrollbarY = gridY;
            float scrollbarH = visibleRows * slot + (visibleRows - 1) * gap;
            var scrollbarRec = new Rectangle(scrollbarX, scrollbarY, 14f, scrollbarH);
            Raylib.DrawRectangleRec(scrollbarRec, SlotBg);
            Raylib.DrawRectangleLinesEx(scrollbarRec, 1f, SlotBorder);

            if (totalRows > visibleRows) {
                float thumbH = Math.Max(24f, scrollbarH * visibleRows / totalRows);
                float thumbY = scrollbarY + (scrollbarH - thumbH) * CreativeScrollRow / (totalRows - visibleRows);
                var thumbRec = new Rectangle(scrollbarX + 2f, thumbY, 10f, thumbH);
                Raylib.DrawRectangleRec(thumbRec, PanelDarkBorder);
                Raylib.DrawRectangle((int)thumbRec.X, (int)thumbRec.Y, (int)thumbRec.Width, 2, PanelLightBorder);
            }
        } else {
            // Вкладка Инвентарь выживания (слоты 9..35, броня, вторая рука)
            Fonts.Draw("Инвентарь игрока (Рюкзак и Снаряжение)", px + 20f, py + 16f, 18f, TextDark);

            Fonts.Draw("Рюкзак:", gridX, gridY - 2f, 15f, TextDark);
            for (int r = 0; r < 3; r++) {
                for (int c = 0; c < cols; c++) {
                    int idx = 9 + r * cols + c;
                    float sx = gridX + c * (slot + gap);
                    float sy = gridY + 20f + r * (slot + gap);
                    DrawSlot(session, inv, sx, sy, idx, false);
                }
            }

            float armorY = gridY + 20f + 3 * (slot + gap) + 8f;
            Fonts.Draw("Броня:", gridX, armorY + 16f, 14f, TextDark);
            for (int a = 0; a < 4; a++) {
                float sx = gridX + 55f + a * (slot + gap);
                var aRec = new Rectangle(sx, armorY, slot, slot);
                bool aHover = Raylib.CheckCollisionPointRec(mouse, aRec);
                Raylib.DrawRectangleRec(aRec, SlotBg);
                Raylib.DrawRectangle((int)sx, (int)armorY, slot, 2, SlotBorder);
                Raylib.DrawRectangle((int)sx, (int)armorY, 2, slot, SlotBorder);
                Raylib.DrawRectangle((int)sx, (int)armorY + slot - 2, slot, 2, PanelLightBorder);
                Raylib.DrawRectangle((int)sx + slot - 2, (int)armorY, 2, slot, PanelLightBorder);
                if (aHover) Raylib.DrawRectangleRec(aRec, SlotHover);

                if (session.Player.Armor[a] is { } aItem) {
                    Hud.DrawItemIcon(aItem.Item.Definition, new Rectangle(sx + 3f, armorY + 3f, 46f, 46f), 1f);
                    Hud.DrawItemDurability(aItem.Item, aRec);
                    if (aHover) hoveredDef = aItem.Item.Definition;
                } else {
                    string aIcon = a switch { 0 => "Шлем", 1 => "Кираса", 2 => "Поножи", _ => "Боты" };
                    Fonts.DrawCentered(aIcon, sx + slot / 2f, armorY + 18f, 11f, new Color(130, 130, 130, 200));
                }
            }

            float offhandX = gridX + 320f;
            var offRec = new Rectangle(offhandX, armorY, slot, slot);
            bool offHover = Raylib.CheckCollisionPointRec(mouse, offRec);
            Raylib.DrawRectangleRec(offRec, SlotBg);
            Raylib.DrawRectangle((int)offhandX, (int)armorY, slot, 2, SlotBorder);
            Raylib.DrawRectangle((int)offhandX, (int)armorY, 2, slot, SlotBorder);
            Raylib.DrawRectangle((int)offhandX, (int)armorY + slot - 2, slot, 2, PanelLightBorder);
            Raylib.DrawRectangle((int)offhandX + slot - 2, (int)armorY, 2, slot, PanelLightBorder);
            if (offHover) Raylib.DrawRectangleRec(offRec, SlotHover);

            if (session.Player.OffhandEntry is { } offItem) {
                Hud.DrawItemIcon(offItem.Item.Definition, new Rectangle(offhandX + 3f, armorY + 3f, 46f, 46f), 1f);
                if (offItem.Quantity > 1) Fonts.DrawShadowed($"×{offItem.Quantity}", offhandX + 4f, armorY + 32f, 15f, Color.White);
                Hud.DrawItemDurability(offItem.Item, offRec);
                if (offHover) hoveredDef = offItem.Item.Definition;
            } else {
                Fonts.DrawCentered("Щит / 2-я рука", offhandX + slot / 2f, armorY + 18f, 10f, new Color(130, 130, 130, 200));
            }

            // Обработка кликов во вкладке инвентаря
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right)) {
                bool right = Raylib.IsMouseButtonPressed(MouseButton.Right);
                bool handled = false;

                // Клик по рюкзаку
                for (int r = 0; r < 3; r++) {
                    for (int c = 0; c < cols; c++) {
                        int idx = 9 + r * cols + c;
                        float sx = gridX + c * (slot + gap);
                        float sy = gridY + 20f + r * (slot + gap);
                        if (SlotClicked(session, inv, sx, sy, idx, right)) {
                            handled = true;
                            break;
                        }
                    }
                    if (handled) break;
                }

                // Клик по броне
                if (!handled) {
                    for (int a = 0; a < 4; a++) {
                        float sx = gridX + 55f + a * (slot + gap);
                        var aRec = new Rectangle(sx, armorY, slot, slot);
                        if (Raylib.CheckCollisionPointRec(mouse, aRec)) {
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
                            handled = true;
                            break;
                        }
                    }
                }

                // Клик по слоту второй руки
                if (!handled) {
                    var offhandRec = new Rectangle(offhandX, armorY, slot, slot);
                    if (Raylib.CheckCollisionPointRec(mouse, offhandRec)) {
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
                    }
                }
            }

            // Подсказка для рюкзака
            if (hoveredDef == null) {
                for (int r = 0; r < 3; r++) {
                    for (int c = 0; c < cols; c++) {
                        int idx = 9 + r * cols + c;
                        float sx = gridX + c * (slot + gap);
                        float sy = gridY + 20f + r * (slot + gap);
                        var sRec = new Rectangle(sx, sy, slot, slot);
                        if (Raylib.CheckCollisionPointRec(mouse, sRec) && inv.Slots[idx] is { } entry) {
                            hoveredDef = entry.Item.Definition;
                            break;
                        }
                    }
                    if (hoveredDef != null) break;
                }
            }
        }

        // Нижняя панель: Хотбар игрока (9 слотов) + Мусорный слот
        float hotbarY = gridY + visibleRows * (slot + gap) + 16f;
        Fonts.Draw("Панель быстрого доступа:", gridX, hotbarY - 18f, 14f, TextDark);

        for (int c = 0; c < cols; c++) {
            DrawSlot(session, inv, gridX + c * (slot + gap), hotbarY, c, c == session.Player.SelectedSlot);
        }

        // Слот корзины (Trash slot)
        float trashX = scrollbarX + 20f;
        float trashY = hotbarY;
        var trashRec = new Rectangle(trashX, trashY, slot, slot);
        bool trashHover = Raylib.CheckCollisionPointRec(mouse, trashRec);

        Raylib.DrawRectangleRec(trashRec, SlotBg);
        Raylib.DrawRectangle((int)trashX, (int)trashY, slot, 2, SlotBorder);
        Raylib.DrawRectangle((int)trashX, (int)trashY, 2, slot, SlotBorder);
        Raylib.DrawRectangle((int)trashX, (int)trashY + slot - 2, slot, 2, PanelLightBorder);
        Raylib.DrawRectangle((int)trashX + slot - 2, (int)trashY, 2, slot, PanelLightBorder);

        if (trashHover) Raylib.DrawRectangleRec(trashRec, new Color(240, 80, 80, 120));

        // Иконка корзины / текст
        Fonts.DrawCentered("УДАЛИТЬ", trashX + slot / 2f, trashY + 12f, 10f, trashHover ? Color.White : TextDark);
        Fonts.DrawCentered("[Клик/Shift]", trashX + slot / 2f, trashY + 28f, 10f, trashHover ? Color.White : new Color(100, 100, 100, 255));

        if (trashHover) {
            Fonts.DrawShadowed("Очистить курсор / Shift+Клик: весь инвентарь", mouse.X + 15f, mouse.Y - 20f, 14f, Color.White);
            if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {
                if (Held.HasValue && Held.Value.Quantity > 0) {
                    Held = null; // Удалить предмет из руки
                    SoundSystem.PlayPop();
                } else if (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift)) {
                    for (int c = 0; c < 36; c++) inv.RemoveAt(c); // Очистить весь инвентарь (0..35)
                    SoundSystem.PlayPop();
                }
            }
        }

        // Обработка клика по хотбару в креативе
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right)) {
            bool right = Raylib.IsMouseButtonPressed(MouseButton.Right);
            for (int col = 0; col < cols; col++) {
                if (SlotClicked(session, inv, gridX + col * (slot + gap), hotbarY, col, right)) {
                    break;
                }
            }
        }

        // Клик за пределами окна инвентаря с предметом в руке — выбросить в мир!
        if (Held.HasValue && Held.Value.Quantity > 0) {
            if ((Raylib.IsMouseButtonPressed(MouseButton.Left) || Raylib.IsMouseButtonPressed(MouseButton.Right)) &&
                !Raylib.CheckCollisionPointRec(mouse, new Rectangle(px, py, panelW, panelH))) {
                var dropPos = session.Player.Eye + session.Player.Forward * 0.5f;
                bool right = Raylib.IsMouseButtonPressed(MouseButton.Right);
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
                SoundSystem.PlayPop();
            }
        }

        // Проверка наведения на хотбар
        if (hoveredDef == null) {
            for (int c = 0; c < cols; c++) {
                var sRec = new Rectangle(gridX + c * (slot + gap), hotbarY, slot, slot);
                if (Raylib.CheckCollisionPointRec(mouse, sRec) && inv.Slots[c] is { } entry) {
                    hoveredDef = entry.Item.Definition;
                    break;
                }
            }
        }

        // Отрисовка подсказки поверх всех слотов и панелей
        if (hoveredDef != null && (!Held.HasValue || Held.Value.Quantity == 0) && !trashHover) {
            DrawItemTooltip(hoveredDef, mouse);
        }

        // Отрисовка предмета за курсором
        if (Held.HasValue && Held.Value.Quantity > 0) {
            var held = Held.Value;
            Hud.DrawItemIcon(held.Item.Definition, new Rectangle(mouse.X - 14f, mouse.Y - 14f, 28f, 28f), 1f);
            if (held.Quantity > 1) {
                Fonts.Draw($"×{held.Quantity}", mouse.X - 14f, mouse.Y + 8f, 15f, Color.White);
            }
        }
    }
}
