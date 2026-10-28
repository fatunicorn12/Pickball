using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Reads the Attack button and nothing else. Split out of MeleeCombat so
    /// the combo can be driven by anything that calls
    /// <c>MeleeCombat.QueueAttack</c> — a test, a replay, an AI sparring partner.
    ///
    /// Event-driven (the action's performed callback), so it adds no Update.
    /// </summary>
    public class SwordInput : MonoBehaviour
    {
        [Tooltip("The orbit camera. Left click also re-captures the cursor after " +
                 "Escape; while the cursor is free, a mouse click belongs to the " +
                 "camera and must not also swing the sword.")]
        [SerializeField] private SandboxOrbitCamera cameraRig;
        [Tooltip("Optional. While Sword is blocked (a conversation), attack presses are " +
                 "dropped, not buffered. Empty: never dropped.")]
        [SerializeField] private PlayerInputGate inputGate;

        /// <summary>Raised on each Attack press that is meant for the sword.</summary>
        public event Action AttackPressed;

        InputAction _attack;

        void Awake()
        {
            if (cameraRig == null && Camera.main != null)
                cameraRig = Camera.main.GetComponent<SandboxOrbitCamera>();
        }

        void OnEnable()
        {
            _attack = new InputAction("Attack", InputActionType.Button);
            _attack.AddBinding("<Mouse>/leftButton");
            _attack.AddBinding("<Gamepad>/buttonWest");
            _attack.performed += OnPerformed;
            _attack.Enable();
        }

        void OnDisable()
        {
            if (_attack == null) return;
            _attack.performed -= OnPerformed;
            _attack.Disable();
            _attack.Dispose();
            _attack = null;
        }

        void OnPerformed(InputAction.CallbackContext context)
        {
            if (IsCameraClick(context)) return;
            if (inputGate != null && inputGate.IsBlocked(InputBlock.Sword)) return;
            AttackPressed?.Invoke();
        }

        /// <summary>
        /// The camera re-captures the cursor in its LateUpdate, after input
        /// callbacks have already fired that frame, so <c>JustRelocked</c> alone
        /// is a frame late: the re-capture click used to swing the sword as well.
        /// A mouse press while the cursor is still free is, by definition, that
        /// click.
        /// </summary>
        bool IsCameraClick(InputAction.CallbackContext context)
        {
            if (cameraRig == null) return false;
            if (cameraRig.JustRelocked) return true;
            return context.control?.device is Mouse && !cameraRig.CursorCaptured;
        }
    }
}
