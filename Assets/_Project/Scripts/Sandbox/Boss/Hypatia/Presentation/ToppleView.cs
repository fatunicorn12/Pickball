using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// Shows her topple: her model pitches forward as the brain sinks her body to
    /// the plate, and straightens when she recovers. If her animator controller
    /// has a <c>Toppled</c> bool (Setup Hypatia Fight adds one, with Newton's Hit
    /// clip as a placeholder stagger before holding idle) it is set too; without
    /// one the topple is purely procedural.
    ///
    /// Presentation only: listens to <see cref="HypatiaBoss.Toppled"/> and
    /// <see cref="HypatiaBoss.Recovered"/>, never calls a gameplay setter. The
    /// tilt goes on the MODEL, not on Body: Body carries her hurtbox, which stays
    /// upright so what batted orbiters and the sword hit does not swing with the
    /// animation. The tilt blends on her clock, so a frozen topple holds its pose.
    /// </summary>
    public class ToppleView : MonoBehaviour
    {
        static readonly int ToppledId = Animator.StringToHash("Toppled");

        [Header("Source")]
        [Tooltip("Her brain.")]
        [SerializeField] private HypatiaBoss boss;

        [Header("Parts")]
        [Tooltip("Her model (a child of Body); pitched forward while toppled.")]
        [SerializeField] private Transform model;
        [Tooltip("Her animator. Optional; used only if its controller has a 'Toppled' bool.")]
        [SerializeField] private Animator animator;

        [Header("Look")]
        [Tooltip("Forward pitch while toppled, °. About 30 reads as 'knocked off balance' " +
                 "without her face disappearing into the plate.")]
        [SerializeField, Range(0f, 90f)] private float tiltDegrees = 32f;
        [Tooltip("How fast the pitch blends in and out, 1/s (exponential, her clock).")]
        [SerializeField, Min(0.1f)] private float tiltRate = 7f;

        HypatiaBoss _subscribed;
        bool _hasParameter;
        // Private but serializable: a script reload keeps the upright pose captured
        // before any tilt, instead of re-capturing a tilted one.
        Quaternion _upright;
        bool _captured;
        float _angle, _target;

        /// <summary>The current forward pitch of the model, °.</summary>
        public float Angle => _angle;

        void OnEnable()
        {
            if (model != null && !_captured)
            {
                _upright = model.localRotation;
                _captured = true;
            }
            _hasParameter = HasToppledParameter(animator);

            _subscribed = boss;
            if (_subscribed != null)
            {
                _subscribed.Toppled += OnToppled;
                _subscribed.Recovered += OnRecovered;
            }
            bool down = boss != null && boss.IsToppled;
            _target = down ? tiltDegrees : 0f;
            SetAnimator(down);
        }

        void OnDisable()
        {
            if (_subscribed != null)
            {
                _subscribed.Toppled -= OnToppled;
                _subscribed.Recovered -= OnRecovered;
            }
            _subscribed = null;
        }

        void OnToppled(float duration)
        {
            _target = tiltDegrees;
            SetAnimator(true);
        }

        void OnRecovered()
        {
            _target = 0f;
            SetAnimator(false);
        }

        void LateUpdate()
        {
            if (model == null || !_captured) return;
            float scale = boss != null && boss.Context != null ? boss.Context.BossTimeScale : 1f;
            float dt = Time.deltaTime * scale;
            _angle += (_target - _angle) * (1f - Mathf.Exp(-tiltRate * dt));
            model.localRotation = _upright * Quaternion.Euler(_angle, 0f, 0f);
        }

        void SetAnimator(bool toppled)
        {
            if (_hasParameter && animator != null && animator.isActiveAndEnabled) animator.SetBool(ToppledId, toppled);
        }

        static bool HasToppledParameter(Animator a)
        {
            if (a == null || a.runtimeAnimatorController == null) return false;
            // Allocates the array once per enable; never per frame.
            foreach (AnimatorControllerParameter p in a.parameters)
                if (p.nameHash == ToppledId && p.type == AnimatorControllerParameterType.Bool) return true;
            return false;
        }
    }
}
