namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// How the left stick, d-pad and arrow keys move the option cursor. Built
    /// from <see cref="DialogueConfig.ChoiceNavigation"/>; times are unscaled
    /// seconds.
    /// </summary>
    public struct ChoiceNavigationSettings
    {
        /// <summary>Stick tilt (0–1) that counts as a press up or down. It must fall below
        /// 60% of this to count as released, so a stick resting near the edge
        /// doesn't chatter.</summary>
        public float Threshold;
        /// <summary>Hold this long before the cursor starts repeating, s.</summary>
        public float RepeatDelay;
        /// <summary>Time between repeats while held, s. 0 or less never repeats.</summary>
        public float RepeatInterval;
    }
}
