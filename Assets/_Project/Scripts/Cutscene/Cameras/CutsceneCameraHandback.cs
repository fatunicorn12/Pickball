using System.Collections.Generic;
using Pickleball.Cutscene.Hooks;
using Pickleball.Cutscene.Playback;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// The camera half of a cutscene's end, on top of <see cref="CameraHandoff"/> (which disables
    /// the brain, restores the FOV and re-enables the orbit camera a frame later):
    /// <list type="bullet">
    /// <item><b>MATCH</b> (block 2, scenes 3–6, ending on pose G): puts the orbit camera's private
    /// view state exactly at G's yaw and resting pitch (<see cref="OrbitCameraAccess"/>) and the main
    /// camera exactly on G, so the orbit camera's first frame reproduces the last cutscene frame. It
    /// then measures and logs the pose difference between the last cutscene frame and the orbit
    /// camera's first frame (<see cref="LastMatchPositionDelta"/>, <see cref="LastMatchAngleDelta"/>).</item>
    /// <item><b>BLEND</b> (block 1, ending on S2_SH10): eases (smoothstep) from the last cutscene frame
    /// to the live orbit camera over <c>blendSeconds</c>, position, rotation and FOV. The orbit camera
    /// keeps running underneath: each frame its own pose is put back before anything reads it
    /// (Update), and the blended pose is written after it has run (LateUpdate, late order), so
    /// its smoothing and the player's camera-relative input never see the blend.</item>
    /// <item>Feeds <see cref="CutsceneHoldCamera"/> the last shot frame, so the frames between a
    /// timeline's end and the next start (or the hand-back) repeat it instead of showing whichever
    /// shot camera the brain would fall back to.</item>
    /// <item>Restores the camera's near and far clip planes (close shots use a nearer clip).</item>
    /// <item>Clears the depth-of-field volumes Cinemachine left behind, so DoF is off in gameplay
    /// even when a cutscene is skipped mid-DoF-shot.</item>
    /// </list>
    /// The pose the cutscene last showed is captured from Cinemachine's camera-updated event, after
    /// the brain has written it. No allocation per frame.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10000)]
    public sealed class CutsceneCameraHandback : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The cutscene player whose blocks end here.")]
        [SerializeField] private CutscenePlayer player;

        [Tooltip("The main camera (the brain's output camera).")]
        [SerializeField] private Camera targetCamera;

        [Tooltip("The brain on the main camera.")]
        [SerializeField] private CinemachineBrain brain;

        [Tooltip("The rigs' context (pose G, the orbit camera, the gameplay FOV).")]
        [SerializeField] private CutsceneCameraContext context;

        [Tooltip("Freeze-frame camera shown when no timeline drives the brain; fed the last pose each frame.")]
        [SerializeField] private CutsceneHoldCamera hold;

        [Header("Hand-offs")]
        [Tooltip("Block that ends on a BLEND into gameplay (scenes 1–2).")]
        [SerializeField] private string blendBlockId = OpeningSequence.BlockOneId;

        [Tooltip("Length of the blend into the gameplay camera, seconds (the shot list's BLEND 1.5 s).")]
        [Min(0f)]
        [SerializeField] private float blendSeconds = 1.5f;

        [Tooltip("Second block that ends on a BLEND into gameplay (scenes 3–6). Lead decision, review round 2: " +
                 "the orbit camera's resting pitch (16°) can't frame the black sun, so S6_SH16 ends behind MC " +
                 "with the sun in frame and blends into the live orbit camera instead of a MATCH at pose G.")]
        [SerializeField] private string blendTwoBlockId = OpeningSequence.BlockTwoId;

        [Tooltip("Length of the scene-6 blend into the gameplay camera, seconds (ease-in-out).")]
        [Min(0f)]
        [SerializeField] private float blendTwoSeconds = 2.0f;

        [Tooltip("Block that ends on a MATCH into gameplay at pose G. Empty: none (scene 6 now blends).")]
        [SerializeField] private string matchBlockId = string.Empty;

        [Tooltip("Largest invisible-cut error tolerated before a warning, centimetres.")]
        [Min(0f)]
        [SerializeField] private float matchToleranceCm = 1f;

        [Tooltip("Largest invisible-cut rotation error tolerated before a warning, degrees.")]
        [Min(0f)]
        [SerializeField] private float matchToleranceDeg = 0.1f;

        bool _playing;
        bool _hasLast;
        Pose _last;
        float _lastFov;
        bool _blending;
        float _blendTime;
        float _blendSeconds;
        Pose _from;
        float _fromFov;
        bool _orbitValid;
        Pose _orbitPose;
        float _orbitFov;
        bool _verifyMatch;
        int _verifyWait;
        float _near;
        float _far;
        List<Volume> _volumes;

        /// <summary>True while blending into the gameplay camera.</summary>
        public bool IsBlending => _blending;

        /// <summary>Whether a MATCH hand-off has been measured since enable.</summary>
        public bool HasMatchResult { get; private set; }

        /// <summary>Last MATCH: distance between the last cutscene frame and the orbit camera's first frame, metres.</summary>
        public float LastMatchPositionDelta { get; private set; }

        /// <summary>Last MATCH: rotation between them, degrees.</summary>
        public float LastMatchAngleDelta { get; private set; }

        /// <summary>Last MATCH: FOV difference, degrees.</summary>
        public float LastMatchFovDelta { get; private set; }

        /// <summary>The last cutscene frame's pose (valid after a cutscene has rendered a frame).</summary>
        public Pose LastCutscenePose => _last;

        void OnEnable()
        {
            _volumes ??= new List<Volume>();
            if (player != null)
            {
                player.Started += OnStarted;
                player.Finished += OnFinished;
            }
            CinemachineCore.CameraUpdatedEvent.AddListener(OnCameraUpdated);
        }

        void OnDisable()
        {
            if (player != null)
            {
                player.Started -= OnStarted;
                player.Finished -= OnFinished;
            }
            CinemachineCore.CameraUpdatedEvent.RemoveListener(OnCameraUpdated);
            if (_blending && _orbitValid && targetCamera != null)
            {
                targetCamera.transform.SetPositionAndRotation(_orbitPose.position, _orbitPose.rotation);
                targetCamera.fieldOfView = _orbitFov;
            }
            _blending = false;
            _playing = false;
        }

        void OnStarted(string id)
        {
            _playing = true;
            _hasLast = false;
            _blending = false;
            _verifyMatch = false;
            if (targetCamera != null)
            {
                _near = targetCamera.nearClipPlane;
                _far = targetCamera.farClipPlane;
                if (context != null) context.SetGameplayFov(targetCamera.fieldOfView);
            }
            if (context != null) context.RefreshRest();
        }

        void OnCameraUpdated(CinemachineBrain updated)
        {
            if (!_playing || updated != brain || targetCamera == null) return;
            var t = targetCamera.transform;
            // While the hold camera is live the brain repeats the last real shot frame.
            if (hold != null && ReferenceEquals(brain.ActiveVirtualCamera as Object, hold.ComponentOwner)) return;
            _last = new Pose(t.position, t.rotation);
            _lastFov = targetCamera.fieldOfView;
            _hasLast = true;
            if (hold != null) hold.Hold(_last, _lastFov, targetCamera.nearClipPlane);
        }

        void OnFinished(string id, bool skipped)
        {
            _playing = false;
            ClearCinemachineVolumes();
            if (targetCamera == null) return;
            // The brain wrote the shots' lens clip planes; CameraHandoff restores only the FOV.
            if (_near > 0f) targetCamera.nearClipPlane = _near;
            if (_far > _near) targetCamera.farClipPlane = _far;
            if (!string.IsNullOrEmpty(matchBlockId) && id == matchBlockId) Match();
            else if (id == blendBlockId) StartBlend(blendSeconds);
            else if (id == blendTwoBlockId) StartBlend(blendTwoSeconds);
        }

        void Match()
        {
            if (context == null) return;
            var orbit = context.OrbitCamera;
            var follow = context.FollowTarget;
            var g = context.GameplayPose();
            float yaw = OrbitRest.YawBehind(follow != null ? follow.forward : g.rotation * Vector3.forward);
            if (!OrbitCameraAccess.SnapView(orbit, yaw, context.Rest.Pitch))
                Debug.LogWarning("Cutscene hand-off: SandboxOrbitCamera's view fields weren't found; it will recentre with a visible swing.", this);
            targetCamera.transform.SetPositionAndRotation(g.position, g.rotation);
            _verifyMatch = _hasLast;
            _verifyWait = 0;
        }

        void StartBlend(float seconds)
        {
            _blendSeconds = seconds;
            var t = targetCamera.transform;
            _from = _hasLast ? _last : new Pose(t.position, t.rotation);
            _fromFov = _hasLast ? _lastFov : targetCamera.fieldOfView;
            _orbitFov = targetCamera.fieldOfView;   // CameraHandoff has put the gameplay FOV back
            _orbitValid = false;
            _blendTime = 0f;
            _blending = seconds > 0f;
        }

        void Update()
        {
            if (!_blending || !_orbitValid || targetCamera == null) return;
            // Give the orbit camera (and anything reading the camera in Update) its own pose back.
            targetCamera.transform.SetPositionAndRotation(_orbitPose.position, _orbitPose.rotation);
            targetCamera.fieldOfView = _orbitFov;
        }

        void LateUpdate()
        {
            if (targetCamera == null) return;
            if (_verifyMatch) VerifyMatch();
            if (!_blending) return;

            var t = targetCamera.transform;
            _orbitPose = new Pose(t.position, t.rotation);
            _orbitFov = targetCamera.fieldOfView;
            _orbitValid = true;
            _blendTime += Time.deltaTime;
            float w = CameraMath.Ease(EaseKind.InOut, _blendTime / Mathf.Max(1e-3f, _blendSeconds));
            t.SetPositionAndRotation(Vector3.LerpUnclamped(_from.position, _orbitPose.position, w),
                Quaternion.Slerp(_from.rotation, _orbitPose.rotation, w));
            targetCamera.fieldOfView = Mathf.Lerp(_fromFov, _orbitFov, w);
            if (w >= 1f) _blending = false;
        }

        /// <summary>Once the orbit camera has run again, compares its pose to the last cutscene frame.</summary>
        void VerifyMatch()
        {
            var orbit = context != null ? context.OrbitCamera : null;
            if (orbit == null || !orbit.enabled)
            {
                if (++_verifyWait > 10) _verifyMatch = false;
                return;
            }
            _verifyMatch = false;
            var t = targetCamera.transform;
            LastMatchPositionDelta = Vector3.Distance(t.position, _last.position);
            LastMatchAngleDelta = Quaternion.Angle(t.rotation, _last.rotation);
            LastMatchFovDelta = Mathf.Abs(targetCamera.fieldOfView - _lastFov);
            HasMatchResult = true;
            string msg = $"Cutscene hand-off (MATCH into gameplay): position Δ {LastMatchPositionDelta * 100f:0.000} cm, " +
                         $"rotation Δ {LastMatchAngleDelta:0.000}°, FOV {_lastFov:0.00}° → {targetCamera.fieldOfView:0.00}°.";
            if (LastMatchPositionDelta * 100f > matchToleranceCm || LastMatchAngleDelta > matchToleranceDeg ||
                LastMatchFovDelta > 0.01f)
                Debug.LogWarning(msg, this);
            else Debug.Log(msg, this);
        }

        /// <summary>Zeroes the global volumes Cinemachine's volume settings drive (they persist when the brain stops).</summary>
        void ClearCinemachineVolumes()
        {
            if (brain == null) return;
            var root = brain.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                var child = root.GetChild(i);
                if (child.gameObject.hideFlags != HideFlags.HideAndDontSave) continue;
                child.GetComponents(_volumes);
                for (int v = 0; v < _volumes.Count; v++)
                {
                    _volumes[v].weight = 0f;
                    _volumes[v].sharedProfile = null;
                }
            }
            _volumes.Clear();
        }

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(CutscenePlayer cutscenePlayer, Camera mainCamera, CinemachineBrain cmBrain,
            CutsceneCameraContext rigContext, CutsceneHoldCamera holdCamera)
        {
            hold = holdCamera;
            player = cutscenePlayer;
            targetCamera = mainCamera;
            brain = cmBrain;
            context = rigContext;
        }
    }
}
