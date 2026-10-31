using NUnit.Framework;
using Pickleball.Sandbox.Magic;
using UnityEngine;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// A heal pressed mid-run must start and stop the player, not cancel on his
    /// own slowdown (playtest): for the settle window movement input and drift
    /// are ignored, the start is not refused for movement, and drift is then
    /// measured from where the body came to rest. Everything else (a jump, a
    /// dash, a hit, letting go) still cancels inside the window.
    /// </summary>
    public class ChannelSettleTests : MagicTestFixture
    {
        const float Duration = 2f;
        const float Drift = 0.4f;
        const float Settle = 0.3f;
        const float Frame = 1f / 60f;

        static readonly Vector3 Start = new(3f, 0f, 5f);

        static ChannelSignals At(Vector3 at, bool moving = false)
            => new() { Held = true, Moving = moving, Position = at };

        static SpellChannel Begun(ChannelSignals first, float settle = Settle)
        {
            var channel = new SpellChannel();
            Assert.That(channel.TryBegin(first, Duration, 0, ChannelManaTiming.OnCompletion, null, Drift, settle),
                        Is.EqualTo(ChannelStopReason.None));
            return channel;
        }

        [Test]
        public void StickHeldOnThePress_StartsTheChannel()
        {
            var channel = Begun(At(Start, moving: true));
            Assert.That(channel.IsChanneling, Is.True);
            Assert.That(channel.IsSettling, Is.True);
        }

        [Test]
        public void WithoutASettleWindow_StickHeldOnThePress_StillRefuses()
        {
            var channel = new SpellChannel();
            Assert.That(channel.TryBegin(At(Start, moving: true), Duration, 0, ChannelManaTiming.OnCompletion,
                                         null, Drift, 0f),
                        Is.EqualTo(ChannelStopReason.Moved));
        }

        [Test]
        public void StickAndBrakingSlide_InsideTheWindow_DoNotCancel()
        {
            var channel = Begun(At(Start, moving: true));
            // A run braking to a stop: the stick lingers and the body slides 0.6 m,
            // past the 0.4 m drift limit.
            var at = Start;
            for (int i = 0; i < 6; i++)
            {
                at += new Vector3(0f, 0f, 0.1f);
                Assert.That(channel.Tick(Frame, At(at, moving: i < 3), null), Is.EqualTo(ChannelStep.Channeling));
            }
            Assert.That(channel.IsChanneling, Is.True);
        }

        [Test]
        public void AfterTheWindow_DriftIsMeasuredFromWhereHeCameToRest()
        {
            var channel = Begun(At(Start));
            var rest = Start + new Vector3(0f, 0f, 0.6f);
            Assert.That(channel.Tick(Settle, At(rest), null), Is.EqualTo(ChannelStep.Channeling));
            Assert.That(channel.IsSettling, Is.False);
            Assert.That(channel.Origin, Is.EqualTo(rest));

            Assert.That(channel.Tick(Frame, At(rest + new Vector3(0.3f, 0f, 0f)), null),
                        Is.EqualTo(ChannelStep.Channeling), "0.3 m from the rest point is inside the limit");
            Assert.That(channel.Tick(Frame, At(rest + new Vector3(0.5f, 0f, 0f)), null),
                        Is.EqualTo(ChannelStep.Cancelled));
            Assert.That(channel.LastStop, Is.EqualTo(ChannelStopReason.Displaced));
        }

        [Test]
        public void AfterTheWindow_TheStickCancels()
        {
            var channel = Begun(At(Start));
            channel.Tick(Settle, At(Start), null);
            Assert.That(channel.Tick(Frame, At(Start, moving: true), null), Is.EqualTo(ChannelStep.Cancelled));
            Assert.That(channel.LastStop, Is.EqualTo(ChannelStopReason.Moved));
        }

        static readonly object[] StillCancelInWindow =
        {
            new object[] { new ChannelSignals { Held = false, Position = Start }, ChannelStopReason.Released },
            new object[] { new ChannelSignals { Held = true, Airborne = true, Position = Start }, ChannelStopReason.Airborne },
            new object[] { new ChannelSignals { Held = true, Busy = true, Position = Start }, ChannelStopReason.Busy },
            new object[] { new ChannelSignals { Held = true, Hurt = true, Position = Start }, ChannelStopReason.Hurt },
        };

        [TestCaseSource(nameof(StillCancelInWindow))]
        public void InsideTheWindow_OtherRulesStillCancel(ChannelSignals signals, ChannelStopReason expected)
        {
            var channel = Begun(At(Start));
            Assert.That(channel.Tick(Frame, signals, null), Is.EqualTo(ChannelStep.Cancelled));
            Assert.That(channel.LastStop, Is.EqualTo(expected));
        }

        [Test]
        public void TheWindowCountsTowardTheDuration()
        {
            var channel = Begun(At(Start, moving: true));
            Assert.That(channel.Tick(Duration, At(Start), null), Is.EqualTo(ChannelStep.Completed));
        }
    }
}
