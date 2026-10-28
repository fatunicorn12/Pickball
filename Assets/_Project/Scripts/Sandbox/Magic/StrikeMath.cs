using UnityEngine;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// The arithmetic of a called strike (the Thunderbolt), pure so it is tested
    /// without a scene: which bystanders a chain jumps to, and how far through
    /// its telegraph a strike is.
    /// </summary>
    public static class StrikeMath
    {
        /// <summary>
        /// Picks up to <paramref name="count"/> candidates nearest to
        /// <paramref name="origin"/>, within <paramref name="radius"/>, skipping
        /// any whose <paramref name="excluded"/> flag is set. Writes their indices
        /// into <paramref name="result"/>, nearest first, and returns how many
        /// were picked. Allocation-free: the caller owns every array.
        /// Ties keep the lower index. Distances are true 3D distances.
        /// </summary>
        /// <param name="positions">Candidate positions; only the first
        /// <paramref name="candidateCount"/> are read.</param>
        /// <param name="excluded">Per-candidate skip flags (already struck);
        /// may be null.</param>
        public static int PickChain(Vector3 origin, Vector3[] positions, bool[] excluded, int candidateCount,
                                    float radius, int count, int[] result)
        {
            if (positions == null || result == null || radius <= 0f || count <= 0) return 0;
            int n = Mathf.Min(candidateCount, positions.Length);
            int wanted = Mathf.Min(count, result.Length);
            float limit = radius * radius;
            int picked = 0;

            // Selection by repeated minimum: n and count are a handful each.
            while (picked < wanted)
            {
                int best = -1;
                float bestSq = float.PositiveInfinity;
                for (int i = 0; i < n; i++)
                {
                    if (excluded != null && i < excluded.Length && excluded[i]) continue;
                    if (AlreadyPicked(result, picked, i)) continue;
                    float sq = (positions[i] - origin).sqrMagnitude;
                    if (sq > limit || sq >= bestSq) continue;
                    bestSq = sq;
                    best = i;
                }
                if (best < 0) break;
                result[picked++] = best;
            }
            return picked;
        }

        /// <summary>0 when the telegraph starts, 1 when the bolt lands; clamped.
        /// A non-positive delay is already 1.</summary>
        public static float TelegraphProgress(float elapsed, float delay)
            => delay <= 0f ? 1f : Mathf.Clamp01(elapsed / delay);

        static bool AlreadyPicked(int[] result, int picked, int index)
        {
            for (int k = 0; k < picked; k++)
                if (result[k] == index) return true;
            return false;
        }
    }
}
