using System.Reflection;
using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Tests.Boss
{
    /// <summary>
    /// When Newton enters phase two, and that it is one-way.
    ///
    /// Phase two turns on the apple barrage and changes every cadence, so the
    /// threshold is the fight's biggest single switch. These tests drive
    /// <c>UpdatePhase</c> directly rather than the whole <c>Update</c>, which
    /// would also run the attack timers, facing and animator.
    /// </summary>
    public class NewtonPhaseTests : BossTestFixture
    {
        NewtonBoss _boss;
        Health _health;

        [SetUp]
        public void MakeBoss()
        {
            _boss = NewComponent<NewtonBoss>();     // RequireComponent adds the Health
            // EditMode never runs Awake. A bare test boss has no body wired, so
            // Awake logs a warning; warnings do not fail a test.
            Call(_boss, "Awake");

            _health = _boss.Health;
            Assert.That(_health, Is.Not.Null, "NewtonBoss.Awake no longer caches its Health.");
            SetTunable(_health, "maxHealth", 100f);
            SetTunable(_boss, "phaseTwoAt", 0.5f);
            _health.ResetToFull();
        }

        static void Call(MonoBehaviour target, string method)
        {
            MethodInfo m = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(m, Is.Not.Null, $"{target.GetType().Name}.{method}() not found; it was probably renamed.");
            m.Invoke(target, null);
        }

        [Test]
        public void StartsInPhaseOne()
        {
            Assert.That(_boss.Phase, Is.EqualTo(1));
        }

        [Test]
        public void EntersPhaseTwo_AtExactlyTheThreshold()
        {
            _health.Damage(50f, Vector3.zero, recoverable: false);    // 50 / 100 = phaseTwoAt

            Call(_boss, "UpdatePhase");

            Assert.That(_boss.Phase, Is.EqualTo(2));
        }

        [Test]
        public void StaysInPhaseOne_JustAboveTheThreshold()
        {
            _health.Damage(49f, Vector3.zero, recoverable: false);

            Call(_boss, "UpdatePhase");

            Assert.That(_boss.Phase, Is.EqualTo(1));
        }

        [Test]
        public void PhaseTwo_SurvivesBeingHealedBackAboveTheThreshold()
        {
            _health.Damage(60f, Vector3.zero, recoverable: false);
            Call(_boss, "UpdatePhase");

            _health.Heal(100f);
            Call(_boss, "UpdatePhase");

            Assert.That(_boss.Phase, Is.EqualTo(2), "Phase two is one-way.");
        }

        /// <summary>A killing blow from above the threshold must not trigger the
        /// phase-two bark and well on the corpse: <c>Update</c> stops at death.</summary>
        [Test]
        public void KillingBlowFromPhaseOne_DoesNotEnterPhaseTwo()
        {
            _health.Damage(1000f, Vector3.zero);

            Tick(_boss);

            Assert.That(_boss.Phase, Is.EqualTo(1));
        }
    }
}
