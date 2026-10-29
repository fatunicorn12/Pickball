using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// Base for anything she releases that moves itself: lobs, orbiters, the
    /// flyby shot, burning scrolls. It honours slows on its own clock: the Freeze
    /// Zone (and the planned Stationary Point) find it through a small trigger
    /// collider and <see cref="ISlowable"/>, and every subclass moves by
    /// <see cref="ScaledDeltaTime"/>. Her own slow does not reach it; once
    /// thrown, it is on its own clock (Docs/Design/StationaryPoint.md).
    ///
    /// The trigger never collides with anything (FreezeZone queries triggers
    /// since the Hypatia change); hits on the player are swept tests in the
    /// subclass (<see cref="HypatiaContact"/>).
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public abstract class HypatiaProjectile : MonoBehaviour, ISlowable
    {
        // Not readonly and built lazily: survives a script reload (§4).
        SlowLedger _ledger;
        SlowLedger Ledger => _ledger ??= new SlowLedger();

        /// <summary>Fraction of normal speed now, 0-1 (0 = frozen).</summary>
        public float SpeedMultiplier => Ledger.MultiplierAt(Time.time);

        /// <summary>This frame's dt on this projectile's clock.</summary>
        protected float ScaledDeltaTime => Time.deltaTime * SpeedMultiplier;

        public void ApplySlow(object source, float speedMultiplier, float seconds)
        {
            if (source == null || float.IsNaN(speedMultiplier) || float.IsNaN(seconds)) return;
            Ledger.Apply(source, speedMultiplier, seconds, Time.time);
        }

        /// <summary>Size the slow-detection trigger (call when spawned).</summary>
        protected void SetSlowTriggerRadius(float radius)
        {
            var sphere = GetComponent<SphereCollider>();
            sphere.isTrigger = true;
            sphere.radius = Mathf.Max(0.01f, radius);
        }

        protected virtual void OnEnable()
        {
            GetComponent<SphereCollider>().isTrigger = true;
        }
    }
}
