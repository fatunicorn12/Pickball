using System;
using System.Collections.Generic;
using System.IO;
using Pickleball.Cutscene.Editor.Pipeline;
using Pickleball.Sandbox;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Pickleball.Cutscene.Cameras.Editor
{
    /// <summary>
    /// <c>Tools ▸ Pickleball ▸ Cutscene ▸ Validate Framing</c>: evaluates every shot of the six
    /// timelines at ten points in edit mode (director evaluated, brain updated by hand with damping
    /// off), reads the main camera, and applies the shot list's automated checks through
    /// <see cref="FramingChecks"/>:
    /// (a) each subject anchor inside the 90% safe frame (shrunk by the shot's worst noise wobble);
    /// (b) Birdy's TCU fraction ≥ 0.30 with his eyes in frame outside the allowed windows;
    /// (c) the lens inside or within the near clip of a character's bounds (skinned meshes baked);
    /// (d) S6's final frame against pose G (1 cm, 0.1°, same FOV) and G's line of sight, and S2's
    /// blend target. Writes <c>Logs/CutsceneFramingReport.txt</c>. The scene is reloaded afterwards,
    /// so evaluation leaves nothing behind.
    /// </summary>
    public static class FramingValidator
    {
        /// <summary>Samples per shot.</summary>
        public const int SamplesPerShot = 10;

        /// <summary>Minimum degrees the black sun stays outside every scene-5 frame.</summary>
        public const float BlackSunMargin = 5f;

        /// <summary>
        /// Minimum degrees the black sun stays outside the frame in every shot before the scene-6
        /// gaze (S1_SH01–S5_SH03); its first on-screen appearance is S6_SH02. S6_SH01 keeps it just
        /// above the frame (0°).
        /// </summary>
        public const float BlackSunHideMargin = 2f;

        /// <summary>Minimum Birdy head fraction (frac) inside an allowed TCU window: the shot must really be tight.</summary>
        public const float TcuMinFraction = 0.40f;

        /// <summary>Grounding tolerance, metres: the Feet anchor vs the ground under it (less the controller's rest lift).</summary>
        public const float GroundTolerance = 0.03f;

        /// <summary>The lowest toe bone may sit this far below / above the actor root while grounded, metres.</summary>
        public const float ToeBelowRoot = 0.04f, ToeAboveRoot = 0.08f;

        /// <summary>The toe band scales with the actor: below = max(ToeBelowRoot, ½·this·H), above = max(ToeAboveRoot, this·H).</summary>
        public const float ToeBandH = 0.05f;

        /// <summary>Body capsule radii for the MC/Birdy overlap check, as fractions of H and of Birdy's height.</summary>
        public const float McRadiusH = 0.15f, BirdyRadiusH = 0.24f;

        static readonly string[] McAirborne = { "lie still", "eyes open", "sit up", "fist pump" };
        static readonly string[] BirdyGrounded = { "point up", "tiny wing spread", "sad droop", "close lean-in", "snatch", "propeller click" };

        /// <summary>(i) Camera jump check: samples per second within a shot.</summary>
        public const float JumpSampleRate = 30f;

        /// <summary>(i) A step larger than this between two 1/30 s samples is a jump, metres.</summary>
        public const float JumpMaxStep = 0.25f;

        /// <summary>(i) A turn larger than this between two 1/30 s samples is a jump, degrees.</summary>
        public const float JumpMaxTurn = 10f;

        /// <summary>(i) A step shorter than this between two 1/30 s samples can't read as a stutter, metres.</summary>
        public const float StutterMinStep = 0.05f;

        /// <summary>(i) A step this many times its neighbours' median is a stutter.</summary>
        public const float StutterRatio = 4f;

        /// <summary>(i) How many samples each side count as a step's neighbours.</summary>
        public const int StutterHalfWindow = 4;

        /// <summary>(j) The subtitle band: the bottom of the frame, as a fraction of its height.</summary>
        public const float SubtitleBandHeight = 0.22f;

        /// <summary>(j) The subtitle band's width, centred, as a fraction of the frame width.</summary>
        public const float SubtitleBandWidth = 0.60f;

        /// <summary>Void dome radius round MC_Start, metres; scene-1 cameras stay inside.</summary>
        public const float DomeRadius = 40f;

        /// <summary>Menu entry.</summary>
        [MenuItem(CameraAssetPaths.ValidateMenu, false, 60)]
        public static void Menu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("Leave Play mode before validating framing.");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var report = Run();
            if (report == null) return;
            string msg = $"Framing validation: {report.Flags.Count} flag(s), {report.Warnings.Count} warning(s). See {CameraAssetPaths.FramingReport}.";
            if (report.Flags.Count > 0) Debug.LogWarning(msg);
            else Debug.Log(msg);
        }

        /// <summary>
        /// Opens the opening scene if needed, validates, writes the report and reloads the scene
        /// from disk. Null when the scene or cameras are missing.
        /// </summary>
        public static FramingReport Run(bool reload = true)
        {
            var scene = SceneManager.GetActiveScene();
            if (scene.path != PipelinePaths.Scene)
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PipelinePaths.Scene) == null)
                {
                    Debug.LogError("No opening scene. Run " + CutsceneMenus.BuildScene + " first.");
                    return null;
                }
                scene = EditorSceneManager.OpenScene(PipelinePaths.Scene, OpenSceneMode.Single);
            }
            FramingReport report;
            try
            {
                report = Validate();
            }
            finally
            {
                if (reload) EditorSceneManager.OpenScene(PipelinePaths.Scene, OpenSceneMode.Single);
            }
            report?.Write($"Opening cutscene framing report ({DateTime.Now:yyyy-MM-dd HH:mm})", CameraAssetPaths.FramingReport);
            return report;
        }

        /// <summary>Evaluates one shot-local time and returns the rendered pose and FOV.</summary>
        public static bool Evaluate(ShotSlot slot, float local, PlayableDirector director, CinemachineBrain brain,
            Camera camera, CinemachineCamera expected, ref int frame, out Pose pose, out float fov, out bool wired)
        {
            if (director.playableAsset != slot.Timeline)
            {
                director.playableAsset = slot.Timeline;
                director.RebuildGraph();
            }
            director.time = slot.Start + local;
            director.Evaluate();
            brain.ManualUpdate(frame++, -1f);
            wired = ReferenceEquals(brain.ActiveVirtualCamera as Object, expected);
            if (!wired && expected != null)
            {
                // Fall back to the shot's own camera so the framing is still checked.
                expected.InternalUpdateCameraState(Vector3.up, -1f);
                var s = expected.State;
                pose = new Pose(s.GetFinalPosition(), s.GetFinalOrientation());
                fov = s.Lens.FieldOfView;
                return true;
            }
            var t = camera.transform;
            pose = new Pose(t.position, t.rotation);
            fov = camera.fieldOfView;
            return true;
        }

        /// <summary>Everything the validator needs from the open scene.</summary>
        public sealed class Setup
        {
            /// <summary>Rig context.</summary>
            public CutsceneCameraContext Context;
            /// <summary>Shot table.</summary>
            public CutsceneShotTable Table;
            /// <summary>Shot slots.</summary>
            public List<ShotSlot> Slots;
            /// <summary>Rigs by shot id.</summary>
            public Dictionary<string, ShotRig> Rigs;
            /// <summary>Director.</summary>
            public PlayableDirector Director;
            /// <summary>Main camera.</summary>
            public Camera Camera;
            /// <summary>Brain.</summary>
            public CinemachineBrain Brain;

            /// <summary>Gathers the scene; null with an error logged when cameras aren't built.</summary>
            public static Setup Gather()
            {
                var root = GameObject.Find(CameraAssetPaths.RootName);
                var context = root != null ? root.GetComponent<CutsceneCameraContext>() : null;
                var table = AssetDatabase.LoadAssetAtPath<CutsceneShotTable>(CameraAssetPaths.ShotTable);
                if (context == null || table == null)
                {
                    Debug.LogError("No cutscene cameras in the scene. Run " + CutsceneMenus.BuildCameras + " first.");
                    return null;
                }
                var setup = new Setup
                {
                    Context = context,
                    Table = table,
                    Slots = CutsceneCameraBuilder.ReadSlots(null),
                    Rigs = new Dictionary<string, ShotRig>(),
                    Director = context.Director,
                };
                foreach (var rig in root.GetComponentsInChildren<ShotRig>(true)) setup.Rigs[rig.ShotId] = rig;
                var handback = root.GetComponent<CutsceneCameraHandback>();
                setup.Camera = Camera.main;
                setup.Brain = setup.Camera != null ? setup.Camera.GetComponent<CinemachineBrain>() : null;
                if (setup.Director == null || setup.Brain == null)
                {
                    Debug.LogError("Validation needs the director and the CinemachineBrain on the main camera.");
                    return null;
                }
                return setup;
            }

            /// <summary>Puts the brain in manual update and the camera at the composition aspect.</summary>
            public void Begin()
            {
                Brain.enabled = true;
                Brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
                Camera.aspect = Table.Aspect;
                // Initialise the actors' Animators and build the graph before the first sample. In a
                // scene built or reopened in the same editor tick, Evaluate only rebuilt the graph when
                // the timeline changed, and scene 1 was sampled with MC frozen in his default pose
                // (63 cm "off the ground"; review round 4).
                foreach (var actor in new[] { Context.MC, Context.Birdy })
                {
                    var animator = actor != null ? actor.GetComponentInChildren<Animator>(true) : null;
                    if (animator != null) animator.Rebind();
                }
                Director.RebuildGraph();
                Director.Evaluate();
            }
        }

        static FramingReport Validate()
        {
            var setup = Setup.Gather();
            if (setup == null) return null;
            setup.Begin();
            var report = new FramingReport();
            var scene = new RigScene();
            var bounds = new ActorBounds();
            var heads = new HeadScreen();
            int frame = 1;
            var context = setup.Context;
            bool wiringWarned = false;

            foreach (var slot in setup.Slots)
            {
                if (!setup.Rigs.TryGetValue(slot.ShotId, out var rig) || rig.Spec == null)
                {
                    report.Flag($"{slot.ShotId}: no camera rig.");
                    continue;
                }
                var spec = rig.Spec;
                var vcam = rig.GetComponent<CinemachineCamera>();
                float dur = (float)slot.Duration;
                float noiseDeg = NoiseDegrees(setup.Table, spec, slot.Beats, dur);
                float minX = 1f, maxX = 0f, minY = 1f, maxY = 0f, maxFrac = 0f, minClear = float.MaxValue;
                int occluded = 0, subjectSamples = 0;
                string occluder = null;

                for (int k = 0; k < SamplesPerShot; k++)
                {
                    float tl = dur * (k + 0.5f) / SamplesPerShot;
                    Evaluate(slot, tl, setup.Director, setup.Brain, setup.Camera, vcam, ref frame, out var pose, out float fov, out bool wired);
                    report.Samples++;
                    bounds.Clear();
                    if (!wired && !wiringWarned)
                    {
                        report.Warn($"{slot.ShotId} @ {tl:0.00}s: the brain shows {Name(setup.Brain.ActiveVirtualCamera)}, not {slot.ShotId}; " +
                                    "checked on the shot's own camera instead (edit-mode timeline evaluation).");
                        wiringWarned = true;
                    }
                    context.Sample(scene);
                    float margin = FramingChecks.NoiseMargin(noiseDeg, fov);

                    // (a) subjects in the safe frame.
                    if (spec.subjects != null)
                        foreach (var sw in spec.subjects)
                        {
                            float from = sw.from.Resolve(slot.Beats, dur), to = sw.to.Resolve(slot.Beats, dur);
                            if (tl < from - 1e-4f || tl > to + 1e-4f) continue;
                            if (!Present(sw.subject, scene)) continue;
                            Vector3 p = RigSolver.Basis(sw.subject, null, scene);
                            Vector3 vp = CameraMath.Viewport(pose.position, pose.rotation, fov, setup.Table.Aspect, p);
                            subjectSamples++;
                            minX = Mathf.Min(minX, vp.x); maxX = Mathf.Max(maxX, vp.x);
                            minY = Mathf.Min(minY, vp.y); maxY = Mathf.Max(maxY, vp.y);
                            if (!FramingChecks.InSafeFrame(vp, setup.Table.SafeFrame, margin))
                                report.Flag($"(a) {slot.ShotId} @ {tl:0.00}s: {sw.subject} at ({vp.x:0.000}, {vp.y:0.000}, depth {vp.z:0.00}) " +
                                            $"outside the safe frame{(margin > 0f ? $" (noise margin {margin:0.000})" : "")}.");
                            if (OccludedBy(bounds, context, sw.subject, pose.position, p, out string who))
                            {
                                occluded++;
                                occluder = who;
                            }
                        }

                    // (b) Birdy's TCU fraction.
                    if (scene.Birdy.Valid)
                    {
                        Vector3 head = CameraMath.Viewport(pose.position, pose.rotation, fov, setup.Table.Aspect, scene.Birdy.Head);
                        Vector3 eyes = CameraMath.Viewport(pose.position, pose.rotation, fov, setup.Table.Aspect, scene.Birdy.Eyes);
                        // On-screen head height (head and helmet skin, projected) when Birdy has
                        // head-weighted skin; HB at the head centre's depth for a stand-in.
                        float frac = head.z > 0f ? CameraMath.TcuFraction(scene.HB, head.z, fov) : 0f;
                        if (context.Birdy != null)
                        {
                            float onScreen = heads.Fraction(context.Birdy, pose.position, pose.rotation, fov, setup.Table.Aspect);
                            if (onScreen >= 0f) frac = onScreen;
                        }
                        bool eyesIn = FramingChecks.InFrame(eyes);
                        if (eyesIn) maxFrac = Mathf.Max(maxFrac, frac);
                        bool allowed = Allowed(spec, slot, tl);
                        if (FramingChecks.TcuViolation(frac, eyesIn, allowed, setup.Table.TcuFlagFraction))
                            report.Flag($"(b) {slot.ShotId} @ {tl:0.00}s: Birdy frac {frac:0.000} (eyes in frame) outside an allowed TCU.");
                        // (b') the reverse: an allowed TCU must actually be tight, eyes in frame.
                        if (InTcuPortion(spec, slot, tl) && (frac < TcuMinFraction || !eyesIn))
                            report.Flag($"(b') {slot.ShotId} @ {tl:0.00}s: allowed TCU is not tight: frac {frac:0.000}{(eyesIn ? "" : ", eyes out of frame")} (wants ≥ {TcuMinFraction:0.00}).");
                    }

                    // (c) lens inside a character.
                    float near = vcam != null ? vcam.Lens.NearClipPlane : setup.Camera.nearClipPlane;
                    foreach (var actor in new[] { context.MC, context.Birdy })
                    {
                        if (actor == null) continue;
                        if (!FramingChecks.LensTooClose(bounds.Get(actor), pose.position, near))
                        {
                            minClear = Mathf.Min(minClear, Mathf.Sqrt(bounds.Get(actor).SqrDistance(pose.position)));
                            continue;
                        }
                        bool inside = bounds.LensInside(actor, pose.position, near, out float clear);
                        minClear = Mathf.Min(minClear, clear);
                        if (inside)
                            report.Flag($"(c) {slot.ShotId} @ {tl:0.00}s: the lens is inside {actor.ActorId} or within the {near:0.00} m " +
                                        $"near clip of its surface (surface {clear:0.000} m away; lens at {pose.position.ToString("F2")}).");
                    }

                    // (e) the black sun is revealed by the scene-6 gaze: out of frame before it.
                    // In the void (look blend 0) the opaque dome hides it whatever the framing.
                    if ((slot.Scene <= 5 || slot.ShotId == "S6_SH01") && !VoidUp() &&
                        context.TryGetMarker(CutsceneMarkerIds.BlackSun, out var sun, out _))
                    {
                        // Measured to the disc's outer edge (its rim ring), not its centre.
                        float outside = FramingChecks.DegreesOutsideFrame(pose.position, pose.rotation, fov, setup.Table.Aspect, sun)
                                        - SunAngularRadius(sun, pose.position);
                        float need = slot.ShotId == "S6_SH01" ? 0f : BlackSunHideMargin;
                        if (outside < need)
                            report.Flag($"(e) {slot.ShotId} @ {tl:0.00}s: the black sun is {outside:0.0}° outside the frame edge (negative = inside; wants ≥ {need:0}° before S6_SH02).");
                        else if (slot.Scene == 5 && outside < BlackSunMargin)
                            report.Warn($"{slot.ShotId} @ {tl:0.00}s: the black sun is only {outside:0.0}° outside the frame (wants ≥ {BlackSunMargin}°).");
                    }

                    // (f) grounded actors stand on the ground, (h) Birdy hovers when he should;
                    // (g) MC and Birdy never overlap.
                    CheckGrounding(slot, tl, context, report);
                    CheckHover(slot, tl, context, report);
                    CheckOverlap(slot, tl, context, report);

                    // Scene 1: inside the void dome.
                    if (slot.Scene == 1 && context.TryGetMarker(CutsceneMarkerIds.MCStart, out var start, out _))
                    {
                        float d = Vector3.Distance(pose.position, start);
                        if (d > DomeRadius - 1f) report.Warn($"{slot.ShotId} @ {tl:0.00}s: the camera is {d:0.0} m from MC_Start (dome radius {DomeRadius} m).");
                    }
                }

                // (i) no camera jumps, (j) no subject head under a subtitle card; both at 30 Hz.
                CheckMotionAndCards(slot, spec, setup, vcam, scene, report, ref frame);

                if (subjectSamples > 0 && occluded * 3 > subjectSamples)
                    report.Warn($"{slot.ShotId}: a subject is behind {occluder}'s bounds in {occluded}/{subjectSamples} subject samples.");
                report.Shot($"{slot.ShotId,-9} {SamplesPerShot,7}  {minX:0.00}–{maxX:0.00}      {minY:0.00}–{maxY:0.00}    {maxFrac,8:0.000}  {(minClear < float.MaxValue ? minClear : 0f),8:0.00}");
            }

            CheckHandoffs(setup, report, ref frame);
            bounds.Dispose();
            heads.Dispose();
            return report;
        }

        static string Name(ICinemachineCamera cam) => cam != null ? cam.Name : "(none)";

        /// <summary>
        /// (i) Samples the shot at <see cref="JumpSampleRate"/> Hz and flags a stutter (a step far
        /// longer than its neighbours, on any camera without handheld noise) and a step of more than
        /// <see cref="JumpMaxStep"/> m or <see cref="JumpMaxTurn"/>° between neighbouring samples
        /// (review round 3: S2_SH10's crane popped twice when the lens-clearance push flipped).
        /// (j) While a subtitle card shows, flags a subject Head anchor inside the card band (bottom
        /// <see cref="SubtitleBandHeight"/> of the frame, centre <see cref="SubtitleBandWidth"/> of
        /// its width). Inserts have no head subject, so they are exempt by construction.
        /// </summary>
        static void CheckMotionAndCards(ShotSlot slot, ShotRigSpec spec, Setup setup, CinemachineCamera vcam, RigScene scene,
                                        FramingReport report, ref int frame)
        {
            float dur = (float)slot.Duration;
            int n = Mathf.Max(2, Mathf.CeilToInt(dur * JumpSampleRate));
            var cards = CardSpans(slot.Timeline);
            bool havePrev = false;
            Pose prev = default;
            int jumps = 0, inBand = 0;
            var steps = new List<float>(n);
            string firstJump = null, firstBand = null;
            for (int k = 0; k < n; k++)
            {
                // Stay a hair inside the shot so the last sample is still this shot's camera.
                float tl = Mathf.Min(k / JumpSampleRate, dur - 1e-3f);
                Evaluate(slot, tl, setup.Director, setup.Brain, setup.Camera, vcam, ref frame, out var pose, out float fov, out _);
                if (havePrev)
                {
                    float step = Vector3.Distance(prev.position, pose.position);
                    float turn = Quaternion.Angle(prev.rotation, pose.rotation);
                    steps.Add(step);
                    if (step > JumpMaxStep || turn > JumpMaxTurn)
                    {
                        jumps++;
                        firstJump ??= $"{tl:0.000}s ({step:0.00} m, {turn:0.0}°)";
                    }
                }
                prev = pose;
                havePrev = true;

                double at = slot.Start + tl;
                bool card = false;
                foreach (var c in cards)
                    if (at >= c.x && at < c.y) { card = true; break; }
                if (!card || spec.subjects == null) continue;
                setup.Context.Sample(scene);
                foreach (var sw in spec.subjects)
                {
                    if (sw.subject != RigBasis.MCHead && sw.subject != RigBasis.BirdyHead) continue;
                    float from = sw.from.Resolve(slot.Beats, dur), to = sw.to.Resolve(slot.Beats, dur);
                    if (tl < from - 1e-4f || tl > to + 1e-4f) continue;
                    if (!Present(sw.subject, scene)) continue;
                    Vector3 vp = CameraMath.Viewport(pose.position, pose.rotation, fov, setup.Table.Aspect,
                                                     RigSolver.Basis(sw.subject, null, scene));
                    if (vp.z <= 0f) continue;
                    float half = SubtitleBandWidth * 0.5f;
                    if (vp.y >= 0f && vp.y < SubtitleBandHeight && vp.x > 0.5f - half && vp.x < 0.5f + half)
                    {
                        inBand++;
                        firstBand ??= $"{tl:0.00}s: {sw.subject} at ({vp.x:0.00}, {vp.y:0.00})";
                    }
                }
            }
            if (jumps > 0)
                report.Flag($"(i) {slot.ShotId}: the camera jumps in {jumps} of {n - 1} 1/30 s steps, first at {firstJump} (max {JumpMaxStep} m / {JumpMaxTurn}°).");
            // Handheld noise is meant to be uneven, so only steady cameras are held to this.
            int stutters = spec.noiseAmplitude > 0f
                ? 0 : FramingChecks.CountStutters(steps, StutterMinStep, StutterRatio, StutterHalfWindow);
            if (stutters > 0)
            {
                float worst = 0f;
                int worstAt = 0;
                for (int i = 0; i < steps.Count; i++)
                    if (steps[i] > worst) { worst = steps[i]; worstAt = i; }
                report.Flag($"(i) {slot.ShotId}: the camera stutters in {stutters} of {steps.Count} 1/30 s steps (over {StutterMinStep} m and {StutterRatio}x its neighbours), worst {worst:0.00} m at {(worstAt + 1) / JumpSampleRate:0.00}s.");
            }
            if (inBand > 0)
                report.Flag($"(j) {slot.ShotId}: a subject head is under the subtitle card in {inBand} samples, first at {firstBand}.");
        }

        /// <summary>The scene-time spans of every subtitle card on a timeline (x = start, y = end).</summary>
        static List<Vector2> CardSpans(UnityEngine.Timeline.TimelineAsset timeline)
        {
            var spans = new List<Vector2>();
            if (timeline == null) return spans;
            foreach (var track in timeline.GetOutputTracks())
                foreach (var clip in track.GetClips())
                    if (clip.asset is Pickleball.Cutscene.Timeline.SubtitleClip)
                        spans.Add(new Vector2((float)clip.start, (float)clip.end));
            return spans;
        }

        /// <summary>
        /// Renders the requested shot-local times (shot id, seconds) to <paramref name="folder"/>,
        /// named <c>NN_Shot_t.png</c>. Edit-mode previews for iterating; Play-mode recordings are
        /// the truth (see Docs/Systems/Cutscene.md §5).
        /// </summary>
        public static List<string> RenderAt(string folder, IList<KeyValuePair<string, float>> requests, int width = 640, bool reload = true)
        {
            var files = new List<string>();
            try
            {
                var setup = Setup.Gather();
                if (setup == null) return files;
                setup.Begin();
                Directory.CreateDirectory(folder);
                int height = Mathf.RoundToInt(width / setup.Table.Aspect);
                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 };
                var ldr = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                int frame = 1, index = 0;
                var old = setup.Camera.targetTexture;
                foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                    smr.forceMatrixRecalculationPerRender = true;
                var cutscenePlayer = Object.FindFirstObjectByType<Pickleball.Cutscene.Playback.CutscenePlayer>();
                cutscenePlayer?.GameplayVisuals?.Hide();
                try
                {
                    foreach (var req in requests)
                    {
                        var slot = setup.Slots.Find(s => s.ShotId == req.Key);
                        if (slot == null) continue;
                        setup.Rigs.TryGetValue(slot.ShotId, out var rig);
                        var vcam = rig != null ? rig.GetComponent<CinemachineCamera>() : null;
                        float tl = Mathf.Clamp(req.Value, 0f, (float)slot.Duration - 1e-3f);
                        Evaluate(slot, tl, setup.Director, setup.Brain, setup.Camera, vcam, ref frame, out var pose, out float fov, out _);
                        setup.Camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
                        setup.Camera.fieldOfView = fov;
                        setup.Camera.targetTexture = rt;
                        setup.Camera.aspect = setup.Table.Aspect;
                        setup.Camera.Render();
                        Graphics.Blit(rt, ldr);
                        RenderTexture.active = ldr;
                        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        tex.Apply();
                        RenderTexture.active = null;
                        string file = Path.Combine(folder, $"{index++:00}_{slot.ShotId}_{tl:0.00}.png");
                        File.WriteAllBytes(file, tex.EncodeToPNG());
                        files.Add(file);
                    }
                }
                finally
                {
                    cutscenePlayer?.GameplayVisuals?.Restore();
                    setup.Camera.targetTexture = old;
                    Object.DestroyImmediate(rt);
                    Object.DestroyImmediate(ldr);
                    Object.DestroyImmediate(tex);
                }
            }
            finally
            {
                if (reload) EditorSceneManager.OpenScene(PipelinePaths.Scene, OpenSceneMode.Single);
            }
            return files;
        }

        /// <summary>The action verb an actor plays at a shot-local time (the clip's display name), or null.</summary>
        static string VerbAt(ShotSlot slot, string actorId, float tl)
        {
            double t = slot.Start + tl;
            foreach (var track in slot.Timeline.GetOutputTracks())
            {
                if (!(track is UnityEngine.Timeline.AnimationTrack) || track.name != actorId + " Actions") continue;
                string verb = null;
                foreach (var c in track.GetClips())
                    if (t >= c.start - 1e-4 && t <= c.end + 1e-4) verb = c.displayName;
                return verb;
            }
            return null;
        }

        /// <summary>
        /// The black sun's angular radius seen from <paramref name="eye"/>, degrees: the largest
        /// renderer within 150 m of its marker (the BlackSunRing, ~7° from the crest). A sun whose
        /// centre was 3° outside the frame still showed half its rim.
        /// </summary>
        static float SunAngularRadius(Vector3 sun, Vector3 eye)
        {
            float best = 0f;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                var b = r.bounds;
                if ((b.center - sun).sqrMagnitude > 150f * 150f) continue;
                float d = Mathf.Max(1f, (b.center - eye).magnitude);
                best = Mathf.Max(best, Mathf.Atan(Mathf.Max(b.extents.x, b.extents.y) / d) * Mathf.Rad2Deg);
            }
            return best;
        }

        /// <summary>True while the void is up (CutsceneLook blend 0): its dome hides everything past 40 m.</summary>
        static bool VoidUp()
        {
            var look = Object.FindFirstObjectByType<Pickleball.Cutscene.Sets.CutsceneLook>();
            return look != null && look.Blend <= 0.001f;
        }

        /// <summary>
        /// The tight part of an allowed-TCU window: the window, less a 0.5 s pull-out transit when the
        /// window ends before the shot does (S6_SH15 dollies out through the banned band).
        /// </summary>
        static bool InTcuPortion(ShotRigSpec spec, ShotSlot slot, float tl)
        {
            if (spec.tcuAllowed == null) return false;
            float dur = (float)slot.Duration;
            foreach (var w in spec.tcuAllowed)
            {
                float from = w.from.Resolve(slot.Beats, dur), to = w.to.Resolve(slot.Beats, dur);
                if (to < dur - 1e-3f) to -= 0.5f;
                if (tl >= from - 1e-4f && tl <= to + 1e-4f) return true;
            }
            return false;
        }

        static bool Grounded(string actorId, string verb)
        {
            if (verb == null) return false;
            if (actorId == CutsceneSpeakers.MC) return System.Array.IndexOf(McAirborne, verb) < 0;
            return System.Array.IndexOf(BirdyGrounded, verb) >= 0;
        }

        /// <summary>
        /// (f) While an actor's verb is grounded: the Feet anchor sits on the ground under it (plus the
        /// CharacterController's rest lift) within <see cref="GroundTolerance"/>, and its lowest toe bone
        /// sits within [-<see cref="ToeBelowRoot"/>, +<see cref="ToeAboveRoot"/>] of the root (the body
        /// isn't lifted off its root, as Timeline foot IK once did by 0.6 m).
        /// </summary>
        static void CheckGrounding(ShotSlot slot, float tl, CutsceneCameraContext context, FramingReport report)
        {
            foreach (var actor in new[] { context.MC, context.Birdy })
            {
                if (actor == null || !Grounded(actor.ActorId, VerbAt(slot, actor.ActorId, tl))) continue;
                Vector3 feet = actor.AnchorPosition(CutsceneAnchor.Feet);
                float lift = Pickleball.Cutscene.Editor.Pipeline.OpeningTimelineBuilder.RestLift(actor);
                // The ground under the root (the mark): the Feet anchor can sit a few cm ahead of it,
                // which on the path's slope reads as a sink that isn't there.
                Vector3 under = new Vector3(actor.transform.position.x, feet.y, actor.transform.position.z);
                if (GroundBelow(actor, under, out float ground))
                {
                    float d = feet.y - ground - lift;
                    if (Mathf.Abs(d) > GroundTolerance)
                        report.Flag($"(f) {slot.ShotId} @ {tl:0.00}s: {actor.ActorId}'s feet are {d * 100f:0.0} cm {(d > 0 ? "above" : "below")} the ground.");
                }
                var anim = actor.GetComponentInChildren<Animator>();
                if (anim == null || !anim.isHuman) continue;
                // Walk cycles on the 20° olive path: no foot IK, so the stride itself sinks the
                // leading foot up to ~10 cm; only the root (Feet anchor, above) is checked there.
                string verbNow = VerbAt(slot, actor.ActorId, tl);
                if (verbNow != null && verbNow.StartsWith("walk")) continue;
                // The planted foot: of the four foot bones, the one nearest the ground under it. Without
                // foot IK a walk on the olive path's slope sinks one foot and lifts the other; the body
                // is off its mark only when even the nearest foot is.
                float off = float.PositiveInfinity;
                foreach (var b in new[] { HumanBodyBones.LeftToes, HumanBodyBones.RightToes, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                {
                    var t = anim.GetBoneTransform(b);
                    if (t == null || !GroundBelow(actor, t.position, out float g)) continue;
                    float o = t.position.y - g - lift;
                    if (Mathf.Abs(o) < Mathf.Abs(off)) off = o;
                }
                if (float.IsInfinity(off)) continue;
                // The toe bone's height above the sole is a property of the model, so the band scales
                // with height: Euler as MC (2.1 m) sits his toe bones 4.3 cm below the chibi-tuned 4 cm.
                float below = Mathf.Max(ToeBelowRoot, ToeBandH * 0.5f * actor.Height);
                float above = Mathf.Max(ToeAboveRoot, ToeBandH * actor.Height);
                if (off < -below || off > above)
                    report.Flag($"(f) {slot.ShotId} @ {tl:0.00}s: {actor.ActorId}'s planted foot bone is {off * 100f:0.0} cm off the ground (body off its mark).");
            }
        }

        /// <summary>Birdy's feet stay at least this share of his height above the ground while he hovers.</summary>
        public const float HoverMinHeight = 0.2f;

        /// <summary>
        /// (h) From S3_SH03's lift-off (0.8 s) to S6_SH16's zip-off (beat 1) Birdy hovers: his Feet
        /// anchor (on his root) is at least <see cref="HoverMinHeight"/>·h above the ground.
        /// </summary>
        static void CheckHover(ShotSlot slot, float tl, CutsceneCameraContext context, FramingReport report)
        {
            var birdy = context.Birdy;
            if (birdy == null || slot.Scene < 3) return;
            if (slot.ShotId == "S3_SH01" || slot.ShotId == "S3_SH02" || (slot.ShotId == "S3_SH03" && tl < 0.8f)) return;
            if (slot.ShotId == "S6_SH16" && slot.Beats != null && slot.Beats.Length > 1 && tl >= slot.Beats[1]) return;
            Vector3 feet = birdy.AnchorPosition(CutsceneAnchor.Feet);
            if (!GroundBelow(birdy, new Vector3(birdy.transform.position.x, feet.y + 1f, birdy.transform.position.z), out float ground)) return;
            float above = feet.y - ground;
            if (above < HoverMinHeight * birdy.Height)
                report.Flag($"(h) {slot.ShotId} @ {tl:0.00}s: Birdy's feet are {above * 100f:0} cm off the ground (hovering wants ≥ {HoverMinHeight * birdy.Height * 100f:0} cm).");
        }

        static bool GroundBelow(CutsceneActor actor, Vector3 p, out float y)
        {
            y = 0f;
            var hits = Physics.RaycastAll(p + Vector3.up * 1.5f, Vector3.down, 4.5f, ~0, QueryTriggerInteraction.Ignore);
            bool any = false;
            foreach (var h in hits)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(actor.transform)) continue;
                if (h.collider.GetComponentInParent<CutsceneActor>() != null) continue;
                if (!any || h.point.y > y) y = h.point.y;
                any = true;
            }
            return any;
        }

        /// <summary>
        /// (g) MC's and Birdy's bodies, as vertical capsules round their hips from the root to the
        /// head (radius <see cref="McRadiusH"/>·H and <see cref="BirdyRadiusH"/>·Birdy's height), never
        /// intersect. Wings, cloak and sword are allowed to cross.
        /// </summary>
        static void CheckOverlap(ShotSlot slot, float tl, CutsceneCameraContext context, FramingReport report)
        {
            var mc = context.MC;
            var birdy = context.Birdy;
            if (mc == null || birdy == null) return;
            if (!Capsule(mc, McRadiusH, out var a0, out var a1, out float ra)) return;
            if (!Capsule(birdy, BirdyRadiusH, out var b0, out var b1, out float rb)) return;
            if (a1.y < b0.y || b1.y < a0.y) return;
            Vector2 d = new Vector2(a0.x - b0.x, a0.z - b0.z);
            float gap = d.magnitude - ra - rb;
            if (gap < 0f)
                report.Flag($"(g) {slot.ShotId} @ {tl:0.00}s: MC and Birdy overlap by {-gap * 100f:0} cm (bodies {d.magnitude:0.00} m apart).");
        }

        static bool Capsule(CutsceneActor actor, float radiusOfHeight, out Vector3 bottom, out Vector3 top, out float radius)
        {
            var anim = actor.GetComponentInChildren<Animator>();
            var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            Vector3 axis = hips != null ? hips.position : actor.transform.position;
            float height = actor.Height;
            Vector3 head = actor.AnchorPosition(CutsceneAnchor.Head);
            bottom = new Vector3(axis.x, actor.transform.position.y, axis.z);
            top = new Vector3(axis.x, head.y, axis.z);
            radius = radiusOfHeight * height;
            return height > 0f;
        }

        static bool Present(RigBasis subject, RigScene s)
        {
            switch (subject)
            {
                case RigBasis.BirdyHead:
                case RigBasis.BirdyEyes:
                case RigBasis.BirdySocket:
                    return s.Birdy.Valid;
                case RigBasis.PropRoot:
                    return s.HasProp;
                default:
                    return s.MC.Valid;
            }
        }

        static bool Allowed(ShotRigSpec spec, ShotSlot slot, float tl)
        {
            if (spec.tcuAllowed == null) return false;
            float dur = (float)slot.Duration;
            foreach (var w in spec.tcuAllowed)
                if (tl >= w.from.Resolve(slot.Beats, dur) - 1e-4f && tl <= w.to.Resolve(slot.Beats, dur) + 1e-4f) return true;
            return false;
        }

        /// <summary>The shot's worst orientation wobble, degrees: the profile's summed pitch/yaw amplitudes × the peak gain.</summary>
        static float NoiseDegrees(CutsceneShotTable table, ShotRigSpec spec, float[] beats, float dur)
        {
            var profile = table.Noise(spec.noise);
            if (profile == null) return 0f;
            float peak = spec.noiseAmplitude;
            if (spec.bursts != null) foreach (var b in spec.bursts) peak = Mathf.Max(peak, spec.noiseAmplitude + b.peak);
            float amp = 0f;
            foreach (var n in profile.OrientationNoise) amp += Mathf.Max(Mathf.Abs(n.X.Amplitude), Mathf.Abs(n.Y.Amplitude));
            return amp * peak;
        }

        static bool OccludedBy(ActorBounds bounds, CutsceneCameraContext context, RigBasis subject, Vector3 lens, Vector3 point,
            out string who)
        {
            who = null;
            CutsceneActor other = subject == RigBasis.BirdyHead || subject == RigBasis.BirdySocket ? context.MC
                : subject == RigBasis.MCHead || subject == RigBasis.MCBlade ? context.Birdy : null;
            if (other == null || !bounds.Occludes(other, lens, point)) return false;
            who = other.ActorId;
            return true;
        }

        // ── (d) hand-offs ───────────────────────────────────────────────────────

        static void CheckHandoffs(Setup setup, FramingReport report, ref int frame)
        {
            var context = setup.Context;
            var rest = context.Rest;
            foreach (var slot in setup.Slots)
            {
                if (slot.ShotId != "S6_SH16" && slot.ShotId != "S2_SH10") continue;
                setup.Rigs.TryGetValue(slot.ShotId, out var rig);
                var vcam = rig != null ? rig.GetComponent<CinemachineCamera>() : null;
                float end = Mathf.Max(0f, (float)slot.Duration - 1e-4f);
                Evaluate(slot, end, setup.Director, setup.Brain, setup.Camera, vcam, ref frame, out var pose, out float fov, out _);
                var g = context.GameplayPose();
                float gFov = context.GameplayFov;
                var delta = FramingChecks.PoseDelta(pose, fov, g, gFov);
                var target = context.OrbitTarget;
                Vector3 e = g.rotation.eulerAngles;
                string gLine = $"pose G = position {g.position.ToString("F3")}, yaw {e.y:0.000}°, pitch {e.x:0.000}°, FOV {gFov:0.00}° " +
                               $"(target {(target != null ? target.name : "none")} at {(target != null ? target.position.ToString("F3") : "-")}, " +
                               $"targetOffset {rest.TargetOffset.ToString("F4")}, distance {rest.Distance} m, lockPitch {rest.Pitch}°, " +
                               $"safe band {(rest.SafeBand ? "on" : "off")} min {rest.SafeBandMin})";
                bool blocked = GBlocked(context, g, out string blocker);
                {
                    // Both hand-offs are blends now (S6: lead decision, review round 2): check the blend's
                    // end target, the orbit camera's resting pose G, rather than an invisible cut.
                    string which = slot.ShotId == "S6_SH16" ? "S6" : "S2";
                    float secs = slot.ShotId == "S6_SH16" ? 2.0f : 1.5f;
                    if (slot.ShotId == "S6_SH16")
                    {
                        report.FinalDelta = delta;
                        // The shot's last frame shows the whole black sun (rim included) above MC.
                        if (context.TryGetMarker(CutsceneMarkerIds.BlackSun, out var sunEnd, out _))
                        {
                            float rim = FramingChecks.DegreesOutsideFrame(pose.position, pose.rotation, fov, setup.Table.Aspect, sunEnd)
                                        + SunAngularRadius(sunEnd, pose.position);
                            if (rim > 0f)
                                report.Flag($"(d) S6_SH16's last frame crops the black sun (its rim is {rim:0.0}° past the frame edge).");
                        }
                    }
                    report.Pose($"{which} end (blend source) = {pose.position.ToString("F3")} FOV {fov:0.0}°; blend target {gLine}; " +
                                $"blend covers Δ {delta.x:0.00} m, {delta.y:0.0}°, FOV {delta.z:0.0}° over {secs:0.0} s.");
                    if (blocked) report.Flag($"(d) The {which} blend target's view is blocked by {blocker}.");
                    if (context.MC != null)
                    {
                        Vector3 vp = CameraMath.Viewport(g.position, g.rotation, gFov, setup.Table.Aspect, context.MC.AnchorPosition(CutsceneAnchor.Head));
                        if (!FramingChecks.InSafeFrame(vp, setup.Table.SafeFrame))
                            report.Flag($"(d) The {which} blend target doesn't frame MC.Head ({vp.x:0.00}, {vp.y:0.00}).");
                    }
                }
            }
        }

        /// <summary>True when the orbit camera's occlusion sweep (focus → G) would hit scenery.</summary>
        static bool GBlocked(CutsceneCameraContext context, Pose g, out string blocker)
        {
            blocker = null;
            var target = context.OrbitTarget;
            if (target == null) return false;
            Vector3 focus = target.position + context.Rest.TargetOffset;
            Vector3 arm = g.position - focus;
            float len = arm.magnitude;
            if (len < 1e-3f) return false;
            var hits = Physics.SphereCastAll(focus, 0.3f, arm / len, len, ~0, QueryTriggerInteraction.Ignore);
            foreach (var h in hits)
            {
                if (h.collider == null || h.collider.transform.IsChildOf(target.root)) continue;
                if (h.collider.GetComponentInParent<Health>() != null) continue;
                blocker = h.collider.name;
                return true;
            }
            return false;
        }

        // ── Contact sheet ───────────────────────────────────────────────────────

        /// <summary>
        /// Renders one 16:9 frame from the middle of every shot to <paramref name="folder"/> as
        /// <c>NN_SHOTID.png</c> and returns the files in order. The scene is reloaded afterwards.
        /// </summary>
        public static List<string> RenderMidFrames(string folder, int width = 640, bool reload = true)
        {
            var files = new List<string>();
            try
            {
                var setup = Setup.Gather();
                if (setup == null) return files;
                setup.Begin();
                Directory.CreateDirectory(folder);
                int height = Mathf.RoundToInt(width / setup.Table.Aspect);
                // Render HDR (as the Game view does) and resolve into an sRGB target: an LDR camera
                // target clamps the void's HDR white before the tonemapper and reads it as grey.
                var rt = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR) { antiAliasing = 4 };
                var ldr = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                int frame = 1, index = 0;
                var old = setup.Camera.targetTexture;
                // Edit-mode skinning only refreshes on the editor's own update; force it per render
                // so each frame shows the pose the director just evaluated (the scene is reloaded after).
                foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None))
                    smr.forceMatrixRecalculationPerRender = true;
                // Frame what the player sees: gameplay indicators are hidden during cutscenes.
                var cutscenePlayer = Object.FindFirstObjectByType<Pickleball.Cutscene.Playback.CutscenePlayer>();
                cutscenePlayer?.GameplayVisuals?.Hide();
                try
                {
                    foreach (var slot in setup.Slots)
                    {
                        setup.Rigs.TryGetValue(slot.ShotId, out var rig);
                        var vcam = rig != null ? rig.GetComponent<CinemachineCamera>() : null;
                        Evaluate(slot, (float)slot.Duration * 0.5f, setup.Director, setup.Brain, setup.Camera, vcam, ref frame,
                            out var pose, out float fov, out _);
                        setup.Camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
                        setup.Camera.fieldOfView = fov;
                        setup.Camera.targetTexture = rt;
                        setup.Camera.aspect = setup.Table.Aspect;
                        setup.Camera.Render();
                        Graphics.Blit(rt, ldr);
                        RenderTexture.active = ldr;
                        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        tex.Apply();
                        RenderTexture.active = null;
                        string file = Path.Combine(folder, $"{index++:00}_{slot.ShotId}.png");
                        File.WriteAllBytes(file, tex.EncodeToPNG());
                        files.Add(file);
                    }
                }
                finally
                {
                    cutscenePlayer?.GameplayVisuals?.Restore();
                    setup.Camera.targetTexture = old;
                    Object.DestroyImmediate(rt);
                    Object.DestroyImmediate(ldr);
                    Object.DestroyImmediate(tex);
                }
            }
            finally
            {
                if (reload) EditorSceneManager.OpenScene(PipelinePaths.Scene, OpenSceneMode.Single);
            }
            return files;
        }
    }
}
