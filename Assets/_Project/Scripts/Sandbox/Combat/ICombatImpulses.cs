using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// The only things combat may ask of movement: a one-off shove, a brief
    /// loss of drive, and a moment's hang in the air. Each is a request any
    /// system can already make of locomotion (a well core locks you, a launcher
    /// shoves you); combat gets no special access, and never sets movement
    /// state directly.
    ///
    /// Kept separate from <see cref="ICombatMovement"/> so a read-only consumer
    /// (HUD, AI) can depend on the state without being handed the levers.
    ///
    /// The shove is horizontal: ChibiLocomotion drops the y of AddMomentum.
    /// Vertical motion goes through <see cref="SuspendGravity"/> (air-combo hang
    /// time) and <see cref="SetVerticalVelocity"/> (catching a fall as a hover
    /// opens, an air step's lift or slam).
    /// </summary>
    public interface ICombatImpulses
    {
        /// <summary>Add a one-off horizontal velocity change, in m/s. An IMPULSE:
        /// call once per event, never per frame, or it compounds.</summary>
        void AddImpulse(Vector3 velocityChange);

        /// <summary>Take the player's drive away for <paramref name="seconds"/>.
        /// Refreshes rather than stacks.</summary>
        void LockMovement(float seconds);

        /// <summary>Hold the player's gravity off for <paramref name="seconds"/>,
        /// easing vertical speed to rest. Refreshes rather than stacks, so call it
        /// every frame of a hang with a short time; it ends on landing.</summary>
        void SuspendGravity(float seconds);
        /// <summary>Set the player's vertical speed outright, in m/s. Once per
        /// event (a swing starting, a hover opening), never per frame.</summary>
        void SetVerticalVelocity(float metresPerSecond);

        /// <summary>The rate <see cref="SuspendGravity"/> eases vertical speed to rest,
        /// in m/s². Lets combat pick a vertical speed that stops at a chosen height.</summary>
        float HangHaltRate { get; }

        /// <summary>End the player's own horizontal travel and carried momentum
        /// outright. Once, on an event: an air homing swing arriving at its target.</summary>
        void StopPlanar();

        /// <summary>Turn the body to face <paramref name="point"/> for the next
        /// <paramref name="seconds"/>, urgently, even while he is moving. For a swing
        /// that must land on a target he hasn't locked; a lock-on faces it anyway.</summary>
        void FaceToward(Vector3 point, float seconds);
    }
}
