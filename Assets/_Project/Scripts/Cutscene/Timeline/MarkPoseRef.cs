using System;
using UnityEngine;

namespace Pickleball.Cutscene.Timeline
{
    /// <summary>
    /// A blocking pose as stored in a clip: a frame transform (a set marker, an actor root or an
    /// actor anchor, as an exposed reference so the timeline asset never holds a scene object), an
    /// offset in metres in the frame's yawed axes, and either a face target or an extra yaw. Read
    /// live each frame, so a replacement set with the same marker ids moves the blocking with it.
    /// </summary>
    [Serializable]
    public struct MarkPoseRef
    {
        /// <summary>The frame transform.</summary>
        [Tooltip("The frame: a CutsceneMarker, an actor root or an actor anchor.")]
        public ExposedReference<Transform> frame;

        /// <summary>Offset in metres in the frame's yawed axes.</summary>
        [Tooltip("Offset from the frame in metres, in the frame's yawed axes (x right, y up, z forward).")]
        public Vector3 offset;

        /// <summary>Face target, or unset.</summary>
        [Tooltip("Optional: face this transform (yaw only). Empty uses the frame's yaw plus Yaw.")]
        public ExposedReference<Transform> face;

        /// <summary>Extra yaw, degrees.</summary>
        [Tooltip("Extra yaw on top of the frame's, in degrees. Ignored when Face is set.")]
        public float yaw;

        /// <summary>Tilt about the pose's own right axis after the yaw, degrees (+ tips its up toward its forward).</summary>
        [Tooltip("Tilt about the pose's right axis after the yaw, degrees: + tips the top toward the forward (a prop leaning at the camera).")]
        public float pitch;
    }
}
