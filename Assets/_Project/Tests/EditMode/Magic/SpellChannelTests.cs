using NUnit.Framework;
using Pickleball.Sandbox.Magic;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// The Heal channel's rules (<see cref="SpellChannel"/>): it completes after
    /// exactly its time at any frame rate, every cancel rule stops it, the most
    /// specific reason is reported, a refused start changes nothing, and mana is
    /// paid when the spell says — on completion by default, so a cancelled heal
    /// is free.
    /// </summary>
    public class SpellChannelTests : MagicTestFixture
    {
        const float Duration = 2f;
        const int Cost = 40;

        static ChannelSignals Still => new() { Held = true };

        // ------------------------------------------------------------ timer

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void HeldStill_CompletesAfterExactlyItsDuration(float dt)
        {
            var mana = NewMana();
            var channel = new SpellChannel();
            Assert.That(channel.TryBegin(Still, Duration, Cost, ChannelManaTiming.OnCompletion, mana),
                        Is.EqualTo(ChannelStopReason.None));

            float time = 0f;
            ChannelStep step;
            int frames = 0;
            do
            {
                step = channel.Tick(dt, Still, mana);
                time += dt;
                frames++;
            } while (step == ChannelStep.Channeling && frames < 10_000);

            Assert.That(step, Is.EqualTo(ChannelStep.Completed));
            Assert.That(time, Is.EqualTo(Duration).Within(dt + 1e-4f), "completes on the frame 2 s is reached");
            Assert.That(time, Is.GreaterThanOrEqualTo(Duration - 1e-4f), "never early");
            Assert.That(channel.IsChanneling, Is.False);
            Assert.That(channel.Progress, Is.EqualTo(1f));
        }

        [Test]
        public void Progress_RisesLinearly()
        {
            var channel = new SpellChannel();
            channel.TryBegin(Still, Duration, 0, ChannelManaTiming.OnCompletion, null);
            channel.Tick(0.5f, Still, null);
            Assert.That(channel.Progress, Is.EqualTo(0.25f).Within(1e-5f));
            channel.Tick(0.5f, Still, null);
            Assert.That(channel.Progress, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(channel.IsChanneling, Is.True);
        }

        [Test]
        public void JustShortOfDuration_StillChanneling()
        {
            var channel = new SpellChannel();
            channel.TryBegin(Still, Duration, 0, ChannelManaTiming.OnCompletion, null);
            Assert.That(channel.Tick(Duration - 0.01f, Still, null), Is.EqualTo(ChannelStep.Channeling));
        }

        // ------------------------------------------------------- cancel rules

        static readonly object[] CancelCases =
        {
            new object[] { new ChannelSignals { Held = false }, ChannelStopReason.Released },
            new object[] { new ChannelSignals { Held = true, Moving = true }, ChannelStopReason.Moved },
            new object[] { new ChannelSignals { Held = true, Airborne = true }, ChannelStopReason.Airborne },
            new object[] { new ChannelSignals { Held = true, Busy = true }, ChannelStopReason.Busy },
            new object[] { new ChannelSignals { Held = true, Hurt = true }, ChannelStopReason.Hurt },
            new object[] { new ChannelSignals { Held = true, Blocked = true }, ChannelStopReason.Blocked },
        };

        [TestCaseSource(nameof(CancelCases))]
        public void BrokenRule_CancelsWithItsReason(ChannelSignals signals, ChannelStopReason expected)
        {
            var channel = new SpellChannel();
            channel.TryBegin(Still, Duration, 0, ChannelManaTiming.OnCompletion, null);
            channel.Tick(0.5f, Still, null);

            Assert.That(channel.Tick(1f / 60f, signals, null), Is.EqualTo(ChannelStep.Cancelled));
            Assert.That(channel.LastStop, Is.EqualTo(expected));
            Assert.That(channel.IsChanneling, Is.False);
            Assert.That(channel.Tick(1f / 60f, Still, null), Is.EqualTo(ChannelStep.Idle), "stays cancelled");
        }

        [TestCaseSource(nameof(CancelCases))]
        public void BrokenRule_RefusesToStart(ChannelSignals signals, ChannelStopReason expected)
        {
            var mana = NewMana();
            var channel = new SpellChannel();
            Assert.That(channel.TryBegin(signals, Duration, Cost, ChannelManaTiming.OnStart, mana), Is.EqualTo(expected));
            Assert.That(channel.IsChanneling, Is.False);
            Assert.That(mana.Current, Is.EqualTo(mana.Max), "a refused start never spends, even OnStart");
        }

        [Test]
        public void SeveralRulesBroken_ReportsTheMostSpecific()
        {
            Assert.That(SpellChannel.Interruption(new ChannelSignals
                        {
                            Held = false, Moving = true, Airborne = true, Busy = true, Hurt = true, Blocked = true,
                        }), Is.EqualTo(ChannelStopReason.Blocked));
            Assert.That(SpellChannel.Interruption(new ChannelSignals { Held = false, Moving = true, Hurt = true }),
                        Is.EqualTo(ChannelStopReason.Hurt));
            Assert.That(SpellChannel.Interruption(new ChannelSignals { Held = false, Moving = true, Busy = true }),
                        Is.EqualTo(ChannelStopReason.Busy));
            Assert.That(SpellChannel.Interruption(new ChannelSignals { Held = false, Moving = true, Airborne = true }),
                        Is.EqualTo(ChannelStopReason.Airborne));
            Assert.That(SpellChannel.Interruption(new ChannelSignals { Held = false, Moving = true }),
                        Is.EqualTo(ChannelStopReason.Moved));
        }

        [Test]
        public void MovingOnTheCompletingFrame_StillCancels()
        {
            var channel = new SpellChannel();
            channel.TryBegin(Still, Duration, 0, ChannelManaTiming.OnCompletion, null);
            channel.Tick(Duration - 0.01f, Still, null);
            Assert.That(channel.Tick(0.1f, new ChannelSignals { Held = true, Moving = true }, null),
                        Is.EqualTo(ChannelStep.Cancelled));
        }

        [Test]
        public void FullHealth_RefusesToStart()
        {
            var channel = new SpellChannel();
            var signals = Still;
            signals.HealthFull = true;
            Assert.That(channel.TryBegin(signals, Duration, 0, ChannelManaTiming.OnCompletion, null),
                        Is.EqualTo(ChannelStopReason.FullHealth));
        }

        [Test]
        public void NoHealth_RefusesToStart()
        {
            var channel = new SpellChannel();
            var signals = Still;
            signals.HealthUnavailable = true;
            Assert.That(channel.TryBegin(signals, Duration, 0, ChannelManaTiming.OnCompletion, null),
                        Is.EqualTo(ChannelStopReason.Unavailable));
        }

        [Test]
        public void ExplicitCancel_StopsARunningChannelOnly()
        {
            var channel = new SpellChannel();
            Assert.That(channel.Cancel(ChannelStopReason.Switched), Is.False, "nothing running");
            channel.TryBegin(Still, Duration, 0, ChannelManaTiming.OnCompletion, null);
            Assert.That(channel.Cancel(ChannelStopReason.Switched), Is.True);
            Assert.That(channel.LastStop, Is.EqualTo(ChannelStopReason.Switched));
        }

        [Test]
        public void BeginWhileChanneling_KeepsTheRunningOne()
        {
            var channel = new SpellChannel();
            channel.TryBegin(Still, Duration, 0, ChannelManaTiming.OnCompletion, null);
            channel.Tick(1f, Still, null);
            channel.TryBegin(Still, Duration, 0, ChannelManaTiming.OnCompletion, null);
            Assert.That(channel.Elapsed, Is.EqualTo(1f).Within(1e-5f));
        }

        // --------------------------------------------------------- mana timing

        [Test]
        public void OnCompletion_SpendsNothingUntilItCompletes_ThenExactlyTheCost()
        {
            var mana = NewMana();
            var channel = new SpellChannel();
            channel.TryBegin(Still, Duration, Cost, ChannelManaTiming.OnCompletion, mana);
            Assert.That(mana.Current, Is.EqualTo(100), "nothing taken at the start");

            channel.Tick(1.5f, Still, mana);
            Assert.That(mana.Current, Is.EqualTo(100), "nothing taken mid-channel");

            Assert.That(channel.Tick(0.6f, Still, mana), Is.EqualTo(ChannelStep.Completed));
            Assert.That(mana.Current, Is.EqualTo(100 - Cost));
        }

        [Test]
        public void OnCompletion_CancelledChannel_IsFree()
        {
            var mana = NewMana();
            var channel = new SpellChannel();
            channel.TryBegin(Still, Duration, Cost, ChannelManaTiming.OnCompletion, mana);
            channel.Tick(1.9f, Still, mana);
            channel.Tick(0.05f, new ChannelSignals { Held = true, Hurt = true }, mana);
            Assert.That(mana.Current, Is.EqualTo(100));
        }

        [Test]
        public void OnCompletion_CannotAfford_RefusesAndSpendsNothing()
        {
            var mana = NewMana();
            Assert.That(mana.TrySpend(70), Is.True);
            var channel = new SpellChannel();
            Assert.That(channel.TryBegin(Still, Duration, Cost, ChannelManaTiming.OnCompletion, mana),
                        Is.EqualTo(ChannelStopReason.NoMana));
            Assert.That(mana.Current, Is.EqualTo(30));
        }

        [Test]
        public void OnCompletion_ManaGoneByTheEnd_CancelsAsNoMana()
        {
            var mana = NewMana();
            var channel = new SpellChannel();
            channel.TryBegin(Still, Duration, Cost, ChannelManaTiming.OnCompletion, mana);
            Assert.That(mana.TrySpend(80), Is.True, "something else spends mid-channel");

            Assert.That(channel.Tick(Duration, Still, mana), Is.EqualTo(ChannelStep.Cancelled));
            Assert.That(channel.LastStop, Is.EqualTo(ChannelStopReason.NoMana));
            Assert.That(mana.Current, Is.EqualTo(20), "the failed spend took nothing");
        }

        [Test]
        public void OnStart_PaysUpFront_AndACancelKeepsItPaid()
        {
            var mana = NewMana();
            var channel = new SpellChannel();
            channel.TryBegin(Still, Duration, Cost, ChannelManaTiming.OnStart, mana);
            Assert.That(mana.Current, Is.EqualTo(100 - Cost));
            Assert.That(channel.Paid, Is.True);

            channel.Tick(1f, new ChannelSignals { Held = true, Moving = true }, mana);
            Assert.That(mana.Current, Is.EqualTo(100 - Cost), "no refund");
        }

        [Test]
        public void OnStart_CompletingDoesNotPayTwice()
        {
            var mana = NewMana();
            var channel = new SpellChannel();
            channel.TryBegin(Still, Duration, Cost, ChannelManaTiming.OnStart, mana);
            Assert.That(channel.Tick(Duration, Still, mana), Is.EqualTo(ChannelStep.Completed));
            Assert.That(mana.Current, Is.EqualTo(100 - Cost));
        }

        [Test]
        public void FreeChannel_NeedsNoMana()
        {
            var channel = new SpellChannel();
            Assert.That(channel.TryBegin(Still, Duration, 0, ChannelManaTiming.OnCompletion, null),
                        Is.EqualTo(ChannelStopReason.None));
            Assert.That(channel.Tick(Duration, Still, null), Is.EqualTo(ChannelStep.Completed));
        }

        [Test]
        public void CostWithoutMana_Refused()
        {
            var channel = new SpellChannel();
            Assert.That(channel.TryBegin(Still, Duration, Cost, ChannelManaTiming.OnCompletion, null),
                        Is.EqualTo(ChannelStopReason.NoMana));
        }
    }
}
