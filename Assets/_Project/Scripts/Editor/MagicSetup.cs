using System.Collections.Generic;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using Pickleball.Sandbox.Magic;
using Pickleball.Sandbox.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Tools ▸ Pickleball ▸ Setup Magic. Gives the player the Terraria-style mana
/// pool and the aimed Arcane Orb spell, idempotently:
///
///  1. <b>Data</b> — seeds ScriptableObjects/Magic/ManaConfig.asset and
///     ArcaneOrb.asset from the code defaults. A seed is written ONLY when the
///     asset does not exist yet; after that the asset is the source of truth and
///     re-running never overwrites tuning (it only fills a blank orb prefab).
///  2. <b>Prefab</b> — regenerates Prefabs/Magic/MagicOrb.prefab through
///     <see cref="MagicOrbPrefabBuilder"/> (same content every run, GUID kept,
///     so the spell's reference holds).
///  3. <b>Scene</b> — adds Mana, SpellInput, SpellCaster and AimInputClaims to
///     the player if missing (and the PlayerInputGate, if Setup Dialogue has not
///     added it), a "SpellOrigin" child over the right shoulder, points the
///     orbit camera's aim source at the caster, gates the spell, sword and
///     lock-on inputs, and rebuilds the mana bar and the crosshair under the
///     FightHUD canvas through <see cref="MagicHudBuilder"/>.
///
/// Data references on the player (the config, the spell) are filled only when
/// blank, so a hand-assigned second spell survives a re-run. Component-to-
/// component references are rewritten every run, because they are wiring, not
/// tuning — the same rule the other Setup menus follow.
///
/// Order: after Setup Newton Fight (it reuses its FightHUD canvas, and creates
/// one with the same settings if it is missing), and again after any Setup
/// Chibi Sandbox, which builds a fresh scene and drops everything later menus
/// added. The components are scene overrides only, like Setup Lock-On's:
/// Chibi.prefab does not carry them, so scenes that instantiate the prefab
/// (SpacetimeLab) have no magic. Build Training Stage copies ChibiSandbox, so
/// save the sandbox first and the stage inherits the setup.
///
/// Marks the scene dirty and never saves it, like the other Setup menus.
/// </summary>
public static class MagicSetup
{
    const string DataDir = "Assets/_Project/ScriptableObjects/Magic";
    const string ManaConfigPath = DataDir + "/ManaConfig.asset";
    const string SpellPath = DataDir + "/ArcaneOrb.asset";
    const string PrefabDir = "Assets/_Project/Prefabs/Magic";
    const string OrbPrefabPath = PrefabDir + "/MagicOrb.prefab";

    /// <summary>The fight canvas NewtonFightSetup.BuildHud makes; the mana bar
    /// sits above its PlayerHealthBar, so the two must share it.</summary>
    const string HudName = "FightHUD";

    const string OriginName = "SpellOrigin";

    /// <summary>Over the right shoulder (right, up, forward of the player root),
    /// where the over-the-shoulder aim camera sits: an orb launched from here
    /// travels close to the crosshair ray instead of crossing it, so what is
    /// under the crosshair is what the orb meets. Applied only when the origin
    /// is created, so a hand-nudged origin survives a re-run.</summary>
    static readonly Vector3 OriginLocalPosition = new(0.3f, 1.3f, 0.45f);

    // ------------------------------------------------------------------- menu

    [MenuItem("Tools/Pickleball/Setup Magic")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Setup Magic edits assets and the scene — exit Play first.");
            return;
        }

        var player = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (player == null)
        {
            Debug.LogError("Setup Magic: no ChibiLocomotion in the open scene — open ChibiSandbox " +
                           "or run Tools ▸ Pickleball ▸ Setup Chibi Sandbox first.");
            return;
        }

        if (!TryAttach(player, out var summary)) return;
        AssetDatabase.SaveAssets();
        Debug.Log(summary);
    }

    /// <summary>
    /// Everything the menu does for one player: seeds the data assets,
    /// regenerates the orb prefab, adds and wires the components, and rebuilds
    /// the HUD. Public so a future orchestrator can chain it after Setup Newton
    /// Fight. Marks the player's scene dirty but saves nothing — call
    /// <see cref="AssetDatabase.SaveAssets"/> afterwards, and save the scene.
    /// </summary>
    public static void AttachToPlayer(ChibiLocomotion player)
    {
        if (TryAttach(player, out var summary)) Debug.Log(summary);
    }

    static bool TryAttach(ChibiLocomotion player, out string summary)
    {
        summary = null;

        // Preconditions BEFORE any asset is written, so a refused run leaves no
        // orphans on disk for the next run to adopt (NewtonFightSetup's lesson).
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Setup Magic edits assets and the scene — exit Play first.");
            return false;
        }
        if (player == null)
        {
            Debug.LogError("Setup Magic: no player given — nothing to wire.");
            return false;
        }

        var warnings = new List<string>();
        var go = player.gameObject;

        // ---- data + prefab -----------------------------------------------
        ChibiSandboxSetup.EnsureFolderPublic(DataDir);
        ChibiSandboxSetup.EnsureFolderPublic(PrefabDir);

        var config = EnsureManaConfig();
        var orbPrefab = MagicOrbPrefabBuilder.Build(OrbPrefabPath);
        if (orbPrefab == null)
            warnings.Add($"the orb prefab did not build at {OrbPrefabPath} — the spell cannot fire " +
                         "until it does");
        var spell = EnsureSpell(orbPrefab);

        // ---- components ----------------------------------------------------
        // Explicit null checks: `??` on a Unity object skips the "fake null"
        // check and would keep a destroyed component.
        var mana = go.GetComponent<Mana>();
        if (mana == null) mana = go.AddComponent<Mana>();
        var input = go.GetComponent<SpellInput>();
        if (input == null) input = go.AddComponent<SpellInput>();
        var caster = go.GetComponent<SpellCaster>();
        if (caster == null) caster = go.AddComponent<SpellCaster>();
        var claims = go.GetComponent<AimInputClaims>();
        if (claims == null) claims = go.AddComponent<AimInputClaims>();
        // The same single gate Setup Dialogue adds (DisallowMultipleComponent),
        // so the two menus can run in either order.
        var gate = go.GetComponent<PlayerInputGate>();
        if (gate == null) gate = go.AddComponent<PlayerInputGate>();

        var origin = EnsureOrigin(player.transform);

        var orbit = Object.FindFirstObjectByType<SandboxOrbitCamera>();
        Camera aimCamera = orbit != null ? orbit.GetComponent<Camera>() : null;
        if (aimCamera == null) aimCamera = Camera.main;
        if (orbit == null)
            warnings.Add("no SandboxOrbitCamera in the scene — no shoulder aim, and the mouse aim and " +
                         "fire buttons count even while the cursor is free");
        if (aimCamera == null)
            warnings.Add("no camera found for the aim ray — SpellCaster will fall back to Camera.main at runtime");

        // ---- wiring --------------------------------------------------------
        FillIfBlank(mana, "config", config);
        // Mana falls back to GetComponent<ICombatMovement>() when this is empty,
        // so an absent adapter is not an error; it is only left alone.
        var adapter = go.GetComponent<ChibiMovementAdapter>();
        if (adapter != null) Set(mana, ("movementSource", adapter));
        else warnings.Add("no ChibiMovementAdapter on the player (run Setup Sword Abilities) — Mana " +
                          "will look for an ICombatMovement itself, else never count as standing still");

        Set(input, ("cameraRig", orbit), ("inputGate", gate));

        FillIfBlank(caster, "spell", spell);
        Set(caster, ("mana", mana), ("input", input), ("castOrigin", origin),
            ("aimCamera", aimCamera), ("locomotion", player));
        SetMask(caster, "aimMask", AimMaskFor(go));

        // While aiming, fire (LMB / RT) must not also swing the sword or toggle
        // lock-on, and the player walks: AimInputClaims claims the gate and caps
        // the drive. The readers it claims against only listen once their gate
        // field is set, so set it here too (Setup Dialogue sets the same one).
        var lockOn = go.GetComponent<LockOnTargeting>();
        var movementInput = go.GetComponent<PlayerMovementInput>();
        Set(claims, ("caster", caster), ("inputGate", gate), ("lockOn", lockOn),
            ("movementInput", movementInput));
        var sword = go.GetComponentInChildren<SwordInput>(true);
        if (sword != null) Set(sword, ("inputGate", gate));
        else warnings.Add("no SwordInput on the player — nothing to keep from swinging while aiming");
        if (lockOn != null) Set(lockOn, ("inputGate", gate));
        else warnings.Add("no LockOnTargeting on the player (run Setup Lock-On) — nothing to release " +
                          "or block while aiming");
        if (movementInput == null)
            warnings.Add("no PlayerMovementInput on the player — aiming will not slow it to a walk");

        bool cameraWired = false;
        if (orbit != null)
        {
            var oso = new SerializedObject(orbit);
            var aimSource = oso.FindProperty("aimSource");
            if (aimSource != null)
            {
                aimSource.objectReferenceValue = caster;
                oso.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(orbit);
                cameraWired = true;
            }
            else
            {
                warnings.Add("SandboxOrbitCamera has no 'aimSource' field yet — the camera will not " +
                             "move over the shoulder while aiming");
            }
        }

        // ---- HUD -----------------------------------------------------------
        var hud = FindOrCreateHud(go.scene, out bool hudCreated);
        if (hudCreated)
            warnings.Add($"no {HudName} canvas, so one was created — run Setup Newton Fight for the " +
                         "health bar, then Setup Magic again so the mana bar sits on it");
        else if (!hud.activeInHierarchy)
            warnings.Add($"{HudName} is inactive — the mana bar and crosshair will not show until it is enabled");
        MagicHudBuilder.Build(hud.transform, mana, caster);
        EditorUtility.SetDirty(hud);

        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(go.scene);

        summary = $"Magic set up on {go.name}: Mana ({ManaConfigPath}), SpellInput, SpellCaster casting " +
                  $"{SpellPath} ({OrbPrefabPath}) from {OriginName}, AimInputClaims on the input gate, " +
                  (cameraWired ? $"{orbit.name}'s aim source, " : "") +
                  $"and the mana bar + crosshair under {HudName}. " +
                  "Hold right mouse / LT to aim, left mouse / RT to fire. " +
                  "SAVE THE SCENE (Ctrl+S) to keep the wiring.";
        if (warnings.Count > 0)
        {
            Debug.LogWarning("Setup Magic: " + string.Join("; ", warnings) + ".");
            summary += $" ({warnings.Count} warning(s) above.)";
        }
        return true;
    }

    // ------------------------------------------------------------------- data

    /// <summary>Created from the code defaults if missing; never retuned.</summary>
    static ManaConfig EnsureManaConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<ManaConfig>(ManaConfigPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<ManaConfig>();
            AssetDatabase.CreateAsset(config, ManaConfigPath);
        }
        return config;
    }

    /// <summary>Created from the code defaults if missing. An existing asset is
    /// only topped up: a blank orb prefab is filled, nothing else is touched.</summary>
    static SpellDefinition EnsureSpell(MagicOrb orbPrefab)
    {
        var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(SpellPath);
        bool created = spell == null;
        if (created) spell = ScriptableObject.CreateInstance<SpellDefinition>();

        var so = new SerializedObject(spell);
        var prefabProp = so.FindProperty("orbPrefab");
        if (prefabProp == null)
            Debug.LogError("SpellDefinition has no serialized 'orbPrefab' — the orb is not assigned.");
        else if (prefabProp.objectReferenceValue == null && orbPrefab != null)
            prefabProp.objectReferenceValue = orbPrefab;
        bool changed = so.ApplyModifiedPropertiesWithoutUndo();

        if (created) AssetDatabase.CreateAsset(spell, SpellPath);
        else if (changed) EditorUtility.SetDirty(spell);
        return spell;
    }

    // ------------------------------------------------------------------ scene

    static Transform EnsureOrigin(Transform player)
    {
        var origin = player.Find(OriginName);
        if (origin != null) return origin;

        origin = new GameObject(OriginName).transform;
        origin.SetParent(player, false);
        origin.localPosition = OriginLocalPosition;
        origin.localRotation = Quaternion.identity;
        return origin;
    }

    /// <summary>
    /// Everything, minus any layer that only the player's own colliders use.
    /// The caster already skips its own colliders, so this is a cheap first
    /// filter rather than the safety net — and it never masks out a layer the
    /// world also uses, or the crosshair would see through those walls. The
    /// sandbox player sits on Default today, so this is ~0.
    /// </summary>
    static int AimMaskFor(GameObject player)
    {
        int playerLayers = 0;
        // CharacterController is a Collider, so the capsule counts too.
        foreach (var c in player.GetComponentsInChildren<Collider>(true))
            playerLayers |= 1 << c.gameObject.layer;
        playerLayers &= ~1;                         // Default is never dedicated
        if (playerLayers == 0) return ~0;

        foreach (var c in Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!c.transform.IsChildOf(player.transform))
                playerLayers &= ~(1 << c.gameObject.layer);
        return ~playerLayers;
    }

    /// <summary>
    /// The FightHUD canvas, found the way NewtonFightSetup finds it (so both
    /// menus agree on which canvas is meant), then among the scene's inactive
    /// roots; created with NewtonFightSetup.BuildHud's settings if absent.
    /// </summary>
    static GameObject FindOrCreateHud(Scene scene, out bool created)
    {
        created = false;
        var hud = GameObject.Find(HudName);
        if (hud != null) return hud;

        foreach (var root in scene.GetRootGameObjects())
            if (root.name == HudName) return root;

        created = true;
        hud = new GameObject(HudName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        if (hud.scene != scene) SceneManager.MoveGameObjectToScene(hud, scene);
        hud.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = hud.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        return hud;
    }

    // ---------------------------------------------------------------- helpers

    static void Set(Object target, params (string field, Object value)[] refs)
    {
        var so = new SerializedObject(target);
        foreach (var (field, value) in refs)
        {
            var p = so.FindProperty(field);
            if (p == null) Debug.LogError($"Setup Magic: {target.GetType().Name} has no serialized '{field}'.");
            else p.objectReferenceValue = value;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    /// <summary>Assigns only when the field is empty (or points at a deleted
    /// asset), so a hand-picked asset survives a re-run.</summary>
    static void FillIfBlank(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"Setup Magic: {target.GetType().Name} has no serialized '{field}'.");
            return;
        }
        if (p.objectReferenceValue != null) return;
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void SetMask(Object target, string field, int mask)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"Setup Magic: {target.GetType().Name} has no serialized '{field}'.");
            return;
        }
        p.intValue = mask;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
