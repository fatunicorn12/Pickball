using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// A spell as an inventory entry. The spell itself (cost, delivery, every
    /// number) stays on its <see cref="SpellDefinition"/>; this only gives it a
    /// menu name, blurb and slot. The Magic tab mirrors the player's
    /// <see cref="SpellSelector"/>, and choosing an entry selects its spell
    /// there, so the selector keeps sole authority over what is cast.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Inventory/Magic Item", fileName = "Magic")]
    public class MagicItemDefinition : InventoryItemDefinition
    {
        [Header("Spell")]
        [Tooltip("The spell this entry selects. Its icon and theme colour are drawn in the " +
                 "slot when this item has no icon of its own.")]
        [SerializeField] private SpellDefinition spell;

        /// <inheritdoc/>
        public override InventoryCategory Category => InventoryCategory.Magic;

        /// <summary>The spell this entry stands for.</summary>
        public SpellDefinition Spell => spell;

        /// <summary>Points the item at its spell. For setup scripts and tests.</summary>
        public void Configure(SpellDefinition definition) => spell = definition;

        /// <summary>
        /// An unsaved entry for a spell no asset describes, so a spell added to
        /// the selector without its menu entry still shows in the Magic tab.
        /// Named after the spell.
        /// </summary>
        public static MagicItemDefinition CreateRuntime(SpellDefinition definition)
        {
            var item = CreateInstance<MagicItemDefinition>();
            item.hideFlags = HideFlags.DontSave;
            item.spell = definition;
            string label = definition != null ? definition.DisplayName : "Spell";
            item.name = $"Runtime_{label}";
            item.SetIdentity(definition != null ? definition.name.ToLowerInvariant() : "spell", label, "");
            return item;
        }
    }
}
