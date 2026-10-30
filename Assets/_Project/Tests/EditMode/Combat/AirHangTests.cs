using NUnit.Framework;
using Pickleball.Sandbox.Combat;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// Air-combo hang time is earned by connecting: a landed air hit holds the
    /// player up for a while, a whiff holds nothing, and the chain's end, a
    /// landing or a dash drops him at once.
    /// </summary>
    public class AirHangTests
    {
        const float Frame = 1f / 60f;
        const float PerHit = 0.6f;

        static int FramesHeld(AirHang hang, float dt, int maxFrames = 1000)
        {
            int held = 0;
            for (int i = 0; i < maxFrames; i++)
            {
                if (hang.Tick(true, false, false, dt) <= 0f) break;
                held++;
            }
            return held;
        }

        [Test]
        public void Whiff_DoesNotHold()
        {
            var hang = new AirHang();
            Assert.That(hang.Tick(true, false, false, Frame), Is.Zero);
            Assert.That(hang.IsHolding, Is.False);
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void LandedHit_HoldsForItsWindow_ThenReleases(float dt)
        {
            var hang = new AirHang();
            hang.HitLanded(PerHit);
            float heldSeconds = FramesHeld(hang, dt) * dt;
            Assert.That(heldSeconds, Is.EqualTo(PerHit).Within(dt + 1e-4f));
            Assert.That(hang.IsHolding, Is.False);
        }

        [Test]
        public void EachHit_RefreshesTheHold()
        {
            var hang = new AirHang();
            hang.HitLanded(PerHit);
            for (int i = 0; i < 30; i++) hang.Tick(true, false, false, Frame);   // 0.5 s later
            hang.HitLanded(PerHit);
            float heldSeconds = FramesHeld(hang, Frame) * Frame;
            Assert.That(heldSeconds, Is.EqualTo(PerHit).Within(Frame + 1e-4f));
        }

        [Test]
        public void SimultaneousHits_DoNotStack()
        {
            // A ring striking three targets in one frame is one hit's worth.
            var hang = new AirHang();
            hang.HitLanded(PerHit);
            hang.HitLanded(PerHit);
            hang.HitLanded(PerHit);
            float heldSeconds = FramesHeld(hang, Frame) * Frame;
            Assert.That(heldSeconds, Is.EqualTo(PerHit).Within(Frame + 1e-4f));
        }

        [Test]
        public void ChainEnding_DropsAtOnce_AndForgetsTheHit()
        {
            var hang = new AirHang();
            hang.HitLanded(PerHit);
            Assert.That(hang.Tick(false, false, false, Frame), Is.Zero);
            // A fresh air swing afterwards must not inherit the old hit's time.
            Assert.That(hang.Tick(true, false, false, Frame), Is.Zero);
        }

        [Test]
        public void Landing_Drops()
        {
            var hang = new AirHang();
            hang.HitLanded(PerHit);
            Assert.That(hang.Tick(true, true, false, Frame), Is.Zero);
            Assert.That(hang.IsHolding, Is.False);
        }

        [Test]
        public void DashOrTether_Drops()
        {
            var hang = new AirHang();
            hang.HitLanded(PerHit);
            Assert.That(hang.Tick(true, false, true, Frame), Is.Zero);
            Assert.That(hang.IsHolding, Is.False);
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 144f)]
        public void Hold_CoversTwoFrames(float dt)
        {
            // The motor may tick its countdown before this frame's refresh lands;
            // a shorter hold would flicker gravity on mid-hang.
            var hang = new AirHang();
            hang.HitLanded(PerHit);
            Assert.That(hang.Tick(true, false, false, dt), Is.EqualTo(2f * dt).Within(1e-6f));
        }
    }
}
