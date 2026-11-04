using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Pickleball;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Boss.Hypatia;
using Pickleball.Sandbox.Boss.Hypatia.Burning;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using Pickleball.Sandbox.Boss.Hypatia.Presentation;
using Pickleball.Sandbox.Boss.Hypatia.Pulse;
using Pickleball.Sandbox.Boss.Hypatia.Rings;
using Pickleball.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools ▸ Pickleball ▸ Setup Hypatia Fight. Builds <c>Scenes/HypatiaFight.unity</c>:
/// Hypatia, the Astrolabe, on the Rete, a brass astrolabe plate under a night sky.
///
/// <b>The player comes from ChibiSandbox.</b> The scene starts as a byte copy of
/// the current ChibiSandbox (as Build Training Stage and Build Academy Greybox
/// do), with the academy, Newton, his HUD bindings, Birdy, the tether anchors and
/// the tether/skateboard pickups stripped; the chibi, camera, lock-on, magic,
/// dialogue and HUD arrive exactly as wired there. Unlike those two, the copy is
/// written over the existing file, so HypatiaFight.unity keeps its GUID.
///
/// <b>Idempotent.</b> Running it twice is the same as running it once. The
/// model import, config and material are compare-before-write. Everything else
/// (materials, meshes, prefabs, controller, scene) is regenerated only when an
/// input changed: a fingerprint of ChibiSandbox.unity, every Hypatia script
/// (gameplay, presentation and this builder), the config's values and the
/// imported model is stored in <c>Generated/HypatiaFight.fingerprint.txt</c>
/// once the scene has saved, and a run whose fingerprint matches does nothing. Re-run after changing ChibiSandbox and the
/// scene is rebuilt. <b>Setup Hypatia Fight (Force Rebuild)</b> ignores the
/// fingerprint (after a hand edit you want to discard).
///
/// Tuning lives in <c>ScriptableObjects/Hypatia/HypatiaConfig.asset</c>, created
/// once and never overwritten. Presentation colours are on the view components;
/// they are regenerated with the scene, so change their defaults in code.
/// </summary>
public static class HypatiaFightSetup
{
    const string SourceScenePath = "Assets/_Project/Scenes/ChibiSandbox.unity";
    public const string ScenePath = "Assets/_Project/Scenes/HypatiaFight.unity";
    public const string ConfigPath = "Assets/_Project/ScriptableObjects/Hypatia/HypatiaConfig.asset";
    const string FingerprintPrefix = "hypatia-fight-v1:";
    const string StampPath = HypatiaFightArt.GenDir + "/HypatiaFight.fingerprint.txt";

    static readonly Vector3 SpawnPosition = new(0f, 0.05f, -9f);
    const string BossName = "HypatiaBoss";
    const string BossTitle = "Hypatia — The Astrolabe";

    /// <summary>Height of the invisible perimeter wall, m: well above the
    /// player's ~9 m best combo (Movement.md) and the Gravity Anchor's reach.</summary>
    const float WallHeight = 40f;

    [MenuItem("Tools/Pickleball/Setup Hypatia Fight")]
    public static void Run() => Run(force: false);

    [MenuItem("Tools/Pickleball/Setup Hypatia Fight (Force Rebuild)")]
    public static void RunForced() => Run(force: true);

    /// <summary>The whole build. Returns true when the scene is up to date
    /// afterwards (rebuilt, or already current).</summary>
    public static bool Run(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[HypatiaFight] Leave Play mode before running Setup Hypatia Fight.");
            return false;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
        {
            Debug.LogError($"[HypatiaFight] {SourceScenePath} not found. Run Tools ▸ Pickleball ▸ " +
                           "Setup Chibi Sandbox first.");
            return false;
        }

        var config = EnsureConfig();
        if (!HypatiaFightModel.Configure(out var avatar)) return false;

        string fingerprint = Fingerprint(config);
        if (!force && IsUpToDate(fingerprint))
        {
            Debug.Log($"[HypatiaFight] {ScenePath} is up to date (inputs unchanged); nothing to do. " +
                      "Use Setup Hypatia Fight (Force Rebuild) to regenerate anyway.");
            return true;
        }

        // Never silently discard unsaved work in whatever scene is open.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Assets first: the scene references them.
        HypatiaFightArt.BuildLobPrefab();
        HypatiaFightArt.BuildOrbiterPrefab();
        HypatiaFightArt.BuildShotPrefab();
        HypatiaFightArt.BuildScrollPrefab();
        HypatiaFightModel.EnsureController();
        SaveOwned();

        if (!CopySourceScene()) return false;
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        // Re-load every asset AFTER the scene switch: opening a scene unloads
        // unreferenced assets, and a reference held from before is then a dead
        // wrapper that serializes as None (the first build lost its config and
        // prefabs exactly this way).
        config = AssetDatabase.LoadAssetAtPath<HypatiaConfig>(ConfigPath);
        avatar = AssetDatabase.LoadAllAssetsAtPath(HypatiaFightModel.ModelPath).OfType<Avatar>().FirstOrDefault();
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(HypatiaFightModel.ControllerPath);
        var lob = AssetDatabase.LoadAssetAtPath<ConicLob>(HypatiaFightArt.LobPrefabPath);
        var orbiter = AssetDatabase.LoadAssetAtPath<ConicOrbiter>(HypatiaFightArt.OrbiterPrefabPath);
        var shot = AssetDatabase.LoadAssetAtPath<HyperbolicShot>(HypatiaFightArt.ShotPrefabPath);
        var scroll = AssetDatabase.LoadAssetAtPath<BurningScroll>(HypatiaFightArt.ScrollPrefabPath);
        if (config == null || avatar == null || lob == null || orbiter == null || shot == null || scroll == null)
        {
            Debug.LogError("[HypatiaFight] A generated asset failed to load after the scene opened " +
                           $"(config={config != null}, avatar={avatar != null}, lob={lob != null}, " +
                           $"orbiter={orbiter != null}, shot={shot != null}, scroll={scroll != null}).");
            return false;
        }

        var player = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (player == null)
        {
            Debug.LogError("[HypatiaFight] ChibiSandbox has no ChibiLocomotion; rebuild it with Setup Chibi Sandbox.");
            return false;
        }

        var oldBossHealth = Object.FindFirstObjectByType<NewtonBoss>() is { } newton
            ? newton.GetComponent<Health>()
            : null;

        var arena = BuildArena(config);
        var hypatia = BuildBoss(config, arena, player, avatar, controller, lob, orbiter, shot, scroll);
        BuildViews(config, hypatia);
        RebindHud(config, hypatia, oldBossHealth);
        StripSandbox(player, arena.transform, hypatia.gameObject);
        PlacePlayer(player);
        ApplyNight();

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
        {
            Debug.LogError($"[HypatiaFight] Could not save {ScenePath}.");
            return false;
        }
        StoreFingerprint(fingerprint);
        SaveOwned();
        Selection.activeGameObject = hypatia.gameObject;
        Debug.Log($"[HypatiaFight] Built {ScenePath}. Press Play; tune {ConfigPath}.");
        return true;
    }

    // ── Config, fingerprint, scene copy ─────────────────────────────────────

    static void SaveOwned() => HypatiaFightArt.SaveOwnedAssets(
        "Assets/_Project/ScriptableObjects/Hypatia", "Assets/_Project/Animation/Hypatia", HypatiaFightArt.ArtDir);

    /// <summary>Created once if missing; never overwritten (tuning lives there).</summary>
    public static HypatiaConfig EnsureConfig()
    {
        var config = AssetDatabase.LoadAssetAtPath<HypatiaConfig>(ConfigPath);
        if (config != null) return config;
        ChibiSandboxSetup.EnsureFolderPublic(Path.GetDirectoryName(ConfigPath)!.Replace('\\', '/'));
        config = ScriptableObject.CreateInstance<HypatiaConfig>();
        AssetDatabase.CreateAsset(config, ConfigPath);
        AssetDatabase.SaveAssetIfDirty(config);
        Debug.Log($"[HypatiaFight] Created {ConfigPath} with the design defaults.");
        return config;
    }

    /// <summary>
    /// Everything the generated assets are a function of: the source scene's
    /// bytes, the Hypatia scripts' text (line endings normalised, so a checkout
    /// with different endings does not rebuild), the config's values and the
    /// imported model, idle clip and shared textures.
    /// </summary>
    static string Fingerprint(HypatiaConfig config)
    {
        using var sha = SHA1.Create();
        var sb = new StringBuilder();
        string root = Directory.GetParent(Application.dataPath)!.FullName;

        sb.Append(Hex(sha.ComputeHash(File.ReadAllBytes(Path.Combine(root, SourceScenePath)))));
        var scripts = Directory.GetFiles(Path.Combine(root, "Assets/_Project/Scripts/Sandbox/Boss/Hypatia"), "*.cs",
                                         SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "Assets/_Project/Scripts/Editor"), "HypatiaFight*.cs"))
            .Append(Path.Combine(root, "Assets/_Project/Scripts/UI/BarkView.cs"))
            .Select(p => p.Replace('\\', '/'))
            .OrderBy(p => p, System.StringComparer.Ordinal);
        foreach (string file in scripts)
        {
            string text = File.ReadAllText(file).Replace("\r", "");
            sb.Append(Path.GetFileName(file)).Append(Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text))));
        }
        sb.Append(EditorJsonUtility.ToJson(config));
        foreach (string asset in new[]
                 {
                     HypatiaFightModel.ModelPath, HypatiaFightModel.IdleClipPath, HypatiaFightModel.ToppleClipPath,
                     $"{NewtonFightArt.VfxDir}/Glow.png", $"{NewtonFightArt.VfxDir}/Disc.png",
                     $"{NewtonFightArt.VfxDir}/TargetRing.png",
                 })
            sb.Append(AssetDatabase.GetAssetDependencyHash(asset).ToString());
        return FingerprintPrefix + Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    static string Hex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    static string StampFile =>
        Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, StampPath);

    static bool IsUpToDate(string fingerprint)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) return false;
        if (!File.Exists(StampFile) || File.ReadAllText(StampFile).Trim() != fingerprint) return false;
        return AssetDatabase.LoadAssetAtPath<GameObject>(HypatiaFightArt.LobPrefabPath) != null
               && AssetDatabase.LoadAssetAtPath<GameObject>(HypatiaFightArt.OrbiterPrefabPath) != null
               && AssetDatabase.LoadAssetAtPath<GameObject>(HypatiaFightArt.ShotPrefabPath) != null
               && AssetDatabase.LoadAssetAtPath<GameObject>(HypatiaFightArt.ScrollPrefabPath) != null;
    }

    /// <summary>Written only after the scene saved. A text asset beside the
    /// generated art rather than the scene's import settings: rewriting an open
    /// scene's .meta makes Unity ask to reload it.</summary>
    static void StoreFingerprint(string fingerprint)
    {
        if (File.Exists(StampFile) && File.ReadAllText(StampFile).Trim() == fingerprint) return;
        ChibiSandboxSetup.EnsureFolderPublic(HypatiaFightArt.GenDir);
        File.WriteAllText(StampFile, fingerprint + "\n");
        AssetDatabase.ImportAsset(StampPath, ImportAssetOptions.ForceUpdate);
    }

    /// <summary>Overwrite HypatiaFight.unity with ChibiSandbox's bytes. Writing
    /// the file (rather than delete + CopyAsset) keeps the scene's .meta, so its
    /// GUID survives every rebuild.</summary>
    static bool CopySourceScene()
    {
        string root = Directory.GetParent(Application.dataPath)!.FullName;
        try
        {
            File.Copy(Path.Combine(root, SourceScenePath), Path.Combine(root, ScenePath), overwrite: true);
        }
        catch (IOException e)
        {
            Debug.LogError($"[HypatiaFight] Could not copy {SourceScenePath} to {ScenePath}: {e.Message}");
            return false;
        }
        AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceUpdate);
        return true;
    }

    // ── Arena: the Rete ─────────────────────────────────────────────────────

    /// <summary>
    /// The plate (solid collider, top at y = 0), four band rings at the config's
    /// radii, a raised brass lip with degree ticks every 15°, spokes every 45°,
    /// an eccentric ecliptic ring and star pointers, and an invisible wall at
    /// the rim so nobody falls off. The root is the arena centre.
    /// </summary>
    static GameObject BuildArena(HypatiaConfig config)
    {
        float r = config.ArenaRadius;
        var root = new GameObject("TheRete");
        var t = root.transform;

        var brass = HypatiaFightArt.BrassMat();
        var silver = HypatiaFightArt.SilverMat();

        // Plate: solid, its top flush with y = 0.
        var plateMesh = HypatiaFightArt.MeshAsset("RetePlate", m => ArcMesh.SolidRing(m, 0f, r + 0.6f, -0.6f, 0f, 144));
        var plate = HypatiaFightArt.Part("Plate", t, plateMesh, HypatiaFightArt.PlateMat());
        plate.AddComponent<MeshCollider>().sharedMesh = plateMesh;
        plate.isStatic = true;

        // Lip outside the rim (decor) and its degree ticks.
        HypatiaFightArt.Part("Lip", t, HypatiaFightArt.MeshAsset("ReteLip",
            m => ArcMesh.SolidRing(m, r + 0.05f, r + 0.6f, 0f, 0.35f, 144)), brass);
        var ticks = new GameObject("DegreeTicks").transform;
        ticks.SetParent(t, false);
        for (int deg = 0; deg < 360; deg += 15)
        {
            bool major = deg % 45 == 0;
            float len = major ? 0.9f : 0.45f;
            Vector3 at = ArcMesh.OnPlate(deg, r - len * 0.5f - 0.05f, 0.012f);
            HypatiaFightArt.Primitive(PrimitiveType.Cube, $"Tick{deg:000}", ticks, at, Quaternion.Euler(0f, deg, 0f),
                                      new Vector3(major ? 0.09f : 0.05f, 0.025f, len), brass);
        }

        // Band rings at the burn bands' outer radii (the last one is the rim).
        var bands = new GameObject("BandRings").transform;
        bands.SetParent(t, false);
        float[] radii = BandMath.Sanitise(config.BandOuterRadii, r);
        for (int i = 0; i < radii.Length; i++)
        {
            float br = radii[i];
            HypatiaFightArt.Part($"Band{i}", bands, HypatiaFightArt.MeshAsset($"ReteBand{i}",
                m => ArcMesh.SolidRing(m, br - 0.05f, br + 0.05f, 0f, 0.02f, 144)), brass);
        }

        // Hub (the mater's centre) and spokes every 45°.
        HypatiaFightArt.Part("Hub", t, HypatiaFightArt.MeshAsset("ReteHub",
            m => ArcMesh.SolidRing(m, 0f, 1.2f, 0f, 0.03f, 64)), silver);
        var spokes = new GameObject("Spokes").transform;
        spokes.SetParent(t, false);
        for (int deg = 0; deg < 360; deg += 45)
        {
            float from = 1.2f, to = r - 1f;
            Vector3 mid = ArcMesh.OnPlate(deg, (from + to) * 0.5f, 0.01f);
            HypatiaFightArt.Primitive(PrimitiveType.Cube, $"Spoke{deg:000}", spokes, mid,
                                      Quaternion.Euler(0f, deg, 0f), new Vector3(0.07f, 0.02f, to - from), brass);
        }

        // The rete proper: an eccentric ecliptic ring and star pointers.
        var ecliptic = HypatiaFightArt.Part("Ecliptic", t, HypatiaFightArt.MeshAsset("ReteEcliptic",
            m => ArcMesh.SolidRing(m, 6.9f, 7.05f, 0f, 0.025f, 144)), silver);
        ecliptic.transform.localPosition = new Vector3(0f, 0.001f, 1.6f);
        var pointers = new GameObject("StarPointers").transform;
        pointers.SetParent(t, false);
        var pointerMesh = HypatiaFightArt.PointerMesh();
        var gem = HypatiaFightArt.GemMat();
        (float az, float rad)[] stars = { (20f, 4.2f), (75f, 9.5f), (130f, 6.6f), (170f, 11.2f), (215f, 3.4f),
                                          (250f, 8.8f), (300f, 10.4f), (340f, 5.6f) };
        foreach (var (az, rad) in stars)
        {
            var p = HypatiaFightArt.Part($"Pointer{az:000}", pointers, pointerMesh, brass);
            p.transform.localPosition = ArcMesh.OnPlate(az, rad, 0.015f);
            p.transform.localRotation = Quaternion.Euler(0f, az + 90f, 0f);
            HypatiaFightArt.Primitive(PrimitiveType.Sphere, "Gem", p.transform, new Vector3(0f, 0.06f, 1f),
                                      Quaternion.identity, Vector3.one * 0.16f, gem);
        }

        // Invisible wall at the rim: boxes tangent to the circle, overlapping.
        // On Ignore Raycast so the orbit camera's occlusion test skips them
        // (its mask is set to match in ApplyNight).
        var walls = new GameObject("PerimeterWall") { layer = HypatiaFightArt.ProjectileLayer };
        walls.transform.SetParent(t, false);
        const int segments = 48;
        float width = 2f * Mathf.PI * r / segments * 1.1f;
        for (int i = 0; i < segments; i++)
        {
            float az = i * 360f / segments;
            var seg = new GameObject($"Wall{i:00}") { layer = HypatiaFightArt.ProjectileLayer };
            seg.transform.SetParent(walls.transform, false);
            seg.transform.localPosition = ArcMesh.OnPlate(az, r + 0.5f, WallHeight * 0.5f);
            seg.transform.localRotation = Quaternion.Euler(0f, az, 0f);
            var box = seg.AddComponent<BoxCollider>();
            box.size = new Vector3(width, WallHeight, 1f);
        }

        HypatiaFightArt.Stars(t);
        return root;
    }

    // ── Hypatia ─────────────────────────────────────────────────────────────

    static HypatiaBoss BuildBoss(HypatiaConfig config, GameObject arena, ChibiLocomotion player, Avatar avatar,
                                 RuntimeAnimatorController controller, ConicLob lob, ConicOrbiter orbiter,
                                 HyperbolicShot shot, BurningScroll scroll)
    {
        var root = new GameObject(BossName);
        root.transform.SetPositionAndRotation(arena.transform.position, Quaternion.Euler(0f, 180f, 0f));

        // Body first: lock-on's centre and the status visual find the first
        // collider in her children, and it must be this one.
        var body = new GameObject("Body").transform;
        body.SetParent(root.transform, false);
        body.localPosition = new Vector3(0f, config.HoverHeight, 0f);   // the brain rises from here
        var capsule = body.gameObject.AddComponent<CapsuleCollider>();
        capsule.height = HypatiaFightModel.TargetHeight;
        capsule.radius = 0.34f;
        capsule.center = new Vector3(0f, HypatiaFightModel.TargetHeight * 0.5f, 0f);

        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(HypatiaFightModel.ModelPath);
        HypatiaFightModel.MeasureHeight(out float belowOrigin);
        var model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
        model.name = "HypatiaModel";
        model.transform.SetParent(body, false);
        model.transform.localPosition = new Vector3(0f, belowOrigin, 0f);
        if (!model.TryGetComponent(out Animator animator)) animator = model.AddComponent<Animator>();
        animator.avatar = avatar;
        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        var glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(body, false);
        glowGo.transform.localPosition = new Vector3(0f, 1.1f, 0.6f);
        var glow = glowGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(0.6f, 0.65f, 1f);
        glow.range = 7f;
        glow.intensity = 3f;
        glow.shadows = LightShadows.None;
        // Scenery that hovers with her: kept out of lock-on's aim and size.
        HypatiaFightArt.Halo("Halo", body, 2.6f, new Color(0.62f, 0.68f, 1f, 0.3f), new Vector3(0f, 0.95f, -0.3f))
            .AddComponent<LockOnIgnore>();
        // The knockback pulse's charge and flash (PulseView drives it); hidden at rest.
        HypatiaFightPulseArt.CoreQuad("PulseGlow", body, new Vector3(0f, 0.95f, 0.2f));

        // Health as Newton's (NewtonFightSetup): no grey regain pool on the boss,
        // and she grants sword XP like him (per damage dealt plus the defeat bonus).
        var health = root.AddComponent<Health>();
        var hso = new SerializedObject(health);
        hso.FindProperty("maxHealth").floatValue = config.MaxHealth;
        hso.FindProperty("regainShare").floatValue = 0f;
        hso.FindProperty("grantsXp").boolValue = true;
        hso.ApplyModifiedPropertiesWithoutUndo();

        // Slowable by the Freeze Zone; the brain sets the animator speed itself,
        // so animatorToSlow stays empty.
        MagicSpellArtBuilder.AttachStatusReceiver(root);
        var statusVisual = root.GetComponentInChildren<Pickleball.Sandbox.Magic.Presentation.StatusReceiverVisual>(true);
        if (statusVisual != null) Set(statusVisual, "body", capsule);

        var rings = root.AddComponent<CelestialRingEmitter>();
        root.AddComponent<OrbiterRegistry>();
        var conics = root.AddComponent<ConicCaster>();
        Set(conics, "lobPrefab", lob);
        Set(conics, "orbiterPrefab", orbiter);
        Set(conics, "shotPrefab", shot);
        var burn = root.AddComponent<ArenaBurn>();
        var keeper = root.AddComponent<ScrollKeeper>();
        Set(keeper, "burn", burn);
        Set(keeper, "scrollPrefab", scroll);
        var barks = root.AddComponent<HypatiaBarks>();
        var pulse = root.AddComponent<KnockbackPulse>();

        var boss = root.AddComponent<HypatiaBoss>();
        var so = new SerializedObject(boss);
        so.FindProperty("config").objectReferenceValue = config;
        so.FindProperty("arenaCentre").objectReferenceValue = arena.transform;
        so.FindProperty("player").objectReferenceValue = player.transform;
        so.FindProperty("playerHealth").objectReferenceValue = player.GetComponent<Health>();
        so.FindProperty("rings").objectReferenceValue = rings;
        so.FindProperty("conics").objectReferenceValue = conics;
        so.FindProperty("burn").objectReferenceValue = burn;
        so.FindProperty("pulse").objectReferenceValue = pulse;
        so.FindProperty("body").objectReferenceValue = body;
        so.FindProperty("animator").objectReferenceValue = animator;
        so.FindProperty("barks").objectReferenceValue = barks;
        so.ApplyModifiedPropertiesWithoutUndo();

        BuildAstrolabe(root.transform);

        // The window ring under her (RecalibrationView shows it).
        var windowRing = HypatiaFightArt.Primitive(PrimitiveType.Quad, "WindowRing", root.transform,
            new Vector3(0f, 0.04f, 0f), Quaternion.Euler(90f, 0f, 0f), Vector3.one, HypatiaFightArt.MarkerRingMat());
        windowRing.AddComponent<LockOnIgnore>();
        windowRing.SetActive(false);

        if (player.GetComponent<Health>() == null)
            Debug.LogWarning("[HypatiaFight] The chibi has no Health; run Setup Newton Fight on ChibiSandbox.");
        return boss;
    }

    /// <summary>
    /// The astrolabe behind and above her, a child of her root so it always faces
    /// the player with her: the mater (dial face), the limb, ticks at e = 0, 1 and 2
    /// with minor ticks every 0.25, their labels, the needle on its pivot and the
    /// "e = …" readout above the dial (below it, it covered her).
    /// </summary>
    static void BuildAstrolabe(Transform bossRoot)
    {
        var brass = HypatiaFightArt.AstrolabeBrassMat();
        var face = HypatiaFightArt.PlateMat();
        var astrolabe = new GameObject("Astrolabe").transform;
        astrolabe.SetParent(bossRoot, false);
        astrolabe.localPosition = new Vector3(0f, 6.4f, -1.8f);
        // Measured with her, the dial put lock-on's aim 3 m above her head.
        astrolabe.gameObject.AddComponent<LockOnIgnore>();

        var faceDisc = HypatiaFightArt.Part("Mater", astrolabe, HypatiaFightArt.MeshAsset("AstrolabeMater",
            m => ArcMesh.SolidRing(m, 0f, 1.75f, -0.06f, 0.06f, 96)), face);
        faceDisc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var limb = HypatiaFightArt.Part("Limb", astrolabe, HypatiaFightArt.MeshAsset("AstrolabeLimb",
            m => ArcMesh.SolidRing(m, 1.75f, 2f, -0.12f, 0.12f, 96)), brass);
        limb.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        var throne = HypatiaFightArt.Part("Throne", astrolabe, HypatiaFightArt.MeshAsset("AstrolabeThrone",
            m => ArcMesh.SolidRing(m, 0.22f, 0.34f, -0.05f, 0.05f, 32)), brass);
        throne.transform.localPosition = new Vector3(0f, 2.3f, 0f);
        throne.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        const float dialHalf = 100f;   // AstrolabeNeedle.dialHalfDegrees default
        var font = NewtonFightArt.Font("Oswald Bold SDF");
        for (int i = 0; i <= 8; i++)
        {
            float e = i * 0.25f;
            float angle = AstrolabeNeedle.AngleForEccentricity(e, dialHalf);
            bool major = i % 4 == 0;
            float len = major ? 0.42f : 0.2f;
            Vector3 dir = Quaternion.Euler(0f, 0f, angle) * Vector3.up;
            HypatiaFightArt.Primitive(PrimitiveType.Cube, $"Tick_e{e:0.00}", astrolabe,
                dir * (1.7f - len * 0.5f) + new Vector3(0f, 0f, 0.08f), Quaternion.Euler(0f, 0f, angle),
                new Vector3(major ? 0.07f : 0.04f, len, 0.03f), brass);
            if (!major) continue;
            Text($"Label_e{e:0}", astrolabe, font, e.ToString("0"), 0.85f, new Color(1f, 0.9f, 0.7f),
                 dir * 1.0f + new Vector3(0f, 0f, 0.1f));
        }

        var pivot = new GameObject("NeedlePivot").transform;
        pivot.SetParent(astrolabe, false);
        pivot.localPosition = new Vector3(0f, 0f, 0.14f);
        pivot.localRotation = Quaternion.Euler(0f, 0f, 180f);   // at rest, as AstrolabeNeedle leaves it
        var needleMat = HypatiaFightArt.NeedleMat();
        var needle = HypatiaFightArt.Primitive(PrimitiveType.Cube, "Needle", pivot, new Vector3(0f, 0.7f, 0f),
                                               Quaternion.identity, new Vector3(0.09f, 1.45f, 0.04f), needleMat);
        HypatiaFightArt.Primitive(PrimitiveType.Cube, "Tail", pivot, new Vector3(0f, -0.25f, 0f),
                                  Quaternion.identity, new Vector3(0.16f, 0.4f, 0.04f), needleMat);
        HypatiaFightArt.Primitive(PrimitiveType.Sphere, "Hub", pivot, Vector3.zero, Quaternion.identity,
                                  Vector3.one * 0.22f, brass);

        var readout = Text("Readout", astrolabe, font, "e = 1.00", 1.3f, Color.white, new Vector3(0f, 3.25f, 0.1f));
        readout.gameObject.SetActive(false);

        // Parked for BuildViews to wire.
        _needlePivot = pivot;
        _needleRenderer = needle.GetComponent<Renderer>();
        _readout = readout;
        _astrolabeParts = new[] { limb.GetComponent<Renderer>(), throne.GetComponent<Renderer>() };
    }

    // Set by BuildAstrolabe, read by BuildViews within one Run.
    static Transform _needlePivot;
    static Renderer _needleRenderer;
    static TMP_Text _readout;
    static Renderer[] _astrolabeParts;

    /// <summary>World-space TMP text facing the player (+Z of her root), placed
    /// at <paramref name="localPosition"/>. The text sits on a child of a plain
    /// anchor: a RectTransform under a non-UI parent loses a localPosition set
    /// from script. TMP reads from its -Z side, so the text is turned half a turn.</summary>
    static TextMeshPro Text(string name, Transform parent, TMP_FontAsset font, string text, float size, Color colour,
                            Vector3 localPosition)
    {
        var anchor = new GameObject(name).transform;
        anchor.SetParent(parent, false);
        anchor.localPosition = localPosition;
        var go = new GameObject("Text");
        go.transform.SetParent(anchor, false);
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        var tmp = go.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size * 10f;
        tmp.color = colour;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(5f, 1.6f);
        return tmp;
    }

    // ── Presentation ────────────────────────────────────────────────────────

    /// <summary>One child per view component, each with its template and pool
    /// container, under a world-space root (not under her: her root turns).</summary>
    static void BuildViews(HypatiaConfig config, HypatiaBoss boss)
    {
        var root = new GameObject("HypatiaFightViews").transform;
        var emitter = boss.GetComponent<CelestialRingEmitter>();
        var caster = boss.GetComponent<ConicCaster>();
        var burn = boss.GetComponent<ArenaBurn>();
        var additive = HypatiaFightArt.AdditiveMat();

        // Rings.
        var ringsGo = ViewObject(root, "CelestialRings", out var ringTemplate, out var ringPool);
        var wall = HypatiaFightArt.Part("Wall", ringTemplate.transform, null, HypatiaFightCelestialArt.RingWallMat());
        NoShadows(wall);
        var ringView = ringsGo.AddComponent<CelestialRingView>();
        Set(ringView, "emitter", emitter);
        Set(ringView, "ringTemplate", ringTemplate);
        Set(ringView, "poolRoot", ringPool);
        SetInt(ringView, "prewarm", Mathf.Max(1, config.MaxLiveRings));
        Set(ringView, "sparks", HypatiaFightCelestialArt.RingSparks(ringsGo.transform));
        var viewCamera = Camera.main;
        if (viewCamera != null && viewCamera.TryGetComponent(out SandboxOrbitCamera ringCameraRig))
            Set(ringView, "cameraRig", ringCameraRig);

        // Gap forecasts.
        var gapGo = ViewObject(root, "GapForecasts", out var wedgeTemplate, out var wedgePool);
        wedgeTemplate.AddComponent<MeshFilter>();
        wedgeTemplate.AddComponent<MeshRenderer>().sharedMaterial = additive;
        NoShadows(wedgeTemplate);
        var gapView = gapGo.AddComponent<GapForecastView>();
        Set(gapView, "emitter", emitter);
        Set(gapView, "boss", boss);
        Set(gapView, "wedgeTemplate", wedgeTemplate);
        Set(gapView, "poolRoot", wedgePool);

        // Lob markers: a ring at the blast's edge, a fill disc growing inside it.
        var lobGo = ViewObject(root, "LobMarkers", out var markerTemplate, out var lobPool);
        var fill = HypatiaFightArt.Primitive(PrimitiveType.Quad, "Fill", markerTemplate.transform, Vector3.zero,
                                             Quaternion.identity, Vector3.one, HypatiaFightArt.MarkerFillMat());
        FlatQuad(fill);
        var ring = HypatiaFightArt.Primitive(PrimitiveType.Quad, "Ring", markerTemplate.transform,
                                             new Vector3(0f, 0.01f, 0f), Quaternion.identity, Vector3.one,
                                             HypatiaFightArt.MarkerRingMat());
        FlatQuad(ring);
        // Fill is scaled 0..1 in XZ by the view: nest the flat quad under a pivot.
        var fillPivot = new GameObject("Fill").transform;
        fillPivot.SetParent(markerTemplate.transform, false);
        fill.name = "Disc";
        fill.transform.SetParent(fillPivot, false);
        var flashTemplate = new GameObject("FlashTemplate");
        flashTemplate.transform.SetParent(lobGo.transform, false);
        var flashDisc = HypatiaFightArt.Primitive(PrimitiveType.Quad, "Disc", flashTemplate.transform, Vector3.zero,
                                                  Quaternion.identity, Vector3.one, HypatiaFightArt.GlowMat());
        FlatQuad(flashDisc);
        flashTemplate.SetActive(false);
        var lobView = lobGo.AddComponent<LobMarkerView>();
        Set(lobView, "caster", caster);
        Set(lobView, "boss", boss);
        Set(lobView, "markerTemplate", markerTemplate);
        Set(lobView, "flashTemplate", flashTemplate);
        Set(lobView, "poolRoot", lobPool);

        // Orbit paths.
        var orbitGo = ViewObject(root, "OrbitPaths", out var lineTemplate, out var orbitPool);
        ConfigureLine(lineTemplate.AddComponent<LineRenderer>(), additive, 0.07f, loop: true);
        var orbitView = orbitGo.AddComponent<OrbitPathView>();
        Set(orbitView, "caster", caster);
        Set(orbitView, "boss", boss);
        Set(orbitView, "lineTemplate", lineTemplate);
        Set(orbitView, "poolRoot", orbitPool);

        // Asymptotes: two solid lines and a dashed path, fixed (no pool).
        var asymGo = new GameObject("Asymptotes");
        asymGo.transform.SetParent(root, false);
        var incoming = LineChild(asymGo.transform, "Incoming", additive, 0.12f);
        var outgoing = LineChild(asymGo.transform, "Outgoing", additive, 0.12f);
        var path = LineChild(asymGo.transform, "Path", HypatiaFightArt.DashMat(), 0.32f);
        var asymView = asymGo.AddComponent<AsymptoteTelegraphView>();
        Set(asymView, "caster", caster);
        Set(asymView, "boss", boss);
        Set(asymView, "incoming", incoming);
        Set(asymView, "outgoing", outgoing);
        Set(asymView, "path", path);

        // Band fire: one overlay + flames slot per band.
        var fireGo = new GameObject("BandFire");
        fireGo.transform.SetParent(root, false);
        int bandCount = BandMath.Sanitise(config.BandOuterRadii, config.ArenaRadius).Length;
        var overlays = new MeshRenderer[bandCount];
        var flames = new ParticleSystem[bandCount];
        var alpha = HypatiaFightArt.AlphaMat();
        for (int i = 0; i < bandCount; i++)
        {
            var slot = new GameObject($"Band{i}").transform;
            slot.SetParent(fireGo.transform, false);
            var overlay = new GameObject("Overlay");
            overlay.transform.SetParent(slot, false);
            overlay.AddComponent<MeshFilter>();
            overlays[i] = overlay.AddComponent<MeshRenderer>();
            overlays[i].sharedMaterial = alpha;
            overlays[i].enabled = false;
            NoShadows(overlay);
            flames[i] = HypatiaFightArt.BandFlames(slot, "Flames");
        }
        var fireView = fireGo.AddComponent<BandFireView>();
        Set(fireView, "burn", burn);
        SetArray(fireView, "overlays", overlays);
        SetArray(fireView, "flames", flames);

        // Needle.
        var needleGo = new GameObject("AstrolabeNeedle");
        needleGo.transform.SetParent(root, false);
        var needle = needleGo.AddComponent<AstrolabeNeedle>();
        Set(needle, "boss", boss);
        Set(needle, "pivot", _needlePivot);
        Set(needle, "needleRenderer", _needleRenderer);
        Set(needle, "readout", _readout);

        // Recalibration.
        var recalGo = new GameObject("Recalibration");
        recalGo.transform.SetParent(root, false);
        var recal = recalGo.AddComponent<RecalibrationView>();
        var body = boss.transform.Find("Body");
        var windowRing = boss.transform.Find("WindowRing");
        Set(recal, "boss", boss);
        Set(recal, "glow", body.Find("Glow").GetComponent<Light>());
        Set(recal, "halo", body.Find("Halo").GetComponent<Renderer>());
        SetArray(recal, "astrolabeParts", _astrolabeParts);
        Set(recal, "windowRing", windowRing);
        Set(recal, "windowRingRenderer", windowRing.GetComponent<Renderer>());

        // Topple: pitches the model (not Body, which carries the hurtbox).
        var toppleGo = new GameObject("Topple");
        toppleGo.transform.SetParent(root, false);
        var topple = toppleGo.AddComponent<ToppleView>();
        var model = body.Find("HypatiaModel");
        Set(topple, "boss", boss);
        Set(topple, "model", model);
        Set(topple, "animator", model != null ? model.GetComponent<Animator>() : null);

        // Knockback pulse: the core on her body; the floor quad, dome, particles and
        // light in world space under this root (PulseView moves them to her). None
        // of those sit under her Health, so lock-on never measures them.
        var pulseGo = new GameObject("Pulse");
        pulseGo.transform.SetParent(root, false);
        var pulseFloor = HypatiaFightArt.Primitive(PrimitiveType.Quad, "Floor", pulseGo.transform, Vector3.zero,
                                                   Quaternion.Euler(90f, 0f, 0f), Vector3.one,
                                                   HypatiaFightPulseArt.FloorMat());
        NoShadows(pulseFloor);
        pulseFloor.SetActive(false);
        var dome = HypatiaFightArt.Primitive(PrimitiveType.Sphere, "Dome", pulseGo.transform, Vector3.zero,
                                             Quaternion.identity, Vector3.one, HypatiaFightPulseArt.ShellMat());
        NoShadows(dome);
        var ripple = HypatiaFightArt.Primitive(PrimitiveType.Sphere, "Ripple", dome.transform, Vector3.zero,
                                               Quaternion.identity, Vector3.one, HypatiaFightPulseArt.RippleMat());
        NoShadows(ripple);
        dome.SetActive(false);
        var pulseView = pulseGo.AddComponent<PulseView>();
        Set(pulseView, "pulse", boss.GetComponent<KnockbackPulse>());
        Set(pulseView, "boss", boss);
        Set(pulseView, "core", body.Find("PulseGlow").GetComponent<Renderer>());
        Set(pulseView, "gather", HypatiaFightPulseArt.Gather(pulseGo.transform));
        Set(pulseView, "floorQuad", pulseFloor.GetComponent<Renderer>());
        Set(pulseView, "dome", dome.transform);
        Set(pulseView, "domeRenderer", dome.GetComponent<Renderer>());
        Set(pulseView, "rippleRenderer", ripple.GetComponent<Renderer>());
        Set(pulseView, "sparks", HypatiaFightPulseArt.Sparks(pulseGo.transform));
        Set(pulseView, "dust", HypatiaFightPulseArt.Dust(pulseGo.transform));
        Set(pulseView, "flashLight", HypatiaFightPulseArt.FlashLight(pulseGo.transform));
        var mainCamera = Camera.main;
        if (mainCamera != null && mainCamera.TryGetComponent(out SandboxOrbitCamera orbitCamera))
            Set(pulseView, "cameraRig", orbitCamera);
    }

    static GameObject ViewObject(Transform root, string name, out GameObject template, out Transform pool)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root, false);
        template = new GameObject("Template");
        template.transform.SetParent(go.transform, false);
        template.SetActive(false);
        pool = new GameObject("Pool").transform;
        pool.SetParent(go.transform, false);
        return go;
    }

    static void FlatQuad(GameObject quad)
    {
        quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        NoShadows(quad);
    }

    static void NoShadows(params GameObject[] gos)
    {
        foreach (var go in gos)
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
    }

    static LineRenderer LineChild(Transform parent, string name, Material mat, float width)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        ConfigureLine(line, mat, width, loop: false);
        line.enabled = false;
        return line;
    }

    static void ConfigureLine(LineRenderer line, Material mat, float width, bool loop)
    {
        line.useWorldSpace = true;
        line.loop = loop;
        line.positionCount = 0;
        line.widthMultiplier = width;
        line.alignment = LineAlignment.TransformZ;
        line.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);   // lie flat on the plate
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = mat;
    }

    // ── HUD ─────────────────────────────────────────────────────────────────

    /// <summary>Re-points the HUD the copy brought from Newton's fight at her:
    /// the boss bar (her name, the notch moved to Phase Two At), the damage
    /// numbers, and the bark line (now reading her IBarkSource).</summary>
    static void RebindHud(HypatiaConfig config, HypatiaBoss boss, Health oldBossHealth)
    {
        var health = boss.GetComponent<Health>();
        var hud = GameObject.Find("FightHUD");
        if (hud == null)
        {
            Debug.LogWarning("[HypatiaFight] No FightHUD in ChibiSandbox (run Setup Newton Fight there); " +
                             "Hypatia has no health bar, damage numbers or bark line.");
            return;
        }

        foreach (var bar in hud.GetComponentsInChildren<HealthBarView>(true))
        {
            var so = new SerializedObject(bar);
            var source = so.FindProperty("source");
            bool isBossBar = (oldBossHealth != null && source.objectReferenceValue == oldBossHealth)
                             || (source.objectReferenceValue == null && bar.name == "BossHealthBar");
            if (!isBossBar) continue;
            source.objectReferenceValue = health;
            so.ApplyModifiedPropertiesWithoutUndo();

            var name = bar.transform.Find("Name");
            if (name != null && name.TryGetComponent(out TMP_Text plate))
            {
                bool dash = plate.font == null || plate.font.HasCharacter('—', searchFallbacks: true);
                plate.text = dash ? BossTitle : BossTitle.Replace("—", "-");
            }
            foreach (var notch in bar.GetComponentsInChildren<RectTransform>(true).Where(t => t.name == "PhaseNotch"))
            {
                notch.anchorMin = new Vector2(config.PhaseTwoAt, notch.anchorMin.y);
                notch.anchorMax = new Vector2(config.PhaseTwoAt, notch.anchorMax.y);
            }
        }

        var numbers = hud.GetComponentInChildren<DamageNumbers>(true);
        if (numbers != null)
        {
            var so = new SerializedObject(numbers);
            var sources = so.FindProperty("sources");
            bool found = false;
            for (int i = 0; i < sources.arraySize; i++)
            {
                var entry = sources.GetArrayElementAtIndex(i).FindPropertyRelative("health");
                if (entry.objectReferenceValue != null && entry.objectReferenceValue != oldBossHealth) continue;
                if (found) continue;
                entry.objectReferenceValue = health;
                found = true;
            }
            for (int i = sources.arraySize - 1; i >= 0; i--)
            {
                var entry = sources.GetArrayElementAtIndex(i).FindPropertyRelative("health");
                if (entry.objectReferenceValue == null) sources.DeleteArrayElementAtIndex(i);
            }
            if (!found)
            {
                sources.arraySize++;
                var entry = sources.GetArrayElementAtIndex(sources.arraySize - 1);
                entry.FindPropertyRelative("health").objectReferenceValue = health;
                entry.FindPropertyRelative("colour").colorValue = Color.white;
                entry.FindPropertyRelative("showRangeScaling").boolValue = true;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var bark = hud.GetComponentInChildren<BarkView>(true);
        if (bark != null)
        {
            bark.gameObject.name = "HypatiaBark";
            Set(bark, "source", boss.GetComponent<HypatiaBarks>());
            if (bark.TryGetComponent(out TMP_Text line)) line.color = new Color(0.8f, 0.86f, 1f);
        }
        else
        {
            Debug.LogWarning("[HypatiaFight] No BarkView under FightHUD; her lines will not show.");
        }
    }

    // ── Cleanup, player, lighting ───────────────────────────────────────────

    /// <summary>
    /// Removes Newton (and everything under his root), the academy model and its
    /// NPCs, the tether anchors and the pickups the plate has no use for. Keeps
    /// the double-jump and dash pickups, moved beside the spawn: the rings are
    /// designed around jumping and the 45° Angle Dash.
    /// </summary>
    static void StripSandbox(ChibiLocomotion player, Transform arena, GameObject hypatia)
    {
        var kept = new List<string>();
        int pickupSlot = 0;
        foreach (var go in player.gameObject.scene.GetRootGameObjects())
        {
            if (go == player.gameObject || go.transform == arena || go == hypatia || go.name == "HypatiaFightViews")
                continue;

            if (go.TryGetComponent(out AbilityPickup pickup))
            {
                var kind = (AbilityKind)new SerializedObject(pickup).FindProperty("ability").enumValueIndex;
                if (kind is AbilityKind.DoubleJump or AbilityKind.Dash)
                {
                    float x = pickupSlot++ == 0 ? -2.5f : 2.5f;
                    go.transform.position = SpawnPosition + new Vector3(x, 0.8f, -1.5f);
                    kept.Add(go.name);
                    continue;
                }
            }

            bool strip = go.name.Contains("Academy")
                         || go.GetComponentInChildren<NewtonBoss>(true) != null
                         || go.GetComponentInChildren<AbilityPickup>(true) != null
                         || go.GetComponentInChildren<TetherAnchor>(true) != null;
            if (strip) Object.DestroyImmediate(go);
            else kept.Add(go.name);
        }
        Debug.Log($"[HypatiaFight] Kept from ChibiSandbox: {player.name}, {string.Join(", ", kept)}");
    }

    static void PlacePlayer(ChibiLocomotion player)
    {
        var spawn = new GameObject("PlayerSpawn").transform;
        spawn.SetPositionAndRotation(SpawnPosition, Quaternion.identity);
        player.transform.SetPositionAndRotation(SpawnPosition, Quaternion.identity);

        var cam = Camera.main;
        if (cam == null) return;
        cam.transform.SetPositionAndRotation(SpawnPosition + new Vector3(0f, 2.4f, -5.2f), Quaternion.Euler(10f, 0f, 0f));
        cam.farClipPlane = Mathf.Max(cam.farClipPlane, 500f);
    }

    /// <summary>A deep indigo night: solid sky colour (the stars are particles),
    /// cool moonlight, indigo ambient and a thin indigo fog that swallows the
    /// void past the rim. The orbit camera's occlusion skips the perimeter wall.</summary>
    static void ApplyNight()
    {
        var sky = new Color(0.02f, 0.02f, 0.07f);
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.2f, 0.2f, 0.42f);
        RenderSettings.ambientEquatorColor = new Color(0.12f, 0.11f, 0.26f);
        RenderSettings.ambientGroundColor = new Color(0.05f, 0.04f, 0.1f);
        // No sky to reflect: the copied scene's reflection was baked from the
        // academy's daytime clouds, which streaked across the polished plate.
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;
        RenderSettings.reflectionIntensity = 0.4f;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = sky;
        RenderSettings.fogDensity = 0.006f;

        var cam = Camera.main;
        if (cam != null)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = sky;
            if (cam.TryGetComponent(out SandboxOrbitCamera orbit))
            {
                var so = new SerializedObject(orbit);
                var mask = so.FindProperty("occlusionMask");
                mask.intValue &= ~(1 << HypatiaFightArt.ProjectileLayer);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        var sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
            .FirstOrDefault(l => l.type == LightType.Directional);
        if (sun != null)
        {
            sun.transform.rotation = Quaternion.Euler(52f, -35f, 0f);
            sun.color = new Color(0.66f, 0.72f, 1f);
            sun.intensity = 0.7f;
            sun.shadows = LightShadows.Soft;
        }
    }

    // ── Serialized-field helpers ────────────────────────────────────────────

    static void Set(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"[HypatiaFight] {target.GetType().Name} has no field '{field}'.");
            return;
        }
        p.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetInt(Object target, string field, int value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"[HypatiaFight] {target.GetType().Name} has no field '{field}'.");
            return;
        }
        p.intValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetArray<T>(Object target, string field, T[] values) where T : Object
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"[HypatiaFight] {target.GetType().Name} has no field '{field}'.");
            return;
        }
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
