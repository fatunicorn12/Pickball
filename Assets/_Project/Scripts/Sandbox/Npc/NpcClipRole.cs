namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// What an NPC animation clip is for. A clip's role comes from its file
    /// name prefix (<see cref="NpcClipRoles"/>), so dropping a new FBX into an
    /// NPC's clip folder and re-running its setup menu is all it takes to wire it.
    /// </summary>
    public enum NpcClipRole
    {
        /// <summary>A standing loop. Several become random idle variants; the first (by name) is the main one.</summary>
        Idle = 0,
        /// <summary>Ordinary conversational gesturing (<c>DialogueEmote.Talk</c>).</summary>
        Talk = 1,
        /// <summary>Big, animated delivery (<c>DialogueEmote.Excited</c>).</summary>
        TalkExcited = 2,
        /// <summary>Slower, reflective delivery (<c>DialogueEmote.Thinking</c>).</summary>
        TalkThinking = 3,
        /// <summary>A one-shot that ends lying down and is held there.</summary>
        Death = 4,
    }
}
