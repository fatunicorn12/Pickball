using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Newton's calculus, pointed at you.
    ///
    /// The beam does not price contact — it prices the fluxion of your position:
    /// the rate at which it is changing, i.e. your speed. Damage per second is
    /// proportional to |dx/dt|. Sprint while it is on you and it bites hard; walk
    /// and it nibbles; stand perfectly still and it does literally nothing. It is
    /// the one moment in the fight where moving is the wrong answer.
    ///
    /// <b>It used to price acceleration (|dv/dt|), which got the design backwards.</b>
    /// A player running at a steady sprint has zero acceleration, so the beam was
    /// free for anyone who simply kept running — the opposite of "standing still
    /// is safer than running". Only starting and stopping cost anything, which
    /// punished exactly the player who tried to obey the tell.
    ///
    /// Two things make it stick rather than being a suggestion:
    ///
    /// 1. <b>Its damage is unrecoverable.</b> Everything else in the fight goes
    ///    into the regain pool and can be won back by pressing. Beam damage is
    ///    taken off the bar permanently, on contact.
    /// 2. <b>It telegraphs, then tracks.</b> A charge window with a widening
    ///    line gives you time to stop, after which it follows you, so running is
    ///    not an escape — it is just a larger bill.
    ///
    /// <b>Field motion is excluded by default.</b> Being dragged by a gravity well
    /// is not a choice, so it is subtracted using
    /// <see cref="ChibiLocomotion.AppliedFieldVelocity"/> — the field velocity the
    /// controller ACHIEVED, which leaves exactly the character's own motion and is
    /// exactly zero when he requests none. Flip <see cref="chargeForFieldMotion"/>
    /// for the crueller reading.
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
        [Tooltip("Damage per second per m/s of your own speed. A 4.84 m/s sprint " +
                 "at 3.0 costs about 13.5 HP/s, permanently.")]
        [SerializeField] private float damagePerSpeed = 3f;

        [Tooltip("Speed below this is free. Covers controller jitter and the tail " +
                 "of a deceleration so standing still really is zero.")]
        [SerializeField] private float speedDeadzone = 0.35f;

        [Tooltip("Ceiling on damage per second, however fast you move — stops a " +
                 "dash (14 m/s) through the beam deleting the bar.")]
        [SerializeField] private float maxDamagePerSecond = 30f;

        [Tooltip("Time constant, seconds, the measured speed is smoothed over. Only " +
                 "absorbs single-frame controller spikes; speed is not spiky the " +
                 "way a derivative of it is.")]
        [SerializeField] private float speedSmoothing = 0.06f;

        [Tooltip("Include motion imposed by gravity wells. Off by default — " +
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

        [Tooltip("Metres per second the beam texture streams away from Newton. " +
                 "Slow while charging, full speed once it is live, so the change " +
                 "of state is visible in the motion as well as the width.")]
        [SerializeField] private float scrollSpeed = 14f;

        public bool IsFiring => _state == State.Firing;
        public bool IsActive => _state != State.Idle;

        enum State { Idle, Charging, Firing }
        State _state = State.Idle;

        float _stateTime;
        Vector3 _aim = Vector3.forward;

        ChibiLocomotion _player;
        Health _playerHealth;
        float _speed;
        float _scroll;
        float _impactBaseSize = 1f;
        MaterialPropertyBlock _block;
        static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");

        void Awake()
        {
            if (muzzle == null) muzzle = transform;
            _block = new MaterialPropertyBlock();
            if (impact != null) _impactBaseSize = impact.main.startSizeMultiplier;
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
                _aim = (_player.transform.position - muzzle.position).normalized;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            MeasureSpeed(dt);

            if (_state == State.Idle) return;
            _stateTime += dt;

            TrackPlayer(dt);

            if (_state == State.Charging)
            {
                // The tell: the line widens and its flicker steadies as the charge
                // completes, so "how long have I got" is readable off the line.
                float progress = chargeTime > 0f ? Mathf.Clamp01(_stateTime / chargeTime) : 1f;
                float flicker = Mathf.Lerp(0.45f, 1f, progress)
                              * (0.8f + 0.2f * Mathf.Sin(_stateTime * 50f));
                Color tell = coolColour;
                tell.a *= flicker;
                DrawBeam(chargeWidth * Mathf.Lerp(0.5f, 1.6f, progress), tell, 0f);
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

        /// <summary>The fluxion: how fast the player is going under their own
        /// power, lightly low-passed. Sampled from the CharacterController's
        /// measured movement, so walking into a wall is not motion however hard
        /// the stick is pushed.</summary>
        void MeasureSpeed(float dt)
        {
            if (_player == null) return;

            float raw = MeasuredVelocity().magnitude;
            _speed = speedSmoothing > 0f
                ? _speed + (raw - _speed) * (1f - Mathf.Exp(-dt / speedSmoothing))
                : raw;
        }

        /// <summary>Velocity as the beam prices it: what the character actually
        /// did, minus what the force fields actually did to him.</summary>
        Vector3 MeasuredVelocity()
        {
            if (_player == null) return Vector3.zero;
            Vector3 v = _player.Velocity;
            if (!chargeForFieldMotion) v -= _player.AppliedFieldVelocity;
            return v;
        }

        float DamagePerSecond()
            => DamageRate(_speed, speedDeadzone, damagePerSpeed, maxDamagePerSecond);

        /// <summary>
        /// The curve: a free deadzone, then linear in the excess, then a ceiling.
        /// Pure so it can be tested without a live character.
        /// </summary>
        public static float DamageRate(float speed, float deadzone, float perSpeed, float ceiling)
        {
            float over = speed - deadzone;
            if (over <= 0f) return 0f;
            return Mathf.Min(over * perSpeed, ceiling);
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
            line.endColor = new Color(colour.r, colour.g, colour.b, colour.a * 0.35f);

            // Streams outward from the muzzle. A property block, not the shared
            // material, so play mode never writes the offset into the asset.
            _scroll += Time.deltaTime * scrollSpeed * (_state == State.Firing ? 1f : 0.2f)
                       * line.textureScale.x;
            _scroll %= 1f;
            line.GetPropertyBlock(_block);
            _block.SetVector(BaseMapStId, new Vector4(1f, 1f, -_scroll, 0f));
            line.SetPropertyBlock(_block);

            if (impact == null) return;
            if (_state == State.Firing && HitsPlayer(out Vector3 point))
            {
                impact.transform.position = point;
                var main = impact.main;
                // Relative to the effect's authored size. Written as an absolute,
                // it turned a spark effect into a 1.4 m white blob.
                main.startSizeMultiplier = _impactBaseSize * Mathf.Lerp(0.5f, 1.8f, heat);
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
        public float CurrentSpeed => _speed;
        public float CurrentDamagePerSecond => _state == State.Firing ? DamagePerSecond() : 0f;
    }
}
