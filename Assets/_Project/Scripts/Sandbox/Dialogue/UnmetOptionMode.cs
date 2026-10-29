namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// What a <see cref="DialogueOption"/> does while one of its conditions
    /// doesn't hold.
    /// </summary>
    public enum UnmetOptionMode
    {
        /// <summary>Left out of the list, as if it weren't authored.</summary>
        Hide = 0,
        /// <summary>Shown greyed out. The cursor skips it and it can't be picked, so
        /// the player sees there is something more to come back for.</summary>
        Disable = 1,
    }
}
