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
        var well = root.AddComponent<GravityWell>();
        var so = new SerializedObject(well);

        // Core sphere: the visible mass, drawn dark and glossy inside a bright
        // halo — that reads as "heavy" in a way a glowing ball never did. Lit,
        // not unlit: an unlit near-black sphere rendered as a flat hole in the
        // screen, with no shape at all. Collider stripped — the well is a field, and a solid body at
        // its centre would let the player stand on the very thing that is
        // supposed to be pulling them in.
        var core = PrimitiveFactory.CreateSphere("Core", 1f, WellColour, root.transform);
        Object.DestroyImmediate(core.GetComponent<Collider>());
        core.GetComponent<Renderer>().sharedMaterial =
            NewtonFightArt.LitMaterial(VfxDir, "WellCore", new Color(0.1f, 0.04f, 0.2f), 0.92f,
                                       emission: new Color(0.07f, 0.02f, 0.18f));

        // Child of the core, so it breathes and ramps with it.
        NewtonFightArt.Quad("Halo", core.transform,
            NewtonFightArt.ParticleMaterial("WellHalo", NewtonFightArt.WellHalo(),
                                            new Color(0.72f, 0.5f, 1f, 0.7f), additive: true),
            2.2f, flat: false);

        // The floor decal draws the field to scale, contour rings at the same
        // distances as the ladder in GravityWell's class comment.
        var fieldTexture = NewtonFightArt.WellField(so.FindProperty("coreRadius").floatValue,
                                                    so.FindProperty("influenceRadius").floatValue,
                                                    new[] { 3f, 5f, 8f });
        var field = NewtonFightArt.Quad("Field", root.transform,
            NewtonFightArt.ParticleMaterial("WellField", fieldTexture,
                                            new Color(0.5f, 0.28f, 0.95f, 0.85f), additive: false,
                                            twoSided: false),
            1f, flat: true);
        // Placeholder height; GravityWell drops it onto the real ground at spawn.
        field.transform.localPosition = new Vector3(0f, 0.05f, 0f);

        var debris = BuildDebrisSwirl(root.transform);

        so.FindProperty("core").objectReferenceValue = core.transform;
        so.FindProperty("debris").objectReferenceValue = debris;
        so.FindProperty("field").objectReferenceValue = field.GetComponent<Renderer>();
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
        // Textured: an untextured particle material draws every mote as a hard
        // square. White tint — the purple comes from startColor above.
        renderer.sharedMaterial = NewtonFightArt.ParticleMaterial(
            "WellDebris", NewtonFightArt.Glow(), Color.white, additive: true);

        var emitOnPlay = ps.main;
        emitOnPlay.playOnAwake = true;
        ps.Play();
        return ps;
    }

    static Apple BuildApplePrefab()
    {
        // No collider anywhere: Apple integrates and tests distances itself,
        // exactly as BallController does, so PhysX must not also get a vote.
        var root = new GameObject("Apple");
        NewtonFightArt.AppleVisual(root.transform, 0.6f);

        var apple = root.AddComponent<Apple>();

        // Two different effects, deliberately. Annihilation IS the phase-two
        // mechanic — it is how the player learns wells bend apples into each
        // other and that well-adjacent ground is the safer ground — so it must
        // not look like the splat of an apple that got through.
        var splat = NewtonFightArt.AppleSplat(VfxDir + "/AppleSplat.prefab");
        var cancel = NewtonFightArt.AppleCancel(VfxDir + "/AppleCancel.prefab");

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
        // Hot white core inside a gold halo. The halo is a child, so it scales
        // with the orb and the size-is-damage reading carries over to it.
        root.GetComponent<Renderer>().sharedMaterial =
            NewtonFightArt.UnlitMaterial("InverseSquareOrbCore", new Color(1f, 0.96f, 0.8f));
        NewtonFightArt.Quad("Halo", root.transform,
            NewtonFightArt.ParticleMaterial("OrbHalo", NewtonFightArt.Glow(),
                                            new Color(OrbColour.r, OrbColour.g * 0.9f, OrbColour.b, 0.9f),
                                            additive: true),
            2.8f, flat: false);
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

        // The same apple the barrage throws, gilded — so the reward is visibly
        // the thing the fight was made of.
        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        // Lit with a gold emission rather than flat unlit: unlit, the apple's
        // facets vanished and it read as a yellow disc.
        NewtonFightArt.AppleVisual(visual.transform, 0.55f,
            NewtonFightArt.LitMaterial(VfxDir, "GoldenAppleLit", GoldenAppleColour, 0.8f,
                                       emission: GoldenAppleColour * 0.35f));

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

    /// <summary>Diameter of the apple telegraph: twice the apple's hit radius
    /// plus the player's body radius (0.55 + 0.45), so standing outside the
    /// ring really is standing outside the hit. The old slab was 1.3 m and
    /// under-promised the danger by a third.</summary>
    const float MarkerDiameter = 2f;

    static GameObject BuildMarkerPrefab()
    {
        var root = new GameObject("AppleImpactMarker");
        // Scaled flat in XZ on the root, so the ImpactMarker can drive the disc
        // as a 0..1 fraction of the ring.
        root.transform.localScale = new Vector3(MarkerDiameter, 1f, MarkerDiameter);

        var fillMat = NewtonFightArt.ParticleMaterial("MarkerFill", NewtonFightArt.Disc(),
                                                      new Color(0.7f, 0.1f, 0.06f, 0.45f), additive: false);
        var ringMat = NewtonFightArt.ParticleMaterial("MarkerRing", NewtonFightArt.TargetRing(),
                                                      Color.white, additive: false);
        // Ring over disc, whatever the camera distance sorting decides.
        fillMat.renderQueue = 3000;
        ringMat.renderQueue = 3001;

        var fill = NewtonFightArt.Quad("Fill", root.transform, fillMat, 1f, flat: true);
        var ring = NewtonFightArt.Quad("Ring", root.transform, ringMat, 1f, flat: true);
        ring.transform.localPosition = new Vector3(0f, 0.01f, 0f);

        var marker = root.AddComponent<ImpactMarker>();
        var so = new SerializedObject(marker);
        so.FindProperty("fill").objectReferenceValue = fill.transform;
        so.FindProperty("ring").objectReferenceValue = ring.GetComponent<Renderer>();
        so.ApplyModifiedPropertiesWithoutUndo();

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
        // White: the beam's own cool-to-hot ramp arrives as vertex colour.
        line.sharedMaterial = NewtonFightArt.ParticleMaterial(
            "FluxionBeam", NewtonFightArt.BeamStreak(), Color.white, additive: true);
        // Tiled at 4 m a repeat, so the streaks keep their size at any range
        // and FluxionBeam can scroll them in metres.
        line.textureMode = LineTextureMode.Tile;
        line.textureScale = new Vector2(0.25f, 1f);
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var impact = NewtonFightArt.BeamImpact(boss.transform);

        var beamSo = new SerializedObject(beam);
        beamSo.FindProperty("muzzle").objectReferenceValue = muzzle;
        beamSo.FindProperty("line").objectReferenceValue = line;
        beamSo.FindProperty("impact").objectReferenceValue = impact;
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

    /// <summary>Two bars — the player's bottom-left with its grey regain region
    /// and a number, Newton's across the top under his name with a notch where
    /// phase two begins — plus the bark line and the damage numbers.</summary>
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

        var style = new BarStyle
        {
            Mask = NewtonFightArt.BarMask(),
            Frame = NewtonFightArt.BarFrame(),
            Fill = NewtonFightArt.BarFill(),
            Font = NewtonFightArt.Font("Oswald Bold SDF"),
        };
        style.FontMaterial = NewtonFightArt.OutlinedFontMaterial(style.Font, "HudLabel", 0.2f);

        // Damage numbers first, so they draw under the bars rather than over them.
        BuildDamageNumbers(canvasGo.transform, player.GetComponent<Health>(), boss.GetComponent<Health>());

        var playerBar = BuildBar(canvasGo.transform, "PlayerHealthBar", player.GetComponent<Health>(), style,
                                 new Vector2(0f, 0f), new Vector2(48f, 48f), new Vector2(520f, 30f),
                                 fillColour: new Color(0.38f, 0.88f, 0.46f),
                                 frameColour: new Color(0.78f, 0.8f, 0.86f),
                                 recoverable: true);
        AddValueText(playerBar, style);

        var bossBar = BuildBar(canvasGo.transform, "BossHealthBar", boss.GetComponent<Health>(), style,
                               new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(900f, 24f),
                               fillColour: new Color(0.9f, 0.28f, 0.3f),
                               frameColour: new Color(1f, 0.8f, 0.42f),
                               recoverable: false);
        AddNamePlate(bossBar, style, "SIR ISAAC NEWTON");

        var newton = boss.GetComponent<NewtonBoss>();
        if (newton != null)
            AddNotch(bossBar, new SerializedObject(newton).FindProperty("phaseTwoAt").floatValue);

        BuildBarkLine(canvasGo.transform, boss.GetComponent<NewtonBarks>());

        EditorUtility.SetDirty(canvasGo);
    }

    struct BarStyle
    {
        public Sprite Mask, Frame, Fill;
        public TMP_FontAsset Font;
        public Material FontMaterial;
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
        rect.anchoredPosition = new Vector2(0f, -108f);
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

    /// <summary>
    /// One bar, painted back to front:
    ///
    ///   Well (rounded track, and the Mask that rounds everything inside it)
    ///     Trail → Recoverable → Current → Flash
    ///   Frame (hollow bevel, 5 px proud of the well)
    ///
    /// The fills are plain gradient strips; the Mask is what gives them the
    /// rounded ends. A Filled image cannot be 9-sliced, so a rounded fill sprite
    /// would have its corners stretched into ovals and then cut off square at
    /// the fill edge.
    /// </summary>
    static RectTransform BuildBar(Transform parent, string name, Health source, BarStyle style,
                                  Vector2 anchor, Vector2 offset, Vector2 size,
                                  Color fillColour, Color frameColour, bool recoverable)
    {
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        var rect = (RectTransform)root.transform;
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;

        var well = AddImage(rect, "Well", new Color(0.06f, 0.05f, 0.08f, 0.92f), style.Mask, Image.Type.Sliced);
        well.gameObject.AddComponent<Mask>().showMaskGraphic = true;

        var trail = AddFill(well.rectTransform, "Trail", new Color(1f, 0.93f, 0.72f), style.Fill);
        Image recoverableFill = recoverable
            ? AddFill(well.rectTransform, "Recoverable", new Color(0.66f, 0.66f, 0.7f), style.Fill)
            : null;
        var current = AddFill(well.rectTransform, "Current", fillColour, style.Fill);

        var flash = AddImage(well.rectTransform, "Flash", new Color(1f, 1f, 1f, 0f), style.Fill, Image.Type.Simple);

        var frame = AddImage(rect, "Frame", frameColour, style.Frame, Image.Type.Sliced);
        frame.rectTransform.offsetMin = new Vector2(-5f, -5f);
        frame.rectTransform.offsetMax = new Vector2(5f, 5f);

        var view = root.AddComponent<HealthBarView>();
        var so = new SerializedObject(view);
        so.FindProperty("source").objectReferenceValue = source;
        so.FindProperty("currentFill").objectReferenceValue = current;
        so.FindProperty("recoverableFill").objectReferenceValue = recoverableFill;
        so.FindProperty("trailFill").objectReferenceValue = trail;
        so.FindProperty("flash").objectReferenceValue = flash;
        // Only the player's bar should panic. A boss bar flashing red at 25%
        // would read as a warning to the person winning.
        if (!recoverable) so.FindProperty("lowThreshold").floatValue = 0f;
        so.ApplyModifiedPropertiesWithoutUndo();
        return rect;
    }

    static Image AddFill(RectTransform parent, string name, Color colour, Sprite sprite)
    {
        var img = AddImage(parent, name, colour, sprite, Image.Type.Filled);
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = (int)Image.OriginHorizontal.Left;
        img.fillAmount = 1f;
        return img;
    }

    /// <summary>"73 / 100" inside the right end of the player's bar.</summary>
    static void AddValueText(RectTransform bar, BarStyle style)
    {
        var text = AddText(bar, "Value", style, 20f, TextAlignmentOptions.Right);
        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(0f, 0f);
        rect.offsetMax = new Vector2(-12f, 0f);
        text.color = new Color(1f, 1f, 1f, 0.95f);

        var so = new SerializedObject(bar.GetComponent<HealthBarView>());
        so.FindProperty("valueText").objectReferenceValue = text;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>The boss's name, letter-spaced over the top of his bar.</summary>
    static void AddNamePlate(RectTransform bar, BarStyle style, string label)
    {
        var text = AddText(bar, "Name", style, 30f, TextAlignmentOptions.Bottom);
        text.text = label;
        text.characterSpacing = 12f;
        text.color = new Color(1f, 0.92f, 0.76f);
        var rect = text.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 8f);
        rect.sizeDelta = new Vector2(0f, 40f);
    }

    /// <summary>A dark tick across the bar where phase two starts, so the
    /// player can see the barrage coming rather than be surprised by it.
    /// Inside the well, so the mask clips it to the bar's shape.</summary>
    static void AddNotch(RectTransform bar, float at)
    {
        var well = bar.Find("Well") as RectTransform;
        if (well == null) return;
        var notch = new GameObject("PhaseNotch", typeof(RectTransform), typeof(Image));
        notch.transform.SetParent(well, false);
        // Above the fills, below the flash.
        notch.transform.SetSiblingIndex(well.Find("Flash").GetSiblingIndex());
        var rect = (RectTransform)notch.transform;
        rect.anchorMin = new Vector2(at, 0f);
        rect.anchorMax = new Vector2(at, 1f);
        rect.sizeDelta = new Vector2(4f, 0f);
        var img = notch.GetComponent<Image>();
        img.color = new Color(0.08f, 0.04f, 0.05f, 0.85f);
        img.raycastTarget = false;
    }

    static TextMeshProUGUI AddText(RectTransform parent, string name, BarStyle style,
                                   float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = style.Font;
        text.fontSharedMaterial = style.FontMaterial;
        text.fontSize = size;
        text.alignment = alignment;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>
    /// The damage-number layer: a full-screen rect holding one hidden template
    /// that <see cref="DamageNumbers"/> clones into its pool. Newton's numbers
    /// get the weak-point styling; the player's merge over a longer window,
    /// because nearly everything that hurts the player hurts every frame.
    /// </summary>
    static void BuildDamageNumbers(Transform canvas, Health player, Health boss)
    {
        const string name = "DamageNumbers";
        var existing = canvas.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(canvas, false);
        go.transform.SetAsFirstSibling();
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var font = NewtonFightArt.Font("Bangers SDF");
        var templateGo = new GameObject("Template", typeof(RectTransform));
        templateGo.transform.SetParent(rect, false);
        var template = templateGo.AddComponent<TextMeshProUGUI>();
        template.font = font;
        template.fontSharedMaterial = NewtonFightArt.OutlinedFontMaterial(font, "DamageNumber", 0.28f);
        template.fontSize = 46f;
        template.alignment = TextAlignmentOptions.Center;
        template.textWrappingMode = TextWrappingModes.NoWrap;
        template.raycastTarget = false;
        template.text = "0";
        template.rectTransform.sizeDelta = new Vector2(300f, 90f);

        var numbers = go.AddComponent<DamageNumbers>();
        var so = new SerializedObject(numbers);
        so.FindProperty("canvas").objectReferenceValue = rect;
        so.FindProperty("template").objectReferenceValue = template;
        so.FindProperty("baseFontSize").floatValue = 46f;

        var sources = so.FindProperty("sources");
        sources.arraySize = 2;
        WriteSource(sources.GetArrayElementAtIndex(0), boss, Color.white,
                    showRangeScaling: true, minHeight: 1.8f, mergeWindow: 0.1f);
        WriteSource(sources.GetArrayElementAtIndex(1), player, new Color(1f, 0.36f, 0.3f),
                    showRangeScaling: false, minHeight: 1.5f, mergeWindow: 0.3f);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void WriteSource(SerializedProperty element, Health health, Color colour,
                            bool showRangeScaling, float minHeight, float mergeWindow)
    {
        element.FindPropertyRelative("health").objectReferenceValue = health;
        element.FindPropertyRelative("colour").colorValue = colour;
        element.FindPropertyRelative("permanentColour").colorValue = new Color(0.45f, 0.8f, 1f);
        element.FindPropertyRelative("showRangeScaling").boolValue = showRangeScaling;
        element.FindPropertyRelative("minHeight").floatValue = minHeight;
        element.FindPropertyRelative("mergeWindow").floatValue = mergeWindow;
    }

    static Image AddImage(RectTransform parent, string name, Color colour, Sprite sprite, Image.Type type)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var image = go.GetComponent<Image>();
        image.color = colour;
        image.type = type;

        // A sprite is NOT optional here, even for a plain fill. Image.OnPopulateMesh
        // bails to a simple quad when sprite is null and never reaches the switch on
        // Image.Type — so Type.Filled and every fillAmount write would be silently
        // ignored and the bars would sit frozen at full. Asserted so a failed
        // texture generation can never pass for a data-binding bug.
        image.sprite = sprite;
        if (image.sprite == null)
            Debug.LogError($"Health bar sprite for '{name}' failed to generate. The bar " +
                           "will not track damage — Image ignores Type.Filled entirely " +
                           "without a sprite.");
        image.raycastTarget = false;
        return image;
    }

    static GameObject SavePrefab(GameObject instance, string path)
    {
        var saved = PrefabUtility.SaveAsPrefabAsset(instance, path);
        Object.DestroyImmediate(instance);
        return saved;
    }
}
