using NUnit.Framework;
using Pickleball.Sandbox.Combat;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// The swing clock: blade window, chain window, burst release and end, all
    /// as fractions of the step's duration. The edge cases that matter are the
    /// long frame that jumps a whole window (the blade must still get its one
    /// sweep) and the burst that must fire exactly once.
    /// </summary>
    public class SwingTimelineTests
    {
        static readonly SwingWindows Windows = new(0.3f, 0.6f, 0.5f, 0.9f);

        [Test]
        public void Advance_ReportsBladeOnThenOffAtTheWindowEdges()
        {
            var t = new SwingTimeline();
            t.Start(1f, Windows);

            Assert.That(t.Advance(0.2f), Is.EqualTo(SwingSignal.None));
            Assert.That(t.Advance(0.15f) & SwingSignal.BladeOn, Is.EqualTo(SwingSignal.BladeOn));
            Assert.That(t.IsBladeLive, Is.True);
            Assert.That(t.Advance(0.3f) & SwingSignal.BladeOff, Is.EqualTo(SwingSignal.BladeOff));
            Assert.That(t.IsBladeLive, Is.False);
        }

        [Test]
        public void Advance_LongFrameAcrossTheWholeWindow_StillReportsBoth()
        {
            var t = new SwingTimeline();
            t.Start(1f, Windows);
            t.Advance(0.1f);
            var signal = t.Advance(0.6f);      // 0.1 -> 0.7: straight over 0.3-0.6
            Assert.That(signal & (SwingSignal.BladeOn | SwingSignal.BladeOff),
                        Is.EqualTo(SwingSignal.BladeOn | SwingSignal.BladeOff));
        }

        [Test]
        public void ChainWindow_OpensAtComboOpen()
        {
            var t = new SwingTimeline();
            t.Start(2f, Windows);
            t.Advance(0.9f);                   // 0.45
            Assert.That(t.IsChainWindowOpen, Is.False);
            t.Advance(0.2f);                   // 0.55
            Assert.That(t.IsChainWindowOpen, Is.True);
        }

        [Test]
        public void Advance_FinishesAtComboClose()
        {
            var t = new SwingTimeline();
            t.Start(1f, Windows);
            t.Advance(0.85f);
            Assert.That(t.IsRunning, Is.True);
            Assert.That(t.Advance(0.1f) & SwingSignal.Finished, Is.EqualTo(SwingSignal.Finished));
            Assert.That(t.IsRunning, Is.False);
            Assert.That(t.Advance(0.1f), Is.EqualTo(SwingSignal.None), "a finished swing is inert");
        }

        [Test]
        public void Burst_FiresOnceWhenItsFractionIsCrossed()
        {
            var t = new SwingTimeline();
            t.Start(1f, Windows, burstAt: 0.55f);
            Assert.That(t.Advance(0.5f) & SwingSignal.BurstDue, Is.EqualTo(SwingSignal.None));
            Assert.That(t.Advance(0.1f) & SwingSignal.BurstDue, Is.EqualTo(SwingSignal.BurstDue));
            Assert.That(t.Advance(0.1f) & SwingSignal.BurstDue, Is.EqualTo(SwingSignal.None));
        }

        [Test]
        public void Burst_NegativeFractionMeansNoBurst()
        {
            var t = new SwingTimeline();
            t.Start(1f, Windows, burstAt: -1f);
            Assert.That(t.Advance(1f) & SwingSignal.BurstDue, Is.EqualTo(SwingSignal.None));
        }

        [Test]
        public void Stop_WhileLive_ReportsBladeOff()
        {
            var t = new SwingTimeline();
            t.Start(1f, Windows);
            t.Advance(0.4f);
            Assert.That(t.Stop(), Is.EqualTo(SwingSignal.BladeOff));
            Assert.That(t.IsRunning, Is.False);
        }
    }
}
