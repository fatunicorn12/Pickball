using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Conics;

namespace Pickleball.Tests.Hypatia.Conics
{
    /// <summary>Orbiters never graze her and never leave the plate.</summary>
    public class OrbitFitTests
    {
        const float PeriMin = 2.5f, ApoMax = 11.5f;

        [Test]
        public void EveryFit_RespectsBothBounds()
        {
            for (float e = 0f; e <= 0.951f; e += 0.05f)
            {
                foreach (float size in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                {
                    OrbitShape s = OrbitFit.Fit(e, PeriMin, ApoMax, size);
                    Assert.That(s.Periapsis, Is.GreaterThanOrEqualTo(PeriMin - 1e-4f), $"e={e} size={size}");
                    Assert.That(s.Apoapsis, Is.LessThanOrEqualTo(ApoMax + 1e-4f), $"e={e} size={size}");
                    Assert.That(s.Eccentricity, Is.InRange(0f, 0.99f));
                }
            }
        }

        [Test]
        public void FeasibleEccentricity_IsKept()
        {
            OrbitShape s = OrbitFit.Fit(0.5f, PeriMin, ApoMax, 0.5f);
            Assert.That(s.Eccentricity, Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void InfeasibleEccentricity_IsLoweredToTheTightestFit()
        {
            OrbitShape s = OrbitFit.Fit(0.9f, PeriMin, ApoMax, 0.5f);
            float eMax = (ApoMax - PeriMin) / (ApoMax + PeriMin);
            Assert.That(s.Eccentricity, Is.EqualTo(eMax).Within(1e-5f));
            Assert.That(s.Periapsis, Is.EqualTo(PeriMin).Within(1e-3f));
            Assert.That(s.Apoapsis, Is.EqualTo(ApoMax).Within(1e-3f));
        }

        [Test]
        public void SizeExtremes_TouchEachBound()
        {
            Assert.That(OrbitFit.Fit(0.4f, PeriMin, ApoMax, 0f).Periapsis, Is.EqualTo(PeriMin).Within(1e-4f));
            Assert.That(OrbitFit.Fit(0.4f, PeriMin, ApoMax, 1f).Apoapsis, Is.EqualTo(ApoMax).Within(1e-4f));
        }

        [Test]
        public void ConflictingBounds_GiveACircleClearOfHer()
        {
            OrbitShape s = OrbitFit.Fit(0.6f, 5f, 4f, 0.5f);
            Assert.That(s.Eccentricity, Is.EqualTo(0f));
            Assert.That(s.Periapsis, Is.EqualTo(5f).Within(1e-5f));
        }
    }
}
