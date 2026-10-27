using NUnit.Framework;
using Pickleball.Sandbox.Combat;
using UnityEngine;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// The finisher ring: grows on an ease-out to its radius, strikes inside a
    /// height band (so it can be jumped), and its visual shares the growth
    /// curve with its damage.
    /// </summary>
    public class AreaBurstTests
    {
        const float Tolerance = 1e-4f;
        static readonly HitSettings Hit = new(12f, 6f, 2f, 0.08f);

        [Test]
        public void RadiusAt_GrowsFromZeroToFullAndEasesOut()
        {
            var b = new AreaBurst(0.6f, 3f, 0.4f, Hit);
            Assert.That(b.RadiusAt(0f), Is.EqualTo(0f).Within(Tolerance));
            Assert.That(b.RadiusAt(0.4f), Is.EqualTo(3f).Within(Tolerance));
            Assert.That(b.RadiusAt(1f), Is.EqualTo(3f).Within(Tolerance));
            Assert.That(b.RadiusAt(0.2f), Is.GreaterThan(1.5f), "ease-out: past half radius at half time");
        }

        [Test]
        public void RadiusAt_ZeroExpand_IsInstant()
        {
            var b = new AreaBurst(0.5f, 1.8f, 0f, Hit);
            Assert.That(b.RadiusAt(0f), Is.EqualTo(1.8f).Within(Tolerance));
            Assert.That(b.IsFinished(0f), Is.True);
        }

        [Test]
        public void Reaches_InsideRadiusAndBand_OutsideEither()
        {
            var b = new AreaBurst(0.5f, 3f, 0.3f, Hit, heightTolerance: 0.5f);
            var c = Vector3.zero;
            Assert.That(b.Reaches(c, new Vector3(2f, 0.2f, 0f), 2.5f), Is.True);
            Assert.That(b.Reaches(c, new Vector3(2f, 0.2f, 1.6f), 2.5f), Is.False, "outside radius");
            Assert.That(b.Reaches(c, new Vector3(1f, 0.8f, 0f), 2.5f), Is.False, "jumped over it");
        }

        [Test]
        public void CentreFor_OffsetsAlongFlatFacing()
        {
            var b = new AreaBurst(0.5f, 3f, 0.3f, Hit, forwardOffset: 0.8f);
            var centre = b.CentreFor(new Vector3(1f, 0f, 1f), new Vector3(0f, 0.5f, 2f));
            Assert.That(centre.x, Is.EqualTo(1f).Within(Tolerance));
            Assert.That(centre.y, Is.EqualTo(0f).Within(Tolerance));
            Assert.That(centre.z, Is.EqualTo(1.8f).Within(Tolerance));
        }

        [Test]
        public void HitSettings_PushAlong_IsHorizontalKnockbackPlusLaunch()
        {
            var push = Hit.PushAlong(new Vector3(3f, 5f, 4f));
            Assert.That(push.x, Is.EqualTo(6f * 0.6f).Within(Tolerance));
            Assert.That(push.z, Is.EqualTo(6f * 0.8f).Within(Tolerance));
            Assert.That(push.y, Is.EqualTo(2f).Within(Tolerance));
            Assert.That(Hit.IsLauncher, Is.True);
        }
    }
}
