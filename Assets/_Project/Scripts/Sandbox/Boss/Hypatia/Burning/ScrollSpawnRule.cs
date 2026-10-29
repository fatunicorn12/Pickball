using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Burning
{
    /// <summary>
    /// When <see cref="ScrollKeeper"/> may put a burning scroll on the floor.
    /// Plain C# so the gating is tested without spawning anything.
    ///
    /// The gate is open only while scrolls are enabled, the fight is live and
    /// at least one band is burned (a scroll lives in the fire). While it is
    /// closed the timer is held at 0, so the first scroll comes one full
    /// interval after the first band catches. At the live-scroll cap the timer
    /// fills to one interval and waits there, so a scroll appears as soon as a
    /// slot frees rather than a whole interval later. One spawn per call at
    /// most: a long frame never bursts several scrolls out at once.
    /// </summary>
    public static class ScrollSpawnRule
    {
        /// <summary>Is the gate open at all?</summary>
        public static bool GateOpen(bool enabled, bool stopped, int burnedBands)
            => enabled && !stopped && burnedBands > 0;

        /// <summary>
        /// Advance the spawn timer by <paramref name="bossDt"/> (her clock) and
        /// say whether to spawn now. Returns the new timer.
        /// </summary>
        public static float Advance(float timer, float bossDt, float interval,
                                    bool gateOpen, int live, int maxLive, out bool spawn)
        {
            spawn = false;
            if (!gateOpen) return 0f;
            if (float.IsNaN(timer) || timer < 0f) timer = 0f;
            if (float.IsNaN(bossDt) || bossDt < 0f) bossDt = 0f;
            interval = Mathf.Max(0.01f, float.IsNaN(interval) ? 0.01f : interval);

            timer += bossDt;
            if (live >= maxLive) return Mathf.Min(timer, interval);
            if (timer < interval) return timer;

            spawn = true;
            return Mathf.Min(timer - interval, interval);
        }
    }
}
