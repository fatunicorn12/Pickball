using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Which sword abilities the player has learned — the sword's counterpart
    /// to <see cref="PlayerAbilities"/>.
    ///
    /// Separate from PlayerAbilities on purpose, not by accident. That one is a
    /// fixed enum of movement verbs that locomotion switches on in code; these
    /// are assets the user adds without touching code, and the combo resolver
    /// reads them as a list. The two share a contract instead — Grant / Has /
    /// an Unlocked event carrying what was granted — so one toast can listen to
    /// both, and a boss drop can grant either through the same idea.
    ///
    /// Knows nothing about levels. <c>LevelUnlocks</c> grants from the level
    /// table; a boss drop or debug menu can grant directly.
    /// </summary>
    public class SwordAbilities : MonoBehaviour
    {
        [Tooltip("Abilities the player starts with, for testing a moveset without " +
                 "levelling up. Leave empty for a real playthrough.")]
        [SerializeField] private List<SwordAbilityDefinition> startingAbilities = new();

        // Rebuilt in OnEnable, not a readonly initializer: a script reload in
        // Play mode must not leave a stale or missing list behind.
        List<SwordAbilityDefinition> _unlocked;

        /// <summary>Raised once per newly granted ability, for toasts, VFX and audio.</summary>
        public event Action<SwordAbilityDefinition> Unlocked;

        /// <summary>Learned abilities, in the order learned. The resolver does
        /// not depend on this order — see <see cref="ComboResolver"/>.</summary>
        public IReadOnlyList<SwordAbilityDefinition> All => Learned;

        // Lazily created as well as in OnEnable: EditMode tests and editor
        // tooling call Grant on a component whose OnEnable never ran.
        List<SwordAbilityDefinition> Learned => _unlocked ??= new List<SwordAbilityDefinition>();

        /// <summary>Merges the starting set in rather than resetting the list, so
        /// a script reload keeps what was learned in play and running it twice
        /// changes nothing. Starting abilities are not announced.</summary>
        void OnEnable()
        {
            foreach (var a in startingAbilities)
                if (a != null && !Learned.Contains(a)) Learned.Add(a);
        }

        public bool Has(SwordAbilityDefinition ability)
            => ability != null && Learned.Contains(ability);

        /// <summary>Learn <paramref name="ability"/>. Returns false if it was
        /// already known (or null), in which case nothing is raised.</summary>
        public bool Grant(SwordAbilityDefinition ability)
        {
            if (ability == null || Has(ability)) return false;
            Learned.Add(ability);
            Unlocked?.Invoke(ability);
            return true;
        }

        public bool Revoke(SwordAbilityDefinition ability)
            => ability != null && Learned.Remove(ability);
    }
}
