using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// One orbiter of the ellipse volley: a body on a Kepler orbit with her at a
    /// focus. With the current tuning (e = 0) the orbit is a circle and it moves
    /// at one steady speed; with e &gt; 0 it is fast at periapsis and slow at
    /// apoapsis. Its orbit and lifetime run on its own clock, so freezing her
    /// leaves it circling and a Freeze Zone pins it in place (multiplier 0 simply
    /// stops time; nothing divides by it).
    ///
    /// Contact is a swept sphere (last frame's position to this one) against the
    /// player's capsule, so a periapsis whip cannot tunnel through them. It may hit
    /// again after the re-hit delay, measured in game time rather than its own
    /// clock, so a frozen orbiter stays a hazard to stand in.
    ///
    /// <b>Batting.</b> It is an <see cref="IDamageable"/>, so the sword strikes it
    /// like any target without combat knowing what it is. The sword finds targets
    /// with a solid-only overlap, so the orbiter carries a second, SOLID sphere
    /// (<see cref="Hitbox"/>, a child on the same Ignore Raycast layer) beside the
    /// slow-detection trigger <see cref="HypatiaProjectile"/> requires. The player's
    /// CharacterController is told to ignore it at launch, so it never blocks or
    /// shoves them; hits on the player stay the swept test. A hit that bats it
    /// (<see cref="OrbiterBatting"/>) takes it off its orbit for good: toward her,
    /// it homes on her body and raises <see cref="StruckBoss"/> on contact; away,
    /// it flies along the swing and expires after Batted Miss Lifetime. A batted
    /// orbiter never hurts the player. Its flight runs on its own clock too.
    ///
    /// It is also an <see cref="IOrbitalAnchor"/> for the planned Orbital Tether,
    /// exposing position and (slowed) velocity read-only, until it is batted.
    /// </summary>
    public class ConicOrbiter : HypatiaProjectile, IOrbitalAnchor, IDamageable
    {
        [Tooltip("The solid sphere the sword overlaps (a child, not a trigger). Built " +
                 "by Setup Hypatia Fight; created at launch when missing.")]
        [SerializeField] private SphereCollider hitbox;

        /// <summary>Its lifetime ran out, or a batted flight ended without reaching
        /// her (own clock). Raised once; the caster then retires and despawns it.</summary>
        public event Action<ConicOrbiter> Expired;

        /// <summary>A hit batted it off its orbit. The bool is true when it was sent
        /// toward her (it will home on her), false when it flies off and expires.
        /// Raised once, from inside <see cref="TakeHit"/>.</summary>
        public event Action<ConicOrbiter, bool> Batted;

        /// <summary>A ball batted toward her reached her body at this point. Raised
        /// once; the caster then damages her, tells the brain and despawns it.</summary>
        public event Action<ConicOrbiter, Vector3> StruckBoss;

        HypatiaContext _context;
        float _orbitTime, _age, _lifetime, _damage, _rehitDelay, _nextHitAt;
        Vector3 _from;
        bool _launched, _expired, _expiryRaised, _spent;

        // Batted flight.
        bool _batted, _towardHer, _struck;
        Vector3 _velocity;
        float _flightAge, _flightLimit, _turnRate;

        /// <summary>The orbit, in its own perifocal frame (see <see cref="Plane"/>).</summary>
        public KeplerOrbit Orbit { get; private set; }

        /// <summary>Where the orbit sits in the world: focus, angle, sense.</summary>
        public ConicPlane Plane { get; private set; }

        /// <summary>Hit radius, m (the trigger, the solid hitbox and the swept tests).</summary>
        public float Radius { get; private set; }

        /// <summary>Seconds lived on its own clock.</summary>
        public float Age => _age;

        /// <summary>Seconds it lives on its own clock (on its orbit).</summary>
        public float Lifetime => _lifetime;

        /// <summary>True once a hit has batted it off its orbit.</summary>
        public bool IsBatted => _batted;

        /// <summary>True when it was batted toward her (homing on her body).</summary>
        public bool IsBattedTowardHer => _batted && _towardHer;

        /// <summary>The solid sphere the sword hits; null before launch.</summary>
        public SphereCollider Hitbox => hitbox;

        /// <inheritdoc/>
        public Vector3 Position => transform.position;

        /// <inheritdoc/>
        public Vector3 Velocity
        {
            get
            {
                if (!_launched || _spent) return Vector3.zero;
                if (_batted) return _velocity * SpeedMultiplier;
                return Plane.DirectionToWorld(Orbit.VelocityAt(_orbitTime)) * SpeedMultiplier;
            }
        }

        /// <inheritdoc/>
        /// <remarks>False once batted: it has left its orbit, so a tether latched on it lets go.</remarks>
        public bool IsAlive => _launched && !_spent && !_expired && !_batted && this != null;

        /// <summary>Put it on <paramref name="orbit"/> at periapsis and start its clock.</summary>
        public void Launch(HypatiaContext context, KeplerOrbit orbit, ConicPlane plane, float lifetime,
                           float radius, float damage, float rehitDelay)
        {
            _context = context;
            Orbit = orbit;
            Plane = plane;
            _lifetime = Mathf.Max(0.01f, lifetime);
            Radius = radius;
            _damage = damage;
            _rehitDelay = Mathf.Max(0f, rehitDelay);
            _orbitTime = _age = 0f;
            _nextHitAt = float.NegativeInfinity;
            _launched = true;
            _expired = _expiryRaised = _spent = false;
            _batted = _towardHer = _struck = false;
            _velocity = Vector3.zero;
            _flightAge = 0f;
            transform.position = plane.ToWorld(orbit.PositionAt(0f));
            _from = transform.position;
            SetSlowTriggerRadius(radius);
            PrepareHitbox(radius);
        }

        /// <summary>Stop acting (no motion, damage or events). Called by the caster
        /// before it despawns this, since Destroy is deferred.</summary>
        public void Retire() => _spent = true;

        /// <summary>
        /// The sword (or anything else) struck it. Bats it when the hit's kind may
        /// (<see cref="OrbiterBatting.CanBat"/>) and the swing has a direction; the
        /// amount and launch flag are ignored (an orbiter has no health). A spent,
        /// expired or already batted orbiter ignores further hits.
        /// </summary>
        public void TakeHit(float amount, Vector3 hitPoint, Vector3 knockback, bool launch,
                            DamageKind kind = DamageKind.Physical)
        {
            if (!_launched || _spent || _expired || _batted || _context == null) return;
            HypatiaConfig cfg = _context.Config;
            if (cfg == null || !OrbiterBatting.CanBat(kind, cfg.SpellsBatOrbiters)) return;

            Vector3 ball = transform.position;
            if (!OrbiterBatting.TrySwingDirection(knockback, hitPoint, ball, out Vector3 swing)) return;

            bool towardHer = OrbiterBatting.IsTowardHer(swing, ball, _context.BossCentre, cfg.BattingConeDegrees);
            _batted = true;
            _towardHer = towardHer;
            _velocity = swing * Mathf.Max(0.1f, cfg.BattedSpeed);
            _turnRate = Mathf.Max(0f, cfg.BattedTurnRate);
            _flightAge = 0f;
            _flightLimit = towardHer ? Mathf.Max(0.1f, cfg.BattedHomingTimeout) : Mathf.Max(0.05f, cfg.BattedMissLifetime);
            _from = ball;
            Batted?.Invoke(this, towardHer);
        }

        void Update()
        {
            if (!_launched) { ConicObjects.Despawn(this); return; } // state lost to a reload
            Advance(ScaledDeltaTime);
        }

        void LateUpdate() => ResolveContacts();

        /// <summary>Advance the orbit (or the batted flight) by
        /// <paramref name="scaledDeltaTime"/> of its own clock. Public as the test seam.</summary>
        public void Advance(float scaledDeltaTime)
        {
            if (_spent || !_launched || _expired) return;
            float dt = Mathf.Max(0f, scaledDeltaTime);
            _from = transform.position;

            if (_batted)
            {
                Fly(dt);
                return;
            }

            _age += dt;
            // Wrapped so float time never loses precision over a long life.
            _orbitTime = (_orbitTime + dt) % Orbit.Period;
            transform.position = Plane.ToWorld(Orbit.PositionAt(_orbitTime));
            if (_age >= _lifetime) _expired = true;
        }

        /// <summary>Batted flight: straight on (a miss), or steered toward her body
        /// at the turn rate (at her). Speed is constant either way.</summary>
        void Fly(float dt)
        {
            _flightAge += dt;
            if (_towardHer && _turnRate > 0f && dt > 0f)
            {
                Vector3 toHer = _context.BossCentre - transform.position;
                if (toHer.sqrMagnitude > 1e-6f)
                {
                    float speed = _velocity.magnitude;
                    Vector3 dir = Vector3.RotateTowards(_velocity / Mathf.Max(1e-4f, speed), toHer.normalized,
                                                        _turnRate * Mathf.Deg2Rad * dt, 0f);
                    _velocity = dir * speed;
                }
            }
            transform.position += _velocity * dt;
            if (_flightAge >= _flightLimit) _expired = true;
        }

        /// <summary>Swept contact (with the player on its orbit, with her once batted
        /// toward her), then the expiry event. Public as the test seam.</summary>
        public void ResolveContacts()
        {
            if (_spent || !_launched) return;

            // Her first: a ball that reaches her on the frame its flight runs out
            // still counts.
            if (_batted)
            {
                if (_towardHer && !_struck && TryStrikeHer()) return;
            }

            if (_expired)
            {
                if (_expiryRaised) return;
                _expiryRaised = true;
                Expired?.Invoke(this);
                return;
            }

            if (_batted) return;   // a batted ball hurts nobody

            if (_context == null || Time.time < _nextHitAt) return;
            if (!_context.TryGetPlayerCapsule(out Vector3 bottom, out Vector3 top, out float capsuleRadius)) return;

            Vector3 to = transform.position;
            if (!HypatiaContact.SweptSphereHitsCapsule(_from, to, Radius, bottom, top, capsuleRadius)) return;

            _nextHitAt = Time.time + _rehitDelay;
            if (_context.PlayerHealth != null)
                _context.PlayerHealth.Damage(_damage, to, recoverable: true);
        }

        bool TryStrikeHer()
        {
            if (_context == null) return false;
            if (!_context.TryGetBossCapsule(out Vector3 bottom, out Vector3 top, out float bodyRadius)) return false;
            Vector3 to = transform.position;
            if (!HypatiaContact.SweptSphereHitsCapsule(_from, to, Radius, bottom, top, bodyRadius)) return false;

            _struck = true;
            _expired = _expiryRaised = true;   // done: no expiry event after the strike
            StruckBoss?.Invoke(this, to);
            return true;
        }

        /// <summary>Size the solid hitbox, creating it if the prefab has none, and
        /// keep the player's controller from ever colliding with it.</summary>
        void PrepareHitbox(float radius)
        {
            if (hitbox == null)
            {
                var go = new GameObject("Hitbox") { layer = gameObject.layer };
                go.transform.SetParent(transform, false);
                hitbox = go.AddComponent<SphereCollider>();
            }
            hitbox.isTrigger = false;
            hitbox.radius = Mathf.Max(0.01f, radius);
            if (_context != null && _context.PlayerController != null)
                Physics.IgnoreCollision(_context.PlayerController, hitbox, true);
        }
    }
}
