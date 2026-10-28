using NUnit.Framework;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// The aimed spell's arithmetic: blast falloff (max at the centre, min at
    /// the edge, nothing outside), homing steer (capped turn, unit result, no
    /// NaN on degenerate vectors) and the launch direction's two fallbacks
    /// (aim point too close, or behind the hand).
    /// </summary>
    public class OrbMathTests
    {
        const float Tolerance = 1e-4f;
        const float AngleTolerance = 0.01f;   // degrees, for angles well away from 0

        // ------------------------------------------------------------ blast

        [Test]
        public void BlastDamageAt_Centre_IsMax()
        {
            Assert.That(OrbMath.BlastDamageAt(0f, 2.2f, 10f, 4f), Is.EqualTo(10f).Within(Tolerance));
        }

        [Test]
        public void BlastDamageAt_Edge_IsMin()
        {
            Assert.That(OrbMath.BlastDamageAt(2.2f, 2.2f, 10f, 4f), Is.EqualTo(4f).Within(Tolerance));
        }

        [Test]
        public void BlastDamageAt_HalfwayIsBetween()
        {
            Assert.That(OrbMath.BlastDamageAt(1.1f, 2.2f, 10f, 4f), Is.EqualTo(7f).Within(Tolerance));
        }

        [TestCase(2.21f)]
        [TestCase(5f)]
        [TestCase(1000f)]
        public void BlastDamageAt_OutsideRadius_IsZero(float distance)
        {
            Assert.That(OrbMath.BlastDamageAt(distance, 2.2f, 10f, 4f), Is.EqualTo(0f));
        }

        [Test]
        public void BlastDamageAt_NeverIncreasesWithDistance()
        {
            float previous = float.PositiveInfinity;
            for (int i = 0; i <= 300; i++)
            {
                float distance = i * 0.01f;   // 0 to 3 m, across the 2.2 m edge
                float damage = OrbMath.BlastDamageAt(distance, 2.2f, 10f, 4f);
                Assert.That(damage, Is.LessThanOrEqualTo(previous + Tolerance), $"rose at {distance} m");
                previous = damage;
            }
        }

        [Test]
        public void BlastDamageAt_NegativeDistance_TreatedAsCentre()
        {
            Assert.That(OrbMath.BlastDamageAt(-1f, 2.2f, 10f, 4f), Is.EqualTo(10f).Within(Tolerance));
        }

        [Test]
        public void BlastDamageAt_NoRadius_IsZero()
        {
            Assert.That(OrbMath.BlastDamageAt(0f, 0f, 10f, 4f), Is.EqualTo(0f));
        }

        // ------------------------------------------------------------ steer

        [TestCase(5f)]
        [TestCase(25f)]
        [TestCase(89f)]
        public void Steer_NeverTurnsMoreThanMaxDegrees(float maxDegrees)
        {
            var direction = Vector3.forward;
            var toTarget = new Vector3(1f, 0.3f, -0.2f);   // ~100 degrees away
            var steered = OrbMath.Steer(direction, toTarget, maxDegrees);
            Assert.That(Vector3.Angle(direction, steered), Is.LessThanOrEqualTo(maxDegrees + AngleTolerance));
            Assert.That(Vector3.Angle(direction, steered), Is.EqualTo(maxDegrees).Within(AngleTolerance),
                        "a target further away than the cap turns by exactly the cap");
        }

        [Test]
        public void Steer_TurnsTowardTheTarget()
        {
            var toTarget = new Vector3(1f, 0f, 1f);
            var steered = OrbMath.Steer(Vector3.forward, toTarget, 10f);
            Assert.That(Vector3.Angle(steered, toTarget), Is.EqualTo(35f).Within(AngleTolerance));
        }

        [Test]
        public void Steer_WithinTheCap_ReachesTheTargetDirection()
        {
            var toTarget = new Vector3(0.2f, 0f, 1f) * 7f;   // ~11 degrees off, not unit length
            var steered = OrbMath.Steer(Vector3.forward, toTarget, 25f);
            AssertSameDirection(steered, toTarget);
        }

        [Test]
        public void Steer_ReturnsUnitLength()
        {
            var steered = OrbMath.Steer(new Vector3(0f, 0f, 9f), new Vector3(3f, 2f, 1f), 15f);
            Assert.That(steered.magnitude, Is.EqualTo(1f).Within(Tolerance));
            var reached = OrbMath.Steer(new Vector3(0f, 0f, 0.1f), new Vector3(0f, 0f, 5f), 15f);
            Assert.That(reached.magnitude, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void Steer_OppositeTarget_StillTurnsByTheCap()
        {
            var steered = OrbMath.Steer(Vector3.forward, Vector3.back, 20f);
            Assert.That(IsFinite(steered), Is.True);
            Assert.That(steered.magnitude, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(Vector3.Angle(Vector3.forward, steered), Is.EqualTo(20f).Within(AngleTolerance));
        }

        [Test]
        public void Steer_ZeroTarget_KeepsDirection()
        {
            var steered = OrbMath.Steer(new Vector3(0f, 2f, 0f), Vector3.zero, 30f);
            AssertSameDirection(steered, Vector3.up);
            Assert.That(steered.magnitude, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void Steer_ZeroDirection_AdoptsTarget()
        {
            var steered = OrbMath.Steer(Vector3.zero, new Vector3(4f, 0f, 0f), 5f);
            AssertSameDirection(steered, Vector3.right);
            Assert.That(steered.magnitude, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void Steer_BothZero_IsUnitAndFinite()
        {
            var steered = OrbMath.Steer(Vector3.zero, Vector3.zero, 5f);
            Assert.That(IsFinite(steered), Is.True);
            Assert.That(steered.magnitude, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void Steer_ZeroTurnRate_KeepsDirection()
        {
            var steered = OrbMath.Steer(Vector3.forward, Vector3.right, 0f);
            AssertSameDirection(steered, Vector3.forward);
        }

        // ----------------------------------------------------------- launch

        [Test]
        public void LaunchDirection_PointsFromOriginToAimPoint()
        {
            var origin = new Vector3(0.4f, 1.3f, 0f);
            var aim = new Vector3(3f, 2f, 20f);
            var direction = OrbMath.LaunchDirection(origin, aim, Vector3.forward, 1.5f);
            AssertSameDirection(direction, aim - origin);
            Assert.That(direction.magnitude, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void LaunchDirection_AimPointTooClose_FallsBackToCameraForward()
        {
            var cameraForward = new Vector3(0f, -0.2f, 1f);
            var origin = Vector3.zero;
            var aim = new Vector3(1f, 0f, 0.5f);   // ~1.1 m away, under the 1.5 m minimum
            var direction = OrbMath.LaunchDirection(origin, aim, cameraForward, 1.5f);
            AssertSameDirection(direction, cameraForward);
            Assert.That(direction.magnitude, Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void LaunchDirection_AimPointBehindOrigin_FallsBackToCameraForward()
        {
            // Crosshair on something between the camera and the player.
            var origin = new Vector3(0f, 1.3f, 0f);
            var aim = new Vector3(0.5f, 1.5f, -3f);
            var direction = OrbMath.LaunchDirection(origin, aim, Vector3.forward, 1.5f);
            AssertSameDirection(direction, Vector3.forward);
        }

        [Test]
        public void LaunchDirection_ZeroCameraForward_StillUnit()
        {
            var direction = OrbMath.LaunchDirection(Vector3.zero, Vector3.zero, Vector3.zero, 1.5f);
            Assert.That(IsFinite(direction), Is.True);
            Assert.That(direction.magnitude, Is.EqualTo(1f).Within(Tolerance));
        }

        /// <summary>Compares unit vectors rather than taking Vector3.Angle, whose
        /// acos loses precision near 0 (a float-exact match can read as 0.02°).</summary>
        static void AssertSameDirection(Vector3 actual, Vector3 expected)
        {
            Assert.That((actual.normalized - expected.normalized).magnitude, Is.LessThan(1e-4f),
                        $"expected direction {expected.normalized}, got {actual.normalized}");
        }

        static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

        // ------------------------------------------- target launch (QA F-9)

        [Test]
        public void TargetLaunch_PointBlank_FliesAtTheTargetNotTheFloor()
        {
            var hand = new Vector3(0.3f, 1.3f, 0.45f);
            var body = new Vector3(0.1f, 0.8f, 1.5f);           // 1.2 m away: inside minAimDistance
            var cameraForward = new Vector3(0f, -0.6f, 0.8f);   // looking down at it
            Vector3 dir = OrbMath.TargetLaunchDirection(hand, body, cameraForward);
            Assert.That(Vector3.Angle(dir, body - hand), Is.LessThan(0.01f));
            Assert.That(dir.magnitude, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(OrbMath.LaunchDirection(hand, body, cameraForward, 1.5f),
                        Is.EqualTo(cameraForward.normalized), "the old rule: camera forward, into the floor");
        }

        [Test]
        public void TargetLaunch_TargetOnTheHand_FallsBackToTheView()
        {
            var hand = new Vector3(1f, 2f, 3f);
            Assert.That(OrbMath.TargetLaunchDirection(hand, hand, Vector3.right), Is.EqualTo(Vector3.right));
            Assert.That(OrbMath.TargetLaunchDirection(hand, hand, Vector3.zero), Is.EqualTo(Vector3.forward));
        }
    }
}
