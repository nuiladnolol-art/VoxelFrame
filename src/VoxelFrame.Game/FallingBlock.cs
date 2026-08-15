using System.Numerics;
using VoxelFrame.Core;
using VoxelFrame.Core.World;

namespace VoxelFrame.Game;

/// <summary>Падающий обломок: обрушение конструкции → падение → укладка или предмет.</summary>
public sealed class FallingBlock {
    public BlockType Block;
    public Vector3 Position;      // центр
    public Vector3 Velocity;
    public bool Alive = true;
    public float Age;
    public const float HalfSize = 0.5f;
    /// Всё, что ниже мира (загруженная зона кончается на y ≈ -32), не приземлится.
    public const float VoidY = -40f;
    public const float MaxFallSeconds = 15f;

    public FallingBlock(BlockType block, Vector3 position) {
        Block = block;
        Position = position;
        Velocity = new Vector3(0f, -2f, 0f);
    }

    public void Tick(float dt, GameWorld world) {
        if (!Alive) return;
        Age += dt;
        Velocity.Y -= 24f * dt;
        Position += Velocity * dt;

        if (Position.Y < VoidY || Age > MaxFallSeconds) {
            // Провалился в бездну / застрял — просто убираем.
            Alive = false;
            return;
        }

        // Ячейка под центром: если твёрдая — приземляемся.
        var below = new Vec3i(
            (int)MathF.Floor(Position.X),
            (int)MathF.Floor(Position.Y - HalfSize - 0.02f),
            (int)MathF.Floor(Position.Z));
        if (world.IsSolidAt(below)) {
            var land = new Vec3i(
                (int)MathF.Floor(Position.X),
                (int)MathF.Floor(Position.Y - HalfSize),
                (int)MathF.Floor(Position.Z));
            if (!world.IsSolidAt(land)) {
                world.PlacePlacedBlock(land, Block, 1f);
            } else {
                ushort dropId = Block.DropItemId != 0 ? Block.DropItemId : (Block.Id == GameData.BSand.Id ? GameData.SandItem.Id : GameData.GravelItem.Id);
                world.SpawnPickup(dropId, 1, land);
            }
            Alive = false;
        }
    }
}
