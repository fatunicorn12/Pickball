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
/// Tools ▸ Pickleball ▸ Build Academy Greybox. Builds
/// <c>Scenes/AcademyGreybox.unity</c>: the hub world of
/// <c>Docs/World/TheAcademy.md</c> in grey boxes, to play and rearrange before
/// any art exists.
///
/// <b>The player comes from ChibiSandbox</b>, as in Build Training Stage: the
/// scene starts as a copy with the academy model, Newton, the pickups and the
/// tether anchors stripped out, so the chibi, camera, HUD, dialogue and Birdy
/// arrive exactly as wired there. The greybox is regenerated each run, so hand
/// edits to the scene are lost; change the numbers here instead.
///
/// <b>Layout</b> (X east, Z north toward the black sun, courtyard lawn at y = 0):
///  * Centre: the courtyard (spawn, Birdy, the Well) inside a stoa ring whose
///    roof is walkable at 5 m, with crates up to it and Birdy's Perch on the
///    bell tower.
///  * North: the Gnomon Plaza (sundial, eight empty plinths), then the
///    Overlook: Zeno's Bridge, the Surveyor's Gate, the fraying rim, and the
///    hanging orchard and Euler's door under its cantilever.
///  * West: the amphitheatre (Pythagoras), the Broken Bridge to the library
///    (Hypatia), and the olive terraces with the aqueduct skate run.
///  * East: the Orrery Chasm (Al-Khwarizmi), the Mirror Court (Fibonacci) and
///    the Nautilus Tower (Newton).
///  * South: the Gymnasium (skateboard, debug shelf), then the Delta Pylons
///    down to Galois's door.
///  * Far off: the seven era islands on a spiral, and the black sun.
///
/// <b>Gates are set against the measured base kit</b> (playtests R2/R3 and
/// MovementConfig): triple jump 6 m apex and 11.2 m carry, tether 9 m. Each
/// sits clear of it, and each Guardian's door carries that Guardian's reward
/// as a stand-in pickup, so the chain can be walked with the abilities that
/// exist today. The Gymnasium's debug shelf holds every one. Abilities not
/// built yet (Equation Swap, Spiral Jump, Network Step, Symmetry Break, Limit
/// Approach) only have their gates.
///
/// Every surface wears the training grid, so heights read off the scenery,
/// and the TrainingStage component supplies the respawn (Backspace, or
/// falling off) and the jump readout.
/// </summary>
public static class AcademyGreyboxSetup
{
    const string SourceScenePath = "Assets/_Project/Scenes/ChibiSandbox.unity";
    const string ScenePath = "Assets/_Project/Scenes/AcademyGreybox.unity";
    const string ArtDir = "Assets/_Project/Art/World/Greybox";
    const string GridShaderName = "Pickball/Training Grid";
    const string WedgePath = ArtDir + "/Wedge.asset";
    const string PickupDir = "Assets/_Project/Prefabs/Pickups";
    const string GravityAnchorPickupPath = "Assets/_Project/Prefabs/Boss/Pickup_GravityAnchor.prefab";
    const string TelescopePath = "Assets/_Project/Prefabs/Tripo/Telescope.prefab";

    /// <summary>Spawn in the courtyard, facing north: the first shot is the
    /// gnomon over the roofs and the black sun on the horizon.</summary>
    static readonly Vector3 SpawnPosition = new(0f, 0.05f, -5f);
    static readonly Vector3 BirdyPosition = new(-3.5f, 0f, 0f);

    /// <summary>The stoa roof's walkable top.</summary>
    const float RoofTop = 5f;
    /// <summary>Birdy's Perch: 7 m above the roof. 1 m past the triple jump's
    /// 6 m, 1 m inside triple jump plus feather (about 8 m).</summary>
    const float PerchTop = RoofTop + 7f;
    /// <summary>The Broken Bridge: past the triple jump's 11.2 m carry.</summary>
    const float BridgeGap = 14f;
    /// <summary>Tether latch range, matching the sandbox's anchors.</summary>
    const float AnchorRange = 9f;

    /// <summary>Falling below this, or straying this far from the courtyard,
    /// respawns. The delta sits at -8 m and the island's underside at -32 m.</summary>
    const float BlastZoneY = -45f, BlastZoneRadius = 180f;

    const float RegionSignSize = 11f, GateSignSize = 4f;

    /// <summary>Seeds the scatter (shards, trees), so every build is identical.</summary>
    const int Seed = 1729;

    // Guardian colour pairs (StoryBrief §6; Newton's is the design doc's proposal).
    static readonly Color Blue = new(0.2f, 0.38f, 0.85f), Gold = new(0.95f, 0.75f, 0.25f);
    static readonly Color Indigo = new(0.3f, 0.22f, 0.62f), Silver = new(0.8f, 0.82f, 0.88f);
    static readonly Color Emerald = new(0.1f, 0.58f, 0.38f);
    static readonly Color Mustard = new(0.88f, 0.68f, 0.18f), Leaf = new(0.42f, 0.68f, 0.25f);
    static readonly Color AppleRed = new(0.8f, 0.16f, 0.14f), Brass = new(0.78f, 0.6f, 0.3f);
    static readonly Color Brown = new(0.5f, 0.33f, 0.2f), Teal = new(0.12f, 0.58f, 0.6f);
    static readonly Color Navy = new(0.12f, 0.16f, 0.42f), Red = new(0.82f, 0.14f, 0.16f);
    static readonly Color Violet = new(0.5f, 0.26f, 0.72f);

    /// <summary>The seven corners of the Margin on their spiral: bearing in
    /// degrees from north (east positive), distance and height. Hypatia's is
    /// nearest; Riemann's is farthest out and closest to the black sun. All
    /// float well clear of the hub, so none reads as somewhere to walk.</summary>
    static readonly (string title, float bearing, float distance, float height, Color main, Color accent)[] Eras =
    {
        ("HYPATIA\nALEXANDRIA", -80f, 220f, 15f, Indigo, Silver),
        ("AL-KHWARIZMI\nBAGHDAD", -60f, 270f, 22f, Emerald, Gold),
        ("FIBONACCI\nPISA", -41f, 330f, 30f, Mustard, Leaf),
        ("NEWTON\nCAMBRIDGE", -24f, 400f, 40f, AppleRed, Brass),
        ("EULER\nBASEL", -8f, 480f, 50f, Brown, Teal),
        ("GALOIS\nPARIS", 6f, 570f, 62f, Navy, Red),
        ("RIEMANN\nGOTTINGEN", 18f, 670f, 76f, Violet, Silver),
    };
    /// <summary>The black sun, 16° up in the north-north-east: high enough to
    /// clear the stoa roof in the first shot from the spawn, and near enough
    /// that the haze leaves it black.</summary>
    const float BlackSunBearing = 24f, BlackSunDistance = 450f, BlackSunHeight = 130f, BlackSunRadius = 45f;

    static Transform _geometry, _signs, _group;
    static Material _lawn, _paving, _stone, _roof, _rock, _sand;
    static Material _parchment, _ink, _iron, _glass, _veil, _black, _white, _olive, _apple;
    static readonly Dictionary<Color, Material> Solid = new();
    static TMP_FontAsset _font;
    static Material _fontMat;
    static System.Random _rng;

    [MenuItem("Tools/Pickleball/Build Academy Greybox")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("Leave Play mode before building the academy greybox.");
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

        StripSandbox(player);

        ChibiSandboxSetup.EnsureFolderPublic(ArtDir);
        LoadArt();
        _rng = new System.Random(Seed);

        var root = new GameObject("AcademyGreybox");
        _geometry = new GameObject("Geometry").transform;
        _geometry.SetParent(root.transform, false);
        _signs = new GameObject("Signs").transform;
        _signs.SetParent(root.transform, false);

        BuildCourtyard();
        BuildGymnasium();
        BuildPlaza();
        BuildOverlook();
        BuildUnderside();
        BuildWest();
        BuildEast();
        BuildDelta();
        BuildVistas();
        BuildPickups(root.transform);
        BuildAnchors(root.transform);

        var spawn = new GameObject("PlayerSpawn").transform;
        spawn.SetParent(root.transform, false);
        spawn.SetPositionAndRotation(SpawnPosition, Quaternion.identity);
        PlacePlayer(player, spawn);
        PlaceBirdy();

        var stage = root.AddComponent<TrainingStage>();
        var so = new SerializedObject(stage);
        so.FindProperty("player").objectReferenceValue = player.GetComponent<CharacterController>();
        so.FindProperty("spawnPoint").objectReferenceValue = spawn;
        so.FindProperty("readout").objectReferenceValue = BuildReadout();
        so.FindProperty("blastZoneY").floatValue = BlastZoneY;
        so.FindProperty("blastZoneRadius").floatValue = BlastZoneRadius;
        so.ApplyModifiedPropertiesWithoutUndo();

        ApplyLighting();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        Selection.activeGameObject = player.gameObject;
        Debug.Log($"Academy greybox built: {ScenePath}. Backspace respawns, Shift+Backspace also takes " +
                  "the abilities back; the Gymnasium's debug shelf has every one.");
    }

    // ── Art ─────────────────────────────────────────────────────────────────

    static void LoadArt()
    {
        _lawn = Grid("GreyboxLawn", new Color(0.6f, 0.75f, 0.5f));
        _paving = Grid("GreyboxPaving", new Color(0.9f, 0.87f, 0.79f));
        _stone = Grid("GreyboxStone", new Color(0.84f, 0.82f, 0.78f));
        _roof = Grid("GreyboxRoof", new Color(0.8f, 0.5f, 0.38f));
        _rock = Grid("GreyboxRock", new Color(0.6f, 0.55f, 0.5f));
        _sand = Grid("GreyboxSand", new Color(0.86f, 0.78f, 0.62f));

        _parchment = NewtonFightArt.LitMaterial(ArtDir, "GreyboxParchment", new Color(0.97f, 0.95f, 0.89f), 0.1f,
                                                new Color(0.3f, 0.29f, 0.26f));
        _ink = NewtonFightArt.LitMaterial(ArtDir, "GreyboxInk", new Color(0.08f, 0.08f, 0.1f), 0.2f);
        _iron = NewtonFightArt.LitMaterial(ArtDir, "GreyboxIron", new Color(0.18f, 0.18f, 0.2f), 0.5f);
        _olive = NewtonFightArt.LitMaterial(ArtDir, "GreyboxOlive", new Color(0.45f, 0.55f, 0.32f), 0.15f);
        _apple = NewtonFightArt.LitMaterial(ArtDir, "GreyboxApple", AppleRed, 0.5f);
        _glass = Glass("GreyboxGlass", new Color(0.55f, 0.9f, 0.75f, 0.3f));
        _veil = Glass("GreyboxVeil", new Color(0.62f, 0.4f, 0.9f, 0.35f));
        _black = Unlit("GreyboxBlackSun", Color.black);
        _white = Unlit("GreyboxHorizon", Color.white);

        Solid.Clear();
        _font = NewtonFightArt.Font("Oswald Bold SDF");
        _fontMat = NewtonFightArt.OutlinedFontMaterial(_font, "HudLabel", 0.2f);
    }

    /// <summary>The training grid in a greybox colour: lines a shade darker
    /// than the base, no red axes. Created once; afterwards the asset is left
    /// alone, so colours tuned live in the inspector survive a rebuild.</summary>
    static Material Grid(string name, Color baseColour)
    {
        string path = $"{ArtDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat != null) return mat;

        var shader = Shader.Find(GridShaderName);
        if (shader == null)
        {
            Debug.LogError($"Shader '{GridShaderName}' not found; is Art/World/Training/TrainingGrid.shader imported?");
            shader = Shader.Find("Universal Render Pipeline/Lit");
        }
        mat = new Material(shader) { name = name };
        mat.SetColor("_BaseColor", baseColour);
        mat.SetColor("_FineColor", Color.Lerp(baseColour, Color.black, 0.05f));
        mat.SetColor("_MinorColor", Color.Lerp(baseColour, Color.black, 0.16f));
        mat.SetColor("_MajorColor", Color.Lerp(baseColour, Color.black, 0.3f));
        mat.SetColor("_AxisColor", Color.clear);
        mat.SetColor("_ShadeColor", Color.Lerp(baseColour, new Color(0.38f, 0.4f, 0.58f), 0.45f));
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// <summary>One flat Lit colour per Guardian colour, with a faint glow so
    /// gates read against the grid.</summary>
    static Material SolidColour(Color colour)
    {
        if (Solid.TryGetValue(colour, out var mat) && mat != null) return mat;
        string hex = ColorUtility.ToHtmlStringRGB(colour);
        mat = NewtonFightArt.LitMaterial(ArtDir, $"GreyboxGate_{hex}", colour, 0.35f, colour * 0.18f);
        Solid[colour] = mat;
        return mat;
    }

    static Material Unlit(string name, Color colour)
    {
        string path = $"{ArtDir}/{name}.mat";
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", colour);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>URP Lit switched to alpha-blended transparency.</summary>
    static Material Glass(string name, Color colour)
    {
        var mat = NewtonFightArt.LitMaterial(ArtDir, name, colour, 0.9f);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ── The courtyard and the stoa ─────────────────────────────────────────

    static void BuildCourtyard()
    {
        // The core plateau: courtyard, gymnasium and the paths east and west.
        Group("Core");
        Plateau("Core", -36f, 40f, -50f, 50f, 0f, _lawn, 28f);

        Group("Courtyard");
        // Stoa back walls, 4.5 m, with a 6 m gate in each side under a lintel.
        Box("WallNorthW", new(-19f, 0f, 15f), new(-3f, 4.5f, 16f), _stone);
        Box("WallNorthE", new(3f, 0f, 15f), new(19f, 4.5f, 16f), _stone);
        Box("GateNorth", new(-3f, 4f, 15f), new(3f, 4.5f, 16f), _stone);
        Box("WallSouthW", new(-19f, 0f, -16f), new(-3f, 4.5f, -15f), _stone);
        Box("WallSouthE", new(3f, 0f, -16f), new(19f, 4.5f, -15f), _stone);
        Box("GateSouth", new(-3f, 4f, -16f), new(3f, 4.5f, -15f), _stone);
        Box("WallWestS", new(-19f, 0f, -15f), new(-18f, 4.5f, -3f), _stone);
        Box("WallWestN", new(-19f, 0f, 3f), new(-18f, 4.5f, 15f), _stone);
        Box("GateWest", new(-19f, 4f, -3f), new(-18f, 4.5f, 3f), _stone);
        Box("WallEastS", new(18f, 0f, -15f), new(19f, 4.5f, -3f), _stone);
        Box("WallEastN", new(18f, 0f, 3f), new(19f, 4.5f, 15f), _stone);
        Box("GateEast", new(18f, 4f, -3f), new(19f, 4.5f, 3f), _stone);

        // The roof ring: 5 m deep over the colonnade, walkable on top.
        Box("RoofNorth", new(-19f, RoofTop - 0.5f, 11f), new(19f, RoofTop, 16f), _roof);
        Box("RoofSouth", new(-19f, RoofTop - 0.5f, -16f), new(19f, RoofTop, -11f), _roof);
        Box("RoofWest", new(-19f, RoofTop - 0.5f, -11f), new(-14f, RoofTop, 11f), _roof);
        Box("RoofEast", new(14f, RoofTop - 0.5f, -11f), new(19f, RoofTop, 11f), _roof);

        for (float x = -16f; x <= 16f; x += 4f)
        {
            Cylinder($"ColumnN_{x:0}", new(x, 0f, 11.5f), 0.45f, RoofTop - 0.5f, _stone);
            Cylinder($"ColumnS_{x:0}", new(x, 0f, -11.5f), 0.45f, RoofTop - 0.5f, _stone);
        }
        for (float z = -8f; z <= 8f; z += 4f)
        {
            Cylinder($"ColumnW_{z:0}", new(-14.5f, 0f, z), 0.45f, RoofTop - 0.5f, _stone);
            Cylinder($"ColumnE_{z:0}", new(14.5f, 0f, z), 0.45f, RoofTop - 0.5f, _stone);
        }

        // Crates up to the south roof: 1.25 m steps, each a single jump.
        Box("Crate_1", new(-6f, 0f, -10.5f), new(-4f, 1.25f, -8.5f), _sand);
        Box("Crate_2", new(-8f, 0f, -10.5f), new(-6f, 2.5f, -8.5f), _sand);
        Box("Crate_3", new(-10f, 0f, -10.5f), new(-8f, 3.75f, -8.5f), _sand);

        // Bell tower on the north-east corner; its top is Birdy's Perch.
        Box("BellTower", new(14.5f, 0f, 11.5f), new(19f, PerchTop, 16f), _stone);
        Box("PerchCache", new(16.4f, PerchTop, 13.4f), new(17.1f, PerchTop + 0.7f, 14.1f), SolidColour(Gold), collide: false);
        Sign("BIRDY'S PERCH\n7 m above the roof · the triple jump reaches 6 m\nneeds BIRDY'S FEATHER",
             new(16.75f, PerchTop + 3f, 13.75f), GateSignSize);
        Sign("STOA ROOFTOPS", new(-16.5f, RoofTop + 3f, -13.5f), GateSignSize);

        // The Well: an octagonal kerb and a grate over a shaft through the island.
        var well = new Vector3(8f, 0f, -4f);
        for (int i = 0; i < 8; i++)
        {
            float a = i * 45f;
            var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
            OrientedBox($"WellKerb_{i}", well + dir * 1.6f + Vector3.up * 0.45f, new(1.35f, 0.9f, 0.35f),
                        Quaternion.LookRotation(dir), _stone);
        }
        Cylinder("WellShaft", well + Vector3.up * 0.01f, 1.45f, 0.02f, _black, collide: false);
        Box("WellGrate", well + new Vector3(-1.45f, 0.72f, -1.45f), well + new Vector3(1.45f, 0.82f, 1.45f), _iron);
        Sign("THE WELL\nright through the island · needs GRAVITY ANCHOR", well + Vector3.up * 3.2f, GateSignSize);

        // Plato's Academy motto, over the north gate on the courtyard side.
        Text("LET NO ONE IGNORANT OF GEOMETRY ENTER", new(0f, 4.25f, 14.97f), Quaternion.identity, 3.2f);
        Sign("THE COURTYARD", new(0f, 9f, 0f), RegionSignSize);
    }

    static void BuildGymnasium()
    {
        Group("Gymnasium");
        Sign("GYMNASIUM", new(0f, 7f, -32f), RegionSignSize);
        Parapet("ParapetSouth", -36f, 40f, -50f, alongX: true);

        // A side door under a mosaic seal: an optional pocket for Symmetry Break.
        Box("SealWall", new(11f, 0f, -48.5f), new(17f, 4.5f, -47.5f), _stone);
        Doorway("SealedDoor", new(14f, 0f, -47.5f), 3f, 3.5f, Quaternion.identity, _stone, SolidColour(Navy));
        Sign("MOSAIC SEAL\nneeds SYMMETRY BREAK", new(14f, 6f, -47f), GateSignSize);

        // The aqueduct's head: a 5 m platform, crates up to it.
        Box("AqueductHead", new(-34f, 0f, -48f), new(-30f, 5f, -44f), _stone);
        Box("AqueductCrate_1", new(-26f, 0f, -47f), new(-24f, 1.25f, -45f), _sand);
        Box("AqueductCrate_2", new(-28f, 0f, -47f), new(-26f, 2.5f, -45f), _sand);
        Box("AqueductCrate_3", new(-30f, 0f, -47f), new(-28f, 3.75f, -45f), _sand);
    }

    // ── North: the plaza and the Overlook ──────────────────────────────────

    static void BuildPlaza()
    {
        Group("GnomonPlaza");
        var centre = new Vector3(0f, 0f, 36f);
        const float top = 1f;
        Cylinder("Plaza", centre, 15f, top, _paving);
        Slab("PlazaRamp", new(0f, 0f, 16.5f), new(0f, top, 21.5f), 6f, 1f, _paving);

        // The gnomon: a right triangle in profile, 16 m tall over a 12 m base,
        // turned to show that profile to the courtyard. At 53° it is past the
        // controller's slope limit.
        Wedge("Gnomon", centre + new Vector3(-6f, top, 0f), 1.4f, 16f, 12f, _stone, Quaternion.Euler(0f, 90f, 0f));

        // Hour lines fanning south from the gnomon's foot, short of the plaza's edge.
        for (int i = -3; i <= 3; i++)
        {
            var rot = Quaternion.Euler(0f, 180f + i * 18f, 0f);
            OrientedBox($"HourLine_{i + 3}", centre + new Vector3(0f, top + 0.012f, 0f) + rot * Vector3.forward * 6.5f,
                        new(0.15f, 0.02f, 13f), rot, SolidColour(Gold), collide: false);
        }

        // Eight empty plinths, one per Guardian, clockwise from the west.
        string[] names = { "PYTHAGORAS", "HYPATIA", "AL-KHWARIZMI", "FIBONACCI", "NEWTON", "EULER", "GALOIS", "RIEMANN" };
        Color[] colours = { Blue, Indigo, Emerald, Mustard, AppleRed, Teal, Navy, Violet };
        for (int i = 0; i < names.Length; i++)
        {
            float a = (202.5f - 45f * i) * Mathf.Deg2Rad;
            var at = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 11f;
            Box($"Plinth_{names[i]}", at + new Vector3(-1f, top, -1f), at + new Vector3(1f, top + 1.5f, 1f), _stone);
            Box($"PlinthBand_{names[i]}", at + new Vector3(-1.05f, top + 1.2f, -1.05f),
                at + new Vector3(1.05f, top + 1.45f, 1.05f), SolidColour(colours[i]), collide: false);
            Sign(names[i], at + Vector3.up * (top + 2.6f), 4f);
        }
        Sign("GNOMON PLAZA\none plinth per Guardian", centre + new Vector3(0f, 21f, 0f), RegionSignSize);
    }

    static void BuildOverlook()
    {
        Group("Overlook");
        // A 3 m cantilever past the island's body, so its underside shows.
        Box("Overlook", new(-36f, -3f, 50f), new(40f, 0f, 60f), _paving);
        Parapet("ParapetNorthW", -36f, -2.1f, 60f, alongX: true);
        Parapet("ParapetNorthE", 2.1f, 40f, 60f, alongX: true);
        Sign("THE OVERLOOK", new(0f, 8f, 54f), RegionSignSize);

        var telescope = AssetDatabase.LoadAssetAtPath<GameObject>(TelescopePath);
        if (telescope != null)
        {
            var t = (GameObject)PrefabUtility.InstantiatePrefab(telescope);
            t.transform.SetParent(_group, false);
            t.transform.SetPositionAndRotation(new Vector3(14f, 0f, 57.5f), Quaternion.identity);
        }
        Sign("TELESCOPE\na look at each era island", new(14f, 4f, 57.5f), 4f);

        // Zeno's Bridge: each plank half the last, behind a see-through veil
        // (so the view north stays open) until Limit Approach. Today the planks
        // just end; a jump over the veil falls and respawns.
        float z = 60.4f, length = 8f;
        for (int i = 0; i < 7; i++)
        {
            Box($"ZenoPlank_{i}", new(-1.5f, -0.4f, z), new(1.5f, 0f, z + length), _stone);
            z += length + 0.5f;
            length *= 0.5f;
        }
        Doorway("ZenoGate", new(0f, 0f, 59.8f), 3f, 4.5f, Quaternion.identity, SolidColour(Violet), _veil);
        Sign("ZENO'S BRIDGE\neach plank half the last · needs LIMIT APPROACH", new(0f, 7f, 59.8f), GateSignSize);

        // Riemann's door at the west end, sealed by a mosaic.
        Doorway("SurveyorsGate", new(-31f, 0f, 55f), 4f, 8f, Quaternion.identity, SolidColour(Violet), SolidColour(Navy));
        Sign("SURVEYOR'S GATE · RIEMANN\nmosaic seal · needs SYMMETRY BREAK", new(-31f, 11f, 55f), GateSignSize);

        // The fraying rim: paper shards and stray construction lines drifting
        // north toward the black sun, smaller the farther they go. No colliders:
        // white is out of bounds.
        for (int i = 0; i < 70; i++)
        {
            float sz = R(62f, 112f), t01 = (sz - 62f) / 50f;
            var at = new Vector3(R(-45f, 50f), R(-14f, 9f), sz);
            float size = Mathf.Lerp(2.8f, 0.35f, t01) * R(0.6f, 1.2f);
            var shape = i % 3 == 0 ? new Vector3(size, size * 0.15f, size * 1.4f) : Vector3.one * size;
            OrientedBox($"Shard_{i}", at, shape, Random3(), _parchment, collide: false);
        }
        for (int i = 0; i < 16; i++)
        {
            var at = new Vector3(R(-40f, 45f), R(-8f, 12f), R(60f, 100f));
            OrientedBox($"ConstructionLine_{i}", at, new(0.05f, 0.05f, R(4f, 14f)), Random3(), _ink, collide: false);
        }
    }

    /// <summary>The hanging orchard and Euler's door under the Overlook's
    /// cantilever, seen side-on from the library and the orrery ledge.</summary>
    static void BuildUnderside()
    {
        Group("Underside");
        for (int i = 0; i < 7; i++)
        {
            var root = new Vector3(-22f + i * 7f + R(-1.5f, 1.5f), -3f, R(51f, 58f));
            float trunk = R(2f, 3.2f);
            Cylinder($"HangingTrunk_{i}", root - Vector3.up * trunk, 0.3f, trunk, _rock, collide: false);
            var crown = root - Vector3.up * (trunk + 1.4f);
            Sphere($"HangingCrown_{i}", crown, 1.9f, _olive, collide: false);
            for (int k = 0; k < 3; k++)
                Sphere($"Apple_{i}_{k}", crown + Random3() * Vector3.forward * 1.8f, 0.22f, _apple, collide: false);
        }
        var door = Quaternion.Euler(0f, 0f, 180f);   // upside down
        Doorway("EulerDoor", new(-14f, -3f, 55f), 4f, 6f, door, SolidColour(Teal), SolidColour(Brown), collide: false);
        Sign("THE UNDERSIDE · EULER\ndrops NETWORK STEP", new(-14f, -12f, 55f), GateSignSize);
    }

    // ── West: amphitheatre, Broken Bridge, library, terraces ──────────────

    static void BuildWest()
    {
        Group("Amphitheatre");
        var arena = new Vector3(-58f, 0f, 0f);
        Plateau("Amphitheatre", -80f, -36f, -22f, 22f, 0f, _paving, 20f);
        Cylinder("Arena", arena, 12f, 0.15f, _sand);
        // Three tiers of seats round the west half, facing the courtyard.
        for (int tier = 0; tier < 3; tier++)
        {
            float inner = 13f + 2.5f * tier, outer = inner + 2.5f, top = 1f + tier;
            for (float a = 100f; a < 260f; a += 10f)
            {
                float mid = (a + 5f) * Mathf.Deg2Rad;
                var dir = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                float width = 2f * outer * Mathf.Sin(5f * Mathf.Deg2Rad) + 0.2f;
                OrientedBox($"Seat_{tier}_{a:0}", arena + dir * (inner + outer) * 0.5f + Vector3.up * top * 0.5f,
                            new(width, top, 2.5f), Quaternion.LookRotation(dir), _stone);
            }
        }
        Sign("AMPHITHEATRE\nPYTHAGORAS fights here · drops ANGLE DASH", arena + Vector3.up * 9f, GateSignSize);

        Group("BrokenBridge");
        // The near stub juts 5 m out; the far side is the library's own edge.
        Box("BridgeNear", new(-41f, -1f, 46f), new(-36f, 0f, 50f), _stone);
        float far = -41f - BridgeGap;
        for (int i = 0; i < 4; i++)
            OrientedBox($"FallenSpan_{i}", new(-44f - i * 2.6f, R(-16f, -9f), 48f + R(-1.5f, 1.5f)),
                        new(2.4f, 1f, 3.6f), Random3(), _stone, collide: false);
        Sign($"BROKEN BRIDGE\n{BridgeGap:0} m gap · the triple jump carries 11 m\nneeds ANGLE DASH",
             new(-48f, 5f, 48f), GateSignSize);

        Group("LibraryWing");
        Plateau("Library", -86f, far, 38f, 62f, 0f, _paving, 18f);
        Box("Library", new(-84f, 0f, 42f), new(-64f, 10f, 60f), _stone);
        Box("LibraryCornice", new(-84.3f, 10f, 41.7f), new(-63.7f, 11f, 60.3f), SolidColour(Indigo));
        Sphere("LibraryDome", new(-74f, 11f, 51f), 6f, SolidColour(Indigo));
        for (float z = 43f; z <= 59f; z += 3.2f)
            Cylinder($"LibraryColumn_{z:0}", new(-62.5f, 0f, z), 0.5f, 9f, _stone);
        Box("LibraryDoor", new(-64.2f, 0f, 49f), new(-63.8f, 6f, 53f), SolidColour(Indigo));
        Sign("LIBRARY · HYPATIA\ndrops ORBITAL TETHER (stand-in: Tether)", new(-61f, 8f, 51f), GateSignSize);

        Group("OliveTerraces");
        Plateau("Terrace_1", -50f, -36f, -52f, -22f, -1.5f, _lawn, 12f);
        Plateau("Terrace_2", -64f, -50f, -52f, -22f, -3f, _lawn, 12f);
        Plateau("Terrace_3", -78f, -64f, -52f, -22f, -4.5f, _lawn, 12f);
        float[] tops = { -1.5f, -3f, -4.5f };
        for (int t = 0; t < 3; t++)
            for (int i = 0; i < 3; i++)
                OliveTree($"Olive_{t}_{i}", new(-43f - 14f * t + R(-3f, 3f), tops[t], -28f - 8f * i + R(-2f, 2f)));
        Sign("OLIVE TERRACES", new(-57f, 6f, -37f), RegionSignSize);

        // The aqueduct: a 3 m channel from the gymnasium's 5 m head down past
        // the terraces, 6.5 m over 36 m. Ride it on the board. At 6.2 m/s the
        // board is slower than a sprint, so it gates nothing.
        var head = new Vector3(-34f, 5f, -46f);
        var foot = new Vector3(-70f, -1.5f, -46f);
        Slab("Aqueduct", head, foot, 3f, 0.5f, _stone);
        Slab("AqueductSideS", head + new Vector3(0f, 0.8f, -1.65f), foot + new Vector3(0f, 0.8f, -1.65f), 0.3f, 1.3f, _stone);
        Slab("AqueductSideN", head + new Vector3(0f, 0.8f, 1.65f), foot + new Vector3(0f, 0.8f, 1.65f), 0.3f, 1.3f, _stone);
        for (float x = -38f; x >= -68f; x -= 6f)
        {
            float y = Mathf.Lerp(head.y, foot.y, (head.x - x) / (head.x - foot.x));
            Box($"AqueductPier_{-x:0}", new(x - 0.6f, -8f, -47f), new(x + 0.6f, y - 0.5f, -45f), _stone);
        }
        Sign("AQUEDUCT\nskate run", new(-46f, 9f, -46f), GateSignSize);

        // Down from the lowest terrace to the delta.
        Slab("DeltaRamp", new(-71f, -4.5f, -51.5f), new(-71f, -8f, -60.5f), 4f, 1f, _lawn);
    }

    // ── East: orrery, mirror court, nautilus tower ─────────────────────────

    static void BuildEast()
    {
        Group("OrreryChasm");
        Plateau("OrreryLedge", 66f, 86f, 28f, 52f, 0f, _paving, 16f);
        Doorway("WisdomArch", new(80.5f, 0f, 40f), 6f, 7f, Quaternion.Euler(0f, 90f, 0f), SolidColour(Gold), SolidColour(Emerald));
        Sign("HOUSE OF WISDOM · AL-KHWARIZMI\ndrops EQUATION SWAP", new(78f, 10f, 40f), GateSignSize);

        // The orrery: a brass pillar rising out of the chasm, a sun, and arms
        // out to the planets. The planets are the tether anchors (BuildAnchors).
        var sun = new Vector3(53f, 5.5f, 52f);
        Cylinder("OrreryPillar", new(53f, -40f, 52f), 1f, 44f, SolidColour(Brass));
        Sphere("OrrerySun", sun, 2f, SolidColour(Gold), collide: false);
        foreach (var planet in Planets)
        {
            var arm = planet - sun;
            OrientedBox("OrreryArm", (sun + planet) * 0.5f, new(0.15f, 0.15f, arm.magnitude),
                        Quaternion.LookRotation(arm), SolidColour(Brass), collide: false);
        }
        Sign("ORRERY CHASM\n26 m · needs ORBITAL TETHER\nthe planets will orbit; static for now", new(53f, 13f, 40f), GateSignSize);

        Group("MirrorCourt");
        Plateau("MirrorCourt", 40f, 70f, -14f, 14f, 0f, _paving, 18f);
        Box("MirrorGlass", new(54.8f, 0f, -14f), new(55.2f, 12f, 14f), _glass);
        Box("MirrorWallN", new(55.2f, 0f, 13.5f), new(70f, 12f, 14f), _stone);
        Box("MirrorWallS", new(55.2f, 0f, -14f), new(70f, 12f, -13.5f), _stone);
        Box("MirrorWallES", new(69.5f, 0f, -13.5f), new(70f, 12f, -2f), _stone);
        Box("MirrorWallEN", new(69.5f, 0f, 2f), new(70f, 12f, 13.5f), _stone);
        Box("MirrorWallLintel", new(69.5f, 5f, -2f), new(70f, 12f, 2f), _stone);
        Box("FibonacciDoor", new(69.55f, 0f, -2f), new(69.95f, 5f, 2f), SolidColour(Mustard));
        // A mirrored pair: what stands on your side stands on the other.
        Box("MirrorPedestal", new(46.4f, 0f, 5.4f), new(47.6f, 1f, 6.6f), SolidColour(Emerald));
        Box("MirrorPedestalTwin", new(62.4f, 0f, 5.4f), new(63.6f, 1f, 6.6f), SolidColour(Emerald));
        Sign("MIRROR COURT\nswap to your mirror point · needs EQUATION SWAP", new(55f, 14f, 0f), GateSignSize);
        Sign("FIBONACCI'S GARDEN GATE\ndrops SPIRAL JUMP", new(66f, 7f, 0f), GateSignSize);

        Group("NautilusTower");
        Plateau("NautilusYard", 40f, 72f, -56f, -24f, 0f, _lawn, 18f);
        var tower = new Vector3(56f, 0f, -40f);
        const float discBottom = 10.75f, discTop = 11.35f, platformTop = 26.85f;
        Cylinder("Tower", tower, 4f, platformTop - 0.6f, _stone);
        // Spiral ledges to 8.75 m, then an overhang 12 m wide that a straight
        // jump can't round, then more ledges to the observatory.
        float angle = -90f;
        for (int k = 1; k <= 5; k++) TowerLedge($"LedgeLow_{k}", tower, angle += 55f, 1.75f * k);
        Cylinder("Overhang", tower + Vector3.up * discBottom, 12f, discTop - discBottom, _roof);
        for (int k = 1; k <= 8; k++) TowerLedge($"LedgeHigh_{k}", tower, angle += 55f, discTop + 1.75f * k);
        Cylinder("ObservatoryFloor", tower + Vector3.up * (platformTop - 0.6f), 6f, 0.6f, _paving);
        Sphere("ObservatoryDome", tower + new Vector3(0f, platformTop, 1.5f), 2.6f, SolidColour(AppleRed));
        Box("NewtonDoor", tower + new Vector3(-1.5f, platformTop, -4.5f), tower + new Vector3(1.5f, platformTop + 3.5f, -4.1f),
            SolidColour(Brass));
        Sign("NAUTILUS TOWER\nledges hide behind the overhang · needs SPIRAL JUMP", tower + new Vector3(0f, 6f, -10f), GateSignSize);
        Sign("OBSERVATORY · NEWTON\ndrops GRAVITY ANCHOR", tower + Vector3.up * (platformTop + 6f), GateSignSize);
    }

    static void TowerLedge(string name, Vector3 tower, float degrees, float top)
    {
        var dir = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
        OrientedBox(name, tower + dir * 5.6f + Vector3.up * (top - 0.2f), new(2.6f, 0.4f, 2.6f), Quaternion.LookRotation(dir), _paving);
    }

    // ── South: the Delta Pylons ────────────────────────────────────────────

    static void BuildDelta()
    {
        Group("DeltaPylons");
        const float top = -8f;
        // Islets 16 m apart: past any jump, a graph waiting for Network Step.
        var islets = new (float x0, float x1, float z0, float z1)[]
        {
            (-80f, -64f, -74f, -60f), (-48f, -36f, -92f, -80f), (-20f, -8f, -104f, -92f), (8f, 26f, -100f, -82f),
        };
        var pylons = new List<Vector3>();
        for (int i = 0; i < islets.Length; i++)
        {
            var (x0, x1, z0, z1) = islets[i];
            Plateau($"Islet_{i}", x0, x1, z0, z1, top, _lawn, 10f);
            var at = new Vector3((x0 + x1) * 0.5f - (i == islets.Length - 1 ? 4f : 0f), top, (z0 + z1) * 0.5f);
            Box($"Pylon_{i}", at + new Vector3(-0.6f, 0f, -0.6f), at + new Vector3(0.6f, 4.5f, 0.6f), _stone);
            Box($"PylonCap_{i}", at + new Vector3(-0.75f, 4.5f, -0.75f), at + new Vector3(0.75f, 5f, 0.75f), SolidColour(Teal));
            pylons.Add(at + Vector3.up * 4.75f);
        }
        // The graph's edges, drawn faintly between the pylon tops.
        for (int i = 0; i + 1 < pylons.Count; i++)
        {
            var edge = pylons[i + 1] - pylons[i];
            OrientedBox($"GraphEdge_{i}", (pylons[i] + pylons[i + 1]) * 0.5f, new(0.08f, 0.08f, edge.magnitude),
                        Quaternion.LookRotation(edge), SolidColour(Teal), collide: false);
        }
        Sign("DELTA PYLONS\n16 m gaps · needs NETWORK STEP", pylons[0] + Vector3.up * 4f, GateSignSize);

        Doorway("GaloisDoor", new(21f, top, -91f), 4f, 6f, Quaternion.Euler(0f, -90f, 0f), SolidColour(Navy), SolidColour(Red));
        Sign("PRE-DAWN STREET · GALOIS\ndrops SYMMETRY BREAK", new(20f, top + 9f, -91f), GateSignSize);
    }

    // ── Far off: the era islands and the black sun ─────────────────────────

    static void BuildVistas()
    {
        Group("Vistas");
        foreach (var era in Eras)
        {
            var at = Bearing(era.bearing, era.distance, era.height);
            string id = era.title.Split('\n')[0];
            // Farther islands are built bigger, so the spiral still reads at 670 m.
            float s = Mathf.Sqrt(era.distance / 220f);
            Vector3 V(float x, float y, float z) => at + new Vector3(x, y, z) * s;
            Box($"Island_{id}", V(-18f, -6f, -18f), V(18f, 0f, 18f), SolidColour(era.main), collide: false);
            Box($"IslandRock_{id}", V(-11f, -20f, -11f), V(11f, -6f, 11f), _rock, collide: false);
            Box($"IslandRoot_{id}", V(-5f, -32f, -5f), V(5f, -20f, 5f), _rock, collide: false);
            Box($"IslandHall_{id}", V(-6f, 0f, -5f), V(6f, 10f, 5f), SolidColour(era.accent), collide: false);
            Sign(era.title, V(0f, 24f, 0f), era.distance * 0.22f);
        }

        // The black sun: a black disc ringed in white, like an eclipse, low in
        // the north-north-east. The ring is a disc just behind it, facing the hub.
        var sunAt = Bearing(BlackSunBearing, BlackSunDistance, BlackSunHeight);
        var toHub = (-sunAt).normalized;
        Sphere("BlackSun", sunAt, BlackSunRadius, _black, collide: false);
        var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        ring.name = "BlackSunRing";
        Object.DestroyImmediate(ring.GetComponent<Collider>());
        ring.transform.SetParent(_group, false);
        ring.transform.SetPositionAndRotation(sunAt - toHub * 4f, Quaternion.FromToRotation(Vector3.up, toHub));
        ring.transform.localScale = new Vector3(BlackSunRadius * 2.3f, 0.2f, BlackSunRadius * 2.3f);
        ring.GetComponent<MeshRenderer>().sharedMaterial = _white;
        ring.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    static Vector3 Bearing(float degrees, float distance, float height)
    {
        float r = degrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Sin(r) * distance, height, Mathf.Cos(r) * distance);
    }

    // ── Pickups and anchors ────────────────────────────────────────────────

    /// <summary>Each Guardian's reward stands at that Guardian's door, the two
    /// hub pickups where the design puts them, and every one on the debug
    /// shelf. None is used up, so Shift+Backspace and a second run work.</summary>
    static void BuildPickups(Transform root)
    {
        var group = new GameObject("Pickups").transform;
        group.SetParent(root, false);
        Group("Pedestals");

        string feather = $"{PickupDir}/Pickup_DoubleJump.prefab", board = $"{PickupDir}/Pickup_Skateboard.prefab";
        string dash = $"{PickupDir}/Pickup_Dash.prefab", tether = $"{PickupDir}/Pickup_Tether.prefab";

        Pickup(group, feather, new(-16.5f, RoofTop, -13.5f), "BIRDY'S FEATHER");
        Pickup(group, board, new(6f, 0f, -34f), "SKATEBOARD");
        Pickup(group, dash, new(-58f, 0.15f, 0f), "ANGLE DASH\n(stand-in: Dash)");
        Pickup(group, tether, new(-59f, 0f, 51f), "ORBITAL TETHER\n(stand-in: Tether)");
        Pickup(group, GravityAnchorPickupPath, new(56f, 26.85f, -43f), "GRAVITY ANCHOR");

        var shelf = new[] { feather, board, dash, tether, GravityAnchorPickupPath };
        for (int i = 0; i < shelf.Length; i++)
            Pickup(group, shelf[i], new(-16f + 3f * i, 0f, -44f), null);
        Sign("DEBUG SHELF\nevery ability, for testing gates", new(-10f, 4.5f, -44f), GateSignSize);
    }

    static void Pickup(Transform group, string path, Vector3 floor, string label)
    {
        Box("Pedestal", floor + new Vector3(-0.6f, 0f, -0.6f), floor + new Vector3(0.6f, 0.4f, 0.6f), _stone);
        if (label != null) Sign(label, floor + Vector3.up * 2.6f, 3.5f);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogWarning($"Pickup prefab missing: {path}. Run the setup that makes it.");
            return;
        }
        var pickup = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        pickup.transform.SetParent(group, false);
        pickup.transform.position = floor + Vector3.up * 1.3f;
        if (pickup.TryGetComponent(out AbilityPickup ability))
        {
            var so = new SerializedObject(ability);
            so.FindProperty("consumeOnPickup").boolValue = false;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>The orrery's planets, 7 m apart across the 26 m chasm, as in
    /// the training stage's tether crossing.</summary>
    static readonly Vector3[] Planets = { new(47f, 6.5f, 40f), new(54f, 7f, 40f), new(61f, 6.5f, 40f) };

    static void BuildAnchors(Transform root)
    {
        var group = new GameObject("TetherAnchors").transform;
        group.SetParent(root, false);
        var mat = NewtonFightArt.LitMaterial(ArtDir, "GreyboxPlanet", Brass, 0.6f, Brass * 0.25f);

        for (int i = 0; i < Planets.Length; i++)
        {
            var go = new GameObject($"OrreryPlanet_{i}");
            go.transform.SetParent(group, false);
            go.transform.position = Planets[i];

            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            ball.name = "Visual";
            Object.DestroyImmediate(ball.GetComponent<Collider>());
            ball.transform.SetParent(go.transform, false);
            ball.transform.localScale = Vector3.one * 1.1f;
            var renderer = ball.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;

            var glowGo = new GameObject("Glow");
            glowGo.transform.SetParent(go.transform, false);
            var glow = glowGo.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = 7f;
            glow.color = new Color(1f, 0.82f, 0.5f);
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

    // ── Player, Birdy, HUD, cleanup, lighting ──────────────────────────────

    static void PlacePlayer(ChibiLocomotion player, Transform spawn)
    {
        player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);

        var cam = Camera.main;
        if (cam == null) return;
        cam.transform.SetPositionAndRotation(spawn.position + new Vector3(0f, 1.6f, -4.6f),
                                             Quaternion.Euler(12f, 0f, 0f));
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 2000f);
    }

    /// <summary>Birdy stands just ahead of the spawn, turned to greet you.</summary>
    static void PlaceBirdy()
    {
        var npcs = GameObject.Find("AcademyNpcs");
        if (npcs == null || npcs.transform.childCount == 0)
        {
            Debug.LogWarning("No AcademyNpcs in ChibiSandbox; the greybox has no Birdy. Run Setup Birdy NPC there.");
            return;
        }
        var birdy = npcs.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Contains("Birdy"));
        if (birdy == null) birdy = npcs.transform.GetChild(0);
        var face = SpawnPosition - BirdyPosition;
        face.y = 0f;
        birdy.SetPositionAndRotation(BirdyPosition, Quaternion.LookRotation(face));
    }

    /// <summary>Speed / height / jump readout in the top-right corner, read by
    /// the TrainingStage component, plus a one-line key hint.</summary>
    static TMP_Text BuildReadout()
    {
        var hud = GameObject.Find("FightHUD");
        if (hud == null)
        {
            hud = new GameObject("FightHUD", typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            hud.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = hud.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }
        var topRight = new Vector2(1f, 1f);

        var panel = new GameObject("GreyboxReadoutPanel", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        panel.transform.SetParent(hud.transform, false);
        var rect = (RectTransform)panel.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = topRight;
        rect.anchoredPosition = new Vector2(-24f, -20f);
        rect.sizeDelta = new Vector2(640f, 250f);
        var image = panel.GetComponent<UnityEngine.UI.Image>();
        image.color = new Color(0.04f, 0.05f, 0.09f, 0.62f);
        image.raycastTarget = false;

        var readout = HudText(hud.transform, "GreyboxReadout", 32f, new Vector2(-44f, -30f), new Vector2(600f, 180f));
        readout.text = "SPEED  0.0 m/s\nHEIGHT  0.00 m\nJUMP  -\nBEST  -";
        var hint = HudText(hud.transform, "GreyboxHint", 26f, new Vector2(-44f, -214f), new Vector2(600f, 40f));
        hint.text = "BACKSPACE respawn   SHIFT+BACKSPACE drop abilities";
        return readout;
    }

    static TextMeshProUGUI HudText(Transform hud, string name, float size, Vector2 pos, Vector2 box)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(hud, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = pos;
        rect.sizeDelta = box;
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = _font;
        text.fontSharedMaterial = _fontMat;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.TopRight;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>Removes the academy model, Newton with his HUD, and the
    /// sandbox's own pickups and anchors. Keeps the player, camera, light,
    /// post-processing, HUD, dialogue and Birdy.</summary>
    static void StripSandbox(ChibiLocomotion player)
    {
        var boss = Object.FindFirstObjectByType<NewtonBoss>();
        var bossHealth = boss != null ? boss.GetComponent<Health>() : null;

        foreach (var bar in Object.FindObjectsByType<HealthBarView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var source = new SerializedObject(bar).FindProperty("source").objectReferenceValue;
            if (bossHealth != null && source == bossHealth) Object.DestroyImmediate(bar.gameObject);
        }

        var numbers = Object.FindFirstObjectByType<DamageNumbers>(FindObjectsInactive.Include);
        if (numbers != null)
        {
            var so = new SerializedObject(numbers);
            var sources = so.FindProperty("sources");
            for (int i = sources.arraySize - 1; i >= 0; i--)
            {
                var h = sources.GetArrayElementAtIndex(i).FindPropertyRelative("health").objectReferenceValue;
                if (h == null || h == bossHealth) sources.DeleteArrayElementAtIndex(i);
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var hud = GameObject.Find("FightHUD");
        if (hud != null && hud.transform.Find("NewtonBark") is { } bark) Object.DestroyImmediate(bark.gameObject);

        var kept = new List<string>();
        foreach (var go in player.gameObject.scene.GetRootGameObjects())
        {
            if (go == player.gameObject) continue;
            bool sandboxOnly = go.name.StartsWith("AcademyWorld")
                               || go.GetComponentInChildren<NewtonBoss>(true) != null
                               || go.GetComponentInChildren<AbilityPickup>(true) != null
                               || go.GetComponentInChildren<TetherAnchor>(true) != null;
            if (sandboxOnly) Object.DestroyImmediate(go);
            else kept.Add(go.name);
        }
        Debug.Log($"Kept from ChibiSandbox: {player.name}, {string.Join(", ", kept)}");
    }

    /// <summary>Golden hour: a warm low sun from the south-west, a procedural
    /// sky, and haze that fades the era islands without swallowing the black sun.</summary>
    static void ApplyLighting()
    {
        var skyShader = Shader.Find("Skybox/Procedural");
        if (skyShader != null)
        {
            string path = $"{ArtDir}/GreyboxSky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky == null)
            {
                sky = new Material(skyShader) { name = "GreyboxSky" };
                sky.SetFloat("_SunSize", 0.035f);
                sky.SetFloat("_AtmosphereThickness", 0.8f);
                sky.SetColor("_SkyTint", new Color(0.45f, 0.58f, 0.78f));
                sky.SetColor("_GroundColor", new Color(0.62f, 0.58f, 0.52f));
                sky.SetFloat("_Exposure", 1.25f);
                AssetDatabase.CreateAsset(sky, path);
            }
            RenderSettings.skybox = sky;
        }
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.86f, 0.82f, 0.74f);
        RenderSettings.fogStartDistance = 300f;
        RenderSettings.fogEndDistance = 2600f;

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.72f, 0.76f, 0.86f);
        RenderSettings.ambientEquatorColor = new Color(0.74f, 0.68f, 0.6f);
        RenderSettings.ambientGroundColor = new Color(0.42f, 0.38f, 0.34f);

        var cam = Camera.main;
        if (cam != null) cam.clearFlags = skyShader != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;

        var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
            .FirstOrDefault(l => l.type == LightType.Directional);
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(28f, 40f, 0f);   // from the south-west, low
            sun.color = new Color(1f, 0.88f, 0.72f);
            sun.intensity = 1.35f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;
        }
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    static void Group(string name)
    {
        _group = new GameObject(name).transform;
        _group.SetParent(_geometry, false);
    }

    static float R(float min, float max) => min + (float)_rng.NextDouble() * (max - min);

    static Quaternion Random3() => Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f));

    /// <summary>A walkable slab of ground with a tapering rock body under it,
    /// so the island reads as floating and its underside shows from the rim.</summary>
    static void Plateau(string name, float x0, float x1, float z0, float z1, float top, Material surface, float depth)
    {
        Box(name, new(x0, top - 4f, z0), new(x1, top, z1), surface);
        float inset = Mathf.Min(x1 - x0, z1 - z0) * 0.18f;
        float mid = top - 4f - depth * 0.45f, bottom = top - 4f - depth;
        Box($"{name}Rock", new(x0 + inset, mid, z0 + inset), new(x1 - inset, top - 4f, z1 - inset), _rock);
        Box($"{name}Root", new(x0 + 2f * inset, bottom, z0 + 2f * inset), new(x1 - 2f * inset, mid, z1 - 2f * inset), _rock);
    }

    /// <summary>A knee-high wall along an edge, so the rim reads as a rim.</summary>
    static void Parapet(string name, float from, float to, float at, bool alongX)
    {
        if (alongX) Box(name, new(from, 0f, at - 0.4f), new(to, 0.9f, at), _stone);
        else Box(name, new(at - 0.4f, 0f, from), new(at, 0.9f, to), _stone);
    }

    /// <summary>A free-standing door: two posts and a lintel round a coloured
    /// panel. <paramref name="foot"/> is the bottom-centre; the panel faces
    /// along the rotation's Z.</summary>
    static void Doorway(string name, Vector3 foot, float width, float height, Quaternion rotation,
                        Material frame, Material panel, bool collide = true)
    {
        Vector3 Up(float y) => rotation * Vector3.up * y;
        Vector3 Side(float x) => rotation * Vector3.right * x;
        float half = width * 0.5f;
        OrientedBox($"{name}PostL", foot + Side(-half - 0.3f) + Up(height * 0.5f), new(0.6f, height, 1f), rotation, frame, collide);
        OrientedBox($"{name}PostR", foot + Side(half + 0.3f) + Up(height * 0.5f), new(0.6f, height, 1f), rotation, frame, collide);
        OrientedBox($"{name}Lintel", foot + Up(height + 0.4f), new(width + 1.2f, 0.8f, 1f), rotation, frame, collide);
        OrientedBox($"{name}Panel", foot + Up(height * 0.5f), new(width, height, 0.4f), rotation, panel, collide);
    }

    static void OliveTree(string name, Vector3 foot)
    {
        Cylinder($"{name}Trunk", foot, 0.3f, 2.2f, _rock);
        Sphere($"{name}Crown", foot + Vector3.up * 3f, 1.6f, _olive, collide: false);
    }

    /// <summary>An axis-aligned block from its two corners.</summary>
    static GameObject Box(string name, Vector3 min, Vector3 max, Material mat, bool collide = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(_group, false);
        go.transform.position = (min + max) * 0.5f;
        go.transform.localScale = max - min;
        return Finish(go, mat, collide);
    }

    static GameObject OrientedBox(string name, Vector3 centre, Vector3 size, Quaternion rotation, Material mat,
                                  bool collide = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(_group, false);
        go.transform.SetPositionAndRotation(centre, rotation);
        go.transform.localScale = size;
        return Finish(go, mat, collide);
    }

    /// <summary>A plank whose top surface runs from <paramref name="a"/> to
    /// <paramref name="b"/>: ramps, the aqueduct.</summary>
    static GameObject Slab(string name, Vector3 a, Vector3 b, float width, float thickness, Material mat)
    {
        var dir = b - a;
        var rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        var centre = (a + b) * 0.5f - rotation * Vector3.up * (thickness * 0.5f);
        return OrientedBox(name, centre, new(width, thickness, dir.magnitude), rotation, mat);
    }

    /// <summary>An upright cylinder from its bottom centre. Its collider is the
    /// mesh, since a primitive cylinder's capsule collider has round ends.</summary>
    static GameObject Cylinder(string name, Vector3 bottom, float radius, float height, Material mat, bool collide = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(_group, false);
        go.transform.position = bottom + Vector3.up * (height * 0.5f);
        go.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
        if (collide) go.AddComponent<MeshCollider>().sharedMesh = go.GetComponent<MeshFilter>().sharedMesh;
        return Finish(go, mat, true);
    }

    static GameObject Sphere(string name, Vector3 centre, float radius, Material mat, bool collide = true)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(_group, false);
        go.transform.position = centre;
        go.transform.localScale = Vector3.one * radius * 2f;
        return Finish(go, mat, collide);
    }

    static GameObject Finish(GameObject go, Material mat, bool collide)
    {
        if (!collide && go.TryGetComponent(out Collider c)) Object.DestroyImmediate(c);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        if (!collide) renderer.shadowCastingMode = ShadowCastingMode.Off;
        go.isStatic = true;
        return go;
    }

    /// <summary>A wedge rising along the rotation's +Z from <paramref name="foot"/>:
    /// width across X, <paramref name="height"/> up, <paramref name="run"/> along Z.</summary>
    static void Wedge(string name, Vector3 foot, float width, float height, float run, Material mat, Quaternion rotation)
    {
        var mesh = WedgeMesh();
        var go = new GameObject(name);
        go.transform.SetParent(_group, false);
        go.transform.SetPositionAndRotation(foot, rotation);
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

    /// <summary>A billboard sign that always faces the main camera.</summary>
    static void Sign(string text, Vector3 at, float size)
    {
        var tmp = Text(text, at, Quaternion.identity, size);
        tmp.gameObject.AddComponent<FaceCamera>();
    }

    static TextMeshPro Text(string text, Vector3 at, Quaternion rotation, float size)
    {
        var go = new GameObject($"Sign {text.Split('\n')[0]}");
        go.transform.SetParent(_signs, false);
        go.transform.SetPositionAndRotation(at, rotation);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.font = _font;
        tmp.fontSharedMaterial = _fontMat;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(12f, 2f);
        go.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return tmp;
    }
}
