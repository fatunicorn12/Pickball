using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// When the player is free to talk: the prompt may linger through a short
    /// airborne grace, but a press only starts a talk while really grounded, and
    /// never while dashing, tethered, skating, swinging or in 4D.
    /// </summary>
    public class TalkEligibilityTests
    {
        const float Grace = 0.15f;

        static bool Offer(bool grounded = true, float airborne = 0f, bool busy = false,
                          bool attacking = false, bool in4D = false, bool requireFree = true)
            => TalkEligibility.MayOffer(requireFree, grounded, airborne, Grace, busy, attacking, in4D);

        static bool Start(bool grounded = true, bool busy = false, bool attacking = false,
                          bool in4D = false, bool requireFree = true)
            => TalkEligibility.MayStart(requireFree, grounded, busy, attacking, in4D);

        [Test]
        public void GroundedAndIdle_OffersAndStarts()
        {
            Assert.That(Offer(), Is.True);
            Assert.That(Start(), Is.True);
        }

        [Test]
        public void Airborne_InsideTheGrace_OffersButCannotStart()
        {
            // The first fraction of a second of a jump: the prompt may stay up,
            // but the press must not freeze him in mid-air.
            Assert.That(Offer(grounded: false, airborne: 0.1f), Is.True);
            Assert.That(Start(grounded: false), Is.False);
        }

        [Test]
        public void Airborne_PastTheGrace_NeitherOffersNorStarts()
        {
            Assert.That(Offer(grounded: false, airborne: 0.2f), Is.False);
            Assert.That(Start(grounded: false), Is.False);
        }

        [TestCase(true, false, false, TestName = "Busy_DashTetherOrSkate_Refused")]
        [TestCase(false, true, false, TestName = "Attacking_Refused")]
        [TestCase(false, false, true, TestName = "In4D_Refused")]
        public void NotFree_NeitherOffersNorStarts(bool busy, bool attacking, bool in4D)
        {
            Assert.That(Offer(busy: busy, attacking: attacking, in4D: in4D), Is.False);
            Assert.That(Start(busy: busy, attacking: attacking, in4D: in4D), Is.False);
        }

        [Test]
        public void RuleSwitchedOff_AllowsEverything()
        {
            Assert.That(Offer(grounded: false, airborne: 5f, busy: true, attacking: true, in4D: true,
                              requireFree: false), Is.True);
            Assert.That(Start(grounded: false, busy: true, attacking: true, in4D: true,
                              requireFree: false), Is.True);
        }
    }
}
