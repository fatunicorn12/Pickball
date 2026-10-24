using System;
using System.IO;
using Pickleball;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Every texture, material and effect the Newton fight is drawn with, generated
/// from code. Called by <see cref="NewtonFightSetup"/>; not a menu item itself.
///
/// Generated rather than painted for the same reason as SandboxVfxBuilder's
/// cloud puff — no external dependency, identical on every machine — and for
/// one more: several of these textures encode the fight's actual numbers. The
/// gravity well's floor decal draws its contour rings at real distances from
/// the prefab's own radii, so retuning the well redraws the picture instead of
/// leaving a painting that quietly lies about the field.
///
/// Unlike the cloud puff builder, everything here is rebuilt on every run:
/// textures are rewritten in place (their .meta, and so every reference, is
/// kept) and materials are re-configured rather than skipped when present.
/// A load-or-skip builder makes the first version of an effect permanent,
/// which is exactly how the old flat-colour placeholders survived.
///
/// The one thing deliberately NOT generated is a hand-made apple. If a model
/// (a Tripo export, say) is dropped into <see cref="AppleDir"/>, the apple
/// prefabs use it; otherwise they fall back to the low-poly lathe built here.
/// </summary>
public static class NewtonFightArt
{
    public const string UiDir = "Assets/_Project/Art/UI";
    public const string VfxDir = "Assets/_Project/Art/VFX";
    public const string AppleDir = "Assets/_Project/Art/Props/Apple";

    const string FontDir = "Assets/TextMesh Pro/Examples & Extras/Resources/Fonts & Materials";

    // ---- UI sprites ------------------------------------------------------

    /// <summary>Rounded white slab, 9-sliced. Tinted, it is the bar's track;
    /// with a Mask on it, it clips the fills to the same rounded shape.</summary>
    public static Sprite BarMask() => WriteSprite($"{UiDir}/BarMask.png", 64, 32, new Vector4(9, 9, 9, 9),
        (x, y) => new Color(1f, 1f, 1f, Coverage(RoundBox(x, y, 64, 32, 7f))));

    /// <summary>Bevelled frame, 9-sliced, hollow. White where it should take the
    /// tint, dark at the outline and inner lip so those survive any tint. Laid
    /// 5 px outside the track, its inner edge sits exactly on the track's.</summary>
    public static Sprite BarFrame() => WriteSprite($"{UiDir}/BarFrame.png", 64, 32, new Vector4(14, 14, 14, 14),
        (x, y) =>
        {
            float d = RoundBox(x, y, 64, 32, 12f);
            float alpha = Coverage(d) * Mathf.Clamp01(d + 5.5f);
            float v = y / 32f;
            float shade = d > -1.5f ? 0.1f
                        : d > -4f ? Mathf.Lerp(0.55f, 1f, v) + (v > 0.8f ? 0.15f : 0f)
                        : 0.18f;
            shade = Mathf.Clamp01(shade);
            return new Color(shade, shade, shade, alpha);
        });

    /// <summary>The fill: a vertical gradient with a gloss band and a bright top
    /// lip. Greyscale so one sprite serves every fill colour.</summary>
    public static Sprite BarFill() => WriteSprite($"{UiDir}/BarFill.png", 8, 64, Vector4.zero,
        (x, y) =>
        {
            float v = y / 64f;
            float shade = Mathf.Lerp(0.6f, 0.92f, v)
                        + 0.2f * Mathf.Exp(-Mathf.Pow((v - 0.74f) / 0.08f, 2f));
            if (v > 0.92f) shade = 1f;
            if (v < 0.1f) shade *= 0.72f;
            shade = Mathf.Clamp01(shade);
            return new Color(shade, shade, shade, 1f);
        });

    // ---- VFX textures ----------------------------------------------------

    /// <summary>Beam body. U tiles along the beam with streaks whose frequencies
    /// are whole numbers, so the seam is invisible when it scrolls; V is a hot
    /// core inside a soft glow.</summary>
    public static Texture2D BeamStreak() => WriteTexture($"{VfxDir}/BeamStreak.png", 256, 64, true,
        (x, y) =>
        {
            float u = x / 256f;
            float t = (y / 64f - 0.5f) * 2f;
            float core = Mathf.Exp(-Mathf.Pow(t / 0.16f, 2f));
            float glow = Mathf.Exp(-Mathf.Pow(t / 0.5f, 2f));
            float n = 0.5f + 0.5f * (0.5f * Mathf.Sin(Tau * 3f * u + 2f * t)
                                   + 0.3f * Mathf.Sin(Tau * 7f * u - 3f * t + 1.3f)
                                   + 0.2f * Mathf.Sin(Tau * 13f * u + 5f * t + 2.1f));
            return White(core * 0.95f + glow * (0.2f + 0.55f * n));
        });

    /// <summary>
    /// The gravity well's floor decal, mapped so the texture's edge is the
    /// influence radius. Brightness climbs toward the centre, spiral arms give
    /// it a direction of pull, and thin contour rings mark fixed distances in
    /// metres — the "terrain with a gradient" the fight is meant to be read as.
    /// </summary>
    public static Texture2D WellField(float coreRadius, float influenceRadius, float[] contourMetres)
    {
        float core = Mathf.Clamp(coreRadius / Mathf.Max(influenceRadius, 0.01f), 0.02f, 0.9f);
        return WriteTexture($"{VfxDir}/WellField.png", 512, 512, false, (x, y) =>
        {
            float dx = x / 256f - 1f, dy = y / 256f - 1f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            if (r >= 1f) return White(0f);

            // Not literal 1/r²: that would be invisible across most of the decal.
            // The exponent is softened so the rim still shows, but the ordering —
            // faint outside, blinding near the core — is the real one.
            float fall = Mathf.Pow(core / Mathf.Max(r, core), 1.1f);
            float theta = Mathf.Atan2(dy, dx);
            float arms = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(3f * theta - 9f * Mathf.Log(Mathf.Max(r, 0.05f))));

            float rings = 0f;
            foreach (float m in contourMetres)
            {
                float ri = m / influenceRadius;
                if (ri <= core || ri >= 1f) continue;
                rings += Mathf.Exp(-Mathf.Pow((r - ri) / 0.006f, 2f));
            }
            float rim = Mathf.Exp(-Mathf.Pow((r - 0.985f) / 0.01f, 2f));
            float fade = Mathf.SmoothStep(0f, 1f, (1f - r) / 0.03f);
            return White((fall * arms * 0.7f + rings * 0.5f + rim * 0.6f) * fade);
        });
    }

    /// <summary>A glowing ring with a soft outer bloom — the well core's
    /// accretion halo.</summary>
    public static Texture2D WellHalo() => WriteTexture($"{VfxDir}/WellHalo.png", 128, 128, false,
        (x, y) =>
        {
            float r = Radius(x, y, 128);
            float ring = Mathf.Exp(-Mathf.Pow((r - 0.55f) / 0.1f, 2f));
            float bloom = Mathf.Exp(-Mathf.Pow((r - 0.55f) / 0.32f, 2f));
            return White((ring * 0.9f + bloom * 0.35f) * Mathf.Clamp01((1f - r) / 0.08f));
        });

    /// <summary>Target ring with four inward ticks and a faint fill — the WHERE
    /// of the apple telegraph.</summary>
    public static Texture2D TargetRing() => WriteTexture($"{VfxDir}/TargetRing.png", 128, 128, false,
        (x, y) =>
        {
            float px = 2f / 128f;
            float r = Radius(x, y, 128);
            float ring = Mathf.Clamp01((r - 0.85f) / px) * Mathf.Clamp01((0.97f - r) / px);
            float ax = Mathf.Abs(x / 64f - 1f), ay = Mathf.Abs(y / 64f - 1f);
            bool tick = (ay < 0.035f && ax > 0.6f && ax < 0.82f) || (ax < 0.035f && ay > 0.6f && ay < 0.82f);
            float fill = r < 0.85f ? 0.14f : 0f;
            return White(Mathf.Max(ring, tick ? 1f : 0f, fill));
        });

    /// <summary>Soft-edged filled disc — the WHEN, growing inside the ring.</summary>
    public static Texture2D Disc() => WriteTexture($"{VfxDir}/Disc.png", 128, 128, false,
        (x, y) => White(Mathf.SmoothStep(0f, 1f, (1f - Radius(x, y, 128)) / 0.12f)));

    /// <summary>Bright-centred glow for halos, sparks and droplets. Brighter in
    /// the middle than SoftDot, so additive sparks have a hot point.</summary>
    public static Texture2D Glow() => WriteTexture($"{VfxDir}/Glow.png", 64, 64, false,
        (x, y) =>
        {
            float r = Radius(x, y, 64);
            float a = Mathf.Clamp01(1f - r);
            return White(a * a * (0.6f + 0.4f * Mathf.Exp(-r * r * 30f)));
        });

    // ---- materials -------------------------------------------------------

    /// <summary>
    /// URP Particles/Unlit, transparent. Chosen over URP/Unlit for every effect
    /// here because it multiplies in vertex colour — which is how LineRenderer
    /// and ParticleSystem colour reach the screen. On URP/Unlit the beam's
    /// charge-to-hot colour ramp was being computed and then silently dropped.
    ///
    /// Blend state is written out by hand: a material made from script never
    /// passes through the URP inspector that would normally derive it.
    /// </summary>
    public static Material ParticleMaterial(string name, Texture texture, Color tint, bool additive,
                                            bool twoSided = true)
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var mat = LoadOrCreate($"{VfxDir}/{name}.mat", shader);

        mat.SetTexture("_BaseMap", texture);
        mat.SetColor("_BaseColor", tint);
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", additive ? 2f : 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)(twoSided ? CullMode.Off : CullMode.Back));
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>Opaque unlit — for things that should read as an absence of
    /// light, like the well's core.</summary>
    public static Material UnlitMaterial(string name, Color colour)
    {
        var mat = LoadOrCreate($"{VfxDir}/{name}.mat", Shader.Find("Universal Render Pipeline/Unlit"));
        mat.SetColor("_BaseColor", colour);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>Lit, for solid props. Smoothness given per call: an apple wants
    /// a sheen, a stalk does not. Emission is off unless a colour is given.</summary>
    public static Material LitMaterial(string dir, string name, Color colour, float smoothness,
                                       Color? emission = null)
    {
        var mat = LoadOrCreate($"{dir}/{name}.mat", Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor", colour);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", 0f);
        if (emission is Color glow)
        {
            mat.SetColor("_EmissionColor", glow);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material LoadOrCreate(string path, Shader shader)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        else if (mat.shader != shader)
        {
            mat.shader = shader;
        }
        return mat;
    }

    // ---- quads -----------------------------------------------------------

    /// <summary>Collider-free unit quad. Face-up when <paramref name="flat"/>,
    /// otherwise camera-facing via FaceCamera.</summary>
    public static GameObject Quad(string name, Transform parent, Material mat, float size, bool flat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(size, size, 1f);
        if (flat) go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        else go.AddComponent<Pickleball.Sandbox.FaceCamera>();

        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }

    // ---- particle effects ------------------------------------------------

    /// <summary>Apple hitting something: red juice and pale flesh, thrown up
    /// and falling back. Distinct from <see cref="AppleCancel"/> on purpose —
    /// a splat means an apple got through, a cancel means the field saved you.</summary>
    public static ParticleSystem AppleSplat(string prefabPath)
    {
        var ps = NewBurst("AppleSplat", ParticleMaterial("AppleJuice", Glow(), Color.white, false),
                          count: 22, life: new Vector2(0.35f, 0.75f), speed: new Vector2(2.5f, 5.5f),
                          size: new Vector2(0.08f, 0.22f), gravity: 1.6f);
        var main = ps.main;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.07f, 0.1f),
                                                            new Color(1f, 0.9f, 0.68f));
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.15f;
        shape.rotation = new Vector3(-90f, 0f, 0f);
        FadeOut(ps, Color.white, Color.white);
        return SaveEffect(ps, prefabPath);
    }

    /// <summary>Two apples annihilating: a gold-white ring of sparks, no
    /// gravity, nothing landing. Reads as "gone", not "hit".</summary>
    public static ParticleSystem AppleCancel(string prefabPath)
    {
        var ps = NewBurst("AppleCancel", ParticleMaterial("Sparkle", Glow(), Color.white, true),
                          count: 26, life: new Vector2(0.3f, 0.55f), speed: new Vector2(3f, 6f),
                          size: new Vector2(0.12f, 0.26f), gravity: 0f);
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.1f;
        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.3f;
        limit.limit = new ParticleSystem.MinMaxCurve(2f);
        FadeOut(ps, new Color(1f, 0.82f, 0.3f), Color.white);
        return SaveEffect(ps, prefabPath);
    }

    /// <summary>Sparks thrown off where the Fluxion Beam meets the player.
    /// Looping; the beam plays, positions and tints it, scaling it with heat.</summary>
    public static ParticleSystem BeamImpact(Transform parent)
    {
        const string name = "BeamImpact";
        var existing = parent.Find(name);
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        var ps = NewBurst(name, ParticleMaterial("BeamSpark", Glow(), Color.white, true),
                          count: 0, life: new Vector2(0.18f, 0.4f), speed: new Vector2(2f, 5.5f),
                          size: new Vector2(0.06f, 0.16f), gravity: 0.6f);
        ps.transform.SetParent(parent, false);
        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        // A single constant, so FluxionBeam's heat scaling of startSizeMultiplier
        // is a clean multiple of it rather than a write to one end of a range.
        main.startSize = 0.11f;
        var emission = ps.emission;
        emission.rateOverTime = 70f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.15f;
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.06f;
        renderer.lengthScale = 1.5f;
        FadeOut(ps, Color.white, Color.white);
        return ps;
    }

    static ParticleSystem NewBurst(string name, Material mat, int count, Vector2 life,
                                   Vector2 speed, Vector2 size, float gravity)
    {
        var go = new GameObject(name);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.8f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
        main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
        main.gravityModifier = gravity;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        if (count > 0) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.35f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return ps;
    }

    static void FadeOut(ParticleSystem ps, Color from, Color to)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                     new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);
    }

    static ParticleSystem SaveEffect(ParticleSystem ps, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var saved = PrefabUtility.SaveAsPrefabAsset(ps.gameObject, path);
        Object.DestroyImmediate(ps.gameObject);
        return saved.GetComponent<ParticleSystem>();
    }

    // ---- apple -----------------------------------------------------------

    /// <summary>
    /// Hangs an apple visual of the given diameter under <paramref name="parent"/>.
    /// A model in <see cref="AppleDir"/> wins; otherwise the generated lathe
    /// body with a stalk and leaf. Returns the body renderer so the caller can
    /// swap its material (the golden reward apple does).
    /// </summary>
    public static Renderer AppleVisual(Transform parent, float diameter, Material skinOverride = null)
    {
        var model = FindAppleModel();
        if (model != null)
        {
            // Unlinked, not InstantiatePrefab: the apple prefab this is saved into
            // must own the copy outright rather than nest the model asset.
            var instance = Object.Instantiate(model);
            instance.name = "Model";
            instance.transform.SetParent(parent, false);
            FitToDiameter(instance, diameter);
            foreach (var c in instance.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            var body = instance.GetComponentInChildren<Renderer>();
            if (skinOverride != null)
                foreach (var r in instance.GetComponentsInChildren<Renderer>()) r.sharedMaterial = skinOverride;
            return body;
        }

        ChibiSandboxSetup.EnsureFolderPublic(AppleDir);
        var skin = skinOverride != null ? skinOverride
                 : LitMaterial(AppleDir, "AppleSkin", new Color(0.82f, 0.1f, 0.13f), 0.55f);

        var bodyGo = new GameObject("Body", typeof(MeshFilter), typeof(MeshRenderer));
        bodyGo.transform.SetParent(parent, false);
        bodyGo.transform.localScale = Vector3.one * diameter;
        bodyGo.GetComponent<MeshFilter>().sharedMesh = AppleMesh();
        var bodyRenderer = bodyGo.GetComponent<MeshRenderer>();
        bodyRenderer.sharedMaterial = skin;

        var stalk = PrimitiveFactory.CreateBox("Stalk", new Vector3(0.06f, 0.36f, 0.06f), Color.white, bodyGo.transform, false);
        stalk.transform.localPosition = new Vector3(0.02f, 0.36f, 0f);
        stalk.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
        stalk.GetComponent<Renderer>().sharedMaterial =
            LitMaterial(AppleDir, "AppleStalk", new Color(0.36f, 0.24f, 0.13f), 0.05f);

        var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        leaf.name = "Leaf";
        Object.DestroyImmediate(leaf.GetComponent<Collider>());
        leaf.transform.SetParent(bodyGo.transform, false);
        leaf.transform.localPosition = new Vector3(0.16f, 0.44f, 0f);
        leaf.transform.localRotation = Quaternion.Euler(0f, 0f, -28f);
        leaf.transform.localScale = new Vector3(0.34f, 0.035f, 0.16f);
        leaf.GetComponent<Renderer>().sharedMaterial =
            LitMaterial(AppleDir, "AppleLeaf", new Color(0.3f, 0.66f, 0.22f), 0.25f);

        return bodyRenderer;
    }

    static GameObject FindAppleModel()
    {
        if (!AssetDatabase.IsValidFolder(AppleDir)) return null;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { AppleDir }))
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (go != null) return go;
        }
        return null;
    }

    /// <summary>Generated models arrive at arbitrary scale and pivot: scale the
    /// largest extent to the diameter and centre the bounds on the parent.</summary>
    static void FitToDiameter(GameObject instance, float diameter)
    {
        var renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        var bounds = renderers[0].bounds;
        foreach (var r in renderers) bounds.Encapsulate(r.bounds);
        float extent = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        if (extent < 1e-5f) return;
        float k = diameter / extent;
        instance.transform.localScale *= k;
        instance.transform.position -= (bounds.center - instance.transform.parent.position) * k;
    }

    /// <summary>
    /// Low-poly apple: a lathed profile with a deep dimple at the stalk, a
    /// shallow one underneath, shoulders wider than the base and five faint
    /// lobes at the bottom. Flat shaded — every triangle owns its vertices —
    /// to sit with the rest of the primitive-built fight. Unit diameter.
    /// </summary>
    public static Mesh AppleMesh()
    {
        string path = $"{AppleDir}/AppleBody.asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null)
        {
            ChibiSandboxSetup.EnsureFolderPublic(AppleDir);
            mesh = new Mesh { name = "AppleBody" };
            AssetDatabase.CreateAsset(mesh, path);
        }

        const int lat = 9, lon = 12;
        var grid = new Vector3[lat + 1, lon];
        for (int i = 0; i <= lat; i++)
        {
            float phi = Mathf.PI * i / lat;
            float s = Mathf.Sin(phi);
            float up = -Mathf.Cos(phi);
            float shoulder = 0.5f + 0.5f * up;
            float y = up * 0.85f
                    - 0.32f * Mathf.Exp(-Mathf.Pow((Mathf.PI - phi) / 0.45f, 2f))
                    + 0.12f * Mathf.Exp(-Mathf.Pow(phi / 0.4f, 2f));
            for (int j = 0; j < lon; j++)
            {
                float theta = Tau * j / lon;
                float radius = s * (0.92f + 0.12f * shoulder)
                             * (1f + 0.05f * Mathf.Cos(5f * theta) * (1f - shoulder));
                grid[i, j] = new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius) * 0.5f;
            }
        }

        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            if ((b - a).sqrMagnitude < 1e-10f || (c - a).sqrMagnitude < 1e-10f || (c - b).sqrMagnitude < 1e-10f) return;
            tris.Add(verts.Count); verts.Add(a);
            tris.Add(verts.Count); verts.Add(b);
            tris.Add(verts.Count); verts.Add(c);
        }
        for (int i = 0; i < lat; i++)
        for (int j = 0; j < lon; j++)
        {
            int k = (j + 1) % lon;
            Vector3 a = grid[i, j], b = grid[i, k], c = grid[i + 1, k], d = grid[i + 1, j];
            Tri(a, c, b);
            Tri(a, d, c);
        }

        mesh.Clear();
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        EditorUtility.SetDirty(mesh);
        return mesh;
    }

    // ---- fonts -----------------------------------------------------------

    /// <summary>A TMP font from the examples package, or TMP's default when the
    /// examples were never imported.</summary>
    public static TMP_FontAsset Font(string name)
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>($"{FontDir}/{name}.asset");
        return font != null ? font : TMP_Settings.defaultFontAsset;
    }

    /// <summary>Thick outline plus a dropped underlay, so a number stays legible
    /// over sky, grass and the boss's own coat alike.</summary>
    public static Material OutlinedFontMaterial(TMP_FontAsset font, string name, float outline)
    {
        ChibiSandboxSetup.EnsureFolderPublic(UiDir);
        var mat = LoadOrCreate($"{UiDir}/{name}.mat", font.material.shader);
        mat.CopyPropertiesFromMaterial(font.material);
        mat.SetFloat(ShaderUtilities.ID_FaceDilate, 0.1f);
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, outline);
        mat.SetColor(ShaderUtilities.ID_OutlineColor, new Color(0.08f, 0.04f, 0.06f, 1f));
        mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
        mat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.55f));
        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.35f);
        mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.6f);
        mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.3f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---- texture plumbing ------------------------------------------------

    const float Tau = Mathf.PI * 2f;

    static Color White(float alpha) => new(1f, 1f, 1f, Mathf.Clamp01(alpha));

    /// <summary>Normalised distance from the texture centre, 0 at centre, 1 at
    /// the edge midpoints. Takes pixel-centre coordinates.</summary>
    static float Radius(float x, float y, int size)
    {
        float h = size * 0.5f;
        float dx = (x - h) / h, dy = (y - h) / h;
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Signed distance in pixels to a rounded rectangle filling a
    /// w×h texture; negative inside.</summary>
    static float RoundBox(float x, float y, float w, float h, float r)
    {
        float qx = Mathf.Abs(x - w * 0.5f) - (w * 0.5f - r);
        float qy = Mathf.Abs(y - h * 0.5f) - (h * 0.5f - r);
        float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
    }

    /// <summary>One-pixel antialiased coverage from a pixel-space distance.</summary>
    static float Coverage(float d) => Mathf.Clamp01(0.5f - d);

    static Sprite WriteSprite(string path, int w, int h, Vector4 border, Func<float, float, Color> pixel)
    {
        Write(path, w, h, pixel);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite;
        ti.spriteImportMode = SpriteImportMode.Single;
        ti.spriteBorder = border;
        ti.spritePixelsPerUnit = 100f;
        ti.mipmapEnabled = false;
        ti.alphaIsTransparency = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        // Uncompressed: these are tiny, and block compression bands a gradient.
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        var settings = new TextureImporterSettings();
        ti.ReadTextureSettings(settings);
        // Full-rect: a tight mesh breaks both slicing and fill.
        settings.spriteMeshType = SpriteMeshType.FullRect;
        ti.SetTextureSettings(settings);
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static Texture2D WriteTexture(string path, int w, int h, bool repeatU, Func<float, float, Color> pixel)
    {
        Write(path, w, h, pixel);
        var ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = true;
        ti.wrapModeU = repeatU ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
        ti.wrapModeV = TextureWrapMode.Clamp;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static void Write(string path, int w, int h, Func<float, float, Color> pixel)
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
    }
}
