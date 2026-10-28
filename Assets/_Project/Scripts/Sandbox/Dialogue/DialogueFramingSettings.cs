namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// The shape of the conversation shot, for <see cref="DialogueFraming.Solve"/>.
    /// Built from <see cref="DialogueConfig.Framing"/>; see the config's tooltips
    /// for what each value feels like.
    /// </summary>
    public struct DialogueFramingSettings
    {
        /// <summary>Pivot height above the player's feet, m.</summary>
        public float PivotHeight;
        /// <summary>Camera distance behind the pivot, along player → NPC, m.</summary>
        public float PushInDistance;
        /// <summary>Camera height above the pivot, m.</summary>
        public float CameraHeight;
        /// <summary>Camera offset to the right of the player → NPC line, m (negative: left).</summary>
        public float SideOffset;
        /// <summary>Aim height relative to the NPC focus, m.</summary>
        public float AimHeightOffset;
        /// <summary>Aim shift toward the player's side of the NPC, m; pushes the NPC
        /// to the open side of the frame, away from the player's shoulder.</summary>
        public float AimSideOffset;
        /// <summary>Vertical field of view, °.</summary>
        public float FieldOfView;
    }
}
