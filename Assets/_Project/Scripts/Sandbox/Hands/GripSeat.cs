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
