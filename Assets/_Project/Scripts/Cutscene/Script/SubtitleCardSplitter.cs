using System.Collections.Generic;

namespace Pickleball.Cutscene.Script
{
    /// <summary>
    /// Splits a line's display text into subtitle cards on sentence boundaries. A card fits when it
    /// word-wraps into at most <c>maxLines</c> lines of at most <c>lineChars</c> characters (2 × 42),
    /// which is stricter than "84 characters": OC_S3_08 is 80 characters but wraps 36/43, so it
    /// splits. The view never splits text itself, so the split here is what players read; it must
    /// match the shot list §3 table.
    /// <para>
    /// Rule: forced breaks (a mid-line hold tag) split first. Each piece then uses the fewest cards
    /// that fit, and among those the split whose longest card is shortest, which gives the most
    /// even cards ("...propeller? Please?" / "I can't fly..." rather than three sentences on one
    /// card). Boundaries are after <c>.</c>, <c>!</c> or <c>?</c> followed by a space; an ellipsis
    /// ("You're... you're") is not a boundary. Only if a sentence alone is too long does it fall
    /// back to word boundaries.
    /// </para>
    /// </summary>
    public static class SubtitleCardSplitter
    {
        /// <summary>Splits <paramref name="text"/>; see the class summary for the rule.</summary>
        public static string[] Split(string text, int lineChars, int maxLines, IReadOnlyList<int> forcedBreaks = null,
            List<int> cardStarts = null)
        {
            cardStarts?.Clear();
            var cards = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                cards.Add(text ?? string.Empty);
                cardStarts?.Add(0);
                return cards.ToArray();
            }

            int pieceStart = 0;
            if (forcedBreaks != null)
            {
                for (int i = 0; i < forcedBreaks.Count; i++)
                {
                    int b = forcedBreaks[i];
                    if (b <= pieceStart || b >= text.Length) continue;
                    SplitPiece(text, pieceStart, b, lineChars, maxLines, cards, cardStarts);
                    pieceStart = b;
                }
            }
            SplitPiece(text, pieceStart, text.Length, lineChars, maxLines, cards, cardStarts);
            return cards.ToArray();
        }

        /// <summary>Total characters over the cards (spaces between cards not counted).</summary>
        public static int TotalChars(string[] cards)
        {
            int n = 0;
            if (cards != null)
                for (int i = 0; i < cards.Length; i++) n += cards[i].Length;
            return n;
        }

        static void SplitPiece(string text, int start, int end, int lineChars, int maxLines, List<string> cards, List<int> starts)
        {
            var bounds = new List<int> { start };
            for (int i = start + 1; i < end - 1; i++)
                if (IsSentenceBoundary(text, i)) bounds.Add(i + 1);   // the space: card starts after it
            bounds.Add(end);

            if (!TryPartition(text, bounds, lineChars, maxLines, cards, starts))
            {
                bounds.Clear();
                bounds.Add(start);
                for (int i = start + 1; i < end; i++)
                    if (text[i] == ' ') bounds.Add(i);
                bounds.Add(end);
                if (!TryPartition(text, bounds, lineChars, maxLines, cards, starts))
                {
                    // A single word longer than a card: give up and keep it whole.
                    AddCard(text, start, end, cards, starts);
                }
            }
        }

        static bool IsSentenceBoundary(string text, int i)
        {
            char c = text[i];
            if (c != '.' && c != '!' && c != '?') return false;
            if (i + 1 >= text.Length || text[i + 1] != ' ') return false;
            if (c == '.' && i > 0 && text[i - 1] == '.') return false;   // ellipsis
            return true;
        }

        /// <summary>
        /// Fewest cards, then the smallest longest card. Dynamic programme over the boundaries,
        /// lexicographic on (cards, longest).
        /// </summary>
        static bool TryPartition(string text, List<int> bounds, int lineChars, int maxLines, List<string> cards, List<int> starts)
        {
            int n = bounds.Count;
            var count = new int[n];
            var longest = new int[n];
            var prev = new int[n];
            for (int j = 1; j < n; j++) count[j] = int.MaxValue;
            for (int j = 1; j < n; j++)
            {
                for (int i = 0; i < j; i++)
                {
                    if (count[i] == int.MaxValue) continue;
                    int len = TrimmedLength(text, bounds[i], bounds[j]);
                    if (!Fits(text, bounds[i], bounds[j], lineChars, maxLines)) continue;
                    int c = count[i] + 1;
                    int l = len > longest[i] ? len : longest[i];
                    if (c < count[j] || (c == count[j] && l < longest[j]))
                    {
                        count[j] = c;
                        longest[j] = l;
                        prev[j] = i;
                    }
                }
            }
            if (count[n - 1] == int.MaxValue) return false;

            var chosen = new List<int>();
            for (int j = n - 1; j > 0; j = prev[j]) chosen.Add(j);
            chosen.Reverse();
            int from = 0;
            foreach (int j in chosen)
            {
                AddCard(text, bounds[from], bounds[j], cards, starts);
                from = j;
            }
            return true;
        }

        static void AddCard(string text, int a, int b, List<string> cards, List<int> starts)
        {
            while (a < b && text[a] == ' ') a++;
            while (b > a && text[b - 1] == ' ') b--;
            cards.Add(text.Substring(a, b - a));
            starts?.Add(a);
        }

        /// <summary>
        /// True when the text word-wraps (greedy, single spaces) into at most <paramref name="maxLines"/>
        /// lines of at most <paramref name="lineChars"/> characters.
        /// </summary>
        public static bool Fits(string text, int lineChars, int maxLines) =>
            text != null && Fits(text, 0, text.Length, lineChars, maxLines);

        static bool Fits(string text, int a, int b, int lineChars, int maxLines)
        {
            while (a < b && text[a] == ' ') a++;
            while (b > a && text[b - 1] == ' ') b--;
            if (a >= b) return true;
            int lines = 1, lineLen = 0, i = a;
            while (i < b)
            {
                int wordEnd = i;
                while (wordEnd < b && text[wordEnd] != ' ') wordEnd++;
                int word = wordEnd - i;
                if (word > lineChars) return false;
                if (lineLen == 0) lineLen = word;
                else if (lineLen + 1 + word <= lineChars) lineLen += 1 + word;
                else
                {
                    if (++lines > maxLines) return false;
                    lineLen = word;
                }
                i = wordEnd;
                while (i < b && text[i] == ' ') i++;
            }
            return true;
        }

        static int TrimmedLength(string text, int a, int b)
        {
            while (a < b && text[a] == ' ') a++;
            while (b > a && text[b - 1] == ' ') b--;
            return b - a;
        }
    }
}
