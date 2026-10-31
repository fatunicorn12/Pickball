using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// Procedural props for the cutscene sets: chunky chibi-scaled columns, blocks, the Greek-key
    /// trim band, low-poly olive trees, terracotta pots, rock blobs, the lawn, the stone path,
    /// and the void's dome and disc. Colour is painted into the vertex colours as soft
    /// gradients (the painterly material multiplies by them), never as fine noise.
    /// Deterministic: the same seed builds the same mesh every run.
    /// </summary>
    public static class PropMeshes
    {
        /// <summary>Cream marble.</summary>
        public static readonly Color Marble = new(0.95f, 0.91f, 0.81f);
        /// <summary>Weathered marble, for the propylon (so its cream trim reads against it).</summary>
        public static readonly Color Sandstone = new(0.84f, 0.74f, 0.6f);
        /// <summary>Terracotta.</summary>
        public static readonly Color Terracotta = new(0.8f, 0.42f, 0.27f);

        // ── Blocks ───────────────────────────────────────────────────────────
        /// <summary>A flat-shaded box standing on its base (y 0..size.y), colour shading from
        /// <paramref name="bottom"/> to <paramref name="top"/>.</summary>
        public static MeshBuffer Box(Vector3 size, Color bottom, Color top)
        {
            var m = new MeshBuffer();
            float x = size.x * 0.5f, z = size.z * 0.5f, y = size.y;
            Vector3 p000 = new(-x, 0, -z), p100 = new(x, 0, -z), p101 = new(x, 0, z), p001 = new(-x, 0, z);
            Vector3 p010 = new(-x, y, -z), p110 = new(x, y, -z), p111 = new(x, y, z), p011 = new(-x, y, z);
            m.Quad(p000, p100, p110, p010, Vector3.back, bottom, top);
            m.Quad(p100, p101, p111, p110, Vector3.right, bottom, top);
            m.Quad(p101, p001, p011, p111, Vector3.forward, bottom, top);
            m.Quad(p001, p000, p010, p011, Vector3.left, bottom, top);
            m.Quad(p010, p110, p111, p011, Vector3.up, top, top);
            m.Quad(p000, p100, p101, p001, Vector3.down, bottom, bottom);
            return m;
        }

        /// <summary>
        /// The Greek-key trim round the top edge of a footprint: four bands
        /// <paramref name="height"/> tall just below the top, standing <paramref name="proud"/>
        /// out from the faces. UV u runs in key repeats along the band, v 0..1 up it.
        /// </summary>
        public static MeshBuffer TrimBand(Vector2 footprint, float height, float proud, float repeatLength)
        {
            var m = new MeshBuffer();
            float x = footprint.x * 0.5f + proud, z = footprint.y * 0.5f + proud;
            Vector3[] corners = { new(-x, 0, -z), new(x, 0, -z), new(x, 0, z), new(-x, 0, z) };
            Vector3[] normals = { Vector3.back, Vector3.right, Vector3.forward, Vector3.left };
            for (int s = 0; s < 4; s++)
            {
                Vector3 a = corners[s], b = corners[(s + 1) % 4];
                float u = (b - a).magnitude / repeatLength;
                Vector3 n = normals[s];
                int i0 = m.Add(a + Vector3.down * height, n, Color.white, new Vector2(0f, 0f));
                int i1 = m.Add(b + Vector3.down * height, n, Color.white, new Vector2(u, 0f));
                int i2 = m.Add(b, n, Color.white, new Vector2(u, 1f));
                int i3 = m.Add(a, n, Color.white, new Vector2(0f, 1f));
                m.AddFacing(i0, i1, i2, n);
                m.AddFacing(i0, i2, i3, n);
            }
            return m;
        }

        // ── Columns ──────────────────────────────────────────────────────────
        /// <summary>
        /// A squat Doric-ish column on a square plinth. <paramref name="brokenHeight"/> 0 builds
        /// it whole (echinus and abacus at <see cref="SetLayout.ColumnHeight"/>); otherwise the
        /// shaft snaps off at that height with a jagged top.
        /// </summary>
        public static MeshBuffer Column(float brokenHeight, int seed)
        {
            var m = new MeshBuffer();
            Color baseShade = Marble * 0.86f, topShade = Marble;
            m.Append(Box(new Vector3(1.2f, 0.3f, 1.2f), baseShade * 0.95f, baseShade), Matrix4x4.identity);
            Ring(m, 0.3f, 0.58f, 0.5f, 0.12f, baseShade, baseShade); // a rounded base step

            bool intact = brokenHeight <= 0f;
            float shaftTop = intact ? SetLayout.ColumnHeight - 0.45f : brokenHeight;
            const int sides = 12;
            float r = SetLayout.ColumnRadius;
            var bottomRing = new int[sides + 1];
            var topRing = new int[sides + 1];
            var topPoints = new Vector3[sides];
            for (int s = 0; s <= sides; s++)
            {
                float a = s * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float jag = intact ? 0f : (SouthIslandTerrain.Hash(seed, s % sides) - 0.5f) * 0.3f;
                float yTop = shaftTop + jag;
                float rTop = Mathf.Lerp(r, r * 0.88f, yTop / SetLayout.ColumnHeight);
                float t = Mathf.Clamp01(yTop / SetLayout.ColumnHeight);
                bottomRing[s] = m.Add(dir * r + Vector3.up * 0.42f, dir, baseShade, new Vector2(s, 0f));
                topRing[s] = m.Add(dir * rTop + Vector3.up * yTop, dir, Color.Lerp(baseShade, topShade, t), new Vector2(s, 1f));
                if (s < sides) topPoints[s] = dir * rTop + Vector3.up * yTop;
            }
            for (int s = 0; s < sides; s++)
            {
                Vector3 n = (m.Normals[bottomRing[s]] + m.Normals[bottomRing[s + 1]]).normalized;
                m.AddFacing(bottomRing[s], bottomRing[s + 1], topRing[s + 1], n);
                m.AddFacing(bottomRing[s], topRing[s + 1], topRing[s], n);
            }

            if (intact)
            {
                // Echinus (a flared cushion) and abacus (a square slab), chunky.
                Ring(m, shaftTop, r * 0.88f, 0.62f, 0.22f, topShade, topShade);
                var abacus = Box(new Vector3(1.32f, 0.23f, 1.32f), topShade * 0.97f, topShade);
                m.Append(abacus, Matrix4x4.Translate(Vector3.up * (shaftTop + 0.22f)));
            }
            else
            {
                // Broken top: a shallow crater, paler where the stone is fresh.
                var fresh = new Color(0.98f, 0.95f, 0.88f);
                int centre = m.Add(Vector3.up * (shaftTop - 0.08f), Vector3.up, fresh, Vector2.zero);
                for (int s = 0; s < sides; s++)
                {
                    int a = m.Add(topPoints[s], Vector3.up, fresh, Vector2.zero);
                    int b = m.Add(topPoints[(s + 1) % sides], Vector3.up, fresh, Vector2.zero);
                    m.AddFacing(centre, a, b, Vector3.up);
                }
            }
            return m;
        }

        // A frustum band from radius r0 at y to r1 at y + h, closed on top.
        private static void Ring(MeshBuffer m, float y, float r0, float r1, float h, Color c0, Color c1)
        {
            const int sides = 12;
            var lo = new int[sides + 1];
            var hi = new int[sides + 1];
            for (int s = 0; s <= sides; s++)
            {
                float a = s * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                var n = (dir * h + Vector3.up * (r0 - r1)).normalized;
                lo[s] = m.Add(dir * r0 + Vector3.up * y, n, c0, Vector2.zero);
                hi[s] = m.Add(dir * r1 + Vector3.up * (y + h), n, c1, Vector2.zero);
            }
            for (int s = 0; s < sides; s++)
            {
                Vector3 n = m.Normals[lo[s]];
                m.AddFacing(lo[s], lo[s + 1], hi[s + 1], n);
                m.AddFacing(lo[s], hi[s + 1], hi[s], n);
            }
            int top = m.Add(Vector3.up * (y + h), Vector3.up, c1, Vector2.zero);
            for (int s = 0; s < sides; s++)
            {
                int a = m.Add(m.Vertices[hi[s]], Vector3.up, c1, Vector2.zero);
                int b = m.Add(m.Vertices[hi[s + 1]], Vector3.up, c1, Vector2.zero);
                m.AddFacing(top, a, b, Vector3.up);
            }
        }

        // ── Lawn and kerb ────────────────────────────────────────────────────
        /// <summary>The shrine's saturated lawn circle, a polar grid with soft colour patches.</summary>
        public static MeshBuffer Lawn(float radius, float y)
        {
            var m = new MeshBuffer();
            const int rings = 6, segments = 48;
            var idx = new int[rings + 1, segments];
            var lawn = new Color(0.33f, 0.72f, 0.22f);
            for (int r = 0; r <= rings; r++)
            for (int s = 0; s < segments; s++)
            {
                float rad = radius * r / rings, a = s * Mathf.PI * 2f / segments;
                var p = new Vector3(Mathf.Sin(a) * rad, y, Mathf.Cos(a) * rad);
                float patch = SouthIslandTerrain.Noise(p.x * 0.35f + 3f, p.z * 0.35f) - 0.5f;
                var c = new Color(lawn.r * (1f + patch * 0.25f), lawn.g * (1f + patch * 0.12f), lawn.b * (1f + patch * 0.2f));
                idx[r, s] = m.Add(p, Vector3.up, c, new Vector2(p.x, p.z));
            }
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                m.AddFacing(idx[r, s], idx[r + 1, s], idx[r + 1, s1], Vector3.up);
                m.AddFacing(idx[r, s], idx[r + 1, s1], idx[r, s1], Vector3.up);
            }
            return m;
        }

        /// <summary>The marble kerb ring round the lawn: an annulus with inner and outer faces.</summary>
        public static MeshBuffer Kerb(float inner, float outer, float height)
        {
            var m = new MeshBuffer();
            const int segments = 48;
            for (int s = 0; s < segments; s++)
            {
                float a0 = s * Mathf.PI * 2f / segments, a1 = (s + 1) * Mathf.PI * 2f / segments;
                var d0 = new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0));
                var d1 = new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1));
                var mid = (d0 + d1).normalized;
                float shade = 0.92f + 0.08f * SouthIslandTerrain.Hash(s, 5);
                Color top = Marble * shade, side = Marble * (shade * 0.88f);
                Vector3 up = Vector3.up * height;
                m.Quad(d0 * inner + up, d1 * inner + up, d1 * outer + up, d0 * outer + up, Vector3.up, top, top);
                m.Quad(d0 * outer + Vector3.down * 0.1f, d1 * outer + Vector3.down * 0.1f, d1 * outer + up, d0 * outer + up, mid, side * 0.9f, side);
                m.Quad(d0 * inner + Vector3.down * 0.1f, d1 * inner + Vector3.down * 0.1f, d1 * inner + up, d0 * inner + up, -mid, side * 0.9f, side);
            }
            return m;
        }

        // ── Olive trees ──────────────────────────────────────────────────────
        /// <summary>
        /// A low-poly olive tree: a twisted, leaning trunk forking into two or three branches, each
        /// carrying a silver-green canopy blob. <paramref name="trunkHeight"/> sets where the fork
        /// is (foreground trees fork high so their canopy stays out of a low side-on frame).
        /// Local origin at the trunk's foot.
        /// </summary>
        public static MeshBuffer OliveTree(int seed, float trunkHeight)
        {
            var m = new MeshBuffer();
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            var bark = new Color(0.47f, 0.41f, 0.34f);
            var barkDark = new Color(0.33f, 0.29f, 0.25f);
            var trunk = new List<Vector3> { Vector3.zero };
            var lean = new Vector3(R(-0.25f, 0.25f), 0f, R(-0.25f, 0.25f));
            const int trunkSegments = 4;
            for (int k = 1; k <= trunkSegments; k++)
            {
                float t = k / (float)trunkSegments;
                var wobble = new Vector3(R(-0.12f, 0.12f), 0f, R(-0.12f, 0.12f));
                trunk.Add(new Vector3(0f, trunkHeight * t, 0f) + lean * t + wobble);
            }
            Tube(m, trunk, 0.27f, 0.17f, bark, barkDark, seed);

            Vector3 fork = trunk[trunk.Count - 1];
            int branches = 2 + rng.Next(2);
            float baseAngle = R(0f, 360f);
            var tips = new List<Vector3>();
            for (int b = 0; b < branches; b++)
            {
                float a = (baseAngle + b * 360f / branches + R(-25f, 25f)) * Mathf.Deg2Rad;
                var outward = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                float reach = R(0.7f, 1.1f), rise = R(0.5f, 0.9f);
                var mid = fork + outward * reach * 0.5f + Vector3.up * rise * 0.6f;
                var tip = fork + outward * reach + Vector3.up * rise;
                Tube(m, new List<Vector3> { fork - Vector3.up * 0.15f, mid, tip }, 0.15f, 0.07f, bark, barkDark, seed + b);
                tips.Add(tip);
            }

            // Silver-green: cool enough that the warm sun doesn't turn it khaki.
            var leafTop = new Color(0.6f, 0.74f, 0.58f);
            var leafLow = new Color(0.3f, 0.43f, 0.33f);
            foreach (var tip in tips)
                Blob(m, tip + Vector3.up * 0.35f, new Vector3(R(1.0f, 1.35f), R(0.7f, 0.9f), R(1.0f, 1.35f)), leafLow, leafTop, rng.Next());
            Blob(m, fork + Vector3.up * R(1.1f, 1.4f), new Vector3(R(1.1f, 1.4f), R(0.75f, 0.95f), R(1.1f, 1.4f)), leafLow, leafTop, rng.Next());
            return m;
        }

        // A 6-sided tube along a polyline, tapering from r0 to r1.
        private static void Tube(MeshBuffer m, List<Vector3> points, float r0, float r1, Color c0, Color c1, int seed)
        {
            const int sides = 6;
            var rings = new int[points.Count, sides + 1];
            for (int k = 0; k < points.Count; k++)
            {
                Vector3 forward = k < points.Count - 1 ? points[k + 1] - points[k] : points[k] - points[k - 1];
                forward.Normalize();
                Vector3 side = Vector3.Cross(forward, Mathf.Abs(forward.y) > 0.9f ? Vector3.right : Vector3.up).normalized;
                Vector3 up = Vector3.Cross(side, forward).normalized;
                float t = k / (float)(points.Count - 1);
                float radius = Mathf.Lerp(r0, r1, t) * (1f + 0.15f * (SouthIslandTerrain.Hash(seed, k) - 0.5f));
                for (int s = 0; s <= sides; s++)
                {
                    float a = s * Mathf.PI * 2f / sides + k * 0.6f;   // twist
                    Vector3 n = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    Color c = Color.Lerp(c0, c1, 0.5f + 0.5f * Mathf.Sin(a * 2f + seed));
                    rings[k, s] = m.Add(points[k] + n * radius, n, c, new Vector2(s, k));
                }
            }
            for (int k = 0; k < points.Count - 1; k++)
            for (int s = 0; s < sides; s++)
            {
                Vector3 n = (m.Normals[rings[k, s]] + m.Normals[rings[k, s + 1]]).normalized;
                m.AddFacing(rings[k, s], rings[k, s + 1], rings[k + 1, s + 1], n);
                m.AddFacing(rings[k, s], rings[k + 1, s + 1], rings[k + 1, s], n);
            }
        }

        /// <summary>A soft low-poly blob (icosphere, jittered), coloured dark below to light above.</summary>
        public static void Blob(MeshBuffer m, Vector3 centre, Vector3 scale, Color low, Color high, int seed)
        {
            Icosphere(out var verts, out var tris);
            var idx = new int[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i] * (1f + 0.12f * (SouthIslandTerrain.Hash(seed, i) - 0.5f));
                Vector3 p = centre + Vector3.Scale(v, scale);
                Vector3 n = Vector3.Scale(verts[i], new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z)).normalized;
                idx[i] = m.Add(p, n, Color.Lerp(low, high, verts[i].y * 0.5f + 0.5f), Vector2.zero);
            }
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 n = (verts[tris[t]] + verts[tris[t + 1]] + verts[tris[t + 2]]).normalized;
                m.AddFacing(idx[tris[t]], idx[tris[t + 1]], idx[tris[t + 2]], n);
            }
        }

        /// <summary>A flat-shaded rock lump (an icosphere, jittered harder), for the island's underside.</summary>
        public static MeshBuffer Rock(Vector3 scale, int seed)
        {
            var m = new MeshBuffer();
            Icosphere(out var verts, out var tris);
            var jittered = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
                jittered[i] = Vector3.Scale(verts[i] * (1f + 0.3f * (SouthIslandTerrain.Hash(seed, i) - 0.5f)), scale);
            var light = new Color(0.6f, 0.53f, 0.45f);
            var dark = new Color(0.36f, 0.32f, 0.3f);
            for (int t = 0; t < tris.Count; t += 3)
            {
                Vector3 a = jittered[tris[t]], b = jittered[tris[t + 1]], c = jittered[tris[t + 2]];
                Vector3 n = Vector3.Cross(b - a, c - a).normalized;
                if (Vector3.Dot(n, a + b + c) < 0f) n = -n;
                Color col = Color.Lerp(dark, light, (verts[tris[t]].y + 1f) * 0.5f);
                int i0 = m.Add(a, n, col, Vector2.zero), i1 = m.Add(b, n, col, Vector2.zero), i2 = m.Add(c, n, col, Vector2.zero);
                m.AddFacing(i0, i1, i2, n);
            }
            return m;
        }

        // ── Terracotta pot ───────────────────────────────────────────────────
        /// <summary>A squat terracotta amphora (a lathe of a chunky profile), origin at its foot.</summary>
        public static MeshBuffer Pot(float scale)
        {
            var m = new MeshBuffer();
            Vector2[] profile =
            {
                new(0.0f, 0f), new(0.16f, 0f), new(0.22f, 0.08f), new(0.34f, 0.32f), new(0.36f, 0.5f),
                new(0.28f, 0.7f), new(0.15f, 0.82f), new(0.14f, 0.92f), new(0.2f, 0.98f), new(0.0f, 0.98f),
            };
            const int sides = 12;
            var ring = new int[profile.Length, sides + 1];
            for (int p = 0; p < profile.Length; p++)
            for (int s = 0; s <= sides; s++)
            {
                float a = s * Mathf.PI * 2f / sides;
                var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                Vector2 prev = profile[Mathf.Max(p - 1, 0)], next = profile[Mathf.Min(p + 1, profile.Length - 1)];
                Vector2 tangent = (next - prev).normalized;
                var n = (dir * tangent.y - Vector3.up * tangent.x).normalized;
                Color c = Color.Lerp(Terracotta * 0.8f, Terracotta * 1.08f, profile[p].y);
                ring[p, s] = m.Add((dir * profile[p].x + Vector3.up * profile[p].y) * scale, n, c, Vector2.zero);
            }
            for (int p = 0; p < profile.Length - 1; p++)
            for (int s = 0; s < sides; s++)
            {
                Vector3 n = (m.Normals[ring[p, s]] + m.Normals[ring[p + 1, s]]).normalized;
                if (n.sqrMagnitude < 0.5f) n = p == 0 ? Vector3.down : Vector3.up;
                m.AddFacing(ring[p, s], ring[p, s + 1], ring[p + 1, s + 1], n);
                m.AddFacing(ring[p, s], ring[p + 1, s + 1], ring[p + 1, s], n);
            }
            return m;
        }

        // ── Path ─────────────────────────────────────────────────────────────
        /// <summary>
        /// A stone path ribbon draped on the island along a polyline whose corners are rounded over
        /// <paramref name="cornerRadius"/>. Flagstone rows alternate warm tones; the edges are a
        /// shade darker. Sits 4 cm above the ground (no collider: the ground carries the player).
        /// Only the part with z in [zMin, zMax) is built.
        /// </summary>
        public static MeshBuffer PathRibbon(Vector2[] points, float width, float cornerRadius, float zMin, float zMax)
        {
            var m = new MeshBuffer();
            List<Vector2> line = SmoothPolyline(points, cornerRadius, 0.35f);
            var stone = new Color(0.86f, 0.76f, 0.6f);
            int[] prev = null;
            float travelled = 0f;
            for (int k = 0; k < line.Count; k++)
            {
                if (k > 0) travelled += (line[k] - line[k - 1]).magnitude;
                Vector2 c = line[k];
                if (c.y < zMin - 0.4f || c.y >= zMax + 0.4f) { prev = null; continue; }
                Vector2 t = (k < line.Count - 1 ? line[k + 1] - c : c - line[k - 1]).normalized;
                var right = new Vector2(t.y, -t.x);
                int row = Mathf.FloorToInt(travelled / 0.6f);
                float tone = 0.9f + 0.18f * SouthIslandTerrain.Hash(row, 3);
                var cur = new int[3];
                for (int col = 0; col < 3; col++)
                {
                    float offset = (col - 1) * width * 0.5f;
                    Vector2 xz = c + right * offset;
                    float y = SouthIslandTerrain.Height(xz.x, xz.y) + (col == 1 ? 0.05f : 0.035f);
                    Color colour = stone * (col == 1 ? tone : tone * 0.88f);
                    cur[col] = m.Add(new Vector3(xz.x, y, xz.y), SouthIslandTerrain.Normal(xz.x, xz.y), colour, xz * 0.5f);
                }
                if (prev != null)
                {
                    for (int col = 0; col < 2; col++)
                    {
                        m.AddFacing(prev[col], prev[col + 1], cur[col + 1], Vector3.up);
                        m.AddFacing(prev[col], cur[col + 1], cur[col], Vector3.up);
                    }
                }
                prev = cur;
            }
            return m;
        }

        /// <summary>Samples a polyline every <paramref name="step"/> metres, rounding each
        /// interior corner with a quadratic curve over <paramref name="radius"/> metres.</summary>
        public static List<Vector2> SmoothPolyline(Vector2[] points, float radius, float step)
        {
            var samples = new List<Vector2>();
            // Corner-rounded control path: straight runs and quadratic corners.
            var path = new List<Vector2> { points[0] };
            for (int i = 1; i < points.Length - 1; i++)
            {
                Vector2 a = points[i - 1], b = points[i], c = points[i + 1];
                float r = Mathf.Min(radius, (b - a).magnitude * 0.45f, (c - b).magnitude * 0.45f);
                Vector2 p0 = b + (a - b).normalized * r, p2 = b + (c - b).normalized * r;
                for (int k = 0; k <= 8; k++)
                {
                    float t = k / 8f;
                    path.Add((1 - t) * (1 - t) * p0 + 2 * (1 - t) * t * b + t * t * p2);
                }
            }
            path.Add(points[points.Length - 1]);

            samples.Add(path[0]);
            float carry = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 a = path[i - 1], b = path[i];
                float len = (b - a).magnitude;
                float d = step - carry;
                while (d <= len)
                {
                    samples.Add(Vector2.Lerp(a, b, d / len));
                    d += step;
                }
                carry = len - (d - step);
            }
            if ((samples[samples.Count - 1] - path[path.Count - 1]).sqrMagnitude > 1e-4f) samples.Add(path[path.Count - 1]);
            return samples;
        }

        // ── Void ─────────────────────────────────────────────────────────────
        /// <summary>A unit UV sphere (the void dome; its material draws the inside).</summary>
        public static MeshBuffer Sphere(int longitude, int latitude)
        {
            var m = new MeshBuffer();
            var idx = new int[latitude + 1, longitude + 1];
            for (int la = 0; la <= latitude; la++)
            for (int lo = 0; lo <= longitude; lo++)
            {
                float theta = la * Mathf.PI / latitude, phi = lo * Mathf.PI * 2f / longitude;
                var n = new Vector3(Mathf.Sin(theta) * Mathf.Sin(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Cos(phi));
                idx[la, lo] = m.Add(n, n, Color.white, new Vector2(lo / (float)longitude, la / (float)latitude));
            }
            for (int la = 0; la < latitude; la++)
            for (int lo = 0; lo < longitude; lo++)
            {
                Vector3 n = m.Vertices[idx[la, lo]] + m.Vertices[idx[la + 1, lo + 1]];
                if (la > 0) m.AddFacing(idx[la, lo], idx[la, lo + 1], idx[la + 1, lo + 1], n);
                if (la < latitude - 1) m.AddFacing(idx[la, lo], idx[la + 1, lo + 1], idx[la + 1, lo], n);
            }
            return m;
        }

        /// <summary>A unit disc facing up.</summary>
        public static MeshBuffer Disc(int segments)
        {
            var m = new MeshBuffer();
            int centre = m.Add(Vector3.zero, Vector3.up, Color.white, new Vector2(0.5f, 0.5f));
            var rim = new int[segments];
            for (int s = 0; s < segments; s++)
            {
                float a = s * Mathf.PI * 2f / segments;
                var p = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                rim[s] = m.Add(p, Vector3.up, Color.white, new Vector2(p.x * 0.5f + 0.5f, p.z * 0.5f + 0.5f));
            }
            for (int s = 0; s < segments; s++) m.AddFacing(centre, rim[s], rim[(s + 1) % segments], Vector3.up);
            return m;
        }

        // ── Shared ───────────────────────────────────────────────────────────
        /// <summary>A unit icosphere subdivided once (42 vertices, 80 triangles).</summary>
        public static void Icosphere(out List<Vector3> verts, out List<int> tris)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            verts = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
                new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
                new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;
            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            var cache = new Dictionary<long, int>();
            var vlist = verts;
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) + b : ((long)b << 32) + a;
                if (cache.TryGetValue(key, out int at)) return at;
                vlist.Add(((vlist[a] + vlist[b]) * 0.5f).normalized);
                cache[key] = vlist.Count - 1;
                return vlist.Count - 1;
            }
            tris = new List<int>();
            for (int f = 0; f < faces.Count; f += 3)
            {
                int a = faces[f], b = faces[f + 1], c = faces[f + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                tris.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
        }
    }
}
