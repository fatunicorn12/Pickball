using NUnit.Framework;
using Pickleball.Sandbox.Magic;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// Pins the mana bar to Terraria 1.4's <c>UpdateManaRegen</c> with the 3 s
    /// empty-bar delay the user asked for: the delay's two ends and what
    /// stretches it, the "starts slow, then accelerates" curve, what standing
    /// still buys, and that none of it depends on frame rate.
    ///
    /// The refill times below are exact tick counts, computed by running the
    /// integer algorithm by hand (and in a reference script): the feel lives in
    /// those integer steps, so an "about right" bound would let a broken
    /// truncation through. They use the shipped defaults on purpose — the
    /// tooltips quote them — and
    /// <see cref="Defaults_AreTerrariasWithAThreeSecondEmptyDelay"/> fails first
    /// if a retune moves them, so the retune has to update the quoted numbers.
    /// </summary>
    public class ManaPoolTests
    {
        const float OneTick = 1f / ManaPool.TicksPerSecond;
        const float TickTolerance = 1f / ManaPool.TicksPerSecond + 1e-4f;

        ManaConfig _config;

        [SetUp]
        public void SetUp() => _config = ScriptableObject.CreateInstance<ManaConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_config);

        ManaPool NewPool() => new ManaPool(_config);

        /// <summary>Run exactly <paramref name="ticks"/> fixed ticks. Each
        /// <c>Tick(1/60 s)</c> call runs exactly one: the accumulator only ever
        /// gains the hair by which the float 1/60 exceeds the true value.</summary>
        static bool Step(ManaPool pool, int ticks, bool still)
        {
            bool filled = false;
            for (int i = 0; i < ticks; i++)
                if (pool.Tick(OneTick, still)) filled = true;
            return filled;
        }

        /// <summary>Ticks until the delay is over. Fails if mana came back first.</summary>
        static int TicksUntilRegen(ManaPool pool, bool still)
        {
            int start = pool.Current, ticks = 0;
            while (pool.IsDelayed && ticks < 10_000)
            {
                pool.Tick(OneTick, still);
                ticks++;
                if (pool.IsDelayed)
                    Assert.That(pool.Current, Is.EqualTo(start),
                                $"Mana came back {ticks} ticks into the delay.");
            }
            return ticks;
        }

        /// <summary>Ticks until the bar is full.</summary>
        static int TicksUntilFull(ManaPool pool, bool still)
        {
            int ticks = 0;
            while (!pool.IsFull && ticks < 100_000)
            {
                pool.Tick(OneTick, still);
                ticks++;
            }
            return ticks;
        }

        /// <summary>A pool holding <paramref name="mana"/> with its delay just
        /// over, so the rate it reports is the regen rate at that fill.</summary>
        ManaPool PoolAt(int mana)
        {
            ManaPool pool = NewPool();
            Assert.That(pool.TrySpend(pool.Max - mana), Is.True);
            TicksUntilRegen(pool, still: false);
            Assert.That(pool.Current, Is.EqualTo(mana),
                        "The tick that ends the delay banks less than one point here.");
            return pool;
        }

        static void SetInt(Object target, string fieldName, int value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            Assert.That(property, Is.Not.Null,
                        $"{target.GetType().Name} has no serialized field '{fieldName}' — " +
                        "it was probably renamed. Update the test's field name.");
            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------ defaults

        [Test]
        public void Defaults_AreTerrariasWithAThreeSecondEmptyDelay()
        {
            Assert.That(_config.MaxMana, Is.EqualTo(100));
            Assert.That(_config.RegenDelayAtEmpty, Is.EqualTo(3f).Within(1e-6f),
                        "The user's 3 s cooldown from an empty bar (Terraria: 3.325 s).");
            Assert.That(_config.RegenDelayAtFull, Is.EqualTo(0.525f).Within(1e-6f),
                        "Terraria's 0.7 × 45 frames.");
            Assert.That(_config.StillDelaySpeedup, Is.EqualTo(2f).Within(1e-6f));
            Assert.That(_config.BaseRegenShareOfMax, Is.EqualTo(1f / 3f).Within(1e-6f));
            Assert.That(_config.StillRegenShareOfMax, Is.EqualTo(1f / 3f).Within(1e-6f));
            Assert.That(_config.FlatRegen, Is.EqualTo(1));
            Assert.That(_config.RegenAtEmptyFraction, Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(_config.RegenMultiplier, Is.EqualTo(1.15f).Within(1e-6f));
            Assert.That(_config.StandingStillSpeed, Is.EqualTo(0.15f).Within(1e-6f));
        }

        // --------------------------------------------------------------- start

        [Test]
        public void NewPool_StartsFull_WithNoDelay()
        {
            ManaPool pool = NewPool();

            Assert.That(pool.Current, Is.EqualTo(100));
            Assert.That(pool.Max, Is.EqualTo(100));
            Assert.That(pool.Normalised, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(pool.IsFull, Is.True);
            Assert.That(pool.IsDelayed, Is.False);
            Assert.That(pool.DelayRemaining, Is.Zero);
            Assert.That(pool.DelayTotal, Is.Zero);
            Assert.That(pool.RegenPerSecond(false), Is.Zero, "A full bar gains nothing.");
        }

        [Test]
        public void Ticking_AFullBar_ChangesNothing_AndNeverReportsFull()
        {
            ManaPool pool = NewPool();

            bool filled = Step(pool, 600, still: true);

            Assert.That(pool.Current, Is.EqualTo(100));
            Assert.That(filled, Is.False, "Already full is not reaching full.");
        }

        // --------------------------------------------------------------- spend

        [Test]
        public void TrySpend_DeductsTheCost_AndStartsTheDelay()
        {
            ManaPool pool = NewPool();

            Assert.That(pool.TrySpend(15), Is.True);

            Assert.That(pool.Current, Is.EqualTo(85));
            Assert.That(pool.IsDelayed, Is.True);
            Assert.That(pool.DelayTotal, Is.EqualTo(pool.DelayRemaining));
            Assert.That(pool.DelayRemaining,
                        Is.EqualTo(_config.RegenDelayFor(0.85f)).Within(TickTolerance));
            Assert.That(pool.RegenPerSecond(false), Is.Zero, "Nothing is gained while delayed.");
        }

        [Test]
        public void TrySpend_CanEmptyTheBarExactly()
        {
            ManaPool pool = NewPool();

            Assert.That(pool.CanAfford(100), Is.True);
            Assert.That(pool.TrySpend(100), Is.True);
            Assert.That(pool.Current, Is.Zero);
            Assert.That(pool.Normalised, Is.Zero);
        }

        [Test]
        public void TrySpend_ThatCannotBeAfforded_ChangesNothing_NotEvenTheRunningDelay()
        {
            ManaPool pool = NewPool();
            pool.TrySpend(60);                // 40 left, a long delay running.
            Step(pool, 10, still: false);
            int mana = pool.Current;
            float remaining = pool.DelayRemaining, total = pool.DelayTotal;
            Assert.That(pool.IsDelayed, Is.True);

            Assert.That(pool.CanAfford(50), Is.False);
            Assert.That(pool.TrySpend(50), Is.False);

            Assert.That(pool.Current, Is.EqualTo(mana));
            Assert.That(pool.DelayRemaining, Is.EqualTo(remaining),
                        "A failed cast must not restart the wait — mashing an " +
                        "unaffordable spell would otherwise lock regen out forever.");
            Assert.That(pool.DelayTotal, Is.EqualTo(total));
        }

        [Test]
        public void TrySpend_ZeroOrNegative_IsFree_AndStartsNoDelay()
        {
            ManaPool pool = NewPool();

            Assert.That(pool.TrySpend(0), Is.True);
            Assert.That(pool.TrySpend(-5), Is.True);

            Assert.That(pool.Current, Is.EqualTo(100), "A negative cost must not mint mana.");
            Assert.That(pool.IsDelayed, Is.False);
        }

        // --------------------------------------------------------------- delay

        [Test]
        public void RegenDelayFor_RunsLinearlyFromTheShortEndAtFullToThreeSecondsAtEmpty()
        {
            Assert.That(_config.RegenDelayFor(1f), Is.EqualTo(0.525f).Within(1e-5f));
            Assert.That(_config.RegenDelayFor(0f), Is.EqualTo(3f).Within(1e-5f));
            Assert.That(_config.RegenDelayFor(0.5f), Is.EqualTo(1.7625f).Within(1e-5f));
        }

        [Test]
        public void Delay_AfterEmptyingTheBar_IsThreeSeconds_AndNoManaReturnsDuringIt()
        {
            // Spent from FULL: the delay is sized by the fill after the spend
            // (empty → 3 s), not before it (full → 0.525 s).
            ManaPool pool = NewPool();
            pool.TrySpend(100);

            Assert.That(pool.DelayRemaining, Is.EqualTo(3f).Within(TickTolerance));

            int ticks = TicksUntilRegen(pool, still: false);

            Assert.That(ticks / (float)ManaPool.TicksPerSecond, Is.EqualTo(3f).Within(TickTolerance));
            Assert.That(ticks, Is.EqualTo(180), "3 s is exactly 180 ticks.");
        }

        [Test]
        public void Delay_AfterASmallSpendFromFull_IsTheShortEndPlusItsShareOfTheRest()
        {
            ManaPool pool = NewPool();
            pool.TrySpend(1);

            // 0.525 s + 1% of the 2.475 s span = 0.54975 s, rounded up to 33 ticks.
            float expected = 0.525f + (3f - 0.525f) * 0.01f;
            Assert.That(pool.DelayRemaining, Is.EqualTo(expected).Within(TickTolerance));
            Assert.That(pool.DelayRemaining, Is.GreaterThanOrEqualTo(expected),
                        "Rounded up to whole ticks: never shorter than configured.");
            Assert.That(TicksUntilRegen(pool, still: false), Is.EqualTo(33));
        }

        [Test]
        public void Delay_ForTheDefaultFifteenManaSpell_IsAboutNineTenthsOfASecond()
        {
            ManaPool pool = NewPool();
            pool.TrySpend(15);

            Assert.That(TicksUntilRegen(pool, still: false), Is.EqualTo(54),
                        "0.525 + 2.475 × 0.15 = 0.896 s → 54 ticks (0.9 s).");
        }

        [Test]
        public void StandingStill_HalvesTheDelayCountdown()
        {
            ManaPool moving = NewPool(), standing = NewPool();
            moving.TrySpend(100);
            standing.TrySpend(100);

            int movingTicks = TicksUntilRegen(moving, still: false);
            int stillTicks = TicksUntilRegen(standing, still: true);

            Assert.That(stillTicks, Is.EqualTo(movingTicks / 2).Within(1));
            Assert.That(stillTicks, Is.EqualTo(90), "An empty bar's 3 s delay ends after 1.5 s still.");
        }

        // ---------------------------------------------------------------- rate

        [TestCase(0)]
        [TestCase(50)]
        [TestCase(99)]
        public void StandingStill_RoughlyDoublesTheRegenRate(int mana)
        {
            ManaPool pool = PoolAt(mana);

            float ratio = pool.RegenPerSecond(true) / pool.RegenPerSecond(false);

            Assert.That(ratio, Is.InRange(1.8f, 2.2f));
        }

        [Test]
        public void Regen_AtEmpty_IsAboutAFifthOfTheRateNearFull()
        {
            // The "starts slow" half of the curve. Not exactly 0.2: the rates are
            // truncated to whole counts per tick (7 and 38 on the move).
            ManaPool empty = PoolAt(0), nearlyFull = PoolAt(99);

            Assert.That(empty.RegenPerSecond(false) / nearlyFull.RegenPerSecond(false),
                        Is.EqualTo(0.2f).Within(0.03f));
            Assert.That(empty.RegenPerSecond(true) / nearlyFull.RegenPerSecond(true),
                        Is.EqualTo(0.2f).Within(0.03f));
            Assert.That(empty.RegenPerSecond(false), Is.EqualTo(3.5f).Within(1e-4f));
            Assert.That(nearlyFull.RegenPerSecond(false), Is.EqualTo(19f).Within(1e-4f));
        }

        [Test]
        public void Refill_StartsSlowAndAccelerates()
        {
            // The "then accelerates" half: the first ten points of a refill take
            // over three times as long as the last ten (139 vs 32 ticks moving).
            ManaPool pool = NewPool();
            pool.TrySpend(100);
            TicksUntilRegen(pool, still: false);

            int ticks = 0, reachedTen = -1, reachedNinety = -1;
            while (!pool.IsFull && ticks < 100_000)
            {
                pool.Tick(OneTick, false);
                ticks++;
                if (reachedTen < 0 && pool.Current >= 10) reachedTen = ticks;
                if (reachedNinety < 0 && pool.Current >= 90) reachedNinety = ticks;
            }
            int firstTen = reachedTen, lastTen = ticks - reachedNinety;

            Assert.That(firstTen, Is.GreaterThan(3 * lastTen),
                        $"First ten points took {firstTen} ticks, the last ten {lastTen}.");
        }

        [Test]
        public void RegenPerSecond_AgreesWithTheGainActuallySimulated()
        {
            ManaPool pool = PoolAt(50);
            float rateAtStart = pool.RegenPerSecond(false);
            int start = pool.Current;

            Step(pool, ManaPool.TicksPerSecond, still: false);   // one second
            int gained = pool.Current - start;
            float rateAtEnd = pool.RegenPerSecond(false);

            // The rate rises with every point, so a second's gain sits between the
            // rate read at either end (±1 for the banked fraction of a point).
            Assert.That(gained, Is.InRange(Mathf.FloorToInt(rateAtStart) - 1,
                                           Mathf.CeilToInt(rateAtEnd) + 1));
            Assert.That(rateAtEnd, Is.GreaterThan(rateAtStart));
        }

        // -------------------------------------------------------- refill times

        [Test]
        public void EmptyToFull_OnTheMove_IsThreeSecondsOfDelayThenAbout10Point7OfRegen()
        {
            ManaPool pool = NewPool();
            pool.TrySpend(100);

            int ticks = TicksUntilFull(pool, still: false);

            // Worked from the integer algorithm: 180 ticks of delay, then 644 of
            // regen at 7 → 39 counts per tick — full 13.733 s after the spend.
            Assert.That(ticks, Is.EqualTo(824));
            float regenSeconds = (ticks - 180) / (float)ManaPool.TicksPerSecond;
            Assert.That(regenSeconds, Is.InRange(10.3f, 10.8f),
                        "The contract's window for a refill on the move.");
        }

        [Test]
        public void EmptyToFull_StandingStill_IsHalfTheDelayThenAbout5Point4OfRegen()
        {
            ManaPool pool = NewPool();
            pool.TrySpend(100);

            int ticks = TicksUntilFull(pool, still: true);

            // 90 ticks of delay (counting down 2 a tick), then 322 of regen at
            // 15 → 77 counts per tick — full 6.867 s after the spend.
            Assert.That(ticks, Is.EqualTo(412));
            float regenSeconds = (ticks - 90) / (float)ManaPool.TicksPerSecond;
            Assert.That(regenSeconds, Is.InRange(5.0f, 5.8f));
        }

        // ------------------------------------------------------- reaching full

        [Test]
        public void Tick_ReportsReachingFullExactlyOncePerRefill()
        {
            ManaPool pool = NewPool();
            int reports = 0;

            for (int refill = 0; refill < 2; refill++)
            {
                pool.TrySpend(30);
                for (int i = 0; i < 1200; i++)
                    if (pool.Tick(OneTick, false)) reports++;
                Assert.That(pool.IsFull, Is.True);
            }

            Assert.That(reports, Is.EqualTo(2));
        }

        [Test]
        public void Refill_FillsAndClearsTheDelay_WithoutReportingFull()
        {
            ManaPool pool = NewPool();
            pool.TrySpend(70);

            pool.Refill();

            Assert.That(pool.Current, Is.EqualTo(pool.Max));
            Assert.That(pool.IsDelayed, Is.False);
            Assert.That(pool.DelayTotal, Is.Zero);
            Assert.That(Step(pool, 120, still: false), Is.False,
                        "Refill is not a regen refill; it does not chime.");
        }

        // ---------------------------------------------------------- frame rate

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void Regen_IsIdenticalAtAnyFrameRate(int fps)
        {
            // The reference runs exactly one tick per call; the pool under test
            // runs whole frames at fps, 0–2 ticks each. Standing still and moving
            // alternate every second so both branches and the delay are covered.
            ManaPool reference = NewPool(), pool = NewPool();
            reference.TrySpend(100);
            pool.TrySpend(100);
            float dt = 1f / fps;

            for (int half = 1; half <= 40; half++)             // 20 s in 0.5 s steps
            {
                bool still = half % 4 < 2;
                for (int f = 0; f < fps / 2; f++) pool.Tick(dt, still);
                Step(reference, ManaPool.TicksPerSecond / 2, still);

                Assert.That(pool.Current, Is.EqualTo(reference.Current),
                            $"Diverged at {half * 0.5f:0.0} s.");
                Assert.That(pool.IsDelayed, Is.EqualTo(reference.IsDelayed));
            }
            Assert.That(pool.IsFull, Is.True);
        }

        [Test]
        public void Tick_IgnoresNonPositiveAndNaNTime()
        {
            ManaPool pool = NewPool();
            pool.TrySpend(100);
            float remaining = pool.DelayRemaining;

            pool.Tick(0f, false);
            pool.Tick(-1f, false);
            pool.Tick(float.NaN, false);

            Assert.That(pool.DelayRemaining, Is.EqualTo(remaining));
        }

        // ---------------------------------------------------------- live tuning

        [Test]
        public void LoweringMaxManaLive_ClampsTheBarOnTheNextTick()
        {
            ManaPool pool = NewPool();

            SetInt(_config, "maxMana", 40);
            pool.Tick(OneTick, false);

            Assert.That(pool.Max, Is.EqualTo(40));
            Assert.That(pool.Current, Is.EqualTo(40));
            Assert.That(pool.IsFull, Is.True);
        }
    }
}
