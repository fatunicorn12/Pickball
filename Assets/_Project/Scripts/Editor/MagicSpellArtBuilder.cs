using System;
using System.IO;
using Pickleball.Sandbox.Magic;
using Pickleball.Sandbox.Magic.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Builds the procedural art of the spells beyond the orbs, for
/// <see cref="MagicSetup"/> (Tools ▸ Pickleball ▸ Setup Magic):
///  - the Thunderbolt's <see cref="LightningStrike"/> prefab (telegraph ring and
///    disc, cloud, bolt, branch, chain arcs, flash, shock ring, sparks);
///  - the Freeze Zone's <see cref="FreezeZone"/> prefab (frost disc, draining
///    rim, ice crystals, snow, frost puff, cold light);
///  - the Fireball's <see cref="BurnStatus"/> prefab (flames, embers, light);
///  - the Heal channel's visual under the player (<see cref="HealChannelVisual"/>);
///  - the frost-and-stars status visual under anything that opts in to slows
///    and staggers (<see cref="StatusReceiverVisual"/>).
///
/// Idempotent like <see cref="MagicOrbPrefabBuilder"/>: prefabs are rebuilt from
/// scratch and saved over the same path (GUIDs kept, so the spells' references
/// hold); scene children are destroyed by name and rebuilt; textures, the
/// crystal mesh and materials are rewritten in place. Every gameplay root is
/// collider-free, and every visual sits on a child that the gameplay root's
/// visual can detach (see the orb's cleanup notes).
///
/// Textures and the crystal mesh go to <see cref="ArtDir"/>; materials go
/// through NewtonFightArt's helpers into its VFX folder, named Thunder*, Frost*,
/// Burn*, Heal* and Status* so they cannot collide with anything else's.
/// </summary>
public static class MagicSpellArtBuilder
{
    /// <summary>Where the spells' generated textures and meshes live.</summary>
    public const string ArtDir = "Assets/_Project/Art/VFX/Magic";

    const string HealVfxName = "HealChannelVfx";
    const string StatusVfxName = "MagicStatusVfx";
    const int MaxChainLinks = 4;
    const int CrystalCount = 14;

    static readonly Color BoltBlue = new(0.75f, 0.88f, 1f);
    static readonly Color StormYellow = new(1f, 0.9f, 0.35f);
    static readonly Color Frost = new(0.62f, 0.9f, 1f);
    static readonly Color IceDeep = new(0.35f, 0.7f, 0.95f);
    static readonly Color FlameCore = new(1f, 0.92f, 0.6f);
    static readonly Color FlameOrange = new(1f, 0.45f, 0.08f);
    static readonly Color FlameRed = new(0.85f, 0.12f, 0.03f);
    static readonly Color Ash = new(0.2f, 0.07f, 0.04f);
    static readonly Color HealGreen = new(0.45f, 1f, 0.55f);
    static readonly Color HealGold = new(1f, 0.95f, 0.55f);

    // ===================================================================
    // Thunderbolt
    // ===================================================================

    /// <summary>Builds (or rebuilds) the strike prefab; returns its
    /// <see cref="LightningStrike"/>, or null if the save failed.</summary>
    public static LightningStrike BuildStrike(string prefabPath)
    {
        PrepareFolders(prefabPath);
        var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
        var strike = root.AddComponent<LightningStrike>();
        var visualGo = new GameObject("Visual");
        visualGo.transform.SetParent(root.transform, false);
        var visual = visualGo.AddComponent<LightningStrikeVisual>();
        var parent = visualGo.transform;

        var lineMat = NewtonFightArt.ParticleMaterial("ThunderLine", LineCore(), Color.white, additive: true);
        // Alpha-blended layers: these must read on a white floor too.
        var blendLineMat = NewtonFightArt.ParticleMaterial("ThunderBlendLine", LineCore(), Color.white, additive: false);
        var ring = Line("Ring", parent, blendLineMat, 0.1f, worldSpace: true, loop: true);
        var marker = NewtonFightArt.Quad("Marker", parent,
            NewtonFightArt.ParticleMaterial("ThunderMarker", NewtonFightArt.TargetRing(), Color.white, additive: true),
            1f, flat: true).GetComponent<Renderer>();

        var cloud = new GameObject("Cloud").transform;
        cloud.SetParent(parent, false);
        var cloudMat = NewtonFightArt.ParticleMaterial("ThunderCloud", CloudPuff(), Color.white, additive: false);
        var puffs = new Renderer[7];
        var rng = new System.Random(7);
        for (int i = 0; i < puffs.Length; i++)
        {
            float a = i * Mathf.PI * 2f / (puffs.Length - 1);
            Vector3 at = i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(a) * 1.3f, (float)rng.NextDouble() * 0.4f - 0.2f, Mathf.Sin(a) * 1.3f);
            var puff = NewtonFightArt.Quad($"Puff{i}", cloud, cloudMat, i == 0 ? 3.4f : 2.4f + (float)rng.NextDouble(), flat: false);
            puff.transform.localPosition = at;
            puffs[i] = puff.GetComponent<Renderer>();
        }
        var cloudLight = PointLight("CloudLight", cloud, BoltBlue, 10f, 0.2f);

        var boltGlow = Line("BoltGlow", parent, blendLineMat, 0.9f, worldSpace: true, loop: false);
        var bolt = Line("Bolt", parent, lineMat, 0.3f, worldSpace: true, loop: false);
        var branch = Line("Branch", parent, lineMat, 0.12f, worldSpace: true, loop: false);
        var chains = new LineRenderer[MaxChainLinks];
        for (int i = 0; i < chains.Length; i++)
            chains[i] = Line($"Chain{i}", parent, blendLineMat, 0.16f, worldSpace: true, loop: false);
        var flash = PointLight("Flash", parent, BoltBlue, 12f, 0f);
        var shock = NewtonFightArt.Quad("Shock", parent,
            NewtonFightArt.ParticleMaterial("ThunderShock", NewtonFightArt.WellHalo(), Color.white, additive: true),
            1f, flat: true).GetComponent<Renderer>();
        shock.enabled = false;
        var sparks = Burst("Sparks", parent, NewtonFightArt.ParticleMaterial("ThunderSpark", NewtonFightArt.Glow(), Color.white, additive: true),
                           count: 40, life: new Vector2(0.2f, 0.5f), speed: new Vector2(4f, 11f), size: new Vector2(0.05f, 0.14f),
                           from: Color.white, to: BoltBlue, gravity: 1.2f);

        var so = new SerializedObject(visual);
        Set(so, "strike", strike);
        Set(so, "ring", ring);
        Set(so, "marker", marker);
        Set(so, "cloud", cloud);
        SetArray(so, "cloudPuffs", puffs);
        Set(so, "cloudLight", cloudLight);
        Set(so, "bolt", bolt);
        Set(so, "boltGlow", boltGlow);
        Set(so, "branch", branch);
        SetArray(so, "chains", chains);
        Set(so, "flash", flash);
        Set(so, "shock", shock);
        Set(so, "sparks", sparks);
        SetColour(so, "boltColour", BoltBlue);
        SetColour(so, "telegraphColour", WithAlpha(StormYellow, 0.9f));
        so.ApplyModifiedPropertiesWithoutUndo();

        return Save(root, prefabPath)?.GetComponent<LightningStrike>();
    }

    // ===================================================================
    // Freeze Zone
    // ===================================================================

    /// <summary>Builds (or rebuilds) the zone prefab; returns its
    /// <see cref="FreezeZone"/>, or null if the save failed.</summary>
    public static FreezeZone BuildZone(string prefabPath)
    {
        PrepareFolders(prefabPath);
        var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
        var zone = root.AddComponent<FreezeZone>();
        var visualGo = new GameObject("Visual");
        visualGo.transform.SetParent(root.transform, false);
        var visual = visualGo.AddComponent<FreezeZoneVisual>();
        var parent = visualGo.transform;

        var disc = NewtonFightArt.Quad("FrostDisc", parent,
            NewtonFightArt.ParticleMaterial("FrostDisc", FrostDiscTexture(), Color.white, additive: false),
            2f, flat: true);
        disc.transform.localPosition = Vector3.up * 0.03f;
        var rimGlow = NewtonFightArt.Quad("RimGlow", parent,
            NewtonFightArt.ParticleMaterial("FrostRimGlow", NewtonFightArt.WellHalo(), Color.white, additive: true),
            2f, flat: true);
        rimGlow.transform.localPosition = Vector3.up * 0.04f;
        var rim = Line("TimerRim", parent, NewtonFightArt.ParticleMaterial("FrostLine", LineCore(), Color.white, additive: false),
                       0.16f, worldSpace: false, loop: false);

        var crystals = new GameObject("Crystals").transform;
        crystals.SetParent(parent, false);
        var crystalMesh = CrystalMesh();
        var iceMat = IceMaterial();
        var rng = new System.Random(11);
        for (int i = 0; i < CrystalCount; i++)
        {
            float a = (i + (float)rng.NextDouble() * 0.4f) * Mathf.PI * 2f / CrystalCount;
            Vector3 dir = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
            float height = 0.5f + (float)rng.NextDouble() * 0.55f;
            float girth = 0.24f + (float)rng.NextDouble() * 0.12f;
            var crystal = MeshPart($"Crystal{i}", crystals, crystalMesh, iceMat);
            crystal.localPosition = dir;   // unit radius: the visual places it on the real rim
            // Leaning outward, 15-35°, so the rim reads as a crown of ice.
            crystal.localRotation = Quaternion.AngleAxis(15f + (float)rng.NextDouble() * 20f, Vector3.Cross(Vector3.up, dir));
            crystal.localScale = new Vector3(girth, height, girth);
        }

        var flakeMat = NewtonFightArt.ParticleMaterial("FrostFlake", NewtonFightArt.Glow(), Color.white, additive: true);
        var snow = Looping("Snow", parent, flakeMat, rate: 40f, life: new Vector2(1.2f, 2.2f), speed: new Vector2(0.05f, 0.3f),
                           size: new Vector2(0.04f, 0.1f), from: Color.white, to: Frost, gravity: -0.04f, maxParticles: 400);
        var snowShape = snow.shape;
        snowShape.shapeType = ParticleSystemShapeType.Circle;
        snowShape.radius = 1f;
        snowShape.rotation = new Vector3(90f, 0f, 0f);
        snow.transform.localPosition = Vector3.up * 0.15f;
        var snowVel = snow.velocityOverLifetime;
        snowVel.enabled = true;
        snowVel.space = ParticleSystemSimulationSpace.World;
        snowVel.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        snowVel.y = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        snowVel.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);

        var puff = Burst("PlacePuff", parent, flakeMat, count: 60, life: new Vector2(0.35f, 0.7f), speed: new Vector2(3f, 6f),
                         size: new Vector2(0.15f, 0.4f), from: Color.white, to: Frost, gravity: 0f);
        var puffShape = puff.shape;
        puffShape.shapeType = ParticleSystemShapeType.Circle;
        puffShape.radius = 0.5f;
        puffShape.rotation = new Vector3(90f, 0f, 0f);
        puff.transform.localPosition = Vector3.up * 0.2f;

        var light = PointLight("ColdLight", parent, Frost, 7f, 1.6f);
        light.transform.localPosition = Vector3.up * 0.8f;

        var so = new SerializedObject(visual);
        Set(so, "zone", zone);
        Set(so, "frostDisc", disc.GetComponent<Renderer>());
        Set(so, "timerRim", rim);
        Set(so, "rimGlow", rimGlow.GetComponent<Renderer>());
        Set(so, "crystals", crystals);
        Set(so, "snow", snow);
        Set(so, "placePuff", puff);
        Set(so, "coldLight", light);
        so.ApplyModifiedPropertiesWithoutUndo();

        return Save(root, prefabPath)?.GetComponent<FreezeZone>();
    }

    // ===================================================================
    // Burn
    // ===================================================================

    /// <summary>Builds (or rebuilds) the burn prefab; returns its
    /// <see cref="BurnStatus"/>, or null if the save failed.</summary>
    public static BurnStatus BuildBurn(string prefabPath)
    {
        PrepareFolders(prefabPath);
        var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
        var burn = root.AddComponent<BurnStatus>();
        var visualGo = new GameObject("Visual");
        visualGo.transform.SetParent(root.transform, false);
        var visual = visualGo.AddComponent<BurnVisual>();
        var parent = visualGo.transform;

        // Flames blend normally (additive fire vanishes on a white floor); the
        // embers and the light stay additive for the glow.
        var flameMat = NewtonFightArt.ParticleMaterial("BurnFlame", NewtonFightArt.Glow(), Color.white, additive: false);
        var emberMat = NewtonFightArt.ParticleMaterial("BurnEmber", NewtonFightArt.Glow(), Color.white, additive: true);
        var flames = Looping("Flames", parent, flameMat, rate: 40f, life: new Vector2(0.35f, 0.7f), speed: new Vector2(0.8f, 2f),
                             size: new Vector2(0.35f, 0.75f), from: FlameCore, to: Ash, gravity: -0.25f, maxParticles: 300);
        var flameShape = flames.shape;
        // A narrow upward cone over the body's footprint: flames rise, they do
        // not spray sideways (a Circle emitter throws particles out radially).
        flameShape.shapeType = ParticleSystemShapeType.Cone;
        flameShape.angle = 12f;
        flameShape.radius = 0.4f;
        flameShape.radiusThickness = 0f;   // from the rim: on the body's skin, not inside it
        flameShape.rotation = new Vector3(-90f, 0f, 0f);
        var flameColour = flames.colorOverLifetime;
        var grad = new Gradient();
        grad.SetKeys(new[]
                     {
                         new GradientColorKey(FlameCore, 0f), new GradientColorKey(FlameOrange, 0.3f),
                         new GradientColorKey(FlameRed, 0.65f), new GradientColorKey(Ash, 1f),
                     },
                     new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        flameColour.color = new ParticleSystem.MinMaxGradient(grad);
        var flameSize = flames.sizeOverLifetime;
        flameSize.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.25f));

        var embers = Looping("Embers", parent, emberMat, rate: 14f, life: new Vector2(0.6f, 1.2f), speed: new Vector2(1f, 2.5f),
                             size: new Vector2(0.03f, 0.07f), from: FlameCore, to: FlameRed, gravity: -0.4f, maxParticles: 60);
        var emberShape = embers.shape;
        emberShape.shapeType = ParticleSystemShapeType.Cone;
        emberShape.angle = 20f;
        emberShape.radius = 0.4f;
        emberShape.radiusThickness = 0f;
        emberShape.rotation = new Vector3(-90f, 0f, 0f);

        var glow = PointLight("Glow", parent, FlameOrange, 3f, 1.8f);
        glow.transform.localPosition = Vector3.up * 0.6f;

        var so = new SerializedObject(visual);
        Set(so, "burn", burn);
        Set(so, "flames", flames);
        Set(so, "embers", embers);
        Set(so, "glow", glow);
        so.ApplyModifiedPropertiesWithoutUndo();

        return Save(root, prefabPath)?.GetComponent<BurnStatus>();
    }

    // ===================================================================
    // Heal channel (scene)
    // ===================================================================

    /// <summary>
    /// Destroys and rebuilds the "HealChannelVfx" child of <paramref name="player"/>
    /// and wires it to <paramref name="caster"/>. Returns the visual. Marks
    /// nothing dirty; the caller owns the scene.
    /// </summary>
    public static HealChannelVisual BuildHealVfx(Transform player, SpellCaster caster)
    {
        if (player == null) return null;
        PrepareFolders(null);
        DestroyChildren(player, HealVfxName);

        var go = new GameObject(HealVfxName);
        go.transform.SetParent(player, false);
        var visual = go.AddComponent<HealChannelVisual>();
        var parent = go.transform;

        // Ring, track, rune and burst ring blend normally so they read on any
        // floor; the glow, light and motes are additive.
        var lineMat = NewtonFightArt.ParticleMaterial("HealLine", LineCore(), Color.white, additive: false);
        var track = Line("Track", parent, lineMat, 0.07f, worldSpace: false, loop: true);
        var progress = Line("Progress", parent, lineMat, 0.15f, worldSpace: false, loop: false);
        // The halo over the head: the part the over-the-shoulder aim camera,
        // which Heal is cast from, keeps in frame (QA F-3).
        var haloTrack = Line("HaloTrack", parent, lineMat, 0.05f, worldSpace: false, loop: true);
        var haloRing = Line("HaloProgress", parent, lineMat, 0.1f, worldSpace: false, loop: false);
        var rune = NewtonFightArt.Quad("Rune", parent,
            NewtonFightArt.ParticleMaterial("HealRune", HealRuneTexture(), Color.white, additive: false), 1.9f, flat: true);
        rune.transform.localPosition = Vector3.up * 0.05f;
        var glow = NewtonFightArt.Quad("Glow", parent,
            NewtonFightArt.ParticleMaterial("HealGlow", NewtonFightArt.Glow(), Color.white, additive: true), 2.6f, flat: false);
        glow.transform.localPosition = Vector3.up * 1f;
        var light = PointLight("Light", parent, HealGreen, 4f, 0f);
        light.transform.localPosition = Vector3.up * 1.1f;

        var moteMat = NewtonFightArt.ParticleMaterial("HealMote", NewtonFightArt.Glow(), Color.white, additive: true);
        var motes = Looping("Motes", parent, moteMat, rate: 20f, life: new Vector2(0.7f, 1.3f), speed: new Vector2(0.6f, 1.5f),
                            size: new Vector2(0.05f, 0.13f), from: HealGold, to: HealGreen, gravity: -0.2f, maxParticles: 160);
        var moteShape = motes.shape;
        moteShape.shapeType = ParticleSystemShapeType.Cone;
        moteShape.angle = 4f;
        moteShape.radius = 0.75f;
        moteShape.rotation = new Vector3(-90f, 0f, 0f);
        var moteMain = motes.main;
        moteMain.playOnAwake = false;

        var burst = Burst("Burst", parent, moteMat, count: 70, life: new Vector2(0.5f, 1f), speed: new Vector2(2f, 5f),
                          size: new Vector2(0.08f, 0.2f), from: Color.white, to: HealGreen, gravity: -0.3f);
        var burstShape = burst.shape;
        burstShape.shapeType = ParticleSystemShapeType.Cone;
        burstShape.angle = 35f;
        burstShape.radius = 0.6f;
        burstShape.rotation = new Vector3(-90f, 0f, 0f);
        burst.transform.localPosition = Vector3.up * 0.2f;

        var burstRing = NewtonFightArt.Quad("BurstRing", parent,
            NewtonFightArt.ParticleMaterial("HealBurstRing", NewtonFightArt.WellHalo(), Color.white, additive: false), 1f, flat: true)
            .GetComponent<Renderer>();
        burstRing.transform.localPosition = Vector3.up * 0.06f;
        burstRing.enabled = false;

        var so = new SerializedObject(visual);
        Set(so, "caster", caster);
        Set(so, "progressRing", progress);
        Set(so, "trackRing", track);
        Set(so, "haloRing", haloRing);
        Set(so, "haloTrack", haloTrack);
        Set(so, "rune", rune.GetComponent<Renderer>());
        Set(so, "glow", glow.GetComponent<Renderer>());
        Set(so, "glowLight", light);
        Set(so, "motes", motes);
        Set(so, "burst", burst);
        Set(so, "burstRing", burstRing);
        so.ApplyModifiedPropertiesWithoutUndo();
        return visual;
    }

    // ===================================================================
    // Status receiver (scene, per target)
    // ===================================================================

    // ------------------------------------------------------- Tripo models

    /// <summary>Where the imported Tripo spell prefabs live.</summary>
    public const string TripoPrefabDir = "Assets/_Project/Prefabs/Tripo";

    /// <summary>
    /// Gives the four spells their Tripo models (Magic.md §4 convention; scales
    /// checked by rendering each at its slot in game context). Each slot is
    /// filled only while empty, and <c>modelScale</c> is written only together
    /// with the <c>modelPrefab</c> it belongs to, so a re-run changes nothing and
    /// a designer's swap or retune survives. A missing prefab is skipped and the
    /// spell keeps its procedural look. Null spells are skipped.
    /// </summary>
    public static void FillSpellModels(SpellDefinition fireball, SpellDefinition thunderbolt,
                                       SpellDefinition freezeZone, SpellDefinition heal)
    {
        FillModelIfBlank(fireball, "Fireball", null, 1.3f);
        // One modelScale drives both: a 0.8 × 7 × 0.8 m bolt and a 4 m cloud.
        FillModelIfBlank(thunderbolt, "LightningBolt", "Thundercloud", 2.5f);
        FillModelIfBlank(freezeZone, "IceCrystalCluster", null, 1f);
        FillModelIfBlank(heal, "HealLotus", null, 0.65f);
    }

    static void FillModelIfBlank(SpellDefinition spell, string model, string secondary, float scale)
    {
        if (spell == null) return;
        var so = new SerializedObject(spell);
        var modelProp = so.FindProperty("modelPrefab");
        if (modelProp.objectReferenceValue == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{TripoPrefabDir}/{model}.prefab");
            if (prefab != null)
            {
                modelProp.objectReferenceValue = prefab;
                so.FindProperty("modelScale").floatValue = scale;
            }
        }
        var secondaryProp = so.FindProperty("secondaryModelPrefab");
        if (secondary != null && secondaryProp.objectReferenceValue == null)
            secondaryProp.objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>($"{TripoPrefabDir}/{secondary}.prefab");
        if (so.ApplyModifiedPropertiesWithoutUndo()) EditorUtility.SetDirty(spell);
    }

    /// <summary>
    /// Opts <paramref name="target"/> in to slows and staggers: adds a
    /// <see cref="StatusReceiver"/> if it has none, and destroys and rebuilds its
    /// "MagicStatusVfx" child (frost shell, ice crust, motes, stagger stars),
    /// sized from the target's collider. Returns the receiver. Used by Setup
    /// Magic for every training dummy in the open scene, and by Build Training
    /// Stage for the dummies it creates.
    /// </summary>
    public static StatusReceiver AttachStatusReceiver(GameObject target)
    {
        if (target == null) return null;
        PrepareFolders(null);
        if (!target.TryGetComponent(out StatusReceiver receiver)) receiver = target.AddComponent<StatusReceiver>();
        DestroyChildren(target.transform, StatusVfxName);

        // Size from the collider, in the target's local space (setup time: the
        // target is upright and unscaled in every scene that uses this).
        float radius = 0.45f, height = 1.6f;
        if (target.TryGetComponent(out Collider col))
        {
            Vector3 size = col.bounds.size;
            radius = Mathf.Max(0.2f, Mathf.Max(size.x, size.z) * 0.5f);
            height = Mathf.Max(0.5f, size.y);
        }

        var go = new GameObject(StatusVfxName);
        go.transform.SetParent(target.transform, false);
        var visual = go.AddComponent<StatusReceiverVisual>();
        var parent = go.transform;

        var shell = Sphere("FrostShell", parent,
            NewtonFightArt.ParticleMaterial("StatusFrost", null, Color.white, additive: true, twoSided: false), 1f);
        shell.enabled = false;

        var crust = new GameObject("IceCrust").transform;
        crust.SetParent(parent, false);
        var crystalMesh = CrystalMesh();
        var iceMat = IceMaterial();
        for (int i = 0; i < 7; i++)
        {
            float a = i * Mathf.PI * 2f / 7f + 0.3f;
            Vector3 dir = new(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var crystal = MeshPart($"Shard{i}", crust, crystalMesh, iceMat);
            crystal.localPosition = dir * radius * 0.95f;
            crystal.localRotation = Quaternion.AngleAxis(25f + (i % 3) * 8f, Vector3.Cross(Vector3.up, dir));
            float h = height * (0.22f + (i % 3) * 0.06f);
            crystal.localScale = new Vector3(h * 0.3f, h, h * 0.3f);
        }
        crust.gameObject.SetActive(false);

        var motes = Looping("FrostMotes", parent,
            NewtonFightArt.ParticleMaterial("FrostFlake", NewtonFightArt.Glow(), Color.white, additive: true),
            rate: 0f, life: new Vector2(0.6f, 1.1f), speed: new Vector2(0.1f, 0.4f), size: new Vector2(0.04f, 0.09f),
            from: Color.white, to: Frost, gravity: -0.05f, maxParticles: 80);
        var moteShape = motes.shape;
        moteShape.shapeType = ParticleSystemShapeType.Sphere;
        moteShape.radius = radius;

        var stars = new GameObject("Stars").transform;
        stars.SetParent(parent, false);
        var starMat = NewtonFightArt.ParticleMaterial("StatusStar", StarTexture(), new Color(1f, 0.92f, 0.4f), additive: true);
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.PI * 2f / 3f;
            var star = NewtonFightArt.Quad($"Star{i}", stars, starMat, 0.28f, flat: false);
            star.transform.localPosition = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Mathf.Max(0.3f, radius * 0.8f);
        }
        stars.gameObject.SetActive(false);

        var so = new SerializedObject(visual);
        Set(so, "receiver", receiver);
        Set(so, "body", col);
        Set(so, "frostShell", shell);
        Set(so, "iceCrust", crust);
        Set(so, "frostMotes", motes);
        Set(so, "stars", stars);
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(target);
        return receiver;
    }

    // ===================================================================
    // Parts
    // ===================================================================

    static LineRenderer Line(string name, Transform parent, Material mat, float width, bool worldSpace, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = worldSpace;
        line.loop = loop;
        line.positionCount = 0;
        line.widthMultiplier = width;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 2;
        line.numCornerVertices = 1;
        line.startColor = line.endColor = Color.white;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = mat;
        return line;
    }

    static Light PointLight(string name, Transform parent, Color colour, float range, float intensity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = colour;
        light.range = range;
        light.intensity = intensity;
        light.shadows = LightShadows.None;
        return light;
    }

    static Renderer Sphere(string name, Transform parent, Material mat, float diameter)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localScale = Vector3.one * diameter;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    static Transform MeshPart(string name, Transform parent, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        return go.transform;
    }

    /// <summary>A looping world-space particle system (plays on awake).</summary>
    static ParticleSystem Looping(string name, Transform parent, Material mat, float rate, Vector2 life, Vector2 speed,
                                  Vector2 size, Color from, Color to, float gravity, int maxParticles)
    {
        var ps = NewParticles(name, parent, mat, loop: true, life, speed, size, maxParticles, gravity);
        var emission = ps.emission;
        emission.rateOverTime = rate;
        Fade(ps, from, to);
        return ps;
    }

    /// <summary>A one-shot world-space burst of <paramref name="count"/> particles.</summary>
    static ParticleSystem Burst(string name, Transform parent, Material mat, int count, Vector2 life, Vector2 speed,
                                Vector2 size, Color from, Color to, float gravity)
    {
        var ps = NewParticles(name, parent, mat, loop: false, life, speed, size, Mathf.Max(count * 2, 16), gravity);
        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.25f;
        limit.limit = new ParticleSystem.MinMaxCurve(1.5f);
        Fade(ps, from, to);
        return ps;
    }

    static ParticleSystem NewParticles(string name, Transform parent, Material mat, bool loop, Vector2 life,
                                       Vector2 speed, Vector2 size, int maxParticles, float gravity)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 1f;
        main.loop = loop;
        main.playOnAwake = loop;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = Color.white;
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = maxParticles;

        var emission = ps.emission;
        emission.rateOverTime = 0f;

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    static void Fade(ParticleSystem ps, Color from, Color to)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);
    }

    // ===================================================================
    // Generated textures and mesh
    // ===================================================================

    /// <summary>A line's cross-section: bright core, soft glow, clear edges.
    /// U runs along the line (constant), V across it.</summary>
    static Texture2D LineCore() => WriteTexture($"{ArtDir}/SpellLineCore.png", 8, 64, (x, y) =>
    {
        float v = y / 32f - 1f;
        float core = Mathf.Exp(-Mathf.Pow(v / 0.18f, 2f));
        float glow = Mathf.Exp(-Mathf.Pow(v / 0.55f, 2f));
        return White(Mathf.Clamp01(core + glow * 0.45f) * Mathf.Clamp01((1f - Mathf.Abs(v)) / 0.1f));
    });

    /// <summary>A lumpy soft cloud blob for the thundercloud puffs.</summary>
    static Texture2D CloudPuff() => WriteTexture($"{ArtDir}/CloudPuff.png", 64, 64, (x, y) =>
    {
        float u = x / 32f - 1f, v = y / 32f - 1f;
        float r = Mathf.Sqrt(u * u + v * v);
        float lumps = Mathf.PerlinNoise(x * 0.11f + 3f, y * 0.11f + 7f) * 0.45f;
        float a = Mathf.Clamp01((1f - r - lumps * 0.6f) / 0.35f);
        return White(a * a);
    });

    /// <summary>
    /// The frost disc: a faint fill thickening toward a bright rim, crossed by
    /// twelve feathery crystal spokes and speckled with rime. White, tinted by
    /// the material.
    /// </summary>
    static Texture2D FrostDiscTexture() => WriteTexture($"{ArtDir}/FrostDisc.png", 256, 256, (x, y) =>
    {
        float u = x / 128f - 1f, v = y / 128f - 1f;
        float r = Mathf.Sqrt(u * u + v * v);
        if (r > 1f) return White(0f);
        float angle = Mathf.Atan2(v, u);
        float fill = Mathf.Lerp(0.18f, 0.55f, r * r);
        float rim = Mathf.Exp(-Mathf.Pow((r - 0.95f) / 0.035f, 2f));
        float spoke = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 6f)), 40f) * Mathf.Clamp01(r / 0.2f) * (1f - r) * 1.6f;
        float feather = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 24f + r * 9f)), 30f) * Mathf.Clamp01((r - 0.3f) / 0.4f) * 0.5f;
        float speck = Mathf.PerlinNoise(x * 0.3f, y * 0.3f) > 0.72f ? 0.35f : 0f;
        float edge = Mathf.Clamp01((1f - r) / 0.03f);
        return White(Mathf.Clamp01(fill + rim + spoke + feather + speck) * edge);
    });

    /// <summary>
    /// The heal rune: two rings, a hexagram between them, six dots on the outer
    /// ring and a plus at the centre. White lines on clear.
    /// </summary>
    static Texture2D HealRuneTexture() => WriteTexture($"{ArtDir}/HealRune.png", 256, 256, (x, y) =>
    {
        float u = x / 128f - 1f, v = y / 128f - 1f;
        float r = Mathf.Sqrt(u * u + v * v);
        const float w = 0.018f;
        float ink = 0f;
        ink = Mathf.Max(ink, Line01(Mathf.Abs(r - 0.92f), w));
        ink = Mathf.Max(ink, Line01(Mathf.Abs(r - 0.74f), w * 0.8f));
        // Hexagram: two triangles inscribed in r = 0.72.
        for (int t = 0; t < 2; t++)
        for (int k = 0; k < 3; k++)
        {
            float a0 = Mathf.PI * 0.5f + t * Mathf.PI + k * Mathf.PI * 2f / 3f;
            float a1 = a0 + Mathf.PI * 2f / 3f;
            var p0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * 0.72f;
            var p1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * 0.72f;
            ink = Mathf.Max(ink, Line01(SegmentDistance(new Vector2(u, v), p0, p1), w * 0.8f));
        }
        for (int k = 0; k < 6; k++)
        {
            float a = k * Mathf.PI / 3f;
            var d = new Vector2(u - Mathf.Cos(a) * 0.92f, v - Mathf.Sin(a) * 0.92f).magnitude;
            ink = Mathf.Max(ink, Line01(Mathf.Max(0f, d - 0.035f), w));
        }
        bool plus = (Mathf.Abs(u) < 0.06f && Mathf.Abs(v) < 0.22f) || (Mathf.Abs(v) < 0.06f && Mathf.Abs(u) < 0.22f);
        if (plus) ink = 1f;
        float glow = Mathf.Exp(-Mathf.Pow((r - 0.83f) / 0.12f, 2f)) * 0.15f;
        return White(Mathf.Clamp01(ink + glow) * Mathf.Clamp01((1f - r) / 0.02f));
    });

    /// <summary>A five-point star for the stagger halo.</summary>
    static Texture2D StarTexture()
    {
        var star = new Vector2[10];
        for (int i = 0; i < star.Length; i++)
        {
            float a = Mathf.PI * 0.5f + i * Mathf.PI / 5f;
            float r = i % 2 == 0 ? 0.92f : 0.4f;
            star[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }
        return WriteTexture($"{ArtDir}/StaggerStar.png", 64, 64, (x, y) =>
        {
            // 4x4 supersampled point-in-polygon: the concave points alias otherwise.
            float covered = 0f;
            for (int j = 0; j < 4; j++)
            for (int i = 0; i < 4; i++)
            {
                var p = new Vector2((x - 0.5f + (i + 0.5f) / 4f) / 32f - 1f, (y - 0.5f + (j + 0.5f) / 4f) / 32f - 1f);
                if (InPolygon(p, star)) covered += 1f / 16f;
            }
            return White(covered);
        });
    }

    static bool InPolygon(Vector2 p, Vector2[] polygon)
    {
        bool inside = false;
        for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
        {
            Vector2 a = polygon[i], b = polygon[j];
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
    }

    /// <summary>
    /// A faceted hexagonal ice crystal, 1 m tall with its base at the origin:
    /// a short prism capped by a long point, flat-shaded (every face has its
    /// own vertices) so it catches the light in facets. Saved as an asset and
    /// rewritten in place, so prefabs that use it keep their reference.
    /// </summary>
    static Mesh CrystalMesh()
    {
        string path = $"{ArtDir}/IceCrystal.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool created = mesh == null;
        if (created) mesh = new Mesh { name = "IceCrystal" };
        mesh.Clear();

        const int sides = 6;
        const float radius = 0.5f, shoulder = 0.62f;
        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        Vector3 Ring(int i, float y) => new(Mathf.Cos(i * Mathf.PI * 2f / sides) * radius, y,
                                            Mathf.Sin(i * Mathf.PI * 2f / sides) * radius);
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            int n = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            tris.Add(n); tris.Add(n + 1); tris.Add(n + 2);
        }
        Vector3 tip = new(0f, 1f, 0f), bottom = new(0f, -0.08f, 0f);
        for (int i = 0; i < sides; i++)
        {
            int j = (i + 1) % sides;
            Vector3 lo0 = Ring(i, 0f), lo1 = Ring(j, 0f), hi0 = Ring(i, shoulder), hi1 = Ring(j, shoulder);
            Tri(lo0, hi0, hi1);
            Tri(lo0, hi1, lo1);
            Tri(hi0, tip, hi1);
            Tri(lo0, lo1, bottom);
        }
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (created) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>Saturated, glossy, faintly self-lit ice: reads on white and on dark.</summary>
    static Material IceMaterial() => NewtonFightArt.LitMaterial(NewtonFightArt.VfxDir, "FrostCrystal",
                                                                new Color(0.42f, 0.78f, 1f), 0.92f,
                                                                new Color(0.08f, 0.3f, 0.55f));

    static float Line01(float distance, float width) => Mathf.Clamp01(1f - (distance - width) / 0.012f);

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return (p - (a + ab * t)).magnitude;
    }

    static Color White(float alpha) => new(1f, 1f, 1f, Mathf.Clamp01(alpha));

    /// <summary>Renders a pixel function to a PNG and imports it like
    /// NewtonFightArt's textures (clamped, mipmapped, alpha is transparency).</summary>
    static Texture2D WriteTexture(string path, int w, int h, Func<float, float, Color> pixel)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            pixels[y * w + x] = pixel(x + 0.5f, y + 0.5f);
        tex.SetPixels(pixels);
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);

        string full = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, png);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Default;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ===================================================================
    // Plumbing
    // ===================================================================

    static void PrepareFolders(string prefabPath)
    {
        if (!string.IsNullOrEmpty(prefabPath))
            ChibiSandboxSetup.EnsureFolderPublic(Path.GetDirectoryName(prefabPath)!.Replace('\\', '/'));
        ChibiSandboxSetup.EnsureFolderPublic(NewtonFightArt.VfxDir);
        ChibiSandboxSetup.EnsureFolderPublic(ArtDir);
    }

    static GameObject Save(GameObject root, string prefabPath)
    {
        var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        if (saved == null) Debug.LogError($"[MagicSpellArtBuilder] Could not save {prefabPath}.");
        else Debug.Log($"  spell prefab → {prefabPath}");
        return saved;
    }

    /// <summary>Every child with the name: a hand-duplicated copy would otherwise
    /// survive the rebuild and draw twice.</summary>
    static void DestroyChildren(Transform parent, string name)
    {
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (child.name == name) Object.DestroyImmediate(child.gameObject);
        }
    }

    static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

    static void Set(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p == null) Debug.LogError($"[MagicSpellArtBuilder] {so.targetObject.GetType().Name} has no field '{field}'.");
        else p.objectReferenceValue = value;
    }

    static void SetColour(SerializedObject so, string field, Color value)
    {
        var p = so.FindProperty(field);
        if (p == null) Debug.LogError($"[MagicSpellArtBuilder] {so.targetObject.GetType().Name} has no field '{field}'.");
        else p.colorValue = value;
    }

    static void SetArray<T>(SerializedObject so, string field, T[] values) where T : Object
    {
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"[MagicSpellArtBuilder] {so.targetObject.GetType().Name} has no field '{field}'.");
            return;
        }
        p.arraySize = values.Length;
        for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
    }
}
