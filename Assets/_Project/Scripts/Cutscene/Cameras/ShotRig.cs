using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Splines;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// Turns one shot's <see cref="ShotRigSpec"/> into a live Cinemachine camera. Every frame, before
    /// Cinemachine's pipeline runs, it samples the actors and markers, reads the shot-local time off
    /// the director, and drives the pipeline:
    /// <list type="bullet">
    /// <item><b>Position:</b> keyed paths, crane splines, the gameplay pose and the push-in are
    /// computed here and written to the state (no Cinemachine body); Orbit, Follow and Track bodies
    /// are Cinemachine's OrbitalFollow, Follow and SplineDolly, whose radius, offset, axis and target
    /// this sets from the anchors.</item>
    /// <item><b>Aim:</b> sets the Rotation Composer's target point (the state's look-at point) and
    /// screen position from the aim keys; top-down and gameplay rotations are set after the aim
    /// stage; a pan turns the result about the vertical.</item>
    /// <item><b>Lens, noise, focus:</b> the zoom, the Perlin gain (base plus bursts on beats) and
    /// the depth-of-field focus distance (eased in dioptres).</item>
    /// </list>
    /// Nothing here is a world coordinate: every point resolves from the anchors, the measured H and
    /// HB and the markers, so a different-size model stays framed. Time comes from the director (not
    /// accumulated), so evaluating the timeline at any time in the editor gives the frame Play mode
    /// shows. No allocation per frame.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class ShotRig : CinemachineExtension
    {
        [Header("Shot")]
        [Tooltip("The table holding this shot's data.")]
        [SerializeField] private CutsceneShotTable table;

        [Tooltip("This shot's id in the table.")]
        [SerializeField] private string shotId = string.Empty;

        [Tooltip("Actors, markers, director and the gameplay camera.")]
        [SerializeField] private CutsceneCameraContext context;

        [Header("Timing (written by the setup menu from the timeline)")]
        [Tooltip("The timeline this shot is on; the rig reads the director's time only while it plays this one.")]
        [SerializeField] private PlayableAsset timeline;

        [Tooltip("Shot start on the timeline, seconds.")]
        [SerializeField] private double shotStart;

        [Tooltip("Shot length, seconds.")]
        [Min(0f)]
        [SerializeField] private float shotDuration;

        [Tooltip("Shot-local start of each duration beat, seconds (the timing plan's beats).")]
        [SerializeField] private float[] beatStarts = new float[0];

        [Header("Proxies")]
        [Tooltip("Track body only: the camera's follow target, moved each frame to the point on MC's walk the dolly stays abeam of.")]
        [SerializeField] private Transform trackTarget;

        RigScene _scene;
        ShotRigSpec _spec;
        CinemachineRotationComposer _composer;
        CinemachineOrbitalFollow _orbit;
        CinemachineFollow _follow;
        CinemachineSplineDolly _dolly;
        CinemachineBasicMultiChannelPerlin _noise;
        CinemachineVolumeSettings _volume;
        float _t;
        float _fov;
        Vector3 _aimPoint;

        /// <summary>The shot id.</summary>
        public string ShotId => shotId;

        /// <summary>The shot's spec, or null when the table lacks it.</summary>
        public ShotRigSpec Spec => ResolveSpec();

        /// <summary>The timeline this shot is on.</summary>
        public PlayableAsset Timeline => timeline;

        /// <summary>Shot start on the timeline, seconds.</summary>
        public double ShotStart => shotStart;

        /// <summary>Shot length, seconds.</summary>
        public float ShotDuration => shotDuration;

        /// <summary>Beat starts, shot-local seconds.</summary>
        public float[] BeatStarts => beatStarts;

        /// <summary>The context.</summary>
        public CutsceneCameraContext Context => context;

        /// <summary>Shot-local time used by the last evaluation, seconds.</summary>
        public float LastTime => _t;

        /// <inheritdoc />
        protected override void OnEnable()
        {
            base.OnEnable();
            _spec = null;
            _composer = null;
            _scene ??= new RigScene();
            if (Application.isPlaying) RefreshTrackSpline();
        }

        void CacheComponents()
        {
            if (_composer == null) TryGetComponent(out _composer);
            if (_orbit == null) TryGetComponent(out _orbit);
            if (_follow == null) TryGetComponent(out _follow);
            if (_dolly == null) TryGetComponent(out _dolly);
            if (_noise == null) TryGetComponent(out _noise);
            if (_volume == null) TryGetComponent(out _volume);
        }

        ShotRigSpec ResolveSpec()
        {
            if (_spec != null && _spec.shotId == shotId) return _spec;
            _spec = null;
            if (table != null) table.TryGet(shotId, out _spec);
            return _spec;
        }

        /// <summary>Shot-local seconds for the director's current time (0 when it plays another timeline).</summary>
        public float LocalTime()
        {
            var director = context != null ? context.Director : null;
            if (director == null || director.playableAsset != timeline) return 0f;
            return Mathf.Clamp((float)(director.time - shotStart), 0f, shotDuration);
        }

        /// <inheritdoc />
        public override void PrePipelineMutateCameraStateCallback(
            CinemachineVirtualCameraBase vcam, ref CameraState state, float deltaTime)
        {
            var spec = ResolveSpec();
            if (spec == null || context == null) return;
            _scene ??= new RigScene();
            CacheComponents();
            context.Sample(_scene);
            _t = LocalTime();
            float t = _t;
            float dur = shotDuration;

            // Lens.
            float lensU = CameraMath.Progress(t, spec.lensFrom.Resolve(beatStarts, dur),
                spec.lensTo.Resolve(beatStarts, dur), spec.lensEase);
            _fov = spec.body == BodyKind.Gameplay ? context.GameplayFov : Mathf.Lerp(spec.lensStart, spec.lensEnd, lensU);
            state.Lens.FieldOfView = _fov;

            // Aim (needed first: the push moves toward it).
            RigSolver.Aim(spec.aimKeys, beatStarts, dur, t, _scene, out _aimPoint, out Vector2 screen);
            if (spec.aim == AimMode.Composer && _composer != null)
            {
                state.ReferenceLookAt = _aimPoint;
                _composer.Composition.ScreenPosition = new Vector2(screen.x - 0.5f, 0.5f - screen.y);
            }

            // Position.
            switch (spec.body)
            {
                case BodyKind.Keyed:
                case BodyKind.Crane:
                {
                    Vector3 p = spec.body == BodyKind.Crane
                        ? RigSolver.Crane(spec.path, beatStarts, dur, t, _scene, spec.craneEase)
                        : RigSolver.Keyed(spec.path, beatStarts, dur, t, _scene);
                    if (spec.push > 0f) p = Push(spec, p, t);
                    p = RigSolver.CapBirdyFraction(p, _scene, spec.fracCap, _fov, _aimPoint);
                    state.RawPosition = p;
                    break;
                }
                case BodyKind.Gameplay:
                {
                    float gu = CameraMath.Progress(t, spec.gameplayFrom.Resolve(beatStarts, dur),
                        spec.gameplayTo.Resolve(beatStarts, dur), spec.gameplayEase);
                    var pose = context.GameplayPose(Mathf.Lerp(spec.gameplayDistanceScale, 1f, gu));
                    state.RawPosition = pose.position;
                    break;
                }
                case BodyKind.Orbit:
                    DriveOrbit(spec, t);
                    break;
                case BodyKind.Follow:
                    DriveFollow(spec, t);
                    break;
                case BodyKind.Track:
                    DriveTrack(spec, t);
                    break;
            }

            // Noise.
            if (_noise != null)
            {
                _noise.AmplitudeGain = RigSolver.NoiseGain(spec, beatStarts, dur, t);
                _noise.FrequencyGain = spec.noiseFrequency;
            }
        }

        /// <inheritdoc />
        protected override void PostPipelineStageCallback(
            CinemachineVirtualCameraBase vcam, CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Aim) return;
            var spec = ResolveSpec();
            if (spec == null || context == null) return;
            float dur = shotDuration;

            if (spec.aim == AimMode.TopDown)
                state.RawOrientation = Quaternion.LookRotation(Vector3.down, RigSolver.BodyAxis(_scene));
            else if (spec.aim == AimMode.Gameplay)
                state.RawOrientation = context.GameplayPose().rotation;

            if (spec.panDegrees != 0f)
            {
                float u = CameraMath.Progress(_t, spec.panFrom.Resolve(beatStarts, dur),
                    spec.panTo.Resolve(beatStarts, dur), spec.panEase);
                state.RawOrientation = Quaternion.AngleAxis(spec.panDegrees * u, Vector3.up) * state.RawOrientation;
            }

            if (!string.IsNullOrEmpty(spec.avoidMarker))
                state.RawOrientation = Avoid(spec, state.GetCorrectedPosition(), state.RawOrientation, state.Lens.FieldOfView,
                    state.Lens.Aspect > 0.01f ? state.Lens.Aspect : 16f / 9f);

            // Keep the lens off both bodies whatever their size (a swapped-in model is often bigger):
            // push straight back along the view axis, which keeps the aim point where it was.
            Vector3 lens = state.GetCorrectedPosition();
            Vector3 cleared = Clear(lens, state.RawOrientation * Vector3.forward, state.Lens.NearClipPlane);
            if (cleared != lens) state.PositionCorrection += cleared - lens;

            if (_volume != null && spec.focus != null && spec.focus.Length > 0)
            {
                _volume.FocusTracking = CinemachineVolumeSettings.FocusTrackingMode.Camera;
                _volume.FocusOffset = RigSolver.Focus(spec.focus, beatStarts, dur, _t, _scene,
                    state.GetCorrectedPosition());
            }
        }

        /// <summary>
        /// The "n% push": along the line to the aim point, by n% of the distance to the shot's first
        /// subject (a push measured against a far aim point such as the rim would fly metres).
        /// </summary>
        Vector3 Push(ShotRigSpec spec, Vector3 p, float t)
        {
            float u = CameraMath.Progress(t, spec.pushFrom.Resolve(beatStarts, shotDuration),
                spec.pushTo.Resolve(beatStarts, shotDuration), spec.pushEase);
            Vector3 subject = spec.subjects != null && spec.subjects.Length > 0
                ? RigSolver.Basis(spec.subjects[0].subject, null, _scene) : _aimPoint;
            Vector3 dir = _aimPoint - p;
            if (dir.sqrMagnitude < 1e-8f) return p;
            return p + dir.normalized * (spec.push * u * Vector3.Distance(p, subject));
        }

        /// <summary>Radius of the arm spheres, as a fraction of the actor's height.</summary>
        public const float ArmRadiusH = 0.07f;

        /// <summary>Metres of clearance kept beyond the near clip plane from a body's head and torso spheres.</summary>
        public const float BodyClearance = 0.08f;

        /// <summary>
        /// Pushes <paramref name="lens"/> radially out until it is at least the near clip plus
        /// <see cref="BodyClearance"/> outside each actor's head sphere (radius 0.6 HB, measured),
        /// torso sphere (radius 0.17 H at mid-height) and right-arm spheres (chest to hand,
        /// <see cref="ArmRadiusH"/>). A no-op for shots that already clear, so it only acts when a
        /// model is bigger than the rig was written for.
        /// </summary>
        Vector3 Clear(Vector3 lens, Vector3 forward, float near)
        {
            float need = Mathf.Max(0.01f, near) + BodyClearance;
            for (int pass = 0; pass < 3; pass++)
            {
                bool moved = false;
                moved |= PushOut(ref lens, forward, _scene.MC, need);
                moved |= PushOut(ref lens, forward, _scene.Birdy, need);
                if (!moved) break;
            }
            return lens;
        }

        static bool PushOut(ref Vector3 lens, Vector3 forward, ActorSample a, float need)
        {
            if (!a.Valid || a.Height <= 0.01f) return false;
            bool moved = false;
            moved |= PushSphere(ref lens, forward, a.Head, 0.6f * Mathf.Max(a.HeadBox, 0.05f) + need);
            moved |= PushSphere(ref lens, forward, Vector3.Lerp(a.Feet, a.Head, 0.45f), 0.17f * a.Height + need);
            // The right arm, as three spheres from the chest to the hand (review round 4: with Euler
            // as MC the blade insert, which sits by the hand, put the lens 0–8 cm off his arm).
            Vector3 chest = Vector3.Lerp(a.Feet, a.Head, 0.62f);
            for (int i = 1; i <= 3; i++)
                moved |= PushSphere(ref lens, forward, Vector3.Lerp(chest, a.HandR, i / 3f), ArmRadiusH * a.Height + need);
            return moved;
        }

        static bool PushSphere(ref Vector3 lens, Vector3 forward, Vector3 centre, float radius)
        {
            Vector3 d = lens - centre;
            if (d.sqrMagnitude >= radius * radius) return false;
            // Straight out of the sphere, radially: continuous as the lens moves. Pushing back along
            // the view axis flipped through the sphere whenever the lens looked away from its centre
            // (review round 3: two one-frame pops of ~1.4 m in S2_SH10's crane).
            float len = d.magnitude;
            Vector3 dir = len > 1e-5f ? d / len : -forward;
            lens = centre + dir * radius;
            return true;
        }

        /// <summary>Degrees past the avoid margin over which an avoidance correction fades out.</summary>
        const float AvoidFade = 6f;

        /// <summary>
        /// Keeps a marker (the black sun) at least <c>avoidMargin</c> degrees outside the frame by
        /// tilting away from it (or, with <c>avoidPanOnly</c>, panning). The correction is
        /// continuous: it fades out over <see cref="AvoidFade"/> degrees once the marker is clear on
        /// the other axis, instead of switching off (or between pan and tilt) in one frame, which
        /// popped the S5_SH01 crane at 3.9 s. Returns the corrected rotation.
        /// </summary>
        Quaternion Avoid(ShotRigSpec spec, Vector3 position, Quaternion rotation, float fov, float aspect)
        {
            if (string.IsNullOrEmpty(spec.avoidMarker) || _scene.Markers == null ||
                !_scene.Markers.TryGetMarker(spec.avoidMarker, out var point, out _)) return rotation;
            for (int i = 0; i < 3; i++)
            {
                Vector3 local = Quaternion.Inverse(rotation) * (point - position);
                if (local.z <= 0f) return rotation;
                float halfV = fov * 0.5f;
                float halfH = Mathf.Atan(Mathf.Tan(halfV * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;
                float h = Mathf.Abs(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg);
                float v = Mathf.Atan2(local.y, local.z) * Mathf.Rad2Deg;
                float m = spec.avoidMargin;
                if (spec.avoidPanOnly)
                {
                    float pan = halfH + m - h;
                    if (pan <= 0f) return rotation;
                    float w = Mathf.Clamp01((halfV + m + AvoidFade - Mathf.Abs(v)) / AvoidFade);
                    if (w <= 0f) return rotation;
                    // Pan away from it about the vertical (positive yaw turns the view right).
                    float sign = local.x >= 0f ? -1f : 1f;
                    rotation = Quaternion.AngleAxis(sign * pan * w, Vector3.up) * rotation;
                }
                else
                {
                    float tilt = halfV + m - Mathf.Abs(v);
                    if (tilt <= 0f) return rotation;
                    float w = Mathf.Clamp01((halfH + m + AvoidFade - h) / AvoidFade);
                    if (w <= 0f) return rotation;
                    // Positive local pitch (about the camera's right) tilts the view down.
                    rotation = rotation * Quaternion.AngleAxis((v >= 0f ? tilt : -tilt) * w, Vector3.right);
                }
            }
            return rotation;
        }

        void DriveOrbit(ShotRigSpec spec, float t)
        {
            if (_orbit == null || spec.path == null || spec.path.Length == 0) return;
            Vector3 head = RigSolver.Basis(spec.followTarget, null, _scene);
            Vector3 start = RigSolver.Resolve(spec.path[0].point, _scene);
            Vector3 d = start - head;
            _orbit.Radius = Mathf.Max(0.05f, d.magnitude);
            float u = CameraMath.Progress(t, spec.orbitFrom.Resolve(beatStarts, shotDuration),
                spec.orbitTo.Resolve(beatStarts, shotDuration), spec.orbitEase);
            // WorldSpace binding: the camera sits at Euler(v, h, 0) · (0, 0, -r) from the target.
            float h0 = CameraMath.YawOf(d) - 180f;
            _orbit.HorizontalAxis.Value = Mathf.Repeat(h0 + spec.orbitDegrees * u + 180f, 360f) - 180f;
            float flat = new Vector2(d.x, d.z).magnitude;
            _orbit.VerticalAxis.Value = Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg;
        }

        void DriveFollow(ShotRigSpec spec, float t)
        {
            if (_follow == null || spec.path == null || spec.path.Length == 0) return;
            Vector3 p = RigSolver.Keyed(spec.path, beatStarts, shotDuration, t, _scene);
            Vector3 from;
            if (!string.IsNullOrEmpty(spec.followRelativeMarker) &&
                _scene.Markers.TryGetMarker(spec.followRelativeMarker, out var mp, out _))
                from = mp;
            else from = RigSolver.Basis(spec.followTarget, null, _scene);
            _follow.FollowOffset = p - from;
            _follow.TrackerSettings.PositionDamping = spec.followDamping;
        }

        void DriveTrack(ShotRigSpec spec, float t)
        {
            if (_dolly == null || trackTarget == null) return;
            // Feet is on the blocking mark, so the stride's sway doesn't surge the camera back and forth.
            Vector3 abeam = _scene.MC.Feet;
            float stop = spec.trackStop.Resolve(beatStarts, shotDuration);
            if (t > stop && spec.walkSpeed > 0f &&
                _scene.Markers.TryGetMarker(spec.walkFrom, out var a, out _) &&
                _scene.Markers.TryGetMarker(spec.walkTo, out var b, out _))
            {
                // Glide to a stop: take back the distance walked since, less the slowdown's own travel.
                Vector3 dir = (b - a).normalized;
                float elapsed = t - stop;
                abeam -= dir * (spec.walkSpeed * elapsed - CameraMath.StoppingDistance(elapsed, spec.walkSpeed, spec.trackStopEase));
            }
            trackTarget.position = abeam;

            // Set the dolly's place directly. Cinemachine's nearest-point auto dolly searches the
            // spline in coarse steps, which held the camera nearly still for 8 frames, then jumped it 20 cm.
            if (_scene.Markers.TryGetMarker(spec.trackStart, out var trackFrom, out _) &&
                _scene.Markers.TryGetMarker(spec.trackEnd, out var trackTo, out _))
                _dolly.CameraPosition = CameraMath.SegmentProgress(trackFrom, trackTo, abeam);
        }

        /// <summary>
        /// Setup-menu entry point: binds the rig to its table entry, context, timeline slot and beats.
        /// </summary>
        public void Configure(CutsceneShotTable shotTable, string id, CutsceneCameraContext rigContext,
            PlayableAsset onTimeline, double start, float duration, float[] beats, Transform trackProxy)
        {
            table = shotTable;
            shotId = id;
            context = rigContext;
            timeline = onTimeline;
            shotStart = start;
            shotDuration = Mathf.Max(0f, duration);
            beatStarts = beats ?? new float[0];
            trackTarget = trackProxy;
            _spec = null;
        }

        /// <summary>Re-reads the track spline's knots from its markers (after the sets move).</summary>
        public void RefreshTrackSpline()
        {
            CacheComponents();
            var spec = ResolveSpec();
            if (_dolly == null || _dolly.Spline == null || spec == null || context == null) return;
            if (!context.TryGetMarker(spec.trackStart, out var a, out _) ||
                !context.TryGetMarker(spec.trackEnd, out var b, out _)) return;
            var container = _dolly.Spline;
            var spline = container.Spline;
            Vector3 la = container.transform.InverseTransformPoint(a);
            Vector3 lb = container.transform.InverseTransformPoint(b);
            if (spline.Count == 2 && (Vector3)spline[0].Position == la && (Vector3)spline[1].Position == lb) return;
            spline.Clear();
            spline.Add(new BezierKnot(la), TangentMode.Linear);
            spline.Add(new BezierKnot(lb), TangentMode.Linear);
            _dolly.SplineSettings.InvalidateCache();
        }
    }
}
