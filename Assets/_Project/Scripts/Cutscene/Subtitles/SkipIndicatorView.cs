using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pickleball.Cutscene.Subtitles
{
    /// <summary>
    /// The hold-to-skip ring: a small radial ring in the bottom-right of the safe area that fills
    /// with <see cref="ICutscenePlayback.SkipProgress"/>, plus a "hold to skip" hint beside it while
    /// the hold is under way. Nothing shows until the player starts holding, so an untouched
    /// cutscene has no UI on it; it fades out again on release and whenever no cutscene plays.
    ///
    /// Presentation only: it polls the playback's two properties each frame (a bool and a float,
    /// no allocation) and never drives the skip itself. The rules for what shows when live in
    /// <see cref="SkipIndicatorRules"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkipIndicatorView : MonoBehaviour
    {
        [Header("Sources")]
        [Tooltip("A component implementing ICutscenePlayback (the cutscene player).")]
        [SerializeField] private MonoBehaviour playback;
        [Tooltip("Ring size, colours, hint text and fade time.")]
        [SerializeField] private SubtitleStyle style;

        [Header("Parts")]
        [Tooltip("Fades the ring (the hint has its own fade).")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("Soft dark panel behind the ring and hint (9-sliced), so they read over white.")]
        [SerializeField] private Image backing;
        [Tooltip("The faint full ring behind the arc.")]
        [SerializeField] private Image track;
        [Tooltip("The arc: Image type Filled, Radial 360, from the top, clockwise.")]
        [SerializeField] private Image fill;
        [Tooltip("The 'hold to skip' hint, left of the ring.")]
        [SerializeField] private TMP_Text label;

        const float FallbackFadeSeconds = 0.15f;

        ICutscenePlayback _playback;
        float _ring;
        float _hint;
        float _appliedRing = -1f;
        float _appliedHint = -1f;
        float _appliedFill = -1f;

        /// <summary>The ring's current opacity, 0..1.</summary>
        public float RingVisibility => _ring;

        /// <summary>The hint's current opacity, 0..1.</summary>
        public float LabelVisibility => _hint;

        /// <summary>The arc's current fill, 0..1.</summary>
        public float FillAmount => fill != null ? fill.fillAmount : 0f;

        void OnEnable()
        {
            _playback = playback as ICutscenePlayback;
            if (_playback == null && playback != null)
                Debug.LogWarning($"[SkipIndicatorView] {name}: playback {playback.GetType().Name} is not an " +
                                 "ICutscenePlayback; the ring will never show.", this);
            ApplyStyle();
            _ring = _hint = 0f;
            _appliedRing = _appliedHint = _appliedFill = -1f;
            Apply(0f);
        }

        void OnDisable()
        {
            _playback = null;
        }

        void Update()
        {
            Tick(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Reads the playback and advances the fades by <paramref name="unscaledDeltaTime"/>
        /// seconds. Update calls it with unscaled time; public so tests and previews can step it.
        /// </summary>
        public void Tick(float unscaledDeltaTime)
        {
            bool playing = _playback != null && _playback.IsPlaying;
            Tick(unscaledDeltaTime, playing, playing ? _playback.SkipProgress : 0f);
        }

        /// <summary>
        /// Advances the fades for an explicit playback state. The no-argument path reads the wired
        /// playback; this one lets tests and previews drive the ring without a playback component.
        /// </summary>
        /// <param name="unscaledDeltaTime">Seconds since the last tick, unscaled.</param>
        /// <param name="playing">Whether a cutscene is playing.</param>
        /// <param name="progress">Hold-to-skip progress, 0..1.</param>
        public void Tick(float unscaledDeltaTime, bool playing, float progress)
        {
            float fade = style != null ? style.SkipFadeSeconds : FallbackFadeSeconds;
            _ring = SubtitleFade.Step(_ring, SkipIndicatorRules.RingTarget(playing, progress), unscaledDeltaTime, fade);
            _hint = SubtitleFade.Step(_hint, SkipIndicatorRules.LabelTarget(playing, progress), unscaledDeltaTime, fade);
            Apply(SkipIndicatorRules.Fill(playing, progress));
        }

        /// <summary>
        /// Re-reads the style: ring size and colours, hint text and size, and the ring's offset from
        /// the safe area's corner. The setup menu calls it so the scene matches the style.
        /// </summary>
        public void ApplyStyle()
        {
            if (group != null)
            {
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            if (fill != null)
            {
                fill.type = Image.Type.Filled;
                fill.fillMethod = Image.FillMethod.Radial360;
                fill.fillOrigin = (int)Image.Origin360.Top;
                fill.fillClockwise = true;
                fill.raycastTarget = false;
                if (style != null) fill.color = style.SkipRingColour;
            }
            if (track != null)
            {
                track.type = Image.Type.Simple;
                track.raycastTarget = false;
                if (style != null) track.color = style.SkipTrackColour;
            }
            if (label != null)
            {
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.enableAutoSizing = false;
                label.overflowMode = TextOverflowModes.Overflow;
                label.richText = false;
                label.fontStyle = FontStyles.SmallCaps;
                label.alignment = TextAlignmentOptions.MidlineRight;
                label.raycastTarget = false;
                label.color = Color.white;
                if (style != null)
                {
                    label.text = style.SkipLabel;
                    label.fontSize = style.SkipLabelFontSize;
                }
                var rect = label.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 0.5f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.anchoredPosition = new Vector2(style != null ? -style.SkipLabelGap : 0f, 0f);
                Vector2 size = label.GetPreferredValues(label.text);
                rect.sizeDelta = new Vector2(Mathf.Ceil(size.x), Mathf.Ceil(size.y));
            }
            if (style != null && transform is RectTransform root)
            {
                root.anchorMin = root.anchorMax = new Vector2(1f, 0f);
                root.pivot = new Vector2(1f, 0f);
                root.anchoredPosition = new Vector2(-style.SkipMargin.x, style.SkipMargin.y);
                root.sizeDelta = new Vector2(style.SkipRingSize, style.SkipRingSize);
            }
            if (backing != null && style != null)
            {
                backing.raycastTarget = false;
                backing.color = style.SkipBackingColour;
                backing.pixelsPerUnitMultiplier = style.BackingCornerScale;
                // From the hint's left edge to the ring's right edge, padded, centred on the ring.
                Vector2 labelSize = label != null ? label.rectTransform.sizeDelta : Vector2.zero;
                Vector2 pad = style.SkipBackingPadding;
                float width = style.SkipRingSize + (label != null ? style.SkipLabelGap + labelSize.x : 0f) + 2f * pad.x;
                float height = Mathf.Max(style.SkipRingSize, labelSize.y) + 2f * pad.y;
                var rect = backing.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
                rect.pivot = new Vector2(1f, 0.5f);
                rect.anchoredPosition = new Vector2(pad.x, 0f);
                rect.sizeDelta = new Vector2(width, height);
            }
            _appliedRing = _appliedHint = _appliedFill = -1f;
        }

        void Apply(float fillAmount)
        {
            if (group != null && _ring != _appliedRing)
            {
                group.alpha = _ring;
                _appliedRing = _ring;
            }
            if (label != null && _hint != _appliedHint)
            {
                label.alpha = _hint;
                _appliedHint = _hint;
            }
            // While fading out after release the arc keeps the last fill instead of snapping empty.
            if (fill != null && fillAmount != _appliedFill && (fillAmount > 0f || _ring <= 0f))
            {
                fill.fillAmount = fillAmount;
                _appliedFill = fillAmount;
            }
        }
    }
}
