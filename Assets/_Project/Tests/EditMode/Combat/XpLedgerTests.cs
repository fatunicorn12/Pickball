using NUnit.Framework;
using Pickleball.Sandbox.Combat.Progression;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// QA CB5: XP used to be paid for hits on targets that were already
    /// defeated (including +250 for someone else's killing blow), and a
    /// refilling training dummy could be farmed for ~400 per KO. These pin the
    /// replacement rules: pay only applied damage, pay the defeat bonus once for
    /// the blow that took the target to zero, and let each target pay its worth
    /// once per session.
    /// </summary>
    public class XpLedgerTests
    {
        const float PerHp = 1.5f;
        const int PerDefeat = 250;
        const float Max = 100f;

        static int Hit(XpLedger ledger, object target, float before, float after)
            => ledger.Credit(target, before, after, Max, PerHp, PerDefeat);

        [Test]
        public void Damage_PaysAppliedDamage_WithFractionalCarry()
        {
            var ledger = new XpLedger();
            var target = new object();
            Assert.That(Hit(ledger, target, 100f, 87f), Is.EqualTo(19));    // 19.5 -> 19, carry 0.5
            Assert.That(Hit(ledger, target, 87f, 74f), Is.EqualTo(20));     // 19.5 + 0.5
        }

        [Test]
        public void KillingBlow_PaysTheDefeatBonusOnce()
        {
            var ledger = new XpLedger();
            var target = new object();
            Assert.That(Hit(ledger, target, 10f, 0f), Is.EqualTo(15 + PerDefeat));
            Assert.That(Hit(ledger, target, 0f, 0f), Is.EqualTo(0));
        }

        [Test]
        public void DeadButUncredited_PaysNothing()
        {
            var ledger = new XpLedger();
            Assert.That(Hit(ledger, new object(), 0f, 0f), Is.EqualTo(0),
                        "hitting a corpse someone else downed is worth nothing");
        }

        [Test]
        public void RefilledTarget_PaysNothingMore()
        {
            var ledger = new XpLedger();
            var dummy = new object();
            int first = Hit(ledger, dummy, 100f, 0f);                        // one big KO
            Assert.That(first, Is.EqualTo(150 + PerDefeat));

            // Health snaps back to full; another KO is worth nothing now.
            Assert.That(Hit(ledger, dummy, 100f, 0f), Is.EqualTo(0));
        }

        [Test]
        public void ChipAndRefill_CannotFarmDamageXp()
        {
            var ledger = new XpLedger();
            var dummy = new object();
            int total = 0;
            for (int i = 0; i < 10; i++)                                     // chip 40, refill, repeat
                total += Hit(ledger, dummy, 100f, 60f);
            Assert.That(total, Is.EqualTo(150), "damage XP is capped at the target's max health");
            Assert.That(ledger.RemainingHp(dummy, Max), Is.EqualTo(0f));
        }

        [Test]
        public void Overkill_PaysOnlyWhatWasThere()
        {
            var ledger = new XpLedger();
            Assert.That(Hit(ledger, new object(), 4f, 0f), Is.EqualTo(6 + PerDefeat));
        }

        [Test]
        public void Targets_HaveSeparateBudgets()
        {
            var ledger = new XpLedger();
            var a = new object();
            var b = new object();
            Hit(ledger, a, 100f, 0f);
            Assert.That(Hit(ledger, b, 100f, 0f), Is.EqualTo(150 + PerDefeat));
        }

        [Test]
        public void NullTargetOrNoMaxHealth_PaysNothing()
        {
            var ledger = new XpLedger();
            Assert.That(Hit(ledger, null, 100f, 0f), Is.EqualTo(0));
            Assert.That(ledger.Credit(new object(), 10f, 0f, 0f, PerHp, PerDefeat), Is.EqualTo(0));
        }
    }
}
