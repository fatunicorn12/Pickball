namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// Which talk gesture a speaker plays while a line types out. Content picks
    /// the mood; the speaker's animator driver maps it to whatever clips that
    /// character has, so a line never names an animation directly and a new
    /// NPC with fewer clips still plays something sensible.
    /// </summary>
    public enum DialogueEmote
    {
        /// <summary>Ordinary conversational gesturing.</summary>
        Talk = 0,
        /// <summary>Big, animated delivery: news, warnings, jokes.</summary>
        Excited = 1,
        /// <summary>Slower, reflective delivery: hints and musing.</summary>
        Thinking = 2,
    }
}
