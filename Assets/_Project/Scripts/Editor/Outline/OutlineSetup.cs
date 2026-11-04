using Pickleball.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Pickleball.EditorTools.Outline
{
    /// <summary>
    /// Tools ▸ Pickleball ▸ Setup Outline. Turns the screen outline on across the whole game by adding
    /// its feature to both project renderers. Running it again changes nothing: an existing feature is
    /// kept, along with its on/off tick, and the settings asset is never overwritten.
    /// </summary>
    public static class OutlineSetup
    {
        /// <summary>The project's renderers: the only ones the two render pipeline assets list.</summary>
        public static readonly string[] RendererPaths =
        {
            "Assets/Settings/PC_Renderer.asset",
            "Assets/Settings/Mobile_Renderer.asset",
        };

        const string SettingsFolder = "Assets/_Project/ScriptableObjects/Rendering";
        const string SettingsPath = SettingsFolder + "/OutlineSettings.asset";
        const string ShaderPath = "Assets/_Project/Shaders/Outline/ScreenOutline.shader";
        const string FeatureName = "ScreenOutline";

        /// <summary>The menu entry.</summary>
        [MenuItem("Tools/Pickleball/Setup Outline")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Leave Play mode before setting up the outline.");
                return;
            }
            Build();
        }

        /// <summary>Does the setup with no prompts.</summary>
        /// <returns>False if it failed; the reason is in the log.</returns>
        public static bool Build()
        {
            var settings = LoadOrCreateSettings();
            if (settings == null) return false;

            foreach (string path in RendererPaths)
            {
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (renderer == null)
                {
                    Debug.LogError($"{path} not found, or it isn't a Universal Renderer.");
                    return false;
                }
                AddFeature(renderer, settings);
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Outline is on in {RendererPaths.Length} renderers. Tune {SettingsPath}.");
            return true;
        }

        /// <summary>The shared look asset, created with its shader on first use.</summary>
        /// <returns>Null if the shader is missing.</returns>
        static OutlineSettings LoadOrCreateSettings()
        {
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
            if (shader == null)
            {
                Debug.LogError($"{ShaderPath} not found.");
                return null;
            }

            var settings = AssetDatabase.LoadAssetAtPath<OutlineSettings>(SettingsPath);
            if (settings != null) return settings;

            if (!AssetDatabase.IsValidFolder(SettingsFolder))
                AssetDatabase.CreateFolder("Assets/_Project/ScriptableObjects", "Rendering");
            settings = ScriptableObject.CreateInstance<OutlineSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            var serialized = new SerializedObject(settings);
            serialized.FindProperty("_shader").objectReferenceValue = shader;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return settings;
        }

        static void AddFeature(UniversalRendererData renderer, OutlineSettings settings)
        {
            foreach (var existing in renderer.rendererFeatures)
                if (existing is OutlineRendererFeature) return;

            var feature = ScriptableObject.CreateInstance<OutlineRendererFeature>();
            feature.name = FeatureName;
            var serializedFeature = new SerializedObject(feature);
            serializedFeature.FindProperty("_settings").objectReferenceValue = settings;
            serializedFeature.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.AddObjectToAsset(feature, renderer);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            // The renderer keeps a list of its features and a parallel map of their file IDs, and
            // treats the list as broken if the two disagree.
            var serializedRenderer = new SerializedObject(renderer);
            var features = serializedRenderer.FindProperty("m_RendererFeatures");
            var map = serializedRenderer.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
        }
    }
}
