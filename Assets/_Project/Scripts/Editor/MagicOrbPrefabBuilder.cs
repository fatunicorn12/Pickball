using System.IO;
using Pickleball.Sandbox.Magic;
using Pickleball.Sandbox.Magic.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the arcane orb prefab the aimed spell fires: a <see cref="MagicOrb"/>
/// root with no collider, and a "Visual" child carrying
/// <see cref="MagicOrbVisual"/> and every part it animates — core sphere, glow
/// halo, circling motes, trail, point light, sparkle dust, burst sparks, burst
/// shell and shock ring. Called by MagicSetup (Tools ▸ Pickleball ▸ Setup
/// Magic); not a menu item itself.
///
/// The root stays collider-free on purpose: the orb finds what it hits with
/// sphere casts, so it can never block the player, be stood on, or catch its
/// own sweep. The visual is a child, never on the root, because it detaches
/// at the burst to finish its flash after the orb has destroyed itself (see
/// MagicOrb's cleanup notes).
///
/// Everything is regenerated on every run, like NewtonFightArt: the prefab is
/// rebuilt from scratch and saved over the same path (SaveAsPrefabAsset keeps
/// the asset's GUID, so the SpellDefinition's reference survives), and the
/// materials are re-configured in place. Materials and textures go through
/// NewtonFightArt's helpers, which keep them in its VFX folder
/// (<see cref="NewtonFightArt.VfxDir"/>), named MagicOrb* so they cannot
/// collide with the Newton fight's. All of them are URP Particles/Unlit
/// (additive, vertex-coloured) except the core, which is opaque URP Unlit.
/// </summary>
public static class MagicOrbPrefabBuilder
{
    static readonly Color Cyan = new(0.3f, 0.88f, 1f);
    static readonly Color Violet = new(0.66f, 0.42f, 1f);
    static readonly Color CoreWhite = new(0.78f, 1f, 1f);

    // Sizes in metres. The core sits a little inside the spell's default
    // 0.28 m hit radius, so what you see is never bigger than what hits.
    const float CoreDiameter = 0.4f;
    const float HaloDiameter = 1.5f;
    const float MoteOrbit = 0.34f;
    const float MoteSize = 0.2f;
    const int MoteCount = 3;

    /// <summary>
    /// Builds (or rebuilds in place) the orb prefab at <paramref name="prefabPath"/>
    /// and returns the <see cref="MagicOrb"/> on the saved asset, ready to drop
    /// into a SpellDefinition. Null if the path is empty or the save failed.
    /// </summary>
    public static MagicOrb Build(string prefabPath)
    {
        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogError("[MagicOrbPrefabBuilder] No prefab path given.");
            return null;
        }

        ChibiSandboxSetup.EnsureFolderPublic(Path.GetDirectoryName(prefabPath)!.Replace('\\', '/'));
        ChibiSandboxSetup.EnsureFolderPublic(NewtonFightArt.VfxDir);

        var root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
        var orb = root.AddComponent<MagicOrb>();

        var visualGo = new GameObject("Visual");
        visualGo.transform.SetParent(root.transform, false);
        var visual = visualGo.AddComponent<MagicOrbVisual>();
        var parent = visualGo.transform;

        var glow = NewtonFightArt.Glow();
        var sparkleMat = NewtonFightArt.ParticleMaterial("MagicOrbSparkle", glow, Color.white, additive: true);

        var core = Sphere("Core", parent, NewtonFightArt.UnlitMaterial("MagicOrbCore", CoreWhite), CoreDiameter);

        var halo = NewtonFightArt.Quad("Halo", parent,
            NewtonFightArt.ParticleMaterial("MagicOrbHalo", glow, WithAlpha(Cyan, 0.85f), additive: true),
            HaloDiameter, flat: false);

        var motes = BuildMotes(parent, glow);
        var trail = BuildTrail(parent);
        var light = BuildLight(parent);
        var sparkles = BuildSparkles(parent, sparkleMat);
        var burstSparks = BuildBurstSparks(parent, sparkleMat);

        // Burst parts start hidden; the visual shows them at the burst.
        // Single-sided shell: from inside the blast you see nothing rather
        // than a full-screen wash.
        var shell = Sphere("BurstShell", parent,
            NewtonFightArt.ParticleMaterial("MagicOrbShell", null, WithAlpha(Violet, 0.7f),
                                            additive: true, twoSided: false), 1f);
        shell.enabled = false;
        var ring = NewtonFightArt.Quad("BurstRing", parent,
            NewtonFightArt.ParticleMaterial("MagicOrbRing", NewtonFightArt.WellHalo(), Cyan, additive: true),
            1f, flat: false).GetComponent<Renderer>();
        ring.enabled = false;

        var so = new SerializedObject(visual);
        SetObject(so, "orb", orb);
        SetObject(so, "core", core);
        SetObject(so, "halo", halo.GetComponent<Renderer>());
        SetObject(so, "motes", motes);
        SetObject(so, "trail", trail);
        SetObject(so, "glowLight", light);
        SetObject(so, "sparkles", sparkles);
        SetObject(so, "burstSparks", burstSparks);
        SetObject(so, "burstShell", shell);
        SetObject(so, "burstRing", ring);
        SetFloat(so, "coreDiameter", CoreDiameter);
        SetFloat(so, "haloDiameter", HaloDiameter);
        SetColour(so, "coreColour", CoreWhite);
        SetColour(so, "glowColour", WithAlpha(Cyan, 0.85f));
        SetColour(so, "accentColour", WithAlpha(Violet, 0.7f));
        so.ApplyModifiedPropertiesWithoutUndo();

        var saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        if (saved == null)
        {
            Debug.LogError($"[MagicOrbPrefabBuilder] Could not save the orb prefab to {prefabPath}.");
            return null;
        }
        Debug.Log($"  magic orb prefab → {prefabPath}");
        return saved.GetComponent<MagicOrb>();
    }

    // ---- parts -------------------------------------------------------------

    /// <summary>Collider-free sphere of <paramref name="diameter"/> metres.</summary>
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

    /// <summary>Small violet glows evenly spaced on a ring round the core. The
    /// visual spins their pivot on a tilted axis, so they read as orbiting
    /// sparks rather than a flat wheel.</summary>
    static Transform BuildMotes(Transform parent, Texture glow)
    {
        var pivot = new GameObject("Motes").transform;
        pivot.SetParent(parent, false);
        var mat = NewtonFightArt.ParticleMaterial("MagicOrbMote", glow, WithAlpha(Violet, 0.95f), additive: true);
        for (int i = 0; i < MoteCount; i++)
        {
            float angle = i * Mathf.PI * 2f / MoteCount;
            var mote = NewtonFightArt.Quad($"Mote{i}", pivot, mat, MoteSize, flat: false);
            mote.transform.localPosition = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * MoteOrbit;
        }
        return pivot;
    }

    /// <summary>A short tapering streak, white-hot at the orb fading through
    /// cyan to violet. The beam's streak texture gives it moving filaments;
    /// the colour rides in the vertex colour, which Particles/Unlit honours.</summary>
    static TrailRenderer BuildTrail(Transform parent)
    {
        var go = new GameObject("Trail");
        go.transform.SetParent(parent, false);
        var trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.35f;
        trail.minVertexDistance = 0.05f;
        trail.widthMultiplier = CoreDiameter * 1.1f;
        trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(CoreWhite, 0f),
                new GradientColorKey(Cyan, 0.25f),
                new GradientColorKey(Violet, 1f),
            },
            new[]
            {
                new GradientAlphaKey(0.9f, 0f),
                new GradientAlphaKey(0.55f, 0.4f),
                new GradientAlphaKey(0f, 1f),
            });
        trail.colorGradient = gradient;
        trail.textureMode = LineTextureMode.Stretch;
        trail.alignment = LineAlignment.View;
        trail.numCapVertices = 2;
        trail.autodestruct = false;
        trail.emitting = true;
        trail.shadowCastingMode = ShadowCastingMode.Off;
        trail.receiveShadows = false;
        trail.sharedMaterial = NewtonFightArt.ParticleMaterial("MagicOrbTrail", NewtonFightArt.BeamStreak(),
                                                               Color.white, additive: true);
        return trail;
    }

    static Light BuildLight(Transform parent)
    {
        var go = new GameObject("Light");
        go.transform.SetParent(parent, false);
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Cyan;
        light.range = 4f;
        light.intensity = 2.5f;
        light.shadows = LightShadows.None;
        return light;
    }

    /// <summary>Looping dust shed in world space, so it hangs behind the orb as
    /// a sparkling wake. Also puffed by the visual at launch and fizzle.</summary>
    static ParticleSystem BuildSparkles(Transform parent, Material mat)
    {
        var ps = NewParticles("Sparkles", parent, mat, loop: true, life: new Vector2(0.35f, 0.7f),
                              speed: new Vector2(0.1f, 0.5f), size: new Vector2(0.04f, 0.11f), maxParticles: 80);
        var emission = ps.emission;
        emission.rateOverTime = 28f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = CoreDiameter * 0.55f;
        Fade(ps, Cyan, Violet);
        return ps;
    }

    /// <summary>One spray at the burst, damped so it hangs in a ball instead of
    /// flying across the arena.</summary>
    static ParticleSystem BuildBurstSparks(Transform parent, Material mat)
    {
        var ps = NewParticles("BurstSparks", parent, mat, loop: false, life: new Vector2(0.25f, 0.55f),
                              speed: new Vector2(3f, 7f), size: new Vector2(0.06f, 0.16f), maxParticles: 64);
        var emission = ps.emission;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)30) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.3f;
        limit.limit = new ParticleSystem.MinMaxCurve(1.5f);
        Fade(ps, Color.white, Violet);
        return ps;
    }

    static ParticleSystem NewParticles(string name, Transform parent, Material mat, bool loop, Vector2 life,
                                       Vector2 speed, Vector2 size, int maxParticles)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        // Stopped before configuring: duration cannot be set while playing.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 1f;
        main.loop = loop;
        main.playOnAwake = loop;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.startColor = Color.white;
        main.gravityModifier = 0f;
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

    // ---- wiring ------------------------------------------------------------

    static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

    // Each setter fails loudly on a renamed field instead of throwing an NRE
    // halfway through the build.
    static void SetObject(SerializedObject so, string field, Object value)
    {
        var p = so.FindProperty(field);
        if (p == null) Debug.LogError($"[MagicOrbPrefabBuilder] {so.targetObject.GetType().Name} has no field '{field}'.");
        else p.objectReferenceValue = value;
    }

    static void SetFloat(SerializedObject so, string field, float value)
    {
        var p = so.FindProperty(field);
        if (p == null) Debug.LogError($"[MagicOrbPrefabBuilder] {so.targetObject.GetType().Name} has no field '{field}'.");
        else p.floatValue = value;
    }

    static void SetColour(SerializedObject so, string field, Color value)
    {
        var p = so.FindProperty(field);
        if (p == null) Debug.LogError($"[MagicOrbPrefabBuilder] {so.targetObject.GetType().Name} has no field '{field}'.");
        else p.colorValue = value;
    }
}
