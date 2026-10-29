using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Burning;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Burning
{
    /// <summary>A scroll's drift stays in the burned ring, holds still on a
    /// frozen clock, and its pickup is swept in the player's frame.</summary>
    public class ScrollMotionTests
    {
        static readonly Vector3 CapsuleBottom = new(0f, 0.35f, 0f), CapsuleTop = new(0f, 1.25f, 0f);
        const float CapsuleRadius = 0.35f, Pickup = 0.8f;

        [Test]
        public void Drift_StaysInTheRing()
        {
            var offset = new Vector3(11.5f, 0f, 0f);
            for (int i = 0; i < 2000; i++)
            {
                offset = ScrollMotion.Drift(offset, i * 0.05f, 0.05f, 3f, 1f, 7.3f, 90f, 1f, 10.67f, 13f);
                Assert.That(offset.magnitude, Is.InRange(10.67f - 1e-3f, 13f + 1e-3f), $"step {i}");
                Assert.That(offset.y, Is.EqualTo(0f));
            }
        }

        [Test]
        public void Drift_OnAFrozenClock_HoldsStill()
        {
            var offset = new Vector3(11.5f, 0f, 2f);
            Assert.That(ScrollMotion.Drift(offset, 1f, 0f, 3f, 1f, 0f, 50f, 0.3f, 10.67f, 13f), Is.EqualTo(offset));
        }

        [Test]
        public void Drift_CirclesTheWayItWasTold()
        {
            // No wander: from +X, direction +1 heads to -Z (Cross(up, right) =
            // back), which is clockwise in Unity's top view (X right, Z up).
            var start = new Vector3(12f, 0f, 0f);
            Vector3 a = ScrollMotion.Drift(start, 0f, 0.1f, 1f, 1f, 0f, 0f, 0f, 10.67f, 13f);
            Vector3 b = ScrollMotion.Drift(start, 0f, 0.1f, 1f, -1f, 0f, 0f, 0f, 10.67f, 13f);
            Assert.That(Mathf.Sign(a.z), Is.EqualTo(-Mathf.Sign(b.z)), "the two directions are opposite");
            Assert.That(Mathf.Abs(a.z), Is.EqualTo(0.1f).Within(1e-3f));
        }

        [Test]
        public void ClampToRing_PullsInsideAndOutsidePointsOntoTheEdges()
        {
            Assert.That(ScrollMotion.ClampToRing(new Vector3(5f, 0f, 0f), 10.67f, 13f).magnitude,
                        Is.EqualTo(10.67f).Within(1e-4f));
            Vector3 outside = ScrollMotion.ClampToRing(new Vector3(0f, 3f, 20f), 10.67f, 13f);
            Assert.That(outside.y, Is.EqualTo(0f));
            Assert.That(outside.x, Is.EqualTo(0f));
            Assert.That(outside.z, Is.EqualTo(13f).Within(1e-4f));
            Assert.That(ScrollMotion.ClampToRing(Vector3.zero, 10.67f, 13f).magnitude,
                        Is.EqualTo(10.67f).Within(1e-4f), "the centre has no direction; any edge point will do");
        }

        [Test]
        public void Touches_AStillScrollAtThePlayer()
        {
            var at = new Vector3(0f, 0.6f, 0f);
            Assert.That(ScrollMotion.Touches(at, at, Vector3.zero, Vector3.zero, Pickup,
                                             CapsuleBottom, CapsuleTop, CapsuleRadius), Is.True);
        }

        [Test]
        public void Touches_APlayerDashingThroughAStillScroll()
        {
            // The scroll sits 1.6 m to the player's left at the start of the
            // frame and 1.6 m to the right at the end: never in reach at
            // either sample, but the player went straight through it.
            var scroll = new Vector3(0f, 0.6f, 0f);
            var from = new Vector3(-1.6f, 0f, 0f);
            var to = new Vector3(1.6f, 0f, 0f);
            Vector3 offset = to; // capsule axis where the player is now
            Assert.That(ScrollMotion.Touches(scroll, scroll, from, to, Pickup,
                                             CapsuleBottom + offset, CapsuleTop + offset, CapsuleRadius), Is.True);
        }

        [Test]
        public void Touches_MovingTogether_DoesNotCountAsContact()
        {
            // Both moved the same 3 m, keeping 2 m apart: no contact.
            var shift = new Vector3(0f, 0f, 3f);
            var scrollFrom = new Vector3(2f, 0.6f, 0f);
            Assert.That(ScrollMotion.Touches(scrollFrom, scrollFrom + shift, Vector3.zero, shift, Pickup,
                                             CapsuleBottom + shift, CapsuleTop + shift, CapsuleRadius), Is.False);
        }
    }
}
