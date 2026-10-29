using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEngine;
using UnityEngine.TestTools;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// The branching runner: linear definitions still play exactly as before,
    /// the graph is walked by id, options appear once the line before them has
    /// typed out, conditions hide or grey options and steer branches, a pick is
    /// recorded and its effects applied exactly once, and nothing is written
    /// unless a pick is made. Every store here is an in-memory fake.
    /// </summary>
    public class DialogueGraphRunnerTests : DialogueTestFixture
    {
        const string Id = "test.talk";

        static TypewriterTiming Timing(float cps = 0f, float lockout = 0f, float choiceLockout = 0f)
            => new() { CharactersPerSecond = cps, AdvanceLockout = lockout, ChoiceLockout = choiceLockout };

        DialogueDefinition Graph(string start, params DialogueNode[] nodes)
            => Track(DialogueDefinition.CreateRuntime("Birdy", Id, start, nodes));

        static DialogueNode Line(string id, string text, string next = "", DialogueEmote emote = DialogueEmote.Talk)
            => DialogueNode.CreateLine(id, text, emote, next);

        static DialogueOption Option(string id, string next = "", FactCondition[] conditions = null,
                                     UnmetOptionMode whenUnmet = UnmetOptionMode.Hide, FactEffect[] effects = null)
            => DialogueOption.Create(id, id.ToUpperInvariant(), next, conditions, whenUnmet, effects);

        static FactCondition FactIsTrue(string key) => FactCondition.Fact(FactConditionKind.FactEquals, key, FactValue.Of(true));

        /// <summary>"intro" (line) → "ask" (choice, no prompt): a / b / c.</summary>
        DialogueDefinition Ask(params DialogueOption[] options)
            => Graph("intro",
                     Line("intro", "Well?", "ask"),
                     DialogueNode.CreateChoice("ask", "", DialogueEmote.Talk, "", options),
                     Line("after.a", "You said A."),
                     Line("after.b", "You said B."),
                     Line("after.c", "You said C."));

        static string[] Shown(DialogueRunner runner)
        {
            var ids = new string[runner.Options.Count];
            for (int i = 0; i < ids.Length; i++) ids[i] = runner.Options[i].OptionId;
            return ids;
        }

        // ------------------------------------------------------------ linear

        [Test]
        public void Linear_LinesPlayExactlyAsBefore_SameIndicesTextsEmotesAndEvents()
        {
            var def = Track(DialogueDefinition.CreateRuntime("Birdy",
                new DialogueLine("One.", DialogueEmote.Excited),
                new DialogueLine("Two.", DialogueEmote.Talk),
                new DialogueLine("Three.", DialogueEmote.Thinking)));
            var runner = new DialogueRunner(Timing());
            var log = new List<string>();
            runner.LineStarted += i => log.Add($"start {i} {runner.CurrentText} {runner.CurrentLine.Emote}");
            runner.LineRevealed += i => log.Add($"revealed {i}");
            runner.ChoicesShown += () => log.Add("choices");
            runner.Finished += () => log.Add("finished");

            Assert.That(runner.Begin(def), Is.True);
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.NextLine));
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.NextLine));
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.Finished));

            Assert.That(log, Is.EqualTo(new[]
            {
                "start 0 One. Excited", "revealed 0", "start 1 Two. Talk", "revealed 1",
                "start 2 Three. Thinking", "revealed 2", "finished",
            }));
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Finished));
        }

        [Test]
        public void Linear_IsAnImplicitChainOfLineNodes()
        {
            var def = Definition("A", "B");
            Assert.That(def.HasGraph, Is.False);
            Assert.That(def.StartNodeId, Is.EqualTo("line.0"));
            Assert.That(def.TryGetNode("line.1", out var node), Is.True);
            Assert.That(node.Text, Is.EqualTo("B"));
            Assert.That(node.Next, Is.Empty);

            var runner = new DialogueRunner(Timing());
            runner.Begin(def);
            Assert.That(runner.CurrentNodeId, Is.EqualTo("line.0"));
            runner.Advance();
            Assert.That(runner.CurrentNodeId, Is.EqualTo("line.1"));
        }

        [Test]
        public void Linear_NoLines_CannotBegin()
        {
            var runner = new DialogueRunner(Timing());
            Assert.That(runner.Begin(Definition()), Is.False);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Idle));
        }

        // ------------------------------------------------------------- graph

        [Test]
        public void Graph_IsWalkedByIdNotByListOrder_AndAnEmptyNextEnds()
        {
            var def = Graph("first", Line("last", "Bye."), Line("first", "Hi.", "middle"), Line("middle", "So.", "last"));
            var runner = new DialogueRunner(Timing());
            var texts = new List<string>();
            runner.LineStarted += _ => texts.Add(runner.CurrentText);

            runner.Begin(def);
            runner.Advance();
            runner.Advance();
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.Finished));
            Assert.That(texts, Is.EqualTo(new[] { "Hi.", "So.", "Bye." }));
        }

        [Test]
        public void Choice_OptionsAppearOnlyOnceThePrecedingLineHasTypedOut_WithNoExtraPress()
        {
            var runner = new DialogueRunner(Timing(cps: 10f));       // "Well?" = 5 characters = 0.5 s
            var log = new List<string>();
            runner.LineRevealed += _ => log.Add("revealed");
            runner.ChoicesShown += () => log.Add("choices");
            runner.Begin(Ask(Option("a", "after.a"), Option("b", "after.b")));

            runner.Tick(0.3f);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Typing));
            Assert.That(runner.Options, Is.Empty, "no options while the line is still typing");

            runner.Tick(0.3f);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Choosing));
            Assert.That(log, Is.EqualTo(new[] { "revealed", "choices" }));
            Assert.That(runner.CurrentText, Is.EqualTo("Well?"), "the line stays in the box under the options");
            Assert.That(runner.CurrentNodeId, Is.EqualTo("ask"));
            Assert.That(Shown(runner), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(runner.HighlightedOption, Is.EqualTo(0));
        }

        [Test]
        public void Choice_SkippingTheTypewriterPutsTheOptionsUpAtOnce()
        {
            var runner = new DialogueRunner(Timing(cps: 10f));
            runner.Begin(Ask(Option("a", "after.a")));
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.RevealedLine));
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Choosing));
        }

        [Test]
        public void Choice_WithAPrompt_TypesThePromptThenShowsTheOptions()
        {
            var def = Graph("ask",
                DialogueNode.CreateChoice("ask", "Pick one.", DialogueEmote.Thinking, "", Option("a", "after")),
                Line("after", "Done."));
            var runner = new DialogueRunner(Timing(cps: 10f));
            runner.Begin(def);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Typing));
            Assert.That(runner.CurrentText, Is.EqualTo("Pick one."));
            Assert.That(runner.CurrentLine.Emote, Is.EqualTo(DialogueEmote.Thinking));

            runner.Tick(2f);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Choosing));
        }

        [Test]
        public void Choice_WithNoPromptAtTheStart_ShowsTheOptionsAtOnce()
        {
            var def = Graph("ask", DialogueNode.CreateChoice("ask", "", DialogueEmote.Talk, "", Option("a")));
            var runner = new DialogueRunner(Timing());
            Assert.That(runner.Begin(def), Is.True);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Choosing));
            Assert.That(runner.LineIndex, Is.EqualTo(-1), "no line was shown");
        }

        [Test]
        public void Pick_IsRecordedOnce_EffectsAppliedOnce_ThenTheTalkMovesOn()
        {
            var store = new FakeChoiceStore();
            var runner = new DialogueRunner(Timing());
            var made = new List<string>();
            runner.ChoiceMade += (node, choice) => made.Add($"{node}/{choice} recorded={store.Choices.Count}");
            runner.Begin(Ask(Option("a", "after.a"),
                             Option("b", "after.b", effects: new[]
                             {
                                 FactEffect.Set("met", FactValue.Of(true)),
                                 FactEffect.Add("count", 2),
                             })), store);

            Assert.That(runner.Choose(1), Is.True);

            Assert.That(store.Choices.Count, Is.EqualTo(1));
            Assert.That(store.Choices[0].Matches(Id, "ask", "b"), Is.True, store.Choices[0].ToString());
            Assert.That(store.GetBool("met"), Is.True);
            Assert.That(store.GetInt("count"), Is.EqualTo(2));
            Assert.That(store.FactWrites, Is.EqualTo(2));
            Assert.That(made, Is.EqualTo(new[] { "ask/b recorded=1" }), "raised once, after recording");
            Assert.That(runner.CurrentText, Is.EqualTo("You said B."));
            Assert.That(runner.Options, Is.Empty);

            // Nothing after the pick writes again.
            runner.Tick(1f);
            runner.Advance();
            runner.Advance();
            Assert.That(store.Choices.Count, Is.EqualTo(1));
            Assert.That(store.FactWrites, Is.EqualTo(2));
            Assert.That(runner.Choose(0), Is.False);
        }

        [Test]
        public void Pick_ByInteract_TakesTheHighlightedOption()
        {
            var store = new FakeChoiceStore();
            var runner = new DialogueRunner(Timing());
            runner.Begin(Ask(Option("a", "after.a"), Option("b", "after.b")), store);
            runner.MoveHighlight(1);

            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.Chose));
            Assert.That(store.LastChoice(Id, "ask"), Is.EqualTo("b"));
        }

        [Test]
        public void Pick_WithAnEmptyNext_EndsTheTalk()
        {
            var runner = new DialogueRunner(Timing());
            int finished = 0;
            runner.Finished += () => finished++;
            runner.Begin(Ask(Option("a")));

            runner.Choose(0);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Finished));
            Assert.That(finished, Is.EqualTo(1));
        }

        [Test]
        public void Pick_LeadingStraightToAnotherChoice_PutsItsOptionsUpAtOnce()
        {
            var def = Graph("one",
                DialogueNode.CreateChoice("one", "", DialogueEmote.Talk, "", Option("x", "two")),
                DialogueNode.CreateChoice("two", "", DialogueEmote.Talk, "", Option("y"), Option("z")));
            var runner = new DialogueRunner(Timing());
            int shown = 0;
            runner.ChoicesShown += () => shown++;
            runner.Begin(def);
            runner.Choose(0);

            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Choosing));
            Assert.That(runner.CurrentNodeId, Is.EqualTo("two"));
            Assert.That(Shown(runner), Is.EqualTo(new[] { "y", "z" }));
            Assert.That(shown, Is.EqualTo(2));
        }

        // ------------------------------------------------------ conditions

        [Test]
        public void Condition_Hide_LeavesTheOptionOut_UntilItHolds()
        {
            var store = new FakeChoiceStore();
            var def = Ask(Option("a", "after.a"),
                          Option("secret", "after.b", new[] { FactIsTrue("knows") }, UnmetOptionMode.Hide),
                          Option("c", "after.c"));
            var runner = new DialogueRunner(Timing());

            runner.Begin(def, store);
            Assert.That(Shown(runner), Is.EqualTo(new[] { "a", "c" }));

            store.SetFact("knows", FactValue.Of(true));
            runner.Begin(def, store);
            Assert.That(Shown(runner), Is.EqualTo(new[] { "a", "secret", "c" }));
        }

        [Test]
        public void Condition_Disable_ShowsItGreyed_TheCursorSkipsIt_AndItCannotBePicked()
        {
            var store = new FakeChoiceStore();
            var runner = new DialogueRunner(Timing());
            runner.Begin(Ask(Option("locked", "after.a", new[] { FactIsTrue("key") }, UnmetOptionMode.Disable),
                             Option("b", "after.b"),
                             Option("c", "after.c")), store);

            Assert.That(Shown(runner), Is.EqualTo(new[] { "locked", "b", "c" }));
            Assert.That(runner.Options[0].IsEnabled, Is.False);
            Assert.That(runner.HighlightedOption, Is.EqualTo(1), "the cursor starts on the first enabled option");

            runner.MoveHighlight(1);
            Assert.That(runner.HighlightedOption, Is.EqualTo(2));
            runner.MoveHighlight(1);
            Assert.That(runner.HighlightedOption, Is.EqualTo(1), "wraps past the greyed one");
            Assert.That(runner.Highlight(0), Is.False);
            Assert.That(runner.Choose(0), Is.False);
            Assert.That(store.Choices, Is.Empty);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Choosing));
        }

        [Test]
        public void Condition_NothingPickable_GoesToTheFallback()
        {
            var def = Graph("intro",
                Line("intro", "Well?", "ask"),
                DialogueNode.CreateChoice("ask", "", DialogueEmote.Talk, "fallback",
                    Option("a", "", new[] { FactIsTrue("x") }, UnmetOptionMode.Disable),
                    Option("b", "", new[] { FactIsTrue("y") })),
                Line("fallback", "Never mind."));
            var runner = new DialogueRunner(Timing());
            runner.Begin(def, new FakeChoiceStore());

            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Revealed), "no options: the line waits for a press");
            runner.Advance();
            Assert.That(runner.CurrentText, Is.EqualTo("Never mind."));
        }

        [Test]
        public void Branch_TakesNextWhenEveryConditionHolds_ElseItsElse()
        {
            var store = new FakeChoiceStore();
            var def = Graph("check",
                DialogueNode.CreateBranch("check", new[] { FactIsTrue("promised") }, "again", "first"),
                Line("first", "Nice to meet you."),
                Line("again", "You came back!"));
            var runner = new DialogueRunner(Timing());

            runner.Begin(def, store);
            Assert.That(runner.CurrentText, Is.EqualTo("Nice to meet you."));

            store.SetFact("promised", FactValue.Of(true));
            runner.Begin(def, store);
            Assert.That(runner.CurrentText, Is.EqualTo("You came back!"));
            Assert.That(runner.CurrentNodeId, Is.EqualTo("again"), "a branch never shows");
        }

        [Test]
        public void Branch_OnAChoiceMadeEarlier_ReadsTheLog()
        {
            var store = new FakeChoiceStore();
            var def = Graph("check",
                DialogueNode.CreateBranch("check", new[] { FactCondition.Choice(true, "", "ask", "b") }, "again", "intro"),
                Line("intro", "Well?", "ask"),
                DialogueNode.CreateChoice("ask", "", DialogueEmote.Talk, "", Option("a"), Option("b")),
                Line("again", "B again?"));
            var runner = new DialogueRunner(Timing());

            runner.Begin(def, store);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Choosing));
            runner.Choose(1);
            runner.Begin(def, store);
            Assert.That(runner.CurrentText, Is.EqualTo("B again?"));
        }

        [Test]
        public void NoStore_ConditionsSeeAnEmptyStore_AndEffectsAreDropped()
        {
            var runner = new DialogueRunner(Timing());
            runner.Begin(Ask(Option("needs", "after.a", new[] { FactIsTrue("x") }),
                             Option("free", "after.b", new[] { FactCondition.Fact(FactConditionKind.FactMissing, "x") },
                                    effects: new[] { FactEffect.Set("x", FactValue.Of(true)) })));

            Assert.That(Shown(runner), Is.EqualTo(new[] { "free" }));
            Assert.That(runner.Choose(0), Is.True);
            Assert.That(runner.CurrentText, Is.EqualTo("You said B."));
        }

        // ---------------------------------------------------- lockouts, aborts

        [Test]
        public void ChoiceLockout_InteractInsideItRestartsIt_SoMashingNeverPicks()
        {
            var store = new FakeChoiceStore();
            var runner = new DialogueRunner(Timing(choiceLockout: 0.3f));
            runner.Begin(Ask(Option("a", "after.a")), store);

            runner.Tick(0.2f);
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.Ignored));
            runner.Tick(0.2f);       // 0.4 s since the options, but only 0.2 since that press
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.Ignored));
            Assert.That(store.Choices, Is.Empty);

            runner.Tick(0.31f);
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.Chose));
            Assert.That(store.Choices.Count, Is.EqualTo(1));
        }

        [Test]
        public void ChoiceLockout_ANumberOrClickInsideIt_OnlyMovesTheCursor()
        {
            var store = new FakeChoiceStore();
            var runner = new DialogueRunner(Timing(choiceLockout: 0.3f));
            runner.Begin(Ask(Option("a", "after.a"), Option("b", "after.b")), store);

            Assert.That(runner.Choose(1), Is.False);
            Assert.That(runner.HighlightedOption, Is.EqualTo(1));
            Assert.That(store.Choices, Is.Empty);

            runner.Tick(0.31f);
            Assert.That(runner.Choose(1), Is.True);
        }

        [Test]
        public void Stop_MidChoice_RecordsNothingAndAppliesNothing()
        {
            var store = new FakeChoiceStore();
            var runner = new DialogueRunner(Timing());
            runner.Begin(Ask(Option("a", "after.a", effects: new[] { FactEffect.Set("x", FactValue.Of(1)) })), store);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Choosing));

            runner.Stop();

            Assert.That(store.Choices, Is.Empty);
            Assert.That(store.Facts, Is.Empty);
            Assert.That(store.FactWrites, Is.Zero);
            Assert.That(runner.Options, Is.Empty);
            Assert.That(runner.Choose(0), Is.False);
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.Ignored));
        }

        [Test]
        public void Highlight_RaisesOnlyOnChange()
        {
            var runner = new DialogueRunner(Timing());
            var seen = new List<int>();
            runner.ChoiceHighlighted += seen.Add;
            runner.Begin(Ask(Option("a"), Option("b"), Option("c")));

            runner.MoveHighlight(1);
            runner.Highlight(1);
            runner.MoveHighlight(-1);
            runner.MoveHighlight(-1);
            Assert.That(seen, Is.EqualTo(new[] { 1, 0, 2 }));
        }

        // ---------------------------------------------------- broken content

        [Test]
        public void ADanglingLink_EndsTheTalkWithAWarning()
        {
            var runner = new DialogueRunner(Timing());
            LogAssert.Expect(LogType.Warning, new Regex("no node 'nowhere'"));
            runner.Begin(Graph("a", Line("a", "Hi.", "nowhere")));
            // The link is resolved when the line is revealed, so the press finishes.
            Assert.That(runner.Advance(), Is.EqualTo(DialogueRunner.AdvanceResult.Finished));
        }

        [Test]
        public void BranchesThatLoopForever_EndTheTalkInsteadOfHanging()
        {
            var def = Graph("x",
                DialogueNode.CreateBranch("x", new[] { FactIsTrue("never") }, "y", "y"),
                DialogueNode.CreateBranch("y", new[] { FactIsTrue("never") }, "x", "x"));
            var runner = new DialogueRunner(Timing());
            LogAssert.Expect(LogType.Warning, new Regex("branches in a row"));
            Assert.That(runner.Begin(def), Is.False);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Idle));
        }
    }
}
