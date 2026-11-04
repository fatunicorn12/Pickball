using System.Collections.Generic;
using Pickleball.Sandbox.Boss.Hypatia.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Everything Alexandria's Burning is drawn with: the floor overlay's material and
/// each band's flames, embers and burning pages. There is deliberately no smoke:
/// dark smoke cannot be seen against her night sky, so it only added overdraw. Called by
/// <see cref="HypatiaFightSetup"/>; not a menu itself. Load-or-create, rewritten in
/// place, GUIDs kept, deterministic. BandFireView fits each system's circle and rate
/// to its band at runtime, so the values here for those are placeholders.
/// </summary>
public static class HypatiaFightFireArt
{
    const string ShaderDir = "Assets/_Project/Shaders/Hypatia";
    const string PageTexturePath = HypatiaFightArt.GenDir + "/BurningPage.png";

    /// <summary>The band's floor: warning, burn front, char and veins.</summary>
    public static Material FloorMat()
    {
        var mat = HypatiaFightPulseArt.ShaderMaterial("HypatiaBandFire", $"{ShaderDir}/BandFire.shader");
        // Set here, not left to the shader's defaults: a material that already exists
        // keeps its old values when a shader default changes.
        mat.SetColor("_EmberColor", new Color(1.7f, 0.36f, 0.05f));
        mat.SetColor("_HotColor", new Color(3.2f, 1.6f, 0.4f));
        mat.SetColor("_CharColor", new Color(0.03f, 0.022f, 0.028f));
        mat.SetFloat("_CharAlpha", 0.97f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material FlameMat()
    {
        var mat = HypatiaFightPulseArt.ShaderMaterial("HypatiaFireFlame", $"{ShaderDir}/FireFlame.shader");
        mat.SetColor("_RedColor", new Color(1.1f, 0.12f, 0.015f));
        mat.SetColor("_OrangeColor", new Color(1.7f, 0.42f, 0.04f));
        mat.SetColor("_WhiteColor", new Color(2.5f, 1.35f, 0.35f));
        mat.SetFloat("_Rise", 1.1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>Upright flames, not playing until the band ignites. The emitter sits
    /// off the floor because a quad is centred on its particle: this stands each
    /// flame on the plate, its lower edge hidden just below it.</summary>
    public static ParticleSystem Flames(Transform parent)
    {
        var ps = Flat("Flames", parent, 0.72f, out var renderer);
        var main = ps.main;
        main.loop = true;
        main.duration = 2f;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(1.5f, 2.9f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(1.1f, 2.1f);
        main.startSizeZ = 1f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 240;
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        // All three axes in the same curve mode (TwoConstants): mixing modes makes the
        // particle system log an error every frame.
        velocity.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.25f, 0.8f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        renderer.renderMode = ParticleSystemRenderMode.VerticalBillboard;
        renderer.sharedMaterial = FlameMat();
        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position,
            ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV,
            ParticleSystemVertexStream.StableRandomXY,
            ParticleSystemVertexStream.AgePercent,
        });
        return ps;
    }

    /// <summary>Sparks rising and wandering on the heat. Also the burst when a band
    /// ignites (BandFireView emits it).</summary>
    public static ParticleSystem Embers(Transform parent)
    {
        var ps = Flat("Embers", parent, 0.3f, out var renderer);
        var main = ps.main;
        main.loop = true;
        main.duration = 2f;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 2.4f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.09f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.72f, 0.3f), new Color(1f, 0.35f, 0.08f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 260;
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        velocity.y = new ParticleSystem.MinMaxCurve(1.4f, 4.2f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.9f;
        noise.frequency = 0.55f;
        noise.quality = ParticleSystemNoiseQuality.Low;
        FadeOverLife(ps, 1f, 0.5f, 0f);
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.04f;
        renderer.lengthScale = 1.5f;
        renderer.sharedMaterial = HypatiaFightArt.Particle("HypatiaFireEmber", HypatiaFightArt.SharedTexture("Glow.png"),
                                                           new Color(4f, 4f, 4f, 1f), additive: true);
        return ps;
    }

    /// <summary>Scraps of burning page, tumbling up on the heat, with their edges
    /// glowing. This is what makes it the Library burning and not just a floor on fire.</summary>
    public static ParticleSystem Pages(Transform parent)
    {
        var ps = Flat("Pages", parent, 0.5f, out var renderer);
        var main = ps.main;
        main.loop = true;
        main.duration = 2f;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(2.6f, 4.6f);
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(0.2f, 0.42f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.26f, 0.5f);
        main.startSizeZ = 1f;
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;
        var velocity = ps.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        velocity.y = new ParticleSystem.MinMaxCurve(0.9f, 2.2f);
        velocity.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
        rotation.y = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
        rotation.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.7f;
        noise.frequency = 0.4f;
        noise.quality = ParticleSystemNoiseQuality.Low;
        FadeOverLife(ps, 0f, 1f, 0f);
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
        renderer.sharedMaterial = HypatiaFightArt.Particle("HypatiaBurningPage", PageTexture(), Color.white, additive: false);
        return ps;
    }

    /// <summary>The light the fire throws. Off until a band burns.</summary>
    public static Light FireLight(Transform parent)
    {
        var go = new GameObject("FireLight");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 3.5f, 0f);
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.52f, 0.2f);
        light.range = 30f;
        light.intensity = 0f;
        light.shadows = LightShadows.None;
        light.enabled = false;
        return light;
    }

    /// <summary>A torn scrap of parchment: pale in the middle, charred toward a
    /// broken edge, glowing where it is still burning.</summary>
    static Texture2D PageTexture()
    {
        const int size = 128;
        float[] noise = TilingNoise.Generate(size, 71, 5, 4);
        var pixels = new Color32[size * size];
        var parchment = new Color(0.86f, 0.74f, 0.52f);
        var char_ = new Color(0.09f, 0.06f, 0.05f);
        var burning = new Color(1f, 0.5f, 0.1f);
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = Mathf.Abs((x + 0.5f) / size * 2f - 1f), dy = Mathf.Abs((y + 0.5f) / size * 2f - 1f);
            // 1 in the middle, 0 at the square's edge, eaten into by the noise.
            float inset = Mathf.Min(1f - dx, 1f - dy) * 2.4f + (noise[y * size + x] - 0.5f) * 1.3f;
            Color c = Color.Lerp(char_, parchment, Mathf.Clamp01((inset - 0.75f) / 0.5f));
            c = Color.Lerp(burning, c, Mathf.Clamp01((inset - 0.08f) / 0.26f));
            float alpha = Mathf.Clamp01(inset / 0.08f);
            pixels[y * size + x] = new Color32(HypatiaFightPulseArt.ToByte(c.r), HypatiaFightPulseArt.ToByte(c.g),
                                               HypatiaFightPulseArt.ToByte(c.b), HypatiaFightPulseArt.ToByte(alpha));
        }
        return HypatiaFightPulseArt.WriteTexture(PageTexturePath, size, size, pixels, srgb: true, TextureWrapMode.Clamp);
    }

    /// <summary>A system lying flat (its circle shape on the plate), raised
    /// <paramref name="height"/> m, with no shadows. Not playing.</summary>
    static ParticleSystem Flat(string name, Transform parent, float height, out ParticleSystemRenderer renderer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.SetLocalPositionAndRotation(new Vector3(0f, height, 0f), Quaternion.Euler(-90f, 0f, 0f));
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1f;
        shape.radiusThickness = 1f;
        renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    /// <summary>White with alpha going start → 1 at <paramref name="peakAt"/> → end.</summary>
    static void FadeOverLife(ParticleSystem ps, float start, float peakAt, float end)
    {
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var gradient = new Gradient();
        float peak = Mathf.Clamp(peakAt, 0.01f, 0.99f);
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(start, 0f), new GradientAlphaKey(1f, peak), new GradientAlphaKey(end, 1f) });
        colour.color = new ParticleSystem.MinMaxGradient(gradient);
    }
}
