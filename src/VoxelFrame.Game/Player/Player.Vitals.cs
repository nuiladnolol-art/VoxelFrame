using System;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;

namespace VoxelFrame.Game;

public sealed partial class Player {
    // ── Жизненные показатели, голод и регенерация ────────────────────────────

    private void TickVitals(float dt, GameSession session) {
        if (Exhaustion >= 4.0f) {
            Exhaustion -= 4.0f;
            if (Saturation > 0f) Saturation = MathF.Max(0f, Saturation - 1f);
            else Hunger = MathF.Max(0f, Hunger - 1f);
        }

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
                FireBurnTimer = 1.0f;
                InvulnerabilityTimer = 0f;
                ApplyDamage(1.0f, session);
            }
        } else {
            FireBurnTimer = 0f;
        }
        if (Health <= 0f) session.DiePlayer();
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
