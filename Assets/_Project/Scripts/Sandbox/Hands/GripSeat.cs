using System.Collections.Generic;
using UnityEngine;

namespace Pickleball.Sandbox.Hands
{
    /// <summary>
    /// How a held item lines up with a grab point, and where on an item the
    /// hand should close.
    ///
    /// Seating is anchor-to-anchor: the item carries a grip anchor in the same
    /// convention as the hand's grab point (<see cref="GripFrame"/>), and the
    /// item is placed so the two frames coincide. Neither side needs to know
    /// anything else about the other, so any item fits any hand.
    ///
    /// Pure maths, so EditMode tests can pin it down without a scene.
    /// </summary>
    public static class GripSeat
    {
        /// <summary>
        /// The item's local pose under the grab point that puts its grip anchor
        /// exactly on the grab point, axes and all.
        /// </summary>
        /// <param name="anchorOffset">The anchor's position relative to the item
        /// root, in the item's local axes but scaled by the item's own scale
        /// (that is, in the grab point's units).</param>
        /// <param name="anchorRotation">The anchor's rotation relative to the
        /// item root.</param>
        public static void ItemPose(Vector3 anchorOffset, Quaternion anchorRotation,
                                    out Vector3 localPosition, out Quaternion localRotation)
        {
            localRotation = Quaternion.Inverse(anchorRotation);
            localPosition = -(localRotation * anchorOffset);
        }

        /// <summary>
        /// As <see cref="ItemPose(Vector3, Quaternion, out Vector3, out Quaternion)"/>,
        /// but the anchor lands on <paramref name="seatOffset"/> (in the grab
        /// point's frame) instead of on the grab point itself. Used to sit a
        /// handle fatter or thinner than the one the grab point was solved for
        /// (see <see cref="HandleOffset"/>).
        /// </summary>
        public static void ItemPose(Vector3 anchorOffset, Quaternion anchorRotation, Vector3 seatOffset,
                                    out Vector3 localPosition, out Quaternion localRotation)
        {
            localRotation = Quaternion.Inverse(anchorRotation);
            localPosition = seatOffset - localRotation * anchorOffset;
        }

        /// <summary>
        /// Where the centre of a handle of <paramref name="handleRadius"/> sits,
        /// in the grab point's frame (<see cref="GripFrame"/>), when the grab
        /// point was solved for a handle of <paramref name="solvedRadius"/>.
        ///
        /// The closed fist holds a handle against the palm on one side and the
        /// curled first finger segments on the other, one handle radius past
        /// each (<see cref="GripFrameSolver.FistCentre"/>). A fatter handle
        /// therefore moves its centre out of the palm (−X on a right hand, +X on
        /// a left one) and back from the knuckles (−Z) by the difference in
        /// radius, and its surface stays where the solved one was: against the
        /// palm, inside the fingers. Without this a 2 cm handle in a fist solved
        /// for 1.5 cm sits 5 mm into the palm and the fingers.
        /// </summary>
        /// <param name="handleRadius">The held item's handle radius, m; zero or
        /// less means "the size the grab point was solved for" (no offset).</param>
        /// <param name="solvedRadius">The radius the grab point was solved for,
        /// m (<see cref="HandGripProfile.GripRadius"/>).</param>
        /// <param name="rightHand">Which hand: the palm is on opposite sides of
        /// the frame (see <see cref="GripFrame"/>).</param>
        public static Vector3 HandleOffset(float handleRadius, float solvedRadius, bool rightHand)
        {
            if (handleRadius <= 0f) return Vector3.zero;
            float delta = handleRadius - Mathf.Max(0f, solvedRadius);
            Vector3 palm = rightHand ? Vector3.left : Vector3.right;
            return delta * (palm - Vector3.forward);
        }

        /// <summary>
        /// Furthest distance of the item's geometry from a line along the item,
        /// in slices starting at the held end. The input to
        /// <see cref="HandleLength"/> and <see cref="MeanRadius"/>.
        /// </summary>
        /// <param name="points">The item's vertices, in any one space.</param>
        /// <param name="heldEnd">A point on the plane of the held end.</param>
        /// <param name="along">Unit direction from the held end toward the far end.</param>
        /// <param name="axisPoint">A point on the line radii are measured from:
        /// the bounds centre, or the handle's own axis (<see cref="HandleAxisPoint"/>).</param>
        /// <param name="sliceLength">Length of one slice, in the points' units.</param>
        /// <param name="count">Number of slices; points past the last are ignored.</param>
        /// <returns>One radius per slice; −1 for a slice with no points.</returns>
        public static float[] SliceRadii(IReadOnlyList<Vector3> points, Vector3 heldEnd, Vector3 along,
                                         Vector3 axisPoint, float sliceLength, int count)
        {
            var radii = new float[Mathf.Max(0, count)];
            for (int i = 0; i < radii.Length; i++) radii[i] = -1f;
            if (points == null || sliceLength <= 0f) return radii;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 p = points[i];
                int bin = Mathf.FloorToInt(Vector3.Dot(p - heldEnd, along) / sliceLength);
                if (bin < 0 || bin >= radii.Length) continue;
                Vector3 off = p - axisPoint;
                float r = (off - Vector3.Dot(off, along) * along).magnitude;
                if (r > radii[bin]) radii[bin] = r;
            }
            return radii;
        }

        /// <summary>
        /// A point on the handle's own axis, level with the held end: the middle
        /// of the handle's extent across both girth directions.
        ///
        /// Not the bounds centre. A sword's blade or guard decides its bounds,
        /// and a Tripo model is rarely symmetric: the ruler sword's handle runs
        /// 5 mm off the centre of its bounds, toward the ruler's tick edge. The
        /// fist must close round the handle, not round the bounds.
        /// </summary>
        /// <param name="girthU">A unit direction across the item.</param>
        /// <param name="girthV">The other unit direction across it.</param>
        /// <param name="handleLength">How far from the held end the handle runs.</param>
        /// <param name="fallback">Returned when no point lies on the handle.</param>
        public static Vector3 HandleAxisPoint(IReadOnlyList<Vector3> points, Vector3 heldEnd, Vector3 along,
                                              Vector3 girthU, Vector3 girthV, float handleLength,
                                              Vector3 fallback)
        {
            if (points == null || handleLength <= 0f) return fallback;
            float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
            for (int i = 0; i < points.Count; i++)
            {
                Vector3 d = points[i] - heldEnd;
                float t = Vector3.Dot(d, along);
                if (t < 0f || t > handleLength) continue;
                float u = Vector3.Dot(d, girthU), v = Vector3.Dot(d, girthV);
                minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
                minV = Mathf.Min(minV, v); maxV = Mathf.Max(maxV, v);
            }
            if (minU > maxU) return fallback;
            return heldEnd + girthU * ((minU + maxU) * 0.5f) + girthV * ((minV + maxV) * 0.5f);
        }

        /// <summary>
        /// Mean slice radius between two distances from the held end: the
        /// handle's radius where the fist closes. Empty slices are skipped.
        /// </summary>
        /// <returns>Zero when no slice in the range has geometry.</returns>
        public static float MeanRadius(IReadOnlyList<float> sliceRadii, float sliceLength, float from, float to)
        {
            if (sliceRadii == null || sliceLength <= 0f) return 0f;
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < sliceRadii.Count; i++)
            {
                float centre = (i + 0.5f) * sliceLength;
                if (centre < from || centre > to || sliceRadii[i] < 0f) continue;
                sum += sliceRadii[i];
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>
        /// Where along the handle the fist closes, from the held end.
        ///
        /// By default the middle of the handle. A long handle (a two-hand-length
        /// grip, or one that counts its pommel as handle) would then leave a gap
        /// between the fist and the guard, so <paramref name="belowGuard"/> can
        /// put the fist's centre that far below the end of the handle instead,
        /// but never past the middle, so a short handle still centres.
        /// </summary>
        /// <param name="handleLength">From <see cref="HandleLength"/>.</param>
        /// <param name="belowGuard">Distance from the guard (the handle's end)
        /// to the centre of the fist, same units; zero or less for the middle.</param>
        public static float GripPosition(float handleLength, float belowGuard)
        {
            float middle = handleLength * 0.5f;
            if (belowGuard <= 0f) return middle;
            return Mathf.Max(middle, handleLength - belowGuard);
        }

        /// <summary>
        /// Length of an item's handle, measured from the held end: how far the
        /// item stays about as narrow as its end before it suddenly widens (a
        /// sword's crossguard, the compass's hinge disc).
        ///
        /// Each slice is compared with the widest slice of handle seen so far,
        /// not with the first one, so a handle that flares gently along its
        /// length doesn't count as ending. Slices with no geometry (a long, thin
        /// part of a low-poly mesh has vertices only at its ends) are skipped.
        /// </summary>
        /// <param name="sliceRadii">Furthest distance from the item's long axis
        /// in each slice, starting at the held end; negative for a slice with no
        /// geometry. Pass only the part of the item a handle could occupy.</param>
        /// <param name="sliceLength">Length of one slice, in the same units.</param>
        /// <param name="widenRatio">How much wider than the handle a slice must
        /// be to end it, e.g. 1.5.</param>
        /// <returns>Distance from the held end to the first wide slice; zero if
        /// the item never widens (no distinct handle).</returns>
        public static float HandleLength(IReadOnlyList<float> sliceRadii, float sliceLength, float widenRatio)
        {
            if (sliceRadii == null) return 0f;
            float handleRadius = -1f;
            for (int i = 0; i < sliceRadii.Count; i++)
            {
                float r = sliceRadii[i];
                if (r < 0f) continue;
                if (handleRadius > 0f && r > handleRadius * widenRatio) return i * sliceLength;
                if (r > handleRadius) handleRadius = r;
            }
            return 0f;
        }
    }
}
