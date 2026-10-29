using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Burning
{
    /// <summary>
    /// How a burning scroll drifts and when the player picks one up. Plain C#,
    /// EditMode-tested.
    ///
    /// Drift: the scroll circles the centre (tangential, one way or the other
    /// as chosen at spawn) with a slow Perlin wander off that heading, then its
    /// radius is clamped into the burned ring, so it stays in the fire and
    /// slides along an edge instead of leaving it.
    ///
    /// Pickup is swept in the PLAYER's frame: the scroll's motion this frame
    /// minus the player's, tested against the player's capsule now. A fast
    /// dash past a scroll, or a scroll sweeping past a still player, cannot
    /// skip over it between frames.
    /// </summary>
    public static class ScrollMotion
    {
        /// <summary>
        /// One drift step. Offsets are horizontal (Y ignored and returned 0),
        /// measured from the plate's centre.
        /// </summary>
        /// <param name="offset">Where the scroll is now.</param>
        /// <param name="age">Seconds the scroll has lived (its clock); drives the wander.</param>
        /// <param name="dt">This frame on the scroll's clock; 0 holds it still.</param>
        /// <param name="speed">Drift speed, m/s.</param>
        /// <param name="direction">+1 clockwise in the top view (X right, Z up), -1 anticlockwise.</param>
        /// <param name="seed">Per-scroll noise offset, so scrolls wander differently.</param>
        /// <param name="wanderDegrees">Largest swing off the tangent, °.</param>
        /// <param name="wanderRate">How fast the wander changes, 1/s.</param>
        /// <param name="innerRadius">Inner edge of the burned ring, m.</param>
        /// <param name="outerRadius">Outer edge of the burned ring (the rim), m.</param>
        public static Vector3 Drift(Vector3 offset, float age, float dt, float speed, float direction,
                                    float seed, float wanderDegrees, float wanderRate,
                                    float innerRadius, float outerRadius)
        {
            offset.y = 0f;
            if (dt > 0f && speed > 0f)
            {
                Vector3 radial = offset.sqrMagnitude > 1e-8f ? offset.normalized : Vector3.forward;
                Vector3 tangent = Vector3.Cross(Vector3.up, radial) * (direction < 0f ? -1f : 1f);
                float wander = (Mathf.PerlinNoise(seed, age * wanderRate) - 0.5f) * 2f * wanderDegrees;
                Vector3 heading = Quaternion.AngleAxis(wander, Vector3.up) * tangent;
                offset += heading * (speed * dt);
            }
            return ClampToRing(offset, innerRadius, outerRadius);
        }

        /// <summary>Pull a horizontal offset radially into [inner, outer]. A
        /// degenerate ring (inner above outer) collapses onto its outer edge.</summary>
        public static Vector3 ClampToRing(Vector3 offset, float innerRadius, float outerRadius)
        {
            offset.y = 0f;
            outerRadius = Mathf.Max(0f, outerRadius);
            innerRadius = Mathf.Clamp(innerRadius, 0f, outerRadius);
            float r = offset.magnitude;
            if (r < 1e-6f) return innerRadius > 0f ? Vector3.forward * innerRadius : offset;
            float clamped = Mathf.Clamp(r, innerRadius, outerRadius);
            return Mathf.Approximately(clamped, r) ? offset : offset * (clamped / r);
        }

        /// <summary>
        /// Did the scroll (moving <paramref name="scrollFrom"/> →
        /// <paramref name="scrollTo"/>) come within <paramref name="pickupRadius"/>
        /// of the player's capsule this frame, while the player moved
        /// <paramref name="playerFrom"/> → <paramref name="playerTo"/>? The
        /// capsule (sphere centres and radius) is where the player is NOW.
        /// </summary>
        public static bool Touches(Vector3 scrollFrom, Vector3 scrollTo,
                                   Vector3 playerFrom, Vector3 playerTo, float pickupRadius,
                                   Vector3 capsuleBottom, Vector3 capsuleTop, float capsuleRadius)
        {
            // In the player's frame the capsule stands still at playerTo, and
            // the scroll starts where it was relative to the player last frame:
            // playerTo + (scrollFrom - playerFrom).
            Vector3 relativeFrom = scrollFrom + (playerTo - playerFrom);
            return HypatiaContact.SweptSphereHitsCapsule(relativeFrom, scrollTo, Mathf.Max(0f, pickupRadius),
                                                         capsuleBottom, capsuleTop, capsuleRadius);
        }
    }
}
