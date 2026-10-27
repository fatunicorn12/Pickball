using UnityEngine;

namespace Pickleball.Sandbox.Combat
{
    /// <summary>
    /// A step's area burst has been released. Carries everything a listener
    /// needs to draw the ring in step with the damage — centre, final radius,
    /// growth time — so the visual never has to guess the gameplay numbers.
    /// </summary>
    public readonly struct BurstReport
    {
        /// <summary>The step that released it.</summary>
        public ResolvedStep Placement { get; }

        /// <summary>World-space centre on the ground, where the ring starts.</summary>
        public Vector3 Centre { get; }

        /// <summary>The settings the ring is running with (radius in m, growth in s).</summary>
        public AreaBurst Burst { get; }

        public BurstReport(ResolvedStep placement, Vector3 centre, AreaBurst burst)
        {
            Placement = placement;
            Centre = centre;
            Burst = burst;
        }
    }
}
