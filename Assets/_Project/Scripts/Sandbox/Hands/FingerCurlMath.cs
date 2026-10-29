using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Hands
{
    /// <summary>
    /// The geometry behind <see cref="FingerCurl"/>: which axis each finger
    /// joint turns about, and which way, so a whole finger wraps around the
    /// held handle.
    ///
    /// Every segment of a finger turns about the same physical axis, parallel
    /// to the handle. Expressing that axis in each joint's own rest frame and
    /// turning each joint by its own angle makes the angles add up along the
    /// chain: the fingertip ends up turned by the sum, about the handle, with
    /// the finger's rest shape (lengths, splay) intact. The thumb wraps over
    /// the fingers instead, about the grab point's knuckle axis.
    ///
    /// Pure maths, so EditMode tests pin it down with plain transforms.
    /// </summary>
    public static class FingerCurlMath
    {
        /// <summary>
        /// The curl axis in the grab point's frame (<see cref="GripFrame"/>):
        /// fingers curl about the handle (+Y); the thumb, which points along the
        /// handle at rest, curls about the knuckle axis (+Z).
        /// </summary>
        public static Vector3 AxisInGrip(bool thumb) => thumb ? Vector3.forward : Vector3.up;

        /// <summary>
        /// The sign that turns a finger toward the palm about
        /// <see cref="AxisInGrip"/>.
        ///
        /// A positive turn about +Y takes the knuckle direction (+Z) toward +X,
        /// and a positive turn about +Z takes the thumb direction (+Y) toward −X.
        /// The palm is −X on a right hand and +X on a left one (see
        /// <see cref="GripFrame"/>), so fingers need −1 on the right and +1 on
        /// the left, and the thumb the opposite.
        /// </summary>
        public static float Sign(bool rightHand, bool thumb) => rightHand == thumb ? 1f : -1f;

        /// <summary>
        /// The curl axis expressed in one joint's rest frame.
        /// </summary>
        /// <param name="restInHand">The joint's rest rotation relative to the
        /// hand bone: the product of the rest local rotations from the hand's
        /// child down to and including this joint.</param>
        /// <param name="axisInHand">The curl axis in the hand bone's space.</param>
        public static Vector3 AxisInJoint(Quaternion restInHand, Vector3 axisInHand)
            => Quaternion.Inverse(restInHand) * axisInHand;

        /// <summary>
        /// A joint's rest rotation relative to the hand bone, from the rest
        /// local rotations along the path, hand's child first.
        /// </summary>
        public static Quaternion RestInHand(IReadOnlyList<Quaternion> restLocalPath)
        {
            Quaternion q = Quaternion.identity;
            for (int i = 0; i < restLocalPath.Count; i++) q *= restLocalPath[i];
            return q;
        }

        /// <summary>A joint's rest rotation turned by <paramref name="degrees"/>
        /// about its local curl axis.</summary>
        public static Quaternion Curled(Quaternion restLocal, Vector3 localAxis, float degrees)
            => restLocal * Quaternion.AngleAxis(degrees, localAxis);
    }
}
