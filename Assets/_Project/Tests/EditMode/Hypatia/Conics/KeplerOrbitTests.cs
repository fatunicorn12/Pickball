using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Conics
{
    /// <summary>Kepler's equation and the laws the orbiters' rhythm depends on.</summary>
    public class KeplerOrbitTests
    {
        [Test]
        public void Solver_ConvergesForEveryEccentricityAndMeanAnomaly()
        {
            foreach (double e in new[] { 0.0, 0.2, 0.5, 0.79, 0.8, 0.9, 0.95 })
            {
                for (int i = 0; i < 72; i++)
                {
                    double m = i * 2.0 * System.Math.PI / 72.0;
                    double E = KeplerOrbit.SolveEccentricAnomaly(m, e, out int iterations);
                    double residual = E - e * System.Math.Sin(E) - m;
                    // E may come back shifted by 2π relative to m's branch; wrap it.
                    residual = System.Math.IEEERemainder(residual, 2.0 * System.Math.PI);
                    Assert.That(System.Math.Abs(residual), Is.LessThan(1e-6), $"e={e} M={m}");
                    Assert.That(iterations, Is.LessThanOrEqualTo(KeplerOrbit.MaxIterations));
                }
            }
        }

        [Test]
        public void EqualAreasInEqualTimes_AtPeriapsisAndApoapsis()
        {
            var orbit = new KeplerOrbit(6f, 0.6f, 8f);
            const float dt = 0.4f;
            double atPeri = SweptArea(orbit, 0f, dt);
            double atApo = SweptArea(orbit, orbit.Period * 0.5f, dt);
            double expected = System.Math.PI * orbit.SemiMajor * orbit.SemiMinor * dt / orbit.Period;

            Assert.That(atPeri, Is.EqualTo(atApo).Within(atApo * 0.01));
            Assert.That(atPeri, Is.EqualTo(expected).Within(expected * 0.01));
            Assert.That(atApo, Is.EqualTo(expected).Within(expected * 0.01));
        }

        [Test]
        public void FasterAtPeriapsis_ByTheVisVivaRatio()
        {
            const float e = 0.6f;
            var orbit = new KeplerOrbit(6f, e, 8f);
            float peri = orbit.VelocityAt(0f).magnitude;
            float apo = orbit.VelocityAt(orbit.Period * 0.5f).magnitude;
            Assert.That(peri, Is.GreaterThan(apo));
            Assert.That(peri / apo, Is.EqualTo((1f + e) / (1f - e)).Within(1e-3f * (1f + e) / (1f - e)));
        }

        [Test]
        public void StartsAtPeriapsis_ReachesApoapsisAtHalfPeriod_AndReturns()
        {
            var orbit = new KeplerOrbit(5f, 0.5f, 7f);
            Vector2 start = orbit.PositionAt(0f);
            Assert.That(Vector2.Distance(start, new Vector2(orbit.Periapsis, 0f)), Is.LessThan(1e-4f));
            Assert.That(Vector2.Distance(orbit.PositionAt(3.5f), new Vector2(-orbit.Apoapsis, 0f)), Is.LessThan(1e-3f));
            Assert.That(Vector2.Distance(orbit.PositionAt(7f), start), Is.LessThan(1e-3f));
            Assert.That(Vector2.Distance(orbit.PositionAt(21f), start), Is.LessThan(1e-3f));
        }

        [Test]
        public void Circle_HasUniformSpeed()
        {
            var orbit = new KeplerOrbit(4f, 0f, 6f);
            float expected = 2f * Mathf.PI * 4f / 6f;
            for (int i = 0; i < 24; i++)
            {
                float t = i * 6f / 24f;
                Assert.That(orbit.VelocityAt(t).magnitude, Is.EqualTo(expected).Within(expected * 1e-4f), $"t={t}");
                Assert.That(orbit.RadiusAt(t), Is.EqualTo(4f).Within(1e-4f));
            }
        }

        [Test]
        public void Velocity_MatchesFiniteDifference()
        {
            var orbit = new KeplerOrbit(6f, 0.7f, 8f);
            const float h = 1e-3f;
            for (int i = 0; i < 16; i++)
            {
                float t = 0.13f + i * 0.5f;
                Vector2 fd = (orbit.PositionAt(t + h) - orbit.PositionAt(t - h)) / (2f * h);
                Assert.That(Vector2.Distance(fd, orbit.VelocityAt(t)), Is.LessThan(0.02f), $"t={t}");
            }
        }

        [Test]
        public void Radius_AgreesWithPositionAndTrueAnomaly()
        {
            var orbit = new KeplerOrbit(6f, 0.4f, 8f);
            for (int i = 0; i < 16; i++)
            {
                float t = i * 0.5f;
                Vector2 p = orbit.PositionAt(t);
                Assert.That(p.magnitude, Is.EqualTo(orbit.RadiusAt(t)).Within(1e-3f));
                float nu = orbit.TrueAnomalyAt(t);
                var conic = new ConicPath(orbit.Periapsis, orbit.Eccentricity);
                Assert.That(conic.RadiusAt(nu), Is.EqualTo(p.magnitude).Within(1e-3f), $"t={t}");
            }
        }

        // Area swept from the focus between t0 and t0 + dt, as a fan of thin triangles.
        static double SweptArea(KeplerOrbit orbit, float t0, float dt)
        {
            const int steps = 400; // few enough that float rounding in each thin triangle stays small
            double area = 0.0;
            Vector2 prev = orbit.PositionAt(t0);
            for (int i = 1; i <= steps; i++)
            {
                Vector2 next = orbit.PositionAt(t0 + dt * i / steps);
                area += 0.5 * System.Math.Abs((double)prev.x * next.y - (double)prev.y * next.x);
                prev = next;
            }
            return area;
        }
    }
}
