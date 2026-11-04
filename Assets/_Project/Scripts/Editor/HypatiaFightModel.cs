using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pickleball.Sandbox.Boss.Hypatia.Presentation;
using Pickleball.Sandbox.Cloth;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Hypatia's model: the Tripo toga (Mixamo-rigged, finger bones included; prepared
/// by Tools/Blender/hypatia_toga_*.py) imported as a Humanoid at about 1.7 m, with
/// a URP Lit material on her 4k colour map capped at 2048, and her fight's animator
/// controller. Called by <see cref="HypatiaFightSetup"/>.
///
/// Idempotent by comparison: every importer, texture and material setting is
/// compared before it is written, and the FBX is reimported only when one
/// actually changed, so a second run touches nothing on disk.
///
/// A Humanoid avatar that fails to build is an error, not a silent fallback to
/// Generic: <see cref="Configure"/> returns false and the setup stops.
/// </summary>
public static class HypatiaFightModel
{
    public const string ModelPath = HypatiaFightArt.ArtDir + "/HypatiaToga.fbx";
    public const string TexturePath = HypatiaFightArt.ArtDir + "/HypatiaToga_Color.jpg";
    public const string MaterialPath = HypatiaFightArt.ArtDir + "/HypatiaToga.mat";
    public const string ControllerPath = "Assets/_Project/Animation/Hypatia/Hypatia.controller";

    /// <summary>The spring settings for her Cloth_* chains. ClothSetup finds it by the
    /// model's file name, so it must be "(model name)Cloth.asset".</summary>
    public const string ClothConfigPath = "Assets/_Project/ScriptableObjects/Cloth/HypatiaTogaCloth.asset";

    /// <summary>Newton's idle (Humanoid, set up by Setup Newton (Boss)). Read
    /// only: retargeted through her avatar, it gives her a breathing stand while
    /// the brain's procedural hover carries her.</summary>
    public const string IdleClipPath = "Assets/_Project/Animation/Newton/Idle.fbx";

    /// <summary>Newton's hit reaction (Humanoid), read only: the PLACEHOLDER for her
    /// topple's stagger until real Mixamo "Kneeling Down"/"Stunned" and
    /// "Standing Up" clips are imported for her.</summary>
    public const string ToppleClipPath = "Assets/_Project/Animation/Newton/Hit.fbx";

    /// <summary>The animator bool ToppleView sets while she is toppled.</summary>
    public const string ToppledParameter = "Toppled";

    /// <summary>Her fight clips, as .anim assets. They are copies, so the source FBXs'
    /// importers stay untouched (Docs/Systems/HypatiaBoss.md lists where each came from).</summary>
    public const string ClipDir = "Assets/_Project/Animation/Hypatia/Clips";

    /// <summary>The upper-body layer's mask.</summary>
    public const string UpperBodyMaskPath = "Assets/_Project/Animation/Hypatia/HypatiaUpperBody.mask";

    static readonly HypatiaAnimationDirector.Action[] UpperBodyActions =
    {
        HypatiaAnimationDirector.Action.Rings, HypatiaAnimationDirector.Action.ConicEllipse,
        HypatiaAnimationDirector.Action.ConicParabola, HypatiaAnimationDirector.Action.ConicHyperbola,
        HypatiaAnimationDirector.Action.Pulse, HypatiaAnimationDirector.Action.Burning,
        HypatiaAnimationDirector.Action.PhaseTwo,
    };

    static readonly HypatiaAnimationDirector.Action[] FullBodyActions =
    {
        HypatiaAnimationDirector.Action.Death,
    };

    static string ClipPath(HypatiaAnimationDirector.Action action) =>
        $"{ClipDir}/{HypatiaAnimationView.ClipPrefix}{HypatiaAnimationView.StateName(action)}.anim";

    /// <summary>Every fight clip's path, for the setup's fingerprint.</summary>
    public static IEnumerable<string> FightClipPaths => UpperBodyActions.Concat(FullBodyActions).Select(ClipPath);

    /// <summary>Her height in metres: human scale, deliberately smaller than
    /// Newton's 2.2 m. She hovers, which reads as presence without bulk.</summary>
    public const float TargetHeight = 1.7f;

    const int MaxTextureSize = 2048;

    /// <summary>Arm bones further than this from straight out in the avatar's
    /// reference pose are rotated to it (<see cref="EnforceArmTPose"/>).</summary>
    const float TPoseToleranceDegrees = 2f;

    /// <summary>
    /// Configure the importer, material and texture; reimport only on a change.
    /// False (with an error logged) if the model is missing or the avatar is not
    /// a valid Humanoid.
    /// </summary>
    public static bool Configure(out Avatar avatar)
    {
        avatar = null;
        if (AssetImporter.GetAtPath(ModelPath) is not ModelImporter importer)
        {
            Debug.LogError($"[HypatiaFight] Model not found at {ModelPath}.");
            return false;
        }

        ConfigureTexture();
        var material = EnsureMaterial();

        bool changed = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            changed = true;
        }
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            changed = true;
        }
        if (importer.importAnimation)
        {
            importer.importAnimation = false;   // her motion comes from the controller
            changed = true;
        }

        float scale = ScaleFor(importer);
        bool rescaled = Mathf.Abs(importer.globalScale - scale) > 0.0005f;
        if (rescaled)
        {
            importer.globalScale = scale;
            changed = true;
        }
        if (changed) importer.SaveAndReimport();

        if (material != null && RemapMaterials(importer, material)) importer.SaveAndReimport();
        EnsureHumanMap(importer, rescaled);
        EnforceArmTPose(importer);
        EnsureClothConfig();

        avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"[HypatiaFight] Hypatia's Humanoid avatar failed (avatar={(avatar != null)}, " +
                           $"valid={avatar != null && avatar.isValid}, human={avatar != null && avatar.isHuman}). " +
                           $"Check {ModelPath} ▸ Rig ▸ Configure. Not falling back to Generic.");
            return false;
        }
        return true;
    }

    /// <summary>Unity human bone name to rig bone, for the Mixamo skeleton.</summary>
    static Dictionary<string, string> BuildHumanMap()
    {
        const string rig = "mixamorig:";
        var map = new Dictionary<string, string>
        {
            { "Hips", rig + "Hips" }, { "Spine", rig + "Spine" }, { "Chest", rig + "Spine1" },
            { "UpperChest", rig + "Spine2" }, { "Neck", rig + "Neck" }, { "Head", rig + "Head" },
        };
        (string finger, string bone)[] digits =
            { ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Little", "Pinky") };
        string[] joints = { "Proximal", "Intermediate", "Distal" };
        foreach (string side in new[] { "Left", "Right" })
        {
            map[side + "UpperLeg"] = rig + side + "UpLeg";
            map[side + "LowerLeg"] = rig + side + "Leg";
            map[side + "Foot"] = rig + side + "Foot";
            map[side + "Toes"] = rig + side + "ToeBase";
            map[side + "Shoulder"] = rig + side + "Shoulder";
            map[side + "UpperArm"] = rig + side + "Arm";
            map[side + "LowerArm"] = rig + side + "ForeArm";
            map[side + "Hand"] = rig + side + "Hand";
            foreach (var (finger, bone) in digits)
                for (int j = 0; j < joints.Length; j++)
                    map[$"{side} {finger} {joints[j]}"] = $"{rig}{side}Hand{bone}{j + 1}";
        }
        return map;
    }

    /// <summary>
    /// Map the avatar by name, never by Unity's guess: the auto-mapper takes the rig's
    /// Cloth_Skirt_* bones, which sit under the hips beside the legs, for her upper legs
    /// and then cannot find a lower leg. A wrong map, or a rescale, leaves a stale
    /// reference pose, so both are dropped and the reimport captures the pose anew.
    /// Writes nothing when the map is already right, so a re-run is a no-op.
    /// </summary>
    static void EnsureHumanMap(ModelImporter importer, bool rescaled)
    {
        var map = BuildHumanMap();
        var desc = importer.humanDescription;
        var current = desc.human ?? System.Array.Empty<HumanBone>();
        bool right = current.Length == map.Count &&
                     current.All(h => map.TryGetValue(h.humanName, out var bone) && bone == h.boneName);
        if (right && !rescaled) return;

        desc.skeleton = System.Array.Empty<SkeletonBone>();
        desc.human = System.Array.Empty<HumanBone>();
        importer.humanDescription = desc;
        importer.SaveAndReimport();

        desc = importer.humanDescription;
        desc.human = map.Select(kv => new HumanBone
        {
            humanName = kv.Key,
            boneName = kv.Value,
            limit = new HumanLimit { useDefaultValues = true },
        }).ToArray();
        importer.humanDescription = desc;
        importer.SaveAndReimport();
    }

    /// <summary>Created once with the values tuned for her long gown and veil, stiffer
    /// than ClothSetup's defaults so a floor-length hem does not balloon; later runs
    /// leave it alone, so Inspector tuning survives.</summary>
    static void EnsureClothConfig()
    {
        if (AssetDatabase.LoadAssetAtPath<SpringClothConfig>(ClothConfigPath) != null) return;
        var cfg = ClothSetup.LoadOrCreateConfig(Path.GetFileNameWithoutExtension(ModelPath));
        cfg.stiffnessRoot = 0.30f;
        cfg.stiffnessTip = 0.12f;
        cfg.dampingRoot = 0.30f;
        cfg.dampingTip = 0.18f;
        cfg.maxAngle = 25f;
        EditorUtility.SetDirty(cfg);
        AssetDatabase.SaveAssetIfDirty(cfg);
    }

    /// <summary>
    /// Unity's Enforce T-Pose, for the arm chains only. Humanoid retargeting treats
    /// the avatar's reference pose as the T-pose, and this model binds her upper arms
    /// about 43° below horizontal, which would add that droop to every clip. Each upper
    /// arm and forearm in the stored reference skeleton is rotated to point straight
    /// out; the mesh's bind pose is untouched. The same rule as BirdyNpcSetup. Writes
    /// nothing when the arms are already within tolerance, so a re-run is a no-op.
    /// </summary>
    static void EnforceArmTPose(ModelImporter importer)
    {
        var desc = importer.humanDescription;
        var skeleton = desc.skeleton;
        if (skeleton == null || skeleton.Length == 0)
        {
            Debug.LogWarning("[HypatiaFight] T-pose: no reference skeleton stored; skipped.");
            return;
        }
        string Bone(string human) => desc.human.FirstOrDefault(h => h.humanName == human).boneName;

        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var go = Object.Instantiate(asset);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var byName = new Dictionary<string, Transform>();
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
                if (t != go.transform) byName[t.name] = t;
            // Pose the instance in the stored reference pose, which differs from the
            // bind pose after an earlier run.
            foreach (var sb in skeleton)
            {
                if (!byName.TryGetValue(sb.name, out var t)) continue;
                t.localPosition = sb.position;
                t.localRotation = sb.rotation;
                t.localScale = sb.scale;
            }

            var changed = new HashSet<string>();
            var report = new List<string>();
            foreach (var side in new[] { "Left", "Right" })
            {
                if (!byName.TryGetValue(Bone(side + "UpperArm") ?? "", out var upper) ||
                    !byName.TryGetValue(Bone(side + "LowerArm") ?? "", out var lower) ||
                    !byName.TryGetValue(Bone(side + "Hand") ?? "", out var hand))
                {
                    Debug.LogWarning($"[HypatiaFight] T-pose: {side} arm chain not mapped; skipped.");
                    continue;
                }
                Vector3 outward = go.transform.right *
                                  Mathf.Sign(Vector3.Dot(hand.position - upper.position, go.transform.right));
                report.Add(Straighten(upper, lower.position - upper.position, outward, changed));
                report.Add(Straighten(lower, hand.position - lower.position, outward, changed));
            }
            Debug.Log($"[HypatiaFight] T-pose (arm angle from straight out): {string.Join(", ", report)}");
            if (changed.Count == 0) return;

            for (int i = 0; i < skeleton.Length; i++)
                if (changed.Contains(skeleton[i].name))
                    skeleton[i].rotation = byName[skeleton[i].name].localRotation;
            desc.skeleton = skeleton;
            importer.humanDescription = desc;
            importer.SaveAndReimport();
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

    /// <summary>The import scale that makes her <see cref="TargetHeight"/> tall,
    /// measured from her bind-pose bounds at the current scale and rounded so the
    /// same model always gives the same number.</summary>
    static float ScaleFor(ModelImporter importer)
    {
        float current = Mathf.Max(1e-4f, importer.globalScale);
        float height = MeasureHeight(out _);
        if (height <= 1e-4f) return current;
        float raw = height / current;
        return Mathf.Round(TargetHeight / raw * 1000f) / 1000f;
    }

    /// <summary>Bind-pose height of the imported model (m) and how far its lowest
    /// point sits below its origin (so the setup can stand her feet on Body's origin).</summary>
    public static float MeasureHeight(out float belowOrigin)
    {
        belowOrigin = 0f;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (prefab == null) return 0f;
        var go = (GameObject)Object.Instantiate(prefab);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return 0f;
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            belowOrigin = Mathf.Round(Mathf.Max(0f, -bounds.min.y) * 1000f) / 1000f;
            return bounds.size.y;
        }
        finally { Object.DestroyImmediate(go); }
    }

    static void ConfigureTexture()
    {
        if (AssetImporter.GetAtPath(TexturePath) is not TextureImporter ti) return;
        if (ti.maxTextureSize == MaxTextureSize) return;
        ti.maxTextureSize = MaxTextureSize;
        ti.SaveAndReimport();
    }

    /// <summary>Created once; later runs only fill a missing texture, so colour
    /// tweaks made in the Inspector survive.</summary>
    static Material EnsureMaterial()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetTexture("_BaseMap", tex);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.15f);
            mat.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        else if (mat.GetTexture("_BaseMap") == null && tex != null)
        {
            mat.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(mat);
        }
        return mat;
    }

    /// <summary>Point every material slot the FBX declares at ours. True when
    /// the map changed (the caller reimports).</summary>
    static bool RemapMaterials(ModelImporter importer, Material material)
    {
        var map = importer.GetExternalObjectMap();
        bool changed = false;
        foreach (var embedded in AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>())
        {
            var id = new AssetImporter.SourceAssetIdentifier(typeof(Material), embedded.name);
            if (map.TryGetValue(id, out var mapped) && mapped == material) continue;
            importer.AddRemap(id, material);
            changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Her controller: an Idle state on Newton's Humanoid idle, retargeted, plus
    /// the topple (<see cref="EnsureTopple"/>). Created once; a later run only
    /// re-points the idle if its clip changed and adds what is missing.
    /// Null (and a warning) when no Humanoid idle clip is available: the
    /// procedural hover still runs without one.
    /// </summary>
    public static AnimatorController EnsureController()
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(IdleClipPath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__") && c.isHumanMotion);
        if (clip == null)
        {
            Debug.LogWarning($"[HypatiaFight] No Humanoid idle clip at {IdleClipPath}; Hypatia gets no " +
                             "animator controller (her hover still runs).");
            return null;
        }

        ChibiSandboxSetup.EnsureFolderPublic(Path.GetDirectoryName(ControllerPath)!.Replace('\\', '/'));
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var sm = controller.layers[0].stateMachine;
            var idle = sm.AddState("Idle");
            idle.motion = clip;
            sm.defaultState = idle;
            EnsureTopple(controller, idle, clip);
            EnsureFightClips(controller);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        var machine = controller.layers[0].stateMachine;
        var state = machine.defaultState;
        if (state == null)
        {
            state = machine.AddState("Idle");
            machine.defaultState = state;
        }
        if (state.motion != clip)
        {
            state.motion = clip;
            EditorUtility.SetDirty(controller);
        }
        EnsureTopple(controller, state, clip);
        EnsureFightClips(controller);
        return controller;
    }

    /// <summary>
    /// Her fight clips: Death on the base layer (full-body), each attack and Phase Two
    /// on an UpperBody layer whose resting Empty state has no motion, so the base layer
    /// shows through. No transitions: <see cref="HypatiaAnimationView"/> starts and ends
    /// each clip itself, so a clip's end follows her clock and Death simply holds.
    /// States are added once and only re-pointed if the clip changed. A missing clip
    /// warns and is skipped, and the view plays that attack without a clip.
    /// </summary>
    static void EnsureFightClips(AnimatorController controller)
    {
        var mask = EnsureUpperBodyMask();

        if (controller.parameters.All(p => p.name != HypatiaAnimationView.SpeedParameter))
        {
            controller.AddParameter(new AnimatorControllerParameter
            {
                name = HypatiaAnimationView.SpeedParameter,
                type = AnimatorControllerParameterType.Float,
                defaultFloat = 1f,
            });
            EditorUtility.SetDirty(controller);
        }

        int upper = System.Array.FindIndex(controller.layers, l => l.name == HypatiaAnimationView.UpperLayer);
        if (upper < 0)
        {
            controller.AddLayer(HypatiaAnimationView.UpperLayer);
            upper = controller.layers.Length - 1;
            EditorUtility.SetDirty(controller);
        }

        // Layers are returned by value: changes only stick when the array is written back.
        var layers = controller.layers;
        if (layers[upper].avatarMask != mask || !Mathf.Approximately(layers[upper].defaultWeight, 1f)
            || layers[upper].blendingMode != AnimatorLayerBlendingMode.Override)
        {
            layers[upper].avatarMask = mask;
            layers[upper].defaultWeight = 1f;
            layers[upper].blendingMode = AnimatorLayerBlendingMode.Override;
            controller.layers = layers;
            EditorUtility.SetDirty(controller);
        }

        var upperMachine = controller.layers[upper].stateMachine;
        var empty = upperMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == HypatiaAnimationView.EmptyState);
        if (empty == null)
        {
            empty = upperMachine.AddState(HypatiaAnimationView.EmptyState);
            upperMachine.defaultState = empty;
            EditorUtility.SetDirty(controller);
        }

        foreach (var action in UpperBodyActions) EnsureClipState(controller, upperMachine, action, speedParameter: true);
        var baseMachine = controller.layers[0].stateMachine;
        foreach (var action in FullBodyActions) EnsureClipState(controller, baseMachine, action, speedParameter: false);
    }

    static void EnsureClipState(AnimatorController controller, AnimatorStateMachine machine,
                                HypatiaAnimationDirector.Action action, bool speedParameter)
    {
        string name = HypatiaAnimationView.StateName(action);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath(action));
        if (clip == null || !clip.isHumanMotion)
        {
            Debug.LogWarning($"[HypatiaFight] No Humanoid clip at {ClipPath(action)}; her '{name}' has no animation.");
            return;
        }

        var state = machine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
        if (state == null)
        {
            state = machine.AddState(name);
            EditorUtility.SetDirty(controller);
        }
        if (state.motion != clip)
        {
            state.motion = clip;
            EditorUtility.SetDirty(controller);
        }
        if (state.iKOnFeet || state.speedParameterActive != speedParameter)
        {
            state.iKOnFeet = false;
            state.speedParameterActive = speedParameter;
            state.speedParameter = speedParameter ? HypatiaAnimationView.SpeedParameter : "";
            EditorUtility.SetDirty(controller);
        }
    }

    /// <summary>Spine, head, arms and fingers. Root and legs stay with the base layer,
    /// so an attack neither moves her nor throws her dress around. Created once.</summary>
    static AvatarMask EnsureUpperBodyMask()
    {
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(UpperBodyMaskPath);
        if (mask != null) return mask;
        mask = new AvatarMask();
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        {
            var part = (AvatarMaskBodyPart)i;
            mask.SetHumanoidBodyPartActive(part, part is AvatarMaskBodyPart.Body or AvatarMaskBodyPart.Head
                or AvatarMaskBodyPart.LeftArm or AvatarMaskBodyPart.RightArm
                or AvatarMaskBodyPart.LeftFingers or AvatarMaskBodyPart.RightFingers);
        }
        AssetDatabase.CreateAsset(mask, UpperBodyMaskPath);
        return mask;
    }

    /// <summary>
    /// The topple, added once: a <c>Toppled</c> bool, a ToppleHit state on
    /// Newton's Hit clip (the placeholder stagger) and a ToppledHold state on the
    /// idle, with Idle → ToppleHit when it turns on, ToppleHit → ToppledHold when
    /// the clip ends, and either back to Idle when it turns off. The procedural
    /// sink and pitch (HypatiaBoss, ToppleView) carry the topple on their own;
    /// this only adds a reaction. Skipped (procedural only) when the Hit clip is
    /// missing or not Humanoid; never touches states that already exist, so a
    /// hand-tuned transition survives a rebuild.
    /// </summary>
    static void EnsureTopple(AnimatorController controller, AnimatorState idle, AnimationClip idleClip)
    {
        var hit = AssetDatabase.LoadAllAssetsAtPath(ToppleClipPath).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__") && c.isHumanMotion);
        if (hit == null)
        {
            Debug.LogWarning($"[HypatiaFight] No Humanoid clip at {ToppleClipPath}; her topple is procedural only.");
            return;
        }

        bool changed = false;
        if (controller.parameters.All(p => p.name != ToppledParameter))
        {
            controller.AddParameter(ToppledParameter, AnimatorControllerParameterType.Bool);
            changed = true;
        }

        var sm = controller.layers[0].stateMachine;
        if (sm.states.All(s => s.state.name != "ToppleHit"))
        {
            var stagger = sm.AddState("ToppleHit", new Vector3(300f, 120f, 0f));
            stagger.motion = hit;
            var hold = sm.AddState("ToppledHold", new Vector3(300f, 240f, 0f));
            hold.motion = idleClip;

            var down = idle.AddTransition(stagger);
            down.hasExitTime = false;
            down.duration = 0.1f;
            down.AddCondition(AnimatorConditionMode.If, 0f, ToppledParameter);

            var settle = stagger.AddTransition(hold);
            settle.hasExitTime = true;
            settle.exitTime = 0.9f;
            settle.duration = 0.25f;

            foreach (var from in new[] { stagger, hold })
            {
                var up = from.AddTransition(idle);
                up.hasExitTime = false;
                up.duration = 0.3f;
                up.AddCondition(AnimatorConditionMode.IfNot, 0f, ToppledParameter);
            }
            changed = true;
        }
        if (changed) EditorUtility.SetDirty(controller);
    }
}
