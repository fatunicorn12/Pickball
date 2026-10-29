using System.Linq;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// One test per validator rule: each builds the smallest definition that
    /// breaks it and checks the issue's severity and node, and that a clean
    /// conversation (with a loop that has a way out) has no issues at all.
    /// </summary>
    public class DialogueValidatorTests : DialogueTestFixture
    {
        DialogueDefinition Graph(string start, params DialogueNode[] nodes)
            => Track(DialogueDefinition.CreateRuntime("Birdy", "test.talk", start, nodes));

        static DialogueNode Line(string id, string next = "", string text = "Hi.")
            => DialogueNode.CreateLine(id, text, DialogueEmote.Talk, next);

        static DialogueNode Choice(string id, string fallback, params DialogueOption[] options)
            => DialogueNode.CreateChoice(id, "", DialogueEmote.Talk, fallback, options);

        static DialogueOption Option(string id, string next = "", params FactCondition[] conditions)
            => DialogueOption.Create(id, "Answer " + id, next, conditions);

        static FactCondition Flag(string key) => FactCondition.Fact(FactConditionKind.FactEquals, key, FactValue.Of(true));

        static DialogueIssue Single(DialogueDefinition definition, DialogueIssueSeverity severity, string containing)
        {
            var issues = DialogueValidator.Validate(definition);
            var matches = issues.Where(i => i.Severity == severity && i.Message.Contains(containing)).ToList();
            Assert.That(matches.Count, Is.EqualTo(1),
                        $"expected one {severity} containing '{containing}':\n{DialogueValidator.Describe(issues)}");
            return matches[0];
        }

        [Test]
        public void ACleanConversation_WithALoopThatHasAWayOut_HasNoIssues()
        {
            var def = Graph("start",
                DialogueNode.CreateBranch("start", new[] { Flag("met") }, "again", "hello"),
                Line("hello", "ask"),
                Choice("ask", "", Option("more", "more"), Option("bye", "bye"),
                       DialogueOption.Create("secret", "Psst.", "bye", new[] { Flag("sly") }, UnmetOptionMode.Disable)),
                Line("more", "ask"),
                Line("bye"),
                Line("again", "ask"));

            var issues = DialogueValidator.Validate(def);
            Assert.That(issues, Is.Empty, DialogueValidator.Describe(issues));
        }

        [Test]
        public void ALinearDefinition_WithLines_IsFine_AndWithNoneIsAWarning()
        {
            Assert.That(DialogueValidator.Validate(Definition("Hi.")), Is.Empty);
            var issue = Single(Definition(), DialogueIssueSeverity.Warning, "No nodes and no lines");
            Assert.That(issue.NodeId, Is.Empty);
        }

        [Test]
        public void EmptyConversationId_IsAnError()
        {
            var def = Track(DialogueDefinition.CreateRuntime("Birdy", "", "a", Line("a")));
            Single(def, DialogueIssueSeverity.Error, "conversation id is empty");
        }

        [Test]
        public void EmptyNodeId_IsAnError()
        {
            Single(Graph("a", Line("a"), Line("")), DialogueIssueSeverity.Error, "empty id");
        }

        [Test]
        public void DuplicateNodeId_IsAnError()
        {
            var issue = Single(Graph("a", Line("a"), Line("a")), DialogueIssueSeverity.Error, "Duplicate node id");
            Assert.That(issue.NodeId, Is.EqualTo("a"));
        }

        [Test]
        public void EmptyOrUnknownStart_IsAnError()
        {
            Single(Graph("", Line("a")), DialogueIssueSeverity.Error, "start node id is empty");
            Single(Graph("zzz", Line("a")), DialogueIssueSeverity.Error, "start node 'zzz' doesn't exist");
        }

        [TestCase("line")]
        [TestCase("branch")]
        [TestCase("else")]
        [TestCase("option")]
        [TestCase("fallback")]
        public void ADanglingLink_IsAnError_WhereverItIs(string where)
        {
            DialogueNode node = where switch
            {
                "line" => Line("n", "ghost"),
                "branch" => DialogueNode.CreateBranch("n", new[] { Flag("x") }, "ghost", ""),
                "else" => DialogueNode.CreateBranch("n", new[] { Flag("x") }, "", "ghost"),
                "option" => Choice("n", "", Option("a", "ghost")),
                _ => Choice("n", "ghost", Option("a", "", Flag("x"))),
            };
            var issue = Single(Graph("n", node), DialogueIssueSeverity.Error, "'ghost', which doesn't exist");
            Assert.That(issue.NodeId, Is.EqualTo("n"));
        }

        [Test]
        public void AChoiceWithNoOptions_IsAnError()
        {
            Single(Graph("c", Choice("c", "")), DialogueIssueSeverity.Error, "no options");
        }

        [Test]
        public void TooManyOptions_IsAnError()
        {
            var options = Enumerable.Range(0, DialogueValidator.MaxOptions + 1).Select(i => Option("o" + i)).ToArray();
            Single(Graph("c", Choice("c", "", options)), DialogueIssueSeverity.Error, "options; at most");
        }

        [Test]
        public void EmptyOrDuplicateOptionId_IsAnError()
        {
            Single(Graph("c", Choice("c", "", Option(""), Option("b"))), DialogueIssueSeverity.Error, "empty id");
            Single(Graph("c", Choice("c", "", Option("a"), Option("a"))), DialogueIssueSeverity.Error, "Duplicate option id 'a'");
        }

        [Test]
        public void EveryOptionConditional_AndNoFallback_IsAnError()
        {
            Single(Graph("c", Choice("c", "", Option("a", "", Flag("x")),
                                    DialogueOption.Create("b", "B", "", new[] { Flag("y") }, UnmetOptionMode.Disable))),
                   DialogueIssueSeverity.Error, "all of them could be hidden or disabled");

            // A fallback, or one unconditional option, fixes it.
            Assert.That(DialogueValidator.HasErrors(DialogueValidator.Validate(
                Graph("c", Choice("c", "f", Option("a", "", Flag("x"))), Line("f")))), Is.False);
            Assert.That(DialogueValidator.HasErrors(DialogueValidator.Validate(
                Graph("c", Choice("c", "", Option("a", "", Flag("x")), Option("b"))))), Is.False);
        }

        [Test]
        public void AnUnreachableNode_IsAWarning()
        {
            var issue = Single(Graph("a", Line("a"), Line("orphan")), DialogueIssueSeverity.Warning, "can't be reached");
            Assert.That(issue.NodeId, Is.EqualTo("orphan"));
        }

        [Test]
        public void ALoopWithNoWayOut_IsAnError_ALoopWithOneIsNot()
        {
            var trapped = Graph("a", Line("a", "b"), Choice("b", "", Option("again", "a"), Option("other", "a")));
            Single(trapped, DialogueIssueSeverity.Error, "loop with no way out");

            var exit = Graph("a", Line("a", "b"), Choice("b", "", Option("again", "a"), Option("leave")));
            Assert.That(DialogueValidator.Validate(exit), Is.Empty);
        }

        [Test]
        public void AnUnconditionalBranchsElse_IsNotAnExit()
        {
            // The else link of a branch with no conditions is never taken.
            var def = Graph("a", Line("a", "b"), DialogueNode.CreateBranch("b", null, "a", ""));
            Single(def, DialogueIssueSeverity.Error, "loop with no way out");
            Single(def, DialogueIssueSeverity.Warning, "no conditions, so it always goes to next");
        }

        [Test]
        public void BranchesLoopingIntoEachOther_IsAnError()
        {
            var def = Graph("x",
                DialogueNode.CreateBranch("x", new[] { Flag("a") }, "y", "out"),
                DialogueNode.CreateBranch("y", new[] { Flag("b") }, "x", "out"),
                Line("out"));
            Single(def, DialogueIssueSeverity.Error, "Branch nodes loop into each other");
        }

        [Test]
        public void ChoseConditions_NamingAMissingNodeOrOption_AreWarnings()
        {
            Single(Graph("b", DialogueNode.CreateBranch("b", new[] { FactCondition.Choice(true, "", "nope") }, "", "")),
                   DialogueIssueSeverity.Warning, "isn't a choice node");
            Single(Graph("b", DialogueNode.CreateBranch("b", new[] { FactCondition.Choice(true, "", "c", "zz") }, "c", ""),
                         Choice("c", "", Option("a"))),
                   DialogueIssueSeverity.Warning, "option 'zz'");
            // Another conversation's nodes aren't checked.
            Assert.That(DialogueValidator.Validate(
                Graph("b", DialogueNode.CreateBranch("b", new[] { FactCondition.Choice(true, "other.talk", "x") }, "", ""))),
                Is.Empty);
        }

        [Test]
        public void AnOptionLeadingStraightToAChoiceWithNoPrompt_IsAWarning()
        {
            var chained = Graph("one", Choice("one", "", Option("x", "two")), Choice("two", "", Option("y")));
            var issue = Single(chained, DialogueIssueSeverity.Warning, "under the question just answered");
            Assert.That(issue.NodeId, Is.EqualTo("one"));

            var prompted = Graph("one", Choice("one", "", Option("x", "two")),
                                 DialogueNode.CreateChoice("two", "And then?", DialogueEmote.Talk, "", Option("y")));
            Assert.That(DialogueValidator.Validate(prompted), Is.Empty);
        }

        [Test]
        public void ConditionsAndEffectsWithNoKey_AreWarnings()
        {
            Single(Graph("b", DialogueNode.CreateBranch("b", new[] { Flag("") }, "", "")),
                   DialogueIssueSeverity.Warning, "no fact key");
            Single(Graph("c", Choice("c", "", DialogueOption.Create("a", "A", "", effects: new[] { FactEffect.Set("", FactValue.Of(1)) }))),
                   DialogueIssueSeverity.Warning, "effect with no fact key");
        }

        [Test]
        public void EmptyAndOverlongText_AreWarnings()
        {
            Single(Graph("a", Line("a", "", " ")), DialogueIssueSeverity.Warning, "line has no text");
            Single(Graph("c", Choice("c", "", DialogueOption.Create("a", "", ""))), DialogueIssueSeverity.Warning, "has no text");
            Single(Graph("c", Choice("c", "", DialogueOption.Create("a", new string('x', DialogueValidator.MaxOptionLength + 1), ""))),
                   DialogueIssueSeverity.Warning, "may not fit");
        }
    }
}
