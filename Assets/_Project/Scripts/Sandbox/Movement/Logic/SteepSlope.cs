using UnityEngine;

namespace Pickleball.Sandbox.Movement.Logic
{
    /// <summary>
    /// What a surface steeper than the controller's slope limit does to him:
    /// Sunshine does not let you freeze on a steep face, it slides you down it.
    /// Pure so the threshold and direction are testable.
    /// </summary>
    public static class SteepSlope
    {
        /// <summary>Walls steeper than this (normal.y below it) are walls, not
        /// slopes, and push nothing.</summary>
        public const float MinNormalY = 0.2f;

        /// <summary>True when a contact with <paramref name="normal"/> is a slope
        /// too steep to stand on (over <paramref name="slopeLimit"/> degrees) but
        /// not a wall.</summary>
        public static bool IsSlideSurface(Vector3 normal, float slopeLimit)
            => normal.y >= MinNormalY && Vector3.Angle(normal, Vector3.up) > slopeLimit;

        /// <summary>Horizontal acceleration (m/s^2) down the steep face, or zero
        /// for a walkable slope or a wall.</summary>
        public static Vector3 SlideAcceleration(Vector3 normal, float slopeLimit, float acceleration)
        {
            if (!IsSlideSurface(normal, slopeLimit)) return Vector3.zero;
            Vector3 down = Vector3.ProjectOnPlane(Vector3.down, normal);
            down.y = 0f;
            return down.sqrMagnitude < 1e-8f ? Vector3.zero : down.normalized * acceleration;
        }
    }
}
