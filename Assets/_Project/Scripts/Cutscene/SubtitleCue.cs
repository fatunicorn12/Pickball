namespace Pickleball.Cutscene
{
    /// <summary>
    /// One subtitle card: the tag-free text for (part of) one script line. A line longer than one
    /// card (2 lines of 42 characters) is split by the pipeline into several cues with the same
    /// <see cref="LineId"/> and increasing <see cref="CardIndex"/>. The view never sees tags and
    /// never splits text itself.
    /// </summary>
    public readonly struct SubtitleCue
    {
        /// <summary>Stable line id, e.g. <c>OC_S3_07</c>.</summary>
        public readonly string LineId;

        /// <summary>Speaker id (<see cref="CutsceneSpeakers"/>).</summary>
        public readonly string Speaker;

        /// <summary>Display text, tags stripped, at most 84 characters.</summary>
        public readonly string Text;

        /// <summary>0-based card index within the line.</summary>
        public readonly int CardIndex;

        /// <summary>Number of cards the line was split into (1 for most lines).</summary>
        public readonly int CardCount;

        /// <summary>Creates a cue.</summary>
        public SubtitleCue(string lineId, string speaker, string text, int cardIndex, int cardCount)
        {
            LineId = lineId;
            Speaker = speaker;
            Text = text;
            CardIndex = cardIndex;
            CardCount = cardCount;
        }
    }
}
