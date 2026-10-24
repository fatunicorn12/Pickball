using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pickleball.Sandbox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Tools ▸ Pickleball ▸ Setup World Look. Makes the World Labs academy and the
/// realtime characters read as one scene:
///
///  * The world gets <c>Pickball/World Unlit Shadowed</c>: the baked texture as-is,
///    darkened only where the sun is blocked, so characters cast shadows on it.
///    The world stops casting realtime shadows (its own are already baked).
///  * A 360° panorama of the world drives the skybox, the ambient light and the
///    reflections. It is captured from the player's spawn, or taken from a Marble
///    panorama export if one is dropped in Art/World with "pano" in its name.
///  * The sun is found in that panorama (its brightest patch) and the directional
///    light is aimed and tinted to match the baked lighting.
///  * Fog in the horizon colour hides the blobby far geometry, plus a global
///    post-processing volume (bloom, grading, vignette).
///
/// Safe to re-run, and independent of the chibi/Newton setups, so rebuilding
/// the sandbox only needs this re-run after. The post-processing profile is only
/// created once: tweak it live in Play mode and the tweaks are kept.
/// </summary>
public static class WorldLookSetup
{
    const string WorldDir = "Assets/_Project/Art/World";
    const string WorldName = "AcademyWorld";
    const string ShaderName = "Pickball/World Unlit Shadowed";
    const string MaterialPath = WorldDir + "/AcademyWorld_Shadowed.mat";
    const string CapturePanoPath = WorldDir + "/AcademyWorld_CapturedPano.png";
    const string SkyboxPath = WorldDir + "/AcademyWorld_Sky.mat";
    const string ProfilePath = WorldDir + "/AcademyWorld_PostFX.asset";
    const string VolumeName = "World Look Volume";

    const int CubeSize = 1024;
    const int PanoWidth = 2048;

    /// <summary>Clear colour for the capture. Anything still this colour is a hole
    /// in the Marble mesh, and is filled from the rest of its row.</summary>
    static readonly Color HoleColor = new(1f, 0f, 1f, 1f);

    /// <summary>The baked sky is soft, so the realtime sun is kept modest; the
    /// panorama ambient supplies the rest.</summary>
    const float SunIntensity = 1.1f;
    const float AmbientScale = 1.0f;
    const float FogStart = 30f;
    const float FogEnd = 180f;

    [MenuItem("Tools/Pickleball/Setup World Look")]
    public static void Run()
    {
        var world = GameObject.Find(WorldName);
        if (world == null)
        {
            Debug.LogError($"No '{WorldName}' in the open scene — run Setup Chibi Sandbox first.");
            return;
        }

        var shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError($"Shader '{ShaderName}' not found.");
            return;
        }

        ApplyWorldMaterial(world, shader);

        Vector3 eye = Object.FindFirstObjectByType<ChibiLocomotion>() is { } player
            ? player.transform.position + Vector3.up * 1.6f
            : WorldBounds(world).center;

        var (panoPath, pano) = LoadOrCapturePanorama(world, eye);
        var sky = Analyse(pano);
        Object.DestroyImmediate(pano);

        var cube = ImportAsCubemap(panoPath);
        ApplySkyAndAmbient(cube, sky);
        ApplySun(sky);
        ApplyFog(sky.Haze);
        ApplyPostProcessing();

        EditorSceneManager.MarkSceneDirty(world.scene);
        Debug.Log($"World look applied from {panoPath}. Sun dir {sky.SunDirection:F3} " +
                  $"(found: {sky.SunFound}), ambient {sky.Sky}/{sky.Horizon}/{sky.Ground}, haze {sky.Haze}.");
    }

    // ── World material ──────────────────────────────────────────────────────

    static void ApplyWorldMaterial(GameObject world, Shader shader)
    {
        var renderers = world.GetComponentsInChildren<MeshRenderer>();
        var current = renderers.Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            mat = new Material(shader) { name = "AcademyWorld_Shadowed" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        mat.shader = shader;

        // First run takes the texture from glTFast's material; re-runs keep ours.
        if (current != null && current != mat && current.HasProperty("baseColorTexture"))
        {
            mat.SetTexture("_BaseMap", current.GetTexture("baseColorTexture"));
            mat.SetTextureScale("_BaseMap", current.GetTextureScale("baseColorTexture"));
            mat.SetTextureOffset("_BaseMap", current.GetTextureOffset("baseColorTexture"));
        }
        if (mat.GetTexture("_BaseMap") == null)
            Debug.LogWarning("World material has no baked texture — assign _BaseMap by hand.");
        EditorUtility.SetDirty(mat);

        foreach (var r in renderers)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = true;
        }
    }

    static Bounds WorldBounds(GameObject world)
    {
        var rs = world.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ── Panorama ────────────────────────────────────────────────────────────

    /// <summary>A Marble panorama export wins over a capture: it has the real sky
    /// behind the mesh's holes and none of the mesh's blur.</summary>
    static (string path, Texture2D pixels) LoadOrCapturePanorama(GameObject world, Vector3 eye)
    {
        string marble = Directory.GetFiles(WorldDir)
            .Select(p => p.Replace('\\', '/'))
            .Where(p => p != CapturePanoPath && Path.GetFileName(p).ToLowerInvariant().Contains("pano"))
            .FirstOrDefault(p => p.EndsWith(".png") || p.EndsWith(".jpg") || p.EndsWith(".jpeg"));
        if (marble != null)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            tex.LoadImage(File.ReadAllBytes(marble));
            return (marble, tex);
        }

        var pano = CapturePanorama(world, eye);
        File.WriteAllBytes(CapturePanoPath, pano.EncodeToPNG());
        AssetDatabase.ImportAsset(CapturePanoPath, ImportAssetOptions.ForceUpdate);
        return (CapturePanoPath, pano);
    }

    static Texture2D CapturePanorama(GameObject world, Vector3 eye)
    {
        // Only the world: hide every other renderer (the player stands right
        // where the camera is) and fog, which would tint the capture.
        var hidden = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .Where(r => !r.transform.IsChildOf(world.transform) && !r.forceRenderingOff).ToList();
        foreach (var r in hidden) r.forceRenderingOff = true;
        bool fog = RenderSettings.fog;
        RenderSettings.fog = false;

        var camGo = new GameObject("World Pano Capture") { hideFlags = HideFlags.HideAndDontSave };
        // URP's render graph rejects a depthless target, and a cube render
        // request gets one, so each face renders to a 2D target with depth and
        // is copied in. Cube faces are stored upside down relative to a camera
        // render, hence the flip.
        var face = new RenderTexture(CubeSize, CubeSize, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var flipped = new RenderTexture(CubeSize, CubeSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        var cube = new RenderTexture(CubeSize, CubeSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            { dimension = TextureDimension.Cube };
        var equirect = new RenderTexture(PanoWidth, PanoWidth / 2, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        try
        {
            camGo.transform.position = eye;
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.fieldOfView = 90f;
            cam.aspect = 1f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 2000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = HoleColor;
            cam.allowMSAA = false;
            cam.allowHDR = false;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.antialiasing = AntialiasingMode.None;
            data.renderShadows = false;

            foreach (var (cubeFace, forward, up) in CubeFaces)
            {
                cam.transform.rotation = Quaternion.LookRotation(forward, up);
                RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = face });
                Graphics.Blit(face, flipped, new Vector2(1f, -1f), new Vector2(0f, 1f));
                Graphics.CopyTexture(flipped, 0, 0, cube, (int)cubeFace, 0);
            }

            cube.ConvertToEquirect(equirect);
            RenderTexture.active = equirect;
            var pano = new Texture2D(equirect.width, equirect.height, TextureFormat.RGBA32, false);
            pano.ReadPixels(new Rect(0, 0, equirect.width, equirect.height), 0, 0);
            pano.Apply();

            FillHoles(pano);
            return pano;
        }
        finally
        {
            RenderTexture.active = null;
            foreach (var r in hidden) r.forceRenderingOff = false;
            RenderSettings.fog = fog;
            Object.DestroyImmediate(camGo);
            foreach (var rt in new[] { face, flipped, cube, equirect })
            {
                rt.Release();
                Object.DestroyImmediate(rt);
            }
        }
    }

    static readonly (CubemapFace face, Vector3 forward, Vector3 up)[] CubeFaces =
    {
        (CubemapFace.PositiveX, Vector3.right, Vector3.up),
        (CubemapFace.NegativeX, Vector3.left, Vector3.up),
        (CubemapFace.PositiveY, Vector3.up, Vector3.back),
        (CubemapFace.NegativeY, Vector3.down, Vector3.forward),
        (CubemapFace.PositiveZ, Vector3.forward, Vector3.up),
        (CubemapFace.NegativeZ, Vector3.back, Vector3.up),
    };

    static bool IsHole(Color c) => Mathf.Min(c.r, c.b) - c.g > 0.35f;

    /// <summary>Holes in the Marble mesh become the average of the rest of their
    /// row — at sky height that is sky, at the horizon the distant haze.</summary>
    static void FillHoles(Texture2D pano)
    {
        int w = pano.width, h = pano.height;
        var px = pano.GetPixels();
        var rowFill = new Color?[h];
        for (int y = 0; y < h; y++)
        {
            Color sum = default; int n = 0;
            for (int x = 0; x < w; x++)
            {
                var c = px[y * w + x];
                if (!IsHole(c)) { sum += c; n++; }
            }
            if (n > w / 50) rowFill[y] = sum / n;
        }
        // Rows that are almost all hole (e.g. straight up) borrow the nearest row.
        for (int y = 0; y < h; y++)
        {
            if (rowFill[y].HasValue) continue;
            for (int d = 1; d < h; d++)
            {
                if (y + d < h && rowFill[y + d].HasValue) { rowFill[y] = rowFill[y + d]; break; }
                if (y - d >= 0 && rowFill[y - d].HasValue) { rowFill[y] = rowFill[y - d]; break; }
            }
        }
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            int i = y * w + x;
            if (IsHole(px[i])) px[i] = rowFill[y] ?? Color.gray;
            px[i].a = 1f;
        }
        pano.SetPixels(px);
        pano.Apply();
    }

    static Cubemap ImportAsCubemap(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureShape = TextureImporterShape.TextureCube;
        importer.generateCubemap = TextureImporterGenerateCubemap.AutoCubemap;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Cubemap>(path);
    }

    // ── Panorama analysis ───────────────────────────────────────────────────

    struct SkyInfo
    {
        public Color Sky, Horizon, Ground;   // gamma space, for RenderSettings
        public Color Haze;                    // sky just above the horizon, for fog
        public Vector3 SunDirection;          // towards the sun
        public Color SunColor;
        public bool SunFound;
    }

    /// <summary>Direction for an equirect pixel, matching ConvertToEquirect and
    /// Unity's latitude-longitude cubemap import (centre column = +Z).</summary>
    static Vector3 PanoDirection(float u, float v)
    {
        float lon = (u - 0.5f) * 2f * Mathf.PI;
        float lat = (v - 0.5f) * Mathf.PI;
        return new Vector3(Mathf.Sin(lon) * Mathf.Cos(lat), Mathf.Sin(lat), Mathf.Cos(lon) * Mathf.Cos(lat));
    }

    static SkyInfo Analyse(Texture2D pano)
    {
        int w = pano.width, h = pano.height;
        var px = pano.GetPixels();   // row 0 = bottom
        Vector3 sky = default, horizon = default, ground = default, haze = default;
        float skyW = 0, horW = 0, gndW = 0, hazeW = 0;
        var upper = new List<(float lum, int i)>();

        for (int y = 0; y < h; y += 2)
        {
            float lat = ((y + 0.5f) / h - 0.5f) * 180f;
            float weight = Mathf.Cos(lat * Mathf.Deg2Rad);
            for (int x = 0; x < w; x += 2)
            {
                var c = px[y * w + x].linear;
                var v = new Vector3(c.r, c.g, c.b) * weight;
                if (lat > 20f) { sky += v; skyW += weight; }
                else if (lat < -20f) { ground += v; gndW += weight; }
                else { horizon += v; horW += weight; }
                // Fog should be the haze, not the grass and trees on the horizon:
                // only sky-coloured (blue-dominant) pixels just above it count.
                if (lat > 0f && lat < 30f && c.b > c.r && c.b > c.g) { haze += v; hazeW += weight; }
                if (lat > 5f) upper.Add((c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f, y * w + x));
            }
        }

        static Color Gamma(Vector3 v) => new Color(v.x, v.y, v.z).gamma;
        var info = new SkyInfo
        {
            Sky = Gamma(sky / Mathf.Max(skyW, 1e-4f) * AmbientScale),
            Horizon = Gamma(horizon / Mathf.Max(horW, 1e-4f) * AmbientScale),
            Ground = Gamma(ground / Mathf.Max(gndW, 1e-4f) * AmbientScale),
        };
        info.Haze = hazeW > 1f ? Gamma(haze / hazeW) : info.Sky;

        // The sun is the brightest patch above the horizon; only trust it if it
        // clearly stands out from the median sky.
        upper.Sort((a, b) => b.lum.CompareTo(a.lum));
        int top = Mathf.Max(1, upper.Count / 500);
        float median = upper[upper.Count / 2].lum;
        Vector3 dir = default; Color col = default;
        for (int k = 0; k < top; k++)
        {
            int i = upper[k].i;
            dir += PanoDirection((i % w + 0.5f) / w, (i / w + 0.5f) / h);
            col += px[i].linear;
        }
        info.SunFound = upper[top - 1].lum > median * 2f;
        info.SunDirection = dir.normalized;
        col /= top;
        info.SunColor = (col / Mathf.Max(col.maxColorComponent, 1e-4f)).gamma;
        return info;
    }

    // ── Scene settings ──────────────────────────────────────────────────────

    static void ApplySkyAndAmbient(Cubemap cube, SkyInfo sky)
    {
        var skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxPath);
        if (skybox == null)
        {
            skybox = new Material(Shader.Find("Skybox/Cubemap")) { name = "AcademyWorld_Sky" };
            AssetDatabase.CreateAsset(skybox, SkyboxPath);
        }
        skybox.SetTexture("_Tex", cube);
        EditorUtility.SetDirty(skybox);
        RenderSettings.skybox = skybox;

        // Trilight rather than skybox ambient: it lives in the scene itself, so
        // it needs no lighting bake and is identical in builds.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = sky.Sky;
        RenderSettings.ambientEquatorColor = sky.Horizon;
        RenderSettings.ambientGroundColor = sky.Ground;

        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = cube;
        RenderSettings.reflectionIntensity = 1f;

        var cam = Camera.main;
        if (cam != null) cam.clearFlags = CameraClearFlags.Skybox;
    }

    static void ApplySun(SkyInfo sky)
    {
        var sun = RenderSettings.sun;
        if (sun == null)
            sun = Object.FindObjectsByType<Light>(FindObjectsSortMode.None)
                .FirstOrDefault(l => l.type == LightType.Directional);
        if (sun == null)
        {
            sun = new GameObject("Directional Light").AddComponent<Light>();
            sun.type = LightType.Directional;
        }

        if (sky.SunFound)
        {
            // Never let a low sun skim the ground: long grazing shadows look wrong
            // against a soft bake. Keep at least 25 degrees of elevation.
            var d = sky.SunDirection;
            var flat = new Vector3(d.x, 0f, d.z).normalized;
            float elev = Mathf.Max(Mathf.Asin(Mathf.Clamp(d.y, -1f, 1f)), 25f * Mathf.Deg2Rad);
            d = flat * Mathf.Cos(elev) + Vector3.up * Mathf.Sin(elev);
            sun.transform.rotation = Quaternion.LookRotation(-d);
            sun.color = sky.SunColor;
        }
        else
        {
            Debug.LogWarning("No clear sun in the panorama — left the light's direction alone.");
        }

        sun.intensity = SunIntensity;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 1f;
        RenderSettings.sun = sun;
        EditorUtility.SetDirty(sun);
    }

    static void ApplyFog(Color horizon)
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = horizon;
        RenderSettings.fogStartDistance = FogStart;
        RenderSettings.fogEndDistance = FogEnd;
    }

    static void ApplyPostProcessing()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);

            // No tonemapping: the world texture is already display-ready, and any
            // curve would flatten it. These only nudge it.
            var bloom = Add<Bloom>(profile);
            bloom.threshold.Override(0.9f);
            bloom.intensity.Override(0.35f);
            bloom.scatter.Override(0.65f);

            var grade = Add<ColorAdjustments>(profile);
            grade.contrast.Override(10f);
            grade.saturation.Override(12f);

            var white = Add<WhiteBalance>(profile);
            white.temperature.Override(6f);

            var vignette = Add<Vignette>(profile);
            vignette.intensity.Override(0.22f);
            vignette.smoothness.Override(0.45f);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        var go = GameObject.Find(VolumeName);
        if (go == null) go = new GameObject(VolumeName);
        var volume = go.GetComponent<Volume>();
        if (volume == null) volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 1f;
        volume.sharedProfile = profile;

        var cam = Camera.main;
        if (cam != null) cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
    }

    static T Add<T>(VolumeProfile profile) where T : VolumeComponent
    {
        var c = profile.Add<T>();
        c.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(c, profile);
        return c;
    }
}
