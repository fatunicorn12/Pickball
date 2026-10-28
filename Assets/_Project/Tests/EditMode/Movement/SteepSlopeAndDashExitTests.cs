using NUnit.Framework;
using Pickleball.Sandbox.Movement.Logic;
using UnityEngine;

namespace Pickleball.Tests.Movement
{
    /// <summary>The steep-slope slide (Sunshine slides you off, it does not freeze
    /// you) and the dash-exit handback (R1 movement playtest).</summary>
    public class SteepSlopeAndDashExitTests
    {
        const float Limit = 50f;

        /// <summary>Normal of a ramp rising toward +z at <paramref name="deg"/>.</summary>
        static Vector3 RampNormal(float deg)
        {
            float r = deg * Mathf.Deg2Rad;
            return new Vector3(0f, Mathf.Cos(r), -Mathf.Sin(r));
        }

        [TestCase(0f, false)]
        [TestCase(15f, false)]
        [TestCase(45f, false)]
        [TestCase(50f, false)]
        [TestCase(55f, true)]
        [TestCase(60f, true)]
        [TestCase(75f, true)]
        [TestCase(90f, false)]   // a wall pushes nothing
        public void SlideSurface_OnlyBetweenSlopeLimitAndWall(float deg, bool slides)
            => Assert.That(SteepSlope.IsSlideSurface(RampNormal(deg), Limit), Is.EqualTo(slides));

        [Test]
        public void Slide_PointsDownhill_AwayFromTheRise()
        {
            Vector3 a = SteepSlope.SlideAcceleration(RampNormal(60f), Limit, 30f);
            Assert.That(a.z, Is.LessThan(0f), "ramp climbs toward +z, so downhill is -z");
            Assert.That(a.y, Is.EqualTo(0f));
            Assert.That(a.magnitude, Is.EqualTo(30f).Within(1e-3f));
        }

        [Test]
        public void Slide_ZeroOnWalkableSlope() =>
            Assert.That(SteepSlope.SlideAcceleration(RampNormal(45f), Limit, 30f), Is.EqualTo(Vector3.zero));

        // The slide integrates acceleration over dt: speed after one second must
        // not depend on the frame rate.
        [TestCase(1f / 30)]
        [TestCase(1f / 60)]
        [TestCase(1f / 144)]
        public void Slide_SpeedAfterOneSecond_FrameRateIndependent(float dt)
        {
            Vector3 a = SteepSlope.SlideAcceleration(RampNormal(60f), Limit, 30f);
            Vector3 v = Vector3.zero;
            int n = Mathf.RoundToInt(1f / dt);
            for (int i = 0; i < n; i++) v += a * dt;
            Assert.That(v.magnitude, Is.EqualTo(30f).Within(0.5f));
        }

        [TestCase(true, false, true)]
        [TestCase(false, true, true)]
        [TestCase(true, true, true)]
        [TestCase(false, false, false)]
        public void DashExit_GroundWhenOnOrAtTheFloor_ElseFall(bool grounded, bool floor, bool ground)
            => Assert.That(DashExitRule.ReturnsToGround(grounded, floor), Is.EqualTo(ground));
    }
}
