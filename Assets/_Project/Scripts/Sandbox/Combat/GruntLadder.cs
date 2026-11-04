using System.Collections.Generic;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Which grunt tier a swing gets, from where it sits in its chain. Pure logic, so
    /// the pattern can be tested without a scene.
    ///
    /// The ordinary hits ramp evenly from Light to Heavy across however many there are,
    /// so a chain always opens light and peaks just before its finisher, and a longer
    /// chain climbs more slowly instead of running out of tiers early:
    ///
    ///   2 hits: Light, Heavy
    ///   3 hits: Light, Medium, Heavy
    ///   5 hits: Light, Medium, Medium, Heavy, Heavy
    ///
    /// Then a step with the Finisher role always gets the Finisher tier. The role is
    /// tested rather than <see cref="ResolvedStep.IsLast"/>, because a chain with no
    /// finisher (the air chain's single slash) also ends on a last step, and it should
    /// not yell as if it were the sword's strongest hit.
    /// </summary>
    public static class GruntLadder
    {
        /// <summary>The tier for <paramref name="step"/> in <paramref name="chain"/>, the
        /// whole chain it belongs to.</summary>
        public static GruntTier TierFor(in ResolvedStep step, IReadOnlyList<ResolvedStep> chain)
        {
            if (step.Role == ComboRole.Finisher) return GruntTier.Finisher;
            return TierForOrdinaryHit(step.Index, OrdinaryHits(chain));
        }

        /// <summary>The tier of ordinary hit <paramref name="index"/> (0-based) out of
        /// <paramref name="ordinaryHits"/>. A lone hit has no ramp to climb, so it sits in
        /// the middle. An index past the end (the chain changed under a swing) stays Heavy.</summary>
        public static GruntTier TierForOrdinaryHit(int index, int ordinaryHits)
        {
            if (ordinaryHits <= 1) return GruntTier.Medium;
            if (index <= 0) return GruntTier.Light;
            if (index >= ordinaryHits - 1) return GruntTier.Heavy;
            // Twice the steps, in integers, so the middle of the ramp rounds up the same
            // way on every platform: (2 * index) / (hits - 1), rounded half up.
            int tier = (4 * index + ordinaryHits - 1) / (2 * (ordinaryHits - 1));
            return tier <= 0 ? GruntTier.Light : tier >= 2 ? GruntTier.Heavy : GruntTier.Medium;
        }

        /// <summary>How many steps of <paramref name="chain"/> are not its finisher.</summary>
        public static int OrdinaryHits(IReadOnlyList<ResolvedStep> chain)
        {
            int hits = 0;
            for (int i = 0; i < chain.Count; i++)
                if (chain[i].Role != ComboRole.Finisher) hits++;
            return hits;
        }
    }
}
