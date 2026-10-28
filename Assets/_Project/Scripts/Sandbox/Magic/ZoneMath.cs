using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// The shape of a ground zone: an upright cylinder standing on its centre
    /// point. Pure, so membership is tested without colliders.
    /// <see cref="FreezeZone"/> asks it about the point of each candidate's
    /// collider nearest the zone's axis, so a big target counts as inside as
    /// soon as its body overlaps the rim, not only once its pivot does.
    /// </summary>
    public static class ZoneMath
    {
        /// <summary>
        /// True when <paramref name="point"/> is inside the cylinder: within
        /// <paramref name="radius"/> of <paramref name="centre"/> on the ground
        /// plane, and no lower than <paramref name="below"/> under the centre nor
        /// higher than <paramref name="height"/> above it. The rim and the caps
        /// count as inside. A non-positive radius or height contains nothing.
        /// </summary>
        public static bool Contains(Vector3 centre, float radius, float height, float below, Vector3 point)
        {
            if (radius <= 0f || height <= 0f) return false;
            float dy = point.y - centre.y;
            if (dy > height || dy < -Mathf.Max(0f, below)) return false;
            float dx = point.x - centre.x, dz = point.z - centre.z;
            return dx * dx + dz * dz <= radius * radius;
        }

        /// <summary>
        /// The point to test for a body whose bounds centre is at
        /// <paramref name="boundsCentre"/>: the zone's axis at that body's height,
        /// clamped into the cylinder's height. Feed it to the collider's nearest-
        /// point query, then <see cref="Contains"/> the result.
        /// </summary>
        public static Vector3 AxisProbe(Vector3 centre, float height, Vector3 boundsCentre)
            => new(centre.x, Mathf.Clamp(boundsCentre.y, centre.y, centre.y + Mathf.Max(0f, height)), centre.z);
    }
}
