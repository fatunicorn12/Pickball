using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// One falling apple.
    ///
    /// It integrates its own motion rather than using a Rigidbody, for the same
    /// reason BallController does: it has to obey the wells' inverse-square field
    /// exactly, and mixing that with PhysX's solver makes the trajectory a
    /// negotiation instead of a calculation.
    ///
    /// <b>Apples annihilate each other.</b> Two that meet mid-air both die and
    /// neither lands. Because the wells bend every apple toward the same points,
    /// the ground near a well is where collisions happen — so during the barrage
    /// the field that was phase one's hazard becomes phase two's cover, and the
    /// open ground that used to be safe is where the survivors land. That
    /// inversion is the whole point of the phase; the cancellation is not a
    /// physics nicety, it is the mechanic.
    ///
    /// <b>Because it is the mechanic, every contact test here is swept and the
    /// speed is bounded.</b> A point sample against the current position has an
    /// effective radius that shrinks as the frame time grows, so the cover the
    /// player is being asked to trust would quietly get worse on slower
    /// hardware — the one property a fairness-critical mechanic must not have.
    /// The integration runs in Update and every contact is resolved in
    /// LateUpdate; see <see cref="LateUpdate"/> for why the split is load-bearing
    /// rather than tidiness.
    /// </summary>
    public class Apple : MonoBehaviour
    {
        [SerializeField] private float baseGravity = -38f;
        [SerializeField] private float damage = 7f;
        [SerializeField] private float hitRadius = 0.55f;
        [SerializeField] private float cancelRadius = 0.75f;
        [SerializeField] private float knockback = 4.5f;

        [Tooltip("Ceiling on speed, m/s. The field is clamped at a well's core rim " +
                 "but not bounded over time, so an apple that falls through a core " +
                 "keeps gaining and a long orbit runs away. Must stay comfortably " +
                 "above free-fall impact speed — sqrt(2 * |baseGravity| * " +
                 "AppleBarrage.spawnHeight), about 37 m/s at the defaults — or " +
                 "ordinary falls get clipped. PROVISIONAL: picked for headroom, " +
                 "not for feel.")]
        [SerializeField] private float terminalSpeed = 50f;

        [Tooltip("Killed after this long regardless, so a stable orbit around a " +
                 "well cannot leave an apple circling forever.")]
        [SerializeField] private float maxLifetime = 12f;

        [SerializeField] private ParticleSystem splatEffect;
        [SerializeField] private ParticleSystem cancelEffect;

        /// <summary>Rough radius of the player's body, so the hit test is against a
        /// person rather than a point. Approximate on purpose — it only has to be
        /// close enough that a graze reads as a graze.</summary>
        const float PlayerBodyRadius = 0.45f;

        /// <summary>Height above the player's transform the hit test is centred on,
        /// roughly chest height.</summary>
        const float PlayerCentreHeight = 0.9f;

        /// <summary>Every apple in flight, for the pairwise cancellation test.</summary>
        public static readonly List<Apple> Airborne = new();

        /// <summary>Downward acceleration this apple falls under with no wells
        /// acting, m/s². Public so <see cref="AppleBarrage"/>'s landing telegraph
        /// solves the same fall the apple will actually perform instead of keeping
        /// a second copy of the number that nothing would catch drifting.</summary>
        public float FallAcceleration => Mathf.Abs(baseGravity);

        Vector3 _velocity;
        float _age;
        ChibiLocomotion _player;
        Health _playerHealth;
        AppleBarrage _owner;
        bool _dead;

        // The segment this apple covered during the current frame. Every contact
        // test runs against this rather than against the end point.
        Vector3 _sweepFrom, _sweepTo;

        // The player's centre as of the previous frame's resolution, giving the
        // player a matching segment to sweep against.
        Vector3 _playerPrevious;
        bool _hasPlayerSample;

        void OnEnable()
        {
            Airborne.Add(this);
            // Degenerate but valid, so an apple is testable from the moment it
            // exists even if something resolves before its first Update.
            _sweepFrom = _sweepTo = transform.position;
        }

        void OnDisable() => Airborne.Remove(this);

        public void Launch(Vector3 initialVelocity, AppleBarrage owner,
                           ChibiLocomotion player, Health playerHealth)
        {
            _velocity = initialVelocity;
            _owner = owner;
            _player = player;
            _playerHealth = playerHealth;
        }

        void Update()
        {
            if (_dead) return;

            float dt = Time.deltaTime;
            _age += dt;
            if (_age >= maxLifetime) { Cancel(); return; }

            Vector3 from = transform.position;

            // Straight gravity plus the resultant of every live well. One shared
            // equation with the player's, so an apple's arc is a readable preview
            // of what the field will do to you.
            Vector3 acceleration = Vector3.up * baseGravity + GravityWell.FieldAt(from);
            _velocity += acceleration * dt;

            // Terminal speed, for the same reason ChibiLocomotion caps its field
            // velocity: the core clamp bounds the force, not the integral of it.
            // An apple that threads a core repeatedly can pass 100 m/s, at which
            // point one frame is longer than the entire cancel window and it goes
            // through the floor, the player and every other apple before
            // maxLifetime gets a chance to save it.
            _velocity = Vector3.ClampMagnitude(_velocity, terminalSpeed);

            transform.position = from + _velocity * dt;

            _sweepFrom = from;
            _sweepTo = transform.position;

            // Spin, purely so it reads as an object rather than a projectile.
            transform.Rotate(Vector3.right, 220f * dt, Space.Self);
        }

        /// <summary>
        /// Contact resolution, deliberately a phase later than the integration.
        ///
        /// The swept cancellation test compares two apples' segments, and that is
        /// only meaningful if both segments describe the same frame. Resolving
        /// inside Update cannot promise that: script execution order across
        /// instances of one component is arbitrary, so an apple that ticks first
        /// would be testing its frame-N segment against its neighbour's frame-N−1
        /// one — an error of a whole frame of travel, which at the closing speeds
        /// the wells produce is the same order as the cancel window itself. Every
        /// Update completes before any LateUpdate, so by here every live apple has
        /// exactly one frame's segment recorded.
        ///
        /// Order within the method matters too: cancelling is tested before the
        /// player, so a pair that is about to annihilate can never splat on the
        /// player on its way out. That is the phase's promise — standing where the
        /// apples converge is cover.
        /// </summary>
        void LateUpdate()
        {
            if (_dead) return;

            if (TryCancelAgainstOtherApple()) return;
            if (TryHitPlayer()) return;
            TryHitGround();
        }

        /// <summary>
        /// Swept pairwise cancellation.
        ///
        /// Both apples move in a straight line within a frame, so their separation
        /// is linear in t and minimising it over t ∈ [0,1] is a clamped dot
        /// product rather than a segment-to-segment solve. That makes the test
        /// exact instead of merely conservative: two apples that pass through the
        /// same metre of air at different moments do not cancel, which a
        /// segment-to-segment distance would wrongly say they did.
        ///
        /// Every apple scans the whole list rather than the higher instance ID
        /// owning the pair, and that is a fix rather than an oversight: whichever
        /// apple resolves first now kills both before reaching its own player
        /// test. With the ownership rule, a pair whose lower-ID member resolved
        /// first went untested and could land on the player in the frame it was
        /// supposed to be annihilated in. The pair cannot be evaluated twice
        /// because CancelAt marks both dead immediately.
        /// </summary>
        bool TryCancelAgainstOtherApple()
        {
            for (int i = 0; i < Airborne.Count; i++)
            {
                Apple other = Airborne[i];
                if (other == this || other == null || other._dead) continue;

                float r = cancelRadius + other.cancelRadius;
                float t = ClosestApproach(_sweepFrom, _sweepTo,
                                          other._sweepFrom, other._sweepTo);
                Vector3 mine = Vector3.LerpUnclamped(_sweepFrom, _sweepTo, t);
                Vector3 theirs = Vector3.LerpUnclamped(other._sweepFrom, other._sweepTo, t);
                if ((theirs - mine).sqrMagnitude > r * r) continue;

                // The point they actually met at, not where they ended the frame.
                Vector3 meeting = (mine + theirs) * 0.5f;
                other.CancelAt(meeting);
                CancelAt(meeting);
                return true;
            }
            return false;
        }

        bool TryHitPlayer()
        {
            if (_player == null) return false;

            // The player's own segment for this frame. Sampled here, in LateUpdate,
            // so it is bracketed by the same frame boundary as the apple's sweep
            // however ChibiLocomotion and this component happen to be ordered.
            Vector3 playerTo = _player.transform.position + Vector3.up * PlayerCentreHeight;
            Vector3 playerFrom = _hasPlayerSample ? _playerPrevious : playerTo;
            _playerPrevious = playerTo;
            _hasPlayerSample = true;

            float r = hitRadius + PlayerBodyRadius;
            float t = ClosestApproach(_sweepFrom, _sweepTo, playerFrom, playerTo);
            Vector3 mine = Vector3.LerpUnclamped(_sweepFrom, _sweepTo, t);
            Vector3 theirs = Vector3.LerpUnclamped(playerFrom, playerTo, t);
            if ((theirs - mine).sqrMagnitude > r * r) return false;

            // Report the contact where it happened. At terminal speed a 30 fps
            // frame is a metre of travel, and both the splat and the damage
            // position read as a miss if they play at the far end of it.
            transform.position = mine;

            if (_playerHealth != null)
                _playerHealth.Damage(damage, mine);

            Vector3 push = _velocity.normalized * knockback;
            push.y = Mathf.Abs(push.y) * 0.35f;
            _player.AddMomentum(push);   // impulse, once — correct use of that API

            Splat();
            return true;
        }

        /// <summary>Splat at the point the fall crossed the floor rather than
        /// wherever the frame happened to end. The path within a frame is a
        /// straight segment, so the end-point test never misses the floor — but at
        /// terminal speed it plays the splat up to a metre underground.</summary>
        void TryHitGround()
        {
            float groundY = _owner != null ? _owner.GroundY : 0f;
            if (_sweepFrom.y > groundY && _sweepTo.y > groundY) return;

            float drop = _sweepFrom.y - _sweepTo.y;
            float t = drop > 1e-5f
                ? Mathf.Clamp01((_sweepFrom.y - groundY) / drop)
                : 0f;
            transform.position = Vector3.LerpUnclamped(_sweepFrom, _sweepTo, t);
            Splat();
        }

        /// <summary>
        /// The moment in the frame, as t ∈ [0,1], at which two points travelling
        /// linearly across it are closest.
        ///
        /// Their separation d(t) = (a0−b0) + t·((a1−a0) − (b1−b0)) is itself
        /// linear, so this is the projection of the starting separation onto the
        /// relative displacement, clamped to the frame. No square roots, and it
        /// costs the same whether the pair closes at 2 m/s or 200.
        /// </summary>
        static float ClosestApproach(Vector3 aFrom, Vector3 aTo,
                                     Vector3 bFrom, Vector3 bTo)
        {
            Vector3 separation = aFrom - bFrom;
            Vector3 closing = (aTo - aFrom) - (bTo - bFrom);
            float denominator = closing.sqrMagnitude;
            if (denominator < 1e-8f) return 0f;   // no relative motion this frame
            return Mathf.Clamp01(-Vector3.Dot(separation, closing) / denominator);
        }

        void Splat()
        {
            Emit(splatEffect);
            Kill();
        }

        void CancelAt(Vector3 point)
        {
            if (_dead) return;
            transform.position = point;
            Emit(cancelEffect != null ? cancelEffect : splatEffect);
            Kill();
        }

        public void Cancel() => CancelAt(transform.position);

        void Emit(ParticleSystem prefabOrInstance)
        {
            if (prefabOrInstance == null) return;
            var vfx = Instantiate(prefabOrInstance, transform.position, Quaternion.identity);
            vfx.Play();
            Destroy(vfx.gameObject, 2.5f);
        }

        void Kill()
        {
            _dead = true;
            Destroy(gameObject);
        }
    }
}
