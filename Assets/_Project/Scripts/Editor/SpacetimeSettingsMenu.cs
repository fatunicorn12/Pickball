using Pickleball.Sandbox;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools ▸ Pickleball ▸ Spacetime ▸ Enable / Disable. The one place the 4D
/// worldline feature is switched on and off.
///
/// Both items flip <see cref="SpacetimeSettings.Enabled"/> on the shared
/// settings asset, save it and log the new state. The asset ships switched
/// off. Flipping it during Play mode takes effect on the next frame: the input
/// binding appears or disappears, and a view in 4D drops back to 3D.
/// </summary>
public static class SpacetimeSettingsMenu
{
    public const string SettingsPath = "Assets/_Project/ScriptableObjects/Spacetime/SpacetimeSettings.asset";
    public const string MaterialPath = "Assets/_Project/Art/Spacetime/Worldline.mat";

    const string EnableItem = "Tools/Pickleball/Spacetime/Enable";
    const string DisableItem = "Tools/Pickleball/Spacetime/Disable";

    [MenuItem(EnableItem, priority = 0)]
    public static void Enable() => SetEnabled(true);

    [MenuItem(EnableItem, true)]
    static bool CanEnable() => !IsEnabled();

    [MenuItem(DisableItem, priority = 1)]
    public static void Disable() => SetEnabled(false);

    [MenuItem(DisableItem, true)]
    static bool CanDisable() => IsEnabled();

    /// <summary>Whether the feature is switched on. A missing asset counts as off.</summary>
    public static bool IsEnabled()
    {
        var settings = AssetDatabase.LoadAssetAtPath<SpacetimeSettings>(SettingsPath);
        return settings != null && settings.Enabled;
    }

    static void SetEnabled(bool on)
    {
        var settings = LoadOrCreateSettings();
        // Through SerializedObject, like the Inspector, so the change is undoable
        // and marks the asset dirty.
        var so = new SerializedObject(settings);
        so.FindProperty("enabled").boolValue = on;
        so.ApplyModifiedProperties();
        AssetDatabase.SaveAssetIfDirty(settings);

        Debug.Log(on
            ? "Spacetime is ON. T / d-pad left flips into 4D in any scene with a " +
              "SpacetimeView (Tools ▸ Pickleball ▸ Spacetime ▸ Add To Sandbox wires one into the open scene)."
            : "Spacetime is OFF. No input binding, no recording, no beams.", settings);
    }

    /// <summary>
    /// The settings asset, created switched off (with its beam material) if it
    /// does not exist yet. Every Spacetime menu wires this one asset, so the
    /// switch on it governs every scene.
    /// </summary>
    public static SpacetimeSettings LoadOrCreateSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<SpacetimeSettings>(SettingsPath);
        if (settings != null) return settings;

        EnsureFolder("Assets/_Project/ScriptableObjects/Spacetime");
        settings = ScriptableObject.CreateInstance<SpacetimeSettings>();
        AssetDatabase.CreateAsset(settings, SettingsPath);

        var so = new SerializedObject(settings);
        so.FindProperty("worldlineMaterial").objectReferenceValue = LoadOrCreateMaterial();
        so.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log($"Created {SettingsPath} (Spacetime is OFF).", settings);
        return settings;
    }

    /// <summary>The beam material as an asset, so the shader it uses is
    /// referenced and ships in builds.</summary>
    static Material LoadOrCreateMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat != null) return mat;

        // URP's particle shader, because it multiplies in vertex colour and the
        // past-to-present gradient lives in the vertex colours.
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) return null;
        EnsureFolder("Assets/_Project/Art/Spacetime");
        mat = new Material(shader) { name = "Worldline" };
        mat.SetColor("_BaseColor", Color.white);
        AssetDatabase.CreateAsset(mat, MaterialPath);
        return mat;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/');
        string parent = path.Substring(0, slash);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
    }
}
