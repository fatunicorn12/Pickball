using System;
using UnityEngine;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// How many slots each tab has. A value type so the model can be built
    /// from a config asset in play and from plain numbers in a test.
    /// </summary>
    [Serializable]
    public struct InventoryCapacity
    {
        [Tooltip("Weapon slots (count). The menu draws this many in the Weapons tab.")]
        [Min(1)] public int Weapons;

        [Tooltip("Magic slots (count). Spells beyond this are not listed.")]
        [Min(1)] public int Magic;

        [Tooltip("Key item slots (count).")]
        [Min(1)] public int KeyItems;

        /// <summary>Builds a capacity from three counts.</summary>
        public InventoryCapacity(int weapons, int magic, int keyItems)
        {
            Weapons = weapons;
            Magic = magic;
            KeyItems = keyItems;
        }

        /// <summary>The slots on <paramref name="category"/>'s tab, never below 0.</summary>
        public int For(InventoryCategory category) => Mathf.Max(0, category switch
        {
            InventoryCategory.Weapon => Weapons,
            InventoryCategory.Magic => Magic,
            InventoryCategory.KeyItem => KeyItems,
            _ => 0,
        });

        /// <summary>The shipped default: 8 weapons, 10 spells, 10 key items.</summary>
        public static InventoryCapacity Default => new(8, 10, 10);
    }
}
