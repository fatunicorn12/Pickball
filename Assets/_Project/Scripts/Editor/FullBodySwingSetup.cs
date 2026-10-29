using System.Collections.Generic;
using System.Linq;
using Pickleball.Sandbox.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Tools ▸ Pickleball ▸ Setup Full-Body Swings. Builds the full-body AttackBody layer
/// on ChibiLocomotion.controller from the step assets, idempotently. Setup Sword
/// Abilities, Setup Weapon and Rebuild Chibi Animator call <see cref="Wire"/> too, via
/// <see cref="SwordAbilitySetup.WireAttackStatesFromAssets"/>, so no rebuild drops it.
///
/// <para><b>The layer:</b></para>
/// <list type="bullet">
/// <item>Index 1, directly above the base layer and under Gesture and Attack.</item>
/// <item>Override, no mask, weight 0, no IK pass.</item>
/// <item>An empty None default state, and one state per step flagged
/// <see cref="ComboStepDefinition.FullBodyWhenStanding"/>. Each has the step's state
/// name, clip and speed, and Foot IK on.</item>
/// </list>
///
/// <para><b>Why under the masked layer:</b> the Attack layer above keeps the arms, spine
/// and head exactly as they are today. This layer only contributes the legs and hips. A
/// full-body override replaces the base layer's IK pose, so ChibiFootIK's standing plant
/// cannot pin the feet against the clip's footwork. SwordAnimatorDriver fades the weight.
/// Nothing else reads this layer, and every other script finds layers by name.</para>
///
/// <para><b>The flagged clip's import:</b> its Root Transform Position (Y) is baked into
/// the pose, as the locomotion clips' is. Root motion is off, so an unbaked clip's crouch
/// went into root motion and was thrown away. Full-body, that leaves the knees bent under
/// a body that never comes down, and the feet float. The masked layer never reads the hip
/// height, so the change is invisible there. XZ stays unbaked, so the step's scripted
/// lunge is still what moves him.</para>
///
/// <para>The menu changes only the controller and the flagged clips' import settings,
/// never a scene or prefab. States for steps that are no longer flagged are removed: this
/// script is the layer's only owner.</para>
/// </summary>
public static class FullBodySwingSetup
{
    /// <summary>Name of the full-body layer. SwordAnimatorDriver.bodyLayer must match.</summary>
    public const string Layer = "AttackBody";

    const string ControllerPath = "Assets/_Project/Animation/Controllers/ChibiLocomotion.controller";
    const string StepsDir = "Assets/_Project/ScriptableObjects/Combat";
    const string NoneState = "None";

    [MenuItem("Tools/Pickleball/Setup Full-Body Swings")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Setup Full-Body Swings edits the controller and clip imports — exit Play first.");
            return;
        }
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"Controller not found: {ControllerPath}");
            return;
        }
        Wire(controller);
        AssetDatabase.SaveAssets();
    }

    /// <summary>
    /// Brings the AttackBody layer on <paramref name="controller"/> in line with the step
    /// assets. Running it twice gives the same controller. Saves nothing; the caller does.
    /// </summary>
    public static void Wire(AnimatorController controller)
    {
        if (controller == null) return;

        // One state per flagged state name. The first asset wins, as on the Attack layer.
        var flagged = new Dictionary<string, ComboStepDefinition>();
        foreach (var guid in AssetDatabase.FindAssets("t:ComboStepDefinition", new[] { StepsDir }))
        {
            var step = AssetDatabase.LoadAssetAtPath<ComboStepDefinition>(AssetDatabase.GUIDToAssetPath(guid));
            if (step == null || !step.FullBodyWhenStanding || step.Clip == null) continue;
            if (string.IsNullOrEmpty(step.AnimatorState) || step.AnimatorState == NoneState) continue;
            if (!flagged.ContainsKey(step.AnimatorState)) flagged[step.AnimatorState] = step;
        }

        foreach (var step in flagged.Values) EnsureRootHeightBaked(step.Clip);

        var layer = EnsureLayer(controller);
        var sm = layer.stateMachine;

        foreach (var kv in flagged.OrderBy(kv => kv.Key))
        {
            string stateName = kv.Key;
            var step = kv.Value;
            var state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == stateName);
            if (state == null)
                state = sm.AddState(stateName, new Vector3(300f, 60f * (sm.states.Length + 1), 0f));
            state.motion = step.Clip;
            state.speed = step.PlaybackSpeed;
            state.iKOnFeet = true;          // the clip's own foot goals: no retarget foot slip
            state.writeDefaultValues = true;
        }

        // This layer mirrors the flagged steps exactly: drop anything else but None.
        foreach (var child in sm.states.ToArray())
            if (child.state.name != NoneState && !flagged.ContainsKey(child.state.name))
                sm.RemoveState(child.state);

        EditorUtility.SetDirty(controller);
        Debug.Log($"Full-body swings: '{Layer}' layer holds {flagged.Count} state(s)" +
                  (flagged.Count > 0 ? $" ({string.Join(", ", flagged.Keys.OrderBy(k => k))})." : "."));
    }

    /// <summary>The AttackBody layer at index 1, created on first use.</summary>
    static AnimatorControllerLayer EnsureLayer(AnimatorController controller)
    {
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
            layer = new AnimatorControllerLayer
            {
                name = Layer,
                stateMachine = sm,
                avatarMask = null,              // whole body; Attack and Gesture above mask it
                blendingMode = AnimatorLayerBlendingMode.Override,
                defaultWeight = 0f,             // faded by SwordAnimatorDriver
                iKPass = false,
            };
        }
        else
        {
            layer = layers[index];
            layers.RemoveAt(index);
            // Settings a hand edit could have drifted; the layer is ours.
            layer.avatarMask = null;
            layer.blendingMode = AnimatorLayerBlendingMode.Override;
            layer.defaultWeight = 0f;
            layer.iKPass = false;
            layer.syncedLayerIndex = -1;
        }

        // Directly above the base layer, so the masked layers keep the upper body.
        layers.Insert(Mathf.Min(1, layers.Count), layer);
        controller.layers = layers.ToArray();
        return controller.layers.First(l => l.name == Layer);
    }

    /// <summary>
    /// Bakes Root Transform Position (Y) into the pose for the clip's FBX, as the
    /// locomotion clips do, so a full-body crouch comes down instead of floating. Reimports
    /// only when something changes. <see cref="WeaponSetup"/>'s ImportAttackClips uses the
    /// same setting, so the two never undo each other.
    /// </summary>
    static void EnsureRootHeightBaked(AnimationClip clip)
    {
        string path = AssetDatabase.GetAssetPath(clip);
        if (!(AssetImporter.GetAtPath(path) is ModelImporter importer)) return;
        var clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        bool changed = false;
        foreach (var c in clips)
        {
            if (c.lockRootHeightY) continue;
            c.lockRootHeightY = true;
            changed = true;
        }
        if (!changed) return;
        importer.clipAnimations = clips;
        importer.SaveAndReimport();
        Debug.Log($"Full-body swings: baked root height into the pose for {path}.");
    }
}
