using UnityEngine;

namespace Pickleball.Cutscene.Playback
{
    /// <summary>
    /// Hold-to-skip logic: accumulates unscaled time while the skip input is held, resets when it
    /// is released, and fires exactly once when the hold reaches <see cref="HoldSeconds"/>.
    /// <see cref="Progress"/> (0..1) drives the indicator. A press that is already down when the
    /// cutscene starts (the jump that landed on the pickup, a held Enter) is ignored until it is
    /// released, so nobody skips a cutscene they never meant to. Plain C#; the player feeds it.
    /// </summary>
    public sealed class SkipHold
    {
        float _holdSeconds;
        float _held;
        bool _fired;
        bool _waitForRelease;

        /// <summary>Creates a hold of <paramref name="holdSeconds"/>.</summary>
        public SkipHold(float holdSeconds)
        {
            HoldSeconds = holdSeconds;
        }

        /// <summary>Seconds to hold. Never below 0.01.</summary>
        public float HoldSeconds
        {
            get => _holdSeconds;
            set => _holdSeconds = Mathf.Max(0.01f, value);
        }

        /// <summary>0 when not held, rising to 1 as the hold completes; 1 once fired.</summary>
        public float Progress => _fired ? 1f : Mathf.Clamp01(_held / _holdSeconds);

        /// <summary>True once the hold has completed, until <see cref="Arm"/>.</summary>
        public bool Fired => _fired;

        /// <summary>True while a press from before <see cref="Arm"/> is still down and being ignored.</summary>
        public bool WaitingForRelease => _waitForRelease;

        /// <summary>
        /// Resets for a new cutscene. Pass whether the input is down right now: if it is, that press
        /// is ignored until released.
        /// </summary>
        public void Arm(bool heldNow)
        {
            _held = 0f;
            _fired = false;
            _waitForRelease = heldNow;
        }

        /// <summary>
        /// Advances by <paramref name="unscaledDeltaTime"/>. Returns true on the one tick the hold
        /// completes.
        /// </summary>
        public bool Tick(bool held, float unscaledDeltaTime)
        {
            if (_waitForRelease)
            {
                if (!held) _waitForRelease = false;
                return false;
            }
            if (_fired) return false;
            if (!held)
            {
                _held = 0f;
                return false;
            }
            _held += Mathf.Max(0f, unscaledDeltaTime);
            if (_held < _holdSeconds) return false;
            _fired = true;
            return true;
        }
    }
}
