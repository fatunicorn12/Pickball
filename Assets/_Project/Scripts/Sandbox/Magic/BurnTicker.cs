using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// The clock of a burn (damage over time): how many ticks fall due in a
    /// frame, and what re-igniting a burning target does. Kept out of
    /// <see cref="BurnStatus"/> so the arithmetic is tested at several frame
    /// rates without a scene.
    ///
    /// <b>Exact tick count.</b> A burn of D seconds ticking every I seconds
    /// deals floor(D / I) ticks, the first one I seconds after ignition, at any
    /// frame rate: a long frame owes several ticks at once, and time past the
    /// end of the burn is never counted.
    ///
    /// <b>Re-igniting refreshes, never stacks.</b> The remaining time becomes
    /// the longer of what was left and the new duration, the damage per tick
    /// the larger of the two, and the tick phase is kept — so spamming fire on
    /// one target cannot tick faster or start a second burn alongside the first.
    /// </summary>
    public sealed class BurnTicker
    {
        // A tick due at exactly the end must not be lost to float sums.
        const float Epsilon = 1e-4f;

        float _damagePerTick;
        float _interval;
        float _remaining;
        float _sinceTick;

        /// <summary>Damage each tick deals.</summary>
        public float DamagePerTick => _damagePerTick;

        /// <summary>Seconds between ticks.</summary>
        public float Interval => _interval;

        /// <summary>Seconds of burning left.</summary>
        public float Remaining => _remaining;

        /// <summary>True while time remains and a tick could still fall due.</summary>
        public bool IsBurning => _remaining > Epsilon && _interval > 0f && _damagePerTick > 0f;

        /// <summary>Starts a burn, or refreshes a running one (see the class summary).
        /// Non-positive values for any argument leave a running burn unchanged and
        /// never start one.</summary>
        public void Ignite(float damagePerTick, float interval, float duration)
        {
            if (damagePerTick <= 0f || interval <= 0f || duration <= 0f) return;

            if (!IsBurning)
            {
                _damagePerTick = damagePerTick;
                _interval = interval;
                _remaining = duration;
                _sinceTick = 0f;
                return;
            }

            _damagePerTick = Mathf.Max(_damagePerTick, damagePerTick);
            _remaining = Mathf.Max(_remaining, duration);
            // The interval follows the newest burn but keeps the phase, clamped so
            // a shorter interval cannot owe a burst of back-dated ticks.
            _interval = interval;
            _sinceTick = Mathf.Min(_sinceTick, interval);
        }

        /// <summary>Advances <paramref name="dt"/> seconds and returns how many
        /// ticks fell due (0 or more). Time beyond the burn's end is ignored.</summary>
        public int Tick(float dt)
        {
            if (!IsBurning || dt <= 0f) return 0;

            float step = Mathf.Min(dt, _remaining);
            _remaining -= step;
            _sinceTick += step;

            int ticks = Mathf.FloorToInt((_sinceTick + Epsilon) / _interval);
            if (ticks > 0) _sinceTick = Mathf.Max(0f, _sinceTick - ticks * _interval);
            if (_remaining <= Epsilon) _remaining = 0f;
            return ticks;
        }

        /// <summary>Puts the burn out now.</summary>
        public void Extinguish()
        {
            _remaining = 0f;
            _sinceTick = 0f;
        }
    }
}
