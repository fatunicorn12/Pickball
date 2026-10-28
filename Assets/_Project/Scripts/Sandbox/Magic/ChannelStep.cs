namespace Pickleball.Sandbox.Magic
{
    /// <summary>What one <see cref="SpellChannel.Tick"/> did.</summary>
    public enum ChannelStep
    {
        /// <summary>No channel is running.</summary>
        Idle,
        /// <summary>Still channelling.</summary>
        Channeling,
        /// <summary>Completed this frame; the mana is paid.</summary>
        Completed,
        /// <summary>Cancelled this frame; see <see cref="SpellChannel.LastStop"/>.</summary>
        Cancelled,
    }
}
