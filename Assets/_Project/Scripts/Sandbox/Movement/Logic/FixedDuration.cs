using UnityEngine;

namespace Pickleball.Sandbox.Movement.Logic
{
    /// <summary>
    /// Time-stepping for moves that last a fixed time (the dash): each frame
    /// spends at most what is left, so the move covers exactly speed × duration
    /// at any frame rate.
    ///
    /// Stepping by the whole frame instead overshoots on the last frame by up to
    /// one frame's worth: QA measured the "fixed" 3.08 m dash at about 3.27 m
    /// (QA-Assessment MV1). Gaps and ledges are designed against that exact reach,
    /// so the overshoot is a bug, not jitter.
    /// </summary>
    public static class FixedDuration
    {
        /// <summary>Spends this frame's share of <paramref name="remaining"/>: returns
        /// the seconds to integrate (never more than what is left, never negative)
        /// and subtracts them.</summary>
        public static float Consume(ref float remaining, float dt)
        {
            float step = Mathf.Clamp(dt, 0f, Mathf.Max(remaining, 0f));
            remaining -= step;
            return step;
        }
    }
}
