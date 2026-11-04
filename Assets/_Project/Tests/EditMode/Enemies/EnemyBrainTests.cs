using NUnit.Framework;
using Pickleball.Sandbox.Enemies;
using UnityEngine;

namespace Pickleball.Tests.Enemies
{
    /// <summary>The basic enemy's rules, as pure functions.</summary>
    public class EnemyBrainTests
    {
        const float Aggro = 14f, Lose = 20f;

        static EnemySenses Senses(bool defeated = false, bool canAct = true, bool hasTarget = true,
                                  bool aware = true, bool inReach = false)
            => new(defeated, canAct, hasTarget, aware, inReach, 5f, Vector3.forward, 1f);

        [TestCase(false, 13.9f, ExpectedResult = true, TestName = "Unaware_InsideAggro_Notices")]
        [TestCase(false, 14.1f, ExpectedResult = false, TestName = "Unaware_JustOutsideAggro_DoesNotNotice")]
        [TestCase(true, 19.9f, ExpectedResult = true, TestName = "Aware_InsideLose_KeepsChasing")]
        [TestCase(true, 20.1f, ExpectedResult = false, TestName = "Aware_OutsideLose_GivesUp")]
        public bool Awareness_HasHysteresis(bool wasAware, float distance)
            => EnemyBrain.IsAware(wasAware, distance, Aggro, Lose);

        [Test]
        public void Awareness_LoseRangeBelowAggro_CountsAsAggro()
        {
            Assert.IsTrue(EnemyBrain.IsAware(true, 9f, 10f, 2f));
            Assert.IsFalse(EnemyBrain.IsAware(true, 11f, 10f, 2f));
        }

        [Test]
        public void Reach_JudgesHeightApartFromHorizontal()
        {
            Assert.IsTrue(EnemyBrain.IsInReach(Vector3.zero, new Vector3(1f, 0f, 1f), 1.7f, 1.4f));
            Assert.IsFalse(EnemyBrain.IsInReach(Vector3.zero, new Vector3(2f, 0f, 0f), 1.7f, 1.4f),
                           "too far away");
            Assert.IsFalse(EnemyBrain.IsInReach(Vector3.zero, new Vector3(0f, 2f, 0f), 1.7f, 1.4f),
                           "jumped over it");
            Assert.IsFalse(EnemyBrain.IsInReach(Vector3.zero, new Vector3(0f, -2f, 0f), 1.7f, 1.4f),
                           "below it");
        }

        [Test]
        public void Decide_NothingInRange_Idles()
        {
            Assert.AreEqual(EnemyState.Idle, EnemyBrain.Decide(Senses(aware: false)));
            Assert.AreEqual(EnemyState.Idle, EnemyBrain.Decide(Senses(hasTarget: false)));
        }

        [Test]
        public void Decide_InRange_ChasesThenAttacks()
        {
            Assert.AreEqual(EnemyState.Chase, EnemyBrain.Decide(Senses()));
            Assert.AreEqual(EnemyState.Attack, EnemyBrain.Decide(Senses(inReach: true)));
        }

        [Test]
        public void Decide_StunBeatsWantingToAttack()
            => Assert.AreEqual(EnemyState.Stunned, EnemyBrain.Decide(Senses(canAct: false, inReach: true)));

        [Test]
        public void Decide_DefeatBeatsEverything()
            => Assert.AreEqual(EnemyState.Dead, EnemyBrain.Decide(Senses(defeated: true, canAct: false)));

        [Test]
        public void Approach_RunsAtFullSpeedWhenFar()
            => Assert.AreEqual(4.5f, EnemyBrain.ApproachSpeed(10f, 1.3f, 4.5f, 1f / 60f), 1e-4f);

        [Test]
        public void Approach_StopsAtStopDistance()
        {
            Assert.AreEqual(0f, EnemyBrain.ApproachSpeed(1.3f, 1.3f, 4.5f, 1f / 60f));
            Assert.AreEqual(0f, EnemyBrain.ApproachSpeed(0.5f, 1.3f, 4.5f, 1f / 60f));
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Approach_NeverOvershootsTheStop(float dt)
        {
            float distance = 1.31f;
            float speed = EnemyBrain.ApproachSpeed(distance, 1.3f, 4.5f, dt);
            Assert.GreaterOrEqual(distance - speed * dt, 1.3f - 1e-4f);
        }
    }
}
