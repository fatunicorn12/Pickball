using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Cutscene.Script;
using Pickleball.Cutscene.Timeline.Plan;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>
    /// The minimum card time for voiced lines: a clip shorter than it keeps its card up for the
    /// minimum, the line's slot stretches to match, and everything after it moves back by the
    /// difference. Placeholder timing is untouched.
    /// </summary>
    public class MinCardTimeTests
    {
        [Test]
        public void ShortClip_GetsAOneSecondSlot()
        {
            var line = new LineSpec("OC_T_01", "MC", 12, new[] { "Status!" }, -1, 0.43f);
            var span = TimelineLayout.PlaceLine(line, "T_SH01", 10.0, 0f, 1.0f);
            Assert.That(span.SpeechEnd - span.Start, Is.EqualTo(1.0).Within(1e-6), "card stays up 1.0 s");
            Assert.That(span.End - span.Start, Is.EqualTo(1.0 + LineTiming.Gap).Within(1e-6), "then the 0.3 s gap");
            Assert.That(span.Cards[0].End - span.Cards[0].Start, Is.EqualTo(1.0).Within(1e-6));
            Assert.That(LineTiming.VoicedSlotSpeech(3.2f, 1.0f), Is.EqualTo(3.2f), "a long clip is unchanged");
        }

        [Test]
        public void ShortClip_ShiftsEveryLaterBeatByTheDifference()
        {
            const string id = "OC_S1_02";
            var audio = new Dictionary<string, float> { { id, 0.43f } };
            var lines = PipelineTestData.Lines(audio);
            var without = TimelineLayout.Layout(1, OpeningTimingPlan.Scene(1), lines, 0f);
            var with = TimelineLayout.Layout(1, OpeningTimingPlan.Scene(1), lines, 1.0f);

            double at = with.FindLine(id).Start;
            Assert.That(at, Is.EqualTo(without.FindLine(id).Start).Within(1e-6), "the line starts where it did");
            Assert.That(with.FindLine(id).SpeechEnd - at, Is.EqualTo(1.0).Within(1e-6));
            int later = 0;
            for (int i = 0; i < with.Shots.Count; i++)
            {
                for (int b = 0; b < with.Shots[i].BeatStarts.Length; b++)
                {
                    double t0 = without.Shots[i].BeatStarts[b], t1 = with.Shots[i].BeatStarts[b];
                    double expected = t0 > at + 1e-6 ? 0.57 : 0.0;
                    if (expected > 0) later++;
                    Assert.That(t1 - t0, Is.EqualTo(expected).Within(1e-5), $"shot {i} beat {b}");
                }
            }
            Assert.Greater(later, 0, "the test must reach beats after the line");
            Assert.That(with.Duration - without.Duration, Is.EqualTo(0.57).Within(1e-5));
        }

        [Test]
        public void MultiCardLine_GetsTheMinimumPerCardWhereItFits()
        {
            // 2.5 s of audio split 10:90 by characters would give the first card 0.25 s.
            var line = new LineSpec("OC_T_02", "BIRDY", 100, new[] { new string('a', 10), new string('b', 90) }, -1, 2.5f);
            var span = TimelineLayout.PlaceLine(line, "T_SH01", 0.0, 0f, 1.0f);
            Assert.That(span.Cards[0].End - span.Cards[0].Start, Is.EqualTo(1.0).Within(1e-6));
            Assert.That(span.Cards[1].End - span.Cards[1].Start, Is.EqualTo(1.5).Within(1e-6));
            Assert.That(span.SpeechEnd, Is.EqualTo(2.5).Within(1e-6), "the line's total is the clip");

            // 1.6 s can't give both cards 1.0 s: they share it equally.
            var tight = new LineSpec("OC_T_03", "BIRDY", 100, new[] { new string('a', 10), new string('b', 90) }, -1, 1.6f);
            var t = TimelineLayout.PlaceLine(tight, "T_SH01", 0.0, 0f, 1.0f);
            Assert.That(t.Cards[0].End - t.Cards[0].Start, Is.EqualTo(0.8).Within(1e-6));
            Assert.That(t.Cards[1].End - t.Cards[1].Start, Is.EqualTo(0.8).Within(1e-6));
        }

        [Test]
        public void PlaceholderTiming_IsUnchanged()
        {
            var line = new LineSpec("OC_T_04", "MC", 3, new[] { "a", "bbbbbbbbbbbbbbbbbbbbbb" }, -1, -1f);
            var a = TimelineLayout.PlaceLine(line, "T_SH01", 0.0, 0f, 0f);
            var b = TimelineLayout.PlaceLine(line, "T_SH01", 0.0, 0f, 1.0f);
            Assert.That(b.SpeechEnd, Is.EqualTo(a.SpeechEnd).Within(1e-9));
            for (int i = 0; i < a.Cards.Length; i++)
                Assert.That(b.Cards[i].Start, Is.EqualTo(a.Cards[i].Start).Within(1e-9));
        }
    }
}
