using Pickleball.Sandbox.Magic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pickleball.UI
{
    /// <summary>
    /// The channel bar under screen centre, for a spell cast by holding still
    /// (Heal). It tells the player the three things the channel needs them to
    /// know:
    ///  - <b>how</b>, while aiming with the spell selected: the spell's name and
    ///    cost and "hold fire · stand still";
    ///  - <b>how far</b>, while channelling: a bar filling over the channel time;
    ///  - <b>what happened</b>, for a moment after: a gold "+30" on completion, or
    ///    the broken rule in red ("Moved", "Hit!", "Not enough mana") with a
    ///    shake on a cancel or a refusal.
    ///
    /// Presentation only: it polls <see cref="SpellCaster"/> for the state and
    /// listens to its channel events for the one-shot results. Text is written
    /// only when it changes. Unscaled time.
    /// </summary>
    public class SpellChannelView : MonoBehaviour
    {
        const string HintText = "hold fire · stand still";

        [Header("Source")]
        [Tooltip("The caster whose channel this shows. Hidden while missing.")]
        [SerializeField] private SpellCaster caster;

        [Header("Parts")]
        [Tooltip("Fades the whole bar.")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("Shaken on a cancel; the parts below sit inside it.")]
        [SerializeField] private RectTransform body;
        [Tooltip("Filled (Horizontal) progress bar.")]
        [SerializeField] private Image fill;
        [Tooltip("Spell name, or the result word after a channel ends.")]
        [SerializeField] private TMP_Text label;
        [Tooltip("The how-to line under the bar.")]
        [SerializeField] private TMP_Text hint;

        [Header("Colour")]
        [Tooltip("Fill colour while channelling.")]
        [SerializeField] private Color fillColour = new(0.45f, 1f, 0.55f, 1f);
        [Tooltip("Fill and label colour on completion.")]
        [SerializeField] private Color completeColour = new(1f, 0.9f, 0.45f, 1f);
        [Tooltip("Fill and label colour on a cancel or refusal.")]
        [SerializeField] private Color failColour = new(1f, 0.38f, 0.35f, 1f);
        [Tooltip("Label colour while ready or channelling.")]
        [SerializeField] private Color labelColour = new(0.9f, 1f, 0.92f, 1f);

        [Header("Timing")]
        [Tooltip("Seconds to fade in or out.")]
        [Min(0.01f)] [SerializeField] private float fadeSeconds = 0.12f;
        [Tooltip("Seconds the result (+30, or the reason) stays up.")]
        [Min(0.1f)] [SerializeField] private float resultSeconds = 0.9f;
        [Tooltip("Shake amplitude on a cancel, in canvas pixels.")]
        [Min(0f)] [SerializeField] private float shakePixels = 9f;

        enum Shown { None, Ready, Channeling, Complete, Failed }

        Shown _shown = Shown.None;
        float _alpha, _resultLeft, _resultTotal;
        bool _failed;
        string _resultWord;
        SpellCaster _subscribed;
        Vector2 _bodyRest;
        bool _hasBodyRest;

        void OnEnable()
        {
            if (!_hasBodyRest && body != null)
            {
                _bodyRest = body.anchoredPosition;
                _hasBodyRest = true;
            }
            Subscribe(caster);
            _shown = Shown.None;
            _alpha = 0f;
            _resultLeft = 0f;
            if (group != null) group.alpha = 0f;
        }

        void OnDisable()
        {
            Subscribe(null);
            if (body != null && _hasBodyRest) body.anchoredPosition = _bodyRest;
        }

        /// <summary>Points the view at a caster at runtime. Null hides it.</summary>
        public void Bind(SpellCaster spellCaster)
        {
            caster = spellCaster;
            if (isActiveAndEnabled) Subscribe(caster);
        }

        void Subscribe(SpellCaster next)
        {
            if (_subscribed != null)
            {
                _subscribed.ChannelCompleted -= OnCompleted;
                _subscribed.ChannelCancelled -= OnCancelled;
            }
            _subscribed = next;
            if (_subscribed != null)
            {
                _subscribed.ChannelCompleted += OnCompleted;
                _subscribed.ChannelCancelled += OnCancelled;
            }
        }

        void OnCompleted(SpellDefinition spell)
        {
            _failed = false;
            _resultWord = spell != null && spell.HealAmount > 0f ? $"+{Mathf.RoundToInt(spell.HealAmount)}" : "Done";
            StartResult();
        }

        void OnCancelled(SpellDefinition spell, ChannelStopReason reason)
        {
            string word = ReasonWord(reason);
            if (word == null) return;   // switching spell or a disable: nothing to explain
            _failed = true;
            _resultWord = word;
            StartResult();
        }

        void StartResult()
        {
            _resultLeft = _resultTotal = resultSeconds;
            _shown = Shown.None;   // force a rewrite
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            _resultLeft = Mathf.Max(0f, _resultLeft - dt);

            Shown want = Shown.None;
            if (_resultLeft > 0f) want = _failed ? Shown.Failed : Shown.Complete;
            else if (caster != null && caster.IsChanneling) want = Shown.Channeling;
            else if (caster != null && caster.IsAiming && caster.Spell != null
                     && caster.Spell.Delivery == SpellDelivery.Channel) want = Shown.Ready;

            if (want != _shown) Rewrite(want);

            float goal = want == Shown.None ? 0f : 1f;
            _alpha = Mathf.MoveTowards(_alpha, goal, dt / fadeSeconds);
            if (group != null) group.alpha = _alpha;

            if (fill != null)
            {
                switch (want)
                {
                    case Shown.Channeling: fill.fillAmount = caster.ChannelProgress; break;
                    case Shown.Ready: fill.fillAmount = 0f; break;
                    case Shown.Complete: fill.fillAmount = 1f; break;
                    case Shown.Failed: fill.fillAmount = caster != null ? caster.ChannelProgress : 0f; break;
                }
            }

            if (body != null && _hasBodyRest)
            {
                Vector2 position = _bodyRest;
                if (want == Shown.Failed && _resultTotal > 0f)
                {
                    float t = 1f - _resultLeft / _resultTotal;
                    float envelope = Mathf.Clamp01(1f - t * 3f);
                    position.x += shakePixels * envelope * Mathf.Sin(t * 70f);
                }
                if (body.anchoredPosition != position) body.anchoredPosition = position;
            }
        }

        void Rewrite(Shown want)
        {
            _shown = want;
            var spell = caster != null ? caster.Spell : null;
            switch (want)
            {
                case Shown.Ready:
                case Shown.Channeling:
                    if (label != null)
                    {
                        label.text = spell != null ? spell.DisplayName : "";
                        label.color = labelColour;
                    }
                    if (hint != null)
                    {
                        hint.text = spell != null && spell.ManaCost > 0 ? $"{HintText} · {spell.ManaCost} mana" : HintText;
                        hint.alpha = 0.85f;
                    }
                    if (fill != null) fill.color = fillColour;
                    break;
                case Shown.Complete:
                    if (label != null)
                    {
                        label.text = _resultWord;
                        label.color = completeColour;
                    }
                    if (hint != null) hint.alpha = 0f;
                    if (fill != null) fill.color = completeColour;
                    break;
                case Shown.Failed:
                    if (label != null)
                    {
                        label.text = _resultWord;
                        label.color = failColour;
                    }
                    if (hint != null) hint.alpha = 0f;
                    if (fill != null) fill.color = failColour;
                    break;
            }
        }

        /// <summary>The word a broken rule shows as; null for the reasons that
        /// need no explanation.</summary>
        static string ReasonWord(ChannelStopReason reason) => reason switch
        {
            ChannelStopReason.Released => "Released",
            ChannelStopReason.Moved => "Moved",
            ChannelStopReason.Airborne => "Jumped",
            ChannelStopReason.Busy => "Interrupted",
            ChannelStopReason.Hurt => "Hit!",
            ChannelStopReason.NoMana => "Not enough mana",
            ChannelStopReason.FullHealth => "Health is full",
            ChannelStopReason.Unavailable => "Can't heal",
            _ => null,   // None, Blocked (a conversation took over), Switched, Disabled
        };
    }
}
