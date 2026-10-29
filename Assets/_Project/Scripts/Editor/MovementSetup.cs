using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Movement;
using Pickleball.Sandbox.Movement.Presentation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wiring for the player's movement system (Sandbox/Movement): the config
/// assets, the components on the player, and the Sunshine-move states in the
/// chibi's Animator.
///
/// Tools ▸ Pickleball ▸ Setup Movement — idempotent. Re-imports the clips,
/// rebuilds the chibi controller in place (GUID kept, weapon layer re-added),
/// creates the config assets if missing (never overwriting tuned values, only
/// the measured stride and ride-height numbers), then adds and wires the
/// movement components on Chibi.prefab and on the open scene's player. It does
/// NOT rebuild the scene or the world; the full Setup Chibi Sandbox calls the
/// same pieces from its own build.
/// </summary>
public static class MovementSetup
{
    const string ConfigDir = "Assets/_Project/ScriptableObjects/Movement";
    const string ConfigPath = ConfigDir + "/MovementConfig.asset";
    const string AnimConfigPath = ConfigDir + "/MovementAnimationConfig.asset";
    const string PrefabPath = "Assets/_Project/Prefabs/Chibi.prefab";

    [MenuItem("Tools/Pickleball/Setup Movement")]
    public static void Run()
    {
        if (!ChibiSandboxSetup.ImportClipsAndRebuildAnimator()) return;
        EnsureConfigs(out _, out var anim);
        WriteMeasuredValues(anim, ChibiSandboxSetup.MeasuredRunStride,
                            ChibiSandboxSetup.MeasuredCrouchStride, null);

        if (File.Exists(PrefabPath))
        {
            // Edit the prefab itself, so the components are part of Chibi.prefab
            // rather than overrides on one scene's instance.
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                WirePlayer(root, null);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // The camera is a scene object, so the scene's instance gets it.
        var player = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (player != null)
        {
            var cam = Camera.main;
            WirePlayer(player.gameObject, cam != null ? cam.transform : null);
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("Movement set up: configs at " + ConfigDir + ", components on " +
                  (player != null ? player.name : "Chibi.prefab only (no player in the open scene)") +
                  ". Save the scene to keep the camera wiring.");
    }

    /// <summary>Loads the two config assets, creating them with code defaults if missing.</summary>
    public static void EnsureConfigs(out MovementConfig config, out MovementAnimationConfig anim)
    {
        ChibiSandboxSetup.EnsureFolderPublic(ConfigDir);
        config = AssetDatabase.LoadAssetAtPath<MovementConfig>(ConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<MovementConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
        }
        anim = AssetDatabase.LoadAssetAtPath<MovementAnimationConfig>(AnimConfigPath);
        if (anim == null)
        {
            anim = ScriptableObject.CreateInstance<MovementAnimationConfig>();
            AssetDatabase.CreateAsset(anim, AnimConfigPath);
        }
    }

    /// <summary>Writes the numbers the setup MEASURES (clip strides on the chibi,
    /// the board's deck height) into the animation config. Tuned values are left
    /// alone. <paramref name="rideHeight"/> null = not measured this run.</summary>
    public static void WriteMeasuredValues(MovementAnimationConfig anim, float runStride,
                                           float crouchStride, float? rideHeight)
    {
        var so = new SerializedObject(anim);
        so.FindProperty("runClipStride").floatValue = runStride;
        so.FindProperty("crouchClipStride").floatValue = crouchStride;
        if (rideHeight.HasValue) so.FindProperty("skateRideHeight").floatValue = rideHeight.Value;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(anim);
    }

    /// <summary>
    /// Adds the movement components to a player root and wires every reference.
    /// Idempotent. Existing effects are found by component and by the names the
    /// sandbox build gives them, so this also upgrades a player built before the
    /// movement extraction (whose old ChibiLocomotion fields no longer exist).
    /// </summary>
    public static void WirePlayer(GameObject player, Transform cameraTransform)
    {
        EnsureConfigs(out var config, out var anim);
        if (player.GetComponent<CharacterController>() == null)
        {
            Debug.LogError($"{player.name} has no CharacterController — not a sandbox player.");
            return;
        }

        var input = GetOrAdd<PlayerMovementInput>(player);
        var motor = GetOrAdd<MovementMotor>(player);
        GetOrAdd<ChibiLocomotion>(player);
        var driver = GetOrAdd<MovementAnimatorDriver>(player);
        var effects = GetOrAdd<MovementEffects>(player);

        var animator = player.GetComponentInChildren<Animator>(true);
        var tether = player.GetComponent<TetherSystem>();
        var dash = player.GetComponent<DashEffects>();
        var board = player.GetComponentInChildren<SkateboardMount>(true);
        var puff = player.transform.Find("DoubleJumpPuff");

        var mso = new SerializedObject(motor);
        mso.FindProperty("config").objectReferenceValue = config;
        mso.FindProperty("input").objectReferenceValue = input;
        mso.FindProperty("abilities").objectReferenceValue = player.GetComponent<PlayerAbilities>();
        mso.FindProperty("tether").objectReferenceValue = tether;
        mso.ApplyModifiedPropertiesWithoutUndo();

        if (cameraTransform != null)
        {
            var iso = new SerializedObject(input);
            iso.FindProperty("cameraTransform").objectReferenceValue = cameraTransform;
            iso.ApplyModifiedPropertiesWithoutUndo();
        }

        var dso = new SerializedObject(driver);
        dso.FindProperty("motor").objectReferenceValue = motor;
        dso.FindProperty("config").objectReferenceValue = anim;
        dso.FindProperty("animator").objectReferenceValue = animator;
        dso.FindProperty("modelRoot").objectReferenceValue = animator != null ? animator.transform : null;
        dso.ApplyModifiedPropertiesWithoutUndo();

        var eso = new SerializedObject(effects);
        eso.FindProperty("motor").objectReferenceValue = motor;
        eso.FindProperty("doubleJumpEffect").objectReferenceValue =
            puff != null ? puff.GetComponent<ParticleSystem>() : null;
        eso.FindProperty("dashEffects").objectReferenceValue = dash;
        eso.FindProperty("tether").objectReferenceValue = tether;
        eso.FindProperty("board").objectReferenceValue = board;
        eso.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(player);
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
        => go.TryGetComponent<T>(out var c) ? c : go.AddComponent<T>();

    // -------------------------------------------------------------- animator

    /// <summary>
    /// Adds the movement parameters and the Sunshine-move states to the chibi's
    /// base layer. Called by ChibiSandboxSetup.BuildController, after the legacy
    /// graph exists, so every rebuild includes them.
    ///
    /// The persistent moves (skid, dive, belly slide, spin) are entered from Any
    /// State on the int <c>Move</c> parameter and left only when it changes, so
    /// nothing can latch or loop. The jump variants use the <c>JumpVariant</c>
    /// trigger plus the int <c>JumpKind</c>: a jump's Move value persists through
    /// its whole arc, and a condition on it alone would re-enter the state every
    /// time the state hands over to Fall.
    ///
    /// Every state falls back to an existing clip while the dedicated Mixamo
    /// clips are missing, so the graph is complete today and improves as roles
    /// are added to the clip table.
    /// </summary>
    /// <returns>The states added, so the caller can switch their foot IK off.</returns>
    public static List<AnimatorState> AddMovementStates(AnimatorController controller,
        IReadOnlyDictionary<string, AnimationClip> clips, AnimatorState locomotion,
        AnimatorState fall, AnimatorState land, AnimatorState flipAir)
    {
        controller.AddParameter("Move", AnimatorControllerParameterType.Int);
        controller.AddParameter("JumpKind", AnimatorControllerParameterType.Int);
        controller.AddParameter("JumpVariant", AnimatorControllerParameterType.Trigger);

        var sm = controller.layers[0].stateMachine;
        var added = new List<AnimatorState>();

        AnimationClip Clip(params string[] roles)
        {
            foreach (var r in roles)
                if (clips.TryGetValue(r, out var c) && c != null) return c;
            return null;
        }

        AnimatorState State(string name, Motion motion, float speed = 1f, string speedParam = null)
        {
            var s = sm.AddState(name);
            s.motion = motion;
            s.speed = speed;
            if (speedParam != null)
            {
                s.speedParameterActive = true;
                s.speedParameter = speedParam;
            }
            added.Add(s);
            return s;
        }

        // ---- skid turn -------------------------------------------------------
        // Placeholder: the braking plant at the head of Run To Stop, played fast
        // enough to read inside the ~0.14 s redirect.
        var skidClip = Clip("skidTurn");
        var skid = State("SkidTurn", skidClip ?? Clip("stop", "idle"), skidClip != null ? 1f : 2f);
        OnMove(sm, skid, MoveKind.SkidTurn, 0.04f);
        Leave(skid, locomotion, MoveKind.SkidTurn, 0.1f, grounded: true);
        Leave(skid, fall, MoveKind.SkidTurn, 0.12f, grounded: false);

        // ---- jump chain variants -----------------------------------------------
        var dbl = State("JumpDouble", Clip("jumpDouble", "runJump", "jump"));
        OnJump(sm, dbl, JumpKind.Double);
        var t = Go(dbl, fall, 0.12f);
        t.AddCondition(AnimatorConditionMode.Less, -0.1f, "VerticalSpeed");
        t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        Go(dbl, land, 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");

        // The flips are the trimmed air front flip, time-scaled to the airtime
        // by the driver (FlipSpeed). The side somersault plays it BACKWARDS — a
        // back flip out of the plant — until a real side flip arrives.
        var tripleClip = Clip("tripleJump");
        var sideClip = Clip("sideFlip");
        var rollClip = Clip("rollout");
        var flipClip = Clip("flipAir", "jump");
        var triple = State("TripleJump", tripleClip ?? flipClip, 1f, "FlipSpeed");
        var side = State("SideFlip", sideClip ?? flipClip, sideClip != null ? 1f : -1f, "FlipSpeed");
        var roll = State("Rollout", rollClip ?? flipClip, 1f, "FlipSpeed");
        OnJump(sm, triple, JumpKind.Triple);
        OnJump(sm, side, JumpKind.SideFlip);
        OnJump(sm, roll, JumpKind.Rollout);
        foreach (var s in new[] { triple, side, roll })
        {
            Go(s, land, 0.08f).AddCondition(AnimatorConditionMode.If, 0, "Grounded");
            t = s.AddTransition(fall);
            t.hasExitTime = true; t.exitTime = 0.97f; t.duration = 0.12f;
            t.AddCondition(AnimatorConditionMode.IfNot, 0, "Grounded");
        }

        // ---- spin jump -----------------------------------------------------------
        // Placeholder: the arms-out fall loop; the driver corkscrews the model.
        var spin = State("SpinJump", Clip("spinJump", "fall", "jump"));
        OnMove(sm, spin, MoveKind.SpinJump, 0.06f);
        Leave(spin, land, MoveKind.SpinJump, 0.08f, grounded: true);
        Leave(spin, fall, MoveKind.SpinJump, 0.12f, grounded: false);

        // ---- dive + belly slide ---------------------------------------------------
        // Both from Run To Dive, which is already flat with the arms ahead, so the
        // driver's pitch is 0. Without that file they fall back to the airborne
        // stretch of Running Jump and the fall loop, which need divePitch ~60 and
        // bellySlidePitch ~82 to lie down.
        var dive = State("Dive", Clip("dive", "dashPose", "fall"));
        OnMove(sm, dive, MoveKind.Dive, 0.06f);
        Leave(dive, land, MoveKind.Dive, 0.08f, grounded: true);
        Leave(dive, fall, MoveKind.Dive, 0.12f, grounded: false);

        var slide = State("BellySlide", Clip("bellySlide", "fall", "dashPose"));
        OnMove(sm, slide, MoveKind.BellySlide, 0.08f);
        Leave(slide, locomotion, MoveKind.BellySlide, 0.2f, grounded: true);
        Leave(slide, fall, MoveKind.BellySlide, 0.15f, grounded: false);

        // The feather's air jump out of any of the new airborne states.
        if (flipAir != null)
            foreach (var s in new[] { dbl, triple, side, roll, spin })
                Go(s, flipAir, 0.06f).AddCondition(AnimatorConditionMode.If, 0, "Flip");

        Debug.Log($"Movement states: {string.Join(", ", added.Select(s => s.name))}");
        return added;
    }

    static AnimatorStateTransition Go(AnimatorState from, AnimatorState to, float duration)
    {
        var tr = from.AddTransition(to);
        tr.hasExitTime = false;
        tr.duration = duration;
        tr.interruptionSource = TransitionInterruptionSource.Source;
        tr.orderedInterruption = true;
        return tr;
    }

    /// <summary>Any State → <paramref name="to"/> while Move == <paramref name="kind"/>.</summary>
    static void OnMove(AnimatorStateMachine sm, AnimatorState to, MoveKind kind, float duration)
    {
        var tr = sm.AddAnyStateTransition(to);
        tr.hasExitTime = false;
        tr.duration = duration;
        tr.canTransitionToSelf = false;
        tr.AddCondition(AnimatorConditionMode.Equals, (int)kind, "Move");
    }

    /// <summary>Any State → <paramref name="to"/> on JumpVariant with JumpKind == <paramref name="kind"/>.</summary>
    static void OnJump(AnimatorStateMachine sm, AnimatorState to, JumpKind kind)
    {
        var tr = sm.AddAnyStateTransition(to);
        tr.hasExitTime = false;
        tr.duration = 0.05f;
        tr.canTransitionToSelf = false;
        tr.AddCondition(AnimatorConditionMode.If, 0, "JumpVariant");
        tr.AddCondition(AnimatorConditionMode.Equals, (int)kind, "JumpKind");
    }

    /// <summary><paramref name="from"/> → <paramref name="to"/> once Move is no longer
    /// <paramref name="kind"/>, split on Grounded.</summary>
    static void Leave(AnimatorState from, AnimatorState to, MoveKind kind, float duration, bool grounded)
    {
        var tr = Go(from, to, duration);
        tr.AddCondition(AnimatorConditionMode.NotEqual, (int)kind, "Move");
        tr.AddCondition(grounded ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "Grounded");
    }
}
