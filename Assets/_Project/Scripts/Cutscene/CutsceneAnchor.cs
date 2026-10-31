namespace Pickleball.Cutscene
{
    /// <summary>
    /// The named points every cutscene stand-in exposes. Shots, look-ats and VFX aim at these,
    /// never at mesh vertices or hard-coded heights, so any model that provides them can be
    /// swapped into a slot without touching a shot. The anchor transforms are children of the
    /// actor named exactly as <see cref="CutsceneAnchors.TransformName"/> returns.
    /// </summary>
    public enum CutsceneAnchor
    {
        /// <summary>Centre of the head. Cameras aim here; the safe-frame check tracks it.</summary>
        Head = 0,
        /// <summary>Midpoint between the eyes, on the face surface.</summary>
        Eyes = 1,
        /// <summary>Right palm (MC) or right wing tip (Birdy). Held props attach here.</summary>
        HandR = 2,
        /// <summary>Ground point between the feet. The actor's blocking mark.</summary>
        Feet = 3,
        /// <summary>Top of Birdy's helmet, where the propeller seats. Unused on MC.</summary>
        PropellerSocket = 4,
        /// <summary>The compass-blade's guard, wherever the blade is worn. Unused on Birdy.</summary>
        Blade = 5,
    }
}
