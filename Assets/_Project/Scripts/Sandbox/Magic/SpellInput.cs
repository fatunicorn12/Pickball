using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// Reads the spell's two buttons and nothing else, Expedition 33 style:
    /// hold right mouse / LT to aim, press left mouse / RT to fire. Split from
    /// <see cref="SpellCaster"/> so the caster can be driven by a test or an AI
    /// that never touches a device.
    ///
    /// Held state (aim, auto-repeat fire) is polled, like
    /// <c>PlayerMovementInput</c>; the fire press is latched from its callback
    /// (<see cref="FirePressedThisFrame"/>), so no press is lost. The actions are built in
    /// code (the project has no shared InputActions asset yet) and rebuilt in
    /// OnEnable, so a script reload in Play mode re-creates them instead of
    /// leaving disposed actions behind.
    ///
    /// Mouse and gamepad are separate actions on purpose: the mouse buttons
    /// only count while the orbit camera owns the cursor. With the cursor
    /// free over the Editor, a click belongs to the Editor, and the left click
    /// that re-captures the cursor must not also fire a shot. That click lands
    /// before the camera's LateUpdate flips <c>CursorCaptured</c>, and
    /// <c>JustRelocked</c> covers the frame after (see SwordInput for the same
    /// bug on the sword). A pad never fights the Editor for the cursor, so its
    /// triggers always count.
    ///
    /// While the gate blocks <see cref="InputBlock.Spell"/> (a conversation)
    /// every button reads as released, so aim ends. Aim is a hold: if the
    /// button is still down when the claim lifts, aiming resumes.
    /// </summary>
    public class SpellInput : MonoBehaviour
    {
        [Tooltip("The orbit camera. Mouse aim and fire only count while it has " +
                 "captured the cursor. Falls back to the main camera's rig when empty; " +
                 "with no rig at all the mouse always counts.")]
        [SerializeField] private SandboxOrbitCamera cameraRig;
        [Tooltip("Optional. While Spell is blocked (a conversation), aim and fire read " +
                 "as released. Empty: never blocked.")]
        [SerializeField] private PlayerInputGate inputGate;

        InputAction _aimMouse, _aimPad, _fireMouse, _firePad;

        /// <summary>True every frame aim is held: right mouse (cursor captured) or LT.</summary>
        public bool AimHeld => !Blocked && (IsHeld(_aimPad) || (MouseCounts && IsHeld(_aimMouse)));

        /// <summary>True every frame fire is held: left mouse (cursor captured) or RT.
        /// Read by auto-repeat spells.</summary>
        public bool FireHeld => !Blocked && (IsHeld(_firePad) || (MouseCounts && IsHeld(_fireMouse)));

        /// <summary>
        /// True through this frame's Update when fire went down since the last
        /// frame's LateUpdate. Every reader in the frame sees the same answer;
        /// <c>LateUpdate</c> clears it, whether anyone used it or not, so a press
        /// made while not aiming never fires on the next aim. A press while
        /// Spell is blocked reads false and is cleared the same way: dropped,
        /// not saved for the release.
        /// </summary>
        public bool FirePressedThisFrame => !Blocked && _firePressed;

        bool Blocked => inputGate != null && inputGate.IsBlocked(InputBlock.Spell);

        // Latched from the actions' performed callbacks rather than polled with
        // WasPressedThisFrame (playtest M-1). Polling only sees a press made in
        // the same input update that Update runs after; input processed
        // elsewhere (a manual InputSystem.Update, an unfocused Editor, another
        // update mode) performs the action but is never "this frame", and the
        // shot is silently lost. A press processed after this frame's
        // LateUpdate is kept for the next frame's Update. Same idea as
        // SpacetimeView and LockOnTargeting, which take their latch in Update;
        // this one is cleared here so the caster can keep reading a property.
        bool _firePressed;

        void LateUpdate() => _firePressed = false;

        // The mouse is judged when the press lands: the click that re-captures
        // the cursor arrives while the cursor is still free, so it is dropped here.
        void OnFireMouse(InputAction.CallbackContext _)
        {
            if (MouseCounts) _firePressed = true;
        }

        void OnFirePad(InputAction.CallbackContext _) => _firePressed = true;

        /// <summary>Mouse buttons are the camera's while the cursor is free, and the
        /// re-capture click is the camera's on the frame after too.</summary>
        bool MouseCounts => cameraRig == null || (cameraRig.CursorCaptured && !cameraRig.JustRelocked);

        void Awake()
        {
            if (cameraRig == null && Camera.main != null)
                cameraRig = Camera.main.GetComponent<SandboxOrbitCamera>();
        }

        void OnEnable()
        {
            _aimMouse = Button("SpellAimMouse", "<Mouse>/rightButton");
            _aimPad = Button("SpellAimPad", "<Gamepad>/leftTrigger");
            _fireMouse = Button("SpellFireMouse", "<Mouse>/leftButton");
            _firePad = Button("SpellFirePad", "<Gamepad>/rightTrigger");
            _fireMouse.performed += OnFireMouse;
            _firePad.performed += OnFirePad;
        }

        void OnDisable()
        {
            // A press latched just before disabling must not fire on re-enable.
            _firePressed = false;
            if (_fireMouse != null) _fireMouse.performed -= OnFireMouse;
            if (_firePad != null) _firePad.performed -= OnFirePad;
            Dispose(ref _aimMouse);
            Dispose(ref _aimPad);
            Dispose(ref _fireMouse);
            Dispose(ref _firePad);
        }

        static bool IsHeld(InputAction action) => action != null && action.IsPressed();

        static InputAction Button(string name, string path)
        {
            var action = new InputAction(name, InputActionType.Button);
            action.AddBinding(path);
            action.Enable();
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
