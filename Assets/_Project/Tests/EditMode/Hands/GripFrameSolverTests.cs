using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Hands;
using UnityEngine;

namespace Pickleball.Tests
{
    /// <summary>
    /// Pins down the grab point convention every held item relies on: +Y out of
    /// the thumb side, +Z along the knuckles, the palm found by handedness, and
    /// the grab point at the centre of the hand's volume (no fingers) or in
    /// front of the palm behind the curled fingers (with fingers).
    ///
    /// Uses a right hand in the chibi's bind pose: knuckles along +X (the
    /// character's right), thumb toward +Z (forward), palm down.
    /// </summary>
    public class GripFrameSolverTests
    {
        const float Tolerance = 1e-4f;

        static void AssertVector(Vector3 expected, Vector3 actual, float tolerance = Tolerance)
        {
            Assert.That((expected - actual).magnitude, Is.LessThan(tolerance),
                        $"expected {expected:F5}, got {actual:F5}");
        }

        // ------------------------------------------------------------- axes

        [Test]
        public void HandAxes_RightHandTPose_PalmFacesDown()
        {
            // Thumb hint leans along the knuckles too; it must be made perpendicular.
            Assert.IsTrue(GripFrameSolver.TryHandAxes(Vector3.right * 0.17f, new Vector3(0.3f, 0f, 1f),
                                                      rightHand: true, out var axes));
            AssertVector(Vector3.right, axes.Knuckle);
            AssertVector(Vector3.forward, axes.Thumb);
            AssertVector(Vector3.down, axes.Palm);
        }

        [Test]
        public void HandAxes_LeftHandIsTheMirror_PalmStillFacesDown()
        {
            Assert.IsTrue(GripFrameSolver.TryHandAxes(Vector3.left, Vector3.forward, rightHand: false, out var axes));
            AssertVector(Vector3.left, axes.Knuckle);
            AssertVector(Vector3.forward, axes.Thumb);
            AssertVector(Vector3.down, axes.Palm);
        }

        [Test]
        public void HandAxes_AreOrthonormal_ForAnyInput()
        {
            Assert.IsTrue(GripFrameSolver.TryHandAxes(new Vector3(0.3f, 0.9f, -0.2f), new Vector3(0.5f, -0.1f, 0.8f),
                                                      rightHand: true, out var a));
            Assert.AreEqual(1f, a.Knuckle.magnitude, Tolerance);
            Assert.AreEqual(1f, a.Thumb.magnitude, Tolerance);
            Assert.AreEqual(1f, a.Palm.magnitude, Tolerance);
            Assert.AreEqual(0f, Vector3.Dot(a.Knuckle, a.Thumb), Tolerance);
            Assert.AreEqual(0f, Vector3.Dot(a.Knuckle, a.Palm), Tolerance);
            Assert.AreEqual(0f, Vector3.Dot(a.Thumb, a.Palm), Tolerance);
        }

        [Test]
        public void HandAxes_RejectsZeroAndParallelDirections()
        {
            Assert.IsFalse(GripFrameSolver.TryHandAxes(Vector3.zero, Vector3.forward, true, out _));
            Assert.IsFalse(GripFrameSolver.TryHandAxes(Vector3.right, Vector3.zero, true, out _));
            Assert.IsFalse(GripFrameSolver.TryHandAxes(Vector3.right, Vector3.right * 2f, true, out _));
            Assert.IsFalse(GripFrameSolver.TryHandAxes(Vector3.right, -Vector3.right, true, out _));
        }

        // ---------------------------------------------------------- compose

        [Test]
        public void Compose_HandleAlongThumb_EdgeAlongKnuckles()
        {
            GripFrameSolver.TryHandAxes(Vector3.right, Vector3.forward, true, out var axes);
            var frame = GripFrameSolver.Compose(new Vector3(0.01f, 0.02f, 0.03f), axes, 0f);

            AssertVector(new Vector3(0.01f, 0.02f, 0.03f), frame.Position);
            AssertVector(axes.Thumb, frame.HandleAxis);
            AssertVector(axes.Knuckle, frame.KnuckleAxis);
        }

        [Test]
        public void Compose_XIsBackOfRightHand_AndPalmOfLeftHand()
        {
            GripFrameSolver.TryHandAxes(Vector3.right, Vector3.forward, true, out var right);
            GripFrameSolver.TryHandAxes(Vector3.left, Vector3.forward, false, out var left);

            Vector3 rightX = GripFrameSolver.Compose(Vector3.zero, right, 0f).Rotation * Vector3.right;
            Vector3 leftX = GripFrameSolver.Compose(Vector3.zero, left, 0f).Rotation * Vector3.right;

            AssertVector(-right.Palm, rightX);
            AssertVector(left.Palm, leftX);
        }

        [TestCase(20f)]
        [TestCase(-15f)]
        public void Compose_TiltLeansTheHandleTowardTheKnuckles(float tilt)
        {
            GripFrameSolver.TryHandAxes(Vector3.right, Vector3.forward, true, out var axes);
            var frame = GripFrameSolver.Compose(Vector3.zero, axes, tilt);

            Assert.AreEqual(Mathf.Abs(tilt), Vector3.Angle(axes.Thumb, frame.HandleAxis), 1e-2f);
            Assert.AreEqual(Mathf.Sign(tilt), Mathf.Sign(Vector3.Dot(frame.HandleAxis, axes.Knuckle)));
            // The tilt turns about the palm normal: the handle stays across the palm.
            Assert.AreEqual(0f, Vector3.Dot(frame.HandleAxis, axes.Palm), Tolerance);
        }

        // --------------------------------------------------------- centroid

        /// <summary>Corners of a closed box, three per triangle, outward winding.</summary>
        static List<Vector3> Box(Vector3 min, Vector3 max, bool skipBottom = false, bool flip = false)
        {
            var c = new Vector3[8];
            for (int i = 0; i < 8; i++)
                c[i] = new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y,
                                   (i & 4) == 0 ? min.z : max.z);
            int[][] quads =
            {
                new[] { 0, 1, 3, 2 }, // z = min
                new[] { 4, 6, 7, 5 }, // z = max
                new[] { 0, 4, 5, 1 }, // y = min (bottom)
                new[] { 2, 3, 7, 6 }, // y = max
                new[] { 0, 2, 6, 4 }, // x = min
                new[] { 1, 5, 7, 3 }, // x = max
            };
            var corners = new List<Vector3>();
            for (int q = 0; q < quads.Length; q++)
            {
                if (skipBottom && q == 2) continue;
                var f = quads[q];
                foreach (var tri in new[] { new[] { f[0], f[1], f[2] }, new[] { f[0], f[2], f[3] } })
                {
                    if (flip) { corners.Add(c[tri[0]]); corners.Add(c[tri[2]]); corners.Add(c[tri[1]]); }
                    else { corners.Add(c[tri[0]]); corners.Add(c[tri[1]]); corners.Add(c[tri[2]]); }
                }
            }
            return corners;
        }

        [Test]
        public void VolumeCentroid_ClosedBox_IsItsCentre_WhateverTheReferenceAndWinding()
        {
            var min = new Vector3(2f, 3f, 4f);
            var max = new Vector3(4f, 3.5f, 5f);
            var expected = (min + max) * 0.5f;

            Assert.IsTrue(GripFrameSolver.TryVolumeCentroid(Box(min, max), Vector3.zero, out var c1, out float v1));
            Assert.IsTrue(GripFrameSolver.TryVolumeCentroid(Box(min, max, flip: true), new Vector3(-7f, 1f, 9f),
                                                            out var c2, out float v2));
            AssertVector(expected, c1);
            AssertVector(expected, c2);
            Assert.AreEqual(1f, v1, Tolerance);   // 2 x 0.5 x 1
            Assert.AreEqual(1f, v2, Tolerance);
        }

        [Test]
        public void VolumeCentroid_OpenAtTheWrist_MatchesTheClosedShape()
        {
            // A hand cut from a body mesh has no cap at the wrist. With the
            // reference on the wrist plane the missing cap adds nothing.
            var min = new Vector3(-0.04f, 0f, -0.03f);
            var max = new Vector3(0.05f, 0.17f, 0.08f);
            var wrist = new Vector3(0.005f, 0f, 0.025f);

            Assert.IsTrue(GripFrameSolver.TryVolumeCentroid(Box(min, max, skipBottom: true), wrist,
                                                            out var centroid, out _));
            AssertVector((min + max) * 0.5f, centroid);
        }

        [Test]
        public void VolumeCentroid_LeansTowardBulk_NotTowardVertices()
        {
            // A thick palm block and a thin finger block: the volume centre sits
            // in the palm, though the finger block has as many corners.
            var corners = Box(new Vector3(0f, 0f, 0f), new Vector3(0.1f, 0.09f, 0.1f));
            corners.AddRange(Box(new Vector3(0.03f, 0.09f, 0f), new Vector3(0.07f, 0.17f, 0.1f)));

            Assert.IsTrue(GripFrameSolver.TryVolumeCentroid(corners, Vector3.zero, out var centroid, out _));
            Vector3 vertexAverage = GripFrameSolver.Centroid(corners);
            Assert.Less(centroid.y, vertexAverage.y);
            Assert.Less(centroid.y, 0.09f);   // inside the palm block
        }

        [Test]
        public void VolumeCentroid_FlatOrEmpty_Fails()
        {
            Assert.IsFalse(GripFrameSolver.TryVolumeCentroid(new List<Vector3>(), Vector3.zero, out _, out _));
            // A double-sided triangle encloses nothing, whatever the reference.
            var flat = new List<Vector3> { Vector3.zero, Vector3.right, Vector3.up, Vector3.zero, Vector3.up, Vector3.right };
            Assert.IsFalse(GripFrameSolver.TryVolumeCentroid(flat, new Vector3(0.2f, 0.3f, 1f), out _, out _));
        }

        // ------------------------------------------------------------ solve

        [Test]
        public void OpenHand_GrabPointIsTheHandsVolumeCentre_AxesFromArmAndFacing()
        {
            var min = new Vector3(0f, -0.04f, -0.03f);
            var max = new Vector3(0.17f, 0.05f, 0.08f);
            var hand = Box(min, max);

            Assert.IsTrue(GripFrameSolver.TrySolveOpenHand(Vector3.zero, Vector3.right, Vector3.forward, true,
                                                           hand, 0f, out var frame));
            AssertVector((min + max) * 0.5f, frame.Position);
            AssertVector(Vector3.forward, frame.HandleAxis);
            AssertVector(Vector3.right, frame.KnuckleAxis);
        }

        [Test]
        public void OpenHand_NoSkin_Fails()
        {
            Assert.IsFalse(GripFrameSolver.TrySolveOpenHand(Vector3.zero, Vector3.right, Vector3.forward, true,
                                                            new List<Vector3>(), 0f, out _));
        }

        [Test]
        public void FistCentre_InFrontOfThePalm_BehindTheCurledFingers()
        {
            GripFrameSolver.TryHandAxes(Vector3.right, Vector3.forward, true, out var axes);
            var knuckles = new Vector3(0.1f, 0f, 0.02f);

            Vector3 centre = GripFrameSolver.FistCentre(knuckles, axes, palmHalfThickness: 0.03f,
                                                        fingerHalfThickness: 0.01f, gripRadius: 0.015f);

            // Palm is down (-Y), knuckles along +X.
            AssertVector(new Vector3(0.1f - 0.025f, -0.045f, 0.02f), centre);
        }

        [Test]
        public void ClosedFist_MeasuresPalmAndFingersFromTheSkin()
        {
            // Four knuckles across the hand at x = 0.1, index (thumb side) first.
            var knuckles = new List<Vector3>
            {
                new(0.1f, 0f, 0.06f), new(0.1f, 0f, 0.02f), new(0.1f, 0f, -0.02f), new(0.1f, 0f, -0.06f),
            };
            // Palm surface 3 cm below the knuckle line; fingers 2 cm thick.
            var palm = new List<Vector3> { new(0.05f, -0.03f, 0f), new(0.05f, 0.025f, 0f) };
            var fingers = new List<Vector3> { new(0.13f, -0.01f, 0f), new(0.13f, 0.01f, 0f) };

            Assert.IsTrue(GripFrameSolver.TrySolveClosedFist(Vector3.zero, knuckles, Vector3.back, true,
                                                             palm, fingers, 0.015f, 0f, out var frame));
            // The knuckles decide the thumb side, not the (wrong) hint.
            AssertVector(Vector3.forward, frame.HandleAxis);
            AssertVector(Vector3.right, frame.KnuckleAxis);
            AssertVector(new Vector3(0.1f - (0.01f + 0.015f), -(0.03f + 0.015f), 0f), frame.Position);
        }

        [Test]
        public void ClosedFist_OneKnuckle_FallsBackOnTheThumbHint()
        {
            var knuckles = new List<Vector3> { new(0.1f, 0f, 0f) };
            Assert.IsTrue(GripFrameSolver.TrySolveClosedFist(Vector3.zero, knuckles, Vector3.forward, true,
                                                             null, null, 0f, 0f, out var frame));
            AssertVector(Vector3.forward, frame.HandleAxis);
            AssertVector(new Vector3(0.1f, 0f, 0f), frame.Position);
        }
    }
}
