using NUnit.Framework;
using Pickleball.Sandbox.Combat.Presentation;

namespace Pickleball.Tests
{
    /// <summary>
    /// The rules that decide when a flagged swing plays full-body:
    ///  - it engages only at swing start, grounded and standing;
    ///  - it lets go for the rest of the swing when he moves, leaves the ground, or the
    ///    swing ends;
    ///  - its weight fades over the given times and never pops.
    /// </summary>
    public class FullBodySwingTests
    {
        const float Standing = 0.12f;
        const float Blend = 0.1f;
        const float Release = 0.15f;
        const float Dt = 1f / 60f;

        static FullBodySwing Engaged()
        {
            var body = new FullBodySwing();
            Assert.IsTrue(body.Begin(true, grounded: true, speed: 0f, Standing, Blend, Release));
            for (int i = 0; i < 12; i++) body.Tick(Dt, swinging: true, grounded: true, speed: 0f, Standing, Release);
            Assert.AreEqual(1f, body.Weight, 1e-4f, "should be fully in after its blend");
            return body;
        }

        [Test]
        public void Begin_FlaggedStandingGrounded_Engages()
        {
            var body = new FullBodySwing();
            Assert.IsTrue(body.Begin(true, true, 0.05f, Standing, Blend, Release));
            Assert.IsTrue(body.Engaged);
        }

        [Test]
        public void Begin_AtExactlyTheThreshold_CountsAsStanding()
        {
            Assert.IsTrue(new FullBodySwing().Begin(true, true, Standing, Standing, Blend, Release));
        }

        [Test]
        public void Begin_NotFlagged_StaysMasked()
        {
            Assert.IsFalse(new FullBodySwing().Begin(false, true, 0f, Standing, Blend, Release));
        }

        [Test]
        public void Begin_Moving_StaysMasked()
        {
            Assert.IsFalse(new FullBodySwing().Begin(true, true, 2.5f, Standing, Blend, Release));
        }

        [Test]
        public void Begin_Airborne_StaysMasked()
        {
            Assert.IsFalse(new FullBodySwing().Begin(true, false, 0f, Standing, Blend, Release));
        }

        [Test]
        public void FadesIn_OverTheBlend_NotAtOnce()
        {
            var body = new FullBodySwing();
            body.Begin(true, true, 0f, Standing, Blend, Release);
            Assert.IsTrue(body.IsHidden, "invisible before the first tick, so the state can be switched outright");
            body.Tick(Dt, true, true, 0f, Standing, Release);
            Assert.AreEqual(Dt / Blend, body.Weight, 1e-4f);
            Assert.IsFalse(body.IsHidden);
        }

        [Test]
        public void StartsMovingMidSwing_LetsGoAndFadesOverTheRelease()
        {
            var body = Engaged();
            body.Tick(Dt, true, true, speed: 1.5f, Standing, Release);
            Assert.IsFalse(body.Engaged);
            Assert.AreEqual(1f - Dt / Release, body.Weight, 1e-4f, "one frame of the release fade, not a pop");
            for (int i = 0; i < 20; i++) body.Tick(Dt, true, true, 1.5f, Standing, Release);
            Assert.AreEqual(0f, body.Weight);
        }

        [Test]
        public void LetGo_NeverReengagesInTheSameSwing()
        {
            var body = Engaged();
            body.Tick(Dt, true, true, 1.5f, Standing, Release);    // moves
            body.Tick(Dt, true, true, 0f, Standing, Release);      // stops again
            Assert.IsFalse(body.Engaged, "a speed hovering at the threshold must not flicker the legs");
            Assert.Less(body.Weight, 1f);
        }

        [Test]
        public void LeavesTheGround_LetsGo()
        {
            var body = Engaged();
            body.Tick(Dt, true, grounded: false, 0f, Standing, Release);
            Assert.IsFalse(body.Engaged);
        }

        [Test]
        public void SwingEnds_LetsGo()
        {
            var body = Engaged();
            body.Tick(Dt, swinging: false, true, 0f, Standing, Release);
            Assert.IsFalse(body.Engaged);
            Assert.Less(body.Weight, 1f);
        }

        [Test]
        public void NextSwingMasked_FadesOutOverTheRelease()
        {
            var body = Engaged();
            Assert.IsFalse(body.Begin(false, true, 0f, Standing, 0.1f, Release));
            body.Tick(Dt, true, true, 0f, Standing, Release);
            Assert.AreEqual(1f - Dt / Release, body.Weight, 1e-4f,
                "the legs leave a lunge over the release time, not the 0.1 s upper-body blend");
        }

        [Test]
        public void NextSwingAlsoFullBody_StaysIn()
        {
            var body = Engaged();
            Assert.IsTrue(body.Begin(true, true, 0f, Standing, Blend, Release));
            Assert.IsFalse(body.IsHidden, "visible, so the driver crossfades the state rather than switching it");
            body.Tick(Dt, true, true, 0f, Standing, Release);
            Assert.AreEqual(1f, body.Weight);
        }

        [Test]
        public void ZeroBlend_SwitchesInOneFrame()
        {
            var body = new FullBodySwing();
            body.Begin(true, true, 0f, Standing, 0f, Release);
            body.Tick(Dt, true, true, 0f, Standing, Release);
            Assert.AreEqual(1f, body.Weight);
        }

        [Test]
        public void ZeroDeltaTime_HoldsTheWeight()
        {
            var body = new FullBodySwing();
            body.Begin(true, true, 0f, Standing, 0f, Release);
            body.Tick(0f, true, true, 0f, Standing, Release);
            Assert.AreEqual(1f, body.Weight, "an instant switch is still instant");
            body = Engaged();
            body.Tick(Dt, true, true, 1.5f, Standing, Release);
            float w = body.Weight;
            body.Tick(0f, true, true, 1.5f, Standing, Release);
            Assert.AreEqual(w, body.Weight, "a paused frame does not move the fade");
        }

        [Test]
        public void Reset_DropsToZero()
        {
            var body = Engaged();
            body.Reset();
            Assert.IsFalse(body.Engaged);
            Assert.AreEqual(0f, body.Weight);
            Assert.IsTrue(body.IsHidden);
        }
    }
}
