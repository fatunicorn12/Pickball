using System;
using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// One row of a <see cref="SwordMoveset"/>'s ability rules: what a learned ability
    /// plays when this weapon is in hand.
    ///
    /// Abilities are learned by the player, not by the sword, so every weapon meets every
    /// ability. Without this, each ability brought its own swing to every moveset: the
    /// compass's fast + C landed in the middle of the ruler's heavy chain. A row lets a
    /// weapon swing the ability its own way (<see cref="Step"/> set) or not at all
    /// (<see cref="Step"/> empty). The ability's role, track, chain order, priority and
    /// condition always come from the ability, so the chain keeps the same shape on
    /// every weapon; only the swing differs.
    /// </summary>
    [Serializable]
    public struct AbilityStepOverride
    {
        [Tooltip("The learned ability this row is about.")]
        [SerializeField] private SwordAbilityDefinition ability;

        [Tooltip("The swing this weapon plays for that ability, in the ability's place " +
                 "in the chain. Empty = the ability does nothing while this weapon is " +
                 "held (it stays learned and comes back with a weapon that accepts it).")]
        [SerializeField] private ComboStepDefinition step;

        public SwordAbilityDefinition Ability => ability;

        /// <summary>The weapon's own swing for the ability, or null if the weapon
        /// declines it.</summary>
        public ComboStepDefinition Step => step;

        /// <summary>True when this row turns the ability off for the weapon.</summary>
        public bool Blocks => step == null;

        public AbilityStepOverride(SwordAbilityDefinition ability, ComboStepDefinition step)
        {
            this.ability = ability;
            this.step = step;
        }
    }
}
