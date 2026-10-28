namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// How looking around during a conversation feels, for
    /// <see cref="DialogueLookInput"/> and <see cref="DialogueOrbit"/>. Built from
    /// <see cref="DialogueConfig.Look"/>; the defaults there match the orbit
    /// camera's own look settings, so the mouse and stick feel the same in a talk
    /// as outside one.
    /// </summary>
    public struct DialogueLookSettings
    {
        /// <summary>Degrees of turn per mouse count, both axes.</summary>
        public float MouseSensitivity;
        /// <summary>Right-stick yaw rate at full tilt, °/s.</summary>
        public float StickYawSpeed;
        /// <summary>Right-stick pitch rate at full tilt, °/s.</summary>
        public float StickPitchSpeed;
        /// <summary>Stick tilt below this is ignored, 0–1.</summary>
        public float StickDeadzone;
        /// <summary>Response curve past the deadzone; 1 is linear.</summary>
        public float StickExponent;
        /// <summary>Stick up looks down instead of up.</summary>
        public bool InvertStickY;
        /// <summary>Lowest camera elevation above the NPC's focus point, ° (negative: below it).</summary>
        public float MinPitch;
        /// <summary>Highest camera elevation above the NPC's focus point, °.</summary>
        public float MaxPitch;
        /// <summary>Time constant of the damping between input and camera, s. 0 follows at once.</summary>
        public float SmoothTime;
        /// <summary>Time constant of the ease back out after a wall pulled the camera in, s.</summary>
        public float PullOutSeconds;
    }
}
