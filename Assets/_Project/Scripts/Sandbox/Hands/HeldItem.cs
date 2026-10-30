using UnityEngine;

namespace Pickleball.Sandbox.Hands
{
    /// <summary>
    /// Marks an object a hand can hold and says where the hand closes on it.
    ///
    /// The grip anchor uses the same convention as a hand's grab point
    /// (<see cref="GripFrame"/>): origin at the middle of the handle, +Y along
    /// the handle toward the business end (a blade), +Z toward the edge that
    /// should lead with the knuckles. <see cref="HandGrip"/> seats the item by
    /// lining the two up, so the item never needs to know which hand or rig is
    /// holding it.
    ///
    /// The anchor is a child transform rather than numbers on this component so
    /// it can be seen and nudged in the Scene view, and so it follows the item's
    /// own scale.
    /// </summary>
    public class HeldItem : MonoBehaviour
    {
        [Tooltip("Where the hand closes: origin at the middle of the handle, +Y " +
                 "along the handle toward the blade end, +Z toward the leading " +
                 "edge. Empty uses this object's own origin and axes. Set by the " +
                 "weapon setup, which measures the handle from the mesh.")]
        [SerializeField] private Transform gripAnchor;

        [Tooltip("Where the cutting part starts, near the hand. Optional: only " +
                 "weapons need it. Whoever equips the item moves the hand's " +
                 "BladeBase marker here, so hit sweeps, the trail and weapon " +
                 "clearance follow whichever weapon is held.")]
        [SerializeField] private Transform bladeBase;

        [Tooltip("The far end of the cutting part. Optional, paired with Blade Base.")]
        [SerializeField] private Transform bladeTip;

        /// <summary>The grip anchor, or this transform when none is set.</summary>
        public Transform GripAnchor => gripAnchor != null ? gripAnchor : transform;

        /// <summary>The item's own blade-start marker, or null for a non-weapon.</summary>
        public Transform BladeBase => bladeBase;

        /// <summary>The item's own blade-end marker, or null for a non-weapon.</summary>
        public Transform BladeTip => bladeTip;

        /// <summary>True when both blade markers are set, so the item can be swung.</summary>
        public bool HasBlade => bladeBase != null && bladeTip != null;

        /// <summary>Points the item at a new grip anchor. For setup scripts.</summary>
        public void SetGripAnchor(Transform anchor) => gripAnchor = anchor;

        /// <summary>Points the item at its blade markers. For setup scripts.</summary>
        public void SetBladeMarkers(Transform baseMarker, Transform tipMarker)
        {
            bladeBase = baseMarker;
            bladeTip = tipMarker;
        }

        /// <summary>
        /// The anchor's pose relative to this item's root, in the form
        /// <see cref="GripSeat.ItemPose"/> takes: the offset in the root's local
        /// axes but scaled by the root's own scale, and the relative rotation.
        /// Assumes a uniform scale on the root, as every prop here has.
        /// </summary>
        public void AnchorInItem(out Vector3 offset, out Quaternion rotation)
        {
            Transform anchor = GripAnchor;
            if (anchor == transform)
            {
                offset = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }
            offset = Vector3.Scale(transform.localScale, transform.InverseTransformPoint(anchor.position));
            rotation = Quaternion.Inverse(transform.rotation) * anchor.rotation;
        }
    }
}
