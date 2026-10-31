namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// The point a shot-list notation starts from: an actor anchor, a derived point (the shot
    /// list's MC.Mid, Pair.Mid and the two-shot midpoints), the propeller, or a set marker. Every
    /// camera position, aim and focus point is one of these plus an offset, so nothing in a shot is
    /// a world coordinate.
    /// </summary>
    public enum RigBasis
    {
        /// <summary>No point (the world origin). As a level reference: don't level.</summary>
        None = 0,
        /// <summary>MC.Head.</summary>
        MCHead = 1,
        /// <summary>MC.Eyes.</summary>
        MCEyes = 2,
        /// <summary>MC.Hand_R.</summary>
        MCHandR = 3,
        /// <summary>MC.Feet.</summary>
        MCFeet = 4,
        /// <summary>MC.Mid = lerp(MC.Feet, MC.Head, 0.5).</summary>
        MCMid = 5,
        /// <summary>MC.Blade, the compass-blade's guard (falls back to MC.Head).</summary>
        MCBlade = 6,
        /// <summary>Birdy.Head.</summary>
        BirdyHead = 7,
        /// <summary>Birdy.Eyes.</summary>
        BirdyEyes = 8,
        /// <summary>Birdy.Hand_R (wing tip).</summary>
        BirdyHandR = 9,
        /// <summary>Birdy.Feet.</summary>
        BirdyFeet = 10,
        /// <summary>Birdy.PropellerSocket (falls back to Birdy.Head).</summary>
        BirdySocket = 11,
        /// <summary>Pair.Mid = midpoint(MC.Head, Birdy.Head).</summary>
        PairMid = 12,
        /// <summary>midpoint(MC.Feet, Birdy.Feet), the side-on two-shots' base.</summary>
        FeetMid = 13,
        /// <summary>midpoint(MC.Hand_R, Birdy.Head), S3_SH01's aim.</summary>
        HandHeadMid = 14,
        /// <summary>The propeller prop's root (Prop.Root).</summary>
        PropRoot = 15,
        /// <summary>A set marker, named by <see cref="RigPoint.marker"/>.</summary>
        Marker = 16,
    }
}
