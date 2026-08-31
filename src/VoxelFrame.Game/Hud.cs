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
        int w = Ui.Vw, h = Ui.Vh;
        var player = session.Player;

        // Прицел: классический крестик (+) с инверсным контуром + контекстная индикация интерактива
        int cx = w / 2, cy = h / 2;
        bool isInteractable = false;
        if (session.HasTarget) {
            ushort tbId = session.World.GetVoxel(session.TargetBlock).TypeId;
            isInteractable = tbId == GameData.BWorkbench.Id || tbId == GameData.BFurnace.Id ||
                             tbId == GameData.BChest.Id || tbId == GameData.BBed.Id ||
                             tbId == GameData.BBedHead.Id || GameData.IsDoor(tbId);
        }

        Color crosshairBg = new Color(0, 0, 0, 160);
        Color crosshairFg = isInteractable ? new Color(255, 230, 120, 250) : new Color(255, 255, 255, 240);

        Raylib.DrawRectangle(cx - 8, cy - 1, 17, 3, crosshairBg);
        Raylib.DrawRectangle(cx - 1, cy - 8, 3, 17, crosshairBg);
        Raylib.DrawRectangle(cx - 7, cy, 15, 1, crosshairFg);
        Raylib.DrawRectangle(cx, cy - 7, 1, 15, crosshairFg);

        if (isInteractable) {
            Raylib.DrawCircle(cx, cy, 2.5f, new Color(255, 215, 80, 255));
        }

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

        // Красная виньетка по краям экрана при получении урона
        if (player.HurtTimer > 0f) {
            float alphaProgress = player.HurtTimer / 0.5f;
            int redAlpha = (int)(170 * Math.Clamp(alphaProgress, 0f, 1f));
            int redAlphaCenter = (int)(25 * Math.Clamp(alphaProgress, 0f, 1f));
            Raylib.DrawRectangle(0, 0, w, h, new Color(200, 20, 20, redAlphaCenter));
            Raylib.DrawRectangleGradientV(0, 0, w, 90, new Color(190, 0, 0, redAlpha), new Color(190, 0, 0, 0));
            Raylib.DrawRectangleGradientV(0, h - 90, w, 90, new Color(190, 0, 0, 0), new Color(190, 0, 0, redAlpha));
            Raylib.DrawRectangleGradientH(0, 0, 90, h, new Color(190, 0, 0, redAlpha), new Color(190, 0, 0, 0));
            Raylib.DrawRectangleGradientH(w - 90, 0, 90, h, new Color(190, 0, 0, 0), new Color(190, 0, 0, redAlpha));
        }

        // Индикатор перезарядки удара (Attack Meter под прицелом)
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

        // Всплывающие уведомления (максимум 2 последних сообщения, компактно)
        float msgY = 14f;
        int msgCount = 0;
        foreach (var (text, age) in session.Messages) {
            if (age >= 2.5f) continue;
            if (msgCount >= 2) break;
            int alpha = (int)(255 * Math.Clamp(1f - age / 2.5f, 0f, 1f));
            Fonts.DrawShadowed(text, 14f, msgY, 17f, new Color(255, 255, 255, alpha));
            msgY += 22f;
            msgCount++;
        }

        // Кинематографичный Заголовок и Субтитры по центру экрана (Cinema Titles & Boss Dialogues)
        if (session.TitleTimer > 0f && !string.IsNullOrEmpty(session.CurrentTitle)) {
            float fade = 1.0f;
            float elapsed = session.TitleDuration - session.TitleTimer;
            if (elapsed < 0.5f) fade = elapsed / 0.5f;
            else if (session.TitleTimer < 0.8f) fade = session.TitleTimer / 0.8f;
            fade = Math.Clamp(fade, 0f, 1f);

            int alpha = (int)(255 * fade);
            float titleSize = 38f;
            float subSize = 22f;
            float titleW = Fonts.Measure(session.CurrentTitle, titleSize);
            float subW = string.IsNullOrEmpty(session.CurrentSubtitle) ? 0f : Fonts.Measure(session.CurrentSubtitle, subSize);

            float titleX = (w - titleW) / 2f;
            float titleY = h * 0.28f;

            // Стильная полупрозрачная затемненная подложка
            float bannerW = MathF.Max(titleW, subW) + 80f;
            float bannerH = string.IsNullOrEmpty(session.CurrentSubtitle) ? 60f : 96f;
            Raylib.DrawRectangle((int)((w - bannerW) / 2f), (int)(titleY - 12f), (int)bannerW, (int)bannerH, new Color(10, 8, 15, (int)(160 * fade)));

            // Отрисовка заголовка
            var tCol = new Color((int)session.TitleColor.R, (int)session.TitleColor.G, (int)session.TitleColor.B, alpha);
            Fonts.DrawShadowed(session.CurrentTitle, titleX, titleY, titleSize, tCol);

            // Отрисовка субтитров / реплики
            if (!string.IsNullOrEmpty(session.CurrentSubtitle)) {
                float subX = (w - subW) / 2f;
                float subY = titleY + 44f;
                var sCol = new Color((int)session.SubtitleColor.R, (int)session.SubtitleColor.G, (int)session.SubtitleColor.B, alpha);
                Fonts.DrawShadowed(session.CurrentSubtitle, subX, subY, subSize, sCol);
            }
        }

        // Индикатор прогресса поедания пищи (1.6 сек)
        if (player.EatingTimer > 0f) {
            float eatFrac = Math.Clamp(player.EatingTimer / 1.6f, 0f, 1f);
            int barW = 120, barH = 7;
            int bx = (w - barW) / 2, by = h / 2 + 36;
            Raylib.DrawRectangle(bx - 2, by - 2, barW + 4, barH + 4, new Color(0, 0, 0, 180));
            Raylib.DrawRectangle(bx, by, (int)(barW * eatFrac), barH, new Color(110, 220, 70, 240));
        }

        // Босс-бар: Слизень Края (когда он жив и игрок в Энде)
        if (session.World.Dimension == Dimension.End && session.World.EndBoss is { Alive: true } sb) {
            float frac = Math.Clamp(sb.Health / EndSlime.MaxHealth, 0f, 1f);
            int barW = Math.Min(360, w - 40);
            const int barH = 14;
            int bx = (w - barW) / 2;
            const int by = 52;
            Raylib.DrawRectangle(bx - 3, by - 3, barW + 6, barH + 6, new Color(0, 0, 0, 180));
            Raylib.DrawRectangle(bx, by, barW, barH, new Color(40, 30, 50, 230));
            var fillCol = frac > 0.5f ? new Color(70, 160, 140, 255)
                : frac > 0.25f ? new Color(200, 160, 40, 255)
                : new Color(200, 50, 50, 255);
            Raylib.DrawRectangle(bx, by, (int)(barW * frac), barH, fillCol);
            Fonts.DrawShadowed("Слизень Края", bx + 6, by - 26, 20f, new Color(255, 255, 255, 235));
        }

        // Босс-бар: Истинный Слизень Края (в Бездне)
        if (session.World.Dimension == Dimension.Void && session.World.TrueVoidBoss is { Alive: true } tb) {
            float frac = Math.Clamp(tb.Health / TrueEndSlime.MaxHealth, 0f, 1f);
            int barW = Math.Min(420, w - 40);
            const int barH = 16;
            int bx = (w - barW) / 2;
            const int by = 52;
            Raylib.DrawRectangle(bx - 4, by - 4, barW + 8, barH + 8, new Color(0, 0, 0, 210));
            Raylib.DrawRectangle(bx, by, barW, barH, new Color(25, 10, 35, 240));
            var fillCol = frac > 0.66f ? new Color(155, 40, 220, 255)
                : frac > 0.33f ? new Color(220, 40, 130, 255)
                : new Color(255, 30, 30, 255);
            Raylib.DrawRectangle(bx, by, (int)(barW * frac), barH, fillCol);
            Fonts.DrawShadowed($"Истинный Слизень Края  [{tb.Health:F0} / {TrueEndSlime.MaxHealth:F0}]", bx + 6, by - 26, 20f, new Color(255, 210, 255, 250));
        }

        // Инфо-панель: Координаты, Направление взгляда, Биом, Время, FPS (F3)
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

        if (ShowDebugInfo) {
            DrawF3DebugOverlay(session, player, px, py, pz, biomeName, facing, w, h);
        }

        // Хотбар и Вторая рука (Off-hand).
        var inv = player.Inventory;
        int hotbarW = 9 * SlotSize + 8 * SlotGap;
        int x0 = w / 2 - hotbarW / 2, y0 = h - SlotSize - 10;

        // Слот второй руки (слева от хотбара)
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
            float pulse = MathF.Sin((float)Raylib.GetTime() * 4.5f) * 0.15f + 0.85f;
            var borderColor = selected ? new Color((byte)255, (byte)(225 * pulse), (byte)(95 * pulse), (byte)255) : new Color(60, 65, 80, 255);
            Raylib.DrawRectangleRoundedLinesEx(rect, 0.15f, 6, selected ? 2.5f : 1.5f, borderColor);
            
            // Subtle slot number (1-9) in top-left corner
            Fonts.Draw($"{i + 1}", rect.X + 4f, rect.Y + 3f, 11f, selected ? new Color(255, 220, 120, 200) : new Color(140, 150, 170, 150));

            var entry = inv.Slots[i];
            if (entry != null) {
                DrawItemIcon(entry.Value.Item.Definition, rect, 0.7f);
                if (entry.Value.Quantity > 1) {
                    Fonts.DrawShadowed($"×{entry.Value.Quantity}", rect.X + 3f, rect.Y + rect.Height - 20f, 15f, Color.White);
                }
                // Полоска прочности инструмента/оружия/брони
                DrawItemDurability(entry.Value.Item, rect);
            }
        }

        // Название предмета при переключении слота хотбара (над индикаторами здоровья и сытости).
        if (player.SlotToastTimer > 0f && player.SlotToastText.Length > 0 && session.ActionbarTimer <= 0f) {
            float fade = Math.Clamp(player.SlotToastTimer / 0.4f, 0f, 1f);
            float toastY = session.GameMode == GameMode.Creative ? (h - 96f) : (h - 124f);
            float tw = Fonts.Measure(player.SlotToastText, 28f);
            Fonts.DrawShadowed(player.SlotToastText, w / 2f - tw / 2f, toastY, 28f,
                new Color((byte)255, (byte)255, (byte)255, (byte)(255 * fade)));
        }

        // Actionbar над хотбаром (статусные уведомления, трек пластинки и т.д., минимум 3+ сек)
        if (session.ActionbarTimer > 0f && !string.IsNullOrEmpty(session.ActionbarText)) {
            float fade = 1.0f;
            float elapsed = session.ActionbarDuration - session.ActionbarTimer;
            if (elapsed < 0.3f) fade = elapsed / 0.3f;
            else if (session.ActionbarTimer < 0.8f) fade = session.ActionbarTimer / 0.8f;
            fade = Math.Clamp(fade, 0f, 1f);

            int alpha = (int)(255 * fade);
            float fontSize = 22f;
            float textW = Fonts.Measure(session.ActionbarText, fontSize);
            float barX = (w - textW) / 2f;
            float barY = session.GameMode == GameMode.Creative ? (h - 96f) : (h - 124f);

            // Тёмная полупрозрачная плашка под текстом
            Raylib.DrawRectangle((int)(barX - 10f), (int)(barY - 3f), (int)(textW + 20f), 26, new Color((byte)10, (byte)10, (byte)15, (byte)(160 * fade)));
            
            var col = new Color((int)session.ActionbarColor.R, (int)session.ActionbarColor.G, (int)session.ActionbarColor.B, alpha);
            Fonts.DrawShadowed(session.ActionbarText, barX, barY, fontSize, col);
        }

        // Предмет во второй руке (левый нижний угол, вид от первого лица)
        if (player.OffhandItem != null && player.OffhandCount > 0) {
            float offhandSize = player.IsBlocking ? 155f : 110f;
            float bob = player.BobOffset * 50f;
            float leftHandX = player.IsBlocking ? (w / 2f - offhandSize - 20f) : 35f;
            float leftHandY = player.IsBlocking ? (h - offhandSize - 60f) : (h - offhandSize - 10f + bob);
            float offhandRot = player.IsBlocking ? 0.05f : -0.35f;
            var leftRect = new Rectangle(leftHandX, leftHandY, offhandSize, offhandSize);
            DrawHandHeldItem(player.OffhandItem, leftRect, 1f, offhandRot);
        }

        // Предмет в правой руке (вид от первого лица)
        if (player.SelectedEntry is { } held) {
            float handSize = 140f;
            float bob = player.BobOffset * 70f;
            float handX = w - handSize - 35f;
            float handY = h - handSize - 10f + bob;

            if (player.EatTimer > 0f) {
                handY += MathF.Sin(player.EatTimer * 14f) * 8f;
            }

            float swing = 0f;
            float hStretch = 1f;
            if (held.Item.Definition.Id == GameData.BowItem.Id && player.BowCharge > 0f) {
                // Лук не уезжает к центру экрана — приподнимается вверх-влево и тянется в ширину
                float pull = player.BowCharge;
                float shake = pull * (Random.Shared.NextSingle() - 0.5f) * 3f;
                handX += shake - pull * 22f;   // немного влево
                handY += shake - pull * 14f;   // немного вверх
                swing = 0.14f + pull * 0.22f;  // наклон: левый конец лука вверх (вверх-влево)
                hStretch = 1f + pull * 0.6f;   // визуально растягивается по горизонтали
            } else if (player.BreakProgress > 0f && player.BreakDuration > 0f) {
                swing = MathF.Sin(player.BreakProgress / player.BreakDuration * MathF.PI) * 0.7f;
            } else if (player.AttackTimer > 0f) {
                swing = MathF.Sin((1f - player.AttackTimer / Player.AttackCooldown) * MathF.PI) * 0.9f;
            }
            var handRect = new Rectangle(handX, handY, handSize, handSize);
            DrawHandHeldItem(held.Item.Definition, handRect, 1f, swing, hStretch);
        }

        // Здоровье (сердечки слева), Сытость (окорочка справа) и Кислород (в режиме Выживания)
        if (session.GameMode != GameMode.Creative) {
            float vitalsY = h - 84f;
            float iconSize = 20f;
            float spacing = 15f;
            bool isHurt = player.HurtTimer > 0f;
            bool isLowHealth = player.Health <= 4f;
            DrawStatusRow(TextureAtlas.THeart, TextureAtlas.THeartEmpty, w / 2f - 165f, vitalsY, iconSize, spacing, player.Health, isHurt || isLowHealth);

            // Шкала брони над сердечками здоровья
            int totalArmor = player.GetTotalArmorPoints();
            if (totalArmor > 0) {
                DrawStatusRow(TextureAtlas.TArmorIcon, TextureAtlas.TArmorIconEmpty, w / 2f - 165f, vitalsY - 14f, iconSize, spacing, totalArmor, false);
            }

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

        // ── ЧАТ И КОМАНДЫ ────────────────────────────────────────────────────
        bool inChat = session.Ui == UiState.Chat;
        float chatBaseY = inChat ? h - 42f : h - 90f;
        int drawnLines = 0;
        int maxLines = inChat ? 14 : 8;

        for (int i = session.ChatLog.Count - 1; i >= 0 && drawnLines < maxLines; i--) {
            var (text, col, timer) = session.ChatLog[i];
            float remaining = inChat ? 1.0f : Math.Clamp(timer / 1.5f, 0f, 1f);
            if (!inChat && timer <= 0f) continue;

            byte alpha = inChat ? (byte)255 : (byte)(255 * remaining);
            byte bgAlpha = inChat ? (byte)150 : (byte)(110 * remaining);

            float lineY = chatBaseY - drawnLines * 22f;
            float textWidth = Fonts.Measure(text, 18f);

            Raylib.DrawRectangle(6, (int)lineY - 2, (int)textWidth + 12, 20, new Color((byte)0, (byte)0, (byte)0, bgAlpha));
            Fonts.DrawShadowed(text, 10f, lineY, 18f, new Color(col.R, col.G, col.B, alpha));
            drawnLines++;
        }

        // Строка ввода чата
        if (inChat) {
            bool cursorBlink = ((int)(Raylib.GetTime() * 2.5)) % 2 == 0;
            Raylib.DrawRectangle(6, h - 34, w - 12, 28, new Color(0, 0, 0, 190));
            Raylib.DrawRectangleLines(6, h - 34, w - 12, 28, new Color(180, 180, 180, 220));
            
            string displayText = "> " + session.ChatInput + (cursorBlink ? "_" : "");
            Fonts.DrawShadowed(displayText, 12f, h - 30, 20f, Color.White);
        }

        // ── СПИСОК ИГРОКОВ (TAB PLAYER LIST) В МУЛЬТИПЛЕЕРЕ ─────────────────
        if (Raylib.IsKeyDown(KeyboardKey.Tab) && (GameClient.Active != null || GameServer.Active != null)) {
            var playerList = new System.Collections.Generic.List<(string name, float hp, bool isHost, Dimension dim)>();
            playerList.Add((session.Player.Name, session.Player.Health, GameServer.Active != null, session.World.Dimension));

            if (GameClient.Active != null) {
                foreach (var rp in GameClient.Active.RemotePlayers) {
                    playerList.Add((rp.Name, rp.Health, rp.Id == 1, rp.Dimension));
                }
            } else if (GameServer.Active != null) {
                foreach (var cl in GameServer.Active.Clients) {
                    playerList.Add((cl.Name, cl.Health, false, cl.Dimension));
                }
            }

            int tabW = Math.Min(440, w - 80);
            int rowH = 26;
            int tabH = 34 + playerList.Count * rowH;
            int tabX = (w - tabW) / 2;
            int tabY = 32;

            var tabRect = new Rectangle(tabX, tabY, tabW, tabH);
            Raylib.DrawRectangleRounded(tabRect, 0.12f, 4, new Color(18, 22, 32, 220));
            Raylib.DrawRectangleRoundedLinesEx(tabRect, 0.12f, 4, 1.2f, new Color(55, 75, 110, 230));

            Fonts.DrawCentered($"Игроки онлайн ({playerList.Count})", tabX + tabW / 2f, tabY + 8f, 15f, new Color(255, 215, 90, 255));

            float curRowY = tabY + 30f;
            foreach (var p in playerList) {
                var rowRect = new Rectangle(tabX + 8, curRowY, tabW - 16, rowH - 4);
                Raylib.DrawRectangleRounded(rowRect, 0.15f, 2, new Color(28, 34, 48, 180));

                string prefix = p.isHost ? "👑 " : "👤 ";
                string dimTag = p.dim == Dimension.Nether ? " [Незер]" : (p.dim == Dimension.End ? " [Энд]" : (p.dim == Dimension.Void ? " [Бездна]" : ""));
                Color nameCol = p.dim == Dimension.Nether ? new Color(255, 140, 100, 255) : (p.dim == Dimension.End ? new Color(200, 130, 255, 255) : Color.White);
                Fonts.Draw(prefix + p.name + dimTag, rowRect.X + 8f, curRowY + 4f, 14f, nameCol);

                float hpPct = Math.Clamp(p.hp / 20f, 0f, 1f);
                int hpBarW = 70;
                int hpBarH = 8;
                int hpBarX = (int)(rowRect.X + rowRect.Width - hpBarW - 10);
                int hpBarY = (int)(curRowY + (rowH - 4 - hpBarH) / 2f);

                Raylib.DrawRectangleRounded(new Rectangle(hpBarX, hpBarY, hpBarW, hpBarH), 0.4f, 2, new Color(20, 20, 20, 200));
                if (hpPct > 0f) {
                    Color hpCol = hpPct > 0.5f ? new Color(60, 220, 70, 240) : (hpPct > 0.25f ? new Color(240, 200, 40, 240) : new Color(240, 60, 50, 240));
                    Raylib.DrawRectangleRounded(new Rectangle(hpBarX, hpBarY, hpBarW * hpPct, hpBarH), 0.4f, 2, hpCol);
                }

                curRowY += rowH;
            }
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
                var src = TextureAtlas.TilePixelRect(filledTile);
                src.Width = TextureAtlas.TilePx / 2f; // левая половина тайла (пол-сердечка)
                var destHalf = new Rectangle(x, y, size / 2f, size);
                unsafe {
                    Raylib.DrawTexturePro(TextureAtlas.Atlas, src, destHalf, new System.Numerics.Vector2(0, 0), 0f, Color.White);
                }
            }
        }
    }

    private static void DrawItemIconByTile(byte tile, Rectangle dest) {
        var src = TextureAtlas.TilePixelRect(tile);
        unsafe {
            Raylib.DrawTexturePro(TextureAtlas.Atlas, src, dest, new System.Numerics.Vector2(0, 0), 0f, Color.White);
        }
    }

    /// <summary>Отрисовка полоски прочности инструмента/оружия/брони в слоте интерфейса.</summary>
    public static void DrawItemDurability(VoxelFrame.Core.Inventory.ItemInstance? item, Rectangle slot) {
        if (item == null) return;
        int maxDur = GameData.GetMaxItemDurability(item.Definition.Id);
        if (maxDur > 0 && item.Durability < maxDur && item.Durability >= 0) {
            float frac = Math.Clamp((float)item.Durability / maxDur, 0f, 1f);
            float bw = slot.Width - 8f;
            float bx = slot.X + 4f;
            float by = slot.Y + slot.Height - 6f;
            Raylib.DrawRectangle((int)bx, (int)by, (int)bw, 3, new Color(20, 20, 25, 200));
            var barCol = frac > 0.5f ? new Color(90, 220, 120, 255) : frac > 0.25f ? new Color(240, 200, 70, 255) : new Color(235, 75, 60, 255);
            Raylib.DrawRectangle((int)bx, (int)by, (int)(bw * frac), 3, barCol);
        }
    }

    /// <summary>Иконка предмета (тайл атласа) в прямоугольнике слота.</summary>
    public static void DrawItemIcon(VoxelFrame.Core.Inventory.ItemDefinition def, Rectangle slot, float scale) {
        byte tile = TextureAtlas.ItemTile(def.Id);
        var src = TextureAtlas.TilePixelRect(tile);
        float size = MathF.Min(slot.Width, slot.Height) * scale;
        var dest = new Rectangle(
            slot.X + (slot.Width - size) / 2f,
            slot.Y + (slot.Height - size) / 2f,
            size, size);
        unsafe {
            Raylib.DrawTexturePro(TextureAtlas.Atlas, src, dest, new System.Numerics.Vector2(0, 0), 0f, Color.White);
        }
    }

    /// <summary>Отрисовка 3D блока или объемного экструдированного 3D предмета в руке.
    /// hStretch > 1 визуально растягивает предмет по горизонтали (натяжение лука).</summary>
    private static void DrawHandHeldItem(VoxelFrame.Core.Inventory.ItemDefinition def, Rectangle slot, float scale, float rotation, float hStretch = 1f) {
        if (GameData.TryGetBlockByItem(def.Id, out var block) && block != null &&
            block.Id != GameData.BTorch.Id && block.Id != GameData.BBed.Id && block.Id != GameData.BBedHead.Id &&
            block.Id != GameData.BFire.Id && block.Id != GameData.BRail.Id && block.Id != GameData.BPressurePlate.Id &&
            block.Id != GameData.BWeb.Id && block.Id != GameData.BWheatCrop.Id && block.Id != GameData.BTallGrass.Id &&
            block.Id != GameData.BSapling.Id && block.Id != GameData.BRedFlower.Id && block.Id != GameData.BYellowFlower.Id &&
            block.Id != GameData.BCarrotCrop.Id && block.Id != GameData.BPotatoCrop.Id &&
            block.Id != GameData.BChorusPlant.Id && block.Id != GameData.BChorusFlower.Id && block.Id != GameData.BEnderCrystal.Id &&
            !GameData.IsDoor(block.Id)) {
            // ── 3D Изометрический куб блока в руке ─────────────────────────
            float cx = slot.X + slot.Width * 0.45f;
            float cy = slot.Y + slot.Height * 0.45f;
            float sz = MathF.Min(slot.Width, slot.Height) * scale * 0.85f;

            var tiles = TextureAtlas.BlockTiles(block.Id);
            var topUv = TextureAtlas.TileUv(tiles.PosY);
            var leftUv = TextureAtlas.TileUv(tiles.PosX);
            var rightUv = TextureAtlas.TileUv(tiles.PosZ);

            Rlgl.SetTexture(TextureAtlas.Atlas.Id);
            Rlgl.PushMatrix();
            Rlgl.Translatef(cx, cy, 0f);
            Rlgl.Rotatef(rotation * (180f / MathF.PI), 0f, 0f, 1f);

            Rlgl.Begin((int)DrawMode.Quads);

            // 1. Верхняя грань (+Y) - светлая
            Rlgl.Color4ub(255, 255, 255, 255);
            Rlgl.TexCoord2f(topUv.X, topUv.Y);                               Rlgl.Vertex2f(0f, -sz * 0.50f);
            Rlgl.TexCoord2f(topUv.X, topUv.Y + topUv.Height);                Rlgl.Vertex2f(-sz * 0.44f, -sz * 0.25f);
            Rlgl.TexCoord2f(topUv.X + topUv.Width, topUv.Y + topUv.Height); Rlgl.Vertex2f(0f, 0f);
            Rlgl.TexCoord2f(topUv.X + topUv.Width, topUv.Y);                Rlgl.Vertex2f(sz * 0.44f, -sz * 0.25f);

            // 2. Левая грань (+X) - умеренно затененная (82%)
            Rlgl.Color4ub(210, 210, 210, 255);
            Rlgl.TexCoord2f(leftUv.X, leftUv.Y);                               Rlgl.Vertex2f(-sz * 0.44f, -sz * 0.25f);
            Rlgl.TexCoord2f(leftUv.X, leftUv.Y + leftUv.Height);                Rlgl.Vertex2f(-sz * 0.44f, sz * 0.25f);
            Rlgl.TexCoord2f(leftUv.X + leftUv.Width, leftUv.Y + leftUv.Height); Rlgl.Vertex2f(0f, sz * 0.50f);
            Rlgl.TexCoord2f(leftUv.X + leftUv.Width, leftUv.Y);                Rlgl.Vertex2f(0f, 0f);

            // 3. Правая грань (+Z) - темная грань (68%)
            Rlgl.Color4ub(175, 175, 175, 255);
            Rlgl.TexCoord2f(rightUv.X, rightUv.Y);                               Rlgl.Vertex2f(0f, 0f);
            Rlgl.TexCoord2f(rightUv.X, rightUv.Y + rightUv.Height);                Rlgl.Vertex2f(0f, sz * 0.50f);
            Rlgl.TexCoord2f(rightUv.X + rightUv.Width, rightUv.Y + rightUv.Height); Rlgl.Vertex2f(sz * 0.44f, sz * 0.25f);
            Rlgl.TexCoord2f(rightUv.X + rightUv.Width, rightUv.Y);                Rlgl.Vertex2f(sz * 0.44f, -sz * 0.25f);

            Rlgl.End();
            Rlgl.PopMatrix();
            Rlgl.SetTexture(0);
        } else {
            // ── 2D Предмет в руке ──────────────────────────────────────────
            byte tile = TextureAtlas.ItemTile(def.Id);
            var src = TextureAtlas.TilePixelRect(tile);
            float size = MathF.Min(slot.Width, slot.Height) * scale * 1.05f;
            float drawW = size * hStretch;
            float cx = slot.X + slot.Width * 0.5f;
            float cy = slot.Y + slot.Height * 0.5f;
            var origin = new System.Numerics.Vector2(drawW * 0.5f, size * 0.5f);
            float rotDeg = rotation * (180f / MathF.PI);

            // Мягкая динамическая тень
            var shadowDest = new Rectangle(cx + 3f, cy + 3f, drawW, size);
            unsafe {
                Raylib.DrawTexturePro(TextureAtlas.Atlas, src, shadowDest, origin, rotDeg, new Color((byte)20, (byte)20, (byte)20, (byte)100));
            }

            // Основной лицевой слой предмета
            var frontDest = new Rectangle(cx, cy, drawW, size);
            unsafe {
                Raylib.DrawTexturePro(TextureAtlas.Atlas, src, frontDest, origin, rotDeg, Color.White);
            }
        }
    }

    /// <summary>Расширенный экран отладки F3. Никогда не роняет игру: любые сбои глушатся.</summary>
    private static void DrawF3DebugOverlay(GameSession session, Player player, int px, int py, int pz, string biomeName, string facing, int w, int h) {
        try {
            DrawF3DebugOverlayCore(session, player, px, py, pz, biomeName, facing, w, h);
        } catch {
            // Отладочный слой не должен ронять игру
        }
    }

    private static void DrawF3DebugOverlayCore(GameSession session, Player player, int px, int py, int pz, string biomeName, string facing, int w, int h) {
        // Левая панель F3
        float y = 10f;
        void LineL(string text, Color? col = null) {
            int tw = (int)(text.Length * 9.2f);
            Raylib.DrawRectangle(8, (int)y - 1, tw + 8, 19, new Color(0, 0, 0, 140));
            Fonts.Draw(text, 12f, y, 16f, col ?? Color.White);
            y += 20f;
        }

        LineL($"VoxelFrame 1.0.0-pre3-fix ({Raylib.GetFPS()} fps, {Raylib.GetFrameTime() * 1000f:F1} ms)");
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
            var blockName = GameData.TryGetBlock(vox.TypeId, out var blk) ? blk.Name : "воздух";
            LineL($"Targeted Block: {tb.X}, {tb.Y}, {tb.Z} ({blockName})", new Color(255, 200, 100, 255));
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
