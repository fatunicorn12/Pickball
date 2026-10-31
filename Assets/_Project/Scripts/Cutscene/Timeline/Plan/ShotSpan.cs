namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>One shot laid out: its start, end, and where each of its beats starts.</summary>
    public sealed class ShotSpan
    {
        /// <summary>Creates a span.</summary>
        public ShotSpan(ShotPlan plan, double start, double end, double[] beatStarts)
        {
            Plan = plan;
            Start = start;
            End = end;
            BeatStarts = beatStarts;
        }

        /// <summary>The shot.</summary>
        public ShotPlan Plan { get; }

        /// <summary>Shot id.</summary>
        public string Id => Plan.Id;

        /// <summary>Start, seconds from the scene start.</summary>
        public double Start { get; }

        /// <summary>End.</summary>
        public double End { get; }

        /// <summary>Length.</summary>
        public double Duration => End - Start;

        /// <summary>Start of each beat.</summary>
        public double[] BeatStarts { get; }

        /// <summary>The time of "beat + at"; a beat index equal to the beat count is the shot's end.</summary>
        public double TimeOf(int beat, float at)
        {
            double t = beat >= 0 && beat < BeatStarts.Length ? BeatStarts[beat] : End;
            return t + at;
        }
    }
}
