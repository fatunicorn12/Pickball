using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Burning
{
    /// <summary>
    /// Is the player standing in the fire this frame, and how much does it cost?
    /// Plain C# so the rule is tested without a scene.
    ///
    /// Burning means both: the feet's horizontal distance from the plate's
    /// centre lies in a <see cref="BandState.Burned"/> band (beyond the plate
    /// is never burning), and the feet are less than the fire's height above the
    /// floor, so a jump carries the player over the flames. A smouldering band
    /// does not burn yet.
    /// </summary>
    public static class FireRule
    {
        /// <param name="feet">The player's root, which sits at the feet.</param>
        /// <param name="floorY">World Y of the plate.</param>
        /// <param name="fireHeight">Flames reach this far above the floor, m.</param>
        public static bool IsBurning(BurnSchedule schedule, Vector3 centre, float floorY,
                                     Vector3 feet, float fireHeight)
        {
            if (schedule == null) return false;
            if (!(feet.y - floorY < fireHeight)) return false; // NaN-safe
            return schedule.IsBurnedAt(BandMath.HorizontalDistance(centre, feet));
        }

        /// <summary>Damage for one frame of standing in it: dps × dt, never
        /// negative or NaN.</summary>
        public static float DamageThisFrame(float damagePerSecond, float dt)
        {
            float amount = damagePerSecond * dt;
            return amount > 0f ? amount : 0f;
        }
    }
}
