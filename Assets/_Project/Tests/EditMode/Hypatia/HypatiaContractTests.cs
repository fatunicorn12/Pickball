using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia;
using UnityEngine;

namespace Pickleball.Tests.Hypatia
{
    /// <summary>The shared pieces every Hypatia attack leans on.</summary>
    public class HypatiaContractTests
    {
        [Test]
        public void ParallelSegments_DistanceIsTheirGap()
        {
            float d2 = HypatiaContact.SegmentSegmentDistanceSq(
                new Vector3(0, 0, 0), new Vector3(10, 0, 0),
                new Vector3(0, 3, 0), new Vector3(10, 3, 0));
            Assert.That(d2, Is.EqualTo(9f).Within(1e-4f));
        }

        [Test]
        public void CrossingSegments_DistanceIsZero()
        {
            float d2 = HypatiaContact.SegmentSegmentDistanceSq(
                new Vector3(-1, 0, 0), new Vector3(1, 0, 0),
                new Vector3(0, -1, 0), new Vector3(0, 1, 0));
            Assert.That(d2, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void PointSegments_AreHandled()
        {
            float d2 = HypatiaContact.SegmentSegmentDistanceSq(
                new Vector3(0, 0, 0), new Vector3(0, 0, 0),
                new Vector3(5, 0, -1), new Vector3(5, 0, 1));
            Assert.That(d2, Is.EqualTo(25f).Within(1e-4f));
        }

        [Test]
        public void FastSphere_ThatPassesThroughCapsuleInOneFrame_StillHits()
        {
            // 30 m in one frame straight through a standing player: no tunnelling.
            bool hit = HypatiaContact.SweptSphereHitsCapsule(
                new Vector3(-15, 1, 0), new Vector3(15, 1, 0), 0.4f,
                new Vector3(0, 0.35f, 0), new Vector3(0, 1.25f, 0), 0.35f);
            Assert.IsTrue(hit);
        }

        [Test]
        public void Sphere_PassingJustOutsideReach_Misses()
        {
            bool hit = HypatiaContact.SweptSphereHitsCapsule(
                new Vector3(-5, 1, 0.8f), new Vector3(5, 1, 0.8f), 0.4f,
                new Vector3(0, 0.35f, 0), new Vector3(0, 1.25f, 0), 0.35f);
            Assert.IsFalse(hit);
        }

        [Test]
        public void VelocityTracker_ConvergesOnConstantVelocity()
        {
            var t = new VelocityTracker(10f);
            var v = new Vector3(3f, 0f, -4f);
            const float dt = 1f / 60f;
            for (int i = 0; i <= 120; i++) t.Sample(v * (i * dt), dt);
            Assert.That((t.Velocity - v).magnitude, Is.LessThan(0.01f));
        }

        [Test]
        public void VelocityTracker_TeleportResetsInsteadOfSpiking()
        {
            var t = new VelocityTracker(10f);
            const float dt = 1f / 60f;
            t.Sample(Vector3.zero, dt);
            t.Sample(new Vector3(0.05f, 0f, 0f), dt);
            t.Sample(new Vector3(100f, 0f, 0f), dt); // respawn
            Assert.That(t.Velocity, Is.EqualTo(Vector3.zero));
        }

        [Test]
        public void Context_ClampsAndDefaultsTheBossClock()
        {
            var noScale = new HypatiaContext(null, null, null, null, null, null, null);
            Assert.That(noScale.BossTimeScale, Is.EqualTo(1f));

            var frozen = new HypatiaContext(null, null, null, null, null, null, () => 0f);
            Assert.That(frozen.BossDelta(0.5f), Is.EqualTo(0f));

            var nan = new HypatiaContext(null, null, null, null, null, null, () => float.NaN);
            Assert.That(nan.BossTimeScale, Is.EqualTo(1f));
        }
    }
}
