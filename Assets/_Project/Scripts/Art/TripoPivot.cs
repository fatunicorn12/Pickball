namespace Pickleball.Art
{
    /// <summary>
    /// Where a Tripo prefab's origin sits relative to its mesh. Tripo exports put
    /// the origin wherever the generator left it, so the import moves the model
    /// under a child offset until the origin lands here.
    /// </summary>
    public enum TripoPivot
    {
        /// <summary>Centre of the footprint, on the ground: drop the prefab at a floor point and it stands there.</summary>
        BottomCentre,

        /// <summary>Centre of the bounds: what code that spins, scales or flies an object expects.</summary>
        Centre,
    }
}
