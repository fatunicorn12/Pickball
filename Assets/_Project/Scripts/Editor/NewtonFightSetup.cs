using Pickleball;
using Pickleball.Sandbox;
using Pickleball.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the Newton fight: the gravity-well and apple prefabs, the beam, the
/// health on both fighters and the HUD, then wires them together.
///
/// Tools ▸ Pickleball ▸ Setup Newton Fight
///
/// Separate from NewtonBossSetup, which owns the *character* — his rig, avatar,
/// controller and placement. This owns the *fight*. Keeping them apart means
/// re-importing the model does not rebuild the arena, and re-tuning the arena
/// does not trigger an FBX reimport.
///
/// Re-runnable: everything is found-or-created, so running it twice is a no-op
/// rather than a second set of duplicates.
/// </summary>
public static class NewtonFightSetup
{
    const string PrefabDir = "Assets/_Project/Prefabs/Boss";
    const string VfxDir = "Assets/_Project/Art/VFX";
    const string WellPrefabPath = PrefabDir + "/GravityWell.prefab";
    const string ApplePrefabPath = PrefabDir + "/Apple.prefab";
    const string MarkerPrefabPath = PrefabDir + "/AppleImpactMarker.prefab";
    const string OrbPrefabPath = PrefabDir + "/InverseSquareOrb.prefab";
    const string RewardPrefabPath = PrefabDir + "/Pickup_GravityAnchor.prefab";

    static readonly Color WellColour = new(0.42f, 0.26f, 0.95f);
    static readonly Color AppleColour = new(0.86f, 0.19f, 0.22f);
    static readonly Color MarkerColour = new(1f, 0.45f, 0.2f, 0.45f);
    static readonly Color OrbColour = new(1f, 0.85f, 0.35f);
    static readonly Color GoldenAppleColour = new(1f, 0.78f, 0.2f);

    [MenuItem("Tools/Pickleball/Setup Newton Fight")]
    public static void Run()
    {
        // Preconditions BEFORE any asset is written. Bailing out after the
        // prefabs were built leaves orphans on disk that the next run then
        // silently adopts.
        var boss = GameObject.Find("NewtonBoss");
        if (boss == null)
        {
            Debug.LogError("NewtonBoss not in the scene. Run " +
                           "Tools ▸ Pickleball ▸ Setup Newton (Boss) first. " +
                           "Note the required order: (Boss) rebuilds the root from " +
                           "scratch and would wipe everything this script attaches, " +
                           "so Fight must always be run last.");
            return;
        }

        var player = Object.FindFirstObjectByType<ChibiLocomotion>();
        if (player == null)
        {
            Debug.LogError("No ChibiLocomotion in the scene — open ChibiSandbox " +
                           "or run the chibi setup first.");
            return;
        }

        ChibiSandboxSetup.EnsureFolderPublic(PrefabDir);
        ChibiSandboxSetup.EnsureFolderPublic(VfxDir);

        var wellPrefab = BuildWellPrefab();
        var markerPrefab = BuildMarkerPrefab();
        var applePrefab = BuildApplePrefab();
        var orbPrefab = BuildOrbPrefab();
        var rewardPrefab = BuildRewardPrefab();

        SetupPlayerHealth(player);
        SetupBoss(boss, wellPrefab, applePrefab, markerPrefab, orbPrefab, rewardPrefab);
        BuildHud(player, boss);

        EditorUtility.SetDirty(boss);
        AssetDatabase.SaveAssets();

        // SaveAssets does NOT save scenes. Every component this script added is a
        // prefab-instance override living only in the open scene, so without an
        // explicit dirty the whole fight is one un-prompted scene switch away from
        // vanishing. Marked rather than saved: silently writing the user's scene
        // is not this script's call.
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(boss.scene);

        Debug.Log("Newton fight ready — wells, Fluxion beam, inverse-square volley, " +
                  "apple barrage, barks, Gravity Anchor reward and rolling HP wired. SAVE THE SCENE (Ctrl+S) to persist the wiring, " +
                  "then press Play.");
    }

    // ---- prefabs ---------------------------------------------------------

    static GravityWell BuildWellPrefab()
    {
        var root = new GameObject("GravityWell");

        // Core sphere: the visible mass. Collider stripped — the well is a field,
        // and a solid body at its centre would let the player stand on the very
        // thing that is supposed to be pulling them in.
        var core = PrimitiveFactory.CreateSphere("Core", 1f, WellColour, root.transform);
        Object.DestroyImmediate(core.GetComponent<Collider>());

        var debris = BuildDebrisSwirl(root.transform);

        var well = root.AddComponent<GravityWell>();
        var so = new SerializedObject(well);
        so.FindProperty("core").objectReferenceValue = core.transform;
        so.FindProperty("debris").objectReferenceValue = debris;
        so.ApplyModifiedPropertiesWithoutUndo();

        var saved = SavePrefab(root, WellPrefabPath);
        return saved.GetComponent<GravityWell>();
    }

    /// <summary>Debris spiralling inward. Built with a negative radial velocity so
    /// the particles genuinely converge rather than merely orbiting — the visual
    /// has to sell "this is pulling" at a glance, from outside its own radius.</summary>
    static ParticleSystem BuildDebrisSwirl(Transform parent)
    {
        var go = new GameObject("Debris");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop();

        var main = ps.main;
        main.duration = 4f;
        main.loop = true;
        main.startLifetime = 1.6f;
        main.startSpeed = 0f;
        main.startSize = 0.18f;
        main.startColor = new ParticleSystem.MinMaxGradient(
            WellColour, new Color(0.85f, 0.8f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 220;

        var emission = ps.emission;
        emission.rateOverTime = 55f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 6f;
        shape.radiusThickness = 0.25f;

        // Inward pull + orbit.
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        velocity.radial = new ParticleSystem.MinMaxCurve(-4.5f);
        velocity.orbitalY = new ParticleSystem.MinMaxCurve(2.2f);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(
            1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));

        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        // sharedMaterial, not material: touching .material at edit time makes Unity
        // instantiate a throwaway copy and leak it into the scene, and the prefab
        // would then be saved pointing at that copy instead of the shared asset.
        renderer.sharedMaterial =
            SandboxVfxBuilder.BuildGlowMaterial(VfxDir, "WellDebris", WellColour);

        var emitOnPlay = ps.main;
        emitOnPlay.playOnAwake = true;
        ps.Play();
        return ps;
    }

    static Apple BuildApplePrefab()
    {
        var root = PrimitiveFactory.CreateSphere("Apple", 0.55f, AppleColour);
        // No collider: Apple integrates and tests distances itself, exactly as
        // BallController does, so PhysX must not also get a vote.
        Object.DestroyImmediate(root.GetComponent<Collider>());

        var stalk = PrimitiveFactory.CreateBox("Stalk", new Vector3(0.05f, 0.18f, 0.05f),
                                               new Color(0.35f, 0.25f, 0.15f), root.transform);
        Object.DestroyImmediate(stalk.GetComponent<Collider>());
        stalk.transform.localPosition = new Vector3(0f, 0.32f, 0f);

        var apple = root.AddComponent<Apple>();

        // Both VFX slots were left null, which made annihilation invisible — two
        // apples simply vanished. That event IS the phase-two mechanic: it is how
        // the player learns wells bend apples into each other and that well-adjacent
        // ground is the safer ground. With no feedback the lesson cannot land.
        var splat = SandboxVfxBuilder.BuildCloudPuff(VfxDir, VfxDir + "/AppleSplat.prefab");
        var cancel = SandboxVfxBuilder.BuildCloudPuff(VfxDir, VfxDir + "/AppleCancel.prefab");

        var appleSo = new SerializedObject(apple);
        appleSo.FindProperty("splatEffect").objectReferenceValue = splat;
        appleSo.FindProperty("cancelEffect").objectReferenceValue = cancel;
        appleSo.ApplyModifiedPropertiesWithoutUndo();

        var saved = SavePrefab(root, ApplePrefabPath);
        return saved.GetComponent<Apple>();
    }

    /// <summary>One orb of the inverse-square ring. A glowing, collider-free
    /// sphere; the volley scales it with distance so its size reads as its damage.</summary>
    static GameObject BuildOrbPrefab()
    {
        var root = PrimitiveFactory.CreateSphere("InverseSquareOrb", 1f, OrbColour);
        Object.DestroyImmediate(root.GetComponent<Collider>());
        root.GetComponent<Renderer>().sharedMaterial =
            SandboxVfxBuilder.BuildGlowMaterial(VfxDir, "InverseSquareOrb", OrbColour);
        return SavePrefab(root, OrbPrefabPath);
    }

    /// <summary>
    /// Newton's drop: a golden apple that grants the Gravity Anchor. Laid out
    /// like the sandbox's other pickups — trigger on the root, bobbing visual
    /// on a child, a point light for the glow.
    /// </summary>
    static GameObject BuildRewardPrefab()
    {
        var root = new GameObject("Pickup_GravityAnchor");
        var trigger = root.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.radius = 0.9f;

        var visual = PrimitiveFactory.CreateSphere("Visual", 0.5f, GoldenAppleColour, root.transform);
        Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.GetComponent<Renderer>().sharedMaterial =
            SandboxVfxBuilder.BuildGlowMaterial(VfxDir, "GoldenApple", GoldenAppleColour);
        var stalk = PrimitiveFactory.CreateBox("Stalk", new Vector3(0.05f, 0.16f, 0.05f),
                                               new Color(0.35f, 0.25f, 0.15f), visual.transform);
        Object.DestroyImmediate(stalk.GetComponent<Collider>());
        stalk.transform.localPosition = new Vector3(0f, 0.6f, 0f);

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(root.transform, false);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 5f;
        light.color = GoldenAppleColour;
        light.intensity = 2.5f;

        var pickup = root.AddComponent<AbilityPickup>();
        var so = new SerializedObject(pickup);
        so.FindProperty("ability").enumValueIndex = (int)AbilityKind.GravityAnchor;
        so.FindProperty("visual").objectReferenceValue = visual.transform;
        so.FindProperty("glowLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return SavePrefab(root, RewardPrefabPath);
    }

    static GameObject BuildMarkerPrefab()
    {
        var root = PrimitiveFactory.CreateBox("AppleImpactMarker",
                                              new Vector3(1.3f, 0.02f, 1.3f),
                                              MarkerColour);
        Object.DestroyImmediate(root.GetComponent<Collider>());
        return SavePrefab(root, MarkerPrefabPath);
    }

    // ---- scene wiring ----------------------------------------------------

    static void SetupPlayerHealth(ChibiLocomotion player)
    {
        var health = player.GetComponent<Health>();
        if (health == null) health = player.gameObject.AddComponent<Health>();
        EditorUtility.SetDirty(player.gameObject);
        Debug.Log($"  player health on {player.name}");
    }

    static void SetupBoss(GameObject boss, GravityWell wellPrefab,
                          Apple applePrefab, GameObject markerPrefab,
                          GameObject orbPrefab, GameObject rewardPrefab)
    {
        // The fight components are rebuilt fresh every run rather than found and
        // reused. Their tunables are serialized into the scene, so an old instance
        // keeps the numbers it was created with and every change to the code
        // defaults is silently ignored — which is how the first tuning pass never
        // reached the game. Every reference is re-wired below, so nothing is lost.
        // NewtonBoss goes first: it requires Health, which is kept.
        RemoveIfPresent<NewtonBoss>(boss);
        RemoveIfPresent<NewtonBarks>(boss);
        RemoveIfPresent<InverseSquareVolley>(boss);
        RemoveIfPresent<AppleBarrage>(boss);
        RemoveIfPresent<FluxionBeam>(boss);

        var health = boss.GetComponent<Health>();
        if (health == null) health = boss.AddComponent<Health>();

        var bossHealth = new SerializedObject(health);
        bossHealth.FindProperty("maxHealth").floatValue = 600f;
        // The boss does not get the player's regain mechanic — a grey bar on the
        // thing you are hitting would quietly undo your damage.
        bossHealth.FindProperty("regainShare").floatValue = 0f;
        bossHealth.ApplyModifiedPropertiesWithoutUndo();

        var barrage = boss.AddComponent<AppleBarrage>();
        var barrageSo = new SerializedObject(barrage);
        barrageSo.FindProperty("applePrefab").objectReferenceValue = applePrefab;
        barrageSo.FindProperty("impactMarkerPrefab").objectReferenceValue = markerPrefab;
        barrageSo.ApplyModifiedPropertiesWithoutUndo();

        // Beam lives on a muzzle child so it fires from his chest rather than
        // from between his feet.
        var muzzle = boss.transform.Find("BeamMuzzle");
        if (muzzle == null)
        {
            muzzle = new GameObject("BeamMuzzle").transform;
            muzzle.SetParent(boss.transform, false);
            muzzle.localPosition = new Vector3(0f, 1.6f, 0.4f);
        }

        var beam = boss.AddComponent<FluxionBeam>();

        var line = muzzle.GetComponent<LineRenderer>();
        if (line == null) line = muzzle.gameObject.AddComponent<LineRenderer>();
        line.positionCount = 2;
        line.useWorldSpace = true;
        line.enabled = false;
        // sharedMaterial for the same reason as the debris renderer — with .material
        // every re-run of this menu item leaked another instance into the scene.
        line.sharedMaterial = SandboxVfxBuilder.BuildGlowMaterial(VfxDir, "FluxionBeam",
                                                            new Color(0.3f, 0.7f, 1f));

        var beamSo = new SerializedObject(beam);
        beamSo.FindProperty("muzzle").objectReferenceValue = muzzle;
        beamSo.FindProperty("line").objectReferenceValue = line;
        beamSo.ApplyModifiedPropertiesWithoutUndo();

        var volley = boss.AddComponent<InverseSquareVolley>();
        var volleySo = new SerializedObject(volley);
        volleySo.FindProperty("orbPrefab").objectReferenceValue = orbPrefab;
        volleySo.ApplyModifiedPropertiesWithoutUndo();

        var barks = boss.AddComponent<NewtonBarks>();

        var newton = boss.AddComponent<NewtonBoss>();

        var model = boss.transform.Find("NewtonModel");
        var newtonSo = new SerializedObject(newton);
        newtonSo.FindProperty("wellPrefab").objectReferenceValue = wellPrefab;
        newtonSo.FindProperty("beam").objectReferenceValue = beam;
        newtonSo.FindProperty("barrage").objectReferenceValue = barrage;
        newtonSo.FindProperty("volley").objectReferenceValue = volley;
        newtonSo.FindProperty("barks").objectReferenceValue = barks;
        newtonSo.FindProperty("rewardPrefab").objectReferenceValue = rewardPrefab;
        if (model != null) newtonSo.FindProperty("body").objectReferenceValue = model;
        newtonSo.FindProperty("animator").objectReferenceValue =
            boss.GetComponentInChildren<Animator>();
        newtonSo.ApplyModifiedPropertiesWithoutUndo();

        Debug.Log("  boss: Health(600) + NewtonBoss + FluxionBeam + InverseSquareVolley " +
                  "+ AppleBarrage + NewtonBarks");
    }

    static void RemoveIfPresent<T>(GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        if (existing != null) Object.DestroyImmediate(existing);
    }

    /// <summary>Two bars: the player's bottom-left with its grey regain region,
    /// and Newton's across the top.</summary>
    static void BuildHud(ChibiLocomotion player, GameObject boss)
    {
        var canvasGo = GameObject.Find("FightHUD");
        if (canvasGo == null)
        {
            canvasGo = new GameObject("FightHUD", typeof(Canvas),
                                      typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
        }

        BuildBar(canvasGo.transform, "PlayerHealthBar", player.GetComponent<Health>(),
                 new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 40f),
                 new Vector2(520f, 34f), new Color(0.35f, 0.85f, 0.45f));

        BuildBar(canvasGo.transform, "BossHealthBar", boss.GetComponent<Health>(),
                 new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -50f),
                 new Vector2(900f, 22f), new Color(0.85f, 0.3f, 0.35f));

        BuildBarkLine(canvasGo.transform, boss.GetComponent<NewtonBarks>());

        EditorUtility.SetDirty(canvasGo);
    }

    /// <summary>Newton's subtitle line, just under his health bar.</summary>
    static void BuildBarkLine(Transform parent, NewtonBarks source)
    {
        const string name = "NewtonBark";
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -84f);
        rect.sizeDelta = new Vector2(1400f, 80f);

        var text = go.AddComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Top;
        text.fontSize = 34f;
        text.fontStyle = FontStyles.Italic;
        text.color = new Color(1f, 0.93f, 0.78f);
        text.textWrappingMode = TextWrappingModes.Normal;
        text.raycastTarget = false;
        text.text = "";

        var view = go.AddComponent<BarkView>();
        var so = new SerializedObject(view);
        so.FindProperty("source").objectReferenceValue = source;
        so.FindProperty("text").objectReferenceValue = text;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void BuildBar(Transform parent, string name, Health source,
                         Vector2 anchorMin, Vector2 anchorMax, Vector2 offset,
                         Vector2 size, Color fillColour)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(anchorMin.x, anchorMin.y);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;

        // Track, grey, then solid — painted back to front.
        AddImage(rect, "Track", new Color(0.08f, 0.08f, 0.1f, 0.85f), out _);
        AddImage(rect, "Recoverable", new Color(0.75f, 0.75f, 0.78f, 0.9f),
                 out Image recoverable);
        AddImage(rect, "Current", fillColour, out Image current);

        foreach (var img in new[] { recoverable, current })
        {
            img.type = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = (int)Image.OriginHorizontal.Left;
            img.fillAmount = 1f;
        }

        var view = root.AddComponent<HealthBarView>();
        var so = new SerializedObject(view);
        so.FindProperty("source").objectReferenceValue = source;
        so.FindProperty("currentFill").objectReferenceValue = current;
        so.FindProperty("recoverableFill").objectReferenceValue = recoverable;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void AddImage(RectTransform parent, string name, Color colour, out Image image)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        image = go.GetComponent<Image>();
        image.color = colour;

        // A sprite is NOT optional here, even though the bar is a plain rectangle.
        // Image.OnPopulateMesh bails to a simple quad when sprite is null and never
        // reaches the switch on Image.Type — so Type.Filled and every fillAmount
        // write would be silently ignored and the bars would sit frozen at full.
        //
        // GetBuiltinExtraResource, not Resources.GetBuiltinResource: the UI skin
        // lives in Unity's builtin EXTRA resources, and the plain call returns null
        // for these paths. It fails silently — you get a null sprite, no exception,
        // and a frozen bar that looks like a data-binding bug rather than a missing
        // asset. Asserted below so it can never fail quietly again.
        image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        if (image.sprite == null)
            Debug.LogError("Could not load the builtin UI sprite. Health bars will " +
                           "render as solid blocks and will not track damage — " +
                           "Image ignores Type.Filled entirely without a sprite.");
        image.raycastTarget = false;
    }

    static GameObject SavePrefab(GameObject instance, string path)
    {
        var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
        Object.DestroyImmediate(instance);
        return saved;
    }
}
