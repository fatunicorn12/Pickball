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

        [Header("Carry pose")]
        [Tooltip("How the weapon arm holds this blade at rest, standing: a one-frame Humanoid " +
                 "clip played on the Carry layer (right arm only) under the swings. Empty = " +
                 "no carry pose: the locomotion clips' own arm, as for the compass. The Carry " +
                 "layer's state is named after this clip; run Tools ▸ Pickleball ▸ Setup " +
                 "Weapon Carry after changing it.")]
        [SerializeField] private AnimationClip carryPose;

        [Tooltip("The same carry authored over the walk, whose chest faces ahead where the " +
                 "idle's is turned. Blended with the standing pose on the Animator's Speed, " +
                 "as idle and walk are. Empty = the standing pose at every speed.")]
        [SerializeField] private AnimationClip carryPoseMoving;

        [Tooltip("Carry layer weight while the pose shows (0-1). 1 holds the arm exactly in " +
                 "the pose; lower lets the locomotion clips' arm swing show through.")]
        [Range(0f, 1f)] [SerializeField] private float carryWeight = 1f;

        [Tooltip("Seconds the carry takes to fade in when the weapon is equipped or a move " +
                 "that needed the arm ends, and to fade out when it is unequipped (s). " +
                 "Shorter snaps the blade into place; longer drifts.")]
        [Range(0.02f, 1f)] [SerializeField] private float carryBlendSeconds = 0.25f;

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

        /// <summary>The standing carry pose, or null for none.</summary>
        public AnimationClip CarryPose => carryPose;

        /// <summary>The moving carry pose, or null to use <see cref="CarryPose"/> at every speed.</summary>
        public AnimationClip CarryPoseMoving => carryPoseMoving;

        /// <summary>True if this weapon has a carry pose that shows (a clip and a weight above 0).</summary>
        public bool HasCarryPose => carryPose != null && carryWeight > 0f;

        /// <summary>Carry layer weight while the pose shows, 0-1.</summary>
        public float CarryWeight => carryWeight;

        /// <summary>Carry fade time in seconds.</summary>
        public float CarryBlendSeconds => carryBlendSeconds;

        /// <summary>Wires the carry pose. For setup scripts and tests.</summary>
        public void ConfigureCarry(AnimationClip standing, AnimationClip moving, float weight, float blendSeconds)
        {
            carryPose = standing;
            carryPoseMoving = moving;
            carryWeight = Mathf.Clamp01(weight);
            carryBlendSeconds = Mathf.Clamp(blendSeconds, 0.02f, 1f);
        }

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
