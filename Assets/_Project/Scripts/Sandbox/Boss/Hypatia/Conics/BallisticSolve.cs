using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// The parabola lob's maths: where to aim (lead the player) and how hard to
    /// throw so the lob lands there after exactly the flight time. Solving for a
    /// fixed time rather than a fixed speed is what lets the landing marker be an
    /// honest countdown: the marker's seconds and the lob's flight are one number.
    /// Plain C#, EditMode-tested.
    /// </summary>
    public static class BallisticSolve
    {
        /// <summary>Flight times below this are raised to it, so a zero in the
        /// config can never divide by zero.</summary>
        public const float MinFlightTime = 0.01f;

        /// <summary>
        /// Where the player will be in <paramref name="secondsAhead"/> at their
        /// current horizontal velocity, scaled by <paramref name="leadFraction"/>
        /// and capped at <paramref name="maxLead"/> metres. Vertical velocity is
        /// ignored on purpose: a jump is not a change of destination, and leading
        /// it would aim lobs into the sky.
        /// </summary>
        public static Vector3 PredictTarget(Vector3 playerPosition, Vector3 playerVelocity,
                                            float secondsAhead, float leadFraction, float maxLead)
        {
            var horizontal = new Vector3(playerVelocity.x, 0f, playerVelocity.z);
            Vector3 lead = horizontal * (Mathf.Max(0f, secondsAhead) * Mathf.Clamp01(leadFraction));
            return playerPosition + Vector3.ClampMagnitude(lead, Mathf.Max(0f, maxLead));
        }

        /// <summary>
        /// Launch velocity that carries a body from <paramref name="from"/> to
        /// <paramref name="to"/> in exactly <paramref name="flightTime"/> seconds
        /// under downward gravity of <paramref name="gravity"/> m/s²:
        /// v = (Δ − ½ g T²) / T with g pointing down.
        /// </summary>
        public static Vector3 LaunchVelocity(Vector3 from, Vector3 to, float flightTime, float gravity)
        {
            float t = Mathf.Max(MinFlightTime, flightTime);
            Vector3 g = Vector3.down * gravity;
            return ((to - from) - 0.5f * t * t * g) / t;
        }

        /// <summary>Closed-form position after <paramref name="time"/>: from + vt + ½gt².
        /// The lob samples this instead of integrating, so it cannot drift off the
        /// arc its marker promised, at any frame rate.</summary>
        public static Vector3 PositionAt(Vector3 from, Vector3 launchVelocity, float gravity, float time) =>
            from + launchVelocity * time + 0.5f * time * time * (Vector3.down * gravity);

        /// <summary>
        /// <paramref name="point"/> pulled horizontally inside a disc of
        /// <paramref name="radius"/> about <paramref name="centre"/> (Y untouched), so
        /// a lead toward the rim never aims a lob off the plate.
        /// </summary>
        public static Vector3 ClampToDisc(Vector3 point, Vector3 centre, float radius)
        {
            var offset = new Vector3(point.x - centre.x, 0f, point.z - centre.z);
            Vector3 clamped = Vector3.ClampMagnitude(offset, Mathf.Max(0f, radius));
            return new Vector3(centre.x + clamped.x, point.y, centre.z + clamped.z);
        }
    }
}
