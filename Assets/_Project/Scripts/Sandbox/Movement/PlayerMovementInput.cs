using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox.Movement
{
    /// <summary>
    /// Reads the player's movement controls and turns them into a
    /// <see cref="MovementIntent"/>: camera-relative, deflection-normalised, and
    /// with one-frame press edges.
    ///
    /// Its only job is input — it does not know what a jump or a dive is. The
    /// bindings are the ones ChibiLocomotion used to build for itself, unchanged,
    /// built in code because the project has no shared InputActions asset yet.
    /// Rebuilt in OnEnable so a script reload in Play mode re-creates them
    /// rather than leaving disposed actions behind.
    ///
    /// Bindings: WASD/arrows/left stick move; Space/A jump; Shift/L3 sprint;
    /// Ctrl/B crouch and dive; F/Y front-flip trick; R/D-pad right skateboard;
    /// E/RB dash; Q/LB tether; G/R3 gravity anchor. (Not ours: X / LMB is the
    /// sword attack, RT / Tab lock-on, d-pad up/down camera zoom; RMB / LT held
    /// is the spell's aim, and LMB / RT fire it while aiming. That is why
    /// sprint left LT and tether left RMB.)
    /// </summary>
    public class PlayerMovementInput : MonoBehaviour
    {
        [Tooltip("Camera that stick directions are relative to. Falls back to the main " +
                 "camera when empty.")]
        [SerializeField] private Transform cameraTransform;
        [Tooltip("Optional. While Movement is blocked (a conversation), Read returns an empty " +
                 "intent; while InteractButton is blocked (a talk prompt is up), E / RB does " +
                 "not dash. Empty: input is always read.")]
        [SerializeField] private PlayerInputGate inputGate;

        InputAction _move, _sprint, _jump, _crouch, _trick, _mount, _dash, _tether, _anchor;

        /// <summary>The camera input is mapped against (may be null: world axes).</summary>
        public Transform CameraTransform => cameraTransform;

        /// <summary>
        /// Cap on stick drive, 0–1 (clamped when read), from now until someone
        /// sets it back to 1. Below 1 it also blocks sprint. Aiming a spell uses
        /// it to slow the player to a walk, the way Expedition 33 does, without
        /// movement having to know that magic exists: this is a generic "drive
        /// no harder than this" knob, and whoever lowers it owns resetting it.
        /// </summary>
        public float DriveLimit { get; set; } = 1f;

        void Awake()
        {
            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;
        }

        void OnEnable()
        {
            _move = new InputAction("Move", InputActionType.Value);
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _move.AddBinding("<Gamepad>/leftStick");

            _sprint = Button("Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
            _jump = Button("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            _crouch = Button("Crouch", "<Keyboard>/leftCtrl", "<Keyboard>/rightCtrl", "<Gamepad>/buttonEast");
            _trick = Button("Flip", "<Keyboard>/f", "<Gamepad>/buttonNorth");
            // D-pad right, not buttonWest: West is the sword attack (user's call),
            // and d-pad up/down are the camera zoom.
            _mount = Button("Mount", "<Keyboard>/r", "<Gamepad>/dpad/right");
            _dash = Button("Dash", "<Keyboard>/e", "<Gamepad>/rightShoulder");
            _tether = Button("Tether", "<Keyboard>/q", "<Gamepad>/leftShoulder");
            _anchor = Button("GravityAnchor", "<Keyboard>/g", "<Gamepad>/rightStickPress");

            _move.Enable(); _sprint.Enable(); _jump.Enable(); _crouch.Enable(); _trick.Enable();
            _mount.Enable(); _dash.Enable(); _tether.Enable(); _anchor.Enable();
        }

        void OnDisable()
        {
            Dispose(ref _move); Dispose(ref _sprint); Dispose(ref _jump); Dispose(ref _crouch);
            Dispose(ref _trick); Dispose(ref _mount); Dispose(ref _dash); Dispose(ref _tether);
            Dispose(ref _anchor);
        }

        /// <summary>This frame's intent. Call once per frame, from Update: the
        /// pressed edges are the Input System's per-frame WasPressedThisFrame.</summary>
        public MovementIntent Read()
        {
            var intent = new MovementIntent();
            if (_move == null) return intent;   // disabled
            if (inputGate != null && inputGate.IsBlocked(InputBlock.Movement)) return intent;

            Vector2 stick = _move.ReadValue<Vector2>();
            if (stick.sqrMagnitude > 1f) stick.Normalize();
            intent.RawStick = stick;

            Vector3 forward = Vector3.forward, right = Vector3.right;
            if (cameraTransform != null)
            {
                forward = Vector3.ProjectOnPlane(cameraTransform.forward, Vector3.up).normalized;
                right = Vector3.ProjectOnPlane(cameraTransform.right, Vector3.up).normalized;
                if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            }
            Vector3 desired = forward * stick.y + right * stick.x;
            // The drive cap trims magnitude only. RawStick above stays raw:
            // the spin gesture reads it, and a walk must still be able to spin.
            float driveLimit = Mathf.Clamp01(DriveLimit);
            float magnitude = Mathf.Min(Mathf.Clamp01(desired.magnitude), driveLimit);
            intent.MoveMagnitude = magnitude;
            intent.MoveDirection = magnitude > 0.001f ? desired / desired.magnitude : Vector3.zero;

            intent.JumpPressed = _jump.WasPressedThisFrame();
            intent.JumpHeld = _jump.IsPressed();
            intent.SprintHeld = driveLimit >= 1f && _sprint.IsPressed();
            intent.CrouchPressed = _crouch.WasPressedThisFrame();
            intent.CrouchHeld = _crouch.IsPressed();
            intent.TrickPressed = _trick.WasPressedThisFrame();
            intent.MountPressed = _mount.WasPressedThisFrame();
            intent.DashPressed = _dash.WasPressedThisFrame()
                                 && (inputGate == null || !inputGate.IsBlocked(InputBlock.InteractButton));
            intent.TetherPressed = _tether.WasPressedThisFrame();
            intent.AnchorPressed = _anchor.WasPressedThisFrame();
            return intent;
        }

        static InputAction Button(string name, params string[] paths)
        {
            var action = new InputAction(name, InputActionType.Button);
            foreach (var p in paths) action.AddBinding(p);
            return action;
        }

        static void Dispose(ref InputAction action)
        {
            if (action == null) return;
            action.Disable();
            action.Dispose();
            action = null;
        }
    }
}
