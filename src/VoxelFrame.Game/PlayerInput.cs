using System.Numerics;

namespace VoxelFrame.Game;

/// <summary>Абстрактный ввод игрока (реальный ввод или смоук-тест).</summary>
public struct PlayerInput {
    public float MoveX;          // +1 = вправо (D)
    public float MoveZ;          // +1 = вперёд (W)
    public bool Jump;
    public bool Sprint;
    public bool AttackHeld;      // ЛКМ зажата (ломание/атака)
    public bool UsePressed;      // ПКМ нажата в этом кадре (установка/еда)
    public bool UseHeld;         // ПКМ зажата (непрерывная установка как в MC)
    public float MouseDX, MouseDY;
    public int Scroll;
    public bool OpenInventory;   // E
    public bool OpenCrafting;    // C
    public bool Pause;           // ESC
    public int HotbarSlot;       // клавиши 1-9: выбор слота хотбара; -1 = нет

    public bool Crouch;
    public bool Drop;

    public static PlayerInput Idle => default;
}
