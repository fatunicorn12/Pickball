using Pickleball.Sandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools ▸ Pickleball ▸ Spacetime ▸ Add To Sandbox / Remove From Sandbox.
///
/// <b>Add</b> wires the 4D view into the open scene (ChibiSandbox, or any scene
/// with the chibi): a "Spacetime" object carrying a <see cref="SpacetimeView"/>
/// bound to the settings asset and the chibi's controller, and a
/// <see cref="WorldlineRecorder"/> on Newton if he is in the scene. Apples are
/// recorded by the view at runtime, so the Apple prefab is not touched, and
/// neither is Chibi.prefab.
///
/// It does nothing at all unless Spacetime is switched on, so it cannot change
/// a scene the user has not opted in. It is idempotent: running it twice gives
/// the same scene as running it once. It marks the scene dirty but never saves
/// it. Saving a shared scene is the caller's decision.
///
/// <b>Remove</b> takes every view and recorder back out, whatever the switch says.
/// </summary>
public static class SpacetimeSandboxSetup
{
    const string RootName = "Spacetime";

    [MenuItem("Tools/Pickleball/Spacetime/Add To Sandbox", priority = 20)]
    public static void Add()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Leave Play mode first; changes made during play are thrown away.");
            return;
        }
        if (!SpacetimeSettingsMenu.IsEnabled())
        {
            Debug.Log("Spacetime is OFF, so the scene was left untouched. Run Tools ▸ " +
                      "Pickleball ▸ Spacetime ▸ Enable first.");
            return;
        }

        var chibi = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (chibi == null)
        {
            Debug.LogError("No ChibiLocomotion in the open scene. Open ChibiSandbox first.");
            return;
        }
        var settings = SpacetimeSettingsMenu.LoadOrCreateSettings();

        bool created = false;
        var view = Object.FindFirstObjectByType<SpacetimeView>(FindObjectsInactive.Include);
        if (view == null)
        {
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Add Spacetime To Sandbox");
            MoveToChibiScene(root, chibi);
            view = Undo.AddComponent<SpacetimeView>(root);
            created = true;
        }

        var vso = new SerializedObject(view);
        vso.FindProperty("settings").objectReferenceValue = settings;
        vso.FindProperty("player").objectReferenceValue = chibi.GetComponent<CharacterController>();
        vso.ApplyModifiedProperties();

        string newtonNote = "no Newton in the scene";
        var newton = Object.FindFirstObjectByType<NewtonBoss>();
        if (newton != null)
        {
            var recorder = newton.GetComponent<WorldlineRecorder>();
            bool addedRecorder = recorder == null;
            if (addedRecorder) recorder = Undo.AddComponent<WorldlineRecorder>(newton.gameObject);
            var rso = new SerializedObject(recorder);
            rso.FindProperty("settings").objectReferenceValue = settings;
            // Chest height rather than his feet, so his line reads as his.
            rso.FindProperty("traceOffset").vector3Value = new Vector3(0f, 1f, 0f);
            rso.ApplyModifiedProperties();
            newtonNote = addedRecorder ? "recorder added to Newton" : "Newton already recorded";
        }

        EditorSceneManager.MarkSceneDirty(view.gameObject.scene);
        Debug.Log($"Spacetime {(created ? "added to" : "already in")} '{view.gameObject.scene.name}' " +
                  $"({newtonNote}). Scene marked dirty, not saved.", view);
    }

    [MenuItem("Tools/Pickleball/Spacetime/Remove From Sandbox", priority = 21)]
    public static void Remove()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Leave Play mode first; changes made during play are thrown away.");
            return;
        }

        int views = 0, recorders = 0;
        foreach (var view in Object.FindObjectsByType<SpacetimeView>(FindObjectsInactive.Include,
                                                                       FindObjectsSortMode.None))
        {
            var go = view.gameObject;
            EditorSceneManager.MarkSceneDirty(go.scene);
            // The object only exists to carry the view, so it goes too.
            if (go.name == RootName && go.GetComponents<Component>().Length == 2)
                Undo.DestroyObjectImmediate(go);
            else
                Undo.DestroyObjectImmediate(view);
            views++;
        }
        foreach (var recorder in Object.FindObjectsByType<WorldlineRecorder>(FindObjectsInactive.Include,
                                                                               FindObjectsSortMode.None))
        {
            // Demo movers in the lab scene keep theirs. They are the point of it.
            if (recorder.GetComponent<SpacetimeDemoMover>() != null) continue;
            EditorSceneManager.MarkSceneDirty(recorder.gameObject.scene);
            Undo.DestroyObjectImmediate(recorder);
            recorders++;
        }
        Debug.Log($"Removed {views} SpacetimeView(s) and {recorders} recorder(s). " +
                  "Scenes marked dirty, not saved.");
    }

    /// <summary>With several scenes open, new objects land in the active one,
    /// which may not be the chibi's. Put the view next to the chibi.</summary>
    static void MoveToChibiScene(GameObject root, ChibiLocomotion chibi)
    {
        if (root.scene != chibi.gameObject.scene)
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, chibi.gameObject.scene);
    }
}
