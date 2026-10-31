using UnityEngine.Playables;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>Runtime half of an <see cref="ActorStateClip"/>: the state values it holds.</summary>
    public sealed class ActorStateBehaviour : PlayableBehaviour
    {
        /// <summary>Rotor attached (1, 0, or -1 = leave).</summary>
        public int RotorAttached = -1;

        /// <summary>Rotor spin, °/s, or -1 = leave.</summary>
        public float RotorSpin = -1f;

        /// <summary>Prop shown (1, 0, or -1 = leave).</summary>
        public int PropVisible = -1;

        /// <summary>Prop spin, °/s, or -1 = leave.</summary>
        public float PropSpin = -1f;

        /// <summary>EyeSwap variant, or -1 = leave.</summary>
        public int Eyes = -1;
    }
}
