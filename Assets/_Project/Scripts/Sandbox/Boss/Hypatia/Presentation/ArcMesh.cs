using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Presentation
{
    /// <summary>
    /// The procedural shapes the fight is drawn with, all flat on the plate or
    /// standing on it, and all in the rings' azimuth convention (0° = +Z, growing
    /// toward +X, <see cref="Rings.RingMath.DirectionFromAzimuth"/>). A shape
    /// built round azimuth 0 is turned onto any azimuth by a Y rotation of that
    /// many degrees, which is how the ring and gap views place theirs.
    ///
    /// Each call fills a caller-owned <see cref="Mesh"/> in place through shared
    /// scratch lists, so a rebuild allocates only when it needs more room than
    /// before. Called on events (a new gap width, a band starting to smoulder)
    /// and by Setup Hypatia Fight, never per frame. Main thread only.
    /// </summary>
    public static class ArcMesh
    {
        static List<Vector3> _vertices;
        static List<Color> _colours;
        static List<Vector2> _uvs;
        static List<int> _triangles;

        /// <summary>Point on the plate at <paramref name="azimuthDeg"/> and radius
        /// <paramref name="r"/> (m), at height <paramref name="y"/>.</summary>
        public static Vector3 OnPlate(float azimuthDeg, float r, float y = 0f)
        {
            float a = azimuthDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(a) * r, y, Mathf.Cos(a) * r);
        }

        /// <summary>
        /// A unit-radius, unit-height cylinder wall with a gap of
        /// <paramref name="gapDegrees"/> centred on azimuth 0. Scale it (R, H, R)
        /// and turn it by the gap's azimuth: the angles survive the scale. Vertex
        /// alpha runs from <paramref name="bottomAlpha"/> to <paramref name="topAlpha"/>;
        /// the two gap edges get at least <paramref name="edgeAlpha"/> so the
        /// opening reads sharply.
        /// </summary>
        public static void Wall(Mesh mesh, float gapDegrees, int segments, float bottomAlpha, float topAlpha,
                                float edgeAlpha)
        {
            Begin();
            float half = Mathf.Clamp(gapDegrees, 0f, 359f) * 0.5f;
            segments = Mathf.Max(3, segments);
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float az = Mathf.Lerp(half, 360f - half, t);
                bool edge = i == 0 || i == segments;
                Vector3 p = OnPlate(az, 1f);
                Add(p, new Color(1f, 1f, 1f, edge ? Mathf.Max(edgeAlpha, bottomAlpha) : bottomAlpha), new Vector2(t, 0f));
                Add(new Vector3(p.x, 1f, p.z), new Color(1f, 1f, 1f, edge ? Mathf.Max(edgeAlpha, topAlpha) : topAlpha),
                    new Vector2(t, 1f));
                if (i > 0) Quad((i - 1) * 2, flip: false);
            }
            End(mesh);
        }

        /// <summary>
        /// A flat slice of annulus from <paramref name="inner"/> to
        /// <paramref name="outer"/> m, <paramref name="widthDegrees"/> wide and
        /// centred on azimuth 0, at y = 0. Alpha is <paramref name="centreAlpha"/>
        /// along the middle and <paramref name="edgeAlpha"/> at the two straight
        /// edges, so the exact width reads.
        /// </summary>
        public static void Sector(Mesh mesh, float inner, float outer, float widthDegrees, int segments,
                                  float centreAlpha, float edgeAlpha)
        {
            Begin();
            float half = Mathf.Clamp(widthDegrees, 0.1f, 360f) * 0.5f;
            segments = Mathf.Max(2, segments);
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float az = Mathf.Lerp(-half, half, t);
                float edgeness = Mathf.Abs(t * 2f - 1f);
                var c = new Color(1f, 1f, 1f, Mathf.Lerp(centreAlpha, edgeAlpha, edgeness * edgeness));
                Add(OnPlate(az, inner), c, new Vector2(t, 0f));
                Add(OnPlate(az, outer), c, new Vector2(t, 1f));
                if (i > 0) Quad((i - 1) * 2, flip: false);
            }
            End(mesh);
        }

        /// <summary>A flat full annulus from <paramref name="inner"/> to
        /// <paramref name="outer"/> m at y = 0 with uniform <paramref name="alpha"/>;
        /// u runs round, v runs outward. Faces up.</summary>
        public static void Annulus(Mesh mesh, float inner, float outer, int segments, float alpha = 1f)
        {
            Begin();
            AddBand(Mathf.Max(8, segments), inner, outer, 0f, faceUp: true, alpha);
            End(mesh);
        }

        /// <summary>
        /// A closed solid ring (top, bottom and both walls) from
        /// <paramref name="inner"/> to <paramref name="outer"/> m and from
        /// <paramref name="bottom"/> to <paramref name="top"/> m. The plate, its
        /// lip and the astrolabe's rims. With <paramref name="inner"/> = 0 it is a
        /// solid disc. Lit, so normals are computed.
        /// </summary>
        public static void SolidRing(Mesh mesh, float inner, float outer, float bottom, float top, int segments)
        {
            Begin();
            segments = Mathf.Max(8, segments);
            AddBand(segments, inner, outer, top, faceUp: true, 1f);
            AddWall(segments, outer, bottom, top, outward: true);
            if (inner > 1e-4f) AddWall(segments, inner, bottom, top, outward: false);
            AddBand(segments, inner, outer, bottom, faceUp: false, 1f);
            End(mesh);
        }

        static void AddBand(int segments, float inner, float outer, float y, bool faceUp, float alpha)
        {
            int start = _vertices.Count;
            var c = new Color(1f, 1f, 1f, alpha);
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float az = t * 360f;
                Add(OnPlate(az, inner, y), c, new Vector2(t, 0f));
                Add(OnPlate(az, outer, y), c, new Vector2(t, 1f));
                if (i > 0) Quad(start + (i - 1) * 2, flip: !faceUp);
            }
        }

        static void AddWall(int segments, float radius, float bottom, float top, bool outward)
        {
            int start = _vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float az = t * 360f;
                Add(OnPlate(az, radius, bottom), Color.white, new Vector2(t, 0f));
                Add(OnPlate(az, radius, top), Color.white, new Vector2(t, 1f));
                if (i > 0) Quad(start + (i - 1) * 2, flip: outward);
            }
        }

        // Two triangles over vertices b, b+1 (this column) and b+2, b+3 (next).
        // Azimuth grows clockwise seen from above, so unflipped, a floor piece
        // (b = inner, b+1 = outer) faces up and a wall (b = bottom, b+1 = top)
        // faces the centre; flip for a floor piece facing down or a wall facing out.
        static void Quad(int b, bool flip)
        {
            if (!flip)
            {
                _triangles.Add(b); _triangles.Add(b + 1); _triangles.Add(b + 2);
                _triangles.Add(b + 2); _triangles.Add(b + 1); _triangles.Add(b + 3);
            }
            else
            {
                _triangles.Add(b); _triangles.Add(b + 2); _triangles.Add(b + 1);
                _triangles.Add(b + 2); _triangles.Add(b + 3); _triangles.Add(b + 1);
            }
        }

        static void Add(Vector3 v, Color c, Vector2 uv)
        {
            _vertices.Add(v);
            _colours.Add(c);
            _uvs.Add(uv);
        }

        static void Begin()
        {
            _vertices ??= new List<Vector3>(512);
            _colours ??= new List<Color>(512);
            _uvs ??= new List<Vector2>(512);
            _triangles ??= new List<int>(1536);
            _vertices.Clear();
            _colours.Clear();
            _uvs.Clear();
            _triangles.Clear();
        }

        static void End(Mesh mesh)
        {
            mesh.Clear();
            mesh.SetVertices(_vertices);
            mesh.SetColors(_colours);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }
    }
}
