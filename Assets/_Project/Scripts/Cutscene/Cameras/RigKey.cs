using System;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// A camera position key: where the camera is at a time, as a point expression. Between keys
    /// the camera moves with the later key's ease; each key is re-resolved every frame from the
    /// live anchors, so a dolly "from 12·HB to 8·HB" stays 12→8 HB however Birdy moves.
    /// </summary>
    [Serializable]
    public struct RigKey
    {
        [Tooltip("When the camera reaches this point.")]
        public TimeRef time;

        [Tooltip("The point.")]
        public RigPoint point;

        [Tooltip("Ease of the move that arrives at this key.")]
        public EaseKind ease;
    }
}
