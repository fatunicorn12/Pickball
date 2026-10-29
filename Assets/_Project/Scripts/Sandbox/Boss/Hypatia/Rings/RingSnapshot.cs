namespace Pickleball.Sandbox.Boss.Hypatia.Rings
{
    /// <summary>
    /// Everything presentation needs to draw one ring or one gap forecast,
    /// copied out so drawing code can never change the simulation. A value type,
    /// so reading rings every frame allocates nothing.
    ///
    /// All values were decided when the ring was queued (its forecast), so the
    /// floor marker, the ring and the contact always agree even if the config is
    /// tuned mid-ring. Angles follow <see cref="RingMath"/>'s azimuth convention
    /// (0° = +Z, growing toward +X); distances are metres from the arena centre;
    /// heights are metres above the floor.
    /// </summary>
    public readonly struct RingSnapshot
    {
        /// <summary>Unique within a fight; also the ring's 0-based index in the
        /// spiral. Pairs the forecast, launch, hit and retire events.</summary>
        public readonly int Id;

        /// <summary>Radius of the band's centre line now, m. The start radius
        /// while still a forecast.</summary>
        public readonly float Radius;

        /// <summary>Radial thickness of the hit band, m (centred on Radius).</summary>
        public readonly float Thickness;

        /// <summary>Gap centre, degrees in [0, 360).</summary>
        public readonly float GapCentreDegrees;

        /// <summary>Full angular width of the gap, degrees.</summary>
        public readonly float GapWidthDegrees;

        /// <summary>Hits at any height (tall cadence, or burning). Only the gap
        /// gets through.</summary>
        public readonly bool Tall;

        /// <summary>Phase-2 fire ring: draw it burning. Always also Tall.</summary>
        public readonly bool Burning;

        /// <summary>Height to draw the ring, m: the low ring's real top, or the
        /// tall ring's drawn height (visual only; a tall ring hits at any height).</summary>
        public readonly float VisualHeight;

        /// <summary>Seconds on HER clock until this ring launches; 0 once it is
        /// live. Freezes while she is frozen.</summary>
        public readonly float LeadSeconds;

        /// <summary>Damage it deals the player, HP.</summary>
        public readonly float Damage;

        /// <summary>Copy one ring's state.</summary>
        public RingSnapshot(int id, float radius, float thickness, float gapCentreDegrees,
                            float gapWidthDegrees, bool tall, bool burning, float visualHeight,
                            float leadSeconds, float damage)
        {
            Id = id;
            Radius = radius;
            Thickness = thickness;
            GapCentreDegrees = gapCentreDegrees;
            GapWidthDegrees = gapWidthDegrees;
            Tall = tall;
            Burning = burning;
            VisualHeight = visualHeight;
            LeadSeconds = leadSeconds;
            Damage = damage;
        }
    }
}
