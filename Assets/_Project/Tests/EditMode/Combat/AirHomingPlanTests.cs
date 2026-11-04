using NUnit.Framework;
using Pickleball.Sandbox.Combat;
using UnityEngine;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// How an air swing carries the player to its target: one impulse and a stop
    /// across, one vertical speed that the hover eases to rest at the target's
    /// height. The tests fly the plan the way the motor does and check where it ends.
    /// </summary>
    public class AirHomingPlanTests
    {
        const float Halt = 25f;
        const float Chest = 0.9f;
        const float Radius = 0.4f;
        const float StopGap = 0.5f;

        static AirHomingSettings Settings(float range = 6f, float maxSpeed = 12f, float stopGap = StopGap,
                                          float maxHeightChange = 3f, float tolerance = 0.35f,
                                          float dropTolerance = 1f)
            => new AirHomingSettings(range, maxSpeed, stopGap, maxHeightChange, tolerance, Chest, dropTolerance);

        static AirHomingPlan Solve(in AirHomingSettings settings, Vector3 target, float arrive = 0.2f,
                                   float halt = Halt, bool mayChangeHeight = true)
            => AirHomingPlan.Solve(settings, Vector3.zero, target, Radius, arrive, halt, mayChangeHeight);

        [Test]
        public void ATargetOutOfRange_IsNotHomedOn()
        {
            Assert.That(Solve(Settings(range: 6f), new Vector3(0f, Chest, 6.5f)).IsValid, Is.False);
        }

        [Test]
        public void RangeIsMeasuredIn3D()
        {
            Assert.That(Solve(Settings(range: 6f), new Vector3(0f, Chest + 7f, 0f)).IsValid, Is.False);
        }

        [Test]
        public void ARangeOfZero_TurnsItOff()
        {
            Assert.That(Solve(Settings(range: 0f), new Vector3(0f, Chest, 0.1f)).IsValid, Is.False);
        }

        [Test]
        public void ItStopsShortOfTheTarget_ByItsRadiusAndTheGap()
        {
            var plan = Solve(Settings(), new Vector3(0f, Chest, 4f));
            float travelled = plan.Velocity.magnitude * plan.TravelSeconds;
            Assert.That(travelled, Is.EqualTo(4f - Radius - StopGap).Within(1e-4f));
            Assert.That(Vector3.Dot(plan.Velocity.normalized, Vector3.forward), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(plan.Velocity.y, Is.Zero);
        }

        [Test]
        public void ItArrivesAsTheBladeGoesLive_WhenThatIsWithinItsTopSpeed()
        {
            // A 1.1 m gap in 0.2 s is 5.5 m/s, under the 12 m/s cap.
            var plan = Solve(Settings(), new Vector3(0f, Chest, 2f), arrive: 0.2f);
            Assert.That(plan.TravelSeconds, Is.EqualTo(0.2f).Within(1e-4f));
            Assert.That(plan.Velocity.magnitude, Is.EqualTo(5.5f).Within(1e-3f));
        }

        [Test]
        public void ItNeverExceedsItsTopSpeed_AndTakesLongerInstead()
        {
            var plan = Solve(Settings(maxSpeed: 12f), new Vector3(0f, Chest, 5.9f), arrive: 0.1f);
            Assert.That(plan.Velocity.magnitude, Is.EqualTo(12f).Within(1e-4f));
            Assert.That(plan.TravelSeconds, Is.EqualTo((5.9f - Radius - StopGap) / 12f).Within(1e-4f));
        }

        [Test]
        public void AlreadyCloseEnough_ItDoesNotMoveAcross_ButStillCountsAsHoming()
        {
            var plan = Solve(Settings(), new Vector3(0f, Chest, 0.5f));
            Assert.That(plan.IsValid, Is.True);
            Assert.That(plan.Velocity, Is.EqualTo(Vector3.zero));
            Assert.That(plan.TravelSeconds, Is.Zero);
        }

        [Test]
        public void ATargetDirectlyOverhead_DoesNotProduceNaN()
        {
            var plan = Solve(Settings(), new Vector3(0f, Chest + 2f, 0f));
            Assert.That(plan.Velocity, Is.EqualTo(Vector3.zero));
            Assert.That(float.IsNaN(plan.VerticalSpeed), Is.False);
        }

        /// <summary>Ease to rest as MovementMotor.AirVertical does under a hover.</summary>
        static float RiseUnderHover(float speed, float halt, float dt)
        {
            float height = 0f;
            while (!Mathf.Approximately(speed, 0f))
            {
                speed = Mathf.MoveTowards(speed, 0f, halt * dt);
                height += speed * dt;
            }
            return height;
        }

        [TestCase(1f / 30)]
        [TestCase(1f / 60)]
        [TestCase(1f / 144)]
        public void TheHoverBringsHimToRestAtTheTargetsHeight_Rising(float dt)
        {
            var plan = Solve(Settings(), new Vector3(0f, Chest + 2f, 1f));
            Assert.That(plan.SetsVertical, Is.True);
            Assert.That(plan.VerticalSpeed, Is.GreaterThan(0f));
            Assert.That(RiseUnderHover(plan.VerticalSpeed, Halt, dt), Is.EqualTo(2f).Within(0.3f));
        }

        [TestCase(1f / 30)]
        [TestCase(1f / 60)]
        [TestCase(1f / 144)]
        public void FarAboveTheTarget_TheHoverBringsHimDown_OnlyToWithinTheTolerance(float dt)
        {
            var plan = Solve(Settings(dropTolerance: 1f), new Vector3(0f, Chest - 2.5f, 1f));
            Assert.That(plan.VerticalSpeed, Is.LessThan(0f));
            Assert.That(RiseUnderHover(plan.VerticalSpeed, Halt, dt), Is.EqualTo(-1.5f).Within(0.3f));
        }

        [Test]
        public void ATargetAlmostLevel_LeavesHisHeightAlone()
        {
            var plan = Solve(Settings(tolerance: 0.35f), new Vector3(0f, Chest + 0.3f, 1f));
            Assert.That(plan.SetsVertical, Is.False);
            Assert.That(plan.VerticalSpeed, Is.Zero);
        }

        [Test]
        public void ATargetWithinBladeReachBelow_DoesNotPullHimDown()
        {
            // The playtest's yo-yo: a metre down, then a metre up, between swings.
            var plan = Solve(Settings(dropTolerance: 1f), new Vector3(0f, Chest - 0.9f, 1f));
            Assert.That(plan.SetsVertical, Is.False);
        }

        [Test]
        public void TheDropIsCapped_LikeTheRise()
        {
            var plan = Solve(Settings(range: 20f, maxHeightChange: 1f, dropTolerance: 1f),
                             new Vector3(0f, Chest - 10f, 1f));
            Assert.That(plan.VerticalSpeed, Is.EqualTo(-Mathf.Sqrt(2f * Halt * 1f)).Within(1e-3f));
        }

        [Test]
        public void TheHeightChangeIsCapped()
        {
            var capped = Solve(Settings(range: 20f, maxHeightChange: 1f), new Vector3(0f, Chest + 10f, 1f));
            Assert.That(capped.VerticalSpeed, Is.EqualTo(Mathf.Sqrt(2f * Halt * 1f)).Within(1e-3f));
        }

        [Test]
        public void ASwingThatManagesItsOwnHeight_IsLeftAlone()
        {
            var plan = Solve(Settings(), new Vector3(0f, Chest + 2f, 3f), mayChangeHeight: false);
            Assert.That(plan.IsValid, Is.True);
            Assert.That(plan.SetsVertical, Is.False);
            Assert.That(plan.TravelSeconds, Is.GreaterThan(0f), "it still closes across");
        }

        [Test]
        public void WithNoHaltRate_HisHeightIsLeftAlone()
        {
            Assert.That(Solve(Settings(), new Vector3(0f, Chest + 2f, 1f), halt: 0f).SetsVertical, Is.False);
        }
    }
}
