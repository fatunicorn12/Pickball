using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// What combat is allowed to know about the player's movement — and
    /// nothing more.
    ///
    /// Dependencies point one way: combat reads movement, movement never knows
    /// combat exists. Going through this interface instead of ChibiLocomotion
    /// directly means the movement rework (dive, spin jump, ground pound) can
    /// swap the implementation, or extend it with the extra state a dive-slash
    /// needs, without the sword noticing. Implemented by
    /// <see cref="ChibiMovementAdapter"/>.
    ///
    /// Deliberately not named IMovementState: that is the movement system's own
    /// read-out (Pickleball.Sandbox.Movement, on the sunshine-movement branch),
    /// which the adapter is expected to wrap for its MoveKind (Dive, SpinJump…)
    /// once the two branches meet. This is combat's narrower view of it.
    /// </summary>
    public interface ICombatMovement
    {
        /// <summary>Standing on something this frame. Decides ground vs air chain.</summary>
        bool IsGrounded { get; }

        /// <summary>Measured world velocity, m/s.</summary>
        Vector3 Velocity { get; }

        /// <summary>True while another system owns the body (tether, skateboard,
        /// dash). Attacks refuse to start rather than fight it.</summary>
        bool IsBusy { get; }

        /// <summary>True while something has taken the player's drive away.</summary>
        bool IsMovementLocked { get; }
    }
}
