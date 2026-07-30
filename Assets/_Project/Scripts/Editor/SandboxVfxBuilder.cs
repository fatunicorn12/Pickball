using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds the double-jump cloud puff from scratch — a soft radial sprite, a URP
/// particle material and a configured ParticleSystem prefab.
///
/// Generated rather than downloaded so the effect has no external dependency
/// and rebuilds identically on any machine. The shape is the Terraria
/// cloud-in-a-bottle beat: a burst that expands outward and fades fast, sitting
/// where the feet left off rather than trailing the character.
/// </summary>
public static class SandboxVfxBuilder
{
    public static ParticleSystem BuildCloudPuff(string vfxDir, string prefabPath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null) return existing.GetComponent<ParticleSystem>();

        var tex = BuildSoftDot(vfxDir);
        var mat = BuildParticleMaterial(vfxDir, tex);

        var go = new GameObject("CloudPuff");
        var ps = go.AddComponent<ParticleSystem>();
        // Must be stopped before editing, or Unity warns about live edits.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.duration = 0.6f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.4f, 2.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.05f;      // drifts up a touch, like dust
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 60;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18, 22) });

        // Flat ring at the feet rather than a sphere, so it reads as a
        // shockwave off the ground instead of an explosion around the body.
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.18f;
        shape.radiusThickness = 0.4f;
        shape.rotation = new Vector3(90f, 0f, 0f);

        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        var sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.35f);
        sizeCurve.AddKey(0.25f, 1f);
        sizeCurve.AddKey(1f, 0.15f);
        sol.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.92f, 1f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(grad);

        var vol = ps.velocityOverLifetime;
        vol.enabled = true;
        vol.space = ParticleSystemSimulationSpace.Local;
        vol.radial = new ParticleSystem.MinMaxCurve(0.6f);

        var limit = ps.limitVelocityOverLifetime;
        limit.enabled = true;
        limit.dampen = 0.35f;               // puff stalls instead of flying off
        limit.limit = new ParticleSystem.MinMaxCurve(3f);

        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-90f * Mathf.Deg2Rad, 90f * Mathf.Deg2Rad);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = mat;
        renderer.sortingFudge = -2f;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        Directory.CreateDirectory(Path.GetDirectoryName(prefabPath)!);
        var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);
        Debug.Log($"Built cloud puff VFX -> {prefabPath}");
        return prefab.GetComponent<ParticleSystem>();
    }

    /// <summary>
    /// Builds the protractor arc: a filled 0-45° annulus sector with tick marks
    /// every 15°, plus a baseline along the ground and a ray up the 45° line.
    /// Generated as a mesh rather than a texture so it stays crisp at any size
    /// and needs no art pass.
    /// </summary>
    public static Mesh BuildProtractorArc(string vfxDir, float innerRadius = 0.55f,
                                          float outerRadius = 1.0f, float sweepDegrees = 45f)
    {
        string path = $"{vfxDir}/ProtractorArc.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null) return existing;

        var verts = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        var uvs = new System.Collections.Generic.List<Vector2>();

        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0));
            uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
            tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
        }

        // Arc band, in the XY plane so it can be rotated into the travel plane.
        const int segments = 48;
        for (int s = 0; s < segments; s++)
        {
            float a0 = Mathf.Deg2Rad * (sweepDegrees * s / segments);
            float a1 = Mathf.Deg2Rad * (sweepDegrees * (s + 1) / segments);
            Vector3 i0 = new(Mathf.Cos(a0) * innerRadius, Mathf.Sin(a0) * innerRadius, 0f);
            Vector3 o0 = new(Mathf.Cos(a0) * outerRadius, Mathf.Sin(a0) * outerRadius, 0f);
            Vector3 i1 = new(Mathf.Cos(a1) * innerRadius, Mathf.Sin(a1) * innerRadius, 0f);
            Vector3 o1 = new(Mathf.Cos(a1) * outerRadius, Mathf.Sin(a1) * outerRadius, 0f);
            Quad(i0, o0, o1, i1);
        }

        // Tick marks every 15°, longer at 0 and at the sweep end.
        for (float deg = 0f; deg <= sweepDegrees + 0.01f; deg += 15f)
        {
            float a = Mathf.Deg2Rad * deg;
            bool major = Mathf.Approximately(deg, 0f) || Mathf.Approximately(deg, sweepDegrees);
            float len = major ? 0.22f : 0.12f;
            float half = Mathf.Deg2Rad * (major ? 1.2f : 0.7f);
            Vector3 dir = new(Mathf.Cos(a), Mathf.Sin(a), 0f);
            Vector3 perp = new(-Mathf.Sin(a), Mathf.Cos(a), 0f);
            Vector3 baseP = dir * outerRadius;
            Vector3 tipP = dir * (outerRadius + len);
            float w = half * outerRadius;
            Quad(baseP - perp * w, baseP + perp * w, tipP + perp * w, tipP - perp * w);
        }

        // Baseline along the ground and the ray up the dash line.
        void Spoke(float deg, float thickness)
        {
            float a = Mathf.Deg2Rad * deg;
            Vector3 dir = new(Mathf.Cos(a), Mathf.Sin(a), 0f);
            Vector3 perp = new(-Mathf.Sin(a), Mathf.Cos(a), 0f);
            Quad(dir * 0.05f - perp * thickness, dir * (outerRadius + 0.28f) - perp * thickness,
                 dir * (outerRadius + 0.28f) + perp * thickness, dir * 0.05f + perp * thickness);
        }
        Spoke(0f, 0.012f);
        Spoke(sweepDegrees, 0.012f);

        var mesh = new Mesh { name = "ProtractorArc" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        Directory.CreateDirectory(vfxDir);
        AssetDatabase.CreateAsset(mesh, path);
        Debug.Log($"Built protractor arc mesh ({verts.Count} verts) -> {path}");
        return mesh;
    }

    /// <summary>Unlit additive material for arc and afterimages.</summary>
    public static Material BuildGlowMaterial(string dir, string name, Color tint)
    {
        string path = $"{dir}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        var mat = new Material(shader);
        mat.SetColor("_BaseColor", tint);
        // Transparent + additive so ghosts brighten rather than occlude.
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 1f);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.renderQueue = 3100;
        Directory.CreateDirectory(dir);
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }

    /// <summary>A 128px soft radial dot. Generated so the effect carries no
    /// texture dependency of its own.</summary>
    static Texture2D BuildSoftDot(string dir)
    {
        string path = $"{dir}/SoftDot.png";
        var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (existing != null) return existing;

        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var centre = new Vector2(size * 0.5f, size * 0.5f);
        float maxDist = size * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre) / maxDist;
            // smoothstep falloff — a hard circle looks like confetti, not cloud
            float a = Mathf.Clamp01(1f - d);
            a = a * a * (3f - 2f * a);
            tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
        }
        tex.Apply();

        Directory.CreateDirectory(dir);
        File.WriteAllBytes(path.Replace("Assets", Application.dataPath), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);

        if (AssetImporter.GetAtPath(path) is TextureImporter ti)
        {
            ti.alphaIsTransparency = true;
            ti.wrapMode = TextureWrapMode.Clamp;
            ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Material BuildParticleMaterial(string dir, Texture2D tex)
    {
        string path = $"{dir}/CloudPuff.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        // URP's particle shader — the built-in Default-Particle material renders
        // magenta under URP.
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Universal Render Pipeline/Unlit");
        var mat = new Material(shader);
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", Color.white);
        // Additive-ish soft blend so it glows without a hard edge.
        mat.SetFloat("_Surface", 1f);           // transparent
        mat.SetFloat("_Blend", 0f);             // alpha
        mat.renderQueue = 3000;
        AssetDatabase.CreateAsset(mat, path);
        return mat;
    }
}
