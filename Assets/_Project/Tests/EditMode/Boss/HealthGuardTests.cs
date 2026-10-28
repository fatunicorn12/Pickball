using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Tests.Boss
{
    /// <summary>
    /// The edges of <see cref="Health"/>'s public API that <c>HealthTests</c>
    /// leaves alone: bad input (zero, negative, NaN), calls on a corpse, and the
    /// order in which a killing blow announces itself.
    ///
    /// Bad input matters because damage arrives from maths: inverse-square
    /// falloff, range multipliers, field-velocity billing. A NaN or a negative
    /// from any of them must be a no-op, never a kill or a full heal.
    /// </summary>
    public class HealthGuardTests : BossTestFixture
    {
        const float Tolerance = 1e-4f;

        Health NewHealth(float max = 100f, float regainShare = 0.5f)
        {
            Health health = NewComponent<Health>();
            SetTunable(health, "maxHealth", max);
            SetTunable(health, "regainShare", regainShare);
            SetTunable(health, "regainPerDamageDealt", 1f);
            health.ResetToFull();
            return health;
        }

        // ------------------------------------------------------------------ NaN

        [Ignore("Known bug NB3: the <= 0 guard lets NaN through and Mathf.Min(NaN, Current) " +
                "returns Current, so the hit is lethal. See Docs/Testing/QA-Assessment.md.")]
        [Test]
        public void Damage_NaN_IsIgnored()
        {
            Health health = NewHealth();

            health.Damage(float.NaN, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(health.IsDead, Is.False);
        }

        [Ignore("Known bug NB3: Heal(NaN) fills the bar. See Docs/Testing/QA-Assessment.md.")]
        [Test]
        public void Heal_NaN_IsIgnored()
        {
            Health health = NewHealth();
            health.Damage(40f, Vector3.zero, recoverable: false);

            health.Heal(float.NaN);

            Assert.That(health.Current, Is.EqualTo(60f).Within(Tolerance));
        }

        [Ignore("Known bug NB3: CreditDamageDealt(NaN) cashes in the whole grey pool. " +
                "See Docs/Testing/QA-Assessment.md.")]
        [Test]
        public void CreditDamageDealt_NaN_IsIgnored()
        {
            Health health = NewHealth(regainShare: 1f);
            health.Damage(40f, Vector3.zero);

            health.CreditDamageDealt(float.NaN);

            Assert.That(health.Current, Is.EqualTo(60f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(40f).Within(Tolerance));
        }

        // ---------------------------------------------------------------- heal

        [TestCase(0f)]
        [TestCase(-25f)]
        public void Heal_OfZeroOrLess_ChangesNothing(float amount)
        {
            Health health = NewHealth();
            health.Damage(40f, Vector3.zero, recoverable: false);
            int changed = 0;
            health.Changed += _ => changed++;

            health.Heal(amount);

            Assert.That(health.Current, Is.EqualTo(60f).Within(Tolerance));
            Assert.That(changed, Is.Zero, "A refused heal should not tell the UI anything changed.");
        }

        [Test]
        public void Heal_StopsAtMax()
        {
            Health health = NewHealth();
            health.Damage(10f, Vector3.zero, recoverable: false);

            health.Heal(500f);

            Assert.That(health.Current, Is.EqualTo(100f).Within(Tolerance));
        }

        [Test]
        public void Heal_CannotRaiseTheDead()
        {
            Health health = NewHealth();
            health.Damage(1000f, Vector3.zero);

            health.Heal(50f);

            Assert.That(health.IsDead, Is.True, "Revival goes through ResetToFull, not Heal.");
            Assert.That(health.Current, Is.EqualTo(0f).Within(Tolerance));
        }

        // -------------------------------------------------------------- credit

        [TestCase(0f)]
        [TestCase(-10f)]
        public void CreditDamageDealt_OfZeroOrLess_LeavesTheGreyPoolAlone(float dealt)
        {
            Health health = NewHealth(regainShare: 1f);
            health.Damage(40f, Vector3.zero);

            health.CreditDamageDealt(dealt);

            Assert.That(health.Current, Is.EqualTo(60f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(40f).Within(Tolerance));
        }

        // -------------------------------------------------------------- events

        /// <summary>Damage numbers and the regain credit read the hit inside
        /// <c>Hurt</c>; the boss tears the fight down inside <c>Died</c>. The
        /// killing blow therefore has to report the hit first.</summary>
        [Test]
        public void KillingBlow_RaisesHurtThenChangedThenDied()
        {
            Health health = NewHealth();
            var order = new List<string>();
            health.Hurt += (_, _) => order.Add("Hurt");
            health.Changed += _ => order.Add("Changed");
            health.Died += _ => order.Add("Died");

            health.Damage(150f, Vector3.zero);

            Assert.That(order, Is.EqualTo(new[] { "Hurt", "Changed", "Died" }));
        }

        [Test]
        public void HitsOnACorpse_RaiseNothing()
        {
            Health health = NewHealth();
            health.Damage(150f, Vector3.zero);
            int events = 0;
            health.Hurt += (_, _) => events++;
            health.Changed += _ => events++;
            health.Died += _ => events++;

            health.Damage(10f, Vector3.zero);
            health.Damage(10f, Vector3.zero, recoverable: false);

            Assert.That(events, Is.Zero);
        }

        /// <summary>Knockback is passed on even when the hit itself did nothing,
        /// so a ragdoll or a training dummy still reacts to the blow that killed
        /// it a frame ago. Pinned so a change to it is deliberate.</summary>
        [Test]
        public void TakeHit_OnACorpse_StillPassesTheKnockbackOn()
        {
            Health health = NewHealth();
            health.Damage(150f, Vector3.zero);
            bool knocked = false;
            health.Knocked += (_, _) => knocked = true;

            health.TakeHit(10f, Vector3.zero, Vector3.forward, launch: false);

            Assert.That(knocked, Is.True);
        }
    }
}
