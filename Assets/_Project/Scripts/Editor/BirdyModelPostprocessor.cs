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
/// <b>The rotor has its own bone.</b> On the head it can't do anything but
/// follow the head: in the death, lying on his back turns the head's up
/// horizontal and stood the blades upright out of the floor.
/// 5. A <see cref="RotorBone"/> transform is added under Head and every vertex
///    of <see cref="RotorPart"/> is weighted 100% to it, so
///    <c>NpcRotor</c> can spin it and, in the death, knock it off. The bone's
///    origin is the bottom of the mast on the spin axis, and its local up is
///    the spin axis: the normal of the blade disc (its least-spread direction),
///    found from the vertices. The disc is tilted about 16° back from the
///    head's up, so the axis is measured, not assumed. Resting at its origin
///    with up pointing up, no part of the rotor is below the ground.
///
/// <b>No see-through gaps (v4).</b> Rendered with backfaces in flat colours,
/// these are what let you see through him:
/// 6. <i>Inside-out parts.</i> The right chest-side patch (tripo_part_15) and
///    the left leg (tripo_part_11) were exported with their winding and
///    normals both pointing inward, the mirror of their left/right twins.
///    With backface culling the patch vanished, leaving a hole through his
///    right side in every pose. Each part in <see cref="FacingTwins"/> is
///    compared face by face with its twin mirrored across X ("use his other
///    side"), and if they face opposite ways its triangles and normals are
///    reversed and its tangents recomputed.
/// 7. <i>The chest followed the thighs.</i> Tripo weighted the chest (41%),
///    the chest-side patches and the compass to the thigh-twist bones, so
///    any leg motion pulled their seams apart. Every part outside
///    <see cref="LegParts"/> has its leg-chain weight moved to the nearest
///    spine bone, as rule 2 does for the arms.
/// 8. <i>Seams tore when the wings or the head moved.</i> The wing roots
///    (100% arm) sat on the spine-weighted patches only in the T-pose, and
///    the helmet's rims carried a different head/spine mix from the chest and
///    head under them. Each follower in <see cref="Seams"/> takes, on the
///    rims where it touches a leader at bind, the weights of the leader
///    surface it touches, blending back to its own over a band. So a wing
///    root stretches like a shoulder instead of lifting off the body.
/// 9. <i>Holes nothing covers.</i> The radio box's open side (and six small
///    holes on it), three centimetre holes in the head and one in each boot
///    are capped with a fan of triangles. Each triangle takes the texture
///    colour of the surface at its rim, and the fan is skinned like the rim.
///    A rim of fewer than three open edges is a crack, not a hole, and is
///    left alone (the rotor hub has one, 9 mm long).
///
/// Deterministic: it rewrites weights computed fresh from the FBX on every
/// import, so reimporting gives the same mesh and the same rotor bone. Bump
/// <see cref="GetVersion"/> when the rules change so Unity reimports the model.
/// </summary>
public class BirdyModelPostprocessor : AssetPostprocessor
{
    /// <summary>The one model this applies to.</summary>
    public const string ModelPath = "Assets/_Project/Art/Characters/Birdy/Birdy.fbx";
    const string HeadBone = "Head";
    const float RigidPartThreshold = 0.9f;
    const float RigidVertexThreshold = 0.9f;

    /// <summary>The rotor's mesh part (mast, hub and the two blades).</summary>
    public const string RotorPart = "tripo_part_1";

    /// <summary>Name of the bone added under Head that the rotor is skinned to.</summary>
    public const string RotorBone = "Rotor";

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

    /// <summary>Parts that are a mirror image of a twin on the other side and
    /// must face the same way: part ← its twin, which is the reference.</summary>
    static readonly (string part, string twin)[] FacingTwins =
    {
        ("tripo_part_15", "tripo_part_14"),     // right chest-side patch ← left
        ("tripo_part_11", "tripo_part_10"),     // left leg ← right
    };

    /// <summary>The parts that should follow the legs: the legs, the boots,
    /// the lower body (hips and belly), the tail, and the radio that rides on
    /// the belly. Every other part sheds its leg-chain weight to the spine.</summary>
    static readonly string[] LegParts =
    {
        "tripo_part_0", "tripo_part_3", "tripo_part_7", "tripo_part_8", "tripo_part_9", "tripo_part_10",
        "tripo_part_11",
    };

    static readonly Regex LegChain = new(@"^[LR]_(Thigh|Calf|Foot|ToeBase)(Twist\d+)?$");

    /// <summary>Whether a bone is on a leg chain: Thigh, Calf, Foot, ToeBase,
    /// or one of their twist bones.</summary>
    public static bool IsLegChain(string boneName) => boneName != null && LegChain.IsMatch(boneName);

    /// <summary>
    /// Seams where parts meet edge to edge and must move together: a
    /// follower's open rims that lie on any of its leaders take, vertex by
    /// vertex, the weights of the leader surface they touch, blending back to
    /// the follower's own over the band, m. Leaders never follow their
    /// followers, and the list runs so each leader is final before anything
    /// follows it.
    /// </summary>
    static readonly (string follower, string[] leaders, float band)[] Seams =
    {
        ("tripo_part_0", new[] { "tripo_part_6" }, 0.04f),                                   // belly's top rim ← chest
        ("tripo_part_14", new[] { "tripo_part_6", "tripo_part_0" }, 0.03f),                  // left chest-side patch ← chest, back
        ("tripo_part_15", new[] { "tripo_part_6", "tripo_part_0" }, 0.03f),                  // right chest-side patch ← chest, back
        ("tripo_part_13", new[] { "tripo_part_6", "tripo_part_14", "tripo_part_15" }, 0.04f), // helmet's lower rim ← chest, patches
        ("tripo_part_13", new[] { "tripo_part_2" }, 0.03f),                                  // helmet's edge ← head
        ("tripo_part_3", new[] { "tripo_part_0" }, 0.03f),                                   // tail root ← belly
        ("tripo_part_4", new[] { "tripo_part_14", "tripo_part_0", "tripo_part_6" }, 0.07f),  // left wing root ← patch, back, chest
        ("tripo_part_5", new[] { "tripo_part_15", "tripo_part_0", "tripo_part_6" }, 0.07f),  // right wing root ← patch, back, chest
    };

    /// <summary>A rim is a seam with the leader when its vertices are this
    /// close to the leader at bind (median), m.</summary>
    const float SeamTouch = 0.01f;

    /// <summary>An open hole is capped when nothing covers it at bind: the
    /// median distance from its rim to every other part, m.</summary>
    const float UncoveredGap = 0.02f;

    /// <summary>Rims spanning more than this, m (bounding-box diagonal), are
    /// openings another part is meant to fill, not holes. (Every such opening
    /// on Birdy is also covered at bind, so this is a second guard.)</summary>
    const float MaxCapSize = 0.25f;

    public override uint GetVersion() => 4;

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

        var rotorSmr = smrs.FirstOrDefault(s => s.name == RotorPart);
        string rotor = rotorSmr != null && transforms.TryGetValue(HeadBone, out var head)
            ? AddRotorBone(rotorSmr, head)
            : $"no {RotorPart} or {HeadBone}; no rotor bone";

        var holes = new List<string>();
        foreach (var (part, twin) in FacingTwins)
        {
            var a = smrs.FirstOrDefault(s => s.name == part);
            var b = smrs.FirstOrDefault(s => s.name == twin);
            if (a != null && b != null && TurnRightWayOut(root, a, b, out float facing))
                holes.Add($"{part} turned right way out (faced {facing:+0.00;-0.00} against mirrored {twin})");
        }
        foreach (var smr in smrs)
        {
            if (Array.IndexOf(LegParts, smr.name) >= 0 || smr.name == RotorPart) continue;
            var influences = Read(smr);
            float leg = Share(influences, IsLegChain);
            if (leg <= 0f) continue;
            MoveChainToSpine(influences, RootSpace(root, smr), spine, root, IsLegChain);
            Write(smr, influences);
            holes.Add($"{smr.name} leg-chain {leg * 100f:0}% -> spine");
        }
        foreach (var (follower, leaders, band) in Seams)
        {
            var f = smrs.FirstOrDefault(s => s.name == follower);
            var l = smrs.Where(s => leaders.Contains(s.name)).ToArray();
            if (f == null || l.Length == 0) continue;
            int sealed_ = SealSeam(root, f, l, band, out int rims);
            if (sealed_ > 0)
                holes.Add($"{follower} sealed to {string.Join("/", leaders)} ({rims} rim(s), {sealed_} vertices within {band * 100f:0} cm)");
        }
        foreach (var smr in smrs)
        {
            int caps = CapUncoveredHoles(root, smr, smrs);
            if (caps > 0) holes.Add($"{smr.name} {caps} open hole(s) capped");
        }

        Debug.Log($"BirdyModelPostprocessor: {rigidParts} part(s) made rigid to {HeadBone}, " +
                  $"{rigidVerts} vertex weight(s) set to 100% {HeadBone}; accessories rebound off the " +
                  $"wings: {(report.Count > 0 ? string.Join("; ", report) : "none")}; {rotor}; " +
                  $"holes: {(holes.Count > 0 ? string.Join("; ", holes) : "none")}.");
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

    static float ArmShare(List<(Transform bone, float w)>[] influences) => Share(influences, IsArmChain);

    /// <summary>Share of all the weight that is on bones matching <paramref name="chain"/>.</summary>
    static float Share(List<(Transform bone, float w)>[] influences, Func<string, bool> chain)
    {
        float on = 0f, total = 0f;
        foreach (var list in influences)
            foreach (var (bone, w) in list)
            {
                total += w;
                if (chain(bone.name)) on += w;
            }
        return total > 0f ? on / total : 0f;
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
        => MoveChainToSpine(influences, positions, spine, root, IsArmChain);

    /// <summary>Moves each vertex's weight on bones matching <paramref name="chain"/>
    /// to the spine bone nearest it.</summary>
    static void MoveChainToSpine(List<(Transform bone, float w)>[] influences, Vector3[] positions,
                                 Transform[] spine, GameObject root, Func<string, bool> chain)
    {
        if (spine.Length == 0) return;
        var spineAt = spine.Select(b => root.transform.InverseTransformPoint(b.position)).ToArray();
        for (int v = 0; v < influences.Length; v++)
        {
            float moved = 0f;
            var kept = new List<(Transform, float)>(4);
            foreach (var (bone, w) in influences[v])
            {
                if (chain(bone.name)) moved += w;
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

    /// <summary>
    /// Adds <see cref="RotorBone"/> under <paramref name="head"/> at the
    /// bottom of the rotor's mast, its up along the spin axis, and skins every
    /// rotor vertex 100% to it (rule 5). Returns a line for the import log.
    /// </summary>
    static string AddRotorBone(SkinnedMeshRenderer smr, Transform head)
    {
        var mesh = smr.sharedMesh;
        var toWorld = smr.transform.localToWorldMatrix;
        var verts = mesh.vertices;
        if (verts.Length == 0) return "rotor has no vertices; no rotor bone";
        for (int v = 0; v < verts.Length; v++) verts[v] = toWorld.MultiplyPoint3x4(verts[v]);

        // The blade disc's two widest directions, then its normal: the spin axis.
        AxesOf(verts, out var wide, out var across);
        var axis = Vector3.Cross(wide, across).normalized;
        if (Vector3.Dot(axis, head.up) < 0f) axis = -axis;
        across = Vector3.Cross(axis, wide).normalized;

        // The hub: the middle of the blades' span in the disc's plane (the two
        // blades are symmetric about it), at the lowest point along the axis.
        float wMin = float.PositiveInfinity, wMax = float.NegativeInfinity;
        float aMin = float.PositiveInfinity, aMax = float.NegativeInfinity, hMin = float.PositiveInfinity;
        foreach (var p in verts)
        {
            float w = Vector3.Dot(p, wide), a = Vector3.Dot(p, across), h = Vector3.Dot(p, axis);
            wMin = Mathf.Min(wMin, w); wMax = Mathf.Max(wMax, w);
            aMin = Mathf.Min(aMin, a); aMax = Mathf.Max(aMax, a);
            hMin = Mathf.Min(hMin, h);
        }
        var origin = wide * ((wMin + wMax) * 0.5f) + across * ((aMin + aMax) * 0.5f) + axis * hMin;
        var forward = Vector3.ProjectOnPlane(Vector3.forward, axis);
        if (forward.sqrMagnitude < 1e-6f) forward = across;

        var bone = new GameObject(RotorBone).transform;
        bone.SetParent(head, false);
        bone.SetPositionAndRotation(origin, Quaternion.LookRotation(forward.normalized, axis));

        // The import hierarchy is in the bind pose (see Write), so the bind
        // pose is read off the transforms.
        var bones = smr.bones.ToList();
        var bindposes = mesh.bindposes.ToList();
        bones.Add(bone);
        bindposes.Add(bone.worldToLocalMatrix * toWorld);
        int index = bones.Count - 1;
        var weights = new BoneWeight[verts.Length];
        for (int v = 0; v < weights.Length; v++) weights[v] = new BoneWeight { boneIndex0 = index, weight0 = 1f };
        mesh.bindposes = bindposes.ToArray();
        smr.bones = bones.ToArray();
        mesh.boneWeights = weights;

        float radius = 0f;
        foreach (var p in verts) radius = Mathf.Max(radius, Vector3.ProjectOnPlane(p - origin, axis).magnitude);
        return $"{RotorPart} skinned to a new {RotorBone} bone under {HeadBone} " +
               $"(axis {Vector3.Angle(axis, head.up):0.0}° from the head's up, blade radius {radius:0.00} m)";
    }

    /// <summary>The two directions of largest spread of a point cloud (its
    /// principal axes), by power iteration from a fixed start so every import
    /// gives the same answer.</summary>
    static void AxesOf(Vector3[] points, out Vector3 first, out Vector3 second)
    {
        var mean = Vector3.zero;
        foreach (var p in points) mean += p;
        mean /= points.Length;
        var c = new float[3, 3];
        foreach (var p in points)
        {
            var d = p - mean;
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++) c[i, j] += d[i] * d[j];
        }
        Vector3 Times(Vector3 x) => new(c[0, 0] * x.x + c[0, 1] * x.y + c[0, 2] * x.z,
                                        c[1, 0] * x.x + c[1, 1] * x.y + c[1, 2] * x.z,
                                        c[2, 0] * x.x + c[2, 1] * x.y + c[2, 2] * x.z);
        first = new Vector3(1f, 0.3f, 0.2f).normalized;
        for (int k = 0; k < 200; k++) first = Times(first).normalized;
        second = Vector3.Cross(first, new Vector3(0.2f, 1f, 0.3f)).normalized;
        for (int k = 0; k < 200; k++)
        {
            second = Times(second);
            second = (second - Vector3.Dot(second, first) * first).normalized;
        }
    }

    // --------------------------------------------------------------- holes

    /// <summary>
    /// Rule 6. Compares each face of <paramref name="part"/> with the nearest
    /// face of its <paramref name="twin"/> mirrored across X. If they face
    /// opposite ways on average (the part was exported inside out), reverses
    /// every triangle and normal and recomputes the tangents. Returns whether
    /// it turned the part; <paramref name="facing"/> is the mean dot, +1 when
    /// they agree.
    /// </summary>
    static bool TurnRightWayOut(GameObject root, SkinnedMeshRenderer part, SkinnedMeshRenderer twin, out float facing)
    {
        var (pc, pn) = Faces(root, part);
        var (tc, tn) = Faces(root, twin);
        float dot = 0f;
        int n = 0;
        for (int i = 0; i < pc.Count; i += 2)
        {
            var m = pc[i];
            m.x = -m.x;
            int best = 0;
            float bd = float.PositiveInfinity;
            for (int j = 0; j < tc.Count; j++)
            {
                float d = (tc[j] - m).sqrMagnitude;
                if (d < bd) { bd = d; best = j; }
            }
            var mirrored = tn[best];
            mirrored.x = -mirrored.x;
            dot += Vector3.Dot(mirrored, pn[i]);
            n++;
        }
        facing = n > 0 ? dot / n : 1f;
        if (facing >= 0f) return false;

        var mesh = part.sharedMesh;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var tris = mesh.GetTriangles(s);
            for (int t = 0; t < tris.Length; t += 3) (tris[t + 1], tris[t + 2]) = (tris[t + 2], tris[t + 1]);
            mesh.SetTriangles(tris, s, false);
        }
        var normals = mesh.normals;
        for (int v = 0; v < normals.Length; v++) normals[v] = -normals[v];
        mesh.normals = normals;
        if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) mesh.RecalculateTangents();
        return true;
    }

    /// <summary>Face centres and geometric normals (Unity's winding) of a part, in root space.</summary>
    static (List<Vector3> centres, List<Vector3> normals) Faces(GameObject root, SkinnedMeshRenderer smr)
    {
        var p = RootSpace(root, smr);
        var tris = smr.sharedMesh.triangles;
        var centres = new List<Vector3>(tris.Length / 3);
        var normals = new List<Vector3>(tris.Length / 3);
        for (int t = 0; t < tris.Length; t += 3)
        {
            Vector3 a = p[tris[t]], b = p[tris[t + 1]], c = p[tris[t + 2]];
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.sqrMagnitude < 1e-16f) continue;
            centres.Add((a + b + c) / 3f);
            normals.Add(cross.normalized);
        }
        return (centres, normals);
    }

    /// <summary>
    /// Rule 8. The rims of <paramref name="follower"/> that lie on its
    /// <paramref name="leaders"/> at bind (open edges whose median distance to
    /// them is within <see cref="SeamTouch"/>) take, vertex by vertex, the
    /// weights of the nearest leader vertex, blending back to the follower's
    /// own over <paramref name="band"/>. The two then move as one at the seam,
    /// and the follower stretches instead of tearing away. Returns how many
    /// vertices changed.
    /// </summary>
    static int SealSeam(GameObject root, SkinnedMeshRenderer follower, SkinnedMeshRenderer[] leaders, float band,
                        out int rims)
    {
        rims = 0;
        var fp = RootSpace(root, follower);
        var lp = leaders.Select(l => RootSpace(root, l)).ToArray();
        var lw = leaders.Select(Read).ToArray();
        (float d, int leader, int i) Touch(Vector3 q)
        {
            (float d, int leader, int i) best = (float.PositiveInfinity, -1, -1);
            for (int l = 0; l < lp.Length; l++)
            {
                var (d, i) = Nearest(lp[l], q);
                if (d < best.d) best = (d, l, i);
            }
            return best;
        }

        var rim = new List<int>();
        foreach (var loop in new Rims(follower.sharedMesh).Loops)
        {
            var d = loop.Select(v => Touch(fp[v]).d).OrderBy(x => x).ToList();
            if (d[d.Count / 2] > SeamTouch) continue;
            rims++;
            rim.AddRange(loop);
        }
        if (rim.Count == 0) return 0;

        // The weights of the leader surface each rim vertex sits on.
        var rimWeights = rim.Select(v => { var t = Touch(fp[v]); return lw[t.leader][t.i]; }).ToArray();
        var fw = Read(follower);
        int changed = 0;
        for (int v = 0; v < fp.Length; v++)
        {
            int nearest = -1;
            float best = band;
            for (int r = 0; r < rim.Count; r++)
            {
                float d = Vector3.Distance(fp[v], fp[rim[r]]);
                if (d < best) { best = d; nearest = r; }
            }
            if (nearest < 0) continue;
            float u = 1f - best / band;
            float s = u * u * (3f - 2f * u);
            var blended = new List<(Transform, float)>(8);
            foreach (var (bone, w) in rimWeights[nearest]) blended.Add((bone, w * s));
            foreach (var (bone, w) in fw[v]) blended.Add((bone, w * (1f - s)));
            fw[v] = blended;
            changed++;
        }
        Write(follower, fw);
        return changed;
    }

    static (float d, int i) Nearest(Vector3[] points, Vector3 q)
    {
        int best = 0;
        float bd = float.PositiveInfinity;
        for (int i = 0; i < points.Length; i++)
        {
            float d = (points[i] - q).sqrMagnitude;
            if (d < bd) { bd = d; best = i; }
        }
        return (Mathf.Sqrt(bd), best);
    }

    /// <summary>
    /// Rule 9. Fills each open hole in <paramref name="smr"/> that nothing
    /// covers at bind (its rim at least <see cref="UncoveredGap"/> from every
    /// other part, its rim spanning under <see cref="MaxCapSize"/>) with a
    /// fan of triangles from the rim's centre to each of its open edges (so
    /// any rim shape works, not only a simple ring). The cap faces away from the
    /// part's centre, each triangle takes the texture colour of its rim vertex
    /// (so it matches the surface around it, whatever the UV layout), and its
    /// vertices are skinned like the rim (the centre like the rim's average).
    /// Returns how many holes it capped.
    /// </summary>
    static int CapUncoveredHoles(GameObject root, SkinnedMeshRenderer smr, SkinnedMeshRenderer[] all)
    {
        var mesh = smr.sharedMesh;
        if (mesh.blendShapeCount > 0) return 0;
        var rims = new Rims(mesh);
        var mine = RootSpace(root, smr);
        var others = all.Where(s => s != smr).Select(s => RootSpace(root, s)).ToArray();
        var verts = mesh.vertices;
        var holes = new List<(int a, int b)[]>();
        foreach (var edges in rims.Edges)
        {
            if (edges.Length < 3) continue;
            var span = new Bounds(mine[edges[0].a], Vector3.zero);
            foreach (var (ea, eb) in edges) { span.Encapsulate(mine[ea]); span.Encapsulate(mine[eb]); }
            if (span.size.magnitude > MaxCapSize) continue;
            var gaps = edges.Select(e => others.Min(o => Nearest(o, mine[e.a]).d)).OrderBy(x => x).ToList();
            if (gaps[gaps.Count / 2] < UncoveredGap) continue;
            holes.Add(edges);
        }
        if (holes.Count == 0) return 0;

        var centre = verts.Aggregate(Vector3.zero, (a, b) => a + b) / verts.Length;
        var normals = new List<Vector3>(); mesh.GetNormals(normals);
        if (normals.Count != verts.Length) return 0;
        var uvs = new List<Vector2>(); mesh.GetUVs(0, uvs);
        var extraUvs = new List<(int channel, List<Vector4> values)>();
        for (int ch = 1; ch < 8; ch++)
        {
            var l = new List<Vector4>();
            mesh.GetUVs(ch, l);
            if (l.Count == verts.Length) extraUvs.Add((ch, l));
        }
        var weights = new List<BoneWeight>(mesh.boneWeights);
        var positions = new List<Vector3>(verts);
        var colors = new List<Color>(); mesh.GetColors(colors);
        var tris = new List<int>(mesh.GetTriangles(0));
        foreach (var edges in holes)
        {
            var rimVerts = edges.SelectMany(e => new[] { e.a, e.b }).Distinct().ToArray();
            var c = rimVerts.Aggregate(Vector3.zero, (acc, v) => acc + verts[v]) / rimVerts.Length;
            var outward = (c - centre).normalized;
            var cw = Average(rimVerts.Select(v => weights[v]));
            foreach (var (ea, eb) in edges)
            {
                int a = ea, b = eb;
                Vector3 pa = verts[a], pb = verts[b];
                var n = Vector3.Cross(pa - c, pb - c);
                if (n.sqrMagnitude < 1e-16f) continue;
                if (Vector3.Dot(n, outward) < 0f) { (a, b) = (b, a); (pa, pb) = (pb, pa); n = -n; }
                n.Normalize();
                int start = positions.Count;
                positions.Add(c); positions.Add(pa); positions.Add(pb);
                for (int k = 0; k < 3; k++) normals.Add(n);
                var uv = uvs.Count > 0 ? uvs[ea] : Vector2.zero;
                if (uvs.Count > 0) { uvs.Add(uv); uvs.Add(uv); uvs.Add(uv); }
                if (colors.Count > 0) { colors.Add(colors[a]); colors.Add(colors[a]); colors.Add(colors[b]); }
                foreach (var (_, values) in extraUvs) { values.Add(values[a]); values.Add(values[a]); values.Add(values[b]); }
                weights.Add(cw); weights.Add(weights[a]); weights.Add(weights[b]);
                tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
            }
        }
        if (positions.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(positions);
        mesh.SetNormals(normals);
        if (uvs.Count > 0) mesh.SetUVs(0, uvs);
        foreach (var (channel, values) in extraUvs) mesh.SetUVs(channel, values);
        if (colors.Count > 0) mesh.SetColors(colors);
        mesh.boneWeights = weights.ToArray();
        mesh.SetTriangles(tris, 0, true);
        if (mesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.Tangent)) mesh.RecalculateTangents();
        return holes.Count;
    }

    /// <summary>The weighted average of several vertices' bone weights, top four kept.</summary>
    static BoneWeight Average(IEnumerable<BoneWeight> ws)
    {
        var sum = new Dictionary<int, float>();
        void Add(int i, float w) { if (w > 0f) sum[i] = (sum.TryGetValue(i, out var s) ? s : 0f) + w; }
        foreach (var w in ws)
        {
            Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1);
            Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
        }
        var top = sum.OrderByDescending(k => k.Value).ThenBy(k => k.Key).Take(4).ToList();
        float total = top.Sum(k => k.Value);
        var bw = new BoneWeight();
        for (int k = 0; k < top.Count; k++)
        {
            float w = total > 0f ? top[k].Value / total : 0f;
            switch (k)
            {
                case 0: bw.boneIndex0 = top[k].Key; bw.weight0 = w; break;
                case 1: bw.boneIndex1 = top[k].Key; bw.weight1 = w; break;
                case 2: bw.boneIndex2 = top[k].Key; bw.weight2 = w; break;
                default: bw.boneIndex3 = top[k].Key; bw.weight3 = w; break;
            }
        }
        return bw;
    }

    /// <summary>
    /// A mesh's open edges (used by one triangle), with vertices welded by
    /// position so UV seams don't count as edges, grouped into connected rims.
    /// <see cref="Loops"/> lists every vertex index (split copies included)
    /// on each rim; <see cref="Edges"/> gives each rim's open edges as pairs of
    /// representative vertices.
    /// </summary>
    sealed class Rims
    {
        public readonly List<int[]> Loops = new();
        public readonly List<(int a, int b)[]> Edges = new();

        public Rims(Mesh mesh)
        {
            var verts = mesh.vertices;
            var key = new Dictionary<Vector3Int, int>();
            var weld = new int[verts.Length];
            var rep = new List<int>();
            for (int v = 0; v < verts.Length; v++)
            {
                var k = new Vector3Int(Mathf.RoundToInt(verts[v].x * 1e4f), Mathf.RoundToInt(verts[v].y * 1e4f),
                                       Mathf.RoundToInt(verts[v].z * 1e4f));
                if (!key.TryGetValue(k, out int id)) { id = rep.Count; key[k] = id; rep.Add(v); }
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
            var open = count.Where(kv => kv.Value == 1).Select(kv => kv.Key).OrderBy(k => k).ToList();
            var adj = new SortedDictionary<int, List<int>>();
            foreach (var (a, b) in open)
            {
                if (!adj.TryGetValue(a, out var la)) adj[a] = la = new List<int>();
                if (!adj.TryGetValue(b, out var lb)) adj[b] = lb = new List<int>();
                la.Add(b); lb.Add(a);
            }
            var members = new Dictionary<int, List<int>>();
            for (int v = 0; v < weld.Length; v++)
            {
                if (!adj.ContainsKey(weld[v])) continue;
                if (!members.TryGetValue(weld[v], out var l)) members[weld[v]] = l = new List<int>();
                l.Add(v);
            }
            var component = new Dictionary<int, int>();
            foreach (int start in adj.Keys)
            {
                if (component.ContainsKey(start)) continue;
                int id = Loops.Count;
                var comp = new List<int>();
                var stack = new Stack<int>();
                stack.Push(start);
                component[start] = id;
                while (stack.Count > 0)
                {
                    int x = stack.Pop();
                    comp.Add(x);
                    foreach (int y in adj[x])
                        if (!component.ContainsKey(y)) { component[y] = id; stack.Push(y); }
                }
                Loops.Add(comp.SelectMany(w => members[w]).OrderBy(v => v).ToArray());
                Edges.Add(open.Where(e => component.TryGetValue(e.Item1, out int c) && c == id)
                              .Select(e => (rep[e.Item1], rep[e.Item2])).ToArray());
            }
        }
    }
}
