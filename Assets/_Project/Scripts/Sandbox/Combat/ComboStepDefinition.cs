using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// One swing of the sword: which Attack-layer state it plays, when its blade
    /// is live, what a hit does, and how it looks.
    ///
    /// A step knows nothing about where it sits in a chain or how it is
    /// unlocked — the same asset can be a base hit in one moveset and a Combo
    /// Plus in another. Chains are a <see cref="SwordMoveset"/>'s business and
    /// unlocks a <see cref="SwordAbilityDefinition"/>'s.
    ///
    /// The step references its clip directly, which removes a whole class of
    /// bug: the old combo had clip durations written into MeleeCombat by the
    /// setup script, so the timer and the animation drifted apart whenever one
    /// was changed without re-running the other. <see cref="Duration"/> is now
    /// computed from the very clip the Animator state plays, and Tools ▸
    /// Pickleball ▸ Setup Sword Abilities builds that state from this asset.
    /// </summary>
    [CreateAssetMenu(fileName = "Step_New", menuName = "Pickleball/Combat/Combo Step")]
    public class ComboStepDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Name shown in toasts and debug readouts. Falls back to the asset name.")]
        [SerializeField] private string displayName;

        [Header("Animation")]
        [Tooltip("Attack-layer state this step crossfades into. Setup Sword " +
                 "Abilities creates or updates a state of this name playing Clip " +
                 "at Playback Speed, so the two can never disagree.")]
        [SerializeField] private string animatorState = "Attack1";

        [Tooltip("The swing's animation. Also the source of Duration.")]
        [SerializeField] private AnimationClip clip;

        [Tooltip("Playback rate of the state, as a multiple of the authored clip " +
                 "speed. Mixamo swings are authored at 1.4-1.7 s, far too slow " +
                 "for an arcade combo; 2.2-2.5x is the house range.")]
        [Range(0.25f, 4f)] [SerializeField] private float playbackSpeed = 2.4f;

        [Tooltip("Seconds the swing lasts if Clip is missing — a placeholder so a " +
                 "step can be tuned before its animation exists.")]
        [Min(0.05f)] [SerializeField] private float fallbackDuration = 0.6f;

        [Tooltip("Crossfade into this step, in seconds. ~0.06 for an opener, ~0.1 " +
                 "between chained hits; longer looks floaty.")]
        [Range(0f, 0.4f)] [SerializeField] private float blendIn = 0.1f;

        [Tooltip("Play this swing on the whole body (legs and hips from the clip too) " +
                 "when it starts with the player grounded and standing still. For " +
                 "swings authored as a whole-body move, such as a lunge, whose pose the " +
                 "upper body alone cannot carry over standing legs: masked, the " +
                 "downstroke twisted the waist 126° and bent it 41° sideways. Moving, " +
                 "or once he starts moving mid-swing, it plays masked as usual. Needs " +
                 "Tools ▸ Pickleball ▸ Setup Full-Body Swings (also run by Setup Sword " +
                 "Abilities) after changing.")]
        [SerializeField] private bool fullBodyWhenStanding;

        [Header("Timing")]
        [SerializeField] private SwingWindows windows = SwingWindows.Default;

        [Header("Blade hit")]
        [SerializeField] private HitSettings hit = new(12f, 4f);

        [Header("Movement")]
        [Tooltip("One-off forward shove given to the player as the step starts, " +
                 "in m/s. 0 keeps the house rule that swinging neither moves nor " +
                 "stops you; a few m/s makes a finisher or gap-closer step in.")]
        [Min(0f)] [SerializeField] private float lungeSpeed;

        [Tooltip("Air track only: a hit from this swing that lands holds the player up " +
                 "(gravity off, vertical speed eased to rest) for MeleeCombat's " +
                 "airHangPerHitSeconds; whiffs hold nothing, and the chain's end drops him. " +
                 "Turn off for a step that should drop, such as a plunge finisher. " +
                 "Ignored on the ground chain.")]
        [SerializeField] private bool hangInAir = true;

        [Header("Area burst")]
        [SerializeField] private AreaBurst burst;

        [Header("Presentation")]
        [SerializeField] private SwingPresentation presentation = SwingPresentation.Default;

        public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
        public string AnimatorState => animatorState;
        public AnimationClip Clip => clip;
        public float PlaybackSpeed => playbackSpeed;
        public float BlendIn => blendIn;

        /// <summary>True if this swing plays full-body when it starts with the player
        /// standing still; see <see cref="Presentation.FullBodySwing"/>.</summary>
        public bool FullBodyWhenStanding => fullBodyWhenStanding;
        public SwingWindows Windows => windows;
        public HitSettings Hit => hit;
        public float LungeSpeed => lungeSpeed;

        /// <summary>This step's landed air hits hold the player up; see
        /// <see cref="AirHang"/>.</summary>
        public bool HangInAir => hangInAir;
        public AreaBurst Burst => burst;
        public SwingPresentation Presentation => presentation;

        /// <summary>Seconds the swing lasts on screen: the clip's length at this
        /// step's playback speed. This is what every window fraction is of.</summary>
        public float Duration => clip != null && playbackSpeed > 0f
            ? clip.length / playbackSpeed
            : fallbackDuration;

        /// <summary>
        /// Build a step in code — for EditMode tests, and for MeleeCombat's
        /// fallback when no moveset asset has been wired yet. The caller owns
        /// the instance and must destroy it.
        /// </summary>
        public static ComboStepDefinition CreateRuntime(string stepName, string state, float duration,
                                                        SwingWindows windows, HitSettings hit)
        {
            var step = CreateInstance<ComboStepDefinition>();
            step.name = stepName;
            step.hideFlags = HideFlags.DontSave;
            step.displayName = stepName;
            step.animatorState = state;
            step.fallbackDuration = Mathf.Max(0.05f, duration);
            step.windows = windows;
            step.hit = hit;
            return step;
        }
    }
}
