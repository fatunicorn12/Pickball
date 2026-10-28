using NUnit.Framework;
using Pickleball.Sandbox.Combat;
using UnityEngine;

namespace Pickleball.Tests.Combat
{
    public class CombatFeelTests
    {
        static readonly float[] Steps = { 1f / 30f, 1f / 60f, 1f / 144f };

        [TestCaseSource(nameof(Steps))]
        public void ChainGrace_StaysOpenForItsDuration_ThenExpiresOnce(float dt)
        {
            var g = new ChainGrace();
            g.Start(0.45f);
            float t = 0f;
            int expiries = 0;
            while (t < 1f)
            {
                if (t + dt < 0.45f - 1e-4f) Assert.IsTrue(g.IsOpen, $"open at {t}");
                if (g.Tick(dt)) expiries++;
                t += dt;
            }
            Assert.AreEqual(1, expiries);
            Assert.IsFalse(g.IsOpen);
        }

        [Test]
        public void ChainGrace_NinetyHundredthsGapFitsInSwingPlusGrace()
        {
            // A 0.5 s swing + 0.45 s grace covers a 0.9 s click cadence (R1: it used to reset).
            var g = new ChainGrace();
            g.Start(0.45f);
            g.Tick(0.4f);   // press arrives 0.4 s after the swing ended
            Assert.IsTrue(g.IsOpen);
        }

        [Test]
        public void ChainGrace_CancelClosesIt()
        {
            var g = new ChainGrace();
            g.Start(1f);
            g.Cancel();
            Assert.IsFalse(g.IsOpen);
            Assert.IsFalse(g.Tick(0.1f));
        }

        [Test]
        public void Homing_BendsTowardTarget_ButCapped()
        {
            var fwd = Vector3.forward;
            var to = new Vector3(5f, 0f, 0f);   // 90 degrees right, 5 m
            var d = HomingLunge.Direction(fwd, to, 6f, 100f, 0.5f);   // 50 degrees allowed
            Assert.AreEqual(50f, Vector3.Angle(fwd, d), 0.5f);
            var full = HomingLunge.Direction(fwd, to, 6f, 1000f, 0.5f);
            Assert.AreEqual(0f, Vector3.Angle(to, full), 0.5f);
        }

        [Test]
        public void Homing_IgnoresTargetOutOfRangeOrDisabled()
        {
            var to = new Vector3(8f, 0f, 0f);
            Assert.AreEqual(Vector3.forward, HomingLunge.Direction(Vector3.forward, to, 6f, 360f, 0.5f));
            Assert.AreEqual(Vector3.forward, HomingLunge.Direction(Vector3.forward, new Vector3(2f, 0, 0), 0f, 360f, 0.5f));
            Assert.AreEqual(Vector3.forward, HomingLunge.Direction(Vector3.forward, new Vector3(2f, 0, 0), 6f, 0f, 0.5f));
        }

        [Test]
        public void HomingSpeed_ZeroWhenAlreadyAdjacent()
        {
            Assert.AreEqual(0f, HomingLunge.Speed(2f, 0.5f, 0.8f));
            Assert.AreEqual(2f, HomingLunge.Speed(2f, 2f, 0.8f), 1e-4f);
        }

        [TestCaseSource(nameof(Steps))]
        public void FlashTimer_LastsConfiguredTime(float dt)
        {
            var f = new FlashTimer();
            f.Trigger(0.08f);
            float t = 0f;
            while (f.Tick(dt)) t += dt;
            Assert.AreEqual(0.08f, t + dt, dt + 1e-4f);
            Assert.IsFalse(f.IsActive);
        }

        [Test]
        public void FlashTimer_ZeroDurationNeverActivates()
        {
            var f = new FlashTimer();
            f.Trigger(0f);
            Assert.IsFalse(f.IsActive);
            Assert.IsFalse(f.Tick(0.016f));
        }
    }
}
