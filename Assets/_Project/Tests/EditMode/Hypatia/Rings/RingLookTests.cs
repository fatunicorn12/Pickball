using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Presentation;
using Pickleball.Sandbox.Boss.Hypatia.Rings;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Rings
{
    /// <summary>
    /// The ring's look must not lie about a dodge: a low ring is drawn exactly as
    /// tall as it hits, a tall ring has no top to clear, and the bright gap edges
    /// sit exactly where the gameplay gap ends.
    /// </summary>
    public class RingLookTests
    {
        [Test]
        public void LowRing_IsDrawnExactlyAsTallAsItHits()
        {
            Assert.That(RingLook.DrawnHeight(0.8f, tall: false, auroraHeight: 8f), Is.EqualTo(0.8f));
        }

        [Test]
        public void TallRing_CarriesOnPastItsWall()
        {
            Assert.That(RingLook.DrawnHeight(7f, tall: true, auroraHeight: 8f), Is.EqualTo(15f));
            Assert.That(RingLook.DrawnHeight(7f, tall: true, auroraHeight: -1f), Is.EqualTo(7f));
        }

        [Test]
        public void Height_IsNeverZero()
        {
            Assert.That(RingLook.DrawnHeight(0f, tall: false, auroraHeight: 0f), Is.GreaterThan(0f));
        }

        [TestCase(0f, 30f)]
        [TestCase(45f, 30f)]
        [TestCase(350f, 30f)]
        public void GapEdges_SitExactlyWhereTheGameplayGapEnds(float centre, float width)
        {
            foreach (int side in new[] { -1, 1 })
            {
                float edge = RingLook.GapEdgeAzimuth(centre, width, side);
                Assert.That(RingMath.IsInGap(edge - side * 0.25f, centre, width), Is.True, $"just inside, side {side}");
                Assert.That(RingMath.IsInGap(edge + side * 0.25f, centre, width), Is.False, $"just outside, side {side}");
            }
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Sparks_HoldTheirRateAtAnyFrameRate(float dt)
        {
            float owed = 0f;
            int total = 0;
            int frames = Mathf.RoundToInt(10f / dt);
            for (int i = 0; i < frames; i++) total += RingLook.SparksDue(ref owed, 26f, dt, 8);
            Assert.That(total, Is.EqualTo(260).Within(2));
        }

        [Test]
        public void Sparks_AreCappedAfterAHitch()
        {
            float owed = 0f;
            Assert.That(RingLook.SparksDue(ref owed, 26f, 5f, 8), Is.EqualTo(8));
        }

        [Test]
        public void Sparks_NoRateOrNoTime_IsNone()
        {
            float owed = 0f;
            Assert.That(RingLook.SparksDue(ref owed, 0f, 1f, 8), Is.EqualTo(0));
            Assert.That(RingLook.SparksDue(ref owed, 26f, 0f, 8), Is.EqualTo(0));
            Assert.That(owed, Is.EqualTo(0f));
        }
    }
}
