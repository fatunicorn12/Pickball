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
    /// the finger's rest shape (lengths, splay) intact.
    ///
    /// The thumb bends about an axis taken from its own rest direction
    /// (<see cref="ThumbFlexAxis"/>), and its first joint can also swing
    /// across the palm (<see cref="ThumbSwingAxis"/>) so it closes over the
    /// fingers rather than beside them.
    ///
    /// Pure maths, so EditMode tests pin it down with plain transforms.
    /// </summary>
    public static class FingerCurlMath
    {
        /// <summary>
        /// The fingers' curl axis in the grab point's frame
        /// (<see cref="GripFrame"/>): the handle, +Y.
        /// </summary>
        public static Vector3 FingerAxisInGrip => Vector3.up;

        /// <summary>
        /// The sign that turns a finger toward the palm about
        /// <see cref="FingerAxisInGrip"/>.
        ///
        /// A positive turn about +Y takes the knuckle direction (+Z) toward +X.
        /// The palm is −X on a right hand and +X on a left one (see
        /// <see cref="GripFrame"/>), so fingers need −1 on the right and +1 on
        /// the left.
        /// </summary>
        public static float FingerSign(bool rightHand) => rightHand ? -1f : 1f;

        /// <summary>
        /// The axis a thumb bends about, in whatever space the arguments are
        /// in: perpendicular to the thumb's rest direction, in the plane of the
        /// palm, turned so a positive angle bends the thumb toward the palm.
        ///
        /// Built from the thumb rather than fixed, because rigs disagree on
        /// where the thumb points at rest. A Mixamo-style thumb points along the
        /// handle, and this is the knuckle axis. The Tripo chibi's points mostly
        /// along the fingers, and turning it about the knuckle axis only rolled
        /// it about its own length.
        /// </summary>
        /// <param name="thumbDirection">From the thumb's first joint toward its
        /// second, at rest.</param>
        /// <param name="palm">Out of the palm.</param>
        /// <returns>Zero if the thumb points straight out of the palm.</returns>
        public static Vector3 ThumbFlexAxis(Vector3 thumbDirection, Vector3 palm)
        {
            Vector3 flat = thumbDirection - Vector3.Dot(thumbDirection, palm) * palm;
            Vector3 axis = Vector3.Cross(flat, palm);
            if (axis.sqrMagnitude < 1e-10f) return Vector3.zero;
            axis.Normalize();
            // A positive turn about the axis carries a vector toward Cross(axis, vector).
            return Vector3.Dot(Vector3.Cross(axis, thumbDirection), palm) >= 0f ? axis : -axis;
        }

        /// <summary>
        /// The axis a thumb's first joint swings about to oppose the fingers:
        /// the palm's normal, turned so a positive angle carries the thumb
        /// across the palm toward the little finger's knuckle (along
        /// knuckle − thumb side), which is where a closed thumb ends up: over
        /// the curled fingers.
        /// </summary>
        /// <param name="thumbDirection">From the thumb's first joint toward its
        /// second, at rest.</param>
        /// <param name="palm">Out of the palm.</param>
        /// <param name="knuckle">The way the knuckles point.</param>
        /// <param name="thumbSide">Toward the thumb, across the hand.</param>
        public static Vector3 ThumbSwingAxis(Vector3 thumbDirection, Vector3 palm, Vector3 knuckle, Vector3 thumbSide)
        {
            Vector3 axis = palm.normalized;
            Vector3 across = knuckle - thumbSide;
            return Vector3.Dot(Vector3.Cross(axis, thumbDirection), across) >= 0f ? axis : -axis;
        }

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

        /// <summary>
        /// How much of the profile's curl a handle of another size needs.
        ///
        /// A finger wrapped round a handle follows a circle whose radius is
        /// the handle's plus half the finger's thickness, and a segment of
        /// fixed length turns through an angle inversely proportional to that
        /// radius. So a handle fatter than the one the curl was tuned on gets
        /// proportionally less bend at every joint, and the fingers lie on it
        /// instead of passing through it; a thinner one gets more.
        /// </summary>
        /// <param name="tunedRadius">Handle radius the curl angles were tuned
        /// for, m (<see cref="HandGripProfile.GripRadius"/>).</param>
        /// <param name="handleRadius">The held handle's radius, m; zero or less
        /// means the tuned size (scale 1).</param>
        /// <param name="fingerHalfThickness">Half a finger's thickness, m.</param>
        /// <returns>The factor for every bend angle, clamped to 0.5-1.5 so an
        /// odd measurement can't fold the fingers flat or through the palm.</returns>
        public static float CurlScale(float tunedRadius, float handleRadius, float fingerHalfThickness)
        {
            if (handleRadius <= 0f) return 1f;
            float t = Mathf.Max(0f, fingerHalfThickness);
            float tuned = Mathf.Max(0f, tunedRadius) + t;
            float held = handleRadius + t;
            if (held <= 1e-6f) return 1f;
            return Mathf.Clamp(tuned / held, 0.5f, 1.5f);
        }

        /// <summary>A joint's rest rotation turned by <paramref name="degrees"/>
        /// about its local curl axis.</summary>
        public static Quaternion Curled(Quaternion restLocal, Vector3 localAxis, float degrees)
            => restLocal * Quaternion.AngleAxis(degrees, localAxis);

        /// <summary>
        /// The thumb's first joint: bent about its flex axis and swung across
        /// the palm, both axes in the joint's rest frame. Bending about the rest
        /// axis and then swinging is the same as swinging first and then
        /// bending about the swung thumb's own axis, so the bend stays a bend
        /// whatever the swing.
        /// </summary>
        public static Quaternion Opposed(Quaternion restLocal, Vector3 localSwingAxis, float swingDegrees,
                                         Vector3 localFlexAxis, float flexDegrees)
            => restLocal * Quaternion.AngleAxis(swingDegrees, localSwingAxis)
                         * Quaternion.AngleAxis(flexDegrees, localFlexAxis);
    }
}
