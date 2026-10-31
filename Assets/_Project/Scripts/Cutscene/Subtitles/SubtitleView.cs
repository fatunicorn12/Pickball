using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pickleball.Cutscene.Subtitles
{
    /// <summary>
    /// Cutscene subtitles: one card at the bottom centre of the safe area, the speaker's name in a
    /// small-caps tab above it in that speaker's colour, white text on a soft dark backing, fading
    /// in and out over <see cref="SubtitleStyle.FadeSeconds"/> of unscaled time.
    ///
    /// <para><b>Presentation only.</b> It listens to an <see cref="ISubtitleSource"/> (the scene's
    /// <see cref="SubtitleRelay"/>) and optionally to an <see cref="ICutscenePlayback"/>, and drives
    /// nothing. It never parses script text and never splits a line into cards; the pipeline sends
    /// cards of at most 2 × 42 characters. The only text work it does is to choose where a card
    /// breaks into two lines (<see cref="SubtitleWrap"/>), because TMP wrapping is off so the break
    /// is the same at every resolution.</para>
    ///
    /// <para><b>Card replacement.</b> When a new card arrives while one is up (the second card of a
    /// long line, or the next speaker straight after), the backing holds at full opacity and snaps
    /// to the new card's size, and only the words and name fade in over
    /// <see cref="SubtitleStyle.CardSwapSeconds"/>. A full fade out and in between back-to-back
    /// lines would flash the backing on every line; an instant swap with no fade makes it hard to
    /// notice that the text changed. A card that arrives with nothing up fades in as a whole.</para>
    ///
    /// <para><b>Tags.</b> Cue text is meant to be tag-free. If a <c>[tag]</c> gets through anyway it
    /// is stripped before display and, in the Editor, logged once as an error, since it means the
    /// pipeline's stripping is broken.</para>
    ///
    /// Lives on its own Screen Space Overlay canvas (<c>CutsceneHUD</c>, built by
    /// <c>Tools ▸ Pickleball ▸ Cutscene ▸ 5 Build Subtitle HUD</c>), separate from the dialogue
    /// box, which it neither reads nor changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SubtitleView : MonoBehaviour
    {
        [Header("Sources")]
        [Tooltip("A component implementing ISubtitleSource: the scene's SubtitleRelay.")]
        [SerializeField] private MonoBehaviour source;
        [Tooltip("Optional: a component implementing ICutscenePlayback (the cutscene player). " +
                 "Its Started and Finished clear any card left up, as a safety net.")]
        [SerializeField] private MonoBehaviour playback;
        [Tooltip("Colours, sizes and timings. Read on enable and on every card.")]
        [SerializeField] private SubtitleStyle style;

        [Header("Parts")]
        [Tooltip("Fades the whole card, tab included.")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("The card: bottom-centre of the safe area, sized to the text plus padding. " +
                 "Carries the soft backing Image.")]
        [SerializeField] private RectTransform card;
        [Tooltip("The soft dark backing behind the text (9-sliced).")]
        [SerializeField] private Image backing;
        [Tooltip("The speaker tab above the card; hidden when the speaker has no name.")]
        [SerializeField] private Image tab;
        [Tooltip("The speaker name inside the tab, in small caps and the speaker's colour.")]
        [SerializeField] private TMP_Text nameText;
        [Tooltip("The card text. Word wrapping off: line breaks come from SubtitleWrap.")]
        [SerializeField] private TMP_Text bodyText;

        // Used only if no style is assigned (a warning says so), so a half-wired view still
        // shows readable cards; spacing then falls back to zero.
        const int FallbackMaxChars = 42;
        const int FallbackMaxLines = 2;
        const float FallbackFadeSeconds = 0.15f;

        ISubtitleSource _source;
        ICutscenePlayback _playback;
        float _visibility;          // 0 hidden … 1 shown (the group's alpha)
        float _target;              // where _visibility is heading
        float _textAlpha = 1f;      // the words' own alpha, dipped when a card replaces another
        float _appliedGroupAlpha = -1f;
        float _appliedTextAlpha = -1f;
        string _lineId;
        string _shownText = string.Empty;
        string _shownName = string.Empty;
#if UNITY_EDITOR
        bool _loggedTag;
        string _warnedOverflowLine;
#endif

        /// <summary>True from a card arriving until it is hidden or cleared (even mid-fade).</summary>
        public bool IsShowing => _target > 0f;

        /// <summary>The card's current opacity, 0..1.</summary>
        public float Visibility => _visibility;

        /// <summary>The line id of the card up, or null.</summary>
        public string CurrentLineId => _lineId;

        /// <summary>The card text as displayed, with its line breaks.</summary>
        public string ShownText => _shownText;

        /// <summary>The speaker name in the tab, or empty.</summary>
        public string ShownSpeakerName => _shownName;

        void OnEnable()
        {
            _source = source as ISubtitleSource;
            if (_source != null)
            {
                _source.CueShown += OnCueShown;
                _source.CueHidden += OnCueHidden;
                _source.Cleared += HideNow;
            }
            else if (source != null)
                Debug.LogWarning($"[SubtitleView] {name}: source {source.GetType().Name} is not an " +
                                 "ISubtitleSource; no subtitles will show.", this);

            _playback = playback as ICutscenePlayback;
            if (_playback != null)
            {
                _playback.Started += OnPlaybackStarted;
                _playback.Finished += OnPlaybackFinished;
            }
            else if (playback != null)
                Debug.LogWarning($"[SubtitleView] {name}: playback {playback.GetType().Name} is not an " +
                                 "ICutscenePlayback.", this);

            if (style == null)
                Debug.LogWarning($"[SubtitleView] {name}: no SubtitleStyle assigned; using bare defaults. " +
                                 "Run Tools > Pickleball > Cutscene > 5 Build Subtitle HUD.", this);

            ApplyStyle();
            HideNow();
        }

        void OnDisable()
        {
            if (_source != null)
            {
                _source.CueShown -= OnCueShown;
                _source.CueHidden -= OnCueHidden;
                _source.Cleared -= HideNow;
            }
            if (_playback != null)
            {
                _playback.Started -= OnPlaybackStarted;
                _playback.Finished -= OnPlaybackFinished;
            }
            _source = null;
            _playback = null;
        }

        void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Advances the fades by <paramref name="unscaledDeltaTime"/> seconds. Update calls it with
        /// unscaled time; it is public so tests and editor previews can step it deterministically.
        /// </summary>
        public void Tick(float unscaledDeltaTime)
        {
            float fade = style != null ? style.FadeSeconds : FallbackFadeSeconds;
            float swap = style != null ? style.CardSwapSeconds : 0f;
            _visibility = SubtitleFade.Step(_visibility, _target, unscaledDeltaTime, fade);
            _textAlpha = SubtitleFade.Step(_textAlpha, 1f, unscaledDeltaTime, swap);
            ApplyAlpha();
        }

        /// <summary>
        /// Re-reads the style into every part: fonts, colours, wrapping off, card anchoring, then
        /// re-lays out the card on screen. The setup menu calls it so the scene matches the style
        /// without entering Play mode.
        /// </summary>
        public void ApplyStyle()
        {
            if (bodyText != null)
            {
                bodyText.textWrappingMode = TextWrappingModes.NoWrap;
                bodyText.enableAutoSizing = false;
                bodyText.overflowMode = TextOverflowModes.Overflow;
                bodyText.richText = false;   // a '<' in a line is text, never markup
                bodyText.alignment = TextAlignmentOptions.Center;
                bodyText.raycastTarget = false;
                if (style != null)
                {
                    bodyText.fontSize = style.FontSize;
                    bodyText.color = style.TextColour;
                    bodyText.lineSpacing = style.LineSpacing;
                }
            }
            if (nameText != null)
            {
                nameText.textWrappingMode = TextWrappingModes.NoWrap;
                nameText.enableAutoSizing = false;
                nameText.overflowMode = TextOverflowModes.Overflow;
                nameText.richText = false;
                nameText.fontStyle = FontStyles.SmallCaps;
                nameText.alignment = TextAlignmentOptions.Center;
                nameText.raycastTarget = false;
                if (style != null) nameText.fontSize = style.TabFontSize;
            }
            if (backing != null)
            {
                backing.raycastTarget = false;
                if (style != null)
                {
                    backing.color = style.BackingColour;
                    backing.pixelsPerUnitMultiplier = style.BackingCornerScale;
                }
            }
            if (tab != null)
            {
                tab.raycastTarget = false;
                if (style != null)
                {
                    tab.color = style.TabColour;
                    tab.pixelsPerUnitMultiplier = style.BackingCornerScale;
                }
            }
            if (card != null)
            {
                card.anchorMin = card.anchorMax = new Vector2(0.5f, 0f);
                card.pivot = new Vector2(0.5f, 0f);
            }
            if (group != null)
            {
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            Layout();
            _appliedGroupAlpha = _appliedTextAlpha = -1f;
            ApplyAlpha();
        }

        void OnCueShown(SubtitleCue cue)
        {
            bool replacing = _target > 0f && _visibility > 0f;

            string text = SubtitleTagGuard.Strip(cue.Text, out bool hadTag);
#if UNITY_EDITOR
            if (hadTag && !_loggedTag)
            {
                _loggedTag = true;
                Debug.LogError($"[SubtitleView] cue {cue.LineId} card {cue.CardIndex} reached the view " +
                               $"with a [tag] in it: \"{cue.Text}\". Stripped for display; the pipeline " +
                               "should strip tags before raising cues.", this);
            }
#endif
            int maxChars = style != null ? style.MaxCharsPerLine : FallbackMaxChars;
            int maxLines = style != null ? style.MaxLines : FallbackMaxLines;
            int sentence = style != null ? style.SentenceBreakBonus : SubtitleWrap.DefaultSentenceBreakBonus;
            int clause = style != null ? style.ClauseBreakBonus : SubtitleWrap.DefaultClauseBreakBonus;
            _shownText = SubtitleWrap.Wrap(text, maxChars, maxLines, sentence, clause, out bool fits);
#if UNITY_EDITOR
            if (!fits && _warnedOverflowLine != cue.LineId)
            {
                _warnedOverflowLine = cue.LineId;
                Debug.LogWarning($"[SubtitleView] cue {cue.LineId} card {cue.CardIndex} can't wrap into " +
                                 $"{maxLines} lines of {maxChars} characters at word boundaries; shown " +
                                 $"overlong. Split it into two cards: \"{text}\"", this);
            }
#endif
            _shownName = style != null ? style.SpeakerName(cue.Speaker) : cue.Speaker ?? string.Empty;
            Color nameColour = style != null ? style.SpeakerColour(cue.Speaker) : Color.white;

            if (bodyText != null) bodyText.text = _shownText;
            if (nameText != null)
            {
                nameText.text = _shownName;
                nameText.color = nameColour;
            }
            Layout();

            _lineId = cue.LineId;
            _target = 1f;
            // Replacing: hold the backing, fade only the words in. Otherwise the whole card fades.
            _textAlpha = replacing ? 0f : 1f;
            _appliedGroupAlpha = _appliedTextAlpha = -1f;
            ApplyAlpha();
        }

        void OnCueHidden(string lineId)
        {
            if (_lineId == null || lineId != _lineId) return;
            _target = 0f;
        }

        void OnPlaybackStarted(string cutsceneId) => HideNow();

        void OnPlaybackFinished(string cutsceneId, bool skipped) => HideNow();

        /// <summary>Hides the card at once, no fade (skip, cutscene end, Cleared).</summary>
        void HideNow()
        {
            _target = 0f;
            _visibility = 0f;
            _textAlpha = 1f;
            _lineId = null;
            ApplyAlpha();
        }

        /// <summary>Sizes the card and the tab around the current text. Runs once per card.</summary>
        void Layout()
        {
            if (card == null || bodyText == null) return;
            var padding = style != null ? style.BackingPadding : Vector2.zero;
            float bottom = style != null ? style.BottomMargin : 0f;

            Vector2 size = string.IsNullOrEmpty(_shownText) ? Vector2.zero : bodyText.GetPreferredValues(_shownText);
            size = new Vector2(Mathf.Ceil(size.x), Mathf.Ceil(size.y));
            var body = bodyText.rectTransform;
            body.anchorMin = body.anchorMax = body.pivot = new Vector2(0.5f, 0.5f);
            body.anchoredPosition = Vector2.zero;
            body.sizeDelta = size;

            card.sizeDelta = size + 2f * padding;
            card.anchoredPosition = new Vector2(0f, bottom);

            if (tab == null || nameText == null) return;
            bool hasName = !string.IsNullOrEmpty(_shownName);
            if (tab.gameObject.activeSelf != hasName) tab.gameObject.SetActive(hasName);
            if (!hasName) return;

            var tabPadding = style != null ? style.TabPadding : Vector2.zero;
            float along = style != null ? style.TabHorizontalAnchor : 0.5f;
            float gap = style != null ? style.TabGap : 0f;
            Vector2 nameSize = nameText.GetPreferredValues(_shownName);
            var tabRect = tab.rectTransform;
            tabRect.anchorMin = tabRect.anchorMax = new Vector2(along, 1f);
            tabRect.pivot = new Vector2(along, 0f);
            tabRect.anchoredPosition = new Vector2(0f, gap);
            tabRect.sizeDelta = new Vector2(Mathf.Ceil(nameSize.x), Mathf.Ceil(nameSize.y)) + 2f * tabPadding;

            var nameRect = nameText.rectTransform;
            nameRect.anchorMin = Vector2.zero;
            nameRect.anchorMax = Vector2.one;
            nameRect.pivot = new Vector2(0.5f, 0.5f);
            nameRect.offsetMin = nameRect.offsetMax = Vector2.zero;
        }

        void ApplyAlpha()
        {
            if (group != null && _visibility != _appliedGroupAlpha)
            {
                group.alpha = _visibility;
                _appliedGroupAlpha = _visibility;
            }
            if (_textAlpha != _appliedTextAlpha)
            {
                if (bodyText != null) bodyText.alpha = _textAlpha;
                if (nameText != null) nameText.alpha = _textAlpha;
                _appliedTextAlpha = _textAlpha;
            }
        }
    }
}
