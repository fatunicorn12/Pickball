namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// The numbers <see cref="TalkTargetSelector"/> judges candidates by. Built
    /// from <see cref="DialogueConfig.SelectionRules"/>; a struct so tests can
    /// state each rule they rely on without an asset.
    /// </summary>
    public struct TalkSelectionRules
    {
        /// <summary>Walk-up range, m, measured flat.</summary>
        public float TalkRange;
        /// <summary>Half-angle of the facing cone, °. 180 or more ignores facing.</summary>
        public float FacingConeDegrees;
        /// <summary>Range for the lock-on target, m, measured flat. Facing is ignored.</summary>
        public float LockedTalkRange;
        /// <summary>Largest height difference between the origin and a candidate, m.</summary>
        public float MaxHeightDifference;
        /// <summary>Range bonus and lead the current target keeps, m.</summary>
        public float StickyMargin;
        /// <summary>Facing bonus the current target keeps, °.</summary>
        public float StickyConeDegrees;
    }
}
