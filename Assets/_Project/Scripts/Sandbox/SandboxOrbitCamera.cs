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

        float _yaw = 180f;
        float _pitch = 12f;
        Vector3 _velocity;
        bool _locked;

        Camera _cam;
        float _baseFov, _punchAmount, _punchTime, _punchDuration;
        float _shakeAmount, _shakeTime, _shakeDuration;
        int _relockFrame = -10;

        /// <summary>True on the frame left-click was used to re-capture the
        /// cursor. Attack input checks this so the click that grabs the cursor
        /// back does not also swing the weapon.</summary>
        public bool JustRelocked => Time.frameCount - _relockFrame <= 1;

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

                if (_locked)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    _yaw += delta.x * sensitivity;
                    _pitch = Mathf.Clamp(_pitch - delta.y * sensitivity, minPitch, maxPitch);
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    distance = Mathf.Clamp(distance - scroll * 0.003f, minDistance, maxDistance);
            }

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 focus = target.position + targetOffset;
            Vector3 wanted = focus - rot * Vector3.forward * distance;

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
