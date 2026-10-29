using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;
using UnityEditor;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// What ships: every <see cref="DialogueDefinition"/> in the project passes
    /// the validator, conversation ids are unique, and Birdy's intro is the
    /// branching one: it validates without even a warning, uses all three
    /// emotes, and plays through an in-memory store the way the content says
    /// (the second talk opens differently once the player promised to help).
    /// Read-only: assets are loaded, never written, and the store is a fake.
    /// </summary>
    [Category("Assets")]
    public class DialogueAssetTests
    {
        const string BirdyPath = "Assets/_Project/ScriptableObjects/Dialogue/Birdy_Intro.asset";
        const string PromisedHelp = "birdy.promised_help";

        static List<(string path, DialogueDefinition definition)> AllDefinitions()
        {
            var all = AssetDatabase.FindAssets("t:" + nameof(DialogueDefinition))
                                   .Select(AssetDatabase.GUIDToAssetPath)
                                   .Select(path => (path, definition: AssetDatabase.LoadAssetAtPath<DialogueDefinition>(path)))
                                   .Where(entry => entry.definition != null)
                                   .ToList();
            Assert.That(all, Is.Not.Empty, "No DialogueDefinition assets found in the project.");
            return all;
        }

        static DialogueDefinition Birdy()
        {
            var birdy = AssetDatabase.LoadAssetAtPath<DialogueDefinition>(BirdyPath);
            Assert.That(birdy, Is.Not.Null, BirdyPath);
            return birdy;
        }

        static DialogueRunner Runner() => new(new TypewriterTiming());   // instant text, no lockouts

        static string[] Shown(DialogueRunner runner) => runner.Options.Select(o => o.OptionId).ToArray();

        /// <summary>Presses through lines until options are up or the talk ends; the texts seen.</summary>
        static List<string> ReadOn(DialogueRunner runner)
        {
            var seen = new List<string> { runner.CurrentText };
            for (int guard = 0; guard < 50 && runner.State == DialogueRunner.Phase.Revealed; guard++)
            {
                runner.Advance();
                if (runner.State != DialogueRunner.Phase.Finished) seen.Add(runner.CurrentText);
            }
            return seen;
        }

        static int IndexOf(DialogueRunner runner, string optionId)
        {
            for (int i = 0; i < runner.Options.Count; i++)
                if (runner.Options[i].OptionId == optionId) return i;
            Assert.Fail($"Option '{optionId}' isn't showing at '{runner.CurrentNodeId}' (showing: {string.Join(", ", Shown(runner))}).");
            return -1;
        }

        [Test]
        public void EveryDefinition_PassesTheValidator()
        {
            var failures = new List<string>();
            foreach (var (path, definition) in AllDefinitions())
            {
                var issues = DialogueValidator.Validate(definition);
                if (DialogueValidator.HasErrors(issues)) failures.Add($"{path}: {DialogueValidator.Describe(issues)}");
            }
            Assert.That(failures, Is.Empty, string.Join("\n", failures));
        }

        [Test]
        public void ConversationIds_AreUniqueAcrossTheProject()
        {
            var clashes = AllDefinitions()
                .Where(entry => !string.IsNullOrEmpty(entry.definition.Id))
                .GroupBy(entry => entry.definition.Id)
                .Where(group => group.Count() > 1)
                .Select(group => $"'{group.Key}': {string.Join(", ", group.Select(e => e.path))}")
                .ToList();
            Assert.That(clashes, Is.Empty, "The choice log keys picks by conversation id:\n" + string.Join("\n", clashes));
        }

        [Test]
        public void Birdy_IsTheBranchingIntro_WithNoIssuesAtAll_AndAllThreeEmotes()
        {
            var birdy = Birdy();
            Assert.That(birdy.HasGraph, Is.True, "Birdy_Intro is still the linear seed: run Tools > Pickleball > Seed Birdy Dialogue.");
            Assert.That(birdy.Id, Is.EqualTo("birdy.intro"));
            Assert.That(birdy.LinearLines, Is.Empty);
            var issues = DialogueValidator.Validate(birdy);
            Assert.That(issues, Is.Empty, DialogueValidator.Describe(issues));

            var emotes = new HashSet<DialogueEmote>();
            for (int i = 0; i < birdy.LineCount; i++) emotes.Add(birdy.GetLine(i).Emote);
            Assert.That(emotes, Is.EquivalentTo(new[] { DialogueEmote.Talk, DialogueEmote.Excited, DialogueEmote.Thinking }));
        }

        [Test]
        public void Birdy_FirstTalk_AsksAFavour_NewtonIsOfferedUntilAsked_AndHelpingSetsTheFact()
        {
            var birdy = Birdy();
            var store = new FakeChoiceStore();
            var runner = Runner();

            Assert.That(runner.Begin(birdy, store), Is.True);
            var lines = ReadOn(runner);
            Assert.That(lines[0], Does.StartWith("Whirr-whirr!"));
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Choosing), string.Join(" | ", lines));
            Assert.That(Shown(runner), Is.EqualTo(new[] { "help", "newton", "later" }));

            // Ask about Newton: his tips, then the same question without that option.
            runner.Choose(IndexOf(runner, "newton"));
            ReadOn(runner);
            Assert.That(runner.CurrentNodeId, Is.EqualTo("intro.ask"));
            Assert.That(Shown(runner), Is.EqualTo(new[] { "help", "later" }), "the Newton option hides once asked");

            runner.Choose(IndexOf(runner, "help"));
            Assert.That(store.GetBool(PromisedHelp), Is.True);
            ReadOn(runner);
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Finished));
            Assert.That(store.Choices.Select(c => c.ChoiceId), Is.EqualTo(new[] { "newton", "help" }));
            Assert.That(store.Choices.All(c => c.DialogueId == "birdy.intro" && c.NodeId == "intro.ask"), Is.True);
        }

        [Test]
        public void Birdy_SecondTalk_WithTheFactSet_OpensWithYouCameBack()
        {
            var birdy = Birdy();
            var store = new FakeChoiceStore();
            var runner = Runner();
            runner.Begin(birdy, store);
            ReadOn(runner);
            runner.Choose(IndexOf(runner, "help"));
            ReadOn(runner);

            Assert.That(runner.Begin(birdy, store), Is.True);
            Assert.That(runner.CurrentText, Is.EqualTo("You came back! Still up for helping?"));
            Assert.That(runner.CurrentLine.Emote, Is.EqualTo(DialogueEmote.Excited));
            Assert.That(runner.State, Is.EqualTo(DialogueRunner.Phase.Choosing), "his options follow at once");

            // Backing out sets the promise back to false; next time he asks again instead.
            runner.Choose(IndexOf(runner, "no"));
            ReadOn(runner);
            Assert.That(store.GetBool(PromisedHelp, fallback: true), Is.False);
            runner.Begin(birdy, store);
            Assert.That(runner.CurrentText, Does.StartWith("Oh! The Guardian again!"));
        }

        [Test]
        public void Birdy_SecondTalk_AfterDecliningAtFirst_AsksAgain()
        {
            var birdy = Birdy();
            var store = new FakeChoiceStore();
            var runner = Runner();
            runner.Begin(birdy, store);
            ReadOn(runner);
            runner.Choose(IndexOf(runner, "later"));
            ReadOn(runner);
            Assert.That(store.TryGetFact(PromisedHelp, out _), Is.False);

            runner.Begin(birdy, store);
            Assert.That(runner.CurrentText, Does.StartWith("Oh! The Guardian again!"));
            Assert.That(Shown(runner), Is.EqualTo(new[] { "help", "newton", "later" }));
        }

        [Test]
        public void Birdy_AnAbortMidChoice_LeavesTheNextTalkAsTheFirst()
        {
            var birdy = Birdy();
            var store = new FakeChoiceStore();
            var runner = Runner();
            runner.Begin(birdy, store);
            ReadOn(runner);
            runner.Stop();

            Assert.That(store.Choices, Is.Empty);
            runner.Begin(birdy, store);
            Assert.That(runner.CurrentText, Does.StartWith("Whirr-whirr!"));
        }
    }
}
