namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// Which of the two horizontal perpendiculars to the MC→Birdy line is Pair.Side. The shot list's
    /// default is the side facing the south-west sun; some shots name another.
    /// </summary>
    public enum SideRule
    {
        /// <summary>The side facing the golden-hour sun (azimuth 225°, south-west).</summary>
        SunSide = 0,
        /// <summary>The southern side.</summary>
        South = 1,
        /// <summary>The side from which MC reads screen-left of Birdy.</summary>
        MCLeft = 2,
        /// <summary>The side from which MC reads screen-right of Birdy.</summary>
        MCRight = 3,
    }
}
