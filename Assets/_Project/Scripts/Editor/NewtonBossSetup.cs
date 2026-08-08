using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Imports Isaac Newton as a Humanoid boss and drops him into the sandbox.
///
/// Tools ▸ Pickleball ▸ Setup Newton (Boss)
///
/// Deliberately separate from ChibiSandboxSetup: the player and a boss want
/// different controllers, and a boss should not inherit WASD locomotion. The
/// two share the reusable parts — the Tripo bone map, the rig setup and the
/// material extraction — through ChibiSandboxSetup's parameterised helpers.
///
/// Attack clips drop in the same way every other animation has: put the FBX in
/// Animation/Newton/ and re-run. They retarget through the Humanoid avatar, so
/// any Mixamo clip works without touching his skeleton.
/// </summary>
public static class NewtonBossSetup
{
    const string CharacterDir = "Assets/_Project/Art/Characters/Newton";
    const string ModelPath = CharacterDir + "/Newton.fbx";
    const string ClipDir = "Assets/_Project/Animation/Newton";
    const string ControllerPath = "Assets/_Project/Animation/Controllers/NewtonBoss.controller";
    const string PrefabPath = "Assets/_Project/Prefabs/NewtonBoss.prefab";

    /// <summary>Bosses read as threatening when they are simply bigger. Newton
    /// was historically about 1.7m; this is a deliberate gameplay choice, not a
    /// historical one.</summary>
    const float BossHeight = 2.2f;

    /// <summary>
    /// Newton's hips sit at 50.5% of his height against the chibi's 41.1% — he
    /// is human-proportioned, so Humanoid retargeting lands his feet close to
    /// correct and he needs nothing like the chibi's 0.64 fudge. Left at zero;
    /// nudge only if his feet visibly sink.
    /// </summary>
    const float ModelYOffset = 0f;

    /// <summary>Clip roles, matched by filename. Idle is the only one required;
    /// everything else is picked up when the file appears.</summary>
    static readonly (string role, string file, bool loop)[] ClipDefs =
    {
        ("idle",   "Idle.fbx",   true),
        ("walk",   "Walking.fbx", true),
        ("attack", "Attack.fbx", false),
        ("attack2", "Attack2.fbx", false),
        ("hit",    "Hit.fbx",    false),
        ("death",  "Death.fbx",  false),
    };

    [MenuItem("Tools/Pickleball/Setup Newton (Boss)")]
    public static void Run()
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"Newton not found at {ModelPath}. Run the Blender " +
                           "pipeline first (tripo_to_unity.py).");
            return;
        }

        ChibiSandboxSetup.EnsureFolderPublic(ClipDir);
        ChibiSandboxSetup.EnsureFolderPublic(Path.GetDirectoryName(ControllerPath)!.Replace('\\', '/'));
        ChibiSandboxSetup.EnsureFolderPublic(Path.GetDirectoryName(PrefabPath)!.Replace('\\', '/'));

        ChibiSandboxSetup.SetupMaterialPublic(importer, ModelPath, CharacterDir, "Newton");
        if (!ChibiSandboxSetup.SetupRigPublic(importer, BossHeight)) return;

        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"Newton avatar invalid (valid={avatar?.isValid}, " +
                           $"human={avatar?.isHuman}). Check FBX ▸ Rig ▸ Configure.");
            return;
        }
        Debug.Log($"Newton avatar OK — human={avatar.isHuman}, scale {BossHeight}.");

        var clips = LoadClips();
        if (clips.Count == 0)
        {
            Debug.LogWarning($"No clips in {ClipDir}. Drop Mixamo FBXs there " +
                             "(Idle.fbx, Attack.fbx, ...) and re-run. Rig is still set up.");
        }

        var controller = BuildController(clips);
        Place(avatar, controller);
        AssetDatabase.SaveAssets();
    }

    static System.Collections.Generic.Dictionary<string, AnimationClip> LoadClips()
    {
        var result = new System.Collections.Generic.Dictionary<string, AnimationClip>();
        foreach (var (role, file, loop) in ClipDefs)
        {
            string path = $"{ClipDir}/{file}";
            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) continue;

            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;

            var defs = imp.defaultClipAnimations;
            if (defs is { Length: > 0 })
            {
                foreach (var d in defs)
                {
                    d.loopTime = loop;
                    // In place: the boss's controller owns his movement.
                    d.lockRootRotation = false;
                    d.lockRootHeightY = false;
                    d.lockRootPositionXZ = false;
                    d.keepOriginalPositionY = false;
                }
                imp.clipAnimations = defs;
            }
            imp.SaveAndReimport();

            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) continue;
            result[role] = clip;
            Debug.Log($"  {role,-8} <- {file,-16} {clip.length:0.00}s loop={loop}");
        }
        return result;
    }

    static AnimatorController BuildController(
        System.Collections.Generic.Dictionary<string, AnimationClip> clips)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Attack", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Attack2", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Hit", AnimatorControllerParameterType.Trigger);
        controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

        var sm = controller.layers[0].stateMachine;

        var idle = sm.AddState("Idle");
        if (clips.TryGetValue("idle", out var idleClip)) idle.motion = idleClip;
        sm.defaultState = idle;

        // One-shots that return to idle on their own. Kept as a loop over roles
        // so adding a third attack is one line in ClipDefs, not new plumbing.
        foreach (var (role, param) in new[]
                 { ("attack", "Attack"), ("attack2", "Attack2"), ("hit", "Hit") })
        {
            if (!clips.TryGetValue(role, out var clip)) continue;
            var state = sm.AddState(char.ToUpper(role[0]) + role[1..]);
            state.motion = clip;

            var t = idle.AddTransition(state);
            t.hasExitTime = false; t.duration = 0.1f;
            t.AddCondition(AnimatorConditionMode.If, 0, param);

            var back = state.AddTransition(idle);
            back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.15f;
        }

        if (clips.TryGetValue("death", out var deathClip))
        {
            var dead = sm.AddState("Death");
            dead.motion = deathClip;
            var t = sm.AddAnyStateTransition(dead);
            t.hasExitTime = false; t.duration = 0.2f;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, "Dead");
        }

        EditorUtility.SetDirty(controller);
        Debug.Log($"Newton controller: {ControllerPath} ({sm.states.Length} states)");
        return controller;
    }

    static void Place(Avatar avatar, AnimatorController controller)
    {
        // Reuse whatever is already open — this does not stomp the scene.
        var existing = GameObject.Find("NewtonBoss");
        if (existing != null)
        {
            // ...but it does stomp the boss. This rebuilds the root from scratch,
            // so anything NewtonFightSetup attached (Health, NewtonBoss, the beam,
            // the barrage, the BeamMuzzle child) goes with it, and the HUD is left
            // holding dangling references. Loud, because the failure is otherwise
            // silent: the fight simply stops working with no error anywhere.
            if (existing.GetComponent("NewtonBoss") != null)
                Debug.LogWarning("Rebuilding NewtonBoss — this DESTROYS the fight " +
                                 "wiring attached by Tools ▸ Pickleball ▸ Setup " +
                                 "Newton Fight. Re-run that afterwards, and note " +
                                 "Fight must always be run last.");
            Object.DestroyImmediate(existing);
        }

        var root = new GameObject("NewtonBoss");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        model.name = "NewtonModel";
        model.transform.SetParent(root.transform, false);
        model.transform.localPosition = new Vector3(0f, ModelYOffset, 0f);

        var animator = model.GetComponent<Animator>() ?? model.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;

        // Capsule sized from his actual bounds, like the player's.
        var renderers = model.GetComponentsInChildren<Renderer>();
        var bounds = renderers.Length > 0 ? renderers[0].bounds : new Bounds(root.transform.position, Vector3.one);
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.height = Mathf.Max(bounds.size.y, 0.1f);
        capsule.radius = Mathf.Clamp(Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.6f,
                                     0.1f, capsule.height * 0.4f);
        capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);

        // Drop him on the ground near, but not on top of, the player spawn.
        Vector3 at = new(6f, 200f, -8f);
        var player = GameObject.Find("Chibi");
        if (player != null) at = player.transform.position + new Vector3(6f, 200f, -8f);
        if (Physics.Raycast(at, Vector3.down, out var hit, 400f, ~0, QueryTriggerInteraction.Ignore))
            root.transform.position = hit.point;
        else
            root.transform.position = new Vector3(at.x, 2f, at.z);

        // Face the player if there is one.
        if (player != null)
        {
            Vector3 look = player.transform.position - root.transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f)
                root.transform.rotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        }

        PrefabUtility.SaveAsPrefabAssetAndConnect(root, PrefabPath, InteractionMode.AutomatedAction);
        Selection.activeGameObject = root;
        Debug.Log($"Newton placed at {root.transform.position}, capsule h={capsule.height:0.00} " +
                  $"r={capsule.radius:0.00}. Prefab: {PrefabPath}");
    }
}
