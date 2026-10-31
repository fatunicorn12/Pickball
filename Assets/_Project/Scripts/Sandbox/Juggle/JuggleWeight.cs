namespace Pickleball.Sandbox.Juggle
{
    /// <summary>
    /// How hard an enemy is to keep in the air, after Castle Crashers' weight
    /// classes. It picks a <see cref="JuggleSettings"/> preset; the numbers in
    /// the settings are what the juggle actually reads.
    /// </summary>
    public enum JuggleWeight
    {
        /// <summary>Every air hit relaunches it as high as the first: juggles forever.</summary>
        Light = 0,
        /// <summary>Each air hit lifts it less; it drops after roughly 15-18 air hits.</summary>
        Medium = 1,
        /// <summary>Barely lifts; it falls after 1-3 air hits.</summary>
        Heavy = 2,
    }
}
