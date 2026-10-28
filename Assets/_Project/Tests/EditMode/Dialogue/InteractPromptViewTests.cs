using NUnit.Framework;
using Pickleball.UI;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// One marker at a time: over an NPC that lock-on can target, the talk pill
    /// steps clear of the hint ring above its head, to the right, or to the left
    /// near the right edge of the screen.
    /// </summary>
    public class InteractPromptViewTests
    {
        const float Width = 1920f, Height = 1080f, HalfPill = 70f;

        [Test]
        public void Shift_ClearsTheWidestHintRing_ToTheRight()
        {
            float shift = InteractPromptView.HintClearanceShift(900f, Width, Height, 0.1f, HalfPill);

            // The hint's spinning quad reaches about 0.085 screen heights either side of its centre.
            float ringEdge = 0.085f * Height;
            Assert.That(shift, Is.GreaterThan(0f));
            Assert.That(shift - HalfPill, Is.GreaterThan(ringEdge), "the pill's near edge is past the ring");
        }

        [Test]
        public void Shift_NearTheRightEdge_GoesLeftInstead()
        {
            float shift = InteractPromptView.HintClearanceShift(Width - 100f, Width, Height, 0.1f, HalfPill);

            Assert.That(shift, Is.LessThan(0f));
            Assert.That(-shift - HalfPill, Is.GreaterThan(0.085f * Height));
        }

        [Test]
        public void Shift_OfZeroClearance_LeavesThePillCentred()
        {
            Assert.That(InteractPromptView.HintClearanceShift(900f, Width, Height, 0f, HalfPill), Is.EqualTo(0f));
        }
    }
}
