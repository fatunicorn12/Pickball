using System;
using UnityEngine;

namespace Pickleball.Cutscene.Cameras
{
    /// <summary>
    /// An aim key: the Rotation Composer's target and its screen position at a time. Between keys
    /// the target point and the screen position are interpolated with the later key's ease, which
    /// is how a shot hands its aim from MC to Birdy (S2_SH01) or from MC to the ledge (S2_SH10).
    /// </summary>
    [Serializable]
    public struct AimKey
    {
        [Tooltip("When this aim is reached.")]
        public TimeRef time;

        [Tooltip("The point the composer aims at.")]
        public RigPoint target;

        [Tooltip("Where the target sits on screen, 0..1 from the bottom-left ((0.5, 0.5) is centre), " +
                 "as the shot list writes it.")]
        public Vector2 screen;

        [Tooltip("Ease of the change that arrives at this key.")]
        public EaseKind ease;
    }
}
