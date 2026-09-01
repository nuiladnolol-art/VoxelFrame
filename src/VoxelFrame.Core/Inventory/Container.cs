namespace VoxelFrame.Core.Inventory;

/// <summary>
/// Классический инвентарь: 36 слотов, стак до 64.
/// Единый путь проверки HasSpaceFor/TryInsert.
/// </summary>
public sealed class Container {
    private readonly ItemEntry?[] _slots = new ItemEntry?[36];

    public IReadOnlyList<ItemEntry> Entries {
        get {
            var list = new List<ItemEntry>();
            for (int i = 0; i < _slots.Length; i++) {
                if (_slots[i] is { } e) list.Add(e);
            }
            return list;
        }
    }
    public ItemEntry?[] Slots => _slots;
    public int Capacity => _slots.Length;

    public void Clear() {
        for (int i = 0; i < _slots.Length; i++) _slots[i] = null;
    }

    public Container() { }

    public bool HasSpaceFor(ItemDefinition def, int quantity = 1) {
        int remaining = quantity;
        int maxStack = def.MaxStack;
        for (int i = 0; i < _slots.Length; i++) {
            var e = _slots[i];
            if (e != null && e.Value.Item.Definition == def) {
                int space = maxStack - e.Value.Quantity;
                if (space > 0) remaining -= space;
            } else if (e == null) {
                remaining -= maxStack;
            }
            if (remaining <= 0) return true;
        }
        return false;
    }

    public bool TryInsert(ItemInstance item, int quantity = 1) {
        if (quantity <= 0) return false;
        if (!HasSpaceFor(item.Definition, quantity)) return false;
        
        int maxStack = item.Definition.MaxStack;
        int remaining = quantity;
        for (int i = 0; i < _slots.Length; i++) {
            var e = _slots[i];
            if (e != null && e.Value.Item.Definition == item.Definition) {
                int currentQty = e.Value.Quantity;
                if (currentQty < maxStack) {
                    int add = Math.Min(maxStack - currentQty, remaining);
                    _slots[i] = e.Value with { Quantity = currentQty + add };
                    remaining -= add;
                    if (remaining <= 0) return true;
                }
            }
        }
        for (int i = 0; i < _slots.Length; i++) {
            if (_slots[i] == null) {
                int add = Math.Min(maxStack, remaining);
                _slots[i] = new ItemEntry(item, add);
                remaining -= add;
                if (remaining <= 0) return true;
            }
        }
        return remaining <= 0;
    }

    public bool TryRemove(ItemDefinition definition, int quantity = 1) {
        if (quantity <= 0) return false;
        if (CountOf(definition) < quantity) return false;
        RemoveItems(definition, quantity);
        return true;
    }

    public void RemoveAt(int index) {
        if (index < 0 || index >= _slots.Length) return;
        _slots[index] = null;
    }

    public void InsertAt(int index, ItemEntry entry) {
        if (index < 0 || index >= _slots.Length) return;
        _slots[index] = entry;
    }

    public int CountOf(ItemDefinition def) {
        int sum = 0;
        for (int i = 0; i < _slots.Length; i++) {
            if (_slots[i] is { } e && e.Item.Definition == def) sum += e.Quantity;
        }
        return sum;
    }

    private void RemoveItems(ItemDefinition def, int quantity) {
        int remaining = quantity;
        for (int i = 0; i < _slots.Length && remaining > 0; i++) {
            var e = _slots[i];
            if (e == null || e.Value.Item.Definition != def) continue;
            int take = Math.Min(e.Value.Quantity, remaining);
            int left = e.Value.Quantity - take;
            _slots[i] = left > 0 ? e.Value with { Quantity = left } : null;
            remaining -= take;
        }
    }
}
