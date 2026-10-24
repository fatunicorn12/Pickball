using UnityEngine;
using NUnit.Framework;
using Pickleball.Sandbox;

namespace Pickleball.Tests
{
    /// <summary>
    /// Pins down the regain ("grey bar") contract.
    ///
    /// The fight is tuned on the assumption that a hit is an invitation to press
    /// rather than to retreat, and that assumption is only true if three numbers
    /// hold: how much of a hit is winnable back, how fast landing hits wins it,
    /// and which attacks opt out of the deal entirely. Those are exactly the
    /// numbers a balance pass will move, so they are the ones worth nailing down —
    /// a tuning change should have to explain itself to a failing test rather than
    /// quietly turning the beam recoverable.
    ///
    /// Every test sets the tunables it depends on. None of them assert the shipped
    /// defaults, so retuning the boss is free; changing what the system *means* is
    /// not.
    /// </summary>
    public class HealthTests : BossTestFixture
    {
        const float Tolerance = 1e-4f;

        /// <summary>A Health in a known state. <see cref="Health.ResetToFull"/>
        /// stands in for the <c>Awake</c> that EditMode never runs, so the fixture
        /// does not care whether the editor ticked the object.</summary>
        Health NewHealth(float max = 100f, float regainShare = 0.5f,
                         float regainPerDamageDealt = 1f)
        {
            Health health = NewComponent<Health>();
            SetTunable(health, "maxHealth", max);
            SetTunable(health, "regainShare", regainShare);
            SetTunable(health, "regainPerDamageDealt", regainPerDamageDealt);
            health.ResetToFull();
            return health;
        }

        [Test]
        public void ResetToFull_PutsTheBarAtMaxWithNoGrey()
        {
            Health health = NewHealth(max: 80f);

            Assert.That(health.Max, Is.EqualTo(80f).Within(Tolerance));
            Assert.That(health.Current, Is.EqualTo(80f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(health.IsDead, Is.False);
        }

        // ---------------------------------------------------------------- split

        [Test]
        public void Damage_SplitsTheHitBetweenGreyAndPermanentLossByRegainShare()
        {
            Health health = NewHealth(max: 100f, regainShare: 0.5f);

            health.Damage(40f, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(60f).Within(Tolerance),
                        "The whole hit always leaves the solid bar.");
            Assert.That(health.Recoverable, Is.EqualTo(20f).Within(Tolerance),
                        "Half of a 40 point hit should be winnable back.");
        }

        [Test]
        public void Damage_WithRegainShareOfOne_MakesEveryPointWinnableBack()
        {
            Health health = NewHealth(regainShare: 1f);

            health.Damage(30f, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(70f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(30f).Within(Tolerance));
        }

        [Test]
        public void Damage_WithRegainShareOfZero_TurnsTheSystemOff()
        {
            Health health = NewHealth(regainShare: 0f);

            health.Damage(30f, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(70f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(0f).Within(Tolerance),
                        "regainShare 0 is documented as 'the regain system is off'.");
        }

        [Test]
        public void Damage_BeyondTheBar_OnlyBanksWhatWasActuallyLost()
        {
            // Overkill must not inflate the pool: a 250 point hit on 100 health is
            // still only 100 points of loss, so it is only 100 points of grey.
            Health health = NewHealth(max: 100f, regainShare: 1f);

            health.Damage(250f, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(100f).Within(Tolerance));
        }

        [Test]
        public void Damage_OfZeroOrLess_IsIgnored()
        {
            Health health = NewHealth();
            int changes = 0;
            health.Changed += _ => changes++;

            health.Damage(0f, Vector3.zero);
            health.Damage(-25f, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(changes, Is.Zero, "A no-op hit should not wake the UI.");
        }

        [Test]
        public void Hurt_ReportsTheLossThatLanded_NotTheDamageRequested()
        {
            Health health = NewHealth(max: 100f);
            float reported = -1f;
            var where = new Vector3(3f, 1f, -2f);
            Vector3 reportedAt = Vector3.zero;
            health.Hurt += (amount, at) => { reported = amount; reportedAt = at; };

            health.Damage(250f, where);

            Assert.That(reported, Is.EqualTo(100f).Within(Tolerance),
                        "Floating damage numbers should read 100, not 250.");
            Assert.That(reportedAt, Is.EqualTo(where));
        }

        // ------------------------------------------------------------ invariant

        [Test]
        public void Grey_NeverPushesTheBarPastMax()
        {
            // The invariant the whole readout rests on: Current + Recoverable is
            // the far edge of the grey span, and it may never overhang the track.
            Health health = NewHealth(max: 100f, regainShare: 1f);

            for (int hit = 0; hit < 5; hit++)
            {
                health.Damage(30f, Vector3.zero);

                Assert.That(health.Current + health.Recoverable,
                            Is.LessThanOrEqualTo(health.Max + Tolerance),
                            $"Grey overhung the bar after hit {hit + 1}.");
                Assert.That(health.RecoverableNormalised,
                            Is.LessThanOrEqualTo(1f + Tolerance));
            }
        }

        [Test]
        public void Heal_TrimsGreyThatWouldNowSitPastTheTopOfTheBar()
        {
            Health health = NewHealth(max: 100f, regainShare: 1f);
            health.Damage(40f, Vector3.zero);   // 60 solid, 40 grey.

            health.Heal(20f);                   // 80 solid leaves room for only 20 grey.

            Assert.That(health.Current, Is.EqualTo(80f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(20f).Within(Tolerance));
        }

        // ----------------------------------------------------------- cashing in

        [Test]
        public void CreditDamageDealt_ConvertsGreyBackIntoHealth()
        {
            Health health = NewHealth(max: 100f, regainShare: 1f, regainPerDamageDealt: 1f);
            health.Damage(40f, Vector3.zero);   // 60 solid, 40 grey.

            health.CreditDamageDealt(10f);

            Assert.That(health.Current, Is.EqualTo(70f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(30f).Within(Tolerance),
                        "Health won back must leave the pool, not be minted.");
        }

        [Test]
        public void CreditDamageDealt_ScalesByRegainPerDamageDealt()
        {
            Health health = NewHealth(max: 100f, regainShare: 1f, regainPerDamageDealt: 0.5f);
            health.Damage(40f, Vector3.zero);

            health.CreditDamageDealt(10f);      // Worth 5 points at half rate.

            Assert.That(health.Current, Is.EqualTo(65f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(35f).Within(Tolerance));
        }

        [Test]
        public void CreditDamageDealt_CannotReturnMoreThanThePoolHolds()
        {
            Health health = NewHealth(max: 100f, regainShare: 1f, regainPerDamageDealt: 1f);
            health.Damage(40f, Vector3.zero);   // 40 grey banked.

            health.CreditDamageDealt(9999f);    // A combo far larger than the debt.

            Assert.That(health.Recoverable, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(health.Current, Is.EqualTo(100f).Within(Tolerance),
                        "Cashing in may refill the bar but never overfill it.");
            Assert.That(health.Current, Is.LessThanOrEqualTo(health.Max + Tolerance));
        }

        [Test]
        public void CreditDamageDealt_WithAnEmptyPool_DoesNothing()
        {
            Health health = NewHealth();
            int changes = 0;
            health.Changed += _ => changes++;

            health.CreditDamageDealt(50f);

            Assert.That(health.Current, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(changes, Is.Zero);
        }

        [Test]
        public void CreditDamageDealt_AfterDeath_DoesNothing()
        {
            // Death is final even with a full pool: the grey you were carrying is
            // not a second life, it is a debt you ran out of time to pay.
            Health health = NewHealth(max: 100f, regainShare: 1f);
            health.Damage(100f, Vector3.zero);

            health.CreditDamageDealt(9999f);

            Assert.That(health.Current, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(health.IsDead, Is.True);
        }

        // ------------------------------------------------------- permanent loss

        [Test]
        public void UnrecoverableDamage_BanksNothingAndCannotBeWonBack()
        {
            // The beam's whole design rests on this: no grey means no aggressive
            // recovery line out of standing in it.
            Health health = NewHealth(max: 100f, regainShare: 1f, regainPerDamageDealt: 1f);

            health.Damage(20f, Vector3.zero, recoverable: false);

            Assert.That(health.Current, Is.EqualTo(80f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(0f).Within(Tolerance));

            health.CreditDamageDealt(9999f);

            Assert.That(health.Current, Is.EqualTo(80f).Within(Tolerance),
                        "Permanent loss must survive any amount of pressing.");
        }

        [Test]
        public void UnrecoverableDamage_LeavesGreyAlreadyBankedIntact()
        {
            // Documents the actual behaviour of the clamp on the permanent branch.
            // Because permanent damage only ever lowers Current, the headroom above
            // the bar grows, so the clamp never binds — grey earned before the beam
            // is still yours to win back afterwards.
            Health health = NewHealth(max: 100f, regainShare: 1f, regainPerDamageDealt: 1f);
            health.Damage(20f, Vector3.zero);                       // 80 solid, 20 grey.

            health.Damage(20f, Vector3.zero, recoverable: false);   // 60 solid.

            Assert.That(health.Current, Is.EqualTo(60f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(20f).Within(Tolerance));
            Assert.That(health.Current + health.Recoverable,
                        Is.LessThanOrEqualTo(health.Max + Tolerance));
        }

        // -------------------------------------------------------------- dying

        [Test]
        public void Died_FiresExactlyOnce_HoweverManyTimesTheCorpseIsHit()
        {
            Health health = NewHealth(max: 100f);
            int deaths = 0;
            health.Died += _ => deaths++;

            health.Damage(150f, Vector3.zero);
            health.Damage(150f, Vector3.zero);
            health.Damage(1f, Vector3.zero);
            health.Damage(1f, Vector3.zero, recoverable: false);

            Assert.That(deaths, Is.EqualTo(1),
                        "A death rattle that fires twice plays the whole death " +
                        "sequence twice.");
        }

        [Test]
        public void Died_FiresOnTheHitThatEmptiesTheBar_NotBefore()
        {
            Health health = NewHealth(max: 100f);
            int deaths = 0;
            health.Died += _ => deaths++;

            health.Damage(60f, Vector3.zero);
            Assert.That(deaths, Is.Zero, "Still standing at 40 health.");
            Assert.That(health.IsDead, Is.False);

            health.Damage(40f, Vector3.zero);

            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(health.IsDead, Is.True);
        }

        [Test]
        public void Died_CanFireAgainAfterResetToFull()
        {
            // The arming flag has to clear or a retry gives you an immortal boss.
            Health health = NewHealth(max: 100f);
            int deaths = 0;
            health.Died += _ => deaths++;

            health.Damage(150f, Vector3.zero);
            health.ResetToFull();
            health.Damage(150f, Vector3.zero);

            Assert.That(deaths, Is.EqualTo(2));
        }

        // -------------------------------------------------------------- filter

        [Test]
        public void IncomingDamageFilter_IsAppliedBeforeTheGreySplit()
        {
            // If the filter ran after the split, a halved hit would still bank grey
            // sized for the full one. Grey must be sized from what actually landed.
            Health health = NewHealth(max: 100f, regainShare: 0.5f);
            health.IncomingDamageFilter = (amount, _) => amount * 0.5f;

            health.Damage(40f, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(80f).Within(Tolerance),
                        "20 points landed, not 40.");
            Assert.That(health.Recoverable, Is.EqualTo(10f).Within(Tolerance),
                        "Grey is half of the 20 that landed, not half of the 40 sent.");
        }

        [Test]
        public void IncomingDamageFilter_SeesTheRawHitAsTheWeaponSentIt()
        {
            Health health = NewHealth();
            var where = new Vector3(1f, 2f, 3f);
            float sawAmount = -1f;
            Vector3 sawPoint = Vector3.zero;
            health.IncomingDamageFilter = (amount, at) =>
            {
                sawAmount = amount;
                sawPoint = at;
                return amount;
            };

            health.Damage(37f, where);

            Assert.That(sawAmount, Is.EqualTo(37f).Within(Tolerance));
            Assert.That(sawPoint, Is.EqualTo(where));
        }

        [Test]
        public void IncomingDamageFilter_CanAmplify()
        {
            // Newton's close-range weak point is expressed this way, so scaling up
            // has to work as cleanly as scaling down.
            Health health = NewHealth(max: 100f, regainShare: 0.5f);
            health.IncomingDamageFilter = (amount, _) => amount * 2f;

            health.Damage(10f, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(80f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(10f).Within(Tolerance));
        }

        [Test]
        public void IncomingDamageFilter_CanCancelAHitEntirely()
        {
            Health health = NewHealth();
            int changes = 0, hurts = 0;
            health.Changed += _ => changes++;
            health.Hurt += (amount, at) => hurts++;
            health.IncomingDamageFilter = (amount, at) => 0f;

            health.Damage(50f, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(hurts, Is.Zero, "A fully absorbed hit is not a hit.");
            Assert.That(changes, Is.Zero);
        }

        [Test]
        public void IncomingDamageFilter_ReturningNegative_IsTreatedAsNoHit()
        {
            Health health = NewHealth();
            health.IncomingDamageFilter = (amount, at) => -25f;

            health.Damage(50f, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(100f).Within(Tolerance),
                        "A negative filter result must not become a heal.");
        }

        [Test]
        public void IncomingDamageFilter_AlsoGovernsUnrecoverableDamage()
        {
            Health health = NewHealth(max: 100f, regainShare: 1f);
            health.IncomingDamageFilter = (amount, _) => amount * 0.25f;

            health.Damage(40f, Vector3.zero, recoverable: false);

            Assert.That(health.Current, Is.EqualTo(90f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(0f).Within(Tolerance));
        }

        // ---------------------------------------------------------------- weapon

        [Test]
        public void TakeHit_DamagesRecoverably_AndPassesTheShoveOnUninterpreted()
        {
            // The weapon deals damage and describes a shove; what the shove means
            // is the receiver's business, so TakeHit must forward it untouched.
            Health health = NewHealth(max: 100f, regainShare: 1f);
            var push = new Vector3(0f, 4f, 6f);
            Vector3 seenKnockback = Vector3.zero;
            bool seenLaunch = false;
            int knocks = 0;
            health.Knocked += (knockback, launch) =>
            {
                seenKnockback = knockback;
                seenLaunch = launch;
                knocks++;
            };

            health.TakeHit(25f, Vector3.zero, push, launch: true);

            Assert.That(health.Current, Is.EqualTo(75f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(25f).Within(Tolerance),
                        "Sword damage is the recoverable kind — only the beam is not.");
            Assert.That(knocks, Is.EqualTo(1));
            Assert.That(seenKnockback, Is.EqualTo(push));
            Assert.That(seenLaunch, Is.True);
        }

        // ------------------------------------------------------------ normalised

        [Test]
        public void Normalised_ReadoutsDescribeTheThreeRegionsOfTheBar()
        {
            Health health = NewHealth(max: 200f, regainShare: 1f);
            health.Damage(50f, Vector3.zero);   // 150 solid, 50 grey.

            Assert.That(health.CurrentNormalised, Is.EqualTo(0.75f).Within(Tolerance));
            Assert.That(health.RecoverableNormalised, Is.EqualTo(1f).Within(Tolerance),
                        "RecoverableNormalised is the far edge of grey, not its width.");
        }

        // ------------------------------------------------------------ last hit

        [Test]
        public void LastHit_ReportsWhatTheFilterDidAndWhetherTheHitWasRecoverable()
        {
            // The damage numbers style weak-point and permanent hits off these two
            // readings, from inside the Hurt handler — so they must already be
            // current when Hurt fires, not only afterwards.
            Health health = NewHealth(max: 100f);
            health.IncomingDamageFilter = (amount, _) => amount * 2f;
            float scaleSeenInHurt = 0f;
            bool recoverableSeenInHurt = true;
            health.Hurt += (_, _) =>
            {
                scaleSeenInHurt = health.LastHitScale;
                recoverableSeenInHurt = health.LastHitRecoverable;
            };

            health.Damage(10f, Vector3.zero, recoverable: false);

            Assert.That(scaleSeenInHurt, Is.EqualTo(2f).Within(Tolerance));
            Assert.That(recoverableSeenInHurt, Is.False);

            health.IncomingDamageFilter = (amount, _) => amount * 0.25f;
            health.Damage(10f, Vector3.zero);

            Assert.That(health.LastHitScale, Is.EqualTo(0.25f).Within(Tolerance));
            Assert.That(health.LastHitRecoverable, Is.True);
        }

        [Test]
        public void Normalised_ReadoutsSurviveAZeroMaxWithoutDividingByZero()
        {
            Health health = NewHealth(max: 0f);

            Assert.That(health.CurrentNormalised, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(health.RecoverableNormalised, Is.EqualTo(0f).Within(Tolerance));
        }
    }
}
