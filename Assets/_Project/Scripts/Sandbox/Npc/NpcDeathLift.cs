using UnityEngine;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// How far to raise an NPC's model during its death clip so the body ends
    /// up lying on the ground rather than sunk into it.
    ///
    /// Mixamo deaths are authored for a thin humanoid. On a chunky character
    /// (Birdy's big head, belly and rotor) the lying pose ends partly below
    /// the floor. No single clip height offset fixes that without also lifting
    /// the standing start, so the lift eases in over the part of the clip where
    /// the body goes down: nothing before <c>start</c>, the full amount at the
    /// clip's end, smooth in between.
    /// </summary>
    public static class NpcDeathLift
    {
        /// <summary>
        /// Lift in metres at <paramref name="normalizedTime"/> of the death
        /// clip (0 = start, 1 = end; values past 1 hold the end).
        /// </summary>
        /// <param name="amount">Lift at the end of the clip, m. 0 or less: none.</param>
        /// <param name="start">Normalised time the lift starts, 0–1.</param>
        public static float At(float normalizedTime, float amount, float start)
        {
            if (amount <= 0f) return 0f;
            start = Mathf.Clamp01(start);
            float t = Mathf.Clamp01(normalizedTime);
            if (start >= 1f) return t >= 1f ? amount : 0f;
            float u = Mathf.Clamp01((t - start) / (1f - start));
            return amount * u * u * (3f - 2f * u);
        }
    }
}
