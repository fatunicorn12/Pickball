using System;
using UnityEngine;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// A collectable that puts any <see cref="InventoryItemDefinition"/> in the
    /// toucher's <see cref="Inventory"/>: a weapon found in the world (the
    /// ruler, later), a spell scroll, a key item. The inventory's own rules
    /// apply, so a key item grants its ability through PlayerAbilities, and a
    /// full tab or an item already owned leaves the pickup where it is.
    ///
    /// The same idle motion as <see cref="AbilityPickup"/> (spin and bob from
    /// where it was placed, on its first child), so the two read as one family.
    /// </summary>
    [RequireComponent(typeof(SphereCollider))]
    public class ItemPickup : MonoBehaviour
    {
        [Header("Grants")]
        [Tooltip("The item put in the inventory.")]
        [SerializeField] private InventoryItemDefinition item;

        [Tooltip("Equip a weapon as soon as it is picked up. Off: it waits in the Weapons tab.")]
        [SerializeField] private bool equipOnPickup;

        [Tooltip("Destroy the pickup once collected. Untick to leave it for testing.")]
        [SerializeField] private bool consumeOnPickup = true;

        [Header("Idle motion")]
        [Tooltip("Spin of the visual (°/s).")]
        [SerializeField] private float spinDegreesPerSecond = 75f;
        [Tooltip("Bob height above and below the start (m).")]
        [SerializeField] private float hoverAmplitude = 0.18f;
        [Tooltip("Seconds per bob cycle.")]
        [Min(0.01f)] [SerializeField] private float hoverCycleSeconds = 2.2f;
        [Tooltip("Spun and bobbed; the first child when empty. Kept apart from the root so " +
                 "the trigger stays still.")]
        [SerializeField] private Transform visual;

        /// <summary>Raised when collected, with the inventory it went into. For effects and sounds.</summary>
        public event Action<Inventory> Collected;

        /// <summary>The item this grants.</summary>
        public InventoryItemDefinition Item => item;

        Vector3 _visualStart;
        float _phase;
        bool _taken;

        void Awake()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual != null) _visualStart = visual.localPosition;
            GetComponent<SphereCollider>().isTrigger = true;
            _phase = UnityEngine.Random.value * 10f;
        }

        void Update()
        {
            if (_taken || visual == null) return;
            float t = Time.time + _phase;
            visual.Rotate(Vector3.up, spinDegreesPerSecond * Time.deltaTime, Space.Self);
            visual.localPosition = _visualStart + Vector3.up *
                (Mathf.Sin(t * Mathf.PI * 2f / hoverCycleSeconds) * hoverAmplitude);
        }

        void OnTriggerEnter(Collider other)
        {
            if (_taken || item == null) return;
            var inventory = other.GetComponentInParent<Inventory>();
            if (inventory == null) return;
            if (inventory.Add(item) != InventoryAddResult.Added) return;
            if (equipOnPickup && item is WeaponDefinition weapon) inventory.Equip(weapon);

            _taken = true;
            Collected?.Invoke(inventory);
            if (consumeOnPickup) Destroy(gameObject);
            else _taken = false;
        }

        /// <summary>Sets the item. For setup scripts and tests.</summary>
        public void Configure(InventoryItemDefinition granted, bool equip)
        {
            item = granted;
            equipOnPickup = equip;
        }
    }
}
