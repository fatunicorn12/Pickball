using NUnit.Framework;
using Pickleball.Sandbox;

namespace Pickleball.Tests
{
    /// <summary>
    /// Both halves of Newton's risk/reward run on the same 1/r² law: his volley's
    /// damage to you, and your damage to him. These pin that the two really do
    /// rise as you close in and fall away as you back off.
    /// </summary>
    public class InverseSquareTests
    {
        const float Tolerance = 1e-3f;

        [Test]
        public void Volley_DoesBaseDamageAtTheReferenceDistance()
        {
            Assert.That(InverseSquareVolley.DamageAt(4f, 8f, 4f, 1f, 30f),
                        Is.EqualTo(8f).Within(Tolerance));
        }

        [Test]
        public void Volley_HalvingTheDistanceQuadruplesTheDamage()
        {
            Assert.That(InverseSquareVolley.DamageAt(2f, 8f, 4f, 0f, 1000f),
                        Is.EqualTo(32f).Within(Tolerance));
            Assert.That(InverseSquareVolley.DamageAt(8f, 8f, 4f, 0f, 1000f),
                        Is.EqualTo(2f).Within(Tolerance));
        }

        [Test]
        public void Volley_IsClampedAtBothEnds()
        {
            Assert.That(InverseSquareVolley.DamageAt(0f, 8f, 4f, 1f, 30f), Is.EqualTo(30f).Within(Tolerance));
            Assert.That(InverseSquareVolley.DamageAt(100f, 8f, 4f, 1f, 30f), Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void WeakPoint_IsNeutralAtTheReferenceRange()
        {
            Assert.That(NewtonBoss.RangeMultiplier(4.5f, 4.5f, 0.25f, 2f), Is.EqualTo(1f).Within(Tolerance));
        }

        [Test]
        public void WeakPoint_RewardsClosingIn_UpToTheCap()
        {
            Assert.That(NewtonBoss.RangeMultiplier(3.5f, 4.5f, 0.25f, 2f), Is.GreaterThan(1f));
            Assert.That(NewtonBoss.RangeMultiplier(0.5f, 4.5f, 0.25f, 2f), Is.EqualTo(2f).Within(Tolerance));
        }

        [Test]
        public void WeakPoint_FallsOffAtRange_DownToTheFloor()
        {
            Assert.That(NewtonBoss.RangeMultiplier(6f, 4.5f, 0.25f, 2f), Is.LessThan(1f));
            Assert.That(NewtonBoss.RangeMultiplier(40f, 4.5f, 0.25f, 2f), Is.EqualTo(0.25f).Within(Tolerance));
        }
    }
}
