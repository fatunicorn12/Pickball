using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Burning
{
    /// <summary>
    /// The geometry of the Rete's concentric floor bands, as plain functions so
    /// the burn's rules are tested without a scene.
    ///
    /// Bands are described by their outer radii, innermost first, as in
    /// <see cref="HypatiaConfig.BandOuterRadii"/>. Band 0 is the hub, from the
    /// centre to <c>radii[0]</c>; band i runs from <c>radii[i-1]</c> to
    /// <c>radii[i]</c>.
    ///
    /// <b>Edges.</b> A radius exactly on an edge belongs to the INNER band:
    /// band 0 is [0, r0], band i is (r(i-1), ri]. Bands burn from the outside
    /// in, so the inner band is the one that burns later; a player balanced on
    /// the line is given the benefit of the doubt. A radius beyond the last
    /// edge (the arena radius) is outside the plate: no band, never burning.
    /// </summary>
    public static class BandMath
    {
        /// <summary>Distances closer than this, m, count as equal. Keeps a band
        /// whose inner edge is 5.99999 m from failing a 6 m safe radius, and
        /// merges radii a designer meant to be the same.</summary>
        public const float Epsilon = 1e-3f;

        /// <summary>
        /// Turns whatever the config holds into a usable edge list: ascending,
        /// distinct, finite, positive, and ending exactly at the arena radius.
        /// Called once per fight (it allocates), never per frame.
        ///
        /// Malformed input is repaired rather than rejected, so a typo in the
        /// Inspector gives a slightly different floor instead of no fight:
        /// <list type="bullet">
        /// <item>NaN, infinite, zero and negative entries are dropped;</item>
        /// <item>entries are sorted (an unsorted array keeps its edges);</item>
        /// <item>entries beyond the arena radius are dropped (the plate ends
        /// there), and near-duplicates (within <see cref="Epsilon"/>) merge;</item>
        /// <item>if the last edge falls short of the arena radius, the arena
        /// radius is APPENDED as one more band, so every edge the designer
        /// wrote survives and the whole plate is covered; an edge within
        /// <see cref="Epsilon"/> of it snaps onto it;</item>
        /// <item>null or empty input gives a single band, the whole plate as
        /// the hub, which never burns.</item>
        /// </list>
        /// A non-positive or NaN arena radius falls back to the largest valid
        /// entry, or 1 m if there is none.
        /// </summary>
        public static float[] Sanitise(float[] raw, float arenaRadius)
        {
            var edges = new List<float>(raw?.Length + 1 ?? 1);
            if (raw != null)
                foreach (float r in raw)
                    if (IsUsable(r)) edges.Add(r);

            float arena = arenaRadius;
            if (!IsUsable(arena))
            {
                arena = 1f;
                foreach (float r in edges) arena = Mathf.Max(arena, r);
            }

            edges.Sort();
            var result = new List<float>(edges.Count + 1);
            foreach (float r in edges)
            {
                if (r > arena + Epsilon) break; // sorted: the rest are beyond too
                if (result.Count > 0 && r - result[result.Count - 1] < Epsilon) continue;
                result.Add(r);
            }

            if (result.Count > 0 && arena - result[result.Count - 1] < Epsilon)
                result[result.Count - 1] = arena;
            else
                result.Add(arena);
            return result.ToArray();
        }

        static bool IsUsable(float r) => !float.IsNaN(r) && !float.IsInfinity(r) && r > 0f;

        /// <summary>Inner radius of <paramref name="band"/>, m: 0 for the hub.</summary>
        public static float InnerRadius(float[] radii, int band)
            => band <= 0 || radii == null || radii.Length == 0
                ? 0f
                : radii[Mathf.Min(band, radii.Length) - 1];

        /// <summary>Outer radius of <paramref name="band"/>, m.</summary>
        public static float OuterRadius(float[] radii, int band)
            => radii == null || radii.Length == 0 ? 0f : radii[Mathf.Clamp(band, 0, radii.Length - 1)];

        /// <summary>
        /// The band containing a horizontal distance from the centre (edges
        /// belong to the inner band, see the class summary). -1 when the
        /// distance is beyond the plate or NaN. A negative distance (only
        /// possible from bad input) is treated as the centre.
        /// </summary>
        public static int BandAt(float[] radii, float radius)
        {
            if (radii == null || float.IsNaN(radius)) return -1;
            if (radius < 0f) radius = 0f;
            for (int i = 0; i < radii.Length; i++)
                if (radius <= radii[i]) return i;
            return -1;
        }

        /// <summary>
        /// May this band ever burn? Never the hub (band 0, whatever the safe
        /// radius says, so a zero safe radius cannot burn the whole plate), and
        /// never a band whose inner edge is inside <paramref name="minSafeRadius"/>
        /// (within <see cref="Epsilon"/>). A NaN safe radius counts as 0.
        /// </summary>
        public static bool IsBurnable(float[] radii, int band, float minSafeRadius)
        {
            if (radii == null || band < 1 || band >= radii.Length) return false;
            if (float.IsNaN(minSafeRadius)) minSafeRadius = 0f;
            return InnerRadius(radii, band) >= minSafeRadius - Epsilon;
        }

        /// <summary>How many bands may ever burn: the outermost ones, down to
        /// the first band that <see cref="IsBurnable"/> refuses.</summary>
        public static int BurnableCount(float[] radii, float minSafeRadius)
        {
            if (radii == null) return 0;
            int count = 0;
            for (int i = radii.Length - 1; i >= 1 && IsBurnable(radii, i, minSafeRadius); i--) count++;
            return count;
        }

        /// <summary>
        /// The safe radius once the <paramref name="burns"/> outermost bands are
        /// alight: the inner edge of the innermost burned band, or the arena
        /// radius before any burn. Clamped to the burnable bands, so asking for
        /// more burns than the floor allows stops at the minimum safe radius.
        /// </summary>
        public static float SafeRadiusAfter(float[] radii, int burns, float minSafeRadius)
        {
            if (radii == null || radii.Length == 0) return 0f;
            int k = Mathf.Clamp(burns, 0, BurnableCount(radii, minSafeRadius));
            return k == 0 ? radii[radii.Length - 1] : InnerRadius(radii, radii.Length - k);
        }

        /// <summary>
        /// A point in the annulus between <paramref name="inner"/> and
        /// <paramref name="outer"/>, as a horizontal offset (Y = 0) from the
        /// centre. <paramref name="u"/> and <paramref name="v"/> are uniform 0-1
        /// draws; the radius uses the square root so points are uniform by AREA
        /// (a plain lerp would crowd them at the inner edge).
        /// </summary>
        public static Vector3 PointInAnnulus(float inner, float outer, float u, float v)
        {
            inner = Mathf.Max(0f, inner);
            outer = Mathf.Max(inner, outer);
            float r = Mathf.Sqrt(Mathf.Lerp(inner * inner, outer * outer, Mathf.Clamp01(u)));
            float angle = Mathf.Clamp01(v) * 2f * Mathf.PI;
            return new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
        }

        /// <summary>Horizontal distance from <paramref name="centre"/>, m (Y ignored).</summary>
        public static float HorizontalDistance(Vector3 centre, Vector3 point)
        {
            float dx = point.x - centre.x, dz = point.z - centre.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
