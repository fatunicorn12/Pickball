namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>What one beat of a shot's duration breakdown is (shot list §1, "Shot duration").</summary>
    public enum BeatKind
    {
        /// <summary>Wordless action time (a push, a tumble, a walk-in).</summary>
        Wordless = 0,
        /// <summary>A silent hold placed before a line (or after one, so a joke lands).</summary>
        Hold = 1,
        /// <summary>A voiced line: its slot is speech + the 0.3 s gap, plus any mid-line hold.</summary>
        Line = 2,
        /// <summary>
        /// A static hold with a line playing inside it (OC_S5_06's "..." inside the 2.0 s long pause):
        /// the hold sets the time, the line starts <see cref="Beat.Offset"/> into it.
        /// </summary>
        LineInHold = 3,
    }
}
