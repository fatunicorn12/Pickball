using UnityEngine;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// What every inventory entry has in common: a stable id, a name and blurb
    /// for the menu, an icon, and the tab it lives on. Subclasses add what their
    /// own system needs (a weapon's moveset, a key item's ability).
    ///
    /// The id is stored rather than taken from the asset name, so renaming an
    /// asset never breaks a save or a pickup that refers to it.
    /// </summary>
    public abstract class InventoryItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable id used by saves and pickups. Lowercase, no spaces " +
                 "(\"ruler\", \"double_jump\"). Never change it once shipped.")]
        [SerializeField] private string id = "";

        [Tooltip("Name shown in the inventory menu.")]
        [SerializeField] private string displayName = "";

        [Tooltip("One or two lines shown under the name in the inventory menu.")]
        [TextArea(2, 4)]
        [SerializeField] private string description = "";

        [Tooltip("Icon shown in the item's slot. Empty draws the name's initial.")]
        [SerializeField] private Sprite icon;

        /// <summary>Stable id for saves and pickups.</summary>
        public string Id => id;

        /// <summary>Name shown in the menu.</summary>
        public string DisplayName => displayName;

        /// <summary>Blurb shown in the menu.</summary>
        public string Description => description;

        /// <summary>Slot icon, or null.</summary>
        public Sprite Icon => icon;

        /// <summary>The tab this item lives on.</summary>
        public abstract InventoryCategory Category { get; }

        /// <summary>Fills in the identity fields. For setup scripts and tests.</summary>
        public void SetIdentity(string newId, string newDisplayName, string newDescription)
        {
            id = newId;
            displayName = newDisplayName;
            description = newDescription;
        }
    }
}
