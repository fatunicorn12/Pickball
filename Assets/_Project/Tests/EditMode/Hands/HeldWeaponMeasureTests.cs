using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Hands;
using UnityEngine;

namespace Pickleball.Tests
{
    /// <summary>
    /// Pins down the measurements the held-weapon builder makes on a sword: the
    /// handle's length, its own axis and its radius where the fist closes, on a
    /// synthetic point cloud shaped like the ruler sword (pommel, grip,
    /// ferrule, guard disc, wide blade, handle off the bounds centre); and the
    /// seating and finger-curl maths that let a fatter handle sit in a fist
    /// solved for a thinner one.
    /// </summary>
    public class HeldWeaponMeasureTests
    {
        const float Slice = 0.02f;   // 2% of a 1 m sword
        const int Count = 20;        // the first 40%
        const float WidenRatio = 1.5f;

        // The ruler sword's handle as built (model units ~ metres): pommel 0-0.03
        // r 0.020, grip 0.03-0.17 r 0.017, ferrule 0.17-0.19 r 0.022, guard disc
        // 0.19-0.21 r 0.11, blade 0.21-1.0, 0.2 wide and 0.07 thick, centred on the
        // bounds. The handle runs 5 mm off that centre along X.
        static readonly Vector3 HandleAxis = new(0.005f, 0f, 0f);

        static List<Vector3> RulerPoints()
        {
            var points = new List<Vector3>();
            void Ring(float z, float r, Vector3 centre, int n = 12)
            {
                for (int i = 0; i < n; i++)
                {
                    float a = i * Mathf.PI * 2f / n;
                    points.Add(centre + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z));
                }
            }
            for (float z = 0.005f; z < 0.03f; z += 0.01f) Ring(z, 0.020f, HandleAxis);
            for (float z = 0.035f; z < 0.17f; z += 0.01f) Ring(z, 0.017f, HandleAxis);
            for (float z = 0.175f; z < 0.19f; z += 0.01f) Ring(z, 0.022f, HandleAxis);
            for (float z = 0.195f; z < 0.21f; z += 0.01f) Ring(z, 0.11f, HandleAxis, 24);
            for (float z = 0.215f; z <= 1f; z += 0.02f)
                foreach (var x in new[] { -0.1f, 0.1f })
                foreach (var y in new[] { -0.035f, 0.035f })
                    points.Add(new Vector3(x, y, z));
            return points;
        }

        // The bounds centre line at the held end, as the builder uses it.
        static readonly Vector3 HeldEnd = Vector3.zero;
        static readonly Vector3 Along = Vector3.forward;

        [Test]
        public void HandleLength_OnARulerSword_EndsAtTheGuardDisc()
        {
            var radii = GripSeat.SliceRadii(RulerPoints(), HeldEnd, Along, HeldEnd, Slice, Count);

            float handle = GripSeat.HandleLength(radii, Slice, WidenRatio);

            // The ferrule (1.3x the pommel) is still handle; the disc ends it.
            Assert.AreEqual(0.18f, handle, Slice + 1e-4f);
        }

        [Test]
        public void SliceRadii_SkipsEmptySlices_AndIgnoresPointsPastTheLast()
        {
            var points = new List<Vector3> { new(0.01f, 0f, 0.005f), new(0f, 0.03f, 0.065f), new(5f, 0f, 0.9f) };

            var radii = GripSeat.SliceRadii(points, HeldEnd, Along, HeldEnd, Slice, 4);

            Assert.AreEqual(4, radii.Length);
            Assert.AreEqual(0.01f, radii[0], 1e-6f);
            Assert.AreEqual(-1f, radii[1]);
            Assert.AreEqual(-1f, radii[2]);
            Assert.AreEqual(0.03f, radii[3], 1e-6f);
        }

        [Test]
        public void HandleAxisPoint_FindsTheHandle_NotTheBoundsCentre()
        {
            var points = RulerPoints();

            var axis = GripSeat.HandleAxisPoint(points, HeldEnd, Along, Vector3.right, Vector3.up, 0.18f, HeldEnd);

            Assert.AreEqual(HandleAxis.x, axis.x, 1e-4f);
            Assert.AreEqual(0f, axis.y, 1e-4f);
            Assert.AreEqual(0f, axis.z, 1e-6f, "stays level with the held end");
        }

        [Test]
        public void HandleAxisPoint_WithNoHandle_ReturnsTheFallback()
        {
            var fallback = new Vector3(1f, 2f, 3f);

            Assert.AreEqual(fallback, GripSeat.HandleAxisPoint(RulerPoints(), HeldEnd, Along, Vector3.right,
                                                               Vector3.up, 0f, fallback));
        }

        [Test]
        public void MeanRadius_AroundTheHandlesAxis_IsTheGripRadius()
        {
            var radii = GripSeat.SliceRadii(RulerPoints(), HeldEnd, Along, HandleAxis, Slice, Count);

            // The fist's span on the grip proper, clear of pommel and ferrule.
            float r = GripSeat.MeanRadius(radii, Slice, 0.06f, 0.15f);

            Assert.AreEqual(0.017f, r, 5e-4f);
        }

        [Test]
        public void MeanRadius_OfAnEmptyRange_IsZero()
        {
            Assert.AreEqual(0f, GripSeat.MeanRadius(new[] { -1f, -1f, 0.02f }, Slice, 0f, 0.039f));
        }

        [TestCase(0.18f, 0f, 0.09f, TestName = "GripPosition_NoInset_IsTheMiddle")]
        [TestCase(0.18f, 0.045f, 0.135f, TestName = "GripPosition_Inset_SitsBelowTheGuard")]
        [TestCase(0.136f, 0.1f, 0.068f, TestName = "GripPosition_ShortHandle_NeverPastTheMiddle")]
        public void GripPosition(float handle, float belowGuard, float expected)
        {
            Assert.AreEqual(expected, GripSeat.GripPosition(handle, belowGuard), 1e-6f);
        }

        // ------------------------------------------------------ fat handles

        [Test]
        public void HandleOffset_IsZero_ForTheSolvedSize_OrAnUnmeasuredHandle()
        {
            Assert.AreEqual(Vector3.zero, GripSeat.HandleOffset(0f, 0.015f, true));
            Assert.Less(GripSeat.HandleOffset(0.015f, 0.015f, true).magnitude, 1e-7f);
        }

        [Test]
        public void HandleOffset_MovesAFatHandleOutOfThePalm_AndBackFromTheKnuckles()
        {
            // Right hand: the palm is -X in the grip frame, the knuckles +Z.
            var right = GripSeat.HandleOffset(0.02f, 0.015f, rightHand: true);
            var left = GripSeat.HandleOffset(0.02f, 0.015f, rightHand: false);

            Assert.AreEqual(-0.005f, right.x, 1e-6f);
            Assert.AreEqual(0f, right.y, 1e-6f);
            Assert.AreEqual(-0.005f, right.z, 1e-6f);
            Assert.AreEqual(0.005f, left.x, 1e-6f, "a left hand is the mirror image");
            Assert.AreEqual(right.z, left.z, 1e-6f);
        }

        [Test]
        public void HandleOffset_KeepsTheSurfaceWhereTheSolvedHandlesWas()
        {
            // Right hand: the palm lies on the handle's +X side (toward the back
            // of the hand) and the curled first finger segments on its +Z side.
            // The fat handle's surface touches both exactly where the thin one's did.
            const float solved = 0.015f, fat = 0.022f;
            var centre = GripSeat.HandleOffset(fat, solved, true);

            Assert.AreEqual(solved, centre.x + fat, 1e-6f, "against the palm");
            Assert.AreEqual(solved, centre.z + fat, 1e-6f, "inside the fingers");
        }

        [Test]
        public void ItemPose_WithASeatOffset_PutsTheAnchorOnTheOffset()
        {
            var anchorOffset = new Vector3(0.1f, -0.3f, 0.05f);
            var anchorRotation = Quaternion.Euler(30f, -70f, 110f);
            var seat = new Vector3(-0.004f, 0f, -0.004f);

            GripSeat.ItemPose(anchorOffset, anchorRotation, seat, out var position, out var rotation);

            Assert.Less((position + rotation * anchorOffset - seat).magnitude, 1e-5f);
            Assert.Less(Quaternion.Angle(Quaternion.identity, rotation * anchorRotation), 0.01f);
        }

        // --------------------------------------------------------- HandGrip

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

        (HandGrip grip, HeldItem item) Sword(float gripRadius)
        {
            var grab = Spawn("GrabPoint").transform;
            grab.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(10f, 40f, -30f));
            var grip = grab.gameObject.AddComponent<HandGrip>();

            var root = Spawn("Held_Sword");
            var anchor = Spawn("GripAnchor").transform;
            anchor.SetParent(root.transform, false);
            var bladeBase = Spawn("BladeBase").transform;
            bladeBase.SetParent(root.transform, false);
            var bladeTip = Spawn("BladeTip").transform;
            bladeTip.SetParent(root.transform, false);
            bladeTip.localPosition = new Vector3(0f, 0.9f, 0f);
            var item = root.AddComponent<HeldItem>();
            item.SetGripAnchor(anchor);
            item.SetBladeMarkers(bladeBase, bladeTip);
            item.SetGripRadius(gripRadius);
            return (grip, item);
        }

        [Test]
        public void Hold_SeatsAFatHandleOutOfThePalm()
        {
            // No profile: the hand assumes the default 0.015 m it was solved for.
            var (grip, item) = Sword(0.02f);

            grip.Hold(item);

            Vector3 local = grip.transform.InverseTransformPoint(item.GripAnchor.position);
            Assert.Less((local - GripSeat.HandleOffset(0.02f, HandGripProfile.DefaultGripRadius, true)).magnitude, 1e-5f);
        }

        [Test]
        public void Hold_SeatsAnUnmeasuredHandleOnTheGrabPoint()
        {
            var (grip, item) = Sword(0f);

            grip.Hold(item);

            Assert.Less((item.GripAnchor.position - grip.transform.position).magnitude, 1e-5f);
        }

        [Test]
        public void MatchBladeMarkers_MovesTheHandsMarkersOntoTheHeldBlade()
        {
            var (grip, item) = Sword(0.02f);
            var handBase = Spawn("HandBladeBase").transform;
            var handTip = Spawn("HandBladeTip").transform;
            handBase.SetParent(grip.transform, false);
            handTip.SetParent(grip.transform, false);
            handTip.localPosition = new Vector3(0f, 0.78f, 0f);   // the compass's tip

            Assert.IsFalse(grip.MatchBladeMarkers(handBase, handTip), "nothing held yet");
            grip.Hold(item);
            Assert.IsTrue(grip.MatchBladeMarkers(handBase, handTip));

            Assert.Less((handBase.position - item.BladeBase.position).magnitude, 1e-5f);
            Assert.Less((handTip.position - item.BladeTip.position).magnitude, 1e-5f);
        }

        [Test]
        public void MatchBladeMarkers_ForAnItemWithoutABlade_MovesNothing()
        {
            var (grip, item) = Sword(0f);
            item.SetBladeMarkers(null, null);
            var handTip = Spawn("HandBladeTip").transform;
            handTip.position = new Vector3(7f, 7f, 7f);
            grip.Hold(item);

            Assert.IsFalse(grip.MatchBladeMarkers(null, handTip));
            Assert.AreEqual(new Vector3(7f, 7f, 7f), handTip.position);
        }

        // ------------------------------------------------------- curl scale

        [Test]
        public void CurlScale_IsOne_ForTheTunedSize_OrAnUnmeasuredHandle()
        {
            Assert.AreEqual(1f, FingerCurlMath.CurlScale(0.015f, 0f, 0.009f));
            Assert.AreEqual(1f, FingerCurlMath.CurlScale(0.015f, 0.015f, 0.009f), 1e-6f);
        }

        [Test]
        public void CurlScale_BendsLessRoundAFatterHandle_AndMoreRoundAThinner()
        {
            // The finger's centre line wraps radius + half-thickness.
            Assert.AreEqual(0.024f / 0.029f, FingerCurlMath.CurlScale(0.015f, 0.02f, 0.009f), 1e-5f);
            Assert.Greater(FingerCurlMath.CurlScale(0.015f, 0.01f, 0.009f), 1f);
        }

        [Test]
        public void CurlScale_IsClamped()
        {
            Assert.AreEqual(0.5f, FingerCurlMath.CurlScale(0.015f, 1f, 0.009f));
            Assert.AreEqual(1.5f, FingerCurlMath.CurlScale(0.05f, 0.001f, 0.001f));
        }
    }
}
