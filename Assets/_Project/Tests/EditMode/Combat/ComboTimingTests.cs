using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Combat;

namespace Pickleball.Tests.Combat
{
    /// <summary>
    /// <see cref="ComboTiming"/> must agree with <see cref="SwingTimeline"/>, the clock
    /// that actually plays the swings: if it drifted, the compass-vs-ruler ratios in the
    /// doc and in <see cref="RulerMovesetAssetTests"/> would describe a game nobody plays.
    /// Each case runs the real timeline with a press buffered every frame and compares.
    /// </summary>
    public class ComboTimingTests : CombatTestFixture
    {
        ComboStepDefinition Make(string name, float duration, SwingWindows w, HitSettings hit)
            => Track(ComboStepDefinition.CreateRuntime(name, name, duration, w, hit));

        /// <summary>Seconds the real timeline takes to reach the point a buffered press
        /// chains (or, for the last step, the end), stepping at <paramref name="dt"/>.</summary>
        static float Simulate(IReadOnlyList<ComboStepDefinition> steps, float dt)
        {
            float t = 0f;
            var timeline = new SwingTimeline();
            for (int i = 0; i < steps.Count; i++)
            {
                timeline.Start(steps[i]);
                bool last = i == steps.Count - 1;
                while (true)
                {
                    var signal = timeline.Advance(dt);
                    t += dt;
                    if ((signal & SwingSignal.Finished) != 0) break;
                    if (!last && timeline.IsChainWindowOpen) break;
                }
            }
            return t;
        }

        static List<ResolvedStep> Placed(params ComboStepDefinition[] steps)
        {
            var list = new List<ResolvedStep>();
            for (int i = 0; i < steps.Length; i++)
                list.Add(new ResolvedStep(steps[i], ComboRole.Regular, ComboTrack.Ground, i, i == steps.Length - 1));
            return list;
        }

        [TestCase(1f / 30)]
        [TestCase(1f / 60)]
        [TestCase(1f / 144)]
        public void ChainSeconds_MatchesTheTimeline(float dt)
        {
            var a = Make("A", 0.6f, new SwingWindows(0.3f, 0.6f, 0.6f, 1f), new HitSettings(10f, 1f));
            // comboOpen before activeEnd: the timeline waits for the blade to close.
            var b = Make("B", 0.9f, new SwingWindows(0.4f, 0.7f, 0.5f, 1f), new HitSettings(20f, 1f));
            // comboOpen after activeEnd: a heavier recovery.
            var c = Make("C", 0.8f, new SwingWindows(0.4f, 0.55f, 0.75f, 1f), new HitSettings(20f, 1f));
            var d = Make("D", 1.5f, new SwingWindows(0.43f, 0.57f, 0.74f, 0.87f), new HitSettings(40f, 1f));
            var steps = new[] { a, b, c, d };

            float predicted = ComboTiming.ChainSeconds(Placed(steps), withHitstop: false);

            Assert.That(predicted, Is.EqualTo(0.6f * 0.6f + 0.9f * 0.7f + 0.8f * 0.75f + 1.5f * 0.87f).Within(1e-4f));
            Assert.That(Simulate(steps, dt), Is.EqualTo(predicted).Within(dt * steps.Length + 1e-4f));
        }

        [Test]
        public void ChainFraction_IsTheLaterOfOpenAndActiveEnd_ButNeverPastTheEnd()
        {
            var ring = Make("Ring", 1f, new SwingWindows(0.3f, 0.5f, 0.5f, 0.9f), new HitSettings(10f, 1f));
            var late = Make("Late", 1f, new SwingWindows(0.3f, 0.5f, 0.95f, 0.8f), new HitSettings(10f, 1f));

            Assert.That(ComboTiming.ChainFraction(late), Is.EqualTo(0.8f).Within(1e-6f),
                        "A swing that ends first chains at its end (the timeline's Finished).");
            Assert.That(ComboTiming.ChainFraction(ring), Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void HitstopAndDamage_CountTheBladeAndTheRing()
        {
            var plain = Make("Plain", 1f, SwingWindows.Default, new HitSettings(10f, 1f, 0f, 0.05f));
            var steps = Placed(plain, plain);

            Assert.That(ComboTiming.ChainDamage(steps), Is.EqualTo(20f));
            Assert.That(ComboTiming.ChainSeconds(steps, true) - ComboTiming.ChainSeconds(steps, false),
                        Is.EqualTo(0.1f).Within(1e-5f));
            Assert.That(ComboTiming.WindupSeconds(plain), Is.EqualTo(SwingWindows.Default.ActiveStart).Within(1e-6f));
        }
    }
}
