using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras.Editor
{
    /// <summary>
    /// An actor's head as it really appears on screen: the skin weighted to the head bone (at least
    /// <see cref="MinWeight"/>; for Birdy that is the head and helmet, not the rotor mast above it),
    /// baked in its current pose and projected through the camera. Its height in the frame is the
    /// frac the TCU rule means.
    /// </summary>
    /// <remarks>
    /// Review round 4: the old frac, HB / frame height at the head centre's depth, read S2_SH05 at
    /// 0.29 where the reviewer measured 0.36 from helmet top to chin. With a wide lens close up, the
    /// near side of the head is much closer than its centre, and the helmet sits partly outside the
    /// head-weighted skin HB is measured from. Projecting the vertices measures what the viewer sees.
    /// </remarks>
    public sealed class HeadScreen
    {
        /// <summary>Minimum head-bone weight for a vertex to count as head.</summary>
        public const float MinWeight = 0.5f;

        sealed class Part
        {
            public SkinnedMeshRenderer Renderer;
            public int[] Vertices;
        }

        readonly Dictionary<CutsceneActor, List<Part>> _parts = new Dictionary<CutsceneActor, List<Part>>();
        readonly Mesh _baked = new Mesh();
        readonly List<Vector3> _verts = new List<Vector3>();

        /// <summary>
        /// The head's height in the frame (viewport units, 0..1+) from a camera, or −1 when the
        /// actor has no head-weighted skin or none of it is in front of the camera.
        /// </summary>
        public float Fraction(CutsceneActor actor, Vector3 cameraPosition, Quaternion cameraRotation, float fov, float aspect)
        {
            var parts = PartsOf(actor);
            if (parts.Count == 0) return -1f;
            float min = float.PositiveInfinity, max = float.NegativeInfinity;
            foreach (var part in parts)
            {
                var smr = part.Renderer;
                if (smr == null || !smr.gameObject.activeInHierarchy) continue;
                smr.BakeMesh(_baked, true);
                _baked.GetVertices(_verts);
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                foreach (int v in part.Vertices)
                {
                    if (v >= _verts.Count) continue;
                    Vector3 vp = CameraMath.Viewport(cameraPosition, cameraRotation, fov, aspect, m.MultiplyPoint3x4(_verts[v]));
                    if (vp.z <= 0f) continue;
                    min = Mathf.Min(min, vp.y);
                    max = Mathf.Max(max, vp.y);
                }
            }
            return max > min ? max - min : -1f;
        }

        List<Part> PartsOf(CutsceneActor actor)
        {
            if (_parts.TryGetValue(actor, out var parts)) return parts;
            parts = new List<Part>();
            _parts[actor] = parts;
            var animator = actor != null ? actor.GetComponentInChildren<Animator>(true) : null;
            Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null) return parts;
            foreach (var smr in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh mesh = smr.sharedMesh;
                if (mesh == null) continue;
                int hi = System.Array.IndexOf(smr.bones, head);
                if (hi < 0) continue;
                BoneWeight[] weights = mesh.boneWeights;
                var list = new List<int>();
                for (int v = 0; v < weights.Length; v++)
                {
                    var w = weights[v];
                    float s = (w.boneIndex0 == hi ? w.weight0 : 0f) + (w.boneIndex1 == hi ? w.weight1 : 0f)
                              + (w.boneIndex2 == hi ? w.weight2 : 0f) + (w.boneIndex3 == hi ? w.weight3 : 0f);
                    if (s >= MinWeight) list.Add(v);
                }
                if (list.Count > 0) parts.Add(new Part { Renderer = smr, Vertices = list.ToArray() });
            }
            return parts;
        }

        /// <summary>Frees the bake buffer.</summary>
        public void Dispose() => Object.DestroyImmediate(_baked);
    }
}
