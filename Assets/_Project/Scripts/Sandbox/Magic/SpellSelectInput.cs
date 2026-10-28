using UnityEngine;
using UnityEngine.InputSystem;

namespace Pickleball.Sandbox.Magic
{
    /// <summary>
    /// Reads the spell selector's buttons and nothing else: the number keys pick
    /// a slot directly, and two pad buttons cycle while aiming. Polled, like
    /// <see cref="SpellInput"/>; <see cref="SpellSelector"/> reads it every
    /// frame and does the selecting.
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
    ///
    /// While the gate blocks <see cref="InputBlock.Spell"/> (a conversation)
    /// nothing reads as pressed, so a number typed mid-dialogue never swaps the
    /// spell. Actions are rebuilt in OnEnable (reload-safe) and never allocate
    /// per frame.
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

        InputAction[] _slots;
        InputAction _previous, _next;
        IAimState _aim;
        bool _aimResolved;

        /// <summary>The slot (0-based) whose key went down this frame; -1 for none.
        /// The lowest wins if several did.</summary>
        public int SlotPressedThisFrame
        {
            get
            {
                if (Blocked || _slots == null) return -1;
                for (int i = 0; i < _slots.Length; i++)
                    if (_slots[i] != null && _slots[i].WasPressedThisFrame()) return i;
                return -1;
            }
        }

        /// <summary>+1 for next, -1 for previous, 0 for neither (or both) this frame.</summary>
        public int CycleThisFrame
        {
            get
            {
                if (Blocked) return 0;
                if (padCycleOnlyWhileAiming && Aim != null && !Aim.IsAiming) return 0;
                int step = 0;
                if (_next != null && _next.WasPressedThisFrame()) step++;
                if (_previous != null && _previous.WasPressedThisFrame()) step--;
                return step;
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
                return _aim is Object o && o == null ? null : _aim;
            }
        }

        void OnEnable()
        {
            _aimResolved = false;
            if (numberKeys)
            {
                _slots = new InputAction[SlotCount];
                for (int i = 0; i < SlotCount; i++)
                {
                    var action = new InputAction($"SpellSlot{i + 1}", InputActionType.Button);
                    action.AddBinding($"<Keyboard>/{i + 1}");
                    action.AddBinding($"<Keyboard>/numpad{i + 1}");
                    action.Enable();
                    _slots[i] = action;
                }
            }
            _previous = Button("SpellPrevious", padPreviousBinding);
            _next = Button("SpellNext", padNextBinding);
        }

        void OnDisable()
        {
            if (_slots != null)
            {
                for (int i = 0; i < _slots.Length; i++) Dispose(ref _slots[i]);
                _slots = null;
            }
            Dispose(ref _previous);
            Dispose(ref _next);
        }

        // An Inspector edit to a binding takes effect on the next enable.
        void OnValidate() => _aimResolved = false;

        static InputAction Button(string name, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
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
