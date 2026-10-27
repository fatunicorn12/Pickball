using System.Collections.Generic;
using System.Linq;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using Pickleball.Sandbox.Combat.Presentation;
using Pickleball.Sandbox.Combat.Progression;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Tools ▸ Pickleball ▸ Setup Sword Abilities. Builds the Kingdom-Hearts-style
/// sword ability system end to end, idempotently:
///
///  1. <b>Data</b> — seeds the step, ability, moveset and leveling assets under
///     ScriptableObjects/Combat. A seed is written ONLY when its asset does not
///     exist yet; after that the asset is the source of truth and re-running
///     never overwrites tuning (it only fills in a missing clip or effect).
///  2. <b>VFX</b> — rebuilds the effect prefabs and materials under
///     Art/VFX/Sword (same content every run, GUIDs kept).
///  3. <b>Animator</b> — makes the Attack layer hold one state per step asset,
///     playing the step's clip at the step's speed. States are added or updated,
///     never removed, so states owned by Setup Weapon are left alone.
///  4. <b>Scene</b> — adds and wires the combat, progression and presentation
///     components on the player, the HUD toast, and re-bakes SwingTwist so the
///     new swings get their hip turn back.
///
/// Run order: after Setup Chibi Sandbox / Setup Weapon / Rebuild Chibi Animator,
/// each of which rebuilds the Attack layer from scratch and drops these states.
/// (Once WeaponSetup calls <see cref="WireAttackStatesFromAssets"/> itself, that
/// ordering stops mattering — see Docs/Systems/SwordAbilities.md.)
///
/// The vertical slice deliberately uses only the five sword clips that are
/// already imported as Humanoid, each in its own state at its own speed: the
/// rest of the pack is still Generic, and reimporting it is shared-asset work.
/// </summary>
public static class SwordAbilitySetup
{
    const string DataDir = "Assets/_Project/ScriptableObjects/Combat";
    const string StepDir = DataDir + "/Steps";
    const string AbilityDir = DataDir + "/Abilities";
    const string VfxDir = "Assets/_Project/Art/VFX/Sword";
    const string PackDir = "Assets/_Project/Animation/Chibi/SwordShield";
    const string ControllerPath = "Assets/_Project/Animation/Controllers/ChibiLocomotion.controller";
    const string MovesetPath = DataDir + "/Moveset_Compass.asset";
    const string LevelingPath = DataDir + "/Leveling_Player.asset";
    const string HudName = "SwordHUD";

    // ------------------------------------------------------------------ seeds

    sealed class StepSeed
    {
        public string Asset, Display, State, Clip;
        public float Speed = 2.4f, BlendIn = 0.1f, Lunge;
        public SwingWindows Windows = SwingWindows.Default;
        public HitSettings Hit;
        public AreaBurst Burst;
        public Color Trail = new(0.8f, 0.95f, 1f, 0.9f);
        public float Shake = 0.15f, ShakeSeconds = 0.18f, Fov;
        public string HitFx, BurstFx;
    }

    /// <summary>
    /// The base chain mirrors WeaponSetup.AttackClips exactly (state names,
    /// clips, speeds, windows) so the two never fight over a state. Measured
    /// directions alternate A/B; see the clip audit in the system doc.
    /// </summary>
    static readonly StepSeed[] Steps =
    {
        new() { Asset = "Step_Slash1", Display = "Opening Slash", State = "Attack1",
                Clip = "sword and shield slash (5).fbx", Speed = 2.5f, BlendIn = 0.06f,
                Windows = new SwingWindows(0.32f, 0.60f, 0.46f, 0.90f), Hit = new HitSettings(10f, 4f, 0f, 0.05f) },
        new() { Asset = "Step_Slash2", Display = "Return Sweep", State = "Attack2",
                Clip = "sword and shield attack (3).fbx", Speed = 2.5f,
                Windows = new SwingWindows(0.35f, 0.62f, 0.50f, 0.92f), Hit = new HitSettings(12f, 4f, 0f, 0.05f) },
        new() { Asset = "Step_Slash3", Display = "Downstroke", State = "Attack3",
                Clip = "sword and shield slash.fbx", Speed = 2.4f,
                Windows = new SwingWindows(0.34f, 0.62f, 0.50f, 0.92f), Hit = new HitSettings(14f, 3f, 0f, 0.07f) },
        new() { Asset = "Step_Slash4", Display = "Rising Finish", State = "Attack4",
                Clip = "sword and shield slash (3).fbx", Speed = 2.2f,
                Windows = new SwingWindows(0.34f, 0.64f, 0.55f, 0.95f), Hit = new HitSettings(20f, 8f, 12.8f, 0.1f),
                Shake = 0.25f },
        new() { Asset = "Step_AirSlash", Display = "Air Slash", State = "AirAttack",
                Clip = "sword and shield attack (4).fbx", Speed = 2.2f, BlendIn = 0.06f,
                Windows = new SwingWindows(0.25f, 0.60f, 0.45f, 0.90f), Hit = new HitSettings(12f, 3f, 0f, 0.05f) },

        // Lv 2 — + C. Attack1's flat sweep, played faster as a quick fourth hit:
        // direction A, so it alternates into the B-direction Rising Finish.
        new() { Asset = "Step_PlusC", Display = "+ C", State = "PlusC",
                Clip = "sword and shield slash (5).fbx", Speed = 3.0f,
                Windows = new SwingWindows(0.30f, 0.58f, 0.44f, 0.88f), Hit = new HitSettings(13f, 1.5f, 0f, 0.05f),
                Trail = new Color(1f, 0.85f, 0.35f, 0.95f), HitFx = "PlusPop" },
        // Lv 3 — Phase Shift. The downstroke, as a second air hit after the
        // compact Air Slash: the wave dips.
        new() { Asset = "Step_PhaseShift", Display = "Phase Shift", State = "PhaseShift",
                Clip = "sword and shield slash.fbx", Speed = 2.6f,
                Windows = new SwingWindows(0.30f, 0.60f, 0.46f, 0.90f), Hit = new HitSettings(14f, 2.5f, 0f, 0.06f),
                Trail = new Color(0.75f, 0.5f, 1f, 0.95f) },
        // Lv 4 — Unit Circle. The widest sweep, releasing a 1.8 m ring all the
        // way round the player in mid-air, with a radius line sweeping like θ.
        new() { Asset = "Step_UnitCircle", Display = "Unit Circle", State = "UnitCircle",
                Clip = "sword and shield attack (3).fbx", Speed = 2.4f,
                Windows = new SwingWindows(0.30f, 0.62f, 0.55f, 0.92f), Hit = new HitSettings(16f, 5f, 0f, 0.08f),
                Burst = new AreaBurst(0.45f, 1.8f, 0.15f, new HitSettings(10f, 5f, 0f, 0.06f), 0f, 1.4f),
                Trail = new Color(0.45f, 1f, 0.95f, 0.95f), Shake = 0.25f, ShakeSeconds = 0.2f, Fov = 3f,
                BurstFx = "UnitCircleRing" },
        // Lv 5 — Area Under the Curve. The rising launcher, slower and heavier,
        // stepping in and releasing a 3 m Riemann ring along the ground.
        new() { Asset = "Step_AreaUnderCurve", Display = "Area Under the Curve", State = "AreaUnderCurve",
                Clip = "sword and shield slash (3).fbx", Speed = 2.0f, Lunge = 2.5f,
                Windows = new SwingWindows(0.34f, 0.64f, 0.58f, 0.95f), Hit = new HitSettings(20f, 8f, 12.8f, 0.1f),
                Burst = new AreaBurst(0.62f, 3f, 0.35f, new HitSettings(12f, 6f, 2f, 0.08f), 0.8f, 0.6f),
                Trail = new Color(0.55f, 1f, 0.45f, 0.95f), Shake = 0.3f, ShakeSeconds = 0.22f, Fov = 4f,
                BurstFx = "RiemannRing" },
    };

    sealed class AbilitySeed
    {
        public string Asset, Display, Description, Step;
        public ComboRole Role;
        public ComboTrack Track;
        public int ChainOrder, Priority, Level;
    }

    static readonly AbilitySeed[] Abilities =
    {
        new() { Asset = "Ability_PlusC", Display = "+ C", Step = "Step_PlusC", Level = 2,
                Role = ComboRole.Extender, Track = ComboTrack.Ground, ChainOrder = 10,
                Description = "Combo Plus. The constant of integration — every slash you forgot to add." },
        new() { Asset = "Ability_PhaseShift", Display = "Phase Shift", Step = "Step_PhaseShift", Level = 3,
                Role = ComboRole.Extender, Track = ComboTrack.Air, ChainOrder = 10,
                Description = "Air Combo Plus. sin(x + π/2): the same wave a quarter-turn later." },
        new() { Asset = "Ability_UnitCircle", Display = "Unit Circle", Step = "Step_UnitCircle", Level = 4,
                Role = ComboRole.Finisher, Track = ComboTrack.Air,
                Description = "Air finisher. Radius one, all the way round: everything in reach is cos θ, sin θ and sorry." },
        new() { Asset = "Ability_AreaUnderCurve", Display = "Area Under the Curve", Step = "Step_AreaUnderCurve", Level = 5,
                Role = ComboRole.Finisher, Track = ComboTrack.Ground,
                Description = "Ground finisher. ∫ from a to b — a Riemann ring strikes everything under it." },
    };

    // ------------------------------------------------------------------- menu

    [MenuItem("Tools/Pickleball/Setup Sword Abilities")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Setup Sword Abilities edits assets and the scene — exit Play first.");
            return;
        }

        BuildData(out var moveset, out var leveling);
        WireAttackStatesFromAssets();

        var player = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (player == null)
        {
            Debug.LogWarning("Sword ability assets built, but there is no ChibiLocomotion in the " +
                             "open scene to wire. Open ChibiSandbox and run this again.");
            AssetDatabase.SaveAssets();
            return;
        }
        if (!WirePlayer(player.gameObject, moveset, leveling)) return;

        // New Attack-layer states need their hip turn baked, or they swat flies.
        SwingTwistSetup.Bake(player.gameObject);

        EditorUtility.SetDirty(player.gameObject);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("Sword abilities set up. Land sword hits to earn Lemmas: Lv2 + C (5-hit chain), " +
                  "Lv3 Phase Shift (2nd air hit), Lv4 Unit Circle (air finisher), Lv5 Area Under the " +
                  "Curve (ground finisher). PlayerLevel ▸ ⋮ ▸ Debug: Gain One Level to skip ahead.");
    }

    // ------------------------------------------------------------------- data

    /// <summary>Create any missing data asset from its seed. Existing assets are
    /// only topped up (a missing clip or effect reference), never retuned.</summary>
    static void BuildData(out SwordMoveset moveset, out LevelingConfig leveling)
    {
        ChibiSandboxSetup.EnsureFolderPublic(StepDir);
        ChibiSandboxSetup.EnsureFolderPublic(AbilityDir);
        var fx = BuildEffects();

        var steps = new Dictionary<string, ComboStepDefinition>();
        foreach (var seed in Steps) steps[seed.Asset] = EnsureStep(seed, fx);

        var abilities = new Dictionary<string, SwordAbilityDefinition>();
        foreach (var seed in Abilities)
            abilities[seed.Asset] = EnsureAbility(seed, steps.TryGetValue(seed.Step, out var s) ? s : null);

        moveset = AssetDatabase.LoadAssetAtPath<SwordMoveset>(MovesetPath);
        if (moveset == null)
        {
            moveset = ScriptableObject.CreateInstance<SwordMoveset>();
            var so = new SerializedObject(moveset);
            SetList(so.FindProperty("groundChain"), steps["Step_Slash1"], steps["Step_Slash2"], steps["Step_Slash3"]);
            so.FindProperty("groundFinisher").objectReferenceValue = steps["Step_Slash4"];
            SetList(so.FindProperty("airChain"), steps["Step_AirSlash"]);
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(moveset, MovesetPath);
        }

        leveling = AssetDatabase.LoadAssetAtPath<LevelingConfig>(LevelingPath);
        if (leveling == null)
        {
            leveling = ScriptableObject.CreateInstance<LevelingConfig>();
            var so = new SerializedObject(leveling);
            var table = so.FindProperty("unlocks");
            table.arraySize = Abilities.Length;
            for (int i = 0; i < Abilities.Length; i++)
            {
                var row = table.GetArrayElementAtIndex(i);
                row.FindPropertyRelative("level").intValue = Abilities[i].Level;
                row.FindPropertyRelative("ability").objectReferenceValue = abilities[Abilities[i].Asset];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(leveling, LevelingPath);
        }
    }

    static ComboStepDefinition EnsureStep(StepSeed seed, Dictionary<string, GameObject> fx)
    {
        string path = $"{StepDir}/{seed.Asset}.asset";
        var step = AssetDatabase.LoadAssetAtPath<ComboStepDefinition>(path);
        bool created = step == null;
        if (created) step = ScriptableObject.CreateInstance<ComboStepDefinition>();

        var so = new SerializedObject(step);
        if (created)
        {
            so.FindProperty("displayName").stringValue = seed.Display;
            so.FindProperty("animatorState").stringValue = seed.State;
            so.FindProperty("playbackSpeed").floatValue = seed.Speed;
            so.FindProperty("blendIn").floatValue = seed.BlendIn;
            so.FindProperty("lungeSpeed").floatValue = seed.Lunge;
            WriteWindows(so.FindProperty("windows"), seed.Windows);
            WriteHit(so.FindProperty("hit"), seed.Hit);
            WriteBurst(so.FindProperty("burst"), seed.Burst);
            var look = so.FindProperty("presentation");
            look.FindPropertyRelative("trailColor").colorValue = seed.Trail;
            look.FindPropertyRelative("shakeAmount").floatValue = seed.Shake;
            look.FindPropertyRelative("shakeSeconds").floatValue = seed.ShakeSeconds;
            look.FindPropertyRelative("fovPunch").floatValue = seed.Fov;
        }

        // Top-ups: safe on an existing asset because they only fill blanks.
        var clipProp = so.FindProperty("clip");
        if (clipProp.objectReferenceValue == null) clipProp.objectReferenceValue = LoadClip(seed.Clip);
        var hitFx = so.FindProperty("presentation.hitEffect");
        if (hitFx.objectReferenceValue == null && seed.HitFx != null && fx.TryGetValue(seed.HitFx, out var h))
            hitFx.objectReferenceValue = h;
        var burstFx = so.FindProperty("presentation.burstEffect");
        if (burstFx.objectReferenceValue == null && seed.BurstFx != null && fx.TryGetValue(seed.BurstFx, out var b))
            burstFx.objectReferenceValue = b;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (created) AssetDatabase.CreateAsset(step, path);
        return step;
    }

    static SwordAbilityDefinition EnsureAbility(AbilitySeed seed, ComboStepDefinition step)
    {
        string path = $"{AbilityDir}/{seed.Asset}.asset";
        var ability = AssetDatabase.LoadAssetAtPath<SwordAbilityDefinition>(path);
        bool created = ability == null;
        if (created) ability = ScriptableObject.CreateInstance<SwordAbilityDefinition>();

        var so = new SerializedObject(ability);
        if (created)
        {
            so.FindProperty("displayName").stringValue = seed.Display;
            so.FindProperty("description").stringValue = seed.Description;
            so.FindProperty("role").enumValueIndex = (int)seed.Role;
            so.FindProperty("track").enumValueIndex = (int)seed.Track;
            so.FindProperty("chainOrder").intValue = seed.ChainOrder;
            so.FindProperty("priority").intValue = seed.Priority;
        }
        var stepProp = so.FindProperty("step");
        if (stepProp.objectReferenceValue == null) stepProp.objectReferenceValue = step;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (created) AssetDatabase.CreateAsset(ability, path);
        return ability;
    }

    static AnimationClip LoadClip(string file)
    {
        if (string.IsNullOrEmpty(file)) return null;
        string path = $"{PackDir}/{file}";
        // Every Mixamo take is called "mixamo.com", so load by path, not name.
        var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        if (clip == null) Debug.LogWarning($"Setup Sword Abilities: no clip in {path}.");
        return clip;
    }

    static void WriteWindows(SerializedProperty p, SwingWindows w)
    {
        p.FindPropertyRelative("activeStart").floatValue = w.ActiveStart;
        p.FindPropertyRelative("activeEnd").floatValue = w.ActiveEnd;
        p.FindPropertyRelative("comboOpen").floatValue = w.ComboOpen;
        p.FindPropertyRelative("comboClose").floatValue = w.ComboClose;
    }

    static void WriteHit(SerializedProperty p, HitSettings h)
    {
        p.FindPropertyRelative("damage").floatValue = h.Damage;
        p.FindPropertyRelative("knockback").floatValue = h.Knockback;
        p.FindPropertyRelative("launchSpeed").floatValue = h.LaunchSpeed;
        p.FindPropertyRelative("hitstop").floatValue = h.Hitstop;
    }

    static void WriteBurst(SerializedProperty p, AreaBurst b)
    {
        p.FindPropertyRelative("enabled").boolValue = b.Enabled;
        p.FindPropertyRelative("triggerAt").floatValue = b.TriggerAt;
        p.FindPropertyRelative("radius").floatValue = b.Radius;
        p.FindPropertyRelative("expandSeconds").floatValue = b.ExpandSeconds;
        p.FindPropertyRelative("forwardOffset").floatValue = b.ForwardOffset;
        p.FindPropertyRelative("heightTolerance").floatValue = Mathf.Max(0.05f, b.HeightTolerance);
        WriteHit(p.FindPropertyRelative("hit"), b.Hit);
    }

    static void SetList(SerializedProperty list, params Object[] items)
    {
        list.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    // --------------------------------------------------------------- animator

    /// <summary>
    /// Make the Attack layer hold a state for every step asset under
    /// ScriptableObjects/Combat — including ones the user adds — playing the
    /// step's clip at its speed. Adds or updates; never removes, so states
    /// Setup Weapon owns are untouched (the base step assets match them).
    ///
    /// Public so WeaponSetup / Rebuild Chibi Animator can call it after they
    /// rebuild the layer from scratch.
    /// </summary>
    public static void WireAttackStatesFromAssets()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var layer = controller == null ? null
            : controller.layers.FirstOrDefault(l => l.name == WeaponSetup.AttackLayer);
        if (layer == null)
        {
            Debug.LogError($"No '{WeaponSetup.AttackLayer}' layer on {ControllerPath} — run " +
                           "Tools ▸ Pickleball ▸ Setup Weapon first.");
            return;
        }

        var sm = layer.stateMachine;
        var claimed = new Dictionary<string, ComboStepDefinition>();
        int added = 0, updated = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:ComboStepDefinition", new[] { DataDir }))
        {
            var step = AssetDatabase.LoadAssetAtPath<ComboStepDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (step == null || string.IsNullOrEmpty(step.AnimatorState)) continue;

            if (claimed.TryGetValue(step.AnimatorState, out var other))
            {
                if (other.Clip != step.Clip || !Mathf.Approximately(other.PlaybackSpeed, step.PlaybackSpeed))
                    Debug.LogWarning($"Steps '{other.name}' and '{step.name}' both use Attack-layer state " +
                                     $"'{step.AnimatorState}' with a different clip or speed; '{other.name}' wins. " +
                                     "Give one its own state name.");
                continue;
            }
            claimed[step.AnimatorState] = step;
            WarnIfNotHumanoid(step);

            var state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == step.AnimatorState);
            if (state == null)
            {
                state = sm.AddState(step.AnimatorState, new Vector3(300f, 60f * (sm.states.Length + 1), 0f));
                added++;
            }
            else if (state.motion != step.Clip || !Mathf.Approximately(state.speed, step.PlaybackSpeed))
            {
                updated++;
            }
            if (step.Clip != null) state.motion = step.Clip;
            state.speed = step.PlaybackSpeed;
            state.iKOnFeet = false;     // upper-body layer: foot IK has nothing to do
        }

        EditorUtility.SetDirty(controller);
        Debug.Log($"Attack layer: {added} sword-ability state(s) added, {updated} updated, " +
                  $"{claimed.Count} step(s) checked.");
    }

    static void WarnIfNotHumanoid(ComboStepDefinition step)
    {
        if (step.Clip == null) return;
        var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(step.Clip)) as ModelImporter;
        if (importer != null && importer.animationType != ModelImporterAnimationType.Human)
            Debug.LogWarning($"Step '{step.name}' uses a {importer.animationType} clip " +
                             $"({importer.assetPath}). The masked Attack layer needs Humanoid: reimport it " +
                             "with WeaponSetup.ImportAttackClips' settings (Humanoid, loop off, lock root " +
                             "rotation, keep original orientation).");
    }

    // ------------------------------------------------------------------ scene

    static bool WirePlayer(GameObject player, SwordMoveset moveset, LevelingConfig leveling)
    {
        var combat = player.GetComponent<MeleeCombat>();
        if (combat == null)
        {
            Debug.LogError("Player has no MeleeCombat — run Tools ▸ Pickleball ▸ Setup Weapon first.");
            return false;
        }
        var locomotion = player.GetComponent<ChibiLocomotion>();
        var animator = player.GetComponentInChildren<Animator>();
        var cam = Camera.main;
        var orbit = cam != null ? cam.GetComponent<SandboxOrbitCamera>() : null;
        var trail = player.GetComponentInChildren<BladeTrail>(true);

        var adapter = GetOrAdd<ChibiMovementAdapter>(player);
        Set(adapter, ("locomotion", locomotion));

        var input = GetOrAdd<SwordInput>(player);
        Set(input, ("cameraRig", orbit));

        var abilities = GetOrAdd<SwordAbilities>(player);

        Set(combat, ("moveset", moveset), ("abilities", abilities), ("movementSource", adapter),
            ("input", input), ("lockOn", player.GetComponent<LockOnTargeting>()));

        var level = GetOrAdd<PlayerLevel>(player);
        Set(level, ("config", leveling));
        Set(GetOrAdd<CombatXpSource>(player), ("combat", combat), ("level", level));
        Set(GetOrAdd<LevelUnlocks>(player), ("level", level), ("abilities", abilities));

        Set(GetOrAdd<SwordAnimatorDriver>(player), ("combat", combat), ("animator", animator));
        Set(GetOrAdd<SwordEffects>(player), ("combat", combat), ("trail", trail), ("cameraRig", orbit));

        // The ribbon's tint and fade are vertex colour; give it a material that
        // multiplies vertex colour in (Setup Weapon's URP/Unlit one does not).
        if (trail != null && trail.TryGetComponent<MeshRenderer>(out var trailRenderer))
            trailRenderer.sharedMaterial = ParticleMaterial("SwordTrail", null, Color.white, additive: true);
        else
            Debug.LogWarning("No BladeTrail under the player — slashes will have no ribbon. Run Setup Weapon.");

        BuildHud(level, abilities, player.GetComponent<PlayerAbilities>());
        return true;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
        => go.TryGetComponent<T>(out var existing) ? existing : Undo.AddComponent<T>(go);

    static void Set(Object target, params (string field, Object value)[] refs)
    {
        var so = new SerializedObject(target);
        foreach (var (field, value) in refs)
        {
            var p = so.FindProperty(field);
            if (p == null) Debug.LogError($"{target.GetType().Name} has no serialized '{field}'.");
            else p.objectReferenceValue = value;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // -------------------------------------------------------------------- HUD

    /// <summary>The Q.E.D. toast, top-centre, and the level readout above the
    /// player's health bar. Own canvas, so the Newton fight HUD can be rebuilt
    /// without losing it; children are rebuilt each run.</summary>
    static void BuildHud(PlayerLevel level, SwordAbilities abilities, PlayerAbilities movement)
    {
        var canvasGo = GameObject.Find(HudName);
        if (canvasGo == null)
        {
            canvasGo = new GameObject(HudName, typeof(Canvas), typeof(CanvasScaler));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 5;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }
        var parent = canvasGo.transform;
        foreach (var stale in new[] { "ProgressionToast", "LevelLabel" })
            if (parent.Find(stale) is { } old) Object.DestroyImmediate(old.gameObject);

        var font = NewtonFightArt.Font("Oswald Bold SDF");
        var outlined = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/UI/HudLabel.mat");

        var toastGo = new GameObject("ProgressionToast", typeof(RectTransform));
        toastGo.transform.SetParent(parent, false);
        var rect = (RectTransform)toastGo.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -190f);
        rect.sizeDelta = new Vector2(1200f, 220f);

        var title = Text(toastGo.transform, "Title", font, outlined, 72f, FontStyles.Bold,
                         new Color(1f, 0.86f, 0.4f), new Vector2(0f, 0f), new Vector2(1200f, 90f));
        var body = Text(toastGo.transform, "Body", font, outlined, 36f, FontStyles.Normal,
                        new Color(1f, 0.96f, 0.88f), new Vector2(0f, -88f), new Vector2(1200f, 130f));

        var labelGo = new GameObject("LevelLabel", typeof(RectTransform));
        labelGo.transform.SetParent(parent, false);
        var lr = (RectTransform)labelGo.transform;
        lr.anchorMin = lr.anchorMax = lr.pivot = Vector2.zero;
        lr.anchoredPosition = new Vector2(48f, 88f);       // just above the player's health bar
        lr.sizeDelta = new Vector2(600f, 40f);
        var label = labelGo.AddComponent<TextMeshProUGUI>();
        StyleText(label, font, outlined, 28f, FontStyles.Bold, new Color(0.85f, 0.95f, 1f));
        label.alignment = TextAlignmentOptions.BottomLeft;

        var toast = toastGo.AddComponent<ProgressionToast>();
        Set(toast, ("level", level), ("swordAbilities", abilities), ("movementAbilities", movement),
            ("title", title), ("body", body), ("levelLabel", label));
        EditorUtility.SetDirty(canvasGo);
    }

    static TMP_Text Text(Transform parent, string name, TMP_FontAsset font, Material mat, float size,
                         FontStyles style, Color colour, Vector2 offset, Vector2 box)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = box;
        var text = go.AddComponent<TextMeshProUGUI>();
        StyleText(text, font, mat, size, style, colour);
        text.alignment = TextAlignmentOptions.Top;
        return text;
    }

    static void StyleText(TMP_Text text, TMP_FontAsset font, Material mat, float size, FontStyles style, Color colour)
    {
        if (font != null) text.font = font;
        if (mat != null) text.fontSharedMaterial = mat;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = colour;
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        text.text = "";
    }

    // -------------------------------------------------------------------- VFX

    /// <summary>Rebuild every sword effect prefab. Same content each run;
    /// saving over an existing prefab keeps its GUID, so step references hold.</summary>
    static Dictionary<string, GameObject> BuildEffects()
    {
        ChibiSandboxSetup.EnsureFolderPublic(VfxDir);
        var glyph = ParticleMaterial("SwordGlyph", null, Color.white, additive: true);
        var line = ParticleMaterial("SwordRing", null, Color.white, additive: true);
        var fill = ParticleMaterial("SwordFill",
                                    AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_Project/Art/VFX/Disc.png"),
                                    Color.white, additive: true);

        return new Dictionary<string, GameObject>
        {
            ["PlusPop"] = BuildPlusPop(glyph),
            ["RiemannRing"] = BuildRing("RiemannRing", line, glyph, fill, bars: 12, radiusLine: false),
            ["UnitCircleRing"] = BuildRing("UnitCircleRing", line, glyph, fill, bars: 0, radiusLine: true),
        };
    }

    /// <summary>"+": two crossed bars, popped at each + C hit.</summary>
    static GameObject BuildPlusPop(Material mat)
    {
        var root = new GameObject("PlusPop");
        Bar(root.transform, "Horizontal", mat, new Vector3(1f, 0.2f, 0.05f));
        Bar(root.transform, "Vertical", mat, new Vector3(0.2f, 1f, 0.05f));
        root.AddComponent<PopFlashEffect>();
        return SavePrefab(root, "PlusPop");
    }

    static GameObject BuildRing(string name, Material lineMat, Material barMat, Material fillMat,
                                int bars, bool radiusLine)
    {
        var root = new GameObject(name);
        var effect = root.AddComponent<RingBurstEffect>();

        var ring = Line(root.transform, "Ring", lineMat, 0.08f);

        LineRenderer radius = null;
        if (radiusLine) radius = Line(root.transform, "Radius", lineMat, 0.05f);

        var barTransforms = new Transform[bars];
        for (int i = 0; i < bars; i++)
        {
            float a = i * Mathf.PI * 2f / bars;
            var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var bar = Bar(root.transform, $"Bar{i:00}", barMat, new Vector3(0.16f, 0.4f, 0.04f));
            // Placed on the unit circle: RingBurstEffect reads each bar's angle
            // from here, then scales it to the ring and to |sin 3θ|.
            bar.localPosition = dir;
            bar.localRotation = Quaternion.LookRotation(dir, Vector3.up);
            barTransforms[i] = bar;
        }

        var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
        disc.name = "Fill";
        Object.DestroyImmediate(disc.GetComponent<Collider>());
        disc.transform.SetParent(root.transform, false);
        disc.transform.localPosition = new Vector3(0f, 0.02f, 0f);
        disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        Quiet(disc.GetComponent<MeshRenderer>(), fillMat);

        var so = new SerializedObject(effect);
        so.FindProperty("ring").objectReferenceValue = ring;
        so.FindProperty("radiusLine").objectReferenceValue = radius;
        so.FindProperty("fill").objectReferenceValue = disc.transform;
        var barsProp = so.FindProperty("bars");
        barsProp.arraySize = bars;
        for (int i = 0; i < bars; i++) barsProp.GetArrayElementAtIndex(i).objectReferenceValue = barTransforms[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        return SavePrefab(root, name);
    }

    static Transform Bar(Transform parent, string name, Material mat, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localScale = scale;
        Quiet(go.GetComponent<MeshRenderer>(), mat);
        return go.transform;
    }

    static LineRenderer Line(Transform parent, string name, Material mat, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.sharedMaterial = mat;
        lr.widthMultiplier = width;
        lr.useWorldSpace = false;
        lr.numCapVertices = 2;
        lr.positionCount = 0;
        lr.shadowCastingMode = ShadowCastingMode.Off;
        lr.receiveShadows = false;
        return lr;
    }

    static void Quiet(Renderer r, Material mat)
    {
        r.sharedMaterial = mat;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    static GameObject SavePrefab(GameObject root, string name)
    {
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{VfxDir}/{name}.prefab");
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>
    /// URP Particles/Unlit, transparent, under Art/VFX/Sword. Particles/Unlit
    /// rather than URP/Unlit because it multiplies in vertex colour, which is
    /// how the LineRenderer rings and the blade ribbon carry their tint and
    /// fade (the same lesson NewtonFightArt.ParticleMaterial records). Blend
    /// state is written by hand: a script-made material never passes through
    /// the URP inspector that would derive it.
    /// </summary>
    static Material ParticleMaterial(string name, Texture texture, Color tint, bool additive)
    {
        string path = $"{VfxDir}/{name}.mat";
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }

        mat.SetTexture("_BaseMap", texture);
        mat.SetColor("_BaseColor", tint);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", additive ? 2f : 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
