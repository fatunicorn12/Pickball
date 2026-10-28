using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>Pure arithmetic behind the orbit camera's occlusion pull-in and
    /// vertical lag clamp, so both can be tested without a scene.</summary>
    public static class CameraOcclusion
    {
        /// <summary>
        /// Longest arm the camera may have given a sphere cast that hit geometry
        /// <paramref name="hitDistance"/> along it (negative = nothing hit):
        /// never beyond the hit, never below <paramref name="minDistance"/>,
        /// never beyond what was wanted.
        /// </summary>
        public static float FreeLength(float desired, float hitDistance, float minDistance)
        {
            if (hitDistance < 0f) return desired;
            float floor = Mathf.Min(minDistance, desired);
            return Mathf.Clamp(hitDistance, floor, desired);
        }

        /// <summary>
        /// Next pulled-in amount (metres taken off the arm). Getting blocked pulls
        /// in at once, so the lens is never inside a boulder; being free again
        /// eases back out at <paramref name="returnSpeed"/> m/s so it does not pop.
        /// </summary>
        public static float NextClip(float clip, float desired, float free, float returnSpeed, float dt)
        {
            float wanted = Mathf.Max(0f, desired - free);
            return wanted >= clip ? wanted : Mathf.MoveTowards(clip, wanted, Mathf.Max(0f, returnSpeed) * dt);
        }

        /// <summary>Keeps the smoothed camera height within <paramref name="maxLag"/>
        /// metres of where it wants to be, so follow smoothing can lag but never
        /// let the player fall out of the frame vertically. Negative = no limit.</summary>
        public static float ClampLag(float current, float wanted, float maxLag)
        {
            if (maxLag < 0f) return current;
            return Mathf.Clamp(current, wanted - maxLag, wanted + maxLag);
        }
    
        /// <summary>
        /// How far (metres, along the camera's up axis) to move the camera so a
        /// point at viewport height <paramref name="viewY"/> and distance
        /// <paramref name="depth"/> in front of the lens lands inside
        /// [<paramref name="min"/>, <paramref name="max"/>]. Zero inside the band.
        /// Negative = move down. Independent of dt: it is a position correction,
        /// not a rate.
        /// </summary>
        public static float SafeBandShift(float viewY, float depth, float tanHalfFov, float min, float max)
        {
            if (depth <= 1e-3f || tanHalfFov <= 0f) return 0f;
            float clamped = Mathf.Clamp(viewY, min, max);
            if (Mathf.Approximately(clamped, viewY)) return 0f;
            return ((2f * viewY - 1f) - (2f * clamped - 1f)) * tanHalfFov * depth;
        }
}
}
