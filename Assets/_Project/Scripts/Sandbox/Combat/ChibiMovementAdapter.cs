using Pickleball.Sandbox.Movement;
using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// Presents <see cref="ChibiLocomotion"/> to combat as
    /// <see cref="ICombatMovement"/> + <see cref="ICombatImpulses"/>, using only
    /// its existing public API.
    ///
    /// This is the single file that has to change when the movement rework
    /// lands: dive- or spin-specific slashes add a property to the interface and
    /// one line here, and the combo never references a movement class.
    ///
    /// It is also combat's <see cref="IFacingSource"/>, registered with the
    /// locomotion while enabled. It answers only while a
    /// <see cref="FaceToward"/> request is running, and otherwise leaves facing
    /// to lock-on as before.
    /// </summary>
    public class ChibiMovementAdapter : MonoBehaviour, ICombatMovement, ICombatImpulses, IFacingSource
    {
        [Tooltip("The locomotion being adapted. Found on this object if left empty.")]
        [SerializeField] private ChibiLocomotion locomotion;

        Vector3 _facePoint;
        float _faceUntil = -1f;

        void Awake()
        {
            if (locomotion == null) locomotion = GetComponent<ChibiLocomotion>();
        }

        void OnEnable()
        {
            if (locomotion != null) locomotion.SetCombatFacingSource(this);
        }

        void OnDisable()
        {
            if (locomotion != null) locomotion.SetCombatFacingSource(null);
            _faceUntil = -1f;
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

        public void SuspendGravity(float seconds)
        {
            if (locomotion != null && seconds > 0f) locomotion.SuspendGravity(seconds);
        }

        public void SetVerticalVelocity(float metresPerSecond)
        {
            if (locomotion != null) locomotion.SetVerticalVelocity(metresPerSecond);
        }

        public float HangHaltRate
            => locomotion != null && locomotion.Motor != null ? locomotion.Motor.Config.AnchorHaltRate : 0f;

        public void StopPlanar()
        {
            if (locomotion != null) locomotion.StopPlanar();
        }

        public void FaceToward(Vector3 point, float seconds)
        {
            _facePoint = point;
            _faceUntil = Time.time + seconds;
        }

        bool IFacingSource.TryGetFacingTarget(out Vector3 point, out bool urgent)
        {
            point = _facePoint;
            urgent = true;
            return Time.time < _faceUntil;
        }
    }
}
