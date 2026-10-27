using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// A learnable sword ability — Kingdom Hearts' Combo Plus, Aerial Sweep,
    /// Ripple Drive and friends — as data.
    ///
    /// An ability is one step plus a rule for where that step goes: appended to
    /// a chain (<see cref="ComboRole.Extender"/>), capping it
    /// (<see cref="ComboRole.Finisher"/>) or replacing its first hit
    /// (<see cref="ComboRole.Opener"/>). It does not say how it is earned: the
    /// level table on <c>LevelingConfig</c> or a boss drop decides that, so the
    /// same ability can be a level-8 unlock in one playthrough and Riemann's
    /// reward in another without touching this asset.
    /// </summary>
    [CreateAssetMenu(fileName = "Ability_New", menuName = "Pickleball/Combat/Sword Ability")]
    public class SwordAbilityDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Name shown on the unlock toast.")]
        [SerializeField] private string displayName;

        [Tooltip("One line of flavour for the unlock toast.")]
        [TextArea(2, 4)] [SerializeField] private string description;

        [Header("Effect on the combo")]
        [Tooltip("Extender = one more hit before the finisher; Finisher = replaces " +
                 "the chain's last hit; Opener = replaces its first hit.")]
        [SerializeField] private ComboRole role = ComboRole.Extender;

        [Tooltip("Which chain it changes. Air abilities never touch the ground " +
                 "chain and vice versa.")]
        [SerializeField] private ComboTrack track = ComboTrack.Ground;

        [Tooltip("The swing this ability adds or substitutes.")]
        [SerializeField] private ComboStepDefinition step;

        [Tooltip("Extenders only: position among the other unlocked extenders on " +
                 "this track, lowest first. Fixed per ability rather than taken " +
                 "from unlock order, so the chain plays the same however it was " +
                 "earned.")]
        [SerializeField] private int chainOrder;

        [Tooltip("Finishers and openers only: when several unlocked ones qualify, " +
                 "the highest priority plays. Give context-specific ones (a ranged " +
                 "finisher) a higher priority than the catch-all.")]
        [SerializeField] private int priority;

        [Tooltip("Finishers and openers only: when this ability may replace the " +
                 "hit. Default = always.")]
        [SerializeField] private ComboCondition condition;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public string Description => description;
        public ComboRole Role => role;
        public ComboTrack Track => track;
        public ComboStepDefinition Step => step;
        public int ChainOrder => chainOrder;
        public int Priority => priority;
        public ComboCondition Condition => condition;

        /// <summary>Build an ability in code, for EditMode tests. The caller owns
        /// the instance and must destroy it.</summary>
        public static SwordAbilityDefinition CreateRuntime(string abilityName, ComboRole role, ComboTrack track,
                                                           ComboStepDefinition step, int chainOrder = 0,
                                                           int priority = 0, ComboCondition condition = default)
        {
            var ability = CreateInstance<SwordAbilityDefinition>();
            ability.name = abilityName;
            ability.hideFlags = HideFlags.DontSave;
            ability.displayName = abilityName;
            ability.role = role;
            ability.track = track;
            ability.step = step;
            ability.chainOrder = chainOrder;
            ability.priority = priority;
            ability.condition = condition;
            return ability;
        }
    }
}
