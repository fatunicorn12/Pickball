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
    }
}
