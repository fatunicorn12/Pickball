using System;
using UnityEngine;

namespace Pickleball.Sandbox.Combat.Progression
{
    /// <summary>
    /// The player's level and Lemmas (XP). Turns XP into levels and announces
    /// each level gained; it does not know what XP is for, or where it came
    /// from. <see cref="CombatXpSource"/> feeds it, <see cref="LevelUnlocks"/>
    /// and the toast listen to it.
    ///
    /// No persistence yet: the sandbox starts every session at
    /// <see cref="startingLevel"/>.
    /// </summary>
    public class PlayerLevel : MonoBehaviour
    {
        [Tooltip("Curve and earning rates. Without one the house default curve is used.")]
        [SerializeField] private LevelingConfig config;

        [Tooltip("Level the player starts the session at — raise it to test late " +
                 "unlocks without grinding.")]
        [Range(1, 99)] [SerializeField] private int startingLevel = 1;

        // Plain fields rather than auto-properties so a script reload in Play
        // mode carries them over.
        int _level, _totalXp;
        bool _initialised;

        /// <summary>Raised with the Lemmas just earned, before any level-up it causes.</summary>
        public event Action<int> XpGained;

        /// <summary>Raised once per level gained, in order: (new level, previous
        /// level). A big hit that crosses two levels raises it twice, so every
        /// level's unlocks and toast happen.</summary>
        public event Action<int, int> LeveledUp;

        public LevelingConfig Config => config;
        public ExperienceCurve Curve => config != null ? config.Curve : ExperienceCurve.Default;

        public int Level { get { EnsureInitialised(); return _level; } }
        public int TotalXp { get { EnsureInitialised(); return _totalXp; } }
        public bool IsMaxLevel => Level >= Curve.MaxLevel;

        /// <summary>Lemmas earned since reaching the current level.</summary>
        public int XpIntoLevel => TotalXp - Curve.TotalXpFor(Level);

        /// <summary>Lemmas the current level costs in total; 0 at max level.</summary>
        public int XpForLevel => Curve.XpToNext(Level);

        /// <summary>Progress to the next level, 0-1 (1 at max level), for a bar.</summary>
        public float Progress01 => XpForLevel > 0 ? Mathf.Clamp01(XpIntoLevel / (float)XpForLevel) : 1f;

        void OnEnable() => EnsureInitialised();

        /// <summary>Lazily, so a listener that reads Level from its own OnEnable
        /// gets the right answer whichever component woke first.</summary>
        void EnsureInitialised()
        {
            if (_initialised) return;
            _initialised = true;
            _level = Mathf.Clamp(startingLevel, 1, Curve.MaxLevel);
            _totalXp = Curve.TotalXpFor(_level);
        }

        /// <summary>Earn <paramref name="amount"/> Lemmas. Ignored at max level
        /// (the bar is full) and for non-positive amounts.</summary>
        public void AddXp(int amount)
        {
            EnsureInitialised();
            if (amount <= 0 || IsMaxLevel) return;

            var curve = Curve;
            _totalXp = Mathf.Min(_totalXp + amount, curve.TotalXpFor(curve.MaxLevel));
            XpGained?.Invoke(amount);

            int reached = curve.LevelFor(_totalXp);
            while (_level < reached)
            {
                int previous = _level++;
                LeveledUp?.Invoke(_level, previous);
            }
        }

        [ContextMenu("Debug: Gain One Level")]
        void DebugGainLevel()
        {
            if (!Application.isPlaying || IsMaxLevel) return;
            AddXp(XpForLevel - XpIntoLevel);
        }

        [ContextMenu("Debug: Max Level")]
        void DebugMaxLevel()
        {
            if (!Application.isPlaying || IsMaxLevel) return;
            AddXp(Curve.TotalXpFor(Curve.MaxLevel) - TotalXp);
        }
    }
}
