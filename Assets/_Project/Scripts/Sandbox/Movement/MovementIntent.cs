using UnityEngine;

namespace Pickleball.Sandbox.Movement
{
    /// <summary>
    /// One frame of what the player is asking movement to do — already mapped to
    /// world space, with no idea of what movement will make of it.
    ///
    /// A plain struct so states can be driven from tests or AI without an input
    /// device, and so the motor reads input exactly once per frame (every state
    /// sees the same presses, which matters for one-frame "pressed" edges).
    /// </summary>
    public struct MovementIntent
    {
        /// <summary>Camera-relative planar direction, unit length or zero.</summary>
        public Vector3 MoveDirection;
        /// <summary>Stick deflection, 0–1.</summary>
        public float MoveMagnitude;
        /// <summary>Raw stick in screen space, −1..1 per axis (for the spin gesture).</summary>
        public Vector2 RawStick;

        public bool JumpPressed, JumpHeld;
        public bool SprintHeld;
        /// <summary>The crouch / dive button (Ctrl, gamepad B).</summary>
        public bool CrouchPressed, CrouchHeld;
        /// <summary>The ground front-flip trick (F, gamepad Y).</summary>
        public bool TrickPressed;
        public bool MountPressed;
        public bool DashPressed;
        public bool TetherPressed;
        public bool AnchorPressed;

        /// <summary>True while the stick is pushed beyond the dead zone.</summary>
        public bool HasMove => MoveMagnitude > 0.001f;

        /// <summary>The planar velocity direction the stick asks for, scaled by deflection.</summary>
        public Vector3 Move => MoveDirection * MoveMagnitude;
    }
}
