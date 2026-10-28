using NUnit.Framework;
using Pickleball.Sandbox.Combat;
using UnityEngine;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// QA CB1: the first blade-live frame tested nothing, and a frame that
    /// jumped the whole active window tested nothing — exactly the frames a
    /// low frame rate produces. These pin the sweep's two guarantees: a live
    /// frame always tests the blade where it is, and the arc since last frame
    /// is swept whenever there is a last frame.
    /// </summary>
    public class BladeSweepTests
    {
        static readonly (Vector3 from, Vector3 to)[] Segments = new (Vector3, Vector3)[BladeSweep.MaxSegments];
        static readonly SwingWindows Windows = new(0.3f, 0.6f, 0.6f, 0.9f);

        static readonly Vector3 BaseA = new(0f, 1f, 0f), TipA = new(0.8f, 1f, 0f);
        static readonly Vector3 BaseB = new(0f, 1f, 0.1f), TipB = new(0f, 1f, 0.9f);

        [Test]
        public void BladeOnFrame_WithNoHistory_TestsTheCurrentBlade()
        {
            var sweep = new BladeSweep();
            int n = sweep.Sample(BaseA, TipA, test: true, Segments);
            Assert.That(n, Is.EqualTo(1));
            Assert.That(Segments[0].from, Is.EqualTo(BaseA));
            Assert.That(Segments[0].to, Is.EqualTo(TipA));
        }

        [Test]
        public void FramesBeforeTheWindow_RecordHistory_SoTheFirstLiveFrameSweepsTheArc()
        {
            var sweep = new BladeSweep();
            Assert.That(sweep.Sample(BaseA, TipA, test: false, Segments), Is.EqualTo(0));
            Assert.That(sweep.HasHistory, Is.True);

            int n = sweep.Sample(BaseB, TipB, test: true, Segments);
            Assert.That(n, Is.EqualTo(3));
            Assert.That(Segments[1], Is.EqualTo((TipA, TipB)), "the tip's arc since last frame");
            Assert.That(Segments[2], Is.EqualTo((BaseA, BaseB)), "the base's arc since last frame");
        }

        [Test]
        public void WholeWindowFrame_TestsAtLeastOnce()
        {
            var timeline = new SwingTimeline();
            var sweep = new BladeSweep();
            timeline.Start(1f, Windows);

            var before = timeline.Advance(0.1f);                 // 0.1: before the window
            Assert.That(sweep.Sample(BaseA, TipA, BladeSweep.ShouldTest(timeline.IsBladeLive, before), Segments),
                        Is.EqualTo(0));

            var jump = timeline.Advance(0.6f);                   // 0.7: straight over 0.3-0.6
            Assert.That(timeline.IsBladeLive, Is.False);
            bool test = BladeSweep.ShouldTest(timeline.IsBladeLive, jump);
            Assert.That(test, Is.True, "a frame that crosses the whole window must be tested");
            Assert.That(sweep.Sample(BaseB, TipB, test, Segments), Is.EqualTo(3));
        }

        [Test]
        public void LastLiveInterval_IsTested_OnTheFrameTheBladeStops()
        {
            var timeline = new SwingTimeline();
            timeline.Start(1f, Windows);
            timeline.Advance(0.4f);                              // live
            var off = timeline.Advance(0.3f);                    // 0.7: just stopped
            Assert.That(BladeSweep.ShouldTest(timeline.IsBladeLive, off), Is.True);
            Assert.That(BladeSweep.ShouldTest(timeline.IsBladeLive, timeline.Advance(0.05f)), Is.False);
        }

        [Test]
        public void Reset_ForgetsHistory()
        {
            var sweep = new BladeSweep();
            sweep.Sample(BaseA, TipA, test: false, Segments);
            sweep.Reset();
            Assert.That(sweep.HasHistory, Is.False);
            Assert.That(sweep.Sample(BaseB, TipB, test: true, Segments), Is.EqualTo(1),
                        "no sweep from a pose left over from a previous swing");
        }
    }
}
