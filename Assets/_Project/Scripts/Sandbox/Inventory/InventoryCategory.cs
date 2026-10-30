namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// The inventory's tabs. Each item belongs to exactly one, and each tab has
    /// its own slots, so a new kind of item is a new value here plus a tab in
    /// the menu, not a change to the existing ones.
    /// </summary>
    public enum InventoryCategory
    {
        /// <summary>Swords and other things held in the hand and swung.</summary>
        Weapon,

        /// <summary>Spells, cast through the magic system.</summary>
        Magic,

        /// <summary>Movement unlocks and quest items (dash, double jump, pendulum…).</summary>
        KeyItem,
    }
}
