using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// Known bugs from the dialogue-choices QA round (reports/R-dialogue-choices.md),
    /// pinned as ignored tests. Each states what should happen; delete the Ignore
    /// with the fix.
    /// </summary>
    public class ChoiceKnownBugTests
    {
        [Test]
        [Ignore("Known bug DLG-C-1: tapping E at about 3 Hz (or slower) to skip text picks the first " +
                "answer unread. The 0.3 s choice debounce only stops presses faster than ~3.4 Hz. " +
                "See reports/R-dialogue-choices.md.")]
        public void TappingE_ThreeTimesASecond_ThroughTheQuestion_NeverPicksUnread()
        {
            var config = ScriptableObject.CreateInstance<DialogueConfig>();   // the shipped defaults
            var definition = DialogueDefinition.CreateRuntime("Birdy", "qa.mash", "ask",
                DialogueNode.CreateLine("ask", "Say... could a Guardian spare a moment for a small bird with a big problem?",
                                        DialogueEmote.Thinking, "choice"),
                DialogueNode.CreateChoice("choice", "", DialogueEmote.Talk, "",
                    DialogueOption.Create("help", "Of course. What's wrong?", ""),
                    DialogueOption.Create("later", "Sorry, I'm in a hurry.", "")));
            try
            {
                var store = new FakeChoiceStore();
                var runner = new DialogueRunner(config.Typewriter);
                Assert.That(runner.Begin(definition, store), Is.True);

                // A player skipping text with a steady tap every 0.35 s (2.9 Hz): the first
                // tap shows the whole question (the answers appear with it), the next comes
                // 0.35 s later, before anyone could have read them.
                for (int tap = 0; tap < 6; tap++)
                {
                    runner.Tick(0.35f);
                    runner.Advance();
                }

                Assert.That(store.Choices, Is.Empty,
                    "A steady 2.9 Hz tap through the text picked an answer the player never read.");
            }
            finally
            {
                Object.DestroyImmediate(definition);
                Object.DestroyImmediate(config);
            }
        }

        // DLG-C-3, fixed: PlayTimeClock is monotonic, and ChoiceStoreHost skips
        // OnDisable's save after OnApplicationQuit and never banks less than the file holds.
        [Test]
        public void PlayTime_NeverGoesBack_WhenTheRealTimeSourceResets_AsAtTheEndOfPlay()
        {
            double realtime = 0;
            var clock = new PlayTimeClock(() => realtime, 100);
            realtime = 3;
            Assert.That(clock.Now, Is.EqualTo(103));

            realtime = 0;   // what OnDisable reads after Play ends (measured: unscaled 0, frame 0)
            Assert.That(clock.Now, Is.GreaterThanOrEqualTo(103),
                "The session's 3 s were dropped: the next save banks 100 and new picks sort before old ones.");
        }

        [Test]
        [Ignore("Known bug DLG-C-2: a reachable loop with no way out is only a validator warning, so " +
                "DialogueAssetTests passes while the talk can never end (time stopped, no exit). " +
                "See reports/R-dialogue-choices.md.")]
        public void AReachableLoopWithNoWayOut_IsAnError()
        {
            var definition = DialogueDefinition.CreateRuntime("Birdy", "qa.loop", "a",
                DialogueNode.CreateLine("a", "Round and round.", DialogueEmote.Talk, "b"),
                DialogueNode.CreateLine("b", "And round again.", DialogueEmote.Talk, "a"));
            try
            {
                var issues = DialogueValidator.Validate(definition);
                Assert.That(DialogueValidator.HasErrors(issues), Is.True, DialogueValidator.Describe(issues));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }
    }
}
