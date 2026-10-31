namespace Pickleball.Cutscene.Sets
{
    /// <summary>
    /// The kinds of gold construction stroke that draw the Outer Shrine out of the void, in the
    /// order the eye should follow them (S1_SH07). Each group has its own time window on
    /// <see cref="ResolveConfig"/>; strokes inside a group are staggered by their order.
    /// </summary>
    public enum StrokeGroup
    {
        /// <summary>Compass arcs sweeping the lawn circle, and the compass's radius line.</summary>
        Arc = 0,
        /// <summary>Ruled lines rising into the column shafts.</summary>
        Column = 1,
        /// <summary>Tick marks pacing out the column ring.</summary>
        Tick = 2,
        /// <summary>The one ruled curve drawing the olive hill's profile.</summary>
        Hill = 3,
    }
}
