using Pickleball.Sandbox.Magic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pickleball.UI
{
    /// <summary>
    /// The spell selector beside the mana bar: one slot per spell carried, each
    /// with its icon in the spell's colour, its number key, and a dark sweep
    /// that drains as its cooldown recovers. The selected slot stands taller and
    /// brighter and pops when picked, and the selected spell's name (white, on
    /// the dark plate the builder lays behind the selector, underlined in the
    /// spell's colour) and cost sit above the row. A spell the bar cannot pay for right now is dimmed, so
    /// "can I cast this?" reads before the button is pressed.
    ///
    /// Presentation only. It polls <see cref="SpellSelector"/> and its caster
    /// every frame (cooldowns and mana move continuously) and listens to
    /// <see cref="SpellSelector.SelectionChanged"/> only for the pop. Text and
    /// sprites are written only when they change, so nothing allocates per
    /// frame. Unscaled time, like the mana bar, so slow motion never freezes it.
    /// Built by MagicHudBuilder with one slot per selector slot; re-run Setup
    /// Magic after changing how many spells the player carries.
    /// </summary>
    public class SpellSelectorView : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("The selector drawn. Hidden while missing.")]
        [SerializeField] private SpellSelector selector;

        [Header("Parts")]
        [Tooltip("Fades the whole selector.")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("One per slot, in slot order: scaled for selection.")]
        [SerializeField] private RectTransform[] slots;
        [Tooltip("One per slot: the slot's frame, brighter when selected.")]
        [SerializeField] private Image[] frames;
        [Tooltip("One per slot: the spell's icon.")]
        [SerializeField] private Image[] icons;
        [Tooltip("One per slot: Filled (Radial 360) cooldown sweep over the icon.")]
        [SerializeField] private Image[] cooldowns;
        [Tooltip("One per slot: the number key.")]
        [SerializeField] private TMP_Text[] keys;
        [Tooltip("The selected spell's name, above the row.")]
        [SerializeField] private TMP_Text nameText;
        [Tooltip("Optional. The selected spell's cost as its own label. Empty: the cost is " +
                 "written right after the name instead (\"Fireball · 25\").")]
        [SerializeField] private TMP_Text costText;
        [Tooltip("Optional. A bar under the name in the selected spell's colour (left-anchored; " +
                 "its width follows the name).")]
        [SerializeField] private Image nameUnderline;

        [Header("Colour")]
        [Tooltip("Frame of an unselected slot.")]
        [SerializeField] private Color frameIdle = new(0.62f, 0.68f, 0.8f, 0.75f);
        [Tooltip("Frame of the selected slot (brightened toward the spell's colour).")]
        [SerializeField] private Color frameSelected = new(1f, 1f, 1f, 1f);
        [Tooltip("How much a spell you cannot afford is dimmed (0 = black, 1 = not at all).")]
        [Range(0f, 1f)] [SerializeField] private float unaffordableDim = 0.35f;
        [Tooltip("Cooldown sweep tint; alpha is its darkness.")]
        [SerializeField] private Color cooldownColour = new(0.02f, 0.03f, 0.08f, 0.72f);
        [Tooltip("The name's colour. White on the dark plate reads on any floor; the spell's " +
                 "colour goes on the underline instead.")]
        [SerializeField] private Color nameColour = Color.white;
        [Tooltip("The cost's colour when it follows the name.")]
        [SerializeField] private Color inlineCostColour = new(0.62f, 0.84f, 1f, 1f);

        [Header("Motion")]
        [Tooltip("Scale of the selected slot (x).")]
        [Min(1f)] [SerializeField] private float selectedScale = 1.2f;
        [Tooltip("Extra scale at the peak of the selection pop (x).")]
        [Min(0f)] [SerializeField] private float popAmount = 0.25f;
        [Tooltip("Seconds the pop takes to settle.")]
        [Min(0.01f)] [SerializeField] private float popSeconds = 0.25f;
        [Tooltip("Seconds for slot scale to ease to its target.")]
        [Min(0.001f)] [SerializeField] private float smoothing = 0.06f;
        [Tooltip("Opacity of the name label while not aiming, after its post-selection hold.")]
        [Range(0f, 1f)] [SerializeField] private float idleNameAlpha = 0.6f;
        [Tooltip("Seconds the name stays fully visible after a selection.")]
        [Min(0f)] [SerializeField] private float nameHoldSeconds = 1.5f;

        float[] _scales;
        Sprite[] _shownIcons;
        float _pop, _nameHold;
        int _shownSelected = int.MinValue, _shownCost = int.MinValue;
        SpellSelector _subscribed;

        void OnEnable()
        {
            int n = slots != null ? slots.Length : 0;
            _scales = new float[n];
            _shownIcons = new Sprite[n];
            for (int i = 0; i < n; i++) _scales[i] = 1f;
            _shownSelected = int.MinValue;
            _shownCost = int.MinValue;
            Subscribe(selector);
        }

        void OnDisable() => Subscribe(null);

        /// <summary>Points the view at a selector at runtime. Null hides it.</summary>
        public void Bind(SpellSelector spellSelector)
        {
            selector = spellSelector;
            if (isActiveAndEnabled) Subscribe(selector);
            _shownSelected = int.MinValue;
        }

        void Subscribe(SpellSelector next)
        {
            if (_subscribed != null) _subscribed.SelectionChanged -= OnSelectionChanged;
            _subscribed = next;
            if (_subscribed != null) _subscribed.SelectionChanged += OnSelectionChanged;
        }

        void OnSelectionChanged(SpellSelector source)
        {
            _pop = 1f;
            _nameHold = nameHoldSeconds;
        }

        void Update()
        {
            if (group != null) group.alpha = selector != null && selector.Count > 0 ? 1f : 0f;
            if (selector == null || slots == null) return;

            float dt = Time.unscaledDeltaTime;
            var caster = selector.Caster;
            var mana = caster != null ? caster.Mana : null;
            int selected = selector.SelectedIndex;
            _pop = Mathf.MoveTowards(_pop, 0f, dt / popSeconds);
            _nameHold = Mathf.Max(0f, _nameHold - dt);
            float ease = 1f - Mathf.Exp(-dt / smoothing);

            for (int i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                var spell = selector.SpellAt(i);
                bool filled = spell != null;
                if (slot.gameObject.activeSelf != filled) slot.gameObject.SetActive(filled);
                if (!filled) continue;

                bool isSelected = i == selected;
                float goal = isSelected ? selectedScale + popAmount * _pop * _pop : 1f;
                _scales[i] = Mathf.Lerp(_scales[i], goal, ease);
                slot.localScale = new Vector3(_scales[i], _scales[i], 1f);

                bool affordable = mana == null || mana.CanAfford(spell.ManaCost);
                Color theme = spell.ThemeColour;
                theme.a = 1f;

                var icon = At(icons, i);
                if (icon != null)
                {
                    if (_shownIcons[i] != spell.Icon)
                    {
                        _shownIcons[i] = spell.Icon;
                        icon.sprite = spell.Icon;
                        icon.enabled = spell.Icon != null;
                    }
                    float dim = affordable ? 1f : unaffordableDim;
                    icon.color = new Color(theme.r * dim, theme.g * dim, theme.b * dim, isSelected ? 1f : 0.85f);
                }

                var frame = At(frames, i);
                if (frame != null)
                    frame.color = isSelected ? Color.Lerp(frameSelected, theme, 0.35f) : frameIdle;

                var sweep = At(cooldowns, i);
                if (sweep != null)
                {
                    sweep.color = cooldownColour;
                    sweep.fillAmount = caster != null ? caster.CooldownNormalisedFor(spell) : 0f;
                }

                var key = At(keys, i);
                if (key != null) key.alpha = isSelected ? 1f : 0.7f;
            }

            UpdateName(selected, caster);
        }

        void UpdateName(int selected, SpellCaster caster)
        {
            var spell = selector.SpellAt(selected);
            int cost = spell != null ? spell.ManaCost : 0;
            if (selected != _shownSelected || cost != _shownCost)
            {
                // Rewritten only on a change, so the string work never runs per frame.
                _shownSelected = selected;
                _shownCost = cost;
                if (nameText != null)
                {
                    string label = spell != null ? spell.DisplayName : "";
                    if (costText == null && spell != null && cost > 0)
                        label = $"{label}  <color=#{ColorUtility.ToHtmlStringRGB(inlineCostColour)}>· {cost}</color>";
                    nameText.text = label;
                    nameText.color = nameColour;
                }
                if (nameUnderline != null)
                {
                    nameUnderline.enabled = spell != null;
                    if (spell != null)
                    {
                        Color theme = spell.ThemeColour;
                        theme.a = 1f;
                        nameUnderline.color = theme;
                        if (nameText != null)
                        {
                            var r = nameUnderline.rectTransform;
                            r.sizeDelta = new Vector2(nameText.GetPreferredValues(nameText.text).x, r.sizeDelta.y);
                        }
                    }
                }
                if (costText != null)
                {
                    costText.SetText("{0}", cost);
                    costText.gameObject.SetActive(cost > 0);
                }
            }

            bool aiming = caster != null && caster.IsAiming;
            float alpha = aiming || _nameHold > 0f ? 1f : idleNameAlpha;
            if (nameText != null) nameText.alpha = alpha;
            if (costText != null) costText.alpha = alpha;
            if (nameUnderline != null)
            {
                var c = nameUnderline.color;
                if (!Mathf.Approximately(c.a, alpha))
                {
                    c.a = alpha;
                    nameUnderline.color = c;
                }
            }
        }

        static T At<T>(T[] array, int i) where T : class => array != null && i < array.Length ? array[i] : null;
    }
}
