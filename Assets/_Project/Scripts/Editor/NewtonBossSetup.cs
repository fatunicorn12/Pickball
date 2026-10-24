using System.Collections.Generic;
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

    /// <summary>Where the shared clips live. Both characters are Humanoid, so
    /// any of these retargets onto Newton through his avatar.</summary>
    const string SharedClipDir = "Assets/_Project/Animation/Chibi";
    const string SharedPackDir = SharedClipDir + "/SwordShield";

    /// <summary>
    /// Clip roles. Each one is loaded from Animation/Newton/&lt;file&gt;; when
    /// that file does not exist yet it is COPIED there from the shared library
    /// first, so Newton gets real animation today and a bespoke clip later just
    /// replaces the file. Copies rather than references because this setup
    /// rewrites the importer settings (Humanoid, root baking, frame range), and
    /// doing that to the chibi's own files would silently change the player.
    ///
    /// Frame ranges are on the 30 fps Mixamo takes; -1/-1 is the whole take.
    ///   summon : Throw.fbx 40-95 — the short wind-up, the overhand fling and
    ///            the follow-through. The well appears the moment he throws, so
    ///            the long idle lead-in (0-40) is cut.
    ///   volley : a one-hand cast, timed by the state speed below so the cast
    ///            peaks as the ring is released.
    ///   beam   : the second cast, cut at frame 22 — the moment of fullest
    ///            extension — so the state HOLDS the casting pose for as long
    ///            as the beam is live instead of finishing and standing idle.
    ///   barrage: "power up" — arms flung wide, which is the sky opening.
    /// </summary>
    static readonly (string role, string file, string source, bool loop, int first, int last)[] ClipDefs =
    {
        ("idle",    "Idle.fbx",    SharedClipDir + "/Idle.fbx",    true,  -1, -1),
        ("walk",    "Walking.fbx", SharedClipDir + "/Walking.fbx", true,  -1, -1),
        ("summon",  "Throw.fbx",   SharedClipDir + "/Throw.fbx",   false, 40, 95),
        ("volley",  "Cast.fbx",    SharedPackDir + "/sword and shield casting.fbx",     false, -1, -1),
        ("beam",    "Cast2.fbx",   SharedPackDir + "/sword and shield casting (2).fbx", false, -1, 22),
        ("barrage", "PowerUp.fbx", SharedPackDir + "/sword and shield power up.fbx",    false, -1, -1),
        ("hit",     "Hit.fbx",     SharedPackDir + "/sword and shield impact.fbx",      false, -1, -1),
        ("death",   "Death.fbx",   SharedPackDir + "/sword and shield death.fbx",       false, -1, -1),
    };

    /// <summary>Mirrors InverseSquareVolley.windUp: the ring leaves this long
    /// after the Volley trigger, so the cast is time-scaled to peak then.</summary>
    const float VolleyReleaseSeconds = 0.55f;

    /// <summary>Mirrors FluxionBeam.chargeTime: the beam cast is time-scaled to
    /// reach full extension as the charge completes and the beam fires.</summary>
    const float BeamChargeSeconds = 1.1f;

    /// <summary>Walk threshold used if the stride cannot be measured.</summary>
    const float FallbackWalkStride = 1.0f;

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

        var avatar = LoadAvatar();
        if (avatar == null) return;
        Debug.Log($"Newton avatar OK — human={avatar.isHuman}, scale {BossHeight}.");

        var controller = BuildController(LoadClips(), avatar);
        Place(avatar, controller);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Rebuilds only Newton's clips and controller, in place. Unlike
    /// Setup Newton (Boss) this does NOT rebuild the NewtonBoss object, so the
    /// fight wiring that Setup Newton Fight attached survives — use this after
    /// changing clips or the state machine.
    /// </summary>
    [MenuItem("Tools/Pickleball/Rebuild Newton Animator")]
    public static void RebuildAnimator()
    {
        ChibiSandboxSetup.EnsureFolderPublic(ClipDir);
        var avatar = LoadAvatar();
        if (avatar == null) return;
        BuildController(LoadClips(), avatar);
        AssetDatabase.SaveAssets();
    }

    static Avatar LoadAvatar()
    {
        var avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"Newton avatar invalid (valid={avatar?.isValid}, " +
                           $"human={avatar?.isHuman}). Check FBX ▸ Rig ▸ Configure.");
            return null;
        }
        return avatar;
    }

    static Dictionary<string, AnimationClip> LoadClips()
    {
        var result = new Dictionary<string, AnimationClip>();
        foreach (var (role, file, source, loop, first, last) in ClipDefs)
        {
            string path = $"{ClipDir}/{file}";
            if (AssetImporter.GetAtPath(path) == null && !string.IsNullOrEmpty(source) &&
                AssetImporter.GetAtPath(source) != null)
            {
                if (AssetDatabase.CopyAsset(source, path))
                    Debug.Log($"  {role,-8} copied {Path.GetFileName(source)} -> {path}");
            }

            var imp = AssetImporter.GetAtPath(path) as ModelImporter;
            if (imp == null) continue;

            imp.animationType = ModelImporterAnimationType.Human;
            imp.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            imp.importAnimation = true;
            imp.materialImportMode = ModelImporterMaterialImportMode.None;

            var defs = imp.defaultClipAnimations;
            if (defs is { Length: > 0 })
            {
                var take = defs[0];
                // He never leaves the spot he is placed on — NewtonBoss only
                // turns him — so travel is code-owned and root motion is off.
                //   Loops (idle, walk): XZ and rotation NOT baked, so the
                //     cycle plays in place and cannot drift or spin him.
                //   One-shots (casts, hit, death): everything baked, so a
                //     stagger, a lunge or a fall shows as authored instead of
                //     being projected out and thrown away.
                //   Height always baked, feet-based: with Y left as root
                //     motion and root motion off, every crouch, flinch and
                //     the death fall would be flattened to standing height
                //     — dying in mid-air.
                take.name = role;
                take.loopTime = loop;
                take.loopPose = loop;
                if (first >= 0) take.firstFrame = first;
                if (last >= 0) take.lastFrame = last;
                take.lockRootRotation = !loop;
                take.lockRootPositionXZ = !loop;
                take.lockRootHeightY = true;
                take.keepOriginalOrientation = true;
                take.keepOriginalPositionXZ = true;
                take.keepOriginalPositionY = false;
                take.heightFromFeet = true;
                imp.clipAnimations = new[] { take };
            }
            imp.SaveAndReimport();

            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) continue;
            result[role] = clip;
            Debug.Log($"  {role,-8} <- {file,-16} {clip.length:0.00}s loop={loop}");
        }
        if (result.Count == 0)
            Debug.LogWarning($"No clips in {ClipDir} and no shared sources found. " +
                             "Newton will hold his bind pose.");
        return result;
    }

    /// <summary>
    /// Newton's state machine.
    ///
    ///   Locomotion  1D blend idle/walk on Speed. He never travels, but turning
    ///               to track the player is driven into Speed as a shuffle, so
    ///               he steps round instead of skating on the spot.
    ///   Summon / Volley / Beam / Barrage
    ///               one cast per ability, entered from ANY state on its own
    ///               trigger (a cast is a tell, and a tell that waits for a
    ///               flinch to finish arrives too late to read). Beam is held
    ///               on the Channeling bool for as long as the beam is live.
    ///   Hit         a flinch, from Locomotion only — getting hit never cancels
    ///               a tell; mid-cast the procedural recoil covers it instead.
    ///   Death       any state, on the Dead bool, and nothing leaves it.
    ///
    /// Parameter names are the contract with NewtonBoss.
    /// </summary>
    static AnimatorController BuildController(Dictionary<string, AnimationClip> clips, Avatar avatar)
    {
        var controller = ChibiSandboxSetup.ResetControllerInPlace(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Dead", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Channeling", AnimatorControllerParameterType.Bool);
        foreach (var trigger in new[] { "Summon", "Volley", "Beam", "Barrage", "Hit" })
            controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;

        // ---- locomotion ------------------------------------------------------
        AnimatorState loco;
        clips.TryGetValue("idle", out var idleClip);
        if (idleClip != null && clips.TryGetValue("walk", out var walkClip))
        {
            float stride = MeasureStride(avatar, walkClip, out _);
            loco = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Speed";
            tree.useAutomaticThresholds = false;
            tree.AddChild(idleClip, 0f);
            tree.AddChild(walkClip, stride);
            Debug.Log($"  Newton walk threshold {stride:0.00} m/s (measured stride on his rig)");
        }
        else
        {
            loco = sm.AddState("Locomotion");
            loco.motion = idleClip;
        }
        sm.defaultState = loco;

        // ---- casts -----------------------------------------------------------
        AnimatorState AddCast(string role, string trigger, float speed, bool yieldsToBeam)
        {
            if (!clips.TryGetValue(role, out var clip)) return null;
            var st = sm.AddState(trigger);
            st.motion = clip;
            st.speed = speed;
            var t = sm.AddAnyStateTransition(st);
            t.hasExitTime = false; t.duration = 0.12f;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, trigger);
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Dead");
            // The beam is the one ability he channels for seconds; a well or a
            // volley dropped mid-beam must not knock him out of the casting
            // pose (play-tested: a Summon cut the beam pose off and the beam
            // then fired from an idling Newton). Their trigger simply expires.
            if (yieldsToBeam) t.AddCondition(AnimatorConditionMode.IfNot, 0, "Channeling");
            return st;
        }

        float volleySpeed = 1f;
        if (clips.TryGetValue("volley", out var volleyClip))
        {
            MeasureStride(avatar, volleyClip, out float peak);
            if (peak > 0.05f) volleySpeed = Mathf.Clamp(peak / VolleyReleaseSeconds, 0.75f, 2.5f);
        }
        float beamSpeed = clips.TryGetValue("beam", out var beamClip)
            ? Mathf.Clamp(beamClip.length / BeamChargeSeconds, 0.4f, 1.5f) : 1f;

        var summon = AddCast("summon", "Summon", 1f, true);
        var volley = AddCast("volley", "Volley", volleySpeed, true);
        var beam = AddCast("beam", "Beam", beamSpeed, false);
        var barrage = AddCast("barrage", "Barrage", 1f, true);

        foreach (var st in new[] { summon, volley, barrage })
        {
            if (st == null) continue;
            var back = st.AddTransition(loco);
            back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.25f;
        }
        if (beam != null)
        {
            // Held while the beam is live (the clip holds its last frame if it
            // runs out first), released the moment it stops.
            var back = beam.AddTransition(loco);
            back.hasExitTime = false; back.duration = 0.3f;
            back.AddCondition(AnimatorConditionMode.IfNot, 0, "Channeling");

            // Back into the pose whenever he returns to Locomotion with the
            // beam still live (e.g. after a flinch, or a cast that started
            // the same frame the beam did) — no trigger needed.
            var resume = loco.AddTransition(beam);
            resume.hasExitTime = false; resume.duration = 0.2f;
            resume.AddCondition(AnimatorConditionMode.If, 0, "Channeling");
        }

        // ---- hit -------------------------------------------------------------
        if (clips.TryGetValue("hit", out var hitClip))
        {
            var hit = sm.AddState("Hit");
            hit.motion = hitClip;
            var t = loco.AddTransition(hit);
            t.hasExitTime = false; t.duration = 0.06f;
            t.AddCondition(AnimatorConditionMode.If, 0, "Hit");
            var back = hit.AddTransition(loco);
            back.hasExitTime = true; back.exitTime = 0.75f; back.duration = 0.25f;
        }

        // ---- death -----------------------------------------------------------
        if (clips.TryGetValue("death", out var deathClip))
        {
            var dead = sm.AddState("Death");
            dead.motion = deathClip;
            var t = sm.AddAnyStateTransition(dead);
            t.hasExitTime = false; t.duration = 0.2f;
            t.canTransitionToSelf = false;
            t.AddCondition(AnimatorConditionMode.If, 0, "Dead");
            // AnyState transitions are tested in list order: death goes first,
            // so a cast trigger landing on the frame he dies cannot win.
            var any = sm.anyStateTransitions;
            sm.anyStateTransitions = new[] { any[^1] }.Concat(any.Take(any.Length - 1)).ToArray();
        }

        // Per-state Foot IK replays the clip's own foot goals, which is what
        // keeps retargeted feet planted on a standing stance. Off for death:
        // pinning the feet of a falling body drags it.
        foreach (var s in sm.states)
            s.state.iKOnFeet = s.state.name != "Death";

        EditorUtility.SetDirty(controller);
        Debug.Log($"Newton controller: {ControllerPath} ({sm.states.Length} states: " +
                  $"{string.Join(", ", sm.states.Select(s => s.state.name))}; " +
                  $"volley x{volleySpeed:0.00}, beam x{beamSpeed:0.00})");
        return controller;
    }

    /// <summary>Stride speed of <paramref name="clip"/> on Newton's own rig,
    /// measured off a throwaway instance of his model — see
    /// ChibiSandboxSetup.MeasureStrideSpeed. Also returns when the hands are
    /// furthest from the hips, which is how a cast's peak is found.</summary>
    static float MeasureStride(Avatar avatar, AnimationClip clip, out float handPeak)
    {
        handPeak = 0f;
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null) return FallbackWalkStride;
        var go = Object.Instantiate(modelAsset);
        go.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            var anim = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            anim.avatar = avatar;
            anim.Rebind();
            float v = ChibiSandboxSetup.MeasureStrideSpeed(anim, clip, out handPeak);
            return v > 0.05f ? v : FallbackWalkStride;
        }
        finally { Object.DestroyImmediate(go); }
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
        // A boss's tells must never pause because his bounds left the frustum
        // for a moment (the camera orbits close to him mid-combo).
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // Spring-cloth chains on the coat; needs the avatar assigned above,
        // and must land before the prefab is saved below.
        ClothSetup.Apply(model);

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
