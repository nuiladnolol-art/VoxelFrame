using System.Runtime.InteropServices;

namespace VoxelFrame.Core.World;

[Flags]
public enum VoxelFlags : byte {
    None  = 0,
    Solid = 1 << 0,   // заполняет ячейку физически: блокирует движение
}

/// <summary>
/// Один куб мира — лицевая сторона хранилища вокселей.
/// Поля «научной» физики (масса, объём, несущая способность) удалены.
/// Оставшийся байт SubGridLayerMask используется как универсальное состояние
/// ячейки: двери (биты 0..1 — facing, бит 8 — открыта), жидкости (уровень),
/// пшеница (стадия роста) — см. GameWorld/ChunkMesher/FluidEngine.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct VoxelData : IEquatable<VoxelData> {
    /// Идентификатор типа блока. 0 = Air.
    public ushort TypeId;
    public VoxelFlags Flags;
    public byte SubGridLayerMask;

    public readonly bool IsAir => TypeId == 0;
    public readonly bool IsSolid => (Flags & VoxelFlags.Solid) != 0;

    public static VoxelData Air => default;

    public readonly bool Equals(VoxelData o) =>
        TypeId == o.TypeId && Flags == o.Flags && SubGridLayerMask == o.SubGridLayerMask;

    public override readonly bool Equals(object? o) => o is VoxelData v && Equals(v);
    public override readonly int GetHashCode() => HashCode.Combine(TypeId, Flags, SubGridLayerMask);
    public static bool operator ==(VoxelData a, VoxelData b) => a.Equals(b);
    public static bool operator !=(VoxelData a, VoxelData b) => !a.Equals(b);
    public override readonly string ToString() => $"#{TypeId} {(IsSolid ? "solid" : "")}";
}
