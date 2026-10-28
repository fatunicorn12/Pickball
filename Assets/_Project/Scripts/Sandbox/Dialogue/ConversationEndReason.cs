namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Why a conversation ended, read from <see cref="Talkable.LastEndReason"/>
    /// in a <see cref="Talkable.ConversationEnded"/> handler. Lets an NPC play a
    /// goodbye only when the player actually finished talking.
    /// </summary>
    public enum ConversationEndReason
    {
        /// <summary>The player read the last line and dismissed it.</summary>
        Completed = 0,
        /// <summary>Cut short: the NPC was disabled, destroyed or killed, or the
        /// director went away (a script reload in Play mode, a scene unload).</summary>
        Aborted = 1,
    }
}
