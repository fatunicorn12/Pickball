using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Conics
{
    /// <summary>The lob lands where its marker is, when its marker says.</summary>
    public class BallisticSolveTests
    {
        [Test]
        public void SolvedArc_LandsExactlyAtTargetAfterFlightTime()
        {
            var from = new Vector3(0f, 3f, 0f);
            var to = new Vector3(5f, 0f, 7f);
            const float T = 1.4f, g = 14f;
            Vector3 v = BallisticSolve.LaunchVelocity(from, to, T, g);

            Assert.That(Vector3.Distance(BallisticSolve.PositionAt(from, v, g, T), to), Is.LessThan(1e-4f));

            // And by stepping (velocity Verlet is exact for constant acceleration),
            // so the closed form is not just agreeing with itself.
            Vector3 x = from, vel = v, a = Vector3.down * g;
            const int steps = 84;
            float dt = T / steps;
            for (int i = 0; i < steps; i++)
            {
                x += vel * dt + 0.5f * dt * dt * a;
                vel += a * dt;
            }
            Assert.That(Vector3.Distance(x, to), Is.LessThan(1e-3f));
        }

        [Test]
        public void SolvedArc_RisesBeforeFalling()
        {
            Vector3 v = BallisticSolve.LaunchVelocity(new Vector3(0, 3, 0), new Vector3(6, 0, 0), 1.4f, 14f);
            Assert.That(v.y, Is.GreaterThan(0f), "a lob, not a spike");
        }

        [Test]
        public void Lead_IsClampedToMaxLead()
        {
            var player = new Vector3(1f, 0f, 2f);
            Vector3 target = BallisticSolve.PredictTarget(player, new Vector3(10f, 0f, 0f), 2f, 1f, 6f);
            Assert.That(Vector3.Distance(target, player + new Vector3(6f, 0f, 0f)), Is.LessThan(1e-5f));
        }

        [Test]
        public void Lead_BelowCap_IsVelocityTimesTimeTimesFraction()
        {
            Vector3 target = BallisticSolve.PredictTarget(Vector3.zero, new Vector3(1f, 0f, 2f), 1.5f, 0.5f, 6f);
            Assert.That(Vector3.Distance(target, new Vector3(0.75f, 0f, 1.5f)), Is.LessThan(1e-5f));
        }

        [Test]
        public void ZeroVelocity_AimsAtThePlayer()
        {
            var player = new Vector3(-3f, 0f, 4f);
            Assert.That(BallisticSolve.PredictTarget(player, Vector3.zero, 1.4f, 1f, 6f), Is.EqualTo(player));
        }

        [Test]
        public void VerticalVelocity_IsNotLed()
        {
            var player = new Vector3(2f, 1f, 2f);
            Assert.That(BallisticSolve.PredictTarget(player, new Vector3(0f, 9f, 0f), 1.4f, 1f, 6f), Is.EqualTo(player));
        }

        [Test]
        public void ClampToDisc_PullsTargetsOntoThePlate()
        {
            Vector3 p = BallisticSolve.ClampToDisc(new Vector3(20f, 1f, 0f), Vector3.zero, 13f);
            Assert.That(Vector3.Distance(p, new Vector3(13f, 1f, 0f)), Is.LessThan(1e-5f));
            var inside = new Vector3(3f, 0f, -4f);
            Assert.That(BallisticSolve.ClampToDisc(inside, Vector3.zero, 13f), Is.EqualTo(inside));
        }

        [Test]
        public void ZeroFlightTime_DoesNotProduceNaN()
        {
            Vector3 v = BallisticSolve.LaunchVelocity(Vector3.zero, Vector3.one, 0f, 14f);
            Assert.IsFalse(float.IsNaN(v.x) || float.IsInfinity(v.x));
        }
    }
}
