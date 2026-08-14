namespace VoxelFrame.Core;

public readonly struct Vec3i : IEquatable<Vec3i> {
    public readonly int X, Y, Z;

    public Vec3i(int x, int y, int z) { X = x; Y = y; Z = z; }

    public static readonly Vec3i Down = new(0, -1, 0);
    public static readonly Vec3i Up = new(0, 1, 0);

    public static Vec3i operator +(Vec3i a, Vec3i b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3i operator -(Vec3i a, Vec3i b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public readonly void Deconstruct(out int x, out int y, out int z) { x = X; y = Y; z = Z; }

    public readonly bool Equals(Vec3i o) => X == o.X && Y == o.Y && Z == o.Z;
    public override readonly bool Equals(object? o) => o is Vec3i v && Equals(v);
    public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);
    public static bool operator ==(Vec3i a, Vec3i b) => a.Equals(b);
    public static bool operator !=(Vec3i a, Vec3i b) => !a.Equals(b);
    public override readonly string ToString() => $"({X}, {Y}, {Z})";
}
