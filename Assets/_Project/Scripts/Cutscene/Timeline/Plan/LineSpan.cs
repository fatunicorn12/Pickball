namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// One line laid out: where its audio and subtitles start, where speech ends, where its slot
    /// (speech + mid-line hold + 0.3 s gap) ends, and its cards.
    /// </summary>
    public sealed class LineSpan
    {
        /// <summary>Creates a span.</summary>
        public LineSpan(LineSpec line, string shotId, double start, double speechEnd, double end, CardSpan[] cards)
        {
            Line = line;
            ShotId = shotId;
            Start = start;
            SpeechEnd = speechEnd;
            End = end;
            Cards = cards;
        }

        /// <summary>The line.</summary>
        public LineSpec Line { get; }

        /// <summary>Line id.</summary>
        public string Id => Line.Id;

        /// <summary>The shot it plays in.</summary>
        public string ShotId { get; }

        /// <summary>Audio and first card start.</summary>
        public double Start { get; }

        /// <summary>End of the spoken part (the audio clip's end, the last card's end).</summary>
        public double SpeechEnd { get; }

        /// <summary>End of the slot, after the gap.</summary>
        public double End { get; }

        /// <summary>Cards in order.</summary>
        public CardSpan[] Cards { get; }
    }
}
