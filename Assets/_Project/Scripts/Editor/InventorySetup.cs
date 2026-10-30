using System.Collections.Generic;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using Pickleball.Sandbox.Hands;
using Pickleball.Sandbox.Inventory;
using Pickleball.Sandbox.Magic;
using Pickleball.Sandbox.Weapon;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Tools ▸ Pickleball ▸ Setup Inventory. Gives the player the Weapons / Magic /
/// Key Items inventory and weapon switching, idempotently:
///
///  1. <b>Data</b> (<c>ScriptableObjects/Inventory/</c>): <c>InventoryConfig</c>,
///     <c>Weapons/Weapon_Compass</c> and <c>Weapons/Weapon_Ruler</c>, one
///     <c>KeyItems/Key_*</c> per <see cref="AbilityKind"/> (named by
///     <see cref="KeyItemNames"/>: Tether is the Pendulum), and one
///     <c>Magic/Magic_*</c> per spell asset in <c>ScriptableObjects/Magic/</c>.
///     Each is created with its seed only when missing; after that the asset is
///     the source of truth. A weapon's references (held prefab, moveset,
///     clearance profile) are filled by their agreed paths <b>only when blank</b>,
///     and a path that does not exist yet is left null with a warning: the
///     ruler's prefab, moveset and profile are built on other branches, and a
///     re-run after they land fills them in.
///  2. <b>Player</b>: adds <see cref="Inventory"/>, <see cref="WeaponEquipper"/>,
///     <see cref="InventoryInput"/>, <see cref="InventoryMenu"/> (and the
///     <see cref="PlayerInputGate"/> if no other setup has) and a hidden
///     "StowedWeapons" child, and wires them. Starting items (both weapons) and
///     the starting weapon (the compass) are filled only when blank; the
///     catalogs and component references are rewritten every run. Once
///     Held_Compass exists and Weapon_Compass points at it, the prefab's own
///     compass ("WeaponVisual") is hidden; the equipper still falls back to it
///     for any weapon without a held prefab.
///  3. <b>HUD</b>: destroys and rebuilds the "InventoryHUD" canvas
///     (<see cref="InventoryHudBuilder"/>): the menu and the equipped-weapon
///     readout.
///
/// Runs at the end of Setup Chibi Sandbox and Build Training Stage (one line
/// each). Run it by hand after Setup Weapon (which rebuilds the socket and
/// markers it points at) and after the ruler's prefab, moveset or clearance
/// profile land. Marks the scene dirty and never saves it; refuses in Play.
/// The components are scene overrides, not part of Chibi.prefab.
/// </summary>
public static class InventorySetup
{
    const string DataDir = "Assets/_Project/ScriptableObjects/Inventory";
    const string WeaponsDir = DataDir + "/Weapons";
    const string KeyItemsDir = DataDir + "/KeyItems";
    const string MagicDir = DataDir + "/Magic";
    const string ConfigPath = DataDir + "/InventoryConfig.asset";
    const string CompassPath = WeaponsDir + "/Weapon_Compass.asset";
    const string RulerPath = WeaponsDir + "/Weapon_Ruler.asset";

    // Agreed paths of the assets other branches build (see the inventory brief).
    const string HeldCompassPath = "Assets/_Project/Prefabs/Weapons/Held_Compass.prefab";
    const string HeldRulerPath = "Assets/_Project/Prefabs/Weapons/Held_Ruler.prefab";
    const string CompassMovesetPath = "Assets/_Project/ScriptableObjects/Combat/Moveset_Compass.asset";
    const string RulerMovesetPath = "Assets/_Project/ScriptableObjects/Combat/Moveset_Ruler.asset";
    const string CompassClearancePath = "Assets/_Project/ScriptableObjects/Weapon/ChibiWeaponClearance.asset";
    const string RulerClearancePath = "Assets/_Project/ScriptableObjects/Weapon/RulerWeaponClearance.asset";
    const string SpellDir = "Assets/_Project/ScriptableObjects/Magic";

    const string StowName = "StowedWeapons";
    const string VisualName = "WeaponVisual";

    /// <summary>Compass hit-capsule radius (m): the radius MeleeCombat has always used.</summary>
    const float CompassRadius = 0.18f;

    /// <summary>
    /// Ruler hit-capsule radius (m). The blade is 20 cm wide and 6-8 cm thick:
    /// half its width plus a little forgiveness, wider than the compass so the
    /// heavy blade lands what it visibly sweeps through.
    /// </summary>
    const float RulerRadius = 0.25f;

    struct WeaponSeed
    {
        public string Path, Id, Name, Description, HeldPath, MovesetPath, ClearancePath;
        public float Radius;
    }

    static readonly WeaponSeed[] Weapons =
    {
        new()
        {
            Path = CompassPath, Id = "compass", Name = "Compass",
            Description = "A geometer's compass, its point honed to an edge. Light and quick: " +
                          "fast swings that chain into long combos.",
            HeldPath = HeldCompassPath, MovesetPath = CompassMovesetPath,
            ClearancePath = CompassClearancePath, Radius = CompassRadius,
        },
        new()
        {
            Path = RulerPath, Id = "ruler", Name = "Ruler Sword",
            Description = "A ruler forged into a buster blade, steel edge leading. Big, slow arcs " +
                          "that hit hard.",
            HeldPath = HeldRulerPath, MovesetPath = RulerMovesetPath,
            ClearancePath = RulerClearancePath, Radius = RulerRadius,
        },
    };

    /// <summary>Menu blurbs for the shipped spells, by asset name. Others get a generic line.</summary>
    static readonly Dictionary<string, string> SpellBlurbs = new()
    {
        { "ArcaneOrb", "A slow, glowing orb fired at the crosshair." },
        { "Fireball", "A fireball that sets whatever it hits burning." },
        { "Thunderbolt", "Calls lightning down on the crosshair after a short warning. Staggers." },
        { "FreezeZone", "A patch of frost on the ground that slows anything inside it." },
        { "Heal", "Hold fire and stand still to channel, restoring your health." },
    };

    const string SpellHowTo = " Aim with right mouse / LT, cast with left mouse / RT.";

    // ------------------------------------------------------------------- menu

    [MenuItem("Tools/Pickleball/Setup Inventory")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Setup Inventory edits assets and the scene — exit Play first.");
            return;
        }
        var player = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (player == null)
        {
            Debug.LogError("Setup Inventory: no ChibiLocomotion in the open scene — open ChibiSandbox " +
                           "or run Tools ▸ Pickleball ▸ Setup Chibi Sandbox first.");
            return;
        }
        AttachToPlayer(player.gameObject);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Everything the menu does for one player: data assets, components and
    /// wiring, HUD. Marks the scene dirty and saves nothing. The entry point for
    /// the Setup Chibi Sandbox and Build Training Stage chains.
    /// </summary>
    public static void AttachToPlayer(GameObject go)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Setup Inventory edits assets and the scene — exit Play first.");
            return;
        }
        if (go == null)
        {
            Debug.LogError("Setup Inventory: no player given — nothing to wire.");
            return;
        }

        var warnings = new List<string>();
        var assets = EnsureAssets(warnings);

        // ---- components ----------------------------------------------------
        // Explicit null checks: `??` on a Unity object skips the fake-null check.
        var gate = go.GetComponent<PlayerInputGate>();
        if (gate == null) gate = go.AddComponent<PlayerInputGate>();
        var input = go.GetComponent<InventoryInput>();
        if (input == null) input = go.AddComponent<InventoryInput>();
        var inventory = go.GetComponent<Inventory>();
        if (inventory == null) inventory = go.AddComponent<Inventory>();
        var equipper = go.GetComponent<WeaponEquipper>();
        if (equipper == null) equipper = go.AddComponent<WeaponEquipper>();
        var menu = go.GetComponent<InventoryMenu>();
        if (menu == null) menu = go.AddComponent<InventoryMenu>();

        // ---- inventory -----------------------------------------------------
        FillIfBlank(inventory, "config", assets.Config);
        FillArrayIfEmpty(inventory, "startingItems", new Object[] { assets.Compass, assets.Ruler });
        FillIfBlank(inventory, "startingWeapon", assets.Compass);
        SetArray(inventory, "keyItems", assets.KeyItems.ToArray());
        SetArray(inventory, "magicItems", assets.MagicItems.ToArray());
        var abilities = go.GetComponent<PlayerAbilities>();
        if (abilities == null)
            warnings.Add("no PlayerAbilities on the player — the Key Items tab shows only starting key items");
        var selector = go.GetComponent<SpellSelector>();
        if (selector == null)
            warnings.Add("no SpellSelector on the player yet (run Setup Magic) — the Magic tab finds it " +
                         "at runtime once it exists");
        Set(inventory, ("abilities", abilities), ("spells", selector), ("input", input), ("inputGate", gate));

        // ---- equipper ------------------------------------------------------
        var melee = go.GetComponent<MeleeCombat>();
        if (melee == null) warnings.Add("no MeleeCombat on the player (run Setup Weapon) — switching changes only the model");
        var grip = go.GetComponentInChildren<HandGrip>(true);
        Transform socket = grip != null ? grip.transform : null;
        Transform bladeBase = socket != null ? socket.Find("BladeBase") : null;
        Transform bladeTip = socket != null ? socket.Find("BladeTip") : null;
        if (grip == null)
            warnings.Add("no HandGrip under the player (run Setup Weapon Grip) — weapons cannot be put in the hand");
        else if (bladeBase == null || bladeTip == null)
            warnings.Add($"{socket.name} has no BladeBase / BladeTip children (run Setup Weapon) — the blade " +
                         "markers will not follow the weapon");
        WeaponClearance clearance = socket != null ? socket.GetComponent<WeaponClearance>() : null;
        if (clearance == null) clearance = go.GetComponentInChildren<WeaponClearance>(true);
        HeldItem fallback = null;
        var visual = socket != null ? socket.Find(VisualName) : null;
        if (visual != null) fallback = visual.GetComponent<HeldItem>();
        if (fallback == null && grip != null) fallback = grip.Held;
        var stow = EnsureStow(go.transform);

        Set(equipper, ("inventory", inventory), ("melee", melee), ("handGrip", grip), ("clearance", clearance),
            ("bladeBase", bladeBase), ("bladeTip", bladeTip), ("stowRoot", stow), ("fallbackItem", fallback));

        // The prefab's own compass gives way once the compass has a held prefab of its own.
        bool compassHasPrefab = assets.Compass != null && assets.Compass.HeldPrefab != null;
        if (visual != null && compassHasPrefab && visual.gameObject.activeSelf)
        {
            visual.gameObject.SetActive(false);
            EditorUtility.SetDirty(visual.gameObject);
        }

        // ---- menu ------------------------------------------------------------
        var orbit = Object.FindFirstObjectByType<SandboxOrbitCamera>();
        if (orbit == null) warnings.Add("no SandboxOrbitCamera in the scene — the view keeps turning while the menu is open");
        Set(menu, ("inventory", inventory), ("input", input), ("inputGate", gate), ("orbitCamera", orbit));

        // ---- HUD -------------------------------------------------------------
        InventoryHudBuilder.Build(go.scene, menu, equipper, assets.Config);
        WeaponCarrySetup.AttachToPlayer(go);   // the equipped weapon's carry pose

        EditorUtility.SetDirty(go);
        EditorSceneManager.MarkSceneDirty(go.scene);

        string summary = $"Inventory set up on {go.name}: {assets.KeyItems.Count} key items, " +
                         $"{assets.MagicItems.Count} magic items, weapons Compass " +
                         $"({Describe(assets.Compass)}) and Ruler ({Describe(assets.Ruler)}); " +
                         "I / Start opens the menu, X / Select equips the next weapon. " +
                         "SAVE THE SCENE (Ctrl+S) to keep the wiring.";
        if (warnings.Count > 0)
        {
            Debug.LogWarning("Setup Inventory: " + string.Join("; ", warnings) + ".");
            summary += $" ({warnings.Count} warning(s) above.)";
        }
        Debug.Log(summary);
    }

    // ------------------------------------------------------------------- data

    /// <summary>The assets one run produces.</summary>
    public sealed class Assets
    {
        public InventoryConfig Config;
        public WeaponDefinition Compass, Ruler;
        public readonly List<KeyItemDefinition> KeyItems = new();
        public readonly List<MagicItemDefinition> MagicItems = new();
    }

    /// <summary>
    /// Creates whatever is missing under ScriptableObjects/Inventory and fills
    /// blank weapon references by path. Never retunes an existing asset.
    /// </summary>
    public static Assets EnsureAssets(List<string> warnings)
    {
        ChibiSandboxSetup.EnsureFolderPublic(WeaponsDir);
        ChibiSandboxSetup.EnsureFolderPublic(KeyItemsDir);
        ChibiSandboxSetup.EnsureFolderPublic(MagicDir);

        var assets = new Assets();
        assets.Config = AssetDatabase.LoadAssetAtPath<InventoryConfig>(ConfigPath);
        if (assets.Config == null)
        {
            assets.Config = ScriptableObject.CreateInstance<InventoryConfig>();
            AssetDatabase.CreateAsset(assets.Config, ConfigPath);
        }

        assets.Compass = EnsureWeapon(Weapons[0], warnings);
        assets.Ruler = EnsureWeapon(Weapons[1], warnings);

        foreach (var kind in KeyItemNames.Order) assets.KeyItems.Add(EnsureKeyItem(kind));
        // Any ability added to the enum after the table was written still gets an item.
        foreach (AbilityKind kind in System.Enum.GetValues(typeof(AbilityKind)))
            if (System.Array.IndexOf(KeyItemNames.Order, kind) < 0) assets.KeyItems.Add(EnsureKeyItem(kind));

        foreach (var guid in AssetDatabase.FindAssets("t:SpellDefinition", new[] { SpellDir }))
        {
            var spell = AssetDatabase.LoadAssetAtPath<SpellDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (spell != null) assets.MagicItems.Add(EnsureMagicItem(spell));
        }
        return assets;
    }

    static WeaponDefinition EnsureWeapon(WeaponSeed seed, List<string> warnings)
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(seed.Path);
        if (weapon == null)
        {
            weapon = ScriptableObject.CreateInstance<WeaponDefinition>();
            weapon.SetIdentity(seed.Id, seed.Name, seed.Description);
            weapon.Configure(null, null, seed.Radius, null);
            AssetDatabase.CreateAsset(weapon, seed.Path);
        }

        var so = new SerializedObject(weapon);
        FillByPath<HeldItem>(so, "heldPrefab", seed.HeldPath, warnings, seed.Name);
        FillByPath<SwordMoveset>(so, "moveset", seed.MovesetPath, warnings, seed.Name);
        FillByPath<WeaponClearanceProfile>(so, "clearanceProfile", seed.ClearancePath, warnings, seed.Name);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(weapon);
        return weapon;
    }

    /// <summary>Fills a blank reference from <paramref name="path"/>; a missing asset is
    /// left null with a warning (it is being built elsewhere; a re-run fills it).</summary>
    static void FillByPath<T>(SerializedObject so, string field, string path, List<string> warnings, string owner)
        where T : Object
    {
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"Setup Inventory: {so.targetObject.GetType().Name} has no serialized '{field}'.");
            return;
        }
        if (p.objectReferenceValue != null) return;

        T value;
        if (typeof(Component).IsAssignableFrom(typeof(T)))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            value = prefab != null ? prefab.GetComponent(typeof(T)) as T : null;
            if (prefab != null && value == null)
                warnings.Add($"{path} exists but has no {typeof(T).Name}; {owner}'s {field} left empty");
        }
        else value = AssetDatabase.LoadAssetAtPath<T>(path);

        if (value == null)
        {
            if (AssetDatabase.LoadMainAssetAtPath(path) == null)
                warnings.Add($"{path} does not exist yet; {owner}'s {field} left empty (re-run after it lands)");
            return;
        }
        p.objectReferenceValue = value;
    }

    static KeyItemDefinition EnsureKeyItem(AbilityKind kind)
    {
        string path = $"{KeyItemsDir}/Key_{kind}.asset";
        var item = AssetDatabase.LoadAssetAtPath<KeyItemDefinition>(path);
        if (item != null) return item;
        var names = KeyItemNames.For(kind);
        item = ScriptableObject.CreateInstance<KeyItemDefinition>();
        item.SetIdentity(names.Id, names.DisplayName, names.Description);
        item.Configure(kind);
        AssetDatabase.CreateAsset(item, path);
        return item;
    }

    static MagicItemDefinition EnsureMagicItem(SpellDefinition spell)
    {
        string path = $"{MagicDir}/Magic_{spell.name}.asset";
        var item = AssetDatabase.LoadAssetAtPath<MagicItemDefinition>(path);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<MagicItemDefinition>();
            string blurb = SpellBlurbs.TryGetValue(spell.name, out var b) ? b : $"{spell.DisplayName}.";
            item.SetIdentity(spell.name.ToLowerInvariant(), spell.DisplayName, blurb + SpellHowTo);
            item.Configure(spell);
            AssetDatabase.CreateAsset(item, path);
        }
        else if (item.Spell == null)
        {
            item.Configure(spell);
            EditorUtility.SetDirty(item);
        }
        // The slot draws the spell's icon; copy it in only while the item has none.
        FillIfBlank(item, "icon", spell.Icon);
        return item;
    }

    // ------------------------------------------------------------------- scene

    static Transform EnsureStow(Transform player)
    {
        var stow = player.Find(StowName);
        if (stow == null)
        {
            var go = new GameObject(StowName);
            go.transform.SetParent(player, false);
            stow = go.transform;
        }
        if (stow.gameObject.activeSelf) stow.gameObject.SetActive(false);
        return stow;
    }

    static string Describe(WeaponDefinition weapon)
    {
        if (weapon == null) return "missing";
        var parts = new List<string>();
        if (weapon.HeldPrefab == null) parts.Add("no held prefab yet: shows the prefab's compass");
        if (weapon.Moveset == null) parts.Add("no moveset yet: swings the current one");
        if (weapon.ClearanceProfile == null) parts.Add("no clearance profile yet");
        return parts.Count == 0 ? "complete" : string.Join(", ", parts);
    }

    // ------------------------------------------------------------------- plumbing

    static void Set(Object target, params (string field, Object value)[] refs)
    {
        var so = new SerializedObject(target);
        foreach (var (field, value) in refs)
        {
            var p = so.FindProperty(field);
            if (p == null) Debug.LogError($"Setup Inventory: {target.GetType().Name} has no serialized '{field}'.");
            else p.objectReferenceValue = value;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    /// <summary>Assigns only when the field is empty, so a hand-picked value survives a re-run.</summary>
    static void FillIfBlank(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"Setup Inventory: {target.GetType().Name} has no serialized '{field}'.");
            return;
        }
        if (p.objectReferenceValue != null || value == null) return;
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    /// <summary>Writes the array only when it is empty (or all nulls), so a hand-arranged loadout survives.</summary>
    static void FillArrayIfEmpty(Object target, string field, Object[] values)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"Setup Inventory: {target.GetType().Name} has no serialized '{field}'.");
            return;
        }
        for (int i = 0; i < p.arraySize; i++)
            if (p.GetArrayElementAtIndex(i).objectReferenceValue != null) return;
        WriteArray(p, values);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void SetArray(Object target, string field, Object[] values)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"Setup Inventory: {target.GetType().Name} has no serialized '{field}'.");
            return;
        }
        WriteArray(p, values);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    static void WriteArray(SerializedProperty p, Object[] values)
    {
        int count = 0;
        foreach (var v in values) if (v != null) count++;
        p.arraySize = count;
        int i = 0;
        foreach (var v in values)
            if (v != null) p.GetArrayElementAtIndex(i++).objectReferenceValue = v;
    }
}
