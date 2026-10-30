using NUnit.Framework;
using Pickleball.Sandbox.Combat;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// Air-combo hang time: an air swing holds the player up, and the hold
    /// lapses on its own once the chain ends because it is only ever refreshed.
    /// </summary>
    public class AirHangTests
    {
        const float Frame = 1f / 60f;
        const float Release = 0.08f;

        [Test]
        public void AirSwing_HoldsForTheRelease()
        {
            float hold = AirHang.HoldSeconds(true, true, false, false, Release, Frame);
            Assert.That(hold, Is.EqualTo(Release).Within(1e-6f));
        }

        [Test]
        public void NoAirSwing_DoesNotHold()
        {
            Assert.That(AirHang.HoldSeconds(false, true, false, false, Release, Frame), Is.Zero);
        }

        [Test]
        public void StepThatOptsOut_Falls()
        {
            Assert.That(AirHang.HoldSeconds(true, false, false, false, Release, Frame), Is.Zero);
        }

        [Test]
        public void Grounded_DoesNotHold()
        {
            Assert.That(AirHang.HoldSeconds(true, true, true, false, Release, Frame), Is.Zero);
        }

        [Test]
        public void DashOrTether_EndsTheHang()
        {
            Assert.That(AirHang.HoldSeconds(true, true, false, true, Release, Frame), Is.Zero);
        }

        [TestCase(0f, 1f / 30f)]
        [TestCase(0f, 1f / 144f)]
        [TestCase(0.01f, 1f / 30f)]
        public void ShortRelease_StillCoversTwoFrames(float release, float dt)
        {
            // The motor may tick its countdown before this frame's refresh lands;
            // a hold shorter than that would flicker gravity on mid-combo.
            float hold = AirHang.HoldSeconds(true, true, false, false, release, dt);
            Assert.That(hold, Is.GreaterThanOrEqualTo(2f * dt - 1e-6f));
        }
    }
}
