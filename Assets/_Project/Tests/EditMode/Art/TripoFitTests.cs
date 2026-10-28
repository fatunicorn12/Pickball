using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Art;
using UnityEngine;

namespace Pickleball.Tests.TripoArt
{
    /// <summary>
    /// The maths the Tripo import fits models with: measuring along the plan's
    /// axis, the scale factor, the pivot offset, triangle counting, and the
    /// texture grade. Pure logic; no assets are read.
    /// </summary>
    public class TripoFitTests
    {
        readonly List<Object> _spawned = new();

        [TearDown]
        public void DestroySpawned()
        {
            foreach (var o in _spawned)
                if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        [TestCase(TripoSizeAxis.Height, 2f)]
        [TestCase(TripoSizeAxis.LargestHorizontal, 3f)]
        [TestCase(TripoSizeAxis.LargestExtent, 3f)]
        public void Measure_ReadsTheAxisThePlanMeans(TripoSizeAxis axis, float expected)
        {
            Assert.That(TripoFit.Measure(new Vector3(1f, 2f, 3f), axis), Is.EqualTo(expected));
        }

        [Test]
        public void Measure_LargestExtent_IsTheTallestSideWhenTallest()
        {
            Assert.That(TripoFit.Measure(new Vector3(0.5f, 4f, 1f), TripoSizeAxis.LargestExtent), Is.EqualTo(4f));
            Assert.That(TripoFit.Measure(new Vector3(0.5f, 4f, 1f), TripoSizeAxis.LargestHorizontal), Is.EqualTo(1f));
        }

        [Test]
        public void ImportScale_ScalesLinearlyFromTheCurrentScale()
        {
            // 1 m tall at scale 1, wanted 1.6 m: scale 1.6. 0.8 m at scale 2, wanted 1.6 m: scale 4.
            Assert.That(TripoFit.ImportScale(1f, 1f, 1.6f), Is.EqualTo(1.6f).Within(1e-6f));
            Assert.That(TripoFit.ImportScale(2f, 0.8f, 1.6f), Is.EqualTo(4f).Within(1e-6f));
        }

        [Test]
        public void ImportScale_IsAFixedPointOnceFitted()
        {
            // The second run measures the target and must return the same scale,
            // or every re-run would rewrite the model's .meta.
            float first = TripoFit.ImportScale(1f, 0.94251f, 0.9f);
            float measuredAfter = 0.94251f * first;
            Assert.That(TripoFit.ImportScale(first, measuredAfter, 0.9f), Is.EqualTo(first));
        }

        [Test]
        public void ImportScale_RefusesADegenerateMeasurement()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => TripoFit.ImportScale(1f, 0f, 1f));
        }

        [TestCase(0.954781234, 5, 0.95478f)]
        [TestCase(1234.567, 3, 1230f)]
        [TestCase(0.000123456, 2, 0.00012f)]
        public void RoundSignificant_KeepsTheLeadingDigits(double value, int digits, float expected)
        {
            Assert.That(TripoFit.RoundSignificant(value, digits), Is.EqualTo(expected).Within(expected * 1e-6f));
        }

        [Test]
        public void PivotOffset_BottomCentre_PutsTheFootprintCentreAtTheOrigin()
        {
            var bounds = new Bounds(new Vector3(0.2f, 0.9f, -0.1f), new Vector3(1f, 1.6f, 0.8f));
            var offset = TripoFit.PivotOffset(bounds, TripoPivot.BottomCentre);
            Assert.That(offset.x, Is.EqualTo(-0.2f).Within(1e-6f));
            Assert.That(offset.y, Is.EqualTo(-0.1f).Within(1e-6f), "bottom (0.9 - 0.8) moves to y = 0");
            Assert.That(offset.z, Is.EqualTo(0.1f).Within(1e-6f));
        }

        [Test]
        public void PivotOffset_Centre_PutsTheBoundsCentreAtTheOrigin()
        {
            var bounds = new Bounds(new Vector3(0.2f, 0.3f, -0.1f), Vector3.one);
            Assert.That(TripoFit.PivotOffset(bounds, TripoPivot.Centre), Is.EqualTo(new Vector3(-0.2f, -0.3f, 0.1f)));
        }

        [TestCase(1.04f, true)]
        [TestCase(0.96f, true)]
        [TestCase(1.06f, false)]
        [TestCase(0.9f, false)]
        public void WithinSize_AcceptsOnlyTheTolerance(float measured, bool expected)
        {
            Assert.That(TripoFit.WithinSize(measured, 1f, 0.05f), Is.EqualTo(expected));
        }

        [Test]
        public void MeshBoundsAndTriangles_FollowTheHierarchyTransforms()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.up, Vector3.right, new Vector3(1f, 1f, 0f) },
                triangles = new[] { 0, 1, 2, 2, 1, 3 },
            };
            mesh.RecalculateBounds();
            _spawned.Add(mesh);

            var root = new GameObject("TripoFitRoot") { hideFlags = HideFlags.DontSave };
            _spawned.Add(root);
            var child = new GameObject("Part", typeof(MeshFilter)) { hideFlags = HideFlags.DontSave };
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = new Vector3(0f, 2f, 0f);
            child.transform.localScale = new Vector3(2f, 3f, 1f);
            child.GetComponent<MeshFilter>().sharedMesh = mesh;

            var b = TripoFit.MeshBounds(root, root.transform.worldToLocalMatrix);
            Assert.That(b.min, Is.EqualTo(new Vector3(0f, 2f, 0f)));
            Assert.That(b.max, Is.EqualTo(new Vector3(2f, 5f, 0f)));
            Assert.That(TripoFit.CountTriangles(root), Is.EqualTo(2));
        }

        [Test]
        public void MeshBounds_WithNoMesh_IsEmpty()
        {
            var root = new GameObject("Empty") { hideFlags = HideFlags.DontSave };
            _spawned.Add(root);
            Assert.That(TripoFit.MeshBounds(root, Matrix4x4.identity).size, Is.EqualTo(Vector3.zero));
            Assert.That(TripoFit.CountTriangles(root), Is.Zero);
        }

        [Test]
        public void Grade_Neutral_EncodesAsNothingAndChangesNothing()
        {
            var neutral = new TripoTextureGrade(1f, 1f);
            Assert.That(neutral.Encode(), Is.Empty);
            Assert.That(TripoTextureGrade.TryDecode("", out _), Is.False);
            var c = new Color(0.8f, 0.6f, 0.4f, 0.5f);
            Assert.That(neutral.Apply(c), Is.EqualTo(c));
        }

        [Test]
        public void Grade_RoundTripsThroughUserData()
        {
            var grade = new TripoTextureGrade(0.15f, 1.25f);
            Assert.That(TripoTextureGrade.TryDecode(grade.Encode(), out var back), Is.True);
            Assert.That(back.Saturation, Is.EqualTo(0.15f));
            Assert.That(back.Gain, Is.EqualTo(1.25f));
            Assert.That(TripoTextureGrade.TryDecode("someone-else's userData", out _), Is.False);
        }

        [Test]
        public void Grade_ZeroSaturation_IsGreyAndGainClamps()
        {
            var grey = new TripoTextureGrade(0f, 1f).Apply(new Color(1f, 0f, 0f));
            Assert.That(grey.r, Is.EqualTo(grey.g).Within(1e-6f));
            Assert.That(grey.g, Is.EqualTo(grey.b).Within(1e-6f));

            var bright = new TripoTextureGrade(1f, 2f).Apply(new Color(0.9f, 0.2f, 0.1f, 1f));
            Assert.That(bright.r, Is.EqualTo(1f), "clamped, not over-range");
            Assert.That(bright.g, Is.EqualTo(0.4f).Within(1e-6f));
        }
    }
}
