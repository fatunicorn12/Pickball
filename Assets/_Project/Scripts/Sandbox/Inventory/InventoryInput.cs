using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// Reads the inventory's buttons and nothing else. <see cref="InventoryMenu"/>
    /// and <see cref="Inventory"/> read it every frame and decide what a press
    /// means; this never opens anything itself.
    ///
    /// <b>Bindings, chosen so that none is taken from anything else</b> (every
    /// <c>AddBinding</c> and polled key or button in <c>Scripts/</c> was checked;
    /// the pad's face buttons, shoulders, triggers, stick presses and all four
    /// d-pad directions are already used, and only Start and Select were free):
    ///  - <b>Menu</b>: Keyboard <b>I</b>, pad <b>Start</b>. Opens and closes it.
    ///  - <b>Next weapon</b>: Keyboard <b>X</b>, pad <b>Select</b> (View / Back).
    ///    Works outside the menu only.
    ///
    /// <b>Inside the open menu</b> the rest of the pad and keyboard are free to
    /// reuse, because the menu claims <see cref="InputBlock.All"/> on the gate
    /// and no gameplay reader acts on them meanwhile:
    ///  - move the highlight: WASD / arrows, d-pad, left stick (held repeats);
    ///  - previous / next tab: Q / E, LB / RB;
    ///  - use the highlighted item: Enter / Space, A (South);
    ///  - close: Escape, B (East), or the menu button again.
    /// The mouse is read by the menu's view (hover and click).
    ///
    /// Presses are latched from the actions' performed callbacks and cleared
    /// in <c>LateUpdate</c>, the same pattern as <c>SpellSelectInput</c>: a
    /// press processed outside the frame's own input update is not lost, and
    /// every reader in a frame sees the same answer. The Input System updates
    /// in unscaled time, so all of this works while the menu has the game
    /// paused. Actions are built in OnEnable and disposed in OnDisable
    /// (reload-safe); nothing allocates per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryInput : MonoBehaviour
    {
        [Header("Bindings")]
        [Tooltip("Input System paths that open and close the menu.")]
        [SerializeField] private string[] menuBindings = { "<Keyboard>/i", "<Gamepad>/start" };

        [Tooltip("Input System paths that equip the next weapon, outside the menu.")]
        [SerializeField] private string[] cycleBindings = { "<Keyboard>/x", "<Gamepad>/select" };

        InputAction _menu, _cycle, _tabPrevious, _tabNext, _confirm, _back, _navigate;
        bool _menuLatched, _cycleLatched, _confirmLatched, _backLatched;
        int _tabStep;

        /// <summary>The menu button went down this frame.</summary>
        public bool MenuPressed => _menuLatched;

        /// <summary>The next-weapon button went down this frame.</summary>
        public bool CyclePressed => _cycleLatched;

        /// <summary>-1 / +1 when previous / next tab was pressed this frame, else 0.</summary>
        public int TabStep => _tabStep;

        /// <summary>The use / confirm button went down this frame.</summary>
        public bool ConfirmPressed => _confirmLatched;

        /// <summary>The back / close button went down this frame.</summary>
        public bool BackPressed => _backLatched;

        /// <summary>
        /// The direction held now, from keys, d-pad and left stick together
        /// (each axis -1..1). Polled, not latched: the menu repeats a held
        /// direction itself.
        /// </summary>
        public Vector2 Navigate => _navigate != null ? _navigate.ReadValue<Vector2>() : Vector2.zero;

        void OnEnable()
        {
            ClearLatches();
            _menu = Button("InventoryMenu", _ => _menuLatched = true, menuBindings);
            _cycle = Button("InventoryCycleWeapon", _ => _cycleLatched = true, cycleBindings);
            _tabPrevious = Button("InventoryTabPrevious", _ => _tabStep = -1,
                                  "<Keyboard>/q", "<Gamepad>/leftShoulder");
            _tabNext = Button("InventoryTabNext", _ => _tabStep = +1,
                              "<Keyboard>/e", "<Gamepad>/rightShoulder");
            _confirm = Button("InventoryConfirm", _ => _confirmLatched = true,
                              "<Keyboard>/enter", "<Keyboard>/numpadEnter", "<Keyboard>/space",
                              "<Gamepad>/buttonSouth");
            _back = Button("InventoryBack", _ => _backLatched = true,
                           "<Keyboard>/escape", "<Gamepad>/buttonEast");

            _navigate = new InputAction("InventoryNavigate", InputActionType.Value);
            _navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
            _navigate.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow").With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow").With("Right", "<Keyboard>/rightArrow");
            _navigate.AddBinding("<Gamepad>/dpad");
            _navigate.AddBinding("<Gamepad>/leftStick");
            _navigate.Enable();
        }

        void OnDisable()
        {
            ClearLatches();
            Dispose(ref _menu);
            Dispose(ref _cycle);
            Dispose(ref _tabPrevious);
            Dispose(ref _tabNext);
            Dispose(ref _confirm);
            Dispose(ref _back);
            Dispose(ref _navigate);
        }

        void LateUpdate() => ClearLatches();

        void ClearLatches()
        {
            _menuLatched = _cycleLatched = _confirmLatched = _backLatched = false;
            _tabStep = 0;
        }

        static InputAction Button(string name, Action<InputAction.CallbackContext> onPerformed,
                                  params string[] paths)
        {
            var action = new InputAction(name, InputActionType.Button);
            if (paths != null)
                foreach (var path in paths)
                    if (!string.IsNullOrEmpty(path)) action.AddBinding(path);
            action.performed += onPerformed;
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
