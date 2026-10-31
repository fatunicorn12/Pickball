using System;

namespace Pickleball.Cutscene
{
    /// <summary>
    /// Anything that tells a subtitle view what to show. The Timeline subtitle track raises these
    /// through a <see cref="SubtitleRelay"/>; the view only listens, so the view can be tested or
    /// replaced without a Timeline and the pipeline never touches UI.
    /// </summary>
    public interface ISubtitleSource
    {
        /// <summary>A card should appear now (replacing any card on screen).</summary>
        event Action<SubtitleCue> CueShown;

        /// <summary>The card for this line id should fade out.</summary>
        event Action<string> CueHidden;

        /// <summary>Remove everything at once (skip, cutscene end).</summary>
        event Action Cleared;
    }
}
