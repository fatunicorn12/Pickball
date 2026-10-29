using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// One parabola lob (e = 1): a ballistic arc to a fixed landing point, then a
    /// blast. Its position is sampled from the closed-form arc on its own clock
    /// (<see cref="HypatiaProjectile.ScaledDeltaTime"/>), never integrated, so it
    /// lands exactly where the marker said whatever the frame rate; a Freeze Zone
    /// slows the arc without bending it.
    ///
    /// Motion runs in Update and the blast in LateUpdate (the Newton apples'
    /// split), so the player's capsule is read after every mover has moved. The
    /// lob deals damage once, on landing, then raises <see cref="Landed"/>; the
    /// caster owns despawning it.
    /// </summary>
    public class ConicLob : HypatiaProjectile
    {
        [Tooltip("Radius of the lob's slow-detection trigger, m: a Freeze Zone " +
                 "touching this sphere slows the lob. Match the visual size.")]
        [SerializeField, Min(0.05f)] private float bodyRadius = 0.35f;

        /// <summary>The lob has landed and its blast has been resolved. Raised once.</summary>
        public event Action<ConicLob> Landed;

        HypatiaContext _context;
        Vector3 _origin, _velocity;
        float _gravity, _flightTime, _age, _damage;
        bool _launched, _touchedDown, _resolved, _spent;

        /// <summary>Where it lands (on the plate).</summary>
        public Vector3 Target { get; private set; }

        /// <summary>Flight time on its own clock, s.</summary>
        public float FlightTime => _flightTime;

        /// <summary>Seconds of flight elapsed on its own clock.</summary>
        public float Age => _age;

        /// <summary>Blast radius on landing, m.</summary>
        public float BlastRadius { get; private set; }

        /// <summary>Did the blast catch the player? Valid once landed.</summary>
        public bool HitPlayer { get; private set; }

        /// <summary>
        /// Throw it. <paramref name="gravity"/> is the downward acceleration (m/s²)
        /// the launch velocity was solved for; the same number drives the arc.
        /// </summary>
        public void Launch(HypatiaContext context, Vector3 origin, Vector3 target, float flightTime,
                           float gravity, float blastRadius, float damage)
        {
            _context = context;
            _origin = origin;
            Target = target;
            _flightTime = Mathf.Max(BallisticSolve.MinFlightTime, flightTime);
            _gravity = gravity;
            _velocity = BallisticSolve.LaunchVelocity(origin, target, _flightTime, gravity);
            BlastRadius = blastRadius;
            _damage = damage;
            _age = 0f;
            _launched = true;
            _touchedDown = _resolved = _spent = false;
            HitPlayer = false;
            transform.position = origin;
            SetSlowTriggerRadius(bodyRadius);
        }

        /// <summary>Stop acting: no more motion, blast or events. The caster calls
        /// this before despawning, since Destroy is deferred to the frame's end.</summary>
        public void Retire() => _spent = true;

        void Update()
        {
            // A script reload wipes the launch state (non-serialized); a lob that no
            // longer knows where it is going removes itself rather than hang mid-air.
            if (!_launched) { ConicObjects.Despawn(this); return; }
            Advance(ScaledDeltaTime);
        }

        void LateUpdate() => ResolveContacts();

        /// <summary>Move along the arc by <paramref name="scaledDeltaTime"/> seconds of
        /// the lob's own clock. Public as the test seam (EditMode has no Update).</summary>
        public void Advance(float scaledDeltaTime)
        {
            if (_spent || !_launched || _touchedDown) return;
            _age += Mathf.Max(0f, scaledDeltaTime);
            if (_age >= _flightTime)
            {
                _age = _flightTime;
                _touchedDown = true;
                transform.position = Target;
                return;
            }
            transform.position = BallisticSolve.PositionAt(_origin, _velocity, _gravity, _age);
        }

        /// <summary>Resolve the landing blast if it touched down. Public as the test seam.</summary>
        public void ResolveContacts()
        {
            if (_spent || !_touchedDown || _resolved) return;
            _resolved = true;

            if (_context != null && _context.TryGetPlayerCapsule(out Vector3 bottom, out Vector3 top, out float radius))
            {
                float reach = BlastRadius + radius;
                if (HypatiaContact.SegmentSegmentDistanceSq(Target, Target, bottom, top) <= reach * reach)
                {
                    HitPlayer = true;
                    if (_context.PlayerHealth != null)
                        _context.PlayerHealth.Damage(_damage, Target, recoverable: true);
                }
            }

            Landed?.Invoke(this);
        }
    }
}
