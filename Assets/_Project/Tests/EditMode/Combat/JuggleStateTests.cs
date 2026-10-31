using NUnit.Framework;
using Pickleball.Sandbox.Juggle;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// The enemy half of the air game, after Castle Crashers: lightweights loop
    /// forever, middleweights give out after roughly 15-18 air hits, heavies
    /// fall after 1-3, every hit refreshes hitstun, and an enemy defeated in
    /// the air dies only when it lands.
    /// </summary>
    public class JuggleStateTests
    {
        const float Frame = 1f / 60f;
        /// <summary>One air hit per this many seconds: a brisk air combo.</summary>
        const float Cadence = 0.3f;

        static JuggleState Juggle(JuggleWeight weight) => new JuggleState(JuggleSettings.For(weight));

        /// <summary>Launch from the ground, then hit every <paramref name="cadence"/>
        /// seconds until it lands. Returns the air hits it took.</summary>
        static int AirHitsBeforeLanding(JuggleState j, float dt, float cadence = Cadence, int maxHits = 300)
        {
            j.Hit(launch: true);
            int taken = 0;
            float untilHit = cadence;
            while (taken < maxHits)
            {
                // Landing resets AirHits, so count them here.
                if (j.Tick(dt) != JuggleLanding.None) return taken;
                untilHit -= dt;
                if (untilHit <= 1e-5f)
                {
                    untilHit += cadence;
                    j.Hit(launch: false);
                    taken++;
                }
            }
            return taken;
        }

        // ---- the weight classes ------------------------------------------------

        [Test]
        public void Lightweight_JugglesIndefinitely()
        {
            Assert.That(AirHitsBeforeLanding(Juggle(JuggleWeight.Light), Frame), Is.EqualTo(300));
        }

        [Test]
        public void Middleweight_DropsAfterRoughly15To18AirHits()
        {
            Assert.That(AirHitsBeforeLanding(Juggle(JuggleWeight.Medium), Frame), Is.InRange(15, 18));
        }

        [Test]
        public void Heavyweight_FallsAfter1To3AirHits()
        {
            Assert.That(AirHitsBeforeLanding(Juggle(JuggleWeight.Heavy), Frame), Is.InRange(1, 3));
        }

        [TestCase(JuggleWeight.Medium, 1f / 30f)]
        [TestCase(JuggleWeight.Medium, 1f / 144f)]
        [TestCase(JuggleWeight.Heavy, 1f / 30f)]
        [TestCase(JuggleWeight.Heavy, 1f / 144f)]
        public void WhereAJuggleGivesOut_DoesNotDependOnFrameRate(JuggleWeight weight, float dt)
        {
            int at60 = AirHitsBeforeLanding(Juggle(weight), Frame);
            Assert.That(AirHitsBeforeLanding(Juggle(weight), dt), Is.InRange(at60 - 1, at60 + 1));
        }

        [Test]
        public void Lightweight_StaysUnderItsCeiling()
        {
            var j = Juggle(JuggleWeight.Light);
            j.Hit(launch: true);
            float peak = 0f, untilHit = Cadence;
            for (int frame = 0; frame < 3000; frame++)
            {
                j.Tick(Frame);
                if (j.Height > peak) peak = j.Height;
                if ((untilHit -= Frame) <= 1e-5f) { untilHit += Cadence; j.Hit(false); }
            }
            Assert.That(j.IsAirborne, Is.True);
            Assert.That(peak, Is.LessThanOrEqualTo(j.Settings.MaxHeight + 0.01f));
        }

        // ---- the relaunch rule -------------------------------------------------

        [Test]
        public void Relaunch_IsRelaunchTimesWeightTimesDecayToTheN()
        {
            var s = new JuggleSettings(JuggleWeight.Medium, 3f, 4f, 0.5f, 0.8f, 0f, 100f, 0.5f);
            Assert.That(s.RelaunchFor(0), Is.EqualTo(2f).Within(1e-5f));
            Assert.That(s.RelaunchFor(1), Is.EqualTo(1.6f).Within(1e-5f));
            Assert.That(s.RelaunchFor(3), Is.EqualTo(2f * 0.8f * 0.8f * 0.8f).Within(1e-5f));
        }

        [Test]
        public void Relaunch_UnderTheMinimum_LiftsNothing()
        {
            var s = new JuggleSettings(JuggleWeight.Heavy, 3f, 4f, 0.6f, 0.5f, 1.5f, 100f, 0.5f);
            Assert.That(s.RelaunchFor(0), Is.EqualTo(2.4f).Within(1e-5f));
            Assert.That(s.RelaunchFor(1), Is.Zero);    // 1.2 < 1.5
        }

        [Test]
        public void AirHit_NeverSlowsABodyRisingFaster()
        {
            var s = new JuggleSettings(JuggleWeight.Medium, 10f, 2f, 1f, 1f, 0f, 100f, 0.5f);
            var j = new JuggleState(s);
            j.Hit(launch: true);
            j.Tick(Frame);
            float rising = j.VerticalSpeed;
            Assert.That(rising, Is.GreaterThan(2f));
            j.Hit(launch: false);
            Assert.That(j.VerticalSpeed, Is.EqualTo(rising));
            Assert.That(j.AirHits, Is.EqualTo(1));
        }

        [Test]
        public void AirHit_LiftsAFallingBody()
        {
            var j = Juggle(JuggleWeight.Medium);
            j.Hit(launch: true);
            for (int i = 0; i < 30; i++) j.Tick(Frame);           // past the apex
            Assert.That(j.VerticalSpeed, Is.LessThan(0f));
            j.Hit(launch: false);
            Assert.That(j.VerticalSpeed, Is.GreaterThan(0f));
        }

        [Test]
        public void SpentLift_StillStallsTheFall()
        {
            var j = Juggle(JuggleWeight.Heavy);
            j.Hit(launch: true);
            j.Hit(launch: false);                                   // the one lift a heavy gets
            for (int i = 0; i < 25; i++) j.Tick(Frame);
            Assert.That(j.VerticalSpeed, Is.LessThan(0f));
            j.Hit(launch: false);                                   // lift spent
            Assert.That(j.VerticalSpeed, Is.Zero);
        }

        [Test]
        public void GroundHit_WithoutLaunch_StaysDown()
        {
            var j = Juggle(JuggleWeight.Light);
            j.Hit(launch: false);
            Assert.That(j.IsAirborne, Is.False);
            Assert.That(j.AirHits, Is.Zero);
        }

        [Test]
        public void Heavyweight_LaunchesLowerThanLightweight()
        {
            var light = Juggle(JuggleWeight.Light);
            var heavy = Juggle(JuggleWeight.Heavy);
            light.Hit(true);
            heavy.Hit(true);
            Assert.That(heavy.VerticalSpeed, Is.LessThan(light.VerticalSpeed));
        }

        [Test]
        public void Landing_ResetsTheAirHitCount()
        {
            var j = Juggle(JuggleWeight.Medium);
            j.Hit(launch: true);
            j.Hit(launch: false);
            j.Hit(launch: false);
            Assert.That(j.AirHits, Is.EqualTo(2));

            JuggleLanding landing = JuggleLanding.None;
            for (int i = 0; i < 1000 && landing == JuggleLanding.None; i++) landing = j.Tick(Frame);
            Assert.That(landing, Is.EqualTo(JuggleLanding.Landed));
            Assert.That(j.AirHits, Is.Zero);
            Assert.That(j.Height, Is.Zero);

            // The next juggle starts fresh: its first air hit gets the full relaunch.
            j.Hit(launch: true);
            j.Hit(launch: false);
            Assert.That(j.AirHits, Is.EqualTo(1));
        }

        // ---- hitstun -----------------------------------------------------------

        [Test]
        public void Hit_StunsAndCannotAct_UntilHitstunRunsOut()
        {
            var j = Juggle(JuggleWeight.Medium);
            Assert.That(j.CanAct, Is.True);
            j.Hit(launch: false);
            Assert.That(j.CanAct, Is.False);
            int frames = 0;
            while (j.InHitstun && frames < 1000) { j.Tick(Frame); frames++; }
            Assert.That(frames * Frame, Is.EqualTo(j.Settings.Hitstun).Within(Frame + 1e-4f));
            Assert.That(j.CanAct, Is.True);
        }

        [Test]
        public void EveryHit_RefreshesHitstun()
        {
            var j = Juggle(JuggleWeight.Medium);
            j.Hit(launch: false);
            for (int i = 0; i < 20; i++) j.Tick(Frame);
            j.Hit(launch: false);
            Assert.That(j.HitstunLeft, Is.EqualTo(j.Settings.Hitstun));
        }

        [Test]
        public void Airborne_CannotAct_EvenWithHitstunSpent()
        {
            var s = new JuggleSettings(JuggleWeight.Light, 8f, 4f, 1f, 1f, 0f, 10f, 0.05f);
            var j = new JuggleState(s);
            j.Hit(launch: true);
            for (int i = 0; i < 10; i++) j.Tick(Frame);
            Assert.That(j.InHitstun, Is.False);
            Assert.That(j.IsAirborne, Is.True);
            Assert.That(j.CanAct, Is.False);
        }

        // ---- defeat waits for the ground ---------------------------------------

        [Test]
        public void DefeatOnTheGround_IsImmediate()
        {
            Assert.That(Juggle(JuggleWeight.Medium).Defeat(), Is.True);
        }

        [Test]
        public void DefeatInTheAir_DiesOnLanding_AndCanStillBeJuggled()
        {
            var j = Juggle(JuggleWeight.Medium);
            j.Hit(launch: true);
            j.Tick(Frame);
            Assert.That(j.Defeat(), Is.False);
            Assert.That(j.DefeatPending, Is.True);

            for (int i = 0; i < 20; i++) j.Tick(Frame);
            j.Hit(launch: false);                                   // still juggleable
            Assert.That(j.AirHits, Is.EqualTo(1));

            JuggleLanding landing = JuggleLanding.None;
            for (int i = 0; i < 1000 && landing == JuggleLanding.None; i++) landing = j.Tick(Frame);
            Assert.That(landing, Is.EqualTo(JuggleLanding.LandedDefeated));
            Assert.That(j.DefeatPending, Is.False);
        }

        [Test]
        public void DefeatInTheAir_WithDeferralOff_IsImmediate()
        {
            var s = new JuggleSettings(JuggleWeight.Medium, 4f, 4f, 1f, 0.9f, 1.5f, 3f, 0.5f,
                                       defeatOnLanding: false);
            var j = new JuggleState(s);
            j.Hit(launch: true);
            j.Tick(Frame);
            Assert.That(j.Defeat(), Is.True);
            Assert.That(j.DefeatPending, Is.False);
        }

        [Test]
        public void Reset_PutsItBackOnTheGroundWithNothingPending()
        {
            var j = Juggle(JuggleWeight.Medium);
            j.Hit(launch: true);
            j.Tick(Frame);
            j.Defeat();
            j.Reset();
            Assert.That(j.IsAirborne, Is.False);
            Assert.That(j.DefeatPending, Is.False);
            Assert.That(j.CanAct, Is.True);
        }
    }
}
