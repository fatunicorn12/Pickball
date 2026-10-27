using UnityEngine;

namespace Pickleball.Sandbox.Combat.Progression
{
    /// <summary>
    /// How many Lemmas (XP) each level costs. Pure arithmetic, so it is pinned
    /// down by EditMode tests rather than by play-testing a whole fight.
    ///
    /// The step from level L to L+1 costs <c>baseXp · L^growth</c>, rounded,
    /// never less than 1. At growth 1 the running totals are the triangular
    /// numbers scaled by baseXp — with baseXp 50, levels 2/3/4/5 land at
    /// 50/150/300/500 — which ramps gently enough that an early boss can teach
    /// the whole first page of abilities. Pythagoras approves.
    /// </summary>
    public readonly struct ExperienceCurve
    {
        /// <summary>Highest reachable level. XP past it is still counted but buys nothing.</summary>
        public int MaxLevel { get; }

        /// <summary>Lemmas to go from level 1 to level 2.</summary>
        public float BaseXp { get; }

        /// <summary>Exponent on the level: 1 = triangular, above 1 = steeper late game.</summary>
        public float Growth { get; }

        public ExperienceCurve(int maxLevel, float baseXp, float growth)
        {
            MaxLevel = Mathf.Max(1, maxLevel);
            BaseXp = Mathf.Max(1f, baseXp);
            Growth = Mathf.Max(0f, growth);
        }

        /// <summary>The house default, used when no LevelingConfig is wired.</summary>
        public static ExperienceCurve Default => new(20, 50f, 1f);

        /// <summary>Lemmas needed to go from <paramref name="level"/> to the next.
        /// 0 at (or past) the max level, where there is no next.</summary>
        public int XpToNext(int level)
        {
            if (level >= MaxLevel) return 0;
            level = Mathf.Max(1, level);
            return Mathf.Max(1, Mathf.RoundToInt(BaseXp * Mathf.Pow(level, Growth)));
        }

        /// <summary>Total Lemmas needed to reach <paramref name="level"/> from
        /// nothing. Level 1 (and below) costs 0; levels past the cap cost the
        /// same as the cap.</summary>
        public int TotalXpFor(int level)
        {
            level = Mathf.Clamp(level, 1, MaxLevel);
            int total = 0;
            for (int l = 1; l < level; l++) total += XpToNext(l);
            return total;
        }

        /// <summary>The level a player with <paramref name="totalXp"/> Lemmas has
        /// reached: the highest level whose total is not more than it.</summary>
        public int LevelFor(int totalXp)
        {
            int level = 1, total = 0;
            while (level < MaxLevel)
            {
                total += XpToNext(level);
                if (totalXp < total) break;
                level++;
            }
            return level;
        }
    }
}
