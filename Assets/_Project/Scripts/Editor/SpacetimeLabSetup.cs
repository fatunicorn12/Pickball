using Pickleball;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools ▸ Pickleball ▸ Spacetime ▸ Create Spacetime Lab Scene. Writes a NEW
/// scene, Scenes/SpacetimeLab.unity, for trying out worldlines: a floor, the
/// chibi (an instance of Chibi.prefab, which is read and never modified), an
/// orbit camera, a <see cref="SpacetimeView"/>, and three recorded movers, one
/// per kind of terrain the 4D view makes.
///
/// A separate scene rather than a rig dropped into ChibiSandbox: that scene is
/// shared by several systems, and an unsaved edit left in it is how one
/// person's work ends up flushed into someone else's save. This scene has one
/// owner. Re-running rebuilds it from scratch.
/// </summary>
public static class SpacetimeLabSetup
{
    const string ScenePath = "Assets/_Project/Scenes/SpacetimeLab.unity";
    const string ChibiPrefabPath = "Assets/_Project/Prefabs/Chibi.prefab";

    [MenuItem("Tools/Pickleball/Spacetime/Create Spacetime Lab Scene")]
    public static void Run()
    {
        var chibiPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChibiPrefabPath);
        if (chibiPrefab == null)
        {
            Debug.LogError($"No prefab at {ChibiPrefabPath}. Run Tools ▸ Pickleball ▸ " +
                           "Setup Chibi Sandbox once to create it.");
            return;
        }

        if (EditorApplication.isPlaying)
        {
            Debug.LogError("Leave Play mode first. A new scene cannot be created during play.");
            return;
        }

        // Opening a new scene closes the open ones. Refuse rather than prompt:
        // a modal save dialog blocks the Editor for anyone driving it remotely,
        // and asks whoever is present to save or discard edits that may not be
        // theirs.
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            var open = EditorSceneManager.GetSceneAt(i);
            if (!open.isDirty) continue;
            Debug.LogError($"Scene '{open.name}' has unsaved changes. Save or revert " +
                           "it, then run this again.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            Debug.Log($"Rebuilding {ScenePath} from scratch.");

        // Comes with a Main Camera and a Directional Light.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor";
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        floor.transform.localScale = new Vector3(40f, 1f, 40f);
        floor.GetComponent<Renderer>().sharedMaterial =
            PrimitiveFactory.CreateFlatMaterial(new Color(0.82f, 0.84f, 0.8f));

        var chibi = (GameObject)PrefabUtility.InstantiatePrefab(chibiPrefab, scene);
        chibi.transform.SetPositionAndRotation(new Vector3(0f, 0f, -6f), Quaternion.identity);
        WireCamera(chibi);

        var view = new GameObject("Spacetime").AddComponent<SpacetimeView>();
        // A material saved with the scene, so the shader it uses is referenced
        // and ships in a build of this scene. Left to the runtime fallback, the
        // shader could be stripped.
        var particleUnlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particleUnlit != null)
        {
            var beamMaterial = new Material(particleUnlit) { name = "Worldline" };
            beamMaterial.SetColor("_BaseColor", Color.white);
            Set(view, "worldlineMaterial", beamMaterial);
        }

        var violet = new Color(0.55f, 0.45f, 0.95f);
        var teal = new Color(0.35f, 0.8f, 0.75f);
        var amber = new Color(1f, 0.75f, 0.3f);

        // A raised platform swinging side to side. With the default upward time
        // axis its worldline is a ramp rising out of the floor to the platform.
        // The period is twice the 4 s history, so the beam holds about one
        // sweep. With a shorter period the legs of the zigzag stack directly
        // above each other, too close together for the chibi to fit between.
        // Flipping mid-sweep gives one long ramp, and flipping near a turnaround
        // gives a hairpin. Timing the flip is part of the puzzle.
        var swing = Mover("Swinging Platform", PrimitiveType.Cube,
                          new Vector3(0f, 2f, 0f), new Vector3(2f, 0.3f, 2f), violet,
                          SpacetimeDemoMover.Mode.Oscillate);
        Set(swing.GetComponent<SpacetimeDemoMover>(), "period", 8f);
        Set(swing.GetComponent<WorldlineRecorder>(), "traceOffset", new Vector3(0f, 0.15f, 0f));

        // A platform circling a point. Its worldline is a spiral staircase.
        var orbit = Mover("Orbiting Platform", PrimitiveType.Cube,
                          new Vector3(-8f, 2.5f, 6f), new Vector3(1.6f, 0.3f, 1.6f), teal,
                          SpacetimeDemoMover.Mode.Orbit);
        Set(orbit.GetComponent<SpacetimeDemoMover>(), "period", 5f);

        // A ball lobbed over and over. Each throw leaves an arched bridge.
        var lob = Mover("Lobbed Ball", PrimitiveType.Sphere,
                        new Vector3(6f, 0.5f, -2f), Vector3.one * 0.5f, amber,
                        SpacetimeDemoMover.Mode.Lob);
        // The ball's own collider would shove the player around in 3D and is
        // not what this demonstrates.
        Object.DestroyImmediate(lob.GetComponent<Collider>());
        Set(lob.GetComponent<WorldlineRecorder>(), "width", 1.4f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log($"Spacetime lab saved to {ScenePath}. Enter Play mode, let the " +
                  "movers run for a few seconds, then press T (or d-pad up) to flip " +
                  "into 4D.");
    }

    /// <summary>Puts the orbit camera on the scene's Main Camera and points the
    /// chibi's movement at it. Both are overrides in this scene only; the prefab
    /// is untouched.</summary>
    static void WireCamera(GameObject chibi)
    {
        var cam = Camera.main;
        if (cam == null) return;

        var cc = chibi.GetComponent<CharacterController>();
        float height = cc != null ? cc.height : 1.6f;

        cam.transform.position = chibi.transform.position + new Vector3(0f, height * 0.8f, -4f);
        cam.transform.rotation = Quaternion.Euler(12f, 0f, 0f);

        var orbit = cam.gameObject.AddComponent<SandboxOrbitCamera>();
        Set(orbit, "target", chibi.transform);
        Set(orbit, "targetOffset", new Vector3(0f, height * 0.8f, 0f));

        var loco = chibi.GetComponent<ChibiLocomotion>();
        if (loco != null) Set(loco, "cameraTransform", cam.transform);
    }

    static GameObject Mover(string name, PrimitiveType shape, Vector3 position,
                            Vector3 scale, Color color, SpacetimeDemoMover.Mode mode)
    {
        var go = GameObject.CreatePrimitive(shape);
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = scale;
        // Saved inside the scene rather than as an asset, so the lab adds nothing
        // to the project beyond the scene file itself.
        go.GetComponent<Renderer>().sharedMaterial = PrimitiveFactory.CreateFlatMaterial(color);

        var mover = go.AddComponent<SpacetimeDemoMover>();
        Set(mover, "mode", mode);
        go.AddComponent<WorldlineRecorder>();
        return go;
    }

    static void Set(Object target, string field, object value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        switch (value)
        {
            case float f: prop.floatValue = f; break;
            case System.Enum e: prop.enumValueIndex = System.Convert.ToInt32(e); break;
            case Vector3 v: prop.vector3Value = v; break;
            case Object o: prop.objectReferenceValue = o; break;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
