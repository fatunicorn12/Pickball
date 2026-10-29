using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia;

namespace Pickleball.Tests.Hypatia.Brain
{
    /// <summary>
    /// Which conic she fires and what the needle reads. The needle is the
    /// player's only advance warning of the shape, so e must always name the
    /// right conic; and the no-repeat rule is what keeps consecutive volleys
    /// teaching something new.
    /// </summary>
    public class ConicPickerTests
    {
        const int Draws = 6000;

        [Test]
        public void ZeroWeightKinds_AreNeverPicked()
        {
            var picker = new ConicPicker(new System.Random(1));
            for (int i = 0; i < 500; i++)
            {
                Assert.IsTrue(picker.TryPick(0f, 1f, 0f, noRepeat: false, out ConicKind kind));
                Assert.That(kind, Is.EqualTo(ConicKind.Ellipse));
            }
        }

        [Test]
        public void AllWeightsZero_PicksNothing()
        {
            var picker = new ConicPicker(new System.Random(2));
            Assert.IsFalse(picker.TryPick(0f, 0f, 0f, noRepeat: true, out _));
        }

        [Test]
        public void NegativeAndNaNWeights_CountAsZero()
        {
            var picker = new ConicPicker(new System.Random(3));
            for (int i = 0; i < 300; i++)
            {
                Assert.IsTrue(picker.TryPick(-5f, float.NaN, 2f, noRepeat: false, out ConicKind kind));
                Assert.That(kind, Is.EqualTo(ConicKind.Hyperbola));
            }
        }

        [Test]
        public void NoRepeat_NeverPicksTheSameKindTwiceInARow()
        {
            var picker = new ConicPicker(new System.Random(4));
            Assert.IsTrue(picker.TryPick(1f, 1f, 1f, noRepeat: true, out ConicKind previous));
            for (int i = 0; i < 2000; i++)
            {
                Assert.IsTrue(picker.TryPick(1f, 1f, 1f, noRepeat: true, out ConicKind kind));
                Assert.That(kind, Is.Not.EqualTo(previous), $"repeat at draw {i}");
                previous = kind;
            }
        }

        [Test]
        public void NoRepeat_WithTwoKinds_Alternates()
        {
            var picker = new ConicPicker(new System.Random(5));
            Assert.IsTrue(picker.TryPick(1f, 0f, 3f, noRepeat: true, out ConicKind previous));
            for (int i = 0; i < 200; i++)
            {
                picker.TryPick(1f, 0f, 3f, noRepeat: true, out ConicKind kind);
                Assert.That(kind, Is.Not.EqualTo(previous));
                Assert.That(kind, Is.Not.EqualTo(ConicKind.Ellipse));
                previous = kind;
            }
        }

        [Test]
        public void NoRepeat_WithOnlyOneWeightedKind_RepeatsRatherThanGoingSilent()
        {
            var picker = new ConicPicker(new System.Random(6));
            for (int i = 0; i < 50; i++)
            {
                Assert.IsTrue(picker.TryPick(2f, 0f, 0f, noRepeat: true, out ConicKind kind));
                Assert.That(kind, Is.EqualTo(ConicKind.Parabola));
            }
        }

        [Test]
        public void Distribution_FollowsTheWeights()
        {
            var picker = new ConicPicker(new System.Random(7));
            var counts = new int[3];
            for (int i = 0; i < Draws; i++)
            {
                Assert.IsTrue(picker.TryPick(1f, 2f, 3f, noRepeat: false, out ConicKind kind));
                counts[(int)kind]++;
            }

            // Expected 1/6, 2/6, 3/6. Six thousand draws keep each within a few
            // percent; the tolerance is wide enough that a seed never flakes.
            Assert.That(counts[(int)ConicKind.Parabola] / (float)Draws, Is.EqualTo(1f / 6f).Within(0.03f));
            Assert.That(counts[(int)ConicKind.Ellipse] / (float)Draws, Is.EqualTo(2f / 6f).Within(0.03f));
            Assert.That(counts[(int)ConicKind.Hyperbola] / (float)Draws, Is.EqualTo(3f / 6f).Within(0.03f));
        }

        [Test]
        public void TryGetLast_ReportsThePreviousPick_UntilReset()
        {
            var picker = new ConicPicker(new System.Random(8));
            Assert.IsFalse(picker.TryGetLast(out _));

            picker.TryPick(0f, 0f, 1f, noRepeat: false, out _);
            Assert.IsTrue(picker.TryGetLast(out ConicKind last));
            Assert.That(last, Is.EqualTo(ConicKind.Hyperbola));

            picker.Reset();
            Assert.IsFalse(picker.TryGetLast(out _));
        }

        // ------------------------------------------------------- eccentricity

        [Test]
        public void Parabola_IsExactlyOne()
        {
            var random = new System.Random(9);
            for (int i = 0; i < 100; i++)
                Assert.That(ConicPicker.PickEccentricity(ConicKind.Parabola, 0.2f, 0.8f, 1.2f, 3f, random),
                            Is.EqualTo(1f));
        }

        [Test]
        public void Ellipse_IsUniformInItsRange_AndBelowOne()
        {
            var random = new System.Random(10);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 2000; i++)
            {
                float e = ConicPicker.PickEccentricity(ConicKind.Ellipse, 0.35f, 0.75f, 1.3f, 2f, random);
                Assert.That(e, Is.InRange(0.35f, 0.75f));
                lo = System.Math.Min(lo, e);
                hi = System.Math.Max(hi, e);
            }
            // Spans the range, not stuck at one end.
            Assert.That(lo, Is.LessThan(0.40f));
            Assert.That(hi, Is.GreaterThan(0.70f));
        }

        [Test]
        public void Hyperbola_IsUniformInItsRange_AndAboveOne()
        {
            var random = new System.Random(11);
            float lo = float.MaxValue, hi = float.MinValue;
            for (int i = 0; i < 2000; i++)
            {
                float e = ConicPicker.PickEccentricity(ConicKind.Hyperbola, 0.35f, 0.75f, 1.3f, 2f, random);
                Assert.That(e, Is.InRange(1.3f, 2f));
                lo = System.Math.Min(lo, e);
                hi = System.Math.Max(hi, e);
            }
            Assert.That(lo, Is.LessThan(1.4f));
            Assert.That(hi, Is.GreaterThan(1.9f));
        }

        [Test]
        public void OutOfShapeRanges_StillNameTheRightConic()
        {
            var random = new System.Random(12);
            for (int i = 0; i < 200; i++)
            {
                float ellipse = ConicPicker.PickEccentricity(ConicKind.Ellipse, 0.9f, 1.4f, 0.5f, 0.8f, random);
                float hyperbola = ConicPicker.PickEccentricity(ConicKind.Hyperbola, 0.9f, 1.4f, 0.5f, 0.8f, random);
                Assert.That(ellipse, Is.LessThan(1f));
                Assert.That(hyperbola, Is.GreaterThan(1f));
            }
        }

        [Test]
        public void ReversedBounds_AreSwapped()
        {
            var random = new System.Random(13);
            for (int i = 0; i < 200; i++)
                Assert.That(ConicPicker.PickEccentricity(ConicKind.Hyperbola, 0f, 0f, 2f, 1.5f, random),
                            Is.InRange(1.5f, 2f));
        }
    }
}
