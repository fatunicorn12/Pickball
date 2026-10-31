using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Pickleball.Cutscene.Editor.Actors
{
    /// <summary>
    /// Renders contact sheets of an actor in posed frames, off-scene (a preview scene, so no open
    /// scene is touched or dirtied), with optional anchor markers. Used to judge the hand-keyed
    /// clips by eye and to document them; output goes outside Assets.
    /// </summary>
    public static class ActorPoseSheet
    {
        /// <summary>One cell: a clip sampled at a time (null clip = rest pose).</summary>
        public struct Cell
        {
            /// <summary>The clip, or null for the rest pose.</summary>
            public AnimationClip Clip;

            /// <summary>Time in seconds.</summary>
            public float Time;

            /// <summary>Yaw of the camera around the actor, degrees (0 = from the front).</summary>
            public float ViewYaw;

            /// <summary>Camera pitch, degrees (positive looks down from above; 0 means the default 12°).</summary>
            public float ViewPitch;

            /// <summary>When positive, frame a box this tall (metres) round the Head anchor (a close-up).</summary>
            public float FocusHeight;

            /// <summary>Called on the instance after posing (e.g. detach the propeller).</summary>
            public Action<GameObject> Prepare;
        }

        /// <summary>Anchor marker colours by anchor index (Head, Eyes, Hand_R, Feet, PropellerSocket, Blade).</summary>
        private static readonly Color[] MarkerColours =
        {
            new Color(1f, 0.15f, 0.15f), new Color(0.1f, 0.9f, 1f), new Color(1f, 0.85f, 0.1f),
            new Color(0.2f, 1f, 0.3f), new Color(1f, 0.2f, 1f), new Color(1f, 0.55f, 0.1f),
        };

        /// <summary>Render cells of <paramref name="source"/> into a grid PNG at <paramref name="outPath"/>.</summary>
        public static void Render(GameObject source, IList<Cell> cells, string outPath, int columns = 4,
            int cellW = 360, int cellH = 440, bool markAnchors = false)
        {
            var preview = new PreviewRenderUtility();
            var tiles = new List<Texture2D>();
            try
            {
                preview.cameraFieldOfView = 30f;
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.backgroundColor = new Color(0.62f, 0.66f, 0.72f);
                preview.camera.nearClipPlane = 0.05f;
                preview.camera.farClipPlane = 100f;
                preview.lights[0].intensity = 1.3f;
                preview.lights[0].transform.rotation = Quaternion.Euler(40f, -30f, 0f);
                preview.lights[1].intensity = 0.6f;
                preview.lights[1].transform.rotation = Quaternion.Euler(20f, 150f, 0f);
                preview.ambientColor = new Color(0.45f, 0.45f, 0.5f);

                // The first render of a fresh PreviewRenderUtility can come back empty; warm it up.
                preview.BeginStaticPreview(new Rect(0, 0, 16, 16));
                preview.Render(true);
                Object.DestroyImmediate(preview.EndStaticPreview());

                foreach (Cell cell in cells)
                {
                    var go = (GameObject)Object.Instantiate(source);
                    go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    preview.AddSingleGO(go);
                    var animator = go.GetComponentInChildren<Animator>();
                    if (cell.Clip != null && animator != null) Evaluate(animator, cell.Clip, cell.Time);
                    cell.Prepare?.Invoke(go);
                    var markers = new List<GameObject>();
                    var cellSlot = go.GetComponent<CutsceneActor>();
                    if (markAnchors && cellSlot != null) AddMarkers(cellSlot, preview, markers);

                    Bounds b = BoundsOf(go);
                    var slot = go.GetComponent<CutsceneActor>();
                    if (cell.FocusHeight > 0f && slot != null)
                        b = new Bounds(slot.AnchorPosition(CutsceneAnchor.Head), Vector3.one * cell.FocusHeight);
                    float aspect = cellW / (float)cellH;
                    float half = Mathf.Max(b.extents.y, Mathf.Max(b.extents.x, b.extents.z) / aspect, 0.1f) * 1.12f;
                    float dist = half / Mathf.Tan(preview.cameraFieldOfView * 0.5f * Mathf.Deg2Rad) +
                                 Mathf.Max(b.extents.x, b.extents.z);
                    Vector3 dir = Quaternion.Euler(cell.ViewPitch != 0f ? -cell.ViewPitch : -12f, cell.ViewYaw, 0f) * Vector3.forward;
                    preview.camera.transform.position = b.center + dir * dist;
                    preview.camera.transform.LookAt(b.center);

                    preview.BeginStaticPreview(new Rect(0, 0, cellW, cellH));
                    // PreviewRenderUtility doesn't always push cameraFieldOfView to its camera, and
                    // the markers must project with the lens the frame was rendered with.
                    preview.camera.usePhysicalProperties = false;
                    preview.camera.fieldOfView = preview.cameraFieldOfView;
                    preview.camera.aspect = cellW / (float)cellH;
                    preview.camera.ResetProjectionMatrix();
                    preview.Render(true);
                    Texture2D tex = preview.EndStaticPreview();
                    tiles.Add(tex);
                    foreach (var m in markers)
                    {
                        var mr = m.GetComponent<Renderer>();
                        if (mr != null && mr.sharedMaterial != null) Object.DestroyImmediate(mr.sharedMaterial);
                        Object.DestroyImmediate(m);
                    }
                    Object.DestroyImmediate(go);
                }

                int rows = Mathf.CeilToInt(tiles.Count / (float)columns);
                int cols = Mathf.Min(columns, tiles.Count);
                var sheet = new Texture2D(cols * cellW, rows * cellH, TextureFormat.RGB24, false);
                for (int i = 0; i < tiles.Count; i++)
                {
                    int cx = i % columns, cy = rows - 1 - i / columns;
                    Texture2D t = tiles[i];
                    var px = t.GetPixels(0, 0, Mathf.Min(cellW, t.width), Mathf.Min(cellH, t.height));
                    sheet.SetPixels(cx * cellW, cy * cellH, Mathf.Min(cellW, t.width), Mathf.Min(cellH, t.height), px);
                }
                sheet.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(outPath));
                File.WriteAllBytes(outPath, sheet.EncodeToPNG());
                Object.DestroyImmediate(sheet);
            }
            finally
            {
                foreach (var t in tiles) Object.DestroyImmediate(t);
                preview.Cleanup();
            }
        }

        /// <summary>
        /// Pose a humanoid Animator's hierarchy with a clip at a time by writing the clip's own
        /// human pose (<see cref="HumanClipCurves"/>) through a HumanPoseHandler. The root never
        /// moves; a clip with unbaked root travel shows its travel as a body offset.
        /// </summary>
        public static void Evaluate(Animator animator, AnimationClip clip, float time)
        {
            if (animator == null || animator.avatar == null || !animator.avatar.isHuman) return;
            var handler = new HumanPoseHandler(animator.avatar, animator.transform);
            try
            {
                var pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
                new HumanClipCurves(clip).Read(time, false, ref pose);
                handler.SetHumanPose(ref pose);
            }
            finally
            {
                handler.Dispose();
            }
        }

        /// <summary>
        /// Bounds of what is drawn in the current pose. Skinned meshes are baked, because their
        /// renderer bounds don't follow an edit-mode pose.
        /// </summary>
        public static Bounds BoundsOf(GameObject go)
        {
            var b = new Bounds(go.transform.position + Vector3.up * 0.8f, Vector3.one * 0.5f);
            bool any = false;
            var mesh = new Mesh();
            try
            {
                foreach (var r in go.GetComponentsInChildren<Renderer>())
                {
                    if (!r.enabled || r.name.StartsWith("AnchorMarker", StringComparison.Ordinal)) continue;
                    if (r is SkinnedMeshRenderer smr)
                    {
                        if (smr.sharedMesh == null) continue;
                        smr.BakeMesh(mesh, true);
                        var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                        foreach (Vector3 v in mesh.vertices)
                        {
                            Vector3 w = m.MultiplyPoint3x4(v);
                            if (!any) { b = new Bounds(w, Vector3.zero); any = true; }
                            else b.Encapsulate(w);
                        }
                    }
                    else if (r is MeshRenderer)
                    {
                        if (!any) { b = r.bounds; any = true; }
                        else b.Encapsulate(r.bounds);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
            return b;
        }

        /// <summary>
        /// A small sphere per anchor plus a tick along its forward, drawn with depth test off so
        /// anchors inside the head or hand still show. Geometry rather than painted pixels, so the
        /// render's own projection places them.
        /// </summary>
        private static void AddMarkers(CutsceneActor actor, PreviewRenderUtility preview, List<GameObject> markers)
        {
            Shader onTop = Shader.Find("Hidden/Internal-Colored");
            for (int i = 0; i < CutsceneAnchors.Count; i++)
            {
                Transform a = actor.GetAnchor((CutsceneAnchor)i);
                if (a == null) continue;
                var mat = new Material(onTop) { color = MarkerColours[i] };
                mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                mat.SetInt("_ZWrite", 0);
                mat.SetInt("_Cull", 0);
                float size = Mathf.Max(0.03f, actor.Height * 0.025f);
                markers.Add(Marker(PrimitiveType.Sphere, a.position, a.rotation, Vector3.one * size, mat, preview));
                markers.Add(Marker(PrimitiveType.Cube, a.position + a.forward * size * 1.5f, a.rotation,
                    new Vector3(size * 0.25f, size * 0.25f, size * 3f), mat, preview));
            }
        }

        private static GameObject Marker(PrimitiveType type, Vector3 pos, Quaternion rot, Vector3 scale, Material mat,
            PreviewRenderUtility preview)
        {
            var m = GameObject.CreatePrimitive(type);
            m.name = "AnchorMarker";
            Object.DestroyImmediate(m.GetComponent<Collider>());
            m.transform.SetPositionAndRotation(pos, rot);
            m.transform.localScale = scale;
            var r = m.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            preview.AddSingleGO(m);
            return m;
        }
    }
}
