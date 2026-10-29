using System;
using Pickleball.Sandbox.Combat;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// One Freeze Zone: a disc of frost on the ground that heavily slows
    /// whatever stands in it, for a few seconds.
    ///
    /// Simulation only. Every frame it finds the <see cref="ISlowable"/>s whose
    /// bodies overlap its cylinder (<see cref="ZoneMath"/>) and re-applies its
    /// slow with a short linger, so a target is slowed while inside and for a
    /// moment after leaving, and two zones never stack (the strongest wins, by
    /// the <see cref="ISlowable"/> contract). It raises <see cref="Placed"/> and
    /// <see cref="Expired"/>; <c>FreezeZoneVisual</c>, on a child, draws the
    /// frost and detaches itself in the Expired handler to fade out.
    ///
    /// Targets opt in by implementing <see cref="ISlowable"/> (the training
    /// dummies carry a <see cref="StatusReceiver"/>); anything else, Newton
    /// included until his owner opts in, is simply not slowed. The zone deals
    /// no damage. It never slows its owner.
    ///
    /// The overlap query includes trigger colliders. Hypatia's projectiles have
    /// no solid collider at all, only a small trigger sphere beside their
    /// <see cref="ISlowable"/>, and an Ignore query never saw them. Including
    /// triggers is safe because the only thing the zone ever does to a
    /// candidate is <see cref="ISlowable.ApplySlow"/>: a trigger with no
    /// ISlowable in its parents (talk rings, hit zones, training-station
    /// volumes) is skipped before anything is read from it, and a target with
    /// both a solid collider and a trigger is slowed once per tick (the
    /// AlreadyApplied check).
    ///
    /// The radius, height, multiplier and duration are read from the spell
    /// every frame, so live tuning reaches a zone already on the ground.
    /// </summary>
    public class FreezeZone : MonoBehaviour
    {
        const int OverlapCapacity = 32;

        // A body may dip this far below the zone's ground point (uneven floor,
        // a capsule's rounded bottom) and still count as standing in it.
        const float Below = 0.5f;

        /// <summary>Raised once by <see cref="Place"/>.</summary>
        public event Action<FreezeZone> Placed;

        /// <summary>Raised once when the duration runs out. The GameObject is
        /// destroyed right after this returns.</summary>
        public event Action<FreezeZone> Expired;

        SpellDefinition _spell;
        Transform _owner;
        Vector3 _centre;
        float _age;
        bool _active;
        int _slowedCount;

        Collider[] _overlap;
        ISlowable[] _applied;

        /// <summary>The spell cast (null before <see cref="Place"/>).</summary>
        public SpellDefinition Spell => _spell;

        /// <summary>Who placed it; never slowed by it.</summary>
        public Transform Owner => _owner;

        /// <summary>The ground point at the zone's centre.</summary>
        public Vector3 Centre => _centre;

        /// <summary>Radius in metres, from the spell.</summary>
        public float Radius => _spell != null ? _spell.ZoneRadius : 0f;

        /// <summary>Height in metres, from the spell.</summary>
        public float Height => _spell != null ? _spell.ZoneHeight : 0f;

        /// <summary>Seconds the zone lasts, from the spell.</summary>
        public float Duration => _spell != null ? _spell.ZoneDuration : 0f;

        /// <summary>Seconds since it was placed.</summary>
        public float Age => _age;

        /// <summary>Seconds left; 0 once expired.</summary>
        public float Remaining => Mathf.Max(0f, Duration - _age);

        /// <summary>1 when placed, falling to 0 as it expires.</summary>
        public float RemainingNormalised => Duration > 0f ? Mathf.Clamp01(Remaining / Duration) : 0f;

        /// <summary>True between <see cref="Place"/> and expiry.</summary>
        public bool IsActive => _active;

        /// <summary>How many targets the last tick slowed.</summary>
        public int SlowedCount => _slowedCount;

        /// <summary>
        /// Puts the zone down at <paramref name="centre"/> (a ground point) and
        /// slows what is already inside. Call once, right after instantiating.
        /// </summary>
        /// <param name="spell">Tuning. Null is a wiring error: the zone removes itself.</param>
        /// <param name="owner">The caster. Never slowed.</param>
        public void Place(Vector3 centre, SpellDefinition spell, Transform owner)
        {
            if (spell == null)
            {
                Debug.LogWarning($"[FreezeZone] {name} placed without a SpellDefinition; removing it.", this);
                Destroy(gameObject);
                return;
            }

            EnsureBuffers();
            _spell = spell;
            _owner = owner;
            _centre = centre;
            _age = 0f;
            _active = true;
            transform.SetPositionAndRotation(centre, Quaternion.identity);
            Placed?.Invoke(this);
            ApplySlows();
        }

        /// <summary>Advances the zone by <paramref name="dt"/> seconds: slows what is
        /// inside, then expires if its time is up. Update calls it with
        /// Time.deltaTime; public so a test or harness can drive time.</summary>
        public void Tick(float dt)
        {
            if (!_active || _spell == null) return;
            if (dt > 0f) _age += dt;
            if (_age >= _spell.ZoneDuration)
            {
                Expire();
                return;
            }
            ApplySlows();
        }

        void Update()
        {
            if (!_active || _spell == null)
            {
                Destroy(gameObject);
                return;
            }
            Tick(Time.deltaTime);
        }

        void EnsureBuffers()
        {
            if (_overlap == null) _overlap = new Collider[OverlapCapacity];
            if (_applied == null) _applied = new ISlowable[OverlapCapacity];
        }

        void ApplySlows()
        {
            EnsureBuffers();
            _slowedCount = 0;
            float radius = _spell.ZoneRadius, height = _spell.ZoneHeight;
            if (radius <= 0f || height <= 0f) return;

            // The smallest sphere round the cylinder: every candidate is in it.
            float half = height * 0.5f;
            Vector3 middle = _centre + Vector3.up * half;
            float reach = Mathf.Sqrt(radius * radius + half * half);
            int count = Physics.OverlapSphereNonAlloc(middle, reach, _overlap, _spell.HitMask,
                                                      QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                var col = _overlap[i];
                if (col == null || IsOwners(col.transform)) continue;
                var slowable = col.GetComponentInParent<ISlowable>();
                if (slowable == null) continue;
                if (slowable is Object unityObject && unityObject == null) continue;
                if (slowable is Component component && IsOwners(component.transform)) continue;
                if (AlreadyApplied(slowable)) continue;

                Vector3 probe = ZoneMath.AxisProbe(_centre, height, col.bounds.center);
                Vector3 nearest = HitPoint.Nearest(col, probe);
                if (!ZoneMath.Contains(_centre, radius, height, Below, nearest)) continue;

                slowable.ApplySlow(this, _spell.SlowMultiplier, _spell.SlowLinger);
                if (_slowedCount < _applied.Length) _applied[_slowedCount++] = slowable;
            }
            for (int i = 0; i < _slowedCount; i++) _applied[i] = null;
        }

        bool AlreadyApplied(ISlowable slowable)
        {
            for (int i = 0; i < _slowedCount; i++)
                if (ReferenceEquals(_applied[i], slowable)) return true;
            return false;
        }

        bool IsOwners(Transform t) => _owner != null && t.IsChildOf(_owner);

        void Expire()
        {
            _active = false;
            Expired?.Invoke(this);
            Destroy(gameObject);
        }
    }
}
