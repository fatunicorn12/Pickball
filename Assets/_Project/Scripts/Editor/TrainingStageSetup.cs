using System.Collections.Generic;
using System.Linq;
using Pickleball.Sandbox;
using Pickleball.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools ▸ Pickleball ▸ Build Training Stage. Builds
/// <c>Scenes/TrainingStage.unity</c>: a test map in the style of Smash Bros'
/// training stage, for trying every movement and combat mechanic in 3D.
///
/// <b>The player comes from ChibiSandbox.</b> The scene starts as a copy of
/// ChibiSandbox with the academy, Newton and the old pickups stripped out, so
/// the chibi, camera, lock-on and HUD arrive exactly as wired there, including
/// anything later setups add. Re-run this after rebuilding the sandbox (or
/// after a movement/combat setup) and the stage picks the changes up. The
/// stage itself is regenerated each run, so hand edits to TrainingStage.unity
/// are lost; change this file instead.
///
/// <b>The look</b> is <c>Pickball/Training Grid</c>: every surface ruled in
/// world-space metres (fine 25 cm lines, metre lines, blue every 5 m, red
/// along the axes), so heights and distances read straight off the scenery.
///
/// <b>Layout</b> (X right, Z away from the spawn, floor top at y = 0):
///  * North: the Smash layout itself, two side platforms and a top one in
///    front of a gridded backboard with Smash's numbering, plus three
///    training dummies to hit, lock on to and switch between. Seen from the
///    spawn camera it frames like the reference screenshot, and it is the
///    natural lane for a future side-on 2D view.
///  * In front of the spawn: one pedestal per ability pickup.
///  * East: a height ladder (0.5 m to 8 m), a zig-zag of floating platforms,
///    a 12 m spiral tower, and a gap course over the void (2 m to 8 m).
///  * West: ramps at 15/30/45/60° (60 is past the controller's slope limit),
///    step heights, a wall-kick corridor, a 6 m wall and a crouch tunnel.
///  * South: an open sprint field measured by the axis numbers, tether
///    anchors overhead, and a tether crossing over the void to an island.
///  * All around: a far gridded room, and blast zones that respawn you.
/// </summary>
public static class TrainingStageSetup
{
    const string SourceScenePath = "Assets/_Project/Scenes/ChibiSandbox.unity";
    const string ScenePath = "Assets/_Project/Scenes/TrainingStage.unity";
    const string ArtDir = "Assets/_Project/Art/World/Training";
    const string ShaderName = "Pickball/Training Grid";
    const string WedgePath = ArtDir + "/Wedge.asset";
    const string PickupDir = "Assets/_Project/Prefabs/Pickups";
    const string GravityAnchorPickupPath = "Assets/_Project/Prefabs/Boss/Pickup_GravityAnchor.prefab";

    static readonly Vector3 SpawnPosition = new(0f, 0.05f, 0f);

    // Stage floor footprint; the top is y = 0.
    const float FloorMinX = -28f, FloorMaxX = 28f, FloorMinZ = -28f, FloorMaxZ = 16f;
    const float FloorThickness = 2f;

    /// <summary>The far room's half-size and its floor/ceiling heights.</summary>
    const float RoomHalf = 120f, RoomBottom = -60f, RoomTop = 60f;

    /// <summary>Tether latch range, matching the sandbox's anchors.</summary>
    const float AnchorRange = 9f;

    static readonly Color Blue = new(0.25f, 0.42f, 0.92f);
    static readonly Color Red = new(0.92f, 0.2f, 0.22f);
    static readonly Color Ink = new(0.33f, 0.35f, 0.4f);

    static Material _floorMat, _blockMat, _platformMat, _roomMat;
    static TMP_FontAsset _font;
    static Transform _geometry, _labels;

    [MenuItem("Tools/Pickleball/Build Training Stage")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Leave Play mode before building the training stage.");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
        {
            Debug.LogError($"{SourceScenePath} not found. Run Tools ▸ Pickleball ▸ Setup Chibi Sandbox first.");
            return;
        }
        // Never silently discard unsaved work in whatever scene is open.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) AssetDatabase.DeleteAsset(ScenePath);
        if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
        {
            Debug.LogError($"Could not copy {SourceScenePath} to {ScenePath}.");
            return;
        }
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var player = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (player == null)
        {
            Debug.LogError("ChibiSandbox has no ChibiLocomotion; rebuild it with Setup Chibi Sandbox.");
            return;
        }

        ChibiSandboxSetup.EnsureFolderPublic(ArtDir);
        LoadArt();

        var root = new GameObject("TrainingStage");
        _geometry = new GameObject("Geometry").transform;
        _geometry.SetParent(root.transform, false);
        _labels = new GameObject("Labels").transform;
        _labels.SetParent(root.transform, false);

        BuildRoom();
        BuildFloor();
        BuildSmashLayout();
        BuildEast();
        BuildWest();
        BuildSouth();

        // Floor numbers are laid last: they skip any spot a block now covers,
        // which needs the colliders above registered first.
        Physics.SyncTransforms();
        BuildAxisNumbers();

        var dummies = BuildDummies(root.transform);
        BuildItems(root.transform);
        BuildAnchors(root.transform);

        var spawn = new GameObject("PlayerSpawn").transform;
        spawn.SetParent(root.transform, false);
        spawn.SetPositionAndRotation(SpawnPosition, Quaternion.identity);
        PlacePlayer(player, spawn);

        var hud = RebindHud(dummies);
        StripSandbox(player);

        var stage = root.AddComponent<TrainingStage>();
        var so = new SerializedObject(stage);
        so.FindProperty("player").objectReferenceValue = player.GetComponent<CharacterController>();
        so.FindProperty("spawnPoint").objectReferenceValue = spawn;
        so.FindProperty("readout").objectReferenceValue = BuildReadout(hud);
        so.ApplyModifiedPropertiesWithoutUndo();

        ApplyLighting();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = player.gameObject;
        Debug.Log($"Training stage built: {ScenePath}. Backspace respawns, Shift+Backspace also " +
                  "takes the abilities back; falling off the stage respawns you.");
    }

    // ── Art ─────────────────────────────────────────────────────────────────

    static void LoadArt()
    {
        // Only the floor and backdrops carry the red axes: on blocks the y = 0
        // line just outlines every base in red.
        var shade = new Color(0.66f, 0.69f, 0.78f);
        _floorMat = GridMaterial("TrainingFloor", new Color(0.93f, 0.93f, 0.94f),
            new Color(0.84f, 0.85f, 0.87f), new Color(0.66f, 0.68f, 0.72f), shade, axes: true);
        _blockMat = GridMaterial("TrainingBlock", new Color(0.84f, 0.86f, 0.91f),
            new Color(0.76f, 0.78f, 0.84f), new Color(0.58f, 0.61f, 0.68f), shade, axes: false);
        _platformMat = GridMaterial("TrainingPlatform", new Color(0.62f, 0.63f, 0.66f),
            new Color(0.57f, 0.58f, 0.61f), new Color(0.46f, 0.47f, 0.5f), shade, axes: false);
        // The far room is mostly seen from its shaded side; keep it pale.
        _roomMat = GridMaterial("TrainingRoom", new Color(0.96f, 0.96f, 0.97f),
            new Color(0.9f, 0.9f, 0.92f), new Color(0.78f, 0.8f, 0.84f), new Color(0.84f, 0.86f, 0.91f), axes: true);
        _font = NewtonFightArt.Font("Oswald Bold SDF");
    }

    /// <summary>Created once; afterwards the asset is left alone, so colours
    /// tuned live in the inspector survive a rebuild.</summary>
    static Material GridMaterial(string name, Color baseColour, Color fine, Color minor, Color shade, bool axes)
    {
        string path = $"{ArtDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"Shader '{ShaderName}' not found; is {ArtDir}/TrainingGrid.shader imported?");
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }
        mat = new Material(shader) { name = name };
        mat.SetColor("_BaseColor", baseColour);
        mat.SetColor("_FineColor", fine);
        mat.SetColor("_MinorColor", minor);
        mat.SetColor("_ShadeColor", shade);
        if (!axes) mat.SetColor("_AxisColor", Color.clear);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    // ── Geometry ────────────────────────────────────────────────────────────

    static void BuildRoom()
    {
        // A far, open-topped room: the grid backdrop the stage floats in.
        var group = Group("Room");
        float h = RoomHalf, t = 1f;
        Box("RoomFloor", group, new(-h, RoomBottom - t, -h), new(h, RoomBottom, h), _roomMat);
        Box("RoomWallNorth", group, new(-h, RoomBottom, h), new(h, RoomTop, h + t), _roomMat);
        Box("RoomWallSouth", group, new(-h, RoomBottom, -h - t), new(h, RoomTop, -h), _roomMat);
        Box("RoomWallEast", group, new(h, RoomBottom, -h), new(h + t, RoomTop, h), _roomMat);
        Box("RoomWallWest", group, new(-h - t, RoomBottom, -h), new(-h, RoomTop, h), _roomMat);
    }

    static void BuildFloor()
    {
        Box("Floor", _geometry, new(FloorMinX, -FloorThickness, FloorMinZ), new(FloorMaxX, 0f, FloorMaxZ), _floorMat);
    }

    /// <summary>The reference layout: floor, two side platforms, one on top,
    /// in front of a backboard numbered like Smash's.</summary>
    static void BuildSmashLayout()
    {
        var group = Group("SmashLayout");
        Box("Backboard", group, new(-12f, 0f, 15f), new(12f, 10f, 16f), _roomMat);
        Box("PlatformLeft", group, new(-7.2f, 1.45f, 8f), new(-2.8f, 1.75f, 12f), _platformMat);
        Box("PlatformRight", group, new(2.8f, 1.45f, 8f), new(7.2f, 1.75f, 12f), _platformMat);
        Box("PlatformTop", group, new(-2.2f, 3.2f, 8f), new(2.2f, 3.5f, 12f), _platformMat);

        // Numbers sit just right of each line and just above the floor line.
        var face = Quaternion.identity;   // reads from the spawn, on the -Z face
        foreach (int x in new[] { -10, -5, 0, 5, 10 })
            Label(Mathf.Abs(x).ToString(), new Vector3(x + 0.45f, 0.5f, 14.98f), face, 8f, x == 0 ? Red : Blue);
        // Further right than the rest, to clear the dummy on the top platform.
        Label("5", new Vector3(1f, 5.5f, 14.98f), face, 8f, Blue);
    }

    static void BuildEast()
    {
        var group = Group("East");

        // Height ladder: free-standing pillars, labelled on the side facing
        // the stage, for reading off exactly how high each jump reaches.
        float[] heights = { 0.5f, 1f, 1.5f, 2f, 2.5f, 3f, 3.5f, 4f, 4.5f, 5f, 6f, 7f, 8f };
        var faceWest = Quaternion.Euler(0f, 90f, 0f);
        for (int i = 0; i < heights.Length; i++)
        {
            float h = heights[i], z = -22f + i * 2.5f;
            Box($"Ladder_{h:0.0}m", group, new(25f, 0f, z - 1f), new(27f, h, z + 1f), _blockMat);
            Label($"{h:0.#} m", new Vector3(24.98f, Mathf.Max(h - 0.35f, h * 0.5f), z), faceWest, 4.5f, Ink);
        }
        FloorLabel("HEIGHT", new Vector3(23f, 0f, -24.5f), 90f, 9f, Ink);

        // Smash-style floating platforms, zig-zagging up 1.75 m at a time.
        for (int i = 0; i < 6; i++)
        {
            float x = i % 2 == 0 ? 15f : 19f, y = 1.75f * (i + 1), z = -20f + 3f * i;
            Box($"Float_{i}", group, new(x - 1.5f, y - 0.3f, z - 1.5f), new(x + 1.5f, y, z + 1.5f), _platformMat);
        }

        // Spiral tower: steps 1.5 m apart round a pillar to a 12 m perch.
        var centre = new Vector3(19.5f, 0f, 10.5f);
        Box("Tower", group, centre + new Vector3(-1f, 0f, -1f), centre + new Vector3(1f, 12f, 1f), _blockMat);
        for (int i = 0; i < 7; i++)
        {
            float angle = (-90f + i * 55f) * Mathf.Deg2Rad, y = 1.5f * (i + 1);
            var c = centre + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 3.6f;
            Box($"TowerStep_{i}", group, new(c.x - 1.3f, y - 0.3f, c.z - 1.3f), new(c.x + 1.3f, y, c.z + 1.3f), _platformMat);
        }
        Box("TowerPerch", group, centre + new Vector3(-2.25f, 12f, -2.25f), centre + new Vector3(2.25f, 12.3f, 2.25f), _platformMat);
        FloorLabel("TOWER 12 m", new Vector3(19.5f, 0f, 4.4f), 0f, 7f, Ink);

        // Gap course over the void, off the east edge: land or fall.
        float[] gaps = { 2f, 3f, 4f, 5f, 6f, 8f };
        float edge = FloorMaxX;
        for (int i = 0; i < gaps.Length; i++)
        {
            float x0 = edge + gaps[i], length = i == gaps.Length - 1 ? 5f : 3f;
            Box($"Gap_{gaps[i]:0}m", group, new(x0, -1f, -28f), new(x0 + length, 0f, -24f), _blockMat);
            FloorLabel($"{gaps[i]:0} m", new Vector3(x0 + 0.8f, 0f, -26f), 90f, 7f, Blue);
            edge = x0 + length;
        }
        FloorLabel("GAPS", new Vector3(26.4f, 0f, -26f), 90f, 7f, Ink);
    }

    static void BuildWest()
    {
        var group = Group("West");

        // Ramps rise north onto a 2 m plateau. 60° is past the
        // CharacterController's 50° slope limit, so it should refuse you.
        Box("Plateau", group, new(-27.5f, 0f, -2f), new(-12.5f, 2f, 2f), _blockMat);
        float[] angles = { 15f, 30f, 45f, 60f };
        for (int i = 0; i < angles.Length; i++)
        {
            float x = -26f + 4f * i;
            float run = 2f / Mathf.Tan(angles[i] * Mathf.Deg2Rad);
            Ramp($"Ramp_{angles[i]:0}deg", group, new Vector3(x, 0f, -2f - run), 3f, 2f, run, _blockMat);
            FloorLabel($"{angles[i]:0}°", new Vector3(x, 0f, -2.9f - run), 0f, 7f, Blue);
        }

        // Step heights: the controller's step offset is 0.3 m.
        float[] steps = { 0.15f, 0.3f, 0.45f, 0.6f };
        for (int i = 0; i < steps.Length; i++)
        {
            float x = -26f + 3f * i;
            Box($"Step_{steps[i]:0.00}m", group, new(x - 0.75f, 0f, -14.75f), new(x + 0.75f, steps[i], -13.25f), _blockMat);
            FloorLabel($"{steps[i]:0.00} m", new Vector3(x, 0f, -15.6f), 0f, 4.5f, Blue);
        }
        FloorLabel("STEPS", new Vector3(-14f, 0f, -14f), 0f, 7f, Ink);

        // Walls: a 2 m wall-kick corridor, a lone 6 m wall, a crouch tunnel.
        Box("CorridorWallWest", group, new(-27.5f, 0f, 5f), new(-26.5f, 10f, 14f), _blockMat);
        Box("CorridorWallEast", group, new(-24.5f, 0f, 5f), new(-23.5f, 10f, 14f), _blockMat);
        FloorLabel("WALL KICK", new Vector3(-25.5f, 0f, 4.1f), 0f, 5.5f, Ink);

        Box("Wall_6m", group, new(-21f, 0f, 5f), new(-20f, 6f, 14f), _blockMat);
        FloorLabel("6 m WALL", new Vector3(-20.5f, 0f, 4.1f), 0f, 5.5f, Ink);

        Box("TunnelSideWest", group, new(-18f, 0f, 5f), new(-17.5f, 1.25f, 13f), _blockMat);
        Box("TunnelSideEast", group, new(-14f, 0f, 5f), new(-13.5f, 1.25f, 13f), _blockMat);
        Box("TunnelRoof", group, new(-18f, 1.25f, 5f), new(-13.5f, 1.75f, 13f), _platformMat);
        FloorLabel("CROUCH 1.25 m", new Vector3(-15.75f, 0f, 4.1f), 0f, 5.5f, Ink);
    }

    static void BuildSouth()
    {
        var group = Group("South");

        // Block garden: a stepped pyramid and a few crates to clamber on.
        Box("Pyramid_1", group, new(-24f, 0f, -26f), new(-18f, 1f, -20f), _blockMat);
        Box("Pyramid_2", group, new(-23f, 1f, -25f), new(-19f, 2f, -21f), _blockMat);
        Box("Pyramid_3", group, new(-22f, 2f, -24f), new(-20f, 3f, -22f), _blockMat);
        Box("Crate_A", group, new(-16f, 0f, -26f), new(-15f, 1f, -25f), _blockMat);
        Box("Crate_B", group, new(-15f, 0f, -26f), new(-14f, 1f, -25f), _blockMat);
        Box("Crate_C", group, new(-15.5f, 1f, -26f), new(-14.5f, 2f, -25f), _blockMat);
        Box("Crate_D", group, new(-16f, 0f, -25f), new(-15f, 1f, -24f), _blockMat);
        Box("Crate_Big", group, new(-15f, 0f, -22f), new(-13f, 2f, -20f), _blockMat);
        Box("Crate_Top", group, new(-14.5f, 2f, -21.5f), new(-13.5f, 3f, -20.5f), _blockMat);

        // The tether crossing ends on an island beyond the south edge.
        Box("TetherIsland", group, new(-4f, -1.5f, -58f), new(4f, 0f, -51f), _blockMat);
        FloorLabel("TETHER", new Vector3(4f, 0f, -26.6f), 0f, 7f, Ink);
        FloorLabel("SPRINT", new Vector3(-4.5f, 0f, -12f), 0f, 7f, Ink);
    }

    /// <summary>Numbers every 5 m along both floor axes, like the reference's
    /// "5 · 0 · 5". Skipped wherever a block covers the spot.</summary>
    static void BuildAxisNumbers()
    {
        for (int x = -25; x <= 25; x += 5)
            AxisNumber(x, 0);
        for (int z = -25; z <= 15; z += 5)
            if (z != 0) AxisNumber(0, z);
    }

    static void AxisNumber(int x, int z)
    {
        var at = new Vector3(x + 0.45f, 0f, z + 0.55f);
        if (Physics.Raycast(at + Vector3.up * 30f, Vector3.down, out var hit, 40f, ~0, QueryTriggerInteraction.Ignore)
            && hit.point.y > 0.05f)
            return;
        int value = Mathf.Abs(x != 0 ? x : z);
        FloorLabel(value.ToString(), at, 0f, 8f, value == 0 ? Red : Blue);
    }

    // ── Dummies, items, anchors ────────────────────────────────────────────

    static List<Health> BuildDummies(Transform root)
    {
        var group = new GameObject("Dummies").transform;
        group.SetParent(root, false);

        var cloth = NewtonFightArt.LitMaterial(ArtDir, "DummyCloth", new Color(0.86f, 0.74f, 0.52f), 0.15f);
        var dark = NewtonFightArt.LitMaterial(ArtDir, "DummyEyes", new Color(0.12f, 0.1f, 0.12f), 0.4f);

        // One on the floor, one on a side platform, one on top: three targets
        // to lock on to and flick between.
        return new List<Health>
        {
            BuildDummy("Dummy_Floor", new Vector3(9.5f, 0f, 10f), group, cloth, dark),
            BuildDummy("Dummy_Platform", new Vector3(-5f, 1.75f, 10f), group, cloth, dark),
            BuildDummy("Dummy_Top", new Vector3(0f, 3.5f, 10f), group, cloth, dark),
        };
    }

    static Health BuildDummy(string name, Vector3 feet, Transform parent, Material cloth, Material dark)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.SetPositionAndRotation(feet, Quaternion.Euler(0f, 180f, 0f));   // faces the spawn

        var col = root.AddComponent<CapsuleCollider>();
        col.height = 1.6f;
        col.radius = 0.42f;
        col.center = new Vector3(0f, 0.8f, 0f);

        // The visual pivots at the feet, so a hit rocks it from the base.
        var visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        Part(PrimitiveType.Capsule, "Body", visual, new Vector3(0f, 0.8f, 0f), new Vector3(0.84f, 0.8f, 0.84f), cloth);
        Part(PrimitiveType.Sphere, "Knot", visual, new Vector3(0f, 1.66f, 0f), new Vector3(0.24f, 0.18f, 0.24f), cloth);
        Part(PrimitiveType.Sphere, "EyeL", visual, new Vector3(-0.13f, 1.18f, 0.38f), new Vector3(0.09f, 0.14f, 0.05f), dark);
        Part(PrimitiveType.Sphere, "EyeR", visual, new Vector3(0.13f, 1.18f, 0.38f), new Vector3(0.09f, 0.14f, 0.05f), dark);

        var health = root.AddComponent<Health>();
        var dummy = root.AddComponent<TrainingDummy>();
        var so = new SerializedObject(dummy);
        so.FindProperty("visual").objectReferenceValue = visual;
        so.ApplyModifiedPropertiesWithoutUndo();
        return health;
    }

    static void Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>One of every ability pickup, each on a pedestal in front of
    /// the spawn. They are left in place when collected, so after
    /// Shift+Backspace takes the abilities away they can be picked up again.</summary>
    static void BuildItems(Transform root)
    {
        var group = new GameObject("Items").transform;
        group.SetParent(root, false);

        var items = new (string path, string label)[]
        {
            ($"{PickupDir}/Pickup_DoubleJump.prefab", "DOUBLE JUMP"),
            ($"{PickupDir}/Pickup_Dash.prefab", "DASH"),
            ($"{PickupDir}/Pickup_Tether.prefab", "TETHER"),
            ($"{PickupDir}/Pickup_Skateboard.prefab", "SKATEBOARD"),
            (GravityAnchorPickupPath, "GRAVITY ANCHOR"),
        };

        for (int i = 0; i < items.Length; i++)
        {
            float x = -6f + 3f * i;
            Box($"Pedestal_{items[i].label}", _geometry, new(x - 0.6f, 0f, 3.4f), new(x + 0.6f, 0.4f, 4.6f), _blockMat);
            FloorLabel(items[i].label, new Vector3(x, 0f, 2.8f), 0f, 4.2f, Ink);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(items[i].path);
            if (prefab == null)
            {
                Debug.LogWarning($"Pickup prefab missing: {items[i].path}. Run the setup that makes it.");
                continue;
            }
            var pickup = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            pickup.transform.SetParent(group, false);
            pickup.transform.position = new Vector3(x, 1.3f, 4f);
            var ability = pickup.GetComponent<AbilityPickup>();
            if (ability != null)
            {
                var so = new SerializedObject(ability);
                so.FindProperty("consumeOnPickup").boolValue = false;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }
    }

    /// <summary>Floating anchors: a pair over the sprint field to swing
    /// between, and a chain across the void to the south island.</summary>
    static void BuildAnchors(Transform root)
    {
        var group = new GameObject("TetherAnchors").transform;
        group.SetParent(root, false);
        var mat = NewtonFightArt.LitMaterial(ArtDir, "TrainingAnchor",
            new Color(0.22f, 0.3f, 0.36f), 0.6f, new Color(0.22f, 0.3f, 0.36f));

        var spots = new[]
        {
            new Vector3(-6f, 6.5f, -17f), new Vector3(6f, 6.5f, -17f),
            new Vector3(0f, 6f, -33f), new Vector3(0f, 7f, -40f), new Vector3(0f, 6f, -47f),
        };
        for (int i = 0; i < spots.Length; i++)
        {
            var go = new GameObject($"TetherAnchor_{i}");
            go.transform.SetParent(group, false);
            go.transform.position = spots[i];

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Visual";
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.transform.SetParent(go.transform, false);
            ball.transform.localScale = Vector3.one * 0.55f;
            var renderer = ball.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(go.transform, false);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 7f;
            glow.color = new Color(0.45f, 0.85f, 1f);
            glow.intensity = 1.2f;

            var anchor = go.AddComponent<TetherAnchor>();
            var so = new SerializedObject(anchor);
            so.FindProperty("attachPoint").objectReferenceValue = go.transform;
            so.FindProperty("glow").objectReferenceValue = glow;
            so.FindProperty("maxRange").floatValue = AnchorRange;
            so.FindProperty("highlightRenderer").objectReferenceValue = renderer;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    // ── Player, HUD, cleanup ───────────────────────────────────────────────

    static void PlacePlayer(ChibiLocomotion player, Transform spawn)
    {
        player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

        var cam = Camera.main;
        if (cam == null) return;
        cam.transform.SetPositionAndRotation(spawn.position + new Vector3(0f, 1.6f, -4.6f),
                                             Quaternion.Euler(12f, 0f, 0f));
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 500f);
    }

    /// <summary>Points everything that tracked Newton (his health bar and
    /// damage numbers) at the dummies, and drops what only makes sense for
    /// him. Returns the HUD canvas.</summary>
    static Transform RebindHud(List<Health> dummies)
    {
        var boss = Object.FindFirstObjectByType<NewtonBoss>();
        var bossHealth = boss != null ? boss.GetComponent<Health>() : null;

        foreach (var bar in Object.FindObjectsByType<HealthBarView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var so = new SerializedObject(bar);
            var source = so.FindProperty("source");
            if (bossHealth == null || source.objectReferenceValue != bossHealth) continue;
            source.objectReferenceValue = dummies[0];
            so.ApplyModifiedPropertiesWithoutUndo();

            if (bar.transform.Find("Name")?.GetComponent<TMP_Text>() is { } plate) plate.text = "TRAINING DUMMY";
            foreach (var t in bar.GetComponentsInChildren<Transform>(true).Where(t => t.name == "PhaseNotch").ToList())
                Object.DestroyImmediate(t.gameObject);
        }

        var numbers = Object.FindFirstObjectByType<DamageNumbers>(FindObjectsInactive.Include);
        if (numbers != null)
        {
            var so = new SerializedObject(numbers);
            var sources = so.FindProperty("sources");
            Color colour = new(1f, 0.92f, 0.62f);
            for (int i = sources.arraySize - 1; i >= 0; i--)
            {
                var entry = sources.GetArrayElementAtIndex(i);
                var h = entry.FindPropertyRelative("health").objectReferenceValue;
                if (h != null && h != bossHealth) continue;
                if (h == bossHealth && bossHealth != null) colour = entry.FindPropertyRelative("colour").colorValue;
                sources.DeleteArrayElementAtIndex(i);
            }
            foreach (var dummy in dummies)
            {
                sources.arraySize++;
                var entry = sources.GetArrayElementAtIndex(sources.arraySize - 1);
                entry.FindPropertyRelative("health").objectReferenceValue = dummy;
                entry.FindPropertyRelative("colour").colorValue = colour;
                entry.FindPropertyRelative("showRangeScaling").boolValue = false;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var hud = GameObject.Find("FightHUD");
        if (hud == null)
        {
            hud = new GameObject("FightHUD", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            hud.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = hud.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }
        if (hud.transform.Find("NewtonBark") is { } bark) Object.DestroyImmediate(bark.gameObject);
        return hud.transform;
    }

    /// <summary>Speed / height / jump readout in the top-right corner, where
    /// the reference shows its timer, plus a one-line key hint.</summary>
    static TMP_Text BuildReadout(Transform hud)
    {
        var font = NewtonFightArt.Font("Oswald Bold SDF");
        var mat = NewtonFightArt.OutlinedFontMaterial(font, "HudLabel", 0.2f);

        var readout = HudText(hud, "TrainingReadout", font, mat, 30f, TextAlignmentOptions.TopRight,
                              new Vector2(-44f, -34f), new Vector2(900f, 200f));
        readout.text = "SPEED  0.0 m/s\nHEIGHT  0.00 m\nJUMP  -\nBEST  -";

        var hint = HudText(hud, "TrainingHint", font, mat, 20f, TextAlignmentOptions.TopRight,
                           new Vector2(-44f, -246f), new Vector2(900f, 40f));
        hint.text = "BACKSPACE  respawn     SHIFT + BACKSPACE  reset abilities";
        hint.color = new Color(1f, 1f, 1f, 0.8f);
        return readout;
    }

    static TextMeshProUGUI HudText(Transform hud, string name, TMP_FontAsset font, Material mat, float size,
                                   TextAlignmentOptions align, Vector2 pos, Vector2 box)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(hud, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = box;
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = font;
        text.fontSharedMaterial = mat;
        text.fontSize = size;
        text.alignment = align;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>Removes the academy, Newton, and the sandbox's own pickups and
    /// anchors; keeps the player, camera, light, post-processing and HUD.</summary>
    static void StripSandbox(ChibiLocomotion player)
    {
        var scene = player.gameObject.scene;
        var kept = new List<string>();
        foreach (var go in scene.GetRootGameObjects())
        {
            if (go == player.gameObject || go.name == "TrainingStage") continue;
            bool sandboxOnly = go.name.Contains("Academy")
                               || go.GetComponentInChildren<NewtonBoss>(true) != null
                               || go.GetComponentInChildren<AbilityPickup>(true) != null
                               || go.GetComponentInChildren<TetherAnchor>(true) != null;
            if (sandboxOnly) Object.DestroyImmediate(go);
            else kept.Add(go.name);
        }
        Debug.Log($"Kept from ChibiSandbox: {player.name}, {string.Join(", ", kept)}");
    }

    static void ApplyLighting()
    {
        // Flat, bright and fog-free, like the reference: the grid has to stay
        // readable at 100 m.
        RenderSettings.fog = false;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.86f, 0.88f, 0.94f);
        RenderSettings.ambientEquatorColor = new Color(0.72f, 0.74f, 0.78f);
        RenderSettings.ambientGroundColor = new Color(0.5f, 0.5f, 0.53f);

        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.88f, 0.9f, 0.94f);
        }

        var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
            .FirstOrDefault(l => l.type == LightType.Directional);
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            sun.color = Color.white;
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    static Transform Group(string name)
    {
        var t = new GameObject(name).transform;
        t.SetParent(_geometry, false);
        return t;
    }

    /// <summary>An axis-aligned block from its two corners.</summary>
    static GameObject Box(string name, Transform parent, Vector3 min, Vector3 max, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = (min + max) * 0.5f;
        go.transform.localScale = max - min;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        go.isStatic = true;
        return go;
    }

    /// <summary>A wedge rising along +Z from <paramref name="foot"/>: width
    /// across X, <paramref name="height"/> up, <paramref name="run"/> along Z.</summary>
    static void Ramp(string name, Transform parent, Vector3 foot, float width, float height, float run, Material mat)
    {
        var mesh = WedgeMesh();
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = foot;
        go.transform.localScale = new Vector3(width, height, run);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var col = go.AddComponent<MeshCollider>();
        col.sharedMesh = mesh;
        col.convex = true;
        go.isStatic = true;
    }

    /// <summary>Unit wedge, x -0.5..0.5, y 0..1, z 0..1, sloping up to the
    /// back. Flat-shaded, so each face has its own vertices.</summary>
    static Mesh WedgeMesh()
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(WedgePath);
        if (mesh != null) return mesh;

        Vector3 a = new(-0.5f, 0f, 0f), b = new(0.5f, 0f, 0f);
        Vector3 c = new(-0.5f, 0f, 1f), d = new(0.5f, 0f, 1f);
        Vector3 e = new(-0.5f, 1f, 1f), f = new(0.5f, 1f, 1f);

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();

        void Face(Vector3 normal, params Vector3[] corners)
        {
            int start = verts.Count;
            verts.AddRange(corners);
            for (int i = 0; i < corners.Length; i++) normals.Add(normal);
            for (int i = 1; i + 1 < corners.Length; i++)
            {
                // Unity's front face is the one cross(p1-p0, p2-p0) points at.
                Vector3 n = Vector3.Cross(corners[i] - corners[0], corners[i + 1] - corners[0]);
                if (Vector3.Dot(n, normal) >= 0f) tris.AddRange(new[] { start, start + i, start + i + 1 });
                else tris.AddRange(new[] { start, start + i + 1, start + i });
            }
        }

        Face(Vector3.down, a, b, d, c);
        Face(Vector3.forward, c, d, f, e);
        Face(new Vector3(0f, 1f, -1f).normalized, a, b, f, e);
        Face(Vector3.left, a, c, e);
        Face(Vector3.right, b, d, f);

        mesh = new Mesh { name = "Wedge" };
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, WedgePath);
        return mesh;
    }

    static TextMeshPro Label(string text, Vector3 pos, Quaternion rot, float size, Color colour)
    {
        var go = new GameObject($"Label {text}");
        go.transform.SetParent(_labels, false);
        go.transform.SetPositionAndRotation(pos, rot);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = _font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = colour;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(12f, 2f);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return tmp;
    }

    /// <summary>A label lying on the floor; <paramref name="yaw"/> 0 reads
    /// from the spawn looking north, 90 reads looking east.</summary>
    static void FloorLabel(string text, Vector3 at, float yaw, float size, Color colour) =>
        Label(text, new Vector3(at.x, at.y + 0.012f, at.z), Quaternion.Euler(90f, yaw, 0f), size, colour);
}
