using UnityEngine;

namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// The voice level → flap angle mapping behind <see cref="MouthFlap"/>: RMS of the output
    /// samples, a noise floor and a full-open level, then attack/release smoothing. Plain static
    /// functions so the mapping is tested without audio, and the same at any frame rate.
    /// </summary>
    public static class MouthFlapMath
    {
        /// <summary>
        /// Root mean square of the first <paramref name="count"/> samples (0 for none). Loops over
        /// a caller-owned buffer, so it allocates nothing.
        /// </summary>
        public static float Rms(float[] samples, int count)
        {
            if (samples == null) return 0f;
            int n = Mathf.Min(count, samples.Length);
            if (n <= 0) return 0f;
            double sum = 0d;
            for (int i = 0; i < n; i++) sum += samples[i] * samples[i];
            return (float)System.Math.Sqrt(sum / n);
        }

        /// <summary>
        /// Openness, 0..1, from an RMS level: 0 at or below <paramref name="noiseFloor"/>, 1 at or
        /// above <paramref name="fullOpen"/>, linear between. Silence, breaths and room tone under
        /// the floor keep the mouth shut.
        /// </summary>
        public static float Openness(float rms, float noiseFloor, float fullOpen)
        {
            if (rms <= noiseFloor) return 0f;
            if (fullOpen <= noiseFloor) return 1f;
            return Mathf.Clamp01((rms - noiseFloor) / (fullOpen - noiseFloor));
        }

        /// <summary>
        /// Ease the shown openness toward the target: <paramref name="attackRate"/> while opening,
        /// <paramref name="releaseRate"/> while closing, both 1/s, exponential so the result after
        /// a given time doesn't depend on how many frames it took.
        /// </summary>
        public static float Smooth(float current, float target, float attackRate, float releaseRate, float dt)
        {
            if (dt <= 0f) return current;
            float rate = target > current ? attackRate : releaseRate;
            if (rate <= 0f) return target;
            return target + (current - target) * Mathf.Exp(-rate * dt);
        }

        /// <summary>The flap angle in degrees for an openness (linear in the openness).</summary>
        public static float Angle(float openness, float maxAngle)
        {
            return Mathf.Clamp01(openness) * maxAngle;
        }
    }
}
