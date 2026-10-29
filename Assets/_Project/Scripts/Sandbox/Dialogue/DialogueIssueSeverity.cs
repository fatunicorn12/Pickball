namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>How bad a <see cref="DialogueIssue"/> is.</summary>
    public enum DialogueIssueSeverity
    {
        /// <summary>Plays, but probably not as meant (an unreachable node, a loop with no
        /// way out). Shipped assets may carry warnings.</summary>
        Warning = 0,
        /// <summary>Broken: a link to nothing, a duplicate id, a choice that can strand the
        /// player. The every-asset test fails on these.</summary>
        Error = 1,
    }
}
