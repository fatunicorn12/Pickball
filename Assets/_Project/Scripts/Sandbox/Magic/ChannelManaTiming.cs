namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// When a channelled spell pays its mana. Stored as an int in spell assets:
    /// append only.
    /// </summary>
    public enum ChannelManaTiming
    {
        /// <summary>Paid when the channel completes. A cancelled channel costs
        /// nothing, so being hit or stepping off mid-heal never wastes mana. The
        /// cost must still be affordable to start. The default: the most
        /// forgiving.</summary>
        OnCompletion = 0,

        /// <summary>Paid when the channel starts and kept when it is cancelled:
        /// committing to the channel is the risk.</summary>
        OnStart = 1,
    }
}
