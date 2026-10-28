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
    /// settling to a stop, and it holds once the blend completes. The NPC's
    /// focus point is read once: the NPC keeps animating in unscaled time, and
    /// following its head bone would make the camera bob. A sphere sweep from the
    /// player's shoulder pulls the camera in when a wall or prop is in the way
    /// (the player and the NPC are ignored). The shoulder is chosen once per talk:
    /// the configured one, unless from there scenery on the obstruction layers
    /// blocks the sightlines to the NPC's head, middle or feet, or pulls the
    /// camera in hard (into the player's back), and the other shoulder does
    /// better. Scenery without a collider is invisible to this, as to every
    /// camera cast.
    ///
    /// <b>Looking around.</b> Once the shot has settled, the mouse (while the
    /// cursor is captured) and the right stick orbit the camera round the NPC's
    /// head (<see cref="DialogueOrbit"/>, read by <see cref="DialogueLookInput"/>),
    /// with the world still stopped: everything runs on unscaled time. The camera
    /// keeps its distance from the NPC's head and is re-aimed each frame by
    /// <see cref="DialogueFraming.Compose"/>, so the NPC stays where the shot put
    /// it on screen. A sphere sweep from the NPC's head pulls it in front of walls
    /// and terrain (the player and the NPC are ignored), and it slides round the
    /// player's body rather than through it. It does not fight the player: if
    /// his body hides the NPC from where he steered, so be it. Advancing lines
    /// leaves the view alone. Input during the blend-in is dropped, so the shot
    /// always starts as composed.
    ///
    /// <b>The blend path</b> swings around the player/NPC midpoint
    /// (<see cref="DialogueFraming.OrbitBlend"/>) rather than cutting straight
    /// across, so a camera that starts in front of the player never passes
    /// through him or the NPC on the way to the shot behind him, or back.
    ///
    /// <b>End</b> (<see cref="DialogueDirector.ConversationEnding"/>): blends back
    /// from wherever the camera is, looked-around or not, to the recorded pose over
    /// <see cref="DialogueConfig.CloseSeconds"/>, the
    /// same time the director keeps the world stopped. On
    /// <see cref="DialogueDirector.ConversationEnded"/> it puts the recorded pose
    /// and field of view back exactly, then re-enables the orbit camera on the
    /// first <see cref="Tick"/> whose scaled delta time is above zero. That
    /// matters: at dt 0 the orbit camera's <c>SmoothDampAngle</c> divides by zero
    /// once it has converged, and a NaN yaw never recovers.
    ///
    /// <b>Why disabling the orbit camera is enough.</b> It is off for the whole
    /// time the input gate is blocked, so its mouse and stick look, its Escape
    /// cursor release, its click to re-capture the cursor and its lock-on swing
    /// never run during a talk; this component reads look input itself. Mouse delta
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
        DialogueOrbit _look;
        bool _canLook;
        Vector2 _pendingLook;
        float _shoulder = -1f;      // −1 over the left shoulder, +1 the right; chosen per talk

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

        void LateUpdate()
        {
            if (_canLook && config != null && config.LookEnabled)
                _pendingLook += DialogueLookInput.Read(config.Look, MouseCaptured(), Time.unscaledDeltaTime);
            Tick(Time.deltaTime, Time.unscaledDeltaTime);
        }

        /// <summary>
        /// Adds look input for the next <see cref="Tick"/>, in degrees: x turns the
        /// view (right positive), y raises the camera (positive looks down). Used
        /// only once the shot has settled; dropped during the blends. The
        /// component's own mouse and stick read comes through here, and so can
        /// another input source (touch) or a test.
        /// </summary>
        public void Look(Vector2 degrees) => _pendingLook += degrees;

        /// <summary>
        /// One frame: hands the orbit camera back once <paramref name="deltaTime"/>
        /// (scaled) is above zero, advances any blend by
        /// <paramref name="unscaledDeltaTime"/>, and once the shot has settled
        /// orbits by the look input gathered since the last tick.
        /// </summary>
        public void Tick(float deltaTime, float unscaledDeltaTime)
        {
            if (_orbitPending && deltaTime > 0f) EnableOrbit();
            Vector2 look = _pendingLook;
            _pendingLook = Vector2.zero;
            if (_mode == Mode.Hold)
            {
                if (_canLook) LookAround(look, unscaledDeltaTime);
                return;
            }
            if (_mode == Mode.Idle) return;
            var cam = Cam;
            if (cam == null) return;

            if (_mode == Mode.BlendIn) Solve();

            _time += Mathf.Min(Mathf.Max(unscaledDeltaTime, 0f), MaxStep);
            float k = _duration > 0f ? _time / _duration : 1f;
            float s = DialogueFraming.SmoothStep01(k);
            var pose = DialogueFraming.OrbitBlend(_blendCenter, _from, _to, s);
            cam.transform.SetPositionAndRotation(pose.position, pose.rotation);
            cam.fieldOfView = Mathf.Lerp(_fromFov, _toFov, s);
            if (k < 1f) return;
            if (_mode == Mode.BlendIn) BeginLook();
            _mode = Mode.Hold;
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
            _canLook = false;
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
            _shoulder = ChooseShoulder();
            Solve();
            _blendCenter = (PlayerHead() + _npcFocus) * 0.5f;
        }

        void OnConversationEnding(Talkable _)
        {
            var cam = Cam;
            _canLook = false;
            if (_mode == Mode.Idle || cam == null || config == null) return;
            // From wherever the camera is now, the player's own view included.
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
            _canLook = false;
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
            if (_disabledOrbit && orbitCamera != null)
            {
                orbitCamera.enabled = true;
                ResyncCursor();
            }
            _disabledOrbit = false;
        }

        /// <summary>
        /// The orbit camera was off for the talk, so it never saw an Escape the
        /// player pressed meanwhile: it still believes it owns the cursor, while
        /// the Editor has freed it. Left like that, the mouse would turn the
        /// camera over the Editor and a click would swing the sword instead of
        /// re-capturing. The orbit camera has no way to be told it lost the
        /// cursor, so the cursor is captured again to match what it believes.
        /// </summary>
        void ResyncCursor()
        {
            if (!orbitCamera.CursorCaptured || Cursor.lockState == CursorLockMode.Locked) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
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
            var shot = ShotOver(_shoulder);
            _to = new Pose(shot.Position, shot.Rotation);
            _toFov = shot.FieldOfView;
        }

        /// <summary>The framing for this talk over <paramref name="shoulder"/> (−1
        /// left, +1 right): the configured side offset's size on that side, and the
        /// body sizes read at the start.</summary>
        DialogueFramingSettings FramingOver(float shoulder)
        {
            var framing = config.Framing;
            framing.SideOffset = shoulder * Mathf.Max(Mathf.Abs(framing.SideOffset), 1e-4f);
            framing.PlayerClearance = _playerRadius + config.PlayerClearanceMargin;
            framing.NpcHalfWidth = _npcHalfWidth;
            return framing;
        }

        /// <summary>The shot over <paramref name="shoulder"/> from where the player
        /// stands now, pulled in front of anything in the way and re-composed.</summary>
        DialogueShot ShotOver(float shoulder) => ShotOver(shoulder, out _);

        /// <inheritdoc cref="ShotOver(float)"/>
        /// <param name="pulledIn">Share of the pivot-to-camera distance lost to the
        /// pull-in, 0 (nothing in the way) to 1.</param>
        DialogueShot ShotOver(float shoulder, out float pulledIn)
        {
            Transform partner = director != null ? director.Partner : null;
            Vector3 forward = partner != null ? partner.forward : _captured.forward;
            var framing = FramingOver(shoulder);
            float aspect = Cam != null ? Cam.aspect : 16f / 9f;
            Vector3 axis = partner != null ? partner.position : PlayerHead();
            var shot = DialogueFraming.Solve(PlayerHead(), axis, forward, _npcFocus, _npcFeet, aspect, framing);
            Vector3 clear = KeepClear(shot, partner, _talkable != null ? _talkable.transform : null);
            float wanted = Vector3.Distance(shot.Pivot, shot.Position);
            pulledIn = wanted > 1e-4f ? 1f - Vector3.Distance(shot.Pivot, clear) / wanted : 0f;
            if (clear != shot.Position)
                shot = DialogueFraming.Compose(shot.Pivot, clear, _npcFocus, _npcFeet, aspect, framing);
            return shot;
        }

        /// <summary>
        /// The configured shoulder, unless its shot is spoiled (scenery cuts a
        /// sightline to the NPC, or a pull-in crowds the camera against the
        /// player) and the other shoulder's shot is better: fewer cut sightlines,
        /// or as few and a much smaller pull-in. Decided once, at the talk press,
        /// so the blend never jumps sides.
        /// </summary>
        float ChooseShoulder()
        {
            float preferred = config.Framing.SideOffset < 0f ? -1f : 1f;
            int blocked = BlockedSightlines(ShotOver(preferred, out float pulled).Position);
            float crowded = config.ShoulderSwapPullIn;
            if (blocked == 0 && pulled <= crowded) return preferred;
            int otherBlocked = BlockedSightlines(ShotOver(-preferred, out float otherPulled).Position);
            bool better = otherBlocked < blocked
                          || (otherBlocked == blocked && otherPulled + crowded < pulled);
            return better ? -preferred : preferred;
        }

        /// <summary>How many of the sightlines from <paramref name="camera"/> to the
        /// NPC's head, middle and feet are cut by something on the obstruction
        /// layers that is neither the player nor the NPC.</summary>
        int BlockedSightlines(Vector3 camera)
        {
            float height = Mathf.Max(0f, _npcFocus.y - _npcFeet.y);
            int blocked = 0;
            if (SightlineBlocked(camera, _npcFocus)) blocked++;
            if (SightlineBlocked(camera, _npcFeet + Vector3.up * (height * 0.5f))) blocked++;
            if (SightlineBlocked(camera, _npcFeet + Vector3.up * Mathf.Min(0.2f, height * 0.25f))) blocked++;
            return blocked;
        }

        bool SightlineBlocked(Vector3 from, Vector3 to)
        {
            Vector3 line = to - from;
            float distance = line.magnitude;
            if (distance < 1e-3f) return false;
            Transform player = director != null ? director.Partner : null;
            Transform npc = _talkable != null ? _talkable.transform : null;
            _hits ??= new RaycastHit[HitBufferSize];
            int count = Physics.RaycastNonAlloc(from, line / distance, _hits, distance, config.CameraObstructionMask,
                                                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                if (hit.collider == null) continue;
                Transform t = hit.transform;
                if (player != null && t.IsChildOf(player)) continue;
                if (npc != null && t.IsChildOf(npc)) continue;
                return true;
            }
            return false;
        }

        /// <summary>Sweeps from the shoulder pivot to the camera and returns where
        /// it must stop: short of the first thing that is neither the player nor
        /// the NPC.</summary>
        Vector3 KeepClear(DialogueShot shot, Transform player, Transform npc)
        {
            Vector3 offset = shot.Position - shot.Pivot;
            float distance = offset.magnitude;
            if (distance < 1e-4f) return shot.Position;
            float clear = ClearDistance(shot.Pivot, offset / distance, distance, player, npc);
            return DialogueFraming.PullIn(shot.Pivot, shot.Position, clear, config.MinCameraDistance);
        }

        /// <summary>How far a camera-sized sphere travels from
        /// <paramref name="origin"/> along <paramref name="direction"/> (up to
        /// <paramref name="distance"/>) before it meets something on the
        /// obstruction layers that is neither the player nor the NPC.</summary>
        float ClearDistance(Vector3 origin, Vector3 direction, float distance, Transform player, Transform npc)
        {
            // autoSyncTransforms is off, and with time stopped no physics step
            // syncs moved transforms for us.
            Physics.SyncTransforms();
            _hits ??= new RaycastHit[HitBufferSize];
            int count = Physics.SphereCastNonAlloc(origin, config.CameraCollisionRadius, direction,
                                                   _hits, distance, config.CameraObstructionMask,
                                                   QueryTriggerInteraction.Ignore);
            float clear = distance;
            for (int i = 0; i < count; i++)
            {
                var hit = _hits[i];
                // Distance 0 is a collider the sweep started inside: the origin
                // is in it, so it cannot be "in the way" of the camera.
                if (hit.collider == null || hit.distance <= 0f) continue;
                Transform t = hit.transform;
                if (player != null && t.IsChildOf(player)) continue;
                if (npc != null && t.IsChildOf(npc)) continue;
                if (hit.distance < clear) clear = hit.distance;
            }
            return clear;
        }

        /// <summary>Closest the looking-around camera may come to the NPC's head, m:
        /// the configured minimum, and never so close that the camera's sphere
        /// reaches into the NPC's body.</summary>
        float LookMinDistance => Mathf.Max(config.MinCameraDistance, _npcHalfWidth + config.CameraCollisionRadius);

        /// <summary>Clear distance along the orbit's current direction, up to its radius.</summary>
        float RoomAt(Transform partner, Transform npc)
            => ClearDistance(_look.Pivot, _look.Direction, _look.Radius, partner, npc);

        /// <summary>Mouse look only while the cursor is captured, as the orbit
        /// camera does: never while it is free over the Editor or a menu.</summary>
        bool MouseCaptured()
            => Cursor.lockState == CursorLockMode.Locked && (orbitCamera == null || orbitCamera.CursorCaptured);

        /// <summary>The shot has settled: looking around starts from it.</summary>
        void BeginLook()
        {
            _canLook = config != null && _talkable != null;
            if (!_canLook) return;
            _look ??= new DialogueOrbit();
            _look.Begin(_npcFocus, _to.position);
        }

        /// <summary>
        /// Orbits the NPC's head by <paramref name="look"/> degrees, damped over
        /// unscaled time. Until the first input the camera stays exactly on the
        /// shot; once the orbit has settled nothing is recomputed (the world is
        /// stopped, so nothing could have changed).
        /// </summary>
        void LookAround(Vector2 look, float unscaledDeltaTime)
        {
            if (config == null || !config.LookEnabled) return;
            var cam = Cam;
            if (cam == null) return;
            var settings = config.Look;
            _look.AddLook(look.x, look.y, settings.MinPitch, settings.MaxPitch);
            if (!_look.Moved || _look.IsSettled) return;

            Transform partner = director != null ? director.Partner : null;
            Transform npc = _talkable != null ? _talkable.transform : null;
            float yaw0 = _look.Yaw, pitch0 = _look.Pitch;
            _look.Step(unscaledDeltaTime, settings.SmoothTime);
            float clear = RoomAt(partner, npc);
            if (clear < LookMinDistance)
            {
                // No room there: keep whichever half of the step still fits (slide
                // along the wall), else neither, and drop the blocked turn.
                float yaw1 = _look.Yaw, pitch1 = _look.Pitch;
                _look.SetShown(yaw1, pitch0);
                if ((clear = RoomAt(partner, npc)) >= LookMinDistance)
                    _look.CancelPitch();
                else
                {
                    _look.SetShown(yaw0, pitch1);
                    if ((clear = RoomAt(partner, npc)) >= LookMinDistance)
                        _look.CancelYaw();
                    else
                    {
                        _look.SetShown(yaw0, pitch0);
                        _look.CancelYaw();
                        _look.CancelPitch();
                        clear = RoomAt(partner, npc);
                    }
                }
            }
            Vector3 direction = _look.Direction;
            float shown = _look.FollowDistance(clear, unscaledDeltaTime, settings.PullOutSeconds);
            Vector3 position = _look.Pivot + direction * shown;
            if (partner != null)
                position = DialogueOrbit.PushOut(position, partner.position, PlayerHead(),
                                                 _playerRadius + config.PlayerClearanceMargin, direction);

            var shot = DialogueFraming.Compose(PlayerHead(), position, _npcFocus, _npcFeet,
                                               cam.aspect, FramingOver(_shoulder));
            cam.transform.SetPositionAndRotation(shot.Position, shot.Rotation);
            cam.fieldOfView = shot.FieldOfView;
        }
    }
}
