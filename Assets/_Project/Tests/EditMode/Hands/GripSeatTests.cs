using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Hands;
using UnityEngine;

namespace Pickleball.Tests
{
    /// <summary>
    /// Pins down seating: an item held by a <see cref="HandGrip"/> ends up with
    /// its grip anchor exactly on the grab point, axes and all, whatever the
    /// item's own pivot, rotation and scale. And the handle measurement that
    /// places the compass's anchor at the middle of its handle.
    /// </summary>
    public class GripSeatTests
    {
        const float PositionTolerance = 1e-4f;
        const float AngleTolerance = 0.01f;

        readonly List<GameObject> _spawned = new();

        GameObject Spawn(string name)
        {
            var go = new GameObject(name);
            _spawned.Add(go);
            return go;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _spawned)
                if (go != null) Object.DestroyImmediate(go);
            _spawned.Clear();
        }

        [Test]
        public void ItemPose_PutsTheAnchorOnTheOrigin()
        {
            var anchorOffset = new Vector3(0.1f, -0.3f, 0.05f);
            var anchorRotation = Quaternion.Euler(30f, -70f, 110f);

            GripSeat.ItemPose(anchorOffset, anchorRotation, out var position, out var rotation);

            Assert.Less((position + rotation * anchorOffset).magnitude, PositionTolerance);
            Assert.Less(Quaternion.Angle(Quaternion.identity, rotation * anchorRotation), AngleTolerance);
        }

        /// <summary>A hand bone rotated somewhere in the world, a grab point on
        /// it, and a scaled, rotated item whose anchor is nested two deep.</summary>
        (HandGrip grip, HeldItem item, Transform anchor) Rig()
        {
            var hand = Spawn("Hand").transform;
            hand.SetPositionAndRotation(new Vector3(1f, 2f, -3f), Quaternion.Euler(10f, 50f, -20f));

            var grab = Spawn("GrabPoint").transform;
            grab.SetParent(hand, false);
            grab.SetLocalPositionAndRotation(new Vector3(0f, 0.06f, 0.03f), Quaternion.Euler(-5f, 80f, 95f));
            var grip = grab.gameObject.AddComponent<HandGrip>();

            var itemGo = Spawn("Compass");
            itemGo.transform.SetPositionAndRotation(new Vector3(5f, 0f, 5f), Quaternion.Euler(0f, 0f, 180f));
            itemGo.transform.localScale = Vector3.one * 0.852f;
            var middle = Spawn("Middle").transform;
            middle.SetParent(itemGo.transform, false);
            middle.SetLocalPositionAndRotation(new Vector3(0f, 0.5f, 0f), Quaternion.Euler(0f, 0f, 15f));
            var anchor = Spawn("GripAnchor").transform;
            anchor.SetParent(middle, false);
            anchor.SetLocalPositionAndRotation(new Vector3(0.01f, 0.43f, 0.02f), Quaternion.Euler(0f, 0f, 165f));

            var item = itemGo.AddComponent<HeldItem>();
            item.SetGripAnchor(anchor);
            return (grip, item, anchor);
        }

        [Test]
        public void Hold_SeatsTheAnchorOnTheGrabPoint()
        {
            var (grip, item, anchor) = Rig();

            grip.Hold(item);

            Assert.AreSame(grip.transform, item.transform.parent);
            Assert.Less((anchor.position - grip.transform.position).magnitude, PositionTolerance);
            Assert.Less(Quaternion.Angle(anchor.rotation, grip.transform.rotation), AngleTolerance);
            // Seating moves and turns the item; it never rescales it.
            Assert.AreEqual(0.852f, item.transform.localScale.x, 1e-6f);
        }

        [Test]
        public void Hold_WithNoAnchor_SeatsTheItemsOwnOrigin()
        {
            var (grip, item, _) = Rig();
            item.SetGripAnchor(null);

            grip.Hold(item);

            Assert.Less(item.transform.localPosition.magnitude, PositionTolerance);
            Assert.Less(Quaternion.Angle(Quaternion.identity, item.transform.localRotation), AngleTolerance);
        }

        [Test]
        public void Hold_RaisesHoldChangedOnce_AndReholdingOnlyReseats()
        {
            var (grip, item, anchor) = Rig();
            int raised = 0;
            grip.HoldChanged += _ => raised++;

            grip.Hold(item);
            item.transform.localPosition += Vector3.one;   // knocked out of place
            grip.Hold(item);

            Assert.AreEqual(1, raised);
            Assert.IsTrue(grip.IsHolding);
            Assert.Less((anchor.position - grip.transform.position).magnitude, PositionTolerance);
        }

        [Test]
        public void Release_KeepsTheWorldPose_AndEmptiesTheHand()
        {
            var (grip, item, _) = Rig();
            grip.Hold(item);
            Vector3 before = item.transform.position;
            HeldItem reported = item;
            grip.HoldChanged += h => reported = h;

            grip.Release();

            Assert.IsFalse(grip.IsHolding);
            Assert.IsNull(reported);
            Assert.IsNull(item.transform.parent);
            Assert.Less((item.transform.position - before).magnitude, PositionTolerance);
        }

        // ----------------------------------------------------- handle length

        [Test]
        public void HandleLength_EndsAtTheFirstSharpWidening()
        {
            // The compass from its held end, 2 cm slices: knob, narrower neck,
            // two slices with no vertices, then the hinge disc.
            var radii = new List<float> { 0.034f, 0.034f, 0.03f, 0.02f, -1f, -1f, 0.065f, 0.06f };
            Assert.AreEqual(0.12f, GripSeat.HandleLength(radii, 0.02f, 1.5f), 1e-6f);
        }

        [Test]
        public void HandleLength_GentleFlareIsStillHandle()
        {
            var radii = new List<float> { 0.02f, 0.025f, 0.03f, 0.036f, 0.043f, 0.05f };
            Assert.AreEqual(0f, GripSeat.HandleLength(radii, 0.02f, 1.5f));
        }

        [Test]
        public void HandleLength_SkipsEmptySlicesAtTheEnd()
        {
            var radii = new List<float> { -1f, 0.02f, 0.02f, 0.05f };
            Assert.AreEqual(0.06f, GripSeat.HandleLength(radii, 0.02f, 1.5f), 1e-6f);
        }

        [Test]
        public void HandleLength_NoGeometry_IsZero()
        {
            Assert.AreEqual(0f, GripSeat.HandleLength(new List<float> { -1f, -1f }, 0.02f, 1.5f));
            Assert.AreEqual(0f, GripSeat.HandleLength(null, 0.02f, 1.5f));
        }
    }
}
