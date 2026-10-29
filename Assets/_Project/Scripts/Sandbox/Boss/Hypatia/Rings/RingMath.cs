using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Rings
{
    /// <summary>
    /// The pure maths of Celestial Rings: the gap spiral, the gap test and the
    /// swept ring-versus-player contact. No Unity objects, so every rule the
    /// player has to learn (where the gap is, what height clears a ring, whether
    /// a fast ring can skip over you) is pinned by EditMode tests.
    ///
    /// <para><b>Azimuth convention</b> (presentation relies on it): degrees in
    /// [0, 360), measured around the arena centre in the horizontal plane;
    /// 0° points along world +Z (forward) and the angle grows toward +X, so it
    /// runs clockwise seen from above. 90° is +X, 180° is −Z, 270° is −X.
    /// <see cref="DirectionFromAzimuth"/> and <see cref="AzimuthDegrees"/> convert.
    /// </para>
    /// </summary>
    public static class RingMath
    {
        /// <summary>
        /// Wrap any angle into [0, 360). Gap centres and azimuths are always
        /// stored this way so equality and the wraparound test stay simple.
        /// </summary>
        public static float NormaliseDegrees(float degrees)
        {
            float d = degrees % 360f;
            if (d < 0f) d += 360f;
            // -1e-7 % 360 + 360 rounds to exactly 360 in float.
            return d >= 360f ? 0f : d;
        }

        /// <summary>
        /// Round to the nearest multiple of <paramref name="snap"/> (halves round
        /// up, unlike Mathf.Round's banker's rounding, so 22.5° on a 45° grid is
        /// predictably 45°). A non-positive snap leaves the value alone. Gaps snap
        /// to 45° so the 45° Angle Dash always lines up with one.
        /// </summary>
        public static float Snap(float degrees, float snap)
        {
            if (snap <= 0f) return degrees;
            return Mathf.Floor(degrees / snap + 0.5f) * snap;
        }

        /// <summary>
        /// Gap centre of ring <paramref name="ringIndex"/> (0 = the fight's first
        /// ring): the snapped first gap, turned by the snapped step once per ring
        /// in the direction of <paramref name="stepSign"/>, so successive gaps
        /// trace a readable spiral. A step that snaps to 0 keeps every gap in the
        /// same place. Result in [0, 360).
        /// </summary>
        /// <param name="stepSign">Spiral direction; only its sign is used (0 counts as +).</param>
        public static float GapCentreDegrees(int ringIndex, float firstGap, int stepSign,
                                             float step, float snap)
        {
            float sign = stepSign < 0 ? -1f : 1f;
            float turn = Snap(step, snap) * sign;
            // Wrap the per-ring turn first so a long fight never loses float precision.
            float total = NormaliseDegrees(turn) * (ringIndex % 360);
            return NormaliseDegrees(Snap(firstGap, snap) + total);
        }

        /// <summary>
        /// Is ring <paramref name="ringIndex"/> (0-based) tall? Every
        /// <paramref name="every"/>-th ring is: with 3, rings 2, 5, 8… (the 3rd,
        /// 6th, 9th). 1 makes every ring tall; 0 or less makes none tall.
        /// </summary>
        public static bool IsTall(int ringIndex, int every)
        {
            return every > 0 && (ringIndex + 1) % every == 0;
        }

        /// <summary>Unsigned angle between two azimuths, degrees in [0, 180].</summary>
        public static float AngularDistance(float a, float b)
        {
            float d = NormaliseDegrees(a - b);
            return d > 180f ? 360f - d : d;
        }

        /// <summary>
        /// Is a point at <paramref name="playerAzimuthDeg"/> inside a gap of
        /// <paramref name="gapWidthDeg"/> centred on <paramref name="gapCentreDeg"/>?
        /// Edges count as inside. Works across 0/360.
        /// </summary>
        public static bool IsInGap(float playerAzimuthDeg, float gapCentreDeg, float gapWidthDeg)
        {
            return AngularDistance(playerAzimuthDeg, gapCentreDeg) <= gapWidthDeg * 0.5f;
        }

        /// <summary>
        /// The angle, degrees, a body of <paramref name="bodyRadius"/> covers on
        /// each side of its own azimuth when it stands <paramref name="distance"/>
        /// from the centre: asin(r/d), the half-angle of the tangent lines (≈ r/d
        /// in radians for small bodies). 90° when the body covers the centre.
        /// </summary>
        public static float AngularHalfWidth(float bodyRadius, float distance)
        {
            if (bodyRadius <= 0f) return 0f;
            if (distance <= bodyRadius) return 90f;
            return Mathf.Asin(bodyRadius / distance) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Does the player's whole capsule fit through the gap? The capsule's
        /// angular half-width at its distance is added to the player's offset
        /// from the gap centre, so standing with the edge of your body in the
        /// solid ring still gets you hit: the gap must actually be threaded.
        /// Narrow gaps near her are therefore harder than the same gap at the rim.
        /// </summary>
        public static bool CapsuleFitsGap(float playerAzimuthDeg, float playerDistance,
                                          float playerRadius, float gapCentreDeg,
                                          float gapWidthDeg)
        {
            float margin = AngularHalfWidth(playerRadius, playerDistance);
            return AngularDistance(playerAzimuthDeg, gapCentreDeg) + margin <= gapWidthDeg * 0.5f;
        }

        /// <summary>
        /// Did the ring's band touch the player's body this frame, radially?
        ///
        /// Both move linearly within the frame (ring radius
        /// <paramref name="ringPrev"/>→<paramref name="ringNow"/>, player distance
        /// <paramref name="playerPrev"/>→<paramref name="playerNow"/>), so their
        /// signed separation is linear too. They touched if it changed sign
        /// (crossed) or its smaller end is within half the thickness plus the
        /// player's radius. This is the exact answer for linear motion, so:
        /// a ring stepping further than its thickness in one frame cannot tunnel
        /// past a standing player, and a player fleeing outward just ahead of the
        /// ring is not falsely hit (a plain overlap of the two swept intervals
        /// would hit them). Ignores the gap and height; see <see cref="RingHitsPlayer"/>.
        /// </summary>
        public static bool BandSweepsPlayer(float ringPrev, float ringNow, float thickness,
                                            float playerPrev, float playerNow, float playerRadius)
        {
            float reach = thickness * 0.5f + Mathf.Max(0f, playerRadius);
            float s0 = playerPrev - ringPrev;
            float s1 = playerNow - ringNow;
            if ((s0 < 0f && s1 > 0f) || (s0 > 0f && s1 < 0f)) return true;
            return Mathf.Min(Mathf.Abs(s0), Mathf.Abs(s1)) <= reach;
        }

        /// <summary>
        /// Can a ring of this height reach feet this high? A tall (or burning)
        /// ring hits at ANY height: chained jumps reach ~9 m and the Gravity
        /// Anchor more, so a finite wall would always be jumpable and only the gap
        /// is meant to get through. A low ring hits only feet below its top.
        /// </summary>
        public static bool ReachesHeight(bool hitsAnyHeight, float playerFeetHeight,
                                         float lowRingHeight)
        {
            return hitsAnyHeight || playerFeetHeight < lowRingHeight;
        }

        /// <summary>
        /// The whole contact rule for one ring and the player this frame: the
        /// band swept across the player (<see cref="BandSweepsPlayer"/>), the
        /// player is not threading the gap (<see cref="CapsuleFitsGap"/>, judged
        /// at this frame's position) and the ring is tall enough
        /// (<see cref="ReachesHeight"/>, this frame's feet). Hitting at most once
        /// per ring is the caller's job (<see cref="RingField"/>).
        /// </summary>
        public static bool RingHitsPlayer(float ringPrev, float ringNow, float thickness,
                                          float gapCentreDeg, float gapWidthDeg,
                                          bool hitsAnyHeight, float lowRingHeight,
                                          float playerPrevDistance, float playerNowDistance,
                                          float playerAzimuthDeg, float playerFeetHeight,
                                          float playerRadius)
        {
            if (!BandSweepsPlayer(ringPrev, ringNow, thickness,
                                  playerPrevDistance, playerNowDistance, playerRadius))
                return false;
            if (!ReachesHeight(hitsAnyHeight, playerFeetHeight, lowRingHeight)) return false;
            return !CapsuleFitsGap(playerAzimuthDeg, playerNowDistance, playerRadius,
                                   gapCentreDeg, gapWidthDeg);
        }

        /// <summary>
        /// Azimuth of a horizontal offset from the centre (x = world X, y = world
        /// Z), in the convention above. Zero offset gives 0°.
        /// </summary>
        public static float AzimuthDegrees(Vector2 offsetXZ)
        {
            if (offsetXZ.sqrMagnitude < 1e-12f) return 0f;
            return NormaliseDegrees(Mathf.Atan2(offsetXZ.x, offsetXZ.y) * Mathf.Rad2Deg);
        }

        /// <summary>
        /// Unit horizontal world direction for an azimuth: 0° → +Z, 90° → +X.
        /// Presentation multiplies by a ring's radius to place its gap.
        /// </summary>
        public static Vector3 DirectionFromAzimuth(float azimuthDeg)
        {
            float rad = azimuthDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
        }
    }
}
