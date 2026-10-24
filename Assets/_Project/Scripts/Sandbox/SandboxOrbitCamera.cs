using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// Minimal third-person orbit camera for the sandbox scene. Cinemachine is
    /// not in this project's manifest, and pulling it in just to look at a
    /// walk cycle would be a heavier dependency than the job needs.
    ///
    /// Escape releases the cursor so you can get back to the Editor.
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
        [SerializeField] private float followSmoothTime = 0.06f;

        [Header("Lock-on")]
        [Tooltip("Read for the locked target. Found on the follow target if empty.")]
        [SerializeField] private LockOnTargeting lockOn;
        [Tooltip("How quickly the camera swings round to keep the target framed.")]
        [SerializeField] private float lockTurnSmoothTime = 0.12f;
        [Tooltip("Downward look while locked. Looking down past the player puts " +
                 "the target above him on screen instead of behind his head.")]
        [SerializeField] private float lockPitch = 16f;
        [Tooltip("How much of the height difference to the target the camera " +
                 "follows. 0 ignores it; 1 aims straight at a target overhead.")]
        [SerializeField, Range(0f, 1f)] private float lockPitchFollow = 0.5f;
        [Tooltip("Extra pull-back while locked, so player and target both fit.")]
        [SerializeField] private float lockDistanceBonus = 1.2f;
        [Tooltip("Recentre behind the player (lock pressed with nothing to lock).")]
        [SerializeField] private float recenterSmoothTime = 0.12f;

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

        /// <summary>True on the frame left-click was used to re-capture the
        /// cursor. Attack input checks this so the click that grabs the cursor
        /// back does not also swing the weapon.</summary>
        public bool JustRelocked => Time.frameCount - _relockFrame <= 1;

        /// <summary>True while the camera owns the mouse. Lock-on only reads
        /// mouse flicks then, not while the cursor is free over the Editor.</summary>
        public bool CursorCaptured => _locked;

        /// <summary>Swing round behind the player — what the lock-on button does
        /// when there is nothing to lock. Any mouse look cancels it.</summary>
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
            if (_punchTime >= _punchDuration) _baseFov = _cam.fieldOfView;
            _punchAmount = degrees;
            _punchDuration = Mathf.Max(duration, 0.01f);
            _punchTime = 0f;
        }

        void UpdatePunch(float dt)
        {
            if (_cam == null || _punchDuration <= 0f) return;
            if (_punchTime >= _punchDuration)
            {
                _cam.fieldOfView = _baseFov;
                return;
            }
            _punchTime += dt;
            float k = Mathf.Clamp01(_punchTime / _punchDuration);
            // Fast out, slow back: sin gives the whole kick in one hump.
            _cam.fieldOfView = _baseFov + _punchAmount * Mathf.Sin(k * Mathf.PI) * (1f - k * 0.35f);
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
            bool locked = IsLockedOn;
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

        void LateUpdate()
        {
            UpdatePunch(Time.deltaTime);
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

                // While locked the mouse belongs to target switching, not look.
                if (_locked && !IsLockedOn)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    _yaw += delta.x * sensitivity;
                    _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity, minPitch, maxPitch);
                    if (delta.sqrMagnitude > 4f) _recenterYaw = null;
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    distance = Mathf.Clamp(distance - scroll * 0.003f, minDistance, maxDistance);
            }

            Vector3 focus = target.position + targetOffset;
            UpdateLockOn(focus, Time.deltaTime);

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            float dist = distance + lockDistanceBonus * _lockBlend;
            Vector3 wanted = focus - rot * Vector3.forward * dist;

            transform.position = Vector3.SmoothDamp(transform.position, wanted,
                                                    ref _velocity, followSmoothTime);
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
