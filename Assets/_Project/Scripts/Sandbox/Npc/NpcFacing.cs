using UnityEngine;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// The yaw maths for an NPC turning to face someone: where to look, and
    /// one frame's step toward it. Static and allocation-free so it can be
    /// tested at several frame rates and called every frame.
    /// </summary>
    public static class NpcFacing
    {
        /// <summary>
        /// Yaw in degrees (0 = +Z, clockwise seen from above) that looks from
        /// <paramref name="from"/> to <paramref name="to"/> across the ground.
        /// False when the two are within <paramref name="minDistance"/>
        /// horizontally, where the direction is meaningless and would spin.
        /// </summary>
        public static bool TryYawTowards(Vector3 from, Vector3 to, out float yaw, float minDistance = 0.05f)
        {
            float dx = to.x - from.x, dz = to.z - from.z;
            if (dx * dx + dz * dz < minDistance * minDistance)
            {
                yaw = 0f;
                return false;
            }
            yaw = Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>
        /// One frame of turning from <paramref name="currentYaw"/> toward
        /// <paramref name="targetYaw"/> (degrees), the short way round.
        ///
        /// Exponential approach (<c>1 - e^(-rate·dt)</c> of the remaining
        /// angle per frame), so it eases in and is frame-rate independent,
        /// capped at <paramref name="maxDegreesPerSecond"/> so a 180° turn
        /// starts as a steady pivot instead of a snap.
        /// </summary>
        /// <param name="rate">Approach rate, 1/s. Higher turns faster.</param>
        /// <param name="maxDegreesPerSecond">Speed cap, °/s. 0 or less means uncapped.</param>
        /// <param name="dt">Frame time, s. 0 or less leaves the yaw unchanged.</param>
        public static float StepYaw(float currentYaw, float targetYaw, float rate,
                                    float maxDegreesPerSecond, float dt)
        {
            if (dt <= 0f || rate <= 0f) return currentYaw;
            float remaining = Mathf.DeltaAngle(currentYaw, targetYaw);
            float step = remaining * (1f - Mathf.Exp(-rate * dt));
            if (maxDegreesPerSecond > 0f)
            {
                float cap = maxDegreesPerSecond * dt;
                step = Mathf.Clamp(step, -cap, cap);
            }
            return currentYaw + step;
        }
    }
}
