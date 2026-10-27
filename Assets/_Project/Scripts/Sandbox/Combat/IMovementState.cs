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
    /// </summary>
    public interface IMovementState
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
