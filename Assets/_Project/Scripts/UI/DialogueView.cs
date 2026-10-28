using Pickleball.Sandbox.Dialogue;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pickleball.UI
{
    /// <summary>
    /// The Breath of the Wild dialogue box: a dark translucent rounded panel at
    /// the bottom centre, the speaker's name on a small tab above its top-left
    /// in the accent colour, white text typing out, and a pulsing ▼ once the
    /// line is fully shown. It fades and scales in and out.
    ///
    /// Presentation only. It listens to the <see cref="DialogueDirector"/> and
    /// never drives it: the typewriter lives in the runner, and this just sets
    /// <c>maxVisibleCharacters</c> when the director says the count changed.
    /// Everything animates in unscaled time, because time is stopped while it
    /// is on screen. Colours and timings are read from the
    /// <see cref="DialogueConfig"/> each time the box opens, so they can be
    /// tuned without re-running the setup.
    ///
    /// The layout (built by Setup Dialogue) anchors the box to 20–80% of the
    /// screen width under a 1920-wide reference, so line wrapping is the same on
    /// a 16:9 monitor and a 20:9 phone; only the space above the box changes.
    /// </summary>
    public class DialogueView : MonoBehaviour
    {
        [Header("Sources")]
        [Tooltip("The director whose conversations this box shows.")]
        [SerializeField] private DialogueDirector director;
        [Tooltip("Colours and timings.")]
        [SerializeField] private DialogueConfig config;

        [Header("Parts")]
        [Tooltip("Fades the whole box, tab included.")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("Scaled during the fade.")]
        [SerializeField] private RectTransform panel;
        [Tooltip("The box fill.")]
        [SerializeField] private Image box;
        [Tooltip("The speaker name tab; hidden when the speaker has no name.")]
        [SerializeField] private Image nameTab;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text bodyText;
        [Tooltip("The ▼ shown once the line is fully revealed.")]
        [SerializeField] private Graphic continueMark;

        [Header("Rest of the HUD")]
        [Tooltip("HUD canvases faded out while the box is up, as Breath of the Wild clears its " +
                 "HUD for a conversation. Frozen damage numbers and barks go with them. Set " +
                 "by Setup Dialogue; the box's own canvas must ignore parent groups.")]
        [SerializeField] private CanvasGroup[] hideWhileTalking = System.Array.Empty<CanvasGroup>();

        // The ▼ never fades out completely while pulsing, so it is always findable.
        const float ContinueMinAlpha = 0.35f;

        float _visibility;      // 0 hidden … 1 shown, before easing
        float _target;          // where _visibility is heading
        bool _continueShown;
        float _continueTime;
        Vector2 _continueHome;
        bool _hasContinueHome;

        void OnEnable()
        {
            if (director != null)
            {
                director.ConversationStarted += OnStarted;
                director.LineStarted += OnLineStarted;
                director.CharactersRevealed += OnCharactersRevealed;
                director.LineRevealed += OnLineRevealed;
                director.ConversationEnding += OnEnding;
                director.ConversationEnded += OnEnded;
            }
            ApplyStyle();
            _target = 0f;
            _visibility = 0f;
            ShowContinue(false);
            Apply();
        }

        void OnDisable()
        {
            // Never leave the rest of the HUD hidden behind us.
            SetHudAlpha(1f);
            if (director == null) return;
            director.ConversationStarted -= OnStarted;
            director.LineStarted -= OnLineStarted;
            director.CharactersRevealed -= OnCharactersRevealed;
            director.LineRevealed -= OnLineRevealed;
            director.ConversationEnding -= OnEnding;
            director.ConversationEnded -= OnEnded;
        }

        void OnStarted(Talkable speaker)
        {
            ApplyStyle();
            string speakerName = speaker != null ? speaker.DisplayName : string.Empty;
            if (nameText != null) nameText.text = speakerName;
            if (nameTab != null) nameTab.gameObject.SetActive(!string.IsNullOrEmpty(speakerName));
            if (bodyText != null)
            {
                bodyText.text = string.Empty;
                bodyText.maxVisibleCharacters = 0;
            }
            ShowContinue(false);
            _target = 1f;
        }

        void OnLineStarted(Talkable _, int index)
        {
            if (bodyText != null)
            {
                bodyText.text = director.CurrentText;
                bodyText.maxVisibleCharacters = 0;
            }
            ShowContinue(false);
        }

        void OnCharactersRevealed(int count)
        {
            if (bodyText != null) bodyText.maxVisibleCharacters = count;
        }

        void OnLineRevealed(Talkable _, int index)
        {
            if (bodyText != null) bodyText.maxVisibleCharacters = int.MaxValue;
            ShowContinue(true);
        }

        void OnEnding(Talkable _)
        {
            ShowContinue(false);
            _target = 0f;
        }

        void OnEnded(Talkable _)
        {
            _target = 0f;
            _visibility = 0f;
            Apply();
        }

        void Update()
        {
            bool fading = !Mathf.Approximately(_visibility, _target);
            if (!fading && !_continueShown) return;

            float dt = Time.unscaledDeltaTime;
            if (fading)
            {
                // No config: no fade, the box just appears and disappears.
                float seconds = config == null ? 0f
                              : _target > _visibility ? config.BoxFadeInSeconds : config.BoxFadeOutSeconds;
                _visibility = seconds > 0f ? Mathf.MoveTowards(_visibility, _target, dt / seconds) : _target;
                Apply();
            }

            if (_continueShown && continueMark != null && config != null)
            {
                _continueTime += dt;
                float wave = 0.5f + 0.5f * Mathf.Cos(_continueTime * config.ContinuePulseHz * 2f * Mathf.PI);
                var c = continueMark.color;
                c.a = Mathf.Lerp(ContinueMinAlpha, 1f, wave);
                continueMark.color = c;
                continueMark.rectTransform.anchoredPosition =
                    _continueHome + Vector2.down * (config.ContinueBob * (1f - wave));
            }
        }

        /// <summary>Alpha and scale from the eased visibility.</summary>
        void Apply()
        {
            // Smoothstep both ways: no snap at either end, and no jump if a
            // close interrupts the fade-in.
            float eased = DialogueFraming.SmoothStep01(_visibility);
            if (group != null)
            {
                group.alpha = eased;
                group.blocksRaycasts = false;
                group.interactable = false;
            }
            if (panel != null)
            {
                float from = config != null ? config.BoxScaleFrom : 1f;
                panel.localScale = Vector3.one * Mathf.Lerp(from, 1f, eased);
            }
            SetHudAlpha(1f - eased);
        }

        void SetHudAlpha(float alpha)
        {
            if (hideWhileTalking == null) return;
            for (int i = 0; i < hideWhileTalking.Length; i++)
                if (hideWhileTalking[i] != null) hideWhileTalking[i].alpha = alpha;
        }

        void ShowContinue(bool show)
        {
            _continueShown = show;
            _continueTime = 0f;
            if (continueMark == null) return;
            if (!_hasContinueHome)
            {
                _continueHome = continueMark.rectTransform.anchoredPosition;
                _hasContinueHome = true;
            }
            continueMark.rectTransform.anchoredPosition = _continueHome;
            continueMark.enabled = show;
        }

        void ApplyStyle()
        {
            if (config == null) return;
            if (box != null) box.color = config.BoxColour;
            if (nameTab != null) nameTab.color = config.NameTabColour;
            if (nameText != null) nameText.color = config.NameColour;
            if (bodyText != null) bodyText.color = config.TextColour;
            if (continueMark != null) continueMark.color = config.ContinueColour;
        }
    }
}
