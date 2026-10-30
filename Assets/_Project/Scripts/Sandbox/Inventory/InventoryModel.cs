using System;
using System.Collections.Generic;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// What the player owns, as plain state: one ordered list of slots per
    /// tab (<see cref="InventoryCategory"/>), a capacity per tab, and which
    /// weapon is equipped. No Unity lifecycle, so every rule here is tested in
    /// EditMode; the <see cref="Inventory"/> component wraps it on the player.
    ///
    /// <b>Rules.</b>
    /// <list type="bullet">
    /// <item>Items keep the order they were added in; removing one closes the gap.</item>
    /// <item>An item is owned once. Two assets with the same non-empty id count as
    /// the same item, so a duplicated pickup asset never fills a second slot.</item>
    /// <item>A full tab refuses the item (<see cref="InventoryAddResult.Full"/>);
    /// nothing is dropped to make room.</item>
    /// <item>The equipped weapon is always an owned weapon or null. Adding a weapon
    /// while none is equipped equips it. Removing the equipped weapon equips the
    /// one that slides into its slot (or the new last one), else none.</item>
    /// </list>
    ///
    /// <b>Equipping here is the request, not the swap.</b> The model changes
    /// at once and raises <see cref="EquippedChanged"/>; the
    /// <see cref="WeaponEquipper"/> applies it to the hand and the combo when
    /// the current swing allows (see <see cref="PendingEquip"/>).
    /// </summary>
    public sealed class InventoryModel
    {
        static readonly InventoryCategory[] AllCategories =
            { InventoryCategory.Weapon, InventoryCategory.Magic, InventoryCategory.KeyItem };

        readonly List<InventoryItemDefinition>[] _slots;
        InventoryCapacity _capacity;

        /// <summary>Raised after an item is added to or removed from <c>category</c>'s tab.</summary>
        public event Action<InventoryCategory> Changed;

        /// <summary>Raised after the equipped weapon changes; carries the new one (null for none).</summary>
        public event Action<WeaponDefinition> EquippedChanged;

        /// <summary>An empty inventory with <paramref name="capacity"/> slots per tab.</summary>
        public InventoryModel(InventoryCapacity capacity)
        {
            _capacity = capacity;
            _slots = new List<InventoryItemDefinition>[AllCategories.Length];
            for (int i = 0; i < _slots.Length; i++) _slots[i] = new List<InventoryItemDefinition>(8);
        }

        /// <summary>Every tab, in menu order.</summary>
        public static IReadOnlyList<InventoryCategory> Categories => AllCategories;

        /// <summary>The weapon in hand (or about to be), or null.</summary>
        public WeaponDefinition Equipped { get; private set; }

        /// <summary>Slots on <paramref name="category"/>'s tab.</summary>
        public int CapacityOf(InventoryCategory category) => _capacity.For(category);

        /// <summary>Items owned on <paramref name="category"/>'s tab.</summary>
        public int Count(InventoryCategory category) => List(category).Count;

        /// <summary>The items on <paramref name="category"/>'s tab, in slot order. Read-only view; do not cache across changes.</summary>
        public IReadOnlyList<InventoryItemDefinition> Items(InventoryCategory category) => List(category);

        /// <summary>The item in slot <paramref name="index"/> of <paramref name="category"/>'s tab, or null for an empty or invalid slot.</summary>
        public InventoryItemDefinition ItemAt(InventoryCategory category, int index)
        {
            var list = List(category);
            return index >= 0 && index < list.Count ? list[index] : null;
        }

        /// <summary>The slot <paramref name="item"/> sits in on its tab, or -1 when not owned.</summary>
        public int IndexOf(InventoryItemDefinition item)
        {
            if (item == null) return -1;
            var list = List(item.Category);
            for (int i = 0; i < list.Count; i++)
                if (Same(list[i], item)) return i;
            return -1;
        }

        /// <summary>True when <paramref name="item"/> (or another asset with its id) is owned.</summary>
        public bool Has(InventoryItemDefinition item) => IndexOf(item) >= 0;

        /// <summary>The owned item with id <paramref name="id"/> on any tab, or null.</summary>
        public InventoryItemDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var list in _slots)
                foreach (var item in list)
                    if (item != null && item.Id == id) return item;
            return null;
        }

        /// <summary>
        /// Puts <paramref name="item"/> in the next free slot of its tab. A weapon
        /// added while nothing is equipped is equipped too.
        /// </summary>
        public InventoryAddResult Add(InventoryItemDefinition item)
        {
            if (item == null) return InventoryAddResult.Invalid;
            if (Has(item)) return InventoryAddResult.AlreadyOwned;
            var list = List(item.Category);
            if (list.Count >= CapacityOf(item.Category)) return InventoryAddResult.Full;

            list.Add(item);
            Changed?.Invoke(item.Category);
            if (Equipped == null && item is WeaponDefinition weapon) SetEquipped(weapon);
            return InventoryAddResult.Added;
        }

        /// <summary>
        /// Takes <paramref name="item"/> out; later slots move up one. Removing
        /// the equipped weapon equips the weapon now in its slot, else the last
        /// one, else none. False when it was not owned.
        /// </summary>
        public bool Remove(InventoryItemDefinition item)
        {
            int index = IndexOf(item);
            if (index < 0) return false;
            var list = List(item.Category);
            var owned = list[index];
            list.RemoveAt(index);
            Changed?.Invoke(owned.Category);

            if (owned is WeaponDefinition && Same(owned, Equipped))
            {
                var next = list.Count == 0 ? null : list[Math.Min(index, list.Count - 1)] as WeaponDefinition;
                SetEquipped(next);
            }
            return true;
        }

        /// <summary>Removes everything on <paramref name="category"/>'s tab (equipping none if that was the weapons).</summary>
        public void Clear(InventoryCategory category)
        {
            var list = List(category);
            if (list.Count == 0) return;
            list.Clear();
            Changed?.Invoke(category);
            if (category == InventoryCategory.Weapon) SetEquipped(null);
        }

        /// <summary>
        /// Equips <paramref name="weapon"/>. False, changing nothing, when it is
        /// not owned or already equipped. The owned asset is the one stored, so
        /// equipping a same-id duplicate equips the original.
        /// </summary>
        public bool Equip(WeaponDefinition weapon)
        {
            int index = IndexOf(weapon);
            if (index < 0) return false;
            var owned = (WeaponDefinition)List(InventoryCategory.Weapon)[index];
            if (owned == Equipped) return false;
            SetEquipped(owned);
            return true;
        }

        /// <summary>
        /// Equips the next owned weapon in <paramref name="direction"/>'s
        /// direction (+1 next, -1 previous), wrapping. Returns the weapon now
        /// equipped, or null when that did not change (no weapons, or only one).
        /// </summary>
        public WeaponDefinition CycleWeapon(int direction)
        {
            var list = List(InventoryCategory.Weapon);
            int next = CycleIndex(list.Count, IndexOf(Equipped), direction);
            if (next < 0) return null;
            var weapon = (WeaponDefinition)list[next];
            return Equip(weapon) ? weapon : null;
        }

        /// <summary>
        /// The slot one step from <paramref name="current"/> in
        /// <paramref name="direction"/>'s direction among <paramref name="count"/>
        /// slots, wrapping. With no current slot (-1 or out of range), forward
        /// starts at the first and backward at the last. Direction 0 keeps a valid
        /// current. -1 only when there are no slots.
        /// </summary>
        public static int CycleIndex(int count, int current, int direction)
        {
            if (count <= 0) return -1;
            bool valid = current >= 0 && current < count;
            if (direction == 0) return valid ? current : 0;
            int step = direction > 0 ? 1 : -1;
            if (!valid) return step > 0 ? 0 : count - 1;
            return ((current + step) % count + count) % count;
        }

        void SetEquipped(WeaponDefinition weapon)
        {
            if (weapon == Equipped) return;
            Equipped = weapon;
            EquippedChanged?.Invoke(weapon);
        }

        List<InventoryItemDefinition> List(InventoryCategory category)
        {
            int i = (int)category;
            return i >= 0 && i < _slots.Length ? _slots[i] : _slots[0];
        }

        static bool Same(InventoryItemDefinition a, InventoryItemDefinition b)
        {
            if (a == null || b == null) return false;
            if (a == b) return true;
            return !string.IsNullOrEmpty(a.Id) && a.Id == b.Id && a.Category == b.Category;
        }
    }
}
