using UnityEngine;

namespace Pickleball.Sandbox.Dialogue
{
    /// <summary>
    /// "The camera is pulled forward while they are talking." Takes the main
    /// camera off the orbit rig for the length of a conversation, blends it to
    /// an over-the-shoulder shot of the NPC, and hands it back exactly where it
    /// was.
    ///
    /// <b>Start</b> (<see cref="DialogueDirector.ConversationStarting"/>, the talk
    /// press): records the camera's pose and field of view and disables
    /// <see cref="SandboxOrbitCamera"/> in that same Update, before any
    /// LateUpdate, so the orbit camera never runs during a talk. The shot comes
    /// from <see cref="DialogueFraming"/>; during the blend-in it is re-solved
    /// each frame from the player's position, because the player is still
    /// settling to a stop, and it is frozen once the blend completes. The NPC's
    /// focus point is read once: the NPC keeps animating in unscaled time, and
    /// following its head bone would make the camera bob. A sphere sweep from the
    /// player's shoulder pulls the camera in when a wall or prop is in the way
    /// (the player and the NPC are ignored).
    ///
    /// <b>End</b> (<see cref="DialogueDirector.ConversationEnding"/>): blends back
    /// to the recorded pose over <see cref="DialogueConfig.CloseSeconds"/>, the
    /// same time the director keeps the world stopped. On
    /// <see cref="DialogueDirector.ConversationEnded"/> it puts the recorded pose
    /// and field of view back exactly, then re-enables the orbit camera on the
    /// first frame whose <c>Time.deltaTime</c> is above zero. That matters: at
    /// dt = 0 the orbit camera's <c>SmoothDampAngle</c> divides by zero once it
    /// has converged, and a NaN yaw never recovers.
    ///
    /// <b>Why disabling the orbit camera is enough.</b> It is off for the whole
    /// time the input gate is blocked, so its mouse and stick look, its Escape
    /// cursor release and its lock-on swing never run during a talk. Mouse delta
    /// is per-frame in the Input System and does not build up while it is off,
    /// and the orbit camera clears its follow velocity in its own OnEnable.
    ///
    /// Blends run in unscaled time with smoothstep, because time is stopped.
    /// Disabling this component mid-conversation (a script reload included)
    /// restores the camera and the orbit rig at once.
    /// </summary>
    [DisallowMultipleComponent]
    public class DialogueCamera : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The director whose conversations this camera frames.")]
        [SerializeField] private DialogueDirector director;
        [Tooltip("Framing, blend times and clearance. Set by Setup Dialogue.")]
        [SerializeField] private DialogueConfig config;
        [Tooltip("The camera to move. Empty uses the Camera on this GameObject.")]
        [SerializeField] private Camera targetCamera;
        [Tooltip("Disabled for the length of each conversation and re-enabled after. Empty: " +
                 "nothing is disabled, so nothing may fight the dialogue camera for the transform.")]
        [SerializeField] private SandboxOrbitCamera orbitCamera;

        enum Mode { Idle, BlendIn, Hold, BlendOut }

        // Longest unscaled step one frame may take, so a hitch cannot skip a blend.
        const float MaxStep = 0.1f;
        const int HitBufferSize = 16;

        Mode _mode = Mode.Idle;
        Vector3 _capturedPosition, _fromPosition, _toPosition;
        Quaternion _capturedRotation, _fromRotation, _toRotation;
        float _capturedFov, _fromFov, _toFov;
        float _time, _duration;
        bool _disabledOrbit;
        bool _orbitPending;
        Talkable _talkable;
        Vector3 _npcFocus;
        RaycastHit[] _hits;

        /// <summary>True from a conversation's start until the camera is handed back.</summary>
        public bool IsActive => _mode != Mode.Idle;

        /// <summary>The shot used for the current or last conversation, after clearance.</summary>
        public DialogueShot LastShot { get; private set; }

        Camera Cam
        {
            get
            {
                if (targetCamera == null) targetCamera = GetComponent<Camera>();
                return targetCamera;
            }
        }

        void OnEnable()
        {
            if (director == null) return;
            director.ConversationStarting += OnConversationStarting;
            director.ConversationEnding += OnConversationEnding;
            director.ConversationEnded += OnConversationEnded;
        }

        void OnDisable()
        {
            if (director != null)
            {
                director.ConversationStarting -= OnConversationStarting;
                director.ConversationEnding -= OnConversationEnding;
                director.ConversationEnded -= OnConversationEnded;
            }
            RestorePose();
            // Going away: hand the orbit camera back now rather than wait for a frame.
            EnableOrbit();
        }

        void Update()
        {
            // Scaled deltaTime on purpose: this waits for the first frame on
            // which the orbit camera's LateUpdate will see dt > 0.
            if (_orbitPending && Time.deltaTime > 0f) EnableOrbit();
        }

        void LateUpdate()
        {
            if (_mode is Mode.Idle or Mode.Hold) return;
            var cam = Cam;
            if (cam == null) return;

            if (_mode == Mode.BlendIn) Solve();

            _time += Mathf.Min(Time.unscaledDeltaTime, MaxStep);
            float k = _duration > 0f ? _time / _duration : 1f;
            float s = DialogueFraming.SmoothStep01(k);
            cam.transform.SetPositionAndRotation(Vector3.Lerp(_fromPosition, _toPosition, s),
                                                 Quaternion.Slerp(_fromRotation, _toRotation, s));
            cam.fieldOfView = Mathf.Lerp(_fromFov, _toFov, s);
            if (k >= 1f) _mode = Mode.Hold;
        }

        void OnConversationStarting(Talkable talkable)
        {
            var cam = Cam;
            if (cam == null || config == null || talkable == null) return;

            // A new talk while the orbit camera is still waiting to come back
            // keeps the pose already recorded, which is the orbit camera's own.
            if (_mode == Mode.Idle && !_orbitPending)
            {
                _capturedPosition = cam.transform.position;
                _capturedRotation = cam.transform.rotation;
                _capturedFov = cam.fieldOfView;
            }
            _orbitPending = false;
            if (orbitCamera != null && orbitCamera.enabled)
            {
                orbitCamera.enabled = false;
                _disabledOrbit = true;
            }

            _talkable = talkable;
            _npcFocus = talkable.FocusPosition;
            _fromPosition = cam.transform.position;
            _fromRotation = cam.transform.rotation;
            _fromFov = cam.fieldOfView;
            _time = 0f;
            _duration = config.BlendInSeconds;
            _mode = Mode.BlendIn;
            Solve();
        }

        void OnConversationEnding(Talkable _)
        {
            var cam = Cam;
            if (_mode == Mode.Idle || cam == null || config == null) return;
            _fromPosition = cam.transform.position;
            _fromRotation = cam.transform.rotation;
            _fromFov = cam.fieldOfView;
            _toPosition = _capturedPosition;
            _toRotation = _capturedRotation;
            _toFov = _capturedFov;
            _time = 0f;
            _duration = config.CloseSeconds;
            _mode = Mode.BlendOut;
        }

        void OnConversationEnded(Talkable _)
        {
            RestorePose();
            if (_disabledOrbit) _orbitPending = true;
            if (_orbitPending && Time.deltaTime > 0f) EnableOrbit();
        }

        /// <summary>Puts the recorded pose and field of view back exactly.</summary>
        void RestorePose()
        {
            if (_mode == Mode.Idle) return;
            _mode = Mode.Idle;
            _talkable = null;
            var cam = Cam;
            if (cam == null) return;
            cam.transform.SetPositionAndRotation(_capturedPosition, _capturedRotation);
            cam.fieldOfView = _capturedFov;
        }

        void EnableOrbit()
        {
            _orbitPending = false;
            if (_disabledOrbit && orbitCamera != null) orbitCamera.enabled = true;
            _disabledOrbit = false;
        }

        /// <summary>The target shot from where the player stands now.</summary>
        void Solve()
        {
            Transform partner = director != null ? director.Partner : null;
            var framing = config.Framing;
            Vector3 feet = partner != null
                ? partner.position
                : _capturedPosition - Vector3.up * framing.PivotHeight;
            Vector3 forward = partner != null ? partner.forward : _capturedRotation * Vector3.forward;

            var shot = DialogueFraming.Solve(feet, forward, _npcFocus, framing);
            shot = KeepClear(shot, partner, _talkable != null ? _talkable.transform : null);
            LastShot = shot;
            _toPosition = shot.Position;
            _toRotation = shot.Rotation;
            _toFov = shot.FieldOfView;
        }

        /// <summary>Sweeps from the shoulder pivot to the camera and stops short
        /// of the first thing that is neither the player nor the NPC.</summary>
        DialogueShot KeepClear(DialogueShot shot, Transform player, Transform npc)
        {
            Vector3 offset = shot.Position - shot.Pivot;
            float distance = offset.magnitude;
            if (distance < 1e-4f) return shot;

            // autoSyncTransforms is off, and with time stopped no physics step
            // syncs moved transforms for us.
            Physics.SyncTransforms();
            _hits ??= new RaycastHit[HitBufferSize];
            int count = Physics.SphereCastNonAlloc(shot.Pivot, config.CameraCollisionRadius, offset / distance,
                                                   _hits, distance, config.CameraObstructionMask,
                                                   QueryTriggerInteraction.Ignore);
            float clear = distance;
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                // Distance 0 is a collider the sweep started inside: the pivot
                // is in it, so it cannot be "in the way" of the camera.
                if (hit.collider == null || hit.distance <= 0f) continue;
                Transform t = hit.transform;
                if (player != null && t.IsChildOf(player)) continue;
                if (npc != null && t.IsChildOf(npc)) continue;
                if (hit.distance < clear) clear = hit.distance;
            }
            return shot.WithPosition(DialogueFraming.PullIn(shot.Pivot, shot.Position, clear,
                                                            config.MinCameraDistance));
        }
    }
}
