using System;
using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Tests.LockOn
{
    /// <summary>
    /// The camera's lock-on steering, with no scene. Most tests fly the real steering along a path
    /// past a floating target at three frame rates. The regression: the wanted yaw was the bearing
    /// of a flat vector that shrinks through zero under the target, so it flipped 180° at up to
    /// 662°/s with the player's look disabled.
    /// </summary>
    public class LockOnSteeringTests
    {
        const float Hold = 2.5f, Smooth = 0.2f, Cap = 240f;
        static readonly Vector3 Floater = new(0f, 6f, 0f);

        readonly struct Flight
        {
            public readonly float MaxRate, FinalYaw, TotalYaw;
            public readonly int Reversals;
            public Flight(float maxRate, float finalYaw, float totalYaw, int reversals)
            {
                MaxRate = maxRate;
                FinalYaw = finalYaw;
                TotalYaw = totalYaw;
                Reversals = reversals;
            }
        }

        delegate float Stepper(float yaw, Vector3 toTarget, ref float velocity, float dt);

        static float NewStep(float yaw, Vector3 toTarget, ref float velocity, float dt)
            => LockOnSteering.Turn(yaw, LockOnSteering.HoldYaw(yaw, toTarget, Hold), ref velocity, Smooth, Cap, dt);

        /// <summary>The code this replaced, kept to show the tests would catch it.</summary>
        static float OldStep(float yaw, Vector3 toTarget, ref float velocity, float dt)
        {
            var flat = new Vector3(toTarget.x, 0f, toTarget.z);
            if (flat.sqrMagnitude < 0.25f) return yaw;
            return Mathf.SmoothDampAngle(yaw, Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg, ref velocity, Smooth, Mathf.Infinity, dt);
        }

        static Flight Fly(Func<float, Vector3> player, float seconds, float dt, float startYaw = 0f, Stepper step = null)
        {
            step ??= NewStep;
            float yaw = startYaw, velocity = 0f, maxRate = 0f, total = 0f, lastSign = 0f;
            int reversals = 0;
            int frames = Mathf.RoundToInt(seconds / dt);
            for (int i = 0; i < frames; i++)
            {
                Vector3 to = Floater - player(i * dt);
                float next = step(yaw, to, ref velocity, dt);
                float moved = next - yaw;
                maxRate = Mathf.Max(maxRate, Mathf.Abs(moved) / dt);
                total += Mathf.Abs(moved);
                if (Mathf.Abs(moved) / dt > 3f)   // ignore the last crawl onto the goal
                {
                    float sign = Mathf.Sign(moved);
                    if (lastSign != 0f && sign != lastSign) reversals++;
                    lastSign = sign;
                }
                yaw = next;
            }
            return new Flight(maxRate, yaw, total, reversals);
        }

        /// <summary>Walks north along x = <paramref name="offset"/> for 20 m, then stands still.</summary>
        static Func<float, Vector3> WalkPast(float offset, float speed = 6f)
            => t => new Vector3(offset, 1.35f, Mathf.Min(-10f + speed * t, 10f));

        // ---------------------------------------------------------- flying past it

        [TestCase(0f, 1f / 30f)]
        [TestCase(0f, 1f / 60f)]
        [TestCase(0f, 1f / 144f)]
        [TestCase(0.01f, 1f / 60f)]
        [TestCase(0.4f, 1f / 30f)]
        [TestCase(0.4f, 1f / 60f)]
        [TestCase(0.4f, 1f / 144f)]
        [TestCase(1f, 1f / 60f)]
        [TestCase(2f, 1f / 60f)]
        [TestCase(-0.4f, 1f / 60f)]
        public void PassingUnder_StaysUnderTheCap_WithoutReversing_AndEndsFacingTheTarget(float offset, float dt)
        {
            var path = WalkPast(offset);
            var flight = Fly(path, 6f, dt);

            Assert.That(flight.MaxRate, Is.LessThanOrEqualTo(Cap * 1.001f), "per-frame yaw rate");
            Assert.That(flight.Reversals, Is.Zero, "one clean turn, not a dither");

            Vector3 to = Floater - path(6f);
            float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(flight.FinalYaw, bearing)), Is.LessThan(3f), "swung round once past it");
        }

        [Test]
        public void PassingUnderAtDashSpeed_StaysUnderTheCap()
        {
            var flight = Fly(WalkPast(0.2f, 22f), 3f, 1f / 60f);
            Assert.That(flight.MaxRate, Is.LessThanOrEqualTo(Cap * 1.001f));
            Assert.That(flight.Reversals, Is.Zero);
        }

        [Test]
        public void TheOldFormula_FailsTheSameWalk()
        {
            var flight = Fly(WalkPast(0f), 6f, 1f / 60f, 0f, OldStep);
            Assert.That(flight.MaxRate, Is.GreaterThan(Cap * 2f), "if this stops failing, the tests above prove nothing");
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void TheTurn_TakesAboutTheSameTimeAtAnyFrameRate(float dt)
        {
            // Under the cap the 180 takes at least 0.75 s; bounded above so a stuck camera fails too.
            float yaw = 0f, velocity = 0f, turnedAt = -1f;
            var path = WalkPast(0f);
            for (int i = 0; i < Mathf.RoundToInt(6f / dt); i++)
            {
                yaw = NewStep(yaw, Floater - path(i * dt), ref velocity, dt);
                if (turnedAt < 0f && Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)) < 5f) turnedAt = i * dt;
            }
            // 12.5 m at 6 m/s reaches the hold radius past the target at 2.08 s.
            Assert.That(turnedAt, Is.InRange(2.08f + 0.7f, 2.08f + 1.3f));
        }

        // ------------------------------------------------------------- under it

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void StandingExactlyUnder_HoldsTheYaw(float dt)
        {
            var flight = Fly(_ => new Vector3(0f, 1.35f, 0f), 3f, dt, 37f);
            Assert.That(flight.FinalYaw, Is.EqualTo(37f).Within(0.01f));
            Assert.That(flight.TotalYaw, Is.LessThan(0.05f));
        }

        [Test]
        public void StandingJustOffCentre_TurnsGently()
        {
            var flight = Fly(_ => new Vector3(0.3f, 1.35f, 0f), 3f, 1f / 60f);
            Assert.That(flight.MaxRate, Is.LessThan(60f), "the hold radius outweighs a 0.3 m bearing");
        }

        // ------------------------------------------------------------ following it

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void AFarTarget_IsFollowed(float dt)
        {
            var flight = Fly(_ => new Vector3(0f, 1.35f, -15f), 2.5f, dt, 60f);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(flight.FinalYaw, 0f)), Is.LessThan(1f));
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void CirclingAtRange_TracksTheBearing(float dt)
        {
            const float radius = 6f, speed = 6f;
            Func<float, Vector3> circle = t => new Vector3(Mathf.Sin(t * speed / radius), 0f, -Mathf.Cos(t * speed / radius)) * radius + Vector3.up * 1.35f;
            float yaw = 0f, velocity = 0f, worst = 0f;
            int frames = Mathf.RoundToInt(6f / dt);
            for (int i = 0; i < frames; i++)
            {
                Vector3 to = Floater - circle(i * dt);
                yaw = NewStep(yaw, to, ref velocity, dt);
                if (i * dt > 2f)
                    worst = Mathf.Max(worst, Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg)));
            }
            Assert.That(worst, Is.LessThan(25f));
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void CirclingCloseUnderIt_DoesNotSpin(float dt)
        {
            // Orbiting 0.8 m out at 6 m/s is about 430 deg/s, 6 turns in these 5 s.
            const float radius = 0.8f, speed = 6f;
            var flight = Fly(t => new Vector3(Mathf.Sin(t * speed / radius), 0f, -Mathf.Cos(t * speed / radius)) * radius + Vector3.up * 1.35f,
                             5f, dt);
            Assert.That(flight.MaxRate, Is.LessThan(Cap));
            Assert.That(flight.TotalYaw, Is.LessThan(800f), "a fraction of the player's own 2150 degrees");
        }

        // ------------------------------------------------------------ the pieces

        [Test]
        public void HoldYaw_IsTheBearingWithNoHoldRadius()
        {
            Assert.That(LockOnSteering.HoldYaw(40f, new Vector3(1f, 5f, 1f), 0f), Is.EqualTo(45f).Within(1e-3f));
        }

        [Test]
        public void HoldYaw_WithNothingToGoOn_KeepsTheYaw()
        {
            Assert.That(LockOnSteering.HoldYaw(40f, Vector3.up * 5f, 0f), Is.EqualTo(40f));
        }

        [Test]
        public void Turn_AtZeroDt_ChangesNothing()
        {
            float velocity = 30f;
            float yaw = LockOnSteering.Turn(10f, 170f, ref velocity, Smooth, Cap, 0f);
            Assert.That(yaw, Is.EqualTo(10f));
            Assert.That(velocity, Is.EqualTo(30f));
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Turn_NeverPassesTheCap_EvenCarryingTooMuchSpeed(float dt)
        {
            float velocity = 5000f;
            float yaw = LockOnSteering.Turn(0f, 90f, ref velocity, Smooth, Cap, dt);
            Assert.That(Mathf.Abs(yaw), Is.LessThanOrEqualTo(Cap * dt * 1.0001f));
            Assert.That(Mathf.Abs(velocity), Is.LessThanOrEqualTo(Cap));
        }

        [Test]
        public void Turn_KeepsItsDirection_WhenTheGoalCrossesTheFarSide()
        {
            // Under way clockwise, with the goal just past the opposite side: the short way is
            // back the way it came.
            float velocity = 100f;
            float yaw = LockOnSteering.Turn(0f, -179f, ref velocity, Smooth, Cap, 1f / 60f);
            Assert.That(yaw, Is.GreaterThan(0f));
        }

        [Test]
        public void Turn_FromRest_TakesTheShortWay()
        {
            float velocity = 0f;
            Assert.That(LockOnSteering.Turn(0f, -170f, ref velocity, Smooth, Cap, 1f / 60f), Is.LessThan(0f));
        }

        [Test]
        public void SoftRise_StaysContinuousDirectlyOverhead()
        {
            float near = LockOnSteering.SoftRise(new Vector3(0.01f, 5f, 0f), Hold);
            float over = LockOnSteering.SoftRise(new Vector3(0f, 5f, 0f), Hold);
            Assert.That(over, Is.EqualTo(Mathf.Atan2(5f, Hold) * Mathf.Rad2Deg).Within(1e-3f), "not 90");
            Assert.That(Mathf.Abs(near - over), Is.LessThan(0.01f));
        }

        [Test]
        public void SoftRise_MatchesTheTrueElevationFarAway()
        {
            Assert.That(LockOnSteering.SoftRise(new Vector3(0f, 10f, 30f), Hold),
                        Is.EqualTo(Mathf.Atan2(10f, 30f) * Mathf.Rad2Deg).Within(0.2f));
        }

        [Test]
        public void SoftRise_IsSafeWithNoHoldRadiusAndNoOffset()
        {
            Assert.That(LockOnSteering.SoftRise(Vector3.zero, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void OrbitPitch_RestsForALevelTarget_AndStopsAboveTheFloor()
        {
            Assert.That(LockOnSteering.OrbitPitch(0f, 16f, 0.5f, -8f, 65f), Is.EqualTo(16f));
            Assert.That(LockOnSteering.OrbitPitch(20f, 16f, 0.5f, -8f, 65f), Is.EqualTo(6f));
            Assert.That(LockOnSteering.OrbitPitch(90f, 16f, 0.5f, -8f, 65f), Is.EqualTo(-8f));
            Assert.That(LockOnSteering.OrbitPitch(-200f, 16f, 0.5f, -8f, 65f), Is.EqualTo(65f));
        }

        [Test]
        public void RisePullback_ScalesWithTheRise()
        {
            Assert.That(LockOnSteering.RisePullback(-30f, 1.5f, 60f), Is.EqualTo(0f));
            Assert.That(LockOnSteering.RisePullback(30f, 1.5f, 60f), Is.EqualTo(0.75f).Within(1e-4f));
            Assert.That(LockOnSteering.RisePullback(80f, 1.5f, 60f), Is.EqualTo(1.5f));
        }

        // --------------------------------------------------------------- the tilt

        static readonly float TanHalf = Mathf.Tan(30f * Mathf.Deg2Rad);

        static float ViewY(Vector3 cameraSpace, float tiltDegrees)
        {
            // Tilting the view up by t turns everything down by t about the camera's x axis.
            Vector3 p = Quaternion.AngleAxis(tiltDegrees, Vector3.right) * cameraSpace;
            return 0.5f + 0.5f * p.y / (p.z * TanHalf);
        }

        [Test]
        public void LookTilt_IsZero_WhileTheTargetIsInFrame()
        {
            Assert.That(LockOnSteering.LookTilt(new Vector3(0f, 1f, 10f), new Vector3(0f, 0f, 5f), TanHalf, 0.85f, 0.1f, 50f), Is.EqualTo(0f));
        }

        [Test]
        public void LookTilt_BringsAHighTargetDownToTheFrameTop()
        {
            var aim = new Vector3(0f, 4f, 5f);
            var head = new Vector3(0f, 0f, 5f);
            float tilt = LockOnSteering.LookTilt(aim, head, TanHalf, 0.85f, 0.1f, 50f);
            Assert.That(tilt, Is.GreaterThan(0f));
            Assert.That(ViewY(aim, tilt), Is.EqualTo(0.85f).Within(0.005f));
            Assert.That(ViewY(head, tilt), Is.GreaterThan(0.1f), "the player stays in the frame");
        }

        [Test]
        public void LookTilt_StopsWhereThePlayerWouldLeaveTheFrame()
        {
            var aim = new Vector3(0f, 30f, 3f);
            var head = new Vector3(0f, 0f, 5f);
            float tilt = LockOnSteering.LookTilt(aim, head, TanHalf, 0.85f, 0.1f, 80f);
            Assert.That(ViewY(head, tilt), Is.EqualTo(0.1f).Within(0.005f));
        }

        [Test]
        public void LookTilt_RespectsItsLimit()
        {
            Assert.That(LockOnSteering.LookTilt(new Vector3(0f, 30f, 3f), new Vector3(0f, 0f, 5f), TanHalf, 0.85f, 0f, 10f), Is.EqualTo(10f));
        }

        [Test]
        public void LookTilt_ForATargetBehindTheLens_IsStillBounded()
        {
            float tilt = LockOnSteering.LookTilt(new Vector3(0f, 4f, -3f), new Vector3(0f, 0f, 5f), TanHalf, 0.85f, 0.1f, 50f);
            Assert.That(tilt, Is.InRange(0f, 50f));
            Assert.That(float.IsNaN(tilt), Is.False);
        }

        [Test]
        public void LookTilt_NeverTiltsDownward_ForATargetBelowTheOrbit()
        {
            Assert.That(LockOnSteering.LookTilt(new Vector3(0f, -9f, 5f), new Vector3(0f, 0f, 5f), TanHalf, 0.85f, 0.1f, 50f), Is.EqualTo(0f));
        }

        // --------------------------------------------------------------- the view

        [TestCase(0.5f, 0.5f, 5f, 0.03f, true)]
        [TestCase(0.04f, 0.96f, 5f, 0.03f, true)]
        [TestCase(0.02f, 0.5f, 5f, 0.03f, false)]
        [TestCase(0.5f, 0.98f, 5f, 0.03f, false)]
        [TestCase(1.01f, 0.5f, 5f, 0f, false)]
        [TestCase(-0.01f, 0.5f, 5f, 0f, false)]
        [TestCase(0.99f, 0.5f, 5f, 0f, true)]
        [TestCase(0.5f, 0.5f, -5f, 0f, false)]
        [TestCase(0.5f, 0.5f, 0f, 0f, false)]
        public void InsideViewport(float x, float y, float z, float inset, bool expected)
        {
            Assert.That(LockOnView.InsideViewport(new Vector3(x, y, z), inset), Is.EqualTo(expected));
        }
    }
}
