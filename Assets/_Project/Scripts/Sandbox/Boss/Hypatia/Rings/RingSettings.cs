namespace Pickleball.Sandbox.Boss.Hypatia.Rings
{
    /// <summary>
    /// The ring tunables as plain numbers, read from <see cref="HypatiaConfig"/>
    /// each frame. It exists so <see cref="RingField"/> can be tested with
    /// numbers written in the test instead of a ScriptableObject whose fields are
    /// private; the config stays the only place they are tuned. A struct with no
    /// references, so building one per frame allocates nothing.
    /// </summary>
    public struct RingSettings
    {
        /// <summary>Outward speed, m/s.</summary>
        public float Speed;
        /// <summary>Radius a ring is born at, m.</summary>
        public float StartRadius;
        /// <summary>Radius a ring dies at, m.</summary>
        public float ArenaRadius;
        /// <summary>Radial thickness of the hit band, m.</summary>
        public float Thickness;
        /// <summary>Top of a low ring above the floor, m.</summary>
        public float LowRingHeight;
        /// <summary>Drawn height of a tall ring, m (visual only).</summary>
        public float TallRingVisualHeight;
        /// <summary>Every Nth ring is tall; 0 = never, 1 = all.</summary>
        public int TallRingEvery;
        /// <summary>Gap centres snap to multiples of this, °.</summary>
        public float GapSnapDegrees;
        /// <summary>Full gap width, °.</summary>
        public float GapWidthDegrees;
        /// <summary>Gap turn per ring, ° (snapped).</summary>
        public float GapStepDegrees;
        /// <summary>Forecast-to-launch delay on her clock, s.</summary>
        public float ForecastLead;
        /// <summary>Damage of a low ring, HP.</summary>
        public float LowRingDamage;
        /// <summary>Damage of a tall ring, HP.</summary>
        public float TallRingDamage;
        /// <summary>Damage of a burning (phase-2) ring, HP.</summary>
        public float FireRingDamage;
        /// <summary>Most rings alive or forecast at once.</summary>
        public int MaxLiveRings;

        /// <summary>Read every ring tunable off the fight's config.</summary>
        public static RingSettings FromConfig(HypatiaConfig config)
        {
            return new RingSettings
            {
                Speed = config.RingSpeed,
                StartRadius = config.RingStartRadius,
                ArenaRadius = config.ArenaRadius,
                Thickness = config.RingThickness,
                LowRingHeight = config.LowRingHeight,
                TallRingVisualHeight = config.TallRingVisualHeight,
                TallRingEvery = config.TallRingEvery,
                GapSnapDegrees = config.GapSnapDegrees,
                GapWidthDegrees = config.GapWidthDegrees,
                GapStepDegrees = config.GapStepDegrees,
                ForecastLead = config.GapForecastLead,
                LowRingDamage = config.LowRingDamage,
                TallRingDamage = config.TallRingDamage,
                FireRingDamage = config.FireRingDamage,
                MaxLiveRings = config.MaxLiveRings,
            };
        }
    }
}
