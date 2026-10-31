namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// One blocking cue laid out at an absolute time. The builder turns the cues of one actor into
    /// contiguous move and hold clips; a walk's travel time comes from the real marker distance.
    /// </summary>
    public readonly struct MarkSpan
    {
        /// <summary>The cue.</summary>
        public readonly MarkCue Cue;

        /// <summary>When the move (or cut) starts.</summary>
        public readonly double Start;

        /// <summary>Order among cues at the same time (plan order).</summary>
        public readonly int Order;

        /// <summary>Creates a span.</summary>
        public MarkSpan(MarkCue cue, double start, int order)
        {
            Cue = cue;
            Start = start;
            Order = order;
        }
    }
}
