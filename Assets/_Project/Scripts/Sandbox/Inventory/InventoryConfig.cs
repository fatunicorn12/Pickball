using UnityEngine;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// The inventory's tunables: slots per tab and how the menu's grid is laid
    /// out and navigated. One asset shared by every scene
    /// (<c>ScriptableObjects/Inventory/InventoryConfig.asset</c>, seeded once by
    /// Setup Inventory and then the source of truth).
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Inventory/Inventory Config", fileName = "InventoryConfig")]
    public class InventoryConfig : ScriptableObject
    {
        [Header("Slots")]
        [Tooltip("Slots per tab. An item added to a full tab is refused (the pickup stays). " +
                 "The menu draws every slot, filled or not, so this is also the grid's size; " +
                 "re-run Setup Inventory after raising it past the largest tab.")]
        [SerializeField] private InventoryCapacity capacity = InventoryCapacity.Default;

        [Header("Menu")]
        [Tooltip("Slots per row in the menu grid (count). Up/down moves by this many.")]
        [Range(1, 10)] [SerializeField] private int columns = 5;

        [Tooltip("Seconds a held stick or d-pad direction waits before it starts repeating " +
                 "(unscaled; the menu runs with the game paused). Lower feels twitchier.")]
        [Range(0.1f, 1f)] [SerializeField] private float repeatDelay = 0.35f;

        [Tooltip("Seconds between repeats while a direction is held. Lower scrolls faster.")]
        [Range(0.03f, 0.5f)] [SerializeField] private float repeatInterval = 0.12f;

        [Tooltip("Stick tilt (0-1) that counts as a direction in the menu. Higher needs a " +
                 "firmer push and ignores a drifting stick.")]
        [Range(0.2f, 0.9f)] [SerializeField] private float stickThreshold = 0.55f;

        /// <summary>Slots per tab.</summary>
        public InventoryCapacity Capacity => capacity;

        /// <summary>Slots per row in the menu grid.</summary>
        public int Columns => Mathf.Max(1, columns);

        /// <summary>Hold time before a direction repeats (s, unscaled).</summary>
        public float RepeatDelay => repeatDelay;

        /// <summary>Time between repeats (s, unscaled).</summary>
        public float RepeatInterval => repeatInterval;

        /// <summary>Stick tilt that counts as a direction.</summary>
        public float StickThreshold => stickThreshold;

        /// <summary>Sets the slot counts and columns. For setup scripts and tests.</summary>
        public void Configure(InventoryCapacity slots, int gridColumns)
        {
            capacity = slots;
            columns = Mathf.Max(1, gridColumns);
        }
    }
}
