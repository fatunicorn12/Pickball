namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// One entry in a shot's duration breakdown, in order. A shot's length is the sum of its beats,
    /// with each line's slot taken from its audio (or the placeholder rule), so assigning a clip
    /// moves every later beat and nothing earlier.
    /// </summary>
    public readonly struct Beat
    {
        /// <summary>What the beat is.</summary>
        public readonly BeatKind Kind;

        /// <summary>Seconds of a wordless beat or hold (the hold length for <see cref="BeatKind.LineInHold"/>).</summary>
        public readonly float Seconds;

        /// <summary>Line id for line beats, else null.</summary>
        public readonly string LineId;

        /// <summary>Seconds held between the line's cards at its mid-line hold tag (OC_S2_09: 0.8).</summary>
        public readonly float MidHold;

        /// <summary>For <see cref="BeatKind.LineInHold"/>: seconds into the hold the line starts.</summary>
        public readonly float Offset;

        Beat(BeatKind kind, float seconds, string lineId, float midHold, float offset)
        {
            Kind = kind;
            Seconds = seconds;
            LineId = lineId;
            MidHold = midHold;
            Offset = offset;
        }

        /// <summary>Wordless action time.</summary>
        public static Beat Wordless(float seconds) => new Beat(BeatKind.Wordless, seconds, null, 0f, 0f);

        /// <summary>A silent hold.</summary>
        public static Beat Hold(float seconds) => new Beat(BeatKind.Hold, seconds, null, 0f, 0f);

        /// <summary>A voiced line, optionally with a hold between its cards.</summary>
        public static Beat Line(string lineId, float midHold = 0f) => new Beat(BeatKind.Line, 0f, lineId, midHold, 0f);

        /// <summary>A static hold of <paramref name="hold"/> s with a line starting <paramref name="offset"/> s in.</summary>
        public static Beat LineInHold(string lineId, float hold, float offset) =>
            new Beat(BeatKind.LineInHold, hold, lineId, 0f, offset);
    }
}
