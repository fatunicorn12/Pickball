using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.EditorTools.WorldPaint
{
    /// <summary>
    /// Tools ▸ Pickleball ▸ Trials ▸ Build Courtyard Texture Test. Builds
    /// <c>Scenes/Trials/CourtyardTextureTest.unity</c>: a copy of AcademyGreybox with the grid swapped
    /// for painted materials, to judge the look of <c>Docs/Art/WorldTextures.md</c> in the game.
    ///
    /// The copy is the point: AcademyGreybox, its materials and its setup script are never touched,
    /// so the test can be thrown away. The scene is rebuilt on every run, so its GUID changes and hand
    /// edits to it are lost. Tune the <c>Painted_*</c> material assets instead, which a rebuild leaves
    /// alone. The materials swap everywhere they're used, so the whole map gets the look, but the
    /// courtyard is what was composed for it.
    /// </summary>
    public static class CourtyardTextureTestSetup
    {
        const string SourceScenePath = "Assets/_Project/Scenes/AcademyGreybox.unity";
        /// <summary>The generated trial scene.</summary>
        public const string ScenePath = "Assets/_Project/Scenes/Trials/CourtyardTextureTest.unity";
        const string TrialsFolder = "Assets/_Project/Scenes/Trials";
        const string GreyboxRootName = "AcademyGreybox";
        const string SignsName = "Signs";

        const int TextureMaxSize = 1024;
        /// <summary>Without it, ground seen at the game's low camera angle smears into streaks.</summary>
        const int AnisoLevel = 4;
        static readonly string[] MobilePlatforms = { "Android", "iOS" };

        /// <summary>The menu entry.</summary>
        [MenuItem("Tools/Pickleball/Trials/Build Courtyard Texture Test")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Leave Play mode before building the texture test.");
                return;
            }
            // Never silently discard unsaved work in whatever scene is open.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Build();
        }

        /// <summary>Does the build with no prompts.</summary>
        /// <returns>False if it failed; the reason is in the log.</returns>
        public static bool Build()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
            {
                Debug.LogError($"{SourceScenePath} not found. Run Tools ▸ Pickleball ▸ Build Academy Greybox first.");
                return false;
            }
            if (Shader.Find(WorldPaintCatalog.ShaderName) == null)
            {
                Debug.LogError($"Shader '{WorldPaintCatalog.ShaderName}' not found; is Art/World/Painted imported?");
                return false;
            }

            var map = BuildMaterialMap();
            if (map == null) return false;

            if (!AssetDatabase.IsValidFolder(TrialsFolder))
                AssetDatabase.CreateFolder("Assets/_Project/Scenes", "Trials");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) AssetDatabase.DeleteAsset(ScenePath);
            if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
            {
                Debug.LogError($"Could not copy {SourceScenePath} to {ScenePath}.");
                return false;
            }

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            // A Library seeded from another worktree can leave scripts unbound. The scene would
            // still save, but as a copy with no working player.
            int missing = CountMissingScripts(scene);
            if (missing > 0)
            {
                Debug.LogError($"{ScenePath} opened with {missing} missing scripts; not saving it. " +
                               "Recompile with a clean build cache, then run this again.");
                return false;
            }
            var renderers =Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int replaced = WorldPaintSwap.Apply(renderers, map);
            if (replaced == 0)
            {
                Debug.LogError($"No renderer in {SourceScenePath} uses a greybox material; nothing was painted.");
                return false;
            }

            HideSigns(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError($"Could not save {ScenePath}.");
                return false;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"Courtyard texture test built: {ScenePath}. Replaced {replaced} material slots.");
            return true;
        }

        static int CountMissingScripts(UnityEngine.SceneManagement.Scene scene)
        {
            int missing = 0;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject);
            return missing;
        }

        /// <summary>The floating region signs are for planning, and they'd sit in every shot.</summary>
        static void HideSigns(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name != GreyboxRootName) continue;
                var signs = root.transform.Find(SignsName);
                if (signs != null) signs.gameObject.SetActive(false);
            }
        }

        /// <summary>Pairs each greybox material with its painted one, making any that don't exist yet.</summary>
        /// <returns>The map, or null if a greybox material is missing.</returns>
        static Dictionary<Material, Material> BuildMaterialMap()
        {
            var map = new Dictionary<Material, Material>();
            foreach (var surface in WorldPaintCatalog.Surfaces)
            {
                string path = $"{WorldPaintCatalog.GreyboxDir}/{surface.GreyboxMaterial}.mat";
                var greybox = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (greybox == null)
                {
                    Debug.LogError($"{path} not found. Run Tools ▸ Pickleball ▸ Build Academy Greybox first.");
                    return null;
                }
                map[greybox] = LoadOrCreate(surface);
            }
            return map;
        }

        /// <summary>Created once; afterwards the asset is left alone, so tuned values survive a rebuild.</summary>
        static Material LoadOrCreate(in WorldPaintSurface surface)
        {
            Texture2D texture = surface.Texture != null ? PrepareTexture(surface.Texture) : null;

            string path = $"{WorldPaintCatalog.PaintedDir}/{surface.PaintedMaterial}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;

            mat = new Material(Shader.Find(WorldPaintCatalog.ShaderName)) { name = surface.PaintedMaterial };
            mat.SetColor("_BaseColor", surface.Tint);
            mat.SetFloat("_TileSize", surface.TileSize);
            mat.SetFloat("_VariationSize", surface.PatchSize);
            mat.SetColor("_VariationDark", surface.PatchDark);
            mat.SetColor("_VariationLight", surface.PatchLight);
            if (texture != null) mat.SetTexture("_BaseMap", texture);
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }

        /// <summary>Applies the import settings a tiling colour map needs. A replacement PNG under
        /// the same name keeps them through its .meta.</summary>
        static Texture2D PrepareTexture(string name)
        {
            string path = $"{WorldPaintCatalog.TextureDir}/{name}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"{path} not found; {name} will be flat colour.");
                return null;
            }

            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.anisoLevel = AnisoLevel;
            importer.isReadable = false;
            importer.maxTextureSize = TextureMaxSize;
            importer.textureCompression = TextureImporterCompression.Compressed;
            foreach (string platform in MobilePlatforms)
            {
                var settings = importer.GetPlatformTextureSettings(platform);
                settings.overridden = true;
                settings.maxTextureSize = TextureMaxSize;
                settings.format = TextureImporterFormat.ASTC_6x6;
                settings.compressionQuality = 50;
                importer.SetPlatformTextureSettings(settings);
            }
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>Headless entry point: builds the test, then renders before and after shots.
        /// Exits non-zero on failure, so a batch run can't report success after a failed build.</summary>
        public static void Batch()
        {
            bool ok = Build();
            ok = ok && CourtyardShots.RenderAfterAndBefore();
            EditorApplication.Exit(ok ? 0 : 1);
        }
    }
}
