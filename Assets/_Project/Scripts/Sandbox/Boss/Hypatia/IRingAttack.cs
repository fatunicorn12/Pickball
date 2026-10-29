namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>Celestial Rings, as the brain drives them.</summary>
    public interface IRingAttack : IHypatiaAttack
    {
        /// <summary>
        /// Forecast the next ring's gap on the floor now and launch the ring
        /// <see cref="HypatiaConfig.GapForecastLead"/> later (on her clock). The
        /// ring picks its own gap (the spiral sequence) and height (tall every Nth;
        /// all burning and tall-acting in phase 2). False if skipped (live cap).
        /// </summary>
        bool QueueRing();

        /// <summary>Rings alive now, not counting forecasts.</summary>
        int LiveRings { get; }
    }
}
