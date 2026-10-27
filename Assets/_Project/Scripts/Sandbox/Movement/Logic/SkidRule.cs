using UnityEngine;

namespace Pickleball.Sandbox.Movement.Logic
{
    /// <summary>
    /// When a stick reversal becomes a skid turn instead of an ordinary turn.
    ///
    /// Sunshine (like Mario 64) skids when the stick points far enough away from
    /// the direction of travel while running fast; anything gentler is steered.
    /// Kept as a pure rule so the threshold is testable and both the ground and
    /// run-stop states ask exactly the same question.
    /// </summary>
    public static class SkidRule
    {
        /// <summary>Angle in degrees between the planar travel direction and the
        /// planar stick direction (0 = straight ahead, 180 = straight back).</summary>
        public static float ReversalAngle(Vector3 travel, Vector3 desired)
        {
            travel.y = 0f;
            desired.y = 0f;
            if (travel.sqrMagnitude < 1e-6f || desired.sqrMagnitude < 1e-6f) return 0f;
            return Vector3.Angle(travel, desired);
        }

        /// <summary>True when the stick input asks for a skid turn: moving at
        /// least <paramref name="minSpeed"/> m/s, the stick pushed at least
        /// <paramref name="minStick"/> (0–1), and pointing at least
        /// <paramref name="minAngle"/>° away from travel.</summary>
        public static bool ShouldSkid(Vector3 travel, Vector3 desired, float stickMagnitude,
                                      float speed, float minSpeed, float minStick, float minAngle)
        {
            if (speed < minSpeed || stickMagnitude < minStick) return false;
            return ReversalAngle(travel, desired) >= minAngle;
        }
    }
}
