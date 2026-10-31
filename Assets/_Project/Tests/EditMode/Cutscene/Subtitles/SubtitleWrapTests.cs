using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Cutscene.Subtitles;

namespace Pickleball.Tests.Cutscene.Subtitles
{
    /// <summary>
    /// SubtitleWrap against every subtitle card of the opening cutscenes, taken from
    /// Docs/Story/OpeningCutscenes_ShotList.md §3 (65 lines, 69 cards: four lines split a/b).
    /// The card texts are embedded so the test doesn't depend on the doc's formatting.
    /// </summary>
    public class SubtitleWrapTests
    {
        const int MaxChars = 42;
        const int MaxLines = 2;

        /// <summary>The one card in the script that can't wrap into 2 × 42 at word boundaries
        /// (80 chars; the best break is 36/43). Reported to the lead: the pipeline should split it.</summary>
        const string UnwrappableCard = "OC_S3_08";

        public readonly struct Card
        {
            public readonly string LineId;
            public readonly string Speaker;
            public readonly int Index;
            public readonly int Count;
            public readonly string Text;

            public Card(string lineId, string speaker, int index, int count, string text)
            {
                LineId = lineId;
                Speaker = speaker;
                Index = index;
                Count = count;
                Text = text;
            }

            public override string ToString() => Count > 1 ? $"{LineId}{(char)('a' + Index)}" : LineId;
        }

        public static readonly Card[] Cards =
        {
            new Card("OC_S1_01", "MC", 0, 1, "...Okay. White void. I'm dead. Probably dead."),
            new Card("OC_S1_02", "MC", 0, 1, "Wait."),
            new Card("OC_S1_03", "MC", 0, 1, "Wait wait wait. White void. No body pain. Weird calm feeling."),
            new Card("OC_S1_04", "MC", 0, 1, "Fuck yeah. Isekai!"),
            new Card("OC_S1_05", "MC", 0, 1, "Okay. Okay okay okay. Status screen. Status! Open status."),
            new Card("OC_S1_06", "MC", 0, 1, "...Menu?"),
            new Card("OC_S1_07", "MC", 0, 1, "Cool. No menu. Hard mode. Love that for me."),
            new Card("OC_S2_01", "BIRDY", 0, 1, "No no no no no. Where'd it go? Where did it go?!"),
            new Card("OC_S2_02", "BIRDY", 0, 1, "My propeller! It was right on my head! It's always on my head!"),
            new Card("OC_S2_03", "MC", 0, 1, "...What is this guy on about?"),
            new Card("OC_S2_04", "BIRDY", 0, 1, "You! Hi! Sorry. Hi. Emergency."),
            new Card("OC_S2_05", "BIRDY", 0, 2, "Can you help me find my propeller? Please?"),
            new Card("OC_S2_05", "BIRDY", 1, 2, "I can't fly without it. I can barely stand without it."),
            new Card("OC_S2_06", "MC", 0, 1, "Okay, okay. I'll help. Where'd you last have it?"),
            new Card("OC_S2_07", "BIRDY", 0, 1, "The sky! I had it in the sky!"),
            new Card("OC_S2_08", "MC", 0, 1, "That's... not helpful."),
            new Card("OC_S2_09", "BIRDY", 0, 2, "It's round! It spins! It's about this big!"),
            new Card("OC_S2_09", "BIRDY", 1, 2, "I can't show you how big. My wings are very small."),
            new Card("OC_S2_10", "MC", 0, 1, "Great. First quest. Fetch a bird his hat."),
            new Card("OC_S3_01", "BIRDY", 0, 1, "My propeller! You found it! Oh, thank you, thank you, thank you!"),
            new Card("OC_S3_02", "BIRDY", 0, 1, "And we're back, baby. One perfect circle, spinning very, very fast."),
            new Card("OC_S3_03", "BIRDY", 0, 1, "Wait."),
            new Card("OC_S3_04", "BIRDY", 0, 1, "Wait wait wait. That sword. That's a compass-blade."),
            new Card("OC_S3_05", "BIRDY", 0, 1, "You're... you're the one. Aren't you?"),
            new Card("OC_S3_06", "MC", 0, 1, "The one what?"),
            new Card("OC_S3_07", "BIRDY", 0, 1, "The Guardian! I've been looking everywhere for you!"),
            new Card("OC_S3_08", "BIRDY", 0, 1, "Well, not everywhere. I crashed. But before I crashed, I was definitely looking."),
            new Card("OC_S3_09", "MC", 0, 1, "I have no idea what you're talking about."),
            new Card("OC_S3_10", "BIRDY", 0, 1, "The prophecy! The ancient texts! The..."),
            new Card("OC_S3_11", "BIRDY", 0, 1, "...the thing a guy told me about once."),
            new Card("OC_S3_12", "MC", 0, 1, "Is this seriously my fucking companion for this world?"),
            new Card("OC_S3_13", "BIRDY", 0, 1, "...I can hear you. I have ears. They're just inside the helmet."),
            new Card("OC_S3_14", "BIRDY", 0, 1, "But that's okay! First impressions are just rough drafts!"),
            new Card("OC_S4_01", "MC", 0, 1, "Okay. Rules. Do I need to eat here? Sleep?"),
            new Card("OC_S4_02", "BIRDY", 0, 1, "Yes and yes!"),
            new Card("OC_S4_03", "MC", 0, 1, "Taxes?"),
            new Card("OC_S4_04", "BIRDY", 0, 1, "What's a taxes?"),
            new Card("OC_S4_05", "MC", 0, 1, "God, I love it here."),
            new Card("OC_S4_06", "MC", 0, 1, "So like... do I even need to worry about STDs in here?"),
            new Card("OC_S4_07", "BIRDY", 0, 1, "S-T-D? Is that like... a bird thing?"),
            new Card("OC_S4_08", "MC", 0, 1, "Never mind."),
            new Card("OC_S5_01", "BIRDY", 0, 1, "And here we are! The Academy!"),
            new Card("OC_S5_02", "MC", 0, 1, "Whoa. It's huge."),
            new Card("OC_S5_03", "BIRDY", 0, 1, "That's what she said."),
            new Card("OC_S5_04", "MC", 0, 1, "Wait. You didn't know what an STD was five minutes ago."),
            new Card("OC_S5_05", "BIRDY", 0, 1, "Oh! No, I have lots of sex. I just... I got them all."),
            new Card("OC_S5_06", "MC", 0, 1, "..."),
            new Card("OC_S5_07", "BIRDY", 0, 1, "I don't think they're a problem though!"),
            new Card("OC_S6_01", "MC", 0, 1, "Hey. What's happening over there?"),
            new Card("OC_S6_02", "MC", 0, 1, "Why is it... coming apart like that?"),
            new Card("OC_S6_03", "BIRDY", 0, 1, "Oh! That. Yeah. That's the bad part."),
            new Card("OC_S6_04", "BIRDY", 0, 1, "Okay! So. This world has Guardians. Eight of them."),
            new Card("OC_S6_05", "BIRDY", 0, 2, "The greatest minds that ever lived. Each one keeps a big idea safe."),
            new Card("OC_S6_05", "BIRDY", 1, 2, "Angles. Stars. Gravity. That kind of thing."),
            new Card("OC_S6_06", "BIRDY", 0, 1, "But lately, math hasn't been mathing."),
            new Card("OC_S6_07", "BIRDY", 0, 2, "They've all been corrupted by something."),
            new Card("OC_S6_07", "BIRDY", 1, 2, "And the world's been coming apart at the edges."),
            new Card("OC_S6_08", "BIRDY", 0, 1, "So you, my friend, are going to set them straight!"),
            new Card("OC_S6_09", "MC", 0, 1, "But how did this even happen? Why are they corrupted?"),
            new Card("OC_S6_10", "BIRDY", 0, 1, "I'm... not really sure. It's just been like this for a while."),
            new Card("OC_S6_11", "MC", 0, 1, "How long's a while?"),
            new Card("OC_S6_12", "BIRDY", 0, 1, "Long enough that people stopped asking."),
            new Card("OC_S6_13", "BIRDY", 0, 1, "But that's why you're here!"),
            new Card("OC_S6_14", "MC", 0, 1, "Why me, though?"),
            new Card("OC_S6_15", "BIRDY", 0, 1, "That compass you wield. Only navigators carry those."),
            new Card("OC_S6_16", "BIRDY", 0, 1, "You were a navigator. Somewhere else. Some other world."),
            new Card("OC_S6_17", "MC", 0, 1, "Huh?"),
            new Card("OC_S6_18", "BIRDY", 0, 1, "You'll figure it out as you go. Just know that this world needs you."),
            new Card("OC_S6_19", "BIRDY", 0, 1, "And hey! We're gonna do this together!"),
        };

        static IEnumerable<Card> WrappableCards()
        {
            foreach (var card in Cards)
                if (card.LineId != UnwrappableCard) yield return card;
        }

        [Test]
        public void Data_Covers65LinesIn69Cards()
        {
            var ids = new HashSet<string>();
            foreach (var card in Cards) ids.Add(card.LineId);
            Assert.AreEqual(65, ids.Count);
            Assert.AreEqual(69, Cards.Length);
            foreach (var card in Cards) Assert.LessOrEqual(card.Text.Length, 84, card.ToString());
        }

        [TestCaseSource(nameof(WrappableCards))]
        public void ScriptCard_WrapsIntoTwoLinesOf42_WordsWhole_Balanced(Card card)
        {
            string wrapped = SubtitleWrap.Wrap(card.Text, MaxChars, MaxLines, out bool fits);
            string[] lines = wrapped.Split('\n');

            Assert.IsTrue(fits, $"{card}: \"{wrapped}\"");
            Assert.LessOrEqual(lines.Length, MaxLines, card.ToString());
            foreach (var line in lines)
            {
                Assert.LessOrEqual(line.Length, MaxChars, $"{card}: \"{line}\"");
                Assert.AreEqual(line.Trim(), line, $"{card}: stray space at a break");
            }
            // No word broken or lost: the breaks replace spaces one for one.
            Assert.AreEqual(card.Text, wrapped.Replace('\n', ' '), card.ToString());

            if (card.Text.Length <= MaxChars)
            {
                Assert.AreEqual(1, lines.Length, $"{card} fits on one line but was broken");
                return;
            }
            // Balanced: never more than the sentence-break bonus longer than the most even split.
            int longest = System.Math.Max(lines[0].Length, lines[1].Length);
            Assert.LessOrEqual(longest, MostEvenLongest(card.Text) + SubtitleWrap.DefaultSentenceBreakBonus,
                               $"{card}: \"{wrapped}\"");
        }

        [Test]
        public void UnwrappableCard_KeepsWordsWhole_AndReportsOverflow()
        {
            var card = System.Array.Find(Cards, c => c.LineId == UnwrappableCard);
            string wrapped = SubtitleWrap.Wrap(card.Text, MaxChars, MaxLines, out bool fits);
            string[] lines = wrapped.Split('\n');

            Assert.IsFalse(fits);
            Assert.AreEqual(2, lines.Length);
            Assert.AreEqual(card.Text, wrapped.Replace('\n', ' '));
            Assert.AreEqual("Well, not everywhere. I crashed. But", lines[0]);   // 36 / 43: least overflow
            Assert.AreEqual(43, lines[1].Length);
        }

        [Test]
        public void SentenceBreak_BeatsAnEvenerMidPhraseBreak()
        {
            Assert.AreEqual("The greatest minds that ever lived.\nEach one keeps a big idea safe.",
                            SubtitleWrap.Wrap("The greatest minds that ever lived. Each one keeps a big idea safe.",
                                              MaxChars, MaxLines, out _));
            Assert.AreEqual("I can't fly without it.\nI can barely stand without it.",
                            SubtitleWrap.Wrap("I can't fly without it. I can barely stand without it.",
                                              MaxChars, MaxLines, out _));
        }

        [Test]
        public void NoPunctuation_SplitsEvenly_NotFortyFour()
        {
            // 10 four-letter words, 49 chars: the even split is 24/24, a greedy fill would be 39/9.
            string wrapped = SubtitleWrap.Wrap("abcd efgh ijkl mnop qrst uvwx yzab cdef ghij klmn",
                                               MaxChars, MaxLines, out bool fits);
            Assert.IsTrue(fits);
            Assert.AreEqual("abcd efgh ijkl mnop qrst\nuvwx yzab cdef ghij klmn", wrapped);
        }

        [Test]
        public void ZeroBonuses_GiveTheMostEvenSplit()
        {
            string text = "I can't fly without it. I can barely stand without it.";
            string wrapped = SubtitleWrap.Wrap(text, MaxChars, MaxLines, 0, 0, out _);
            string[] lines = wrapped.Split('\n');
            Assert.AreEqual(MostEvenLongest(text), System.Math.Max(lines[0].Length, lines[1].Length));
        }

        [Test]
        public void WordLongerThanALine_IsNeverBroken()
        {
            string word = new string('x', 50);
            string wrapped = SubtitleWrap.Wrap("a " + word + " b", MaxChars, MaxLines, out bool fits);
            Assert.IsFalse(fits);
            StringAssert.Contains(word, wrapped);
        }

        [Test]
        public void EmptyAndWhitespace_GiveEmpty()
        {
            Assert.AreEqual(string.Empty, SubtitleWrap.Wrap(null, MaxChars, MaxLines, out bool a));
            Assert.AreEqual(string.Empty, SubtitleWrap.Wrap("   ", MaxChars, MaxLines, out bool b));
            Assert.IsTrue(a && b);
        }

        [Test]
        public void ExistingBreaksAndDoubleSpaces_AreCollapsedFirst()
        {
            Assert.AreEqual("Hey. What's happening over there?",
                            SubtitleWrap.Wrap("Hey.\n What's  happening over there?", MaxChars, MaxLines, out _));
        }

        [Test]
        public void ThreeLinesAllowed_UsesTheFewestThatFit()
        {
            string text = "The greatest minds that ever lived. Each one keeps a big idea safe.";
            string wrapped = SubtitleWrap.Wrap(text, MaxChars, 3, out bool fits);
            Assert.IsTrue(fits);
            Assert.AreEqual(2, wrapped.Split('\n').Length);
        }

        /// <summary>Brute force, independent of SubtitleWrap: the smallest possible longest line
        /// over every two-line split at a space.</summary>
        static int MostEvenLongest(string text)
        {
            int best = int.MaxValue;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] != ' ') continue;
                int longest = System.Math.Max(i, text.Length - i - 1);
                if (longest < best) best = longest;
            }
            return best;
        }
    }
}
