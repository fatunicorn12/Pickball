using UnityEngine;

namespace Pickleball.Sandbox.Boss.Hypatia.Conics
{
    /// <summary>
    /// Everything presentation needs to draw the flyby's warning: two rays from
    /// the hyperbola's centre along its asymptotes (the shot comes in near one and
    /// leaves near the other, and never strays outside the wedge between them),
    /// the periapsis where it passes closest to her, and the full path. All points
    /// are in world space at the shot's flight height.
    /// </summary>
    public readonly struct HyperbolaTelegraph
    {
        public HyperbolaTelegraph(Vector3 centre, Vector3 incomingDirection, Vector3 outgoingDirection,
                                  Vector3 periapsis, Vector3 focus, Vector3 entry, Vector3 exit,
                                  float eccentricity, float leadSeconds, ConicPolyline path)
        {
            Centre = centre;
            IncomingDirection = incomingDirection;
            OutgoingDirection = outgoingDirection;
            Periapsis = periapsis;
            Focus = focus;
            Entry = entry;
            Exit = exit;
            Eccentricity = eccentricity;
            LeadSeconds = leadSeconds;
            Path = path;
        }

        /// <summary>Where the asymptotes cross. Both rays start here.</summary>
        public Vector3 Centre { get; }

        /// <summary>Unit ray from <see cref="Centre"/> toward where the shot comes
        /// FROM (it travels against this direction on the way in).</summary>
        public Vector3 IncomingDirection { get; }

        /// <summary>Unit ray from <see cref="Centre"/> along which the shot leaves.
        /// With Hyperbola Exit At Player on, this ray passes through the player's
        /// position at fire time.</summary>
        public Vector3 OutgoingDirection { get; }

        /// <summary>The closest point of the path to her.</summary>
        public Vector3 Periapsis { get; }

        /// <summary>Her position projected to the flight height: the focus.</summary>
        public Vector3 Focus { get; }

        /// <summary>First point of the path (Hyperbola Reach from her, incoming leg).</summary>
        public Vector3 Entry { get; }

        /// <summary>Last point of the path (Hyperbola Reach from her, outgoing leg).</summary>
        public Vector3 Exit { get; }

        public float Eccentricity { get; }

        /// <summary>Seconds on HER clock until the shot is released.</summary>
        public float LeadSeconds { get; }

        /// <summary>The exact polyline the shot will follow. Owned by the caster and
        /// valid until <see cref="ConicCaster.ShotFinished"/> or
        /// <see cref="ConicCaster.Stopped"/>; read it, don't keep it.</summary>
        public ConicPolyline Path { get; }
    }
}
