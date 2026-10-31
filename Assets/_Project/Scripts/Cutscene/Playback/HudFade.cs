using UnityEngine;

namespace Pickleball.Cutscene.Playback
{
    /// <summary>
    /// The HUD fade step: frame-rate independent exponential smoothing toward a target alpha,
    /// snapping the last thousandth so a faded-in HUD ends at exactly 1 and a hidden one at exactly
    /// its hidden alpha. Plain maths so the 30/60/144 fps sweep is a unit test.
    /// </summary>
    public static class HudFade
    {
        /// <summary>Distance from the target under which the alpha snaps to it.</summary>
        public const float SnapEpsilon = 0.001f;

        /// <summary>One step from <paramref name="current"/> toward <paramref name="target"/>.</summary>
        public static float Step(float current, float target, float ratePerSecond, float unscaledDeltaTime)
        {
            float k = 1f - Mathf.Exp(-Mathf.Max(0f, ratePerSecond) * Mathf.Max(0f, unscaledDeltaTime));
            float next = current + (target - current) * k;
            return Mathf.Abs(next - target) < SnapEpsilon ? target : next;
        }
    }
}
