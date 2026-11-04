using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Enemies;
using UnityEngine;

namespace Pickleball.Tests.Enemies
{
    /// <summary>Strike timing, including against the player's real hit grace.</summary>
    public class AttackCadenceTests : BossTestFixture
    {
        const float Interval = 0.5f, Delay = 0.25f;

        [Test]
        public void OutOfReach_NeverStrikes()
        {
            var cadence = new AttackCadence();
            for (float t = 0f; t < 5f; t += 0.1f)
                Assert.IsFalse(cadence.Tick(t, false, Interval, Delay));
        }

        [Test]
        public void FirstStrike_WaitsTheDelay()
        {
            var cadence = new AttackCadence();
            Assert.IsFalse(cadence.Tick(10f, true, Interval, Delay));
            Assert.IsFalse(cadence.Tick(10.2f, true, Interval, Delay));
            Assert.IsTrue(cadence.Tick(10.25f, true, Interval, Delay));
        }

        [Test]
        public void SteppingOutAndBack_DoesNotShortenTheWait()
        {
            var cadence = new AttackCadence();
            cadence.Tick(0f, true, Interval, Delay);
            Assert.IsTrue(cadence.Tick(0.25f, true, Interval, Delay));
            cadence.Tick(0.3f, false, Interval, Delay);
            // Back in reach 0.05 s after the strike: still due 0.5 s after it.
            Assert.IsFalse(cadence.Tick(0.35f, true, Interval, Delay));
            Assert.IsFalse(cadence.Tick(0.74f, true, Interval, Delay));
            Assert.IsTrue(cadence.Tick(0.75f, true, Interval, Delay));
        }

        [Test]
        public void ReEnteringAfterALongGap_WaitsTheDelayAgain()
        {
            var cadence = new AttackCadence();
            cadence.Tick(0f, true, Interval, Delay);
            cadence.Tick(0.25f, true, Interval, Delay);
            cadence.Tick(1f, false, Interval, Delay);
            Assert.IsFalse(cadence.Tick(5f, true, Interval, Delay));
            Assert.IsTrue(cadence.Tick(5.25f, true, Interval, Delay));
        }

        [Test]
        public void AHitch_GivesOneStrikeNotABurst()
        {
            var cadence = new AttackCadence();
            cadence.Tick(0f, true, Interval, 0f);
            Assert.IsTrue(cadence.Tick(5f, true, Interval, 0f), "due after a 5 s hitch");
            Assert.IsFalse(cadence.Tick(5.01f, true, Interval, 0f), "no catch-up");
        }

        [Test]
        public void Reset_ForgetsTheLastStrike()
        {
            var cadence = new AttackCadence();
            cadence.Tick(0f, true, Interval, 0f);
            cadence.Reset();
            Assert.IsTrue(cadence.Tick(0.01f, true, Interval, 0f));
        }

        // The rate the user asked for: one hit per 0.5 s, at any frame rate. Ten seconds
        // holds 20 at most; a frame's overshoot per strike can cost one at 30 fps.
        [TestCase(30, 19)]
        [TestCase(60, 19)]
        [TestCase(144, 19)]
        public void SteadyContact_StrikesTwicePerSecond(int fps, int atLeast)
        {
            var cadence = new AttackCadence();
            float dt = 1f / fps;
            int strikes = 0;
            for (int frame = 0; frame < fps * 10; frame++)
                if (cadence.Tick(frame * dt, true, Interval, Delay)) strikes++;
            Assert.That(strikes, Is.InRange(atLeast, 20));
        }

        // The bug this shape exists to avoid: the player's Health refuses any discrete hit
        // for 0.5 s after the last, and a strike that came a hair early would vanish.
        [TestCase(30)]
        [TestCase(60)]
        [TestCase(75)]
        [TestCase(144)]
        public void EveryStrike_LandsThroughThePlayersHitGrace(int fps)
        {
            var player = NewComponent<Health>();
            player.SetMaxHealth(100000f);
            player.HitGraceSeconds = Interval;

            float now = 0f;
            player.TimeSource = () => now;

            var cadence = new AttackCadence();
            float dt = 1f / fps;
            int strikes = 0;
            for (int frame = 0; frame < fps * 30; frame++)
            {
                // Uneven frames, as a real game has; the clock is the one Health reads.
                now += dt * (frame % 7 == 0 ? 1.3f : 0.95f);
                if (!cadence.Tick(now, true, Interval, Delay)) continue;
                strikes++;
                player.Damage(10f, Vector3.zero);
            }

            Assert.Greater(strikes, 50);
            Assert.AreEqual(100000f - strikes * 10f, player.Current, 1e-2f,
                            "a strike was refused by the player's hit grace");
        }
    }
}
