using System.Collections.Generic;
using System.Linq;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat.Presentation;
using Pickleball.Sandbox.Inventory;
using Pickleball.Sandbox.Movement;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Tools ▸ Pickleball ▸ Setup Weapon Carry. Gives a big weapon its own rest pose for the
/// weapon arm, idempotently:
///
///  1. <b>Poses</b> (<c>Animation/Chibi/Carry/</c>): the ruler's standing and moving low
///     trail, authored by <see cref="WeaponCarryPoseBuilder"/> from its specs. Built only
///     when missing; <b>Tools ▸ Pickleball ▸ Rebuild Carry Poses</b> re-solves them after a
///     spec change (in place, GUIDs kept).
///  2. <b>Data:</b> <c>Weapon_Ruler</c>'s carry fields, filled by path only while blank
///     (after that the asset is the source of truth). <c>Weapon_Compass</c> has none.
///  3. <b>Controller:</b> the Carry layer on ChibiLocomotion.controller (<see cref="Wire"/>).
///  4. <b>Player:</b> <see cref="WeaponCarryPose"/> on the player, wired to the equipper,
///     MeleeCombat, the Animator and the MovementMotor (<see cref="AttachToPlayer"/>).
///
/// <para><b>The layer</b> (<see cref="Layer"/>): Override, weight 0 (faded by
/// WeaponCarryPose), no IK pass, masked to the right arm only
/// (<c>Carry/RightArm.mask</c>). It sits directly under Gesture, so the order is Base,
/// AttackBody, Carry, Gesture, Attack: the hook throw and every swing override it, and it
/// overrides only the base layer's right arm. One state per weapon with a carry pose,
/// named after its standing clip; a weapon with a moving pose too gets a 1D blend tree on
/// Speed (standing at 0, moving at the base layer's walk threshold), as the base layer
/// blends idle and walk. An empty None state is the default. This script is the layer's
/// only owner, so states no weapon names any more are removed.</para>
///
/// <para><b>Who keeps it.</b> <see cref="Wire"/> is called by
/// <c>SwordAbilitySetup.WireAttackStatesFromAssets</c>, right after the AttackBody layer, so
/// Setup Weapon, Setup Sword Abilities and Rebuild Chibi Animator all keep the layer.
/// <see cref="AttachToPlayer"/> is called at the end of <c>InventorySetup.AttachToPlayer</c>,
/// so Setup Inventory, Setup Chibi Sandbox and Build Training Stage add the component.</para>
///
/// Marks the scene dirty and never saves it; refuses in Play.
/// </summary>
public static class WeaponCarrySetup
{
    /// <summary>Name of the carry layer. WeaponCarryPose.carryLayer must match.</summary>
    public const string Layer = "Carry";

    const string ControllerPath = "Assets/_Project/Animation/Controllers/ChibiLocomotion.controller";
    const string CarryDir = "Assets/_Project/Animation/Chibi/Carry";
    const string MaskPath = CarryDir + "/RightArm.mask";
    const string WeaponsDir = "Assets/_Project/ScriptableObjects/Inventory";
    const string RulerWeaponPath = WeaponsDir + "/Weapons/Weapon_Ruler.asset";
    const string NoneState = "None";
    const string SpeedParameter = "Speed";

    /// <summary>The ruler's carry weight: the pose replaces the clips' arm outright.</summary>
    const float RulerCarryWeight = 1f;

    /// <summary>
    /// The ruler's carry fade, s. A 1 m blade needs a little longer than a light sword to
    /// settle without looking flung; 0.3 s still lands before a walk's second step.
    /// </summary>
    const float RulerCarryBlend = 0.3f;

    // ------------------------------------------------------------------- menu

    [MenuItem("Tools/Pickleball/Setup Weapon Carry")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Setup Weapon Carry edits assets and the scene — exit Play first.");
            return;
        }
        var player = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (player == null)
        {
            EnsureAssets();
            WireController();
            AssetDatabase.SaveAssets();
            Debug.LogWarning("Weapon carry poses and the Carry layer are built, but there is no " +
                             "ChibiLocomotion in the open scene to wire. Open ChibiSandbox and run this again.");
            return;
        }
        AttachToPlayer(player.gameObject);
        AssetDatabase.SaveAssets();
    }

    [MenuItem("Tools/Pickleball/Rebuild Carry Poses")]
    public static void RebuildPoses()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Rebuild Carry Poses edits assets — exit Play first.");
            return;
        }
        BuildPose(WeaponCarryPoseBuilder.RulerCarryStanding, force: true);
        BuildPose(WeaponCarryPoseBuilder.RulerCarryMoving, force: true);
        WireController();
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Everything the menu does for one player: poses and data (built if missing), the
    /// Carry layer, and the component. Marks the scene dirty and saves nothing. Called
    /// at the end of InventorySetup.AttachToPlayer, so every inventory chain wires it.
    /// </summary>
    public static void AttachToPlayer(GameObject player)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || player == null) return;
        EnsureAssets();
        WireController();

        var animator = player.GetComponentInChildren<Animator>();
        var equipper = player.GetComponent<WeaponEquipper>();
        var melee = player.GetComponent<MeleeCombat>();
        var motor = player.GetComponent<MovementMotor>();
        var warnings = new List<string>();
        if (equipper == null) warnings.Add("no WeaponEquipper (run Setup Inventory), so no weapon ever shows its carry");
        if (melee == null) warnings.Add("no MeleeCombat (run Setup Weapon)");
        if (motor == null) warnings.Add("no MovementMotor (run Setup Movement), so the carry never fades out for a move");

        if (!player.TryGetComponent(out WeaponCarryPose carry)) carry = player.AddComponent<WeaponCarryPose>();
        var so = new SerializedObject(carry);
        so.FindProperty("equipper").objectReferenceValue = equipper;
        so.FindProperty("combat").objectReferenceValue = melee;
        so.FindProperty("animator").objectReferenceValue = animator;
        so.FindProperty("movementSource").objectReferenceValue = motor;
        so.FindProperty("carryLayer").stringValue = Layer;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(player);
        EditorSceneManager.MarkSceneDirty(player.scene);
        if (warnings.Count > 0) Debug.LogWarning("Setup Weapon Carry: " + string.Join("; ", warnings) + ".");
        Debug.Log($"Weapon carry set up on {player.name}. SAVE THE SCENE to keep the wiring.");
    }

    // ----------------------------------------------------------------- assets

    /// <summary>Builds missing pose clips and fills the ruler's blank carry fields.</summary>
    public static void EnsureAssets()
    {
        var stand = BuildPose(WeaponCarryPoseBuilder.RulerCarryStanding, force: false);
        var move = BuildPose(WeaponCarryPoseBuilder.RulerCarryMoving, force: false);

        var ruler = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(RulerWeaponPath);
        if (ruler == null)
        {
            Debug.LogWarning($"Setup Weapon Carry: {RulerWeaponPath} is missing (run Setup Inventory); " +
                             "the ruler gets its carry on the next run.");
            return;
        }
        if (ruler.CarryPose != null) return;    // seeded once; the asset is the source of truth now
        ruler.ConfigureCarry(stand, move, RulerCarryWeight, RulerCarryBlend);
        EditorUtility.SetDirty(ruler);
    }

    static AnimationClip BuildPose(WeaponCarryPoseBuilder.Spec spec, bool force)
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(spec.ClipPath);
        if (existing != null && !force) return existing;
        var clip = WeaponCarryPoseBuilder.Solve(spec, write: true, out _);
        return clip != null ? clip : existing;
    }

    // ------------------------------------------------------------- controller

    static void WireController()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"Controller not found: {ControllerPath}");
            return;
        }
        Wire(controller);
    }

    /// <summary>
    /// Brings the Carry layer on <paramref name="controller"/> in line with the weapon
    /// assets. Running it twice gives the same controller. Saves nothing; the caller does.
    /// Builds no pose: a weapon whose clip doesn't exist yet simply gets no state.
    /// </summary>
    public static void Wire(AnimatorController controller)
    {
        if (controller == null) return;

        // One state per standing clip. The first weapon wins.
        var poses = new SortedDictionary<string, WeaponDefinition>(System.StringComparer.Ordinal);
        foreach (var guid in AssetDatabase.FindAssets("t:WeaponDefinition", new[] { WeaponsDir }))
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (weapon == null || weapon.CarryPose == null) continue;
            string name = weapon.CarryPose.name;
            if (name == NoneState || poses.ContainsKey(name)) continue;
            poses[name] = weapon;
        }

        var layer = EnsureLayer(controller);
        var sm = layer.stateMachine;
        bool hasSpeed = controller.parameters.Any(p => p.name == SpeedParameter
                                                       && p.type == AnimatorControllerParameterType.Float);
        float walk = WalkThreshold(controller);

        foreach (var (stateName, weapon) in poses)
        {
            var state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName);
            if (state == null)
                state = sm.AddState(stateName, new Vector3(300f, 60f * (sm.states.Length + 1), 0f));
            var moving = hasSpeed ? weapon.CarryPoseMoving : null;
            if (moving == null)
            {
                DestroyTree(state.motion as BlendTree);
                state.motion = weapon.CarryPose;
            }
            else
            {
                if (!(state.motion is BlendTree tree))
                {
                    tree = new BlendTree { hideFlags = HideFlags.HideInHierarchy };
                    AssetDatabase.AddObjectToAsset(tree, controller);
                    state.motion = tree;
                }
                tree.name = stateName;
                tree.blendType = BlendTreeType.Simple1D;
                tree.blendParameter = SpeedParameter;
                tree.useAutomaticThresholds = false;
                tree.children = new[]
                {
                    new ChildMotion { motion = weapon.CarryPose, threshold = 0f, timeScale = 1f },
                    new ChildMotion { motion = moving, threshold = walk, timeScale = 1f },
                };
            }
            state.speed = 1f;
            state.iKOnFeet = false;            // arm-only layer: the feet are the base layer's
            state.writeDefaultValues = true;
        }

        // This layer mirrors the weapons exactly: drop anything else but None.
        foreach (var child in sm.states.ToArray())
        {
            if (child.state.name == NoneState || poses.ContainsKey(child.state.name)) continue;
            DestroyTree(child.state.motion as BlendTree);
            sm.RemoveState(child.state);
        }

        EditorUtility.SetDirty(controller);
        Debug.Log($"Weapon carry: '{Layer}' layer holds {poses.Count} pose(s)" +
                  (poses.Count > 0 ? $" ({string.Join(", ", poses.Keys)})." : "."));
    }

    static void DestroyTree(BlendTree tree)
    {
        if (tree != null && AssetDatabase.IsSubAsset(tree)) Object.DestroyImmediate(tree, true);
    }

    /// <summary>The base layer's idle→walk threshold (Speed, m/s), so the carry blends
    /// from standing to moving exactly as the legs do. 1 if there is no such tree.</summary>
    static float WalkThreshold(AnimatorController controller)
    {
        var loco = controller.layers[0].stateMachine.states.Select(s => s.state)
            .FirstOrDefault(s => s.name == "Locomotion");
        if (loco != null && loco.motion is BlendTree tree)
            foreach (var child in tree.children)
                if (child.motion != null && child.motion.name == "walk" && child.threshold > 0f)
                    return child.threshold;
        return 1f;
    }

    /// <summary>The Carry layer, created on first use, placed directly under Gesture.</summary>
    static AnimatorControllerLayer EnsureLayer(AnimatorController controller)
    {
        var mask = EnsureMask();
        var layers = controller.layers.ToList();
        int index = layers.FindIndex(l => l.name == Layer);
        AnimatorControllerLayer layer;
        if (index < 0)
        {
            var sm = new AnimatorStateMachine { name = Layer, hideFlags = HideFlags.HideInHierarchy };
            AssetDatabase.AddObjectToAsset(sm, controller);
            var none = sm.AddState(NoneState);
            none.iKOnFeet = false;
            sm.defaultState = none;
            layer = new AnimatorControllerLayer { name = Layer, stateMachine = sm };
        }
        else
        {
            layer = layers[index];
            layers.RemoveAt(index);
        }
        // Settings a hand edit could have drifted; the layer is ours.
        layer.avatarMask = mask;
        layer.blendingMode = AnimatorLayerBlendingMode.Override;
        layer.defaultWeight = 0f;               // faded by WeaponCarryPose
        layer.iKPass = false;
        layer.syncedLayerIndex = -1;

        // Under Gesture (the hook throw) and Attack, so both override it; above the base
        // layer and AttackBody, whose right arm it replaces.
        int at = layers.FindIndex(l => l.name == "Gesture");
        if (at < 0) at = layers.FindIndex(l => l.name == WeaponSetup.AttackLayer);
        if (at < 0) at = layers.Count;
        layers.Insert(Mathf.Max(1, Mathf.Min(at, layers.Count)), layer);
        controller.layers = layers.ToArray();
        return controller.layers.First(l => l.name == Layer);
    }

    /// <summary>The right arm alone (shoulder to hand). Not the fingers: FingerCurl closes
    /// the fist after the Animator, and the base clips already hold a grip.</summary>
    static AvatarMask EnsureMask()
    {
        ChibiSandboxSetup.EnsureFolderPublic(CarryDir);
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(MaskPath);
        bool created = mask == null;
        if (created) mask = new AvatarMask();
        bool changed = created;
        foreach (AvatarMaskBodyPart part in System.Enum.GetValues(typeof(AvatarMaskBodyPart)))
        {
            if (part == AvatarMaskBodyPart.LastBodyPart) continue;
            bool on = part == AvatarMaskBodyPart.RightArm;
            if (mask.GetHumanoidBodyPartActive(part) == on) continue;
            mask.SetHumanoidBodyPartActive(part, on);
            changed = true;
        }
        if (created) AssetDatabase.CreateAsset(mask, MaskPath);
        else if (changed) EditorUtility.SetDirty(mask);
        return mask;
    }
}
