using System;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// A depth-of-field focus key: the point that is sharp at a time. The focus distance is the
    /// camera's distance to that point; between keys it eases in dioptres (1/distance), the way a
    /// focus puller's ring moves, so a rack from 1 m to 120 m doesn't spend the whole pull in the
    /// far field.
    /// </summary>
    [Serializable]
    public struct FocusKey
    {
        [Tooltip("When this point is in focus.")]
        public TimeRef time;

        [Tooltip("The point in focus.")]
        public RigPoint target;

        [Tooltip("Ease of the pull that arrives at this key.")]
        public EaseKind ease;
    }
}
