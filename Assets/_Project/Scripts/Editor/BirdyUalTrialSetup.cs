using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Sandbox.Npc;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// A trial of Quaternius's Universal Animation Library (UAL) as the source of
/// Birdy's clips, and the maker of the one UAL clip Birdy ships: his death.
/// It never touches BirdyNpcSetup, Birdy.controller or Birdy.prefab.
///
/// <b>Adopted: the death only.</b> Rendered side by side on Birdy, Mixamo reads
/// better for the idles and the three talk emotes, and UAL1 Death01 beats both
/// Mixamo deaths. So the Death row is copied straight into Birdy's live clip
/// folder, Animation/Birdy/Death.anim, where BirdyNpcSetup picks it up like any
/// other clip; every other row stays in Animation/Quaternius/Birdy/ for the
/// trial controller.
///
/// Tools ▸ Pickleball ▸ Setup Birdy UAL Trial
///
/// <b>Source.</b> UAL1 and UAL2, the free "Standard" editions, CC0 1.0
/// (UAL1_License.txt, UAL2_License.txt beside the FBX), downloaded 2026-10-28
/// from opengameart.org/content/universal-animation-library and
/// opengameart.org/content/universal-animation-library-2. Each is one Unity FBX
/// holding a mannequin and every take. The OpenGameArt UAL1 (March 2025) is the
/// pre-v2.0 rig with Rigify <c>DEF-</c> bone names; UAL2 (January 2026) uses
/// UE-mannequin names. Both get an explicit bone map below.
///
/// <b>What it does, in order.</b>
/// 1. Imports both FBX as Humanoid, avatar <i>Create From This Model</i>, with
///    Bake Axis Conversion on (Quaternius's Unity_Setup.png), the explicit
///    bone map, and the arms of the stored reference pose straightened to a T.
///    Unity treats that pose as the T-pose, and both mannequins bind their
///    upper arms 4° below horizontal, which would otherwise lift Birdy's wings
///    by 4° in every clip.
/// 2. Lists only the mapped takes as the FBX's clips, with the settings
///    BirdyNpcSetup.ImportClip gives Mixamo clips: Loop Time and Loop Pose on
///    except for death, and root rotation, height and XZ baked into the pose
///    (height based upon the feet).
/// 3. Copies each mapped clip to Animation/Quaternius/Birdy/&lt;Role&gt;.anim
///    (the death to Animation/Birdy/Death.anim, above). A
///    UAL FBX holds dozens of takes, but BirdyNpcSetup.ImportClip only ever
///    uses a file's first take, so the clips are copied out rather than the
///    FBX dropped into Animation/Birdy/.
/// 4. Builds Animation/Quaternius/Birdy_UAL.controller with the parameters,
///    states and transitions BirdyNpcSetup builds into Birdy.controller, with
///    the UAL clips as the motions. Adopting the trial is then one field: the
///    Animator's controller on Birdy.prefab. The build ends with a parity check
///    against the live Birdy.controller and warns about any difference.
///
/// <b>Idempotent.</b> Import settings are written only when they differ; a
/// .anim is re-copied only when its source changed (a hash kept in the .anim's
/// .meta userData); the controller is rewritten in place (GUID kept) only when
/// its inputs changed (a hash in the controller's .meta userData). An
/// unchanged re-run writes nothing.
/// </summary>
public static class BirdyUalTrialSetup
{
    const string Root = "Assets/_Project/Animation/Quaternius";
    const string Ual1Path = Root + "/UAL1_Standard.fbx";
    const string Ual2Path = Root + "/UAL2_Standard.fbx";
    const string ClipDir = Root + "/Birdy";

    /// <summary>Birdy's live clip folder, which BirdyNpcSetup scans. Only the
    /// death is copied there (see the class summary).</summary>
    const string LiveClipDir = "Assets/_Project/Animation/Birdy";
    const string ControllerPath = Root + "/Birdy_UAL.controller";

    /// <summary>The live controller the trial mirrors. Read only.</summary>
    const string LiveControllerPath = "Assets/_Project/Animation/Controllers/Birdy.controller";

    /// <summary>Birdy's config, for the blend times baked into the controller. Read only.</summary>
    const string ConfigPath = "Assets/_Project/ScriptableObjects/Npc/NpcAnimationConfig.asset";

    /// <summary>
    /// The layout of BirdyNpcSetup.BuildController this script reproduces
    /// (BirdyNpcSetup.ControllerLayoutVersion). If that changes, port the
    /// change to <see cref="BuildController"/>; the parity check names what
    /// differs.
    /// </summary>
    const string MirroredLayoutVersion = "birdy-controller-2";

    /// <summary>Bump when the clip import or copy settings change, so every .anim is re-copied.</summary>
    const string ClipCopyVersion = "ual-clip-1";

    /// <summary>Arm bones further than this from horizontal in a source
    /// avatar's reference pose are rotated to it (Enforce T-Pose), °.</summary>
    const float TPoseToleranceDegrees = 2f;

    /// <summary>
    /// The mapped clips, in role order; the first Idle is the main idle and any
    /// later Idle is a random variant (NpcAnimatorDriver's IdleVariant).
    /// Chosen by rendering 20 candidate takes on Birdy (contact sheets, 2026-10-28):
    ///   Idle           UAL1 Idle_Loop: neutral standing idle, wings at the sides.
    ///   Idle_FoldArms  UAL2 Idle_FoldArms_Loop: a waiting fidget, wings crossed
    ///                  over the chest. Neither free edition has a look-around:
    ///                  Idle_No_Loop drops the head and shakes it (reads as
    ///                  "no"), Idle_Lantern_Loop holds a wing across the belly.
    ///   Talk_Talk      UAL1 Idle_Talking_Loop: wing tips gesture together in
    ///                  front of the chest.
    ///   Talk_Excited   UAL2 Yes: one wing pumps out to the side, a small nod.
    ///                  UAL1 Dance_Loop is livelier but reads as dancing.
    ///   Talk_Thinking  UAL2 Idle_TalkingPhone_Loop: one wing tip at the side of
    ///                  the head, the other on the hip; reads as "let me think".
    ///   Death          UAL1 Death01: from a plain stand, a stagger, then a fall
    ///                  onto the back with the feet up. Adopted: copied to
    ///                  Animation/Birdy/Death.anim, Birdy's live death. Without
    ///                  a lift the body and head end 11 cm into the floor; the
    ///                  config's death lift is tuned for it (0.10 m from t = 0.35
    ///                  to 0.6), and NpcRotor knocks the rotor off at t = 0.4.
    /// </summary>
    static readonly (string anim, NpcClipRole role, string source, string take)[] Clips =
    {
        ("Idle", NpcClipRole.Idle, Ual1Path, "Idle_Loop"),
        ("Idle_FoldArms", NpcClipRole.Idle, Ual2Path, "Idle_FoldArms_Loop"),
        ("Talk_Talk", NpcClipRole.Talk, Ual1Path, "Idle_Talking_Loop"),
        ("Talk_Excited", NpcClipRole.TalkExcited, Ual2Path, "Yes"),
        ("Talk_Thinking", NpcClipRole.TalkThinking, Ual2Path, "Idle_TalkingPhone_Loop"),
        ("Death", NpcClipRole.Death, Ual1Path, "Death01"),
    };

    /// <summary>
    /// Human bone → source bone, per FBX. Fingers stay unmapped: Birdy's
    /// avatar has none, so their muscles would be dropped anyway.
    /// </summary>
    static readonly Dictionary<string, Dictionary<string, string>> BoneMaps = new()
    {
        // UAL1 as on OpenGameArt: Rigify deform bones.
        [Ual1Path] = new Dictionary<string, string>
        {
            { "Hips", "DEF-hips" },
            { "Spine", "DEF-spine.001" }, { "Chest", "DEF-spine.002" }, { "UpperChest", "DEF-spine.003" },
            { "Neck", "DEF-neck" }, { "Head", "DEF-head" },
            { "LeftShoulder", "DEF-shoulder.L" }, { "LeftUpperArm", "DEF-upper_arm.L" },
            { "LeftLowerArm", "DEF-forearm.L" }, { "LeftHand", "DEF-hand.L" },
            { "RightShoulder", "DEF-shoulder.R" }, { "RightUpperArm", "DEF-upper_arm.R" },
            { "RightLowerArm", "DEF-forearm.R" }, { "RightHand", "DEF-hand.R" },
            { "LeftUpperLeg", "DEF-thigh.L" }, { "LeftLowerLeg", "DEF-shin.L" },
            { "LeftFoot", "DEF-foot.L" }, { "LeftToes", "DEF-toe.L" },
            { "RightUpperLeg", "DEF-thigh.R" }, { "RightLowerLeg", "DEF-shin.R" },
            { "RightFoot", "DEF-foot.R" }, { "RightToes", "DEF-toe.R" },
        },
        // UAL2: UE-mannequin names.
        [Ual2Path] = new Dictionary<string, string>
        {
            { "Hips", "pelvis" },
            { "Spine", "spine_01" }, { "Chest", "spine_02" }, { "UpperChest", "spine_03" },
            { "Neck", "neck_01" }, { "Head", "Head" },
            { "LeftShoulder", "clavicle_l" }, { "LeftUpperArm", "upperarm_l" },
            { "LeftLowerArm", "lowerarm_l" }, { "LeftHand", "hand_l" },
            { "RightShoulder", "clavicle_r" }, { "RightUpperArm", "upperarm_r" },
            { "RightLowerArm", "lowerarm_r" }, { "RightHand", "hand_r" },
            { "LeftUpperLeg", "thigh_l" }, { "LeftLowerLeg", "calf_l" },
            { "LeftFoot", "foot_l" }, { "LeftToes", "ball_l" },
            { "RightUpperLeg", "thigh_r" }, { "RightLowerLeg", "calf_r" },
            { "RightFoot", "foot_r" }, { "RightToes", "ball_r" },
        },
    };

    // ================================================================= menus

    [MenuItem("Tools/Pickleball/Setup Birdy UAL Trial")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Setup Birdy UAL Trial: exit Play mode first.");
            return;
        }

        var config = AssetDatabase.LoadAssetAtPath<NpcAnimationConfig>(ConfigPath);
        if (config == null)
        {
            Debug.LogError($"Setup Birdy UAL Trial: no {ConfigPath}. Run Setup Birdy NPC first.");
            return;
        }

        foreach (var path in new[] { Ual1Path, Ual2Path })
            if (!SetupSource(path)) return;

        var clips = ExtractClips();
        if (clips == null) return;
        var controller = BuildAnimator(clips, config);
        CheckParity(controller);
        Debug.Log($"Setup Birdy UAL Trial done. Controller: {ControllerPath}. Birdy.prefab is unchanged; " +
                  "to adopt, set BirdyModel's Animator controller to it.");
    }

    // ======================================================= the source FBX

    /// <summary>
    /// Humanoid import of one UAL FBX: settings, bone map, T-pose arms, and
    /// its mapped takes as clips. False (with an error) if the file is missing
    /// or its avatar is not a valid human.
    /// </summary>
    static bool SetupSource(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp == null)
        {
            Debug.LogError($"Setup Birdy UAL Trial: no model at {path}. Copy the Unity FBX from the " +
                           "UAL Standard zip there.");
            return false;
        }

        bool dirty = false;
        void Set<T>(T current, T wanted, Action<T> write)
        {
            if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
            write(wanted);
            dirty = true;
        }
        Set(imp.bakeAxisConversion, true, v => imp.bakeAxisConversion = v);
        Set(imp.globalScale, 1f, v => imp.globalScale = v);
        Set(imp.animationType, ModelImporterAnimationType.Human, v => imp.animationType = v);
        Set(imp.avatarSetup, ModelImporterAvatarSetup.CreateFromThisModel, v => imp.avatarSetup = v);
        Set(imp.importAnimation, true, v => imp.importAnimation = v);
        Set(imp.materialImportMode, ModelImporterMaterialImportMode.None, v => imp.materialImportMode = v);
        if (dirty) imp.SaveAndReimport();

        if (!MapBones(imp, path)) return false;
        EnforceArmTPose(imp, path);
        ConfigureTakes(imp, path);

        var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"Setup Birdy UAL Trial: {path} avatar invalid (valid={avatar != null && avatar.isValid}, " +
                           $"human={avatar != null && avatar.isHuman}).");
            return false;
        }
        return true;
    }

    /// <summary>Writes the explicit bone map, only when it differs from the stored one.</summary>
    static bool MapBones(ModelImporter imp, string path)
    {
        var desc = imp.humanDescription;
        var present = new HashSet<string>(desc.skeleton?.Select(s => s.name) ?? Enumerable.Empty<string>());
        var bones = new List<HumanBone>();
        foreach (var kv in BoneMaps[path])
        {
            if (present.Count > 0 && !present.Contains(kv.Value))
            {
                Debug.LogWarning($"Setup Birdy UAL Trial: bone '{kv.Value}' missing from {path}; {kv.Key} unmapped.");
                continue;
            }
            bones.Add(new HumanBone
            {
                humanName = kv.Key, boneName = kv.Value, limit = new HumanLimit { useDefaultValues = true },
            });
        }
        if (bones.Count < 15)
        {
            Debug.LogError($"Setup Birdy UAL Trial: only {bones.Count} bones of {path} mapped; Unity needs 15.");
            return false;
        }

        string Key(IEnumerable<HumanBone> list) =>
            string.Join(",", list.Select(b => $"{b.humanName}={b.boneName}").OrderBy(s => s, StringComparer.Ordinal));
        if (desc.human != null && Key(desc.human) == Key(bones)) return true;

        desc.human = bones.ToArray();
        imp.humanDescription = desc;
        imp.SaveAndReimport();
        Debug.Log($"UAL rig {Path.GetFileName(path)}: Humanoid, {bones.Count} bones mapped.");
        return true;
    }

    /// <summary>
    /// Unity's Enforce T-Pose for the arm chains of a source avatar, as
    /// BirdyNpcSetup does for Birdy's own. Humanoid retargeting treats the
    /// avatar's stored reference pose as the T-pose, so arms bound below
    /// horizontal there would lift Birdy's wings by the same angle in every
    /// clip. Writes nothing when the arms are within tolerance.
    /// </summary>
    static void EnforceArmTPose(ModelImporter imp, string path)
    {
        var desc = imp.humanDescription;
        var skeleton = desc.skeleton;
        if (skeleton == null || skeleton.Length == 0)
        {
            Debug.LogWarning($"UAL T-pose: no reference skeleton stored for {path}; skipped.");
            return;
        }
        string Bone(string human) => desc.human.FirstOrDefault(h => h.humanName == human).boneName;
        var chains = new[]
        {
            (Bone("LeftUpperArm"), Bone("LeftLowerArm"), Bone("LeftHand")),
            (Bone("RightUpperArm"), Bone("RightLowerArm"), Bone("RightHand")),
        };

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var go = Object.Instantiate(asset);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != go.transform) byName[t.name] = t;
            foreach (var sb in skeleton)
            {
                if (!byName.TryGetValue(sb.name, out var t)) continue;
                t.localPosition = sb.position;
                t.localRotation = sb.rotation;
                t.localScale = sb.scale;
            }

            var changed = new HashSet<string>();
            var report = new List<string>();
            foreach (var (upperName, lowerName, handName) in chains)
            {
                if (upperName == null || lowerName == null || handName == null ||
                    !byName.TryGetValue(upperName, out var upper) ||
                    !byName.TryGetValue(lowerName, out var lower) ||
                    !byName.TryGetValue(handName, out var hand))
                {
                    Debug.LogWarning($"UAL T-pose: arm chain of {path} not mapped; skipped.");
                    continue;
                }
                Vector3 side = go.transform.right *
                               Mathf.Sign(Vector3.Dot(hand.position - upper.position, go.transform.right));
                report.Add(Straighten(upper, lower.position - upper.position, side, changed));
                report.Add(Straighten(lower, hand.position - lower.position, side, changed));
            }
            Debug.Log($"UAL T-pose {Path.GetFileName(path)} (arm angle from straight-out): {string.Join(", ", report)}");
            if (changed.Count == 0) return;

            for (int i = 0; i < skeleton.Length; i++)
                if (changed.Contains(skeleton[i].name))
                    skeleton[i].rotation = byName[skeleton[i].name].localRotation;
            desc.skeleton = skeleton;
            imp.humanDescription = desc;
            imp.SaveAndReimport();
            Debug.Log($"UAL T-pose enforced on {string.Join(", ", changed)}.");
        }
        finally { Object.DestroyImmediate(go); }

        static string Straighten(Transform bone, Vector3 direction, Vector3 target, HashSet<string> changed)
        {
            float angle = Vector3.Angle(direction, target);
            if (angle > TPoseToleranceDegrees)
            {
                bone.rotation = Quaternion.FromToRotation(direction, target) * bone.rotation;
                changed.Add(bone.name);
            }
            return $"{bone.name} {angle:0.0}°";
        }
    }

    /// <summary>A take's name without its armature prefix ("Rig|Idle_Loop" → "Idle_Loop").</summary>
    static string ShortTake(string take) => take[(take.LastIndexOf('|') + 1)..];

    /// <summary>
    /// Lists exactly this FBX's mapped takes as its clips, each named after
    /// its take, with BirdyNpcSetup.ImportClip's settings. An existing entry is
    /// edited rather than replaced, so it keeps its internal ID and a re-run
    /// writes an identical .meta.
    /// </summary>
    static void ConfigureTakes(ModelImporter imp, string path)
    {
        var takes = Clips.Where(c => c.source == path)
                         .GroupBy(c => c.take)
                         .Select(g => (take: g.Key, loop: g.Any(c => c.role != NpcClipRole.Death)))
                         .ToList();
        var defaults = imp.defaultClipAnimations ?? Array.Empty<ModelImporterClipAnimation>();
        var existing = imp.clipAnimations ?? Array.Empty<ModelImporterClipAnimation>();

        var wanted = new List<ModelImporterClipAnimation>();
        foreach (var (take, loop) in takes)
        {
            var source = defaults.FirstOrDefault(d => ShortTake(d.takeName) == take);
            if (source == null)
            {
                Debug.LogError($"Setup Birdy UAL Trial: no take '{take}' in {path}. Takes: " +
                               string.Join(", ", defaults.Select(d => ShortTake(d.takeName))));
                continue;
            }
            var clip = existing.FirstOrDefault(c => c.name == take) ?? source;
            clip.name = take;
            clip.takeName = source.takeName;
            clip.firstFrame = source.firstFrame;
            clip.lastFrame = source.lastFrame;
            clip.loopTime = loop;
            clip.loopPose = loop;
            clip.lockRootRotation = true;
            clip.lockRootPositionXZ = true;
            clip.lockRootHeightY = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionXZ = true;
            clip.keepOriginalPositionY = false;
            clip.heightFromFeet = true;
            clip.mirror = false;
            wanted.Add(clip);
        }

        static string Describe(ModelImporterClipAnimation c) =>
            $"{c.name}/{c.takeName}/{c.firstFrame}/{c.lastFrame}/{c.loopTime}/{c.loopPose}/{c.lockRootRotation}/" +
            $"{c.lockRootPositionXZ}/{c.lockRootHeightY}/{c.keepOriginalOrientation}/{c.keepOriginalPositionXZ}/" +
            $"{c.keepOriginalPositionY}/{c.heightFromFeet}/{c.mirror}";
        if (existing.Select(Describe).SequenceEqual(wanted.Select(Describe))) return;

        imp.clipAnimations = wanted.ToArray();
        imp.SaveAndReimport();
        Debug.Log($"UAL clips of {Path.GetFileName(path)}: {string.Join(", ", wanted.Select(c => c.name))}.");
    }

    // ============================================================ the clips

    /// <summary>
    /// Copies each mapped clip to ClipDir/&lt;anim&gt;.anim and returns them by
    /// role, in table order. Null if a clip could not be found.
    /// </summary>
    static Dictionary<NpcClipRole, List<AnimationClip>> ExtractClips()
    {
        ChibiSandboxSetup.EnsureFolderPublic(ClipDir);
        ChibiSandboxSetup.EnsureFolderPublic(LiveClipDir);
        var result = new Dictionary<NpcClipRole, List<AnimationClip>>();
        var lines = new List<string>();
        foreach (var (anim, role, source, take) in Clips)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(source).OfType<AnimationClip>()
                                    .FirstOrDefault(c => c.name == take);
            if (clip == null)
            {
                Debug.LogError($"Setup Birdy UAL Trial: clip '{take}' not imported from {source}.");
                return null;
            }
            var copy = CopyClip(clip, source, ClipPath(anim, role), anim, out bool written);
            if (!result.TryGetValue(role, out var list)) result[role] = list = new List<AnimationClip>();
            list.Add(copy);
            lines.Add($"  {anim,-16} <- {Path.GetFileNameWithoutExtension(source)} {take} " +
                      $"{copy.length:0.00}s{(copy.isLooping ? " loop" : "")}{(written ? " (copied)" : "")}");
        }
        Debug.Log("Birdy UAL clips:\n" + string.Join("\n", lines));
        return result;
    }

    /// <summary>Where a mapped clip is copied: the death into Birdy's live
    /// clip folder, the rest beside the trial controller.</summary>
    static string ClipPath(string anim, NpcClipRole role)
        => role == NpcClipRole.Death ? $"{LiveClipDir}/{anim}.anim" : $"{ClipDir}/{anim}.anim";

    /// <summary>
    /// Copies an imported clip into a standalone .anim at <paramref name="path"/>,
    /// in place when it exists (GUID kept). Skipped when the source FBX, its
    /// import settings and the take are unchanged since the last copy.
    /// </summary>
    static AnimationClip CopyClip(AnimationClip source, string sourcePath, string path, string name, out bool written)
    {
        string signature = Hash128.Compute(string.Join("|",
            ClipCopyVersion, AssetDatabase.GetAssetDependencyHash(sourcePath).ToString(), source.name, name)).ToString();
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        var importer = AssetImporter.GetAtPath(path);
        written = false;
        if (existing != null && importer != null && importer.userData == signature) return existing;

        if (existing == null)
        {
            existing = Object.Instantiate(source);
            existing.name = name;
            AssetDatabase.CreateAsset(existing, path);
        }
        else
        {
            EditorUtility.CopySerialized(source, existing);
            existing.name = name;
            EditorUtility.SetDirty(existing);
        }
        AssetDatabase.SaveAssetIfDirty(existing);
        written = true;

        importer = AssetImporter.GetAtPath(path);
        if (importer != null && importer.userData != signature)
        {
            importer.userData = signature;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
    }

    // ======================================================= the controller

    /// <summary>
    /// Rebuilds the trial controller unless its inputs (clips, blend times,
    /// mirrored layout) are unchanged: rewriting it would give every state a
    /// new file ID, so an unchanged re-run would still show a diff. The
    /// inputs' hash is kept in the controller's .meta userData, as BirdyNpcSetup does.
    /// </summary>
    static AnimatorController BuildAnimator(Dictionary<NpcClipRole, List<AnimationClip>> clips, NpcAnimationConfig cfg)
    {
        string signature = ControllerSignature(clips, cfg);
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var importer = AssetImporter.GetAtPath(ControllerPath);
        if (existing != null && importer != null && importer.userData == signature)
        {
            Debug.Log($"Birdy UAL controller is up to date ({ControllerPath}); left untouched.");
            return existing;
        }

        var controller = BuildController(clips, cfg);
        AssetDatabase.SaveAssetIfDirty(controller);
        importer = AssetImporter.GetAtPath(ControllerPath);
        if (importer != null && importer.userData != signature)
        {
            importer.userData = signature;
            importer.SaveAndReimport();
        }
        return controller;
    }

    static string ControllerSignature(Dictionary<NpcClipRole, List<AnimationClip>> clips, NpcAnimationConfig cfg)
    {
        var parts = new List<string> { MirroredLayoutVersion };
        foreach (var role in NpcClipRoles.All)
        {
            parts.Add(role.ToString());
            if (!clips.TryGetValue(role, out var list)) continue;
            foreach (var clip in list)
                parts.Add(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long id)
                    ? $"{guid}/{id}" : clip.name);
        }
        foreach (float value in new[]
                 {
                     cfg.TalkBlend, cfg.SettleBlend, cfg.IdleBlend, cfg.DeathBlend, cfg.IdleExitTime,
                     cfg.FallbackTalkSpeed, cfg.FallbackExcitedSpeed, cfg.FallbackThinkingSpeed,
                 })
            parts.Add(value.ToString("R", CultureInfo.InvariantCulture));
        return Hash128.Compute(string.Join("|", parts)).ToString();
    }

    /// <summary>
    /// BirdyNpcSetup.BuildController's layout (<see cref="MirroredLayoutVersion"/>),
    /// written to <see cref="ControllerPath"/>: Idle (default) and Idle_1…, one per
    /// idle clip, swapping at the loop's exit time on IdleVariant; TalkIdle
    /// while Talking; Talk_Talk / Talk_Excited / Talk_Thinking from Any State
    /// (and from TalkIdle, which may cut into a settle) on Speaking + Emote;
    /// Death from Any State on Dead, tested first, never left. Foot IK on in
    /// every state but Death. See BirdyNpcSetup.BuildController for why each
    /// transition is set the way it is.
    /// </summary>
    static AnimatorController BuildController(Dictionary<NpcClipRole, List<AnimationClip>> clips,
                                              NpcAnimationConfig cfg)
    {
        ChibiSandboxSetup.EnsureFolderPublic(Root);
        var controller = ChibiSandboxSetup.ResetControllerInPlace(ControllerPath);
        controller.AddParameter(NpcAnimatorParameters.Talking, AnimatorControllerParameterType.Bool);
        controller.AddParameter(NpcAnimatorParameters.Speaking, AnimatorControllerParameterType.Bool);
        controller.AddParameter(NpcAnimatorParameters.Emote, AnimatorControllerParameterType.Int);
        controller.AddParameter(NpcAnimatorParameters.Dead, AnimatorControllerParameterType.Bool);
        controller.AddParameter(NpcAnimatorParameters.IdleVariant, AnimatorControllerParameterType.Int);

        var sm = controller.layers[0].stateMachine;
        sm.entryPosition = new Vector3(0f, 0f);
        sm.anyStatePosition = new Vector3(0f, 240f);
        sm.exitPosition = new Vector3(0f, 480f);

        // ---- idles ---------------------------------------------------------
        if (!clips.TryGetValue(NpcClipRole.Idle, out var idleClips)) idleClips = new List<AnimationClip>();
        if (idleClips.Count == 0) Debug.LogWarning("Birdy UAL: no idle clip at all; he will hold his bind pose.");
        int idleCount = Mathf.Clamp(idleClips.Count, 1, NpcAnimatorParameters.MaxIdleVariants);
        var mainIdle = idleClips.Count > 0 ? idleClips[0] : null;

        var idles = new List<AnimatorState>();
        for (int i = 0; i < idleCount; i++)
        {
            var st = sm.AddState(NpcAnimatorParameters.IdleStateName(i), new Vector3(260f, i * 60f));
            st.motion = i < idleClips.Count ? idleClips[i] : null;
            idles.Add(st);
        }
        sm.defaultState = idles[0];
        for (int i = 0; i < idles.Count; i++)
            for (int j = 0; j < idles.Count; j++)
            {
                if (i == j) continue;
                var t = idles[i].AddTransition(idles[j]);
                t.hasExitTime = true;
                t.exitTime = cfg.IdleExitTime;
                t.duration = cfg.IdleBlend;
                t.AddCondition(AnimatorConditionMode.Equals, j, NpcAnimatorParameters.IdleVariant);
                t.AddCondition(AnimatorConditionMode.IfNot, 0, NpcAnimatorParameters.Talking);
            }

        // ---- listening -----------------------------------------------------
        var listen = sm.AddState(NpcAnimatorParameters.TalkIdleState, new Vector3(560f, 0f));
        listen.motion = mainIdle;
        foreach (var idle in idles)
        {
            var t = idle.AddTransition(listen);
            t.hasExitTime = false;
            t.duration = cfg.SettleBlend;
            t.AddCondition(AnimatorConditionMode.If, 0, NpcAnimatorParameters.Talking);
        }
        var listenOut = listen.AddTransition(idles[0]);
        listenOut.hasExitTime = false;
        listenOut.duration = cfg.IdleBlend;
        listenOut.AddCondition(AnimatorConditionMode.IfNot, 0, NpcAnimatorParameters.Talking);

        // ---- talk gestures ---------------------------------------------------
        var report = new List<string>();
        int row = 0;
        foreach (DialogueEmote emote in Enum.GetValues(typeof(DialogueEmote)))
        {
            var st = sm.AddState(NpcAnimatorParameters.TalkStateName(emote), new Vector3(860f, row++ * 70f));
            if (NpcClipRoles.ResolveTalk(emote, r => clips.ContainsKey(r), out var role))
            {
                st.motion = clips[role][0];
                if (role == NpcClipRole.Idle) st.speed = FallbackSpeed(cfg, emote);
                report.Add($"{st.name} <- {st.motion.name}" +
                           (role == NpcClipRoles.TalkRole(emote) ? "" : $" (fallback, x{st.speed:0.##})"));
            }
            else report.Add($"{st.name} <- nothing");

            var into = sm.AddAnyStateTransition(st);
            into.hasExitTime = false;
            into.duration = cfg.TalkBlend;
            into.canTransitionToSelf = false;
            into.AddCondition(AnimatorConditionMode.If, 0, NpcAnimatorParameters.Speaking);
            into.AddCondition(AnimatorConditionMode.Equals, (int)emote, NpcAnimatorParameters.Emote);
            into.AddCondition(AnimatorConditionMode.IfNot, 0, NpcAnimatorParameters.Dead);

            var settle = st.AddTransition(listen);
            settle.hasExitTime = false;
            settle.duration = cfg.SettleBlend;
            settle.interruptionSource = TransitionInterruptionSource.Destination;
            settle.AddCondition(AnimatorConditionMode.IfNot, 0, NpcAnimatorParameters.Speaking);
            settle.AddCondition(AnimatorConditionMode.If, 0, NpcAnimatorParameters.Talking);

            var cut = listen.AddTransition(st);
            cut.hasExitTime = false;
            cut.duration = cfg.TalkBlend;
            cut.AddCondition(AnimatorConditionMode.If, 0, NpcAnimatorParameters.Speaking);
            cut.AddCondition(AnimatorConditionMode.Equals, (int)emote, NpcAnimatorParameters.Emote);
            cut.AddCondition(AnimatorConditionMode.IfNot, 0, NpcAnimatorParameters.Dead);

            var end = st.AddTransition(idles[0]);
            end.hasExitTime = false;
            end.duration = cfg.IdleBlend;
            end.AddCondition(AnimatorConditionMode.IfNot, 0, NpcAnimatorParameters.Talking);
        }

        // ---- death -------------------------------------------------------------
        if (clips.TryGetValue(NpcClipRole.Death, out var deathClips))
        {
            var dead = sm.AddState(NpcAnimatorParameters.DeathState, new Vector3(260f, 400f));
            dead.motion = deathClips[0];
            var t = sm.AddAnyStateTransition(dead);
            t.hasExitTime = false;
            t.duration = cfg.DeathBlend;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, NpcAnimatorParameters.Dead);
            var any = sm.anyStateTransitions;
            sm.anyStateTransitions = new[] { any[^1] }.Concat(any.Take(any.Length - 1)).ToArray();
            report.Add($"Death <- {dead.motion.name}");
        }
        else
        {
            Debug.LogWarning("Birdy UAL: no Death clip; he keeps idling when killed.");
        }

        foreach (var s in sm.states) s.state.iKOnFeet = s.state.name != NpcAnimatorParameters.DeathState;

        EditorUtility.SetDirty(controller);
        Debug.Log($"Birdy UAL controller: {ControllerPath}\n  " +
                  $"{string.Join(", ", idles.Select(s => $"{s.name} <- {(s.motion != null ? s.motion.name : "nothing")}"))}\n  " +
                  string.Join("\n  ", report));
        return controller;
    }

    static float FallbackSpeed(NpcAnimationConfig cfg, DialogueEmote emote) => emote switch
    {
        DialogueEmote.Excited => cfg.FallbackExcitedSpeed,
        DialogueEmote.Thinking => cfg.FallbackThinkingSpeed,
        _ => cfg.FallbackTalkSpeed,
    };

    // ========================================================= parity check

    /// <summary>
    /// Compares the trial controller's graph with the live Birdy.controller:
    /// parameters, default state, and every transition's ends, conditions and
    /// timing, with the motions and speeds left out (they are what the trial
    /// swaps). Transitions between idle variants are left out too, since the
    /// two may have different numbers of idles. Logs a warning naming each
    /// difference; a match means swapping the prefab's controller field is
    /// the whole adoption.
    /// </summary>
    static void CheckParity(AnimatorController trial)
    {
        var live = AssetDatabase.LoadAssetAtPath<AnimatorController>(LiveControllerPath);
        if (live == null || trial == null)
        {
            Debug.LogWarning($"Birdy UAL parity: {LiveControllerPath} missing; not compared.");
            return;
        }
        var a = GraphShape(live);
        var b = GraphShape(trial);
        var onlyLive = a.Except(b).ToList();
        var onlyTrial = b.Except(a).ToList();
        int liveIdles = live.layers[0].stateMachine.states.Count(s => IsIdle(s.state.name));
        int trialIdles = trial.layers[0].stateMachine.states.Count(s => IsIdle(s.state.name));
        if (onlyLive.Count == 0 && onlyTrial.Count == 0)
        {
            Debug.Log($"Birdy UAL parity: same parameters, states and transitions as {LiveControllerPath} " +
                      $"(idle variants: live {liveIdles}, trial {trialIdles}).");
            return;
        }
        Debug.LogWarning("Birdy UAL parity: the trial controller differs from the live one. Port " +
                         "BirdyNpcSetup.BuildController's changes to BirdyUalTrialSetup.BuildController.\n" +
                         "  only live:  " + string.Join("\n              ", onlyLive) + "\n" +
                         "  only trial: " + string.Join("\n              ", onlyTrial));
    }

    /// <summary>"Idle" or a variant ("Idle_1", "Idle_2"…), as NpcAnimatorParameters.IdleStateName names them.</summary>
    static bool IsIdle(string state) => state == NpcAnimatorParameters.IdleStateName(0) || IsIdleVariant(state);

    static bool IsIdleVariant(string state) =>
        state.StartsWith(NpcAnimatorParameters.IdleStateName(0) + "_", StringComparison.Ordinal);

    static HashSet<string> GraphShape(AnimatorController c)
    {
        var shape = new HashSet<string>();
        foreach (var p in c.parameters) shape.Add($"param {p.name}:{p.type}");
        var sm = c.layers[0].stateMachine;
        shape.Add($"default {(sm.defaultState != null ? sm.defaultState.name : "none")}");

        string Conditions(AnimatorStateTransition t) => string.Join(" & ",
            t.conditions.Select(k => $"{k.parameter} {k.mode} {k.threshold.ToString("R", CultureInfo.InvariantCulture)}"));
        string Timing(AnimatorStateTransition t) =>
            $"exit {t.hasExitTime}/{t.exitTime.ToString("R", CultureInfo.InvariantCulture)} " +
            $"dur {t.duration.ToString("R", CultureInfo.InvariantCulture)} fixed {t.hasFixedDuration} " +
            $"int {t.interruptionSource} ordered {t.orderedInterruption} self {t.canTransitionToSelf}";

        foreach (var cs in sm.states)
        {
            var st = cs.state;
            if (IsIdleVariant(st.name)) continue;
            shape.Add($"state {st.name} footIK {st.iKOnFeet}");
            foreach (var t in st.transitions)
            {
                if (t.destinationState == null || IsIdleVariant(t.destinationState.name)) continue;
                shape.Add($"{st.name} -> {t.destinationState.name} [{Conditions(t)}] {Timing(t)}");
            }
        }
        var any = sm.anyStateTransitions;
        for (int i = 0; i < any.Length; i++)
        {
            var t = any[i];
            string to = t.destinationState != null ? t.destinationState.name : "?";
            // Death must be tested first; the others' order does not matter.
            string order = to == NpcAnimatorParameters.DeathState ? $"#{i} " : "";
            shape.Add($"{order}Any -> {to} [{Conditions(t)}] {Timing(t)}");
        }
        return shape;
    }

    // ============================================================= headless

    /// <summary>
    /// Command-line entry point (-executeMethod BirdyUalTrialSetup.BatchRun).
    /// Runs the setup twice (the second run must write nothing) and exits
    /// with 1 if anything logged an error. Run it in a separate worktree,
    /// never against the shared Editor's checkout.
    /// </summary>
    public static void BatchRun()
    {
        int errors = 0;
        void Count(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
        }
        Application.logMessageReceived += Count;
        try
        {
            Run();
            Run();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            Application.logMessageReceived -= Count;
        }
        Debug.Log($"[BirdyUalTrial] finished with {errors} error(s).");
        EditorApplication.Exit(errors == 0 ? 0 : 1);
    }
}
