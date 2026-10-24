using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pickleball.Sandbox.Cloth;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Gives the characters' coat tails / cloak secondary motion.
///
/// Tools ▸ Pickleball ▸ Setup Cloth (Secondary Motion)
///
/// Both characters are single fused Tripo meshes with the skirt skinned
/// straight onto the thighs — on Newton the back panel is blended 50/50 across
/// BOTH thighs, so every stride tears it open and the front panel rides the
/// forward leg. There are no cloth bones in either rig. Rather than wait on a
/// Blender pass, this generates them:
///
///  1. Finds the skirt in bind pose: vertices below the hips that are far from
///     every leg bone seed a flood fill (welded across UV seams) that may only
///     climb or move sideways, so it covers the skirt but cannot go up the coat
///     and back down the breeches.
///  2. Places N chains of K joints around the hips on the skirt surface, as
///     children of the Hips transform (found through the humanoid avatar, never
///     by path, so a re-rig in Blender only needs a re-run).
///  3. Writes a copy of the mesh with the skirt blended from its original bones
///     onto the chains (0% at the waist, 100% from blendFalloff down), and
///     swaps it into the SkinnedMeshRenderer. The FBX is never touched.
///  4. Adds <see cref="SpringCloth"/> with capsules on the thighs and shins.
///
/// If the rig already has hand-made cloth bones (chain roots named "Cloth_*"
/// somewhere under the hips, each bone's first child the next bone down) steps
/// 1-3 are skipped and those chains are simulated as they are.
///
/// Re-runnable: it removes its own previous output first. Nothing is saved —
/// save the scene / apply prefab overrides yourself, or call
/// <see cref="Apply(GameObject)"/> from a setup script before it writes the prefab.
/// </summary>
public static class ClothSetup
{
    const string ConfigDir = "Assets/_Project/ScriptableObjects/Cloth";
    const string MeshDir = "Assets/_Project/Art/Characters/ClothGenerated";
    public const string JointPrefix = "ClothJoint";

    [MenuItem("Tools/Pickleball/Setup Cloth (Secondary Motion)")]
    static void SetupMenu()
    {
        int done = 0;
        foreach (var anim in Targets())
            if (Apply(anim.gameObject)) done++;
        if (done == 0)
            Debug.LogWarning("Setup Cloth: no humanoid character with a SkinnedMeshRenderer found " +
                             "(select one, or run Setup Chibi Sandbox / Setup Newton first).");
        else
            EditorSceneManager.MarkAllScenesDirty();
    }

    [MenuItem("Tools/Pickleball/Remove Cloth (Secondary Motion)")]
    static void RemoveMenu()
    {
        foreach (var anim in Targets()) Remove(anim.gameObject);
        EditorSceneManager.MarkAllScenesDirty();
    }

    /// <summary>Selected characters if any are selected, else every humanoid in the open scenes.</summary>
    static IEnumerable<Animator> Targets()
    {
        var selected = Selection.gameObjects
            .Select(g => g.GetComponentInChildren<Animator>(true) ?? g.GetComponentInParent<Animator>(true))
            .Where(a => a != null).Distinct().ToList();
        var all = selected.Count > 0
            ? selected
            : Object.FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None).ToList();
        return all.Where(a => a.avatar != null && a.avatar.isHuman && FindRenderer(a) != null
                              && !EditorUtility.IsPersistent(a));
    }

    /// <summary>
    /// One-line hook for the character setup scripts: pass the model object
    /// (the one with the Animator) after its avatar is assigned.
    /// </summary>
    public static bool Apply(GameObject model) => Apply(model, saveMeshAsset: true, log: true);

    public static bool Apply(GameObject model, bool saveMeshAsset, bool log)
    {
        var anim = model.GetComponentInChildren<Animator>(true);
        if (anim == null || anim.avatar == null || !anim.avatar.isHuman)
        {
            Debug.LogWarning($"Setup Cloth: '{model.name}' has no humanoid Animator.", model);
            return false;
        }
        var smr = FindRenderer(anim);
        if (smr == null) return false;

        Remove(anim.gameObject);
        var src = smr.sharedMesh;
        string key = KeyFor(src);
        var cfg = LoadOrCreateConfig(key);

        var result = Generate(anim, smr, src, cfg, out string why);

        // A rig that already has hand-made cloth bones (Blender: "Cloth_*" under the hips) is
        // simulated as-is — no generated joints, no mesh copy. Generate() is still run above,
        // only to size the leg capsules.
        var authored = FindAuthoredChains(anim);
        if (authored.Count > 0)
        {
            var caps = result?.capsules ?? DefaultCapsules(anim);
            float height = result?.worldHeight ?? smr.bounds.size.y;
            AddSimulator(anim, smr, cfg, authored, caps, height, sourceMesh: null, smr.localBounds);
            if (log)
                Debug.Log($"Setup Cloth: '{model.name}' — using {authored.Count} authored Cloth_* chains from the rig.", model);
            return true;
        }

        if (result == null)
        {
            Debug.LogWarning($"Setup Cloth: skipped '{model.name}' — {why}", model);
            return false;
        }

        // ---- mesh ---------------------------------------------------------------
        var mesh = Object.Instantiate(src);
        mesh.name = src.name + "_Cloth";
        mesh.bindposes = result.bindposes;
        mesh.boneWeights = result.weights;
        if (saveMeshAsset) mesh = SaveMesh(mesh, key);

        // ---- joints ---------------------------------------------------------------
        var roots = new List<Transform>();
        var allJoints = new List<Transform>();
        for (int c = 0; c < result.chains.Count; c++)
        {
            var parent = result.hips;
            Vector3 prevLocal = Vector3.zero;
            for (int j = 0; j < result.chains[c].Length; j++)
            {
                var go = new GameObject($"{JointPrefix}_{c}_{j}");
                Undo.RegisterCreatedObjectUndo(go, "Setup Cloth");
                var t = go.transform;
                t.SetParent(parent, false);
                t.localPosition = result.chains[c][j] - prevLocal;
                t.localRotation = Quaternion.identity;
                t.localScale = Vector3.one;
                prevLocal = result.chains[c][j];
                parent = t;
                if (j == 0) roots.Add(t);
                if (j < result.chains[c].Length - 1) allJoints.Add(t); // the tip carries no weights
            }
        }

        // ---- renderer ---------------------------------------------------------------
        Undo.RecordObject(smr, "Setup Cloth");
        var bounds = smr.localBounds;
        smr.sharedMesh = mesh;
        smr.bones = result.originalBones.Concat(allJoints).ToArray();
        var padded = bounds;
        float rootScale = smr.rootBone != null ? Mathf.Abs(smr.rootBone.lossyScale.y) : 1f;
        padded.Expand(cfg.boundsPadding * result.worldHeight / Mathf.Max(rootScale, 1e-6f) * 2f);
        smr.localBounds = padded;
        PrefabUtility.RecordPrefabInstancePropertyModifications(smr);

        // ---- simulator ---------------------------------------------------------------
        AddSimulator(anim, smr, cfg, roots, result.capsules, result.worldHeight, src, bounds);

        if (log)
            Debug.Log($"Setup Cloth: '{model.name}' — {result.regionVertices} skirt vertices on " +
                      $"{result.chains.Count} chains x {cfg.segmentsPerChain} bones " +
                      $"(sectors dropped: {cfg.chainCount - result.chains.Count}), " +
                      $"{result.capsules.Count} leg capsules, config '{AssetDatabase.GetAssetPath(cfg)}'.", model);
        return true;
    }

    static void AddSimulator(Animator anim, SkinnedMeshRenderer smr, SpringClothConfig cfg,
                             List<Transform> roots, List<(Transform start, Transform end, float radius)> capsules,
                             float worldHeight, Mesh sourceMesh, Bounds sourceBounds)
    {
        var cloth = Undo.AddComponent<SpringCloth>(anim.gameObject);
        var so = new SerializedObject(cloth);
        so.FindProperty("config").objectReferenceValue = cfg;
        so.FindProperty("visibilityRenderer").objectReferenceValue = smr;
        so.FindProperty("sourceMesh").objectReferenceValue = sourceMesh;
        so.FindProperty("sourceBounds").boundsValue = sourceBounds;
        so.FindProperty("characterHeight").floatValue =
            worldHeight / Mathf.Max(Mathf.Abs(anim.transform.lossyScale.y), 1e-6f);
        var rootsProp = so.FindProperty("chainRoots");
        rootsProp.arraySize = roots.Count;
        for (int i = 0; i < roots.Count; i++)
            rootsProp.GetArrayElementAtIndex(i).objectReferenceValue = roots[i];
        var capsProp = so.FindProperty("colliders");
        capsProp.arraySize = capsules.Count;
        for (int i = 0; i < capsules.Count; i++)
        {
            var e = capsProp.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("start").objectReferenceValue = capsules[i].start;
            e.FindPropertyRelative("end").objectReferenceValue = capsules[i].end;
            e.FindPropertyRelative("radius").floatValue = capsules[i].radius;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Name prefix for cloth bones authored in Blender (chain roots under the hips).</summary>
    public const string AuthoredPrefix = "Cloth_";
    const string AuthoredTipName = "ClothTip";

    /// <summary>
    /// Roots of hand-authored chains: "Cloth_*" transforms under the Hips whose parent is not
    /// itself a Cloth_ bone. A chain whose last bone has no child (FBX exported without leaf
    /// bones) gets a weightless tip continuing its direction, because the simulator aims each
    /// bone at its child.
    /// </summary>
    static List<Transform> FindAuthoredChains(Animator anim)
    {
        var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null) return new List<Transform>();
        bool IsCloth(Transform t) => t.name.StartsWith(AuthoredPrefix, System.StringComparison.OrdinalIgnoreCase);
        var roots = hips.GetComponentsInChildren<Transform>(true)
            .Where(t => IsCloth(t) && (t.parent == null || !IsCloth(t.parent))).ToList();
        foreach (var root in roots)
        {
            var end = root;
            while (end.childCount > 0) end = end.GetChild(0);
            if (end.name == AuthoredTipName) continue;
            var tip = new GameObject(AuthoredTipName);
            Undo.RegisterCreatedObjectUndo(tip, "Setup Cloth");
            tip.transform.SetParent(end, false);
            tip.transform.localPosition = Quaternion.Inverse(end.localRotation) * end.localPosition;
        }
        return roots;
    }

    static List<(Transform start, Transform end, float radius)> DefaultCapsules(Animator anim)
    {
        var list = new List<(Transform, Transform, float)>();
        void Add(HumanBodyBones a, HumanBodyBones b, float r)
        {
            var ta = anim.GetBoneTransform(a); var tb = anim.GetBoneTransform(b);
            if (ta != null && tb != null) list.Add((ta, tb, r));
        }
        Add(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, 0.06f);
        Add(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, 0.04f);
        Add(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, 0.06f);
        Add(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, 0.04f);
        return list;
    }

    /// <summary>Undo everything Apply did: original mesh, bones and bounds back, joints and component gone.</summary>
    public static void Remove(GameObject model)
    {
        var anim = model.GetComponentInChildren<Animator>(true);
        if (anim == null) return;
        var cloth = anim.GetComponent<SpringCloth>();
        var smr = FindRenderer(anim);

        if (cloth != null && smr != null && cloth.SourceMesh != null)
        {
            Undo.RecordObject(smr, "Remove Cloth");
            int n = cloth.SourceMesh.bindposes.Length;
            smr.sharedMesh = cloth.SourceMesh;
            smr.bones = smr.bones.Take(n).ToArray();
            smr.localBounds = cloth.SourceBounds;
            PrefabUtility.RecordPrefabInstancePropertyModifications(smr);
        }

        foreach (var t in anim.GetComponentsInChildren<Transform>(true)
                     .Where(t => t.name.StartsWith(JointPrefix + "_") &&
                                 (t.parent == null || !t.parent.name.StartsWith(JointPrefix + "_")))
                     .ToList())
            Undo.DestroyObjectImmediate(t.gameObject);

        foreach (var t in anim.GetComponentsInChildren<Transform>(true)
                     .Where(t => t.name == AuthoredTipName).ToList())
            Undo.DestroyObjectImmediate(t.gameObject);

        if (cloth != null) Undo.DestroyObjectImmediate(cloth);
    }

    // =====================================================================================
    //  Generation (pure maths in the mesh's bind space — independent of the current pose)
    // =====================================================================================

    public sealed class Result
    {
        public Transform hips;
        public Transform[] originalBones;
        public Matrix4x4[] bindposes;
        public BoneWeight[] weights;
        /// <summary>Per chain, joint positions in HIPS-LOCAL space (root first, tip last).</summary>
        public List<Vector3[]> chains = new();
        public List<(Transform start, Transform end, float radius)> capsules = new();
        public int regionVertices;
        public float worldHeight;
        /// <summary>Per vertex: true if it was re-skinned (for tooling/verification).</summary>
        public bool[] inRegion;
    }

    static readonly HumanBodyBones[] Excluded =
    {
        HumanBodyBones.Head, HumanBodyBones.Jaw, HumanBodyBones.LeftEye, HumanBodyBones.RightEye,
        HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
    };

    static readonly HashSet<HumanBodyBones> FootBones = new()
    {
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes,
    };

    public static Result Generate(Animator anim, SkinnedMeshRenderer smr, Mesh src, SpringClothConfig cfg, out string why)
    {
        why = null;
        int boneCount = src.bindposes.Length;
        var bind = src.bindposes;
        var bones = smr.bones.Take(boneCount).ToArray();
        var vertices = src.vertices;
        var weights = src.boneWeights;
        if (vertices.Length == 0 || weights.Length != vertices.Length || boneCount == 0)
        {
            why = $"mesh '{src.name}' has no readable skinning data (vertices={vertices.Length}, " +
                  $"weights={weights.Length}, bindposes={boneCount}).";
            return null;
        }

        // ---- humanoid lookup (avatar mapping, then names from the avatar as a fallback) ----
        var human = new Dictionary<HumanBodyBones, Transform>();
        var byName = anim.GetComponentsInChildren<Transform>(true)
            .GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.First());
        var desc = anim.avatar.humanDescription.human;
        for (var hb = HumanBodyBones.Hips; hb < HumanBodyBones.LastBone; hb++)
        {
            Transform t = anim.GetBoneTransform(hb);
            if (t == null)
            {
                string humanName = HumanTrait.BoneName[(int)hb];
                var map = desc.FirstOrDefault(h => h.humanName == humanName);
                if (!string.IsNullOrEmpty(map.boneName)) byName.TryGetValue(map.boneName, out t);
            }
            if (t != null) human[hb] = t;
        }
        Transform Bone(HumanBodyBones hb) => human.TryGetValue(hb, out var t) ? t : null;

        var hips = Bone(HumanBodyBones.Hips);
        var head = Bone(HumanBodyBones.Head);
        var lul = Bone(HumanBodyBones.LeftUpperLeg); var rul = Bone(HumanBodyBones.RightUpperLeg);
        var lll = Bone(HumanBodyBones.LeftLowerLeg); var rll = Bone(HumanBodyBones.RightLowerLeg);
        var lf = Bone(HumanBodyBones.LeftFoot); var rf = Bone(HumanBodyBones.RightFoot);
        if (hips == null || head == null || lul == null || rul == null || lll == null || rll == null ||
            lf == null || rf == null)
        {
            why = "avatar is missing hips/head/leg bones.";
            return null;
        }

        var index = new Dictionary<Transform, int>();
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] != null && !index.ContainsKey(bones[i])) index[bones[i]] = i;

        // Bind matrix (mesh space -> bone local) for any transform; unskinned bones fall back to the
        // current pose, which for an un-animated edit-mode instance is the import pose.
        Matrix4x4 BindOf(Transform t) => index.TryGetValue(t, out int i)
            ? bind[i]
            : t.worldToLocalMatrix * smr.transform.localToWorldMatrix;
        Vector3 MeshPos(Transform t) => BindOf(t).inverse.MultiplyPoint3x4(Vector3.zero);

        // Each skin bone -> the humanoid bone it belongs to (twist bones walk up to their mapped parent).
        var boneToHuman = new Dictionary<Transform, HumanBodyBones>();
        foreach (var kv in human) boneToHuman[kv.Value] = kv.Key;
        var humanOf = new HumanBodyBones[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            humanOf[i] = HumanBodyBones.LastBone;
            for (var t = bones[i]; t != null && t != anim.transform; t = t.parent)
                if (boneToHuman.TryGetValue(t, out var hb)) { humanOf[i] = hb; break; }
        }
        var excludedSet = new HashSet<HumanBodyBones>(Excluded);
        for (var hb = HumanBodyBones.LeftThumbProximal; hb <= HumanBodyBones.RightLittleDistal; hb++)
            excludedSet.Add(hb);

        // ---- frame ------------------------------------------------------------------------
        Vector3 hipsPos = MeshPos(hips);
        Vector3 up = (MeshPos(head) - hipsPos).normalized;
        Vector3 rightRaw = MeshPos(rul) - MeshPos(lul);
        Vector3 right = (rightRaw - up * Vector3.Dot(rightRaw, up)).normalized;
        Vector3 forward = Vector3.Cross(right, up); // Unity is left-handed: right x up = forward

        float hMin = float.MaxValue, hMax = float.MinValue;
        foreach (var v in vertices)
        {
            float h = Vector3.Dot(v, up);
            hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h);
        }
        float H = hMax - hMin;
        if (H <= 1e-6f) { why = "degenerate mesh height."; return null; }

        Vector3 Toe(Transform foot, HumanBodyBones toes, Transform calf)
        {
            var t = Bone(toes);
            if (t != null) return MeshPos(t);
            Vector3 f = MeshPos(foot);
            return f + Vector3.ProjectOnPlane(f - MeshPos(calf), up).normalized * 0.05f * H;
        }
        var segs = new (Vector3 a, Vector3 b)[]
        {
            (MeshPos(lul), MeshPos(lll)), (MeshPos(lll), MeshPos(lf)), (MeshPos(lf), Toe(lf, HumanBodyBones.LeftToes, lll)),
            (MeshPos(rul), MeshPos(rll)), (MeshPos(rll), MeshPos(rf)), (MeshPos(rf), Toe(rf, HumanBodyBones.RightToes, rll)),
        };

        float top = cfg.regionTop * H;
        float Height(Vector3 p) => Vector3.Dot(p - hipsPos, up);

        // ---- weld by position so UV seams don't break the flood fill or tear the result ----
        int vc = vertices.Length;
        var weld = new int[vc];
        var cellOf = new Dictionary<Vector3Int, int>();
        float q = H * 1e-4f;
        for (int i = 0; i < vc; i++)
        {
            var v = vertices[i];
            var cell = new Vector3Int(Mathf.RoundToInt(v.x / q), Mathf.RoundToInt(v.y / q), Mathf.RoundToInt(v.z / q));
            if (!cellOf.TryGetValue(cell, out int w)) { w = i; cellOf[cell] = i; }
            weld[i] = w;
        }

        // ---- candidates ---------------------------------------------------------------------
        var dist = new Dictionary<int, float>(); // welded id -> leg distance / H
        var shoe = new HashSet<int>();           // welded ids that are footwear, never a seed
        float ankle = Mathf.Max(Height(MeshPos(lf)), Height(MeshPos(rf))) + 0.03f * H;
        for (int i = 0; i < vc; i++)
        {
            int w = weld[i];
            if (w != i || dist.ContainsKey(w)) continue;
            var p = vertices[i];
            if (Height(p) > top) continue;
            var bw = weights[i];
            float excluded = 0f, total = 0f, foot = 0f;
            Accumulate(bw.boneIndex0, bw.weight0); Accumulate(bw.boneIndex1, bw.weight1);
            Accumulate(bw.boneIndex2, bw.weight2); Accumulate(bw.boneIndex3, bw.weight3);
            void Accumulate(int bi, float wt)
            {
                if (wt <= 0f || bi < 0 || bi >= humanOf.Length) return;
                total += wt;
                if (excludedSet.Contains(humanOf[bi])) excluded += wt;
                if (FootBones.Contains(humanOf[bi])) foot += wt;
            }
            if (total <= 0f || excluded / total > cfg.maxExcludedBoneWeight) continue;
            float d = float.MaxValue;
            foreach (var s in segs) d = Mathf.Min(d, SegmentDistance(p, s.a, s.b));
            dist[w] = d / H;
            // Shoe toes and heels can sit further from the foot bones than the seed clearance, so
            // they must not seed the fill. (Cloak tatters skinned to the toes are still reachable
            // from the cloak above them — they are only barred from STARTING the fill.)
            if (foot / total > 0.5f && Height(p) < ankle) shoe.Add(w);
        }

        var neighbours = new Dictionary<int, HashSet<int>>();
        void Link(int a, int b)
        {
            a = weld[a]; b = weld[b];
            if (a == b || !dist.ContainsKey(a) || !dist.ContainsKey(b)) return;
            if (!neighbours.TryGetValue(a, out var na)) neighbours[a] = na = new HashSet<int>();
            if (!neighbours.TryGetValue(b, out var nb)) neighbours[b] = nb = new HashSet<int>();
            na.Add(b); nb.Add(a);
        }
        for (int sm = 0; sm < src.subMeshCount; sm++)
        {
            if (src.GetTopology(sm) != MeshTopology.Triangles) continue;
            var tris = src.GetTriangles(sm);
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                Link(tris[t], tris[t + 1]); Link(tris[t + 1], tris[t + 2]); Link(tris[t + 2], tris[t]);
            }
        }

        // ---- flood fill: seeds far from the legs; no single edge may step down more than monotoneTolerance ----
        var region = new HashSet<int>();
        var stack = new Stack<int>();
        foreach (var kv in dist)
            if (kv.Value > cfg.seedClearance && !shoe.Contains(kv.Key)) { region.Add(kv.Key); stack.Push(kv.Key); }
        float step = cfg.monotoneTolerance * H;
        while (stack.Count > 0)
        {
            int i = stack.Pop();
            if (!neighbours.TryGetValue(i, out var ns)) continue;
            float hi = Height(vertices[i]);
            foreach (int j in ns)
            {
                if (region.Contains(j) || dist[j] <= cfg.innerClearance) continue;
                if (Height(vertices[j]) < hi - step) continue;
                region.Add(j); stack.Push(j);
            }
        }
        if (region.Count == 0) { why = "no skirt/cloak vertices found below the hips."; return null; }

        float hem = region.Min(i => Height(vertices[i]));
        float L = top - hem;
        if (L < 0.05f * H) { why = $"skirt too short ({L / H:P0} of height)."; return null; }

        // ---- chains ----------------------------------------------------------------------------
        int N = cfg.chainCount, K = cfg.segmentsPerChain;
        float spacing = 2f * Mathf.PI / N;
        var chainAngle = new float[N];
        for (int c = 0; c < N; c++) chainAngle[c] = Mathf.PI - c * spacing; // chain 0 at the back
        float Angle(Vector3 p)
        {
            var d = Vector3.ProjectOnPlane(p - hipsPos, up);
            return Mathf.Atan2(Vector3.Dot(d, right), Vector3.Dot(d, forward));
        }
        float Radius(Vector3 p) => Vector3.ProjectOnPlane(p - hipsPos, up).magnitude;

        var buckets = new List<int>[N];
        for (int c = 0; c < N; c++) buckets[c] = new List<int>();
        foreach (int i in region)
        {
            float a = Angle(vertices[i]);
            int best = 0;
            for (int c = 1; c < N; c++)
                if (Mathf.Abs(AngleDiff(a, chainAngle[c])) < Mathf.Abs(AngleDiff(a, chainAngle[best]))) best = c;
            buckets[best].Add(i);
        }
        var active = Enumerable.Range(0, N).Where(c => buckets[c].Count >= cfg.minVerticesPerChain).ToList();
        if (active.Count == 0) { why = "every sector had too few vertices for a chain."; return null; }

        var jointMesh = new Dictionary<int, Vector3[]>();
        foreach (int c in active)
        {
            var radii = new float[K + 1];
            for (int j = 0; j <= K; j++)
            {
                float hj = top - L * j / K;
                var rs = buckets[c].Where(i => Mathf.Abs(Height(vertices[i]) - hj) <= L / K * 0.5f)
                                   .Select(i => Radius(vertices[i])).OrderBy(r => r).ToList();
                radii[j] = rs.Count > 0 ? rs[(int)(0.75f * (rs.Count - 1))] : float.NaN;
            }
            // Fill gaps from neighbours, then force a non-narrowing skirt so joints don't zig-zag.
            for (int j = 1; j <= K; j++) if (float.IsNaN(radii[j])) radii[j] = radii[j - 1];
            for (int j = K - 1; j >= 0; j--) if (float.IsNaN(radii[j])) radii[j] = radii[j + 1];
            for (int j = 1; j <= K; j++) radii[j] = Mathf.Max(radii[j], radii[j - 1]);

            var dir = forward * Mathf.Cos(chainAngle[c]) + right * Mathf.Sin(chainAngle[c]);
            var js = new Vector3[K + 1];
            for (int j = 0; j <= K; j++)
                js[j] = hipsPos + up * (top - L * j / K) + dir * (float.IsNaN(radii[j]) ? 0f : radii[j]);
            jointMesh[c] = js;
        }

        // ---- bones & bindposes ----------------------------------------------------------------
        Matrix4x4 hipsBind = BindOf(hips);
        var newBind = new List<Matrix4x4>(bind);
        var boneIndexOf = new Dictionary<(int c, int j), int>();
        var result = new Result
        {
            hips = hips,
            originalBones = bones,
            worldHeight = H * Mathf.Abs(smr.transform.lossyScale.y),
            regionVertices = 0,
        };
        foreach (int c in active)
        {
            var local = jointMesh[c].Select(p => hipsBind.MultiplyPoint3x4(p)).ToArray();
            result.chains.Add(local);
            for (int j = 0; j < K; j++)
            {
                boneIndexOf[(c, j)] = newBind.Count;
                newBind.Add(Matrix4x4.Translate(-local[j]) * hipsBind);
            }
        }
        result.bindposes = newBind.ToArray();

        // ---- weights ----------------------------------------------------------------------------
        var newWeights = (BoneWeight[])weights.Clone();
        result.inRegion = new bool[vc];
        var entries = new Dictionary<int, float>();
        for (int i = 0; i < vc; i++)
        {
            int w = weld[i];
            if (!region.Contains(w)) continue;
            result.inRegion[i] = true;
            result.regionVertices++;
            var p = vertices[w];
            float h = Height(p);
            float t = Smooth((top - h) / Mathf.Max(1e-6f, cfg.blendFalloff * L));
            float a = Angle(p);

            var order = active.OrderBy(c => Mathf.Abs(AngleDiff(a, chainAngle[c]))).ToList();
            var cw = new List<(int c, float w)> { (order[0], 1f) };
            if (order.Count > 1 && Mathf.Abs(AngleDiff(chainAngle[order[0]], chainAngle[order[1]])) < spacing * 1.5f)
            {
                float d0 = Mathf.Abs(AngleDiff(a, chainAngle[order[0]]));
                float d1 = Mathf.Abs(AngleDiff(a, chainAngle[order[1]]));
                float w1 = d0 / Mathf.Max(1e-6f, d0 + d1);
                cw = new List<(int, float)> { (order[0], 1f - w1), (order[1], w1) };
            }

            // Joint j owns the segment j..j+1; blend across half a segment either side of each joint.
            float s = Mathf.Clamp((top - h) / L * K, 0f, K - 1e-4f);
            int j0 = (int)s; float f = s - j0;
            var sw = new List<(int j, float w)>();
            if (f < 0.5f && j0 > 0) { sw.Add((j0 - 1, 0.5f - f)); sw.Add((j0, 0.5f + f)); }
            else if (f >= 0.5f && j0 < K - 1) { sw.Add((j0, 1.5f - f)); sw.Add((j0 + 1, f - 0.5f)); }
            else sw.Add((j0, 1f));

            entries.Clear();
            var bw = weights[i];
            Add(bw.boneIndex0, bw.weight0 * (1f - t)); Add(bw.boneIndex1, bw.weight1 * (1f - t));
            Add(bw.boneIndex2, bw.weight2 * (1f - t)); Add(bw.boneIndex3, bw.weight3 * (1f - t));
            foreach (var (c, wc) in cw)
                foreach (var (j, wj) in sw)
                    Add(boneIndexOf[(c, j)], wc * wj * t);
            void Add(int bi, float wt)
            {
                if (wt <= 0f) return;
                entries.TryGetValue(bi, out float cur);
                entries[bi] = cur + wt;
            }
            newWeights[i] = ToBoneWeight(entries);
        }
        result.weights = newWeights;

        // ---- leg capsules, sized from the leg vertices that are NOT skirt ----------------------
        void Capsule(Transform a, Transform b, HumanBodyBones owner, int seg)
        {
            var ds = new List<float>();
            for (int i = 0; i < vc; i++)
            {
                if (result.inRegion[i] || weld[i] != i) continue;
                var bw = weights[i];
                int dom = bw.boneIndex0; // Unity sorts weights descending
                if (dom < 0 || dom >= humanOf.Length || humanOf[dom] != owner) continue;
                ds.Add(SegmentDistance(vertices[i], segs[seg].a, segs[seg].b));
            }
            float r = ds.Count > 0
                ? ds.OrderBy(x => x).ElementAt((int)(cfg.legRadiusPercentile * (ds.Count - 1))) / H
                : 0.05f;
            result.capsules.Add((a, b, Mathf.Clamp(r, 0.015f, 0.1f)));
        }
        Capsule(lul, lll, HumanBodyBones.LeftUpperLeg, 0);
        Capsule(lll, lf, HumanBodyBones.LeftLowerLeg, 1);
        Capsule(rul, rll, HumanBodyBones.RightUpperLeg, 3);
        Capsule(rll, rf, HumanBodyBones.RightLowerLeg, 4);
        // Legs are symmetric; one side's dominant-bone set often soaks up stray coat vertices
        // (Tripo weights are lopsided), which inflates that capsule and shoves the hem outward.
        // Use the slimmer side for both.
        for (int k = 0; k < 2; k++)
        {
            float r = Mathf.Min(result.capsules[k].radius, result.capsules[k + 2].radius);
            result.capsules[k] = (result.capsules[k].start, result.capsules[k].end, r);
            result.capsules[k + 2] = (result.capsules[k + 2].start, result.capsules[k + 2].end, r);
        }
        return result;
    }

    // =====================================================================================
    //  Helpers
    // =====================================================================================

    static SkinnedMeshRenderer FindRenderer(Animator anim) =>
        anim.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(r => r.sharedMesh != null)
            .OrderByDescending(r => r.sharedMesh.vertexCount)
            .FirstOrDefault();

    /// <summary>Config key: the source model's file name, so the generated mesh never becomes its own source.</summary>
    static string KeyFor(Mesh src)
    {
        string path = AssetDatabase.GetAssetPath(src);
        return string.IsNullOrEmpty(path) ? src.name : Path.GetFileNameWithoutExtension(path);
    }

    public static SpringClothConfig LoadOrCreateConfig(string key)
    {
        string path = $"{ConfigDir}/{key}Cloth.asset";
        var cfg = AssetDatabase.LoadAssetAtPath<SpringClothConfig>(path);
        if (cfg != null) return cfg;

        EnsureFolder(ConfigDir);
        cfg = ScriptableObject.CreateInstance<SpringClothConfig>();
        // Per-character starting points (measured on the bind-pose meshes): Newton's frock coat is
        // short and heavy wool; the chibi's cloak is long, light and tattered.
        if (key.ToLowerInvariant().Contains("chibi"))
        {
            cfg.stiffnessTip = 0.04f;
            cfg.dampingTip = 0.08f;
            cfg.inertia = 0.65f;
            cfg.blendFalloff = 0.35f;
        }
        else
        {
            cfg.stiffnessTip = 0.08f;
            cfg.dampingTip = 0.15f;
            cfg.inertia = 0.5f;
            cfg.blendFalloff = 0.45f;
        }
        AssetDatabase.CreateAsset(cfg, path);
        AssetDatabase.SaveAssets();
        return cfg;
    }

    static Mesh SaveMesh(Mesh mesh, string key)
    {
        EnsureFolder(MeshDir);
        string path = $"{MeshDir}/{key}_Cloth.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            // Overwrite in place so the GUID (and every reference to it) survives a re-run.
            EditorUtility.CopySerialized(mesh, existing);
            Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        AssetDatabase.SaveAssets();
        return mesh;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static BoneWeight ToBoneWeight(Dictionary<int, float> entries)
    {
        var top4 = entries.OrderByDescending(kv => kv.Value).Take(4).ToList();
        float sum = top4.Sum(kv => kv.Value);
        if (sum <= 0f) sum = 1f;
        var bw = new BoneWeight();
        for (int k = 0; k < top4.Count; k++)
        {
            int bi = top4[k].Key; float w = top4[k].Value / sum;
            switch (k)
            {
                case 0: bw.boneIndex0 = bi; bw.weight0 = w; break;
                case 1: bw.boneIndex1 = bi; bw.weight1 = w; break;
                case 2: bw.boneIndex2 = bi; bw.weight2 = w; break;
                case 3: bw.boneIndex3 = bi; bw.weight3 = w; break;
            }
        }
        return bw;
    }

    static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-12f));
        return (p - (a + ab * t)).magnitude;
    }

    static float AngleDiff(float a, float b)
    {
        float d = (a - b + Mathf.PI) % (2f * Mathf.PI);
        if (d < 0f) d += 2f * Mathf.PI;
        return d - Mathf.PI;
    }

    static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }
}
