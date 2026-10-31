using System;
using UnityEngine;

namespace Pickleball.Cutscene.Subtitles
{
    /// <summary>
    /// Every tunable of the cutscene subtitles and the hold-to-skip ring. The views read it when
    /// they are enabled and on every card, so colours and sizes can be tuned in Play mode without
    /// re-running the setup menu. Sizes are in canvas reference pixels: the HUD canvas scales a
    /// 1920×1080 reference by width, so a value here is the same share of the screen width on a
    /// 16:9 monitor and a 19.5:9 phone.
    ///
    /// Created by <c>Tools ▸ Pickleball ▸ Cutscene ▸ 5 Build Subtitle HUD</c> at
    /// <c>ScriptableObjects/Cutscene/UI/SubtitleStyle.asset</c>; a re-run keeps tuned values and
    /// only adds speakers that are missing.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Cutscene/Subtitle Style", fileName = "SubtitleStyle")]
    public sealed class SubtitleStyle : ScriptableObject
    {
        [Header("Speakers")]
        [Tooltip("Name and colour per script speaker id. A speaker not listed shows its id in the " +
                 "fallback colour.")]
        [SerializeField] private SubtitleSpeakerStyle[] speakers = DefaultSpeakers();
        [Tooltip("Name colour for a speaker with no entry. Neutral so an unstyled speaker reads " +
                 "as 'not set up' rather than as someone else.")]
        [SerializeField] private Color fallbackSpeakerColour = new(0.92f, 0.92f, 0.92f, 1f);

        [Header("Text")]
        [Tooltip("Subtitle font size, reference px. 42 characters must fit across the screen: at " +
                 "44 px Roboto Bold a full line is about 1050 px of the 1920 width. Bigger reads " +
                 "better on a phone but leaves less room beside a notch.")]
        [Min(12f)] [SerializeField] private float fontSize = 44f;
        [Tooltip("Subtitle text colour. White on the dark backing is the broadcast standard.")]
        [SerializeField] private Color textColour = Color.white;
        [Tooltip("Longest line, characters. The pipeline cuts cards to 2 × this; lowering it " +
                 "without the pipeline makes long cards overflow.")]
        [Range(16, 80)] [SerializeField] private int maxCharsPerLine = 42;
        [Tooltip("Most lines per card. Two is the readable maximum for a moving scene.")]
        [Range(1, 3)] [SerializeField] private int maxLines = 2;
        [Tooltip("Characters of imbalance a line break after . ! ? may cost (so 'lived. / Each' " +
                 "beats an even break mid-phrase). 0 = always the most even split.")]
        [Range(0, 20)] [SerializeField] private int sentenceBreakBonus = SubtitleWrap.DefaultSentenceBreakBonus;
        [Tooltip("Characters of imbalance a line break after , ; : may cost.")]
        [Range(0, 20)] [SerializeField] private int clauseBreakBonus = SubtitleWrap.DefaultClauseBreakBonus;
        [Tooltip("Extra space between the two lines, in em/100 (TMP line spacing). Positive " +
                 "airs the lines out; negative packs them.")]
        [Range(-30f, 50f)] [SerializeField] private float lineSpacing = 0f;

        [Header("Timing")]
        [Tooltip("Seconds for the card to fade fully in or out (unscaled time). Shorter feels " +
                 "snappier; past ~0.3 s the text lags the voice.")]
        [Range(0f, 1f)] [SerializeField] private float fadeSeconds = 0.15f;
        [Tooltip("Seconds for the new text to fade in when a card replaces one on screen. The " +
                 "backing holds, so this is only the words changing.")]
        [Range(0f, 0.5f)] [SerializeField] private float cardSwapSeconds = 0.1f;

        [Header("Backing")]
        [Tooltip("Colour and opacity of the soft panel behind the text. More alpha reads better " +
                 "over the white void, less hides less of the shot.")]
        [SerializeField] private Color backingColour = new(0.02f, 0.025f, 0.04f, 0.9f);
        [Tooltip("Space between the text and the backing's edge, reference px (x = left/right, " +
                 "y = top/bottom). The backing's soft edge eats about 16 px of it.")]
        [SerializeField] private Vector2 backingPadding = new(40f, 18f);
        [Tooltip("Corner and edge softness of the backing sprite: higher = smaller, crisper " +
                 "corners. 1 shows the sprite's full 48 px feathered corner.")]
        [Range(0.5f, 6f)] [SerializeField] private float backingCornerScale = 1.4f;

        [Header("Safe area")]
        [Tooltip("Gap between the bottom of the safe area and the bottom of the backing, " +
                 "reference px. Keeps the card clear of the home indicator and thumbs.")]
        [Min(0f)] [SerializeField] private float bottomMargin = 48f;

        [Header("Speaker tab")]
        [Tooltip("Speaker name size, reference px. Small caps, so it reads smaller still.")]
        [Min(8f)] [SerializeField] private float tabFontSize = 28f;
        [Tooltip("Tab panel colour behind the name.")]
        [SerializeField] private Color tabColour = new(0.02f, 0.025f, 0.04f, 0.78f);
        [Tooltip("Space between the name and the tab's edge, reference px.")]
        [SerializeField] private Vector2 tabPadding = new(22f, 6f);
        [Tooltip("Gap between the tab and the top of the backing, reference px. Negative tucks the " +
                 "tab onto the backing.")]
        [SerializeField] private float tabGap = 4f;
        [Tooltip("Where the tab sits along the backing's top edge: 0 = flush left, 0.5 = centred, " +
                 "1 = flush right. Centred matches the centred text.")]
        [Range(0f, 1f)] [SerializeField] private float tabHorizontalAnchor = 0.5f;

        [Header("Skip indicator")]
        [Tooltip("Ring diameter, reference px.")]
        [Min(16f)] [SerializeField] private float skipRingSize = 64f;
        [Tooltip("Colour of the filling arc.")]
        [SerializeField] private Color skipRingColour = new(1f, 1f, 1f, 0.95f);
        [Tooltip("Colour of the unfilled track behind the arc. Faint, so the arc is what you see.")]
        [SerializeField] private Color skipTrackColour = new(1f, 1f, 1f, 0.22f);
        [Tooltip("Hint shown beside the ring while the hold is in progress.")]
        [SerializeField] private string skipLabel = "hold to skip";
        [Tooltip("Hint size, reference px (small caps).")]
        [Min(8f)] [SerializeField] private float skipLabelFontSize = 24f;
        [Tooltip("Gap between the hint and the ring, reference px.")]
        [Min(0f)] [SerializeField] private float skipLabelGap = 14f;
        [Tooltip("Soft panel behind the ring and hint, so both read over the white void as well " +
                 "as over a dark sky.")]
        [SerializeField] private Color skipBackingColour = new(0.02f, 0.025f, 0.04f, 0.6f);
        [Tooltip("Space between the ring/hint and the panel's edge, reference px.")]
        [SerializeField] private Vector2 skipBackingPadding = new(18f, 10f);
        [Tooltip("Seconds for the ring and hint to fade in or out (unscaled time).")]
        [Range(0f, 1f)] [SerializeField] private float skipFadeSeconds = 0.15f;
        [Tooltip("Ring offset from the safe area's bottom-right corner, reference px (x = from " +
                 "the right, y = from the bottom).")]
        [SerializeField] private Vector2 skipMargin = new(48f, 48f);

        /// <summary>
        /// The house speakers: Birdy in warm gold (bright, eager, the colour of his propeller) and
        /// MC in a soft cool grey (dry, reserved, and the player's own voice, so it shouldn't
        /// shout). A new array each call, so callers can't edit a shared default.
        /// </summary>
        public static SubtitleSpeakerStyle[] DefaultSpeakers() => new[]
        {
            new SubtitleSpeakerStyle(CutsceneSpeakers.Birdy, "Birdy", new Color(1f, 0.80f, 0.34f, 1f)),
            new SubtitleSpeakerStyle(CutsceneSpeakers.MC, "MC", new Color(0.90f, 0.92f, 0.98f, 1f)),
        };

        /// <summary>Name colour for speakers with no entry.</summary>
        public Color FallbackSpeakerColour => fallbackSpeakerColour;
        /// <summary>Subtitle font size, reference px.</summary>
        public float FontSize => fontSize;
        /// <summary>Subtitle text colour.</summary>
        public Color TextColour => textColour;
        /// <summary>Longest line, characters.</summary>
        public int MaxCharsPerLine => maxCharsPerLine;
        /// <summary>Most lines per card.</summary>
        public int MaxLines => maxLines;
        /// <summary>Wrap bonus for a break after sentence punctuation, characters.</summary>
        public int SentenceBreakBonus => sentenceBreakBonus;
        /// <summary>Wrap bonus for a break after clause punctuation, characters.</summary>
        public int ClauseBreakBonus => clauseBreakBonus;
        /// <summary>TMP line spacing, em/100.</summary>
        public float LineSpacing => lineSpacing;
        /// <summary>Full fade in or out, seconds.</summary>
        public float FadeSeconds => fadeSeconds;
        /// <summary>New-card text fade-in under a held backing, seconds.</summary>
        public float CardSwapSeconds => cardSwapSeconds;
        /// <summary>Backing colour and opacity.</summary>
        public Color BackingColour => backingColour;
        /// <summary>Text-to-backing padding, reference px.</summary>
        public Vector2 BackingPadding => backingPadding;
        /// <summary>Pixels-per-unit multiplier for the backing's 9-sliced sprite.</summary>
        public float BackingCornerScale => backingCornerScale;
        /// <summary>Gap below the backing inside the safe area, reference px.</summary>
        public float BottomMargin => bottomMargin;
        /// <summary>Speaker name size, reference px.</summary>
        public float TabFontSize => tabFontSize;
        /// <summary>Tab panel colour.</summary>
        public Color TabColour => tabColour;
        /// <summary>Name-to-tab padding, reference px.</summary>
        public Vector2 TabPadding => tabPadding;
        /// <summary>Tab-to-backing gap, reference px.</summary>
        public float TabGap => tabGap;
        /// <summary>Tab position along the backing's top, 0 left … 1 right.</summary>
        public float TabHorizontalAnchor => tabHorizontalAnchor;
        /// <summary>Ring diameter, reference px.</summary>
        public float SkipRingSize => skipRingSize;
        /// <summary>Filling arc colour.</summary>
        public Color SkipRingColour => skipRingColour;
        /// <summary>Unfilled track colour.</summary>
        public Color SkipTrackColour => skipTrackColour;
        /// <summary>Hint text.</summary>
        public string SkipLabel => skipLabel;
        /// <summary>Hint size, reference px.</summary>
        public float SkipLabelFontSize => skipLabelFontSize;
        /// <summary>Hint-to-ring gap, reference px.</summary>
        public float SkipLabelGap => skipLabelGap;
        /// <summary>Panel colour behind the ring and hint.</summary>
        public Color SkipBackingColour => skipBackingColour;
        /// <summary>Ring/hint-to-panel padding, reference px.</summary>
        public Vector2 SkipBackingPadding => skipBackingPadding;
        /// <summary>Ring and hint fade, seconds.</summary>
        public float SkipFadeSeconds => skipFadeSeconds;
        /// <summary>Ring offset from the safe area's bottom-right, reference px.</summary>
        public Vector2 SkipMargin => skipMargin;
        /// <summary>Number of speaker entries.</summary>
        public int SpeakerCount => speakers?.Length ?? 0;

        /// <summary>
        /// The entry for <paramref name="speakerId"/>, matched ignoring case. Loops instead of a
        /// dictionary so it never allocates and survives domain reloads without a rebuild step.
        /// </summary>
        public bool TryGetSpeaker(string speakerId, out SubtitleSpeakerStyle entry)
        {
            if (speakers != null && !string.IsNullOrEmpty(speakerId))
            {
                for (int i = 0; i < speakers.Length; i++)
                {
                    if (string.Equals(speakers[i].SpeakerId, speakerId, StringComparison.OrdinalIgnoreCase))
                    {
                        entry = speakers[i];
                        return true;
                    }
                }
            }
            entry = default;
            return false;
        }

        /// <summary>The speaker's name colour, or the fallback colour.</summary>
        public Color SpeakerColour(string speakerId) =>
            TryGetSpeaker(speakerId, out var entry) ? entry.Colour : fallbackSpeakerColour;

        /// <summary>The name shown in the speaker's tab: the entry's name, else the id itself.</summary>
        public string SpeakerName(string speakerId) =>
            TryGetSpeaker(speakerId, out var entry) ? entry.DisplayName : speakerId ?? string.Empty;

        /// <summary>
        /// Adds an entry for <paramref name="speakerId"/> unless one exists (any case). The setup
        /// menu uses it so a re-run fills in new speakers without overwriting tuned colours.
        /// </summary>
        /// <returns>True when an entry was added.</returns>
        public bool EnsureSpeaker(string speakerId, string displayName, Color colour)
        {
            if (TryGetSpeaker(speakerId, out _)) return false;
            int count = SpeakerCount;
            var grown = new SubtitleSpeakerStyle[count + 1];
            if (count > 0) Array.Copy(speakers, grown, count);
            grown[count] = new SubtitleSpeakerStyle(speakerId, displayName, colour);
            speakers = grown;
            return true;
        }
    }
}
