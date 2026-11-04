using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// The honest parts of the ring's look, as plain maths so they can be tested:
    /// a low ring is drawn exactly as tall as it hits, a tall ring keeps going, and
    /// the bright gap edges sit exactly where the gameplay gap ends. Art must not
    /// lie about a dodge. Used by <see cref="CelestialRingView"/>.
    /// </summary>
    public static class RingLook
    {
        /// <summary>The wall's drawn height, m. A low ring stops at
        /// <paramref name="visualHeight"/>, its real top; a tall ring (which hits at
        /// any height) carries on by <paramref name="auroraHeight"/> and fades out.</summary>
        public static float DrawnHeight(float visualHeight, bool tall, float auroraHeight)
        {
            float solid = Mathf.Max(0.01f, visualHeight);
            return tall ? solid + Mathf.Max(0f, auroraHeight) : solid;
        }

        /// <summary>The azimuth of one gap edge, degrees: <paramref name="side"/> is
        /// -1 or +1. Same convention as the ring's gap centre.</summary>
        public static float GapEdgeAzimuth(float gapCentreDegrees, float gapWidthDegrees, int side) =>
            gapCentreDegrees + (side < 0 ? -0.5f : 0.5f) * gapWidthDegrees;

        /// <summary>How many sparks are due this frame at
        /// <paramref name="perSecond"/>, carrying the remainder in
        /// <paramref name="owed"/> so the rate holds at any frame rate. Never more
        /// than <paramref name="cap"/>, so a long hitch cannot dump a burst.</summary>
        public static int SparksDue(ref float owed, float perSecond, float deltaTime, int cap)
        {
            if (perSecond <= 0f || deltaTime <= 0f) return 0;
            owed += perSecond * deltaTime;
            int due = Mathf.FloorToInt(owed);
            owed -= due;
            return Mathf.Min(due, Mathf.Max(0, cap));
        }
    }
}
