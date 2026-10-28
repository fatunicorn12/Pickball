namespace Pickleball.Sandbox
{
    /// <summary>
    /// Post-hit invulnerability window, as pure arithmetic on an injected clock so
    /// it can be tested without a scene. <see cref="Health"/> owns one.
    ///
    /// The rule is about WHAT a hit is, not who sent it. A hit is "discrete" when
    /// the caller did not flag it continuous and it is at least
    /// <c>minDiscrete</c> points. Only discrete hits open a window and only
    /// discrete hits are refused inside one. Per-frame ticks (the Fluxion Beam,
    /// the gravity-well core: dps x dt, a fraction of a point) fall under the
    /// threshold, so they neither hide behind the window nor start one: the beam
    /// stays exactly as punishing as designed. A window of 0 disables the system.
    /// </summary>
    public struct HitGrace
    {
        float _until;

        /// <summary>Whether a hit of this size counts as a discrete blow.</summary>
        public static bool IsDiscrete(float amount, float minDiscrete, bool continuous)
            => !continuous && amount >= minDiscrete;

        /// <summary>True while a window is open at <paramref name="now"/>.</summary>
        public bool IsOpen(float now) => now < _until;

        /// <summary>Whether this hit is refused.</summary>
        public bool Blocks(float now, bool discrete) => discrete && IsOpen(now);

        /// <summary>Start a window of <paramref name="seconds"/> from <paramref name="now"/>.</summary>
        public void Open(float now, float seconds)
        {
            if (seconds > 0f) _until = now + seconds;
        }

        public void Clear() => _until = 0f;
    }
}
