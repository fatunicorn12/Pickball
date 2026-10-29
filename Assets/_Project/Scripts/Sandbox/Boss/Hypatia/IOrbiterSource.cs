using System.Collections.Generic;

namespace Pickleball.Sandbox.Boss.Hypatia
{
    /// <summary>
    /// Where a future Orbital Tether finds the live orbiters, through a serialized
    /// reference rather than a static list (Newton's <c>GravityWell.Active</c> is
    /// the exception CodingStandards §1 records; this fight does not repeat it).
    /// </summary>
    public interface IOrbiterSource
    {
        /// <summary>Live orbiters, oldest first. The list is owned by the source;
        /// do not keep it across frames or cast it to modify it.</summary>
        IReadOnlyList<IOrbitalAnchor> Orbiters { get; }
    }
}
