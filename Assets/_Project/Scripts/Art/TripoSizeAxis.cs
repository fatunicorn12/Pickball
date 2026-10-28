namespace Pickleball.Art
{
    /// <summary>
    /// Which measurement of a Tripo asset its target size refers to, so the plan's
    /// wording ("1.6 m tall", "0.9 m wide", "unit diameter") maps onto one number.
    /// </summary>
    public enum TripoSizeAxis
    {
        /// <summary>Bounds height (Y).</summary>
        Height,

        /// <summary>The larger of the bounds' width (X) and depth (Z).</summary>
        LargestHorizontal,

        /// <summary>The largest of X, Y and Z: the "diameter" of a roundish object.</summary>
        LargestExtent,
    }
}
