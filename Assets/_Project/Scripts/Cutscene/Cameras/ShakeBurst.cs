using System;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// A Perlin shake burst on a beat: the noise amplitude ramps to <see cref="peak"/> over
    /// <see cref="attack"/>, holds, then decays to zero. S1_SH04's hit on "Isekai!" is one.
    /// </summary>
    [Serializable]
    public struct ShakeBurst
    {
        [Tooltip("When the burst starts.")]
        public TimeRef at;

        [Tooltip("Ramp-up time, seconds. Shorter reads as a harder hit.")]
        [Min(0f)] public float attack;

        [Tooltip("Time held at the peak, seconds.")]
        [Min(0f)] public float hold;

        [Tooltip("Decay to zero, seconds. Longer feels like an aftershock.")]
        [Min(0f)] public float decay;

        [Tooltip("Peak amplitude gain (× the noise profile). 1.2 is a strong handheld jolt.")]
        [Min(0f)] public float peak;

        /// <summary>The burst's gain <paramref name="seconds"/> after the shot starts, given its resolved start.</summary>
        public float GainAt(float start, float seconds)
        {
            float u = seconds - start;
            if (u <= 0f) return 0f;
            if (u < attack) return peak * (attack > 0f ? u / attack : 1f);
            u -= attack;
            if (u <= hold) return peak;
            u -= hold;
            if (u >= decay) return 0f;
            return peak * (1f - u / decay);
        }
    }
}
