namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>One subtitle card's time on screen, in seconds from the scene start.</summary>
    public readonly struct CardSpan
    {
        /// <summary>0-based card index in the line.</summary>
        public readonly int Index;

        /// <summary>Card text.</summary>
        public readonly string Text;

        /// <summary>Appears.</summary>
        public readonly double Start;

        /// <summary>Goes (the next card's start, or the end of speech for the last).</summary>
        public readonly double End;

        /// <summary>Creates a span.</summary>
        public CardSpan(int index, string text, double start, double end)
        {
            Index = index;
            Text = text;
            Start = start;
            End = end;
        }
    }
}
