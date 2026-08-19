namespace VoxelFrame.Core.Materials;

/// <summary>
/// Global conservation audit. Every material flow is logged in cm³ (long —
/// exact, no float drift). If the ledger's totals drift between crafted mass
/// in and out, a recipe or inventory code path is violating the First Law.
/// </summary>
public sealed class VolumeLedger {
    private readonly Dictionary<ushort, long> _cm3 = new();

    public void Add(Material material, double volumeM3) {
        long cm3 = (long)Math.Round(volumeM3 * Units.Cm3PerM3);
        lock (_cm3) {
            _cm3.TryGetValue(material.Id, out long current);
            _cm3[material.Id] = current + cm3;
        }
    }

    public long Cm3Of(ushort materialId) {
        lock (_cm3) {
            return _cm3.TryGetValue(materialId, out long v) ? v : 0;
        }
    }
    public double TotalVolumeM3 => TotalCm3 / Units.Cm3PerM3;

    public long TotalCm3 {
        get {
            long total = 0;
            lock (_cm3) {
                foreach (var kv in _cm3) total += kv.Value;
            }
            return total;
        }
    }
}
