using NUnit.Framework;
using Pickleball.Cutscene.Timeline;
using UnityEngine;

namespace Pickleball.Tests.Cutscene.Pipeline
{
    /// <summary>Blocking maths: marker-relative poses, face targets and eased interpolation.</summary>
    public class ActorMarkMathTests
    {
        static void AssertVec(Vector3 actual, Vector3 expected, string message = null) =>
            Assert.That(Vector3.Distance(actual, expected), Is.LessThan(1e-4f), $"{message} {actual} vs {expected}");

        [Test]
        public void OffsetIsInTheFramesYawedAxes()
        {
            // A marker facing +X (MC_Handover): forward is +X, right is -Z.
            var pose = ActorMarkMath.Resolve(new Vector3(-11f, 0f, -91f), Quaternion.Euler(0f, 90f, 0f),
                new Vector3(0f, 0f, 1.04f), 0f, false, default);
            AssertVec(pose.position, new Vector3(-11f + 1.04f, 0f, -91f));
            Assert.That(ActorMarkMath.YawOf(pose.rotation), Is.EqualTo(90f).Within(1e-3f));

            pose = ActorMarkMath.Resolve(Vector3.zero, Quaternion.Euler(0f, 90f, 0f), new Vector3(1f, 0.5f, 0f), 0f, false, default);
            AssertVec(pose.position, new Vector3(0f, 0.5f, -1f));
        }

        [Test]
        public void TiltedMarkersOnlyContributeYaw()
        {
            var pose = ActorMarkMath.Resolve(Vector3.zero, Quaternion.Euler(40f, 0f, 20f), new Vector3(0f, 0f, 2f), 0f, false, default);
            AssertVec(pose.position, new Vector3(0f, 0f, 2f));
            AssertVec(pose.rotation * Vector3.up, Vector3.up, "upright");
        }

        [Test]
        public void FaceTargetSetsYawOnly()
        {
            var pose = ActorMarkMath.Resolve(Vector3.zero, Quaternion.identity, Vector3.zero, 45f, true, new Vector3(-3f, 10f, 0f));
            Assert.That(ActorMarkMath.YawOf(pose.rotation), Is.EqualTo(-90f).Within(1e-3f));
            AssertVec(pose.rotation * Vector3.up, Vector3.up);
        }

        [Test]
        public void FacingYourOwnPositionKeepsTheFrameYaw()
        {
            var pose = ActorMarkMath.Resolve(Vector3.zero, Quaternion.Euler(0f, 30f, 0f), Vector3.zero, 0f, true, Vector3.zero);
            Assert.That(ActorMarkMath.YawOf(pose.rotation), Is.EqualTo(30f).Within(1e-3f));
        }

        [TestCase(MarkEase.Linear, 0.5f, 0.5f)]
        [TestCase(MarkEase.EaseInOut, 0.5f, 0.5f)]
        [TestCase(MarkEase.EaseInOut, 0.25f, 0.15625f)]
        [TestCase(MarkEase.EaseIn, 0.5f, 0.25f)]
        [TestCase(MarkEase.EaseOut, 0.5f, 0.75f)]
        public void Eases(MarkEase ease, float t, float expected)
        {
            Assert.That(ActorMarkMath.Ease(ease, t), Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void InterpolationHitsBothEndsAndClamps()
        {
            var a = new Pose(Vector3.zero, Quaternion.identity);
            var b = new Pose(new Vector3(0f, 0f, 10f), Quaternion.Euler(0f, 90f, 0f));
            foreach (MarkEase ease in System.Enum.GetValues(typeof(MarkEase)))
            {
                AssertVec(ActorMarkMath.Interpolate(a, b, 0f, ease).position, a.position, ease + " start");
                AssertVec(ActorMarkMath.Interpolate(a, b, 1f, ease).position, b.position, ease + " end");
                AssertVec(ActorMarkMath.Interpolate(a, b, 2f, ease).position, b.position, ease + " clamped");
                AssertVec(ActorMarkMath.Interpolate(a, b, -1f, ease).position, a.position, ease + " clamped low");
            }
            var mid = ActorMarkMath.Interpolate(a, b, 0.5f, MarkEase.Linear);
            AssertVec(mid.position, new Vector3(0f, 0f, 5f));
            Assert.That(ActorMarkMath.YawOf(mid.rotation), Is.EqualTo(45f).Within(1e-3f));
        }

        [Test]
        public void WalkAtConstantSpeedIsLinear()
        {
            var a = new Pose(new Vector3(0f, 1f, -84f), Quaternion.identity);
            var b = new Pose(new Vector3(-3f, 8f, -60f), Quaternion.identity);
            float length = Vector3.Distance(a.position, b.position);
            for (int i = 1; i < 10; i++)
            {
                var p = ActorMarkMath.Interpolate(a, b, i / 10f, MarkEase.Linear);
                Assert.That(Vector3.Distance(a.position, p.position), Is.EqualTo(length * i / 10f).Within(1e-3f));
            }
        }
    }
}
