using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Makes Birdy's helicopter rotor (and the most head-bound vertices of his
/// head) rigid to the Head bone at import time.
///
/// Tripo's auto-rigger skinned the rotor mesh about 97% to Head, with the rest
/// spread over the upper-arm and forearm twist bones. With the arms still
/// that is invisible, but when a clip folds the wings down, the blades follow
/// the arms by those few percent: QA measured blade tips drifting up to 0.37 m
/// relative to the head, drooping across the face in idle, talk and death.
///
/// Two rules, applied to Birdy.fbx only (exact path match; every other model
/// passes through untouched):
/// 1. A mesh part whose average Head weight is at least
///    <see cref="RigidPartThreshold"/> (the rotor, about 0.97) becomes 100%
///    Head. Its stray low-weight vertices are the blade tips that droop, so a
///    per-vertex rule alone would leave exactly the visible ones bent.
/// 2. Any other vertex with Head weight at least <see cref="RigidVertexThreshold"/>
///    becomes 100% Head, so the helmet and face don't shear by the last few percent.
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

    public override uint GetVersion() => 1;

    void OnPostprocessModel(GameObject root)
    {
        if (!string.Equals(assetPath, ModelPath, StringComparison.OrdinalIgnoreCase)) return;

        int rigidParts = 0, rigidVerts = 0;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var mesh = smr.sharedMesh;
            var bones = smr.bones;
            if (mesh == null || bones == null) continue;
            int head = Array.FindIndex(bones, b => b != null && b.name == HeadBone);
            if (head < 0) continue;

            var weights = mesh.boneWeights;
            if (weights.Length == 0) continue;
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
            if (!changed) continue;
            mesh.boneWeights = weights;
            if (wholePart) rigidParts++;
        }
        Debug.Log($"BirdyModelPostprocessor: {rigidParts} part(s) made rigid to {HeadBone}, " +
                  $"{rigidVerts} vertex weight(s) set to 100% {HeadBone}.");
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
