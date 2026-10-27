using NUnit.Framework;
using Pickleball.Sandbox.Movement.Logic;

namespace Pickleball.Tests.Movement
{
    /// <summary>
    /// Jumps are authored as apex heights and converted to launch speeds, so the
    /// conversion and the integrator have to agree exactly — otherwise "2 m"
    /// in the config is a lie that changes with frame rate. These pin both.
    /// </summary>
    public class JumpMathTests
    {
        // A Sunshine-shaped profile: light rise, lighter apex, heavy fall.
        static readonly GravityProfile Floaty = new(rise: 20f, apex: 8f, fall: 34f,
                                                     apexBand: 2f, maxFallSpeed: 18f);
        static readonly GravityProfile Plain = GravityProfile.Uniform(18f, 30f);

        [TestCase(0.35f)]
        [TestCase(1.2f)]
        [TestCase(2.6f)]
        public void LaunchSpeed_RoundTripsThroughApexHeight(float height)
        {
            Assert.That(JumpMath.ApexHeight(JumpMath.LaunchSpeed(height, Floaty), Floaty),
                        Is.EqualTo(height).Within(1e-4f));
            Assert.That(JumpMath.ApexHeight(JumpMath.LaunchSpeed(height, Plain), Plain),
                        Is.EqualTo(height).Within(1e-4f));
        }

        [Test]
        public void LaunchSpeed_MatchesSqrt2ghForUniformGravity()
        {
            // The old ChibiLocomotion formula, so plain arcs are unchanged.
            Assert.That(JumpMath.LaunchSpeed(0.56f, Plain),
                        Is.EqualTo(UnityEngine.Mathf.Sqrt(2f * 18f * 0.56f)).Within(1e-4f));
        }

        [Test]
        public void LaunchSpeed_HeightInsideTheApexBandUsesApexGravityOnly()
        {
            // 0.1 m is below the 0.25 m the band alone climbs (2² / (2·8)).
            Assert.That(JumpMath.LaunchSpeed(0.1f, Floaty),
                        Is.EqualTo(UnityEngine.Mathf.Sqrt(2f * 8f * 0.1f)).Within(1e-4f));
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void Step_ReachesTheAuthoredApexAtAnyFrameRate(int fps)
        {
            const float height = 2.1f;
            float vy = JumpMath.LaunchSpeed(height, Floaty);
            float y = 0f, peak = 0f, dt = 1f / fps;
            for (int i = 0; i < fps * 3 && vy > -1f; i++)
            {
                y += JumpMath.Step(ref vy, true, Floaty, dt);
                if (y > peak) peak = y;
            }
            // Sampled at frame boundaries, so the peak can only be under-read, by
            // at most the band's apex speed over one frame — a millimetre.
            Assert.That(peak, Is.EqualTo(height).Within(0.005f));
            Assert.That(peak, Is.LessThanOrEqualTo(height + 1e-4f));
        }

        [TestCase(30)]
        [TestCase(60)]
        [TestCase(144)]
        public void Step_AirTimeMatchesTheClosedForm(int fps)
        {
            float v0 = JumpMath.LaunchSpeed(1.5f, Floaty);
            float vy = v0, y = 0f, t = 0f, dt = 1f / fps;
            while (t < 5f)
            {
                y += JumpMath.Step(ref vy, true, Floaty, dt);
                t += dt;
                if (y <= 0f && vy < 0f) break;
            }
            Assert.That(t, Is.EqualTo(JumpMath.AirTime(v0, Floaty)).Within(dt + 1e-4f));
        }

        [Test]
        public void Step_NeverFallsFasterThanTheCap()
        {
            float vy = 0f;
            for (int i = 0; i < 600; i++) JumpMath.Step(ref vy, false, Floaty, 1f / 60f);
            Assert.That(vy, Is.EqualTo(-Floaty.MaxFallSpeed).Within(1e-4f));
        }

        [Test]
        public void Step_ClampsAnEntrySpeedAlreadyPastTheCap()
        {
            float vy = -50f;
            float moved = JumpMath.Step(ref vy, false, Floaty, 0.1f);
            Assert.That(vy, Is.EqualTo(-18f).Within(1e-4f));
            Assert.That(moved, Is.EqualTo(-1.8f).Within(1e-4f));
        }

        [Test]
        public void Releasing_Jump_LandsLowerThanHolding()
        {
            float held = Peak(Floaty, releaseAfter: 99f);
            float tapped = Peak(Floaty, releaseAfter: 0.08f);
            Assert.That(tapped, Is.LessThan(held * 0.8f));
        }

        [Test]
        public void ApexBand_GivesMoreHangThanThePlainArcOfTheSameHeight()
        {
            const float h = 1.5f;
            // Same height, same fall: only the rise and the apex differ.
            var noBand = new GravityProfile(20f, 20f, 34f, 0f, 18f);
            Assert.That(JumpMath.TimeToApex(JumpMath.LaunchSpeed(h, Floaty), Floaty),
                        Is.GreaterThan(JumpMath.TimeToApex(JumpMath.LaunchSpeed(h, noBand), noBand)));
        }

        static float Peak(GravityProfile p, float releaseAfter)
        {
            float vy = JumpMath.LaunchSpeed(2f, p), y = 0f, peak = 0f, t = 0f;
            const float dt = 1f / 60f;
            while (vy > 0f || y > 0f)
            {
                y += JumpMath.Step(ref vy, t < releaseAfter, p, dt);
                peak = UnityEngine.Mathf.Max(peak, y);
                t += dt;
                if (t > 5f) break;
            }
            return peak;
        }
    }
}
