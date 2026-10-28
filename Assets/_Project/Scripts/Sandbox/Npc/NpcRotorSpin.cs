using UnityEngine;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// How fast a living NPC's rotor turns: a lazy idle spin, a whir while it
    /// speaks an Excited line, and a smooth change between the two. Pure rules,
    /// so <see cref="NpcRotor"/> stays wiring and EditMode tests can pin them.
    /// </summary>
    public static class NpcRotorSpin
    {
        /// <summary>The spin to head for, °/s: <paramref name="excitedSpin"/>
        /// while an Excited line's gesture plays, otherwise
        /// <paramref name="idleSpin"/> (idling, listening, calm lines).</summary>
        public static float Target(bool speakingExcited, float idleSpin, float excitedSpin)
            => speakingExcited ? excitedSpin : idleSpin;

        /// <summary>
        /// One frame of easing from <paramref name="current"/> toward
        /// <paramref name="target"/>, exponential at <paramref name="response"/>
        /// (1/s), so the result is the same at any frame rate.
        /// </summary>
        public static float Approach(float current, float target, float response, float dt)
            => dt <= 0f ? current : Mathf.Lerp(current, target, 1f - Mathf.Exp(-response * dt));

        /// <summary>The spin angle after <paramref name="dt"/> s at
        /// <paramref name="speed"/> °/s, kept in [0, 360).</summary>
        public static float Advance(float angle, float speed, float dt) => Mathf.Repeat(angle + speed * dt, 360f);
    }
}
