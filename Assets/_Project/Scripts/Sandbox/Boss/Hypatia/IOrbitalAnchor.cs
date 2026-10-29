using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// A moving point the planned Orbital Tether (Hypatia's reward) can latch onto
    /// and swing around: today, her elliptical orbiters. Read-only on purpose; the
    /// tether will read where the anchor is and where it is going, never steer it.
    /// Nothing implements the tether yet.
    /// </summary>
    public interface IOrbitalAnchor
    {
        /// <summary>World position this frame.</summary>
        Vector3 Position { get; }

        /// <summary>World velocity this frame, m/s (already slowed, if slowed).</summary>
        Vector3 Velocity { get; }

        /// <summary>False once it has expired or been destroyed; drop the latch.</summary>
        bool IsAlive { get; }
    }
}
