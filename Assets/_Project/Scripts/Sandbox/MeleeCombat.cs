using System;
using System.Collections.Generic;
using Pickleball.Sandbox.Combat;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The sword's gameplay: which swing plays next, when its blade is live,
    /// and what it hits. Kingdom-Hearts-style — the chain grows as the player
    /// learns abilities, the air has its own chain, finishers cap both.
    ///
    /// What to swing is decided by <see cref="ComboResolver"/> from data (a
    /// <see cref="SwordMoveset"/> plus the learned <see cref="SwordAbilities"/>),
    /// so a new slash is an asset, not a code change. See
    /// Docs/Systems/SwordAbilities.md.
    ///
    /// This component only simulates. It reads no input (<see cref="SwordInput"/>
    /// calls <see cref="QueueAttack"/>), plays no animation and spawns no
    /// effects: it raises <see cref="SwingStarted"/>, <see cref="HitLanded"/>,
    /// <see cref="BurstReleased"/> and friends, and SwordAnimatorDriver and
    /// SwordEffects listen. Movement is read through <see cref="IMovementState"/>
    /// only.
    ///
    /// Two rules carried over from the original combo, because breaking either
    /// brought back a bug:
    ///  - Timing is owned by <see cref="SwingTimeline"/>, not read back off the
    ///    Animator, which is unreliable on the frame a transition happens.
    ///  - Swinging neither moves nor stops the character. Steps may add a
    ///    one-off lunge impulse, never a per-frame push: a per-frame lunge once
    ///    slid the player metres per swing.
    /// </summary>
    public class MeleeCombat : MonoBehaviour
    {
        [Header("Moveset")]
        [Tooltip("The base chains (ground, air and their default finishers). " +
                 "Created by Tools ▸ Pickleball ▸ Setup Sword Abilities.")]
        [SerializeField] private SwordMoveset moveset;

        [Tooltip("Learned abilities layered onto the moveset. Found on this " +
                 "object if left empty.")]
        [SerializeField] private SwordAbilities abilities;

        [Header("Blade")]
        [Tooltip("Base of the blade — the pivot end.")]
        [SerializeField] private Transform bladeBase;
        [Tooltip("Tip of the blade. The swept volume is measured between the " +
                 "two, across frames.")]
        [SerializeField] private Transform bladeTip;
        [Tooltip("Thickness of the hit capsule around the blade, in metres. " +
                 "Bigger is more forgiving and less honest.")]
        [Min(0.01f)] [SerializeField] private float bladeRadius = 0.18f;
        [Tooltip("Layers the blade and bursts can hit.")]
        [SerializeField] private LayerMask hittable = ~0;

        [Header("Wiring")]
        [Tooltip("Component implementing IMovementState (and IMovementImpulses " +
                 "for lunges) — normally ChibiMovementAdapter. Found on this " +
                 "object if left empty.")]
        [SerializeField] private MonoBehaviour movementSource;
        [Tooltip("Attack button reader. Found on this object if left empty.")]
        [SerializeField] private SwordInput input;
        [Tooltip("Optional. Supplies the target distance that openers and " +
                 "ranged finishers test against.")]
        [SerializeField] private LockOnTargeting lockOn;

        [Header("Feel")]
        [Tooltip("Seconds a press made while unable to swing (mid-dash, in " +
                 "hitstop after a swing ended) is remembered. Without it, pressing " +
                 "a hair early simply does nothing.")]
        [Range(0f, 0.5f)] [SerializeField] private float pressBufferSeconds = 0.15f;
        [Tooltip("Seconds off the ground before the player counts as airborne " +
                 "for chain bookkeeping. Absorbs one-frame grounded flicker on " +
                 "slopes, which would otherwise reset a ground chain mid-combo.")]
        [Range(0f, 0.3f)] [SerializeField] private float airborneGraceSeconds = 0.08f;

#pragma warning disable 0169, 0414, 0649
        // ---- Legacy wiring --------------------------------------------------
        // Tools ▸ Pickleball ▸ Setup Weapon still writes these by name (with no
        // null checks), so they must keep existing until WeaponSetup is moved
        // onto the step assets. Nothing reads them at runtime; animation, the
        // trail and the camera now belong to SwordAnimatorDriver/SwordEffects.
        [Serializable]
        public class LegacyAttack
        {
            public string trigger;
            public float duration;
            public float activeStart, activeEnd, comboOpen, comboClose;
            public bool launcher;
        }
        [HideInInspector] [SerializeField] private Animator animator;
        [HideInInspector] [SerializeField] private ChibiLocomotion locomotion;
        [HideInInspector] [SerializeField] private BladeTrail trail;
        [HideInInspector] [SerializeField] private SandboxOrbitCamera cameraRig;
        [HideInInspector] [SerializeField] private LegacyAttack[] chain = new LegacyAttack[4];
        [HideInInspector] [SerializeField] private LegacyAttack airAttack = new();
#pragma warning restore 0169, 0414, 0649

        /// <summary>A step has started. Raised for every step, chained or not.</summary>
        public event Action<ResolvedStep> SwingStarted;

        /// <summary>The chain is over — the last swing ended without a follow-up.
        /// Not raised when one step chains into the next.</summary>
        public event Action<ResolvedStep> ChainEnded;

        /// <summary>The blade started (true) or stopped (false) hitting.</summary>
        public event Action<bool> BladeLiveChanged;

        /// <summary>A hit connected and its damage has been dealt.</summary>
        public event Action<MeleeHitReport> HitLanded;

        /// <summary>A step's area burst was released.</summary>
        public event Action<BurstReport> BurstReleased;

        /// <summary>True while a swing is playing. ChibiLocomotion reads this to
        /// square up to a lock-on target mid-combo.</summary>
        public bool IsAttacking => _timeline != null && _timeline.IsRunning;

        /// <summary>True while a connecting hit has frozen the action.</summary>
        public bool IsInHitstop => _hitstopLeft > 0f;

        /// <summary>The swing playing now; invalid when idle.</summary>
        public ResolvedStep Current => IsAttacking ? _current : default;

        /// <summary>Progress through the current swing, 0-1.</summary>
        public float SwingProgress => IsAttacking ? _timeline.Normalized : 0f;

        public SwordMoveset Moveset => moveset;

        const int OverlapCapacity = 16;
        const int BurstCapacity = 4;

        IMovementState _movement;
        IMovementImpulses _impulses;

        // Runtime-built state is (re)created in OnEnable, never in readonly
        // initializers, so a script reload in Play mode rebuilds it cleanly.
        ComboResolver _resolver;
        ComboProgress _progress;
        SwingTimeline _timeline;
        HashSet<Object> _alreadyHit;
        Collider[] _overlap;
        ActiveBurst[] _bursts;

        ResolvedStep _current;
        float _hitstopLeft;
        bool _queued;
        float _startBufferedUntil = -1f;
        float _airborneFor;
        bool _airborne;
        Vector3 _prevTip, _prevBase;
        bool _hasPrev;

        /// <summary>One ring in flight. Pooled: a finisher every second must not
        /// allocate.</summary>
        sealed class ActiveBurst
        {
            public bool Active;
            public ResolvedStep Placement;
            public AreaBurst Burst;
            public Vector3 Centre;
            public float Elapsed;
            public readonly HashSet<Object> Struck = new();
        }

        void Awake()
        {
            if (abilities == null) abilities = GetComponent<SwordAbilities>();
            if (input == null) input = GetComponent<SwordInput>();
            if (lockOn == null) lockOn = GetComponent<LockOnTargeting>();
            ResolveMovement();
        }

        void ResolveMovement()
        {
            _movement = movementSource as IMovementState ?? GetComponent<IMovementState>();
            _impulses = movementSource as IMovementImpulses ?? GetComponent<IMovementImpulses>();
        }

        void OnEnable()
        {
            _resolver = new ComboResolver();
            _progress = new ComboProgress();
            _timeline = new SwingTimeline();
            _alreadyHit = new HashSet<Object>();
            _overlap = new Collider[OverlapCapacity];
            _bursts = new ActiveBurst[BurstCapacity];
            for (int i = 0; i < _bursts.Length; i++) _bursts[i] = new ActiveBurst();
            _hitstopLeft = 0f;
            _queued = false;
            _startBufferedUntil = -1f;

            if (_movement == null) ResolveMovement();
            if (input != null) input.AttackPressed += QueueAttack;
            if (moveset == null)
                Debug.LogWarning($"[MeleeCombat] {name} has no SwordMoveset — the sword " +
                                 "will not swing. Run Tools ▸ Pickleball ▸ Setup Sword Abilities.", this);
        }

        void OnDisable()
        {
            if (input != null) input.AttackPressed -= QueueAttack;
            if (_timeline != null && _timeline.IsRunning) FinishChain();
        }

        /// <summary>
        /// Ask for a swing. Starts one now if idle, otherwise buffers the press
        /// until the current swing's chain window opens. Requiring the press to
        /// land inside the window would punish pressing early, which is exactly
        /// what players do.
        /// </summary>
        public void QueueAttack()
        {
            if (!isActiveAndEnabled) return;
            if (IsAttacking) _queued = true;
            else _startBufferedUntil = Time.time + pressBufferSeconds;
        }

        /// <summary>The whole chain the next fresh press on <paramref name="track"/>
        /// would play — for a HUD preview or debugging. The list is reused.</summary>
        public IReadOnlyList<ResolvedStep> PreviewChain(ComboTrack track)
            => (_resolver ??= new ComboResolver()).BuildSequence(BuildContext(track, 0), moveset, Learned);

        IReadOnlyList<SwordAbilityDefinition> Learned => abilities != null ? abilities.All : null;

        void Update()
        {
            float dt = Time.deltaTime;
            TrackGround(dt);

            // Hitstop: hold the clock (and so the blade, the chain windows and
            // any rings in flight) so a connecting hit reads as impact rather
            // than a pass-through. The animator driver freezes the pose.
            if (_hitstopLeft > 0f)
            {
                _hitstopLeft = Mathf.Max(0f, _hitstopLeft - dt);
                return;
            }

            TickBursts(dt);

            if (!_timeline.IsRunning)
            {
                if (Time.time <= _startBufferedUntil && TryStartStep())
                    _startBufferedUntil = -1f;
                return;
            }

            var signal = _timeline.Advance(dt);
            if ((signal & SwingSignal.BladeOn) != 0) BladeLiveChanged?.Invoke(true);
            // A whole window crossed in one long frame still gets one sweep.
            if (_timeline.IsBladeLive || (signal & SwingSignal.BladeOn) != 0) SweepForHits();
            if ((signal & SwingSignal.BladeOff) != 0)
            {
                _hasPrev = false;
                BladeLiveChanged?.Invoke(false);
            }
            if ((signal & SwingSignal.BurstDue) != 0) ReleaseBurst();

            if (_queued && (_timeline.IsChainWindowOpen || (signal & SwingSignal.Finished) != 0))
            {
                _queued = false;
                if (TryStartStep()) return;
            }
            if ((signal & SwingSignal.Finished) != 0) FinishChain();
        }

        /// <summary>
        /// Ground/air bookkeeping for the chain counters.
        ///
        /// Asymmetric on purpose. Leaving the ground only counts after a short
        /// grace, so one-frame grounded flicker on a slope cannot drop a ground
        /// chain. Touching the ground refills the air chain at once, with no
        /// grace — otherwise an air step started during such a flicker would
        /// leave the counter part-spent until the next real jump and landing.
        /// </summary>
        void TrackGround(float dt)
        {
            bool grounded = IsGrounded;
            _airborneFor = grounded ? 0f : _airborneFor + dt;
            if (grounded && _progress.AirHits > 0) _progress.Land();

            bool airborne = _airborneFor > airborneGraceSeconds;
            if (airborne && !_airborne) _progress.LeaveGround();
            _airborne = airborne;
        }

        bool IsGrounded => _movement == null || _movement.IsGrounded;

        /// <summary>Attacks refuse to start while another system owns the body
        /// (tether, skateboard, dash) rather than fight it for control.</summary>
        bool CanSwing => _movement == null || !_movement.IsBusy;

        ComboContext BuildContext(ComboTrack track, int hits)
        {
            bool hasTarget = lockOn != null && lockOn.IsLocked;
            float distance = 0f;
            if (hasTarget)
            {
                Vector3 to = lockOn.AimPoint - transform.position;
                to.y = 0f;
                distance = Mathf.Max(0f, to.magnitude - lockOn.TargetRadius);
            }
            return new ComboContext(track, hits, hasTarget, distance);
        }

        /// <summary>Resolve and start the next step in the current context.
        /// False if the chain has nothing more to give, or the body is busy.</summary>
        bool TryStartStep()
        {
            if (!CanSwing || moveset == null) return false;
            var track = IsGrounded ? ComboTrack.Ground : ComboTrack.Air;
            var placement = _resolver.Resolve(BuildContext(track, _progress.HitsIn(track)),
                                              moveset, Learned);
            if (!placement.IsValid) return false;
            Begin(placement);
            return true;
        }

        void Begin(ResolvedStep placement)
        {
            if (_timeline.IsRunning && (_timeline.Stop() & SwingSignal.BladeOff) != 0)
                BladeLiveChanged?.Invoke(false);

            var step = placement.Step;
            _current = placement;
            _progress.RecordStep(placement.Track);
            _timeline.Start(step);
            _queued = false;
            _alreadyHit.Clear();
            _hasPrev = false;

            // An impulse, once — see IMovementImpulses for why never per frame.
            if (step.LungeSpeed > 0f && _impulses != null)
                _impulses.AddImpulse(Flat(transform.forward) * step.LungeSpeed);

            SwingStarted?.Invoke(placement);
        }

        void FinishChain()
        {
            var ended = _current;
            if (_timeline.IsRunning && (_timeline.Stop() & SwingSignal.BladeOff) != 0)
                BladeLiveChanged?.Invoke(false);
            _queued = false;
            _alreadyHit.Clear();
            _hasPrev = false;
            _progress.DropChain();
            ChainEnded?.Invoke(ended);
        }

        // ------------------------------------------------------------ blade

        /// <summary>
        /// Sweep the volume the blade covered since last frame.
        ///
        /// A collider on the blade would tunnel: at swing speed the tip crosses
        /// metres per frame and can be on both sides of a target without ever
        /// overlapping it. Testing the capsule between last frame's blade and
        /// this frame's catches the whole arc.
        /// </summary>
        void SweepForHits()
        {
            if (bladeBase == null || bladeTip == null) return;
            Vector3 b = bladeBase.position, tip = bladeTip.position;

            if (_hasPrev)
            {
                TestCapsule(b, tip);
                TestCapsule(_prevTip, tip);
                TestCapsule(_prevBase, b);
            }
            _prevBase = b;
            _prevTip = tip;
            _hasPrev = true;
        }

        void TestCapsule(Vector3 a, Vector3 b)
        {
            int count = Physics.OverlapCapsuleNonAlloc(a, b, bladeRadius, _overlap, hittable,
                                                       QueryTriggerInteraction.Ignore);
            var hit = _current.Step.Hit;
            for (int i = 0; i < count; i++)
            {
                var col = _overlap[i];
                if (!TryGetTarget(col, _alreadyHit, out var target)) continue;   // once per swing

                Vector3 point = col.ClosestPoint((a + b) * 0.5f);
                Deal(target, col, point, hit, hit.PushAlong(transform.forward), _current, fromBurst: false);
            }
        }

        bool TryGetTarget(Collider col, HashSet<Object> struck, out IDamageable target)
        {
            target = null;
            if (col == null || col.transform.IsChildOf(transform)) return false;
            target = col.GetComponentInParent<IDamageable>();
            if (target == null) return false;
            return !(target is Object key) || struck.Add(key);
        }

        void Deal(IDamageable target, Collider col, Vector3 point, HitSettings hit, Vector3 push,
                  ResolvedStep placement, bool fromBurst)
        {
            target.TakeHit(hit.Damage, point, push, hit.IsLauncher);
            // Max, not sum: a ring sweeping three targets in one frame is one impact.
            _hitstopLeft = Mathf.Max(_hitstopLeft, hit.Hitstop);
            HitLanded?.Invoke(new MeleeHitReport(target, col, point, hit.Damage, placement, fromBurst));
        }

        // ----------------------------------------------------------- bursts

        void ReleaseBurst()
        {
            var burst = _current.Step.Burst;
            if (!burst.Enabled) return;

            ActiveBurst slot = null;
            foreach (var b in _bursts) if (!b.Active) { slot = b; break; }
            if (slot == null) return;    // four rings in flight already; drop the fifth

            slot.Active = true;
            slot.Placement = _current;
            slot.Burst = burst;
            slot.Centre = burst.CentreFor(transform.position, transform.forward);
            slot.Elapsed = 0f;
            slot.Struck.Clear();
            BurstReleased?.Invoke(new BurstReport(_current, slot.Centre, burst));
            StrikeWithin(slot, burst.RadiusAt(0f));
        }

        /// <summary>Grow each ring and strike whatever its edge has reached. A
        /// ring outlives the swing that released it, so this runs whether or
        /// not a swing is playing.</summary>
        void TickBursts(float dt)
        {
            foreach (var slot in _bursts)
            {
                if (!slot.Active) continue;
                slot.Elapsed += dt;
                StrikeWithin(slot, slot.Burst.RadiusAt(slot.Elapsed));
                if (slot.Burst.IsFinished(slot.Elapsed)) slot.Active = false;
            }
        }

        void StrikeWithin(ActiveBurst slot, float radius)
        {
            if (radius <= 0f) return;
            var burst = slot.Burst;
            // The query sphere has to contain the whole cylinder band.
            float reach = Mathf.Sqrt(radius * radius + burst.HeightTolerance * burst.HeightTolerance);
            int count = Physics.OverlapSphereNonAlloc(slot.Centre, reach, _overlap, hittable,
                                                      QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var col = _overlap[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;
                Vector3 point = col.ClosestPoint(slot.Centre);
                if (!burst.Reaches(slot.Centre, point, radius)) continue;
                if (!TryGetTarget(col, slot.Struck, out var target)) continue;
                // The ring is reported as the step that released it, even if the
                // chain has moved on by the time its edge arrives.
                Deal(target, col, point, burst.Hit, burst.Hit.PushAlong(point - slot.Centre),
                     slot.Placement, fromBurst: true);
            }
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.zero;
        }
    }
}
