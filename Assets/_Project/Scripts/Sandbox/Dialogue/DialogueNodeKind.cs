namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// What a <see cref="DialogueNode"/> does when the conversation reaches it.
    /// Content links nodes by their string ids, so the kinds can be mixed freely.
    /// </summary>
    public enum DialogueNodeKind
    {
        /// <summary>The speaker says <see cref="DialogueNode.Text"/> with its emote; a press
        /// moves on to <see cref="DialogueNode.Next"/>.</summary>
        Line = 0,
        /// <summary>The player picks one of <see cref="DialogueNode.Options"/>. They appear
        /// once the line before has typed out, or once the node's own prompt
        /// (<see cref="DialogueNode.Text"/>, optional) has. <see cref="DialogueNode.Next"/>
        /// is the fallback when no option can be picked.</summary>
        Choice = 1,
        /// <summary>Shows nothing: goes to <see cref="DialogueNode.Next"/> when every one of
        /// <see cref="DialogueNode.Conditions"/> holds, else to
        /// <see cref="DialogueNode.ElseNext"/>. How a later talk checks a fact.</summary>
        Branch = 2,
    }
}
