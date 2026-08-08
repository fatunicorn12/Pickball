using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Newton's calculus, pointed at you.
    ///
    /// The beam does not price contact — it prices the derivative of your
    /// velocity. Damage per second is proportional to |dv/dt|: sprint, dash or
    /// jump while it is on you and it bites; stand perfectly still and it does
    /// literally nothing. It is the one moment in the fight where aggression is
    /// the wrong answer.
    ///
    /// Two things make that stick rather than being a suggestion:
    ///
    /// 1. <b>Its damage is unrecoverable.</b> Everything else in the fight goes
    ///    into the regain pool and can be won back by pressing. Beam damage is
    ///    taken off the bar permanently, on contact. There is no aggressive
    ///    recovery line out of it.
    /// 2. <b>It telegraphs, then tracks.</b> A charge window with a widening
    ///    line gives you time to stop, after which it follows you, so running is
    ///    not an escape — it is just a larger bill.
    ///
    /// <b>Field motion is excluded by default.</b> A gravity well accelerates the
    /// player continuously, so measuring raw dv/dt would make standing still
    /// impossible whenever a well is up and turn the beam into unavoidable
    /// permanent chip. Subtracting the field's contribution means the beam
    /// charges you for what you chose to do. Flip <see cref="chargeForFieldMotion"/>
    /// if you want the crueller reading, where being dragged is also your problem.
    ///
    /// That subtraction reads <see cref="ChibiLocomotion.AppliedFieldVelocity"/> —
    /// the field velocity the character controller ACHIEVED — and not the velocity
    /// the field requested. Mixing the two is a category error that used to bill a
    /// motionless player about 6.6 permanent HP per second: achieved motion is
    /// measured after collision resolution, requested motion before it, and the
    /// difference is a phantom velocity whenever anything blocks the pull. Pinned
    /// against a wall, held in a well core (where the well locks your movement, so
    /// you are motionless BY DESIGN), or simply stood on a floor absorbing a
    /// vertical pull — all three billed you for standing perfectly still, which is
    /// the one thing this attack promises is free.
    ///
    /// <b>The bill is frame-rate independent.</b> Damage integrates |dv/dt| over
    /// time, which is frame-rate independent in the open — a fixed velocity step
    /// costs the same however it is sliced — but stops being so the moment
    /// <see cref="maxDamagePerSecond"/> saturates, because the clamped bill is
    /// then rate × dt for a fixed number of frames. Raw, a single jump cost 0.29 HP
    /// at 144 fps and 1.40 HP at 30 fps: 4.8× more permanent damage for owning a
    /// slower phone. <see cref="accelerationSmoothing"/> is the fix; see
    /// <see cref="MeasureAcceleration"/>.
    /// </summary>
    public class FluxionBeam : MonoBehaviour
    {
        [Header("Timing")]
        [Tooltip("Warning window before the beam does damage. The line is drawn " +
                 "and tracking during it, so it doubles as the 'stop now' tell.")]
        [SerializeField] private float chargeTime = 1.1f;

        [Tooltip("How long the beam stays lethal once charged.")]
        [SerializeField] private float fireTime = 2.4f;

        [Header("Damage")]
        [Tooltip("Damage per second at one unit of acceleration. Actual damage is " +
                 "this times measured |dv/dt|, so it is a rate, not a total.")]
        [SerializeField] private float damagePerAcceleration = 0.55f;

        [Tooltip("Acceleration below this is free. Covers controller jitter, step " +
                 "offsets and landing settle so standing still really is zero.")]
        [SerializeField] private float deadzone = 2.5f;

        [Tooltip("Ceiling on damage per second, however violently you move. A " +
                 "backstop now rather than the main shaping force — the smoothing " +
                 "below already bounds how high a single velocity step can drive " +
                 "the reading. PROVISIONAL, with the rest of the fight's numbers.")]
        [SerializeField] private float maxDamagePerSecond = 42f;

        [Tooltip("Time constant, in seconds, that the measured |dv/dt| is smoothed " +
                 "over. This is what makes the bill frame-rate independent: a " +
                 "one-frame velocity step is spread over a fixed span of real time " +
                 "instead of a single frame, so its peak no longer depends on how " +
                 "long that frame was. It also absorbs controller jitter far better " +
                 "than the deadzone alone. Must stay well above a worst-case frame " +
                 "time (0.033s at 30fps) for the smoothing to mean anything. " +
                 "PROVISIONAL — see MeasureAcceleration for the arithmetic.")]
        [SerializeField] private float accelerationSmoothing = 0.12f;

        [Tooltip("Include acceleration imposed by gravity wells. Off by default — " +
                 "see the class comment.")]
        [SerializeField] private bool chargeForFieldMotion;

        [Header("Aim")]
        [SerializeField] private Transform muzzle;

        [Tooltip("Degrees per second the beam can swing while tracking. Finite, so " +
                 "a hard lateral break genuinely buys you a moment off-beam.")]
        [SerializeField] private float trackDegreesPerSecond = 110f;

        [SerializeField] private float range = 40f;
        [SerializeField] private float beamRadius = 0.55f;

        [Header("Presentation")]
        [SerializeField] private LineRenderer line;
        [SerializeField] private ParticleSystem impact;
        [SerializeField] private Color coolColour = new(0.25f, 0.6f, 1f, 1f);
        [SerializeField] private Color hotColour = new(1f, 1f, 1f, 1f);
        [SerializeField] private float chargeWidth = 0.06f;
        [SerializeField] private float fireWidth = 0.4f;

        public bool IsFiring => _state == State.Firing;
        public bool IsActive => _state != State.Idle;

        enum State { Idle, Charging, Firing }
        State _state = State.Idle;

        float _stateTime;
        Vector3 _aim = Vector3.forward;

        ChibiLocomotion _player;
        Health _playerHealth;
        Vector3 _previousVelocity;
        float _acceleration;

        void Awake()
        {
            if (muzzle == null) muzzle = transform;
            if (line != null)
            {
                line.positionCount = 2;
                line.enabled = false;
                line.useWorldSpace = true;
            }
        }

        void Start()
        {
            _player = FindFirstObjectByType<ChibiLocomotion>();
            if (_player != null)
            {
                _playerHealth = _player.GetComponent<Health>();
                _previousVelocity = MeasuredVelocity();
                _aim = (_player.transform.position - muzzle.position).normalized;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            MeasureAcceleration(dt);

            if (_state == State.Idle) return;
            _stateTime += dt;

            TrackPlayer(dt);

            if (_state == State.Charging)
            {
                DrawBeam(chargeWidth, coolColour, 0f);
                if (_stateTime >= chargeTime) EnterState(State.Firing);
                return;
            }

            // Firing.
            float dps = DamagePerSecond();
            float heat = maxDamagePerSecond > 0f ? Mathf.Clamp01(dps / maxDamagePerSecond) : 0f;
            DrawBeam(Mathf.Lerp(chargeWidth, fireWidth, 0.35f + heat * 0.65f),
                     Color.Lerp(coolColour, hotColour, heat), heat);

            if (dps > 0f && _playerHealth != null && HitsPlayer(out Vector3 point))
            {
                // recoverable: false — this is the permanent one.
                _playerHealth.Damage(dps * dt, point, recoverable: false);
            }

            if (_stateTime >= fireTime) Stop();
        }

        /// <summary>
        /// The derivative. Velocity is sampled from the CharacterController's own
        /// measured movement, so it reflects what actually happened — walking into
        /// a wall is not motion, however hard the stick is pushed.
        ///
        /// <b>The raw derivative is smoothed, and that is a correctness fix rather
        /// than a feel one.</b> Damage is rate × dt, and the rate is capped, so the
        /// instant <see cref="maxDamagePerSecond"/> saturates the bill collapses to
        /// (ceiling × dt) for a fixed number of frames — proportional to frame
        /// time. A jump is one instantaneous velocity step of 5.55 m/s, which at
        /// 144 fps reads as 800 m/s² for a single 6.9 ms frame and at 30 fps as
        /// 167 m/s² for a single 33 ms frame. Both saturate; the second is billed
        /// for five times as long. Measured: 0.29 HP at 144 fps against 1.40 HP at
        /// 30 fps, permanent and unrecoverable, purely as a function of hardware.
        ///
        /// A first-order low-pass fixes it at the source instead of patching the
        /// bill. The discrete filter aₙ₊₁ = aₙ + (raw − aₙ)·α with
        /// α = 1 − e^(−dt/τ) preserves ∑a·dt EXACTLY — for a step of Δv the sum is
        /// a₀·dt/α = Δv at any dt — while turning the one-frame spike into a decay
        /// over τ whose peak is Δv/τ, a number with no dt in it. So the reading a
        /// discrete event produces, and therefore whether it saturates the ceiling
        /// at all, stops depending on the frame rate. With τ = 0.12 a jump peaks at
        /// 46 m/s², comfortably under the 79 m/s² where the ceiling starts to bite,
        /// and costs 2.406 HP at 30 fps and 2.406 HP at 144 fps. A dash still
        /// saturates — it should — and costs 6.479 against 6.445, a 0.5% spread
        /// against the 380% it used to be.
        ///
        /// Note the same structural move as the gravity field's damping fix: a hard
        /// clamp that produced a frame-rate artefact replaced by a time constant
        /// that bounds the same quantity smoothly. The ceiling stays as a backstop.
        /// </summary>
        void MeasureAcceleration(float dt)
        {
            if (_player == null) return;

            Vector3 v = MeasuredVelocity();
            float raw = (v - _previousVelocity).magnitude / dt;
            _previousVelocity = v;

            if (accelerationSmoothing <= 0f)
            {
                // Unsmoothed. Restores the frame-rate-dependent bill described
                // above; only useful for isolating the filter while tuning.
                _acceleration = raw;
                return;
            }

            _acceleration += (raw - _acceleration)
                             * (1f - Mathf.Exp(-dt / accelerationSmoothing));
        }

        /// <summary>
        /// Velocity as the beam prices it: what the character actually did, minus
        /// what the force fields actually did to him.
        ///
        /// <see cref="ChibiLocomotion.AppliedFieldVelocity"/> is the ACHIEVED field
        /// contribution and is contracted so that this subtraction leaves exactly
        /// the character's own motion on every path — exactly zero when he is
        /// asking for none, whatever the field is doing and whatever the geometry
        /// is absorbing. Subtracting the field's REQUESTED velocity instead is what
        /// used to bill a motionless player; see the class comment.
        /// </summary>
        Vector3 MeasuredVelocity()
        {
            if (_player == null) return Vector3.zero;
            Vector3 v = _player.Velocity;
            if (!chargeForFieldMotion) v -= _player.AppliedFieldVelocity;
            return v;
        }

        /// <summary>
        /// The curve: a free deadzone, then linear in the excess, then a ceiling.
        ///
        /// The ceiling is deliberately left in even though
        /// <see cref="accelerationSmoothing"/> now bounds how high a single event
        /// can drive the reading. Frame-rate independence is established upstream,
        /// on the measurement, so clamping here no longer distorts the integral in
        /// the ordinary case — it is a guard against a tuning combination or a
        /// physics glitch producing a rate that deletes the bar, which permanent
        /// damage makes unrecoverable by design.
        /// </summary>
        float DamagePerSecond()
        {
            float over = _acceleration - deadzone;
            if (over <= 0f) return 0f;
            return Mathf.Min(over * damagePerAcceleration, maxDamagePerSecond);
        }

        void TrackPlayer(float dt)
        {
            if (_player == null) return;
            Vector3 wanted = _player.transform.position + Vector3.up * 0.9f - muzzle.position;
            if (wanted.sqrMagnitude < 0.0001f) return;
            _aim = Vector3.RotateTowards(_aim, wanted.normalized,
                                         trackDegreesPerSecond * Mathf.Deg2Rad * dt,
                                         1f).normalized;
        }

        bool HitsPlayer(out Vector3 point)
        {
            point = Vector3.zero;
            if (_player == null) return false;

            // Distance from the player to the beam's infinite line, clamped to the
            // segment. Cheaper and steadier than a spherecast, and the beam is a
            // pure ray by nature — nothing should occlude Newton's mathematics.
            Vector3 origin = muzzle.position;
            Vector3 target = _player.transform.position + Vector3.up * 0.9f;
            Vector3 toTarget = target - origin;

            float along = Mathf.Clamp(Vector3.Dot(toTarget, _aim), 0f, range);
            Vector3 nearest = origin + _aim * along;
            if ((target - nearest).sqrMagnitude > beamRadius * beamRadius) return false;

            point = nearest;
            return true;
        }

        void DrawBeam(float width, Color colour, float heat)
        {
            if (line == null) return;
            Vector3 origin = muzzle.position;
            Vector3 end = origin + _aim * range;

            line.enabled = true;
            line.SetPosition(0, origin);
            line.SetPosition(1, end);
            line.startWidth = width;
            line.endWidth = width * 0.75f;
            line.startColor = colour;
            line.endColor = new Color(colour.r, colour.g, colour.b, 0.35f);

            if (impact == null) return;
            if (_state == State.Firing && HitsPlayer(out Vector3 point))
            {
                impact.transform.position = point;
                var main = impact.main;
                main.startSizeMultiplier = Mathf.Lerp(0.3f, 1.4f, heat);
                main.startColor = colour;
                if (!impact.isPlaying) impact.Play();
            }
            else if (impact.isPlaying)
            {
                impact.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        public void Fire()
        {
            if (_state != State.Idle) return;
            EnterState(State.Charging);
        }

        public void Stop()
        {
            _state = State.Idle;
            _stateTime = 0f;
            if (line != null) line.enabled = false;
            if (impact != null && impact.isPlaying)
                impact.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void EnterState(State next)
        {
            _state = next;
            _stateTime = 0f;
        }

        /// <summary>Live reading, for a HUD that wants to tell the player exactly
        /// why they are being hurt.</summary>
        public float CurrentAcceleration => _acceleration;
        public float CurrentDamagePerSecond => _state == State.Firing ? DamagePerSecond() : 0f;
    }
}
