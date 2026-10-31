using UnityEngine;

namespace Pickleball.Cutscene.Subtitles
{
    /// <summary>
    /// The fade both subtitle widgets use: a linear ramp at a fixed rate. Linear is exactly
    /// frame-rate independent (one 1/30 s step lands where two 1/60 s steps do), which an
    /// exponential chase is only approximately, and at 0.15 s the shape of the ramp is invisible
    /// anyway. Driven with unscaled time, since a cutscene may run with gameplay time paused.
    /// </summary>
    public static class SubtitleFade
    {
        /// <summary>
        /// Moves <paramref name="current"/> toward <paramref name="target"/> by
        /// <paramref name="deltaTime"/> / <paramref name="seconds"/>, without overshooting. A
        /// full 0→1 fade therefore takes <paramref name="seconds"/>; zero or less snaps.
        /// </summary>
        /// <param name="current">Current alpha, 0..1.</param>
        /// <param name="target">Alpha heading for, 0..1.</param>
        /// <param name="deltaTime">Seconds since the last step (unscaled).</param>
        /// <param name="seconds">Seconds for a full 0→1 fade.</param>
        public static float Step(float current, float target, float deltaTime, float seconds)
        {
            if (seconds <= 0f) return target;
            return Mathf.MoveTowards(current, target, Mathf.Max(0f, deltaTime) / seconds);
        }
    }
}
