using NUnit.Framework;
using Pickleball.Rendering;
using UnityEngine;

namespace Pickleball.Tests.Rendering
{
    /// <summary>Pins the width and fade sums the outline shader depends on.</summary>
    public class OutlineMathTests
    {
        [Test]
        public void Radius_IsHalfTheWidth_At1080p()
        {
            Assert.AreEqual(2.5f, OutlineMath.RadiusPixels(5f, 1080), 0.0001f);
        }

        [Test]
        public void Radius_ScalesWithScreenHeight_SoTheLineKeepsItsShareOfTheScreen()
        {
            Assert.AreEqual(5f, OutlineMath.RadiusPixels(5f, 2160), 0.0001f);
            Assert.AreEqual(1.25f, OutlineMath.RadiusPixels(5f, 540), 0.0001f);
        }

        [Test]
        public void Radius_NeverDropsBelowTheMinimum_SoTheLineCannotVanishOnSmallTargets()
        {
            Assert.AreEqual(OutlineMath.MinRadiusPixels, OutlineMath.RadiusPixels(1f, 100), 0.0001f);
        }

        [Test]
        public void FadeRange_KeepsValidValues()
        {
            Assert.AreEqual(new Vector2(45f, 110f), OutlineMath.FadeRange(45f, 110f));
        }

        [TestCase(50f, 50f)]
        [TestCase(80f, 20f)]
        public void FadeRange_EndIsAlwaysBeyondStart_SoTheShaderCannotDivideByZero(float start, float end)
        {
            Vector2 range = OutlineMath.FadeRange(start, end);
            Assert.AreEqual(start, range.x, 0.0001f);
            Assert.Greater(range.y, range.x);
        }

        [Test]
        public void FadeRange_ClampsANegativeStart()
        {
            Assert.AreEqual(0f, OutlineMath.FadeRange(-5f, 10f).x, 0.0001f);
        }
    }
}
