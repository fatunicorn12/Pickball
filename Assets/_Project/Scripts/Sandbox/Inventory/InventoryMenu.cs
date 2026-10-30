using System;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// The inventory menu's state: open or shut, which tab, which slot is
    /// highlighted, and what using the highlighted item does. It owns the
    /// pause and the input hand-over while open; the view
    /// (<c>Pickleball.UI.InventoryMenuView</c>) only draws this and reports the
    /// mouse through <see cref="Highlight"/>, <see cref="SelectTab"/> and
    /// <see cref="UseSlot"/>.
    ///
    /// <b>Opening, in this order.</b>
    /// <list type="number">
    /// <item>Refused while a conversation (or anything else) blocks
    /// <see cref="InputBlock.Movement"/>, or while time is already stopped.</item>
    /// <item><c>Time.timeScale</c> goes to 0 through the dialogue's
    /// <see cref="TimeScalePause"/>, which records and restores the previous
    /// value and refuses when someone else already stopped time. Everything
    /// that runs on scaled time freezes where it is: movement, the swing
    /// timeline and hitstop (MeleeCombat), animators, cloth, UniTask delays,
    /// boss timers. The same pause dialogue uses, so nothing new has to cope
    /// with it.</item>
    /// <item><see cref="InputBlock.All"/> is claimed on the gate, so no gameplay
    /// reader acts on the keys the menu reuses (WASD, Q/E, Space, the pad).</item>
    /// <item>The orbit camera is disabled, as the dialogue camera does: it reads
    /// mouse look and the re-capture click on unscaled time, so pausing alone
    /// would leave the view turning and a slot click grabbing the cursor. The
    /// cursor is freed for the mouse.</item>
    /// <item>The Key Items and Magic tabs are re-synced, then <see cref="Opened"/>.</item>
    /// </list>
    /// Closing undoes each step in reverse (camera back first, cursor captured
    /// again if the camera owned it, input released, time restored), then
    /// <see cref="Closed"/>. Disabling the menu, which a script reload in Play
    /// mode does first, closes it, so time and input are never left held.
    ///
    /// <b>Using an item.</b> A weapon is equipped (it reaches the hand when the
    /// current swing ends), a spell is selected on the spell selector, a key
    /// item does nothing (its blurb says how to use it). The menu stays open.
    ///
    /// <see cref="Tick"/> is public so EditMode tests drive navigation with
    /// explicit time.
    /// </summary>
    [DisallowMultipleComponent]
    public class InventoryMenu : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The inventory shown. Found on this object when empty.")]
        [SerializeField] private Inventory inventory;

        [Tooltip("The menu's buttons. Found on this object when empty.")]
        [SerializeField] private InventoryInput input;

        [Tooltip("Claimed in full while the menu is open. Empty: nothing is blocked (time " +
                 "still stops), and the menu cannot tell a conversation is running.")]
        [SerializeField] private PlayerInputGate inputGate;

        [Tooltip("Disabled while the menu is open so the mouse moves the cursor, not the view. " +
                 "Empty: the camera keeps reading look input.")]
        [SerializeField] private SandboxOrbitCamera orbitCamera;

        /// <summary>Raised after the menu opens.</summary>
        public event Action Opened;

        /// <summary>Raised after the menu closes.</summary>
        public event Action Closed;

        /// <summary>Raised when the tab or the highlighted slot changes.</summary>
        public event Action HighlightChanged;

        /// <summary>Raised when an item is used; true if it changed something (equipped, selected).</summary>
        public event Action<InventoryItemDefinition, bool> ItemUsed;

        TimeScalePause _pause;
        DirectionRepeat _repeat;
        bool _disabledCamera, _cursorWasLocked;
        int _openedFrame = -1;

        /// <summary>True while the menu is open.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>The tab shown.</summary>
        public InventoryCategory Tab { get; private set; } = InventoryCategory.Weapon;

        /// <summary>The highlighted slot on the current tab (0-based).</summary>
        public int Slot { get; private set; }

        /// <summary>The inventory shown; may be null.</summary>
        public Inventory Inventory => inventory;

        /// <summary>Slots per row, from the inventory's config.</summary>
        public int Columns => inventory != null && inventory.Config != null ? inventory.Config.Columns : 5;

        /// <summary>Slots on the current tab.</summary>
        public int SlotCount => inventory != null ? inventory.Model.CapacityOf(Tab) : 0;

        /// <summary>The highlighted item, or null on an empty slot.</summary>
        public InventoryItemDefinition Highlighted => inventory != null ? inventory.Model.ItemAt(Tab, Slot) : null;

        void Awake()
        {
            if (inventory == null) inventory = GetComponent<Inventory>();
            if (input == null) input = GetComponent<InventoryInput>();
        }

        void OnEnable()
        {
            _pause ??= new TimeScalePause();
            _repeat ??= new DirectionRepeat();
        }

        void OnDisable() => Close();

        void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>One frame of menu input, <paramref name="unscaledDeltaTime"/> seconds long.</summary>
        public void Tick(float unscaledDeltaTime)
        {
            if (input == null) return;
            if (!IsOpen)
            {
                if (input.MenuPressed) Open();
                return;
            }
            // The press that opened the menu must not also close it or use a slot.
            if (Time.frameCount == _openedFrame) return;
            if (input.MenuPressed || input.BackPressed)
            {
                Close();
                return;
            }
            if (input.TabStep != 0) StepTab(input.TabStep);

            var config = inventory != null ? inventory.Config : null;
            Vector2Int step = _repeat.Tick(input.Navigate, unscaledDeltaTime,
                                           config != null ? config.StickThreshold : 0.55f,
                                           config != null ? config.RepeatDelay : 0.35f,
                                           config != null ? config.RepeatInterval : 0.12f);
            if (step != Vector2Int.zero) Move(step.x, step.y);
            if (input.ConfirmPressed) UseSlot(Slot);
        }

        /// <summary>Opens the menu. False, changing nothing, when it is open already or refused (see the class notes).</summary>
        public bool Open()
        {
            if (IsOpen || !isActiveAndEnabled || inventory == null) return false;
            if (inputGate != null && inputGate.IsBlocked(InputBlock.Movement)) return false;
            _pause ??= new TimeScalePause();
            if (!_pause.Hold(pauseAudio: false)) return false;

            IsOpen = true;
            _openedFrame = Time.frameCount;
            if (inputGate != null) inputGate.Claim(this, InputBlock.All);
            if (orbitCamera != null && orbitCamera.enabled)
            {
                orbitCamera.enabled = false;
                _disabledCamera = true;
            }
            _cursorWasLocked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            _repeat?.Reset();
            inventory.Resync();
            ClampSlot();
            Opened?.Invoke();
            HighlightChanged?.Invoke();
            return true;
        }

        /// <summary>Closes the menu and gives play back. Does nothing when shut.</summary>
        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            try
            {
                if (_disabledCamera && orbitCamera != null) orbitCamera.enabled = true;
                bool capture = orbitCamera != null ? orbitCamera.CursorCaptured : _cursorWasLocked;
                if (capture)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                    Cursor.visible = false;
                }
            }
            finally
            {
                _disabledCamera = false;
                if (inputGate != null) inputGate.Release(this);
                _pause?.Release();
            }
            Closed?.Invoke();
        }

        /// <summary>Opens a shut menu, closes an open one.</summary>
        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        /// <summary>Shows <paramref name="tab"/>, keeping the slot where it can.</summary>
        public void SelectTab(InventoryCategory tab)
        {
            if (tab == Tab) return;
            Tab = tab;
            ClampSlot();
            HighlightChanged?.Invoke();
        }

        /// <summary>Moves one tab left (-1) or right (+1), wrapping.</summary>
        public void StepTab(int step)
        {
            var tabs = InventoryModel.Categories;
            int current = 0;
            for (int i = 0; i < tabs.Count; i++) if (tabs[i] == Tab) current = i;
            SelectTab(tabs[InventoryGrid.StepTab(current, step, tabs.Count)]);
        }

        /// <summary>Moves the highlight by whole slots (x right, y up the screen).</summary>
        public void Move(int dx, int dy) => Highlight(InventoryGrid.Move(Slot, dx, dy, SlotCount, Columns));

        /// <summary>Highlights slot <paramref name="slot"/> of the current tab (ignored out of range).</summary>
        public void Highlight(int slot)
        {
            if (slot < 0 || slot >= SlotCount || slot == Slot) return;
            Slot = slot;
            HighlightChanged?.Invoke();
        }

        /// <summary>
        /// Highlights and uses slot <paramref name="slot"/>: equips a weapon,
        /// selects a spell. False when the slot is empty or using it changed
        /// nothing (already equipped, a key item).
        /// </summary>
        public bool UseSlot(int slot)
        {
            if (!IsOpen || inventory == null) return false;
            Highlight(slot);
            var item = inventory.Model.ItemAt(Tab, slot);
            if (item == null) return false;
            bool changed = item switch
            {
                WeaponDefinition weapon => inventory.Equip(weapon),
                MagicItemDefinition spell => inventory.SelectSpell(spell),
                _ => false,
            };
            ItemUsed?.Invoke(item, changed);
            return changed;
        }

        void ClampSlot()
        {
            int count = SlotCount;
            Slot = count <= 0 ? 0 : Mathf.Clamp(Slot, 0, count - 1);
        }

        /// <summary>Wires the menu. For setup scripts and tests.</summary>
        public void Bind(Inventory owner, InventoryInput buttons, PlayerInputGate gate, SandboxOrbitCamera camera)
        {
            inventory = owner;
            input = buttons;
            inputGate = gate;
            orbitCamera = camera;
        }
    }
}
