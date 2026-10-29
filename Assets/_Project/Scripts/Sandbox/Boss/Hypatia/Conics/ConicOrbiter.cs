using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// One elliptical orbiter (e &lt; 1): a body on a Kepler orbit with her at a
    /// focus, fast at periapsis and slow at apoapsis. Its orbit and lifetime run on
    /// its own clock, so freezing her leaves it circling and a Freeze Zone pins it
    /// in place (multiplier 0 simply stops time; nothing divides by it).
    ///
    /// Contact is a swept sphere (last frame's position to this one) against the
    /// player's capsule, so a periapsis whip cannot tunnel through them. It may hit
    /// again after the re-hit delay, measured in game time rather than its own
    /// clock, so a frozen orbiter stays a hazard to stand in.
    ///
    /// It is also an <see cref="IOrbitalAnchor"/> for the planned Orbital Tether,
    /// exposing position and (slowed) velocity read-only.
    /// </summary>
    public class ConicOrbiter : HypatiaProjectile, IOrbitalAnchor
    {
        /// <summary>Its lifetime ran out (own clock). Raised once; the caster then
        /// retires and despawns it.</summary>
        public event Action<ConicOrbiter> Expired;

        HypatiaContext _context;
        float _orbitTime, _age, _lifetime, _damage, _rehitDelay, _nextHitAt;
        Vector3 _from;
        bool _launched, _expired, _expiryRaised, _spent;

        /// <summary>The orbit, in its own perifocal frame (see <see cref="Plane"/>).</summary>
        public KeplerOrbit Orbit { get; private set; }

        /// <summary>Where the orbit sits in the world: focus, angle, sense.</summary>
        public ConicPlane Plane { get; private set; }

        /// <summary>Hit radius, m.</summary>
        public float Radius { get; private set; }

        /// <summary>Seconds lived on its own clock.</summary>
        public float Age => _age;

        /// <summary>Seconds it lives on its own clock.</summary>
        public float Lifetime => _lifetime;

        /// <inheritdoc/>
        public Vector3 Position => transform.position;

        /// <inheritdoc/>
        public Vector3 Velocity =>
            _launched && !_spent ? Plane.DirectionToWorld(Orbit.VelocityAt(_orbitTime)) * SpeedMultiplier : Vector3.zero;

        /// <inheritdoc/>
        public bool IsAlive => _launched && !_spent && !_expired && this != null;

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
            transform.position = plane.ToWorld(orbit.PositionAt(0f));
            _from = transform.position;
            SetSlowTriggerRadius(radius);
        }

        /// <summary>Stop acting (no motion, damage or events). Called by the caster
        /// before it despawns this, since Destroy is deferred.</summary>
        public void Retire() => _spent = true;

        void Update()
        {
            if (!_launched) { ConicObjects.Despawn(this); return; } // state lost to a reload
            Advance(ScaledDeltaTime);
        }

        void LateUpdate() => ResolveContacts();

        /// <summary>Advance the orbit by <paramref name="scaledDeltaTime"/> of its own
        /// clock. Public as the test seam.</summary>
        public void Advance(float scaledDeltaTime)
        {
            if (_spent || !_launched || _expired) return;
            float dt = Mathf.Max(0f, scaledDeltaTime);
            _from = transform.position;
            _age += dt;
            // Wrapped so float time never loses precision over a long life.
            _orbitTime = (_orbitTime + dt) % Orbit.Period;
            transform.position = Plane.ToWorld(Orbit.PositionAt(_orbitTime));
            if (_age >= _lifetime) _expired = true;
        }

        /// <summary>Swept contact with the player, then the expiry event. Public as the
        /// test seam.</summary>
        public void ResolveContacts()
        {
            if (_spent || !_launched) return;

            if (_expired)
            {
                if (_expiryRaised) return;
                _expiryRaised = true;
                Expired?.Invoke(this);
                return;
            }

            if (_context == null || Time.time < _nextHitAt) return;
            if (!_context.TryGetPlayerCapsule(out Vector3 bottom, out Vector3 top, out float capsuleRadius)) return;

            Vector3 to = transform.position;
            if (!HypatiaContact.SweptSphereHitsCapsule(_from, to, Radius, bottom, top, capsuleRadius)) return;

            _nextHitAt = Time.time + _rehitDelay;
            if (_context.PlayerHealth != null)
                _context.PlayerHealth.Damage(_damage, to, recoverable: true);
        }
    }
}
