namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>Alexandria's Burning, as the brain drives it.</summary>
    public interface IArenaBurn : IHypatiaAttack
    {
        /// <summary>False once every band outside the minimum safe radius is
        /// burned or smouldering.</summary>
        bool CanIgnite { get; }

        /// <summary>Start the outermost unburned band smouldering; it ignites
        /// <see cref="HypatiaConfig.BurnWarningLead"/> later (on her clock) and
        /// never recovers. Does nothing when <see cref="CanIgnite"/> is false.</summary>
        void IgniteNext();

        /// <summary>Bands fully alight.</summary>
        int BurnedBands { get; }

        /// <summary>Outer radius of the unburned floor, m from the centre.</summary>
        float SafeRadius { get; }
    }
}
