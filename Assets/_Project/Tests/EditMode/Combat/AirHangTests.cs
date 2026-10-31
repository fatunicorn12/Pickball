using NUnit.Framework;
using Pickleball.Sandbox.Combat;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// Air-combo hang time belongs to the swing, as in Kingdom Hearts II and
    /// Castle Crashers: every air swing that hangs holds the player up through
    /// its own hover window, hit or miss; outside the window, when the chain
    /// ends, on landing or during a dash he falls. <see cref="AirHang.JustOpened"/>
    /// marks the frame a hold begins, when the fall is caught.
    /// </summary>
    public class AirHangTests
    {
        const float Frame = 1f / 60f;

        /// <summary>One frame of an air swing <paramref name="progress"/> through,
        /// with the default whole-swing window.</summary>
        static float Swing(AirHang hang, float progress, float start = 0f, float end = 1f)
            => hang.Tick(true, progress, start, end, false, false, Frame);

        [Test]
        public void AirSwing_Holds_WithoutAnyHit()
        {
            // The old rule (only landed hits hold) dropped him on every whiff.
            var hang = new AirHang();
            Assert.That(Swing(hang, 0f), Is.GreaterThan(0f));
            Assert.That(hang.IsHolding, Is.True);
        }

        [Test]
        public void HoldsOnlyInsideTheHoverWindow()
        {
            var hang = new AirHang();
            Assert.That(Swing(hang, 0.1f, 0.2f, 0.7f), Is.Zero, "before the window");
            Assert.That(Swing(hang, 0.2f, 0.2f, 0.7f), Is.GreaterThan(0f));
            Assert.That(Swing(hang, 0.7f, 0.2f, 0.7f), Is.GreaterThan(0f), "the end is inclusive");
            Assert.That(Swing(hang, 0.71f, 0.2f, 0.7f), Is.Zero, "gravity returns after it");
        }

        [Test]
        public void EmptyWindow_NeverHolds()
        {
            Assert.That(AirHang.InWindow(0.5f, 0.6f, 0.6f), Is.False);
            Assert.That(AirHang.InWindow(0.5f, 0.8f, 0.2f), Is.False);
        }

        [Test]
        public void JustOpened_OnTheFirstHeldFrameOnly()
        {
            var hang = new AirHang();
            Swing(hang, 0f);
            Assert.That(hang.JustOpened, Is.True);
            Swing(hang, 0.1f);
            Assert.That(hang.JustOpened, Is.False);
        }

        [Test]
        public void ChainingHoverIntoHover_DoesNotReopen()
        {
            // Swing 1 ends inside its window and swing 2 starts inside its own:
            // one continuous hang, no second fall-catch.
            var hang = new AirHang();
            Swing(hang, 0.95f);
            Swing(hang, 0f);
            Assert.That(hang.IsHolding, Is.True);
            Assert.That(hang.JustOpened, Is.False);
        }

        [Test]
        public void ReopeningAfterAGap_CountsAsOpening()
        {
            var hang = new AirHang();
            Swing(hang, 0.5f, 0f, 0.6f);
            Swing(hang, 0.9f, 0f, 0.6f);      // sinking through the follow-through
            Swing(hang, 0f, 0f, 0.6f);        // next swing
            Assert.That(hang.JustOpened, Is.True);
        }

        [Test]
        public void ChainEnding_DropsAtOnce()
        {
            var hang = new AirHang();
            Swing(hang, 0.3f);
            Assert.That(hang.Tick(false, 0.3f, 0f, 1f, false, false, Frame), Is.Zero);
            Assert.That(hang.IsHolding, Is.False);
        }

        [Test]
        public void Landing_Drops()
        {
            var hang = new AirHang();
            Swing(hang, 0.3f);
            Assert.That(hang.Tick(true, 0.3f, 0f, 1f, true, false, Frame), Is.Zero);
            Assert.That(hang.IsHolding, Is.False);
        }

        [Test]
        public void DashOrTether_Drops()
        {
            var hang = new AirHang();
            Swing(hang, 0.3f);
            Assert.That(hang.Tick(true, 0.3f, 0f, 1f, false, true, Frame), Is.Zero);
            Assert.That(hang.IsHolding, Is.False);
        }

        [Test]
        public void Hitstop_KeepsHolding()
        {
            // A frozen swing's progress doesn't move, so it stays in its window.
            var hang = new AirHang();
            for (int i = 0; i < 10; i++) Assert.That(Swing(hang, 0.4f), Is.GreaterThan(0f));
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 144f)]
        public void Hold_CoversTwoFrames(float dt)
        {
            // The motor may tick its countdown before this frame's refresh lands;
            // a shorter hold would flicker gravity on mid-hang.
            var hang = new AirHang();
            Assert.That(hang.Tick(true, 0.5f, 0f, 1f, false, false, dt), Is.EqualTo(2f * dt).Within(1e-6f));
        }

        [Test]
        public void Cancel_ClearsTheHold()
        {
            var hang = new AirHang();
            Swing(hang, 0.5f);
            hang.Cancel();
            Assert.That(hang.IsHolding, Is.False);
            Assert.That(hang.JustOpened, Is.False);
        }
    }
}
