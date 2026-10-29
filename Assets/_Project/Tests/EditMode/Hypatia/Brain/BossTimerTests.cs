using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia;

namespace Pickleball.Tests.Hypatia.Brain
{
    /// <summary>
    /// Her attack timers. The rule that matters most: a zero multiplier (the
    /// planned Stationary Point) freezes a timer exactly, with nothing firing
    /// while frozen.
    /// </summary>
    public class BossTimerTests
    {
        [Test]
        public void FiresOnce_OnTheTickItRunsOut()
        {
            var timer = new BossTimer();
            timer.Start(1f);
            int fired = 0;
            for (int i = 0; i < 30; i++)
                if (timer.Tick(0.1f)) fired++;
            Assert.That(fired, Is.EqualTo(1));
            Assert.IsFalse(timer.IsRunning);
        }

        [Test]
        public void ZeroDt_FreezesIt_EvenWhenAlreadyDue()
        {
            var timer = new BossTimer();
            timer.Start(0f);
            for (int i = 0; i < 100; i++) Assert.IsFalse(timer.Tick(0f));
            Assert.IsTrue(timer.IsRunning);
            Assert.IsTrue(timer.Tick(0.016f));
        }

        [Test]
        public void ZeroDt_HoldsTheRemainingTime()
        {
            var timer = new BossTimer();
            timer.Start(2f);
            timer.Tick(0.5f);
            for (int i = 0; i < 100; i++) timer.Tick(0f);
            Assert.That(timer.Remaining, Is.EqualTo(1.5f).Within(1e-5f));
        }

        [Test]
        public void NotStarted_NeverFires()
        {
            var timer = new BossTimer();
            Assert.IsFalse(timer.Tick(100f));
        }

        [Test]
        public void Stop_CancelsWithoutFiring()
        {
            var timer = new BossTimer();
            timer.Start(0.1f);
            timer.Stop();
            Assert.IsFalse(timer.Tick(1f));
            Assert.That(timer.Remaining, Is.EqualTo(0f));
        }

        [Test]
        public void NaNStart_FiresOnTheNextRealTick()
        {
            var timer = new BossTimer();
            timer.Start(float.NaN);
            Assert.IsTrue(timer.Tick(0.01f));
        }
    }
}
