namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Which of the two independent combo chains a step belongs to.
    ///
    /// Ground and air are separate chains with separate hit counters, as in
    /// Kingdom Hearts: jumping mid-combo does not continue the ground chain in
    /// the air, it starts the air chain; landing mid-air-chain starts the ground
    /// chain from its first hit. The track is decided per press from whether the
    /// player is grounded at that moment, never carried over from the last swing.
    /// </summary>
    public enum ComboTrack
    {
        Ground,
        Air,
    }
}
