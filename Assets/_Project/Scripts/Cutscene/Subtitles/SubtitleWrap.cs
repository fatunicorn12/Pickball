using System;
using System.Text;

namespace Pickleball.Cutscene.Subtitles
{
    /// <summary>
    /// Breaks one subtitle card into at most N lines of at most M characters, at word boundaries
    /// only, and returns it with explicit <c>\n</c> breaks. The subtitle text has TMP word wrapping
    /// off, so where a line breaks is decided here, once per card, by a rule that can be tested
    /// against every card in the script, rather than by the font's glyph widths at runtime.
    ///
    /// <para><b>Rule.</b> A card that fits on one line stays on one line. Otherwise it uses the
    /// fewest lines that fit, and among those picks the break with the lowest cost: the longest
    /// line, minus a bonus when the break follows sentence punctuation (<c>. ! ?</c>) or clause
    /// punctuation (<c>, ; :</c>). So lines come out balanced (never 40/4 when 22/22 fits), but a
    /// slightly less even break at a full stop beats an even one mid-phrase, which is how broadcast
    /// subtitles read. Ties go to the shorter first line.</para>
    ///
    /// <para><b>Overflow.</b> Some cards can't fit (a word longer than a line, or no break point
    /// that leaves both halves short enough). Words are never broken: the wrap then minimises the
    /// characters over the limit, and reports <c>fits = false</c> so the caller can warn that the
    /// pipeline should have split the line into two cards.</para>
    ///
    /// Allocates (it splits and joins strings), so call it when a card arrives, never per frame.
    /// </summary>
    public static class SubtitleWrap
    {
        /// <summary>Default bonus, in characters, for breaking after <c>. ! ?</c>.</summary>
        public const int DefaultSentenceBreakBonus = 8;

        /// <summary>Default bonus, in characters, for breaking after <c>, ; :</c>.</summary>
        public const int DefaultClauseBreakBonus = 4;

        static readonly char[] Whitespace = { ' ', '\t', '\n', '\r' };

        /// <summary>Wraps with the default punctuation bonuses.</summary>
        public static string Wrap(string text, int maxCharsPerLine, int maxLines, out bool fits) =>
            Wrap(text, maxCharsPerLine, maxLines, DefaultSentenceBreakBonus, DefaultClauseBreakBonus, out fits);

        /// <summary>
        /// Wraps <paramref name="text"/> into at most <paramref name="maxLines"/> lines of at most
        /// <paramref name="maxCharsPerLine"/> characters, joined by <c>\n</c>. Runs of whitespace
        /// (including existing line breaks) collapse to single spaces first.
        /// </summary>
        /// <param name="text">The card text; null is treated as empty.</param>
        /// <param name="maxCharsPerLine">Longest line allowed, in characters (at least 1).</param>
        /// <param name="maxLines">Most lines allowed (at least 1).</param>
        /// <param name="sentenceBreakBonus">Characters of imbalance a break after <c>. ! ?</c> may cost.</param>
        /// <param name="clauseBreakBonus">Characters of imbalance a break after <c>, ; :</c> may cost.</param>
        /// <param name="fits">False when no word-boundary wrap meets both limits.</param>
        public static string Wrap(string text, int maxCharsPerLine, int maxLines,
                                  int sentenceBreakBonus, int clauseBreakBonus, out bool fits)
        {
            maxCharsPerLine = Math.Max(1, maxCharsPerLine);
            maxLines = Math.Max(1, maxLines);
            string[] words = (text ?? string.Empty).Split(Whitespace, StringSplitOptions.RemoveEmptyEntries);
            int n = words.Length;
            fits = true;
            if (n == 0) return string.Empty;

            // prefix[i] = total characters of words[0..i-1], without spaces.
            var prefix = new int[n + 1];
            for (int i = 0; i < n; i++) prefix[i + 1] = prefix[i] + words[i].Length;
            var bonus = new int[n];
            for (int i = 0; i < n; i++) bonus[i] = BreakBonus(words[i], sentenceBreakBonus, clauseBreakBonus);

            if (LineLength(prefix, 0, n) <= maxCharsPerLine) return string.Join(" ", words);

            var search = new Search(prefix, bonus, maxCharsPerLine);
            for (int lines = 2; lines <= Math.Min(maxLines, n); lines++)
            {
                search.Run(lines);
                if (search.BestOverflow == 0) return Join(words, search.BestBreaks);
            }

            // Nothing fits: least overflow at the most lines allowed, words kept whole.
            fits = false;
            int most = Math.Min(maxLines, n);
            if (most < 2) return string.Join(" ", words);
            search.Run(most);
            return Join(words, search.BestBreaks);
        }

        /// <summary>Characters in words [from, to) joined by single spaces.</summary>
        static int LineLength(int[] prefix, int from, int to) => prefix[to] - prefix[from] + (to - from - 1);

        static int BreakBonus(string word, int sentence, int clause)
        {
            char last = word[word.Length - 1];
            if (last == '"' || last == '\'' || last == ')') last = word.Length > 1 ? word[word.Length - 2] : last;
            switch (last)
            {
                case '.': case '!': case '?': case '…': return sentence;
                case ',': case ';': case ':': case '—': return clause;
                default: return 0;
            }
        }

        static string Join(string[] words, int[] breaks)
        {
            // breaks[j] = index of the first word on line j + 1.
            var sb = new StringBuilder();
            int line = 0;
            for (int i = 0; i < words.Length; i++)
            {
                if (i > 0)
                {
                    bool lineBreak = line < breaks.Length && breaks[line] == i;
                    sb.Append(lineBreak ? '\n' : ' ');
                    if (lineBreak) line++;
                }
                sb.Append(words[i]);
            }
            return sb.ToString();
        }

        /// <summary>
        /// Exhaustive search over break positions for an exact line count. Cards are at most a
        /// few dozen words and two or three lines, so this is a few hundred candidates at most.
        /// Ordering: least overflow, then lowest cost (longest line minus the mean break bonus),
        /// then the shortest first line.
        /// </summary>
        sealed class Search
        {
            readonly int[] _prefix;
            readonly int[] _bonus;
            readonly int _max;
            int[] _current;

            public int[] BestBreaks;
            public int BestOverflow;
            float _bestCost;
            int _bestFirst;

            public Search(int[] prefix, int[] bonus, int max)
            {
                _prefix = prefix;
                _bonus = bonus;
                _max = max;
            }

            public void Run(int lines)
            {
                _current = new int[lines - 1];
                BestBreaks = null;
                BestOverflow = int.MaxValue;
                _bestCost = float.MaxValue;
                _bestFirst = int.MaxValue;
                Place(0, 1);
            }

            void Place(int slot, int firstAllowed)
            {
                int words = _prefix.Length - 1;
                if (slot == _current.Length)
                {
                    Score();
                    return;
                }
                int remainingSlots = _current.Length - slot - 1;
                for (int b = firstAllowed; b <= words - 1 - remainingSlots; b++)
                {
                    _current[slot] = b;
                    Place(slot + 1, b + 1);
                }
            }

            void Score()
            {
                int words = _prefix.Length - 1;
                int overflow = 0, longest = 0, bonusSum = 0, start = 0;
                int first = 0;
                for (int line = 0; line <= _current.Length; line++)
                {
                    int end = line < _current.Length ? _current[line] : words;
                    int length = LineLength(_prefix, start, end);
                    if (line == 0) first = length;
                    if (length > longest) longest = length;
                    if (length > _max) overflow += length - _max;
                    if (line < _current.Length) bonusSum += _bonus[end - 1];
                    start = end;
                }
                float cost = longest - (float)bonusSum / _current.Length;

                bool better = overflow < BestOverflow
                              || (overflow == BestOverflow && (cost < _bestCost
                                  || (cost == _bestCost && first < _bestFirst)));
                if (!better) return;
                BestOverflow = overflow;
                _bestCost = cost;
                _bestFirst = first;
                BestBreaks = (int[])_current.Clone();
            }
        }
    }
}
