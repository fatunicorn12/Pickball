namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// How an actor travels between two blocking marks. Walks are linear so the pace matches the
    /// walk cycle; turns and settles ease so they don't start or stop with a jolt.
    /// </summary>
    public enum MarkEase
    {
        /// <summary>Constant speed (walks).</summary>
        Linear = 0,
        /// <summary>Smoothstep in and out (turns, repositioning).</summary>
        EaseInOut = 1,
        /// <summary>Accelerates away (zip-offs).</summary>
        EaseIn = 2,
        /// <summary>Decelerates into the mark (landings, stopping on a mark).</summary>
        EaseOut = 3,
    }
}
