using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Hands;
using UnityEngine;

namespace Pickleball.Tests
{
    /// <summary>
    /// Pins down what <see cref="FingerCurl"/> relies on: each joint's curl
    /// axis, expressed in its own rest frame, makes the bends add up about the
    /// one handle axis along the whole finger; the sign rule bends fingers
    /// toward the palm on both hands, never backwards; and the thumb's axes,
    /// built from where the thumb points at rest, bend it into the palm and
    /// swing it across toward the little finger on any rig.
    /// </summary>
    public class FingerCurlMathTests
    {
        const float AngleTolerance = 0.01f;

        static HandAxes Axes(bool rightHand)
        {
            Vector3 knuckle = rightHand ? Vector3.right : Vector3.left;
            GripFrameSolver.TryHandAxes(knuckle, Vector3.forward, rightHand, out var axes);
            return axes;
        }

        /// <summary>The chibi's thumb at rest: mostly along the fingers, some
        /// toward the thumb side and toward the palm.</summary>
        static Vector3 ChibiThumb(in HandAxes a) => (0.81f * a.Knuckle + 0.44f * a.Thumb + 0.44f * a.Palm).normalized;

        [TestCase(true)]
        [TestCase(false)]
        public void FingerSign_BendsFingersTowardThePalm(bool rightHand)
        {
            var axes = Axes(rightHand);
            Quaternion grip = GripFrameSolver.Compose(Vector3.zero, axes, 0f).Rotation;

            // A straight finger points along the knuckles.
            Vector3 fingerAxis = grip * FingerCurlMath.FingerAxisInGrip;
            Vector3 finger = Quaternion.AngleAxis(FingerCurlMath.FingerSign(rightHand) * 60f, fingerAxis)
                             * axes.Knuckle;

            Assert.Greater(Vector3.Dot(finger, axes.Palm), 0.8f, "finger should bend into the palm");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ThumbFlexAxis_ForAThumbAlongTheHandle_IsTheKnuckleAxis(bool rightHand)
        {
            var axes = Axes(rightHand);
            Vector3 axis = FingerCurlMath.ThumbFlexAxis(axes.Thumb, axes.Palm);

            Assert.Greater(Mathf.Abs(Vector3.Dot(axis, axes.Knuckle)), 0.999f);
            Vector3 bent = Quaternion.AngleAxis(60f, axis) * axes.Thumb;
            Assert.Greater(Vector3.Dot(bent, axes.Palm), 0.8f, "thumb should bend into the palm");
        }

        [TestCase(true, 0)]
        [TestCase(false, 0)]
        [TestCase(true, 1)]
        [TestCase(false, 1)]
        [TestCase(true, 2)]
        [TestCase(false, 2)]
        public void ThumbFlexAxis_BendsAnyThumbIntoThePalm_AboutAnAxisAcrossIt(bool rightHand, int shape)
        {
            var a = Axes(rightHand);
            Vector3 thumb = shape switch
            {
                0 => ChibiThumb(a),
                1 => (0.95f * a.Knuckle + 0.3f * a.Thumb).normalized,           // along the fingers, flat
                _ => (0.5f * a.Knuckle + 0.7f * a.Thumb - 0.2f * a.Palm).normalized, // Mixamo-ish, a little raised
            };

            Vector3 axis = FingerCurlMath.ThumbFlexAxis(thumb, a.Palm);
            Assert.AreEqual(0f, Vector3.Dot(axis, a.Palm), 1e-4f, "the bend axis lies in the palm plane");
            Assert.AreEqual(0f, Vector3.Dot(axis, thumb), 1e-4f, "and across the thumb");

            Vector3 bent = Quaternion.AngleAxis(45f, axis) * thumb;
            Assert.Greater(Vector3.Dot(bent, a.Palm), Vector3.Dot(thumb, a.Palm) + 0.3f);
        }

        [Test]
        public void ThumbFlexAxis_ThumbStraightOutOfThePalm_IsZero()
        {
            var a = Axes(true);
            Assert.AreEqual(Vector3.zero, FingerCurlMath.ThumbFlexAxis(a.Palm, a.Palm));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ThumbSwing_CarriesAChibiThumbAcrossToTheLittleFingerSide(bool rightHand)
        {
            var a = Axes(rightHand);
            Vector3 thumb = ChibiThumb(a);
            Vector3 axis = FingerCurlMath.ThumbSwingAxis(thumb, a.Palm, a.Knuckle, a.Thumb);

            Vector3 swung = Quaternion.AngleAxis(80f, axis) * thumb;

            Assert.Less(Vector3.Dot(swung, a.Thumb), -0.3f, "past the index finger, over the fingers");
            Assert.AreEqual(Vector3.Dot(thumb, a.Palm), Vector3.Dot(swung, a.Palm), 1e-4f,
                            "a swing about the palm normal keeps the thumb's depth");
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ThumbSwing_ForAThumbAlongTheHandle_TurnsItTowardTheKnuckles(bool rightHand)
        {
            var a = Axes(rightHand);
            Vector3 axis = FingerCurlMath.ThumbSwingAxis(a.Thumb, a.Palm, a.Knuckle, a.Thumb);
            Vector3 swung = Quaternion.AngleAxis(30f, axis) * a.Thumb;
            Assert.Greater(Vector3.Dot(swung, a.Knuckle), 0.4f);
        }

        [Test]
        public void Opposed_WithNoSwing_IsCurled()
        {
            var rest = Quaternion.Euler(12f, -30f, 44f);
            var flex = new Vector3(0.2f, 0.9f, -0.1f).normalized;
            Assert.Less(Quaternion.Angle(FingerCurlMath.Curled(rest, flex, 35f),
                                         FingerCurlMath.Opposed(rest, Vector3.right, 0f, flex, 35f)),
                        AngleTolerance);
        }

        [Test]
        public void Opposed_BendsTheSwungThumbAboutItsOwnAxis()
        {
            // Bending about the rest axis then swinging = swinging, then bending
            // about the swung axis: the bend stays perpendicular to the thumb.
            var rest = Quaternion.Euler(5f, 70f, -20f);
            var swing = new Vector3(0f, 0f, 1f);
            var flex = new Vector3(1f, 0f, 0f);
            Quaternion opposed = FingerCurlMath.Opposed(rest, swing, 50f, flex, 30f);

            Quaternion swungFirst = Quaternion.AngleAxis(50f, swing);
            Vector3 swungFlex = swungFirst * flex;
            Quaternion expected = rest * Quaternion.AngleAxis(30f, swungFlex) * swungFirst;
            Assert.Less(Quaternion.Angle(expected, opposed), AngleTolerance);
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
