using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Hands;
using UnityEngine;

namespace Pickleball.Tests
{
    /// <summary>
    /// Pins down what <see cref="FingerCurl"/> relies on: each joint's curl
    /// axis, expressed in its own rest frame, makes the bends add up about the
    /// one handle axis along the whole finger; and the sign rule bends fingers
    /// and thumb toward the palm on both hands, never backwards.
    /// </summary>
    public class FingerCurlMathTests
    {
        const float AngleTolerance = 0.01f;

        [TestCase(true)]
        [TestCase(false)]
        public void Sign_BendsFingersAndThumbTowardThePalm(bool rightHand)
        {
            Vector3 knuckle = rightHand ? Vector3.right : Vector3.left;
            GripFrameSolver.TryHandAxes(knuckle, Vector3.forward, rightHand, out var axes);
            Quaternion grip = GripFrameSolver.Compose(Vector3.zero, axes, 0f).Rotation;

            // A straight finger points along the knuckles; the thumb along the handle.
            Vector3 fingerAxis = grip * FingerCurlMath.AxisInGrip(thumb: false);
            Vector3 finger = Quaternion.AngleAxis(FingerCurlMath.Sign(rightHand, false) * 60f, fingerAxis)
                             * axes.Knuckle;
            Vector3 thumbAxis = grip * FingerCurlMath.AxisInGrip(thumb: true);
            Vector3 thumb = Quaternion.AngleAxis(FingerCurlMath.Sign(rightHand, true) * 60f, thumbAxis)
                            * axes.Thumb;

            Assert.Greater(Vector3.Dot(finger, axes.Palm), 0.8f, "finger should bend into the palm");
            Assert.Greater(Vector3.Dot(thumb, axes.Palm), 0.8f, "thumb should bend into the palm");
        }

        [Test]
        public void ChainBends_AddUpAboutTheHandleAxis()
        {
            // An unmapped metacarpal, then three phalanges, each with an
            // arbitrary rest rotation, as a real rig has.
            var metacarpal = Quaternion.Euler(4f, -12f, 7f);
            var rest = new[]
            {
                Quaternion.Euler(10f, 20f, -5f),
                Quaternion.Euler(-3f, 8f, 15f),
                Quaternion.Euler(6f, -9f, 2f),
            };
            var angles = new[] { -75f, -95f, -55f };
            Vector3 axisInHand = new Vector3(0.1f, -0.2f, 0.97f).normalized;

            var path = new List<Quaternion> { metacarpal };
            Quaternion posed = metacarpal;      // the metacarpal stays at rest
            float total = 0f;
            for (int i = 0; i < rest.Length; i++)
            {
                path.Add(rest[i]);
                Vector3 local = FingerCurlMath.AxisInJoint(FingerCurlMath.RestInHand(path), axisInHand);
                posed *= FingerCurlMath.Curled(rest[i], local, angles[i]);
                total += angles[i];
            }

            Quaternion restTip = FingerCurlMath.RestInHand(path);
            Quaternion expected = Quaternion.AngleAxis(total, axisInHand) * restTip;
            Assert.Less(Quaternion.Angle(expected, posed), AngleTolerance);
        }

        [Test]
        public void ZeroCurl_IsTheRestPose()
        {
            var rest = Quaternion.Euler(12f, -30f, 44f);
            Assert.Less(Quaternion.Angle(rest, FingerCurlMath.Curled(rest, Vector3.up, 0f)), AngleTolerance);
        }

        [Test]
        public void RestInHand_ComposesHandChildFirst()
        {
            var a = Quaternion.Euler(0f, 90f, 0f);
            var b = Quaternion.Euler(90f, 0f, 0f);
            Assert.Less(Quaternion.Angle(a * b, FingerCurlMath.RestInHand(new List<Quaternion> { a, b })),
                        AngleTolerance);
            Assert.Less(Quaternion.Angle(Quaternion.identity, FingerCurlMath.RestInHand(new List<Quaternion>())),
                        AngleTolerance);
        }
    }
}
