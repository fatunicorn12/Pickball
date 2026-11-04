using NUnit.Framework;
using Pickleball.Sandbox.Juggle;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// Kingdom Hearts II's revenge value: damage in one stretch fills a tally, and
    /// past the limit the enemy breaks out and shrugs off reactions for a moment.
    /// </summary>
    public class RevengeGaugeTests
    {
        static RevengeGauge Gauge(float limit = 100f, float decay = 30f, float delay = 1f, float armor = 2f)
            => new RevengeGauge(new RevengeSettings(limit, decay, delay, armor));

        [Test]
        public void HitsUnderTheLimit_DoNotBreakOut()
        {
            var g = Gauge();
            Assert.That(g.Add(60f), Is.False);
            Assert.That(g.Add(39f), Is.False);
            Assert.That(g.Value, Is.EqualTo(99f).Within(1e-4f));
            Assert.That(g.IsArmored, Is.False);
        }

        [Test]
        public void TheHitThatReachesTheLimit_BreaksOut_OnceOnly()
        {
            var g = Gauge();
            g.Add(60f);
            Assert.That(g.Add(40f), Is.True);
            Assert.That(g.IsArmored, Is.True);
            Assert.That(g.ArmorLeft, Is.EqualTo(2f));
            Assert.That(g.Add(500f), Is.False, "armored, it doesn't break out again");
        }

        [Test]
        public void WhileArmored_NothingIsTallied_SoItStartsAfreshAfterwards()
        {
            var g = Gauge();
            g.Add(100f);
            g.Add(70f);
            Assert.That(g.Value, Is.Zero);

            g.Tick(2.01f);
            Assert.That(g.IsArmored, Is.False);
            Assert.That(g.Add(99f), Is.False, "a full limit again before the next break-out");
            Assert.That(g.Add(1f), Is.True);
        }

        [Test]
        public void ALimitOfZero_NeverBreaksOut()
        {
            var g = Gauge(limit: 0f);
            Assert.That(g.Add(10000f), Is.False);
            Assert.That(g.IsArmored, Is.False);
        }

        [Test]
        public void ArmorOfZero_BreaksOut_WithoutEverArmoring()
        {
            var g = Gauge(armor: 0f);
            Assert.That(g.Add(100f), Is.True);
            Assert.That(g.IsArmored, Is.False);
        }

        [TestCase(1f / 30)]
        [TestCase(1f / 60)]
        [TestCase(1f / 144)]
        public void TheTally_HoldsThroughTheDelay_ThenShedsAtItsRate(float dt)
        {
            var g = Gauge(decay: 30f, delay: 1f);
            g.Add(90f);

            for (float t = dt; t <= 0.9f; t += dt) g.Tick(dt);
            Assert.That(g.Value, Is.EqualTo(90f), "still inside the delay");

            // One simulated second from the same frame grid, to 2.0 s after the hit.
            var fresh = Gauge(decay: 30f, delay: 1f);
            fresh.Add(90f);
            int ticks = UnityEngine.Mathf.RoundToInt(2f / dt);
            for (int i = 0; i < ticks; i++) fresh.Tick(dt);
            Assert.That(fresh.Value, Is.EqualTo(60f).Within(0.5f), "the same shed at any frame rate");
        }

        [Test]
        public void AHit_RestartsTheDelay()
        {
            var g = Gauge(decay: 30f, delay: 1f);
            g.Add(50f);
            g.Tick(0.9f);
            g.Add(10f);
            g.Tick(0.9f);
            Assert.That(g.Value, Is.EqualTo(60f), "a steady combo keeps filling it");
        }

        [Test]
        public void TheTally_NeverGoesNegative()
        {
            var g = Gauge(decay: 1000f, delay: 0f);
            g.Add(10f);
            g.Tick(1f);
            Assert.That(g.Value, Is.Zero);
        }

        [Test]
        public void HeavierClasses_BreakOutSooner()
        {
            float light = RevengeSettings.For(JuggleWeight.Light).Limit;
            float medium = RevengeSettings.For(JuggleWeight.Medium).Limit;
            float heavy = RevengeSettings.For(JuggleWeight.Heavy).Limit;
            Assert.That(light, Is.GreaterThan(medium));
            Assert.That(medium, Is.GreaterThan(heavy));
        }

        [Test]
        public void Reset_EmptiesAndUnarmors()
        {
            var g = Gauge();
            g.Add(100f);
            g.Reset();
            Assert.That(g.IsArmored, Is.False);
            Assert.That(g.Value, Is.Zero);
        }
    }
}
