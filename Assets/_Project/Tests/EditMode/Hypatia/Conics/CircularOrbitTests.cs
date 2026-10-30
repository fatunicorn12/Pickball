using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Conics
{
    /// <summary>
    /// The ellipse volley's current design is circles (e = 0): a circle is an
    /// ellipse with e = 0, so the Kepler maths and the fit must handle it cleanly:
    /// constant radius, uniform speed, radius inside the allowed band, and the
    /// code defaults on HypatiaConfig must give e = 0 with no jitter.
    /// </summary>
    public class CircularOrbitTests
    {
        [Test]
        public void ZeroEccentricity_HasConstantRadius_AndUniformSpeed()
        {
            var orbit = new KeplerOrbit(6f, 0f, 7.5f);
            float expectedSpeed = 2f * Mathf.PI * 6f / 7.5f;
            for (int i = 0; i <= 60; i++)
            {
                float t = i * 7.5f / 60f;
                Assert.That(orbit.PositionAt(t).magnitude, Is.EqualTo(6f).Within(1e-4f), $"radius at t={t}");
                Assert.That(orbit.RadiusAt(t), Is.EqualTo(6f).Within(1e-4f));
                Assert.That(orbit.VelocityAt(t).magnitude, Is.EqualTo(expectedSpeed).Within(1e-3f), $"speed at t={t}");
            }
            Assert.That(orbit.Periapsis, Is.EqualTo(orbit.Apoapsis).Within(1e-5f));
            Assert.That(orbit.SemiMinor, Is.EqualTo(orbit.SemiMajor).Within(1e-5f));
        }

        [Test]
        public void ZeroEccentricity_SolvesInOneIteration()
        {
            for (int i = 0; i < 16; i++)
            {
                double m = i * Mathf.PI / 8.0;
                double e = KeplerOrbit.SolveEccentricAnomaly(m, 0.0, out int iterations);
                Assert.That(e, Is.EqualTo(m % (2.0 * System.Math.PI)).Within(1e-9));
                Assert.That(iterations, Is.EqualTo(1));
            }
        }

        [Test]
        public void Fit_ZeroEccentricity_GivesACircleBetweenTheBounds()
        {
            foreach (float size in new[] { 0f, 0.3f, 1f })
            {
                OrbitShape s = OrbitFit.Fit(0f, 2.5f, 11.5f, size);
                Assert.That(s.Eccentricity, Is.EqualTo(0f));
                Assert.That(s.SemiMajor, Is.InRange(2.5f, 11.5f));
                Assert.That(s.Periapsis, Is.EqualTo(s.Apoapsis));
            }
            Assert.That(OrbitFit.Fit(0f, 2.5f, 11.5f, 0f).SemiMajor, Is.EqualTo(2.5f).Within(1e-5f));
            Assert.That(OrbitFit.Fit(0f, 2.5f, 11.5f, 1f).SemiMajor, Is.EqualTo(11.5f).Within(1e-5f));
        }

        [Test]
        public void CodeDefaults_AreCircles_WithNoJitter()
        {
            var config = ScriptableObject.CreateInstance<HypatiaConfig>();
            try
            {
                Assert.That(config.EllipseEccentricityMin, Is.EqualTo(0f));
                Assert.That(config.EllipseEccentricityMax, Is.EqualTo(0f));
                Assert.That(config.OrbitEccentricityJitter, Is.EqualTo(0f));
                Assert.That(ConicPicker.PickEccentricity(ConicKind.Ellipse, config.EllipseEccentricityMin,
                                                         config.EllipseEccentricityMax, 1.3f, 2f,
                                                         new System.Random(3)), Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }
    }
}
