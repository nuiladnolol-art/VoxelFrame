using VoxelFrame.Core.Materials;

namespace VoxelFrame.Core.Inventory;

/// <summary>
/// Volumetric inventory. Limits are physical: max volume (m³) and max mass
/// (kg) the container can hold. There is no stack-size constant — a pile of
/// N identical items fits iff N × volume ≤ free volume AND N × mass ≤ free
/// mass. Quantities are bounded by physics, never by a magic 64.
///
/// Also implements IInventorySource/IInventorySink: the conservation ledger's
/// door into crafting (see MaterialVolumeManager.TryCraft).
/// </summary>
public sealed class Container : IInventorySource, IInventorySink {
    public double VolumeCapacityM3 { get; }
    public double MassCapacityKg { get; }

    private readonly ItemEntry?[] _slots = new ItemEntry?[36];
    private readonly Dictionary<ushort, double> _bulk = new();   // materialId → solid-equivalent m³

    /// Storage volume used (packing factors applied). Not the same as the
    /// conservation ledger's solid-equivalent volume — packing is storage.
    public double UsedVolumeM3 { get; private set; }
    public double UsedMassKg { get; private set; }
    public double FreeVolumeM3 => VolumeCapacityM3 - UsedVolumeM3;
    public double FreeMassKg => MassCapacityKg - UsedMassKg;
    public IReadOnlyList<ItemEntry> Entries => _slots.Where(e => e != null).Select(e => e!.Value).ToList();
    public ItemEntry?[] Slots => _slots;
    public int Capacity => _slots.Length;

    public void Clear() {
        for (int i = 0; i < _slots.Length; i++) _slots[i] = null;
        UsedVolumeM3 = 0;
        UsedMassKg = 0;
        _bulk.Clear();
    }

    public Container(double volumeCapacityM3, double massCapacityKg) {
        if (volumeCapacityM3 <= 0 || massCapacityKg <= 0) throw new ArgumentOutOfRangeException();
        VolumeCapacityM3 = volumeCapacityM3;
        MassCapacityKg = massCapacityKg;
    }

    /// The ONLY rule: real volume and real mass. No slot counts.
    public bool CanFit(double storageVolumeM3, double massKg) =>
        UsedVolumeM3 + storageVolumeM3 <= VolumeCapacityM3 + 1e-9 &&
        UsedMassKg + massKg <= MassCapacityKg + 1e-9;

    // ── Items ────────────────────────────────────────────────────────────────

    public bool TryInsert(ItemInstance item, int quantity = 1) {
        if (quantity <= 0) return false;
        
        const int maxStack = 64;
        if (!CanFit(item.VolumeM3 * quantity, item.MassKg * quantity)) return false;
        
        int remaining = quantity;
        // 1. Try to merge into an existing slot containing the same item
        for (int i = 0; i < _slots.Length; i++) {
            var e = _slots[i];
            if (e != null && e.Value.Item.Definition == item.Definition) {
                int currentQty = e.Value.Quantity;
                if (currentQty < maxStack) {
                    int add = Math.Min(maxStack - currentQty, remaining);
                    _slots[i] = e.Value with { Quantity = currentQty + add };
                    UsedVolumeM3 += item.VolumeM3 * add;
                    UsedMassKg += item.MassKg * add;
                    remaining -= add;
                    if (remaining <= 0) return true;
                }
            }
        }
        // 2. Try to find the first empty slot and insert there
        for (int i = 0; i < _slots.Length; i++) {
            if (_slots[i] == null) {
                int add = Math.Min(maxStack, remaining);
                _slots[i] = new ItemEntry(item, add);
                UsedVolumeM3 += item.VolumeM3 * add;
                UsedMassKg += item.MassKg * add;
                remaining -= add;
                if (remaining <= 0) return true;
            }
        }
        return false;
    }

    public bool TryRemove(ItemDefinition definition, int quantity = 1) {
        for (int i = _slots.Length - 1; i >= 0; i--) {
            var e = _slots[i];
            if (e == null || e.Value.Item.Definition != definition || e.Value.Quantity < quantity) continue;
            if (e.Value.Quantity == quantity) _slots[i] = null;
            else _slots[i] = e.Value with { Quantity = e.Value.Quantity - quantity };
            UsedVolumeM3 -= e.Value.Item.VolumeM3 * quantity;
            UsedMassKg -= e.Value.Item.MassKg * quantity;
            return true;
        }
        return false;
    }

    // ── Упорядочивание (UI: хотбар ⇄ хранилище) ──────────────────────────────

    public void RemoveAt(int index) {
        if (index < 0 || index >= _slots.Length) return;
        var e = _slots[index];
        if (e != null) {
            UsedVolumeM3 -= e.Value.Item.VolumeM3 * e.Value.Quantity;
            UsedMassKg -= e.Value.Item.MassKg * e.Value.Quantity;
            _slots[index] = null;
        }
    }

    public void InsertAt(int index, ItemEntry entry) {
        if (index < 0 || index >= _slots.Length) return;
        var existing = _slots[index];
        if (existing != null) {
            UsedVolumeM3 -= existing.Value.Item.VolumeM3 * existing.Value.Quantity;
            UsedMassKg -= existing.Value.Item.MassKg * existing.Value.Quantity;
        }
        _slots[index] = entry;
        UsedVolumeM3 += entry.Item.VolumeM3 * entry.Quantity;
        UsedMassKg += entry.Item.MassKg * entry.Quantity;
    }

    public int CountOf(ItemDefinition def) =>
        _slots.Where(e => e != null && e.Value.Item.Definition == def).Sum(e => e!.Value.Quantity);

    private void RemoveItems(ItemDefinition def, int quantity) {
        int remaining = quantity;
        for (int i = _slots.Length - 1; i >= 0 && remaining > 0; i--) {
            var e = _slots[i];
            if (e == null || e.Value.Item.Definition != def) continue;
            int take = Math.Min(e.Value.Quantity, remaining);
            if (take == e.Value.Quantity) _slots[i] = null;
            else _slots[i] = e.Value with { Quantity = e.Value.Quantity - take };
            remaining -= take;
            UsedVolumeM3 -= e.Value.Item.VolumeM3 * take;
            UsedMassKg -= e.Value.Item.MassKg * take;
        }
    }

    // ── Bulk (volume-continuous materials: scrap, sand, fluids) ─────────────

    public double BulkVolumeM3(ushort materialId) => _bulk.TryGetValue(materialId, out double v) ? v : 0;

    public bool TryAddBulk(Material material, double volumeM3) {
        if (volumeM3 <= 0) return false;
        double storage = volumeM3 * material.PackingFactor;
        double mass = material.MassOf(volumeM3);
        if (!CanFit(storage, mass)) return false;
        _bulk.TryGetValue(material.Id, out double current);
        _bulk[material.Id] = current + volumeM3;
        UsedVolumeM3 += storage;
        UsedMassKg += mass;
        return true;
    }

    public bool TryTakeBulk(Material material, double volumeM3) {
        if (!_bulk.TryGetValue(material.Id, out double current) || current < volumeM3 - 1e-9) return false;
        _bulk[material.Id] = current - volumeM3;
        UsedVolumeM3 -= volumeM3 * material.PackingFactor;
        UsedMassKg -= material.MassOf(volumeM3);
        return true;
    }

    // ── IInventorySource / IInventorySink (crafting's atomic door) ───────────

    bool IInventorySource.TryTakeAll(IReadOnlyList<RecipePart> parts) {
        foreach (var p in parts) {
            switch (p) {
                case ItemPart ip when CountOf(ip.Item) < ip.Quantity: return false;
                case BulkPart bp when BulkVolumeM3(bp.Material.Id) < bp.VolumeM3 - 1e-9: return false;
            }
        }
        foreach (var p in parts) {
            switch (p) {
                case ItemPart ip: RemoveItems(ip.Item, ip.Quantity); break;
                case BulkPart bp: TryTakeBulk(bp.Material, bp.VolumeM3); break;
            }
        }
        return true;
    }

    void IInventorySource.ReturnAll(IReadOnlyList<RecipePart> parts) {
        foreach (var p in parts) {
            switch (p) {
                case ItemPart ip: TryInsert(new ItemInstance(ip.Item, 0), ip.Quantity); break;
                case BulkPart bp: TryAddBulk(bp.Material, bp.VolumeM3); break;
            }
        }
    }

    bool IInventorySink.TryAddAll(IReadOnlyList<RecipePart> parts) {
        // Two-phase: verify the whole recipe fits, then commit — atomicity for
        // crafting (a partial add would double matter on the refund path).
        double v = 0, m = 0;
        foreach (var p in parts) { v += p.StorageVolumeM3; m += p.MassKg; }
        if (!CanFit(v, m)) return false;
        foreach (var p in parts) {
            switch (p) {
                case ItemPart ip: TryInsert(new ItemInstance(ip.Item, 0), ip.Quantity); break;
                case BulkPart bp: TryAddBulk(bp.Material, bp.VolumeM3); break;
            }
        }
        return true;
    }
}
