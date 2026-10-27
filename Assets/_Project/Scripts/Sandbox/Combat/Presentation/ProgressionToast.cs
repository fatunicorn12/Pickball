using Pickleball.Sandbox.Combat.Progression;
using TMPro;
using UnityEngine;

namespace Pickleball.Sandbox.Combat.Presentation
{
    /// <summary>
    /// The "Q.E.D." banner: announces level-ups and newly learned abilities —
    /// sword abilities and the movement ones in <see cref="PlayerAbilities"/>
    /// alike, since both raise an Unlocked event carrying what was granted — and
    /// keeps a small level / Lemmas readout up to date.
    ///
    /// Messages queue rather than overwrite, so a level-up that teaches an
    /// ability shows the level, then the ability. Entirely event-driven except
    /// for the fade, which only ticks while something is on screen.
    /// </summary>
    public class ProgressionToast : MonoBehaviour
    {
        [Header("Sources")]
        [SerializeField] private PlayerLevel level;
        [SerializeField] private SwordAbilities swordAbilities;
        [Tooltip("Optional. Movement unlocks (double jump, dash…) toast here too.")]
        [SerializeField] private PlayerAbilities movementAbilities;

        [Header("Text")]
        [SerializeField] private TMP_Text title;
        [SerializeField] private TMP_Text body;
        [Tooltip("Optional persistent readout, e.g. 'LV 3 · 42 / 150 Lemmas'.")]
        [SerializeField] private TMP_Text levelLabel;

        [Header("Wording")]
        [SerializeField] private string levelUpTitle = "Q.E.D.";
        [Tooltip("{0} = the new level.")]
        [SerializeField] private string levelUpBody = "Theorem {0} proven — level {0}";
        [SerializeField] private string abilityTitle = "New technique";
        [Tooltip("Name of the XP unit in the readout.")]
        [SerializeField] private string xpUnit = "Lemmas";
        [Tooltip("One is appended to each level-up, cycling by level.")]
        [TextArea(1, 3)]
        [SerializeField] private string[] flavourLines =
        {
            "Pythagoras is relieved this is a whole number of levels, not √2.",
            "Every theorem stands on the shoulders of lemmas. Newton says he said that first.",
            "Quite Easily Done. Your teacher would still like to see your working.",
        };

        [Header("Timing")]
        [Tooltip("Seconds each message stays fully visible.")]
        [Min(0.1f)] [SerializeField] private float holdSeconds = 2.2f;
        [Tooltip("Seconds to fade out.")]
        [Min(0.01f)] [SerializeField] private float fadeSeconds = 0.5f;

        const int QueueCapacity = 8;
        (string title, string body)[] _queue = new (string, string)[QueueCapacity];
        int _head, _count;
        float _shownFor = -1f;

        void Awake()
        {
            if (level == null) level = GetComponentInParent<PlayerLevel>();
        }

        void OnEnable()
        {
            SetAlpha(0f);
            if (level != null)
            {
                level.LeveledUp += OnLeveledUp;
                level.XpGained += OnXpGained;
            }
            if (swordAbilities != null) swordAbilities.Unlocked += OnSwordUnlocked;
            if (movementAbilities != null) movementAbilities.Unlocked += OnMovementUnlocked;
            RefreshLabel();
        }

        void OnDisable()
        {
            if (level != null)
            {
                level.LeveledUp -= OnLeveledUp;
                level.XpGained -= OnXpGained;
            }
            if (swordAbilities != null) swordAbilities.Unlocked -= OnSwordUnlocked;
            if (movementAbilities != null) movementAbilities.Unlocked -= OnMovementUnlocked;
        }

        void OnLeveledUp(int newLevel, int previous)
        {
            string text = string.Format(levelUpBody, newLevel);
            if (flavourLines is { Length: > 0 })
                text += "\n<size=70%><i>" + flavourLines[(newLevel - 2 + flavourLines.Length * 8) % flavourLines.Length] + "</i></size>";
            Enqueue(levelUpTitle, text);
            RefreshLabel();
        }

        void OnXpGained(int _) => RefreshLabel();

        void OnSwordUnlocked(SwordAbilityDefinition ability)
        {
            string text = ability.DisplayName;
            if (!string.IsNullOrEmpty(ability.Description))
                text += "\n<size=70%><i>" + ability.Description + "</i></size>";
            Enqueue(abilityTitle, text);
        }

        void OnMovementUnlocked(AbilityKind kind) => Enqueue(abilityTitle, kind.ToString());

        void Enqueue(string t, string b)
        {
            if (_count == QueueCapacity) { _head = (_head + 1) % QueueCapacity; _count--; }   // drop oldest
            _queue[(_head + _count) % QueueCapacity] = (t, b);
            _count++;
            if (_shownFor < 0f) ShowNext();
        }

        void ShowNext()
        {
            if (_count == 0) { _shownFor = -1f; SetAlpha(0f); return; }
            var (t, b) = _queue[_head];
            _head = (_head + 1) % QueueCapacity;
            _count--;
            if (title != null) title.text = t;
            if (body != null) body.text = b;
            _shownFor = 0f;
            SetAlpha(1f);
        }

        void Update()
        {
            if (_shownFor < 0f) return;
            _shownFor += Time.unscaledDeltaTime;
            float fade = _shownFor - holdSeconds;
            if (fade <= 0f) return;
            if (fade >= fadeSeconds) { ShowNext(); return; }
            SetAlpha(1f - fade / fadeSeconds);
        }

        void SetAlpha(float a)
        {
            if (title != null) title.alpha = a;
            if (body != null) body.alpha = a;
        }

        void RefreshLabel()
        {
            if (levelLabel == null || level == null) return;
            levelLabel.text = level.IsMaxLevel
                ? $"LV {level.Level}  ·  MAX"
                : $"LV {level.Level}  ·  {level.XpIntoLevel} / {level.XpForLevel} {xpUnit}";
        }
    }
}
