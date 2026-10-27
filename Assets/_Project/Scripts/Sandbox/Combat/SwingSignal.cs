using System;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>What changed during one <see cref="SwingTimeline.Advance"/> tick.
    /// Flags, because a long frame (or a very short step) can cross several
    /// windows at once and none of them may be lost.</summary>
    [Flags]
    public enum SwingSignal
    {
        None = 0,

        /// <summary>The blade went live this tick.</summary>
        BladeOn = 1 << 0,

        /// <summary>The blade stopped hitting this tick.</summary>
        BladeOff = 1 << 1,

        /// <summary>The step's area burst is due to be released this tick.</summary>
        BurstDue = 1 << 2,

        /// <summary>The swing passed its comboClose and is over.</summary>
        Finished = 1 << 3,
    }
}
