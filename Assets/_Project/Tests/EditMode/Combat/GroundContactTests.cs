using NUnit.Framework;
using Pickleball.Sandbox.Combat;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// QA CB3: a one-frame grounded flicker on a slope used to start an air step
    /// (and so drop the ground chain), because the grace only delayed leaving
    /// the ground, not the track choice. Both now read the same answer.
    /// </summary>
    public class GroundContactTests
    {
        const float Grace = 0.08f;
        const float Frame = 1f / 60f;

        [Test]
        public void FlickerFrame_StaysOnGroundTrack()
        {
            var c = new GroundContact();
            c.Tick(true, Frame, Grace);
            bool left = c.Tick(false, Frame, Grace);             // one frame of flicker
            Assert.That(left, Is.False);
            Assert.That(c.Track, Is.EqualTo(ComboTrack.Ground));
            c.Tick(true, Frame, Grace);
            Assert.That(c.Track, Is.EqualTo(ComboTrack.Ground));
        }

        [Test]
        public void RealJump_BecomesAirAfterTheGrace_AndReportsLeavingOnce()
        {
            var c = new GroundContact();
            int leftCount = 0;
            for (int i = 0; i < 30; i++)                         // half a second off the ground
                if (c.Tick(false, Frame, Grace)) leftCount++;
            Assert.That(c.Track, Is.EqualTo(ComboTrack.Air));
            Assert.That(leftCount, Is.EqualTo(1));
        }

        [Test]
        public void Landing_IsImmediate()
        {
            var c = new GroundContact();
            for (int i = 0; i < 30; i++) c.Tick(false, Frame, Grace);
            c.Tick(true, Frame, Grace);
            Assert.That(c.IsGrounded, Is.True);
            Assert.That(c.Track, Is.EqualTo(ComboTrack.Ground));
        }

        [Test]
        public void Grace_IsFrameRateIndependent()
        {
            foreach (float dt in new[] { 1f / 30f, 1f / 60f, 1f / 144f })
            {
                var c = new GroundContact();
                float t = 0f;
                while (!c.IsAirborne) { c.Tick(false, dt, Grace); t += dt; }
                Assert.That(t, Is.GreaterThan(Grace).And.LessThanOrEqualTo(Grace + dt + 1e-5f), $"dt {dt}");
            }
        }
    }
}
