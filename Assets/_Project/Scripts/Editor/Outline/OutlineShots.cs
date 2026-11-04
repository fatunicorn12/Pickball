using System.IO;
using Pickleball.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Pickleball.EditorTools.Outline
{
    /// <summary>
    /// Tools ▸ Pickleball ▸ Render Outline Shots. Renders each game scene twice from the same spot,
    /// with the outline off and on, into <c>Logs/Outline/</c>, which is gitignored. Scenes are opened
    /// but never saved. Characters are framed by name, in their first idle pose. These shots judge the
    /// look of a line, not how it holds up in motion, and a camera render says nothing about speed.
    /// </summary>
    public static class OutlineShots
    {
        const string OutputDir = "Logs/Outline";
        const int Width = 1920, Height = 1080;
        const string PlayerName = "Chibi";

        /// <summary>A framing: stand behind and above the named object, by these distances in metres.</summary>
        readonly struct View
        {
            public readonly string Name, Subject;
            public readonly float Back, Up;

            public View(string name, string subject, float back, float up)
            {
                Name = name;
                Subject = subject;
                Back = back;
                Up = up;
            }
        }

        static readonly View[] PlayerViews =
        {
            new View("Player", PlayerName, 4.5f, 1.8f),
            new View("Wide", PlayerName, 15f, 7f),
        };

        /// <summary>Stays where the scene's own camera is, for scenes with nobody to frame.</summary>
        static readonly View[] SceneCameraView = { new View("Camera", null, 0f, 0f) };

        static readonly (string path, View[] views)[] Scenes =
        {
            ("Assets/_Project/Scenes/ChibiSandbox.unity", new[]
            {
                PlayerViews[0],
                PlayerViews[1],
                new View("Newton", "NewtonBoss", 9f, 3f),
            }),
            ("Assets/_Project/Scenes/TrainingStage.unity", PlayerViews),
            ("Assets/_Project/Scenes/HypatiaFight.unity", new[]
            {
                PlayerViews[0],
                // Negative: from in front of her, since she stands at the edge of the arena.
                new View("Hypatia", "HypatiaModel", -4f, 0.5f),
            }),
            ("Assets/_Project/Scenes/AcademyGreybox.unity", PlayerViews),
            ("Assets/_Project/Scenes/SpacetimeLab.unity", SceneCameraView),
            ("Assets/_Project/Scenes/Cutscene/OpeningPlayable.unity", SceneCameraView),
        };

        /// <summary>The menu entry: renders every scene with the current settings asset.</summary>
        [MenuItem("Tools/Pickleball/Render Outline Shots")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Leave Play mode before rendering shots.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            RenderAll();
        }

        /// <summary>Command line entry: sets up the outline, renders everything, then quits.</summary>
        public static void BuildAndRenderBatch()
        {
            bool ok = OutlineSetup.Build() && RenderAll();
            EditorApplication.Exit(ok ? 0 : 1);
        }

        /// <summary>Renders off and on shots of every scene.</summary>
        /// <returns>False if a scene had no camera.</returns>
        public static bool RenderAll()
        {
            bool asyncShaders = ShaderUtil.allowAsyncCompilation;
            // A placeholder shader in the first frames would land in the shot.
            ShaderUtil.allowAsyncCompilation = false;
            try
            {
                Directory.CreateDirectory(OutputDir);
                foreach (var (path, views) in Scenes)
                {
                    var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    PoseAnimators(scene);
                    HidePlayerEffects(scene);
                    string sceneName = Path.GetFileNameWithoutExtension(path);
                    foreach (var view in views)
                    {
                        if (!TryFrame(scene, view, out Vector3 eye, out Quaternion rotation))
                        {
                            Debug.LogWarning($"Outline shots: nothing to frame for '{view.Name}' in {sceneName}.");
                            continue;
                        }
                        string prefix = $"{sceneName}_{view.Name}";
                        if (!Render(eye, rotation, false, $"{prefix}_off")) return false;
                        if (!Render(eye, rotation, true, $"{prefix}_on")) return false;
                    }
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = asyncShaders;
            }
            Debug.Log($"Outline shots written to {Path.GetFullPath(OutputDir)}.");
            return true;
        }

        /// <summary>Switches the outline on every project renderer, in memory only.</summary>
        static void SetOutlineActive(bool active)
        {
            foreach (string path in OutlineSetup.RendererPaths)
            {
                var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (renderer == null) continue;
                foreach (var feature in renderer.rendererFeatures)
                    if (feature is OutlineRendererFeature) feature.SetActive(active);
            }
        }

        /// <summary>Puts characters in their first animation pose, so they don't stand in a T-pose.</summary>
        static void PoseAnimators(Scene scene)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var animator in root.GetComponentsInChildren<Animator>())
                {
                    if (animator.runtimeAnimatorController == null) continue;
                    animator.Rebind();
                    animator.Update(0.25f);
                }
            }
        }

        /// <summary>
        /// Hides the player's glow, rune and arc effects. Their scripts hide them in Play mode, but
        /// shots are taken in edit mode, where they'd stand lit around the player and wash out the
        /// very lines being judged.
        /// </summary>
        static void HidePlayerEffects(Scene scene)
        {
            Transform player = FindByName(scene, PlayerName);
            if (player == null) return;
            foreach (var renderer in player.GetComponentsInChildren<Renderer>())
            {
                var material = renderer.sharedMaterial;
                if (material != null && material.renderQueue >= (int)RenderQueue.GeometryLast + 1) renderer.enabled = false;
            }
        }

        static bool TryFrame(Scene scene, View view, out Vector3 eye, out Quaternion rotation)
        {
            eye = default;
            rotation = default;

            if (view.Subject == null)
            {
                var camera = FindCamera();
                if (camera == null) return false;
                eye = camera.transform.position;
                rotation = camera.transform.rotation;
                return true;
            }

            Transform subject = FindByName(scene, view.Subject);
            if (subject == null) return false;

            var bounds = new Bounds(subject.position + Vector3.up, Vector3.one);
            foreach (var renderer in subject.GetComponentsInChildren<Renderer>())
            {
                // Trails and particles can sit far from the character and throw the framing off.
                if (renderer is SkinnedMeshRenderer || renderer is MeshRenderer) bounds.Encapsulate(renderer.bounds);
            }

            Vector3 look = bounds.center;
            Vector3 flatForward = Vector3.ProjectOnPlane(subject.forward, Vector3.up);
            flatForward = flatForward.sqrMagnitude > 0.001f ? flatForward.normalized : Vector3.forward;
            eye = look - flatForward * view.Back + Vector3.up * view.Up;
            rotation = Quaternion.LookRotation(look - eye, Vector3.up);
            return true;
        }

        static Transform FindByName(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    if (transform.name == name) return transform;
            return null;
        }

        static Camera FindCamera()
        {
            var camera = Camera.main;
            return camera != null ? camera : Object.FindFirstObjectByType<Camera>();
        }

        static bool Render(Vector3 eye, Quaternion rotation, bool outlineOn, string fileName)
        {
            var camera = FindCamera();
            if (camera == null)
            {
                Debug.LogError("The open scene has no camera to render with.");
                return false;
            }

            var transform = camera.transform;
            Vector3 position = transform.position;
            Quaternion previousRotation = transform.rotation;
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            SetOutlineActive(outlineOn);
            try
            {
                camera.targetTexture = target;
                transform.SetPositionAndRotation(eye, rotation);
                camera.Render();
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                readback.Apply();
                File.WriteAllBytes(Path.Combine(OutputDir, $"{fileName}.png"), readback.EncodeToPNG());
            }
            finally
            {
                // The scene stays as it was, and the outline is always left on.
                SetOutlineActive(true);
                camera.targetTexture = previousTarget;
                transform.SetPositionAndRotation(position, previousRotation);
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
            }
            return true;
        }
    }
}
