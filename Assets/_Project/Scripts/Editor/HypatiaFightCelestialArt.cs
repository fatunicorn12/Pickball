using System.Collections.Generic;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using Pickleball.Sandbox.Boss.Hypatia.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The moons, their star trails and the Celestial Ring walls, split out of
/// <see cref="HypatiaFightArt"/> for the same reason as
/// <see cref="HypatiaFightPulseArt"/>: each has its own shader and generated
/// texture. Called by <see cref="HypatiaFightArt"/> and
/// <see cref="HypatiaFightSetup"/>; not a menu itself. Load-or-create, rewritten
/// in place, GUIDs kept, deterministic.
/// </summary>
public static class HypatiaFightCelestialArt
{
    const string ShaderDir = "Assets/_Project/Shaders/Hypatia";
    const string MoonTexturePath = HypatiaFightArt.GenDir + "/MoonSurface.png";

    // ── The moon ────────────────────────────────────────────────────────────

    /// <summary>The orbiters' body: lit by her alone (see MoonLook).</summary>
    public static Material MoonMat()
    {
        var mat = HypatiaFightPulseArt.ShaderMaterial("HypatiaMoon", $"{ShaderDir}/Moon.shader");
        mat.SetTexture("_Surface", MoonTexture());
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>Bakes <see cref="MoonSurface"/>: RGB = object-space normal, A =
    /// brightness, longitude-latitude. Data, not colour, so it is imported linear.</summary>
    static Texture2D MoonTexture()
    {
        const int width = 512, height = 256;
        var moon = new MoonSurface(seed: 7, craterCount: 110, seaCount: 7);
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Vector3 direction = MoonSurface.DirectionAt((x + 0.5f) / width, (y + 0.5f) / height);
            Vector3 normal = moon.Normal(direction, 1f);
            pixels[y * width + x] = new Color32(Encode(normal.x), Encode(normal.y), Encode(normal.z),
                                                HypatiaFightPulseArt.ToByte(moon.Albedo(direction)));
        }
        return HypatiaFightPulseArt.WriteTexture(MoonTexturePath, width, height, pixels, srgb: false, TextureWrapMode.Repeat);
    }

    static byte Encode(float v) => HypatiaFightPulseArt.ToByte(v * 0.5f + 0.5f);

    /// <summary>Makes the body sphere a moon: its material and the look that lights
    /// it from her. The sphere itself stays, so what you see is what you can hit.</summary>
    public static void MakeMoon(ConicOrbiter orbiter, GameObject body)
    {
        body.GetComponent<Renderer>().sharedMaterial = MoonMat();
        var look = orbiter.gameObject.AddComponent<MoonLook>();
        var so = new SerializedObject(look);
        so.FindProperty("orbiter").objectReferenceValue = orbiter;
        so.FindProperty("body").objectReferenceValue = body.GetComponent<Renderer>();
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ── Ring walls ──────────────────────────────────────────────────────────

    /// <summary>The Celestial Rings' walls.</summary>
    public static Material RingWallMat() => HypatiaFightPulseArt.ShaderMaterial("HypatiaRingWall", $"{ShaderDir}/RingWall.shader");

    /// <summary>The sparks off the rings' gap edges, one world-space system shared
    /// by every ring. Emitted from code (CelestialRingView), so its own rate is 0.</summary>
    public static ParticleSystem RingSparks(Transform parent)
    {
        var go = new GameObject("Sparks");
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.1f);
        main.gravityModifier = 0.35f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 300;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.enabled = false;
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
        colour.color = new ParticleSystem.MinMaxGradient(fade);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.05f;
        renderer.lengthScale = 1.5f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = HypatiaFightArt.Particle("HypatiaRingSpark", HypatiaFightArt.SharedTexture("Glow.png"),
                                                           new Color(5f, 5f, 5f, 1f), additive: true);
        return ps;
    }

    // ── Star trails ─────────────────────────────────────────────────────────

    static Material StarMat() => HypatiaFightPulseArt.ShaderMaterial("HypatiaStarTrail", $"{ShaderDir}/StarTrail.shader");

    /// <summary>Stars left behind <paramref name="root"/> as it moves, between two
    /// tints. The system detaches itself at runtime (see <see cref="StarTrail"/>).</summary>
    /// <param name="perMetre">Stars per metre flown. Lower for faster projectiles.</param>
    /// <param name="spread">How far off the flight line they land, m.</param>
    public static void AddStarTrail(GameObject root, Color tintA, Color tintB, float perMetre, float spread)
    {
        var go = new GameObject("Stars");
        go.transform.SetParent(root.transform, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.duration = 5f;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.03f, 0.3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
        main.startColor = new ParticleSystem.MinMaxGradient(tintA, tintB);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = perMetre;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = spread;
        shape.radiusThickness = 1f;

        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.75f, 0.6f), new GradientAlphaKey(0f, 1f) });
        colour.color = new ParticleSystem.MinMaxGradient(fade);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = StarMat();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.StableRandomXY,
        });

        var trail = root.AddComponent<StarTrail>();
        var so = new SerializedObject(trail);
        so.FindProperty("stars").objectReferenceValue = ps;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
