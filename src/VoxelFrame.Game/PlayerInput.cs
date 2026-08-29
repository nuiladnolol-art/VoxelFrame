using System.Numerics;

namespace VoxelFrame.Game;

/// <summary>Абстрактный ввод игрока (реальный ввод или смоук-тест).</summary>
public struct PlayerInput {
    public float MoveX;          // +1 = вправо (D)
    public float MoveZ;          // +1 = вперёд (W)
    public bool Jump;
    public bool JumpPressed;
    public bool AttackHeld;      // ЛКМ зажата (ломание блоков)
    public bool AttackPressed;   // ЛКМ нажата в этом кадре (удары по мобам / клик)
    public bool UsePressed;      // ПКМ нажата в этом кадре (установка/еда)
    public bool UseHeld;         // ПКМ зажата (непрерывное действие/установка)
    public float MouseDX, MouseDY;
    public int Scroll;
    public bool OpenInventory;   // E
    public bool Pause;           // ESC
    public int HotbarSlot;       // клавиши 1-9: выбор слота хотбара; -1 = нет

    public bool Crouch;
    public bool Sprint;
    public bool Drop;

    public static PlayerInput Idle => default;
}
