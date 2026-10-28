using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// The Fireball's burn: the tick clock (<see cref="BurnTicker"/>) deals the
    /// same number of ticks at any frame rate, the first one only after an
    /// interval, never past the end; re-igniting refreshes and never stacks;
    /// and <see cref="BurnStatus"/> turns ticks into damage on a real
    /// <see cref="Health"/> and keeps one burn per target.
    /// </summary>
    public class BurnTests : MagicTestFixture
    {
        const float Damage = 2.5f, Interval = 0.5f, Duration = 3f;

        static int Run(BurnTicker ticker, float seconds, float dt)
        {
            int ticks = 0;
            for (float t = 0f; t < seconds - 1e-6f; t += dt) ticks += ticker.Tick(dt);
            return ticks;
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        [TestCase(0.25f)]
        public void FullBurn_DealsDurationOverIntervalTicks(float dt)
        {
            var ticker = new BurnTicker();
            ticker.Ignite(Damage, Interval, Duration);
            Assert.That(Run(ticker, Duration + 1f, dt), Is.EqualTo(6));
            Assert.That(ticker.IsBurning, Is.False);
        }

        [Test]
        public void FirstTick_OnlyAfterAnInterval()
        {
            var ticker = new BurnTicker();
            ticker.Ignite(Damage, Interval, Duration);
            Assert.That(ticker.Tick(0.49f), Is.EqualTo(0));
            Assert.That(ticker.Tick(0.02f), Is.EqualTo(1));
        }

        [Test]
        public void LongFrame_OwesSeveralTicks_ButNotPastTheEnd()
        {
            var ticker = new BurnTicker();
            ticker.Ignite(Damage, Interval, Duration);
            Assert.That(ticker.Tick(1.6f), Is.EqualTo(3));
            Assert.That(ticker.Tick(100f), Is.EqualTo(3), "only the time that was left counts");
        }

        [Test]
        public void Reignite_RefreshesTheTime_NeverStacks()
        {
            var ticker = new BurnTicker();
            ticker.Ignite(Damage, Interval, Duration);
            int ticks = ticker.Tick(1f);                 // 2 ticks, 2 s left
            ticker.Ignite(Damage, Interval, Duration);   // back to 3 s left, same phase
            ticks += Run(ticker, 10f, 1f / 60f);
            Assert.That(ticks, Is.EqualTo(8), "1 s burnt + a fresh 3 s, one tick per 0.5 s");
        }

        [Test]
        public void Reignite_KeepsTheStrongerDamage_AndTheLongerTime()
        {
            var ticker = new BurnTicker();
            ticker.Ignite(4f, Interval, 5f);
            ticker.Ignite(2f, Interval, 1f);
            Assert.That(ticker.DamagePerTick, Is.EqualTo(4f));
            Assert.That(ticker.Remaining, Is.EqualTo(5f));
        }

        [Test]
        public void NonPositiveValues_LightNothing()
        {
            var ticker = new BurnTicker();
            ticker.Ignite(0f, Interval, Duration);
            ticker.Ignite(Damage, 0f, Duration);
            ticker.Ignite(Damage, Interval, 0f);
            Assert.That(ticker.IsBurning, Is.False);
            Assert.That(ticker.Tick(5f), Is.EqualTo(0));
        }

        [Test]
        public void Extinguish_StopsIt()
        {
            var ticker = new BurnTicker();
            ticker.Ignite(Damage, Interval, Duration);
            ticker.Extinguish();
            Assert.That(ticker.IsBurning, Is.False);
            Assert.That(ticker.Tick(1f), Is.EqualTo(0));
        }

        // ------------------------------------------------------- BurnStatus

        Health NewVictim()
        {
            var health = NewComponent<Health>();
            health.gameObject.AddComponent<CapsuleCollider>();
            health.ResetToFull();   // Awake does not run in EditMode
            return health;
        }

        BurnStatus NewBurnPrefab()
        {
            // A plain template object stands in for the prefab: Ignite clones it.
            return NewComponent<BurnStatus>();
        }

        [Test]
        public void Ignite_TicksDamageIntoHealth()
        {
            var victim = NewVictim();
            float before = victim.Current;
            var burn = BurnStatus.Ignite(NewBurnPrefab(), victim, Damage, Interval, Duration);
            Assert.That(burn, Is.Not.Null);
            Assert.That(burn.transform.parent, Is.SameAs(victim.transform), "the burn follows its victim");

            int ticked = 0;
            burn.Ticked += (_, _) => ticked++;
            burn.Tick(1.01f);   // two ticks; stop short of the end (Destroy is Play-mode only)
            Assert.That(ticked, Is.EqualTo(2));
            Assert.That(victim.Current, Is.EqualTo(before - 2 * Damage).Within(1e-4f));
        }

        [Test]
        public void IgniteTwice_RefreshesTheSameBurn()
        {
            var victim = NewVictim();
            var prefab = NewBurnPrefab();
            var first = BurnStatus.Ignite(prefab, victim, Damage, Interval, Duration);
            first.Tick(1f);
            var second = BurnStatus.Ignite(prefab, victim, Damage, Interval, Duration);
            Assert.That(second, Is.SameAs(first));
            Assert.That(victim.GetComponentsInChildren<BurnStatus>().Length, Is.EqualTo(1));
            Assert.That(second.Remaining, Is.EqualTo(Duration).Within(1e-5f));
        }

        [Test]
        public void Ignite_WithoutPrefabOrTarget_LightsNothing()
        {
            var victim = NewVictim();
            Assert.That(BurnStatus.Ignite(null, victim, Damage, Interval, Duration), Is.Null);
            Assert.That(BurnStatus.Ignite(NewBurnPrefab(), null, Damage, Interval, Duration), Is.Null);
            Assert.That(BurnStatus.Ignite(NewBurnPrefab(), victim, 0f, Interval, Duration), Is.Null);
        }
    }
}
