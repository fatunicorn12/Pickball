using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Cutscene.Script;
using Pickleball.Cutscene.Timeline.Plan;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>
    /// Card splits and card timing against the shot list §3 table: every line checked. A card must
    /// word-wrap into 2 lines of 42 characters, which splits OC_S3_08 (80 chars, wraps 36/43).
    /// </summary>
    public class SubtitleCardTests
    {
        static readonly Dictionary<string, string[]> Split = new Dictionary<string, string[]>
        {
            { "OC_S2_05", new[] { "Can you help me find my propeller? Please?", "I can't fly without it. I can barely stand without it." } },
            { "OC_S2_09", new[] { "It's round! It spins! It's about this big!", "I can't show you how big. My wings are very small." } },
            { "OC_S3_08", new[] { "Well, not everywhere. I crashed.", "But before I crashed, I was definitely looking." } },
            { "OC_S6_05", new[] { "The greatest minds that ever lived. Each one keeps a big idea safe.", "Angles. Stars. Gravity. That kind of thing." } },
            { "OC_S6_07", new[] { "They've all been corrupted by something.", "And the world's been coming apart at the edges." } },
        };

        [Test]
        public void EveryLineSplitsAsTheShotListSays()
        {
            foreach (var line in PipelineTestData.Script().Lines)
            {
                if (Split.TryGetValue(line.Id, out var expected))
                    Assert.That(line.Cards, Is.EqualTo(expected), line.Id);
                else
                    Assert.That(line.Cards, Is.EqualTo(new[] { line.DisplayText }), line.Id + " is one card");
            }
        }

        [Test]
        public void EveryCardWrapsIntoTwoLinesOf42()
        {
            foreach (var line in PipelineTestData.Script().Lines)
                foreach (var card in line.Cards)
                    Assert.That(SubtitleCardSplitter.Fits(card, 42, 2), Is.True, $"{line.Id}: \"{card}\"");
        }

        [Test]
        public void FitIsAboutWrappingNotLength()
        {
            Assert.That(SubtitleCardSplitter.Fits("Well, not everywhere. I crashed. But before I crashed, I was definitely looking.", 42, 2), Is.False);
            Assert.That(SubtitleCardSplitter.Fits("I can't fly without it. I can barely stand without it.", 42, 2), Is.True);
        }

        [Test]
        public void ForcedBreakWinsOverBalance()
        {
            var cards = SubtitleCardSplitter.Split("One. Two. Three.", 42, 2, new[] { 5 });
            Assert.That(cards, Is.EqualTo(new[] { "One.", "Two. Three." }));
        }

        static void AssertCardStarts(string id, params double[] starts)
        {
            var span = TimelineLayout.PlaceLine(PipelineTestData.Lines()[id], "test", 0, id == "OC_S2_09" ? 0.8f : 0f);
            Assert.That(span.Cards.Length, Is.EqualTo(starts.Length), id);
            for (int i = 0; i < starts.Length; i++)
                Assert.That(span.Cards[i].Start, Is.EqualTo(starts[i]).Within(0.006), $"{id} card {i}");
            Assert.That(span.Cards[starts.Length - 1].End, Is.EqualTo(span.SpeechEnd).Within(1e-6));
        }

        [Test]
        public void CardTimingsAreProportionalToCharacters()
        {
            AssertCardStarts("OC_S2_05", 0, 2.83);
            AssertCardStarts("OC_S2_09", 0, 3.63);   // card a stays up through the 0.8 s hold
            AssertCardStarts("OC_S6_05", 0, 4.51);
            AssertCardStarts("OC_S6_07", 0, 2.70);
        }

        [Test]
        public void MidLineHoldKeepsCardAUp()
        {
            var span = TimelineLayout.PlaceLine(PipelineTestData.Lines()["OC_S2_09"], "S2_SH09", 10, 0.8f);
            Assert.That(span.Cards[0].End, Is.EqualTo(span.Cards[1].Start));
            Assert.That(span.End - span.Start, Is.EqualTo(6.50 + 0.8).Within(1e-4));
        }
    }
}
