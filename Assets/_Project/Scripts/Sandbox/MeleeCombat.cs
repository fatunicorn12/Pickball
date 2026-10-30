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
    /// SwordEffects listen. Movement is read through <see cref="ICombatMovement"/>
    /// only.
    ///
    /// Two rules carried over from the original combo, because breaking either
    /// brought back a bug:
    ///  - Timing is owned by <see cref="SwingTimeline"/>, not read back off the
    ///    Animator, which is unreliable on the frame a transition happens.
    ///  - Swinging neither moves nor stops the character. Steps may add a
    ///    one-off lunge impulse, never a per-frame push: a per-frame lunge once
    ///    slid the player metres per swing. The one per-frame request is the
    ///    air hang (<see cref="AirHang"/>), which only holds gravity off.
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
        [Tooltip("Component implementing ICombatMovement (and ICombatImpulses " +
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
        [Tooltip("Seconds off the ground before the player counts as airborne, " +
                 "for both choosing the air chain and dropping the ground chain. " +
                 "Absorbs grounded flicker on slopes; the cost is that a press this " +
                 "soon after a real jump still plays a ground step.")]
        [Range(0f, 0.3f)] [SerializeField] private float airborneGraceSeconds = 0.08f;

        [Tooltip("Seconds after a swing ends during which a press still continues " +
                 "the chain instead of restarting it. R1: 0.9 s-apart clicks always reset.")]
        [Range(0f, 1f)] [SerializeField] private float chainGraceSeconds = 0.45f;

        [Tooltip("Seconds an air combo keeps holding the player up after its last swing " +
                 "ends, before he falls. 0 drops him the frame the chain ends; a little " +
                 "gives the fall a beat.")]
        [Range(0f, 0.5f)] [SerializeField] private float airHangReleaseSeconds = 0.08f;

        [Header("Lunge homing")]
        [Tooltip("A locked target within this many metres bends the lunge toward it. 0 = off.")]
        [Min(0f)] [SerializeField] private float homingRange = 6f;
        [Tooltip("Max degrees per second the lunge direction may turn toward the target " +
                 "(applied over the swing's length).")]
        [Min(0f)] [SerializeField] private float homingTurnRate = 360f;
        [Tooltip("Gap to the target surface, in metres, under which the lunge is skipped " +
                 "so the player is not shoved through it.")]
        [Min(0f)] [SerializeField] private float homingStopGap = 0.8f;

        /// <summary>A step has started. Raised for every step, chained or not.</summary>
        public event Action<ResolvedStep> SwingStarted;

        /// <summary>The chain is over — the last swing ended without a follow-up.
        /// Not raised when one step chains into the next.</summary>
        public event Action<ResolvedStep> ChainEnded;

        /// <summary>The blade started (true) or stopped (false) hitting.</summary>
        public event Action<bool> BladeLiveChanged;

        /// <summary>A hit has connected and is about to be dealt. Raised just
        /// before the target's TakeHit, so listeners can read the target's
        /// state as it was — XP uses it to pay only for damage actually applied
        /// and never for hitting what was already defeated.</summary>
        public event Action<MeleeHitReport> HitConnecting;

        /// <summary>A hit connected and its damage has been dealt.</summary>
        public event Action<MeleeHitReport> HitLanded;

        /// <summary>A step's area burst was released.</summary>
        public event Action<BurstReport> BurstReleased;

        /// <summary>True while a swing is playing. ChibiLocomotion reads this to
        /// square up to a lock-on target mid-combo.</summary>
        public bool IsAttacking => _timeline != null && _timeline.IsRunning;

        /// <summary>True while a connecting hit has frozen the action. Never
        /// true while disabled, so nothing can stay frozen on its account.</summary>
        public bool IsInHitstop => isActiveAndEnabled && _hitstopLeft > 0f;

        /// <summary>The swing playing now; invalid when idle.</summary>
        public ResolvedStep Current => IsAttacking ? _current : default;

        /// <summary>Progress through the current swing, 0-1.</summary>
        public float SwingProgress => IsAttacking ? _timeline.Normalized : 0f;

        public SwordMoveset Moveset => moveset;

        // Room for level geometry as well as targets: the overlap queries run on
        // the hittable mask, and a 3 m ring on a busy map can return a dozen
        // floor and wall colliders. With 16 slots those could crowd the actual
        // target out of the results.
        const int OverlapCapacity = 64;
        const int BurstCapacity = 4;

        ICombatMovement _movement;
        ICombatImpulses _impulses;

        // Runtime-built state is (re)created in OnEnable, never in readonly
        // initializers, so a script reload in Play mode rebuilds it cleanly.
        ComboResolver _resolver;
        ComboProgress _progress;
        SwingTimeline _timeline;
        HashSet<Object> _alreadyHit;
        Collider[] _overlap;
        ActiveBurst[] _bursts;
        BladeSweep _sweep;
        GroundContact _contact;
        (Vector3 from, Vector3 to)[] _segments;

        ResolvedStep _current;
        float _hitstopLeft;
        bool _queued;
        float _startBufferedUntil = -1f;
        ChainGrace _grace;

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

        /// <summary>The serialized source if it is alive and implements the
        /// interface, else whatever on this object does. TryGetComponent rather
        /// than GetComponent ?? …: in the Editor a missing component comes back
        /// as a fake-null object, which C#'s ?? and interface casts treat as real.</summary>
        void ResolveMovement()
        {
            var source = movementSource != null ? movementSource : null;
            _movement = source as ICombatMovement;
            if (_movement == null && TryGetComponent(out ICombatMovement state)) _movement = state;
            _impulses = source as ICombatImpulses;
            if (_impulses == null && TryGetComponent(out ICombatImpulses levers)) _impulses = levers;
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
            _sweep = new BladeSweep();
            _contact = new GroundContact();
            _grace = new ChainGrace();
            _segments = new (Vector3, Vector3)[BladeSweep.MaxSegments];
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
            // Disabled mid-hitstop, the countdown would never run out and the
            // animator driver would hold the pose frozen for good (QA CB4).
            _hitstopLeft = 0f;
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
            HoldInAir(dt);

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
                _sweep.Reset();    // no swing: the next one must not sweep from a stale pose
                if (_grace.Tick(dt)) _progress.DropChain();
                if (Time.time <= _startBufferedUntil && TryStartStep())
                    _startBufferedUntil = -1f;
                return;
            }

            var signal = _timeline.Advance(dt);
            if ((signal & SwingSignal.BladeOn) != 0) BladeLiveChanged?.Invoke(true);
            // Sampled every frame of the swing; tested whenever any part of this
            // frame's interval was live (QA CB1).
            SampleBlade(BladeSweep.ShouldTest(_timeline.IsBladeLive, signal));
            if ((signal & SwingSignal.BladeOff) != 0) BladeLiveChanged?.Invoke(false);
            if ((signal & SwingSignal.BurstDue) != 0) ReleaseBurst();

            if (_queued && (_timeline.IsChainWindowOpen || (signal & SwingSignal.Finished) != 0))
            {
                _queued = false;
                if (TryStartStep()) return;
            }
            if ((signal & SwingSignal.Finished) != 0) EndSwing();
        }

        /// <summary>The swing ran out. A finisher ends the chain outright; any
        /// other step keeps the chain alive for <c>chainGraceSeconds</c> so a
        /// late press continues it.</summary>
        void EndSwing()
        {
            if (_current.IsLast || chainGraceSeconds <= 0f) { FinishChain(); return; }
            var ended = _current;
            if (_timeline.IsRunning && (_timeline.Stop() & SwingSignal.BladeOff) != 0)
                BladeLiveChanged?.Invoke(false);
            _queued = false;
            _alreadyHit.Clear();
            _sweep.Reset();
            _grace.Start(chainGraceSeconds);
            ChainEnded?.Invoke(ended);
        }

        /// <summary>
        /// Ground/air bookkeeping for the chain counters; see
        /// <see cref="GroundContact"/> for why leaving the ground has a grace and
        /// touching it does not, and why the track choice uses the same grace.
        /// </summary>
        void TrackGround(float dt)
        {
            bool justLeft = _contact.Tick(IsGroundedRaw, dt, airborneGraceSeconds);
            if (_contact.IsGrounded && _progress.AirHits > 0) _progress.Land();
            if (justLeft) _progress.LeaveGround();
        }

        /// <summary>Air-combo hang time: keep the player up while an air swing
        /// plays; see <see cref="AirHang"/>. Runs ahead of the hitstop return so a
        /// connecting air hit doesn't drop him mid-freeze.</summary>
        void HoldInAir(float dt)
        {
            if (_impulses == null) return;
            bool airSwing = IsAttacking && _current.Track == ComboTrack.Air;
            float hold = AirHang.HoldSeconds(airSwing, airSwing && _current.Step.HangInAir,
                                             IsGroundedRaw, !CanSwing, airHangReleaseSeconds, dt);
            if (hold > 0f) _impulses.SuspendGravity(hold);
        }

        bool IsGroundedRaw => _movement == null || _movement.IsGrounded;

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
            var track = _contact.Track;
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
            _grace.Cancel();
            _alreadyHit.Clear();
            // The sweep history is kept across a chain: the blade moves
            // continuously from one step into the next.

            // An impulse, once — see ICombatImpulses for why never per frame.
            if (step.LungeSpeed > 0f && _impulses != null)
            {
                Vector3 dir = Flat(transform.forward);
                float speed = step.LungeSpeed;
                if (lockOn != null && lockOn.IsLocked && homingRange > 0f)
                {
                    Vector3 to = lockOn.AimPoint - transform.position;
                    dir = HomingLunge.Direction(dir, to, homingRange, homingTurnRate, step.Duration);
                    to.y = 0f;
                    speed = HomingLunge.Speed(speed, to.magnitude - lockOn.TargetRadius, homingStopGap);
                }
                if (speed > 0f) _impulses.AddImpulse(dir * speed);
            }

            SwingStarted?.Invoke(placement);
        }

        void FinishChain()
        {
            var ended = _current;
            if (_timeline.IsRunning && (_timeline.Stop() & SwingSignal.BladeOff) != 0)
                BladeLiveChanged?.Invoke(false);
            _queued = false;
            _alreadyHit.Clear();
            _sweep.Reset();
            _progress.DropChain();
            _grace.Cancel();
            ChainEnded?.Invoke(ended);
        }

        // ------------------------------------------------------------ blade

        /// <summary>Record the blade every frame of a swing and, when
        /// <paramref name="test"/>, overlap-test what it swept. See
        /// <see cref="BladeSweep"/>.</summary>
        void SampleBlade(bool test)
        {
            if (bladeBase == null || bladeTip == null) return;
            int count = _sweep.Sample(bladeBase.position, bladeTip.position, test, _segments);
            for (int i = 0; i < count; i++) TestCapsule(_segments[i].from, _segments[i].to);
        }

        void TestCapsule(Vector3 a, Vector3 b)
        {
            int count = Physics.OverlapCapsuleNonAlloc(a, b, bladeRadius, _overlap, hittable,
                                                       QueryTriggerInteraction.Ignore);
            var hit = _current.Step.Hit;
            for (int i = 0; i < count; i++)
            {
                var col = _overlap[i];
                if (!TryResolveTarget(col, out var target)) continue;
                if (!MarkStruck(_alreadyHit, target)) continue;                   // once per swing

                Vector3 point = HitPoint.Nearest(col, (a + b) * 0.5f);
                Deal(target, col, point, hit, hit.PushAlong(transform.forward), _current, fromBurst: false);
            }
        }

        /// <summary>The damageable <paramref name="col"/> belongs to, if any and
        /// not ourselves. No side effects: resolving a target must not count as
        /// striking it — a ring checks reach first.</summary>
        bool TryResolveTarget(Collider col, out IDamageable target)
        {
            target = null;
            if (col == null || col.transform.IsChildOf(transform)) return false;
            target = col.GetComponentInParent<IDamageable>();
            return target != null;
        }

        static bool AlreadyStruck(HashSet<Object> struck, IDamageable target)
            => target is Object key && struck.Contains(key);

        /// <summary>Record <paramref name="target"/> as struck; false if it already
        /// was. A damageable that is not a Unity object cannot be tracked and is
        /// always allowed through.</summary>
        static bool MarkStruck(HashSet<Object> struck, IDamageable target)
            => !(target is Object key) || struck.Add(key);

        void Deal(IDamageable target, Collider col, Vector3 point, HitSettings hit, Vector3 push,
                  ResolvedStep placement, bool fromBurst)
        {
            var report = new MeleeHitReport(target, col, point, hit.Damage, placement, fromBurst);
            HitConnecting?.Invoke(report);
            target.TakeHit(hit.Damage, point, push, hit.IsLauncher);
            // Max, not sum: a ring sweeping three targets in one frame is one impact.
            _hitstopLeft = Mathf.Max(_hitstopLeft, hit.Hitstop);
            HitLanded?.Invoke(report);
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
                // Target first: the overlap returns map geometry too, and measuring
                // a non-convex level mesh is what flooded the console with
                // ClosestPoint warnings. Only a target's colliders get measured.
                if (!TryResolveTarget(col, out var target)) continue;
                if (AlreadyStruck(slot.Struck, target)) continue;
                Vector3 point = HitPoint.Nearest(col, slot.Centre);
                if (!burst.Reaches(slot.Centre, point, radius)) continue;   // edge not there yet
                MarkStruck(slot.Struck, target);
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
