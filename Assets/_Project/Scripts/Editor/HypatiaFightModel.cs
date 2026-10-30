using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Hypatia's model: the Tripo princess (Mixamo-rigged, finger bones included)
/// imported as a Humanoid at about 1.7 m, with a URP Lit material on her 4k
/// colour map capped at 2048, and an idle-only animator controller. Called by
/// <see cref="HypatiaFightSetup"/>.
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
    public const string ModelPath = HypatiaFightArt.ArtDir + "/HypatiaPrincess.fbx";
    public const string TexturePath = HypatiaFightArt.ArtDir + "/HypatiaPrincess_Color.jpg";
    public const string MaterialPath = HypatiaFightArt.ArtDir + "/HypatiaPrincess.mat";
    public const string ControllerPath = "Assets/_Project/Animation/Hypatia/Hypatia.controller";

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

    /// <summary>Her height in metres: human scale, deliberately smaller than
    /// Newton's 2.2 m. She hovers, which reads as presence without bulk.</summary>
    public const float TargetHeight = 1.7f;

    const int MaxTextureSize = 2048;

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
        if (Mathf.Abs(importer.globalScale - scale) > 0.0005f)
        {
            importer.globalScale = scale;
            changed = true;
        }
        if (changed) importer.SaveAndReimport();

        if (material != null && RemapMaterials(importer, material)) importer.SaveAndReimport();

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
        return controller;
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
