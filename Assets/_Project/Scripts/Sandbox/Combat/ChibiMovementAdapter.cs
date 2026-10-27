using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Presents <see cref="ChibiLocomotion"/> to combat as
    /// <see cref="IMovementState"/> + <see cref="IMovementImpulses"/>, using only
    /// its existing public API.
    ///
    /// This is the single file that has to change when the movement rework
    /// lands: dive- or spin-specific slashes add a property to the interface and
    /// one line here, and the combo never references a movement class.
    /// </summary>
    public class ChibiMovementAdapter : MonoBehaviour, IMovementState, IMovementImpulses
    {
        [Tooltip("The locomotion being adapted. Found on this object if left empty.")]
        [SerializeField] private ChibiLocomotion locomotion;

        void Awake()
        {
            if (locomotion == null) locomotion = GetComponent<ChibiLocomotion>();
        }

        // With no locomotion the player is treated as a grounded, free swordsman,
        // which is what the old MeleeCombat did — a bare test rig still swings.
        public bool IsGrounded => locomotion == null || locomotion.IsGrounded;
        public Vector3 Velocity => locomotion != null ? locomotion.Velocity : Vector3.zero;
        public bool IsBusy => locomotion != null && locomotion.IsBusy;
        public bool IsMovementLocked => locomotion != null && locomotion.IsMovementLocked;

        public void AddImpulse(Vector3 velocityChange)
        {
            if (locomotion != null) locomotion.AddMomentum(velocityChange);
        }

        public void LockMovement(float seconds)
        {
            if (locomotion != null && seconds > 0f) locomotion.LockMovement(seconds);
        }
    }
}
