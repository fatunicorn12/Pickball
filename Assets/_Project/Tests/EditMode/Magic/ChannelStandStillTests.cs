using NUnit.Framework;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// "Stand still" for the Heal channel means it (QA F-4): being carried more
    /// than the drift limit from where the channel began cancels it whatever the
    /// cause (<see cref="ChannelStopReason.Displaced"/>), and damage interrupts
    /// by one rule, <see cref="SpellChannel.HitInterrupts"/>: a hit of at least
    /// the threshold does, a smaller tick (a gravity well's core) does not.
    /// </summary>
    public class ChannelStandStillTests : MagicTestFixture
    {
        const float Duration = 2f;
        const int Cost = 40;
        const float Drift = 0.4f;

        static ChannelSignals StillAt(Vector3 at) => new() { Held = true, Position = at };

        static readonly Vector3 Start = new(10f, 2f, -4f);

        SpellChannel Begun(Mana mana = null, int cost = 0, float maxDrift = Drift)
        {
            var channel = new SpellChannel();
            Assert.That(channel.TryBegin(StillAt(Start), Duration, cost, ChannelManaTiming.OnCompletion, mana, maxDrift),
                        Is.EqualTo(ChannelStopReason.None));
            return channel;
        }

        // ------------------------------------------------------- displacement

        [Test]
        public void CarriedWithinTheLimit_KeepsChanneling()
        {
            var channel = Begun();
            Assert.That(channel.Tick(0.1f, StillAt(Start + new Vector3(0.39f, 0f, 0f)), null),
                        Is.EqualTo(ChannelStep.Channeling));
            Assert.That(channel.Origin, Is.EqualTo(Start));
        }

        [Test]
        public void CarriedPastTheLimit_CancelsAsDisplaced()
        {
            var channel = Begun();
            channel.Tick(0.1f, StillAt(Start + new Vector3(0.3f, 0f, 0f)), null);
            // 0.3, 0.3 is 0.42 m from the start: a gravity well's pull, no input.
            Assert.That(channel.Tick(0.1f, StillAt(Start + new Vector3(0.3f, 0f, 0.3f)), null),
                        Is.EqualTo(ChannelStep.Cancelled));
            Assert.That(channel.LastStop, Is.EqualTo(ChannelStopReason.Displaced));
            Assert.That(channel.IsChanneling, Is.False);
        }

        [Test]
        public void Displaced_PaysNothing()
        {
            var mana = NewMana();
            var channel = Begun(mana, Cost);
            channel.Tick(1.9f, StillAt(Start + Vector3.forward), mana);
            Assert.That(channel.LastStop, Is.EqualTo(ChannelStopReason.Displaced));
            Assert.That(mana.Current, Is.EqualTo(mana.Max), "paid on completion, so a cancel is free");
        }

        [Test]
        public void Height_IsNotDrift()
        {
            // Rising or settling in place (a step, ground snap) is not being moved.
            var channel = Begun();
            Assert.That(channel.Tick(0.1f, StillAt(Start + Vector3.up * 1.5f), null), Is.EqualTo(ChannelStep.Channeling));
        }

        [Test]
        public void NoLimit_NeverDisplaced()
        {
            var channel = Begun(maxDrift: 0f);
            Assert.That(channel.Tick(0.1f, StillAt(Start + Vector3.right * 10f), null), Is.EqualTo(ChannelStep.Channeling));
        }

        [Test]
        public void MovementInput_OutranksDisplacement()
        {
            var channel = Begun();
            var walked = StillAt(Start + Vector3.right);
            walked.Moving = true;
            channel.Tick(0.1f, walked, null);
            Assert.That(channel.LastStop, Is.EqualTo(ChannelStopReason.Moved), "the player walked: say so");
        }

        [Test]
        public void Displacement_OutranksLettingGo()
        {
            var channel = Begun();
            var dragged = StillAt(Start + Vector3.right);
            dragged.Held = false;
            channel.Tick(0.1f, dragged, null);
            Assert.That(channel.LastStop, Is.EqualTo(ChannelStopReason.Displaced));
        }

        [Test]
        public void Drifted_MeasuresAcrossTheGround()
        {
            Assert.That(SpellChannel.Drifted(Vector3.zero, new Vector3(0.4f, 0f, 0f), 0.4f), Is.False, "the limit itself is in");
            Assert.That(SpellChannel.Drifted(Vector3.zero, new Vector3(0.3f, 9f, 0.3f), 0.4f), Is.True);
            Assert.That(SpellChannel.Drifted(Vector3.zero, new Vector3(5f, 0f, 0f), -1f), Is.False, "no limit");
        }

        // --------------------------------------------------- damage threshold

        [TestCase(0.1f, 1f, false, TestName = "WellCoreTick_DoesNotInterrupt")]
        [TestCase(0.99f, 1f, false, TestName = "JustUnder_DoesNotInterrupt")]
        [TestCase(1f, 1f, true, TestName = "AtTheThreshold_Interrupts")]
        [TestCase(3f, 1f, true, TestName = "RealHit_Interrupts")]
        [TestCase(0.1f, 0f, true, TestName = "ZeroThreshold_AnyDamageInterrupts")]
        [TestCase(0f, 0f, false, TestName = "NoDamage_NeverInterrupts")]
        public void HitInterrupts_OnlyAtOrAboveTheThreshold(float damage, float minDamage, bool interrupts)
        {
            Assert.That(SpellChannel.HitInterrupts(damage, minDamage), Is.EqualTo(interrupts));
        }

        [Test]
        public void SpellDefaults_DriftAndInterruptThreshold()
        {
            var spell = NewAsset<SpellDefinition>();
            Assert.That(spell.ChannelMaxDrift, Is.InRange(0.3f, 0.5f), "a small step, not a slide");
            Assert.That(spell.ChannelInterruptDamage, Is.EqualTo(1f));
        }
    }
}
