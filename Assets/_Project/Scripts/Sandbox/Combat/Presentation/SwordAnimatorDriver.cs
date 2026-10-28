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

        static readonly int ComboHash = Animator.StringToHash("Combo");

        int _layer = -1;
        bool _layerResolved, _hasComboParam;
        float _weight;
        bool _frozeAnimator;

        void Awake()
        {
            if (combat == null) combat = GetComponent<MeleeCombat>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        void OnEnable()
        {
            if (combat != null) combat.SwingStarted += OnSwingStarted;
        }

        void OnDisable()
        {
            if (combat != null) combat.SwingStarted -= OnSwingStarted;
            if (_frozeAnimator && animator != null) animator.speed = 1f;
            _frozeAnimator = false;
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
            foreach (var p in animator.parameters)
                if (p.nameHash == ComboHash && p.type == AnimatorControllerParameterType.Int)
                    _hasComboParam = true;
            if (_layer < 0)
                Debug.LogWarning($"[SwordAnimatorDriver] No '{attackLayer}' layer on the Animator — " +
                                 "swings will not play. Run Tools ▸ Pickleball ▸ Setup Weapon, " +
                                 "then Setup Sword Abilities.", this);
            return _layer;
        }

        void OnSwingStarted(ResolvedStep placement)
        {
            int layer = Layer();
            if (layer < 0 || placement.Step == null) return;
            float blend = placement.StartsChain ? chainStartBlend : placement.Step.BlendIn;
            animator.CrossFadeInFixedTime(placement.Step.AnimatorState, blend, layer, 0f);
            if (_hasComboParam)
                animator.SetInteger(ComboHash, placement.Track == ComboTrack.Ground ? placement.Index + 1 : 0);
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
            if (Mathf.Approximately(_weight, wanted)) return;
            _weight = Mathf.MoveTowards(_weight, wanted, layerFadeSpeed * Time.deltaTime);
            animator.SetLayerWeight(layer, _weight);
            if (_hasComboParam && wanted == 0f && _weight == 0f) animator.SetInteger(ComboHash, 0);
        }
    }
}
