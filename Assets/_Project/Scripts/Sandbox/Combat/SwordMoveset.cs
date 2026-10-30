using System;
using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// The weapon's base kit: the chains a level-1 player has before any
    /// ability is learned. Abilities are layered on top by
    /// <see cref="ComboResolver"/>; this asset never lists them.
    ///
    /// Each track is a run of ordinary hits plus an optional default finisher.
    /// Keeping the finisher separate is what lets Combo Plus lengthen the run
    /// while the heavy last hit stays last, and lets a finisher ability swap
    /// the cap without rebuilding the chain.
    ///
    /// The moveset also says how learned abilities apply to this weapon
    /// (<see cref="StepFor"/>): with its own swing, with the ability's, or not at
    /// all. Abilities belong to the player and outlive a weapon swap, but their
    /// swings are timed for the weapon they were authored on.
    /// </summary>
    [CreateAssetMenu(fileName = "Moveset_New", menuName = "Pickleball/Combat/Sword Moveset")]
    public class SwordMoveset : ScriptableObject
    {
        [Header("Ground")]
        [Tooltip("Ordinary ground hits, in order. Alternate the swing direction " +
                 "hit to hit — two the same way read as a stutter.")]
        [SerializeField] private List<ComboStepDefinition> groundChain = new();

        [Tooltip("Last ground hit when no finisher ability qualifies. Optional.")]
        [SerializeField] private ComboStepDefinition groundFinisher;

        [Header("Air")]
        [Tooltip("Ordinary air hits, in order. The whole air track is spent once " +
                 "per airtime and refills on landing.")]
        [SerializeField] private List<ComboStepDefinition> airChain = new();

        [Tooltip("Last air hit when no air finisher ability qualifies. Optional.")]
        [SerializeField] private ComboStepDefinition airFinisher;

        [Header("Learned abilities")]
        [Tooltip("On: a learned ability with no row below plays its own swing with this " +
                 "weapon (the compass: every ability was authored for it). Off: only " +
                 "abilities listed below do anything — a weapon with its own pace (the " +
                 "ruler) opts in to each ability rather than inheriting swings timed for " +
                 "another blade.")]
        [SerializeField] private bool acceptOtherAbilities = true;

        [Tooltip("Per-ability rules for this weapon. Step set = play this swing in the " +
                 "ability's place (same role, track, order and condition); Step empty = " +
                 "the ability does nothing while this weapon is held. Rows win over " +
                 "Accept Other Abilities.")]
        [SerializeField] private List<AbilityStepOverride> abilitySteps = new();

        /// <summary>True if learned abilities without a row play their own step.</summary>
        public bool AcceptsOtherAbilities => acceptOtherAbilities;

        /// <summary>This weapon's per-ability rows.</summary>
        public IReadOnlyList<AbilityStepOverride> AbilitySteps => abilitySteps;

        /// <summary>
        /// The swing <paramref name="ability"/> plays with this weapon, or null if it
        /// does nothing here: the ability's row if it has one (its step, or null for a
        /// block), else the ability's own step when <see cref="AcceptsOtherAbilities"/>,
        /// else null. <see cref="ComboResolver"/> asks this for every learned ability,
        /// so the rule holds for extenders, finishers and openers alike.
        /// </summary>
        public ComboStepDefinition StepFor(SwordAbilityDefinition ability)
        {
            if (ability == null) return null;
            for (int i = 0; i < abilitySteps.Count; i++)
                if (abilitySteps[i].Ability == ability) return abilitySteps[i].Step;
            return acceptOtherAbilities ? ability.Step : null;
        }

        /// <summary>The ordinary hits of a track, in play order.</summary>
        public IReadOnlyList<ComboStepDefinition> ChainFor(ComboTrack track)
            => track == ComboTrack.Ground ? groundChain : airChain;

        /// <summary>A track's default finisher, or null if it has none.</summary>
        public ComboStepDefinition DefaultFinisherFor(ComboTrack track)
            => track == ComboTrack.Ground ? groundFinisher : airFinisher;

        /// <summary>Every step this moveset can play on its own, for setup scripts
        /// that build Animator states. Abilities' steps are not included.</summary>
        public IEnumerable<ComboStepDefinition> AllBaseSteps()
        {
            foreach (var s in groundChain) if (s != null) yield return s;
            if (groundFinisher != null) yield return groundFinisher;
            foreach (var s in airChain) if (s != null) yield return s;
            if (airFinisher != null) yield return airFinisher;
        }

        /// <summary>Build a moveset in code — for EditMode tests and MeleeCombat's
        /// unwired fallback. The caller owns the instance and must destroy it.</summary>
        public static SwordMoveset CreateRuntime(IEnumerable<ComboStepDefinition> ground,
                                                 ComboStepDefinition groundFinisher,
                                                 IEnumerable<ComboStepDefinition> air,
                                                 ComboStepDefinition airFinisher)
        {
            var set = CreateInstance<SwordMoveset>();
            set.name = "RuntimeMoveset";
            set.hideFlags = HideFlags.DontSave;
            set.groundChain = new List<ComboStepDefinition>(ground);
            set.groundFinisher = groundFinisher;
            set.airChain = new List<ComboStepDefinition>(air);
            set.airFinisher = airFinisher;
            return set;
        }

        /// <summary>Set the ability rules of a moveset built in code (tests, runtime
        /// fallbacks). Replaces any rules it had; returns the moveset for chaining.</summary>
        public SwordMoveset WithAbilityRules(bool acceptOthers, params AbilityStepOverride[] rows)
        {
            acceptOtherAbilities = acceptOthers;
            abilitySteps = new List<AbilityStepOverride>(rows ?? Array.Empty<AbilityStepOverride>());
            return this;
        }
    }
}
