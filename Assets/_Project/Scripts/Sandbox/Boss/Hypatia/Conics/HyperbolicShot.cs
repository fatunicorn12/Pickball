using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// The hyperbola flyby (e &gt; 1): a fast shot that comes in along one
    /// asymptote, swings round her at periapsis and leaves along the other. It walks
    /// a polyline precomputed at fire time at a constant speed along the path, on
    /// its own clock, so the curve it flies is exactly the curve that was
    /// telegraphed and a Freeze Zone can hold it mid-flight.
    ///
    /// Contact is a swept sphere against the player's capsule, at most once per
    /// shot; it keeps flying after a hit (it is a flyby, not a bullet). When it
    /// reaches the end of the path it raises <see cref="Left"/> and the caster
    /// despawns it.
    /// </summary>
    public class HyperbolicShot : HypatiaProjectile
    {
        /// <summary>The shot reached the end of its path. Raised once.</summary>
        public event Action<HyperbolicShot> Left;

        HypatiaContext _context;
        ConicPolyline _path;
        float _speed, _travelled, _damage;
        Vector3 _from;
        bool _launched, _done, _leftRaised, _spent;

        /// <summary>The path it follows (shared with the telegraph; read-only use).</summary>
        public ConicPolyline Path => _path;

        /// <summary>Metres travelled along <see cref="Path"/>.</summary>
        public float Travelled => _travelled;

        /// <summary>Hit radius, m.</summary>
        public float Radius { get; private set; }

        /// <summary>Did it hit the player?</summary>
        public bool HitPlayer { get; private set; }

        /// <summary>Unit travel direction now (for a trail or the model's facing).</summary>
        public Vector3 Direction => _path != null ? _path.DirectionAt(_travelled) : Vector3.forward;

        /// <summary>Start at the path's first point.</summary>
        public void Launch(HypatiaContext context, ConicPolyline path, float speed, float radius, float damage)
        {
            _context = context;
            _path = path;
            _speed = Mathf.Max(0.01f, speed);
            Radius = radius;
            _damage = damage;
            _travelled = 0f;
            _launched = path != null && path.Count > 0;
            _done = !_launched;
            _leftRaised = _spent = false;
            HitPlayer = false;
            transform.position = _launched ? path.PointAt(0f) : transform.position;
            _from = transform.position;
            SetSlowTriggerRadius(radius);
        }

        /// <summary>Stop acting (no motion, damage or events); the caster despawns it.</summary>
        public void Retire() => _spent = true;

        void Update()
        {
            if (_path == null) { ConicObjects.Despawn(this); return; } // state lost to a reload
            Advance(ScaledDeltaTime);
        }

        void LateUpdate() => ResolveContacts();

        /// <summary>Move <paramref name="scaledDeltaTime"/> × speed along the path. Public
        /// as the test seam.</summary>
        public void Advance(float scaledDeltaTime)
        {
            if (_spent || !_launched || _done) return;
            _from = transform.position;
            _travelled += _speed * Mathf.Max(0f, scaledDeltaTime);
            if (_travelled >= _path.Length)
            {
                _travelled = _path.Length;
                _done = true;
            }
            transform.position = _path.PointAt(_travelled);
        }

        /// <summary>Swept contact, then the leave event. Public as the test seam.</summary>
        public void ResolveContacts()
        {
            if (_spent) return;

            if (!HitPlayer && _launched && _context != null &&
                _context.TryGetPlayerCapsule(out Vector3 bottom, out Vector3 top, out float capsuleRadius) &&
                HypatiaContact.SweptSphereHitsCapsule(_from, transform.position, Radius, bottom, top, capsuleRadius))
            {
                HitPlayer = true;
                if (_context.PlayerHealth != null)
                    _context.PlayerHealth.Damage(_damage, transform.position, recoverable: true);
            }

            if (_done && !_leftRaised)
            {
                _leftRaised = true;
                Left?.Invoke(this);
            }
        }
    }
}
