using UnityEngine;

namespace Pickleball.Sandbox.Combat.Presentation
{
    /// <summary>
    /// Plays MeleeCombat's swings on the Animator's masked Attack layer. Pure
    /// listener: it is told what started and mirrors it, and owns nothing the
    /// combo's timing depends on.
    ///
    /// Each step's state is crossfaded into directly rather than triggered.
    /// MeleeCombat's timer already owns every window, so the state machine only
    /// has to play what it is told:
    ///  - no trigger can be left latched by a press that landed mid-transition
    ///    and fire a swing later;
    ///  - a fresh chain can start while the last swing is still fading out;
    ///  - swings never transition back to the empty None state. Blending an
    ///    Override layer toward an empty state at full weight drags the arms to
    ///    the bind pose; instead the swing holds its last frame while the layer
    ///    weight fades out from under it.
    ///
    /// <b>Full body when standing.</b> A step flagged
    /// <see cref="ComboStepDefinition.FullBodyWhenStanding"/> also plays, in sync,
    /// on the full-body AttackBody layer that sits directly above the base layer.
    /// It plays there only if it starts with him grounded and standing still.
    /// <see cref="FullBodySwing"/> decides, and fades that layer's weight. The masked
    /// Attack layer above still owns the arms, spine and head, so only the legs and
    /// hips change hands. While the clip supplies the hips, SwingTwist's correction
    /// falls to zero by itself, because it always aims the chest where the clip has
    /// it. When he moves, the layer fades out and the legs return to locomotion
    /// under the masked swing. "Standing" is read off the Animator's Grounded and
    /// damped Speed parameters, which MovementAnimatorDriver sets from his own drive.
    /// A lunge's momentum never counts as moving.
    /// </summary>
    public class SwordAnimatorDriver : MonoBehaviour
    {
        [SerializeField] private MeleeCombat combat;
        [SerializeField] private Animator animator;

        [Tooltip("Masked upper-body layer the swing states live on. Must match " +
                 "WeaponSetup.AttackLayer and SwingTwist's layer.")]
        [SerializeField] private string attackLayer = "Attack";

        [Tooltip("Attack-layer weight fade, in weight per second. Fast enough that " +
                 "a swing does not visibly ramp in, slow enough not to pop.")]
        [Min(0.1f)] [SerializeField] private float layerFadeSpeed = 12f;

        [Tooltip("Crossfade into the first swing of a chain, in seconds. Shorter " +
                 "than a step's own blend-in, because the layer weight is also " +
                 "still fading in.")]
        [Range(0f, 0.3f)] [SerializeField] private float chainStartBlend = 0.06f;

        [Header("Full body when standing")]
        [Tooltip("Full-body layer a step flagged Full Body When Standing also plays on. " +
                 "Built by Tools ▸ Pickleball ▸ Setup Full-Body Swings; must match " +
                 "FullBodySwingSetup.Layer. If it is missing, every swing plays masked.")]
        [SerializeField] private string bodyLayer = "AttackBody";

        [Tooltip("At or under this speed, in m/s, he counts as standing: the Animator's " +
                 "damped Speed parameter, i.e. his own drive, not a lunge's momentum. " +
                 "The same threshold as ChibiFootIK's foot plant, so 'standing' means " +
                 "the same thing to both.")]
        [Min(0f)] [SerializeField] private float standingMaxSpeed = 0.12f;

        [Tooltip("Seconds the legs take to hand back to locomotion when he starts moving " +
                 "mid-swing, leaves the ground, or the swing ends. Shorter pops; longer " +
                 "drags the clip's footwork into the first stride.")]
        [Range(0.02f, 0.5f)] [SerializeField] private float bodyReleaseSeconds = 0.15f;

        static readonly int ComboHash = Animator.StringToHash("Combo");
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");

        int _layer = -1, _bodyLayer = -1;
        bool _layerResolved, _hasComboParam, _hasSpeedParam, _hasGroundedParam;
        float _weight;
        bool _frozeAnimator;
        // Rebuilt in OnEnable: a script reload re-runs OnEnable, never Awake.
        FullBodySwing _body;
        float _bodyWeightSet = -1f;

        /// <summary>Weight of the full-body layer this frame, 0-1, for debugging and
        /// playtests. 0 while every swing plays masked.</summary>
        public float FullBodyWeight => _body != null ? _body.Weight : 0f;

        void Awake()
        {
            if (combat == null) combat = GetComponent<MeleeCombat>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        void OnEnable()
        {
            _body = new FullBodySwing();
            _bodyWeightSet = -1f;
            if (combat != null) combat.SwingStarted += OnSwingStarted;
        }

        void OnDisable()
        {
            if (combat != null) combat.SwingStarted -= OnSwingStarted;
            if (_frozeAnimator && animator != null) animator.speed = 1f;
            _frozeAnimator = false;
            // Never leave the legs pinned to a swing on a disabled driver.
            _body?.Reset();
            if (animator != null && _bodyLayer >= 0) animator.SetLayerWeight(_bodyLayer, 0f);
            _bodyWeightSet = -1f;
        }

        /// <summary>
        /// Layer index, resolved on first use rather than in Awake. The Animator
        /// is on the model child and this component on the root, so the root
        /// wakes first, before the Animator has built its graph: GetLayerIndex
        /// would return -1 and the swing would play in the state machine but
        /// never blend in.
        /// </summary>
        int Layer()
        {
            if (_layerResolved) return _layer;
            if (animator == null || animator.runtimeAnimatorController == null) return -1;
            _layerResolved = true;
            _layer = animator.GetLayerIndex(attackLayer);
            _bodyLayer = string.IsNullOrEmpty(bodyLayer) ? -1 : animator.GetLayerIndex(bodyLayer);
            foreach (var p in animator.parameters)
            {
                if (p.nameHash == ComboHash && p.type == AnimatorControllerParameterType.Int)
                    _hasComboParam = true;
                else if (p.nameHash == SpeedHash && p.type == AnimatorControllerParameterType.Float)
                    _hasSpeedParam = true;
                else if (p.nameHash == GroundedHash && p.type == AnimatorControllerParameterType.Bool)
                    _hasGroundedParam = true;
            }
            if (_layer < 0)
                Debug.LogWarning($"[SwordAnimatorDriver] No '{attackLayer}' layer on the Animator — " +
                                 "swings will not play. Run Tools ▸ Pickleball ▸ Setup Weapon, " +
                                 "then Setup Sword Abilities.", this);
            return _layer;
        }

        // Without the parameters he never counts as standing, so swings stay masked.
        bool Grounded => _hasGroundedParam && animator.GetBool(GroundedHash);
        float Speed => _hasSpeedParam ? animator.GetFloat(SpeedHash) : float.PositiveInfinity;

        void OnSwingStarted(ResolvedStep placement)
        {
            int layer = Layer();
            if (layer < 0 || placement.Step == null) return;
            float blend = placement.StartsChain ? chainStartBlend : placement.Step.BlendIn;
            string state = placement.Step.AnimatorState;
            animator.CrossFadeInFixedTime(state, blend, layer, 0f);
            if (_hasComboParam)
                animator.SetInteger(ComboHash, placement.Track == ComboTrack.Ground ? placement.Index + 1 : 0);

            if (_bodyLayer < 0 || _body == null) return;
            bool wants = placement.Step.FullBodyWhenStanding
                         && animator.HasState(_bodyLayer, Animator.StringToHash(state));
            bool wasHidden = _body.IsHidden;
            if (!_body.Begin(wants, Grounded, Speed, standingMaxSpeed, blend, bodyReleaseSeconds)) return;
            // Same state, same start, same speed as the masked layer, so the two stay in
            // step. Switched outright while the layer is invisible.
            if (wasHidden) animator.Play(state, _bodyLayer, 0f);
            else animator.CrossFadeInFixedTime(state, blend, _bodyLayer, 0f);
        }

        void Update()
        {
            if (combat == null || animator == null) return;

            // Hitstop freezes the pose; MeleeCombat freezes the clock. IsInHitstop
            // is false while combat is disabled, so a swordsman disabled mid-impact
            // is thawed here on the next frame instead of staying frozen (QA CB4).
            bool freeze = combat.IsInHitstop;
            if (freeze != _frozeAnimator)
            {
                animator.speed = freeze ? 0f : 1f;
                _frozeAnimator = freeze;
            }

            // Runs after the swing ends too: the fade-out takes a few frames.
            int layer = Layer();
            if (layer < 0) return;
            float wanted = combat.IsAttacking ? 1f : 0f;
            if (!Mathf.Approximately(_weight, wanted))
            {
                _weight = Mathf.MoveTowards(_weight, wanted, layerFadeSpeed * Time.deltaTime);
                animator.SetLayerWeight(layer, _weight);
                if (_hasComboParam && wanted == 0f && _weight == 0f) animator.SetInteger(ComboHash, 0);
            }

            if (_bodyLayer < 0 || _body == null) return;
            _body.Tick(Time.deltaTime, combat.IsAttacking, Grounded, Speed, standingMaxSpeed, bodyReleaseSeconds);
            if (!Mathf.Approximately(_body.Weight, _bodyWeightSet))
            {
                _bodyWeightSet = _body.Weight;
                animator.SetLayerWeight(_bodyLayer, _bodyWeightSet);
            }
        }
    }
}
