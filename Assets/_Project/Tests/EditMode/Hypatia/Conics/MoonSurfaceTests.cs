using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia.Presentation;
using UnityEngine;

namespace Pickleball.Tests.Hypatia.Conics
{
    /// <summary>
    /// The moon baked into the orbiters' texture: it must come out the same every
    /// time (a rebuild must not rewrite the texture), have no seam where the
    /// texture wraps, and its craters must really be dents with raised rims.
    /// </summary>
    public class MoonSurfaceTests
    {
        static MoonSurface Moon() => new(seed: 7, craterCount: 110, seaCount: 7);

        [Test]
        public void DirectionAt_IsUnitLength_AndCoversThePoles()
        {
            Assert.That(MoonSurface.DirectionAt(0.3f, 0.7f).magnitude, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(MoonSurface.DirectionAt(0.3f, 1f).y, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(MoonSurface.DirectionAt(0.3f, 0f).y, Is.EqualTo(-1f).Within(1e-5f));
        }

        [Test]
        public void SameSeed_IsIdentical_DifferentSeed_IsNot()
        {
            Vector3 d = MoonSurface.DirectionAt(0.31f, 0.62f);
            Assert.That(Moon().Albedo(d), Is.EqualTo(Moon().Albedo(d)));
            Assert.That(Moon().Height(d), Is.EqualTo(Moon().Height(d)));

            bool differs = false;
            var other = new MoonSurface(seed: 8, craterCount: 110, seaCount: 7);
            for (float u = 0f; u < 1f; u += 0.05f)
                differs |= !Mathf.Approximately(Moon().Height(MoonSurface.DirectionAt(u, 0.5f)),
                                                other.Height(MoonSurface.DirectionAt(u, 0.5f)));
            Assert.That(differs, Is.True);
        }

        [Test]
        public void Wrap_HasNoSeam()
        {
            var moon = Moon();
            for (float v = 0.05f; v < 1f; v += 0.05f)
            {
                Vector3 left = MoonSurface.DirectionAt(0f, v), right = MoonSurface.DirectionAt(1f, v);
                Assert.That(moon.Height(left), Is.EqualTo(moon.Height(right)).Within(1e-3f), $"height at v={v}");
                Assert.That(moon.Albedo(left), Is.EqualTo(moon.Albedo(right)).Within(1e-3f), $"brightness at v={v}");
            }
        }

        [Test]
        public void Normals_AreUnitLength_AndNeverFaceInward()
        {
            var moon = Moon();
            for (float u = 0.02f; u < 1f; u += 0.09f)
            for (float v = 0.02f; v < 1f; v += 0.09f)
            {
                Vector3 d = MoonSurface.DirectionAt(u, v);
                Vector3 n = moon.Normal(d, 1.5f);
                Assert.That(n.magnitude, Is.EqualTo(1f).Within(1e-4f));
                Assert.That(Vector3.Dot(n, d), Is.GreaterThan(0f), $"at {u},{v}");
            }
        }

        [Test]
        public void Surface_HasBothDentsAndRims_AndBrightnessStaysInRange()
        {
            var moon = Moon();
            float lowest = float.MaxValue, highest = float.MinValue;
            for (float u = 0f; u < 1f; u += 0.004f)
            for (float v = 0.02f; v < 1f; v += 0.008f)
            {
                Vector3 d = MoonSurface.DirectionAt(u, v);
                float h = moon.Height(d);
                lowest = Mathf.Min(lowest, h);
                highest = Mathf.Max(highest, h);
                float albedo = moon.Albedo(d);
                Assert.That(albedo, Is.InRange(0f, 1f));
            }
            Assert.That(lowest, Is.LessThan(-0.01f));
            Assert.That(highest, Is.GreaterThan(0.001f));
        }

        [Test]
        public void NoFeatures_IsASmoothBall()
        {
            var bare = new MoonSurface(seed: 1, craterCount: 0, seaCount: 0);
            Vector3 d = MoonSurface.DirectionAt(0.4f, 0.6f);
            Assert.That(bare.Height(d), Is.EqualTo(0f));
            Assert.That(Vector3.Dot(bare.Normal(d, 1.5f), d), Is.EqualTo(1f).Within(1e-5f));
        }
    }
}
