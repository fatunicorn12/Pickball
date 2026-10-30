using System.Collections.Generic;
using System.IO;
using Pickleball.Sandbox.Weapon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Adds <see cref="WeaponClearance"/> to the player's weapon so the blade stays
/// out of the head in every animation.
///
/// Tools ▸ Pickleball ▸ Setup Weapon Clearance. Setup Weapon also runs it (it
/// rebuilds the WeaponSocket, so the component has to be re-added each time).
/// Safe to run repeatedly.
///
/// The profile asset is created only when missing, with the head sphere fitted
/// to the skinned mesh; after that it belongs to whoever tunes it, and re-runs
/// leave it alone.
/// </summary>
public static class WeaponClearanceSetup
{
    const string ProfileDir = "Assets/_Project/ScriptableObjects/Weapon";
    const string ProfilePath = ProfileDir + "/ChibiWeaponClearance.asset";

    /// <summary>The ruler sword's profile (see <see cref="LoadOrCreateRulerProfile"/>).</summary>
    public const string RulerProfilePath = ProfileDir + "/RulerWeaponClearance.asset";

    /// <summary>The ruler's blade is 22 cm wide and 7 cm thick, with a 23 cm
    /// guard disc. Seen edge-on from the head it is thin, but a swing turns its
    /// flat to the head too, so the radius covers most of the half-width
    /// rather than the half-thickness the compass needs.</summary>
    const float RulerBladeRadius = 0.08f;
    const float RulerMargin = 0.05f;
    /// <summary>More of the push on the shoulder: a heavy blade is carried by
    /// the arm, and a wrist bent far under it looks broken.</summary>
    const float RulerShoulderShare = 0.6f;

    /// <summary>
    /// Torso spheres, for the ruler only: its wide blade comes across the body
    /// in the backhand sweeps (Attack2's middle runs the blade through the
    /// waist; the compass does the same, but thin, it barely shows). Centres in
    /// each bone's space, m; radii sized to the chibi's torso under the cloak
    /// (shoulder joints 0.27 m apart), not the cloak's flare, so the arm isn't
    /// pushed for cloth.
    /// </summary>
    static readonly WeaponClearanceProfile.Volume[] RulerTorso =
    {
        new() { bone = HumanBodyBones.UpperChest, centre = new Vector3(0f, 0.02f, 0f), radius = 0.15f },
        new() { bone = HumanBodyBones.Hips, centre = new Vector3(0f, 0.08f, 0f), radius = 0.16f },
    };

    /// <summary>
    /// Share of the head's vertices the fitted sphere must contain. What reads
    /// as "through the head" is the blade threading the hair spikes, so the
    /// sphere follows the silhouette: on the chibi a 90% sphere (0.34 m) let
    /// walk and run cut 2-6 cm into the spikes. 99% (0.37 m) covers them while
    /// ignoring the last few spike tips, which would otherwise size the sphere
    /// to a single outlier.
    /// </summary>
    const float HeadCoverage = 0.99f;

    [MenuItem("Tools/Pickleball/Setup Weapon Clearance")]
    public static void Run()
    {
        var player = GameObject.Find("Chibi");
        if (player == null)
        {
            Debug.LogError("No 'Chibi' in the scene — run Setup Chibi Sandbox first.");
            return;
        }
        if (Attach(player)) AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Put a WeaponClearance on the player's WeaponSocket and point it at the
    /// blade ends and the profile. Returns false if Setup Weapon hasn't run.
    /// </summary>
    public static bool Attach(GameObject player)
    {
        var animator = player.GetComponentInChildren<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            Debug.LogError("Player has no Humanoid avatar; cannot set up weapon clearance.");
            return false;
        }

        // Setup Weapon always uses the right hand, but the handedness is read
        // off wherever the socket actually is rather than assumed. Writing a
        // fixed `true` flipped a left-handed rig back to right on every re-run.
        bool rightHanded = true;
        var socket = FindSocket(animator, HumanBodyBones.RightHand);
        if (socket == null)
        {
            socket = FindSocket(animator, HumanBodyBones.LeftHand);
            rightHanded = false;
        }
        var bladeBase = socket != null ? socket.Find("BladeBase") : null;
        var bladeTip = socket != null ? socket.Find("BladeTip") : null;
        if (bladeBase == null || bladeTip == null)
        {
            Debug.LogError("No WeaponSocket with BladeBase/BladeTip on either hand — " +
                           "run Tools ▸ Pickleball ▸ Setup Weapon first.");
            return false;
        }

        var profile = LoadOrCreateProfile(animator);
        if (profile == null) return false;

        // TryGetComponent, not `??`: in the Editor a missing component can come
        // back as Unity's fake-null object, which `??` treats as present.
        if (!socket.TryGetComponent(out WeaponClearance clearance))
            clearance = socket.gameObject.AddComponent<WeaponClearance>();
        var so = new SerializedObject(clearance);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.FindProperty("bladeBase").objectReferenceValue = bladeBase;
        so.FindProperty("bladeTip").objectReferenceValue = bladeTip;
        so.FindProperty("rightHanded").boolValue = rightHanded;
        so.FindProperty("profile").objectReferenceValue = profile;
        so.ApplyModifiedPropertiesWithoutUndo();

        // SetDirty alone doesn't mark a scene object's scene as modified, so a
        // re-run that only rewired an existing component was never saved.
        EditorUtility.SetDirty(clearance);
        if (!EditorApplication.isPlaying)
            EditorSceneManager.MarkSceneDirty(socket.gameObject.scene);
        Debug.Log($"Weapon clearance on {socket.parent.name}/{socket.name}, profile {ProfilePath}.");
        return true;
    }

    /// <summary>
    /// The ruler sword's clearance profile, created when missing from the
    /// chibi's (same head sphere, same arm settings) with a blade radius for a
    /// 22 cm-wide blade and two torso spheres. Like the chibi's, it belongs to
    /// whoever tunes it once it exists. A <see cref="Pickleball.Sandbox.Inventory.WeaponDefinition"/>
    /// hands it to <see cref="WeaponClearance.SetProfile"/> on equip.
    /// </summary>
    /// <returns>Null if the chibi's profile doesn't exist yet (run Setup
    /// Weapon Clearance first).</returns>
    public static WeaponClearanceProfile LoadOrCreateRulerProfile()
    {
        var ruler = AssetDatabase.LoadAssetAtPath<WeaponClearanceProfile>(RulerProfilePath);
        if (ruler != null) return ruler;
        var chibi = AssetDatabase.LoadAssetAtPath<WeaponClearanceProfile>(ProfilePath);
        if (chibi == null)
        {
            Debug.LogError($"No {ProfilePath} to base the ruler's clearance on: run Tools ▸ Pickleball ▸ " +
                           "Setup Weapon Clearance first.");
            return null;
        }

        ruler = Object.Instantiate(chibi);
        var volumes = new List<WeaponClearanceProfile.Volume>(chibi.Volumes);
        foreach (var torso in RulerTorso)
            if (volumes.TrueForAll(v => v.bone != torso.bone))
                volumes.Add(torso);
        ruler.SetVolumes(volumes);
        var so = new SerializedObject(ruler);
        so.FindProperty("bladeRadius").floatValue = RulerBladeRadius;
        so.FindProperty("margin").floatValue = RulerMargin;
        so.FindProperty("shoulderShare").floatValue = RulerShoulderShare;
        so.ApplyModifiedPropertiesWithoutUndo();

        Directory.CreateDirectory(ProfileDir);
        AssetDatabase.CreateAsset(ruler, RulerProfilePath);
        Debug.Log($"Created {RulerProfilePath} from {ProfilePath}: blade radius {RulerBladeRadius} m, " +
                  $"margin {RulerMargin} m, {volumes.Count} volumes.");
        return ruler;
    }

    static Transform FindSocket(Animator animator, HumanBodyBones hand)
    {
        var bone = animator.GetBoneTransform(hand);
        return bone != null ? bone.Find("WeaponSocket") : null;
    }

    static WeaponClearanceProfile LoadOrCreateProfile(Animator animator)
    {
        var profile = AssetDatabase.LoadAssetAtPath<WeaponClearanceProfile>(ProfilePath);
        if (profile != null) return profile;

        if (!TryFitHead(animator, out var head)) return null;

        Directory.CreateDirectory(ProfileDir);
        profile = ScriptableObject.CreateInstance<WeaponClearanceProfile>();
        profile.SetVolumes(new List<WeaponClearanceProfile.Volume> { head });
        AssetDatabase.CreateAsset(profile, ProfilePath);
        Debug.Log($"Created {ProfilePath}: head sphere centre {head.centre:F3}, " +
                  $"radius {head.radius:F3} m.");
        return profile;
    }

    /// <summary>
    /// Fit a sphere to the vertices skinned mostly to the head bone, in the
    /// head's own space, so it rides the head at runtime with no further work.
    ///
    /// Bind poses map mesh space straight into each bone's local space, so the
    /// fit is exact regardless of the pose the scene happens to be in.
    /// </summary>
    static bool TryFitHead(Animator animator, out WeaponClearanceProfile.Volume volume)
    {
        volume = default;
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var skin = animator.GetComponentInChildren<SkinnedMeshRenderer>();
        if (head == null || skin == null || skin.sharedMesh == null)
        {
            Debug.LogError("Need a Head bone and a SkinnedMeshRenderer to fit the head sphere.");
            return false;
        }

        int headIndex = System.Array.IndexOf(skin.bones, head);
        if (headIndex < 0)
        {
            Debug.LogError("The skinned mesh isn't bound to the avatar's Head bone.");
            return false;
        }

        var mesh = skin.sharedMesh;
        var vertices = mesh.vertices;
        var weights = mesh.boneWeights;
        var toHead = mesh.bindposes[headIndex];

        var points = new List<Vector3>();
        for (int i = 0; i < vertices.Length; i++)
        {
            var w = weights[i];
            float onHead = (w.boneIndex0 == headIndex ? w.weight0 : 0f)
                         + (w.boneIndex1 == headIndex ? w.weight1 : 0f)
                         + (w.boneIndex2 == headIndex ? w.weight2 : 0f)
                         + (w.boneIndex3 == headIndex ? w.weight3 : 0f);
            if (onHead >= 0.5f) points.Add(toHead.MultiplyPoint3x4(vertices[i]));
        }
        if (points.Count == 0)
        {
            Debug.LogError("No vertices are skinned to the Head bone.");
            return false;
        }

        var bounds = new Bounds(points[0], Vector3.zero);
        foreach (var p in points) bounds.Encapsulate(p);

        var distances = new List<float>(points.Count);
        foreach (var p in points) distances.Add((p - bounds.center).magnitude);
        distances.Sort();

        volume = new WeaponClearanceProfile.Volume
        {
            bone = HumanBodyBones.Head,
            centre = bounds.center,
            radius = distances[Mathf.Clamp((int)(HeadCoverage * (distances.Count - 1)),
                                           0, distances.Count - 1)],
        };
        return true;
    }
}
