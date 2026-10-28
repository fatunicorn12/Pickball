namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// The shape of the conversation shot, for <see cref="DialogueFraming.Solve"/>.
    /// Built from <see cref="DialogueConfig.Framing"/>; see the config's tooltips
    /// for what each value feels like.
    /// </summary>
    public struct DialogueFramingSettings
    {
        /// <summary>Camera distance behind the pivot, along player → NPC, m.</summary>
        public float PushInDistance;
        /// <summary>Camera height above the pivot, m.</summary>
        public float CameraHeight;
        /// <summary>Least sideways camera offset, m; the sign picks the shoulder
        /// (negative: left). Widened by <see cref="PlayerClearance"/> when needed.</summary>
        public float SideOffset;
        /// <summary>How far the sightline to the NPC's head must pass from the
        /// player's head, m. Sets the side offset needed at each distance.</summary>
        public float PlayerClearance;
        /// <summary>Largest sideways offset, m, however close the NPC stands.</summary>
        public float MaxSideOffset;
        /// <summary>Screen height of the NPC's focus point, 0 (bottom) to 1 (top).</summary>
        public float NpcHeadScreenHeight;
        /// <summary>Screen height the NPC's feet should reach down to, 0 to 1; above the dialogue box.</summary>
        public float NpcFeetScreenHeight;
        /// <summary>Horizontal offset of the NPC from the screen centre toward the
        /// open side (away from the player's shoulder), in viewport units.</summary>
        public float NpcScreenSide;
        /// <summary>Narrowest vertical field of view, °. Short NPCs are not zoomed past this.</summary>
        public float MinFieldOfView;
        /// <summary>Widest vertical field of view, °. Tall NPCs are cut off below rather than shrunk.</summary>
        public float MaxFieldOfView;
    }
}
