namespace Pickleball.Sandbox.Juggle
{
    /// <summary>What one <see cref="JuggleState.Tick"/> reports about the ground.</summary>
    public enum JuggleLanding
    {
        /// <summary>Still airborne, or was never off the ground.</summary>
        None = 0,
        /// <summary>Touched down this tick; the air-hit count has reset.</summary>
        Landed = 1,
        /// <summary>Touched down this tick carrying a defeat that happened in the
        /// air. The owner should die now (Castle Crashers: enemies only die on
        /// the ground).</summary>
        LandedDefeated = 2,
    }
}
