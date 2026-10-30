using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// Her topple: the reward for batting an orbiter back into her. This class
    /// only keeps the time and raises the edges; the brain reacts (pauses her
    /// timers, drops her to the plate, doubles her damage taken, shatters the
    /// other orbiters) and forwards the edges as its own events for presentation.
    ///
    /// One topple at a time: <see cref="Begin"/> while toppled is refused, so a
    /// second ball arriving during a topple cannot extend or restart it. It counts
    /// on HER clock (the dt passed to <see cref="Tick"/> is already scaled by her
    /// slow), so a Freeze Zone on her holds her down longer, as it holds the
    /// recalibration window open; a zero dt freezes it exactly.
    ///
    /// Plain C#, serializable so a topple survives a script reload (its events do
    /// not; the brain re-subscribes in OnEnable). EditMode-tested.
    /// </summary>
    [Serializable]
    public sealed class ToppleState
    {
        [SerializeField] private float _remaining;
        [SerializeField] private float _duration;
        [SerializeField] private bool _toppled;

        /// <summary>Raised once per accepted <see cref="Begin"/>, with its length in
        /// seconds on her clock.</summary>
        public event Action<float> Started;

        /// <summary>Raised once when a topple ends, by running out or by <see cref="End"/>.</summary>
        public event Action Ended;

        /// <summary>True while she is down.</summary>
        public bool IsToppled => _toppled;

        /// <summary>Seconds left on her clock; 0 when not toppled.</summary>
        public float Remaining => _toppled ? Mathf.Max(0f, _remaining) : 0f;

        /// <summary>Length the current (or last) topple began with.</summary>
        public float Duration => _duration;

        /// <summary>
        /// Topple her for <paramref name="duration"/> seconds on her clock. False
        /// (and no event) when she is already toppled. A zero, negative or NaN
        /// duration starts and ends at once, so both edges still fire.
        /// </summary>
        public bool Begin(float duration)
        {
            if (_toppled) return false;
            float d = float.IsNaN(duration) || duration < 0f ? 0f : duration;
            _toppled = true;
            _duration = d;
            _remaining = d;
            Started?.Invoke(d);
            if (d <= 0f) End();
            return true;
        }

        /// <summary>Advance by a dt already on her clock. Zero or NaN does nothing.</summary>
        public void Tick(float scaledDt)
        {
            if (!_toppled || !(scaledDt > 0f)) return;
            _remaining -= scaledDt;
            if (_remaining <= 0f) End();
        }

        /// <summary>End it now (recovery, her death, the player's). No event when not toppled.</summary>
        public void End()
        {
            if (!_toppled) return;
            _toppled = false;
            _remaining = 0f;
            Ended?.Invoke();
        }
    }
}
