namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// A key on one of the look/VFX channels (<see cref="LookChannels"/>), timed like
    /// <see cref="ActionCue"/>. Keys are joined linearly and hold their last value.
    /// </summary>
    public readonly struct CurveKey
    {
        /// <summary>Channel name.</summary>
        public readonly string Channel;

        /// <summary>Beat index the time is measured from (the beat count means the shot's end).</summary>
        public readonly int Beat;

        /// <summary>Seconds after that beat starts.</summary>
        public readonly float At;

        /// <summary>Value at that time.</summary>
        public readonly float Value;

        /// <summary>Creates a key.</summary>
        public CurveKey(string channel, int beat, float at, float value)
        {
            Channel = channel;
            Beat = beat;
            At = at;
            Value = value;
        }
    }
}
