using System.IO;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Boss.Hypatia.Burning;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using Pickleball.Sandbox.Boss.Hypatia.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Every material, generated mesh and projectile prefab the Hypatia fight is
/// drawn with, made from code. Called by <see cref="HypatiaFightSetup"/>; not a
/// menu itself. Placeholder art on purpose: primitives and procedural rings in
/// an indigo, silver and brass palette, readable from the chase camera.
///
/// Everything lives under paths the fight owns (<see cref="GenDir"/>,
/// <see cref="PrefabDir"/>). The only shared assets touched are read, never
/// written: the Disc, TargetRing and Glow textures NewtonFightArt generates,
/// and the TMP example fonts.
///
/// Assets are load-or-create and rewritten in place, so their GUIDs (and every
/// reference to them) survive a rebuild. Setup only rebuilds when an input
/// changed (see HypatiaFightSetup's fingerprint), which is what makes a second
/// run of the menu a no-op.
/// </summary>
public static class HypatiaFightArt
{
    public const string ArtDir = "Assets/_Project/Art/Characters/Hypatia";
    public const string GenDir = ArtDir + "/Generated";
    public const string MeshDir = GenDir + "/Meshes";
    public const string MatDir = GenDir + "/Materials";
    public const string PrefabDir = "Assets/_Project/Prefabs/Hypatia";

    public const string LobPrefabPath = PrefabDir + "/HypatiaLob.prefab";
    public const string OrbiterPrefabPath = PrefabDir + "/HypatiaOrbiter.prefab";
    public const string ShotPrefabPath = PrefabDir + "/HypatiaShot.prefab";
    public const string ScrollPrefabPath = PrefabDir + "/HypatiaScroll.prefab";
    public const string DashTexturePath = GenDir + "/Dash.png";

    /// <summary>Unity's built-in "Ignore Raycast" layer. Projectile triggers sit
    /// here: every spell's HitMask includes it (all assets use ~0), so the
    /// Freeze Zone still finds them, while the orbit camera and lock-on line of
    /// sight raycasts (Default layers) do not.</summary>
    public const int ProjectileLayer = 2;

    // Palette.
    public static readonly Color Indigo = new(0.16f, 0.12f, 0.45f);
    public static readonly Color DeepIndigo = new(0.05f, 0.05f, 0.14f);
    public static readonly Color Silver = new(0.82f, 0.85f, 0.92f);
    public static readonly Color Brass = new(0.86f, 0.64f, 0.3f);
    public static readonly Color Starlight = new(0.62f, 0.75f, 1f);
    public static readonly Color Hyperbolic = new(1f, 0.35f, 0.55f);
    public static readonly Color Ember = new(1f, 0.45f, 0.12f);

    // ── Materials ───────────────────────────────────────────────────────────

    public static Material BrassMat() => Lit("Brass", Brass, 0.85f, 0.55f, Brass * 0.12f);
    public static Material AstrolabeBrassMat() => Lit("AstrolabeBrass", Brass, 0.85f, 0.6f, Brass * 0.9f);
    public static Material SilverMat() => Lit("Silver", Silver, 0.9f, 0.7f, null);
    public static Material PlateMat() => Lit("ReteFloor", new Color(0.08f, 0.08f, 0.2f), 0.2f, 0.45f, null);
    public static Material GemMat() => Lit("StarGem", Starlight, 0.1f, 0.9f, Starlight * 1.6f);
    public static Material NeedleMat() => Lit("Needle", Brass, 0.8f, 0.6f, Brass);

    /// <summary>Additive, untextured, vertex-coloured: ring walls, wedges, lines.</summary>
    public static Material AdditiveMat() => Particle("HypatiaAdditive", null, Color.white, additive: true);

    /// <summary>Alpha-blended, untextured: the band overlays (ash must darken, not glow).</summary>
    public static Material AlphaMat() => Particle("HypatiaAlpha", null, Color.white, additive: false);

    /// <summary>Soft additive glow (NewtonFightArt's Glow texture, read only).</summary>
    public static Material GlowMat() => Particle("HypatiaGlow", SharedTexture("Glow.png"), Color.white, additive: true);

    public static Material MarkerFillMat() =>
        Particle("HypatiaMarkerFill", SharedTexture("Disc.png"), new Color(0.35f, 0.25f, 1f, 0.45f), additive: false);

    /// <summary>The knockback pulse's shockwave ring (its own asset, so tuning its
    /// tint never touches the lob markers or the window ring).</summary>
    public static Material PulseRingMat() =>
        Particle("HypatiaPulseRing", SharedTexture("TargetRing.png"), new Color(0.8f, 0.9f, 1f, 1f), additive: true);

    public static Material MarkerRingMat() =>
        Particle("HypatiaMarkerRing", SharedTexture("TargetRing.png"), new Color(0.8f, 0.85f, 1f, 1f), additive: true);

    public static Material DashMat() => Particle("HypatiaDash", DashTexture(), Color.white, additive: true);

    static Material Lit(string name, Color colour, float metallic, float smoothness, Color? emission)
    {
        var mat = LoadOrCreate($"{MatDir}/{name}.mat", Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", colour);
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Smoothness", smoothness);
        if (emission is Color glow)
        {
            mat.SetColor("_EmissionColor", glow);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>URP Particles/Unlit, transparent, two-sided. Blend state written
    /// by hand: a material made from script never passes through the URP
    /// inspector that would normally derive it (same recipe as NewtonFightArt).</summary>
    static Material Particle(string name, Texture texture, Color tint, bool additive)
    {
        var mat = LoadOrCreate($"{MatDir}/{name}.mat", Shader.Find("Universal Render Pipeline/Particles/Unlit"));
        mat.SetTexture("_BaseMap", texture);
        mat.SetColor("_BaseColor", tint);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", additive ? 2f : 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material LoadOrCreate(string path, Shader shader)
    {
        ChibiSandboxSetup.EnsureFolderPublic(Path.GetDirectoryName(path)!.Replace('\\', '/'));
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }
        return mat;
    }

    /// <summary>A texture NewtonFightArt generated, loaded read-only; null (plain
    /// white) when Setup Newton Fight has never run.</summary>
    static Texture2D SharedTexture(string file) =>
        AssetDatabase.LoadAssetAtPath<Texture2D>($"{NewtonFightArt.VfxDir}/{file}");

    /// <summary>One dash and one gap, tiled along the hyperbola's path. Written
    /// once; deterministic.</summary>
    static Texture2D DashTexture()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(DashTexturePath);
        if (existing != null) return existing;
        ChibiSandboxSetup.EnsureFolderPublic(GenDir);
        const int w = 64, h = 8;
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float edge = Mathf.Clamp01(Mathf.Min(x, 36 - x) / 3f);
            float a = x < 36 ? edge : 0f;
            float v = 1f - Mathf.Abs((y + 0.5f) / h * 2f - 1f);
            pixels[y * w + x] = new Color(1f, 1f, 1f, a * Mathf.Clamp01(v * 2f));
        }
        tex.SetPixels(pixels);
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);
        File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, DashTexturePath), png);
        AssetDatabase.ImportAsset(DashTexturePath, ImportAssetOptions.ForceUpdate);
        var ti = (TextureImporter)AssetImporter.GetAtPath(DashTexturePath);
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = true;
        ti.wrapModeU = TextureWrapMode.Repeat;
        ti.wrapModeV = TextureWrapMode.Clamp;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(DashTexturePath);
    }

    // ── Meshes ──────────────────────────────────────────────────────────────

    /// <summary>A generated mesh asset, rewritten in place (GUID kept).</summary>
    public static Mesh MeshAsset(string name, System.Action<Mesh> fill)
    {
        ChibiSandboxSetup.EnsureFolderPublic(MeshDir);
        string path = $"{MeshDir}/{name}.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        bool created = mesh == null;
        if (created) mesh = new Mesh { name = name };
        fill(mesh);
        mesh.name = name;
        if (created) AssetDatabase.CreateAsset(mesh, path);
        else EditorUtility.SetDirty(mesh);
        return mesh;
    }

    /// <summary>A flat kite, point toward +Z: a rete star pointer.</summary>
    public static Mesh PointerMesh() => MeshAsset("RetePointer", m =>
    {
        m.Clear();
        m.SetVertices(new[]
        {
            new Vector3(0f, 0f, 1f), new Vector3(0.16f, 0f, 0.15f), new Vector3(0f, 0f, -0.25f),
            new Vector3(-0.16f, 0f, 0.15f),
        });
        m.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
    });

    // ── Scene parts ─────────────────────────────────────────────────────────

    /// <summary>A child with a mesh and material and no collider.</summary>
    public static GameObject Part(string name, Transform parent, Mesh mesh, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var r = go.AddComponent<MeshRenderer>();
        r.sharedMaterial = mat;
        return go;
    }

    /// <summary>A collider-free primitive child.</summary>
    public static GameObject Primitive(PrimitiveType type, string name, Transform parent, Vector3 localPosition,
                                       Quaternion localRotation, Vector3 localScale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.SetLocalPositionAndRotation(localPosition, localRotation);
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    /// <summary>A camera-facing additive glow quad, no collider, no shadows.</summary>
    public static GameObject Halo(string name, Transform parent, float size, Color tint, Vector3 localPosition)
    {
        var go = Primitive(PrimitiveType.Quad, name, parent, localPosition, Quaternion.identity,
                           new Vector3(size, size, 1f), GlowMat());
        go.AddComponent<FaceCamera>();
        var r = go.GetComponent<MeshRenderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", tint);
        r.SetPropertyBlock(block);
        return go;
    }

    // ── Prefabs ─────────────────────────────────────────────────────────────

    /// <summary>The parabola lob: an indigo orb with a silver band and a short trail.</summary>
    public static ConicLob BuildLobPrefab()
    {
        var root = ProjectileRoot("HypatiaLob");
        root.AddComponent<ConicLob>();
        TriggerOf(root);
        var core = Primitive(PrimitiveType.Sphere, "Core", root.transform, Vector3.zero, Quaternion.identity,
                             Vector3.one * 0.7f, Lit("LobCore", Indigo, 0.2f, 0.8f, new Color(0.45f, 0.35f, 1f)));
        NoShadows(core);
        var band = Part("Band", core.transform,
                        MeshAsset("LobBand", m => ArcMesh.SolidRing(m, 0.62f, 0.72f, -0.03f, 0.03f, 48)),
                        SilverMat());
        band.transform.localRotation = Quaternion.Euler(25f, 0f, 15f);
        Halo("Halo", root.transform, 1.8f, new Color(0.5f, 0.4f, 1f, 0.8f), Vector3.zero);
        Trail(root, new Color(0.55f, 0.45f, 1f, 0.8f), 0.45f, 0.5f);
        return Save(root, LobPrefabPath).GetComponent<ConicLob>();
    }

    /// <summary>
    /// An orbiter: a silver moon with a cold halo and trail. Besides the slow
    /// trigger on the root it has a SOLID sphere on a child ("Hitbox"), because
    /// the sword's overlap ignores triggers and the orbiter must be swingable
    /// (ConicOrbiter is an IDamageable). A child, not a second collider on the
    /// root: HypatiaProjectile reads "the" SphereCollider on its root and forces
    /// it to a trigger. Same layer (Ignore Raycast), so the orbit camera and the
    /// lock-on stay blind to it; the orbiter tells the player's controller to
    /// ignore it at launch, so it never blocks or shoves them.
    /// </summary>
    public static ConicOrbiter BuildOrbiterPrefab()
    {
        var root = ProjectileRoot("HypatiaOrbiter");
        var orbiter = root.AddComponent<ConicOrbiter>();
        TriggerOf(root);
        var hitboxGo = new GameObject("Hitbox") { layer = ProjectileLayer };
        hitboxGo.transform.SetParent(root.transform, false);
        var hitbox = hitboxGo.AddComponent<SphereCollider>();
        hitbox.isTrigger = false;
        hitbox.radius = 0.45f;   // sized at launch to Orbiter Radius
        var so = new SerializedObject(orbiter);
        so.FindProperty("hitbox").objectReferenceValue = hitbox;
        so.ApplyModifiedPropertiesWithoutUndo();
        var core = Primitive(PrimitiveType.Sphere, "Core", root.transform, Vector3.zero, Quaternion.identity,
                             Vector3.one * 0.9f, Lit("OrbiterCore", Silver, 0.9f, 0.85f, Starlight * 0.6f));
        NoShadows(core);
        Halo("Halo", root.transform, 2.2f, new Color(0.6f, 0.8f, 1f, 0.7f), Vector3.zero);
        Trail(root, new Color(0.62f, 0.85f, 1f, 0.7f), 0.6f, 0.7f);
        return Save(root, OrbiterPrefabPath).GetComponent<ConicOrbiter>();
    }

    /// <summary>The hyperbolic flyby: a hot brass core in a crimson halo with a long streak.</summary>
    public static HyperbolicShot BuildShotPrefab()
    {
        var root = ProjectileRoot("HypatiaShot");
        root.AddComponent<HyperbolicShot>();
        TriggerOf(root);
        var core = Primitive(PrimitiveType.Sphere, "Core", root.transform, Vector3.zero, Quaternion.identity,
                             Vector3.one * 0.8f, Lit("ShotCore", Brass, 0.7f, 0.8f, new Color(1f, 0.55f, 0.35f)));
        NoShadows(core);
        Halo("Halo", root.transform, 2.4f, new Color(Hyperbolic.r, Hyperbolic.g, Hyperbolic.b, 0.85f), Vector3.zero);
        Trail(root, new Color(1f, 0.45f, 0.6f, 0.9f), 0.35f, 0.75f);
        return Save(root, ShotPrefabPath).GetComponent<HyperbolicShot>();
    }

    /// <summary>A burning scroll: a rolled parchment with embers rising off it.</summary>
    public static BurningScroll BuildScrollPrefab()
    {
        var root = ProjectileRoot("HypatiaScroll");
        root.AddComponent<BurningScroll>();
        TriggerOf(root);
        var parchment = Lit("Parchment", new Color(0.93f, 0.84f, 0.64f), 0f, 0.2f, new Color(0.35f, 0.12f, 0.02f));
        var roll = Primitive(PrimitiveType.Cylinder, "Roll", root.transform, Vector3.zero,
                             Quaternion.Euler(0f, 0f, 90f), new Vector3(0.22f, 0.3f, 0.22f), parchment);
        NoShadows(roll);
        var sheet = Primitive(PrimitiveType.Cube, "Sheet", root.transform, new Vector3(0f, -0.04f, -0.16f),
                              Quaternion.Euler(20f, 0f, 0f), new Vector3(0.5f, 0.015f, 0.3f), parchment);
        NoShadows(sheet);
        Halo("Glow", root.transform, 1.2f, new Color(1f, 0.5f, 0.15f, 0.7f), Vector3.zero);
        Embers(root.transform, "Embers", 18f, 0.6f);
        return Save(root, ScrollPrefabPath).GetComponent<BurningScroll>();
    }

    static GameObject ProjectileRoot(string name) => new(name) { layer = ProjectileLayer };

    /// <summary>The slow-detection trigger HypatiaProjectile requires; sized at
    /// spawn by the projectile itself.</summary>
    static void TriggerOf(GameObject root)
    {
        var sphere = root.GetComponent<SphereCollider>();
        if (sphere == null) sphere = root.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.radius = 0.4f;
    }

    static void NoShadows(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }

    static void Trail(GameObject root, Color colour, float seconds, float width)
    {
        var trail = root.AddComponent<TrailRenderer>();
        trail.time = seconds;
        trail.widthMultiplier = width;
        trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        trail.minVertexDistance = 0.08f;
        trail.startColor = colour;
        trail.endColor = new Color(colour.r, colour.g, colour.b, 0f);
        trail.sharedMaterial = AdditiveMat();
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.numCapVertices = 2;
    }

    /// <summary>Embers rising: small additive sparks in world space.</summary>
    public static ParticleSystem Embers(Transform parent, string name, float rate, float radius)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.75f, 0.3f), Ember);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = -0.05f;
        main.maxParticles = 80;
        var emission = ps.emission;
        emission.rateOverTime = rate;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = radius * 0.3f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = GlowMat();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    /// <summary>
    /// Flames for one burning band: a flat circle emitter (shape fitted to the
    /// annulus at runtime by BandFireView), particles rising and fading from
    /// yellow to red. Not playing until the band ignites.
    /// </summary>
    public static ParticleSystem BandFlames(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.35f), Ember);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 1000;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1f;
        shape.radiusThickness = 1f;
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.Local;
        // All three axes in the same curve mode (TwoConstants): mixing modes
        // makes the particle system log an error every frame.
        velocity.x = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        velocity.y = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        velocity.z = new ParticleSystem.MinMaxCurve(1.2f, 2.8f);
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(Ember, 0.45f),
                    new GradientColorKey(new Color(0.5f, 0.08f, 0.04f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0f, 1f) });
        colour.color = new ParticleSystem.MinMaxGradient(gradient);
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.2f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = GlowMat();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    /// <summary>A far shell of fixed stars: a sphere of motionless, long-lived
    /// particles, prewarmed so the sky is full from the first frame.</summary>
    public static ParticleSystem Stars(Transform parent)
    {
        var go = new GameObject("Stars");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = true;
        main.duration = 10f;
        main.startLifetime = 1000f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 1.1f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.8f, 1f), new Color(1f, 0.95f, 0.8f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 700;
        var emission = ps.emission;
        emission.rateOverTime = 70f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 160f;
        shape.radiusThickness = 0f;
        go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = GlowMat();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        return ps;
    }

    /// <summary>
    /// Save only the assets this fight owns. A blanket AssetDatabase.SaveAssets
    /// would also write whatever else the Editor holds dirty (TMP's dynamic
    /// font atlases, after the HUD text renders), which belong to nobody here.
    /// </summary>
    public static void SaveOwnedAssets(params string[] extraFolders)
    {
        var folders = new System.Collections.Generic.List<string> { GenDir, PrefabDir };
        foreach (string f in extraFolders)
            if (AssetDatabase.IsValidFolder(f)) folders.Add(f);
        foreach (string guid in AssetDatabase.FindAssets("", folders.ToArray()))
            AssetDatabase.SaveAssetIfDirty(new GUID(guid));
    }

    static GameObject Save(GameObject root, string path)
    {
        ChibiSandboxSetup.EnsureFolderPublic(PrefabDir);
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ProjectileLayer;
        var saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        if (saved == null) Debug.LogError($"[HypatiaFightArt] Could not save {path}.");
        return saved;
    }
}
