using System;
using UnityEngine;

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

        /// <summary>
        /// An orbiter the player batted back reached her body at this point, and its
        /// damage has already been dealt through her Health. The brain topples her.
        /// </summary>
        event Action<Vector3> OrbiterStruckBoss;

        /// <summary>
        /// Shatter every orbiter now: those on their orbits AND those already batted
        /// and in flight, and spawn no more from the current ellipse volley. The
        /// brain calls it when she topples, so one volley can never chain a second
        /// topple. Already-thrown lobs and the flyby are untouched. Idempotent.
        /// </summary>
        void ShatterOrbiters();
    }
}
