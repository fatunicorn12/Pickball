using System;
using Pickleball.Sandbox.Combat;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// One arcane orb in flight: a slow, gently homing ball that bursts on the
    /// first thing it touches, damaging what it hit directly and everything
    /// else damageable in a radius with falloff.
    ///
    /// Simulation only. It moves in Update (the Sandbox's variable-dt clock),
    /// deals damage and raises <see cref="Launched"/>, <see cref="Burst"/> and
    /// <see cref="Fizzled"/>; it plays no effect. <c>MagicOrbVisual</c> listens.
    /// All tuning is read from the <see cref="SpellDefinition"/> every frame.
    ///
    /// Design decisions, each avoiding a specific failure:
    ///  - <b>Swept, not stepped.</b> Each frame sphere-casts from where the orb
    ///    was to where it will be, so a long frame cannot tunnel it through a
    ///    thin wall or a target. Initial overlaps (spawned inside something)
    ///    burst on the spot, so a point-blank shot into the boss still lands.
    ///  - <b>No collider of its own.</b> Everything is a query, so the orb can
    ///    never block the player, be stood on, or be hit by its own sweep.
    ///  - <b>Never hurts its owner.</b> Any collider under the owner's transform
    ///    is skipped by the sweep and the blast, and so is any damageable that
    ///    belongs to the owner.
    ///  - <b>Each damageable at most once per burst,</b> even if it has several
    ///    colliders, tracked in a fixed array (no allocation per burst).
    ///  - <b>Homing only while the target is ahead.</b> At the default 25 °/s the
    ///    turn circle is ~20 m, so a shot that has passed its target flies on
    ///    rather than boomeranging back for a second try.
    ///
    /// <b>Cleanup.</b> The orb destroys its own GameObject the moment it bursts
    /// or fizzles, so with or without a visual it can never leak. A visual that
    /// wants to finish its burst flash detaches itself from the orb inside the
    /// <see cref="Burst"/> / <see cref="Fizzled"/> handler (they are raised
    /// synchronously, before the Destroy) and owns its own lifetime from then
    /// on — presentation decides how long presentation lasts.
    ///
    /// Hot reload: buffers are rebuilt lazily. If a script reload in Play mode
    /// drops the flight state, the orb finds itself not flying on its next
    /// Update and removes itself; a stray instance that was never launched
    /// (dragged into a scene) does the same.
    /// </summary>
    public class MagicOrb : MonoBehaviour
    {
        const int SweepCapacity = 16;
        const int BlastCapacity = 32;

        /// <summary>Raised once by <see cref="Launch"/>, after the orb is placed and
        /// aimed — the visual's muzzle flash and trail reset.</summary>
        public event Action<MagicOrb> Launched;

        /// <summary>Raised once when the orb touches something: the burst centre
        /// (the orb's centre at contact) and how many damageables were hit, direct
        /// hit included. Damage has already been dealt. The orb's GameObject is
        /// destroyed right after this returns.</summary>
        public event Action<MagicOrb, Vector3, int> Burst;

        /// <summary>Raised once when the lifetime ran out without touching anything.
        /// The orb's GameObject is destroyed right after this returns.</summary>
        public event Action<MagicOrb> Fizzled;

        /// <summary>World velocity, m/s; zero once it has burst or fizzled.</summary>
        public Vector3 Velocity => _flying && _spell != null ? _direction * _spell.OrbSpeed : Vector3.zero;

        /// <summary>True once the orb has burst on something (stays false on a fizzle).</summary>
        public bool HasBurst { get; private set; }

        /// <summary>True between <see cref="Launch"/> and the burst or fizzle.</summary>
        public bool IsFlying => _flying;

        /// <summary>The spell this orb was launched with (null before launch). The
        /// visual reads its blast radius so the shell it draws is the real one.</summary>
        public SpellDefinition Spell => _spell;

        /// <summary>Who cast it; never damaged by it. May be null.</summary>
        public Transform Owner => _owner;

        /// <summary>What it is homing on; null when flying straight.</summary>
        public Transform HomingTarget => _homingTarget;

        SpellDefinition _spell;
        Transform _owner;
        Transform _homingTarget;
        Collider _homingCollider;
        Vector3 _direction = Vector3.forward;
        float _age;
        bool _flying;

        // Runtime-built, never readonly: rebuilt lazily so a reload in Play
        // mode cannot leave them null.
        RaycastHit[] _sweepHits;
        Collider[] _blastHits;
        IDamageable[] _struck;
        int _struckCount;

        /// <summary>
        /// Put the orb in flight. Call once, right after instantiating the prefab.
        /// </summary>
        /// <param name="position">World start point (the caster's hand).</param>
        /// <param name="direction">Initial travel direction; need not be normalised.</param>
        /// <param name="spell">Tuning. Null is a wiring error: the orb removes itself.</param>
        /// <param name="owner">The caster. Nothing under it is ever hit.</param>
        /// <param name="homingTarget">Transform of the collider that was under the
        /// crosshair at launch, or null to fly straight. The orb steers toward
        /// that collider's bounds centre (its pivot if it has no collider), so a
        /// tall target is homed on at the chest rather than the feet.</param>
        public void Launch(Vector3 position, Vector3 direction, SpellDefinition spell, Transform owner,
                           Transform homingTarget)
        {
            if (spell == null)
            {
                Debug.LogWarning($"[MagicOrb] {name} launched without a SpellDefinition; removing it.", this);
                Destroy(gameObject);
                return;
            }

            EnsureBuffers();
            _spell = spell;
            _owner = owner;
            _homingTarget = homingTarget;
            _homingCollider = null;
            if (homingTarget != null) homingTarget.TryGetComponent(out _homingCollider);

            if (direction.sqrMagnitude > 1e-8f) _direction = direction.normalized;
            else _direction = owner != null ? owner.forward : Vector3.forward;

            _age = 0f;
            _flying = true;
            HasBurst = false;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(_direction));
            Launched?.Invoke(this);
        }

        void EnsureBuffers()
        {
            if (_sweepHits == null) _sweepHits = new RaycastHit[SweepCapacity];
            if (_blastHits == null) _blastHits = new Collider[BlastCapacity];
            // +1: the direct hit plus every blast collider, all distinct.
            if (_struck == null) _struck = new IDamageable[BlastCapacity + 1];
        }

        void Update()
        {
            if (!_flying || _spell == null)
            {
                // Never launched, or a script reload dropped the flight state.
                Destroy(gameObject);
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            EnsureBuffers();

            _age += dt;
            SteerTowardTarget(dt);

            Vector3 from = transform.position;
            float step = _spell.OrbSpeed * dt;
            if (TrySweep(from, step, out RaycastHit hit))
            {
                // Initial overlap: the cast reports distance 0 and no contact
                // point, so burst where the orb already is.
                bool overlapping = hit.distance <= 0f;
                Vector3 centre = overlapping ? from : from + _direction * hit.distance;
                Vector3 contact = overlapping ? from : hit.point;
                BurstAt(centre, contact, hit.collider);
                return;
            }

            transform.SetPositionAndRotation(from + _direction * step, Quaternion.LookRotation(_direction));
            if (_age >= _spell.OrbLifetime) Fizzle();
        }

        // ------------------------------------------------------------ flight

        void SteerTowardTarget(float dt)
        {
            float rate = _spell.HomingTurnRate;
            if (rate <= 0f || _homingTarget == null || !_homingTarget.gameObject.activeInHierarchy) return;

            Vector3 centre = _homingCollider != null && _homingCollider.enabled
                ? _homingCollider.bounds.center
                : _homingTarget.position;
            Vector3 toTarget = centre - transform.position;
            if (Vector3.Dot(toTarget, _direction) <= 0f) return;   // passed it: fly on
            _direction = OrbMath.Steer(_direction, toTarget, rate * dt);
        }

        /// <summary>Nearest thing this frame's movement touches that is not the owner.</summary>
        bool TrySweep(Vector3 from, float distance, out RaycastHit nearest)
        {
            nearest = default;
            bool found = false;
            float best = float.PositiveInfinity;
            int count = Physics.SphereCastNonAlloc(from, _spell.OrbRadius, _direction, _sweepHits, distance,
                                                   _spell.HitMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var h = _sweepHits[i];
                if (h.collider == null || IsOwners(h.collider.transform)) continue;
                if (h.distance >= best) continue;
                best = h.distance;
                nearest = h;
                found = true;
            }
            return found;
        }

        // ------------------------------------------------------------- burst

        void BurstAt(Vector3 centre, Vector3 contact, Collider direct)
        {
            _flying = false;
            HasBurst = true;
            transform.position = centre;
            _struckCount = 0;
            int hits = 0;

            var target = TargetOf(direct);
            if (target != null)
            {
                Remember(target);
                target.TakeHit(_spell.DirectDamage, contact, Push(_direction, _spell.Knockback), false);
                hits++;
            }

            float radius = _spell.BlastRadius;
            if (radius > 0f)
            {
                float max = _spell.BlastDamageMax;
                int count = Physics.OverlapSphereNonAlloc(centre, radius, _blastHits, _spell.HitMask,
                                                          QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    var col = _blastHits[i];
                    var victim = TargetOf(col);
                    if (victim == null || HasStruck(victim)) continue;

                    // Distance to the collider's surface, not its pivot: a big
                    // boss caught by the edge of the blast is caught at his skin.
                    // HitPoint, not ClosestPoint: a damageable on a CharacterController
                    // or a non-convex mesh would otherwise warn every burst.
                    Vector3 point = HitPoint.Nearest(col, centre);
                    float damage = OrbMath.BlastDamageAt(Vector3.Distance(centre, point), radius,
                                                         max, _spell.BlastDamageMin);
                    if (damage <= 0f) continue;

                    Remember(victim);
                    Vector3 away = col.bounds.center - centre;
                    if (away.sqrMagnitude < 1e-6f) away = _direction;
                    float share = max > 0f ? Mathf.Clamp01(damage / max) : 1f;
                    victim.TakeHit(damage, point, Push(away, _spell.Knockback * share), false);
                    hits++;
                }
            }

            Burst?.Invoke(this, centre, hits);
            Destroy(gameObject);
        }

        void Fizzle()
        {
            _flying = false;
            Fizzled?.Invoke(this);
            Destroy(gameObject);
        }

        /// <summary>The damageable behind <paramref name="col"/>, or null if it has
        /// none or it belongs to the owner.</summary>
        IDamageable TargetOf(Collider col)
        {
            if (col == null || IsOwners(col.transform)) return null;
            var target = col.GetComponentInParent<IDamageable>();
            if (target == null) return null;
            if (target is Object unityObject && unityObject == null) return null;   // destroyed
            if (target is Component component && IsOwners(component.transform)) return null;
            return target;
        }

        bool IsOwners(Transform t) => _owner != null && t.IsChildOf(_owner);

        bool HasStruck(IDamageable target)
        {
            for (int i = 0; i < _struckCount; i++)
                if (ReferenceEquals(_struck[i], target)) return true;
            return false;
        }

        void Remember(IDamageable target)
        {
            if (_struckCount < _struck.Length) _struck[_struckCount++] = target;
        }

        /// <summary>Horizontal shove of <paramref name="speed"/> m/s along
        /// <paramref name="direction"/>; zero when the direction is vertical.</summary>
        static Vector3 Push(Vector3 direction, float speed)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 1e-6f ? direction.normalized * speed : Vector3.zero;
        }
    }
}
