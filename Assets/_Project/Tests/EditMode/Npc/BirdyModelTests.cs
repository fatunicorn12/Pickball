using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Pickleball.Tests.Npc
{
    /// <summary>
    /// Birdy's imported mesh, as BirdyModelPostprocessor (v4) leaves it, on the
    /// shipped prefab. These are the faults that made him see-through:
    /// - no part is inside out against its mirror twin (the right chest patch
    ///   was, so his right side showed a hole);
    /// - only the lower body follows the legs;
    /// - every open hole is covered at bind;
    /// - the seams between parts stay closed in the idle and talk poses (no
    ///   rim vertex lifts more than about a centimetre off the surface it
    ///   sat on at bind).
    ///
    /// The part names are Tripo's, as the postprocessor lists them. Read-only
    /// use of shipped assets, so it is in the Assets category.
    /// </summary>
    [Category("Assets")]
    public class BirdyModelTests
    {
        const string PrefabPath = "Assets/_Project/Prefabs/Npc/Birdy.prefab";

        static readonly (string a, string b)[] Twins =
        {
            ("tripo_part_14", "tripo_part_15"), ("tripo_part_10", "tripo_part_11"),
            ("tripo_part_4", "tripo_part_5"), ("tripo_part_7", "tripo_part_8"),
        };

        /// <summary>The legs, boots, belly, tail, and the radio that rides on the belly.</summary>
        static readonly string[] FollowTheLegs =
        {
            "tripo_part_0", "tripo_part_3", "tripo_part_7", "tripo_part_8", "tripo_part_9", "tripo_part_10",
            "tripo_part_11",
        };

        /// <summary>Follower ← the parts its rims sit on: those rims move with them.</summary>
        static readonly (string follower, string[] leaders)[] Seams =
        {
            ("tripo_part_0", new[] { "tripo_part_6" }),
            ("tripo_part_14", new[] { "tripo_part_6", "tripo_part_0" }),
            ("tripo_part_15", new[] { "tripo_part_6", "tripo_part_0" }),
            ("tripo_part_13", new[] { "tripo_part_6", "tripo_part_14", "tripo_part_15" }),
            ("tripo_part_13", new[] { "tripo_part_2" }),
            ("tripo_part_3", new[] { "tripo_part_0" }),
            ("tripo_part_4", new[] { "tripo_part_14", "tripo_part_0", "tripo_part_6" }),
            ("tripo_part_5", new[] { "tripo_part_15", "tripo_part_0", "tripo_part_6" }),
        };

        readonly List<Object> _spawned = new();

        [TearDown]
        public void DestroySpawned()
        {
            foreach (var o in _spawned) if (o != null) Object.DestroyImmediate(o);
            _spawned.Clear();
        }

        GameObject Spawn(out Animator animator)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, $"{PrefabPath} is missing; run Setup Birdy NPC.");
            var go = Object.Instantiate(prefab);
            go.hideFlags = HideFlags.DontSave;
            _spawned.Add(go);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            animator = go.GetComponentInChildren<Animator>();
            return go;
        }

        Dictionary<string, Vector3[]> Bake(GameObject go)
        {
            var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            _spawned.Add(mesh);
            var result = new Dictionary<string, Vector3[]>();
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                smr.BakeMesh(mesh, true);
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                result[smr.name] = mesh.vertices.Select(v => m.MultiplyPoint3x4(v)).ToArray();
            }
            return result;
        }

        static float Nearest(Vector3[] points, Vector3 q)
        {
            float best = float.PositiveInfinity;
            foreach (var p in points) best = Mathf.Min(best, (p - q).sqrMagnitude);
            return Mathf.Sqrt(best);
        }

        /// <summary>Open rims of a mesh (edges used by one triangle, welded by
        /// position), each as every vertex index on it.</summary>
        static List<int[]> Rims(Mesh mesh) => RimsWithEdges(mesh).Select(r => r.verts).ToList();

        /// <summary>As <see cref="Rims"/>, with each rim's number of open edges.</summary>
        static List<(int[] verts, int edges)> RimsWithEdges(Mesh mesh)
        {
            var verts = mesh.vertices;
            var key = new Dictionary<Vector3Int, int>();
            var weld = new int[verts.Length];
            for (int v = 0; v < verts.Length; v++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(verts[v].x * 1e4f), Mathf.RoundToInt(verts[v].y * 1e4f),
                                       Mathf.RoundToInt(verts[v].z * 1e4f));
                if (!key.TryGetValue(k, out int id)) key[k] = id = key.Count;
                weld[v] = id;
            }
            var count = new Dictionary<(int, int), int>();
            var tris = mesh.triangles;
            for (int t = 0; t < tris.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = weld[tris[t + e]], b = weld[tris[t + (e + 1) % 3]];
                    if (a == b) continue;
                    var k = a < b ? (a, b) : (b, a);
                    count[k] = count.TryGetValue(k, out int n) ? n + 1 : 1;
                }
            var adj = new Dictionary<int, List<int>>();
            foreach (var kv in count.Where(kv => kv.Value == 1))
            {
                var (a, b) = kv.Key;
                if (!adj.TryGetValue(a, out var la)) adj[a] = la = new List<int>();
                if (!adj.TryGetValue(b, out var lb)) adj[b] = lb = new List<int>();
                la.Add(b); lb.Add(a);
            }
            var seen = new HashSet<int>();
            var rims = new List<(int[] verts, int edges)>();
            foreach (int s in adj.Keys)
            {
                if (!seen.Add(s)) continue;
                var comp = new HashSet<int> { s };
                var stack = new Stack<int>();
                stack.Push(s);
                while (stack.Count > 0)
                    foreach (int y in adj[stack.Pop()])
                        if (seen.Add(y)) { comp.Add(y); stack.Push(y); }
                int edges = count.Count(kv => kv.Value == 1 && comp.Contains(kv.Key.Item1));
                rims.Add((Enumerable.Range(0, verts.Length).Where(v => comp.Contains(weld[v])).ToArray(), edges));
            }
            return rims;
        }

        [Test]
        public void MirrorTwins_FaceTheSameWay()
        {
            var go = Spawn(out var animator);
            animator.enabled = false;
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>().ToDictionary(s => s.name);
            var bind = Bake(go);
            (List<Vector3> c, List<Vector3> n) Faces(string part)
            {
                var p = bind[part];
                var tris = smrs[part].sharedMesh.triangles;
                var c = new List<Vector3>(); var n = new List<Vector3>();
                for (int t = 0; t < tris.Length; t += 3)
                {
                    Vector3 a = p[tris[t]], b = p[tris[t + 1]], d = p[tris[t + 2]];
                    var x = Vector3.Cross(b - a, d - a);
                    if (x.sqrMagnitude < 1e-16f) continue;
                    c.Add((a + b + d) / 3f); n.Add(x.normalized);
                }
                return (c, n);
            }
            foreach (var (a, b) in Twins)
            {
                var (ac, an) = Faces(a);
                var (bc, bn) = Faces(b);
                float dot = 0f; int count = 0;
                for (int i = 0; i < bc.Count; i += 4)
                {
                    var m = bc[i]; m.x = -m.x;
                    int best = 0; float bd = float.PositiveInfinity;
                    for (int j = 0; j < ac.Count; j++)
                    {
                        float d = (ac[j] - m).sqrMagnitude;
                        if (d < bd) { bd = d; best = j; }
                    }
                    var mirrored = an[best]; mirrored.x = -mirrored.x;
                    dot += Vector3.Dot(mirrored, bn[i]); count++;
                }
                Assert.That(dot / count, Is.GreaterThan(0.3f), $"{b} faces against its mirrored twin {a} (inside out)");
            }
        }

        [Test]
        public void OnlyTheLowerBody_FollowsTheLegs()
        {
            var go = Spawn(out _);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (FollowTheLegs.Contains(smr.name)) continue;
                var bones = smr.bones;
                float leg = 0f;
                foreach (var w in smr.sharedMesh.boneWeights)
                {
                    void Add(int i, float x)
                    {
                        if (x <= 0f || i < 0 || i >= bones.Length || bones[i] == null) return;
                        var n = bones[i].name;
                        if (n.Contains("Thigh") || n.Contains("Calf") || n.Contains("Foot") || n.Contains("ToeBase")) leg += x;
                    }
                    Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1);
                    Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
                }
                Assert.That(leg, Is.EqualTo(0f), $"{smr.name} still follows the legs");
            }
        }

        [Test]
        public void EverySmallHole_IsCoveredAtBind()
        {
            var go = Spawn(out var animator);
            animator.enabled = false;
            var bind = Bake(go);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var others = bind.Where(kv => kv.Key != smr.name).SelectMany(kv => kv.Value).ToArray();
                foreach (var (rim, edges) in RimsWithEdges(smr.sharedMesh))
                {
                    // Fewer than three open edges is a crack, not a hole: nothing to fill
                    // (the rotor hub has a 9 mm one).
                    if (edges < 3) continue;
                    var rimPos = rim.Select(v => bind[smr.name][v]).ToArray();
                    var b = new Bounds(rimPos[0], Vector3.zero);
                    foreach (var p in rimPos) b.Encapsulate(p);
                    if (b.size.magnitude > 0.25f) continue;      // a big opening another part fills
                    var gaps = rimPos.Where((_, i) => i % 2 == 0).Select(p => Nearest(others, p)).OrderBy(d => d).ToList();
                    Assert.That(gaps[gaps.Count / 2], Is.LessThan(0.02f),
                                $"{smr.name} has an open hole at {b.center} ({edges} open edges, spanning " +
                                $"{b.size.magnitude * 100f:0.0} cm) that nothing covers");
                }
            }
        }

        [TestCase("Idle", 0.5f)]
        [TestCase("Talk_Talk", 0.3f)]
        [TestCase("Talk_Excited", 0.4f)]
        [TestCase("Talk_Thinking", 0.5f)]
        public void Seams_StayClosed(string state, float t)
        {
            var go = Spawn(out var animator);
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>().ToDictionary(s => s.name);
            animator.enabled = false;
            var bind = Bake(go);
            animator.enabled = true;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            animator.Update(0f);
            animator.Play(state, 0, t);
            animator.Update(0f);
            var posed = Bake(go);

            foreach (var (follower, leaders) in Seams)
            {
                var atBind = leaders.SelectMany(l => bind[l]).ToArray();
                var atPose = leaders.SelectMany(l => posed[l]).ToArray();
                foreach (var rim in Rims(smrs[follower].sharedMesh))
                {
                    var before = rim.Select(v => Nearest(atBind, bind[follower][v])).ToArray();
                    if (before.OrderBy(d => d).ElementAt(before.Length / 2) > 0.01f) continue;   // not on them
                    // How far each rim vertex has moved off the surface it sat on.
                    var opened = rim.Select((v, i) => Nearest(atPose, posed[follower][v]) - before[i])
                                    .OrderBy(d => d).ToList();
                    float p90 = opened[(int)(opened.Count * 0.9f)];
                    Assert.That(p90, Is.LessThan(0.012f),
                                $"{state}: {follower}'s seam with {string.Join("/", leaders)} opens {p90 * 100f:0.0} cm");
                }
            }
        }
    }
}
