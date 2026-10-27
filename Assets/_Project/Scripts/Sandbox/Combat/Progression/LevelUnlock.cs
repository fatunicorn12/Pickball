using System;
using UnityEngine;

namespace Pickleball.Sandbox.Combat.Progression
{
    /// <summary>One row of the level table: reaching <see cref="Level"/> teaches
    /// <see cref="Ability"/>. Kept on the progression side, not on the ability,
    /// so a different table (another weapon, a harder mode) can teach the same
    /// abilities in a different order — Kingdom Hearts' Dream Sword / Shield /
    /// Rod choice works exactly like this.</summary>
    [Serializable]
    public struct LevelUnlock
    {
        [Tooltip("Level at which the ability is learned (2 = the first level-up).")]
        [Min(1)] [SerializeField] private int level;

        [Tooltip("The ability learned.")]
        [SerializeField] private SwordAbilityDefinition ability;

        public int Level => level;
        public SwordAbilityDefinition Ability => ability;

        public LevelUnlock(int level, SwordAbilityDefinition ability)
        {
            this.level = Mathf.Max(1, level);
            this.ability = ability;
        }
    }
}
