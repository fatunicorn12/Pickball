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
    /// press): disables <see cref="SandboxOrbitCamera"/> in that same Update,
    /// before any LateUpdate and before anything else can fail, so the orbit
    /// camera never runs during a talk (it would tick at dt 0). Then it records
    /// the camera's pose and field of view. The shot comes from
    /// <see cref="DialogueFraming.Solve"/>; during the blend-in it is re-solved
    /// each frame from the player's position, because the player is still
    /// settling to a stop, and it is frozen once the blend completes. The NPC's
    /// focus point is read once: the NPC keeps animating in unscaled time, and
    /// following its head bone would make the camera bob. A sphere sweep from the
    /// player's shoulder pulls the camera in when a wall or prop is in the way
    /// (the player and the NPC are ignored).
    ///
    /// <b>The blend path</b> swings around the player/NPC midpoint
    /// (<see cref="DialogueFraming.OrbitBlend"/>) rather than cutting straight
    /// across, so a camera that starts in front of the player never passes
    /// through him or the NPC on the way to the shot behind him, or back.
    ///
    /// <b>End</b> (<see cref="DialogueDirector.ConversationEnding"/>): blends back
    /// to the recorded pose over <see cref="DialogueConfig.CloseSeconds"/>, the
    /// same time the director keeps the world stopped. On
    /// <see cref="DialogueDirector.ConversationEnded"/> it puts the recorded pose
    /// and field of view back exactly, then re-enables the orbit camera on the
    /// first <see cref="Tick"/> whose scaled delta time is above zero. That
    /// matters: at dt 0 the orbit camera's <c>SmoothDampAngle</c> divides by zero
    /// once it has converged, and a NaN yaw never recovers.
    ///
    /// <b>Why disabling the orbit camera is enough.</b> It is off for the whole
    /// time the input gate is blocked, so its mouse and stick look, its Escape
    /// cursor release and its lock-on swing never run during a talk. Mouse delta
    /// is per-frame in the Input System and does not build up while it is off,
    /// and the orbit camera clears its follow velocity in its own OnEnable.
    ///
    /// Blends run in unscaled time with smoothstep, because time is stopped.
    /// Disabling this component mid-conversation (a script reload included)
    /// restores the camera and the orbit rig at once. <see cref="Tick"/> is public
    /// so EditMode tests can drive it with explicit time.
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
        Pose _captured, _from, _to;
        float _capturedFov, _fromFov, _toFov;
        float _time, _duration;
        bool _disabledOrbit;
        bool _orbitPending;
        Talkable _talkable;
        Vector3 _npcFocus, _npcFeet;
        float _playerRadius, _npcHalfWidth;
        Transform _playerHead;
        Vector3 _blendCenter;
        RaycastHit[] _hits;

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

        void LateUpdate() => Tick(Time.deltaTime, Time.unscaledDeltaTime);

        /// <summary>
        /// One frame: hands the orbit camera back once <paramref name="deltaTime"/>
        /// (scaled) is above zero, and advances any blend by
        /// <paramref name="unscaledDeltaTime"/>.
        /// </summary>
        public void Tick(float deltaTime, float unscaledDeltaTime)
        {
            if (_orbitPending && deltaTime > 0f) EnableOrbit();
            if (_mode is Mode.Idle or Mode.Hold) return;
            var cam = Cam;
            if (cam == null) return;

            if (_mode == Mode.BlendIn) Solve();

            _time += Mathf.Min(Mathf.Max(unscaledDeltaTime, 0f), MaxStep);
            float k = _duration > 0f ? _time / _duration : 1f;
            float s = DialogueFraming.SmoothStep01(k);
            var pose = DialogueFraming.OrbitBlend(_blendCenter, _from, _to, s);
            cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
            cam.fieldOfView = Mathf.Lerp(_fromFov, _toFov, s);
            if (k >= 1f) _mode = Mode.Hold;
        }

        void OnConversationStarting(Talkable talkable)
        {
            var cam = Cam;
            // First, before any early return: the orbit camera must not tick at
            // dt 0 during the pause, whatever else is missing.
            if (orbitCamera != null && orbitCamera.enabled)
            {
                orbitCamera.enabled = false;
                _disabledOrbit = true;
            }
            if (cam == null) return;

            // A new talk while the orbit camera is still waiting to come back
            // keeps the pose already recorded, which is the orbit camera's own.
            if (_mode == Mode.Idle && !_orbitPending)
            {
                _captured = new Pose(cam.transform.position, cam.transform.rotation);
                _capturedFov = cam.fieldOfView;
            }
            _orbitPending = false;
            if (config == null || talkable == null) return;   // stays put; handed back at the end

            _talkable = talkable;
            _npcFocus = talkable.FocusPosition;
            _npcFeet = talkable.transform.position;
            Transform partner = director != null ? director.Partner : null;
            _playerHead = HeadBoneOf(partner);
            // Body sizes, read once per talk: the clearance cylinder round the
            // player and the NPC's half-width.
            _playerRadius = partner != null && partner.TryGetComponent(out CharacterController controller)
                ? controller.radius * Mathf.Max(Mathf.Abs(partner.lossyScale.x), Mathf.Abs(partner.lossyScale.z))
                : config.PlayerFallbackRadius;
            _npcHalfWidth = talkable.TryGetComponent(out CapsuleCollider capsule)
                ? Mathf.Max(config.NpcHalfWidth, capsule.radius * Mathf.Abs(talkable.transform.lossyScale.x))
                : config.NpcHalfWidth;
            _from = new Pose(cam.transform.position, cam.transform.rotation);
            _fromFov = cam.fieldOfView;
            _time = 0f;
            _duration = config.BlendInSeconds;
            _mode = Mode.BlendIn;
            Solve();
            _blendCenter = (PlayerHead() + _npcFocus) * 0.5f;
        }

        void OnConversationEnding(Talkable _)
        {
            var cam = Cam;
            if (_mode == Mode.Idle || cam == null || config == null) return;
            _from = new Pose(cam.transform.position, cam.transform.rotation);
            _fromFov = cam.fieldOfView;
            _to = _captured;
            _toFov = _capturedFov;
            _time = 0f;
            _duration = config.CloseSeconds;
            _mode = Mode.BlendOut;
        }

        void OnConversationEnded(Talkable _)
        {
            RestorePose();
            if (_disabledOrbit) _orbitPending = true;
        }

        /// <summary>Puts the recorded pose and field of view back exactly.</summary>
        void RestorePose()
        {
            if (_mode == Mode.Idle) return;
            _mode = Mode.Idle;
            _talkable = null;
            _playerHead = null;
            var cam = Cam;
            if (cam == null) return;
            cam.transform.SetPositionAndRotation(_captured.position, _captured.rotation);
            cam.fieldOfView = _capturedFov;
        }

        void EnableOrbit()
        {
            _orbitPending = false;
            if (_disabledOrbit && orbitCamera != null) orbitCamera.enabled = true;
            _disabledOrbit = false;
        }

        /// <summary>The framing pivot: the player's Head bone where there is one
        /// (the chibi's root sits well below its feet, so a height above the root
        /// is a guess), else the root raised by <see cref="DialogueConfig.PlayerFocusHeight"/>.</summary>
        Vector3 PlayerHead()
        {
            if (_playerHead != null) return _playerHead.position;
            Transform partner = director != null ? director.Partner : null;
            return partner != null
                ? partner.position + Vector3.up * config.PlayerFocusHeight
                : _captured.position;
        }

        /// <summary>The humanoid Head bone under <paramref name="player"/>, or null.
        /// Looked up once per conversation, not per frame.</summary>
        static Transform HeadBoneOf(Transform player)
        {
            if (player == null) return null;
            var animator = player.GetComponentInChildren<Animator>();
            return animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        }

        /// <summary>The target shot from where the player stands now: framed by
        /// <see cref="DialogueFraming.Solve"/>, pulled in front of anything in the
        /// way, and re-composed for wherever it ended up.</summary>
        void Solve()
        {
            Transform partner = director != null ? director.Partner : null;
            Vector3 forward = partner != null ? partner.forward : _captured.forward;
            var framing = config.Framing;
            framing.PlayerClearance = _playerRadius + config.PlayerClearanceMargin;
            framing.NpcHalfWidth = _npcHalfWidth;
            float aspect = Cam != null ? Cam.aspect : 16f / 9f;
            Vector3 axis = partner != null ? partner.position : PlayerHead();
            var shot = DialogueFraming.Solve(PlayerHead(), axis, forward, _npcFocus, _npcFeet, aspect, framing);
            Vector3 clear = KeepClear(shot, partner, _talkable != null ? _talkable.transform : null);
            if (clear != shot.Position)
                shot = DialogueFraming.Compose(shot.Pivot, clear, _npcFocus, _npcFeet, aspect, framing);
            _to = new Pose(shot.Position, shot.Rotation);
            _toFov = shot.FieldOfView;
        }

        /// <summary>Sweeps from the shoulder pivot to the camera and returns where
        /// it must stop: short of the first thing that is neither the player nor
        /// the NPC.</summary>
        Vector3 KeepClear(DialogueShot shot, Transform player, Transform npc)
        {
            Vector3 offset = shot.Position - shot.Pivot;
            float distance = offset.magnitude;
            if (distance < 1e-4f) return shot.Position;

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
            return DialogueFraming.PullIn(shot.Pivot, shot.Position, clear, config.MinCameraDistance);
        }
    }
}
