using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// Reads the spell selector's buttons and nothing else: the number keys pick
    /// a slot directly, and two pad buttons and the mouse wheel cycle while aiming.
    /// <see cref="SpellSelector"/> reads it every frame and does the selecting.
    ///
    /// <b>Bindings, chosen so that none is taken from anything else</b> (every
    /// <c>AddBinding</c> and polled device in the Sandbox was checked):
    ///  - Keyboard 1-9 and numpad 1-9: free everywhere.
    ///  - Pad: d-pad <b>up / down while aiming</b>. D-pad right is the
    ///    skateboard mount and d-pad left is the Spacetime toggle (when that
    ///    feature is on), so cycling on left / right — as first asked for —
    ///    would mount the board mid-aim. Up / down are the camera zoom, which
    ///    the orbit camera already ignores while aiming, so while aim is held
    ///    they belong to nobody. The binding paths are serialized: switching to
    ///    left / right is an Inspector change once Mount has moved.
    ///  - Mouse wheel <b>while aiming</b>: down is next, up is previous. Outside
    ///    aim the wheel is the orbit camera's zoom, which already ignores it while
    ///    aiming, so the same aim-only rule as the pad makes it nobody else's.
    ///    One step per wheel event at most every <c>scrollStepSeconds</c>, so a
    ///    smooth-scrolling trackpad's stream of tiny deltas doesn't spin the row.
    ///
    /// <b>Presses are latched from the actions' performed callbacks</b>, the
    /// same way <see cref="SpellInput"/> latches fire (playtest M-1): a press
    /// processed outside the frame's own input update (a manual
    /// InputSystem.Update, an unfocused Editor) is not lost, every reader in a
    /// frame sees the same answer, and <c>LateUpdate</c> clears the latch
    /// whether it was used or not.
    ///
    /// While the gate blocks <see cref="InputBlock.Spell"/> (a conversation)
    /// nothing reads as pressed and the latch is cleared as usual, so a number
    /// typed mid-dialogue is dropped, never saved to swap the spell afterwards.
    /// A d-pad press made while not aiming is dropped the same way. Actions are
    /// rebuilt in OnEnable (reload-safe); nothing allocates per frame.
    /// </summary>
    public class SpellSelectInput : MonoBehaviour
    {
        const int SlotCount = 9;

        [Tooltip("Optional. While Spell is blocked (a conversation) nothing is pressed. " +
                 "Empty: never blocked.")]
        [SerializeField] private PlayerInputGate inputGate;

        [Tooltip("Whoever implements IAimState (the SpellCaster). The pad cycle counts only " +
                 "while it is aiming, because outside aim d-pad up/down zoom the camera. " +
                 "Empty: the pad cycle always counts.")]
        [SerializeField] private MonoBehaviour aimSource;

        [Header("Bindings")]
        [Tooltip("Number keys 1-9 (and the numpad) pick slots 1-9 directly, aiming or not.")]
        [SerializeField] private bool numberKeys = true;

        [Tooltip("Input System path for 'previous spell' on a pad. Default d-pad up: d-pad " +
                 "left/right are taken by Spacetime and the skateboard mount.")]
        [SerializeField] private string padPreviousBinding = "<Gamepad>/dpad/up";

        [Tooltip("Input System path for 'next spell' on a pad. Default d-pad down.")]
        [SerializeField] private string padNextBinding = "<Gamepad>/dpad/down";

        [Tooltip("On: the pad cycle only counts while aiming (d-pad up/down are camera zoom " +
                 "otherwise). Turn off only for bindings nothing else reads.")]
        [SerializeField] private bool padCycleOnlyWhileAiming = true;

        [Tooltip("The mouse wheel cycles spells: down = next, up = previous. Only while aiming " +
                 "(outside aim the wheel zooms the camera).")]
        [SerializeField] private bool scrollWheel = true;

        [Tooltip("Swap the wheel's direction: up = next.")]
        [SerializeField] private bool invertScroll;

        [Tooltip("Shortest time between two wheel steps, in seconds (unscaled). Tames trackpads " +
                 "and fast spins; one notch is always one step.")]
        [Range(0f, 0.5f)] [SerializeField] private float scrollStepSeconds = 0.08f;

        InputAction[] _slots;
        Action<InputAction.CallbackContext>[] _slotHandlers;
        InputAction _previous, _next, _scroll;
        IAimState _aim;
        bool _aimResolved;

        // Latched presses, cleared in LateUpdate (see the class summary).
        int _slotLatched = -1;
        bool _nextLatched, _previousLatched;
        int _scrollLatched;
        float _lastScrollStepAt = float.NegativeInfinity;

        /// <summary>The slot (0-based) whose key went down since the last frame's
        /// LateUpdate; -1 for none. The lowest wins if several did.</summary>
        public int SlotPressedThisFrame => Blocked ? -1 : _slotLatched;

        /// <summary>+1 for next, -1 for previous, 0 for neither (or both), from
        /// pad presses and wheel steps since the last frame's LateUpdate. Each
        /// source is dropped while not aiming when it is aim-only (the wheel
        /// always is).</summary>
        public int CycleThisFrame
        {
            get
            {
                if (Blocked) return 0;
                bool notAiming = Aim != null && !Aim.IsAiming;
                int pad = padCycleOnlyWhileAiming && notAiming
                    ? 0 : (_nextLatched ? 1 : 0) - (_previousLatched ? 1 : 0);
                int wheel = notAiming ? 0 : _scrollLatched;
                return Math.Sign(pad + wheel);
            }
        }

        bool Blocked => inputGate != null && inputGate.IsBlocked(InputBlock.Spell);

        IAimState Aim
        {
            get
            {
                if (!_aimResolved)
                {
                    _aimResolved = true;
                    _aim = aimSource as IAimState;
                    if (_aim == null && aimSource == null) TryGetComponent(out _aim);
                }
                return _aim is UnityEngine.Object o && o == null ? null : _aim;
            }
        }

        void OnEnable()
        {
            _aimResolved = false;
            ClearLatches();
            if (numberKeys)
            {
                _slots = new InputAction[SlotCount];
                _slotHandlers = new Action<InputAction.CallbackContext>[SlotCount];
                for (int i = 0; i < SlotCount; i++)
                {
                    int slot = i;   // one closure per slot, built here, never per frame
                    var action = new InputAction($"SpellSlot{i + 1}", InputActionType.Button);
                    action.AddBinding($"<Keyboard>/{i + 1}");
                    action.AddBinding($"<Keyboard>/numpad{i + 1}");
                    _slotHandlers[i] = _ => OnSlotPressed(slot);
                    action.performed += _slotHandlers[i];
                    action.Enable();
                    _slots[i] = action;
                }
            }
            _previous = Button("SpellPrevious", padPreviousBinding, OnPrevious);
            _next = Button("SpellNext", padNextBinding, OnNext);
            if (scrollWheel)
            {
                // PassThrough: every wheel event, not just changes of value.
                _scroll = new InputAction("SpellScroll", InputActionType.PassThrough, "<Mouse>/scroll/y");
                _scroll.performed += OnScrollPerformed;
                _scroll.Enable();
            }
        }

        void OnDisable()
        {
            // A press latched just before disabling must not act on re-enable.
            ClearLatches();
            if (_slots != null)
            {
                for (int i = 0; i < _slots.Length; i++)
                {
                    if (_slots[i] != null && _slotHandlers != null) _slots[i].performed -= _slotHandlers[i];
                    Dispose(ref _slots[i]);
                }
                _slots = null;
                _slotHandlers = null;
            }
            if (_previous != null) _previous.performed -= OnPrevious;
            if (_next != null) _next.performed -= OnNext;
            if (_scroll != null) _scroll.performed -= OnScrollPerformed;
            Dispose(ref _previous);
            Dispose(ref _next);
            Dispose(ref _scroll);
        }

        void LateUpdate() => ClearLatches();

        // An Inspector edit to the aim source takes effect at once; to a binding,
        // on the next enable.
        void OnValidate() => _aimResolved = false;

        void OnSlotPressed(int slot)
        {
            if (_slotLatched < 0 || slot < _slotLatched) _slotLatched = slot;
        }

        void OnNext(InputAction.CallbackContext _) => _nextLatched = true;

        void OnPrevious(InputAction.CallbackContext _) => _previousLatched = true;

        void OnScrollPerformed(InputAction.CallbackContext context) => OnScroll(context.ReadValue<float>());

        /// <summary>One wheel event of <paramref name="delta"/> (positive = up).
        /// Latches a step unless one was taken under <c>scrollStepSeconds</c> ago.</summary>
        void OnScroll(float delta)
        {
            if (Mathf.Abs(delta) < 0.01f) return;
            float now = Time.unscaledTime;
            if (now - _lastScrollStepAt < scrollStepSeconds) return;
            _lastScrollStepAt = now;
            int step = delta < 0f ? 1 : -1;     // wheel down = next
            _scrollLatched = invertScroll ? -step : step;
        }

        void ClearLatches()
        {
            _slotLatched = -1;
            _nextLatched = false;
            _previousLatched = false;
            _scrollLatched = 0;
        }

        static InputAction Button(string name, string path, Action<InputAction.CallbackContext> onPerformed)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var action = new InputAction(name, InputActionType.Button);
            action.AddBinding(path);
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
