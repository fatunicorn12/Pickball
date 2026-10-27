using System.Collections.Generic;

namespace Pickleball.Sandbox.Combat.Progression
{
    /// <summary>
    /// Which rows of a level table a level-up teaches. Pure, so "reaching level
    /// 5 from level 2 teaches everything for levels 3, 4 and 5, in table order,
    /// once each" is a test rather than something to check by hand.
    /// </summary>
    public static class LevelUnlockRules
    {
        /// <summary>
        /// Append to <paramref name="into"/> every ability whose level is above
        /// <paramref name="fromLevelExclusive"/> and at most
        /// <paramref name="toLevelInclusive"/>, in table order, skipping nulls
        /// and duplicates. Pass 0 as the lower bound to collect everything up
        /// to a level (e.g. a save loaded at level 7).
        /// </summary>
        public static void Collect(IReadOnlyList<LevelUnlock> table, int fromLevelExclusive,
                                   int toLevelInclusive, List<SwordAbilityDefinition> into)
        {
            if (table == null || into == null) return;
            for (int i = 0; i < table.Count; i++)
            {
                var row = table[i];
                if (row.Ability == null) continue;
                if (row.Level <= fromLevelExclusive || row.Level > toLevelInclusive) continue;
                if (!into.Contains(row.Ability)) into.Add(row.Ability);
            }
        }
    }
}
