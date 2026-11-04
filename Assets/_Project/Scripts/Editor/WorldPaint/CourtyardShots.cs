using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.EditorTools.WorldPaint
{
    /// <summary>
    /// Tools ▸ Pickleball ▸ Trials ▸ Render Courtyard Shots. Renders fixed views of the courtyard
    /// through the scene's own camera, so post-processing applies, and writes them to
    /// <c>Logs/WorldPaint/</c>, which is gitignored. The views never change, so textures can be
    /// compared shot for shot. A camera render can't show motion or the real follow camera: these
    /// shots judge colour and scale, and walking the scene judges the rest.
    /// </summary>
    public static class CourtyardShots
    {
        const string SourceScenePath = "Assets/_Project/Scenes/AcademyGreybox.unity";
        const string OutputDir = "Logs/WorldPaint";
        const int Width = 1600, Height = 900;

        /// <summary>Fixed views: the first is about where the game starts, behind the player.</summary>
        static readonly (string name, Vector3 eye, Vector3 target)[] Views =
        {
            ("1_Spawn", new(0f, 2.4f, -10.5f), new(0f, 2f, 6f)),
            ("2_Colonnade", new(-9.5f, 1.4f, -5.5f), new(-14.5f, 2.3f, 3f)),
            ("3_FromRoof", new(-17f, 8f, -17f), new(5f, 0f, 3f)),
            ("4_NorthGate", new(0f, 2.2f, 8f), new(0f, 2f, 34f)),
        };

        /// <summary>The menu entry: renders the open scene into <c>Logs/WorldPaint/current</c>.</summary>
        [MenuItem("Tools/Pickleball/Trials/Render Courtyard Shots")]
        public static void RenderOpenScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Leave Play mode before rendering shots.");
                return;
            }
            Render(Path.Combine(OutputDir, "current"));
        }

        /// <summary>Renders the trial scene, then the untouched greybox for comparison.</summary>
        /// <returns>False if either render failed.</returns>
        public static bool RenderAfterAndBefore()
        {
            EditorSceneManager.OpenScene(CourtyardTextureTestSetup.ScenePath, OpenSceneMode.Single);
            if (!Render(Path.Combine(OutputDir, "after"))) return false;
            EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            return Render(Path.Combine(OutputDir, "before"));
        }

        static bool Render(string folder)
        {
            var camera = Camera.main;
            if (camera == null) camera = Object.FindFirstObjectByType<Camera>();
            if (camera == null)
            {
                Debug.LogError("The open scene has no camera to render with.");
                return false;
            }
            Directory.CreateDirectory(folder);

            // A placeholder shader in the first frames would land in the shot.
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;

            var transform = camera.transform;
            Vector3 position = transform.position;
            Quaternion rotation = transform.rotation;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                foreach (var (name, eye, look) in Views)
                {
                    transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
                    camera.Render();
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    readback.Apply();
                    File.WriteAllBytes(Path.Combine(folder, $"{name}.png"), readback.EncodeToPNG());
                }
            }
            finally
            {
                // The scene stays as it was and isn't marked dirty.
                camera.targetTexture = previousTarget;
                transform.SetPositionAndRotation(position, rotation);
                RenderTexture.active = previousActive;
                ShaderUtil.allowAsyncCompilation = asyncShaders;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
            }
            Debug.Log($"Courtyard shots written to {Path.GetFullPath(folder)}.");
            return true;
        }
    }
}
