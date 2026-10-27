using NUnit.Framework;
using Pickleball.Sandbox.Combat.Progression;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// The XP curve and its inverse. The shipped slice leans on the triangular
    /// shape (50 / 150 / 300 / 500 to reach levels 2-5) so the first four
    /// abilities arrive inside one Newton fight; the properties below are the
    /// ones any retune must keep.
    /// </summary>
    public class ExperienceCurveTests
    {
        [Test]
        public void Triangular_HitsTheDocumentedThresholds()
        {
            var curve = new ExperienceCurve(20, 50f, 1f);
            Assert.That(curve.TotalXpFor(1), Is.EqualTo(0));
            Assert.That(curve.TotalXpFor(2), Is.EqualTo(50));
            Assert.That(curve.TotalXpFor(3), Is.EqualTo(150));
            Assert.That(curve.TotalXpFor(4), Is.EqualTo(300));
            Assert.That(curve.TotalXpFor(5), Is.EqualTo(500));
        }

        [TestCase(1f)]
        [TestCase(1.5f)]
        [TestCase(0f)]
        public void TotalXp_StrictlyIncreasesUpToTheCap(float growth)
        {
            var curve = new ExperienceCurve(30, 40f, growth);
            for (int l = 1; l < curve.MaxLevel; l++)
                Assert.That(curve.TotalXpFor(l + 1), Is.GreaterThan(curve.TotalXpFor(l)), $"level {l}");
        }

        [Test]
        public void LevelFor_IsTheInverseOfTotalXpFor()
        {
            var curve = new ExperienceCurve(25, 50f, 1.3f);
            for (int l = 1; l <= curve.MaxLevel; l++)
            {
                int total = curve.TotalXpFor(l);
                Assert.That(curve.LevelFor(total), Is.EqualTo(l), $"exactly at level {l}");
                if (l > 1) Assert.That(curve.LevelFor(total - 1), Is.EqualTo(l - 1), $"one short of {l}");
            }
        }

        [Test]
        public void LevelFor_ClampsBelowOneAndAboveTheCap()
        {
            var curve = new ExperienceCurve(5, 50f, 1f);
            Assert.That(curve.LevelFor(-100), Is.EqualTo(1));
            Assert.That(curve.LevelFor(int.MaxValue / 2), Is.EqualTo(5));
            Assert.That(curve.XpToNext(5), Is.EqualTo(0), "nothing to earn at the cap");
            Assert.That(curve.TotalXpFor(99), Is.EqualTo(curve.TotalXpFor(5)));
        }

        [Test]
        public void XpToNext_IsNeverZeroBelowTheCap()
        {
            var curve = new ExperienceCurve(10, 1f, 0f);
            for (int l = 1; l < 10; l++) Assert.That(curve.XpToNext(l), Is.GreaterThanOrEqualTo(1));
        }
    }
}
