namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// Something a heavy spell hit can briefly stagger (the Thunderbolt): its
    /// current action is interrupted and it reels for a moment. Opt-in, like
    /// <see cref="ISlowable"/>: a target without it only takes the damage. What
    /// a stagger interrupts is the target's business — a boss might cancel a
    /// wind-up but finish a committed attack.
    /// </summary>
    public interface IStaggerable
    {
        /// <param name="seconds">How long to reel. A second stagger while one runs
        /// extends it to the longer of the two, never adds.</param>
        void Stagger(float seconds);
    }
}
