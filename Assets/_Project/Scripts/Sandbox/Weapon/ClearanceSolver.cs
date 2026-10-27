using UnityEngine;

namespace Pickleball.Sandbox.Weapon
{
    /// <summary>
    /// The geometry behind <see cref="WeaponClearance"/>: how far a straight
    /// blade has to turn about its grip so that no part of it lies inside a
    /// sphere.
    ///
    /// Kept as pure functions of positions and directions, with no Transforms,
    /// so the maths can be pinned down by EditMode tests without a rig.
    ///
    /// The blade is the segment grip + dir·[0, length]. With D the distance from
    /// the grip to the sphere's centre and θ the angle between the blade and
    /// the centre, the blade clears once θ reaches the clearing angle:
    ///  - a blade long enough to reach the tangent point (length ≥ √(D² − R²))
    ///    clears exactly when it is tangent: θ = asin(R / D);
    ///  - a shorter blade only needs its tip to leave the sphere:
    ///    |tip − centre| = R, i.e. cos θ = (L² + D² − R²) / (2LD).
    /// Turning by (clearing angle − θ), and no more, is the smallest correction,
    /// so it vanishes smoothly as the animation carries the blade clear by
    /// itself — no pop when the correction switches off.
    /// </summary>
    public static class ClearanceSolver
    {
        /// <summary>
        /// While the grip is inside the sphere no turn about it can clear the
        /// blade. Rather than give up (the correction would snap to zero just as
        /// the hand enters), the sphere is treated as shrinking to stay this
        /// fraction of the grip's distance, so the push keeps going in the same
        /// direction and fades continuously.
        /// </summary>
        public const float InsideRadiusFraction = 0.95f;

        /// <summary>
        /// Smallest angle, in degrees, between the blade and the direction to
        /// the sphere's centre at which the whole blade lies outside the sphere.
        /// </summary>
        /// <param name="distance">Grip to centre, m. Must exceed <paramref name="radius"/>.</param>
        /// <param name="length">Blade length from the grip, m.</param>
        /// <param name="radius">Sphere radius, m.</param>
        public static float ClearingAngle(float distance, float length, float radius)
        {
            float tangentLength = Mathf.Sqrt(Mathf.Max(0f, distance * distance - radius * radius));
            if (length >= tangentLength)
                return Mathf.Asin(Mathf.Clamp01(radius / distance)) * Mathf.Rad2Deg;

            float cos = (length * length + distance * distance - radius * radius)
                        / (2f * length * distance);
            return Mathf.Acos(Mathf.Clamp(cos, -1f, 1f)) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// How far, in degrees, the blade must turn away from the sphere's
        /// centre to clear it. Zero when it already clears.
        /// </summary>
        /// <param name="grip">World position of the blade's grip end.</param>
        /// <param name="dir">Unit direction from the grip to the tip.</param>
        /// <param name="length">Blade length, m.</param>
        /// <param name="centre">World centre of the sphere.</param>
        /// <param name="radius">Sphere radius, m.</param>
        public static float RequiredAngle(Vector3 grip, Vector3 dir, float length,
                                          Vector3 centre, float radius)
        {
            if (length <= 0f || radius <= 0f) return 0f;

            Vector3 toCentre = centre - grip;
            float distance = toCentre.magnitude;
            if (distance < 1e-5f) return 0f;

            float effectiveRadius = Mathf.Min(radius, distance * InsideRadiusFraction);
            float angle = Vector3.Angle(dir, toCentre);
            return Mathf.Max(0f, ClearingAngle(distance, length, effectiveRadius) - angle);
        }

        /// <summary>
        /// Axis to turn the blade about so a positive angle swings it away from
        /// the sphere's centre — the plane holding the blade and the centre.
        /// Falls back to <paramref name="fallback"/> (made perpendicular) when the
        /// blade points straight at or away from the centre and that plane is
        /// undefined.
        /// </summary>
        public static Vector3 AwayAxis(Vector3 grip, Vector3 dir, Vector3 centre, Vector3 fallback)
        {
            Vector3 toCentre = centre - grip;
            Vector3 axis = Vector3.Cross(toCentre, dir);
            if (axis.sqrMagnitude > 1e-8f) return axis.normalized;

            axis = Vector3.Cross(toCentre, fallback);
            if (axis.sqrMagnitude > 1e-8f) return axis.normalized;

            // Fallback parallel to the blade too: any perpendicular will do.
            axis = Vector3.Cross(toCentre, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right);
            return axis.normalized;
        }
    }
}
