using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// The knockback pulse's look as plain maths, so <see cref="PulseView"/> only
    /// has to apply it and the promises that matter can be tested: the drawn wave
    /// stops at exactly Pulse Reach (it is the player's evidence of whether they
    /// were inside), and the camera kick never grows with distance.
    ///
    /// Every curve is a function of age, not an integral of frames, so the same
    /// moment looks the same at any frame rate.
    /// </summary>
    public static class PulseLook
    {
        /// <summary>How far along its journey the wave is, 0..1: fast out, gentle
        /// in, so it hits hard and slows at the edge. Exactly 1 from
        /// <paramref name="expandSeconds"/> on.</summary>
        public static float WaveFraction(float age, float expandSeconds)
        {
            if (expandSeconds <= 0f) return 1f;
            float u = 1f - Mathf.Clamp01(age / expandSeconds);
            return 1f - u * u * u;
        }

        /// <summary>The wave's radius, m, <paramref name="age"/> seconds after it
        /// fired. Never passes <paramref name="reach"/>.</summary>
        public static float WaveRadius(float age, float expandSeconds, float reach) =>
            Mathf.Max(0f, reach) * WaveFraction(age, expandSeconds);

        /// <summary>The wave's brightness, 0..1: full while it travels, then falling
        /// to 0 over <paramref name="fadeSeconds"/> once it has arrived.</summary>
        public static float WaveFade(float age, float expandSeconds, float fadeSeconds)
        {
            float past = age - Mathf.Max(0f, expandSeconds);
            if (past <= 0f) return 1f;
            return fadeSeconds <= 0f ? 0f : 1f - Mathf.Clamp01(past / fadeSeconds);
        }

        /// <summary>True once the wave has arrived and fully faded.</summary>
        public static bool WaveFinished(float age, float expandSeconds, float fadeSeconds) =>
            age >= Mathf.Max(0f, expandSeconds) + Mathf.Max(0f, fadeSeconds);

        /// <summary>The flash, 1..0: a blown-out first frame, then a fast fall.
        /// 0 from <paramref name="seconds"/> on.</summary>
        public static float Flash(float age, float seconds)
        {
            if (seconds <= 0f) return 0f;
            float u = 1f - Mathf.Clamp01(age / seconds);
            return u * u;
        }

        /// <summary>How much of the build-up to show at charge fraction
        /// <paramref name="charge"/> (0..1): slow, then rushing into the release.</summary>
        public static float Swell(float charge)
        {
            float t = Mathf.Clamp01(charge);
            return t * t;
        }

        /// <summary>The dial's spin rate at charge fraction <paramref name="charge"/>,
        /// from <paramref name="restRate"/> to <paramref name="peakRate"/> (any unit).</summary>
        public static float SpinRate(float charge, float restRate, float peakRate) =>
            Mathf.Lerp(restRate, peakRate, Swell(charge));

        /// <summary>
        /// The camera kick for a viewer <paramref name="distance"/> m from her:
        /// <paramref name="atCentre"/> beside her, <paramref name="atEdge"/> at Pulse
        /// Reach, then dying away to nothing at twice the reach, so a pulse felt from
        /// outside it is still felt, just less.
        /// </summary>
        public static float ShakeAmount(float distance, float reach, float atCentre, float atEdge)
        {
            if (reach <= 0f) return 0f;
            float d = Mathf.Max(0f, distance);
            return d <= reach
                ? Mathf.Lerp(atCentre, atEdge, d / reach)
                : Mathf.Lerp(atEdge, 0f, (d - reach) / reach);
        }
    }
}
