using System;
using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// A ready-made <see cref="ISlowable"/> and <see cref="IStaggerable"/> for
    /// anything that wants to opt in to the Freeze Zone and the Thunderbolt's
    /// stagger without writing its own bookkeeping. Put it on the same object as
    /// the target's <see cref="IDamageable"/> (or a parent of its colliders) and
    /// read <see cref="SpeedMultiplier"/> / <see cref="IsStaggered"/> wherever
    /// the target moves or attacks.
    ///
    /// Gameplay only. It holds the state and raises edge events; the frost and
    /// the dizzy stars are drawn by <c>StatusReceiverVisual</c>, which listens.
    /// The training dummies carry one (added by Setup Magic and Build Training
    /// Stage). They never move, so the slow there is purely a readable status
    /// until something reads the multiplier.
    ///
    /// <b>Optional animator slow.</b> With <see cref="animatorToSlow"/> set, the
    /// animator's speed follows the multiplier, the cheapest way to make a slow
    /// visible on an animated target. Leave it empty on anything whose gameplay
    /// timing is driven by code rather than the animation (Newton), or the
    /// animation and the hitboxes drift apart.
    ///
    /// Time is <c>Time.time</c>, read lazily, so a slow applied in one frame
    /// expires correctly whether or not this component's Update ran in
    /// between. Reload-safe: the ledger is rebuilt on first use, and a reload
    /// simply ends the running slow and stagger.
    /// </summary>
    public class StatusReceiver : MonoBehaviour, ISlowable, IStaggerable
    {
        [Tooltip("Optional. Its speed follows the slow multiplier while slowed and goes back " +
                 "to what it was afterwards. Leave empty when gameplay timing is driven by code.")]
        [SerializeField] private Animator animatorToSlow;

        [Tooltip("Longest stagger accepted, in seconds, whatever a spell asks for. Keeps a " +
                 "chain of strikes from stun-locking the target.")]
        [Min(0f)] [SerializeField] private float maxStaggerSeconds = 1.5f;

        /// <summary>Raised on the frame a target goes from unslowed to slowed.</summary>
        public event Action<StatusReceiver> SlowStarted;

        /// <summary>Raised on the frame the last slow runs out (checked in Update).</summary>
        public event Action<StatusReceiver> SlowEnded;

        /// <summary>Raised on every accepted stagger, with its length in seconds.</summary>
        public event Action<StatusReceiver, float> Staggered;

        SlowLedger _ledger;
        float _staggerUntil = -1f;
        bool _wasSlowed;
        float _animatorBaseSpeed = 1f;
        bool _animatorScaled;

        SlowLedger Ledger => _ledger ??= new SlowLedger();

        // Not isActiveAndEnabled: that reads false until OnEnable has run (during
        // Awake, and for editor tooling), and a status applied then must count.
        bool Live => enabled && gameObject.activeInHierarchy;

        /// <summary>Fraction of normal speed now, 0-1; 1 when nothing slows it.</summary>
        public float SpeedMultiplier => Ledger.MultiplierAt(Time.time);

        /// <summary>True while any slow is active.</summary>
        public bool IsSlowed => Ledger.IsSlowedAt(Time.time);

        /// <summary>Seconds until the last active slow ends; 0 when none.</summary>
        public float SlowRemaining => Ledger.RemainingAt(Time.time);

        /// <summary>True while a stagger runs.</summary>
        public bool IsStaggered => Time.time < _staggerUntil;

        /// <summary>Seconds of stagger left; 0 when none.</summary>
        public float StaggerRemaining => Mathf.Max(0f, _staggerUntil - Time.time);

        /// <inheritdoc/>
        public void ApplySlow(object source, float speedMultiplier, float seconds)
        {
            if (!Live) return;
            Ledger.Apply(source, speedMultiplier, seconds, Time.time);
            if (!_wasSlowed && Ledger.IsSlowedAt(Time.time))
            {
                _wasSlowed = true;
                SlowStarted?.Invoke(this);
            }
            ApplyAnimatorSpeed();
        }

        /// <inheritdoc/>
        public void Stagger(float seconds)
        {
            if (!Live || seconds <= 0f) return;
            float length = Mathf.Min(seconds, maxStaggerSeconds);
            if (length <= 0f) return;
            _staggerUntil = Mathf.Max(_staggerUntil, Time.time + length);
            Staggered?.Invoke(this, length);
        }

        /// <summary>Ends every slow and stagger now (respawn, cleanse).</summary>
        public void ClearAll()
        {
            Ledger.Clear();
            _staggerUntil = -1f;
            if (_wasSlowed)
            {
                _wasSlowed = false;
                SlowEnded?.Invoke(this);
            }
            RestoreAnimator();
        }

        void OnEnable()
        {
            _wasSlowed = false;
            _animatorScaled = false;
        }

        void OnDisable()
        {
            Ledger.Clear();
            _staggerUntil = -1f;
            _wasSlowed = false;
            RestoreAnimator();
        }

        void Update()
        {
            if (!_wasSlowed) return;
            if (Ledger.IsSlowedAt(Time.time))
            {
                ApplyAnimatorSpeed();
                return;
            }
            _wasSlowed = false;
            RestoreAnimator();
            SlowEnded?.Invoke(this);
        }

        void ApplyAnimatorSpeed()
        {
            if (animatorToSlow == null) return;
            if (!_animatorScaled)
            {
                _animatorBaseSpeed = animatorToSlow.speed;
                _animatorScaled = true;
            }
            animatorToSlow.speed = _animatorBaseSpeed * SpeedMultiplier;
        }

        void RestoreAnimator()
        {
            if (!_animatorScaled) return;
            if (animatorToSlow != null) animatorToSlow.speed = _animatorBaseSpeed;
            _animatorScaled = false;
        }
    }
}
