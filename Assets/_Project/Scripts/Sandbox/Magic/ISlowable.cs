namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// Something a spell can slow (the Freeze Zone). Opt-in: a damageable that
    /// does not implement it is simply not slowed, so a spell never has to know
    /// how a target moves. The target decides what "slowed" means for it —
    /// walk speed, attack wind-ups, animation speed.
    ///
    /// <b>Contract for implementers.</b> Slows from different sources do not
    /// stack multiplicatively: the strongest active one (the lowest
    /// multiplier) applies, so two overlapping zones are not twice as strong.
    /// Each source holds one entry; calling again from the same source
    /// replaces it (refreshing its time). <see cref="SlowLedger"/> implements
    /// exactly this and <see cref="StatusReceiver"/> wraps it, so most
    /// targets can just carry a StatusReceiver and read its
    /// <see cref="StatusReceiver.SpeedMultiplier"/>.
    /// </summary>
    public interface ISlowable
    {
        /// <param name="source">Who applies it (a zone); its entry is replaced on
        /// every call. Never null.</param>
        /// <param name="speedMultiplier">Fraction of normal speed, 0-1: 0.3 runs at
        /// 30%. Clamped by the receiver.</param>
        /// <param name="seconds">How long it lasts unless reapplied. A zone
        /// reapplies every frame with a short linger, so the slow ends a moment
        /// after the target leaves it.</param>
        void ApplySlow(object source, float speedMultiplier, float seconds);
    }
}
