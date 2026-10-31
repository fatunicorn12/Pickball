namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// Plan key channels that become held states on an <c>ActorStateTrack</c> rather than
    /// animated curves: Birdy's skinned rotor (on/off, spin) and the loose propeller (shown,
    /// spin). A channel is <c>ACTOR.field</c>; the actor id picks the bound object.
    /// </summary>
    public static class StateChannels
    {
        /// <summary>Birdy's rotor on his helmet: 1 attached, 0 detached.</summary>
        public const string BirdyRotor = PlanActors.Birdy + ".rotor";

        /// <summary>Birdy's rotor target spin, °/s.</summary>
        public const string BirdyRotorSpin = PlanActors.Birdy + ".rotorSpin";

        /// <summary>The loose propeller shown: 1 or 0.</summary>
        public const string PropVisible = PlanActors.Propeller + ".visible";

        /// <summary>The loose propeller's target spin, °/s (0 winds it down).</summary>
        public const string PropSpin = PlanActors.Propeller + ".spin";

        /// <summary>MC's EyeSwap variant (0 open, 1 closed).</summary>
        public const string McEyes = PlanActors.MC + ".eyes";

        /// <summary>Birdy's EyeSwap variant (0 open, 1 the cheer-slip droop).</summary>
        public const string BirdyEyes = PlanActors.Birdy + ".eyes";

        /// <summary>Every state channel. A fresh array per call.</summary>
        public static string[] All => new[] { BirdyRotor, BirdyRotorSpin, PropVisible, PropSpin, McEyes, BirdyEyes };

        /// <summary>The actor id a state channel belongs to, or null for a look channel.</summary>
        public static string ActorOf(string channel)
        {
            if (channel == McEyes) return PlanActors.MC;
            if (channel == BirdyRotor || channel == BirdyRotorSpin || channel == BirdyEyes) return PlanActors.Birdy;
            if (channel == PropVisible || channel == PropSpin) return PlanActors.Propeller;
            return null;
        }
    }
}
