using System;
using System.Collections.Generic;
using Pickleball.Sandbox.Hands;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Measures a character's hand and places its grab point there, then puts a
/// <see cref="HandGrip"/> on it (and a <see cref="FingerCurl"/> when the rig
/// has finger bones to curl).
///
/// No menu of its own: Setup Weapon builds the grab point with it, and Setup
/// Weapon Grip re-fits it in place. Both live in WeaponSetup.
///
/// Everything is measured in the hand bone's space at bind pose, read from
/// the skinned mesh's bind poses, so the answer doesn't depend on whatever
/// pose the scene happens to be in. That is the same approach the weapon
/// clearance setup uses to fit the head sphere.
/// </summary>
public static class HandGripSetup
{
    const string ProfileDir = "Assets/_Project/ScriptableObjects/Hands";
    const string ProfilePath = ProfileDir + "/HandGripProfile.asset";

    /// <summary>
    /// Share of a vertex's skinning that must belong to a bone for the vertex
    /// to count as part of it. A majority keeps the wrist's blend zone, which
    /// belongs as much to the forearm, out of the hand. On the chibi, 0.3 and
    /// 0.7 move the grab point less than 0.7 cm from where 0.5 puts it.
    /// </summary>
    const float OwnWeight = 0.5f;

    static readonly HumanBodyBones[] RightKnuckles =
    {
        HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal,
        HumanBodyBones.RightRingProximal, HumanBodyBones.RightLittleProximal,
    };

    static readonly HumanBodyBones[] LeftKnuckles =
    {
        HumanBodyBones.LeftIndexProximal, HumanBodyBones.LeftMiddleProximal,
        HumanBodyBones.LeftRingProximal, HumanBodyBones.LeftLittleProximal,
    };

    /// <summary>
    /// The shared hand-grip profile, created with defaults if missing. After
    /// that the asset belongs to whoever tunes it; re-runs never overwrite it.
    /// </summary>
    public static HandGripProfile LoadOrCreateProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<HandGripProfile>(ProfilePath);
        if (profile != null) return profile;

        ChibiSandboxSetup.EnsureFolderPublic(ProfileDir);
        profile = ScriptableObject.CreateInstance<HandGripProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);
        Debug.Log($"Created {ProfilePath} with default grip settings.");
        return profile;
    }

    /// <summary>The proximal finger joints the avatar maps on that hand, index
    /// first; empty on a rig without finger bones (every Tripo rig).</summary>
    public static List<Transform> Knuckles(Animator animator, bool rightHand)
    {
        var list = new List<Transform>();
        foreach (var b in rightHand ? RightKnuckles : LeftKnuckles)
        {
            var t = animator.GetBoneTransform(b);
            if (t != null) list.Add(t);
        }
        return list;
    }

    /// <summary>
    /// Works out the grab point for one hand from the rig.
    /// </summary>
    /// <param name="how">What was measured, for the log; or why it failed.</param>
    /// <returns>False if the hand has no skinned mesh to measure.</returns>
    public static bool TrySolve(Animator animator, bool rightHand, HandGripProfile profile,
                                out GripFrame frame, out string how)
    {
        frame = default;
        var hand = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
        var lowerArm = animator.GetBoneTransform(rightHand ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
        if (hand == null || lowerArm == null)
        {
            how = "the avatar maps no hand or lower arm";
            return false;
        }

        var skin = FindSkin(animator, hand, out int handIndex);
        if (skin == null)
        {
            how = "no SkinnedMeshRenderer is skinned to the hand bone";
            return false;
        }

        var mesh = skin.sharedMesh;
        var bindposes = mesh.bindposes;
        Matrix4x4 toHand = bindposes[handIndex];   // mesh space -> hand bone space, at bind
        Vector3 wrist = Vector3.zero;

        // Where the forearm points: from the elbow to the wrist at bind pose.
        int lowerIndex = Array.IndexOf(skin.bones, lowerArm);
        Vector3 armAxis = lowerIndex >= 0
            ? wrist - toHand.MultiplyPoint3x4(bindposes[lowerIndex].inverse.MultiplyPoint3x4(Vector3.zero))
            : hand.InverseTransformDirection(hand.position - lowerArm.position);

        // The humanoid T-pose convention: thumbs point the way the character
        // faces. Only used when the rig has no finger joints to say better.
        Vector3 forwardInMesh = skin.transform.InverseTransformDirection(animator.transform.forward);
        Vector3 thumbHint = toHand.MultiplyVector(forwardInMesh);

        float tilt = profile != null ? profile.HandleTilt : 0f;
        float gripRadius = profile != null ? profile.GripRadius : 0.015f;

        var vertices = mesh.vertices;
        var weights = mesh.boneWeights;
        if (weights.Length != vertices.Length)
        {
            how = $"'{mesh.name}' has no bone weights";
            return false;
        }

        var knuckleBones = Knuckles(animator, rightHand);
        if (knuckleBones.Count > 0)
        {
            // Rig with fingers: the fist closes in front of the palm.
            var knuckles = new List<Vector3>();
            var fingerIndices = new List<int>();
            foreach (var k in knuckleBones)
            {
                int ki = Array.IndexOf(skin.bones, k);
                fingerIndices.Add(ki);
                knuckles.Add(ki >= 0
                    ? toHand.MultiplyPoint3x4(bindposes[ki].inverse.MultiplyPoint3x4(Vector3.zero))
                    : hand.InverseTransformPoint(k.position));
            }

            var palm = new List<Vector3>();
            var fingers = new List<Vector3>();
            for (int v = 0; v < vertices.Length; v++)
            {
                if (Share(weights[v], handIndex) >= OwnWeight)
                {
                    palm.Add(toHand.MultiplyPoint3x4(vertices[v]));
                    continue;
                }
                foreach (int fi in fingerIndices)
                {
                    if (fi < 0 || Share(weights[v], fi) < OwnWeight) continue;
                    fingers.Add(toHand.MultiplyPoint3x4(vertices[v]));
                    break;
                }
            }

            if (!GripFrameSolver.TrySolveClosedFist(wrist, knuckles, thumbHint, rightHand, palm, fingers,
                                                    gripRadius, tilt, out frame))
            {
                how = "the knuckles give no usable hand direction";
                return false;
            }
            how = $"closed fist from {knuckles.Count} knuckle joints, {palm.Count} palm and " +
                  $"{fingers.Count} finger vertices; grab point {frame.Position:F3} in {hand.name}";
            return true;
        }

        // Rig without fingers: the centre of the hand's volume, so the hand
        // mesh itself closes round the handle.
        var tris = mesh.triangles;
        var corners = new List<Vector3>();
        for (int t = 0; t + 2 < tris.Length; t += 3)
        {
            int a = tris[t], b = tris[t + 1], c = tris[t + 2];
            if (Share(weights[a], handIndex) < OwnWeight || Share(weights[b], handIndex) < OwnWeight
                || Share(weights[c], handIndex) < OwnWeight)
                continue;
            corners.Add(toHand.MultiplyPoint3x4(vertices[a]));
            corners.Add(toHand.MultiplyPoint3x4(vertices[b]));
            corners.Add(toHand.MultiplyPoint3x4(vertices[c]));
        }

        if (!GripFrameSolver.TrySolveOpenHand(wrist, armAxis, thumbHint, rightHand, corners, tilt, out frame))
        {
            how = corners.Count == 0
                ? $"no triangles of '{mesh.name}' are skinned mostly to {hand.name}"
                : "the arm and facing directions are parallel";
            return false;
        }
        GripFrameSolver.TryVolumeCentroid(corners, wrist, out _, out float volume);
        how = $"no finger bones on {hand.name}, so the hand can't close: grab point at the centre of " +
              $"the hand's volume ({corners.Count / 3} triangles, {volume * 1e6f:0} cm³), " +
              $"{frame.Position:F3} in {hand.name}";
        return true;
    }

    /// <summary>
    /// Puts a <see cref="HandGrip"/> on the grab point, plus a
    /// <see cref="FingerCurl"/> when the rig has finger bones on that hand
    /// (removing a stale one when it doesn't). Safe to run repeatedly.
    /// </summary>
    public static HandGrip Attach(Animator animator, bool rightHand, Transform grabPoint, HandGripProfile profile)
    {
        // TryGetComponent, not `??`: a missing component can come back as
        // Unity's fake-null object in the Editor.
        if (!grabPoint.TryGetComponent(out HandGrip grip))
            grip = grabPoint.gameObject.AddComponent<HandGrip>();

        bool hasFingers = Knuckles(animator, rightHand).Count > 0
                          || animator.GetBoneTransform(rightHand ? HumanBodyBones.RightThumbProximal
                                                                 : HumanBodyBones.LeftThumbProximal) != null;
        grabPoint.TryGetComponent(out FingerCurl curl);
        if (hasFingers)
        {
            if (curl == null) curl = grabPoint.gameObject.AddComponent<FingerCurl>();
            var so = new SerializedObject(curl);
            so.FindProperty("animator").objectReferenceValue = animator;
            so.FindProperty("grip").objectReferenceValue = grip;
            so.FindProperty("profile").objectReferenceValue = profile;
            so.FindProperty("rightHand").boolValue = rightHand;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            if (curl != null) Object.DestroyImmediate(curl);
            Debug.Log($"{animator.name}: the avatar maps no finger bones, so the hand can't close on " +
                      "the grip (the clips' finger curves have nothing to drive). The grab point sits " +
                      "in the hand's volume instead. Add finger bones to the rig and map them to " +
                      "get a closing fist.");
        }
        EditorUtility.SetDirty(grip);
        return grip;
    }

    /// <summary>The skinned mesh bound to the hand bone, and that bone's index
    /// in its bone list.</summary>
    static SkinnedMeshRenderer FindSkin(Animator animator, Transform hand, out int handIndex)
    {
        foreach (var smr in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            handIndex = Array.IndexOf(smr.bones, hand);
            if (handIndex >= 0 && handIndex < smr.sharedMesh.bindposes.Length) return smr;
        }
        handIndex = -1;
        return null;
    }

    /// <summary>How much of a vertex's skinning belongs to one bone, 0-1.</summary>
    static float Share(BoneWeight w, int bone)
    {
        float s = 0f;
        if (w.boneIndex0 == bone) s += w.weight0;
        if (w.boneIndex1 == bone) s += w.weight1;
        if (w.boneIndex2 == bone) s += w.weight2;
        if (w.boneIndex3 == bone) s += w.weight3;
        return s;
    }
}
