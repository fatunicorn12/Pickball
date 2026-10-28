using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Tests.Training
{
    public class ComboTrackerTests
    {
        [Test]
        public void Hits_AccumulateCountTotalAndLast()
        {
            var c = new ComboTracker(2.5f);
            c.RegisterHit(12f, 0f);
            c.RegisterHit(14f, 0.5f);
            Assert.AreEqual(2, c.Hits);
            Assert.AreEqual(26f, c.TotalDamage, 1e-4f);
            Assert.AreEqual(14f, c.LastHit, 1e-4f);
        }

        [Test]
        public void Combo_ResetsAfterTimeout_NotBefore()
        {
            var c = new ComboTracker(2.5f);
            c.RegisterHit(10f, 1f);
            Assert.IsFalse(c.Tick(3.4f));
            Assert.AreEqual(1, c.Hits);
            Assert.IsTrue(c.Tick(3.5f));
            Assert.AreEqual(0, c.Hits);
            Assert.AreEqual(0f, c.TotalDamage);
        }

        [Test]
        public void HitInsideTimeout_ExtendsTheCombo()
        {
            var c = new ComboTracker(2f);
            c.RegisterHit(5f, 0f);
            c.RegisterHit(5f, 1.9f);
            Assert.IsFalse(c.Tick(3.8f));
            Assert.AreEqual(2, c.Hits);
            Assert.IsTrue(c.Tick(3.9f));
        }

        [Test]
        public void LateHit_BeforeTick_StartsAFreshCombo()
        {
            var c = new ComboTracker(2f);
            c.RegisterHit(5f, 0f);
            c.RegisterHit(7f, 5f);
            Assert.AreEqual(1, c.Hits);
            Assert.AreEqual(7f, c.TotalDamage, 1e-4f);
        }

        [Test]
        public void ZeroDamage_IsIgnored()
        {
            var c = new ComboTracker(2f);
            c.RegisterHit(0f, 0f);
            Assert.AreEqual(0, c.Hits);
        }

        [Test]
        public void ChainStep_IsOneBased_AndExpires()
        {
            var c = new ComboTracker(2f);
            c.RegisterSwing(2, "Slash", 1f);
            Assert.AreEqual(3, c.ChainStep);
            Assert.AreEqual("Slash", c.StepName);
            Assert.IsTrue(c.Tick(3.1f));
            Assert.AreEqual(0, c.ChainStep);
        }
    }

    public class JumpSamplerTests
    {
        const float G = 9.81f, V0 = 7f;

        /// <summary>Drives a ballistic jump at a fixed step. The "controller"
        /// reports grounded=false only on the tick after it left the floor, and
        /// grounded=true on the tick it touches down, as CharacterController does.</summary>
        static JumpSampler.Result Jump(float dt, out float trueApex)
        {
            var sampler = new JumpSampler(0.2f);
            float t = 0f, y = 0f, vy = 0f, x = 0f;
            trueApex = 0f;

            for (int i = 0; i < 5; i++) { sampler.Sample(new Vector3(x, 0f, 0f), true, t, out _); t += dt; }

            vy = V0;
            JumpSampler.Result result = default;
            bool landed = false;
            for (int i = 0; i < 100000 && !landed; i++)
            {
                vy -= G * dt;
                y += vy * dt;
                x += 5f * dt;
                t += dt;
                bool grounded = y <= 0f;
                if (grounded) y = 0f;
                trueApex = Mathf.Max(trueApex, y);
                landed = sampler.Sample(new Vector3(x, y, 0f), grounded, t, out result);
            }
            Assert.IsTrue(landed, "jump never completed");
            return result;
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Rise_MatchesTheTrueApex_AtAnyFrameRate(float dt)
        {
            var r = Jump(dt, out float apex);
            Assert.AreEqual(apex, r.Rise, 1e-4f);
            // Close to the analytic peak, within one step's worth of fall.
            Assert.AreEqual(V0 * V0 / (2f * G), r.Rise, V0 * dt);
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Rise_IsMeasuredFromTheLastGroundedSample_NotTheFirstAirborneOne(float dt)
        {
            var r = Jump(dt, out float apex);
            // First airborne sample is already V0*dt above the floor; latching there loses that.
            Assert.GreaterOrEqual(r.Rise, apex - 1e-4f);
            Assert.Greater(r.Carry, 0f);
            Assert.AreEqual(2f * V0 / G, r.AirTime, 3f * dt);
        }

        [Test]
        public void ShortStumble_IsNotAJump()
        {
            var s = new JumpSampler(0.2f);
            s.Sample(Vector3.zero, true, 0f, out _);
            s.Sample(new Vector3(0, -0.1f, 0), false, 0.05f, out _);
            Assert.IsFalse(s.Sample(Vector3.zero, true, 0.1f, out _));
        }
    }
}
