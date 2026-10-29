using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// The live orbiters, oldest first, on her root: where the planned Orbital
    /// Tether finds something to swing on (through <see cref="IOrbiterSource"/>,
    /// a serialized reference, never a static list) and where
    /// <see cref="ConicCaster"/> finds the oldest to retire at the live cap. Only
    /// the caster registers and unregisters; everyone else gets a read-only view.
    ///
    /// The list is built lazily, so a script reload (which wipes non-serialized
    /// fields) leaves an empty registry rather than a null one.
    /// </summary>
    public class OrbiterRegistry : MonoBehaviour, IOrbiterSource
    {
        List<ConicOrbiter> _live;
        ReadOnlyCollection<ConicOrbiter> _view;

        List<ConicOrbiter> Live => _live ??= new List<ConicOrbiter>(8);

        /// <inheritdoc/>
        public IReadOnlyList<IOrbitalAnchor> Orbiters => _view ??= Live.AsReadOnly();

        /// <summary>How many are registered.</summary>
        public int Count => Live.Count;

        /// <summary>The i-th orbiter, oldest first.</summary>
        public ConicOrbiter At(int index) => Live[index];

        /// <summary>The longest-lived orbiter, or null when empty.</summary>
        public ConicOrbiter Oldest => Live.Count > 0 ? Live[0] : null;

        /// <summary>Add as the newest. Ignores null and duplicates.</summary>
        public void Register(ConicOrbiter orbiter)
        {
            if (orbiter == null || Live.Contains(orbiter)) return;
            Live.Add(orbiter);
        }

        /// <summary>Remove; false if it was not registered.</summary>
        public bool Unregister(ConicOrbiter orbiter) => Live.Remove(orbiter);

        /// <summary>Drop entries whose GameObject was destroyed behind the caster's
        /// back (scene unload). No allocation.</summary>
        public void PruneDestroyed()
        {
            for (int i = Live.Count - 1; i >= 0; i--)
                if (Live[i] == null) Live.RemoveAt(i);
        }
    }
}
