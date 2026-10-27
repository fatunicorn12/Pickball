using System;
using UnityEngine;

namespace Pickleball.Sandbox.Movement
{
    /// <summary>
    /// Read-only view of the player's movement, for systems that react to it
    /// without being allowed to drive it: combat (dive and spin slashes), the
    /// animator driver, VFX, audio, HUD.
    ///
    /// Dependencies point one way: those systems hold this interface, and
    /// movement never learns they exist. Nothing here can change movement —
    /// pushes go through the motor's own impulse/force API.
    /// </summary>
    public interface IMovementState
    {
        /// <summary>The move in progress.</summary>
        MoveKind Current { get; }

        /// <summary>The move before <see cref="Current"/>.</summary>
        MoveKind Previous { get; }

        /// <summary>Which jump, while <see cref="Current"/> is <see cref="MoveKind.Jump"/>;
        /// otherwise <see cref="JumpKind.None"/>.</summary>
        JumpKind CurrentJump { get; }

        /// <summary>Seconds spent in <see cref="Current"/>.</summary>
        float TimeInMove { get; }

        /// <summary>Raised after every move change, once the new move has been
        /// entered (so its data — jump kind, dash direction — is already set).
        /// Arguments are (from, to). A move can re-enter itself, in which case
        /// from == to.</summary>
        event Action<MoveKind, MoveKind> MoveChanged;

        /// <summary>Feet on walkable ground this frame. Rising is never grounded.</summary>
        bool IsGrounded { get; }

        /// <summary>Measured world velocity, m/s — what the character actually did.</summary>
        Vector3 Velocity { get; }

        /// <summary>Speed along the travel direction from his own drive, m/s
        /// (excludes carried momentum and force fields).</summary>
        float PlanarSpeed { get; }

        /// <summary>Vertical speed, m/s, up positive.</summary>
        float VerticalSpeed { get; }

        /// <summary>Unit planar direction he is travelling in.</summary>
        Vector3 TravelDirection { get; }

        /// <summary>True while the stick asks for movement (not while merely coasting).</summary>
        bool HasMoveIntent { get; }

        /// <summary>0–1: how far into the sprint ramp he is (0 = run speed).</summary>
        float SprintBlend { get; }
    }
}
