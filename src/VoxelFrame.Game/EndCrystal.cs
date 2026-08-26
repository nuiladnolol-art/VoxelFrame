using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>
/// Эндер-кристалл как сущность: парит над точкой, покачивается и взрывается при
/// касании игрока или попадании любого снаряда — как в Minecraft.
///
/// Якорь — невидимый блок BEnderCrystal в сетке: он сохраняется/загружается как
/// обычный чанк, даёт свет и управляет лечением Слизня Края (CountAliveEndCrystals
/// смотрит на якорные блоки). Когда кристалл взрывается — якорь удаляется.
/// </summary>
public sealed class EndCrystalEntity {
    /// <summary>Клетка невидимого якорного блока (в ней же хранится позиция в сейве).</summary>
    public Vec3i Anchor;
    public Vector3 Position;
    public bool Alive = true;
    public float BobPhase;

    /// <summary>Радиус касания игрока и снарядов, при котором происходит взрыв.</summary>
    public const float ExplodeRadius = 1.2f;

    public EndCrystalEntity(Vec3i anchor) {
        Anchor = anchor;
        Position = new Vector3(anchor.X + 0.5f, anchor.Y + 1.35f, anchor.Z + 0.5f);
        BobPhase = Random.Shared.NextSingle() * MathF.Tau;
    }

    public void Tick(float dt, GameWorld world, Player player, GameSession session) {
        if (!Alive) return;

        // Якорный блок исчез (чужой взрыв, удаление) — сущность больше не существует.
        if (world.GetVoxel(Anchor).TypeId != GameData.BEnderCrystal.Id) {
            Alive = false;
            return;
        }

        // Удар игрока (меч/рука) рядом → взрыв. Простое хождение НЕ взрывает кристалл!
        if (player.SwingMarker > 0f) {
            float mdx = Position.X - player.Position.X;
            float mdy = Position.Y - (player.Position.Y + 1.0f);
            float mdz = Position.Z - player.Position.Z;
            if (mdx * mdx + mdy * mdy + mdz * mdz < 2.6f * 2.6f) {
                Explode(world, session);
                return;
            }
        }

        // Попадание любого снаряда (стрела, жемчуг, око, огненный шар ифрита) → взрыв.
        foreach (var arr in world.Arrows) {
            if (!arr.Alive) continue;
            float adx = arr.Position.X - Position.X;
            float ady = arr.Position.Y - Position.Y;
            float adz = arr.Position.Z - Position.Z;
            if (adx * adx + ady * ady + adz * adz < 0.95f) {
                arr.Alive = false;
                Explode(world, session);
                return;
            }
        }
    }

    private void Explode(GameWorld world, GameSession session) {
        if (!Alive) return;
        Alive = false;
        world.RemoveBlock(Anchor);
        GameWorld.CreateExplosion(Position, 3.5f, 26f, session);
        SoundSystem.PlayExplosion();
    }
}
