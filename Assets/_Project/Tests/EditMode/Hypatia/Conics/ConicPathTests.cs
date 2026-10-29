using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Conics;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Conics
{
    /// <summary>The shape a single e gives: circle, ellipse, parabola, hyperbola,
    /// and the hyperbola's promise to stay between its asymptotes.</summary>
    public class ConicPathTests
    {
        const float Q = 2.5f;

        [Test]
        public void ZeroEccentricity_IsACircleOfRadiusQ()
        {
            var c = new ConicPath(3f, 0f);
            Assert.IsTrue(c.IsEllipse);
            for (int i = 0; i < 36; i++)
            {
                float theta = -Mathf.PI + i * Mathf.PI / 18f;
                Assert.That(c.RadiusAt(theta), Is.EqualTo(3f).Within(1e-5f), $"θ={theta}");
            }
        }

        [Test]
        public void Ellipse_ReachesApoapsisAtPi()
        {
            var c = new ConicPath(2f, 0.5f);
            Assert.That(c.RadiusAt(0f), Is.EqualTo(2f).Within(1e-5f));
            Assert.That(c.RadiusAt(Mathf.PI), Is.EqualTo(6f).Within(1e-4f)); // q(1+e)/(1-e)
            Assert.That(c.Apoapsis, Is.EqualTo(6f).Within(1e-4f));
            Assert.IsTrue(c.IsValidAnomaly(Mathf.PI));
        }

        [Test]
        public void Ellipse_ApoapsisFormulaHoldsAcrossE()
        {
            for (float e = 0.05f; e < 0.96f; e += 0.1f)
            {
                var c = new ConicPath(Q, e);
                float expected = Q * (1f + e) / (1f - e);
                Assert.That(c.RadiusAt(Mathf.PI), Is.EqualTo(expected).Within(expected * 1e-4f), $"e={e}");
            }
        }

        [Test]
        public void Parabola_RunsToInfinityAsThetaNearsPi()
        {
            var c = new ConicPath(Q, 1f);
            Assert.IsTrue(c.IsParabola);
            Assert.That(c.SemiLatusRectum, Is.EqualTo(2f * Q).Within(1e-5f));

            float previous = 0f;
            for (int i = 1; i <= 6; i++)
            {
                float r = c.RadiusAt(Mathf.PI - Mathf.Pow(10f, -i * 0.5f));
                Assert.That(r, Is.GreaterThan(previous));
                previous = r;
            }
            Assert.That(c.RadiusAt(Mathf.PI - 1e-3f), Is.GreaterThan(1e4f * Q));
            Assert.IsFalse(c.IsValidAnomaly(Mathf.PI));
            Assert.That(float.IsPositiveInfinity(c.RadiusAt(Mathf.PI)));
        }

        [Test]
        public void Hyperbola_RunsToInfinityAsThetaNearsAsymptoteAngle()
        {
            var c = new ConicPath(Q, 2f);
            Assert.IsTrue(c.IsHyperbola);
            float thetaInf = Mathf.Acos(-1f / 2f);
            Assert.That(c.MaxTrueAnomaly, Is.EqualTo(thetaInf).Within(1e-5f));

            Assert.That(c.RadiusAt(thetaInf - 1e-3f), Is.GreaterThan(1000f * Q));
            Assert.That(c.RadiusAt(-(thetaInf - 1e-3f)), Is.GreaterThan(1000f * Q));
            Assert.IsFalse(c.IsValidAnomaly(thetaInf + 0.01f));
            Assert.That(float.IsPositiveInfinity(c.RadiusAt(thetaInf + 0.01f)));
        }

        [Test]
        public void Hyperbola_FarPointsHeadAlongTheAsymptotes()
        {
            var c = new ConicPath(Q, 1.5f);
            Vector2 far = c.PointAt(c.MaxTrueAnomaly - 1e-4f).normalized;
            Assert.That(Vector2.Distance(far, c.OutgoingAsymptote), Is.LessThan(1e-2f));
            Vector2 farIn = c.PointAt(-(c.MaxTrueAnomaly - 1e-4f)).normalized;
            Assert.That(Vector2.Distance(farIn, c.IncomingAsymptote), Is.LessThan(1e-2f));
        }

        [Test]
        public void Hyperbola_EverySampleIsStrictlyBetweenTheAsymptotes()
        {
            foreach (float e in new[] { 1.05f, 1.3f, 1.5f, 2f, 3f })
            {
                var c = new ConicPath(Q, e);
                float max = c.MaxTrueAnomaly - 0.05f;
                for (int i = 0; i <= 200; i++)
                {
                    float theta = Mathf.Lerp(-max, max, i / 200f);
                    Vector2 p = c.PointAt(theta);
                    Assert.IsTrue(InsideFocusWedge(c, p), $"e={e} θ={theta} p={p}");
                    Assert.IsTrue(c.IsBetweenAsymptotes(p), $"e={e} θ={theta}");
                }
            }
        }

        [Test]
        public void Hyperbola_WedgeExcludesTheFarBranchAndOutsidePoints()
        {
            var c = new ConicPath(Q, 1.5f);
            Assert.IsTrue(c.IsBetweenAsymptotes(Vector2.zero), "the focus is in the wedge");
            // Far branch vertex: on the other side of the centre.
            Vector2 farVertex = c.Centre + (c.Centre - c.PointAt(0f));
            Assert.IsFalse(c.IsBetweenAsymptotes(farVertex));
            Assert.IsFalse(InsideFocusWedge(c, farVertex));
        }

        [Test]
        public void HyperbolicAnomaly_RoundTripsRadius()
        {
            var c = new ConicPath(Q, 1.7f);
            Assert.That(Vector2.Distance(c.HyperbolicPoint(0f), new Vector2(Q, 0f)), Is.LessThan(1e-5f));
            foreach (float r in new[] { 3f, 8f, 16f, 40f })
            {
                float f = c.HyperbolicAnomalyAtRadius(r);
                Assert.That(c.HyperbolicPoint(f).magnitude, Is.EqualTo(r).Within(r * 1e-4f));
                Assert.That(c.HyperbolicPoint(-f).magnitude, Is.EqualTo(r).Within(r * 1e-4f));
                Assert.IsTrue(InsideFocusWedge(c, c.HyperbolicPoint(f)));
            }
        }

        /// <summary>
        /// Independent of <see cref="ConicPath.IsBetweenAsymptotes"/>: the point is on
        /// the same (strict) side of each asymptote line as the focus is.
        /// </summary>
        internal static bool InsideFocusWedge(ConicPath c, Vector2 p)
        {
            Vector2 centre = c.Centre;
            return SameStrictSide(centre, c.OutgoingAsymptote, Vector2.zero, p) &&
                   SameStrictSide(centre, c.IncomingAsymptote, Vector2.zero, p);
        }

        static bool SameStrictSide(Vector2 origin, Vector2 dir, Vector2 reference, Vector2 p)
        {
            double sr = Cross(dir, reference - origin);
            double sp = Cross(dir, p - origin);
            return sr != 0.0 && sp != 0.0 && System.Math.Sign(sr) == System.Math.Sign(sp);
        }

        static double Cross(Vector2 a, Vector2 b) => (double)a.x * b.y - (double)a.y * b.x;
    }
}
