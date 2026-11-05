using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Where a lock-on target is this frame: the one answer that scoring, the hint, line of sight,
    /// the reticle, the camera and swing homing all share. Built by <see cref="LockOnAim"/>.
    /// </summary>
    public readonly struct LockOnBody
    {
        /// <summary>The measured volume, world space.</summary>
        public readonly Bounds Bounds;

        /// <summary>The point to aim at: the chest, always inside <see cref="Bounds"/>.</summary>
        public readonly Vector3 AimPoint;

        /// <summary>True when measured from colliders the sword can hit. False for a fallback
        /// (no such collider is enabled: renderers, or a default chest).</summary>
        public readonly bool IsHittable;

        /// <summary>Creates a body with its aim point <paramref name="chestBias"/> of the way
        /// from the middle of <paramref name="bounds"/> to its top.</summary>
        public LockOnBody(Bounds bounds, float chestBias, bool isHittable)
        {
            Bounds = bounds;
            AimPoint = bounds.center + Vector3.up * (bounds.extents.y * Mathf.Clamp01(chestBias));
            IsHittable = isHittable;
        }

        /// <summary>Horizontal radius, m: how far short of the aim point a swing should stop.</summary>
        public float Radius => Mathf.Max(Bounds.extents.x, Bounds.extents.z);

        /// <summary>Half the height, m.</summary>
        public float HalfHeight => Bounds.extents.y;
    }
}
