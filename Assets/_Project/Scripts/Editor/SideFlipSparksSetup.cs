using Pickleball.Sandbox.Movement;
using Pickleball.Sandbox.Movement.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Builds the side flip's sparks on the player: a <see cref="SideFlipSparks"/>
/// child with its particle system and alpha-blended material.
///
/// Tools ▸ Pickleball ▸ Setup Side Flip Sparks edits Chibi.prefab alone, so every
/// scene's player picks it up without a scene save, and it does not rebuild the
/// Animator the way Setup Movement does. <see cref="MovementSetup.WirePlayer"/>
/// also calls <see cref="Wire"/>, so a Setup Chibi Sandbox rebuild keeps it.
/// Idempotent: the child is reused and every setting rewritten. The spark's look
/// lives here; how many sparks and where they start is on MovementAnimationConfig.
/// </summary>
public static class SideFlipSparksSetup
{
    const string PrefabPath = "Assets/_Project/Prefabs/Chibi.prefab";
    const string VfxDir = "Assets/_Project/Art/VFX";
    const string MaterialPath = VfxDir + "/SideFlipSpark.mat";
    const string TexturePath = VfxDir + "/SoftDot.png";
    const string ChildName = "SideFlipSparks";

    [MenuItem("Tools/Pickleball/Setup Side Flip Sparks")]
    public static void Run()
    {
        MovementSetup.EnsureConfigs(out _, out var anim);
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Wire(root, anim);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }
        AssetDatabase.SaveAssets();
        Debug.Log("Side flip sparks set up on Chibi.prefab.");
    }

    /// <summary>Adds or updates the sparks child on a player root and wires it.</summary>
    public static void Wire(GameObject player, MovementAnimationConfig anim)
    {
        var child = player.transform.Find(ChildName);
        if (child == null)
        {
            child = new GameObject(ChildName).transform;
            child.SetParent(player.transform, false);
        }
        var go = child.gameObject;
        var system = go.TryGetComponent<ParticleSystem>(out var ps) ? ps : go.AddComponent<ParticleSystem>();
        ConfigureParticles(system);

        var sparks = go.TryGetComponent<SideFlipSparks>(out var s) ? s : go.AddComponent<SideFlipSparks>();
        var so = new SerializedObject(sparks);
        so.FindProperty("motor").objectReferenceValue = player.GetComponent<MovementMotor>();
        so.FindProperty("config").objectReferenceValue = anim;
        so.FindProperty("animator").objectReferenceValue = player.GetComponentInChildren<Animator>(true);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureParticles(ParticleSystem ps)
    {
        // Must be stopped before editing, or Unity warns about live edits.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.duration = 1f;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.035f, 0.08f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.95f, 0.75f), new Color(1f, 0.75f, 0.45f));
        main.gravityModifier = 1.3f;
        // World space: thrown sparks stay behind as he runs off the other way.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 200;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;
        emission.SetBursts(new ParticleSystem.Burst[0]);

        // A cone along the transform's Z, which SideFlipSparks aims up the spray.
        // Wide on purpose: reversing straight back toward the chase camera puts his
        // body between it and the spot, and a narrow spray stays hidden behind him.
        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 50f;
        shape.radius = 0.05f;
        shape.radiusThickness = 1f;
        shape.position = Vector3.zero;
        shape.rotation = Vector3.zero;

        // Hot to cold, and gone before most of them reach the floor.
        var colour = ps.colorOverLifetime;
        colour.enabled = true;
        var fade = new Gradient();
        fade.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(new Color(1f, 0.62f, 0.3f), 0.45f),
                new GradientColorKey(new Color(1f, 0.25f, 0.08f), 1f),
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.55f),
                new GradientAlphaKey(0f, 1f),
            });
        colour.color = new ParticleSystem.MinMaxGradient(fade);

        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.35f));

        // Streaks, not dots: the length comes from each spark's own speed.
        var renderer = ps.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.05f;
        renderer.lengthScale = 2f;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.sharedMaterial = SparkMaterial();
    }

    /// <summary>URP Particles/Unlit, alpha blended. Additive washes out to nothing on
    /// the pale floors: a floor already near white has no room left to add to.
    /// Blended, the colour holds on any floor, and over 1 it still blooms on a dark
    /// one. Blend state is written by hand: a material made from script never passes
    /// through the URP inspector that would normally derive it. Red stays well above
    /// the others, and blue near zero, so HDR clips a spark to orange, not white.</summary>
    static Material SparkMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null)
        {
            ChibiSandboxSetup.EnsureFolderPublic(VfxDir);
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
        mat.SetColor("_BaseColor", new Color(2.4f, 1.05f, 0.18f, 1f));
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_Cull", (float)CullMode.Off);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
