using System.Collections.Generic;
using Pickleball.Cutscene.Sets;
using UnityEngine;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// Lays out the gold construction strokes that draw the Outer Shrine (S1_SH07) and bakes them
    /// into the line mesh ConstructionLineResolve reveals: the compass's radius line and its
    /// arcs round the lawn and the column ring, two ruled lines up each column shaft and one
    /// across its top, 24 ticks pacing the ring, and one curve along the olive hill's skyline
    /// (up its west flank, along the crest, down the ridge). Positions follow the set layout,
    /// so the lines trace the real geometry.
    /// </summary>
    public static class ConstructionStrokes
    {
        /// <summary>One stroke: its polyline (world), group, order inside the group and half-width.</summary>
        public readonly struct Stroke
        {
            /// <summary>Points along the stroke, in drawing order.</summary>
            public readonly List<Vector3> Points;
            /// <summary>Which window it draws in.</summary>
            public readonly StrokeGroup Group;
            /// <summary>0 = first in its group, 1 = last.</summary>
            public readonly float Order;
            /// <summary>Half the line's width, metres.</summary>
            public readonly float HalfWidth;

            /// <summary>Builds a stroke.</summary>
            public Stroke(List<Vector3> points, StrokeGroup group, float order, float halfWidth)
            {
                Points = points;
                Group = group;
                Order = order;
                HalfWidth = halfWidth;
            }
        }

        /// <summary>Every stroke, in stroke-index order.</summary>
        public static List<Stroke> Build()
        {
            var strokes = new List<Stroke>();
            Vector3 c = SetLayout.ShrineCentre;

            // Arcs: the compass's radius line, concentric circles, eight ruled spokes and four wide
            // compass sweeps across the meadow (review: "no compass arcs sweep the lawn").
            const float arcW = 0.045f;
            strokes.Add(new Stroke(new List<Vector3> { c + Vector3.up * 0.06f, c + new Vector3(0f, 0.16f, SetLayout.LawnRadius + 0.1f) },
                                   StrokeGroup.Arc, 0f, arcW));
            strokes.Add(new Stroke(Circle(c, SetLayout.LawnRadius + 0.1f, SetLayout.KerbHeight + 0.03f), StrokeGroup.Arc, 0.15f, arcW));
            strokes.Add(new Stroke(Circle(c, SetLayout.LawnRadius - 1.6f, 0.06f), StrokeGroup.Arc, 0.25f, arcW));
            strokes.Add(new Stroke(Circle(c, SetLayout.ColumnRingRadius, 0.06f), StrokeGroup.Arc, 0.35f, arcW));
            strokes.Add(new Stroke(Circle(c, SetLayout.ColumnRingRadius + 1.4f, 0.06f), StrokeGroup.Arc, 0.45f, arcW));
            for (int s = 0; s < 8; s++)
            {
                Vector3 d = Dir(22.5f + s * 45f);
                strokes.Add(new Stroke(new List<Vector3> { c + d * 0.4f + Vector3.up * 0.06f, c + d * (SetLayout.ColumnRingRadius + 1.4f) + Vector3.up * 0.06f },
                                       StrokeGroup.Arc, 0.5f + s * 0.04f, arcW * 0.8f));
            }
            float[] sweepRadius = { 9.5f, 11f, 12.5f, 14f };
            float[] sweepFrom = { -70f, 120f, 20f, 200f };
            for (int s = 0; s < sweepRadius.Length; s++)
                strokes.Add(new Stroke(Arc(c, sweepRadius[s], sweepFrom[s], 140f, 0.08f), StrokeGroup.Arc, 0.8f + s * 0.06f, arcW * 1.3f));

            // Columns, clockwise from north: two shaft lines and a ruled line across the top.
            var columns = new List<(float bearing, float height)>();
            foreach (var col in SetLayout.Columns)
                columns.Add((Mathf.Repeat(col.bearing, 360f), col.brokenHeight > 0f ? col.brokenHeight : SetLayout.ColumnHeight));
            columns.Sort((a, b) => a.bearing.CompareTo(b.bearing));
            for (int i = 0; i < columns.Count; i++)
            {
                float order = columns.Count > 1 ? i / (float)(columns.Count - 1) : 0f;
                Vector3 radial = Dir(columns[i].bearing);
                Vector3 side = new(radial.z, 0f, -radial.x);
                Vector3 at = c + radial * SetLayout.ColumnRingRadius;
                float top = columns[i].height;
                float offset = SetLayout.ColumnRadius + 0.05f;
                const float colW = 0.035f;
                strokes.Add(new Stroke(new List<Vector3> { at - side * offset, at - side * offset + Vector3.up * (top + 0.6f) },
                                       StrokeGroup.Column, order, colW));
                strokes.Add(new Stroke(new List<Vector3> { at + side * offset, at + side * offset + Vector3.up * (top + 0.6f) },
                                       StrokeGroup.Column, order, colW));
                strokes.Add(new Stroke(new List<Vector3> { at - side * (offset + 0.35f) + Vector3.up * (top + 0.05f),
                                                           at + side * (offset + 0.35f) + Vector3.up * (top + 0.05f) },
                                       StrokeGroup.Column, order, colW));
            }

            // The propylon: both piers' edges and the three steps' tops, ruled up from the ground.
            BoxEdges(strokes, SetLayout.PierCentre, SetLayout.PierSize, 0f, SetLayout.LedgeTop, 0.55f);
            BoxEdges(strokes, SetLayout.SecondPierCentre, SetLayout.SecondPierSize, 0f, SetLayout.SecondPierHeight, 0.7f);
            for (int s = 0; s < SetLayout.LedgeSteps.Length; s++)
            {
                Vector3 st = SetLayout.LedgeSteps[s];
                BoxTop(strokes, new Vector2(st.x, st.z), SetLayout.StepSize, st.y, 0.8f + s * 0.07f);
            }

            // Ticks pacing round the ring.
            const int ticks = 48;
            for (int k = 0; k < ticks; k++)
            {
                Vector3 radial = Dir(k * 360f / ticks);
                float len = k % 4 == 0 ? 0.6f : 0.35f;
                strokes.Add(new Stroke(new List<Vector3> { c + radial * (SetLayout.ColumnRingRadius - len) + Vector3.up * 0.06f,
                                                           c + radial * (SetLayout.ColumnRingRadius + len) + Vector3.up * 0.06f },
                                       StrokeGroup.Tick, k / (float)(ticks - 1), 0.03f));
            }

            // The olive hill's profile: up the west flank, along the crest, down the ridge.
            var hill = new List<Vector3>();
            for (float z = -84f; z <= -55f; z += 1f) hill.Add(SetLayout.Ground(-16.8f, z) + Vector3.up * 0.25f);
            for (float x = -15.8f; x <= 25.5f; x += 1f) hill.Add(SetLayout.Ground(x, -55f) + Vector3.up * 0.25f);
            strokes.Add(new Stroke(hill, StrokeGroup.Hill, 0f, 0.1f));
            // Contours up the slope and the path's own profile.
            float[] contourX = { -10f, -3f, 4f };
            for (int s = 0; s < contourX.Length; s++)
            {
                var line = new List<Vector3>();
                for (float z = -86f; z <= -55f; z += 1f) line.Add(SetLayout.Ground(contourX[s], z) + Vector3.up * 0.25f);
                strokes.Add(new Stroke(line, StrokeGroup.Hill, 0.3f + s * 0.25f, 0.08f));
            }
            return strokes;
        }

        /// <summary>Bakes the strokes into a camera-facing ribbon mesh (see ConstructionLine.shader).</summary>
        public static MeshBuffer BakeMesh(List<Stroke> strokes)
        {
            var m = new MeshBuffer();
            for (int s = 0; s < strokes.Count; s++)
            {
                List<Vector3> p = strokes[s].Points;
                float total = 0f;
                for (int k = 1; k < p.Count; k++) total += (p[k] - p[k - 1]).magnitude;
                float run = 0f;
                for (int k = 0; k < p.Count - 1; k++)
                {
                    Vector3 a = p[k], b = p[k + 1];
                    float len = (b - a).magnitude;
                    if (len < 1e-5f) continue;
                    Vector3 tangent = (b - a) / len;
                    float u0 = run / total, u1 = (run + len) / total;
                    run += len;
                    int i0 = LineVertex(m, a, tangent, u0, -1f, s, strokes[s].HalfWidth);
                    int i1 = LineVertex(m, a, tangent, u0, 1f, s, strokes[s].HalfWidth);
                    int i2 = LineVertex(m, b, tangent, u1, 1f, s, strokes[s].HalfWidth);
                    int i3 = LineVertex(m, b, tangent, u1, -1f, s, strokes[s].HalfWidth);
                    m.Triangle(i0, i1, i2);
                    m.Triangle(i0, i2, i3);
                }
            }
            return m;
        }

        private static int LineVertex(MeshBuffer m, Vector3 at, Vector3 tangent, float along, float side, int stroke, float halfWidth)
        {
            int i = m.Add(at, tangent, Color.white, new Vector2(along, side));
            m.Uv1[i] = new Vector2(stroke, halfWidth);
            return i;
        }

        private static List<Vector3> Arc(Vector3 centre, float radius, float fromBearing, float sweep, float y)
        {
            var points = new List<Vector3>();
            const int segments = 48;
            for (int k = 0; k <= segments; k++) points.Add(centre + Dir(fromBearing + sweep * k / segments) * radius + Vector3.up * y);
            return points;
        }

        // A box's four vertical edges, then its top and bottom rims, as ruled lines.
        private static void BoxEdges(List<Stroke> strokes, Vector2 centre, Vector2 size, float y0, float y1, float order)
        {
            var corners = Corners(centre, size);
            for (int i = 0; i < 4; i++)
                strokes.Add(new Stroke(new List<Vector3> { corners[i] + Vector3.up * y0, corners[i] + Vector3.up * (y1 + 0.4f) },
                                       StrokeGroup.Column, order, 0.035f));
            BoxTop(strokes, centre, size, y1 + 0.02f, order + 0.05f);
        }

        private static void BoxTop(List<Stroke> strokes, Vector2 centre, Vector2 size, float y, float order)
        {
            var corners = Corners(centre, size);
            var rim = new List<Vector3>();
            for (int i = 0; i <= 4; i++) rim.Add(corners[i % 4] + Vector3.up * y);
            strokes.Add(new Stroke(rim, StrokeGroup.Column, Mathf.Min(1f, order), 0.035f));
        }

        private static Vector3[] Corners(Vector2 centre, Vector2 size)
        {
            float hx = size.x * 0.5f + 0.05f, hz = size.y * 0.5f + 0.05f;
            return new[]
            {
                new Vector3(centre.x - hx, 0f, centre.y - hz), new Vector3(centre.x + hx, 0f, centre.y - hz),
                new Vector3(centre.x + hx, 0f, centre.y + hz), new Vector3(centre.x - hx, 0f, centre.y + hz),
            };
        }

        private static List<Vector3> Circle(Vector3 centre, float radius, float y)
        {
            var points = new List<Vector3>();
            const int segments = 96;
            for (int k = 0; k <= segments; k++) points.Add(centre + Dir(k * 360f / segments) * radius + Vector3.up * y);
            return points;
        }

        // A horizontal unit vector at a compass bearing (degrees east of north).
        private static Vector3 Dir(float bearing)
        {
            float r = bearing * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
        }
    }
}
