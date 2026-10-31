using NUnit.Framework;
using Pickleball.Cutscene.Playback;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>Hold-to-skip: progress, release reset, fires once, frame-rate independent.</summary>
    public class SkipHoldTests
    {
        [Test]
        public void ProgressRisesWhileHeld()
        {
            var hold = new SkipHold(1f);
            hold.Arm(false);
            Assert.That(hold.Progress, Is.EqualTo(0f));
            hold.Tick(true, 0.25f);
            Assert.That(hold.Progress, Is.EqualTo(0.25f).Within(1e-5f));
            hold.Tick(true, 0.5f);
            Assert.That(hold.Progress, Is.EqualTo(0.75f).Within(1e-5f));
        }

        [Test]
        public void ReleaseResets()
        {
            var hold = new SkipHold(1f);
            hold.Arm(false);
            hold.Tick(true, 0.9f);
            hold.Tick(false, 0.016f);
            Assert.That(hold.Progress, Is.EqualTo(0f));
            Assert.That(hold.Tick(true, 0.5f), Is.False);
            Assert.That(hold.Fired, Is.False);
        }

        [Test]
        public void FiresExactlyOnce()
        {
            var hold = new SkipHold(1f);
            hold.Arm(false);
            int fires = 0;
            for (int i = 0; i < 200; i++)
                if (hold.Tick(true, 1f / 60f)) fires++;
            Assert.That(fires, Is.EqualTo(1));
            Assert.That(hold.Progress, Is.EqualTo(1f));
            hold.Tick(false, 0.1f);
            for (int i = 0; i < 200; i++)
                if (hold.Tick(true, 1f / 60f)) fires++;
            Assert.That(fires, Is.EqualTo(1), "no second skip until re-armed");
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void FiresAtOneSecondAtAnyFrameRate(float dt)
        {
            var hold = new SkipHold(1f);
            hold.Arm(false);
            float t = 0f;
            while (!hold.Tick(true, dt)) t += dt;
            t += dt;
            Assert.That(t, Is.EqualTo(1f).Within(dt + 1e-4f));
        }

        [Test]
        public void APressHeldBeforeTheCutsceneIsIgnoredUntilReleased()
        {
            var hold = new SkipHold(1f);
            hold.Arm(true);
            for (int i = 0; i < 120; i++) Assert.That(hold.Tick(true, 1f / 60f), Is.False);
            Assert.That(hold.Progress, Is.EqualTo(0f));
            hold.Tick(false, 1f / 60f);
            Assert.That(hold.WaitingForRelease, Is.False);
            bool fired = false;
            for (int i = 0; i < 70 && !fired; i++) fired = hold.Tick(true, 1f / 60f);
            Assert.That(fired, Is.True);
        }

        [Test]
        public void NegativeTimeIsIgnored()
        {
            var hold = new SkipHold(1f);
            hold.Arm(false);
            hold.Tick(true, -5f);
            Assert.That(hold.Progress, Is.EqualTo(0f));
        }
    }
}
