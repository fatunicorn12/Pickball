using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Presentation;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Burning
{
    /// <summary>
    /// The floor fire's look must stay fair: the burn front arrives exactly when the
    /// band ignites, flames never stand over safe ground, and the bright boundary
    /// line marks the real edge of the fire, not an old seam.
    /// </summary>
    public class BandLookTests
    {
        [Test]
        public void Warning_RunsFromZeroToOneOverTheRealCountdown()
        {
            Assert.That(BandLook.WarningProgress(2f, 2f), Is.EqualTo(0f));
            Assert.That(BandLook.WarningProgress(1f, 2f), Is.EqualTo(0.5f));
            Assert.That(BandLook.WarningProgress(0f, 2f), Is.EqualTo(1f));
        }

        [Test]
        public void Warning_StaysInRange_WithNoLead_OrAnOverlongCountdown()
        {
            Assert.That(BandLook.WarningProgress(0f, 0f), Is.EqualTo(1f));
            Assert.That(BandLook.WarningProgress(3f, 2f), Is.EqualTo(0f));
            Assert.That(BandLook.WarningProgress(-1f, 2f), Is.EqualTo(1f));
        }

        [Test]
        public void Area_IsTheAnnulus_AndNeverNegative()
        {
            Assert.That(BandLook.Area(6f, 8f), Is.EqualTo(Mathf.PI * 28f).Within(1e-3f));
            Assert.That(BandLook.Area(8f, 6f), Is.EqualTo(0f));
        }

        [Test]
        public void Rate_FollowsAreaUpToTheCap()
        {
            Assert.That(BandLook.Rate(6f, 8f, 1f, 1000f), Is.EqualTo(Mathf.PI * 28f).Within(1e-3f));
            Assert.That(BandLook.Rate(6f, 8f, 100f, 170f), Is.EqualTo(170f));
            Assert.That(BandLook.Rate(6f, 8f, -1f, 170f), Is.EqualTo(0f));
        }

        // The band sizes from the shipped config.
        [TestCase(6f, 8.33f, 0.45f)]
        [TestCase(8.33f, 10.67f, 0.45f)]
        [TestCase(10.67f, 13f, 0.45f)]
        public void Thickness_KeepsEmissionOffTheSafeGround(float inner, float outer, float margin)
        {
            float thickness = BandLook.RadiusThickness(inner, outer, margin);
            float innermostEmission = outer * (1f - thickness);
            Assert.That(innermostEmission, Is.EqualTo(inner + margin).Within(1e-3f));
            Assert.That(innermostEmission, Is.GreaterThan(inner));
        }

        [Test]
        public void Thickness_OnABandTooNarrowForTheMargin_EmitsAlongTheOuterEdge()
        {
            Assert.That(BandLook.RadiusThickness(10f, 10.2f, 0.5f), Is.EqualTo(0f));
        }

        [Test]
        public void Thickness_WithNoRadius_IsZero()
        {
            Assert.That(BandLook.RadiusThickness(0f, 0f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void Frontier_IsTheSafeBoundary_NowOrOnceTheWarningCatches()
        {
            // Burned out to 8.33, with the 6-8.33 band smouldering: both edges matter.
            Assert.That(BandLook.IsFrontier(8.33f, 8.33f, 6f), Is.True);
            Assert.That(BandLook.IsFrontier(6f, 8.33f, 6f), Is.True);
            // An old seam further out does not.
            Assert.That(BandLook.IsFrontier(10.67f, 8.33f, 6f), Is.False);
        }

        [Test]
        public void Flash_PeaksAtOnce_AndDiesOnTime()
        {
            Assert.That(BandLook.Flash(0f, 0.45f), Is.EqualTo(1f));
            Assert.That(BandLook.Flash(0.225f, 0.45f), Is.LessThan(0.5f));
            Assert.That(BandLook.Flash(0.45f, 0.45f), Is.EqualTo(0f));
            Assert.That(BandLook.Flash(float.MaxValue, 0.45f), Is.EqualTo(0f));
            Assert.That(BandLook.Flash(0f, 0f), Is.EqualTo(0f));
        }
    }
}
