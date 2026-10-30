using System;
using UnityEngine;

namespace Pickleball.Sandbox.Hands
{
    /// <summary>
    /// A hand's grab point, and what the hand is holding.
    ///
    /// This transform sits on the hand bone at the centre of the fist, with the
    /// axes described on <see cref="GripFrame"/>. The weapon setup places it by
    /// measuring the rig (<see cref="GripFrameSolver"/>), not by a typed-in
    /// offset. Telling it to <see cref="Hold"/> an item parents the item here
    /// and lines the item's grip anchor up with this frame, so the item sits in
    /// the fist whatever its own pivot and scale. The sword is the first item;
    /// any prop with a <see cref="HeldItem"/> works the same way.
    ///
    /// It only parents and places the item. Closing the fingers is
    /// presentation and belongs to <see cref="FingerCurl"/>, which reads
    /// <see cref="IsHolding"/>; anything else can listen to
    /// <see cref="HoldChanged"/>.
    ///
    /// The held item is serialized, and OnEnable re-seats it, so a script
    /// reload or a scene load always puts it back exactly on the grab point.
    /// </summary>
    public class HandGrip : MonoBehaviour
    {
        [Tooltip("What this hand is holding; seated on this transform. Set by " +
                 "Hold(), or by the weapon setup. If empty on enable, a HeldItem " +
                 "already parented here is adopted.")]
        [SerializeField] private HeldItem held;

        [Tooltip("The profile this grab point was solved with. Its grip radius is " +
                 "the handle size the fist closes on here; an item with a fatter " +
                 "handle (HeldItem.gripRadius) is seated out of the palm by the " +
                 "difference. Empty assumes the default 0.015 m. Set by the setup.")]
        [SerializeField] private HandGripProfile profile;

        [Tooltip("Which hand this grab point is on. The palm is on opposite sides " +
                 "of the grab point's frame on the two hands, so a fat handle moves " +
                 "the other way on a left hand. Set by the setup.")]
        [SerializeField] private bool rightHand = true;

        /// <summary>The item in the hand, or null.</summary>
        public HeldItem Held => held;

        /// <summary>True while the hand holds something.</summary>
        public bool IsHolding => held != null;

        /// <summary>Raised after the hand takes or lets go of an item; carries
        /// the new item, or null on release.</summary>
        public event Action<HeldItem> HoldChanged;

        void OnEnable()
        {
            if (held == null)
            {
                // Explicit loop rather than GetComponentInChildren: only an item
                // parented straight onto the grab point counts as held. An
                // inactive one is a weapon stowed there by a swap, not in hand.
                for (int i = 0; i < transform.childCount; i++)
                {
                    var c = transform.GetChild(i);
                    if (c.gameObject.activeSelf && c.TryGetComponent(out HeldItem child))
                    {
                        held = child;
                        break;
                    }
                }
            }
            if (held != null) Seat(held);
        }

        /// <summary>
        /// Takes <paramref name="item"/> into the hand: parents it to the grab
        /// point and seats its grip anchor here. Whatever was held before is
        /// released first, unless it is the same item, which is just re-seated.
        /// </summary>
        public void Hold(HeldItem item)
        {
            if (item == null)
            {
                Release();
                return;
            }
            if (held != null && held != item) Release();

            // Only reparent when it changes. In the Editor an item inside a
            // prefab instance (the sword in every saved scene) can't be moved
            // to another parent: Unity logs an error and leaves it where it
            // is. Re-holding what is already here must not even try.
            if (item.transform.parent != transform) item.transform.SetParent(transform, false);
            Seat(item);
            bool changed = held != item;
            held = item;
            if (changed) HoldChanged?.Invoke(item);
        }

        /// <summary>
        /// Lets go: the item keeps its world pose and is moved under
        /// <paramref name="newParent"/> (the scene root when null).
        /// </summary>
        public void Release(Transform newParent = null)
        {
            if (held == null) return;
            var item = held;
            held = null;
            if (item.transform.parent == transform) item.transform.SetParent(newParent, true);
            HoldChanged?.Invoke(null);
        }

        /// <summary>Puts the held item back exactly on the grab point, e.g.
        /// after its grip anchor was moved.</summary>
        [ContextMenu("Re-seat Held Item")]
        public void Reseat()
        {
            if (held != null) Seat(held);
        }

        /// <summary>
        /// Moves a pair of blade markers onto the held item's own
        /// (<see cref="HeldItem.BladeBase"/> / <see cref="HeldItem.BladeTip"/>),
        /// so whatever already reads them (the hit sweep, the trail, weapon
        /// clearance) follows the blade now in the hand without being rewired.
        /// Call it after <see cref="Hold"/> when the markers live on the grab
        /// point, as on the chibi.
        /// </summary>
        /// <returns>False, and nothing moved, if the hand holds nothing or the
        /// item has no blade.</returns>
        public bool MatchBladeMarkers(Transform bladeBase, Transform bladeTip)
        {
            if (held == null || !held.HasBlade) return false;
            if (bladeBase != null) bladeBase.SetPositionAndRotation(held.BladeBase.position, held.BladeBase.rotation);
            if (bladeTip != null) bladeTip.SetPositionAndRotation(held.BladeTip.position, held.BladeTip.rotation);
            return true;
        }

        void Seat(HeldItem item)
        {
            if (item.transform.parent != transform) return;
            item.AnchorInItem(out Vector3 offset, out Quaternion rotation);
            // A handle fatter than the one this grab point was solved for sits
            // further out of the palm, so its surface, not its centre, is where
            // the solved handle's was.
            float solved = profile != null ? profile.GripRadius : HandGripProfile.DefaultGripRadius;
            Vector3 seat = GripSeat.HandleOffset(item.GripRadius, solved, rightHand);
            GripSeat.ItemPose(offset, rotation, seat, out Vector3 localPosition, out Quaternion localRotation);
            item.transform.SetLocalPositionAndRotation(localPosition, localRotation);
        }

        void OnDrawGizmosSelected()
        {
            // The grab point's axes: handle (green, +Y) and knuckles (blue, +Z).
            const float size = 0.06f;
            Vector3 p = transform.position;
            Gizmos.color = Color.green;
            Gizmos.DrawLine(p, p + transform.up * size * 2f);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(p, p + transform.forward * size);
            Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
            Gizmos.DrawWireSphere(p, size * 0.25f);
        }
    }
}
