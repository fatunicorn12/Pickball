using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Minimal third-person orbit camera for the sandbox scene. Cinemachine is
    /// not in this project's manifest, and pulling it in just to look at a
    /// walk cycle would be a heavier dependency than the job needs.
    ///
    /// Escape releases the cursor so you can get back to the Editor. On a
    /// gamepad the right stick orbits and d-pad up/down zooms.
    ///
    /// While the player aims a spell (an <see cref="IAimState"/>, read from
    /// aimSource) it swings over the shoulder: closer, offset to the right in
    /// camera space, a little zoomed in, following tighter, looking slower and
    /// with a much wider pitch range so airborne targets can be aimed at.
    /// Zoom, recentring and lock-on steering are ignored meanwhile. All of it
    /// blends by <see cref="AimBlend"/>, so with no aim source, or while never
    /// aiming, the camera behaves exactly as it did before aim existed.
    /// </summary>
    public class SandboxOrbitCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 targetOffset = new(0f, 1.35f, 0f);
        [SerializeField] private float distance = 4f;
        [SerializeField] private float minDistance = 1.5f;
        [SerializeField] private float maxDistance = 10f;

        [Header("Look")]
        [SerializeField] private float sensitivity = 0.12f;
        [SerializeField] private float minPitch = -25f;
        [SerializeField] private float maxPitch = 65f;
        [SerializeField] private float followSmoothTime = 0.18f;
        [Tooltip("Furthest the smoothed camera may trail its goal vertically, in metres. " +
                 "Follow smoothing lags a fast jump or fall; without a cap the player " +
                 "leaves the frame (on-screen y reached 0.00 at 0.18 s). Negative = no cap.")]
        [SerializeField] private float maxVerticalLag = 1f;
        [Tooltip("Keep the player's feet above, and his head below, these viewport heights " +
                 "(0 bottom, 1 top) by moving the camera immediately when smoothing would " +
                 "let him drift out. Inside the band the smooth follow is untouched. " +
                 "Fades out while aiming (the shoulder view frames him low on purpose).")]
        [SerializeField] private bool safeBandEnabled = true;
        [SerializeField, Range(0f, 0.45f)] private float safeBandMin = 0.15f;
        [SerializeField, Range(0.55f, 1f)] private float safeBandMax = 0.75f;

        [Header("Occlusion")]
        [Tooltip("Pull the camera in when geometry sits between the player and it.")]
        [SerializeField] private bool occlusionEnabled = true;
        [Tooltip("Radius of the sphere swept from the follow point to the camera.")]
        [SerializeField, Min(0.01f)] private float occlusionRadius = 0.3f;
        [Tooltip("Layers that block the camera. Triggers, the player and anything " +
                 "with a Health (bosses, dummies) are always ignored.")]
        [SerializeField] private LayerMask occlusionMask = ~0;
        [Tooltip("Closest the camera may be pulled to the follow point. Keep it big " +
                 "enough that the player stays framed with his back to a wall; the lens " +
                 "may then poke into that wall, which beats losing the player.")]
        [SerializeField, Min(0f)] private float occlusionMinDistance = 2f;
        [Tooltip("Metres per second the camera eases back out once the view is clear.")]
        [SerializeField, Min(0f)] private float occlusionReturnSpeed = 5f;

        [Header("Gamepad")]
        [Tooltip("Right-stick turn rate at full tilt, in degrees per second.")]
        [SerializeField] private float stickYawSpeed = 200f;
        [Tooltip("Vertical turn rate. Slower than yaw, since pitch only has a " +
                 "90 degree range to cover.")]
        [SerializeField] private float stickPitchSpeed = 120f;
        [Tooltip("Tilt below this is ignored, so a worn stick does not drift the camera.")]
        [SerializeField, Range(0f, 0.5f)] private float stickDeadzone = 0.15f;
        [Tooltip("Response curve past the deadzone. 1 is linear; higher gives finer " +
                 "control near the centre and full speed only at the edge.")]
        [SerializeField, Range(1f, 3f)] private float stickExponent = 1.6f;
        [SerializeField] private bool invertStickY;
        [Tooltip("D-pad up/down zoom rate, in metres per second.")]
        [SerializeField] private float dpadZoomSpeed = 4f;

        [Header("Lock-on")]
        [Tooltip("Read for the locked target. Found on the follow target if empty.")]
        [SerializeField] private LockOnTargeting lockOn;
        [Tooltip("How quickly the camera swings round to keep the target framed.")]
        [SerializeField] private float lockTurnSmoothTime = 0.2f;
        [Tooltip("Downward look while locked. Looking down past the player puts " +
                 "the target above him on screen instead of behind his head.")]
        [SerializeField] private float lockPitch = 16f;
        [Tooltip("How much of the height difference to the target the camera " +
                 "follows. 0 ignores it; 1 aims straight at a target overhead.")]
        [SerializeField, Range(0f, 1f)] private float lockPitchFollow = 0.5f;
        [Tooltip("Extra pull-back while locked, so player and target both fit.")]
        [SerializeField] private float lockDistanceBonus = 0.6f;
        [Tooltip("Recentre behind the player (lock pressed with nothing to lock).")]
        [SerializeField] private float recenterSmoothTime = 0.12f;

        [Header("Aim")]
        [Tooltip("Says whether the player is aiming a spell: any behaviour that " +
                 "implements IAimState (the SpellCaster). Found on the follow " +
                 "target if empty; with none, the camera never enters aim mode.")]
        [SerializeField] private MonoBehaviour aimSource;
        [Tooltip("Over-the-shoulder offset while aiming, in metres in camera space " +
                 "(x right, y up). Moves the player off the crosshair so the line " +
                 "of fire past him is clear. Sized for the 2.5 m chibi, whose head is " +
                 "the follow point: it lands in the left third of the frame, as " +
                 "in Expedition 33's free aim. Kept low so more of the player's body " +
                 "shows below the head.")]
        [SerializeField] private Vector3 aimShoulderOffset = new(1.2f, 0.2f, 0f);
        [Tooltip("Distance from the follow point while aiming, in metres. Closer " +
                 "than the orbit, so the target reads bigger behind the crosshair.")]
        [SerializeField, Min(0.1f)] private float aimDistance = 3.3f;
        [Tooltip("Field of view while aiming, as a fraction of the normal one. " +
                 "Below 1 zooms in a little; 1 leaves the FOV alone.")]
        [SerializeField, Range(0.3f, 1.2f)] private float aimFovScale = 0.8f;
        [Tooltip("Time constant of the ease into and out of the aim view, in " +
                 "seconds of real time (hitstop does not freeze it). It settles in " +
                 "about three of these; 0 snaps.")]
        [SerializeField, Min(0f)] private float aimBlendTime = 0.1f;
        [Tooltip("Follow smoothing while aiming, in seconds. Tighter than " +
                 "followSmoothTime so the shoulder view does not swim behind the " +
                 "player as he walks.")]
        [SerializeField, Min(0f)] private float aimFollowSmoothTime = 0.03f;
        [Tooltip("Mouse look speed while aiming, as a fraction of sensitivity. " +
                 "Lower gives finer aim on the narrower view.")]
        [SerializeField, Range(0.05f, 2f)] private float aimSensitivityScale = 0.65f;
        [Tooltip("Right-stick turn rate while aiming, as a fraction of the " +
                 "normal yaw and pitch speeds.")]
        [SerializeField, Range(0.05f, 2f)] private float aimStickScale = 0.6f;
        [Tooltip("Furthest look up while aiming, in degrees (negative is up). Far " +
                 "past minPitch, so targets in the air can be aimed at.")]
        [SerializeField] private float aimMinPitch = -60f;
        [Tooltip("Furthest look down while aiming, in degrees (positive is down).")]
        [SerializeField] private float aimMaxPitch = 70f;

        float _yaw = 180f;
        float _pitch = 12f;
        Vector3 _velocity;
        bool _locked;

        Camera _cam;
        float _baseFov, _punchAmount, _punchTime, _punchDuration;
        float _shakeAmount, _shakeTime, _shakeDuration;
        int _relockFrame = -10;
        float _yawVelocity, _pitchVelocity, _lockBlend;
        float? _recenterYaw;
        float _aimBlend, _nextAimSearch;
        bool _aiming, _pitchRecovering, _ownsFov, _aimSourceWarned;
        MonoBehaviour _aimBehaviour;

        /// <summary>Seconds between fallback searches for an aim source on the
        /// follow target, in scenes that have none.</summary>
        const float AimSearchInterval = 1f;

        /// <summary>How far into the over-the-shoulder aim view the camera is:
        /// 0 is the normal orbit, 1 fully aimed. Eased on unscaled time, so a
        /// hitstop landing mid-aim does not freeze the swing; presentation (the
        /// crosshair, say) can fade with it.</summary>
        public float AimBlend => _aimBlend;

        /// <summary>True on the frame left-click was used to re-capture the
        /// cursor. Attack input checks this so the click that grabs the cursor
        /// back does not also swing the weapon.</summary>
        public bool JustRelocked => Time.frameCount - _relockFrame <= 1;

        /// <summary>True while the camera owns the mouse. Lock-on only reads
        /// mouse flicks then, not while the cursor is free over the Editor.</summary>
        public bool CursorCaptured => _locked;

        /// <summary>Swing round behind the player — what the lock-on button does
        /// when there is nothing to lock. Any mouse or stick look cancels it.</summary>
        public void RecenterBehind(Vector3 forward)
        {
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) return;
            _recenterYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        }

        /// <summary>Positional shake, for weapon impacts.</summary>
        public void Shake(float amount, float duration)
        {
            _shakeAmount = Mathf.Max(_shakeAmount, amount);
            _shakeDuration = Mathf.Max(duration, 0.01f);
            _shakeTime = 0f;
        }

        /// <summary>Kick the field of view out and let it spring back. Reads as
        /// acceleration far more cheaply than actually moving the camera, and
        /// it is what gives a 0.2s dash any sense of speed at all.</summary>
        public void PunchFov(float degrees, float duration)
        {
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam == null) return;
            if (_baseFov <= 0f) _baseFov = _cam.fieldOfView;
            _ownsFov = true;
            _punchAmount = degrees;
            _punchDuration = Mathf.Max(duration, 0.01f);
            _punchTime = 0f;
        }

        /// <summary>
        /// Writes the field of view: the base, zoomed by aim, plus any punch on
        /// top. The base is captured once (Start, or lazily) and never re-read
        /// from the camera. PunchFov used to re-capture it whenever no punch was
        /// running, which mid-aim would capture the aim zoom as the new base and
        /// leave the camera stuck zoomed in after aim. Nothing is written until
        /// the first punch or aim, so a camera that does neither keeps the FOV
        /// it was given.
        /// </summary>
        void UpdateFov(float dt)
        {
            if (_aimBlend > 0f) _ownsFov = true;
            if (!_ownsFov) return;
            if (_cam == null) _cam = GetComponent<Camera>();   // lazy: survives a hot reload
            if (_cam == null) return;
            if (_baseFov <= 0f) _baseFov = _cam.fieldOfView;

            float punch = 0f;
            if (_punchTime < _punchDuration)
            {
                _punchTime += dt;
                float k = Mathf.Clamp01(_punchTime / _punchDuration);
                // Fast out, slow back: sin gives the whole kick in one hump.
                punch = _punchAmount * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.35f);
            }
            _cam.fieldOfView = _baseFov * Mathf.Lerp(1f, aimFovScale, _aimBlend) + punch;
        }

        /// <summary>
        /// Reads the aim source and eases <see cref="AimBlend"/> toward it on
        /// unscaled time. The exponential ease never lands exactly, so it snaps
        /// the last thousandth: an AimBlend of exactly 0 is what keeps the
        /// never-aiming camera identical to the old one.
        /// </summary>
        void UpdateAim(float dt)
        {
            _aiming = ResolveAiming();
            if (_aiming) _recenterYaw = null;
            float goal = _aiming ? 1f : 0f;
            _aimBlend = Mathf.Lerp(_aimBlend, goal, AimStep(dt));
            if (Mathf.Abs(_aimBlend - goal) < 0.001f) _aimBlend = goal;
        }

        /// <summary>Exponential smoothing factor for the aim ease this frame.</summary>
        float AimStep(float dt) => aimBlendTime > 0f ? 1f - Mathf.Exp(-dt / aimBlendTime) : 1f;

        /// <summary>
        /// Whether the player is aiming this frame. The source is resolved
        /// lazily and again whenever it goes missing, so a hot reload (which
        /// can clear the cached reference) or a destroyed caster costs a lookup
        /// rather than a dead aim mode. It is cached as the behaviour, not the
        /// interface, so Unity's null check sees a destroyed caster. The
        /// fallback search on the follow target is throttled, since scenes with
        /// no caster at all would otherwise search every frame.
        /// </summary>
        bool ResolveAiming()
        {
            if (_aimBehaviour == null)
            {
                if (aimSource != null)
                {
                    if (aimSource is IAimState) _aimBehaviour = aimSource;
                    else if (!_aimSourceWarned)
                    {
                        _aimSourceWarned = true;
                        Debug.LogWarning($"{name}: aimSource {aimSource.GetType().Name} does not " +
                                         "implement IAimState; looking on the follow target instead.", this);
                    }
                }
                if (_aimBehaviour == null && target != null && Time.unscaledTime >= _nextAimSearch)
                {
                    _nextAimSearch = Time.unscaledTime + AimSearchInterval;
                    _aimBehaviour = target.GetComponent<IAimState>() as MonoBehaviour;
                }
            }
            return _aimBehaviour != null && _aimBehaviour is IAimState aim && aim.IsAiming;
        }

        /// <summary>
        /// Clamps a new pitch to this frame's look limits: the wide aim range
        /// while aiming, else the orbit's. Just after aim, while the pitch is
        /// still easing back inside the orbit's range, the limits stretch to
        /// include the current pitch, so look can bring it in but not push it
        /// further out, and nothing snaps.
        /// </summary>
        float ClampPitch(float pitch)
        {
            if (_aiming) return Mathf.Clamp(pitch, aimMinPitch, aimMaxPitch);
            if (_pitchRecovering)
                return Mathf.Clamp(pitch, Mathf.Min(minPitch, _pitch), Mathf.Max(maxPitch, _pitch));
            return Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        /// <summary>
        /// Aim looks further up and down than the orbit allows. Letting go of
        /// aim eases the pitch back inside [minPitch, maxPitch] instead of
        /// snapping the view the moment the button comes up.
        /// </summary>
        void RecoverPitch(float dt)
        {
            if (_aiming) { _pitchRecovering = true; return; }
            if (!_pitchRecovering) return;
            float inside = Mathf.Clamp(_pitch, minPitch, maxPitch);
            _pitch = Mathf.Lerp(_pitch, inside, AimStep(dt));
            if (Mathf.Abs(_pitch - inside) < 0.05f)
            {
                _pitch = inside;
                _pitchRecovering = false;
            }
        }

        // Disabled while another rig (the dialogue camera) holds the transform:
        // resume from the pose handed back, not with the follow speed it had.
        void OnEnable()
        {
            _velocity = Vector3.zero;
            _occlusionClip = 0f;
            _yawVelocity = _pitchVelocity = 0f;
        }

        void Start()
        {
            _cam = GetComponent<Camera>();
            if (_cam != null) _baseFov = _cam.fieldOfView;
            SetLocked(true);
            if (lockOn == null && target != null) lockOn = target.GetComponent<LockOnTargeting>();
            if (target != null)
            {
                Vector3 e = transform.eulerAngles;
                _yaw = e.y;
                _pitch = e.x;
            }
        }

        void SetLocked(bool locked)
        {
            _locked = locked;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        bool IsLockedOn => lockOn != null && lockOn.IsLocked;

        /// <summary>Lock-on drives the camera only while not aiming. Aim lets go
        /// of the lock, but if one is still held for a frame the player's look
        /// must win: the crosshair is the camera's centre.</summary>
        bool LockSteering => IsLockedOn && !_aiming;

        /// <summary>
        /// Drives yaw and pitch while locked on, or while recentring.
        ///
        /// Yaw points along the line from the player to the target, so the target
        /// sits straight ahead beyond him; pitch looks down past him so the
        /// target clears his head. Both are smoothed rather than snapped, and
        /// written back into the same _yaw/_pitch the mouse drives — so releasing
        /// the lock hands control back from exactly where the camera is, with
        /// no jump.
        /// </summary>
        void UpdateLockOn(Vector3 focus, float dt)
        {
            bool locked = LockSteering;
            _lockBlend = Mathf.MoveTowards(_lockBlend, locked ? 1f : 0f, dt * 3f);

            if (locked)
            {
                _recenterYaw = null;
                Vector3 to = lockOn.AimPoint - focus;
                var flat = new Vector3(to.x, 0f, to.z);
                // Standing on top of the target: hold the current yaw rather
                // than spinning on a near-zero vector.
                if (flat.sqrMagnitude < 0.25f) return;

                float yawWanted = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                float rise = Mathf.Atan2(to.y, flat.magnitude) * Mathf.Rad2Deg;
                float pitchWanted = Mathf.Clamp(lockPitch - rise * lockPitchFollow, minPitch, maxPitch);

                _yaw = Mathf.SmoothDampAngle(_yaw, yawWanted, ref _yawVelocity, lockTurnSmoothTime);
                _pitch = Mathf.SmoothDampAngle(_pitch, pitchWanted, ref _pitchVelocity, lockTurnSmoothTime);
                return;
            }

            if (_recenterYaw.HasValue)
            {
                _yaw = Mathf.SmoothDampAngle(_yaw, _recenterYaw.Value, ref _yawVelocity, recenterSmoothTime);
                _pitch = Mathf.SmoothDampAngle(_pitch, lockPitch, ref _pitchVelocity, recenterSmoothTime);
                if (Mathf.Abs(Mathf.DeltaAngle(_yaw, _recenterYaw.Value)) < 0.5f) _recenterYaw = null;
            }
        }

        /// <summary>
        /// Right stick orbits, d-pad up/down zooms. Like the mouse, the stick
        /// only orbits while not locked on, because LockOnTargeting reads the
        /// same stick for target flicks then. It does not wait for the cursor to
        /// be captured: that is about who owns the mouse, and a pad never fights
        /// the Editor for it. Unscaled time, so hitstop and slow-mo do not
        /// freeze the camera; mouse look ignores timeScale too.
        /// </summary>
        void ReadGamepad()
        {
            var pad = Gamepad.current;
            if (pad == null) return;
            float dt = Time.unscaledDeltaTime;

            if (!LockSteering)
            {
                Vector2 stick = ShapeStick(pad.rightStick.ReadUnprocessedValue());
                if (stick != Vector2.zero)
                {
                    float rise = invertStickY ? -stick.y : stick.y;
                    float turn = Mathf.Lerp(1f, aimStickScale, _aimBlend);
                    _yaw += stick.x * stickYawSpeed * turn * dt;
                    _pitch = ClampPitch(_pitch - rise * stickPitchSpeed * turn * dt);
                    _recenterYaw = null;
                }
            }

            float zoom = _aiming ? 0f : pad.dpad.ReadValue().y;
            if (zoom != 0f)
                distance = Mathf.Clamp(distance - zoom * dpadZoomSpeed * dt, minDistance, maxDistance);
        }

        /// <summary>Radial deadzone, rescaled so tilt ramps up from zero at its
        /// edge instead of jumping, then bent by the response curve. Fed the raw
        /// stick, so this is the only deadzone in play.</summary>
        Vector2 ShapeStick(Vector2 raw)
        {
            float mag = raw.magnitude;
            if (mag <= stickDeadzone) return Vector2.zero;
            float t = Mathf.Clamp01((mag - stickDeadzone) / (1f - stickDeadzone));
            return raw / mag * Mathf.Pow(t, stickExponent);
        }

        float _occlusionClip;
        static readonly RaycastHit[] OcclusionHits = new RaycastHit[8];

        /// <summary>Pulls <paramref name="pos"/> in along the focus-to-camera line
        /// when a collider is in the way, smoothing the return.</summary>
        Vector3 ApplyOcclusion(Vector3 focus, Vector3 pos, float dt)
        {
            if (!occlusionEnabled) return pos;
            Vector3 arm = pos - focus;
            float length = arm.magnitude;
            if (length < 1e-3f) return pos;
            Vector3 dir = arm / length;

            float hitDistance = -1f;
            int n = Physics.SphereCastNonAlloc(focus, occlusionRadius, dir, OcclusionHits, length,
                                               occlusionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var hit = OcclusionHits[i];
                if (hit.collider == null || IsIgnoredBlocker(hit.collider)) continue;
                if (hitDistance < 0f || hit.distance < hitDistance) hitDistance = hit.distance;
            }

            float free = CameraOcclusion.FreeLength(length, hitDistance, occlusionMinDistance);
            _occlusionClip = CameraOcclusion.NextClip(_occlusionClip, length, free, occlusionReturnSpeed, dt);
            return focus + dir * Mathf.Max(0f, length - _occlusionClip);
        }

        /// <summary>World-space shift that keeps the feet above safeBandMin and
        /// the head point below safeBandMax in the viewport, for a camera at
        /// <paramref name="pos"/> facing <paramref name="rot"/>.</summary>
        Vector3 SafeBandCorrection(Vector3 pos, Quaternion rot, Vector3 feet, Vector3 head)
        {
            if (!safeBandEnabled) return Vector3.zero;
            if (_cam == null) _cam = GetComponent<Camera>();
            if (_cam == null) return Vector3.zero;
            float tanHalf = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Quaternion inv = Quaternion.Inverse(rot);
            Vector3 f = inv * (feet - pos);
            Vector3 h = inv * (head - pos);
            float feetY = f.z > 1e-3f ? 0.5f + 0.5f * f.y / (f.z * tanHalf) : 0.5f;
            float headY = h.z > 1e-3f ? 0.5f + 0.5f * h.y / (h.z * tanHalf) : 0.5f;
            float shift = CameraOcclusion.SafeBandShift(feetY, f.z, tanHalf, safeBandMin, 1f);
            if (shift == 0f) shift = CameraOcclusion.SafeBandShift(headY, h.z, tanHalf, 0f, safeBandMax);
            if (shift == 0f) return Vector3.zero;
            _velocity.y = 0f;
            return rot * Vector3.up * shift;
        }

        bool IsIgnoredBlocker(Collider c)
        {
            if (target != null && c.transform.IsChildOf(target.root)) return true;
            return c.GetComponentInParent<Health>() != null;
        }

        void LateUpdate()
        {
            UpdateAim(Time.unscaledDeltaTime);
            UpdateFov(Time.deltaTime);
            if (target == null) return;

            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) SetLocked(false);

            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (!_locked && mouse.leftButton.wasPressedThisFrame)
                {
                    SetLocked(true);
                    _relockFrame = Time.frameCount;
                }

                // While locked the mouse belongs to target switching, not look
                // (aiming overrides that: see LockSteering).
                if (_locked && !LockSteering)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    float look = sensitivity * Mathf.Lerp(1f, aimSensitivityScale, _aimBlend);
                    _yaw += delta.x * look;
                    _pitch = ClampPitch(_pitch - delta.y * look);
                    if (delta.sqrMagnitude > 4f) _recenterYaw = null;
                }

                float scroll = _aiming ? 0f : mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    distance = Mathf.Clamp(distance - scroll * 0.003f, minDistance, maxDistance);
            }

            ReadGamepad();
            RecoverPitch(Time.unscaledDeltaTime);

            Vector3 focus = target.position + targetOffset;
            UpdateLockOn(focus, Time.deltaTime);

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            float dist = Mathf.Lerp(distance + lockDistanceBonus * _lockBlend, aimDistance, _aimBlend);
            Vector3 wanted = focus - rot * Vector3.forward * dist;
            // Over the shoulder: offset in camera space, after the rotation, so
            // "right" stays screen-right at any yaw and pitch.
            if (_aimBlend > 0f) wanted += rot * (aimShoulderOffset * _aimBlend);

            // Occlusion shortens the GOAL along the true focus-to-goal ray before
            // smoothing. (Casting along the smoothed position instead aims at a
            // stale spot after a teleport and pins the camera to the player.)
            wanted = ApplyOcclusion(focus, wanted, Time.unscaledDeltaTime);

            Vector3 smoothed = Vector3.SmoothDamp(transform.position, wanted, ref _velocity,
                                                  Mathf.Lerp(followSmoothTime, aimFollowSmoothTime, _aimBlend));
            float lagged = CameraOcclusion.ClampLag(smoothed.y, wanted.y, maxVerticalLag);
            if (!Mathf.Approximately(lagged, smoothed.y)) { smoothed.y = lagged; _velocity.y = 0f; }
            smoothed += SafeBandCorrection(smoothed, rot, target.position, focus) * (1f - _aimBlend);
            transform.position = smoothed;
            transform.rotation = rot;

            // Shake is applied after the follow so it never feeds back into the
            // smoothing and turn into a wobble that outlives the impact.
            if (_shakeTime < _shakeDuration)
            {
                _shakeTime += Time.deltaTime;
                float falloff = 1f - Mathf.Clamp01(_shakeTime / _shakeDuration);
                transform.position += new Vector3(
                    (Random.value - 0.5f), (Random.value - 0.5f), (Random.value - 0.5f))
                    * (_shakeAmount * falloff);
                if (_shakeTime >= _shakeDuration) _shakeAmount = 0f;
            }
        }
    }
}
