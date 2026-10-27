using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Combat.Progression
{
    /// <summary>
    /// Teaches the sword abilities in the level table as the player reaches
    /// each level. The glue between <see cref="PlayerLevel"/> (which knows
    /// nothing of abilities) and <see cref="SwordAbilities"/> (which knows
    /// nothing of levels), so either side can be used without the other — a
    /// boss drop grants straight into SwordAbilities with no level involved.
    /// </summary>
    public class LevelUnlocks : MonoBehaviour
    {
        [SerializeField] private PlayerLevel level;
        [SerializeField] private SwordAbilities abilities;

        List<SwordAbilityDefinition> _scratch;

        void Awake()
        {
            if (level == null) level = GetComponent<PlayerLevel>();
            if (abilities == null) abilities = GetComponent<SwordAbilities>();
        }

        void OnEnable()
        {
            _scratch ??= new List<SwordAbilityDefinition>();
            if (level == null) return;
            level.LeveledUp += OnLeveledUp;
            // Catch up on everything up to the current level — a raised
            // starting level, or a reload mid-session. Grant ignores repeats.
            Teach(0, level.Level);
        }

        void OnDisable()
        {
            if (level != null) level.LeveledUp -= OnLeveledUp;
        }

        void OnLeveledUp(int newLevel, int previousLevel) => Teach(previousLevel, newLevel);

        void Teach(int fromExclusive, int toInclusive)
        {
            if (abilities == null || level == null || level.Config == null) return;
            _scratch.Clear();
            LevelUnlockRules.Collect(level.Config.Unlocks, fromExclusive, toInclusive, _scratch);
            foreach (var ability in _scratch) abilities.Grant(ability);
        }
    }
}
