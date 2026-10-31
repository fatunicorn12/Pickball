using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras.Editor
{
    /// <summary>
    /// A character's surface for the camera-in-character check. A whole-body box is far too loose
    /// for close shots (an over-the-shoulder lens sits inside the box of a chibi with a sword on his
    /// back), so skinned meshes are baked in their current pose and kept as world triangles; mesh
    /// renderers without readable meshes fall back to their bounds. <see cref="LensInside"/> asks
    /// "is the lens within the near clip of the surface, or enclosed by it": the second test casts
    /// six axis rays and calls the lens enclosed when every one hits the body (robust for meshes
    /// built from several open shells). Effects (particles, lines, trails) are ignored. Cached per
    /// actor until <see cref="Clear"/>.
    /// </summary>
    public sealed class ActorBounds
    {
        sealed class Body
        {
            public Bounds Box;
            public readonly List<Vector3> Tris = new List<Vector3>();
            public readonly List<Bounds> Boxes = new List<Bounds>();
        }

        readonly Dictionary<CutsceneActor, Body> _cache = new Dictionary<CutsceneActor, Body>();
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<int> _idx = new List<int>();
        Mesh _baked;

        static readonly Vector3[] Axes = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        /// <summary>Forgets cached bodies (call when the actors have moved).</summary>
        public void Clear() => _cache.Clear();

        /// <summary>The actor's overall bounds now.</summary>
        public Bounds Get(CutsceneActor actor) => Build(actor).Box;

        Body Build(CutsceneActor actor)
        {
            if (_cache.TryGetValue(actor, out var body)) return body;
            body = new Body();
            bool any = false;
            // Only the character model (the Animator's hierarchy): the Player's gameplay indicators
            // (heal ring glow, dash arc, ...) are hidden during cutscenes, and their boxes (the glow's
            // is 2.4 m wide) flagged S6_SH12's lens "inside MC" when Euler stood in (review round 4).
            var animator = actor.GetComponentInChildren<Animator>(true);
            Transform model = animator != null ? animator.transform : null;
            foreach (var r in actor.GetComponentsInChildren<Renderer>(false))
            {
                if (!r.enabled) continue;
                if (model != null && !r.transform.IsChildOf(model)) continue;
                if (r is SkinnedMeshRenderer smr)
                {
                    if (smr.sharedMesh == null) continue;
                    if (_baked == null) _baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    smr.BakeMesh(_baked, true);
                    _baked.GetVertices(_verts);
                    var t = smr.transform;
                    for (int i = 0; i < _verts.Count; i++) _verts[i] = t.position + t.rotation * _verts[i];
                    for (int s = 0; s < _baked.subMeshCount; s++)
                    {
                        _baked.GetTriangles(_idx, s);
                        for (int i = 0; i < _idx.Count; i++) body.Tris.Add(_verts[_idx[i]]);
                    }
                    foreach (var v in _verts)
                    {
                        if (!any) { body.Box = new Bounds(v, Vector3.zero); any = true; }
                        else body.Box.Encapsulate(v);
                    }
                }
                else if (r is MeshRenderer)
                {
                    var mf = r.GetComponent<MeshFilter>();
                    var mesh = mf != null ? mf.sharedMesh : null;
                    if (mesh != null && mesh.isReadable)
                    {
                        mesh.GetVertices(_verts);
                        var m = r.transform.localToWorldMatrix;
                        for (int i = 0; i < _verts.Count; i++) _verts[i] = m.MultiplyPoint3x4(_verts[i]);
                        for (int s = 0; s < mesh.subMeshCount; s++)
                        {
                            mesh.GetTriangles(_idx, s);
                            for (int i = 0; i < _idx.Count; i++) body.Tris.Add(_verts[_idx[i]]);
                        }
                    }
                    else body.Boxes.Add(r.bounds);
                    if (!any) { body.Box = r.bounds; any = true; }
                    else body.Box.Encapsulate(r.bounds);
                }
            }
            if (!any) body.Box = new Bounds(actor.transform.position, Vector3.zero);
            _cache[actor] = body;
            return body;
        }

        /// <summary>
        /// Distance from <paramref name="point"/> to the actor's surface (triangles and fallback
        /// boxes), metres; only computed within <paramref name="cutoff"/> of the overall bounds
        /// (otherwise the box distance is returned).
        /// </summary>
        public float SurfaceDistance(CutsceneActor actor, Vector3 point, float cutoff)
        {
            var body = Build(actor);
            float boxDist = Mathf.Sqrt(body.Box.SqrDistance(point));
            if (boxDist > cutoff) return boxDist;
            float best = float.MaxValue;
            var tris = body.Tris;
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                float d = (ClosestOnTriangle(point, tris[i], tris[i + 1], tris[i + 2]) - point).sqrMagnitude;
                if (d < best) best = d;
            }
            best = best < float.MaxValue ? Mathf.Sqrt(best) : float.MaxValue;
            foreach (var b in body.Boxes) best = Mathf.Min(best, Mathf.Sqrt(b.SqrDistance(point)));
            return best;
        }

        /// <summary>
        /// (c) True when the lens is within <paramref name="nearClip"/> of the body's surface or
        /// enclosed by it. <paramref name="distance"/> is the surface distance found.
        /// </summary>
        public bool LensInside(CutsceneActor actor, Vector3 lens, float nearClip, out float distance)
        {
            var body = Build(actor);
            distance = SurfaceDistance(actor, lens, nearClip + 0.01f);
            if (distance <= nearClip) return true;
            if (!body.Box.Contains(lens)) return false;
            foreach (var b in body.Boxes) if (b.Contains(lens)) return true;
            float reach = body.Box.size.magnitude;
            foreach (var axis in Axes)
                if (!RayHits(body.Tris, lens, axis, reach)) return false;
            return true;
        }

        /// <summary>
        /// The mean of the body box and the fallback boxes the segment from <paramref name="lens"/>
        /// to <paramref name="point"/> passes through before reaching it (occlusion review aid).
        /// </summary>
        public bool Occludes(CutsceneActor actor, Vector3 lens, Vector3 point)
        {
            var body = Build(actor);
            Vector3 d = point - lens;
            float dist = d.magnitude;
            if (dist < 1e-3f || body.Box.Contains(point)) return false;
            var dir = d / dist;
            if (!body.Box.IntersectRay(new Ray(lens, dir), out float enter) || enter > dist) return false;
            return RayHits(body.Tris, lens, dir, dist - 0.05f);
        }

        static bool RayHits(List<Vector3> tris, Vector3 origin, Vector3 dir, float maxDist)
        {
            for (int i = 0; i + 2 < tris.Count; i += 3)
            {
                Vector3 a = tris[i], b = tris[i + 1], c = tris[i + 2];
                Vector3 e1 = b - a, e2 = c - a;
                Vector3 p = Vector3.Cross(dir, e2);
                float det = Vector3.Dot(e1, p);
                if (det > -1e-9f && det < 1e-9f) continue;
                float inv = 1f / det;
                Vector3 s = origin - a;
                float u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(s, e1);
                float v = Vector3.Dot(dir, q) * inv;
                if (v < 0f || u + v > 1f) continue;
                float t = Vector3.Dot(e2, q) * inv;
                if (t > 0f && t < maxDist) return true;
            }
            return false;
        }

        /// <summary>Closest point on triangle abc to p (Ericson, Real-Time Collision Detection 5.1.5).</summary>
        static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        /// <summary>Releases the bake mesh.</summary>
        public void Dispose()
        {
            if (_baked != null) Object.DestroyImmediate(_baked);
            _baked = null;
        }
    }
}
