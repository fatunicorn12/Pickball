using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Conics
{
    /// <summary>The flyby's aim, its path, and the plane and polyline under them.</summary>
    public class HyperbolaFlybyTests
    {
        const float Q = 2.5f, Reach = 16f;
        static readonly Vector3 Focus = new(1f, 1f, -2f);

        [Test]
        public void OutgoingAsymptoteRay_PassesThroughThePlayer()
        {
            var players = new[] { new Vector3(8f, 0f, 5f), new Vector3(-6f, 0f, -9f), new Vector3(1f, 0f, 10f) };
            foreach (float e in new[] { 1.3f, 1.6f, 2f })
            foreach (int sense in new[] { 1, -1 })
            foreach (Vector3 player in players)
            {
                var conic = new ConicPath(Q, e);
                float angle = HyperbolaFlyby.ExitAngleTowards(conic, new Vector2(player.x - Focus.x, player.z - Focus.z), sense);
                var plane = new ConicPlane(Focus, angle, sense);
                var path = new ConicPolyline();
                HyperbolaFlyby.FillPath(path, conic, plane, Reach);
                HyperbolaTelegraph t = HyperbolaFlyby.Telegraph(conic, plane, path, 0.8f);

                Vector3 w = new Vector3(player.x, Focus.y, player.z) - t.Centre;
                float cross = t.OutgoingDirection.x * w.z - t.OutgoingDirection.z * w.x;
                Assert.That(Mathf.Abs(cross) / w.magnitude, Is.LessThan(1e-3f), $"e={e} sense={sense} p={player}");
                Assert.That(Vector3.Dot(w, t.OutgoingDirection), Is.GreaterThan(0f), "on the ray, not behind it");
            }
        }

        [Test]
        public void ExitAngleThrough_PathPassesThroughAStillPlayer()
        {
            var players = new[] { new Vector3(8f, 0f, 5f), new Vector3(-6f, 0f, -9f), new Vector3(1f, 0f, 10f) };
            foreach (float e in new[] { 1.3f, 1.6f, 2f })
            foreach (int sense in new[] { 1, -1 })
            foreach (Vector3 player in players)
            {
                var conic = new ConicPath(Q, e);
                float angle = HyperbolaFlyby.ExitAngleThrough(conic, new Vector2(player.x - Focus.x, player.z - Focus.z), sense);
                var path = new ConicPolyline();
                HyperbolaFlyby.FillPath(path, conic, new ConicPlane(Focus, angle, sense), Reach);

                // Closest polyline segment to the player, horizontally, on the outgoing half.
                float best = float.MaxValue;
                var target = new Vector2(player.x, player.z);
                for (int i = path.Count / 2; i < path.Count - 1; i++)
                {
                    Vector2 a = new(path[i].x, path[i].z), b = new(path[i + 1].x, path[i + 1].z);
                    Vector2 ab = b - a;
                    float t = Mathf.Clamp01(Vector2.Dot(target - a, ab) / Mathf.Max(1e-8f, ab.sqrMagnitude));
                    best = Mathf.Min(best, (a + ab * t - target).magnitude);
                }
                Assert.That(best, Is.LessThan(0.15f), $"e={e} sense={sense} p={player}");
            }
        }

        [Test]
        public void Path_StaysBetweenTheAsymptotes_FromReachToReach()
        {
            foreach (float e in new[] { 1.1f, 1.5f, 2.5f })
            foreach (int sense in new[] { 1, -1 })
            {
                var conic = new ConicPath(Q, e);
                var plane = new ConicPlane(Focus, 0.7f, sense);
                var path = new ConicPolyline();
                HyperbolaFlyby.FillPath(path, conic, plane, Reach);

                Assert.That(path.Count, Is.EqualTo(path.Capacity));
                Assert.That(Horizontal(path[0] - Focus), Is.EqualTo(Reach).Within(1e-2f));
                Assert.That(Horizontal(path[path.Count - 1] - Focus), Is.EqualTo(Reach).Within(1e-2f));

                float closest = float.MaxValue;
                for (int i = 0; i < path.Count; i++)
                {
                    Assert.That(path[i].y, Is.EqualTo(Focus.y), "flat at the flight height");
                    Vector2 local = plane.ToPerifocal(path[i]);
                    Assert.IsTrue(ConicPathTests.InsideFocusWedge(conic, local), $"e={e} i={i}");
                    float d = Horizontal(path[i] - Focus);
                    Assert.That(d, Is.GreaterThanOrEqualTo(Q - 1e-3f));
                    closest = Mathf.Min(closest, d);
                }
                Assert.That(closest, Is.EqualTo(Q).Within(0.05f), "passes through periapsis");

                // Incoming leg first (θ < 0), outgoing last (θ > 0).
                Assert.That(plane.ToPerifocal(path[0]).y, Is.LessThan(0f));
                Assert.That(plane.ToPerifocal(path[path.Count - 1]).y, Is.GreaterThan(0f));
            }
        }

        [Test]
        public void Telegraph_DescribesTheSamePath()
        {
            var conic = new ConicPath(Q, 1.5f);
            var plane = new ConicPlane(Focus, -1.2f, 1);
            var path = new ConicPolyline();
            HyperbolaFlyby.FillPath(path, conic, plane, Reach);
            HyperbolaTelegraph t = HyperbolaFlyby.Telegraph(conic, plane, path, 0.8f);

            Assert.That(Horizontal(t.Periapsis - Focus), Is.EqualTo(Q).Within(1e-4f));
            Assert.That(t.Entry, Is.EqualTo(path[0]));
            Assert.That(t.Exit, Is.EqualTo(path[path.Count - 1]));
            Assert.That(t.LeadSeconds, Is.EqualTo(0.8f));
            Assert.That(t.Path, Is.SameAs(path));
            // Exit point lies roughly along the outgoing ray from the centre.
            Assert.That(Vector3.Dot((t.Exit - t.Centre).normalized, t.OutgoingDirection), Is.GreaterThan(0.95f));
            Assert.That(Vector3.Dot((t.Entry - t.Centre).normalized, t.IncomingDirection), Is.GreaterThan(0.95f));
        }

        [Test]
        public void Polyline_WalksByArcLength()
        {
            var line = new ConicPolyline(4);
            line.Add(new Vector3(0, 0, 0));
            line.Add(new Vector3(3, 0, 0));
            line.Add(new Vector3(3, 0, 4));
            Assert.That(line.Length, Is.EqualTo(7f).Within(1e-5f));
            Assert.That(line.PointAt(-1f), Is.EqualTo(Vector3.zero));
            Assert.That(Vector3.Distance(line.PointAt(1.5f), new Vector3(1.5f, 0, 0)), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(line.PointAt(5f), new Vector3(3, 0, 2)), Is.LessThan(1e-5f));
            Assert.That(line.PointAt(99f), Is.EqualTo(new Vector3(3, 0, 4)));
            Assert.That(Vector3.Distance(line.DirectionAt(5f), Vector3.forward), Is.LessThan(1e-5f));

            line.Add(Vector3.one);
            Assert.IsFalse(line.Add(Vector3.one), "full buffers refuse, never grow");
        }

        [Test]
        public void Plane_RoundTripsAndRotatesFromXTowardZ()
        {
            foreach (int sense in new[] { 1, -1 })
            {
                var plane = new ConicPlane(Focus, 0.9f, sense);
                var p = new Vector2(3f, -1.5f);
                Assert.That(Vector2.Distance(plane.ToPerifocal(plane.ToWorld(p)), p), Is.LessThan(1e-5f));
            }
            var quarter = new ConicPlane(Vector3.zero, Mathf.PI / 2f, 1);
            Assert.That(Vector3.Distance(quarter.ToWorld(new Vector2(1f, 0f)), new Vector3(0f, 0f, 1f)), Is.LessThan(1e-5f));
            var mirrored = new ConicPlane(Vector3.zero, 0f, -1);
            Assert.That(Vector3.Distance(mirrored.ToWorld(new Vector2(0f, 1f)), new Vector3(0f, 0f, -1f)), Is.LessThan(1e-5f));
        }

        static float Horizontal(Vector3 v) => new Vector2(v.x, v.z).magnitude;
    }
}
