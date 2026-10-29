using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// The option list's input logic without devices: how stick, d-pad and keys
    /// combine into one axis, how a held direction steps and repeats at any
    /// frame rate, where the cursor lands (skipping greyed-out options,
    /// wrapping), and which option a number key means.
    /// </summary>
    public class ChoiceInputTests
    {
        static readonly ChoiceNavigationSettings Nav = new() { Threshold = 0.5f, RepeatDelay = 0.4f, RepeatInterval = 0.15f };

        static PresentedOption[] Options(params bool[] enabled)
        {
            var options = new PresentedOption[enabled.Length];
            for (int i = 0; i < enabled.Length; i++)
                options[i] = new PresentedOption(DialogueOption.Create("o" + i, "O" + i, ""), enabled[i]);
            return options;
        }

        // ------------------------------------------------------------- axis

        [Test]
        public void Axis_StickAndButtonsCombine_UpIsPositive_AndClampToOne()
        {
            Assert.That(DialogueChoiceInput.Axis(0f, true, false), Is.EqualTo(1f));
            Assert.That(DialogueChoiceInput.Axis(0f, false, true), Is.EqualTo(-1f));
            Assert.That(DialogueChoiceInput.Axis(0.7f, true, false), Is.EqualTo(1f));
            Assert.That(DialogueChoiceInput.Axis(-0.3f, false, false), Is.EqualTo(-0.3f).Within(1e-6f));
            Assert.That(DialogueChoiceInput.Axis(0f, true, true), Is.EqualTo(0f), "up and down cancel");
        }

        [Test]
        public void NumberKeys_OneToNine_AreOptionsZeroToEight()
        {
            Assert.That(DialogueChoiceInput.IndexForDigit(1), Is.EqualTo(0));
            Assert.That(DialogueChoiceInput.IndexForDigit(9), Is.EqualTo(8));
            Assert.That(DialogueChoiceInput.IndexForDigit(0), Is.EqualTo(-1));
            Assert.That(DialogueChoiceInput.IndexForDigit(10), Is.EqualTo(-1));
        }

        // -------------------------------------------------------- navigator

        [Test]
        public void Navigator_APushStepsOnce_UpIsTowardTheFirstOption()
        {
            var nav = new ChoiceNavigator();
            Assert.That(nav.Step(1f, 1f / 60f, Nav), Is.EqualTo(-1));
            Assert.That(nav.Step(1f, 1f / 60f, Nav), Is.EqualTo(0), "held: no second step before the repeat delay");
            Assert.That(nav.Step(0f, 1f / 60f, Nav), Is.EqualTo(0));
            Assert.That(nav.Step(-1f, 1f / 60f, Nav), Is.EqualTo(1));
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void Navigator_HoldingRepeats_TheSameNumberOfTimesAtAnyFrameRate(float dt)
        {
            // 0.95 s held: the push at 0, then repeats at 0.40, 0.55, 0.70, 0.85.
            var nav = new ChoiceNavigator();
            int steps = 0;
            int frames = Mathf.RoundToInt(0.95f / dt);
            for (int i = 0; i < frames; i++) steps += nav.Step(-1f, dt, Nav);
            Assert.That(steps, Is.EqualTo(5));
        }

        [Test]
        public void Navigator_AHitch_MovesTheCursorOnlyOneOption()
        {
            var nav = new ChoiceNavigator();
            nav.Step(-1f, 0.01f, Nav);
            Assert.That(nav.Step(-1f, 5f, Nav), Is.EqualTo(1));
            Assert.That(nav.Step(-1f, 0.01f, Nav), Is.EqualTo(0));
        }

        [Test]
        public void Navigator_ReversingStepsAtOnce()
        {
            var nav = new ChoiceNavigator();
            nav.Step(1f, 0.01f, Nav);
            Assert.That(nav.Step(-1f, 0.01f, Nav), Is.EqualTo(1));
        }

        [Test]
        public void Navigator_HasHysteresis_ARestingStickNearTheThresholdDoesNotChatter()
        {
            var nav = new ChoiceNavigator();
            Assert.That(nav.Step(0.55f, 0.01f, Nav), Is.EqualTo(-1));
            Assert.That(nav.Step(0.45f, 0.01f, Nav), Is.EqualTo(0), "still held above the release point");
            Assert.That(nav.Step(0.55f, 0.01f, Nav), Is.EqualTo(0), "no new push");
            Assert.That(nav.Step(0.2f, 0.01f, Nav), Is.EqualTo(0), "let go");
            Assert.That(nav.Step(0.55f, 0.01f, Nav), Is.EqualTo(-1));
        }

        [Test]
        public void Navigator_ADirectionHeldWhenOptionsAppear_DoesNothingUntilPushedAgain()
        {
            var nav = new ChoiceNavigator();
            nav.Prime(-1f, Nav);
            for (int i = 0; i < 120; i++)
                Assert.That(nav.Step(-1f, 1f / 60f, Nav), Is.EqualTo(0), $"frame {i}");
            Assert.That(nav.Step(0f, 1f / 60f, Nav), Is.EqualTo(0));
            Assert.That(nav.Step(-1f, 1f / 60f, Nav), Is.EqualTo(1));
        }

        [Test]
        public void Navigator_ZeroInterval_NeverRepeats()
        {
            var nav = new ChoiceNavigator();
            var once = new ChoiceNavigationSettings { Threshold = 0.5f, RepeatDelay = 0.1f, RepeatInterval = 0f };
            int steps = 0;
            for (int i = 0; i < 100; i++) steps += nav.Step(1f, 0.05f, once);
            Assert.That(steps, Is.EqualTo(-1));
        }

        // -------------------------------------------------------- selection

        [Test]
        public void Selection_StartsOnTheFirstEnabled()
        {
            Assert.That(ChoiceSelection.FirstEnabled(Options(false, true, true)), Is.EqualTo(1));
            Assert.That(ChoiceSelection.FirstEnabled(Options(false, false)), Is.EqualTo(-1));
        }

        [Test]
        public void Selection_StepsWrapAndSkipGreyedOutOptions()
        {
            var options = Options(true, false, true, true);
            Assert.That(ChoiceSelection.Step(options, 0, 1), Is.EqualTo(2));
            Assert.That(ChoiceSelection.Step(options, 3, 1), Is.EqualTo(0), "wraps from the bottom to the top");
            Assert.That(ChoiceSelection.Step(options, 0, -1), Is.EqualTo(3), "wraps from the top to the bottom");
            Assert.That(ChoiceSelection.Step(options, 2, -1), Is.EqualTo(0));
            Assert.That(ChoiceSelection.Step(options, 0, 2), Is.EqualTo(3));
            Assert.That(ChoiceSelection.Step(options, 2, 0), Is.EqualTo(2));
        }

        [Test]
        public void Selection_TheOnlyEnabledOption_StaysPut()
        {
            Assert.That(ChoiceSelection.Step(Options(false, true, false), 1, 1), Is.EqualTo(1));
            Assert.That(ChoiceSelection.Step(Options(false, true, false), 1, -1), Is.EqualTo(1));
        }
    }
}
