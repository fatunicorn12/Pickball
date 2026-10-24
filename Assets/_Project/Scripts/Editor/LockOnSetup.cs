using Pickleball.Sandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools ▸ Pickleball ▸ Setup Lock-On. Adds <see cref="LockOnTargeting"/> to the
/// chibi and wires it into the orbit camera and locomotion.
///
/// Enemies need nothing: anything with a <see cref="Health"/> that is not the
/// player is lockable. Safe to re-run, and independent of the chibi and Newton
/// setups, so rebuilding either does not lose it — as long as this runs after.
/// </summary>
public static class LockOnSetup
{
    const string VfxDir = "Assets/_Project/Art/VFX";

    [MenuItem("Tools/Pickleball/Setup Lock-On")]
    public static void Run()
    {
        var player = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (player == null)
        {
            Debug.LogError("No ChibiLocomotion in the scene — open ChibiSandbox " +
                           "or run the chibi setup first.");
            return;
        }

        var lockOn = player.GetComponent<LockOnTargeting>();
        if (lockOn == null) lockOn = player.gameObject.AddComponent<LockOnTargeting>();

        ChibiSandboxSetup.EnsureFolderPublic(VfxDir);
        // White, so the per-state colour set on the component comes through as-is.
        var mat = SandboxVfxBuilder.BuildGlowMaterial(VfxDir, "LockOnReticle", Color.white);

        var cam = Camera.main;
        var orbit = cam != null ? cam.GetComponent<SandboxOrbitCamera>() : null;

        var lso = new SerializedObject(lockOn);
        lso.FindProperty("reticleMaterial").objectReferenceValue = mat;
        if (cam != null) lso.FindProperty("aimSource").objectReferenceValue = cam.transform;
        if (orbit != null) lso.FindProperty("cameraRig").objectReferenceValue = orbit;
        lso.ApplyModifiedPropertiesWithoutUndo();

        var pso = new SerializedObject(player);
        pso.FindProperty("lockOn").objectReferenceValue = lockOn;
        pso.ApplyModifiedPropertiesWithoutUndo();

        if (orbit != null)
        {
            var cso = new SerializedObject(orbit);
            cso.FindProperty("lockOn").objectReferenceValue = lockOn;
            cso.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            Debug.LogWarning("Main Camera has no SandboxOrbitCamera — the lock " +
                             "will target, but the camera will not follow it.");
        }

        EditorUtility.SetDirty(player.gameObject);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        Debug.Log($"Lock-on set up on {player.name}. Tab / middle mouse / right " +
                  "trigger toggles it; flick the mouse or right stick to switch.");
    }
}
