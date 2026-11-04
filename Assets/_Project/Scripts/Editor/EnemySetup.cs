using System;
using System.Collections.Generic;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Enemies;
using Pickleball.Sandbox.Magic;
using Pickleball.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Tools ▸ Pickleball ▸ Enemies. Builds the basic enemy's config and prefab, and puts
/// some in a scene to fight.
///
/// <list type="bullet">
/// <item><b>Build Alien Enemy:</b> seeds <c>BasicEnemy.asset</c> once (after that the
/// asset is the source of truth, so tuning survives) and regenerates
/// <c>AlienEnemy.prefab</c> in place, keeping its GUID. Needs the alien imported first:
/// Tools ▸ Pickleball ▸ Tripo ▸ Import Tripo Assets. Touches no scene.</item>
/// <item><b>Place Alien Enemies In Open Scene:</b> replaces the group under
/// <c>Enemies</c>, one inside aggro range and two outside, and registers them with any
/// <see cref="DamageNumbers"/> so hits show. Marks the scene dirty and never saves.</item>
/// <item><b>Build Enemy Trial Scene:</b> copies TrainingStage to
/// <c>Scenes/Trials/EnemyTrial.unity</c> and places them there, so trying the enemy
/// never touches the shared scenes. Deletes and re-creates that copy on each run.</item>
/// </list>
///
/// Headless: <c>-executeMethod EnemySetup.Batch</c>, which builds the prefab and the
/// trial scene, and exits 1 if either failed.
/// </summary>
public static class EnemySetup
{
    const string MenuRoot = "Tools/Pickleball/Enemies/";
    const string ConfigFolder = "Assets/_Project/ScriptableObjects/Enemies";
    const string ConfigPath = ConfigFolder + "/BasicEnemy.asset";
    const string PrefabFolder = "Assets/_Project/Prefabs/Enemies";
    const string PrefabPath = PrefabFolder + "/AlienEnemy.prefab";
    const string ModelPrefabPath = "Assets/_Project/Prefabs/Tripo/AlienCreature.prefab";
    const string TrialFolder = "Assets/_Project/Scenes/Trials";
    const string TrialScenePath = TrialFolder + "/EnemyTrial.unity";
    const string TrialSourcePath = "Assets/_Project/Scenes/TrainingStage.unity";
    const string GroupName = "Enemies";
    const string VisualName = "Visual";

    /// <summary>Metres. Matches the catalog row's target size (EN-02).</summary>
    const float BodyHeight = 1.6f;

    /// <summary>Metres. The body, not the arm span: wide enough that swings connect,
    /// narrow enough that two can stand side by side.</summary>
    const float BodyRadius = 0.45f;

    /// <summary>Metres. The controller rests this far above the ground, so its centre is
    /// raised by the same amount to keep the feet on the floor.</summary>
    const float SkinWidth = 0.05f;

    /// <summary>Metres above the player's level that a placement looks down from.</summary>
    const float PlacementProbeHeight = 1f;

    /// <summary>Metres, in the player's frame: x right, z forward. The first is inside
    /// the 14 m aggro range and comes at once; the others wait to be approached.</summary>
    static readonly Vector3[] PlacementOffsets =
    {
        new(1.5f, 0f, 10f),
        new(20f, 0f, -5f),
        new(-20f, 0f, -15f),
    };

    [MenuItem(MenuRoot + "Build Alien Enemy")]
    static void BuildMenu()
    {
        if (RefuseInPlayMode()) return;
        var prefab = BuildPrefab();
        if (prefab != null) Debug.Log($"Enemy setup: built {PrefabPath}.", prefab);
    }

    [MenuItem(MenuRoot + "Place Alien Enemies In Open Scene")]
    static void PlaceMenu()
    {
        if (RefuseInPlayMode()) return;
        PlaceInOpenScene();
    }

    [MenuItem(MenuRoot + "Build Enemy Trial Scene")]
    static void TrialMenu()
    {
        if (RefuseInPlayMode()) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        BuildTrialScene();
    }

    /// <summary>Headless entry. Exits with a code that means something.</summary>
    public static void Batch()
    {
        int code = 0;
        try
        {
            if (BuildPrefab() == null || !BuildTrialScene()) code = 1;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            code = 1;
        }
        EditorApplication.Exit(code);
    }

    /// <summary>Seeds the config once; an existing asset is never overwritten.</summary>
    public static EnemyConfig EnsureConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<EnemyConfig>(ConfigPath);
        if (config != null) return config;
        EnsureFolder(ConfigFolder);
        config = ScriptableObject.CreateInstance<EnemyConfig>();
        AssetDatabase.CreateAsset(config, ConfigPath);
        AssetDatabase.SaveAssetIfDirty(config);
        return config;
    }

    /// <summary>Writes the prefab, or updates it in place. Null if the model is missing.</summary>
    public static GameObject BuildPrefab()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPrefabPath);
        if (model == null)
        {
            Debug.LogError($"Enemy setup: no model at {ModelPrefabPath}. Run Tools > Pickleball > Tripo > " +
                           "Import Tripo Assets with the alien's download first (catalog row EN-02).");
            return null;
        }

        var config = EnsureConfig();
        EnsureFolder(PrefabFolder);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null)
        {
            var seed = new GameObject("AlienEnemy");
            PrefabUtility.SaveAsPrefabAsset(seed, PrefabPath);
            Object.DestroyImmediate(seed);
        }

        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            var controller = GetOrAdd<CharacterController>(root);
            controller.height = BodyHeight;
            controller.radius = BodyRadius;
            controller.skinWidth = SkinWidth;
            controller.center = new Vector3(0f, BodyHeight * 0.5f + SkinWidth, 0f);
            controller.stepOffset = 0.3f;
            controller.slopeLimit = 45f;
            controller.minMoveDistance = 0f;

            // The bar has no grey region on an enemy: the regain pool is the player's rule.
            var health = GetOrAdd<Health>(root);
            var healthSo = new SerializedObject(health);
            healthSo.FindProperty("maxHealth").floatValue = config.MaxHealth;
            healthSo.FindProperty("regainShare").floatValue = 0f;
            healthSo.ApplyModifiedPropertiesWithoutUndo();

            GetOrAdd<EnemyMotor>(root);
            var enemy = GetOrAdd<Enemy>(root);
            var enemySo = new SerializedObject(enemy);
            enemySo.FindProperty("config").objectReferenceValue = config;
            enemySo.ApplyModifiedPropertiesWithoutUndo();

            GetOrAdd<HurtFlash>(root);

            var visual = root.transform.Find(VisualName);
            if (visual == null)
            {
                visual = new GameObject(VisualName).transform;
                visual.SetParent(root.transform, false);
            }
            visual.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            visual.localScale = Vector3.one;

            bool hasModel = visual.childCount == 1 &&
                            PrefabUtility.GetCorrespondingObjectFromSource(visual.GetChild(0).gameObject) == model;
            if (!hasModel)
            {
                for (int i = visual.childCount - 1; i >= 0; i--)
                    Object.DestroyImmediate(visual.GetChild(i).gameObject);
                PrefabUtility.InstantiatePrefab(model, visual);
            }

            var view = GetOrAdd<EnemyWaddleView>(root);
            var viewSo = new SerializedObject(view);
            viewSo.FindProperty("enemy").objectReferenceValue = enemy;
            viewSo.FindProperty("visual").objectReferenceValue = visual;
            viewSo.ApplyModifiedPropertiesWithoutUndo();

            // Opts it in to the Freeze Zone's slow and the Thunderbolt's stagger, with
            // the frost and stars. Once only: it rebuilds its visual children each call.
            if (root.GetComponent<StatusReceiver>() == null)
                MagicSpellArtBuilder.AttachStatusReceiver(root);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
    }

    /// <summary>Replaces the placed group in the open scene, in front of the player.</summary>
    public static bool PlaceInOpenScene()
    {
        var player = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (player == null)
        {
            Debug.LogError("Enemy setup: no player (ChibiLocomotion) in the open scene.");
            return false;
        }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) prefab = BuildPrefab();
        if (prefab == null) return false;

        var group = GameObject.Find(GroupName);
        if (group == null) group = new GameObject(GroupName);
        for (int i = group.transform.childCount - 1; i >= 0; i--)
            Object.DestroyImmediate(group.transform.GetChild(i).gameObject);

        Physics.SyncTransforms();
        Transform origin = player.transform;
        var placed = new List<Health>();
        for (int i = 0; i < PlacementOffsets.Length; i++)
        {
            Vector3 at = origin.TransformPoint(PlacementOffsets[i]);
            // Stand it on the floor the player is on. The ray starts just above that level
            // so it passes under platforms instead of landing the enemy on top of one.
            if (Physics.Raycast(at + Vector3.up * PlacementProbeHeight, Vector3.down, out var hit, 60f, ~0,
                                QueryTriggerInteraction.Ignore))
                at = hit.point;

            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.transform);
            go.name = $"AlienEnemy_{i + 1}";
            Vector3 toPlayer = Vector3.ProjectOnPlane(origin.position - at, Vector3.up);
            go.transform.SetPositionAndRotation(at, toPlayer.sqrMagnitude > 1e-4f
                ? Quaternion.LookRotation(toPlayer, Vector3.up)
                : Quaternion.identity);
            placed.Add(go.GetComponent<Health>());
        }

        RegisterWithDamageNumbers(placed);
        EditorSceneManager.MarkSceneDirty(group.scene);
        Debug.Log($"Enemy setup: placed {placed.Count} enemies in {group.scene.name}. Save the scene to keep them.");
        return true;
    }

    /// <summary>Copies TrainingStage to the trial path and places enemies in the copy.
    /// Saves only that copy, and leaves it open.</summary>
    public static bool BuildTrialScene()
    {
        EnsureFolder(TrialFolder);
        if (AssetDatabase.LoadMainAssetAtPath(TrialScenePath) != null)
        {
            // Close it first: deleting the open scene's asset would leave it orphaned.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(TrialScenePath);
        }
        if (!AssetDatabase.CopyAsset(TrialSourcePath, TrialScenePath))
        {
            Debug.LogError($"Enemy setup: could not copy {TrialSourcePath}.");
            return false;
        }

        var scene = EditorSceneManager.OpenScene(TrialScenePath, OpenSceneMode.Single);
        if (scene.path != TrialScenePath || !PlaceInOpenScene()) return false;
        return EditorSceneManager.SaveScene(scene);
    }

    // The HUD's list is explicit, so enemies placed in a scene are added here. One spawned
    // at runtime is not on it; see Docs/Systems/Enemies.md.
    static void RegisterWithDamageNumbers(List<Health> enemies)
    {
        var all = Object.FindObjectsByType<DamageNumbers>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var numbers in all)
        {
            var so = new SerializedObject(numbers);
            var sources = so.FindProperty("sources");
            for (int i = sources.arraySize - 1; i >= 0; i--)
                if (sources.GetArrayElementAtIndex(i).FindPropertyRelative("health").objectReferenceValue == null)
                    sources.DeleteArrayElementAtIndex(i);
            foreach (var health in enemies)
            {
                // Inserting at the end copies the last entry, so enemies take the style
                // of whatever target is already listed.
                int at = sources.arraySize;
                sources.InsertArrayElementAtIndex(at);
                sources.GetArrayElementAtIndex(at).FindPropertyRelative("health").objectReferenceValue = health;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
        => go.TryGetComponent(out T existing) ? existing : go.AddComponent<T>();

    static bool RefuseInPlayMode()
    {
        if (!EditorApplication.isPlayingOrWillChangePlaymode) return false;
        Debug.LogError("Enemy setup: leave Play mode first.");
        return true;
    }

    static void EnsureFolder(string dir)
    {
        if (AssetDatabase.IsValidFolder(dir)) return;
        string parent = System.IO.Path.GetDirectoryName(dir)!.Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(dir));
    }
}
