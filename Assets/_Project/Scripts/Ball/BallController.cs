using System;
using UnityEngine;

namespace Pickleball
{
    /// <summary>
    /// Rigidbody ball with exaggerated, floaty Wii-style arc physics.
    /// Gravity is applied manually (the only FixedUpdate in the project) so the ball can
    /// hang at the peak and then drop snappily. Emits ScriptableObject events on every
    /// bounce and when it dies (second bounce, out-of-bounds, or net contact).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BallController : MonoBehaviour
    {
        [Header("Config")]
        [SerializeField] private BallConfig config;

        [Header("Dependencies")]
        [SerializeField] private Court court;

        [Header("Event Channels")]
        [Tooltip("Raised on every surface contact (carries zone/side/in-bounds).")]
        [SerializeField] private BallContactEventChannelSO bounceChannel;
        [Tooltip("Raised once when the ball becomes Dead (point-ending contact).")]
        [SerializeField] private BallContactEventChannelSO ballDeadChannel;

        private Rigidbody _rb;
        private SphereCollider _collider;
        private int _bounceCount;
        private Vector3 _preVelocity; // velocity entering the current physics step, used for the bounce

        public BallState State { get; private set; } = BallState.Idle;
        public CourtSide LastHitter { get; private set; } = CourtSide.Player;
        public Rigidbody Body => _rb;
        public BallConfig Config => config;

        /// <summary>Fired on every state change (optional convenience hook).</summary>
        public event Action<BallState> StateChanged;

        // ---- Setup -------------------------------------------------------------

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _collider = GetComponent<SphereCollider>();
            if (_collider == null) _collider = gameObject.AddComponent<SphereCollider>();

            ApplyConfig();
            SetState(BallState.Idle);
        }

        private void ApplyConfig()
        {
            if (config == null)
            {
                Debug.LogError("[BallController] No BallConfig assigned.", this);
                return;
            }

            _rb.useGravity = false;            // we integrate gravity ourselves
            _rb.mass = config.mass;
            _rb.linearDamping = config.linearDamping;
            _rb.angularDamping = config.angularDamping;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            _collider.radius = 0.5f;           // unit sphere; visual scale carries the size
            transform.localScale = Vector3.one * (config.radius * 2f);

            // Low-friction, ZERO-restitution material: the bounce is done manually in OnCollisionEnter
            // because PhysX ignores restitution below Physics.bounceThreshold (slow floaty balls
            // would never bounce). This keeps the bounce fully controllable and arcade-snappy.
            _collider.sharedMaterial = PrimitiveFactory.CreatePhysicsMaterial(bounciness: 0f, friction: 0.2f);
        }

        // ---- The one physics loop ---------------------------------------------

        private void FixedUpdate()
        {
            if (config == null || State == BallState.Idle) return; // parked ball is inert

            Vector3 v = _rb.linearVelocity;
            _preVelocity = v; // remember it for any bounce resolved later this step

            // Float at the apex, fall snappily afterwards.
            float scale = Mathf.Abs(v.y) < config.peakVelocityThreshold
                ? config.peakGravityScale
                : config.gravityScale;

            Vector3 accel = Physics.gravity * scale;
            if (v.y < 0f) accel += Vector3.down * config.descendForceBoost;

            _rb.AddForce(accel, ForceMode.Acceleration);

            if (transform.position.y < config.killFloorY)
            {
                Kill(new BallContactInfo(transform.position, court ? court.SideOf(transform.position) : LastHitter,
                    CourtZone.OutOfBounds, false));
            }
        }

        // ---- Public control API ------------------------------------------------

        /// <summary>Parks the ball at a position, motionless and inert (e.g. while waiting to serve).</summary>
        public void ResetTo(Vector3 position)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            transform.position = position;
            _rb.WakeUp();
            _bounceCount = 0;
            SetState(BallState.Idle);
        }

        /// <summary>Strikes the ball with an explicit velocity. Resets the bounce count for the new shot.</summary>
        public void Hit(Vector3 velocity, CourtSide hitter)
        {
            LastHitter = hitter;
            _bounceCount = 0;
            _rb.linearVelocity = velocity;
            _rb.angularVelocity = Vector3.zero;
            SetState(BallState.InFlight);
        }

        // ---- Collisions --------------------------------------------------------

        private void OnCollisionEnter(Collision collision)
        {
            if (State == BallState.Dead || State == BallState.Idle) return;
            if (config == null) return;

            Vector3 contactPoint = collision.GetContact(0).point;
            Vector3 normal = collision.GetContact(0).normal;

            // A mostly-vertical normal means a floor/ground bounce; otherwise it's the net or a post.
            bool floorBounce = normal.y > 0.5f;

            if (!floorBounce)
            {
                // Net / post contact ends the rally as a fault against the last hitter.
                var netInfo = new BallContactInfo(contactPoint, LastHitter, CourtZone.Net, false);
                bounceChannel?.Raise(netInfo);
                Kill(netInfo);
                return;
            }

            CourtZone zone = court != null ? court.ResolveZone(contactPoint) : CourtZone.CourtLeft;
            CourtSide side = court != null ? court.SideOf(contactPoint) : LastHitter;
            bool inBounds = zone != CourtZone.OutOfBounds;

            _bounceCount++;
            var info = new BallContactInfo(contactPoint, side, zone, inBounds);
            bounceChannel?.Raise(info);

            SetState(BallState.Bounced);

            // Out of bounds, or too many bounces -> the ball is dead and the point is over.
            if (!inBounds || _bounceCount >= config.bouncesBeforeDead)
            {
                Kill(info);
                return; // let it settle naturally; no arcade pop on the point-ending bounce
            }

            ApplyManualBounce();
        }

        /// <summary>
        /// Reflects the ball off the floor using the pre-impact velocity. Done in code (not via a
        /// PhysicsMaterial) so slow floaty balls still bounce and the feel stays tunable.
        /// </summary>
        private void ApplyManualBounce()
        {
            Vector3 v = _preVelocity;
            float up = Mathf.Max(Mathf.Abs(v.y) * config.bounciness, config.minBounceUpSpeed);
            _rb.linearVelocity = new Vector3(
                v.x * config.bounceHorizontalKeep,
                up,
                v.z * config.bounceHorizontalKeep);
        }

        private void Kill(BallContactInfo info)
        {
            if (State == BallState.Dead) return;
            SetState(BallState.Dead);
            ballDeadChannel?.Raise(info);
        }

        private void SetState(BallState next)
        {
            if (State == next) return;
            State = next;
            StateChanged?.Invoke(next);
        }

        // ---- Trajectory prediction (used by the AI) ----------------------------

        /// <summary>
        /// Simulates the current trajectory forward using the same custom-gravity model and
        /// returns where the ball will next cross <paramref name="groundY"/> on the way down.
        /// Returns the current position if no descending crossing is found within the horizon.
        /// </summary>
        public Vector3 PredictLanding(float groundY = 0f, float horizonSeconds = 6f)
        {
            if (config == null) return transform.position;

            float dt = Time.fixedDeltaTime;
            int maxSteps = Mathf.CeilToInt(horizonSeconds / dt);
            Vector3 pos = transform.position;
            Vector3 vel = _rb.linearVelocity;

            for (int i = 0; i < maxSteps; i++)
            {
                float scale = Mathf.Abs(vel.y) < config.peakVelocityThreshold
                    ? config.peakGravityScale
                    : config.gravityScale;
                Vector3 accel = Physics.gravity * scale;
                if (vel.y < 0f) accel += Vector3.down * config.descendForceBoost;

                vel += accel * dt;
                vel *= Mathf.Clamp01(1f - config.linearDamping * dt); // approximate Rigidbody damping
                Vector3 next = pos + vel * dt;

                if (vel.y < 0f && next.y <= groundY && pos.y > groundY)
                {
                    float t = Mathf.InverseLerp(pos.y, next.y, groundY);
                    return Vector3.Lerp(pos, next, t);
                }
                pos = next;
            }
            return pos;
        }
    }
}
