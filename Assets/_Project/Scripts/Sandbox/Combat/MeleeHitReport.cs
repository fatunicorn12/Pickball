using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// One connecting hit, after it has been dealt. Raised by MeleeCombat for
    /// anyone who reacts to hits — impact VFX and camera shake, XP, audio —
    /// so none of them have to be called by the weapon.
    /// </summary>
    public readonly struct MeleeHitReport
    {
        /// <summary>What was struck.</summary>
        public IDamageable Target { get; }

        /// <summary>The struck collider, for listeners that need the GameObject
        /// (e.g. to look for a Health and see whether this was the killing blow).</summary>
        public Collider Collider { get; }

        /// <summary>World point of contact.</summary>
        public Vector3 Point { get; }

        /// <summary>Damage dealt before the receiver's own filters (weak points,
        /// resistances), in HP.</summary>
        public float Damage { get; }

        /// <summary>The step that landed it, and where it sat in the chain.</summary>
        public ResolvedStep Placement { get; }

        /// <summary>True if the area burst landed it rather than the blade.</summary>
        public bool FromBurst { get; }

        public MeleeHitReport(IDamageable target, Collider collider, Vector3 point, float damage,
                              ResolvedStep placement, bool fromBurst)
        {
            Target = target;
            Collider = collider;
            Point = point;
            Damage = damage;
            Placement = placement;
            FromBurst = fromBurst;
        }
    }
}
