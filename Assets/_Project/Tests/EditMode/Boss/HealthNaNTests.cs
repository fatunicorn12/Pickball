using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Tests.Boss
{
    /// <summary>
    /// The non-finite cases of QA NB3 that <see cref="HealthGuardTests"/> does not
    /// cover: infinities, and a damage filter that turns a good hit into NaN.
    ///
    /// Hypatia's damage filter multiplies by a config value and her clock scales
    /// by a slow multiplier, so a NaN or infinity can arrive from the boss side
    /// as easily as from the weapon side. Either must be a no-op, never a kill,
    /// a full heal or a cashed-in grey pool.
    /// </summary>
    public class HealthNaNTests : BossTestFixture
    {
        const float Tolerance = 1e-4f;

        Health NewHealth(float max = 100f, float regainShare = 1f)
        {
            Health health = NewComponent<Health>();
            SetTunable(health, "maxHealth", max);
            SetTunable(health, "regainShare", regainShare);
            SetTunable(health, "regainPerDamageDealt", 1f);
            SetTunable(health, "hitGraceSeconds", 0f);
            health.ResetToFull();
            return health;
        }

        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void Damage_Infinite_IsIgnored(float amount)
        {
            Health health = NewHealth();
            int hurt = 0, died = 0;
            health.Hurt += (_, _) => hurt++;
            health.Died += _ => died++;

            health.Damage(amount, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(health.IsDead, Is.False);
            Assert.That(hurt, Is.EqualTo(0));
            Assert.That(died, Is.EqualTo(0));
        }

        [Test]
        public void Damage_NaN_RaisesNoEvents()
        {
            Health health = NewHealth();
            int hurt = 0, died = 0, changed = 0;
            health.Hurt += (_, _) => hurt++;
            health.Died += _ => died++;
            health.Changed += _ => changed++;

            health.Damage(float.NaN, Vector3.zero);

            Assert.That(hurt, Is.EqualTo(0));
            Assert.That(died, Is.EqualTo(0));
            Assert.That(changed, Is.EqualTo(0));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Damage_FilterReturningNonFinite_IsIgnored(float filtered)
        {
            Health health = NewHealth();
            health.IncomingDamageFilterByKind = (_, _, _) => filtered;

            health.Damage(10f, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(100f).Within(Tolerance));
            Assert.That(health.IsDead, Is.False);
        }

        [Test]
        public void Heal_Infinity_IsIgnored()
        {
            Health health = NewHealth();
            health.Damage(40f, Vector3.zero, recoverable: false);

            health.Heal(float.PositiveInfinity);

            Assert.That(health.Current, Is.EqualTo(60f).Within(Tolerance));
        }

        [Test]
        public void CreditDamageDealt_Infinity_IsIgnored()
        {
            Health health = NewHealth(regainShare: 1f);
            health.Damage(40f, Vector3.zero);

            health.CreditDamageDealt(float.PositiveInfinity);

            Assert.That(health.Current, Is.EqualTo(60f).Within(Tolerance));
            Assert.That(health.Recoverable, Is.EqualTo(40f).Within(Tolerance));
        }

        [Test]
        public void FiniteDamage_StillLands()
        {
            Health health = NewHealth(regainShare: 0f);

            health.Damage(25f, Vector3.zero);

            Assert.That(health.Current, Is.EqualTo(75f).Within(Tolerance));
        }

        [Test]
        public void FiniteHealAndCredit_StillWork()
        {
            Health health = NewHealth(regainShare: 1f);
            health.Damage(40f, Vector3.zero);

            health.CreditDamageDealt(10f);
            Assert.That(health.Current, Is.EqualTo(70f).Within(Tolerance));

            health.Heal(5f);
            Assert.That(health.Current, Is.EqualTo(75f).Within(Tolerance));
        }
    }
}
