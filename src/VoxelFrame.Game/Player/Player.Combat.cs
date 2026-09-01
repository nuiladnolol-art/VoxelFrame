using System;
using System.Numerics;
using Raylib_cs;
using VoxelFrame.Core;
using VoxelFrame.Core.Inventory;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

public sealed partial class Player {
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
        GameClient.Active?.SendAttackEntity(best.Id, dmg, false);
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
            // В мультиплеере только сервер/хост авторитетно вызывает Die() и спавнит дроп
            if (GameClient.Active == null) {
                best.Die(world, session);
            }
        }
        DamageSelectedTool(session);
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
        GameClient.Active?.SendAttackEntity(mob.Id, dmg, true);
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
                    if (dot > 0.25f) {
                        other.Health -= dmg * 0.5f;
                        other.HurtTime = 0.35f;
                        var sPush = Vector2.Normalize(new Vector2(toOther.X, toOther.Z));
                        other.Velocity += new Vector3(sPush.X * 4.0f, 1.5f, sPush.Y * 4.0f);
                        world.SpawnCrit(other.Position + new Vector3(0f, 0.6f, 0f), 5);
                        if (other.Health <= 0f) {
                            if (GameClient.Active == null) other.Die(world, session);
                        }
                    }
                }
            }
            world.SpawnCrit(Position + Forward * 1.5f + Vector3.UnitY * 0.6f, 8);
        }

        if (mob.Health <= 0f) {
            if (GameClient.Active == null) {
                mob.Die(world, session);
            }
        }
        DamageSelectedTool(session);
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
        DamageSelectedTool(session);
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

    /// <summary>Снимает 1 прочность с инструмента/оружия в выбранном слоте; ломает при нуле.</summary>
    public void DamageSelectedTool(GameSession session) {
        if (session.GameMode == GameMode.Creative) return;
        var entry = Inventory.Slots[SelectedSlot];
        if (entry == null) return;
        var def = entry.Value.Item.Definition;
        if (GameData.GetToolTier(def.Id) <= 0) return;
        int dur = entry.Value.Item.Durability - 1;
        if (dur <= 0) {
            Inventory.RemoveAt(SelectedSlot);
            session.AddMessage($"Инструмент «{def.Name}» сломался!");
            SoundSystem.PlayBreakTool();
        } else {
            entry.Value.Item.Durability = dur;
        }
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
}
