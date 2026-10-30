using UnityEngine;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// A key item: the inventory's face for one <see cref="AbilityKind"/>
    /// (the Pendulum is <see cref="AbilityKind.Tether"/>). It grants nothing by
    /// itself. <see cref="PlayerAbilities"/> stays the one source of truth for
    /// what the player can do, and the inventory only shows a key item while
    /// the matching ability is unlocked, so a pickup, a level unlock and a
    /// debug grant all show up in the menu without knowing it exists.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Inventory/Key Item", fileName = "Key")]
    public class KeyItemDefinition : InventoryItemDefinition
    {
        [Header("Ability")]
        [Tooltip("The ability this item stands for. Shown in the Key Items tab while " +
                 "PlayerAbilities has it unlocked.")]
        [SerializeField] private AbilityKind ability = AbilityKind.Dash;

        /// <inheritdoc/>
        public override InventoryCategory Category => InventoryCategory.KeyItem;

        /// <summary>The ability this item mirrors.</summary>
        public AbilityKind Ability => ability;

        /// <summary>Points the item at its ability. For setup scripts and tests.</summary>
        public void Configure(AbilityKind kind) => ability = kind;
    }
}
