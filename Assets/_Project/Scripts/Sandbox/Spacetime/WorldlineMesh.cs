using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Turns a worldline path into a solid slab: a flat-topped beam swept along
    /// the path, with a rectangular cross-section.
    ///
    /// A slab rather than a tube because the point of the 4D view is to stand on
    /// these. The CharacterController's ground probe rejects anything steeper
    /// than its slope limit, so a round tube is a ridge you slide off, while a
    /// flat top is a walkway. The same shape also does the right thing when the
    /// path steepens. A shallow stretch is a bridge, a steep one is a wall, and
    /// that difference is readable at a glance as the difference between a slow
    /// object and a fast one.
    ///
    /// The slab's sideways axis is kept horizontal (perpendicular to world up and
    /// to the path), so the top stays level across the width however the path
    /// climbs. Where the path runs straight up or down, horizontal is undefined
    /// and the previous sample's side vector is carried forward instead.
    /// </summary>
    public static class WorldlineMesh
    {
        /// <summary>Below this much horizontal component, the tangent counts as
        /// vertical and the side vector is carried rather than recomputed.</summary>
        const float VerticalThreshold = 0.15f;

        /// <summary>Consecutive segments closer than this to opposite directions
        /// (about 135° apart) count as a hairpin, and the sweep is split there.</summary>
        const float ReversalDot = -0.7f;

        // Reused between builds. The builder runs on the main thread only, and
        // all of a 4D flip's worldlines are built in one go, so sharing these
        // saves allocating fresh lists per line on the frame that has the most
        // work to do.
        static readonly List<Vector3> Points = new();
        static readonly List<float> Along = new();
        static readonly List<Vector3> Verts = new();
        static readonly List<Vector2> Uvs = new();
        static readonly List<Color> Colors = new();
        static readonly List<int> Tris = new();

        /// <summary>
        /// Rebuilds <paramref name="mesh"/> as a slab along <paramref name="path"/>.
        /// Returns false and leaves the mesh empty if the path has fewer than two
        /// distinct points, since a single point has no direction to sweep along.
        /// Colour runs from <paramref name="pastColor"/> at the oldest end to
        /// <paramref name="presentColor"/> at the newest, so the arrow of time
        /// along the line is readable without labels.
        /// </summary>
        public static bool Build(IReadOnlyList<Vector3> path, float width, float thickness,
                                 Color pastColor, Color presentColor, Mesh mesh)
        {
            mesh.Clear();

            // Drop consecutive duplicates. A zero-length segment has no tangent,
            // and the evenly-thinned projection can land two picks on one sample.
            Points.Clear();
            for (int i = 0; i < path.Count; i++)
                if (Points.Count == 0 || (path[i] - Points[^1]).sqrMagnitude > 1e-8f)
                    Points.Add(path[i]);
            int n = Points.Count;
            if (n < 2) return false;

            Verts.Clear(); Uvs.Clear(); Colors.Clear(); Tris.Clear();

            // Arc length to every point, over the whole path, so colour and UVs
            // run on unbroken across the splits below.
            Along.Clear();
            Along.Add(0f);
            for (int i = 1; i < n; i++) Along.Add(Along[^1] + Vector3.Distance(Points[i - 1], Points[i]));
            float total = Along[^1];

            // Split the sweep wherever the path doubles back on itself, and build
            // each stretch as its own slab with its own caps. Sweeping straight
            // through a hairpin flips the cross-section within one segment, which
            // pinches that segment to zero thickness and turns half of it inside
            // out, so neither the camera nor the ground probe can see it.
            // Something that falls and bounces straight back up does this, and so
            // does a swinging platform drawn with no time axis.
            int runStart = 0;
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 before = (Points[i] - Points[i - 1]).normalized;
                Vector3 after = (Points[i + 1] - Points[i]).normalized;
                if (Vector3.Dot(before, after) >= ReversalDot) continue;
                BuildRun(runStart, i, width * 0.5f, thickness * 0.5f, total, pastColor, presentColor);
                runStart = i;
            }
            BuildRun(runStart, n - 1, width * 0.5f, thickness * 0.5f, total, pastColor, presentColor);

            mesh.indexFormat = Verts.Count > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(Verts);
            mesh.SetUVs(0, Uvs);
            mesh.SetColors(Colors);
            mesh.SetTriangles(Tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return true;
        }

        /// <summary>Sweeps one unbroken stretch of the path, points
        /// <paramref name="first"/> to <paramref name="last"/> inclusive.</summary>
        static void BuildRun(int first, int last, float halfWidth, float halfThick,
                             float total, Color pastColor, Color presentColor)
        {
            int baseVertex = Verts.Count;

            // Cross-section corners, going round the rectangle in one consistent
            // order: top-left, top-right, bottom-right, bottom-left. Each face of
            // the slab spans corner k to corner k+1, so one winding rule covers
            // all four faces.
            var corners = new Vector3[4];
            var firstCorners = new Vector3[4];
            Vector3 side = SeedSide(first, last);

            for (int i = first; i <= last; i++)
            {
                Vector3 tangent = Tangent(i, first, last);
                side = SideVector(tangent, side);
                Vector3 up = Vector3.Cross(tangent, side);

                Vector3 p = Points[i];
                Vector3 s = side * halfWidth;
                Vector3 u = up * halfThick;
                corners[0] = p - s + u;
                corners[1] = p + s + u;
                corners[2] = p + s - u;
                corners[3] = p - s - u;
                if (i == first) System.Array.Copy(corners, firstCorners, 4);

                float along = total > 0f ? Along[i] / total : 0f;
                Color c = Color.Lerp(pastColor, presentColor, along);

                // Two vertices per face per sample, unshared between faces, so
                // the edges of the beam stay crisp under lighting instead of
                // being smoothed into a pill.
                for (int f = 0; f < 4; f++)
                {
                    Verts.Add(corners[f]);
                    Verts.Add(corners[(f + 1) & 3]);
                    Uvs.Add(new Vector2(along, 0f));
                    Uvs.Add(new Vector2(along, 1f));
                    Colors.Add(c);
                    Colors.Add(c);
                }
            }

            const int PerSample = 8;
            for (int i = 0; i < last - first; i++)
            {
                for (int f = 0; f < 4; f++)
                {
                    int a = baseVertex + i * PerSample + f * 2;   // corner f, this sample
                    int b = a + 1;                                // corner f+1, this sample
                    int c = a + PerSample;                        // corner f, next sample
                    int d = b + PerSample;                        // corner f+1, next sample
                    // Clockwise seen from outside, which is Unity's front face.
                    Tris.Add(a); Tris.Add(c); Tris.Add(d);
                    Tris.Add(a); Tris.Add(d); Tris.Add(b);
                }
            }

            // End caps, so the beam reads as solid when seen end-on and the
            // collider has no open mouth to walk into.
            float startAlong = total > 0f ? Along[first] / total : 0f;
            float endAlong = total > 0f ? Along[last] / total : 1f;
            AddCap(firstCorners, Color.Lerp(pastColor, presentColor, startAlong), facingBack: true);
            AddCap(corners, Color.Lerp(pastColor, presentColor, endAlong), facingBack: false);
        }

        /// <summary>
        /// The side vector a stretch starts from: the first well-defined one along
        /// it. A stretch that begins vertical (something standing still under an
        /// upward time axis, then setting off) would otherwise start from an
        /// arbitrary horizontal and swing 90° to the real one at the first
        /// sideways step, twisting the slab over a single segment.
        /// </summary>
        static Vector3 SeedSide(int first, int last)
        {
            for (int i = first; i <= last; i++)
            {
                Vector3 level = Vector3.Cross(Vector3.up, Tangent(i, first, last));
                if (level.magnitude >= VerticalThreshold) return level.normalized;
            }
            return Vector3.right;
        }

        /// <summary>Direction of travel at sample <paramref name="i"/>: a central
        /// difference in the middle, one-sided at the ends.</summary>
        static Vector3 Tangent(int i, int first, int last)
        {
            Vector3 from = Points[Mathf.Max(i - 1, first)];
            Vector3 to = Points[Mathf.Min(i + 1, last)];
            Vector3 t = to - from;
            // A central difference can cancel at a sharp reversal, which is
            // exactly what an oscillating platform does at each end of its
            // swing. Fall back to the forward segment, which the duplicate pass
            // guarantees is non-zero.
            if (t.sqrMagnitude < 1e-10f)
                t = i < last ? Points[i + 1] - Points[i] : Points[i] - Points[i - 1];
            return t.normalized;
        }

        /// <summary>
        /// The slab's sideways axis at a sample: horizontal and perpendicular to
        /// the path where that is defined, otherwise <paramref name="previous"/>
        /// with its along-path part removed.
        /// </summary>
        static Vector3 SideVector(Vector3 tangent, Vector3 previous)
        {
            Vector3 level = Vector3.Cross(Vector3.up, tangent);
            if (level.magnitude >= VerticalThreshold)
            {
                // Keep the side pointing the same way as before. When the path
                // reverses horizontally (an oscillating platform at the end of
                // its swing), the cross product flips. Followed blindly, that
                // swaps left and right between two samples and twists the slab
                // into a bow-tie. Flipping it back turns the slab upside down
                // instead, which costs nothing: the cross-section is a symmetric
                // box and both of its flat faces are walkable.
                if (Vector3.Dot(level, previous) < 0f) level = -level;
                return level.normalized;
            }

            // Vertical. Carry the last side forward, or pick any horizontal one
            // if this is the very first sample.
            Vector3 carried = previous.sqrMagnitude > 0f ? previous : Vector3.right;
            carried -= Vector3.Dot(carried, tangent) * tangent;
            if (carried.sqrMagnitude < 1e-8f) carried = Vector3.Cross(tangent, Vector3.forward);
            return carried.normalized;
        }

        static void AddCap(Vector3[] corner, Color color, bool facingBack)
        {
            int start = Verts.Count;
            for (int k = 0; k < 4; k++)
            {
                Verts.Add(corner[k]);
                Uvs.Add(new Vector2(facingBack ? 0f : 1f, k < 2 ? 1f : 0f));
                Colors.Add(color);
            }
            // Seen from behind the start, the corners run clockwise. Seen from
            // ahead of the end, the same order runs anticlockwise, so it flips.
            if (facingBack)
            {
                Tris.Add(start); Tris.Add(start + 1); Tris.Add(start + 2);
                Tris.Add(start); Tris.Add(start + 2); Tris.Add(start + 3);
            }
            else
            {
                Tris.Add(start); Tris.Add(start + 2); Tris.Add(start + 1);
                Tris.Add(start); Tris.Add(start + 3); Tris.Add(start + 2);
            }
        }
    }
}
