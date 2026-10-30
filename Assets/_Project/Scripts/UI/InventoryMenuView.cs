using Pickleball.Sandbox.Inventory;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Pickleball.UI
{
    /// <summary>
    /// Draws the inventory menu: a dimmed screen, a dark rounded panel with
    /// three tabs (Weapons, Magic, Key Items), the tab's slot grid, and the
    /// highlighted item's name, status line and description beside it. The
    /// equipped weapon (Weapons tab) and the selected spell (Magic tab) carry
    /// a gold marker; the highlighted slot stands taller with a bright frame.
    /// Same parts and palette as the spell selector (slot well and frame
    /// sprites, Oswald with the HUD outline), so the two read as one HUD.
    ///
    /// Presentation only. It reads <see cref="InventoryMenu"/> and its
    /// <see cref="Inventory"/>, redraws text and sprites only when one of
    /// their events says something changed, and eases the highlight and fade
    /// on unscaled time (the menu runs with the game paused). It is also the
    /// mouse: hovering a slot highlights it, clicking uses it, clicking a tab
    /// shows it, through the menu's own methods, the way DialogueView reports
    /// clicks to the dialogue director. No EventSystem is needed.
    ///
    /// Built by InventoryHudBuilder with as many slots as the largest tab;
    /// slots past the current tab's capacity are hidden.
    /// </summary>
    public class InventoryMenuView : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("The menu drawn. Hidden while missing.")]
        [SerializeField] private InventoryMenu menu;

        [Tooltip("Optional. Says when a weapon switch is waiting for the swing to end.")]
        [SerializeField] private WeaponEquipper equipper;

        [Header("Parts")]
        [Tooltip("Fades the whole menu; blocks nothing (the mouse is polled).")]
        [SerializeField] private CanvasGroup group;
        [Tooltip("One per tab, in tab order (Weapons, Magic, Key Items): the tab's plate.")]
        [SerializeField] private Image[] tabPlates;
        [Tooltip("One per tab: the tab's label.")]
        [SerializeField] private TMP_Text[] tabLabels;
        [Tooltip("One per slot, in slot order: scaled for the highlight.")]
        [SerializeField] private RectTransform[] slots;
        [Tooltip("One per slot: the slot's frame.")]
        [SerializeField] private Image[] slotFrames;
        [Tooltip("One per slot: the item's icon.")]
        [SerializeField] private Image[] slotIcons;
        [Tooltip("One per slot: the item's initial, shown when it has no icon.")]
        [SerializeField] private TMP_Text[] slotInitials;
        [Tooltip("One per slot: the equipped / selected marker.")]
        [SerializeField] private Image[] slotMarkers;
        [Tooltip("The highlighted item's name.")]
        [SerializeField] private TMP_Text nameText;
        [Tooltip("Under the name: Equipped, Selected, a key item's status.")]
        [SerializeField] private TMP_Text statusText;
        [Tooltip("The highlighted item's description.")]
        [SerializeField] private TMP_Text descriptionText;
        [Tooltip("The large icon's well, hidden while the highlighted slot is empty.")]
        [SerializeField] private GameObject detailWell;
        [Tooltip("Large icon beside the name.")]
        [SerializeField] private Image detailIcon;
        [Tooltip("Large initial, shown when the item has no icon.")]
        [SerializeField] private TMP_Text detailInitial;
        [Tooltip("Optional. The tab's count, \"2 / 8\".")]
        [SerializeField] private TMP_Text countText;

        [Header("Colour")]
        [Tooltip("Plate of the tab shown.")]
        [SerializeField] private Color tabActive = new(0.32f, 0.62f, 1f, 0.95f);
        [Tooltip("Plate of the other tabs.")]
        [SerializeField] private Color tabIdle = new(0.12f, 0.15f, 0.26f, 0.9f);
        [Tooltip("Frame of an ordinary slot.")]
        [SerializeField] private Color frameIdle = new(0.62f, 0.68f, 0.8f, 0.75f);
        [Tooltip("Frame of the highlighted slot.")]
        [SerializeField] private Color frameHighlighted = Color.white;
        [Tooltip("Frame of an empty slot.")]
        [SerializeField] private Color frameEmpty = new(0.62f, 0.68f, 0.8f, 0.25f);
        [Tooltip("Equipped / selected marker, and the status line when equipped.")]
        [SerializeField] private Color markerColour = new(1f, 0.8f, 0.25f, 1f);
        [Tooltip("Status line otherwise.")]
        [SerializeField] private Color statusColour = new(0.62f, 0.84f, 1f, 1f);

        [Header("Motion")]
        [Tooltip("Scale of the highlighted slot (x).")]
        [Min(1f)] [SerializeField] private float highlightScale = 1.12f;
        [Tooltip("Seconds for slot scale to ease to its target.")]
        [Min(0.001f)] [SerializeField] private float smoothing = 0.05f;
        [Tooltip("Seconds the menu takes to fade in or out.")]
        [Min(0.01f)] [SerializeField] private float fadeSeconds = 0.12f;

        static readonly string[] TabNames = { "WEAPONS", "MAGIC", "KEY ITEMS" };

        float[] _scales;
        bool _dirty = true;
        bool _pendingShown;
        InventoryMenu _subscribedMenu;
        Inventory _subscribedInventory;
        WeaponEquipper _subscribedEquipper;

        void OnEnable()
        {
            int n = slots != null ? slots.Length : 0;
            _scales = new float[n];
            for (int i = 0; i < n; i++) _scales[i] = 1f;
            _dirty = true;
            Subscribe(menu, equipper);
            if (group != null) group.alpha = menu != null && menu.IsOpen ? 1f : 0f;
        }

        void OnDisable() => Subscribe(null, null);

        /// <summary>Points the view at a menu at runtime. Null hides it.</summary>
        public void Bind(InventoryMenu inventoryMenu, WeaponEquipper weaponEquipper)
        {
            menu = inventoryMenu;
            equipper = weaponEquipper;
            if (isActiveAndEnabled) Subscribe(menu, equipper);
            _dirty = true;
        }

        void Subscribe(InventoryMenu nextMenu, WeaponEquipper nextEquipper)
        {
            if (_subscribedMenu != null)
            {
                _subscribedMenu.HighlightChanged -= MarkDirty;
                _subscribedMenu.Opened -= MarkDirty;
                _subscribedMenu.ItemUsed -= OnItemUsed;
            }
            if (_subscribedInventory != null)
            {
                _subscribedInventory.Changed -= OnInventoryChanged;
                _subscribedInventory.EquippedChanged -= OnEquippedChanged;
            }
            if (_subscribedEquipper != null) _subscribedEquipper.WeaponEquipped -= OnEquippedChanged;

            _subscribedMenu = nextMenu;
            _subscribedInventory = nextMenu != null ? nextMenu.Inventory : null;
            _subscribedEquipper = nextEquipper;

            if (_subscribedMenu != null)
            {
                _subscribedMenu.HighlightChanged += MarkDirty;
                _subscribedMenu.Opened += MarkDirty;
                _subscribedMenu.ItemUsed += OnItemUsed;
            }
            if (_subscribedInventory != null)
            {
                _subscribedInventory.Changed += OnInventoryChanged;
                _subscribedInventory.EquippedChanged += OnEquippedChanged;
            }
            if (_subscribedEquipper != null) _subscribedEquipper.WeaponEquipped += OnEquippedChanged;
        }

        void MarkDirty() => _dirty = true;
        void OnInventoryChanged(InventoryCategory _) => _dirty = true;
        void OnEquippedChanged(WeaponDefinition _) => _dirty = true;
        void OnItemUsed(InventoryItemDefinition item, bool changed) => _dirty = true;

        void Update()
        {
            bool open = menu != null && menu.IsOpen;
            float dt = Time.unscaledDeltaTime;
            if (group != null)
                group.alpha = Mathf.MoveTowards(group.alpha, open ? 1f : 0f, dt / fadeSeconds);
            if (!open) return;

            // A waiting switch lands when the swing ends, with no event while
            // the menu is open; poll the one flag rather than miss it.
            bool pending = equipper != null && equipper.IsSwitchPending;
            if (pending != _pendingShown) _dirty = true;

            PollPointer();
            if (_dirty) Redraw();
            AnimateSlots(dt);
        }

        // ------------------------------------------------------------ drawing

        void Redraw()
        {
            _dirty = false;
            var inventory = menu.Inventory;
            if (inventory == null) return;
            var model = inventory.Model;
            var tab = menu.Tab;
            int capacity = menu.SlotCount;
            InventoryItemDefinition marked = tab switch
            {
                InventoryCategory.Weapon => model.Equipped,
                InventoryCategory.Magic => inventory.SelectedSpellItem,
                _ => null,
            };

            for (int t = 0; t < TabNames.Length; t++)
            {
                bool active = (int)tab == t;
                var plate = At(tabPlates, t);
                if (plate != null) plate.color = active ? tabActive : tabIdle;
                var label = At(tabLabels, t);
                if (label != null)
                {
                    label.text = TabNames[t];
                    label.alpha = active ? 1f : 0.6f;
                }
            }

            int n = slots != null ? slots.Length : 0;
            for (int i = 0; i < n; i++)
            {
                var slot = slots[i];
                if (slot == null) continue;
                bool exists = i < capacity;
                if (slot.gameObject.activeSelf != exists) slot.gameObject.SetActive(exists);
                if (!exists) continue;

                var item = model.ItemAt(tab, i);
                bool highlighted = i == menu.Slot;
                var frame = At(slotFrames, i);
                if (frame != null)
                    frame.color = highlighted ? frameHighlighted : item != null ? frameIdle : frameEmpty;

                DrawIcon(item, At(slotIcons, i), At(slotInitials, i));
                var marker = At(slotMarkers, i);
                if (marker != null)
                {
                    bool on = item != null && item == marked;
                    marker.enabled = on;
                    marker.color = markerColour;
                }
            }

            if (countText != null) countText.text = $"{model.Count(tab)} / {capacity}";
            DrawDetail(menu.Highlighted, marked, tab);
        }

        void DrawDetail(InventoryItemDefinition item, InventoryItemDefinition marked, InventoryCategory tab)
        {
            _pendingShown = equipper != null && equipper.IsSwitchPending;
            if (nameText != null) nameText.text = item != null ? item.DisplayName : "";
            if (descriptionText != null)
                descriptionText.text = item != null ? item.Description
                    : tab switch
                    {
                        InventoryCategory.Weapon => "An empty weapon slot.",
                        InventoryCategory.Magic => "An empty spell slot.",
                        _ => "Abilities you find show up here.",
                    };
            DrawIcon(item, detailIcon, detailInitial);
            if (detailWell != null && detailWell.activeSelf != (item != null)) detailWell.SetActive(item != null);

            if (statusText == null) return;
            bool isMarked = item != null && item == marked;
            string status = "";
            if (item is WeaponDefinition)
            {
                if (isMarked && _pendingShown) status = "Equipped · switches when the swing ends";
                else status = isMarked ? "Equipped" : "Use to equip";
            }
            else if (item is MagicItemDefinition magic)
            {
                int cost = magic.Spell != null ? magic.Spell.ManaCost : 0;
                status = (isMarked ? "Selected" : "Use to select") + (cost > 0 ? $"  ·  {cost} mana" : "");
            }
            else if (item is KeyItemDefinition)
            {
                status = "Key item · always on";
            }
            statusText.text = status;
            statusText.color = isMarked ? markerColour : statusColour;
        }

        static void DrawIcon(InventoryItemDefinition item, Image icon, TMP_Text initial)
        {
            Sprite sprite = null;
            Color tint = Color.white;
            if (item != null)
            {
                sprite = item.Icon;
                if (item is MagicItemDefinition magic && magic.Spell != null)
                {
                    if (sprite == null) sprite = magic.Spell.Icon;
                    tint = magic.Spell.ThemeColour;
                    tint.a = 1f;
                }
            }
            if (icon != null)
            {
                if (icon.sprite != sprite) icon.sprite = sprite;
                icon.enabled = sprite != null;
                icon.color = tint;
            }
            if (initial != null)
            {
                bool show = item != null && sprite == null && !string.IsNullOrEmpty(item.DisplayName);
                initial.enabled = show;
                if (show) initial.text = item.DisplayName.Substring(0, 1).ToUpperInvariant();
            }
        }

        void AnimateSlots(float dt)
        {
            if (slots == null || _scales == null) return;
            float ease = 1f - Mathf.Exp(-dt / smoothing);
            for (int i = 0; i < slots.Length && i < _scales.Length; i++)
            {
                var slot = slots[i];
                if (slot == null || !slot.gameObject.activeSelf) continue;
                float goal = i == menu.Slot ? highlightScale : 1f;
                _scales[i] = Mathf.Lerp(_scales[i], goal, ease);
                slot.localScale = new Vector3(_scales[i], _scales[i], 1f);
            }
        }

        // ------------------------------------------------------------ mouse

        void PollPointer()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse == null) return;
            bool moved = mouse.delta.ReadValue().sqrMagnitude > 0f;
            bool clicked = mouse.leftButton.wasPressedThisFrame;
            if (!moved && !clicked) return;
            Vector2 point = mouse.position.ReadValue();

            int tab = HitIndex(tabPlates, point, TabNames.Length);
            if (tab >= 0)
            {
                if (clicked) menu.SelectTab((InventoryCategory)tab);
                return;
            }
            int slot = HitIndex(slots, point, menu.SlotCount);
            if (slot < 0) return;
            if (clicked) menu.UseSlot(slot);
            else menu.Highlight(slot);
        }

        static int HitIndex<T>(T[] parts, Vector2 screenPoint, int count) where T : Component
        {
            if (parts == null) return -1;
            for (int i = 0; i < parts.Length && i < count; i++)
            {
                var part = parts[i];
                if (part == null || !part.gameObject.activeInHierarchy) continue;
                if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)part.transform, screenPoint, null))
                    return i;
            }
            return -1;
        }

        static T At<T>(T[] array, int i) where T : class => array != null && i >= 0 && i < array.Length ? array[i] : null;
    }
}
