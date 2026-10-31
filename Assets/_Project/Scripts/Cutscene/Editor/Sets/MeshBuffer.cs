using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pickleball.Cutscene.Editor.Sets
{
    /// <summary>
    /// Collects vertices, normals, vertex colours, UVs and triangles for one generated set mesh,
    /// then writes them into a Mesh. Editor-only build helper: allocation is fine here.
    /// </summary>
    public sealed class MeshBuffer
    {
        /// <summary>Positions.</summary>
        public readonly List<Vector3> Vertices = new();
        /// <summary>Normals (or, for line meshes, stroke tangents).</summary>
        public readonly List<Vector3> Normals = new();
        /// <summary>Vertex colours: the painterly material multiplies its colour by these.</summary>
        public readonly List<Color> Colours = new();
        /// <summary>UV channel 0.</summary>
        public readonly List<Vector2> Uv0 = new();
        /// <summary>UV channel 1 (line meshes: stroke index, half-width).</summary>
        public readonly List<Vector2> Uv1 = new();
        /// <summary>Triangle indices.</summary>
        public readonly List<int> Triangles = new();

        /// <summary>Adds one vertex and returns its index.</summary>
        public int Add(Vector3 position, Vector3 normal, Color colour, Vector2 uv)
        {
            Vertices.Add(position);
            Normals.Add(normal);
            Colours.Add(colour);
            Uv0.Add(uv);
            Uv1.Add(Vector2.zero);
            return Vertices.Count - 1;
        }

        /// <summary>Adds a triangle.</summary>
        public void Triangle(int a, int b, int c)
        {
            Triangles.Add(a);
            Triangles.Add(b);
            Triangles.Add(c);
        }

        /// <summary>Adds a flat-shaded quad facing <paramref name="normal"/>. Corners go round the
        /// quad (a→b along the bottom, d→c along the top; either winding): the triangles are wound
        /// to face the normal. UVs are in metres × <paramref name="uvScale"/>.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color colourBottom, Color colourTop,
                         float uvScale = 1f)
        {
            Vector3 n = normal.normalized;
            float w = (b - a).magnitude * uvScale, h = (d - a).magnitude * uvScale;
            int i0 = Add(a, n, colourBottom, new Vector2(0f, 0f));
            int i1 = Add(b, n, colourBottom, new Vector2(w, 0f));
            int i2 = Add(c, n, colourTop, new Vector2(w, h));
            int i3 = Add(d, n, colourTop, new Vector2(0f, h));
            AddFacing(i0, i1, i2, n);
            AddFacing(i0, i2, i3, n);
        }

        /// <summary>Adds triangle (a, b, c) wound so its front faces <paramref name="normal"/>.</summary>
        public void AddFacing(int a, int b, int c, Vector3 normal)
        {
            Vector3 cross = Vector3.Cross(Vertices[b] - Vertices[a], Vertices[c] - Vertices[a]);
            if (Vector3.Dot(cross, normal) >= 0f) Triangle(a, b, c);
            else Triangle(a, c, b);
        }

        /// <summary>Appends another buffer, transformed.</summary>
        public void Append(MeshBuffer other, Matrix4x4 transform)
        {
            int start = Vertices.Count;
            for (int i = 0; i < other.Vertices.Count; i++)
            {
                Vertices.Add(transform.MultiplyPoint3x4(other.Vertices[i]));
                Normals.Add(transform.MultiplyVector(other.Normals[i]).normalized);
                Colours.Add(other.Colours[i]);
                Uv0.Add(other.Uv0[i]);
                Uv1.Add(other.Uv1[i]);
            }
            for (int i = 0; i < other.Triangles.Count; i++) Triangles.Add(other.Triangles[i] + start);
        }

        /// <summary>Writes the buffer into <paramref name="mesh"/> (cleared first).</summary>
        public Mesh ToMesh(Mesh mesh, string name)
        {
            mesh ??= new Mesh();
            mesh.Clear();
            mesh.name = name;
            mesh.indexFormat = Vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(Vertices);
            mesh.SetNormals(Normals);
            mesh.SetColors(Colours);
            mesh.SetUVs(0, Uv0);
            mesh.SetUVs(1, Uv1);
            mesh.SetTriangles(Triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
