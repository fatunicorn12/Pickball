using UnityEngine;

namespace Pickleball.Sandbox.Movement
{
    /// <summary>
    /// Something that may want the body squared up to a point instead of facing
    /// the way it travels — lock-on, and an attack starting while locked on.
    ///
    /// This is how movement honours lock-on and combat without knowing either
    /// exists: the player's glue component (ChibiLocomotion) implements it and
    /// hands it to the motor. Movement decides WHEN facing applies (an urgent
    /// request always; a passive one only once he has stopped), the source only
    /// says WHERE and how urgently.
    /// </summary>
    public interface IFacingSource
    {
        /// <summary>True when the body should face <paramref name="point"/> this frame.
        /// <paramref name="urgent"/> requests a fast turn regardless of movement
        /// (an attack has to land on the target); a non-urgent request applies
        /// only while he is stopped.</summary>
        bool TryGetFacingTarget(out Vector3 point, out bool urgent);
    }
}
