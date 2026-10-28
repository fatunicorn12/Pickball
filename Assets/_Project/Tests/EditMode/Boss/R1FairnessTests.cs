using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Boss
{
    /// <summary>
    /// Playtest round 1 fairness rules: well core no longer locks the stick, the
    /// beam is capped per fire, a ring hits at most once per grace window, and the
    /// core fades near the camera. Time-integrated rules are swept at three frame
    /// rates so they cannot depend on dt.
    /// </summary>
    public class R1FairnessTests : BossTestFixture
    {
        static readonly float[] Dts = { 1f / 30f, 1f / 60f, 1f / 144f };

        static float Read(Component c, string field)
            => new SerializedObject(c).FindProperty(field).floatValue;

        [Test]
        public void WellCore_DefaultsDoNotLockMovement()
        {
            var well = NewComponent<GravityWell>();
            Assert.AreEqual(0f, Read(well, "coreMoveLock"));
            Assert.AreEqual(60f, Read(well, "strength"));
            Assert.AreEqual(6f, Read(well, "coreDamagePerSecond"));
            Assert.AreEqual(6f, Read(well, "lifetime"));
        }

        [Test]
        public void CoreAlpha_ZeroInsideFullBeyondFadeDistance()
        {
            Assert.AreEqual(0f, GravityWell.CoreAlpha(1f, 1.8f, 3f));
            Assert.AreEqual(0f, GravityWell.CoreAlpha(1.8f, 1.8f, 3f));
            Assert.AreEqual(0.5f, GravityWell.CoreAlpha(3.3f, 1.8f, 3f), 1e-4f);
            Assert.AreEqual(1f, GravityWell.CoreAlpha(20f, 1.8f, 3f));
            Assert.AreEqual(1f, GravityWell.CoreAlpha(0f, 1.8f, 0f), "fade disabled");
        }

        [Test]
        public void Beam_TotalDamagePerFire_NeverExceedsCap([ValueSource(nameof(Dts))] float dt)
        {
            const float cap = 30f, dps = 18f;
            float spent = 0f;
            for (float t = 0f; t < 2f; t += dt)
                spent += FluxionBeam.BudgetedDamage(dps * dt, spent, cap);
            Assert.LessOrEqual(spent, cap + 1e-3f);
            Assert.Greater(spent, cap - dps * dt - 1e-3f,
                           "cap should be reached when dps*fireTime exceeds it");
        }

        [Test]
        public void Beam_UncappedWhenCapZero()
            => Assert.AreEqual(5f, FluxionBeam.BudgetedDamage(5f, 999f, 0f));

        [Test]
        public void Beam_Defaults_MatchR1Tuning()
        {
            var beam = NewComponent<FluxionBeam>();
            Assert.AreEqual(1.6f, Read(beam, "chargeTime"));
            Assert.AreEqual(2.0f, Read(beam, "fireTime"));
            Assert.AreEqual(1.5f, Read(beam, "damagePerSpeed"));
            Assert.AreEqual(18f, Read(beam, "maxDamagePerSecond"));
            Assert.AreEqual(30f, Read(beam, "maxDamagePerFire"));
        }

        [Test]
        public void RingGrace_OnlyFirstOrbOfARing_DamagesInsideWindow([ValueSource(nameof(Dts))] float dt)
        {
            var g = new RingHitGrace();
            int hits = 0;
            // One orb of ring 0 reaches the player every frame for 0.4 s.
            for (float t = 0f; t < 0.4f; t += dt)
                if (g.TryHit(0, t, 0.5f)) hits++;
            Assert.AreEqual(1, hits);
        }

        [Test]
        public void RingGrace_DoesNotBlockOtherRings_AndExpires()
        {
            var g = new RingHitGrace();
            Assert.IsTrue(g.TryHit(0, 1f, 0.5f));
            Assert.IsFalse(g.TryHit(0, 1.49f, 0.5f));
            Assert.IsTrue(g.TryHit(1, 1.1f, 0.5f), "second ring is independent");
            Assert.IsTrue(g.TryHit(0, 1.5f, 0.5f), "window over");
        }

        [Test]
        public void Volley_Defaults_AreSofterAtMelee()
        {
            var v = NewComponent<InverseSquareVolley>();
            Assert.AreEqual(7f, Read(v, "baseDamage"));
            Assert.AreEqual(18f, Read(v, "maxDamage"));
            Assert.AreEqual(0.5f, Read(v, "ringHitGrace"));
            var boss = NewComponent<NewtonBoss>();
            Assert.AreEqual(1.4f, Read(boss, "volleyCloseUrgency"));
            Assert.AreEqual(7f, Read(boss, "wellMinDistanceFromPlayer"));
        }
    }
}
