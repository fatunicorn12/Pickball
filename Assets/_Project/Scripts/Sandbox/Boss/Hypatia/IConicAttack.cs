using System;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>Conic Sections, as the brain drives them.</summary>
    public interface IConicAttack : IHypatiaAttack
    {
        /// <summary>
        /// Fire one volley of <paramref name="kind"/> at eccentricity
        /// <paramref name="eccentricity"/> (the value the needle just showed). The
        /// attack runs its own shape telegraph (landing marker, asymptotes) and
        /// raises <see cref="VolleyFinished"/> when the volley is done: lobs have
        /// landed, the flyby has left, the orbiters are all out.
        /// </summary>
        void Fire(ConicKind kind, float eccentricity);

        /// <summary>True from <see cref="Fire"/> until <see cref="VolleyFinished"/>.</summary>
        bool IsBusy { get; }

        /// <summary>The volley is over; the brain opens the recalibration window.</summary>
        event Action<ConicKind> VolleyFinished;
    }
}
