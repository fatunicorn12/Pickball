namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Whether the swordsman counts as airborne for combo purposes — which
    /// chain a press continues, and when the chain counters reset. Pure, fed the
    /// raw grounded flag each frame.
    ///
    /// Leaving the ground only counts after a short grace, and that one answer
    /// is used for BOTH decisions. The old code used the grace for resetting
    /// the ground chain but the raw flag for picking the track, so a press during
    /// a one-frame grounded flicker on a slope started an air step and dropped
    /// the ground chain anyway (QA CB3). The cost is that a press within the
    /// grace of a real jump plays a ground step; at 0.08 s that is a few frames.
    ///
    /// Touching the ground counts at once — no grace — so the air chain refills
    /// the moment the player lands.
    /// </summary>
    public sealed class GroundContact
    {
        float _airborneFor;

        /// <summary>The raw grounded flag from the last <see cref="Tick"/>.</summary>
        public bool IsGrounded { get; private set; } = true;

        /// <summary>Off the ground for longer than the grace.</summary>
        public bool IsAirborne { get; private set; }

        /// <summary>The chain a press made now would continue.</summary>
        public ComboTrack Track => IsAirborne ? ComboTrack.Air : ComboTrack.Ground;

        /// <summary>
        /// Advance by <paramref name="dt"/> seconds with this frame's grounded
        /// flag. Returns true on the one tick the player becomes airborne (the
        /// grace has just run out), which is when a ground chain is dropped.
        /// </summary>
        public bool Tick(bool grounded, float dt, float graceSeconds)
        {
            IsGrounded = grounded;
            _airborneFor = grounded ? 0f : _airborneFor + dt;
            bool airborne = !grounded && _airborneFor > graceSeconds;
            bool justLeft = airborne && !IsAirborne;
            IsAirborne = airborne;
            return justLeft;
        }
    }
}
