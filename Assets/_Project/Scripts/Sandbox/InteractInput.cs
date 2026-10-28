using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox
{
    /// <summary>
    /// The one owner of the Interact button: E on the keyboard, RB on a pad.
    /// Anything that acts on Interact (the talk interactor, the dialogue
    /// director advancing text) listens to <see cref="Pressed"/> instead of
    /// binding the keys itself, so the binding lives in exactly one place.
    ///
    /// The same keys are also Dash, bound separately by
    /// <c>PlayerMovementInput</c>; which of the two a press means is decided
    /// through <see cref="PlayerInputGate"/> (<see cref="InputBlock.InteractButton"/>),
    /// not here. Never left click (it re-captures the cursor and queues attacks)
    /// or Space (jump).
    ///
    /// Event-driven from the action's performed callback, like
    /// <c>SwordInput</c>, so it adds no Update; listeners latch the press and
    /// act on it in their own Update. Built in OnEnable and disposed in
    /// OnDisable, so a script reload in Play mode rebuilds it.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractInput : MonoBehaviour
    {
        InputAction _interact;

        /// <summary>Raised once per press of Interact (on press, not on hold or release).</summary>
        public event Action Pressed;

        void OnEnable()
        {
            _interact = new InputAction("Interact", InputActionType.Button);
            _interact.AddBinding("<Keyboard>/e");
            _interact.AddBinding("<Gamepad>/rightShoulder");
            _interact.performed += OnPerformed;
            _interact.Enable();
        }

        void OnDisable()
        {
            if (_interact == null) return;
            _interact.performed -= OnPerformed;
            _interact.Disable();
            _interact.Dispose();
            _interact = null;
        }

        void OnPerformed(InputAction.CallbackContext _) => Pressed?.Invoke();
    }
}
