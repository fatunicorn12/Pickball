using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Pickleball.Cutscene.Script
{
    /// <summary>
    /// Reads <c>Docs/Story/OpeningCutscenes.md</c> into scenes and lines. The markdown is the source
    /// of truth for every line, so the importer re-parses it rather than anyone retyping text into
    /// an asset.
    /// <list type="bullet">
    /// <item>Scenes come from <c>## Scene N: Title</c> headings.</item>
    /// <item>Voiced lines are the <c>SPEAKER: [tag] text</c> lines inside <c>```text</c> blocks; tags
    /// outside those blocks (the "How to use" notes) are ignored.</item>
    /// <item>Italic lines (<c>*...*</c>) in a scene are its stage directions.</item>
    /// <item>Ids are <c>OC_S&lt;scene&gt;_&lt;nn&gt;</c>, two digits, script order within the scene.</item>
    /// </list>
    /// Plain C#, no Unity calls, so the parse is tested headless.
    /// </summary>
    public static class CutsceneScriptParser
    {
        /// <summary>Subtitle card line width in characters.</summary>
        public const int CardLineChars = 42;

        /// <summary>Lines per subtitle card.</summary>
        public const int CardLines = 2;

        /// <summary>Line id prefix ("Opening Cutscene").</summary>
        public const string IdPrefix = "OC_S";

        /// <summary>The id for a scene and a 1-based line index: <c>OC_S2_09</c>.</summary>
        public static string MakeId(int scene, int index) =>
            IdPrefix + scene.ToString(CultureInfo.InvariantCulture) + "_" +
            index.ToString("00", CultureInfo.InvariantCulture);

        /// <summary>Parses the whole markdown document.</summary>
        public static ParsedScript Parse(string markdown)
        {
            if (markdown == null) throw new ArgumentNullException(nameof(markdown));
            var scenes = new List<ParsedScene>();
            var lines = new List<ParsedLine>();

            int sceneNumber = 0;
            string sceneTitle = null;
            List<string> notes = null;
            int lineIndex = 0;
            bool inText = false, inOtherFence = false;

            string[] rows = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            foreach (string row in rows)
            {
                string trimmed = row.Trim();

                if (trimmed.StartsWith("```", StringComparison.Ordinal))
                {
                    if (inText || inOtherFence)
                    {
                        inText = false;
                        inOtherFence = false;
                    }
                    else if (trimmed.Substring(3).Trim() == "text" && sceneNumber > 0) inText = true;
                    else inOtherFence = true;
                    continue;
                }

                if (inText)
                {
                    if (trimmed.Length == 0) continue;
                    int colon = trimmed.IndexOf(':');
                    if (colon <= 0) continue;
                    string speaker = trimmed.Substring(0, colon).Trim();
                    if (!IsSpeaker(speaker)) continue;
                    string raw = trimmed.Substring(colon + 1).Trim();
                    lineIndex++;
                    lines.Add(BuildLine(sceneNumber, lineIndex, speaker, raw));
                    continue;
                }
                if (inOtherFence) continue;

                if (TryParseSceneHeading(trimmed, out int number, out string title))
                {
                    if (sceneNumber > 0) scenes.Add(new ParsedScene(sceneNumber, sceneTitle, notes));
                    sceneNumber = number;
                    sceneTitle = title;
                    notes = new List<string>();
                    lineIndex = 0;
                    continue;
                }
                if (trimmed.StartsWith("## ", StringComparison.Ordinal) && sceneNumber > 0)
                {
                    // A non-scene section ends the current scene.
                    scenes.Add(new ParsedScene(sceneNumber, sceneTitle, notes));
                    sceneNumber = 0;
                    sceneTitle = null;
                    notes = null;
                    continue;
                }
                if (sceneNumber > 0 && IsItalicLine(trimmed))
                    notes.Add(trimmed.Substring(1, trimmed.Length - 2).Trim());
            }
            if (sceneNumber > 0) scenes.Add(new ParsedScene(sceneNumber, sceneTitle, notes));
            return new ParsedScript(scenes, lines);
        }

        /// <summary>
        /// Builds one line: tags out (with positions), display text, cards. Exposed for tests and for
        /// anyone re-deriving a single line.
        /// </summary>
        public static ParsedLine BuildLine(int scene, int index, string speaker, string raw)
        {
            var tags = new List<ScriptTag>();
            string display = StripTags(raw, tags);

            var forced = new List<int>();
            for (int i = 0; i < tags.Count; i++)
                if (tags[i].IsMidLine && tags[i].IsHold && tags[i].Position < display.Length && !forced.Contains(tags[i].Position))
                    forced.Add(tags[i].Position);
            forced.Sort();

            var starts = new List<int>();
            string[] cards = SubtitleCardSplitter.Split(display, CardLineChars, CardLines, forced, starts);
            int midHoldAfter = -1;
            if (forced.Count > 0)
            {
                // The hold follows the last card that starts before the first forced break.
                for (int c = 0; c < starts.Count; c++)
                    if (starts[c] < forced[0]) midHoldAfter = c;
            }
            return new ParsedLine(MakeId(scene, index), scene, index, speaker, raw, tags, display, cards, midHoldAfter);
        }

        /// <summary>
        /// Removes every <c>[tag]</c>, collapses whitespace and trims. Each tag is added to
        /// <paramref name="tags"/> lower-cased, with the display index of the first character
        /// after it (0 for a leading tag).
        /// </summary>
        public static string StripTags(string raw, List<ScriptTag> tags)
        {
            var sb = new StringBuilder(raw.Length);
            bool pendingSpace = false;
            int i = 0;
            while (i < raw.Length)
            {
                char c = raw[i];
                if (c == '[')
                {
                    int close = raw.IndexOf(']', i + 1);
                    if (close > i)
                    {
                        string content = raw.Substring(i + 1, close - i - 1).Trim().ToLowerInvariant();
                        int pos = sb.Length + (pendingSpace && sb.Length > 0 ? 1 : 0);
                        tags?.Add(new ScriptTag(content, pos));
                        // A tag counts as whitespace: "big! [pause] I" becomes "big! I".
                        pendingSpace = true;
                        i = close + 1;
                        continue;
                    }
                }
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = true;
                    i++;
                    continue;
                }
                if (pendingSpace && sb.Length > 0) sb.Append(' ');
                pendingSpace = false;
                sb.Append(c);
                i++;
            }
            string display = sb.ToString();
            if (tags != null)
            {
                for (int t = 0; t < tags.Count; t++)
                    if (tags[t].Position > display.Length) tags[t] = new ScriptTag(tags[t].Text, display.Length);
            }
            return display;
        }

        static bool TryParseSceneHeading(string row, out int number, out string title)
        {
            number = 0;
            title = null;
            const string prefix = "## Scene ";
            if (!row.StartsWith(prefix, StringComparison.Ordinal)) return false;
            int colon = row.IndexOf(':', prefix.Length);
            if (colon < 0) return false;
            if (!int.TryParse(row.Substring(prefix.Length, colon - prefix.Length).Trim(),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out number)) return false;
            title = row.Substring(colon + 1).Trim();
            return true;
        }

        static bool IsSpeaker(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s)
                if (!(char.IsUpper(c) || char.IsDigit(c) || c == '_' || c == ' ')) return false;
            return true;
        }

        static bool IsItalicLine(string s) =>
            s.Length > 2 && s[0] == '*' && s[s.Length - 1] == '*' && s[1] != '*' && s[s.Length - 2] != '*';
    }
}
