using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// A burn on one target: damage over time through
    /// <see cref="IDamageable.TakeHit"/>, so it works on anything a spell can
    /// hurt (Newton, the dummies) with no opt-in. Spawned by the Fireball's
    /// burst through <see cref="Ignite"/>, parented to the victim so it follows
    /// it, and gone when the burn ends.
    ///
    /// Simulation only: the clock is a <see cref="BurnTicker"/>, the effects
    /// are <c>BurnVisual</c> on a child of the prefab, which listens to
    /// <see cref="Ignited"/>, <see cref="Ticked"/> and <see cref="Extinguished"/>.
    /// Like <see cref="MagicOrb"/>, it destroys its own GameObject the moment
    /// it ends; a visual that wants to fade detaches itself inside the
    /// Extinguished handler.
    ///
    /// <b>One burn per target.</b> Igniting a target that already burns
    /// refreshes that burn (see <see cref="BurnTicker.Ignite"/>) instead of
    /// spawning a second, so damage per second is capped however many
    /// fireballs land.
    ///
    /// Ticks pass no knockback and are not launchers. They do count as hurt,
    /// so they show damage numbers and pay Newton's regain credit like any
    /// other hit. A reload in Play mode drops the target reference (an
    /// interface is not serialized); the burn then puts itself out.
    /// </summary>
    public class BurnStatus : MonoBehaviour
    {
        /// <summary>Raised when a burn starts or is refreshed.</summary>
        public event Action<BurnStatus> Ignited;

        /// <summary>Raised once per damage tick, after the damage, with its amount.</summary>
        public event Action<BurnStatus, float> Ticked;

        /// <summary>Raised once when the burn ends. The GameObject is destroyed
        /// right after this returns.</summary>
        public event Action<BurnStatus> Extinguished;

        BurnTicker _ticker;
        IDamageable _target;
        Component _targetComponent;
        Collider _targetCollider;
        bool _ended;

        // Built on first use, never readonly (CodingStandards §4).
        BurnTicker Ticker => _ticker ??= new BurnTicker();

        /// <summary>What is burning; null once it has gone.</summary>
        public IDamageable Target => _targetComponent != null ? _target : null;

        /// <summary>The burning object's transform; null once it has gone.</summary>
        public Transform TargetTransform => _targetComponent != null ? _targetComponent.transform : null;

        /// <summary>Seconds of burn left.</summary>
        public float Remaining => Ticker.Remaining;

        /// <summary>World centre of the burning body (collider bounds, else pivot +1 m).</summary>
        public Vector3 BodyCentre
        {
            get
            {
                if (_targetCollider != null && _targetCollider.enabled) return _targetCollider.bounds.center;
                return _targetComponent != null ? _targetComponent.transform.position + Vector3.up : transform.position;
            }
        }

        /// <summary>World extents of the burning body; a 0.4 m ball without a collider.</summary>
        public Vector3 BodyExtents => _targetCollider != null && _targetCollider.enabled
            ? _targetCollider.bounds.extents
            : Vector3.one * 0.4f;

        /// <summary>
        /// Sets <paramref name="target"/> alight, or refreshes its burn. Returns the
        /// burn, or null when nothing could be lit (no prefab, no target, a target
        /// that is not a component, or values that make no burn).
        /// </summary>
        public static BurnStatus Ignite(BurnStatus prefab, IDamageable target, float damagePerTick,
                                        float interval, float duration)
        {
            if (prefab == null || damagePerTick <= 0f || interval <= 0f || duration <= 0f) return null;
            if (!(target is Component component) || component == null) return null;

            var existing = FindOn(component.transform, target);
            if (existing != null)
            {
                existing.Refresh(damagePerTick, interval, duration);
                return existing;
            }

            var burn = Object.Instantiate(prefab, component.transform);
            burn.transform.localPosition = Vector3.zero;
            burn.transform.localRotation = Quaternion.identity;
            burn.Bind(target, component);
            burn.Refresh(damagePerTick, interval, duration);
            return burn;
        }

        /// <summary>Advances the burn by <paramref name="dt"/> seconds, dealing every
        /// tick that fell due. Update calls it with Time.deltaTime; public so a test
        /// or harness can drive time.</summary>
        public void Tick(float dt)
        {
            if (_ended) return;
            if (Target == null)
            {
                End();
                return;
            }

            int ticks = Ticker.Tick(dt);
            for (int i = 0; i < ticks && Target != null; i++)
            {
                float damage = Ticker.DamagePerTick;
                _target.TakeHit(damage, BodyCentre, Vector3.zero, false);
                Ticked?.Invoke(this, damage);
            }

            if (!Ticker.IsBurning) End();
        }

        void Update()
        {
            if (_target == null && !_ended)
            {
                // Never ignited (dragged into a scene) or a reload dropped the target.
                Destroy(gameObject);
                _ended = true;
                return;
            }
            Tick(Time.deltaTime);
        }

        void Bind(IDamageable target, Component component)
        {
            _target = target;
            _targetComponent = component;
            _targetCollider = component.GetComponent<Collider>();
            if (_targetCollider == null) _targetCollider = component.GetComponentInChildren<Collider>();
        }

        void Refresh(float damagePerTick, float interval, float duration)
        {
            Ticker.Ignite(damagePerTick, interval, duration);
            Ignited?.Invoke(this);
        }

        void End()
        {
            if (_ended) return;
            _ended = true;
            Ticker.Extinguish();
            Extinguished?.Invoke(this);
            Destroy(gameObject);
        }

        static BurnStatus FindOn(Transform parent, IDamageable target)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                if (!parent.GetChild(i).TryGetComponent(out BurnStatus burn)) continue;
                if (!burn._ended && ReferenceEquals(burn._target, target)) return burn;
            }
            return null;
        }
    }
}
