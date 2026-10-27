using System.Collections.Generic;
using System.Linq;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Attaches the drafting-compass weapon to the player and wires the combo chain.
///
/// Tools ▸ Pickleball ▸ Setup Weapon
///
/// The weapon hangs off HumanBodyBones.RightHand rather than a bone found by
/// name: the avatar already knows which transform that is, so this works for any
/// Tripo rig without hard-coding "R_Hand".
///
/// Runs with a primitive stand-in until the real compass is imported, so the
/// combo timing and hit sweep can be tuned before the model exists — the same
/// approach that let the skateboard mechanic be built ahead of its model.
/// </summary>
public static class WeaponSetup
{
    const string PropsDir = "Assets/_Project/Art/Props";
    const string ClipDir = "Assets/_Project/Animation/Chibi";
    const string VfxDir = "Assets/_Project/Art/VFX";
    const string ControllerPath = "Assets/_Project/Animation/Controllers/ChibiLocomotion.controller";

    const string PackDir = ClipDir + "/SwordShield";
    const string PackSource = "Assets/Sword and Shield Pack";

    /// <summary>
    /// The combo chain, chosen by measuring the pack rather than by filename.
    ///
    /// Ordered so the swing direction ALTERNATES every hit — L>R, R>L, L>R, R>L.
    /// Two consecutive swings the same way read as a stutter rather than a
    /// combo, which is the most common way a chain looks wrong.
    ///
    /// It escalates: quick opener, widest sweep, heavy downward, then the
    /// upward slash as the finisher. Launching LAST is deliberate — the combo
    /// ends by popping the target up, which is the setup for juggling it with
    /// the air attack.
    ///
    /// Windows are fractions of each clip. These clips carry long wind-ups, so
    /// the blade goes live around 35% and the chain opens at 50%.
    ///
    /// `speed` is the Animator state's playback rate. Mixamo authors these
    /// swings at 1.37-1.73s, which is far too ponderous for an arcade combo, so
    /// each is played back at roughly 2.2-2.5x. Measured peak hand speed lands
    /// at 40-53% of every clip, comfortably inside the active windows below, and
    /// the windows are fractions — so speeding the states up rescales the whole
    /// chain without moving the strike out of its hit window.
    /// </summary>
    static readonly (string role, string file, string trigger, float speed,
                     float activeStart, float activeEnd,
                     float comboOpen, float comboClose)[] AttackClips =
    {
        // 1.37s -> 0.55s. L>R, flat, wide — fastest of the set, so it opens.
        ("attack1", "sword and shield slash (5).fbx", "Attack1", 2.5f, 0.32f, 0.60f, 0.46f, 0.90f),
        // 1.73s -> 0.69s. R>L, widest lateral sweep (0.603) and longest reach.
        ("attack2", "sword and shield attack (3).fbx", "Attack2", 2.5f, 0.35f, 0.62f, 0.50f, 0.92f),
        // 1.50s -> 0.63s. L>R, downward, big vertical — the heavy third hit.
        ("attack3", "sword and shield slash.fbx", "Attack3", 2.4f, 0.34f, 0.62f, 0.50f, 0.92f),
        // 1.57s -> 0.71s. R>L, UPWARD — the launcher that ends the ground chain.
        // Kept the slowest of the four so the finisher still lands with weight.
        ("attack4", "sword and shield slash (3).fbx", "Attack4", 2.2f, 0.34f, 0.64f, 0.55f, 0.95f),
        // 1.00s -> 0.45s. In place, compact — suits being airborne.
        ("airAttack", "sword and shield attack (4).fbx", "AirAttack", 2.2f, 0.25f, 0.60f, 0.5f, 0.9f),
    };

    /// <summary>
    /// Which end of the weapon sits in the fist. The compass model runs 1.0
    /// units along its own +Y with the origin at the low end, so the old code —
    /// which just parented it at the socket origin — always gripped that low
    /// end. True flips it, so the far end is held and the model points the other
    /// way out of the hand.
    /// </summary>
    const bool GripFarEnd = true;

    /// <summary>Weapon length after normalising, whatever the authored scale.</summary>
    const float WeaponLength = 0.85f;

    /// <summary>
    /// Name of the masked layer the combo lives on. MeleeCombat looks the layer
    /// up by this name at runtime, so the two must agree.
    ///
    /// The attacks used to be full-body states on the base layer, which meant a
    /// swing replaced the run — the legs froze mid-stride while the character
    /// kept travelling. Masked to the arms, spine and head instead, the swing
    /// composes over whatever the legs are already doing.
    /// </summary>
    public const string AttackLayer = "Attack";

    [MenuItem("Tools/Pickleball/Setup Weapon")]
    public static void Run()
    {
        var clips = PrepareAssets();

        var player = GameObject.Find("Chibi");
        if (player == null)
        {
            Debug.LogError("No 'Chibi' in the scene — run Setup Chibi Sandbox first.");
            return;
        }
        if (!AttachToPlayer(player, clips)) return;

        AssetDatabase.SaveAssets();
        Debug.Log("Weapon setup complete. Left click to attack; click again mid-swing to chain.");
    }

    /// <summary>
    /// Phase 1, assets only: file the pack, import the attack clips, and add the
    /// masked Attack layer to the controller. Touches no scene objects.
    ///
    /// Split from the scene work so ChibiSandboxSetup can run it BEFORE building
    /// the scene. ImportAttackClips calls SaveAndReimport on five FBXs, and
    /// kicking off asset imports halfway through scene construction is asking
    /// for a half-built scene. Getting the Attack layer onto the controller
    /// first also means the Animator is wired to a finished controller rather
    /// than one that grows a layer afterwards.
    /// </summary>
    public static Dictionary<string, AnimationClip> PrepareAssets()
    {
        OrganizeWeaponAssets();
        var clips = ImportAttackClips();
        WireController(clips);
        return clips;
    }

    /// <summary>
    /// Phase 2, scene only: hang the weapon off the right hand and wire up
    /// MeleeCombat. Returns false if the player is not in a fit state for it.
    /// </summary>
    public static bool AttachToPlayer(GameObject player,
                                      Dictionary<string, AnimationClip> clips)
    {
        var animator = player.GetComponentInChildren<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            Debug.LogError("Player has no Humanoid avatar; cannot find the hand bone.");
            return false;
        }

        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null)
        {
            Debug.LogError("Avatar has no RightHand bone mapped.");
            return false;
        }

        var weapon = AttachWeapon(hand, out Transform bladeBase, out Transform bladeTip);
        WireCombat(player, animator, weapon, bladeBase, bladeTip, clips);
        // The Attack layer's mask drops the swings' hip turn; put it back.
        SwingTwistSetup.Bake(player);
        // The socket was just rebuilt, so its clearance pass has to be re-added.
        WeaponClearanceSetup.Attach(player);
        EditorUtility.SetDirty(player);
        return true;
    }

    // --------------------------------------------------------------- organize

    /// <summary>
    /// Files the whole Mixamo pack under Animation and the compass under Props.
    ///
    /// The pack moves as one folder and keeps Mixamo's filenames: only 5 of its
    /// 49 clips are wired right now, and renaming would make the other 44 harder
    /// to find later. ChibiSandboxSetup's organizer deliberately does not touch
    /// it — its loose-model sweep would file these animations as props.
    /// </summary>
    static void OrganizeWeaponAssets()
    {
        if (AssetDatabase.IsValidFolder(PackSource))
        {
            ChibiSandboxSetup.EnsureFolderPublic(ClipDir);
            string err = AssetDatabase.MoveAsset(PackSource, PackDir);
            if (string.IsNullOrEmpty(err)) Debug.Log($"moved {PackSource} -> {PackDir}");
            else Debug.LogWarning($"could not move pack: {err}");
        }

        // Compass: same treatment as any other Tripo prop, sidecar included.
        const string compassSrc = "Assets/compass-weapon";
        string compassDst = $"{PropsDir}/compass-weapon";
        if (AssetDatabase.IsValidFolder(compassSrc) &&
            !AssetDatabase.IsValidFolder(compassDst))
        {
            ChibiSandboxSetup.EnsureFolderPublic(PropsDir);
            string err = AssetDatabase.MoveAsset(compassSrc, compassDst);
            if (string.IsNullOrEmpty(err)) Debug.Log($"moved {compassSrc} -> {compassDst}");
            else Debug.LogWarning($"could not move compass: {err}");
        }
        AssetDatabase.Refresh();
    }

    // ------------------------------------------------------------------ clips

    static Dictionary<string, AnimationClip> ImportAttackClips()
    {
        var result = new Dictionary<string, AnimationClip>();
        foreach (var (role, file, _, _, _, _, _, _) in AttackClips)
        {
            string path = $"{PackDir}/{file}";
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
                    d.loopTime = false;                 // attacks are one-shots
                    // Bake the body's turn into the pose, from its original
                    // orientation. These swings are authored side-on, hips
                    // turned up to ~110°; left as root motion (and root motion
                    // is off) that turn was simply discarded. SwingTwist bakes
                    // it from the clip, so it has to be in the pose to be seen.
                    d.lockRootRotation = true;
                    d.keepOriginalOrientation = true;
                    d.lockRootHeightY = false;
                    d.lockRootPositionXZ = false;       // lunge is scripted
                    d.keepOriginalPositionY = false;
                }
                imp.clipAnimations = defs;
            }
            imp.SaveAndReimport();

            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip == null) continue;
            result[role] = clip;
            Debug.Log($"  {role,-10} <- {file,-14} {clip.length:0.00}s");
        }
        if (result.Count == 0)
            Debug.LogWarning($"No attack clips found in {PackDir}. Expected the " +
                             "Mixamo sword-and-shield pack. The weapon and combo " +
                             "system are still wired; add clips and re-run.");
        return result;
    }

    // ----------------------------------------------------------------- weapon

    static GameObject AttachWeapon(Transform hand, out Transform bladeBase, out Transform bladeTip)
    {
        // Socket in between, so grip offsets can be tuned without touching the
        // bone or the model's own transform.
        var socketGo = hand.Find("WeaponSocket")?.gameObject;
        if (socketGo != null) Object.DestroyImmediate(socketGo);
        socketGo = new GameObject("WeaponSocket");
        socketGo.transform.SetParent(hand, false);

        string modelPath = FindModel("compass") ?? FindModel("protractor");
        GameObject visual;
        float length = WeaponLength;

        if (modelPath != null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            visual.transform.SetParent(socketGo.transform, false);
            AlignWeaponToSocket(visual, WeaponLength, GripFarEnd);
            Debug.Log($"Weapon model: {modelPath} (scaled to {WeaponLength:0.00}m, " +
                      $"gripped by its {(GripFarEnd ? "far" : "near")} end)");
        }
        else
        {
            visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.transform.SetParent(socketGo.transform, false);
            visual.transform.localScale = new Vector3(0.04f, 0.42f, 0.04f);
            Debug.Log("No compass model found — using a primitive stand-in. " +
                      $"Drop one under {PropsDir} and re-run.");
        }
        visual.name = "WeaponVisual";
        foreach (var c in visual.GetComponentsInChildren<Collider>())
            Object.DestroyImmediate(c);

        // First-guess grip: held forward out of the fist, angled slightly down.
        // Expect one tuning pass — the model's origin is not its handle.
        socketGo.transform.localPosition = new Vector3(0f, 0.02f, 0.04f);
        socketGo.transform.localRotation = Quaternion.Euler(0f, 90f, 100f);

        var baseGo = new GameObject("BladeBase");
        baseGo.transform.SetParent(socketGo.transform, false);
        baseGo.transform.localPosition = Vector3.zero;

        var tipGo = new GameObject("BladeTip");
        tipGo.transform.SetParent(socketGo.transform, false);
        tipGo.transform.localPosition = new Vector3(0f, length, 0f);

        bladeBase = baseGo.transform;
        bladeTip = tipGo.transform;
        return socketGo;
    }

    /// <summary>
    /// Scale a weapon model to <paramref name="wanted"/> metres, lay its long
    /// axis along the socket's +Y, and seat the chosen end at the socket origin
    /// — the point the fist closes on.
    ///
    /// The old code assumed the model's own origin WAS the handle and just
    /// parented it at zero. That happens to be half-right for the compass
    /// (measured: it runs 0.998 along its +Y with the origin exactly at the low
    /// end), but it gave no way to hold the other end, and it would be wrong for
    /// any model whose origin sits at its centre. Measuring instead means
    /// flipping the grip is one bool, and a different weapon just works.
    /// </summary>
    static void AlignWeaponToSocket(GameObject visual, float wanted, bool gripFarEnd)
    {
        var tr = visual.transform;
        var parent = tr.parent;

        // Measure in isolation. Renderer.bounds is a WORLD AABB, so at the world
        // origin with no rotation it is exactly the model's own local AABB —
        // measuring it parented to the hand would fold the bone's rotation in.
        tr.SetParent(null, false);
        tr.localPosition = Vector3.zero;
        tr.localRotation = Quaternion.identity;
        tr.localScale = Vector3.one;
        var local = MeasureBounds(visual);

        // Longest dimension is the weapon's length; the other two are its girth.
        int axis = 0;
        if (local.size.y > local.size[axis]) axis = 1;
        if (local.size.z > local.size[axis]) axis = 2;
        float span = local.size[axis];

        float scale = span > 0.0001f ? wanted / span : 1f;

        // The end that goes in the hand, and the direction from it to the tip.
        var gripPoint = local.center;
        gripPoint[axis] = gripFarEnd ? local.max[axis] : local.min[axis];
        var alongModel = Vector3.zero;
        alongModel[axis] = gripFarEnd ? -1f : 1f;

        tr.SetParent(parent, false);
        tr.localScale = Vector3.one * scale;

        // Point grip->tip up the socket's +Y, then slide the grip onto the
        // origin. Flipping the grip makes alongModel exactly antiparallel to up,
        // where FromToRotation is free to pick any perpendicular spin axis; pin
        // it to X so the roll is reproducible and stays where it is tuned.
        tr.localRotation = Vector3.Dot(alongModel, Vector3.up) < -0.999f
            ? Quaternion.AngleAxis(180f, Vector3.right)
            : Quaternion.FromToRotation(alongModel, Vector3.up);
        tr.localPosition = -(tr.localRotation * (gripPoint * scale));
    }

    // ------------------------------------------------------------- controller

    /// <summary>
    /// Rebuilds the Attack layer from the attack clips exactly as they are
    /// imported now, WITHOUT the SaveAndReimport that PrepareAssets does — so
    /// it cannot overwrite import settings tuned on those FBXs since. Used by
    /// ChibiSandboxSetup.RebuildAnimator.
    /// </summary>
    public static void RewireControllerFromImportedClips()
    {
        var clips = new Dictionary<string, AnimationClip>();
        foreach (var (role, file, _, _, _, _, _, _) in AttackClips)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath($"{PackDir}/{file}").OfType<AnimationClip>()
                .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            if (clip != null) clips[role] = clip;
        }
        WireController(clips);
    }

    static void WireController(Dictionary<string, AnimationClip> clips)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"Controller not found: {ControllerPath}");
            return;
        }

        var existing = new HashSet<string>(controller.parameters.Select(p => p.name));
        foreach (var (_, _, trigger, _, _, _, _, _) in AttackClips)
            if (!existing.Contains(trigger))
                controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
        if (!existing.Contains("Combo"))
            controller.AddParameter("Combo", AnimatorControllerParameterType.Int);

        var baseSm = controller.layers[0].stateMachine;
        if (baseSm.states.All(s => s.state.name != "Locomotion"))
        {
            Debug.LogError("No 'Locomotion' state — run Setup Chibi Sandbox first.");
            return;
        }

        // Older versions put the attacks on the base layer as full-body states.
        // Clear those out, or a rebuilt controller would keep stopping the legs.
        foreach (var s in baseSm.states.Where(s => s.state.name.StartsWith("Attack")).ToList())
            baseSm.RemoveState(s.state);

        // Re-running rebuilds the layer from scratch rather than appending.
        // RemoveLayer rather than reassigning controller.layers: it also destroys
        // the layer's state machine, which is a sub-asset of the .controller and
        // would otherwise be orphaned inside the file on every re-run.
        for (int i = controller.layers.Length - 1; i >= 0; i--)
            if (controller.layers[i].name == AttackLayer)
                controller.RemoveLayer(i);

        var sm = new AnimatorStateMachine
        {
            name = AttackLayer,
            hideFlags = HideFlags.HideInHierarchy,
        };
        AssetDatabase.AddObjectToAsset(sm, controller);

        // Empty default state: the layer contributes nothing until a swing
        // starts, and MeleeCombat fades its weight in for the duration.
        var idle = sm.AddState("None");
        sm.defaultState = idle;

        // One state per swing, named after its trigger so MeleeCombat can
        // crossfade to it by name. There are deliberately NO transitions on
        // this layer:
        //  - MeleeCombat's timer already owns every window (when the blade is
        //    live, when the chain may extend), so it crossfades straight to the
        //    next state itself. Trigger transitions duplicated that timing, and
        //    a click that landed while the layer was mid-blend left its trigger
        //    latched to fire a swing later on.
        //  - No exit back to None. An Override layer blending toward an empty
        //    state at full weight drags the arms toward the bind pose; the
        //    swing instead holds its last frame while MeleeCombat fades the
        //    layer weight to zero underneath it.
        int chain = 0;
        for (int i = 0; i < 4; i++)
        {
            string role = $"attack{i + 1}";
            if (!clips.TryGetValue(role, out var clip)) break;
            var st = sm.AddState(AttackClips[i].trigger);
            st.motion = clip;
            // MeleeCombat's windows are fractions of duration, and duration is
            // written as clip length / this speed, so the two stay in step.
            st.speed = AttackClips[i].speed;
            chain++;
        }

        if (clips.TryGetValue("airAttack", out var airClip))
        {
            var air = sm.AddState(AttackClips[^1].trigger);   // "AirAttack"
            air.motion = airClip;
            air.speed = AttackClips[^1].speed;
        }

        // Upper-body layer: Foot IK has nothing to do here.
        foreach (var s in sm.states) s.state.iKOnFeet = false;

        controller.AddLayer(new AnimatorControllerLayer
        {
            name = AttackLayer,
            stateMachine = sm,
            avatarMask = ChibiSandboxSetup.EnsureUpperBodyMask(),
            blendingMode = AnimatorLayerBlendingMode.Override,
            defaultWeight = 0f,       // faded in by MeleeCombat
        });

        EditorUtility.SetDirty(controller);
        Debug.Log($"Combo chain wired on masked '{AttackLayer}' layer: " +
                  $"{chain} ground attack(s)" +
                  (clips.ContainsKey("airAttack") ? " + air attack" : "") +
                  " — legs stay under locomotion, so you can swing while moving.");
    }

    // ------------------------------------------------------------------ combat

    static void WireCombat(GameObject player, Animator animator, GameObject weapon,
                           Transform bladeBase, Transform bladeTip,
                           Dictionary<string, AnimationClip> clips)
    {
        var combat = player.GetComponent<MeleeCombat>() ?? player.AddComponent<MeleeCombat>();

        // Every previous run created one of these at the SCENE ROOT, unparented
        // and never cleaned up, so re-running the setup piled up a new BladeTrail
        // each time. Sweep them all, then build one parented to the player.
        foreach (var stale in Object.FindObjectsByType<BladeTrail>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(stale.gameObject);

        var trailGo = new GameObject("BladeTrail");
        trailGo.transform.SetParent(player.transform, false);
        trailGo.AddComponent<MeshFilter>();
        var mr = trailGo.AddComponent<MeshRenderer>();
        mr.sharedMaterial = SandboxVfxBuilder.BuildGlowMaterial(
            VfxDir, "BladeTrail", new Color(0.8f, 0.95f, 1f, 0.9f));
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var trail = trailGo.AddComponent<BladeTrail>();
        var tso = new SerializedObject(trail);
        tso.FindProperty("bladeBase").objectReferenceValue = bladeBase;
        tso.FindProperty("bladeTip").objectReferenceValue = bladeTip;
        tso.ApplyModifiedPropertiesWithoutUndo();

        var so = new SerializedObject(combat);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.FindProperty("locomotion").objectReferenceValue = player.GetComponent<ChibiLocomotion>();
        so.FindProperty("bladeBase").objectReferenceValue = bladeBase;
        so.FindProperty("bladeTip").objectReferenceValue = bladeTip;
        so.FindProperty("trail").objectReferenceValue = trail;
        if (Camera.main != null)
            so.FindProperty("cameraRig").objectReferenceValue =
                Camera.main.GetComponent<SandboxOrbitCamera>();

        // Write the measured clip length AND the per-attack windows, so timing
        // is derived from the real clips rather than left at generic defaults.
        // Length is divided by the state's playback speed: MeleeCombat's timer
        // runs in real seconds, so it has to track the sped-up swing, not the
        // authored one, or the blade would go live after the animation had
        // already finished the strike.
        var chainProp = so.FindProperty("chain");
        for (int i = 0; i < chainProp.arraySize && i < 4; i++)
        {
            var def = AttackClips[i];
            if (!clips.TryGetValue(def.role, out var clip)) continue;
            var el = chainProp.GetArrayElementAtIndex(i);
            el.FindPropertyRelative("trigger").stringValue = def.trigger;
            el.FindPropertyRelative("duration").floatValue = clip.length / def.speed;
            el.FindPropertyRelative("activeStart").floatValue = def.activeStart;
            el.FindPropertyRelative("activeEnd").floatValue = def.activeEnd;
            el.FindPropertyRelative("comboOpen").floatValue = def.comboOpen;
            el.FindPropertyRelative("comboClose").floatValue = def.comboClose;
            // The launcher is the last hit: the chain ends by popping the target
            // up, which is what sets up juggling with the air attack.
            el.FindPropertyRelative("launcher").boolValue = i == 3;
        }

        var airDef = AttackClips[^1];
        if (clips.TryGetValue("airAttack", out var air))
        {
            var ap = so.FindProperty("airAttack");
            ap.FindPropertyRelative("trigger").stringValue = airDef.trigger;
            ap.FindPropertyRelative("duration").floatValue = air.length / airDef.speed;
            ap.FindPropertyRelative("activeStart").floatValue = airDef.activeStart;
            ap.FindPropertyRelative("activeEnd").floatValue = airDef.activeEnd;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ----------------------------------------------------------------- helpers

    static string FindModel(string keyword)
    {
        if (!AssetDatabase.IsValidFolder(PropsDir)) return null;
        return AssetDatabase.FindAssets("t:Model", new[] { PropsDir })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => p.ToLowerInvariant().Contains(keyword))
            .OrderBy(p => p.Length)
            .FirstOrDefault();
    }

    static Bounds MeasureBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }
}
