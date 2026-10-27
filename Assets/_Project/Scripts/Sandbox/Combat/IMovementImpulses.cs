using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// The only two things combat may ask of movement: a one-off shove and a
    /// brief loss of drive. Both are requests any system can already make of
    /// locomotion (a well core locks you, a launcher shoves you); combat gets no
    /// special access, and never sets movement state directly.
    ///
    /// Kept separate from <see cref="IMovementState"/> so a read-only consumer
    /// (HUD, AI) can depend on the state without being handed the levers.
    /// </summary>
    public interface IMovementImpulses
    {
        /// <summary>Add a one-off horizontal velocity change, in m/s. An IMPULSE:
        /// call once per event, never per frame, or it compounds.</summary>
        void AddImpulse(Vector3 velocityChange);

        /// <summary>Take the player's drive away for <paramref name="seconds"/>.
        /// Refreshes rather than stacks.</summary>
        void LockMovement(float seconds);
    }
}
