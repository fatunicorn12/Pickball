using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// One countdown on her clock: the brain keeps one per attack (rings, conics,
    /// the needle, the burn), Newton's independent-timer pattern rather than a
    /// sequencer, so a stalled attack never holds up the others.
    ///
    /// It is fed a dt that is already scaled by her slow, and a dt of zero (or NaN)
    /// does nothing at all, so a zero-multiplier slow freezes it exactly where it
    /// is: nothing divides by the multiplier and nothing fires while frozen, even a
    /// timer that was started at zero. Plain C# and serializable, so it survives a
    /// script reload as a private field of the brain, and is EditMode-tested.
    /// </summary>
    [Serializable]
    public struct BossTimer
    {
        [SerializeField] private float _remaining;
        [SerializeField] private bool _running;

        /// <summary>True between <see cref="Start"/> and the tick it fires on (or <see cref="Stop"/>).</summary>
        public bool IsRunning => _running;

        /// <summary>Seconds on her clock until it fires; 0 when not running.</summary>
        public float Remaining => _running ? Mathf.Max(0f, _remaining) : 0f;

        /// <summary>Count down <paramref name="seconds"/> from now. Restarts a running
        /// timer. NaN or negative counts as zero, so it fires on the next unfrozen tick.</summary>
        public void Start(float seconds)
        {
            _remaining = float.IsNaN(seconds) || seconds < 0f ? 0f : seconds;
            _running = true;
        }

        /// <summary>Cancel without firing.</summary>
        public void Stop()
        {
            _running = false;
            _remaining = 0f;
        }

        /// <summary>
        /// Advance by <paramref name="scaledDt"/> (already on her clock). True on
        /// exactly the tick it runs out; it is then stopped, so the caller restarts it
        /// with whatever interval applies at that moment (phase 1 or 2).
        /// </summary>
        public bool Tick(float scaledDt)
        {
            if (!_running || !(scaledDt > 0f)) return false;
            _remaining -= scaledDt;
            if (_remaining > 0f) return false;
            Stop();
            return true;
        }
    }
}
