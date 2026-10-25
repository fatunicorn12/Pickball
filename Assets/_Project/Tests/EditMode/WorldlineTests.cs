using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox;
using UnityEngine;

namespace Pickleball.Tests
{
    /// <summary>
    /// Pins down what a worldline promises: the past ages out on schedule, the
    /// present is always drawn where the object really is, and any path,
    /// including the awkward vertical and reversing ones, turns into a closed,
    /// finite mesh. Those are the properties a player relies on when stepping
    /// onto one. Everything else (width, colours, the time axis) is tuning.
    /// </summary>
    public class WorldlineTests
    {
        const float Tolerance = 1e-4f;

        [Test]
        public void History_KeepsOnlyTheLastSpanSeconds()
        {
            var h = new WorldlineHistory(span: 2f, minSpacing: 0f, maxInterval: 0.1f);
            for (int i = 0; i <= 50; i++) h.Record(new Vector3(i, 0f, 0f), i * 0.1f);

            h.Prune(now: 5f);

            Assert.That(h.OldestTime, Is.GreaterThanOrEqualTo(3f - Tolerance));
            Assert.That(h.NewestTime, Is.EqualTo(5f).Within(Tolerance));
        }

        [Test]
        public void History_StandingStill_StillRecordsOverTime()
        {
            // Without the time-based sample, a stationary object writes one sample
            // and then ages out to nothing, and its pillar disappears.
            var h = new WorldlineHistory(span: 2f, minSpacing: 0.5f, maxInterval: 0.25f);
            for (int i = 0; i <= 40; i++) h.Record(Vector3.zero, i * 0.05f);

            h.Prune(now: 2f);

            // About one sample per 0.25 s over 2 s. The bound is loose because
            // float timestamps can push a sample to the next step. The point is
            // that it is not one.
            Assert.That(h.Count, Is.GreaterThanOrEqualTo(6));
        }

        [Test]
        public void History_SmallMovesWithinIntervalAreSkipped()
        {
            var h = new WorldlineHistory(span: 10f, minSpacing: 1f, maxInterval: 10f);
            h.Record(Vector3.zero, 0f);

            Assert.That(h.Record(new Vector3(0.5f, 0f, 0f), 0.1f), Is.False);
            Assert.That(h.Record(new Vector3(1.5f, 0f, 0f), 0.2f), Is.True);
        }

        [Test]
        public void Project_PresentStaysPut_PastShiftsAgainstTimeAxis()
        {
            var h = new WorldlineHistory(span: 10f, minSpacing: 0f, maxInterval: 0.1f);
            h.Record(new Vector3(0f, 0f, 0f), 0f);
            h.Record(new Vector3(1f, 0f, 0f), 2f);
            var path = new List<Vector3>();

            h.Project(now: 2f, timeAxis: new Vector3(0f, 0.5f, 0f), maxPoints: 0, path);

            Assert.That(path.Count, Is.EqualTo(2));
            // Two seconds old, at half a metre per second: one metre down.
            AssertNear(new Vector3(0f, -1f, 0f), path[0]);
            AssertNear(new Vector3(1f, 0f, 0f), path[1]);
        }

        [Test]
        public void Project_Thinning_KeepsBothEnds()
        {
            var h = new WorldlineHistory(span: 100f, minSpacing: 0f, maxInterval: 0.01f);
            for (int i = 0; i < 500; i++) h.Record(new Vector3(i, 0f, 0f), i * 0.1f);
            var path = new List<Vector3>();

            h.Project(now: 49.9f, timeAxis: Vector3.zero, maxPoints: 32, path);

            Assert.That(path.Count, Is.EqualTo(32));
            AssertNear(new Vector3(0f, 0f, 0f), path[0]);
            AssertNear(new Vector3(499f, 0f, 0f), path[^1]);
        }

        [Test]
        public void Mesh_SinglePoint_BuildsNothing()
        {
            var mesh = new Mesh();
            bool built = WorldlineMesh.Build(new[] { Vector3.zero, Vector3.zero }, 1f, 0.2f,
                                             Color.black, Color.white, mesh);
            Assert.That(built, Is.False);
            Assert.That(mesh.vertexCount, Is.Zero);
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Mesh_StraightPath_TopIsLevelAndAsWideAsAsked()
        {
            var mesh = new Mesh();
            var path = new[] { Vector3.zero, new Vector3(0f, 0f, 5f) };

            Assert.That(WorldlineMesh.Build(path, 2f, 0.4f, Color.black, Color.white, mesh), Is.True);

            Bounds b = mesh.bounds;
            Assert.That(b.size.x, Is.EqualTo(2f).Within(Tolerance));
            Assert.That(b.size.y, Is.EqualTo(0.4f).Within(Tolerance));
            Assert.That(b.size.z, Is.EqualTo(5f).Within(Tolerance));
            Object.DestroyImmediate(mesh);
        }

        // The cases most likely to break a sweep: straight up, a hard reversal,
        // and a helix (the orbiting platform under an upward time axis).
        static IEnumerable<Vector3[]> AwkwardPaths()
        {
            yield return new[] { Vector3.zero, Vector3.up, Vector3.up * 2f, Vector3.up * 3f };
            yield return new[] { Vector3.zero, Vector3.right, Vector3.zero, Vector3.right };
            var helix = new Vector3[60];
            for (int i = 0; i < helix.Length; i++)
            {
                float a = i * 0.3f;
                helix[i] = new Vector3(Mathf.Cos(a) * 3f, i * 0.05f, Mathf.Sin(a) * 3f);
            }
            yield return helix;
        }

        [TestCaseSource(nameof(AwkwardPaths))]
        public void Mesh_AwkwardPaths_AreFiniteAndIndexed(Vector3[] path)
        {
            var mesh = new Mesh();

            Assert.That(WorldlineMesh.Build(path, 1f, 0.2f, Color.black, Color.white, mesh), Is.True);

            foreach (var v in mesh.vertices)
                Assert.That(float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z),
                            $"non-finite vertex {v}");
            int[] tris = mesh.triangles;
            Assert.That(tris.Length % 3, Is.Zero);
            foreach (int t in tris) Assert.That(t, Is.InRange(0, mesh.vertexCount - 1));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Mesh_VerticalStart_TakesItsWidthFromTheFirstSidewaysStep()
        {
            // Something standing still under an upward time axis, then setting off
            // along +x. The rising stretch has no horizontal direction of its own,
            // so its width has to come from where the path goes next, not from an
            // arbitrary default. Otherwise the slab twists 90° at the corner.
            var mesh = new Mesh();
            var path = new[]
            {
                Vector3.zero, Vector3.up, Vector3.up * 2f,
                new Vector3(3f, 2f, 0f), new Vector3(6f, 2f, 0f),
            };
            WorldlineMesh.Build(path, 1f, 0.2f, Color.black, Color.white, mesh);

            // Vertices 0 and 1 are the first sample's top-left and top-right.
            // Moving along +x, the width runs along z.
            Vector3 across = mesh.vertices[1] - mesh.vertices[0];
            Assert.That(Mathf.Abs(across.z), Is.EqualTo(1f).Within(Tolerance));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Mesh_Hairpin_IsSplitIntoSeparateCappedRuns()
        {
            // Out and straight back. Swept as one run, the segment at the turn
            // would pinch to zero thickness.
            var mesh = new Mesh();
            var path = new[] { Vector3.zero, new Vector3(2f, 0f, 0f), new Vector3(0f, 0f, 0f) };
            WorldlineMesh.Build(path, 1f, 0.2f, Color.black, Color.white, mesh);

            // Two runs of two samples each: 4 faces × 2 triangles per segment,
            // plus 2 caps × 2 triangles per run.
            Assert.That(mesh.triangles.Length / 3, Is.EqualTo(2 * (8 + 4)));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void Mesh_FacesPointOutward()
        {
            // Every triangle's normal should point away from the line it was swept
            // along. A flipped winding renders inside-out and leaves the top face
            // invisible from above.
            var mesh = new Mesh();
            var path = new[] { Vector3.zero, new Vector3(0f, 0f, 2f), new Vector3(0f, 0f, 4f) };
            WorldlineMesh.Build(path, 1f, 0.2f, Color.black, Color.white, mesh);

            Vector3[] v = mesh.vertices;
            int[] t = mesh.triangles;
            for (int i = 0; i < t.Length; i += 3)
            {
                Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                Vector3 normal = Vector3.Cross(b - a, c - a);
                Vector3 centre = (a + b + c) / 3f;
                // Nearest point on the line, kept back from its ends so the end
                // caps have an outward direction too (along the line, away from it).
                Vector3 axisPoint = new(0f, 0f, Mathf.Clamp(centre.z, 0.5f, 3.5f));
                Vector3 outward = centre - axisPoint;
                Assert.That(Vector3.Dot(normal, outward), Is.GreaterThan(0f),
                            $"triangle {i / 3} faces inward");
            }
            Object.DestroyImmediate(mesh);
        }

        static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.That(Vector3.Distance(expected, actual), Is.LessThan(Tolerance),
                        $"expected {expected}, got {actual}");
        }
    }
}
