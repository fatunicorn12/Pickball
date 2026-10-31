using NUnit.Framework;
using Pickleball.Cutscene.Script;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>The duration rule: max(1.5, chars / 15) + 0.3, or the clip plus the same gap.</summary>
    public class LineTimingTests
    {
        [TestCase(45, 3.30f)]
        [TestCase(5, 1.80f)]
        [TestCase(22, 1.80f)]
        [TestCase(23, 1.8333f)]
        [TestCase(97, 6.7667f)]
        [TestCase(111, 7.70f)]
        [TestCase(0, 1.80f)]
        public void PlaceholderDuration(int chars, float expected)
        {
            Assert.That(LineTiming.PlaceholderDuration(chars), Is.EqualTo(expected).Within(0.0005f));
        }

        [Test]
        public void AudioSetsTheSpeechAndKeepsTheGap()
        {
            Assert.That(LineTiming.SlotSeconds(45, 5.0f), Is.EqualTo(5.3f).Within(1e-5f));
            Assert.That(LineTiming.SlotSeconds(45, -1f), Is.EqualTo(LineTiming.PlaceholderDuration(45)).Within(1e-5f));
            Assert.That(LineTiming.SpeechSeconds(3, 0.4f), Is.EqualTo(0.4f), "a short clip is not padded to 1.5 s");
        }
    }
}
