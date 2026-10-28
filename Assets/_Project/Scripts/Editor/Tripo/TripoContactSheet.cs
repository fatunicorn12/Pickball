using System.Collections.Generic;
using System.IO;
using System.Linq;
using Pickleball.Art;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Pickleball.EditorTools.Tripo
{
    /// <summary>
    /// Renders contact sheets for judging a Tripo batch before it is hooked into
    /// the game, written as PNGs to Logs/TripoRenders, or headless to
    /// <c>-tripoRenderDir &lt;dir&gt;</c> with
    /// <c>-executeMethod Pickleball.EditorTools.Tripo.TripoContactSheet.Batch</c>
    /// (needs a GPU, so not with -nographics).
    ///
    /// Every row gets a sheet: three views of the prefab next to the chibi player
    /// (1.56 m) for scale, then a close-up. Humanoid rows also get a pose sheet:
    /// the character between the chibi and Newton, then retargeted idle, walk and
    /// cast clips (plus any extra clips passed in), with close-ups of the shoulders
    /// and hands and of the hem and feet, where auto-rigs tear first. The lowest
    /// vertex of each pose is logged, so "feet on the ground" is a number.
    ///
    /// Renders in a temporary empty scene and never saves it. From the menu it
    /// refuses to run while any open scene has unsaved changes, and restores the
    /// open scenes afterwards.
    /// </summary>
    public static class TripoContactSheet
    {
        const string ChibiPrefab = "Assets/_Project/Prefabs/Chibi.prefab";
        const string NewtonPrefab = "Assets/_Project/Prefabs/NewtonBoss.prefab";
        const int Tile = 768;
        const float FieldOfView = 30f;
        static readonly Color Background = new(0.74f, 0.79f, 0.86f);

        /// <summary>Humanoid clips already in the project that every pose sheet uses.</summary>
        static readonly string[] PreviewClipPaths =
        {
            "Assets/_Project/Animation/Newton/Idle.fbx",
            "Assets/_Project/Animation/Newton/Walking.fbx",
            "Assets/_Project/Animation/Newton/Cast.fbx",
        };

        [MenuItem("Tools/Pickleball/Tripo/Render Contact Sheets")]
        static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Tripo contact sheets: leave Play mode first.");
                return;
            }
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                if (EditorSceneManager.GetSceneAt(i).isDirty)
                {
                    EditorUtility.DisplayDialog("Render Contact Sheets",
                        "Save or discard the open scene's changes first: the renderer swaps in a temporary scene.", "OK");
                    return;
                }
            }
            var setup = EditorSceneManager.GetSceneManagerSetup();
            string dir = DefaultDir;
            try
            {
                RenderAll(dir);
            }
            finally
            {
                if (setup.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(setup);
            }
            EditorUtility.RevealInFinder(dir);
        }

        /// <summary>Headless entry; exits 1 when nothing was rendered. Optional
        /// <c>-tripoPreviewClips a.fbx,b.fbx</c> adds humanoid clips to the pose sheets.</summary>
        public static void Batch()
        {
            int count = 0;
            try
            {
                var extra = (TripoAssetImporter.CommandLineValue("-tripoPreviewClips") ?? "")
                            .Split(',').Where(p => p.Length > 0).Select(LoadClip).Where(c => c != null).ToList();
                count = RenderAll(TripoAssetImporter.CommandLineValue("-tripoRenderDir") ?? DefaultDir, extra);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
            }
            EditorApplication.Exit(count > 0 ? 0 : 1);
        }

        static string DefaultDir =>
            Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, "Logs", "TripoRenders");

        /// <summary>The first real clip in a model file, or null.</summary>
        public static AnimationClip LoadClip(string assetPath) =>
            AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<AnimationClip>()
                         .FirstOrDefault(c => !c.name.StartsWith("__preview__"));

        /// <summary>Renders a sheet for every catalog row that has a prefab, plus a
        /// pose sheet for humanoid rows. Returns how many sheets were written.</summary>
        public static int RenderAll(string outDir, IReadOnlyList<AnimationClip> extraClips = null)
        {
            var catalog = AssetDatabase.LoadAssetAtPath<TripoAssetCatalog>(TripoAssetCatalog.AssetPath);
            if (catalog == null)
            {
                Debug.LogError("Tripo contact sheets: no catalog; run Import Tripo Assets first.");
                return 0;
            }
            Directory.CreateDirectory(outDir);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var stage = BuildStage();
            var chibi = Figure(ChibiPrefab);
            var newton = Figure(NewtonPrefab);
            if (newton != null) newton.SetActive(false);
            // The chibi's saved bone pose is whatever was last sampled (a crouch):
            // stand it in its controller's default state for the prop sheets.
            var chibiAnimator = chibi != null ? chibi.GetComponentInChildren<Animator>() : null;
            if (chibiAnimator != null && chibiAnimator.runtimeAnimatorController != null)
            {
                chibiAnimator.Rebind();
                chibiAnimator.Update(0f);
                chibiAnimator.Update(0.25f);
            }

            // Async compilation draws a placeholder for any variant not compiled yet.
            bool async = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            int count = 0;
            try
            {
                foreach (var spec in catalog.Assets)
                {
                    var prefab = spec != null ? AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) : null;
                    if (prefab == null) continue;
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    try
                    {
                        RenderSheet(stage, instance, chibi, Path.Combine(outDir, $"{spec.Id}_{spec.AssetName}_sheet.png"));
                        count++;
                        if (spec.Rig == TripoRig.Humanoid)
                        {
                            // Nulls kept, so each clip keeps its slot (idle, walk, cast).
                            var clips = PreviewClipPaths.Select(LoadClip).ToList();
                            if (extraClips != null) clips.AddRange(extraClips);
                            RenderPoseSheet(stage, instance, chibi, newton, clips,
                                            Path.Combine(outDir, $"{spec.Id}_{spec.AssetName}_poses.png"));
                            count++;
                        }
                    }
                    finally
                    {
                        Object.DestroyImmediate(instance);
                    }
                }
            }
            finally
            {
                ShaderUtil.allowAsyncCompilation = async;
            }
            Debug.Log($"Tripo contact sheets: {count} written to {outDir}");
            return count;
        }

        /// <summary>A prefab instance showing only its body (skinned meshes) and,
        /// for the chibi, its sword: the skateboard, dash arc, rope and beam say
        /// nothing about scale.</summary>
        static GameObject Figure(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset == null)
            {
                Debug.LogWarning($"Tripo contact sheets: {path} missing; rendering without it.");
                return null;
            }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                bool body = r is SkinnedMeshRenderer;
                bool sword = r is MeshRenderer && r.transform.parent != null &&
                             r.transform.parent.name == "WeaponSocket";
                r.enabled = body || sword;
            }
            return go;
        }

        sealed class Stage
        {
            public Camera Camera;
            public RenderTexture Target;
            public Texture2D Sheet;
            public int Rows;
        }

        static Stage BuildStage()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.66f, 0.74f);
            RenderSettings.ambientEquatorColor = new Color(0.48f, 0.48f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.28f, 0.26f, 0.24f);
            RenderSettings.skybox = null;
            RenderSettings.fog = false;

            var sun = new GameObject("Sun").AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.25f;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            // Wide enough that its edge is beyond every camera's view: a horizon, not a wall.
            ground.transform.localScale = Vector3.one * 200f;
            var groundMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            groundMat.SetColor("_BaseColor", new Color(0.55f, 0.56f, 0.55f));
            groundMat.SetFloat("_Smoothness", 0.05f);
            ground.GetComponent<Renderer>().sharedMaterial = groundMat;

            var camera = new GameObject("ContactCamera").AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = FieldOfView;
            camera.nearClipPlane = 0.02f;
            camera.farClipPlane = 3000f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;
            return new Stage { Camera = camera };
        }

        /// <summary>Four tiles side by side: front, three-quarter and side with the
        /// chibi, then a close three-quarter of the prefab alone.</summary>
        static void RenderSheet(Stage stage, GameObject instance, GameObject chibi, string file)
        {
            // Stand the prefab on the ground at the origin, whatever its pivot.
            instance.transform.position = Vector3.zero;
            var b = WorldBounds(instance);
            instance.transform.position = new Vector3(0f, -b.min.y, 0f);
            b = WorldBounds(instance);

            if (chibi != null)
            {
                chibi.SetActive(true);
                chibi.transform.position = Vector3.zero;
                var cb = WorldBounds(chibi);
                // Left of the prefab, a hand's width apart, feet on the ground whatever
                // pose a previous pose sheet left it in.
                chibi.transform.position = new Vector3(b.min.x - 0.35f - cb.max.x, -cb.min.y, 0f);
            }
            var both = b;
            if (chibi != null) both.Encapsulate(WorldBounds(chibi));

            BeginSheet(stage, 4, 1);
            try
            {
                // Warm-up: in batch mode the first frame that meets a new shader
                // variant (the orb's emissive Lit) drew it magenta. Overwritten next.
                RenderTile(stage, 0, 0, both, yaw: 0f, pitch: 8f);
                RenderTile(stage, 0, 0, both, yaw: 0f, pitch: 8f);
                RenderTile(stage, 1, 0, both, yaw: 40f, pitch: 20f);
                RenderTile(stage, 2, 0, both, yaw: 90f, pitch: 10f);
                if (chibi != null) chibi.SetActive(false);
                RenderTile(stage, 3, 0, b, yaw: -35f, pitch: 28f);
                EndSheet(stage, file);
            }
            finally
            {
                if (chibi != null) chibi.SetActive(true);
                DisposeSheet(stage);
            }
        }

        /// <summary>
        /// Eight tiles for a humanoid: a line-up with the chibi and Newton in the
        /// same idle; the character's idle, walk (side and back) and cast; any
        /// extra clip (a talk); then close-ups of the upper body and of the hem
        /// and feet. Every figure is posed by retargeting the same humanoid clip.
        /// </summary>
        static void RenderPoseSheet(Stage stage, GameObject character, GameObject chibi, GameObject newton,
                                    List<AnimationClip> clips, string file)
        {
            // By position in PreviewClipPaths (Mixamo clips are often all named "mixamo.com").
            var idle = clips.ElementAtOrDefault(0);
            var walk = clips.ElementAtOrDefault(1);
            var cast = clips.ElementAtOrDefault(2);
            var extra = clips.ElementAtOrDefault(PreviewClipPaths.Length) ?? cast ?? idle;
            var action = cast ?? idle;
            if (idle == null)
            {
                Debug.LogWarning("Tripo pose sheet: no preview clips found; skipped.");
                return;
            }

            var figures = new[] { chibi, character, newton }.Where(f => f != null).ToArray();
            // No player loop runs between batch-mode renders, so without this the
            // GPU skins with the last frame's bone matrices: the bind pose.
            foreach (var smr in figures.SelectMany(f => f.GetComponentsInChildren<SkinnedMeshRenderer>(true)))
                smr.forceMatrixRecalculationPerRender = true;

            BeginSheet(stage, 4, 2);
            var graphs = new List<PlayableGraph>();
            try
            {
                // Line-up: chibi, character, Newton, all in the same idle.
                if (newton != null) newton.SetActive(true);
                Pose(graphs, figures, idle, 0.3f);
                LineUp(figures);
                var all = figures.Select(WorldBounds).Aggregate((a, x) => { a.Encapsulate(x); return a; });
                RenderTile(stage, 0, 0, all, yaw: 0f, pitch: 6f);
                RenderTile(stage, 0, 0, all, yaw: 0f, pitch: 6f);
                if (chibi != null) chibi.SetActive(false);
                if (newton != null) newton.SetActive(false);
                character.transform.position = Vector3.zero;
                LogFeet(character, idle.name);

                RenderTile(stage, 1, 0, WorldBounds(character), yaw: 35f, pitch: 12f);
                var feetIdle = Slice(WorldBounds(character), 0f, 0.32f);

                if (walk != null)
                {
                    Pose(graphs, new[] { character }, walk, 0.25f);
                    LogFeet(character, walk.name + " 25%");
                    RenderTile(stage, 2, 0, WorldBounds(character), yaw: 90f, pitch: 5f);
                    Pose(graphs, new[] { character }, walk, 0.75f);
                    RenderTile(stage, 3, 0, WorldBounds(character), yaw: 150f, pitch: 12f);
                }

                Pose(graphs, new[] { character }, action, 0.45f);
                LogFeet(character, action.name + " 45%");
                RenderTile(stage, 0, 1, WorldBounds(character), yaw: -30f, pitch: 10f);

                Pose(graphs, new[] { character }, extra, 0.4f);
                LogFeet(character, extra.name + " 40%");
                var b = WorldBounds(character);
                RenderTile(stage, 1, 1, b, yaw: 0f, pitch: 8f);
                RenderTile(stage, 2, 1, Slice(b, 0.5f, 1f), yaw: 25f, pitch: 10f);

                Pose(graphs, new[] { character }, idle, 0.3f);
                RenderTile(stage, 3, 1, feetIdle, yaw: 20f, pitch: 3f);
                EndSheet(stage, file);
            }
            finally
            {
                foreach (var g in graphs) if (g.IsValid()) g.Destroy();
                if (chibi != null) chibi.SetActive(true);
                if (newton != null) newton.SetActive(false);
                DisposeSheet(stage);
            }
        }

        /// <summary>Retargets <paramref name="clip"/> at a fraction of its length onto
        /// each figure's humanoid Animator through a manual PlayableGraph, the way
        /// Timeline previews in edit mode. The graphs stay alive until the caller
        /// destroys them, so the pose holds for the render.</summary>
        static void Pose(List<PlayableGraph> graphs, IEnumerable<GameObject> figures, AnimationClip clip, float fraction)
        {
            foreach (var g in graphs) if (g.IsValid()) g.Destroy();
            graphs.Clear();
            foreach (var figure in figures)
            {
                var animator = figure.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    Debug.LogWarning($"Tripo pose sheet: {figure.name} has no humanoid Animator; left in its bind pose.");
                    continue;
                }
                animator.applyRootMotion = false;
                // An Animator with no controller is never bound in edit mode, so a
                // graph evaluated on it moves nothing until it is rebound.
                animator.Rebind();
                var graph = PlayableGraph.Create($"TripoPose_{figure.name}");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(true);
                output.SetSourcePlayable(playable);
                playable.SetTime(clip.length * fraction);
                graph.Evaluate();
                graphs.Add(graph);
            }
        }

        /// <summary>Places the figures left to right, standing on the ground, 0.4 m apart.</summary>
        static void LineUp(GameObject[] figures)
        {
            float x = 0f;
            foreach (var f in figures)
            {
                f.transform.position = Vector3.zero;
                var b = WorldBounds(f);
                f.transform.position = new Vector3(x - b.min.x, -b.min.y, 0f);
                x += b.size.x + 0.4f;
            }
            float shift = -x * 0.5f;
            foreach (var f in figures) f.transform.position += new Vector3(shift, 0f, 0f);
        }

        static void LogFeet(GameObject character, string pose)
        {
            var b = WorldBounds(character);
            Debug.Log($"Tripo pose sheet: {character.name} '{pose}': lowest vertex {b.min.y - character.transform.position.y:+0.000;-0.000} m " +
                      $"from the root, height {b.size.y:0.00} m, span {b.size.x:0.00} m");
        }

        /// <summary>The horizontal slab of <paramref name="b"/> between two height fractions.</summary>
        static Bounds Slice(Bounds b, float from, float to)
        {
            var min = b.min; var max = b.max;
            float h = b.size.y;
            var s = new Bounds();
            s.SetMinMax(new Vector3(min.x, min.y + h * from, min.z), new Vector3(max.x, min.y + h * to, max.z));
            return s;
        }

        static void BeginSheet(Stage stage, int columns, int rows)
        {
            stage.Rows = rows;
            stage.Sheet = new Texture2D(Tile * columns, Tile * rows, TextureFormat.RGB24, false);
            stage.Target = new RenderTexture(Tile, Tile, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        }

        static void EndSheet(Stage stage, string file)
        {
            stage.Sheet.Apply();
            File.WriteAllBytes(file, stage.Sheet.EncodeToPNG());
        }

        static void DisposeSheet(Stage stage)
        {
            RenderTexture.active = null;
            if (stage.Target != null) Object.DestroyImmediate(stage.Target);
            if (stage.Sheet != null) Object.DestroyImmediate(stage.Sheet);
            stage.Target = null;
            stage.Sheet = null;
        }

        /// <summary>Frames <paramref name="bounds"/> from a direction given as yaw
        /// (0 = from +Z, looking at the asset's front) and pitch, and copies the
        /// render into the sheet at (<paramref name="column"/>, <paramref name="row"/>),
        /// row 0 at the top.</summary>
        static void RenderTile(Stage stage, int column, int row, Bounds bounds, float yaw, float pitch)
        {
            var cam = stage.Camera;
            var rotation = Quaternion.Euler(pitch, yaw + 180f, 0f);
            var forward = rotation * Vector3.forward;
            var right = rotation * Vector3.right;
            var up = rotation * Vector3.up;

            // Tightest distance at which every bounds corner is inside the frame:
            // a corner at centre+v needs max(|x|,|y|) <= depth * tan(fov/2).
            float tan = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            float distance = 0.1f;
            for (int i = 0; i < 8; i++)
            {
                var v = Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float lateral = Mathf.Max(Mathf.Abs(Vector3.Dot(v, right)), Mathf.Abs(Vector3.Dot(v, up)));
                distance = Mathf.Max(distance, lateral / tan - Vector3.Dot(v, forward));
            }
            distance *= 1.1f;
            cam.transform.SetPositionAndRotation(bounds.center - forward * distance, rotation);
            cam.aspect = 1f;

            RenderPipeline.SubmitRenderRequest(cam, new RenderPipeline.StandardRequest { destination = stage.Target });
            RenderTexture.active = stage.Target;
            stage.Sheet.ReadPixels(new Rect(0, 0, Tile, Tile), column * Tile, (stage.Rows - 1 - row) * Tile);
        }

        /// <summary>World bounds of the visible meshes. Skinned meshes are baked, so
        /// the bounds follow the current pose exactly; trails and particles are
        /// ignored (their edit-time bounds are empty or huge).</summary>
        static Bounds WorldBounds(GameObject go)
        {
            bool any = false;
            var result = new Bounds(go.transform.position, Vector3.zero);
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                Bounds b;
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh != null) b = BakedBounds(smr);
                else if (r is MeshRenderer) b = r.bounds;
                else continue;
                if (!any) { result = b; any = true; }
                else result.Encapsulate(b);
            }
            return result;
        }

        static Bounds BakedBounds(SkinnedMeshRenderer smr)
        {
            var mesh = new Mesh();
            try
            {
                smr.BakeMesh(mesh, true);
                var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                var verts = mesh.vertices;
                if (verts.Length == 0) return new Bounds(smr.transform.position, Vector3.zero);
                var b = new Bounds(m.MultiplyPoint3x4(verts[0]), Vector3.zero);
                foreach (var v in verts) b.Encapsulate(m.MultiplyPoint3x4(v));
                return b;
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }
    }
}
