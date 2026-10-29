using UnityEngine;

namespace Pickleball.Sandbox.Hands
{
    /// <summary>
    /// A hand's grab point: a pose in the hand bone's local space.
    ///
    /// Axis convention, shared by every grab point and every
    /// <see cref="HeldItem"/> grip anchor, so any item seats in any hand by
    /// lining the two frames up:
    ///  - origin: the centre of the fist, where the middle of a held handle sits;
    ///  - +Y: along the handle, out of the thumb side of the fist. A sword's
    ///    blade points this way;
    ///  - +Z: the way the knuckles point. A blade's leading edge faces this way,
    ///    so its flat faces the palm;
    ///  - +X: completes the frame (Y × Z). That is out of the back of a right
    ///    hand and out of the palm of a left hand, because a left hand is a
    ///    mirror image and a rotation can't mirror.
    /// </summary>
    public readonly struct GripFrame
    {
        /// <summary>Centre of the fist, in the hand bone's local space (m).</summary>
        public readonly Vector3 Position;

        /// <summary>Orientation in the hand bone's local space; see the class
        /// summary for what each axis means.</summary>
        public readonly Quaternion Rotation;

        /// <summary>Use <see cref="GripFrameSolver.Compose"/>, which builds the
        /// rotation from the hand's axes; this stores the pose as given.</summary>
        public GripFrame(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        /// <summary>The handle's axis (+Y): out of the thumb side.</summary>
        public Vector3 HandleAxis => Rotation * Vector3.up;

        /// <summary>The knuckles' direction (+Z).</summary>
        public Vector3 KnuckleAxis => Rotation * Vector3.forward;
    }
}
