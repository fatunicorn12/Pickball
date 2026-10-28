using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Dialogue;
using Pickleball.Sandbox.Npc;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Imports Birdy, the little helicopter bird, as a talking NPC and drops one
/// into the open ChibiSandbox.
///
/// Tools ▸ Pickleball ▸ Setup Birdy NPC       model, material, rig, clips,
///                                             animator, dialogue, prefab, placement
/// Tools ▸ Pickleball ▸ Rebuild Birdy Animator clips and controller only
///
/// Follows the Newton path (NewtonBossSetup): a Tripo model imported as
/// Humanoid with the shared Tripo bone map (ChibiSandboxSetup), Mixamo clips
/// retargeted through the avatar, and a controller rewritten in place so its
/// GUID never changes. Birdy's wings are the arm chain (the wing meshes are
/// skinned to L/R_Upperarm…Hand and their twist bones), so arm gestures move
/// the wings.
///
/// Idempotent. Every import setting is written only when it differs, the
/// prefab is edited in place (component file IDs survive), the dialogue and
/// config assets are seeded once and then left alone, and the scene gets one
/// Birdy: an existing instance is kept where it is. The scene is marked dirty
/// and never saved.
///
/// Clips: drop Mixamo FBX (FBX for Unity, Without Skin, 30 fps) into
/// Animation/Birdy/ named by role prefix (Idle*, Talk_Talk*, Talk_Excited*,
/// Talk_Thinking*, Death*; see NpcClipRoles) and re-run either menu. A role
/// with no clip there falls back to Animation/Birdy/Fallback/, and a talk
/// emote with no clip at all falls back to Talk, then to the idle.
/// </summary>
public static class BirdyNpcSetup
{
    const string CharacterDir = "Assets/_Project/Art/Characters/Birdy";
    const string ModelPath = CharacterDir + "/Birdy.fbx";
    const string BaseColorPath = CharacterDir + "/Birdy_BaseColor.jpg";
    const string NormalPath = CharacterDir + "/Birdy_Normal.jpg";
    const string MaterialPath = CharacterDir + "/Birdy.mat";
    const string ClipDir = "Assets/_Project/Animation/Birdy";
    const string FallbackDir = ClipDir + "/Fallback";
    const string ControllerPath = "Assets/_Project/Animation/Controllers/Birdy.controller";
    const string PrefabDir = "Assets/_Project/Prefabs/Npc";
    const string PrefabPath = PrefabDir + "/Birdy.prefab";
    const string DialogueDir = "Assets/_Project/ScriptableObjects/Dialogue";
    const string DialoguePath = DialogueDir + "/Birdy_Intro.asset";
    const string ConfigPath = "Assets/_Project/ScriptableObjects/Npc/NpcAnimationConfig.asset";
    const string SandboxScenePath = "Assets/_Project/Scenes/ChibiSandbox.unity";

    /// <summary>
    /// Fallback clips copied into Animation/Birdy/Fallback/ when missing
    /// (copied, like Newton's, so their import settings can be rewritten
    /// without changing the chibi's files). Chosen by sampling candidates on
    /// Birdy (Report Birdy Poses):
    ///   idle  : the unarmed Idle. The wings hang folded at the sides.
    ///   death : sword-and-shield death (2), a 3.9 s collapse that starts
    ///           from standing and ends lying down. Mixamo "Falling Flat
    ///           Impact" was rejected: it begins mid-fall, so Birdy would pop
    ///           0.8 m into the air before landing. Death (1) ends with the
    ///           rotor 0.37 m into the floor against (2)'s 0.21 m.
    /// </summary>
    static readonly (string file, string source)[] FallbackCopies =
    {
        ("Idle_ChibiIdle.fbx", "Assets/_Project/Animation/Chibi/Idle.fbx"),
        ("Death_SwordShieldDeath2.fbx", "Assets/_Project/Animation/Chibi/SwordShield/sword and shield death (2).fbx"),
    };

    /// <summary>Scene object and prefab name; the dialogue speaker name too.</summary>
    const string InstanceName = "Birdy";
    const string ModelChildName = "BirdyModel";

    /// <summary>
    /// Scene root that holds NPCs. The name is load-bearing: Build Training
    /// Stage copies ChibiSandbox and strips every root whose name contains
    /// "Academy", which keeps Birdy out of the TrainingStage scene without
    /// touching that script.
    /// </summary>
    const string NpcRootName = "AcademyNpcs";

    /// <summary>
    /// Birdy's height, m, rotor included. The chibi is 1.70 m (Tripo's unit-tall
    /// export at globalScale 1.7), with his head bone at about 1.15 m. At 1.15 m
    /// Birdy's rotor reaches the chibi's chin: clearly the smaller character,
    /// but big enough that the dialogue camera can frame both heads together.
    /// </summary>
    const float TargetHeight = 1.15f;

    /// <summary>
    /// Vertical nudge of the model under the root, m. Clips bake their height
    /// feet-based (like Newton's), but on Birdy's short legs the fallback idle
    /// still leaves the soles slightly below the root. Measured through the
    /// Animator (Rebind + Update, with per-state foot IK, as at runtime) by
    /// Report Birdy Poses: the "soles" of the idle, read with this at 0, is
    /// the negative of what it should be. (A first calibration with
    /// AnimationClip.SampleAnimation skipped foot IK and over-lifted him by
    /// 3.7 cm, so he hovered.) Re-measure when real clips land.
    /// </summary>
    const float ModelYOffset = 0.008f;

    /// <summary>
    /// How far, °, the avatar lets the upper arm (the wing) fold down from the
    /// T-pose. Less range scales every clip's arm-down motion by the same
    /// ratio (the retargeted muscle value is unchanged), so the wings fold
    /// less in every clip without a per-clip change. Unity's default is 60°:
    /// the fallback idle then folds the wings 80° and presses them into the
    /// body. At 40° they fold 70° and sit just off it, which still reads as
    /// folded. Rendered at 40° and at 25° (61–65°): neither closed the white
    /// tears at the wing roots and harness. Those come from Tripo's skin
    /// weights (the wing planks are hinged at the shoulder, and the right
    /// strap part is weighted to the right arm while its left mirror is not),
    /// so this is kept at 40° for the look, not as a fix for them.
    /// </summary>
    const float WingFoldDegrees = 40f;

    /// <summary>The rotor mesh part, for the rotor-drift line of Report Birdy Poses.</summary>
    const string RotorPartName = "tripo_part_1";

    /// <summary>Modest: a basic sword combo (10+12+14+20) downs him, a single
    /// stray hit does not.</summary>
    const float MaxHealth = 40f;

    /// <summary>Arm-chain bones further than this from horizontal in the
    /// avatar's reference pose are rotated to it (Enforce T-Pose).</summary>
    const float TPoseToleranceDegrees = 2f;

    /// <summary>Capsule radius as a share of the narrower horizontal extent,
    /// so the wings (the wider extent) stick out past it.</summary>
    const float CapsuleRadiusShare = 0.75f;

    // Placement around the player's spawn.
    /// <summary>Kept from Newton's root, m. 7 rather than more because the
    /// starting camera looks toward him: in ChibiSandbox the only central,
    /// flat, open spots near the spawn are 7.3–7.7 m from him.</summary>
    const float NewtonClearance = 7f;
    const float DummyClearance = 3f;
    /// <summary>Gap kept between Birdy's capsule and a pickup's / tether anchor's renderer footprint, m.</summary>
    const float PickupMargin = 0.8f;
    const float AnchorMargin = 0.6f;
    const float NpcClearance = 2f;
    /// <summary>Preferred distance from the spawn, m: close enough to notice,
    /// far enough not to crowd the first steps.</summary>
    const float PreferredDistance = 6f;
    const float MaxGroundStep = 1f;
    const float MaxGroundSlope = 15f;

    // ================================================================= menus

    [MenuItem("Tools/Pickleball/Setup Birdy NPC")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Setup Birdy NPC: exit Play mode first.");
            return;
        }

        TidyDownloadNames();
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"Setup Birdy NPC: no model at {ModelPath}. Put the Tripo export " +
                           $"(.fbx plus its textures) in {CharacterDir}.");
            return;
        }

        SetupTextures();
        var material = SetupMaterial(importer);
        if (!SetupRig(importer, out var bounds)) return;
        var avatar = LoadAvatar();
        if (avatar == null) return;

        var config = EnsureConfig();
        var controller = BuildAnimator(config);
        var dialogue = EnsureDialogue();
        var prefab = BuildPrefab(avatar, controller, material, dialogue, config, bounds);
        if (prefab == null) return;

        PlaceInOpenScene(prefab, bounds);
        // Birdy's own assets only. AssetDatabase.SaveAssets() would also write
        // every other dirty asset, such as the TMP example fonts that loading
        // ChibiSandbox upgrades in memory (DialogueSetup avoids it the same way).
        // Importer settings, the prefab, the seeded assets and the controller's
        // .meta are written where they are changed.
        SaveIfDirty(material, controller, config, dialogue);
        Debug.Log($"Setup Birdy NPC done. Prefab: {PrefabPath}. The scene is marked dirty, not saved.");
    }

    static void SaveIfDirty(params Object[] assets)
    {
        foreach (var asset in assets)
            if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
    }

    /// <summary>
    /// Re-imports the clips and rewrites the controller in place (GUID kept),
    /// without touching the model, the prefab or the scene. Use it when clips
    /// land in Animation/Birdy/ or the config's blend times change.
    /// </summary>
    [MenuItem("Tools/Pickleball/Rebuild Birdy Animator")]
    public static void RebuildAnimator()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Rebuild Birdy Animator: exit Play mode first.");
            return;
        }
        SaveIfDirty(BuildAnimator(EnsureConfig(), force: true));
    }

    // ============================================================ the model

    /// <summary>
    /// The Tripo download arrives as tripo_convert_&lt;uuid&gt;.fbx beside a
    /// tripo_convert_&lt;uuid&gt;.fbm folder of textures. Gives them clean names
    /// with AssetDatabase.MoveAsset, so every GUID (and reference) survives,
    /// and removes the emptied folder.
    ///
    /// Unity will not re-create a .fbm: this FBX embeds no media (its texture
    /// nodes only point at the .fbm by relative path), so the folder was the
    /// download's, not an import by-product. The material is remapped below,
    /// so the dangling texture paths are never looked up.
    /// </summary>
    static void TidyDownloadNames()
    {
        if (!AssetDatabase.IsValidFolder(CharacterDir)) return;

        foreach (var file in Directory.GetFiles(CharacterDir, "tripo_convert_*.fbx"))
        {
            string path = file.Replace('\\', '/');
            if (AssetImporter.GetAtPath(ModelPath) == null) Move(path, ModelPath);
            else Debug.LogWarning($"Setup Birdy NPC: {path} duplicates {ModelPath} (and probably " +
                                  "shares its GUID). Delete it.");
        }

        foreach (var dir in Directory.GetDirectories(CharacterDir, "*.fbm"))
        {
            string folder = dir.Replace('\\', '/');
            foreach (var file in Directory.GetFiles(folder))
            {
                string path = file.Replace('\\', '/');
                if (path.EndsWith(".meta")) continue;
                string lower = Path.GetFileName(path).ToLowerInvariant();
                string target = lower.Contains("basecolor") ? BaseColorPath
                              : lower.Contains("normal") ? NormalPath : null;
                if (target != null && AssetImporter.GetAtPath(target) == null) Move(path, target);
            }
            if (Directory.GetFiles(folder).All(f => f.EndsWith(".meta")) && AssetDatabase.DeleteAsset(folder))
                Debug.Log($"Removed the emptied {folder}.");
        }
    }

    static void Move(string from, string to)
    {
        string error = AssetDatabase.MoveAsset(from, to);
        if (string.IsNullOrEmpty(error)) Debug.Log($"Renamed {from} -> {to} (GUID kept).");
        else Debug.LogError($"Could not rename {from} -> {to}: {error}");
    }

    static void SetupTextures()
    {
        ConfigureTexture(BaseColorPath, normalMap: false, maxSize: 4096);
        ConfigureTexture(NormalPath, normalMap: true, maxSize: 2048);
    }

    /// <summary>Writes texture import settings only where they differ, so a
    /// re-run neither reimports nor churns the .meta.</summary>
    static void ConfigureTexture(string path, bool normalMap, int maxSize)
    {
        if (!(AssetImporter.GetAtPath(path) is TextureImporter ti))
        {
            Debug.LogWarning($"Setup Birdy NPC: texture missing at {path}.");
            return;
        }
        bool dirty = false;
        var type = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
        if (ti.textureType != type) { ti.textureType = type; dirty = true; }
        if (!normalMap && !ti.sRGBTexture) { ti.sRGBTexture = true; dirty = true; }
        if (ti.maxTextureSize != maxSize) { ti.maxTextureSize = maxSize; dirty = true; }
        if (ti.textureCompression != TextureImporterCompression.CompressedHQ)
        {
            ti.textureCompression = TextureImporterCompression.CompressedHQ;
            dirty = true;
        }
        if (dirty) ti.SaveAndReimport();
    }

    /// <summary>A URP Lit material with the base and normal maps, remapped
    /// onto every material slot the FBX declares (the Chibi/Newton pattern).</summary>
    static Material SetupMaterial(ModelImporter importer)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("Setup Birdy NPC: URP Lit shader not found.");
            return null;
        }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        if (mat.shader != shader) mat.shader = shader;
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(BaseColorPath));
        mat.SetColor("_BaseColor", Color.white);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        mat.SetTexture("_BumpMap", normal);
        mat.SetFloat("_BumpScale", 1f);
        if (normal != null) mat.EnableKeyword("_NORMALMAP");
        else mat.DisableKeyword("_NORMALMAP");
        mat.SetFloat("_Smoothness", 0.15f);    // feathers, not plastic
        EditorUtility.SetDirty(mat);

        bool reimport = false;
        if (importer.materialImportMode != ModelImporterMaterialImportMode.ImportStandard)
        {
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            importer.SaveAndReimport();
        }
        // Slots not yet remapped still show up as embedded sub-assets.
        foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>())
        {
            importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name), mat);
            reimport = true;
        }
        foreach (var kv in importer.GetExternalObjectMap())
        {
            if (kv.Key.type != typeof(Material) || kv.Value == mat) continue;
            importer.AddRemap(kv.Key, mat);
            reimport = true;
        }
        if (reimport)
        {
            importer.SaveAndReimport();
            Debug.Log($"Remapped Birdy's material slot(s) -> {MaterialPath}");
        }
        return mat;
    }

    /// <summary>
    /// Humanoid, "Create From This Model", with the shared Tripo bone map:
    /// Hips = Hip, Spine = Waist, Chest = Spine01, UpperChest = Spine02,
    /// Neck = NeckTwist01, Head, the arm, leg and toe chains; twist bones
    /// unmapped (exactly as the chibi and Newton). Scaled so the model is
    /// <see cref="TargetHeight"/> tall, and the reference pose's arms (wings)
    /// straightened to a true T.
    /// </summary>
    static bool SetupRig(ModelImporter importer, out Bounds bounds)
    {
        float current = importer.globalScale;
        float height = MeasureModel(out bounds);
        if (height < 0.01f)
        {
            Debug.LogError("Setup Birdy NPC: could not measure the model (no renderers?).");
            return false;
        }
        float native = height / Mathf.Max(current, 1e-4f);
        float scale = (float)Math.Round(TargetHeight / native, 3);

        if (Mathf.Abs(scale - current) > 0.0015f)
        {
            importer.globalScale = scale;
            // The stored reference pose was captured at the old scale. Drop it
            // (and the map, re-applied below) so the reimport captures it anew.
            var desc = importer.humanDescription;
            desc.skeleton = Array.Empty<SkeletonBone>();
            desc.human = Array.Empty<HumanBone>();
            importer.humanDescription = desc;
            importer.SaveAndReimport();
            Debug.Log($"Birdy scale {current:0.###} -> {scale:0.###} (native height {native:0.###} m, " +
                      $"target {TargetHeight} m).");
        }

        if (!ChibiSandboxSetup.SetupRigPublic(importer, scale)) return false;
        LimitWingFold(importer);
        EnforceArmTPose(importer);
        MeasureModel(out bounds);
        return true;
    }

    /// <summary>
    /// Narrows the upper arms' Down-Up muscle range to
    /// <see cref="WingFoldDegrees"/> (see there). SetupRigPublic writes default
    /// limits on every run, so this rewrites the two arm limits every run too;
    /// the stored result is the same each time.
    /// </summary>
    static void LimitWingFold(ModelImporter importer)
    {
        var desc = importer.humanDescription;
        var report = new List<string>();
        for (int i = 0; i < desc.human.Length; i++)
        {
            var hb = desc.human[i];
            if (hb.humanName != "LeftUpperArm" && hb.humanName != "RightUpperArm") continue;
            int bone = Array.IndexOf(HumanTrait.BoneName, hb.humanName);
            if (bone < 0) continue;

            Vector3 min = Vector3.zero, max = Vector3.zero;
            int downUp = -1;
            for (int dof = 0; dof < 3; dof++)
            {
                int muscle = HumanTrait.MuscleFromBone(bone, dof);
                if (muscle < 0) continue;
                min[dof] = HumanTrait.GetMuscleDefaultMin(muscle);
                max[dof] = HumanTrait.GetMuscleDefaultMax(muscle);
                if (HumanTrait.MuscleName[muscle].Contains("Down-Up")) downUp = dof;
            }
            if (downUp < 0) continue;
            report.Add($"{hb.humanName} down {min[downUp]:0}° -> {-WingFoldDegrees:0}°");
            min[downUp] = -WingFoldDegrees;

            // The upper arm's length, for the limit's axis: the stored
            // reference skeleton's offset of the forearm.
            string lower = desc.human.FirstOrDefault(h => h.humanName ==
                (hb.humanName == "LeftUpperArm" ? "LeftLowerArm" : "RightLowerArm")).boneName;
            float length = desc.skeleton.Where(s => s.name == lower).Select(s => s.position.magnitude)
                                        .DefaultIfEmpty(0f).First();
            hb.limit = new HumanLimit
            {
                useDefaultValues = false, min = min, max = max, center = Vector3.zero, axisLength = length,
            };
            desc.human[i] = hb;
        }
        if (report.Count == 0) return;
        importer.humanDescription = desc;
        importer.SaveAndReimport();
        Debug.Log($"Birdy wing fold limited: {string.Join(", ", report)}.");
    }

    /// <summary>Height of the model as imported, m, with its renderer bounds
    /// relative to its root.</summary>
    static float MeasureModel(out Bounds bounds)
    {
        bounds = default;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (asset == null) return 0f;
        var go = Object.Instantiate(asset);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0f;
            bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            return bounds.size.y;
        }
        finally { Object.DestroyImmediate(go); }
    }

    /// <summary>
    /// Unity's Enforce T-Pose, for the arm chains only. Humanoid retargeting
    /// treats the avatar's reference pose as the T-pose; Tripo binds the arms
    /// (Birdy's wings) about 13° below horizontal, which would add 13° of droop
    /// to every Mixamo gesture. Each upper arm and forearm in the stored
    /// reference skeleton is rotated so it points straight out sideways. The
    /// mesh's bind pose is untouched; only the avatar's notion of "T" moves.
    /// Writes nothing when the arms are already within tolerance, so a re-run
    /// is a no-op.
    /// </summary>
    static void EnforceArmTPose(ModelImporter importer)
    {
        var desc = importer.humanDescription;
        var skeleton = desc.skeleton;
        if (skeleton == null || skeleton.Length == 0)
        {
            Debug.LogWarning("Birdy T-pose: no reference skeleton stored; skipped.");
            return;
        }
        string Bone(string human) => desc.human.FirstOrDefault(h => h.humanName == human).boneName;
        var chains = new[]
        {
            (Bone("LeftUpperArm"), Bone("LeftLowerArm"), Bone("LeftHand")),
            (Bone("RightUpperArm"), Bone("RightLowerArm"), Bone("RightHand")),
        };

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var go = Object.Instantiate(asset);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != go.transform) byName[t.name] = t;
            // Pose the instance in the stored reference pose (it may already
            // differ from the bind pose after an earlier run).
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
                    Debug.LogWarning("Birdy T-pose: arm chain not mapped; skipped.");
                    continue;
                }
                Vector3 side = go.transform.right *
                               Mathf.Sign(Vector3.Dot(hand.position - upper.position, go.transform.right));
                report.Add(Straighten(upper, lower.position - upper.position, side, changed));
                report.Add(Straighten(lower, hand.position - lower.position, side, changed));
            }
            Debug.Log($"Birdy T-pose (arm angle from straight-out): {string.Join(", ", report)}");
            if (changed.Count == 0) return;

            for (int i = 0; i < skeleton.Length; i++)
                if (changed.Contains(skeleton[i].name))
                    skeleton[i].rotation = byName[skeleton[i].name].localRotation;
            desc.skeleton = skeleton;
            importer.humanDescription = desc;
            importer.SaveAndReimport();
            Debug.Log($"Birdy T-pose enforced on {string.Join(", ", changed)}.");
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

    static Avatar LoadAvatar()
    {
        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"Birdy avatar invalid (valid={(avatar != null && avatar.isValid)}, " +
                           $"human={(avatar != null && avatar.isHuman)}). Check {ModelPath} ▸ Rig ▸ Configure.");
            return null;
        }
        return avatar;
    }

    // ============================================================ the clips

    /// <summary>Bump when BuildController's layout changes, so Setup Birdy NPC
    /// rebuilds controllers whose inputs have not.</summary>
    const string ControllerLayoutVersion = "birdy-controller-2";

    /// <summary>
    /// Imports the clips and rebuilds the controller. With
    /// <paramref name="force"/> off (Setup Birdy NPC), a controller whose
    /// inputs (clips, blend times, layout version) are unchanged is left
    /// alone: rewriting it would give every state a new file ID, so an
    /// unchanged re-run would still show a diff. The inputs' hash is kept in
    /// the controller's .meta userData.
    /// </summary>
    static AnimatorController BuildAnimator(NpcAnimationConfig config, bool force = false)
    {
        var clips = ImportClips();
        string signature = ControllerSignature(clips, config);
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var importer = AssetImporter.GetAtPath(ControllerPath);
        if (!force && existing != null && importer != null && importer.userData == signature)
        {
            Debug.Log($"Birdy controller is up to date ({ControllerPath}); left untouched.");
            return existing;
        }

        var controller = BuildController(clips, config);
        // Write the controller before its importer reimports from disk.
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
        var parts = new List<string> { ControllerLayoutVersion };
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
            parts.Add(value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        return Hash128.Compute(string.Join("|", parts)).ToString();
    }

    /// <summary>Every role's clips, real ones from Animation/Birdy/ first and
    /// Animation/Birdy/Fallback/ only for a role with none.</summary>
    static Dictionary<NpcClipRole, List<AnimationClip>> ImportClips()
    {
        ChibiSandboxSetup.EnsureFolderPublic(ClipDir);
        ChibiSandboxSetup.EnsureFolderPublic(FallbackDir);
        foreach (var (file, source) in FallbackCopies)
        {
            string path = $"{FallbackDir}/{file}";
            if (AssetImporter.GetAtPath(path) != null || AssetImporter.GetAtPath(source) == null) continue;
            if (AssetDatabase.CopyAsset(source, path)) Debug.Log($"  copied fallback {source} -> {path}");
        }

        var real = ScanClipFolder(ClipDir);
        var fallback = ScanClipFolder(FallbackDir);
        var result = new Dictionary<NpcClipRole, List<AnimationClip>>();
        var lines = new List<string>();
        foreach (var role in NpcClipRoles.All)
        {
            bool useFallback = !real.TryGetValue(role, out var paths);
            if (useFallback && !fallback.TryGetValue(role, out paths)) paths = new List<string>();

            var clips = new List<AnimationClip>();
            foreach (var path in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                var clip = ImportClip(path, loop: role != NpcClipRole.Death);
                if (clip != null) clips.Add(clip);
            }
            if (clips.Count > 0) result[role] = clips;
            lines.Add($"  {role,-13} " + (clips.Count == 0 ? "(none)"
                : (useFallback ? "FALLBACK " : "") + string.Join(", ", clips.Select(c => $"{c.name} {c.length:0.00}s"))));
        }
        Debug.Log("Birdy clips:\n" + string.Join("\n", lines));
        return result;
    }

    static Dictionary<NpcClipRole, List<string>> ScanClipFolder(string dir)
    {
        var found = new Dictionary<NpcClipRole, List<string>>();
        if (!Directory.Exists(dir)) return found;
        foreach (var file in Directory.GetFiles(dir, "*.fbx", SearchOption.TopDirectoryOnly))
        {
            string path = file.Replace('\\', '/');
            if (!NpcClipRoles.TryGetRole(path, out var role))
            {
                Debug.LogWarning($"Birdy clips: ignoring {path}. Name it Idle*, Talk_Talk*, " +
                                 "Talk_Excited*, Talk_Thinking* or Death*.");
                continue;
            }
            if (!found.TryGetValue(role, out var list)) found[role] = list = new List<string>();
            list.Add(path);
        }
        return found;
    }

    /// <summary>
    /// Humanoid with the avatar created from the Mixamo source rig, exactly as
    /// the Animation/Chibi/*.fbx clips are set up, and one clip named after
    /// the file. Root motion is baked into the pose on every axis (Birdy never
    /// travels, and root motion is off on his Animator), with the height
    /// feet-based so a death ends on the ground rather than at standing
    /// height. Settings are written only where they differ.
    /// </summary>
    static AnimationClip ImportClip(string path, bool loop)
    {
        var imp = AssetImporter.GetAtPath(path) as ModelImporter;
        if (imp == null) return null;

        bool dirty = false;
        if (imp.animationType != ModelImporterAnimationType.Human)
        { imp.animationType = ModelImporterAnimationType.Human; dirty = true; }
        if (imp.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        { imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
        if (!imp.importAnimation) { imp.importAnimation = true; dirty = true; }
        if (imp.materialImportMode != ModelImporterMaterialImportMode.None)
        { imp.materialImportMode = ModelImporterMaterialImportMode.None; dirty = true; }
        if (dirty)
        {
            imp.SaveAndReimport();
            dirty = false;
        }

        // Edit the existing clip when there is one: it keeps its internal ID,
        // so a re-run writes an identical .meta.
        var existing = imp.clipAnimations;
        var take = existing is { Length: > 0 } ? existing[0]
                 : imp.defaultClipAnimations is { Length: > 0 } d ? d[0] : null;
        if (take == null)
        {
            Debug.LogWarning($"Birdy clips: no animation take in {path}.");
            return null;
        }

        string clipName = Path.GetFileNameWithoutExtension(path);
        if (existing is not { Length: 1 }) dirty = true;
        void Set<T>(T current, T wanted, Action<T> write)
        {
            if (EqualityComparer<T>.Default.Equals(current, wanted)) return;
            write(wanted);
            dirty = true;
        }
        Set(take.name, clipName, v => take.name = v);
        Set(take.loopTime, loop, v => take.loopTime = v);
        Set(take.loopPose, loop, v => take.loopPose = v);
        Set(take.lockRootRotation, true, v => take.lockRootRotation = v);
        Set(take.lockRootPositionXZ, true, v => take.lockRootPositionXZ = v);
        Set(take.lockRootHeightY, true, v => take.lockRootHeightY = v);
        Set(take.keepOriginalOrientation, true, v => take.keepOriginalOrientation = v);
        Set(take.keepOriginalPositionXZ, true, v => take.keepOriginalPositionXZ = v);
        Set(take.keepOriginalPositionY, false, v => take.keepOriginalPositionY = v);
        Set(take.heightFromFeet, true, v => take.heightFromFeet = v);
        Set(take.mirror, false, v => take.mirror = v);
        if (dirty)
        {
            imp.clipAnimations = new[] { take };
            imp.SaveAndReimport();
        }

        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }

    // ======================================================= the controller

    /// <summary>
    /// Birdy's state machine, rewritten in place (GUID kept). Parameter and
    /// state names are the contract in <see cref="NpcAnimatorParameters"/>.
    ///
    ///   Idle, Idle_1…  default; one state per idle clip. Each loop the driver
    ///                  sets IdleVariant and the loop's exit time moves there.
    ///   TalkIdle       listening: in a conversation, between lines.
    ///   Talk_Talk / Talk_Excited / Talk_Thinking
    ///                  one per DialogueEmote, from Any State on Speaking +
    ///                  Emote; an emote without its own clip plays Talk's, or
    ///                  the idle a little faster or slower.
    ///   Death          from Any State on Dead, tested first; nothing leaves it.
    /// </summary>
    static AnimatorController BuildController(Dictionary<NpcClipRole, List<AnimationClip>> clips,
                                              NpcAnimationConfig cfg)
    {
        ChibiSandboxSetup.EnsureFolderPublic(Path.GetDirectoryName(ControllerPath)!.Replace('\\', '/'));
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
        if (idleClips.Count == 0) Debug.LogWarning("Birdy: no idle clip at all; he will hold his bind pose.");
        if (idleClips.Count > NpcAnimatorParameters.MaxIdleVariants)
            Debug.LogWarning($"Birdy: only the first {NpcAnimatorParameters.MaxIdleVariants} idle clips are used.");
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

            // The settle back to listening is interruptible by its
            // destination's transitions (below), so a line that starts while
            // the previous gesture is still settling cuts straight in instead
            // of waiting out the blend. With the default (None) a quick skip
            // left the next line's gesture 0.35 s late.
            var settle = st.AddTransition(listen);
            settle.hasExitTime = false;
            settle.duration = cfg.SettleBlend;
            settle.interruptionSource = TransitionInterruptionSource.Destination;
            settle.AddCondition(AnimatorConditionMode.IfNot, 0, NpcAnimatorParameters.Speaking);
            settle.AddCondition(AnimatorConditionMode.If, 0, NpcAnimatorParameters.Talking);

            // TalkIdle's own way into each gesture: the same conditions as the
            // Any State transition, but as a destination transition of the
            // settle above it is what may interrupt it.
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
            // Any State transitions are tested in list order: death first, so
            // a line starting on the frame he dies cannot win.
            var any = sm.anyStateTransitions;
            sm.anyStateTransitions = new[] { any[^1] }.Concat(any.Take(any.Length - 1)).ToArray();
            report.Add($"Death <- {dead.motion.name}");
        }
        else
        {
            Debug.LogWarning("Birdy: no Death clip; he keeps idling when killed.");
        }

        // Per-state Foot IK replays the clip's own foot goals, which keeps
        // retargeted feet planted. Off for death: pinning the feet of a falling
        // body drags it. (No ChibiFootIK: it reads scaled time and would freeze
        // while the conversation pauses the game.)
        foreach (var s in sm.states) s.state.iKOnFeet = s.state.name != NpcAnimatorParameters.DeathState;

        EditorUtility.SetDirty(controller);
        Debug.Log($"Birdy controller: {ControllerPath}\n  " +
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

    // ======================================================= seeded assets

    static NpcAnimationConfig EnsureConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<NpcAnimationConfig>(ConfigPath);
        if (config != null) return config;
        ChibiSandboxSetup.EnsureFolderPublic(Path.GetDirectoryName(ConfigPath)!.Replace('\\', '/'));
        config = ScriptableObject.CreateInstance<NpcAnimationConfig>();
        AssetDatabase.CreateAsset(config, ConfigPath);
        Debug.Log($"Seeded {ConfigPath}.");
        return config;
    }

    /// <summary>Seeded once. After that the asset is the source of truth and
    /// a re-run leaves hand edits alone.</summary>
    static DialogueDefinition EnsureDialogue()
    {
        var dialogue = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(DialoguePath);
        if (dialogue != null) return dialogue;
        ChibiSandboxSetup.EnsureFolderPublic(DialogueDir);
        dialogue = DialogueDefinition.CreateRuntime(InstanceName,
            new DialogueLine("Whirr-whirr! A real Guardian of Mathematics! I saw your compass-blade " +
                             "glinting from the rooftops!", DialogueEmote.Excited),
            new DialogueLine("I'm Birdy. My rotor runs on pure geometry: one perfect circle, " +
                             "spinning very, very fast.", DialogueEmote.Talk),
            new DialogueLine("You'll be wanting Sir Isaac. He paces the courtyard all day, muttering " +
                             "about falling bodies...", DialogueEmote.Thinking),
            new DialogueLine("His gravity wells will drag you in. But his weak spot is the inverse " +
                             "square. Distance is everything.", DialogueEmote.Thinking),
            new DialogueLine("Halve the distance and your blows land four times as hard! So get " +
                             "close. Brave-close!", DialogueEmote.Excited),
            new DialogueLine("And when the apples start raining, watch the ground for their marks. " +
                             "I'll cheer from a safe altitude!", DialogueEmote.Talk));
        AssetDatabase.CreateAsset(dialogue, DialoguePath);
        Debug.Log($"Seeded {DialoguePath} ({dialogue.LineCount} lines).");
        return dialogue;
    }

    // ============================================================ the prefab

    /// <summary>
    /// Prefabs/Npc/Birdy.prefab: root (capsule, Health, Talkable,
    /// NpcAnimatorDriver, NpcBody) over the model (Animator). Talkable and
    /// Health share the root, because CanTalk only checks a Health on its own
    /// object and lock-on locks the Health's object.
    ///
    /// An existing prefab is edited in place through LoadPrefabContents, so
    /// its components keep their file IDs and anything referencing them, in a
    /// scene or another asset, stays wired.
    /// </summary>
    static GameObject BuildPrefab(Avatar avatar, AnimatorController controller, Material material,
                                  DialogueDefinition dialogue, NpcAnimationConfig config, Bounds bounds)
    {
        ChibiSandboxSetup.EnsureFolderPublic(PrefabDir);
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        var root = exists ? PrefabUtility.LoadPrefabContents(PrefabPath) : new GameObject(InstanceName);
        try
        {
            root.transform.localScale = Vector3.one;

            // ---- model -------------------------------------------------------
            GameObject model = null;
            foreach (Transform child in root.transform)
            {
                if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == modelAsset)
                {
                    model = child.gameObject;
                    break;
                }
            }
            if (model == null)
            {
                var stale = root.transform.Find(ModelChildName);
                if (stale != null) Object.DestroyImmediate(stale.gameObject);
                model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
            }
            model.name = ModelChildName;
            model.transform.localPosition = new Vector3(0f, ModelYOffset, 0f);
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;

            var animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.Normal;   // the driver switches it while talking
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

            // The remap already points every slot at Birdy.mat; this covers a
            // renderer the remap missed.
            if (material != null)
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                    if (r.sharedMaterials.Any(m => m != material))
                        r.sharedMaterials = Enumerable.Repeat(material, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();

            var head = FindDeep(model.transform, HumanBoneName(avatar, "Head"));
            if (head == null) Debug.LogWarning("Birdy: no Head bone found; the Talkable falls back to a fixed height.");

            // ---- root components ---------------------------------------------
            float height = Mathf.Max(bounds.size.y, 0.2f);
            float radius = Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z) * CapsuleRadiusShare,
                                       0.1f, height * 0.4f);
            var capsule = GetOrAdd<CapsuleCollider>(root);
            capsule.isTrigger = false;      // sword sweeps and lock-on sight lines ignore triggers
            capsule.enabled = true;
            capsule.direction = 1;
            capsule.height = height;
            capsule.radius = radius;
            capsule.center = new Vector3(0f, height * 0.5f, 0f);

            var health = GetOrAdd<Health>(root);
            var hso = new SerializedObject(health);
            hso.FindProperty("maxHealth").floatValue = MaxHealth;
            // No grey pool: there is no fight to win it back in.
            hso.FindProperty("regainShare").floatValue = 0f;
            // A friendly NPC: cutting Birdy down must not be a 310-Lemma shortcut.
            hso.FindProperty("grantsXp").boolValue = false;
            hso.ApplyModifiedPropertiesWithoutUndo();

            var talkable = GetOrAdd<Talkable>(root);
            var tso = new SerializedObject(talkable);
            var dialogueProp = tso.FindProperty("dialogue");
            if (dialogueProp.objectReferenceValue == null) dialogueProp.objectReferenceValue = dialogue;
            tso.FindProperty("focusPoint").objectReferenceValue = head;
            tso.FindProperty("fallbackFocusHeight").floatValue = height * 0.7f;
            // The shared runtime set the player's interactor scans, so Birdy can
            // be talked to by walking up. DialogueSetup owns the asset and
            // creates it on first use, so this works before or after Setup
            // Dialogue. A hand-picked registry is left alone.
            var registryProp = tso.FindProperty("registry");
            if (registryProp.objectReferenceValue == null)
                registryProp.objectReferenceValue = DialogueSetup.EnsureTalkableSet();
            tso.ApplyModifiedPropertiesWithoutUndo();

            var driver = GetOrAdd<NpcAnimatorDriver>(root);
            var dso = new SerializedObject(driver);
            dso.FindProperty("animator").objectReferenceValue = animator;
            dso.FindProperty("talkable").objectReferenceValue = talkable;
            dso.FindProperty("health").objectReferenceValue = health;
            dso.FindProperty("config").objectReferenceValue = config;
            dso.ApplyModifiedPropertiesWithoutUndo();

            var body = GetOrAdd<NpcBody>(root);
            var bso = new SerializedObject(body);
            bso.FindProperty("blocker").objectReferenceValue = capsule;
            bso.FindProperty("health").objectReferenceValue = health;
            bso.ApplyModifiedPropertiesWithoutUndo();

            var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out bool ok);
            if (!ok)
            {
                Debug.LogError($"Setup Birdy NPC: could not save {PrefabPath}.");
                return null;
            }
            Debug.Log($"Birdy prefab: height {height:0.00} m, capsule r {radius:0.00} m, " +
                      $"focus {(head != null ? head.name : "fallback")}, HP {MaxHealth}.");
            return saved;
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        // Explicit null check, not ??: Unity's fake null defeats it.
        var c = go.GetComponent<T>();
        if (c == null) c = go.AddComponent<T>();
        return c;
    }

    static string HumanBoneName(Avatar avatar, string humanName)
        => avatar.humanDescription.human.FirstOrDefault(h => h.humanName == humanName).boneName;

    static Transform FindDeep(Transform parent, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var t in parent.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    // ============================================================ the scene

    /// <summary>
    /// One Birdy in the open ChibiSandbox, under the AcademyNpcs root, on flat
    /// open ground in view of the player's spawn and well clear of Newton, the
    /// pickups, the tether anchors and any training dummy, facing the spawn.
    /// An existing instance is kept where it is (extra ones are removed). Any
    /// other open scene only gets the prefab updated.
    /// </summary>
    static void PlaceInOpenScene(GameObject prefab, Bounds bounds)
    {
        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != SandboxScenePath)
        {
            Debug.Log($"Setup Birdy NPC: the open scene is '{scene.path}', not {SandboxScenePath}; " +
                      "the prefab is updated but nothing is placed.");
            return;
        }

        bool changed = false;
        var instances = new List<GameObject>();
        foreach (var top in scene.GetRootGameObjects())
            foreach (var t in top.GetComponentsInChildren<Transform>(true))
                if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) &&
                    PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == prefab)
                    instances.Add(t.gameObject);

        var npcRoot = scene.GetRootGameObjects().FirstOrDefault(g => g.name == NpcRootName);
        if (npcRoot == null)
        {
            npcRoot = new GameObject(NpcRootName);
            SceneManagerMoveToScene(npcRoot, scene);
            changed = true;
        }

        GameObject birdy;
        if (instances.Count > 0)
        {
            birdy = instances[0];
            for (int i = 1; i < instances.Count; i++)
            {
                Debug.LogWarning($"Setup Birdy NPC: removing duplicate {instances[i].name}.");
                Object.DestroyImmediate(instances[i]);
                changed = true;
            }
            if (birdy.transform.parent != npcRoot.transform)
            {
                birdy.transform.SetParent(npcRoot.transform, true);
                PrefabUtility.RecordPrefabInstancePropertyModifications(birdy.transform);
                changed = true;
            }
            if (birdy.name != InstanceName)
            {
                birdy.name = InstanceName;
                PrefabUtility.RecordPrefabInstancePropertyModifications(birdy);
                changed = true;
            }
            Debug.Log($"Birdy already placed at {birdy.transform.position}; left where it is.");
        }
        else
        {
            var player = Object.FindFirstObjectByType<ChibiLocomotion>();
            if (player == null)
            {
                Debug.LogWarning("Setup Birdy NPC: no ChibiLocomotion in the scene, so there is no spawn " +
                                 "to place Birdy near. Prefab updated; nothing placed.");
                return;
            }
            Vector3 spawn = player.transform.position;
            var cam = Camera.main;
            Vector3 view = cam != null ? cam.transform.forward : player.transform.forward;
            view.y = 0f;
            if (view.sqrMagnitude < 1e-4f) view = Vector3.forward;
            view.Normalize();

            float height = Mathf.Max(bounds.size.y, 0.2f);
            float radius = Mathf.Max(Mathf.Min(bounds.extents.x, bounds.extents.z) * CapsuleRadiusShare, 0.1f);
            if (!ChooseSpot(spawn, view, cam, height, radius, player.transform, out var spot, out string why))
            {
                spot = spawn + view * 4f;
                Debug.LogWarning($"Setup Birdy NPC: no clear spot found ({why}); placed at {spot}. " +
                                 "Move him by hand.");
            }

            birdy = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            birdy.name = InstanceName;
            birdy.transform.SetParent(npcRoot.transform, false);
            Vector3 look = spawn - spot;
            look.y = 0f;
            birdy.transform.SetPositionAndRotation(
                spot, look.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(look.normalized, Vector3.up) : Quaternion.identity);
            // Without this, script changes to a prefab instance are not
            // overrides yet, and the next prefab save resets them to the
            // prefab's own values (Birdy jumped to the origin on a re-run).
            PrefabUtility.RecordPrefabInstancePropertyModifications(birdy.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(birdy);
            changed = true;
            Debug.Log($"Birdy placed at {spot} ({Vector3.Distance(spot, spawn):0.0} m from the spawn), " +
                      $"facing it. {why}");
        }

        if (changed) EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = birdy;
    }

    static void SceneManagerMoveToScene(GameObject go, UnityEngine.SceneManagement.Scene scene)
    {
        if (go.scene != scene) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
    }

    /// <summary>
    /// Where Birdy stands: the candidate around the spawn that is on flat
    /// ground near spawn height, has room for him, keeps clear of Newton,
    /// training dummies, other NPCs and the footprint of every pickup and
    /// tether anchor, can be seen from the player's eye, and is most central in
    /// the starting camera's view at about <see cref="PreferredDistance"/>.
    /// If nothing in view qualifies, the first clear spot in a ring around
    /// the spawn (side away from Newton first) is used instead.
    /// </summary>
    static bool ChooseSpot(Vector3 spawn, Vector3 view, Camera cam, float height, float radius,
                           Transform player, out Vector3 spot, out string why)
    {
        Physics.SyncTransforms();   // autoSyncTransforms is off in this project
        var circles = new List<(Vector3 at, float clearance, string what)>();
        foreach (var b in Object.FindObjectsByType<NewtonBoss>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            circles.Add((b.transform.position, NewtonClearance, "Newton"));
        foreach (var d in Object.FindObjectsByType<TrainingDummy>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            circles.Add((d.transform.position, DummyClearance, "a training dummy"));
        foreach (var n in Object.FindObjectsByType<Talkable>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            circles.Add((n.transform.position, NpcClearance, n.name));
        // Pickups and anchors by their footprint: a pendulum frame is wide, a
        // pickup is small, and a fixed radius was either too strict or too loose.
        var boxes = new List<Bounds>();
        foreach (var p in Object.FindObjectsByType<AbilityPickup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            boxes.Add(Footprint(p.gameObject, PickupMargin + radius));
        foreach (var a in Object.FindObjectsByType<TetherAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            boxes.Add(Footprint(a.gameObject, AnchorMargin + radius));

        bool Blocked(Vector3 at)
        {
            foreach (var (pos, clearance, _) in circles)
            {
                Vector3 d = at - pos;
                d.y = 0f;
                if (d.magnitude < clearance) return true;
            }
            foreach (var b in boxes)
                if (at.x > b.min.x && at.x < b.max.x && at.z > b.min.z && at.z < b.max.z) return true;
            return false;
        }

        float baseYaw = Mathf.Atan2(view.x, view.z) * Mathf.Rad2Deg;
        Vector3 eye = spawn + Vector3.up * 1.4f;
        int tried = 0, noGround = 0, crowded = 0, noRoom = 0, hidden = 0, offscreen = 0;
        bool Usable(Vector3 probe, out Vector3 ground)
        {
            tried++;
            if (!GroundAt(probe, spawn.y, player, out ground)) { noGround++; return false; }
            if (Blocked(ground)) { crowded++; return false; }
            if (!HasRoom(ground, height, radius, player)) { noRoom++; return false; }
            if (!Visible(eye, ground + Vector3.up * height * 0.6f, player)) { hidden++; return false; }
            return true;
        }
        string Counts() => $"{tried} tried: no flat ground {noGround}, too close to something {crowded}, " +
                           $"no room {noRoom}, out of sight {hidden}, off the starting screen {offscreen}";

        // ---- in the starting view, most central first -------------------------
        bool found = false;
        float bestScore = float.PositiveInfinity, bestAngle = 0f, bestDistance = 0f, bestVx = 0f;
        spot = default;
        for (float a = -60f; a <= 60f; a += 10f)
            foreach (float d in new[] { 4.5f, 5f, 5.5f, 6f, 6.5f, 7f, 7.5f, 8f })
            {
                Vector3 dir = Quaternion.Euler(0f, baseYaw + a, 0f) * Vector3.forward;
                if (!Usable(spawn + dir * d, out var ground)) continue;
                float vx = 0.5f;
                if (cam != null)
                {
                    var vp = cam.WorldToViewportPoint(ground + Vector3.up * height * 0.75f);
                    if (vp.z <= 0f || vp.x < 0.15f || vp.x > 0.85f || vp.y < 0f || vp.y > 1f)
                    {
                        offscreen++;
                        continue;
                    }
                    vx = vp.x;
                }
                float score = Mathf.Abs(vx - 0.5f) * 2f + Mathf.Abs(d - PreferredDistance) * 0.15f;
                if (score >= bestScore) continue;
                bestScore = score;
                spot = ground;
                bestAngle = a;
                bestDistance = d;
                bestVx = vx;
                found = true;
            }
        if (found)
        {
            why = $"Chose {bestAngle:+0;-0}° off the start view, {bestDistance} m out, at viewport x " +
                  $"{bestVx:0.00} of the starting camera ({Counts()}).";
            return true;
        }

        // ---- anywhere clear around the spawn, away from Newton first ---------
        float side = 1f;
        var newton = circles.FirstOrDefault(c => c.what == "Newton");
        if (newton.what != null)
        {
            Vector3 to = newton.at - spawn;
            if (Mathf.DeltaAngle(baseYaw, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg) > 0f) side = -1f;
        }
        foreach (float a in new[] { 75f, -75f, 105f, -105f, 140f, -140f, 180f })
            foreach (float d in new[] { 4.5f, 5.5f, 3.5f, 6.5f, 8f })
            {
                Vector3 dir = Quaternion.Euler(0f, baseYaw + a * side, 0f) * Vector3.forward;
                if (!Usable(spawn + dir * d, out var ground)) continue;
                spot = ground;
                why = $"Nothing clear in the starting view; chose {a * side:+0;-0}° off it, {d} m out ({Counts()}).";
                return true;
            }
        why = $"no candidate qualified ({Counts()})";
        return false;
    }

    /// <summary>XZ footprint of a scene object's renderers, grown by <paramref name="margin"/>.</summary>
    static Bounds Footprint(GameObject go, float margin)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        var b = new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in renderers)
            if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds);
        b.Expand(new Vector3(margin * 2f, 0f, margin * 2f));
        return b;
    }

    static bool GroundAt(Vector3 probe, float spawnY, Transform player, out Vector3 ground)
    {
        ground = default;
        var from = new Vector3(probe.x, spawnY + 2.5f, probe.z);
        var hits = Physics.RaycastAll(from, Vector3.down, 2.5f + MaxGroundStep + 1f, ~0, QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        bool found = false;
        foreach (var h in hits)
        {
            if (h.transform.IsChildOf(player) || h.collider.GetComponentInParent<Health>() != null) continue;
            if (h.point.y <= best) continue;
            best = h.point.y;
            found = Vector3.Angle(h.normal, Vector3.up) <= MaxGroundSlope;
            ground = h.point;
        }
        return found && Mathf.Abs(ground.y - spawnY) <= MaxGroundStep;
    }

    static bool HasRoom(Vector3 ground, float height, float radius, Transform player)
    {
        const float lift = 0.25f, margin = 0.3f;
        float r = radius + margin;
        var a = ground + Vector3.up * (lift + r);
        var b = ground + Vector3.up * Mathf.Max(lift + r, height - r);
        foreach (var c in Physics.OverlapCapsule(a, b, r, ~0, QueryTriggerInteraction.Ignore))
            if (!c.transform.IsChildOf(player)) return false;
        return true;
    }

    static bool Visible(Vector3 eye, Vector3 target, Transform player)
    {
        Vector3 d = target - eye;
        foreach (var h in Physics.RaycastAll(eye, d.normalized, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
            if (!h.transform.IsChildOf(player)) return false;
        return true;
    }

    // ======================================================== diagnostics

    /// <summary>
    /// Evaluates each of Birdy's states on a throwaway copy of the prefab
    /// through the Animator (Play, then Update(0): the runtime path, per-state
    /// foot IK included) and logs, per sample: the lowest point of the body
    /// (soles), the rotor's lowest point, the top, how far the wings droop,
    /// the arm Down-Up muscle values, and how far the rotor has drifted
    /// relative to the Head bone since the bind pose (should be ~0: it is
    /// rigid to the head). For the death it also sweeps the clip and reports
    /// the sink with and without the configured ground lift. A stand-in for
    /// looking at him, for checks that have to run headless.
    /// </summary>
    [MenuItem("Tools/Pickleball/Diagnostics/Report Birdy Poses")]
    public static void ReportPoses()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var config = AssetDatabase.LoadAssetAtPath<NpcAnimationConfig>(ConfigPath);
        if (prefab == null || controller == null)
        {
            Debug.LogWarning("Report Birdy Poses: run Setup Birdy NPC first.");
            return;
        }

        var bind = Object.Instantiate(prefab);
        var go = Object.Instantiate(prefab);
        bind.hideFlags = go.hideFlags = HideFlags.HideAndDontSave;
        var baked = new Mesh();
        try
        {
            bind.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            bind.GetComponentInChildren<Animator>().enabled = false;
            var anim = go.GetComponentInChildren<Animator>();
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.Rebind();
            anim.Update(0f);

            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>();
            var bindSmrs = bind.GetComponentsInChildren<SkinnedMeshRenderer>();
            int rotor = Array.FindIndex(smrs, s => s.name == RotorPartName);
            string headName = HumanBoneName(anim.avatar, "Head");
            var head = FindDeep(go.transform, headName);
            var bindHead = FindDeep(bind.transform, headName);

            // Rotor vertices in Head space at bind, to measure drift against.
            Vector3[] rotorBind = null;
            if (rotor >= 0 && bindHead != null)
            {
                rotorBind = Baked(bindSmrs[rotor]);
                for (int v = 0; v < rotorBind.Length; v++) rotorBind[v] = bindHead.InverseTransformPoint(rotorBind[v]);
            }

            var handler = new HumanPoseHandler(anim.avatar, anim.transform);
            var pose = new HumanPose();
            int MuscleOf(HumanBodyBones b)
            {
                for (int dof = 0; dof < 3; dof++)
                {
                    int m = HumanTrait.MuscleFromBone((int)b, dof);
                    if (m >= 0 && HumanTrait.MuscleName[m].Contains("Down-Up")) return m;
                }
                return -1;
            }
            int lMuscle = MuscleOf(HumanBodyBones.LeftUpperArm), rMuscle = MuscleOf(HumanBodyBones.RightUpperArm);
            Transform Bone(HumanBodyBones b) => anim.GetBoneTransform(b);
            var lUpper = Bone(HumanBodyBones.LeftUpperArm);
            var rUpper = Bone(HumanBodyBones.RightUpperArm);
            var lHand = Bone(HumanBodyBones.LeftHand);
            var rHand = Bone(HumanBodyBones.RightHand);

            Vector3[] Baked(SkinnedMeshRenderer smr)
            {
                smr.BakeMesh(baked, true);
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                var verts = baked.vertices;
                for (int v = 0; v < verts.Length; v++) verts[v] = m.MultiplyPoint3x4(verts[v]);
                return verts;
            }

            (float body, float rotorLow, float top, float drift) Measure(float lift)
            {
                float body = float.PositiveInfinity, rotorLow = float.PositiveInfinity, top = float.NegativeInfinity;
                float drift = 0f;
                for (int s = 0; s < smrs.Length; s++)
                {
                    var verts = Baked(smrs[s]);
                    for (int v = 0; v < verts.Length; v++)
                    {
                        float y = verts[v].y + lift;
                        if (s == rotor) rotorLow = Mathf.Min(rotorLow, y);
                        else body = Mathf.Min(body, y);
                        top = Mathf.Max(top, y);
                        if (s == rotor && rotorBind != null && head != null)
                            drift = Mathf.Max(drift, Vector3.Distance(head.InverseTransformPoint(verts[v]), rotorBind[v]));
                    }
                }
                return (body, rotorLow, top, drift);
            }

            static float Droop(Transform upper, Transform hand)
            {
                if (upper == null || hand == null) return float.NaN;
                Vector3 d = hand.position - upper.position;
                return Mathf.Atan2(-d.y, new Vector2(d.x, d.z).magnitude) * Mathf.Rad2Deg;
            }

            string Line(string label, float lift = 0f)
            {
                var (body, rotorLow, top, drift) = Measure(lift);
                handler.GetHumanPose(ref pose);
                string muscles = lMuscle >= 0 ? $", arm down-up muscle L {pose.muscles[lMuscle]:+0.00;-0.00} R {pose.muscles[rMuscle]:+0.00;-0.00}" : "";
                return $"  {label,-30} soles {body:+0.000;-0.000} m, rotor low {rotorLow:+0.00;-0.00} m, top {top:0.00} m, " +
                       $"wing droop L {Droop(lUpper, lHand):0}° R {Droop(rUpper, rHand):0}°, " +
                       $"rotor drift vs Head {drift * 100f:0.0} cm{muscles}";
            }

            var lines = new List<string>
            {
                $"[BirdyPoses] model offset {ModelYOffset:0.000} m, humanScale {anim.humanScale:0.000}; " +
                "evaluated through the Animator (Play + Update, foot IK on)",
            };
            {
                var (body, rotorLow, top, _) = Measure(0f);
                lines.Add($"  {"bind pose (no animation)",-30} soles {body:+0.000;-0.000} m, rotor low {rotorLow:+0.00;-0.00} m, top {top:0.00} m");
            }

            var sm = controller.layers[0].stateMachine;
            foreach (var cs in sm.states)
            {
                if (!(cs.state.motion is AnimationClip clip)) continue;
                bool death = cs.state.name == NpcAnimatorParameters.DeathState;
                foreach (float f in death ? new[] { 0f, 0.5f, 1f } : new[] { 0.1f, 0.5f, 0.9f })
                {
                    anim.Play(cs.state.nameHash, 0, f);
                    anim.Update(0f);
                    lines.Add(Line($"{cs.state.name} ({clip.name}) @{f:0.0}"));
                }
                if (!death) continue;

                // Sweep the death: the lowest body point over the clip, raw and
                // with the configured lift, and where the sink begins.
                float amount = config != null ? config.DeathGroundLift : 0f;
                float start = config != null ? config.DeathLiftStart : 0f;
                float worstRaw = float.PositiveInfinity, worstLifted = float.PositiveInfinity, endRaw = 0f;
                float firstSink = -1f, maxHover = 0f;
                var profile = new List<string>();
                for (int i = 0; i <= 20; i++)
                {
                    float t = i / 20f;
                    anim.Play(cs.state.nameHash, 0, t);
                    anim.Update(0f);
                    float raw = Measure(0f).body;
                    float lifted = raw + NpcDeathLift.At(t, amount, start);
                    worstRaw = Mathf.Min(worstRaw, raw);
                    worstLifted = Mathf.Min(worstLifted, lifted);
                    if (firstSink < 0f && raw < -0.02f) firstSink = t;
                    if (i == 20) endRaw = raw;
                    // Hover: the body's lowest point above the ground while it
                    // should be touching it (the lift raising a standing body).
                    maxHover = Mathf.Max(maxHover, lifted - Mathf.Max(raw, 0f));
                    profile.Add($"{t:0.00}:{raw:+0.00;-0.00}");
                }
                lines.Add($"  death sink: raw lowest {worstRaw:+0.000;-0.000} m (end {endRaw:+0.000;-0.000}), " +
                          $"first below -2 cm at t={firstSink:0.00}; with lift {amount:0.000} m from t={start:0.00}: " +
                          $"lowest {worstLifted:+0.000;-0.000} m, end {endRaw + NpcDeathLift.At(1f, amount, start):+0.000;-0.000} m, " +
                          $"largest lift over a non-sunk body {maxHover:0.000} m");
                lines.Add("  death profile (t:raw soles) " + string.Join(" ", profile));
            }
            handler.Dispose();
            Debug.Log(string.Join("\n", lines));
        }
        finally
        {
            Object.DestroyImmediate(baked);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(bind);
        }
    }

    /// <summary>
    /// Command-line entry point (-executeMethod BirdyNpcSetup.BatchVerify).
    /// Opens ChibiSandbox, runs Setup Birdy NPC twice (the second run must
    /// change nothing), reports the result and the poses, and exits with 1 if
    /// anything logged an error. Never saves the scene. Run it in a separate
    /// worktree, never against the shared Editor's checkout.
    /// </summary>
    public static void BatchVerify()
    {
        int errors = 0;
        void Count(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
        }
        Application.logMessageReceived += Count;
        try
        {
            EditorSceneManager.OpenScene(SandboxScenePath, OpenSceneMode.Single);
            Run();
            Run();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var scene = EditorSceneManager.GetActiveScene();
            int count = 0;
            foreach (var top in scene.GetRootGameObjects())
                foreach (var t in top.GetComponentsInChildren<Transform>(true))
                    if (PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) &&
                        PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == prefab)
                        count++;
            var comps = prefab != null
                ? string.Join(", ", prefab.GetComponents<Component>().Select(c => c.GetType().Name))
                : "(no prefab)";
            Debug.Log($"[BirdyVerify] instances in scene: {count}; prefab root components: {comps}; " +
                      $"scene dirty: {scene.isDirty} (not saved)");
            if (count != 1) Debug.LogError($"[BirdyVerify] expected exactly one Birdy, found {count}.");
            ReportPoses();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            Application.logMessageReceived -= Count;
        }
        Debug.Log($"[BirdyVerify] finished with {errors} error(s).");
        EditorApplication.Exit(errors == 0 ? 0 : 1);
    }
}
