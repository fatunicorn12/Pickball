using Pickleball.Cutscene.Script;

namespace Pickleball.Cutscene.Timeline.Plan
{
    /// <summary>
    /// What the layout needs to know about one line: its cards (for card timing), its display length
    /// (for the placeholder rule), where a mid-line hold sits, and the audio length if a clip is
    /// assigned. A missing clip is normal for any line at any time (OC_S5_06's "..." never gets
    /// one): <see cref="AudioSeconds"/> is then negative and the placeholder rule applies.
    /// </summary>
    public sealed class LineSpec
    {
        /// <summary>Creates a spec.</summary>
        public LineSpec(string id, string speaker, int displayChars, string[] cards, int midHoldAfterCard, float audioSeconds)
        {
            Id = id;
            Speaker = speaker;
            DisplayChars = displayChars;
            Cards = cards ?? new string[0];
            MidHoldAfterCard = midHoldAfterCard;
            AudioSeconds = audioSeconds;
        }

        /// <summary>Line id.</summary>
        public string Id { get; }

        /// <summary>Speaker id.</summary>
        public string Speaker { get; }

        /// <summary>Display-text length.</summary>
        public int DisplayChars { get; }

        /// <summary>Subtitle cards.</summary>
        public string[] Cards { get; }

        /// <summary>Card a mid-line hold follows, or -1.</summary>
        public int MidHoldAfterCard { get; }

        /// <summary>Assigned clip length in seconds, or -1 when there is no clip.</summary>
        public float AudioSeconds { get; }

        /// <summary>True when a clip is assigned.</summary>
        public bool HasAudio => AudioSeconds >= 0f;

        /// <summary>The same line with a different audio length (negative for none).</summary>
        public LineSpec WithAudio(float seconds) => new LineSpec(Id, Speaker, DisplayChars, Cards, MidHoldAfterCard, seconds);

        /// <summary>From a parsed line, without audio.</summary>
        public static LineSpec FromParsed(ParsedLine line) =>
            new LineSpec(line.Id, line.Speaker, line.DisplayText.Length, line.Cards, line.MidHoldAfterCard, -1f);

        /// <summary>From an imported line; a null clip gives the placeholder timing.</summary>
        public static LineSpec FromScript(CutsceneScriptLine line) =>
            new LineSpec(line.Id, line.Speaker, line.DisplayText != null ? line.DisplayText.Length : 0,
                line.Cards, line.MidHoldAfterCard, line.AudioSeconds);
    }
}
