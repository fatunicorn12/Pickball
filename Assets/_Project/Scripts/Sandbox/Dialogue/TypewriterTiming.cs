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
        /// <summary>Quiet time after options appear before a pick counts, s. An
        /// Interact press inside it restarts it (a debounce, so mashing through
        /// text never picks the first option unread); a number key or click inside
        /// it only moves the cursor.</summary>
        public float ChoiceLockout;
    }
}
