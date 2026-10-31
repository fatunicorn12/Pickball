using System.Collections.Generic;

namespace Pickleball.Cutscene.Script
{
    /// <summary>
    /// One voiced line as <see cref="CutsceneScriptParser"/> reads it from the markdown: its stable
    /// id, the speaker, the raw text, the tags, the tag-free display text and the subtitle cards.
    /// Plain data so the parser and the timing maths are testable without an asset.
    /// </summary>
    public sealed class ParsedLine
    {
        /// <summary>Creates a parsed line.</summary>
        public ParsedLine(string id, int scene, int index, string speaker, string rawText,
            IReadOnlyList<ScriptTag> tags, string displayText, string[] cards, int midHoldAfterCard)
        {
            Id = id;
            Scene = scene;
            Index = index;
            Speaker = speaker;
            RawText = rawText;
            Tags = tags;
            DisplayText = displayText;
            Cards = cards;
            MidHoldAfterCard = midHoldAfterCard;
        }

        /// <summary><c>OC_S&lt;scene&gt;_&lt;nn&gt;</c>.</summary>
        public string Id { get; }

        /// <summary>Scene number from the <c>## Scene N:</c> heading.</summary>
        public int Scene { get; }

        /// <summary>1-based position in the scene, in script order.</summary>
        public int Index { get; }

        /// <summary>Speaker id before the colon ("MC", "BIRDY").</summary>
        public string Speaker { get; }

        /// <summary>The text after the colon, tags included, as written.</summary>
        public string RawText { get; }

        /// <summary>Every tag in script order, lower-cased, with its display-text position.</summary>
        public IReadOnlyList<ScriptTag> Tags { get; }

        /// <summary>Tags removed, whitespace collapsed, trimmed; "..." kept.</summary>
        public string DisplayText { get; }

        /// <summary>Subtitle cards, each at most 84 characters.</summary>
        public string[] Cards { get; }

        /// <summary>
        /// Index of the card a mid-line hold tag follows (0 for OC_S2_09: the hold sits between cards
        /// a and b), or -1 when the line has none.
        /// </summary>
        public int MidHoldAfterCard { get; }
    }
}
