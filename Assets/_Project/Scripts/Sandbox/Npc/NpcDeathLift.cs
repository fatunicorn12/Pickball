using UnityEngine;

namespace Pickleball.Sandbox.Npc
{
    /// <summary>
    /// How far to raise an NPC's model during its death clip so the body ends
    /// up lying on the ground rather than sunk into it.
    ///
    /// Library deaths (Mixamo, Quaternius UAL) are authored for a thin
    /// humanoid. On a chunky character (Birdy's big head and belly) the lying
    /// pose ends partly below the floor. No single clip height offset fixes
    /// that without also lifting the standing start, so the lift eases in over
    /// the part of the clip where the body goes down: nothing before
    /// <c>start</c>, the full amount from <c>end</c>, smooth in between. Ending
    /// it where the body lands matters for a clip that lands early and then
    /// lies still: a lift still rising after that reads as the body floating
    /// up.
    /// </summary>
    public static class NpcDeathLift
    {
        /// <summary>
        /// Lift in metres at <paramref name="normalizedTime"/> of the death
        /// clip (0 = start, 1 = end; values past 1 hold the end).
        /// </summary>
        /// <param name="amount">Full lift, m. 0 or less: none.</param>
        /// <param name="start">Normalised time the lift starts, 0–1.</param>
        /// <param name="end">Normalised time it is complete, 0–1 (1: the
        /// clip's end). Clamped to no earlier than <paramref name="start"/>.</param>
        public static float At(float normalizedTime, float amount, float start, float end = 1f)
        {
            if (amount <= 0f) return 0f;
            start = Mathf.Clamp01(start);
            end = Mathf.Clamp(end, start, 1f);
            float t = Mathf.Clamp01(normalizedTime);
            if (end <= start) return t >= end ? amount : 0f;
            float u = Mathf.Clamp01((t - start) / (end - start));
            return amount * u * u * (3f - 2f * u);
        }
    }
}
