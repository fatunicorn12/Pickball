using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The screen half of "in view". What a press may lock, what the hint marks and what a held
    /// lock must stay on all use this one rule, so they can't disagree.
    /// </summary>
    public static class LockOnView
    {
        /// <summary>Whether a viewport-space point (x and y 0..1 across the screen, z metres in
        /// front of the lens) is in front of the camera and inside the screen by
        /// <paramref name="inset"/> on every edge. On the inset line counts as inside.</summary>
        /// <param name="inset">Margin as a fraction of the screen. 0 is the screen's edge.</param>
        public static bool InsideViewport(Vector3 viewport, float inset)
        {
            return viewport.z > 0f
                   && viewport.x >= inset && viewport.x <= 1f - inset
                   && viewport.y >= inset && viewport.y <= 1f - inset;
        }
    }
}
