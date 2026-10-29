namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// How fast <see cref="DialogueRunner"/> types and how eagerly it takes
    /// presses. Built from <see cref="DialogueConfig.Typewriter"/>; all times
    /// are unscaled seconds.
    /// </summary>
    public struct TypewriterTiming
    {
        /// <summary>Characters revealed per second. 0 or less reveals each line at once.</summary>
        public float CharactersPerSecond;
        /// <summary>Extra pause after , ; : and dashes, s.</summary>
        public float CommaPause;
        /// <summary>Extra pause after . ! ? and …, s.</summary>
        public float SentencePause;
        /// <summary>A press this soon after a line starts or is revealed is ignored, s.</summary>
        public float AdvanceLockout;
        /// <summary>How long options must have been up before Interact can pick one, s.</summary>
        public float ChoiceLockout;
        /// <summary>How long Interact must have been left alone before a press can pick
        /// an option, s. Every press during the talk (skipping text included) restarts
        /// it, so a steady tap through the text never picks an answer unread.
        /// Number keys and clicks are deliberate and pick at once.</summary>
        public float ChoiceQuiet;
    }
}
