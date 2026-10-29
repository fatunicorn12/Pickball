namespace Pickleball.Sandbox.Boss.Hypatia.Burning
{
    /// <summary>
    /// Where one floor band is in Alexandria's Burning. Only ever moves forward
    /// (Unburned → Smouldering → Burned); the one way back is a smoulder that
    /// <see cref="BurnSchedule.CancelSmoulders"/> calls off when the fight ends
    /// before it caught. A burned band never recovers.
    /// </summary>
    public enum BandState
    {
        /// <summary>Safe floor.</summary>
        Unburned = 0,

        /// <summary>The warning: still safe to stand on, ignites after the lead.</summary>
        Smouldering = 1,

        /// <summary>Alight: deals fire damage to a player standing in it.</summary>
        Burned = 2,
    }
}
