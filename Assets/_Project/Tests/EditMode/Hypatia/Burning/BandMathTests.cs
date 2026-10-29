using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Burning;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Burning
{
    /// <summary>
    /// The band geometry: which band a radius is in (edges go to the inner
    /// band; beyond the plate is no band), how malformed radii are repaired,
    /// which bands may burn, and the safe radius after k burns.
    /// </summary>
    public class BandMathTests
    {
        static readonly float[] Radii = { 6f, 8.33f, 10.67f, 13f };

        // ---------------------------------------------------------- BandAt

        [Test]
        public void BandAt_EdgesBelongToTheInnerBand()
        {
            Assert.That(BandMath.BandAt(Radii, 0f), Is.EqualTo(0));
            Assert.That(BandMath.BandAt(Radii, 5.9f), Is.EqualTo(0));
            Assert.That(BandMath.BandAt(Radii, 6f), Is.EqualTo(0), "the hub's rim is hub");
            Assert.That(BandMath.BandAt(Radii, 6.01f), Is.EqualTo(1));
            Assert.That(BandMath.BandAt(Radii, 8.33f), Is.EqualTo(1));
            Assert.That(BandMath.BandAt(Radii, 10.67f), Is.EqualTo(2));
            Assert.That(BandMath.BandAt(Radii, 12.99f), Is.EqualTo(3));
            Assert.That(BandMath.BandAt(Radii, 13f), Is.EqualTo(3), "the rim is on the plate");
        }

        [Test]
        public void BandAt_BeyondThePlateOrNaN_IsNoBand()
        {
            Assert.That(BandMath.BandAt(Radii, 13.01f), Is.EqualTo(-1));
            Assert.That(BandMath.BandAt(Radii, 100f), Is.EqualTo(-1));
            Assert.That(BandMath.BandAt(Radii, float.NaN), Is.EqualTo(-1));
            Assert.That(BandMath.BandAt(null, 3f), Is.EqualTo(-1));
            Assert.That(BandMath.BandAt(Radii, -1f), Is.EqualTo(0), "negative is the centre");
        }

        // -------------------------------------------------------- Sanitise

        [Test]
        public void Sanitise_WellFormed_IsUnchanged()
        {
            Assert.That(BandMath.Sanitise(Radii, 13f), Is.EqualTo(Radii));
        }

        [Test]
        public void Sanitise_Unsorted_IsSorted()
        {
            Assert.That(BandMath.Sanitise(new[] { 13f, 6f, 10.67f, 8.33f }, 13f), Is.EqualTo(Radii));
        }

        [Test]
        public void Sanitise_NullOrEmpty_IsOneHubCoveringThePlate()
        {
            Assert.That(BandMath.Sanitise(null, 13f), Is.EqualTo(new[] { 13f }));
            Assert.That(BandMath.Sanitise(new float[0], 13f), Is.EqualTo(new[] { 13f }));
        }

        [Test]
        public void Sanitise_LastEdgeShortOfTheArena_AppendsTheArena()
        {
            Assert.That(BandMath.Sanitise(new[] { 6f, 8f, 10f }, 13f), Is.EqualTo(new[] { 6f, 8f, 10f, 13f }));
        }

        [Test]
        public void Sanitise_LastEdgeNearlyTheArena_SnapsOntoIt()
        {
            Assert.That(BandMath.Sanitise(new[] { 6f, 12.9995f }, 13f), Is.EqualTo(new[] { 6f, 13f }));
            Assert.That(BandMath.Sanitise(new[] { 6f, 13.0005f }, 13f), Is.EqualTo(new[] { 6f, 13f }));
        }

        [Test]
        public void Sanitise_DropsJunkDuplicatesAndEdgesBeyondTheArena()
        {
            var raw = new[] { float.NaN, -2f, 0f, 6f, 6.0001f, 20f, float.PositiveInfinity, 9f };
            Assert.That(BandMath.Sanitise(raw, 13f), Is.EqualTo(new[] { 6f, 9f, 13f }));
        }

        [Test]
        public void Sanitise_BadArenaRadius_FallsBackToTheLargestEdge()
        {
            Assert.That(BandMath.Sanitise(new[] { 6f, 9f }, float.NaN), Is.EqualTo(new[] { 6f, 9f }));
            Assert.That(BandMath.Sanitise(new float[0], 0f), Is.EqualTo(new[] { 1f }));
        }

        // ------------------------------------------------------ IsBurnable

        [Test]
        public void IsBurnable_TheHubNeverBurns_EvenWithNoSafeRadius()
        {
            Assert.That(BandMath.IsBurnable(Radii, 0, 0f), Is.False);
            Assert.That(BandMath.IsBurnable(new[] { 13f }, 0, 0f), Is.False);
        }

        [Test]
        public void IsBurnable_RespectsTheMinimumSafeRadius()
        {
            Assert.That(BandMath.IsBurnable(Radii, 1, 6f), Is.True, "inner edge 6 = safe radius 6");
            Assert.That(BandMath.IsBurnable(Radii, 1, 6.0005f), Is.True, "within epsilon");
            Assert.That(BandMath.IsBurnable(Radii, 1, 7f), Is.False);
            Assert.That(BandMath.IsBurnable(Radii, 3, 7f), Is.True);
            Assert.That(BandMath.IsBurnable(Radii, 1, float.NaN), Is.True, "NaN safe radius is 0");
            Assert.That(BandMath.IsBurnable(Radii, 4, 0f), Is.False, "out of range");
            Assert.That(BandMath.IsBurnable(Radii, -1, 0f), Is.False);
        }

        [Test]
        public void BurnableCount_CountsTheOuterBandsDownToTheSafeRadius()
        {
            Assert.That(BandMath.BurnableCount(Radii, 6f), Is.EqualTo(3));
            Assert.That(BandMath.BurnableCount(Radii, 0f), Is.EqualTo(3), "never the hub");
            Assert.That(BandMath.BurnableCount(Radii, 9f), Is.EqualTo(1));
            Assert.That(BandMath.BurnableCount(Radii, 20f), Is.EqualTo(0));
        }

        // -------------------------------------------------- SafeRadiusAfter

        [Test]
        public void SafeRadiusAfter_ShrinksBandByBandAndStopsAtTheSafeRadius()
        {
            Assert.That(BandMath.SafeRadiusAfter(Radii, 0, 6f), Is.EqualTo(13f));
            Assert.That(BandMath.SafeRadiusAfter(Radii, 1, 6f), Is.EqualTo(10.67f));
            Assert.That(BandMath.SafeRadiusAfter(Radii, 2, 6f), Is.EqualTo(8.33f));
            Assert.That(BandMath.SafeRadiusAfter(Radii, 3, 6f), Is.EqualTo(6f));
            Assert.That(BandMath.SafeRadiusAfter(Radii, 10, 6f), Is.EqualTo(6f), "clamped");
            Assert.That(BandMath.SafeRadiusAfter(Radii, -1, 6f), Is.EqualTo(13f));
            Assert.That(BandMath.SafeRadiusAfter(Radii, 2, 9f), Is.EqualTo(10.67f), "band 2 is inside 9 m");
        }

        // --------------------------------------------------- PointInAnnulus

        [Test]
        public void PointInAnnulus_StaysInTheRing()
        {
            for (int i = 0; i <= 10; i++)
            for (int j = 0; j <= 10; j++)
            {
                Vector3 p = BandMath.PointInAnnulus(10.67f, 13f, i / 10f, j / 10f);
                Assert.That(p.y, Is.EqualTo(0f));
                Assert.That(p.magnitude, Is.InRange(10.67f - 1e-3f, 13f + 1e-3f));
            }
        }

        [Test]
        public void PointInAnnulus_IsUniformByArea()
        {
            // Half the area of the 6-13 ring lies inside sqrt((36 + 169) / 2).
            float r = BandMath.PointInAnnulus(6f, 13f, 0.5f, 0f).magnitude;
            Assert.That(r, Is.EqualTo(Mathf.Sqrt(102.5f)).Within(1e-3f));
        }

        [Test]
        public void HorizontalDistance_IgnoresHeight()
        {
            Assert.That(BandMath.HorizontalDistance(new Vector3(1f, 5f, 1f), new Vector3(4f, -3f, 5f)),
                        Is.EqualTo(5f).Within(1e-5f));
        }
    }
}
