using Pickleball.Sandbox;
using UnityEngine;
using UnityEngine.Playables;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// What every shot rig in the opening reads: the actors, the propeller, the set markers, the
    /// director whose clock times the shots, and the gameplay orbit camera whose resting pose the
    /// last shot ends on. Wired by the cameras setup menu (no runtime searching); one per scene, on
    /// the <c>CutsceneCameras</c> root. <see cref="Sample"/> copies the live anchors into a
    /// <see cref="RigScene"/> without allocating.
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class CutsceneCameraContext : MonoBehaviour, IMarkerSource
    {
        [Header("Actors")]
        [Tooltip("MC's slot (the gameplay Player's CutsceneActor).")]
        [SerializeField] private CutsceneActor mc;

        [Tooltip("Birdy's slot.")]
        [SerializeField] private CutsceneActor birdy;

        [Tooltip("The propeller prop's root.")]
        [SerializeField] private Transform propeller;

        [Header("Markers")]
        [Tooltip("Marker ids, parallel to Marker Transforms. Wired from the sets by the setup menu.")]
        [SerializeField] private string[] markerIds = new string[0];

        [Tooltip("Marker transforms, parallel to Marker Ids.")]
        [SerializeField] private Transform[] markerTransforms = new Transform[0];

        [Header("Timing")]
        [Tooltip("The director the opening's timelines play on; rigs read its time.")]
        [SerializeField] private PlayableDirector director;

        [Header("Gameplay camera")]
        [Tooltip("The gameplay orbit camera. Its resting settings (target offset, distance, resting " +
                 "pitch, safe band) define pose G, the last shot's end pose.")]
        [SerializeField] private SandboxOrbitCamera orbitCamera;

        [Tooltip("The player root whose forward the orbit camera recentres behind (CameraHandoff's follow target).")]
        [SerializeField] private Transform followTarget;

        [Tooltip("Gameplay vertical FOV, degrees: the main camera's FOV outside cutscenes. Refreshed when " +
                 "a cutscene starts; pose G uses it so the final frame's FOV equals gameplay's.")]
        [Range(5f, 120f)]
        [SerializeField] private float gameplayFov = 60f;

        OrbitRest _rest;
        bool _restRead;

        /// <summary>The director.</summary>
        public PlayableDirector Director => director;

        /// <summary>MC's slot.</summary>
        public CutsceneActor MC => mc;

        /// <summary>Birdy's slot.</summary>
        public CutsceneActor Birdy => birdy;

        /// <summary>The propeller.</summary>
        public Transform Propeller => propeller;

        /// <summary>The gameplay orbit camera.</summary>
        public SandboxOrbitCamera OrbitCamera => orbitCamera;

        /// <summary>The follow target (the player root).</summary>
        public Transform FollowTarget => followTarget;

        /// <summary>Gameplay vertical FOV.</summary>
        public float GameplayFov => gameplayFov;

        /// <summary>The orbit camera's resting settings, read once and cached (reflection boxes).</summary>
        public OrbitRest Rest
        {
            get
            {
                if (!_restRead) RefreshRest();
                return _restRead ? _rest : OrbitRest.Defaults;
            }
        }

        /// <summary>The transform pose G is measured from: the orbit camera's target, else the follow target.</summary>
        public Transform OrbitTarget
        {
            get
            {
                var t = OrbitCameraAccess.Target(orbitCamera);
                return t != null ? t : followTarget;
            }
        }

        void OnEnable() => _restRead = false;

        /// <summary>Re-reads the orbit camera's settings (a cutscene start calls this).</summary>
        public void RefreshRest()
        {
            _rest = OrbitCameraAccess.Rest(orbitCamera);
            _restRead = true;
        }

        /// <summary>Records the gameplay FOV (the hand-back calls this as a cutscene starts).</summary>
        public void SetGameplayFov(float fov)
        {
            if (fov > 1f) gameplayFov = fov;
        }

        /// <summary>
        /// Pose G for the current actor positions: the orbit camera's resting pose behind the follow
        /// target's forward, at <paramref name="distanceScale"/> × its distance.
        /// </summary>
        public Pose GameplayPose(float distanceScale = 1f)
        {
            var target = OrbitTarget;
            if (target == null) return new Pose(transform.position, transform.rotation);
            var forward = followTarget != null ? followTarget.forward : target.forward;
            return Rest.Solve(target.position, OrbitRest.YawBehind(forward), gameplayFov, distanceScale);
        }

        /// <summary>Copies the live anchors into <paramref name="scene"/>. No allocation.</summary>
        public void Sample(RigScene scene)
        {
            scene.MC = ActorSample.From(mc);
            scene.Birdy = ActorSample.From(birdy);
            scene.HasProp = propeller != null;
            scene.Prop = propeller != null ? propeller.position : Vector3.zero;
            scene.Markers = this;
        }

        /// <inheritdoc />
        public bool TryGetMarker(string id, out Vector3 position, out float yaw)
        {
            if (markerIds != null && markerTransforms != null && !string.IsNullOrEmpty(id))
            {
                int n = Mathf.Min(markerIds.Length, markerTransforms.Length);
                for (int i = 0; i < n; i++)
                {
                    if (markerTransforms[i] == null || markerIds[i] != id) continue;
                    position = markerTransforms[i].position;
                    yaw = CameraMath.YawOf(markerTransforms[i].forward);
                    return true;
                }
            }
            position = Vector3.zero;
            yaw = 0f;
            return false;
        }

        /// <summary>The marker transform for an id, or null (editor tools and the validator).</summary>
        public Transform MarkerTransform(string id)
        {
            if (markerIds == null || markerTransforms == null) return null;
            int n = Mathf.Min(markerIds.Length, markerTransforms.Length);
            for (int i = 0; i < n; i++)
                if (markerIds[i] == id) return markerTransforms[i];
            return null;
        }

        /// <summary>Setup-menu entry point.</summary>
        public void Configure(CutsceneActor mcActor, CutsceneActor birdyActor, Transform prop, string[] ids,
            Transform[] markers, PlayableDirector timelineDirector, SandboxOrbitCamera orbit, Transform follow, float fov)
        {
            mc = mcActor;
            birdy = birdyActor;
            propeller = prop;
            markerIds = ids ?? new string[0];
            markerTransforms = markers ?? new Transform[0];
            director = timelineDirector;
            orbitCamera = orbit;
            followTarget = follow;
            gameplayFov = fov > 1f ? fov : 60f;
            _restRead = false;
        }
    }
}
