using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using Pickleball.Sandbox.Juggle;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// The kinds of blow, after Kingdom Hearts II's attack data: a flinch stuns,
    /// a launch lifts, a heavy hit staggers for longer, and a knock-away ends a
    /// juggle. Also that the reaction survives the trip from a step's settings to
    /// the receiver, and that receivers with no use for it are left as they were.
    /// </summary>
    public class HitReactionTests
    {
        static JuggleState Medium() => new JuggleState(JuggleSettings.For(JuggleWeight.Medium));

        /// <summary>Tick until it lands. Returns the seconds taken.</summary>
        static float FlyUntilLanded(JuggleState j, float dt)
        {
            float t = 0f;
            for (int i = 0; i < 2000; i++)
            {
                t += dt;
                if (j.Tick(dt) != JuggleLanding.None) return t;
            }
            Assert.Fail("It never landed.");
            return t;
        }

        // ---- HitSettings -------------------------------------------------------

        [Test]
        public void SettingsWithALaunchSpeedAndNoReaction_CountAsALauncher()
        {
            var hit = new HitSettings(20f, 8f, 12.8f);
            Assert.That(hit.Reaction, Is.EqualTo(HitReaction.Launch));
            Assert.That(hit.IsLauncher, Is.True);
        }

        [Test]
        public void SettingsWithNeitherAreAFlinch()
        {
            var hit = new HitSettings(10f, 2f);
            Assert.That(hit.Reaction, Is.EqualTo(HitReaction.Flinch));
            Assert.That(hit.IsLauncher, Is.False);
        }

        [Test]
        public void AnExplicitReaction_BeatsTheLaunchSpeed()
        {
            var hit = new HitSettings(16f, 5f, 3f, 0.08f, HitReaction.KnockAway);
            Assert.That(hit.Reaction, Is.EqualTo(HitReaction.KnockAway));
            Assert.That(hit.IsLauncher, Is.False);
        }

        // ---- flinch, launch, heavy ---------------------------------------------

        [Test]
        public void TheBoolOverload_MatchesFlinchAndLaunch()
        {
            var byBool = Medium();
            var byReaction = Medium();
            byBool.Hit(launch: true);
            byReaction.Hit(HitReaction.Launch);
            Assert.That(byReaction.VerticalSpeed, Is.EqualTo(byBool.VerticalSpeed));

            byBool.Hit(launch: false);
            byReaction.Hit(HitReaction.Flinch);
            Assert.That(byReaction.VerticalSpeed, Is.EqualTo(byBool.VerticalSpeed));
            Assert.That(byReaction.AirHits, Is.EqualTo(byBool.AirHits));
        }

        [Test]
        public void AFlinchOnTheGround_DoesNotLift()
        {
            var j = Medium();
            j.Hit(HitReaction.Flinch);
            Assert.That(j.IsAirborne, Is.False);
            Assert.That(j.InHitstun, Is.True);
        }

        [Test]
        public void AHeavyHit_StunsLongerThanAFlinch_WithoutLifting()
        {
            var settings = JuggleSettings.For(JuggleWeight.Medium);
            var j = new JuggleState(settings);
            j.Hit(HitReaction.Heavy);
            Assert.That(j.HitstunLeft, Is.EqualTo(settings.Hitstun * settings.HeavyStunScale).Within(1e-4f));
            Assert.That(j.HitstunLeft, Is.GreaterThan(settings.Hitstun));
            Assert.That(j.IsAirborne, Is.False);
        }

        // ---- knock-away --------------------------------------------------------

        [Test]
        public void AKnockAway_ThrowsItUpAndAcrossAtTheBlowsSpeed()
        {
            var settings = JuggleSettings.For(JuggleWeight.Medium);
            var j = new JuggleState(settings);
            j.Hit(HitReaction.KnockAway, 5f);
            Assert.That(j.IsKnockedAway, Is.True);
            Assert.That(j.IsAirborne, Is.True);
            Assert.That(j.KnockSpeed, Is.EqualTo(5f * settings.KnockAwayCarry).Within(1e-4f));
            Assert.That(j.VerticalSpeed, Is.EqualTo(settings.KnockAwayLift * settings.WeightScale).Within(1e-4f));
        }

        [Test]
        public void AKnockAway_EndsAJuggle_FurtherHitsDoNotLiftIt()
        {
            var j = Medium();
            j.Hit(HitReaction.Launch);
            j.Tick(0.1f);
            j.Hit(HitReaction.KnockAway, 5f);

            // The blade and the ring of one finisher land a moment apart: the second
            // must not re-lift or re-throw the body the first just threw.
            float vy = j.VerticalSpeed;
            int airHits = j.AirHits;
            float thrownAt = j.KnockSpeed;
            j.Hit(HitReaction.Flinch);
            j.Hit(HitReaction.Launch);
            j.Hit(HitReaction.KnockAway, 50f);
            Assert.That(j.VerticalSpeed, Is.EqualTo(vy));
            Assert.That(j.AirHits, Is.EqualTo(airHits));
            Assert.That(j.KnockSpeed, Is.EqualTo(thrownAt));
        }

        [TestCase(1f / 30)]
        [TestCase(1f / 60)]
        [TestCase(1f / 144)]
        public void AKnockAway_LandsThenRecovers_BeforeItCanActOrBeLiftedAgain(float dt)
        {
            var settings = JuggleSettings.For(JuggleWeight.Medium);
            var j = new JuggleState(settings);
            j.Hit(HitReaction.KnockAway, 5f);

            float flight = FlyUntilLanded(j, dt);
            float expected = 2f * settings.KnockAwayLift * settings.WeightScale / settings.Gravity;
            Assert.That(flight, Is.EqualTo(expected).Within(2f * dt), "the same arc at any frame rate");

            Assert.That(j.IsKnockedAway, Is.False);
            Assert.That(j.KnockSpeed, Is.Zero, "it stops flying on landing");
            Assert.That(j.IsRecovering, Is.True);
            Assert.That(j.CanAct, Is.False);

            j.Hit(HitReaction.Launch);
            Assert.That(j.IsAirborne, Is.False, "a downed body can't be launched");

            for (float t = 0f; t < settings.RecoverSeconds + 2f * dt; t += dt) j.Tick(dt);
            Assert.That(j.IsRecovering, Is.False);
            Assert.That(j.CanAct, Is.True);

            j.Hit(HitReaction.Launch);
            Assert.That(j.IsAirborne, Is.True, "up and fighting, it can be launched again");
        }

        [Test]
        public void AKnockAway_WithNoLiftToGive_LandsAtOnce_AndNeverSticks()
        {
            var settings = new JuggleSettings(JuggleWeight.Heavy, 3.7f, 4f, weightScale: 0f, 0.5f, 1.5f, 3f, 0.3f);
            var j = new JuggleState(settings);
            j.Hit(HitReaction.KnockAway, 5f);
            Assert.That(j.IsKnockedAway, Is.True);

            Assert.That(j.Tick(0.01f), Is.EqualTo(JuggleLanding.Landed));
            Assert.That(j.IsKnockedAway, Is.False);
            Assert.That(j.IsRecovering, Is.True);
        }

        [Test]
        public void AKnockAway_NeverPassesTheCeiling()
        {
            var settings = JuggleSettings.For(JuggleWeight.Medium);
            var j = new JuggleState(settings);
            j.Hit(HitReaction.Launch);
            for (int i = 0; i < 400 && j.VerticalSpeed > 0f; i++) j.Tick(0.001f);

            j.Hit(HitReaction.KnockAway, 5f);
            float peak = j.Height;
            while (j.VerticalSpeed > 0f)
            {
                j.Tick(0.001f);
                peak = Mathf.Max(peak, j.Height);
            }
            Assert.That(peak, Is.LessThanOrEqualTo(settings.MaxHeight + 0.01f));
        }

        [Test]
        public void ADefeatTakenWhileKnockedAway_WaitsForTheLanding()
        {
            var j = Medium();
            j.Hit(HitReaction.KnockAway, 5f);
            Assert.That(j.Defeat(), Is.False);
            Assert.That(j.DefeatPending, Is.True);

            JuggleLanding landing = JuggleLanding.None;
            for (int i = 0; i < 2000 && landing == JuggleLanding.None; i++) landing = j.Tick(1f / 60f);
            Assert.That(landing, Is.EqualTo(JuggleLanding.LandedDefeated));
        }

        [Test]
        public void Reset_ClearsAThrowAndARecovery()
        {
            var j = Medium();
            j.Hit(HitReaction.KnockAway, 5f);
            j.Reset();
            Assert.That(j.IsKnockedAway, Is.False);
            Assert.That(j.KnockSpeed, Is.Zero);
            Assert.That(j.CanAct, Is.True);

            j.Hit(HitReaction.KnockAway, 5f);
            FlyUntilLanded(j, 1f / 60f);
            j.Reset();
            Assert.That(j.IsRecovering, Is.False);
        }

        // ---- the trip to the receiver -------------------------------------------

        sealed class PlainTarget : IDamageable
        {
            public float Amount;
            public bool Launch;
            public DamageKind Kind;

            public void TakeHit(float amount, Vector3 hitPoint, Vector3 knockback, bool launch,
                                DamageKind kind = DamageKind.Physical)
            {
                Amount = amount;
                Launch = launch;
                Kind = kind;
            }
        }

        [Test]
        public void AReceiverWithNoUseForReactions_GetsThePlainCall_WithOnlyALaunchSurviving()
        {
            var target = new PlainTarget();
            IDamageable damageable = target;

            damageable.TakeHit(new HitImpact(7f, Vector3.zero, Vector3.up, HitReaction.Launch, DamageKind.Spell));
            Assert.That(target.Amount, Is.EqualTo(7f));
            Assert.That(target.Launch, Is.True);
            Assert.That(target.Kind, Is.EqualTo(DamageKind.Spell));

            damageable.TakeHit(new HitImpact(7f, Vector3.zero, Vector3.up, HitReaction.KnockAway));
            Assert.That(target.Launch, Is.False);
        }

        static Health MakeHealth(out GameObject host)
        {
            host = new GameObject("Target") { hideFlags = HideFlags.DontSave };
            return host.AddComponent<Health>();
        }

        [Test]
        public void Health_RaisesImpacted_WithTheReaction_AndStillRaisesKnocked()
        {
            var health = MakeHealth(out var host);
            try
            {
                HitImpact seen = default;
                Vector3 shove = default;
                health.Impacted += i => seen = i;
                health.Knocked += (k, _) => shove = k;

                health.TakeHit(new HitImpact(5f, Vector3.zero, Vector3.right, HitReaction.Heavy));
                Assert.That(seen.Reaction, Is.EqualTo(HitReaction.Heavy));
                Assert.That(seen.Damage, Is.EqualTo(5f));
                Assert.That(shove, Is.EqualTo(Vector3.right));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [TestCase(true, HitReaction.Launch)]
        [TestCase(false, HitReaction.Flinch)]
        public void Health_ThePlainCall_ArrivesAsAnImpactToo(bool launch, HitReaction expected)
        {
            // Spells still make the plain call, and they must keep reaching a dummy.
            var health = MakeHealth(out var host);
            try
            {
                HitReaction seen = (HitReaction)(-1);
                DamageKind kind = DamageKind.Physical;
                health.Impacted += i => { seen = i.Reaction; kind = i.Kind; };
                health.TakeHit(5f, Vector3.zero, Vector3.zero, launch, DamageKind.Spell);
                Assert.That(seen, Is.EqualTo(expected));
                Assert.That(kind, Is.EqualTo(DamageKind.Spell));
            }
            finally { Object.DestroyImmediate(host); }
        }

        // ---- the shipped finishers ----------------------------------------------

        const string Steps = "Assets/_Project/ScriptableObjects/Combat/Steps/";

        static ComboStepDefinition Load(string path)
        {
            var step = AssetDatabase.LoadAssetAtPath<ComboStepDefinition>(Steps + path);
            Assert.That(step, Is.Not.Null, path);
            return step;
        }

        [Test, Category("Assets")]
        public void TheAirFinishers_KnockAway()
        {
            var unitCircle = Load("Step_UnitCircle.asset");
            Assert.That(unitCircle.Hit.Reaction, Is.EqualTo(HitReaction.KnockAway));
            Assert.That(unitCircle.Burst.Hit.Reaction, Is.EqualTo(HitReaction.KnockAway), "its ring too");
            Assert.That(Load("Ruler/Step_RulerPhaseShift.asset").Hit.Reaction, Is.EqualTo(HitReaction.KnockAway));
        }

        [Test, Category("Assets")]
        public void TheGroundFinishers_LaunchOrStagger()
        {
            Assert.That(Load("Step_Slash4.asset").Hit.Reaction, Is.EqualTo(HitReaction.Launch));
            Assert.That(Load("Step_AreaUnderCurve.asset").Hit.Reaction, Is.EqualTo(HitReaction.Launch));
            Assert.That(Load("Ruler/Step_RulerSlam.asset").Hit.Reaction, Is.EqualTo(HitReaction.Heavy));
            Assert.That(Load("Ruler/Step_RulerAreaUnderCurve.asset").Hit.Reaction, Is.EqualTo(HitReaction.Heavy));
        }

        [Test, Category("Assets")]
        public void AirSlash_SharesPlusCsState_WithTheSameClipAndSpeed()
        {
            // One Attack-layer state plays one clip at one speed: if these drift apart,
            // the state keeps one step's and the other's timeline no longer matches it.
            var air = Load("Step_AirSlash.asset");
            var plusC = Load("Step_PlusC.asset");
            Assert.That(air.AnimatorState, Is.EqualTo(plusC.AnimatorState));
            Assert.That(air.Clip, Is.SameAs(plusC.Clip));
            Assert.That(air.PlaybackSpeed, Is.EqualTo(plusC.PlaybackSpeed));
        }

        [Test, Category("Assets")]
        public void MidChainHits_StayFlinches()
        {
            Assert.That(Load("Step_Slash1.asset").Hit.Reaction, Is.EqualTo(HitReaction.Flinch));
            Assert.That(Load("Step_AirSlash.asset").Hit.Reaction, Is.EqualTo(HitReaction.Flinch));
            Assert.That(Load("Ruler/Step_RulerSpin.asset").Hit.Reaction, Is.EqualTo(HitReaction.Flinch));
        }
    }
}
