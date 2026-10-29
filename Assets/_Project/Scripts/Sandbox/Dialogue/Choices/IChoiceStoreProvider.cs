namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// A component that owns an <see cref="IChoiceStore"/> (loading and saving
    /// it). Dialogue holds one of these as an Inspector reference, wired by
    /// Setup Dialogue, so it never needs to know which class keeps the record.
    /// </summary>
    public interface IChoiceStoreProvider
    {
        /// <summary>The live store. Never null once the provider has woken.</summary>
        IChoiceStore Store { get; }
    }
}
