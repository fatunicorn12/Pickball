using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball
{
    /// <summary>A single swing gesture derived from a tap or swipe.</summary>
    public struct SwingGesture
    {
        /// <summary>Horizontal aim in [-1, 1] (left .. right) from the swipe's X travel.</summary>
        public float Aim;
        /// <summary>Shot power in [0, 1] from swipe speed/length (a plain tap uses the default).</summary>
        public float Power;
        /// <summary>True if the gesture was a quick tap rather than a deliberate swipe.</summary>
        public bool WasTap;

        public SwingGesture(float aim, float power, bool wasTap)
        {
            Aim = aim;
            Power = power;
            WasTap = wasTap;
        }
    }

    /// <summary>
    /// Mobile-first input. Tap anywhere to swing; swipe direction/speed shapes aim and power.
    /// Built entirely on the new Input System and driven by action callbacks — no Update loop.
    /// A <see cref="SwingGesture"/> is emitted on pointer release so the full swipe can be measured.
    /// </summary>
    public class InputReader : MonoBehaviour
    {
        [Header("Gesture Tuning (screen-relative)")]
        [Tooltip("Travel below this (fraction of screen height) counts as a tap, not a swipe.")]
        [SerializeField, Range(0.01f, 0.2f)] private float tapTravelFraction = 0.04f;

        [Tooltip("Horizontal travel (fraction of screen width) that maps to full left/right aim.")]
        [SerializeField, Range(0.1f, 0.6f)] private float fullAimTravelFraction = 0.25f;

        [Tooltip("Swipe speed (fraction of screen height per second) that maps to full power.")]
        [SerializeField] private float fullPowerSpeedFraction = 3f;

        [Tooltip("Power used for a plain tap with no meaningful swipe.")]
        [SerializeField, Range(0f, 1f)] private float defaultTapPower = 0.5f;

        /// <summary>Raised when the player completes a swing gesture.</summary>
        public event Action<SwingGesture> SwingPerformed;

        private InputAction _pressAction;
        private InputAction _positionAction;

        private Vector2 _startPos;
        private float _startTime;

        private void OnEnable()
        {
            // <Pointer> covers mouse (editor), pen, and the primary touch (device), so the same
            // bindings work on desktop and mobile without an .inputactions asset.
            _pressAction = new InputAction("Press", InputActionType.Button, "<Pointer>/press");
            _positionAction = new InputAction("Position", InputActionType.Value, "<Pointer>/position");

            _pressAction.started += OnPressStarted;
            _pressAction.canceled += OnPressReleased;

            _pressAction.Enable();
            _positionAction.Enable();
        }

        private void OnDisable()
        {
            if (_pressAction != null)
            {
                _pressAction.started -= OnPressStarted;
                _pressAction.canceled -= OnPressReleased;
                _pressAction.Disable();
                _pressAction.Dispose();
                _pressAction = null;
            }
            if (_positionAction != null)
            {
                _positionAction.Disable();
                _positionAction.Dispose();
                _positionAction = null;
            }
        }

        private void OnPressStarted(InputAction.CallbackContext ctx)
        {
            _startPos = _positionAction.ReadValue<Vector2>();
            _startTime = Time.unscaledTime;
        }

        private void OnPressReleased(InputAction.CallbackContext ctx)
        {
            Vector2 endPos = _positionAction.ReadValue<Vector2>();
            float duration = Mathf.Max(Time.unscaledTime - _startTime, 0.0001f);

            Vector2 delta = endPos - _startPos;
            float travel = delta.magnitude;
            float tapThreshold = Screen.height * tapTravelFraction;

            SwingGesture gesture;
            if (travel < tapThreshold)
            {
                gesture = new SwingGesture(0f, defaultTapPower, true);
            }
            else
            {
                float aim = Mathf.Clamp(delta.x / (Screen.width * fullAimTravelFraction), -1f, 1f);

                float speed = travel / duration;                       // px / s
                float speedFraction = speed / Screen.height;           // screen-heights / s
                float power = Mathf.Clamp01(speedFraction / fullPowerSpeedFraction);
                power = Mathf.Max(power, defaultTapPower * 0.6f);       // keep even gentle swipes playable

                gesture = new SwingGesture(aim, power, false);
            }

            SwingPerformed?.Invoke(gesture);
        }
    }
}
