namespace VoxelFrame.Core.World;

public enum ComponentOrientation : byte { Vertical = 0, AxisX = 1, AxisZ = 2 }

/// <summary>
/// A physical sub-voxel element (beam, panel, board) inside a 1 m³ cell.
/// Volume and mass are EXACT — placing a component consumes precisely
/// VolumeM3 of material from the builder (conservation is enforced upstream
/// by MaterialVolumeManager / crafting).
/// </summary>
public readonly struct FrameworkComponent {
    public readonly ushort TypeId;            // catalog id (framework part type)
    public readonly ComponentOrientation Orientation;
    public readonly byte LengthCells;         // 1 for panels/boards; 1..8 for beams (8 = full cell)
    public readonly float VolumeM3;           // exact material volume, m³
    public readonly float MassKg;             // = density × VolumeM3 — never stored independently
    public readonly float LoadCapacityKN;     // this element's load capacity

    public FrameworkComponent(ushort typeId, ComponentOrientation orientation, byte lengthCells,
                              float volumeM3, float massKg, float loadCapacityKN) {
        TypeId = typeId;
        Orientation = orientation;
        LengthCells = lengthCells;
        VolumeM3 = volumeM3;
        MassKg = massKg;
        LoadCapacityKN = loadCapacityKN;
    }

    /// Cell occupied by segment i of this component when anchored at (x, y, z).
    public readonly (int X, int Y, int Z) CellAt(int x, int y, int z, int i) => Orientation switch {
        ComponentOrientation.Vertical => (x, y + i, z),
        ComponentOrientation.AxisX    => (x + i, y, z),
        _                             => (x, y, z + i),
    };
}

/// <summary>
/// 8×8×8 occupancy grid inside one voxel cell (sub-cell edge = 12.5 cm).
/// Sparse by design: allocated only for cells that hold framework components.
/// Occupancy bitmap: 512 bytes; component table: ≤ 255 entries per cell.
/// </summary>
public sealed class SubGrid {
    public const int Res = 8;
    public const float CellSizeM = 1f / Res;
    public const int CellCount = Res * Res * Res;
    public const int MaxComponents = byte.MaxValue;

    private readonly byte[] _cells = new byte[CellCount];                 // 0 = empty, n = component n (1-based)
    private readonly List<FrameworkComponent> _components = new();
    private readonly List<(byte X, byte Y, byte Z)> _anchors = new();

    public byte OccupiedLayerMask { get; private set; }
    public int ComponentCount => _components.Count;
    public IReadOnlyList<FrameworkComponent> Components => _components;
    public double TotalVolumeM3 { get; private set; }
    public double TotalMassKg { get; private set; }
    public double TotalCapacityKN { get; private set; }

    public static int Index(int x, int y, int z) => (y << 6) | (z << 3) | x;   // 8³ → 9 bits
    public bool IsCellFree(int x, int y, int z) => _cells[Index(x, y, z)] == 0;

    public bool TryPlace(in FrameworkComponent c, int x, int y, int z) {
        if (_components.Count >= MaxComponents) return false;
        for (int i = 0; i < c.LengthCells; i++) {
            var (cx, cy, cz) = c.CellAt(x, y, z, i);
            if (cx >= Res || cy >= Res || cz >= Res) return false;
            if (_cells[Index(cx, cy, cz)] != 0) return false;
        }
        int id = _components.Count + 1;
        for (int i = 0; i < c.LengthCells; i++) {
            var (cx, cy, cz) = c.CellAt(x, y, z, i);
            _cells[Index(cx, cy, cz)] = (byte)id;
        }
        _components.Add(c);
        _anchors.Add(((byte)x, (byte)y, (byte)z));
        TotalVolumeM3 += c.VolumeM3;
        TotalMassKg += c.MassKg;
        TotalCapacityKN += c.LoadCapacityKN;
        OccupiedLayerMask |= (byte)(1 << y);
        return true;
    }

    public bool TryRemove(int componentIndex) {
        if ((uint)componentIndex >= (uint)_components.Count) return false;
        var removed = _components[componentIndex];
        _components.RemoveAt(componentIndex);
        _anchors.RemoveAt(componentIndex);
        RebuildOccupancy();
        TotalVolumeM3 -= removed.VolumeM3;
        TotalMassKg -= removed.MassKg;
        TotalCapacityKN -= removed.LoadCapacityKN;
        return true;
    }

    private void RebuildOccupancy() {
        Array.Clear(_cells);
        OccupiedLayerMask = 0;
        for (int i = 0; i < _components.Count; i++) {
            var c = _components[i];
            var (x, y, z) = _anchors[i];
            for (int k = 0; k < c.LengthCells; k++) {
                var (cx, cy, cz) = c.CellAt(x, y, z, k);
                _cells[Index(cx, cy, cz)] = (byte)(i + 1);
            }
            OccupiedLayerMask |= (byte)(1 << y);
        }
    }
}
