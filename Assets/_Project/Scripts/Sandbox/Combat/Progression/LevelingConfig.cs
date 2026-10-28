using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Combat.Progression
{
    /// <summary>
    /// Every progression tunable in one asset: the XP curve, what earns XP,
    /// and which level teaches which sword ability.
    ///
    /// XP is called Lemmas in game — small results that add up to theorems.
    /// </summary>
    [CreateAssetMenu(fileName = "Leveling_New", menuName = "Pickleball/Combat/Leveling Config")]
    public class LevelingConfig : ScriptableObject
    {
        [Header("Curve")]
        [Tooltip("Highest reachable level.")]
        [Range(1, 99)] [SerializeField] private int maxLevel = 20;

        [Tooltip("Lemmas to go from level 1 to 2. Each later level costs this × " +
                 "level^Growth, so this sets the pace of the whole curve.")]
        [Min(1f)] [SerializeField] private float baseXp = 50f;

        [Tooltip("Curve exponent. 1 = triangular (50, 150, 300, 500… at base 50): " +
                 "gentle. 1.5+ makes late levels a real grind.")]
        [Range(0f, 3f)] [SerializeField] private float growth = 1f;

        [Header("Earning")]
        [Tooltip("Lemmas per HP of sword damage actually dealt. Each target pays for " +
                 "at most its max health, once per session. Newton has 600 HP, so at " +
                 "1.5 he is worth 900 Lemmas before the defeat bonus.")]
        [Min(0f)] [SerializeField] private float xpPerDamage = 1.5f;

        [Tooltip("Lemmas for landing the blow that defeats something with Health. " +
                 "Paid once per target per session, so a refilling dummy pays it once.")]
        [Min(0)] [SerializeField] private int xpPerDefeat = 250;

        [Header("Unlocks")]
        [Tooltip("Abilities taught by levelling, in any order. Two rows may share a level.")]
        [SerializeField] private List<LevelUnlock> unlocks = new();

        public ExperienceCurve Curve => new(maxLevel, baseXp, growth);
        public float XpPerDamage => xpPerDamage;
        public int XpPerDefeat => xpPerDefeat;
        public IReadOnlyList<LevelUnlock> Unlocks => unlocks;
    }
}
