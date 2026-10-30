using Pickleball.Sandbox.Combat;
using Pickleball.Sandbox.Hands;
using Pickleball.Sandbox.Weapon;
using UnityEngine;

namespace Pickleball.Sandbox.Inventory
{
    /// <summary>
    /// One weapon as data: the model the hand holds and the combo it swings.
    /// Equipping a weapon swaps both at once, so a heavy blade never plays the
    /// light blade's chain or the reverse.
    ///
    /// The held prefab brings its own grip anchor and blade markers
    /// (<see cref="HeldItem"/>), so the hand seats any weapon the same way and
    /// the hit sweep, trail and clearance follow the blade that is actually
    /// held.
    /// </summary>
    [CreateAssetMenu(menuName = "Pickleball/Inventory/Weapon", fileName = "Weapon")]
    public class WeaponDefinition : InventoryItemDefinition
    {
        [Header("Held model")]
        [Tooltip("Prefab the hand holds. Its HeldItem must have a grip anchor and " +
                 "both blade markers set.")]
        [SerializeField] private HeldItem heldPrefab;

        [Header("Combat")]
        [Tooltip("The combo this weapon swings: its ground and air chains and " +
                 "default finishers.")]
        [SerializeField] private SwordMoveset moveset;

        [Tooltip("Radius of the capsule swept along the blade to find hits (m). " +
                 "Roughly half the blade's width plus a little forgiveness. " +
                 "Bigger is easier to land and reads as a heavier weapon.")]
        [Min(0.01f)] [SerializeField] private float bladeRadius = 0.18f;

        [Tooltip("Keeps this blade out of the wielder's head and body. Empty " +
                 "keeps whatever profile the player already has.")]
        [SerializeField] private WeaponClearanceProfile clearanceProfile;

        /// <inheritdoc/>
        public override InventoryCategory Category => InventoryCategory.Weapon;

        /// <summary>The prefab the hand holds.</summary>
        public HeldItem HeldPrefab => heldPrefab;

        /// <summary>The combo this weapon swings.</summary>
        public SwordMoveset Moveset => moveset;

        /// <summary>Hit-sweep capsule radius in metres.</summary>
        public float BladeRadius => bladeRadius;

        /// <summary>Clearance profile for this blade, or null to keep the current one.</summary>
        public WeaponClearanceProfile ClearanceProfile => clearanceProfile;

        /// <summary>Wires the weapon's parts. For setup scripts and tests.</summary>
        public void Configure(HeldItem prefab, SwordMoveset weaponMoveset, float radius,
                              WeaponClearanceProfile clearance)
        {
            heldPrefab = prefab;
            moveset = weaponMoveset;
            bladeRadius = Mathf.Max(0.01f, radius);
            clearanceProfile = clearance;
        }
    }
}
