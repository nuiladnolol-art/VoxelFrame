using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>HUD: прицел, хотбар, здоровье, сытость, сообщения, часы.</summary>
public static class Hud {
    private const int SlotSize = 52;
    private const int SlotGap = 4;

    public static bool ShowDebugInfo = false;

    public static void Draw(GameSession session, float dt) {
        int w = Raylib.GetScreenWidth(), h = Raylib.GetScreenHeight();
        var player = session.Player;

        // Прицел: классический крестик (+) с инверсным/контрастным контуром
        int cx = w / 2, cy = h / 2;
        Raylib.DrawRectangle(cx - 8, cy - 1, 17, 3, new Color(0, 0, 0, 160));
        Raylib.DrawRectangle(cx - 1, cy - 8, 3, 17, new Color(0, 0, 0, 160));
        Raylib.DrawRectangle(cx - 7, cy, 15, 1, new Color(255, 255, 255, 240));
        Raylib.DrawRectangle(cx, cy - 7, 1, 15, new Color(255, 255, 255, 240));

        // Подводный полупрозрачный экранный фильтр (Underwater overlay)
        var eyeVoxel = session.World.GetVoxel(new Vec3i((int)MathF.Floor(player.Eye.X), (int)MathF.Floor(player.Eye.Y), (int)MathF.Floor(player.Eye.Z)));
        if (eyeVoxel.TypeId == GameData.BWater.Id) {
            Raylib.DrawRectangle(0, 0, w, h, new Color(12, 65, 175, 100)); // Полупрозрачный водный оттенок
            Raylib.DrawRectangleGradientV(0, 0, w, 70, new Color(8, 45, 130, 140), new Color(8, 45, 130, 0));
            Raylib.DrawRectangleGradientV(0, h - 70, w, 70, new Color(8, 45, 130, 0), new Color(8, 45, 130, 140));
        } else if (eyeVoxel.TypeId == GameData.BLava.Id) {
            Raylib.DrawRectangle(0, 0, w, h, new Color(220, 50, 10, 195));
        } else if (eyeVoxel.TypeId == GameData.BNetherPortal.Id) {
            float portalWave = MathF.Sin((float)session.TotalPlaySeconds * 4.0f) * 0.5f + 0.5f;
            Raylib.DrawRectangle(0, 0, w, h, new Color((byte)130, (byte)30, (byte)210, (byte)(110 + portalWave * 45)));
        }

        // Красная вспышка/виньетка при получении урона
        if (player.HurtTimer > 0f) {
            float alphaProgress = player.HurtTimer / 0.5f;
            int redAlpha = (int)(110 * Math.Clamp(alphaProgress, 0f, 1f));
            Raylib.DrawRectangle(0, 0, w, h, new Color(220, 20, 20, redAlpha));
        }

        // Индикатор перезарядки удара (Minecraft 1.9+ Attack Meter под прицелом)
        ushort selectedId = player.SelectedItem?.Id ?? 0;
        float cd = GameData.GetWeaponCooldown(selectedId);
        float charge = Math.Clamp(player.AttackRechargeTimer / cd, 0f, 1f);
        if (charge < 1.0f) {
            int meterW = 20, meterH = 4;
            int mx = cx - meterW / 2, my = cy + 12;
            Raylib.DrawRectangle(mx - 1, my - 1, meterW + 2, meterH + 2, new Color(0, 0, 0, 160));
            Raylib.DrawRectangle(mx, my, meterW, meterH, new Color(40, 40, 40, 200));
            var chargeCol = charge >= 0.85f ? new Color(255, 255, 255, 240) : new Color(180, 180, 180, 220);
            Raylib.DrawRectangle(mx, my, (int)(meterW * charge), meterH, chargeCol);
        }

        // Сообщения.
        float msgY = 14f;
        foreach (var (text, age) in session.Messages.Reverse()) {
            int alpha = (int)(255 * Math.Clamp(1f - age / 6f, 0f, 1f));
            Fonts.DrawShadowed(text, 14f, msgY, 20f, new Color(255, 255, 255, alpha));
            msgY += 24f;
        }

        // Инфо-панель: Координаты, Направление взгляда, Биом, Время, FPS
        int px = (int)MathF.Floor(player.Position.X);
        int py = (int)MathF.Floor(player.Position.Y);
        int pz = (int)MathF.Floor(player.Position.Z);
        var curBiome = session.World.Generator.GetBiome(px, py, pz);
        string biomeName = WorldGenerator.GetBiomeName(curBiome);

        string facing;
        if (MathF.Abs(player.Forward.X) > MathF.Abs(player.Forward.Z)) {
            facing = player.Forward.X > 0 ? "Восток (+X)" : "Запад (-X)";
        } else {
            facing = player.Forward.Z > 0 ? "Юг (+Z)" : "Север (-Z)";
        }

        // Верхний правый угол: компактная инфо-панель координат и мира
        float rightPanelX = w - 245f;
        Raylib.DrawRectangleRounded(new Rectangle(rightPanelX - 8, 6, 240, 110), 0.12f, 4, new Color(15, 20, 30, 140));
        Raylib.DrawRectangleRoundedLinesEx(new Rectangle(rightPanelX - 8, 6, 240, 110), 0.12f, 4, 1.0f, new Color(80, 100, 140, 100));

        Fonts.DrawShadowed($"XYZ: {player.Position.X:F1} / {player.Position.Y:F1} / {player.Position.Z:F1}", rightPanelX, 10f, 16f, new Color(255, 240, 120, 255));
        Fonts.DrawShadowed($"Блок: {px} {py} {pz}", rightPanelX, 30f, 15f, new Color(220, 225, 235, 255));
        Fonts.DrawShadowed($"Взгляд: {facing}", rightPanelX, 48f, 15f, new Color(175, 215, 255, 255));
        Fonts.DrawShadowed($"Биом: {biomeName}", rightPanelX, 66f, 15f, new Color(150, 235, 175, 255));
        Fonts.DrawShadowed($"День: {session.DayNight.ClockText}  |  FPS: {Raylib.GetFPS()}", rightPanelX, 84f, 15f, Color.White);

        if (ShowDebugInfo) {
            DrawF3DebugOverlay(session, player, px, py, pz, biomeName, facing, w, h);
        }

        // Хотбар и Вторая рука (Off-hand).
        var inv = player.Inventory;
        int hotbarW = 9 * SlotSize + 8 * SlotGap;
        int x0 = w / 2 - hotbarW / 2, y0 = h - SlotSize - 10;

        // Слот второй руки (слева от хотбара, как в Minecraft Java Edition)
        var offhandRect = new Rectangle(x0 - SlotSize - 14, y0, SlotSize, SlotSize);
        Raylib.DrawRectangleRounded(offhandRect, 0.15f, 6, new Color(45, 45, 60, 200));
        Raylib.DrawRectangleRoundedLinesEx(offhandRect, 0.15f, 6, 1.5f, new Color(80, 90, 115, 255));
        Fonts.Draw("F", offhandRect.X + 4f, offhandRect.Y + 3f, 11f, new Color(170, 190, 225, 180));

        if (player.OffhandItem != null && player.OffhandCount > 0) {
            DrawItemIcon(player.OffhandItem, offhandRect, 0.7f);
            if (player.OffhandCount > 1) {
                Fonts.DrawShadowed($"×{player.OffhandCount}", offhandRect.X + 3f, offhandRect.Y + offhandRect.Height - 20f, 15f, Color.White);
            }
        }

        for (int i = 0; i < 9; i++) {
            var rect = new Rectangle(x0 + i * (SlotSize + SlotGap), y0, SlotSize, SlotSize);
            bool selected = i == player.SelectedSlot;
            
            // Rounded slots with dark transparent background
            Raylib.DrawRectangleRounded(rect, 0.15f, 6, selected ? new Color(90, 100, 120, 230) : new Color(40, 45, 55, 180));
            
            // Glowing yellow-gold border for selected slot, dark border for others
            var borderColor = selected ? new Color(255, 220, 120, 255) : new Color(60, 65, 80, 255);
            Raylib.DrawRectangleRoundedLinesEx(rect, 0.15f, 6, selected ? 2.5f : 1.5f, borderColor);
            
            // Subtle slot number (1-9) in top-left corner
            Fonts.Draw($"{i + 1}", rect.X + 4f, rect.Y + 3f, 11f, selected ? new Color(255, 220, 120, 200) : new Color(140, 150, 170, 150));

            var entry = inv.Slots[i];
            if (entry != null) {
                DrawItemIcon(entry.Value.Item.Definition, rect, 0.7f);
                if (entry.Value.Quantity > 1) {
                    Fonts.DrawShadowed($"×{entry.Value.Quantity}", rect.X + 3f, rect.Y + rect.Height - 20f, 15f, Color.White);
                }
                if (entry.Value.Item.Condition < 0.999) {
                    float dur = (float)entry.Value.Item.Condition;
                    var barRec = new Rectangle(rect.X + 6f, rect.Y + rect.Height - 6f, 40f, 3f);
                    Raylib.DrawRectangleRec(barRec, new Color(20, 20, 20, 220));
                    var color = dur > 0.5f ? new Color(50, 220, 50, 255) : dur > 0.2f ? new Color(240, 200, 30, 255) : new Color(240, 40, 40, 255);
                    Raylib.DrawRectangleRec(new Rectangle(rect.X + 6f, rect.Y + rect.Height - 6f, 40f * dur, 3f), color);
                }
            }
        }

        // Название предмета при переключении слота хотбара (над индикаторами здоровья и сытости).
        if (player.SlotToastTimer > 0f && player.SlotToastText.Length > 0) {
            float fade = Math.Clamp(player.SlotToastTimer / 0.4f, 0f, 1f);
            float toastY = h - 116f;
            float tw = Fonts.Measure(player.SlotToastText, 28f);
            Fonts.DrawShadowed(player.SlotToastText, w / 2f - tw / 2f, toastY, 28f,
                new Color((byte)255, (byte)255, (byte)255, (byte)(255 * fade)));
        }

        // Предмет во второй руке (левый нижний угол, вид от первого лица)
        if (player.OffhandItem != null && player.OffhandCount > 0) {
            float offhandSize = player.IsBlocking ? 155f : 110f;
            float bob = player.BobOffset * 50f;
            float leftHandX = player.IsBlocking ? (w / 2f - offhandSize - 20f) : 35f;
            float leftHandY = player.IsBlocking ? (h - offhandSize - 60f) : (h - offhandSize - 10f + bob);
            float offhandRot = player.IsBlocking ? 0.05f : -0.35f;
            var leftRect = new Rectangle(leftHandX, leftHandY, offhandSize, offhandSize);
            DrawItemIconRotated(player.OffhandItem, leftRect, 1f, offhandRot);
        }

        // Предмет в правой руке (правый нижний угол, классический Minecraft-вид от первого лица)
        if (player.SelectedEntry is { } held) {
            float handSize = 140f;
            float bob = player.BobOffset * 70f;
            float handX = w - handSize - 35f;
            float handY = h - handSize - 10f + bob;

            if (player.EatTimer > 0f) {
                handY += MathF.Sin(player.EatTimer * 14f) * 8f;
            }

            float swing = 0f;
            if (held.Item.Definition.Id == GameData.BowItem.Id && player.BowCharge > 0f) {
                // Натягивание лука к центру экрана с дрожью
                float pull = player.BowCharge;
                float shake = pull * (Random.Shared.NextSingle() - 0.5f) * 4f;
                handX = w / 2f + 10f - pull * 60f + shake;
                handY = h / 2f + 20f - pull * 30f + shake;
                swing = -0.2f - pull * 0.4f;
            } else if (player.BreakProgress > 0f && player.BreakDuration > 0f) {
                swing = MathF.Sin(player.BreakProgress / player.BreakDuration * MathF.PI) * 0.7f;
            } else if (player.AttackTimer > 0f) {
                swing = MathF.Sin((1f - player.AttackTimer / Player.AttackCooldown) * MathF.PI) * 0.9f;
            }
            var handRect = new Rectangle(handX, handY, handSize, handSize);
            DrawItemIconRotated(held.Item.Definition, handRect, 1f, swing);
        }

        // Здоровье (сердечки слева) и Сытость (окорочка справа) над хотбаром
        float vitalsY = h - 84f;
        float iconSize = 20f;
        float spacing = 15f;
        bool isHurt = player.HurtTimer > 0f;
        bool isLowHealth = player.Health <= 4f;
        DrawStatusRow(TextureAtlas.THeart, TextureAtlas.THeartEmpty, w / 2f - 165f, vitalsY, iconSize, spacing, player.Health, isHurt || isLowHealth);

        bool isStarving = player.Hunger <= 6f;
        DrawStatusRow(TextureAtlas.TFood, TextureAtlas.TFoodEmpty, w / 2f + 15f, vitalsY, iconSize, spacing, player.Hunger, isStarving);

        // Индикатор воздуха под водой (пузырьки над полосой сытости)
        if (player.AirSupply < 10f) {
            int bubbles = (int)MathF.Ceiling(player.AirSupply);
            for (int b = 0; b < 10; b++) {
                float bx = w / 2f + 150f - b * 13f;
                var bColor = b < bubbles ? new Color(60, 180, 255, 230) : new Color(30, 60, 90, 120);
                Raylib.DrawCircle((int)bx, (int)(vitalsY - 14f), 5f, bColor);
                Raylib.DrawCircleLines((int)bx, (int)(vitalsY - 14f), 5f, new Color(20, 30, 50, 180));
            }
        }

        // Прогресс ломания.
        if (player.BreakProgress > 0f && player.BreakDuration > 0f) {
            var rect = new Rectangle(w / 2f - 30f, h / 2f + 14f, 60f, 5f);
            Raylib.DrawRectangleRec(rect, new Color(0, 0, 0, 160));
            Raylib.DrawRectangleRec(new Rectangle(rect.X, rect.Y, rect.Width * player.BreakProgress / player.BreakDuration, rect.Height),
                Color.White);
        }

        // Анимация активации Тотема Бессмертия (парящий тотем по центру + золотая вспышка)
        if (player.TotemAnimationTimer > 0f) {
            float t = 1.0f - (player.TotemAnimationTimer / 2.5f);
            float alpha = MathF.Sin(t * MathF.PI);
            byte flashA = (byte)(160 * Math.Clamp(alpha, 0f, 1f));
            Raylib.DrawRectangle(0, 0, w, h, new Color((byte)255, (byte)215, (byte)0, flashA));

            float totemScale = 1.0f + 0.3f * MathF.Sin(t * MathF.PI);
            float totemSize = 220f * totemScale;
            float totemY = h / 2f - totemSize / 2f - MathF.Sin(t * MathF.PI) * 40f;
            var totemRect = new Rectangle(w / 2f - totemSize / 2f, totemY, totemSize, totemSize);
            DrawItemIconByTile((byte)TextureAtlas.TTotem, totemRect);

            string tmsg = "ТОТЕМ БЕССМЕРТИЯ АКТИВИРОВАН!";
            float mw = Fonts.Measure(tmsg, 32f);
            Fonts.DrawShadowed(tmsg, w / 2f - mw / 2f, h / 2f + 100f, 32f, new Color((byte)255, (byte)240, (byte)120, (byte)(255 * alpha)));
        }

        // Анимация сна (затемнение и плавный переход к утру)
        if (session.IsSleeping) {
            float alphaFactor = MathF.Sin(MathF.Min(1f, session.SleepProgress / 2.0f) * MathF.PI);
            byte overlayAlpha = (byte)(235 * Math.Clamp(alphaFactor * 1.5f, 0f, 1f));
            Raylib.DrawRectangle(0, 0, w, h, new Color((byte)5, (byte)5, (byte)15, overlayAlpha));
            string sleepText = "Сон... Пропуск ночи";
            float tw = Fonts.Measure(sleepText, 28f);
            Fonts.DrawShadowed(sleepText, w / 2f - tw / 2f, h / 2f - 14f, 28f, new Color((byte)255, (byte)255, (byte)255, overlayAlpha));
        }
    }

    private static void DrawStatusRow(byte filledTile, byte emptyTile, float xStart, float y, float size, float spacing, float value, bool shake = false) {
        for (int i = 0; i < 10; i++) {
            float x = xStart + i * spacing;
            float yOffset = shake ? (MathF.Sin((float)Raylib.GetTime() * 25f + i * 1.5f) * 2.5f) : 0f;
            var dest = new Rectangle(x, y + yOffset, size, size);
            
            // Сначала рисуем пустое сердечко/еду как фон
            DrawItemIconByTile(emptyTile, dest);
            
            float valForThisIcon = value - i * 2f;
            if (valForThisIcon >= 2f) {
                DrawItemIconByTile(filledTile, dest);
            } else if (valForThisIcon >= 0.5f) {
                var src = new Rectangle(
                    filledTile % TextureAtlas.Cols * TextureAtlas.TilePx,
                    filledTile / TextureAtlas.Cols * TextureAtlas.TilePx,
                    TextureAtlas.TilePx / 2f, TextureAtlas.TilePx);
                var destHalf = new Rectangle(x, y, size / 2f, size);
                unsafe {
                    Raylib.DrawTexturePro(TextureAtlas.Atlas, src, destHalf, new System.Numerics.Vector2(0, 0), 0f, Color.White);
                }
            }
        }
    }

    private static void DrawItemIconByTile(byte tile, Rectangle dest) {
        var src = new Rectangle(
            tile % TextureAtlas.Cols * TextureAtlas.TilePx,
            tile / TextureAtlas.Cols * TextureAtlas.TilePx,
            TextureAtlas.TilePx, TextureAtlas.TilePx);
        unsafe {
            Raylib.DrawTexturePro(TextureAtlas.Atlas, src, dest, new System.Numerics.Vector2(0, 0), 0f, Color.White);
        }
    }

    /// <summary>Иконка предмета (тайл атласа) в прямоугольнике слота.</summary>
    public static void DrawItemIcon(VoxelFrame.Core.Inventory.ItemDefinition def, Rectangle slot, float scale) {
        byte tile = TextureAtlas.ItemTile(def.Id);
        var src = new Rectangle(
            tile % TextureAtlas.Cols * TextureAtlas.TilePx,
            tile / TextureAtlas.Cols * TextureAtlas.TilePx,
            TextureAtlas.TilePx, TextureAtlas.TilePx);
        float size = MathF.Min(slot.Width, slot.Height) * scale;
        var dest = new Rectangle(
            slot.X + (slot.Width - size) / 2f,
            slot.Y + (slot.Height - size) / 2f,
            size, size);
        unsafe {
            Raylib.DrawTexturePro(TextureAtlas.Atlas, src, dest, new System.Numerics.Vector2(0, 0), 0f, Color.White);
        }
    }

    /// <summary>Иконка предмета с поворотом вокруг центра (замах в руке).</summary>
    private static void DrawItemIconRotated(VoxelFrame.Core.Inventory.ItemDefinition def, Rectangle slot, float scale, float rotation) {
        byte tile = TextureAtlas.ItemTile(def.Id);
        var src = new Rectangle(
            tile % TextureAtlas.Cols * TextureAtlas.TilePx,
            tile / TextureAtlas.Cols * TextureAtlas.TilePx,
            TextureAtlas.TilePx, TextureAtlas.TilePx);
        float size = MathF.Min(slot.Width, slot.Height) * scale;
        var dest = new Rectangle(slot.X + slot.Width / 2f, slot.Y + slot.Height / 2f, size, size);
        unsafe {
            Raylib.DrawTexturePro(TextureAtlas.Atlas, src, dest, new System.Numerics.Vector2(size / 2f, size / 2f), rotation, Color.White);
        }
    }

    /// <summary>Расширенный экран отладки Minecraft F3.</summary>
    private static void DrawF3DebugOverlay(GameSession session, Player player, int px, int py, int pz, string biomeName, string facing, int w, int h) {
        // Левая панель F3
        float y = 10f;
        void LineL(string text, Color? col = null) {
            int tw = (int)(text.Length * 9.2f);
            Raylib.DrawRectangle(8, (int)y - 1, tw + 8, 19, new Color(0, 0, 0, 140));
            Fonts.Draw(text, 12f, y, 16f, col ?? Color.White);
            y += 20f;
        }

        LineL($"VoxelFrame 0.9.0 ({Raylib.GetFPS()} fps, {Raylib.GetFrameTime() * 1000f:F1} ms)");
        LineL($"XYZ: {player.Position.X:F3} / {player.Position.Y:F5} / {player.Position.Z:F3}", new Color(255, 240, 120, 255));
        LineL($"Block: {px} {py} {pz} [{(px & 15)} {(py & 15)} {(pz & 15)} in sub-chunk]");
        LineL($"Chunk: {px >> 4} {py >> 4} {pz >> 4} in chunk [{px >> 4}, {pz >> 4}]");
        LineL($"Facing: {facing} (Yaw: {player.Yaw * 180f / MathF.PI:F1}°, Pitch: {player.Pitch * 180f / MathF.PI:F1}°)", new Color(175, 215, 255, 255));
        LineL($"Light: {session.World.GetSunLight(new Vec3i(px, py, pz))} (sky {session.DayNight.SkyFactor * 15f:F1})");
        LineL($"Biome: {biomeName}", new Color(150, 235, 175, 255));
        LineL($"Time: {session.DayNight.ClockText} (tod {session.DayNight.TimeOfDay:F3})");

        if (session.HasTarget) {
            var tb = session.TargetBlock;
            var vox = session.World.GetVoxel(tb);
            var block = GameData.GetBlock(vox.TypeId);
            LineL($"Targeted Block: {tb.X}, {tb.Y}, {tb.Z} ({block.Name})", new Color(255, 200, 100, 255));
        }

        // Правая панель F3
        float ry = 130f;
        void LineR(string text, Color? col = null) {
            int tw = (int)(text.Length * 9.2f);
            float rx = w - tw - 16f;
            Raylib.DrawRectangle((int)rx - 4, (int)ry - 1, tw + 8, 19, new Color(0, 0, 0, 140));
            Fonts.Draw(text, rx, ry, 16f, col ?? Color.White);
            ry += 20f;
        }

        LineR($".NET 10.0 (Windows x64)");
        LineR($"Mem: {GC.GetTotalMemory(false) / (1024 * 1024)} MB");
        LineR($"Mobs: {session.World.HostileMobs.Count} hostile, {session.World.Animals.Count} passive");
        LineR($"Pickups: {session.World.Pickups.Count}");
        LineR($"Display: {w}x{h}");
    }
}
