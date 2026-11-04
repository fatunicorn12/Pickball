using UnityEngine;

namespace Pickleball.Sandbox.Enemies
{
    /// <summary>
    /// When a strike lands: one every interval while the target stays in reach, with a
    /// short delay on first getting there so contact is not instant.
    ///
    /// <b>It reads a clock rather than summing dt, and never catches up.</b> The next
    /// strike is due exactly <c>interval</c> after the last one landed. That is what
    /// keeps it working against the player's <see cref="Health"/>, whose hit grace is
    /// also 0.5 s and is refused on <c>now &lt; until</c>. A timer that made up for frame
    /// overshoot would fire a little under 0.5 s after the frame the last hit actually
    /// landed on, and the player's i-frames would silently eat that hit, roughly every
    /// other strike. Reading the same clock gives the same float, so a strike is due on
    /// the first frame the grace has closed. The price is that the real rate is at most
    /// one per interval, a frame's overshoot under it at low frame rates.
    ///
    /// Plain C#, built lazily by its owner because a hot reload nulls it.
    /// </summary>
    public sealed class AttackCadence
    {
        float _readyAt = float.NegativeInfinity;
        bool _wasInReach;

        /// <summary>
        /// Advance to <paramref name="now"/>. True on the frame a strike should land.
        /// </summary>
        /// <param name="now">Current time, s. Must be the clock the target's
        /// <see cref="Health"/> uses (<see cref="Time.time"/> in play).</param>
        /// <param name="inReach">Whether it may strike this frame.</param>
        /// <param name="interval">Seconds between strikes.</param>
        /// <param name="firstHitDelay">Seconds from entering reach to the first strike.
        /// Stepping out and back never shortens the wait for the next strike.</param>
        public bool Tick(float now, bool inReach, float interval, float firstHitDelay)
        {
            if (!inReach)
            {
                _wasInReach = false;
                return false;
            }
            if (!_wasInReach)
            {
                _wasInReach = true;
                _readyAt = Mathf.Max(_readyAt, now + firstHitDelay);
            }
            if (now < _readyAt) return false;

            _readyAt = now + interval;
            return true;
        }

        /// <summary>Back to never having struck: a respawn.</summary>
        public void Reset()
        {
            _readyAt = float.NegativeInfinity;
            _wasInReach = false;
        }
    }
}
