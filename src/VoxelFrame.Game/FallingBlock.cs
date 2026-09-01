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

        // Ищем твёрдое основание под падающим блоком
        int bx = (int)MathF.Floor(Position.X);
        int bz = (int)MathF.Floor(Position.Z);
        int by = (int)MathF.Floor(Position.Y - HalfSize);

        var below = new Vec3i(bx, by, bz);
        if (world.IsSolidAt(below)) {
            // Нашли препятствие — ищем ближайшую свободную ячейку выше и устанавливаем блок обратно в мир
            int placeY = by + 1;
            while (placeY < 256 && world.IsSolidAt(new Vec3i(bx, placeY, bz))) {
                placeY++;
            }
            var land = new Vec3i(bx, placeY, bz);
            world.PlacePlacedBlock(land, Block);
            Alive = false;
        }
    }
}
