using NUnit.Framework;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// Slow stacking and expiry (<see cref="SlowLedger"/>, and the
    /// <see cref="StatusReceiver"/> that wraps it): the strongest slow wins and
    /// nothing multiplies, each source refreshes only its own entry, entries
    /// expire exactly on time, and a stagger is capped.
    /// </summary>
    public class SlowLedgerTests : MagicTestFixture
    {
        readonly object _zoneA = new(), _zoneB = new(), _zoneC = new();

        [Test]
        public void Nothing_IsFullSpeed()
        {
            var ledger = new SlowLedger();
            Assert.That(ledger.MultiplierAt(0f), Is.EqualTo(1f));
            Assert.That(ledger.IsSlowedAt(0f), Is.False);
            Assert.That(ledger.RemainingAt(0f), Is.EqualTo(0f));
        }

        [Test]
        public void OneSlow_AppliesItsMultiplier()
        {
            var ledger = new SlowLedger();
            ledger.Apply(_zoneA, 0.3f, 1f, now: 10f);
            Assert.That(ledger.MultiplierAt(10.5f), Is.EqualTo(0.3f));
            Assert.That(ledger.RemainingAt(10.5f), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void Expires_ExactlyOnTime()
        {
            var ledger = new SlowLedger();
            ledger.Apply(_zoneA, 0.3f, 0.35f, now: 1f);
            Assert.That(ledger.IsSlowedAt(1.349f), Is.True);
            Assert.That(ledger.IsSlowedAt(1.35f), Is.False, "an entry counts up to, not at, its expiry");
            Assert.That(ledger.MultiplierAt(2f), Is.EqualTo(1f));
            Assert.That(ledger.Count, Is.EqualTo(0), "expired entries are pruned");
        }

        [Test]
        public void TwoZones_StrongestWins_NeverMultiplies()
        {
            var ledger = new SlowLedger();
            ledger.Apply(_zoneA, 0.3f, 1f, now: 0f);
            ledger.Apply(_zoneB, 0.3f, 1f, now: 0f);
            Assert.That(ledger.MultiplierAt(0.1f), Is.EqualTo(0.3f), "not 0.09");

            ledger.Apply(_zoneC, 0.6f, 1f, now: 0f);
            Assert.That(ledger.MultiplierAt(0.1f), Is.EqualTo(0.3f), "a weaker slow changes nothing");
        }

        [Test]
        public void StrongerExpires_WeakerTakesOver()
        {
            var ledger = new SlowLedger();
            ledger.Apply(_zoneA, 0.2f, 0.5f, now: 0f);
            ledger.Apply(_zoneB, 0.6f, 2f, now: 0f);
            Assert.That(ledger.MultiplierAt(0.4f), Is.EqualTo(0.2f));
            Assert.That(ledger.MultiplierAt(0.6f), Is.EqualTo(0.6f));
        }

        [Test]
        public void SameSource_ReplacesItsOwnEntry()
        {
            var ledger = new SlowLedger();
            ledger.Apply(_zoneA, 0.2f, 1f, now: 0f);
            ledger.Apply(_zoneA, 0.7f, 1f, now: 0.5f);
            Assert.That(ledger.Count, Is.EqualTo(1));
            Assert.That(ledger.MultiplierAt(0.6f), Is.EqualTo(0.7f), "the source's new value, not the old minimum");
            Assert.That(ledger.IsSlowedAt(1.4f), Is.True, "refreshed from 0.5 s");
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void ZoneReappliedEveryFrame_EndsOneLingerAfterTheLastFrame(float dt)
        {
            const float linger = 0.35f, inside = 1f;
            var ledger = new SlowLedger();
            float last = 0f;
            for (float now = 0f; now < inside; now += dt)
            {
                ledger.Apply(_zoneA, 0.3f, linger, now);
                last = now;
            }
            Assert.That(ledger.IsSlowedAt(last + linger - 0.001f), Is.True);
            Assert.That(ledger.IsSlowedAt(last + linger), Is.False);
        }

        [Test]
        public void MultiplierIsClamped()
        {
            var ledger = new SlowLedger();
            ledger.Apply(_zoneA, -1f, 1f, 0f);
            Assert.That(ledger.MultiplierAt(0f), Is.EqualTo(0f));
            ledger.Apply(_zoneA, 3f, 1f, 0f);
            Assert.That(ledger.MultiplierAt(0f), Is.EqualTo(1f));
        }

        [Test]
        public void ZeroSeconds_RemovesTheSource_NullIsIgnored()
        {
            var ledger = new SlowLedger();
            ledger.Apply(_zoneA, 0.3f, 1f, 0f);
            ledger.Apply(_zoneA, 0.3f, 0f, 0f);
            ledger.Apply(null, 0.1f, 1f, 0f);
            Assert.That(ledger.IsSlowedAt(0f), Is.False);
        }

        [Test]
        public void Overflow_ReplacesTheSoonestToExpire()
        {
            var ledger = new SlowLedger(2);
            ledger.Apply(_zoneA, 0.5f, 5f, 0f);
            ledger.Apply(_zoneB, 0.2f, 1f, 0f);
            ledger.Apply(_zoneC, 0.4f, 3f, 0f);   // replaces B, which ends first
            Assert.That(ledger.MultiplierAt(0.1f), Is.EqualTo(0.4f));
        }

        // ---------------------------------------------------- StatusReceiver

        [Test]
        public void Receiver_StrongestSlowWins()
        {
            var receiver = NewComponent<StatusReceiver>();
            receiver.ApplySlow(_zoneA, 0.3f, 5f);
            receiver.ApplySlow(_zoneB, 0.5f, 5f);
            Assert.That(receiver.IsSlowed, Is.True);
            Assert.That(receiver.SpeedMultiplier, Is.EqualTo(0.3f));
        }

        [Test]
        public void Receiver_RaisesSlowStartedOnce()
        {
            var receiver = NewComponent<StatusReceiver>();
            int started = 0;
            receiver.SlowStarted += _ => started++;
            receiver.ApplySlow(_zoneA, 0.3f, 5f);
            receiver.ApplySlow(_zoneA, 0.3f, 5f);
            receiver.ApplySlow(_zoneB, 0.3f, 5f);
            Assert.That(started, Is.EqualTo(1));
        }

        [Test]
        public void Receiver_StaggerIsCappedAndNeverAdds()
        {
            var receiver = NewComponent<StatusReceiver>();
            SetFloat(receiver, "maxStaggerSeconds", 1.5f);
            float reported = 0f;
            receiver.Staggered += (_, s) => reported = s;

            receiver.Stagger(10f);
            Assert.That(reported, Is.EqualTo(1.5f));
            Assert.That(receiver.IsStaggered, Is.True);
            Assert.That(receiver.StaggerRemaining, Is.LessThanOrEqualTo(1.5f + 1e-4f));

            receiver.Stagger(0.2f);
            Assert.That(receiver.StaggerRemaining, Is.GreaterThan(1f), "a shorter stagger does not cut a longer one");
        }

        [Test]
        public void Receiver_ClearAll_EndsEverything()
        {
            var receiver = NewComponent<StatusReceiver>();
            receiver.ApplySlow(_zoneA, 0.3f, 5f);
            receiver.Stagger(1f);
            int ended = 0;
            receiver.SlowEnded += _ => ended++;
            receiver.ClearAll();
            Assert.That(receiver.IsSlowed, Is.False);
            Assert.That(receiver.IsStaggered, Is.False);
            Assert.That(receiver.SpeedMultiplier, Is.EqualTo(1f));
            Assert.That(ended, Is.EqualTo(1));
        }
    }
}
