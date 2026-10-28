using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Fixes Birdy's skin weights at import time, where Tripo's auto-rigger got
/// them wrong in ways that show once a clip moves the wings (the arm chain).
/// Applied to Birdy.fbx only (exact path match; every other model passes
/// through untouched).
///
/// <b>Accessories follow the body, not the wings.</b> Several non-wing parts
/// carry arm-chain weight (Clavicle, Upperarm, Forearm, Hand and their twist
/// bones), so folding the wings dragged them along and tore white gaps at the
/// shoulders and harness. QA found the worst two:
/// the right shoulder strap (tripo_part_15) was 61% R_UpperarmTwist02 while its
/// left mirror (tripo_part_14) follows the spine, and the back radio
/// (tripo_part_9) was 15% right forearm.
/// 1. The right strap copies its left mirror's weighting vertex by vertex
///    (nearest mirrored vertex, bones swapped L/R). The two parts mirror each
///    other to 3 mm median.
/// 2. Every non-wing part with more than <see cref="ArmShareThreshold"/> of
///    its weight on the arm chain (after rule 1) has that share moved, per
///    vertex, to the nearest spine bone (Waist, Spine01, Spine02), and the
///    vertex's weights are renormalised. The wing planks
///    (<see cref="WingParts"/>) keep their arm weights; their hinge at the
///    shoulder is part of the model.
///
/// <b>The rotor is rigid to the head.</b> Tripo skinned the rotor about 97% to
/// Head and the rest to the arm twist bones, so folding the wings bent the
/// blades across the face (QA measured 0.37 m of drift relative to the head).
/// 3. A part whose average Head weight is at least
///    <see cref="RigidPartThreshold"/> (the rotor) becomes 100% Head. Its stray
///    low-weight vertices are the drooping blade tips, so a per-vertex rule
///    alone would leave exactly the visible ones bent.
/// 4. Any other vertex with Head weight of at least
///    <see cref="RigidVertexThreshold"/> becomes 100% Head.
///
/// Deterministic: it rewrites weights computed fresh from the FBX on every
/// import, so reimporting gives the same mesh. Bump <see cref="GetVersion"/>
/// when the rules change so Unity reimports the model.
/// </summary>
public class BirdyModelPostprocessor : AssetPostprocessor
{
    /// <summary>The one model this applies to.</summary>
    public const string ModelPath = "Assets/_Project/Art/Characters/Birdy/Birdy.fbx";
    const string HeadBone = "Head";
    const float RigidPartThreshold = 0.9f;
    const float RigidVertexThreshold = 0.9f;

    /// <summary>Share of a part's weight on the arm chain above which its
    /// arm weights are moved to the spine.</summary>
    public const float ArmShareThreshold = 0.05f;

    /// <summary>The wing planks: the arm chain is what moves them.</summary>
    static readonly string[] WingParts = { "tripo_part_4", "tripo_part_5" };

    /// <summary>Whether a mesh part is one of the wing planks, which keep their arm weights.</summary>
    public static bool IsWingPart(string partName) => Array.IndexOf(WingParts, partName) >= 0;

    /// <summary>Destination part ← its mirror image across X, whose weighting it copies.</summary>
    static readonly (string part, string mirror)[] MirrorCopies = { ("tripo_part_15", "tripo_part_14") };

    static readonly string[] SpineBones = { "Waist", "Spine01", "Spine02" };
    static readonly Regex ArmChain = new(@"^[LR]_(Clavicle|Upperarm|Forearm|Hand)(Twist\d+)?$");

    /// <summary>Whether a bone is on an arm (a wing) chain: Clavicle, Upperarm,
    /// Forearm, Hand, or one of their twist bones.</summary>
    public static bool IsArmChain(string boneName) => boneName != null && ArmChain.IsMatch(boneName);

    public override uint GetVersion() => 2;

    void OnPostprocessModel(GameObject root)
    {
        if (!string.Equals(assetPath, ModelPath, StringComparison.OrdinalIgnoreCase)) return;

        var smrs = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
            .Where(s => s.sharedMesh != null && s.bones != null).ToArray();
        var transforms = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) transforms[t.name] = t;
        var spine = SpineBones.Where(transforms.ContainsKey).Select(n => transforms[n]).ToArray();

        var report = new List<string>();
        foreach (var smr in smrs)
        {
            if (IsWingPart(smr.name)) continue;
            var influences = Read(smr);
            var positions = RootSpace(root, smr);
            string how = null;

            var mirror = MirrorCopies.FirstOrDefault(m => m.part == smr.name).mirror;
            var source = mirror != null ? smrs.FirstOrDefault(s => s.name == mirror) : null;
            if (source != null)
            {
                influences = MirrorFrom(source, RootSpace(root, source), positions, transforms, out float worst);
                how = $"mirrored from {mirror} (worst match {worst * 100f:0.0} cm)";
            }

            float before = ArmShare(influences);
            if (before > ArmShareThreshold)
            {
                MoveArmToSpine(influences, positions, spine, root);
                how = (how != null ? how + ", then " : "") + $"arm-chain {before * 100f:0}% -> spine";
            }
            if (how == null) continue;
            Write(smr, influences);
            report.Add($"{smr.name} {how}");
        }

        int rigidParts = 0, rigidVerts = 0;
        foreach (var smr in smrs) MakeHeadRigid(smr, ref rigidParts, ref rigidVerts);

        Debug.Log($"BirdyModelPostprocessor: {rigidParts} part(s) made rigid to {HeadBone}, " +
                  $"{rigidVerts} vertex weight(s) set to 100% {HeadBone}; accessories rebound off the " +
                  $"wings: {(report.Count > 0 ? string.Join("; ", report) : "none")}.");
    }

    // ------------------------------------------------------------- weights

    /// <summary>Per vertex, its influences as (bone, weight).</summary>
    static List<(Transform bone, float w)>[] Read(SkinnedMeshRenderer smr)
    {
        var bones = smr.bones;
        var weights = smr.sharedMesh.boneWeights;
        var result = new List<(Transform, float)>[weights.Length];
        for (int v = 0; v < weights.Length; v++)
        {
            var list = new List<(Transform, float)>(4);
            void Add(int i, float w)
            {
                if (w > 0f && i >= 0 && i < bones.Length && bones[i] != null) list.Add((bones[i], w));
            }
            var b = weights[v];
            Add(b.boneIndex0, b.weight0);
            Add(b.boneIndex1, b.weight1);
            Add(b.boneIndex2, b.weight2);
            Add(b.boneIndex3, b.weight3);
            result[v] = list;
        }
        return result;
    }

    /// <summary>Writes influences back, adding any bone the renderer does not
    /// yet list (with its bind pose), keeping the four strongest per vertex.</summary>
    static void Write(SkinnedMeshRenderer smr, List<(Transform bone, float w)>[] influences)
    {
        var mesh = smr.sharedMesh;
        var bones = smr.bones.ToList();
        var bindposes = mesh.bindposes.ToList();
        int IndexOf(Transform bone)
        {
            int i = bones.IndexOf(bone);
            if (i >= 0) return i;
            // The import hierarchy is in the bind pose, so the bind pose of a
            // new bone is read straight off the transforms (the same relation
            // every existing bind pose satisfies).
            bones.Add(bone);
            bindposes.Add(bone.worldToLocalMatrix * smr.transform.localToWorldMatrix);
            return bones.Count - 1;
        }

        var weights = new BoneWeight[influences.Length];
        for (int v = 0; v < influences.Length; v++)
        {
            var top = influences[v].GroupBy(x => x.bone)
                .Select(g => (bone: g.Key, w: g.Sum(x => x.w)))
                .OrderByDescending(x => x.w).Take(4).ToList();
            float total = top.Sum(x => x.w);
            if (total <= 0f) continue;
            var bw = new BoneWeight();
            for (int k = 0; k < top.Count; k++)
            {
                int index = IndexOf(top[k].bone);
                float w = top[k].w / total;
                switch (k)
                {
                    case 0: bw.boneIndex0 = index; bw.weight0 = w; break;
                    case 1: bw.boneIndex1 = index; bw.weight1 = w; break;
                    case 2: bw.boneIndex2 = index; bw.weight2 = w; break;
                    default: bw.boneIndex3 = index; bw.weight3 = w; break;
                }
            }
            weights[v] = bw;
        }
        mesh.bindposes = bindposes.ToArray();
        smr.bones = bones.ToArray();
        mesh.boneWeights = weights;
    }

    static Vector3[] RootSpace(GameObject root, SkinnedMeshRenderer smr)
    {
        var m = root.transform.worldToLocalMatrix * smr.transform.localToWorldMatrix;
        var verts = smr.sharedMesh.vertices;
        for (int v = 0; v < verts.Length; v++) verts[v] = m.MultiplyPoint3x4(verts[v]);
        return verts;
    }

    static float ArmShare(List<(Transform bone, float w)>[] influences)
    {
        float arm = 0f, total = 0f;
        foreach (var list in influences)
            foreach (var (bone, w) in list)
            {
                total += w;
                if (IsArmChain(bone.name)) arm += w;
            }
        return total > 0f ? arm / total : 0f;
    }

    /// <summary>Each vertex takes the influences of the nearest vertex of the
    /// mirror part (reflected across X), with L_/R_ bones swapped.</summary>
    static List<(Transform bone, float w)>[] MirrorFrom(SkinnedMeshRenderer source, Vector3[] sourcePositions,
        Vector3[] positions, Dictionary<string, Transform> transforms, out float worst)
    {
        var sourceInfluences = Read(source);
        var result = new List<(Transform, float)>[positions.Length];
        worst = 0f;
        for (int v = 0; v < positions.Length; v++)
        {
            var p = positions[v];
            var mirrored = new Vector3(-p.x, p.y, p.z);
            int best = 0;
            float bestSq = float.PositiveInfinity;
            for (int s = 0; s < sourcePositions.Length; s++)
            {
                float d = (sourcePositions[s] - mirrored).sqrMagnitude;
                if (d < bestSq) { bestSq = d; best = s; }
            }
            worst = Mathf.Max(worst, Mathf.Sqrt(bestSq));
            var list = new List<(Transform, float)>(4);
            foreach (var (bone, w) in sourceInfluences[best])
            {
                string swapped = Swap(bone.name);
                list.Add((transforms.TryGetValue(swapped, out var t) ? t : bone, w));
            }
            result[v] = list;
        }
        return result;
    }

    static string Swap(string name)
        => name.StartsWith("L_") ? "R_" + name.Substring(2)
         : name.StartsWith("R_") ? "L_" + name.Substring(2) : name;

    /// <summary>Moves each vertex's arm-chain weight to the spine bone nearest it.</summary>
    static void MoveArmToSpine(List<(Transform bone, float w)>[] influences, Vector3[] positions,
                               Transform[] spine, GameObject root)
    {
        if (spine.Length == 0) return;
        var spineAt = spine.Select(b => root.transform.InverseTransformPoint(b.position)).ToArray();
        for (int v = 0; v < influences.Length; v++)
        {
            float moved = 0f;
            var kept = new List<(Transform, float)>(4);
            foreach (var (bone, w) in influences[v])
            {
                if (IsArmChain(bone.name)) moved += w;
                else kept.Add((bone, w));
            }
            if (moved <= 0f) continue;
            int nearest = 0;
            for (int s = 1; s < spine.Length; s++)
                if ((spineAt[s] - positions[v]).sqrMagnitude < (spineAt[nearest] - positions[v]).sqrMagnitude)
                    nearest = s;
            kept.Add((spine[nearest], moved));
            influences[v] = kept;
        }
    }

    // --------------------------------------------------------------- rotor

    static void MakeHeadRigid(SkinnedMeshRenderer smr, ref int rigidParts, ref int rigidVerts)
    {
        var mesh = smr.sharedMesh;
        var bones = smr.bones;
        int head = Array.FindIndex(bones, b => b != null && b.name == HeadBone);
        if (head < 0) return;

        var weights = mesh.boneWeights;
        if (weights.Length == 0) return;
        float total = 0f;
        foreach (var w in weights) total += HeadWeight(w, head);
        bool wholePart = total / weights.Length >= RigidPartThreshold;

        bool changed = false;
        for (int v = 0; v < weights.Length; v++)
        {
            float h = HeadWeight(weights[v], head);
            if (h >= 0.99999f) continue;
            if (!wholePart && h < RigidVertexThreshold) continue;
            weights[v] = new BoneWeight { boneIndex0 = head, weight0 = 1f };
            changed = true;
            rigidVerts++;
        }
        if (!changed) return;
        mesh.boneWeights = weights;
        if (wholePart) rigidParts++;
    }

    static float HeadWeight(BoneWeight w, int head)
    {
        float h = 0f;
        if (w.boneIndex0 == head) h += w.weight0;
        if (w.boneIndex1 == head) h += w.weight1;
        if (w.boneIndex2 == head) h += w.weight2;
        if (w.boneIndex3 == head) h += w.weight3;
        return h;
    }
}
