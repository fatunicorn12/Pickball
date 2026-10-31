using UnityEngine;

namespace Pickleball.Cutscene.Actors
{
    /// <summary>
    /// The propeller's spin rules, shared by Birdy's own rotor (<see cref="CutsceneRotor"/>) and
    /// the loose prop (<see cref="PropellerProp"/>) so the two read as the same object when the
    /// cutscene swaps one for the other. Pure functions of their inputs, so they are tested
    /// without a scene and give the same result at any frame rate.
    /// </summary>
    public static class PropellerSpinMath
    {
        /// <summary>
        /// Ease a spin speed toward a target, exponentially and frame-rate independently.
        /// A response of 0 or less snaps straight to the target.
        /// </summary>
        /// <param name="current">Current speed, °/s.</param>
        /// <param name="target">Target speed, °/s.</param>
        /// <param name="response">Rate, 1/s (3 ≈ a second to change).</param>
        /// <param name="dt">Frame time, s.</param>
        public static float Approach(float current, float target, float response, float dt)
        {
            if (response <= 0f || dt <= 0f) return response <= 0f ? target : current;
            return target + (current - target) * Mathf.Exp(-response * dt);
        }

        /// <summary>
        /// Speed while winding down from <paramref name="startSpeed"/> to rest over
        /// <paramref name="seconds"/>: a quadratic ease-out, so it slows visibly at the end
        /// instead of stopping dead. Exactly 0 from <paramref name="seconds"/> on.
        /// </summary>
        public static float WindDown(float startSpeed, float elapsed, float seconds)
        {
            if (seconds <= 0f || elapsed >= seconds) return 0f;
            if (elapsed <= 0f) return startSpeed;
            float left = 1f - elapsed / seconds;
            return startSpeed * left * left;
        }

        /// <summary>Advance an angle by a speed over a frame, wrapped to [0, 360).</summary>
        public static float Advance(float angleDegrees, float speed, float dt)
        {
            return Mathf.Repeat(angleDegrees + speed * dt, 360f);
        }
    }
}
