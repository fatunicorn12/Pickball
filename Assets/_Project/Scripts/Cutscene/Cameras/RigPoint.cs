using System;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// One shot-list point expression, as data: <c>basis + offset</c> where the offset is written
    /// in a <see cref="RigFrame"/> and split by unit (×H, ×HB, metres) so <c>MC.Feet + 0.45·H fwd
    /// + 2.33·HB back</c> is one point. H and HB are read from the actors when the point is
    /// resolved, so a model of another size moves every camera with it.
    /// </summary>
    [Serializable]
    public struct RigPoint
    {
        [Tooltip("The point the offset starts from.")]
        public RigBasis basis;

        [Tooltip("Marker id when the basis is Marker.")]
        public string marker;

        [Tooltip("The axes the offset is written in (yaw only).")]
        public RigFrame frame;

        [Tooltip("Marker id for the Marker and Toward frames.")]
        public string frameMarker;

        [Tooltip("Which side of the MC→Birdy line Pair.Side is, for the PairSide frame.")]
        public SideRule side;

        [Tooltip("Degrees the frame is turned about the vertical (positive = clockwise seen from " +
                 "above). Writes \"30° off his forward\" and the off-axis moves that keep a camera " +
                 "out of the other actor.")]
        public float yaw;

        [Tooltip("Offset in units of H (MC's measured height): x right, y up, z forward.")]
        public Vector3 h;

        [Tooltip("Offset in units of HB (Birdy's measured head-box height).")]
        public Vector3 hb;

        [Tooltip("Offset in metres. Only for set-scale distances that don't belong to a character.")]
        public Vector3 m;

        [Tooltip("When set, the result's height is this point's height plus the offsets' vertical " +
                 "parts (\"at Birdy's eye level\", \"at MC.Eyes height\"). None keeps the basis height.")]
        public RigBasis level;

        [Tooltip("Marker id when the level reference is Marker.")]
        public string levelMarker;

        /// <summary>A point at an anchor with no offset.</summary>
        public static RigPoint At(RigBasis basis) => new RigPoint { basis = basis };

        /// <summary>A point at a marker with no offset.</summary>
        public static RigPoint AtMarker(string id) => new RigPoint { basis = RigBasis.Marker, marker = id };
    }
}
