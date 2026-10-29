using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// Sizes an orbiter's ellipse so it never grazes her and never leaves the
    /// plate: periapsis ≥ the minimum and apoapsis ≤ the maximum. The needle's e is
    /// kept whenever any semi-major axis fits; when none does (a thin ellipse in a
    /// narrow band), e is lowered to the most eccentric orbit that fits rather than
    /// letting an orbiter sweep through her or off the rim. Plain C#.
    /// </summary>
    public static class OrbitFit
    {
        /// <summary>
        /// Pick a and e for an orbit whose eccentricity should be
        /// <paramref name="desiredEccentricity"/>.
        /// </summary>
        /// <param name="periapsisMin">Closest allowed pass to the focus, m.</param>
        /// <param name="apoapsisMax">Furthest allowed reach from the focus, m.</param>
        /// <param name="size01">0 picks the smallest orbit that fits, 1 the largest;
        /// the caster feeds a random number so a volley's orbits differ in size.</param>
        /// <remarks>
        /// For e: a ≥ periapsisMin/(1−e) and a ≤ apoapsisMax/(1+e). Both hold for
        /// some a iff e ≤ (apo − peri)/(apo + peri). If the bounds themselves
        /// conflict (apoapsisMax ≤ periapsisMin, a broken config) the result is a
        /// circle at periapsisMin: keeping clear of her wins over the rim.
        /// </remarks>
        public static OrbitShape Fit(float desiredEccentricity, float periapsisMin, float apoapsisMax, float size01)
        {
            float peri = Mathf.Max(1e-3f, periapsisMin);
            float apo = apoapsisMax;
            if (apo <= peri) return new OrbitShape(peri, 0f);

            float eMaxFit = (apo - peri) / (apo + peri);
            float e = Mathf.Clamp(desiredEccentricity, 0f, Mathf.Min(eMaxFit, 0.99f));

            float aMin = peri / (1f - e);
            float aMax = apo / (1f + e);
            if (aMax < aMin) aMax = aMin; // only by rounding at e == eMaxFit
            float a = Mathf.Lerp(aMin, aMax, Mathf.Clamp01(size01));
            return new OrbitShape(a, e);
        }
    }
}
