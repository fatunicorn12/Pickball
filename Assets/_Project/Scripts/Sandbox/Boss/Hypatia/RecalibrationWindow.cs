using System;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// Her vulnerable beat after each conic volley: she drops low and takes
    /// <see cref="HypatiaConfig.RecalibrationDamageMultiplier"/> damage while she
    /// re-aims the astrolabe. This class only keeps the time and raises the edges;
    /// the brain reacts (hover target, damage filter, conic interval) and forwards
    /// the edges as its own events for presentation.
    ///
    /// It counts on HER clock (the dt passed to <see cref="Tick"/> is already scaled
    /// by her slow). So a Freeze Zone or a stun on her LENGTHENS the window in real
    /// time: slowing her is how the player buys a longer punish. That is intended.
    ///
    /// Plain C#, serializable so an open window survives a script reload (its
    /// events do not; the brain re-subscribes in OnEnable). EditMode-tested.
    /// </summary>
    [Serializable]
    public sealed class RecalibrationWindow
    {
        [SerializeField] private float _remaining;
        [SerializeField] private float _duration;
        [SerializeField] private bool _isOpen;

        /// <summary>Raised once per <see cref="Open"/>, with the window's length in
        /// seconds on her clock.</summary>
        public event Action<float> Opened;

        /// <summary>Raised once when an open window closes, by running out or by
        /// <see cref="Close"/>.</summary>
        public event Action Closed;

        /// <summary>True while she is recalibrating.</summary>
        public bool IsOpen => _isOpen;

        /// <summary>Seconds left on her clock; 0 when closed.</summary>
        public float Remaining => _isOpen ? Mathf.Max(0f, _remaining) : 0f;

        /// <summary>Length the current (or last) window opened with.</summary>
        public float Duration => _duration;

        /// <summary>
        /// Open for <paramref name="duration"/> seconds on her clock. False (and no
        /// event) when already open: one volley earns one window. A zero, negative
        /// or NaN duration opens and closes at once, so both edges still fire and
        /// the brain's conic cycle still advances.
        /// </summary>
        public bool Open(float duration)
        {
            if (_isOpen) return false;

            float d = float.IsNaN(duration) || duration < 0f ? 0f : duration;
            _isOpen = true;
            _duration = d;
            _remaining = d;
            Opened?.Invoke(d);
            if (d <= 0f) Close();
            return true;
        }

        /// <summary>Advance by a dt already on her clock. Zero or NaN does nothing, so
        /// a full freeze holds the window open exactly.</summary>
        public void Tick(float scaledDt)
        {
            if (!_isOpen || !(scaledDt > 0f)) return;
            _remaining -= scaledDt;
            if (_remaining <= 0f) Close();
        }

        /// <summary>End it now (her death, the player's). No event when already closed.</summary>
        public void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;
            _remaining = 0f;
            Closed?.Invoke();
        }
    }
}
