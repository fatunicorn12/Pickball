using System.Text;

namespace Pickleball.Cutscene.Subtitles
{
    /// <summary>
    /// The last line of defence against a script direction reaching the screen. The pipeline
    /// strips <c>[tags]</c> before it raises a cue, so this should never fire; if it does, the view
    /// logs an error (a pipeline bug) and shows the line without the tag rather than showing
    /// "[pause]" to the player.
    /// </summary>
    public static class SubtitleTagGuard
    {
        /// <summary>True when <paramref name="text"/> holds a '[' followed later by a ']'.</summary>
        public static bool HasTag(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            int open = text.IndexOf('[');
            return open >= 0 && text.IndexOf(']', open + 1) > open;
        }

        /// <summary>
        /// Removes every <c>[…]</c> span, then collapses runs of spaces and trims. Returns the
        /// input unchanged (no allocation) when there is no tag. An unmatched '[' is left alone:
        /// it isn't a tag, and guessing where it ends could eat dialogue.
        /// </summary>
        /// <param name="text">Card text from a cue.</param>
        /// <param name="stripped">True when at least one tag was removed.</param>
        public static string Strip(string text, out bool stripped)
        {
            stripped = HasTag(text);
            if (!stripped) return text ?? string.Empty;

            var sb = new StringBuilder(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
                if (c == '[')
                {
                    int close = text.IndexOf(']', i + 1);
                    if (close > i)
                    {
                        // A tag stands for a gap between words, so leave one.
                        sb.Append(' ');
                        i = close + 1;
                        continue;
                    }
                }
                sb.Append(c);
                i++;
            }

            // Collapse whitespace and trim.
            var outSb = new StringBuilder(sb.Length);
            bool pendingSpace = false;
            for (int k = 0; k < sb.Length; k++)
            {
                char c = sb[k];
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = outSb.Length > 0;
                    continue;
                }
                if (pendingSpace) outSb.Append(' ');
                pendingSpace = false;
                outSb.Append(c);
            }
            return outSb.ToString();
        }
    }
}
