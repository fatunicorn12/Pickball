using System.IO;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Boss.Hypatia.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Everything the knockback pulse is drawn with, split out of
/// <see cref="HypatiaFightArt"/> because the effect has grown its own shaders,
/// generated textures and particle layers. Called by
/// <see cref="HypatiaFightSetup"/>; not a menu itself. Same rules as the rest of
/// the fight's art: load-or-create, rewritten in place, GUIDs kept, deterministic.
///
/// Particle colours are capped at 1 once they reach the vertices, so the HDR
/// punch that makes them bloom comes from the materials' tints instead.
/// </summary>
public static class HypatiaFightPulseArt
{
    const string ShaderDir = "Assets/_Project/Shaders/Hypatia";
    const string NoiseTexturePath = HypatiaFightArt.GenDir + "/PulseNoise.png";
    const string PuffTexturePath = HypatiaFightArt.GenDir + "/PulsePuff.png";

    // ── Materials ───────────────────────────────────────────────────────────

    /// <summary>The dial and the shockwave on the plate.</summary>
    public static Material FloorMat() => ShaderMat("HypatiaPulseFloor", "PulseFloor.shader");

    /// <summary>The dome's glowing silhouette.</summary>
    public static Material ShellMat() => ShaderMat("HypatiaPulseShell", "PulseShell.shader");

    /// <summary>The heat ripple through the dome's silhouette (PC render settings only).</summary>
    public static Material RippleMat() => ShaderMat("HypatiaPulseRipple", "PulseRipple.shader");

    /// <summary>The charge and flash on her body.</summary>
    public static Material CoreMat() => ShaderMat("HypatiaPulseCore", "PulseCore.shader");

    static Material SparkMat() => HypatiaFightArt.Particle("HypatiaPulseSpark", HypatiaFightArt.SharedTexture("Glow.png"),
                                                           new Color(3.5f, 3.5f, 3.5f, 1f), additive: true);

    static Material DustMat() => HypatiaFightArt.Particle("HypatiaPulseDust", PuffTexture(), Color.white, additive: false);

    static Material ShaderMat(string name, string shaderFile)
    {
        string path = $"{ShaderDir}/{shaderFile}";
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null)
        {
            Debug.LogError($"[HypatiaFight] Missing shader {path}; the pulse will not draw.");
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        }
        var mat = HypatiaFightArt.LoadOrCreate($"{HypatiaFightArt.MatDir}/{name}.mat", shader);
        mat.SetTexture("_Noise", NoiseTexture());
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ── Textures ────────────────────────────────────────────────────────────

    /// <summary>Three independent tiling noises, one per channel: R and G broad,
    /// B finer. Data, not colour, so it is imported linear.</summary>
    static Texture2D NoiseTexture()
    {
        const int size = 256;
        float[] r = TilingNoise.Generate(size, 11, 5, 5);
        float[] g = TilingNoise.Generate(size, 23, 4, 4);
        float[] b = TilingNoise.Generate(size, 37, 12, 4);
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = new Color32(Byte(r[i]), Byte(g[i]), Byte(b[i]), 255);
        return WriteTexture(NoiseTexturePath, size, pixels, srgb: false, TextureWrapMode.Repeat);
    }

    /// <summary>One soft, broken-edged puff of dust: white, shaped entirely by alpha.</summary>
    static Texture2D PuffTexture()
    {
        const int size = 128;
        float[] noise = TilingNoise.Generate(size, 53, 4, 4);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
            float falloff = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
            float a = Mathf.Clamp01(falloff * 1.5f - (1f - noise[y * size + x]) * 0.55f);
            pixels[y * size + x] = new Color32(255, 255, 255, Byte(a * a * (3f - 2f * a)));
        }
        return WriteTexture(PuffTexturePath, size, pixels, srgb: true, TextureWrapMode.Clamp);
    }

    static byte Byte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

    /// <summary>Writes the PNG only when its bytes changed, so a rebuild with the
    /// same generator touches nothing.</summary>
    static Texture2D WriteTexture(string path, int size, Color32[] pixels, bool srgb, TextureWrapMode wrap)
    {
        ChibiSandboxSetup.EnsureFolderPublic(HypatiaFightArt.GenDir);
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, !srgb);
        tex.SetPixels32(pixels);
        byte[] png = tex.EncodeToPNG();
        Object.DestroyImmediate(tex);

        string file = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, path);
        bool same = File.Exists(file) && SameBytes(File.ReadAllBytes(file), png);
        if (!same)
        {
            File.WriteAllBytes(file, png);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        if (ti.sRGBTexture != srgb || ti.wrapMode != wrap || ti.alphaIsTransparency != srgb || !ti.mipmapEnabled ||
            ti.textureCompression != TextureImporterCompression.Uncompressed)
        {
            ti.sRGBTexture = srgb;
            ti.wrapMode = wrap;
            ti.alphaIsTransparency = srgb;
            ti.mipmapEnabled = true;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static bool SameBytes(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++)
            if (a[i] != b[i]) return false;
        return true;
    }

    // ── Scene parts ─────────────────────────────────────────────────────────

    /// <summary>The camera-facing quad on her body the charge and flash draw on.
    /// Kept out of lock-on's aim and size. Hidden at rest.</summary>
    public static GameObject CoreQuad(string name, Transform body, Vector3 localPosition)
    {
        var go = HypatiaFightArt.Primitive(PrimitiveType.Quad, name, body, localPosition, Quaternion.identity,
                                           Vector3.one, CoreMat());
        go.AddComponent<FaceCamera>();
        go.AddComponent<LockOnIgnore>();
        NoShadows(go);
        go.SetActive(false);
        return go;
    }

    /// <summary>Motes of light rushing in toward her over the charge. Local space,
    /// so they stay gathered on her; PulseView moves the emitter to her chest and
    /// runs it on her clock, so freezing her freezes them too.</summary>
    public static ParticleSystem Gather(Transform parent)
    {
        var ps = NewSystem("Gather", parent, out var renderer);
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.playOnAwake = false;
        main.startLifetime = 0.55f;
        main.startSpeed = -6f;   // negative: from the shell inward
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.62f, 0.75f, 1f), HypatiaFightArt.Brass);
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 160;
        var emission = ps.emission;
        emission.rateOverTime = 220f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 3.4f;
        shape.radiusThickness = 0f;
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        colour.color = new ParticleSystem.MinMaxGradient(FadeGradient(0f, 0.25f, 1f, 1f));
        Stretch(renderer, 0.1f, SparkMat());
        return ps;
    }

    /// <summary>Sparks thrown out flat across the plate when it fires.</summary>
    public static ParticleSystem Sparks(Transform parent)
    {
        var ps = NewSystem("Sparks", parent, out var renderer);
        ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var main = ps.main;
        main.loop = false;
        main.duration = 1f;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.95f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(12f, 36f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.18f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.86f, 1f), HypatiaFightArt.Brass);
        main.gravityModifier = 0.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 140;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 130) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1f;
        shape.radiusThickness = 1f;
        shape.randomDirectionAmount = 0.12f;
        Drag(ps, 2.6f);
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        colour.color = new ParticleSystem.MinMaxGradient(FadeGradient(1f, 0.55f, 1f, 0f));
        Stretch(renderer, 0.03f, SparkMat());
        return ps;
    }

    /// <summary>Dust kicked outward by the wave. Drag makes each puff settle at
    /// speed ÷ drag (6–11 m at PulseView's default speeds), so the plate is left
    /// hazy right out to Pulse Reach. PulseView sets the speed on each burst.</summary>
    public static ParticleSystem Dust(Transform parent)
    {
        var ps = NewSystem("Dust", parent, out var renderer);
        ps.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        var main = ps.main;
        main.loop = false;
        main.duration = 1f;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.85f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(30f, 55f);
        main.startSize = new ParticleSystem.MinMaxCurve(2.4f, 4.6f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 2f * Mathf.PI);
        main.startColor = new Color(0.62f, 0.68f, 0.95f, 0.14f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 1f;
        shape.radiusThickness = 1f;
        Drag(ps, 5f);
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        colour.color = new ParticleSystem.MinMaxGradient(FadeGradient(0f, 0.08f, 1f, 0f));
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.55f, 1f, 1.6f));
        renderer.sharedMaterial = DustMat();
        return ps;
    }

    /// <summary>The real light the flash throws on the plate, her and the player.
    /// Off until the pulse charges.</summary>
    public static Light FlashLight(Transform parent)
    {
        var go = new GameObject("FlashLight");
        go.transform.SetParent(parent, false);
        var light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.72f, 0.82f, 1f);
        light.range = 22f;
        light.intensity = 0f;
        light.shadows = LightShadows.None;
        light.enabled = false;
        return light;
    }

    static ParticleSystem NewSystem(string name, Transform parent, out ParticleSystemRenderer renderer)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    static void Stretch(ParticleSystemRenderer renderer, float velocityScale, Material mat)
    {
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = velocityScale;
        renderer.lengthScale = 1.5f;
        renderer.sharedMaterial = mat;
    }

    static void Drag(ParticleSystem ps, float drag)
    {
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.limit = 1000f;   // never clamps: only the drag acts
        limit.dampen = 0f;
        limit.drag = drag;
        // Both default on, and either one stops a fast particle almost at once;
        // off, a particle settles at speed ÷ drag, which the sizes here rely on.
        limit.multiplyDragByParticleSize = false;
        limit.multiplyDragByParticleVelocity = false;
    }

    /// <summary>White with alpha going start → 1 by <paramref name="peakAt"/>, holding
    /// <paramref name="hold"/> there, then to <paramref name="end"/>.</summary>
    static Gradient FadeGradient(float start, float peakAt, float hold, float end)
    {
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(start, 0f), new GradientAlphaKey(hold, peakAt), new GradientAlphaKey(end, 1f) });
        return gradient;
    }

    static void NoShadows(GameObject go)
    {
        var r = go.GetComponent<Renderer>();
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
    }
}
