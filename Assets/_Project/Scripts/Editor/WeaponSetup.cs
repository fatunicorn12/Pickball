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
    /// </summary>
    static readonly (string role, string file, string trigger,
                     float activeStart, float activeEnd,
                     float comboOpen, float comboClose)[] AttackClips =
    {
        // 1.37s, L>R, flat, wide — fastest of the set, so it opens.
        ("attack1", "sword and shield slash (5).fbx", "Attack1", 0.32f, 0.60f, 0.46f, 0.90f),
        // 1.73s, R>L, widest lateral sweep (0.603) and longest reach.
        ("attack2", "sword and shield attack (3).fbx", "Attack2", 0.35f, 0.62f, 0.50f, 0.92f),
        // 1.50s, L>R, downward, big vertical — the heavy third hit.
        ("attack3", "sword and shield slash.fbx", "Attack3", 0.34f, 0.62f, 0.50f, 0.92f),
        // 1.57s, R>L, UPWARD — the launcher that ends the ground chain.
        ("attack4", "sword and shield slash (3).fbx", "Attack4", 0.34f, 0.64f, 0.55f, 0.95f),
        // 1.00s, in place, compact — suits being airborne.
        ("airAttack", "sword and shield attack (4).fbx", "AirAttack", 0.25f, 0.60f, 0.5f, 0.9f),
    };

    [MenuItem("Tools/Pickleball/Setup Weapon")]
    public static void Run()
    {
        var player = GameObject.Find("Chibi");
        if (player == null)
        {
            Debug.LogError("No 'Chibi' in the scene — run Setup Chibi Sandbox first.");
            return;
        }
        var animator = player.GetComponentInChildren<Animator>();
        if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
        {
            Debug.LogError("Player has no Humanoid avatar; cannot find the hand bone.");
            return;
        }

        var hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null)
        {
            Debug.LogError("Avatar has no RightHand bone mapped.");
            return;
        }

        OrganizeWeaponAssets();
        var clips = ImportAttackClips();
        var weapon = AttachWeapon(hand, out Transform bladeBase, out Transform bladeTip);
        WireController(clips);
        WireCombat(player, animator, weapon, bladeBase, bladeTip, clips);

        EditorUtility.SetDirty(player);
        AssetDatabase.SaveAssets();
        Debug.Log("Weapon setup complete. Left click to attack; click again mid-swing to chain.");
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
        foreach (var (role, file, _, _, _, _, _) in AttackClips)
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
                    d.lockRootRotation = false;
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
        float length;

        if (modelPath != null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            visual.transform.SetParent(socketGo.transform, false);
            var b = MeasureBounds(visual);
            length = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
            // Normalise to a believable weapon length regardless of authored scale.
            const float wanted = 0.85f;
            if (length > 0.01f)
            {
                visual.transform.localScale = Vector3.one * (wanted / length);
                length = wanted;
            }
            Debug.Log($"Weapon model: {modelPath} (scaled to {wanted:0.00}m)");
        }
        else
        {
            visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.transform.SetParent(socketGo.transform, false);
            visual.transform.localScale = new Vector3(0.04f, 0.42f, 0.04f);
            length = 0.85f;
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

    // ------------------------------------------------------------- controller

    static void WireController(Dictionary<string, AnimationClip> clips)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"Controller not found: {ControllerPath}");
            return;
        }

        var existing = new HashSet<string>(controller.parameters.Select(p => p.name));
        foreach (var (_, _, trigger, _, _, _, _) in AttackClips)
            if (!existing.Contains(trigger))
                controller.AddParameter(trigger, AnimatorControllerParameterType.Trigger);
        if (!existing.Contains("Combo"))
            controller.AddParameter("Combo", AnimatorControllerParameterType.Int);

        var sm = controller.layers[0].stateMachine;
        var loco = sm.states.FirstOrDefault(s => s.state.name == "Locomotion").state;
        var fall = sm.states.FirstOrDefault(s => s.state.name == "Fall").state;
        if (loco == null)
        {
            Debug.LogError("No 'Locomotion' state — run Setup Chibi Sandbox first.");
            return;
        }

        // Remove any previous attack states so re-running is idempotent.
        foreach (var s in sm.states.Where(s => s.state.name.StartsWith("Attack")).ToList())
            sm.RemoveState(s.state);

        var chainStates = new List<AnimatorState>();
        for (int i = 0; i < 4; i++)
        {
            string role = $"attack{i + 1}";
            if (!clips.TryGetValue(role, out var clip)) break;
            var st = sm.AddState($"Attack{i + 1}");
            st.motion = clip;
            st.iKOnFeet = false;         // planted, and the arms are the point
            chainStates.Add(st);
        }

        // Chain: locomotion opens it, each attack can only reach the next one.
        // That is what makes the combo a chain rather than four separate moves.
        for (int i = 0; i < chainStates.Count; i++)
        {
            var from = i == 0 ? loco : chainStates[i - 1];
            var t = from.AddTransition(chainStates[i]);
            t.hasExitTime = false;
            t.duration = i == 0 ? 0.06f : 0.1f;   // snap into the first, blend later ones
            t.AddCondition(AnimatorConditionMode.If, 0, $"Attack{i + 1}");

            var back = chainStates[i].AddTransition(loco);
            back.hasExitTime = true; back.exitTime = 0.85f; back.duration = 0.18f;
        }

        if (clips.TryGetValue("airAttack", out var airClip) && fall != null)
        {
            var air = sm.AddState("AttackAir");
            air.motion = airClip;
            air.iKOnFeet = false;

            var t = fall.AddTransition(air);
            t.hasExitTime = false; t.duration = 0.06f;
            t.AddCondition(AnimatorConditionMode.If, 0, "AirAttack");

            var back = air.AddTransition(fall);
            back.hasExitTime = true; back.exitTime = 0.9f; back.duration = 0.15f;
        }

        EditorUtility.SetDirty(controller);
        Debug.Log($"Combo chain wired: {chainStates.Count} ground attack(s)" +
                  (clips.ContainsKey("airAttack") ? " + air attack" : ""));
    }

    // ------------------------------------------------------------------ combat

    static void WireCombat(GameObject player, Animator animator, GameObject weapon,
                           Transform bladeBase, Transform bladeTip,
                           Dictionary<string, AnimationClip> clips)
    {
        var combat = player.GetComponent<MeleeCombat>() ?? player.AddComponent<MeleeCombat>();

        var trailGo = new GameObject("BladeTrail");
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
        var chainProp = so.FindProperty("chain");
        for (int i = 0; i < chainProp.arraySize && i < 4; i++)
        {
            var def = AttackClips[i];
            if (!clips.TryGetValue(def.role, out var clip)) continue;
            var el = chainProp.GetArrayElementAtIndex(i);
            el.FindPropertyRelative("trigger").stringValue = def.trigger;
            el.FindPropertyRelative("duration").floatValue = clip.length;
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
            ap.FindPropertyRelative("duration").floatValue = air.length;
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
