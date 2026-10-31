using System;
using System.Collections.Generic;
using Pickleball.Cutscene.Editor.Pipeline;
using Pickleball.Cutscene.Playback;
using Pickleball.Cutscene.Script;
using Pickleball.Cutscene.Timeline;
using Pickleball.Cutscene.Timeline.Plan;
using Pickleball.Sandbox;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using UnityEngine.Timeline;
using Object = UnityEngine.Object;

namespace Pickleball.Cutscene.Cameras.Editor
{
    /// <summary>
    /// <c>Tools ▸ Pickleball ▸ Cutscene ▸ 6 Build Cameras</c>. In the open OpeningPlayable scene
    /// (menu 4's), builds every shot's camera and the camera tracks:
    /// <list type="bullet">
    /// <item>Seeds the shot table from <see cref="OpeningShotSeed"/> (keeping tuned shots), copies the
    /// noise profiles and writes the depth-of-field volume profiles.</item>
    /// <item><c>CutsceneCameras</c> root: <see cref="CutsceneCameraContext"/> (actors, markers, director,
    /// orbit camera), <see cref="CutsceneCameraHandback"/>, the shared aim target, the scene-4 track
    /// spline and its proxy, and one CinemachineCamera + <see cref="ShotRig"/> per shot, named by id.</item>
    /// <item>A CinemachineBrain on the main camera, disabled, appended to CameraHandoff.cutsceneOnly
    /// so it runs only during cutscenes.</item>
    /// <item>In each OC_S*.playable, a "Cameras" group with a Cinemachine track: one CinemachineShot
    /// per ShotClip over the same start and duration (hard cuts), bound to the brain. The Shots
    /// track's placeholder camera is turned off.</item>
    /// </list>
    /// Idempotent: objects, components, tracks and clips are found by name and reused, so a second
    /// run (or a run after the pipeline's Rebuild Timelines) gives the same scene and timelines.
    /// </summary>
    public static class CutsceneCameraBuilder
    {
        /// <summary>Near clip of every cutscene shot but the hand-off, metres (an ECU sits 0.25 m off the face).</summary>
        public const float CloseNearClip = 0.1f;

        /// <summary>Menu entry.</summary>
        [MenuItem(CutsceneMenus.BuildCameras, false, 6)]
        public static void BuildMenu() => Build();

        /// <summary>Builds the cameras. Returns false on a blocking error.</summary>
        public static bool Build(bool save = true)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Leave Play mode before building the cutscene cameras.");
                return false;
            }
            var scene = SceneManager.GetActiveScene();
            if (scene.path != PipelinePaths.Scene)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PipelinePaths.Scene) == null)
                {
                    Debug.LogError("No opening scene. Run " + CutsceneMenus.BuildScene + " first.");
                    return false;
                }
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
                scene = EditorSceneManager.OpenScene(PipelinePaths.Scene, OpenSceneMode.Single);
            }

            var report = new BuildReport();
            var table = EnsureTable(report);
            var ctx = OpeningSceneContext.Gather(report);
            if (ctx.Director == null || ctx.Player == null || ctx.Camera == null)
            {
                Debug.LogError("The opening scene has no Cutscene root or main camera. Run " + CutsceneMenus.BuildScene + ".");
                return false;
            }

            var slots = ReadSlots(report);
            var root = FindOrCreateRoot();
            var context = Ensure<CutsceneCameraContext>(root.gameObject);
            ConfigureContext(context, ctx, report);
            var brain = EnsureBrain(ctx.Camera, ctx.Player, report);
            var aim = Child(root, CameraAssetPaths.AimTargetName);
            var hold = BuildHold(root, ctx.Camera);
            Ensure<CutsceneCameraHandback>(root.gameObject).Configure(ctx.Player, ctx.Camera, brain, context, hold);

            var trackPath = Child(root, CameraAssetPaths.TrackPathName);
            var trackTarget = Child(root, CameraAssetPaths.TrackTargetName);
            BuildTrackSpline(trackPath, context, report);

            var keep = new HashSet<string> { aim.name, trackPath.name, trackTarget.name, hold.name };
            var byShot = new Dictionary<string, CinemachineCamera>();
            foreach (var slot in slots)
            {
                if (!table.TryGet(slot.ShotId, out var spec))
                {
                    report.Warn($"{slot.ShotId}: not in the shot table; no camera.");
                    continue;
                }
                CheckLens(slot, spec, report);
                var vcam = BuildShotCamera(root, slot, spec, table, context, ctx, aim, trackPath, trackTarget);
                byShot[slot.ShotId] = vcam;
                keep.Add(vcam.name);
            }
            RemoveStale(root, keep, report);
            BuildTracks(slots, byShot, ctx.Director, brain, report);

            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (save) EditorSceneManager.SaveScene(scene);
            report.Info($"Cutscene cameras: {byShot.Count} shot cameras on {CountTimelines(slots)} timelines.");
            report.Write("Cutscene cameras built", "Logs/CutsceneCameraBuildReport.txt");
            return true;
        }

        // ── Shot slots ──────────────────────────────────────────────────────────

        /// <summary>
        /// Every shot on the six timelines: cut points from the Shots track's ShotClips, beats from
        /// the pipeline's timing layout. Warns where the two disagree.
        /// </summary>
        public static List<ShotSlot> ReadSlots(BuildReport report)
        {
            var result = new List<ShotSlot>();
            var lines = new Dictionary<string, LineSpec>();
            var script = AssetDatabase.LoadAssetAtPath<CutsceneScript>(PipelinePaths.ScriptAsset);
            if (script != null)
                for (int i = 0; i < script.LineCount; i++)
                {
                    var line = script.GetLine(i);
                    lines[line.Id] = LineSpec.FromScript(line);
                }
            else report?.Warn("No script asset: beat times unknown; camera moves use shot fractions only.");

            for (int s = 1; s <= OpeningTimingPlan.SceneCount; s++)
            {
                var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(PipelinePaths.Timeline(s));
                if (timeline == null)
                {
                    report?.Warn($"{PipelinePaths.Timeline(s)} missing: run {CutsceneMenus.BuildScene}.");
                    continue;
                }
                SceneLayout layout = null;
                try
                {
                    if (script != null) layout = TimelineLayout.Layout(s, OpeningTimingPlan.Scene(s), lines, OpeningTimelineBuilder.MinCardSeconds());
                }
                catch (ArgumentException e)
                {
                    report?.Warn($"Scene {s} layout: {e.Message}");
                }
                var shotTrack = FindShotTrack(timeline);
                if (shotTrack == null)
                {
                    report?.Warn($"OC_S{s}: no Shots track.");
                    continue;
                }
                var clips = new List<TimelineClip>(shotTrack.GetClips());
                clips.Sort((a, b) => a.start.CompareTo(b.start));
                foreach (var clip in clips)
                {
                    if (!(clip.asset is ShotClip shot)) continue;
                    var slot = new ShotSlot
                    {
                        Scene = s,
                        Timeline = timeline,
                        ShotId = shot.ShotId,
                        Clip = shot,
                        Start = clip.start,
                        Duration = clip.duration,
                        LensStart = shot.LensStart,
                        LensEnd = shot.LensEnd,
                        Beats = new float[0],
                    };
                    var span = layout != null ? layout.FindShot(shot.ShotId) : null;
                    if (span != null)
                    {
                        slot.Beats = new float[span.BeatStarts.Length];
                        for (int i = 0; i < span.BeatStarts.Length; i++)
                            slot.Beats[i] = (float)(span.BeatStarts[i] - span.Start);
                        if (Math.Abs(span.Start - clip.start) > 1e-3 || Math.Abs(span.Duration - clip.duration) > 1e-3)
                            report?.Warn($"{shot.ShotId}: the timeline's cut points ({clip.start:0.000}+{clip.duration:0.000}) differ " +
                                         $"from the timing layout ({span.Start:0.000}+{span.Duration:0.000}); rerun Rebuild Timelines.");
                    }
                    result.Add(slot);
                }
            }
            return result;
        }

        static ShotTrack FindShotTrack(TimelineAsset timeline)
        {
            foreach (var t in timeline.GetRootTracks())
                if (t is ShotTrack st && t.name == OpeningTimelineBuilder.ShotTrackName) return st;
            foreach (var t in timeline.GetRootTracks())
                if (t is ShotTrack st) return st;
            return null;
        }

        static int CountTimelines(List<ShotSlot> slots)
        {
            var set = new HashSet<TimelineAsset>();
            foreach (var s in slots) set.Add(s.Timeline);
            return set.Count;
        }

        static void CheckLens(ShotSlot slot, ShotRigSpec spec, BuildReport report)
        {
            if (Mathf.Abs(slot.LensStart - spec.lensStart) > 0.01f || Mathf.Abs(slot.LensEnd - spec.lensEnd) > 0.01f)
                report.Warn($"{slot.ShotId}: lens in the table ({spec.lensStart}→{spec.lensEnd}) differs from the ShotClip " +
                            $"({slot.LensStart}→{slot.LensEnd}).");
        }

        // ── Assets ──────────────────────────────────────────────────────────────

        static CutsceneShotTable EnsureTable(BuildReport report)
        {
            EnsureFolder(CameraAssetPaths.Dir);
            var handheld = CopyOnce<NoiseSettings>(CameraAssetPaths.PresetHandheld, CameraAssetPaths.NoiseHandheld);
            var shake = CopyOnce<NoiseSettings>(CameraAssetPaths.PresetShake, CameraAssetPaths.NoiseShake);
            var table = AssetDatabase.LoadAssetAtPath<CutsceneShotTable>(CameraAssetPaths.ShotTable);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<CutsceneShotTable>();
                AssetDatabase.CreateAsset(table, CameraAssetPaths.ShotTable);
            }
            int before = table.SeedVersion;
            int n = table.Merge(OpeningShotSeed.Build(), OpeningShotSeed.Version, handheld, shake);
            if (before != OpeningShotSeed.Version && before != 0)
                report.Warn($"Shot table reseeded (seed v{before} → v{OpeningShotSeed.Version}): tuning on it was replaced.");
            else if (n > 0) report.Info($"Shot table: {n} shot(s) seeded.");
            EditorUtility.SetDirty(table);
            return table;
        }

        static T CopyOnce<T>(string from, string to) where T : Object
        {
            var existing = AssetDatabase.LoadAssetAtPath<T>(to);
            if (existing != null) return existing;
            if (!AssetDatabase.CopyAsset(from, to)) Debug.LogWarning($"Couldn't copy {from} to {to}.");
            return AssetDatabase.LoadAssetAtPath<T>(to);
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
        }

        /// <summary>The DoF profile for an f-number and lens: Bokeh, focal length from the lens (f = 12 / tan(vFOV/2)).</summary>
        static VolumeProfile EnsureDofProfile(float aperture, float fov)
        {
            string path = CameraAssetPaths.DofProfile(aperture, fov);
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, path);
            }
            if (!profile.TryGet(out DepthOfField dof))
            {
                dof = profile.Add<DepthOfField>(true);
                dof.name = nameof(DepthOfField);
                dof.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(dof, profile);
            }
            dof.active = true;
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(10f);
            dof.aperture.Override(aperture);
            dof.focalLength.Override(Mathf.Round(CameraMath.FocalLengthMm(fov) * 10f) / 10f);
            dof.bladeCount.Override(6);
            EditorUtility.SetDirty(dof);
            EditorUtility.SetDirty(profile);
            return profile;
        }

        // ── Scene objects ───────────────────────────────────────────────────────

        static Transform FindOrCreateRoot()
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
                if (go.name == CameraAssetPaths.RootName) return go.transform;
            var root = new GameObject(CameraAssetPaths.RootName);
            return root.transform;
        }

        static Transform Child(Transform parent, string name)
        {
            var t = parent.Find(name);
            if (t == null)
            {
                t = new GameObject(name).transform;
                t.SetParent(parent, false);
            }
            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            t.localScale = Vector3.one;
            return t;
        }

        static T Ensure<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c != null ? c : go.AddComponent<T>();
        }

        static void Remove<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            if (c != null) Object.DestroyImmediate(c);
        }

        static void ConfigureContext(CutsceneCameraContext context, OpeningSceneContext ctx, BuildReport report)
        {
            var markers = new List<(string id, Transform t)>();
            foreach (string id in ctx.MarkerIds) markers.Add((id, ctx.Marker(id)));
            markers.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            var ids = new string[markers.Count];
            var transforms = new Transform[markers.Count];
            for (int i = 0; i < markers.Count; i++)
            {
                ids[i] = markers[i].id;
                transforms[i] = markers[i].t;
            }
            var orbit = ctx.Camera.GetComponent<SandboxOrbitCamera>();
            if (orbit == null) orbit = Object.FindFirstObjectByType<SandboxOrbitCamera>(FindObjectsInactive.Include);
            var handoff = new SerializedObject(ctx.Player).FindProperty("cameraHandoff");
            var follow = handoff.FindPropertyRelative("followTarget").objectReferenceValue as Transform;
            var mc = ctx.Actor(CutsceneSpeakers.MC);
            var birdy = ctx.Actor(CutsceneSpeakers.Birdy);
            if (mc == null) report.Warn("No MC CutsceneActor: MC-relative shots fall back to the origin.");
            if (birdy == null) report.Warn("No BIRDY CutsceneActor: Birdy-relative shots fall back to the origin.");
            if (orbit == null) report.Warn("No SandboxOrbitCamera: pose G uses the orbit camera's defaults.");
            context.Configure(mc, birdy, ctx.Propeller, ids, transforms, ctx.Director, orbit,
                follow != null ? follow : (mc != null ? mc.transform : null), ctx.Camera.fieldOfView);
            EditorUtility.SetDirty(context);

            var rest = OrbitCameraAccess.Rest(orbit);
            var target = OrbitCameraAccess.Target(orbit);
            report.Info($"Pose G from SandboxOrbitCamera ({(orbit != null ? orbit.name : "defaults")}): targetOffset {rest.TargetOffset}, " +
                        $"distance {rest.Distance} m, resting pitch (lockPitch) {rest.Pitch}°, safe band {(rest.SafeBand ? "on" : "off")} " +
                        $"[{rest.SafeBandMin}, {rest.SafeBandMax}], FOV {ctx.Camera.fieldOfView}°, target {(target != null ? target.name : "none")}.");
        }

        static CinemachineBrain EnsureBrain(Camera camera, CutscenePlayer player, BuildReport report)
        {
            var brain = Ensure<CinemachineBrain>(camera.gameObject);
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.LateUpdate;
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.Cut, 0f);
            brain.IgnoreTimeScale = false;
            brain.ShowDebugText = false;
            brain.enabled = false;
            EditorUtility.SetDirty(brain);

            var so = new SerializedObject(player);
            var list = so.FindProperty("cameraHandoff").FindPropertyRelative("cutsceneOnly");
            bool present = false;
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == brain) present = true;
            if (!present)
            {
                list.arraySize++;
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = brain;
                so.ApplyModifiedPropertiesWithoutUndo();
                report.Info("CinemachineBrain added to CameraHandoff.cutsceneOnly.");
            }
            return brain;
        }

        /// <summary>The freeze-frame camera: top priority, so the brain shows it whenever no track drives it.</summary>
        static CutsceneHoldCamera BuildHold(Transform root, Camera camera)
        {
            var go = Child(root, CameraAssetPaths.HoldName).gameObject;
            var vcam = Ensure<CinemachineCamera>(go);
            vcam.Priority = 1000;
            vcam.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Never;
            var lens = LensSettings.Default;
            lens.FieldOfView = camera.fieldOfView;
            lens.NearClipPlane = camera.nearClipPlane;
            lens.FarClipPlane = camera.farClipPlane;
            vcam.Lens = lens;
            vcam.Target = new CameraTarget();
            var hold = Ensure<CutsceneHoldCamera>(go);
            EditorUtility.SetDirty(vcam);
            EditorUtility.SetDirty(hold);
            return hold;
        }

        static void BuildTrackSpline(Transform path, CutsceneCameraContext context, BuildReport report)
        {
            var container = Ensure<SplineContainer>(path.gameObject);
            if (!context.TryGetMarker(CutsceneMarkerIds.CamTrackS4Start, out var a, out _) ||
                !context.TryGetMarker(CutsceneMarkerIds.CamTrackS4End, out var b, out _))
            {
                report.Warn("Cam_Track_S4_Start/End markers missing: the scene-4 track has no spline.");
                return;
            }
            var spline = container.Spline;
            bool same = spline.Count == 2 && (Vector3)spline[0].Position == a && (Vector3)spline[1].Position == b;
            if (!same)
            {
                spline.Clear();
                spline.Add(new BezierKnot(a), TangentMode.Linear);
                spline.Add(new BezierKnot(b), TangentMode.Linear);
            }
            EditorUtility.SetDirty(container);
        }

        static CinemachineCamera BuildShotCamera(Transform root, ShotSlot slot, ShotRigSpec spec, CutsceneShotTable table,
            CutsceneCameraContext context, OpeningSceneContext ctx, Transform aim, Transform trackPath, Transform trackTarget)
        {
            var t = Child(root, slot.ShotId);
            var go = t.gameObject;
            var vcam = Ensure<CinemachineCamera>(go);
            vcam.Priority = 0;
            vcam.StandbyUpdate = CinemachineVirtualCameraBase.StandbyUpdateMode.Never;
            var lens = LensSettings.Default;
            lens.FieldOfView = spec.lensStart;
            // Close-ups and inserts need a nearer clip than gameplay's; the hand-off shot keeps
            // gameplay's so the MATCH is identical. The hand-back restores the camera's own.
            lens.NearClipPlane = spec.body == BodyKind.Gameplay ? ctx.Camera.nearClipPlane : Mathf.Min(ctx.Camera.nearClipPlane, CloseNearClip);
            lens.FarClipPlane = ctx.Camera.farClipPlane;
            vcam.Lens = lens;

            Transform follow = null;
            switch (spec.body)
            {
                case BodyKind.Orbit:
                case BodyKind.Follow:
                    follow = BasisTransform(spec.followTarget, context);
                    break;
                case BodyKind.Track:
                    follow = trackTarget;
                    break;
            }
            vcam.Target = new CameraTarget
            {
                TrackingTarget = follow,
                LookAtTarget = spec.aim == AimMode.Composer ? aim : null,
                CustomLookAtTarget = true,
            };

            // Body.
            if (spec.body == BodyKind.Orbit)
            {
                var orbit = Ensure<CinemachineOrbitalFollow>(go);
                orbit.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
                orbit.TrackerSettings = TrackerSettings.Default;
                orbit.TrackerSettings.BindingMode = BindingMode.WorldSpace;
                orbit.TrackerSettings.PositionDamping = Vector3.zero;
                orbit.HorizontalAxis.Range = new Vector2(-180f, 180f);
                orbit.HorizontalAxis.Wrap = true;
                orbit.HorizontalAxis.Recentering.Enabled = false;
                orbit.VerticalAxis.Range = new Vector2(-89f, 89f);
                orbit.VerticalAxis.Wrap = false;
                orbit.VerticalAxis.Recentering.Enabled = false;
            }
            else Remove<CinemachineOrbitalFollow>(go);

            if (spec.body == BodyKind.Follow)
            {
                var f = Ensure<CinemachineFollow>(go);
                f.TrackerSettings = TrackerSettings.Default;
                f.TrackerSettings.BindingMode = BindingMode.WorldSpace;
                f.TrackerSettings.PositionDamping = spec.followDamping;
            }
            else Remove<CinemachineFollow>(go);

            if (spec.body == BodyKind.Track)
            {
                var dolly = Ensure<CinemachineSplineDolly>(go);
                dolly.Spline = trackPath.GetComponent<SplineContainer>();
                dolly.PositionUnits = PathIndexUnit.Normalized;
                dolly.CameraRotation = CinemachineSplineDolly.RotationMode.Default;
                dolly.Damping.Enabled = false;
                dolly.AutomaticDolly.Enabled = true;
                if (!(dolly.AutomaticDolly.Method is SplineAutoDolly.NearestPointToTarget))
                    dolly.AutomaticDolly.Method = new SplineAutoDolly.NearestPointToTarget();
            }
            else Remove<CinemachineSplineDolly>(go);

            // Aim.
            if (spec.aim == AimMode.Composer)
            {
                var c = Ensure<CinemachineRotationComposer>(go);
                c.Composition = ScreenComposerSettings.Default;
                c.Composition.DeadZone.Enabled = false;
                c.Composition.HardLimits.Enabled = false;
                c.Damping = Vector2.zero;
                c.Lookahead = new LookaheadSettings();
                c.CenterOnActivate = true;
                c.TargetOffset = Vector3.zero;
            }
            else Remove<CinemachineRotationComposer>(go);

            // Noise.
            if (spec.noise != NoiseKind.None)
            {
                var n = Ensure<CinemachineBasicMultiChannelPerlin>(go);
                n.NoiseProfile = table.Noise(spec.noise);
                n.AmplitudeGain = spec.noiseAmplitude;
                n.FrequencyGain = spec.noiseFrequency;
                n.PivotOffset = Vector3.zero;
            }
            else Remove<CinemachineBasicMultiChannelPerlin>(go);

            // The rig, then depth of field (its focus is set by the rig).
            var rig = Ensure<ShotRig>(go);
            rig.Configure(table, slot.ShotId, context, slot.Timeline, slot.Start, (float)slot.Duration, slot.Beats,
                spec.body == BodyKind.Track ? trackTarget : null);

            if (spec.aperture > 0f)
            {
                var v = Ensure<CinemachineVolumeSettings>(go);
                v.Weight = 1f;
                v.Profile = EnsureDofProfile(spec.aperture, spec.lensStart);
                v.FocusTracking = CinemachineVolumeSettings.FocusTrackingMode.Camera;
                v.FocusTarget = null;
                v.FocusOffset = 10f;
            }
            else Remove<CinemachineVolumeSettings>(go);

            t.localPosition = Vector3.zero;
            t.localRotation = Quaternion.identity;
            foreach (var c in go.GetComponents<Component>()) EditorUtility.SetDirty(c);
            return vcam;
        }

        /// <summary>The transform behind an anchor basis (Follow and Orbit targets).</summary>
        static Transform BasisTransform(RigBasis basis, CutsceneCameraContext context)
        {
            CutsceneAnchor anchor;
            CutsceneActor actor;
            switch (basis)
            {
                case RigBasis.MCHead: actor = context.MC; anchor = CutsceneAnchor.Head; break;
                case RigBasis.MCFeet: actor = context.MC; anchor = CutsceneAnchor.Feet; break;
                case RigBasis.BirdyHead: actor = context.Birdy; anchor = CutsceneAnchor.Head; break;
                case RigBasis.BirdyFeet: actor = context.Birdy; anchor = CutsceneAnchor.Feet; break;
                case RigBasis.PropRoot: return context.Propeller;
                default: return null;
            }
            if (actor == null) return null;
            var t = actor.GetAnchor(anchor);
            return t != null ? t : actor.transform;
        }

        static void RemoveStale(Transform root, HashSet<string> keep, BuildReport report)
        {
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (keep.Contains(child.name)) continue;
                report.Info($"Removed stale camera object {child.name}.");
                Object.DestroyImmediate(child.gameObject);
            }
            // Children in shot order, so the hierarchy reads like the shot list.
        }

        // ── Timelines ───────────────────────────────────────────────────────────

        static void BuildTracks(List<ShotSlot> slots, Dictionary<string, CinemachineCamera> byShot,
            PlayableDirector director, CinemachineBrain brain, BuildReport report)
        {
            var byTimeline = new Dictionary<TimelineAsset, List<ShotSlot>>();
            var order = new List<TimelineAsset>();
            foreach (var s in slots)
            {
                if (!byTimeline.TryGetValue(s.Timeline, out var list))
                {
                    list = new List<ShotSlot>();
                    byTimeline[s.Timeline] = list;
                    order.Add(s.Timeline);
                }
                list.Add(s);
            }
            foreach (var timeline in order)
            {
                var shots = byTimeline[timeline];
                var shotTrack = FindShotTrack(timeline);
                if (shotTrack != null && shotTrack.DrivePlaceholderCamera)
                {
                    shotTrack.DrivePlaceholderCamera = false;
                    EditorUtility.SetDirty(shotTrack);
                }
                var group = FindRoot<GroupTrack>(timeline, CameraAssetPaths.GroupName) ??
                            timeline.CreateTrack<GroupTrack>(null, CameraAssetPaths.GroupName);
                CinemachineTrack track = null;
                foreach (var child in group.GetChildTracks())
                    if (child is CinemachineTrack ct && child.name == CameraAssetPaths.TrackName) track = ct;
                track ??= timeline.CreateTrack<CinemachineTrack>(group, CameraAssetPaths.TrackName);

                var clips = new List<TimelineClip>(track.GetClips());
                clips.Sort((a, b) => a.start.CompareTo(b.start));
                if (clips.Count != shots.Count)
                {
                    foreach (var c in clips) timeline.DeleteClip(c);
                    clips.Clear();
                    for (int i = 0; i < shots.Count; i++) clips.Add(track.CreateClip<CinemachineShot>());
                }
                for (int i = 0; i < shots.Count; i++)
                {
                    var slot = shots[i];
                    var clip = clips[i];
                    clip.start = slot.Start;
                    clip.duration = slot.Duration;
                    clip.displayName = slot.ShotId;
                    clip.easeInDuration = 0;
                    clip.easeOutDuration = 0;
                    var shot = (CinemachineShot)clip.asset;
                    shot.DisplayName = slot.ShotId;
                    var name = new PropertyName(CameraAssetPaths.RefPrefix + slot.ShotId);
                    shot.VirtualCamera = new ExposedReference<CinemachineVirtualCameraBase> { exposedName = name };
                    if (byShot.TryGetValue(slot.ShotId, out var vcam)) director.SetReferenceValue(name, vcam);
                    else report.Warn($"{slot.ShotId}: no camera for its CinemachineShot.");
                    EditorUtility.SetDirty(shot);
                }
                director.SetGenericBinding(track, brain);
                EditorUtility.SetDirty(track);
                EditorUtility.SetDirty(timeline);
            }
            EditorUtility.SetDirty(director);
        }

        static T FindRoot<T>(TimelineAsset timeline, string name) where T : TrackAsset
        {
            foreach (var t in timeline.GetRootTracks())
                if (t is T typed && t.name == name) return typed;
            return null;
        }
    }
}
