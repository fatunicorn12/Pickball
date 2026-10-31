namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>One look/VFX key at an absolute time.</summary>
    public readonly struct CurveKeySpan
    {
        /// <summary>Channel (<see cref="LookChannels"/>).</summary>
        public readonly string Channel;

        /// <summary>Seconds from the scene start.</summary>
        public readonly double Time;

        /// <summary>Value.</summary>
        public readonly float Value;

        /// <summary>Creates a key.</summary>
        public CurveKeySpan(string channel, double time, float value)
        {
            Channel = channel;
            Time = time;
            Value = value;
        }
    }
}
