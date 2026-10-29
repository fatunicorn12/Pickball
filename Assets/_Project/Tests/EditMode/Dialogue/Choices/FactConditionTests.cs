using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// <see cref="FactCondition"/>, every kind, against a real
    /// <see cref="ChoiceStore"/> and against no store at all (which must act
    /// like an empty one), including the Chose kinds' fallback to the
    /// conversation the condition sits in.
    /// </summary>
    public class FactConditionTests : ChoiceTestFixture
    {
        static FactCondition Fact(FactConditionKind kind, string key, FactValue value = default)
            => FactCondition.Fact(kind, key, value);

        // ---- FactEquals / FactNotEquals ----

        [Test]
        public void FactEquals_ComparesTheStoredValue()
        {
            var store = NewStore();
            store.SetFact("rep", FactValue.Of(3));

            Assert.That(Fact(FactConditionKind.FactEquals, "rep", FactValue.Of(3)).Evaluate(store), Is.True);
            Assert.That(Fact(FactConditionKind.FactEquals, "rep", FactValue.Of(4)).Evaluate(store), Is.False);
            Assert.That(Fact(FactConditionKind.FactNotEquals, "rep", FactValue.Of(4)).Evaluate(store), Is.True);
            Assert.That(Fact(FactConditionKind.FactNotEquals, "rep", FactValue.Of(3)).Evaluate(store), Is.False);
        }

        [Test]
        public void FactEquals_ComparesInTheAuthoredType()
        {
            var store = NewStore();
            store.SetFact("int_one", FactValue.Of(1));
            store.SetFact("text_five", FactValue.Of("5"));
            store.SetFact("flag", FactValue.Of(true));

            Assert.That(Fact(FactConditionKind.FactEquals, "int_one", FactValue.Of(true)).Evaluate(store), Is.True,
                "An int 1 meets 'flag == true'.");
            Assert.That(Fact(FactConditionKind.FactEquals, "text_five", FactValue.Of(5)).Evaluate(store), Is.True);
            Assert.That(Fact(FactConditionKind.FactEquals, "flag", FactValue.Of("true")).Evaluate(store), Is.True);
            Assert.That(Fact(FactConditionKind.FactEquals, "flag", FactValue.Of("True")).Evaluate(store), Is.False,
                "String comparison is ordinal.");
        }

        [Test]
        public void FactEquals_AMissingFact_ReadsAsTheAuthoredTypesDefault()
        {
            var store = NewStore();

            Assert.That(Fact(FactConditionKind.FactEquals, "x", FactValue.Of(false)).Evaluate(store), Is.True);
            Assert.That(Fact(FactConditionKind.FactEquals, "x", FactValue.Of(0)).Evaluate(store), Is.True);
            Assert.That(Fact(FactConditionKind.FactEquals, "x", FactValue.Of("")).Evaluate(store), Is.True);
            Assert.That(Fact(FactConditionKind.FactEquals, "x", FactValue.Of(true)).Evaluate(store), Is.False);
            Assert.That(Fact(FactConditionKind.FactNotEquals, "x", FactValue.Of(true)).Evaluate(store), Is.True);
        }

        // ---- FactAtLeast / FactAtMost ----

        [TestCase(2, 3, false)]
        [TestCase(3, 3, true)]
        [TestCase(4, 3, true)]
        [TestCase(-1, 0, false)]
        public void FactAtLeast(int fact, int threshold, bool expected)
        {
            var store = NewStore();
            store.SetFact("rep", FactValue.Of(fact));
            Assert.That(Fact(FactConditionKind.FactAtLeast, "rep", FactValue.Of(threshold)).Evaluate(store), Is.EqualTo(expected));
        }

        [TestCase(2, 3, true)]
        [TestCase(3, 3, true)]
        [TestCase(4, 3, false)]
        public void FactAtMost(int fact, int threshold, bool expected)
        {
            var store = NewStore();
            store.SetFact("rep", FactValue.Of(fact));
            Assert.That(Fact(FactConditionKind.FactAtMost, "rep", FactValue.Of(threshold)).Evaluate(store), Is.EqualTo(expected));
        }

        [Test]
        public void FactAtLeast_ReadsBoolsAndStringsAsInts_AndAMissingFactAsZero()
        {
            var store = NewStore();
            store.SetFact("flag", FactValue.Of(true));
            store.SetFact("text", FactValue.Of("7"));

            Assert.That(Fact(FactConditionKind.FactAtLeast, "flag", FactValue.Of(1)).Evaluate(store), Is.True);
            Assert.That(Fact(FactConditionKind.FactAtLeast, "text", FactValue.Of(7)).Evaluate(store), Is.True);
            Assert.That(Fact(FactConditionKind.FactAtLeast, "missing", FactValue.Of(0)).Evaluate(store), Is.True);
            Assert.That(Fact(FactConditionKind.FactAtLeast, "missing", FactValue.Of(1)).Evaluate(store), Is.False);
            Assert.That(Fact(FactConditionKind.FactAtMost, "missing", FactValue.Of(0)).Evaluate(store), Is.True);
        }

        // ---- FactExists / FactMissing ----

        [Test]
        public void FactExists_AndFactMissing()
        {
            var store = NewStore();
            store.SetFact("met", FactValue.Of(false));

            Assert.That(Fact(FactConditionKind.FactExists, "met").Evaluate(store), Is.True, "Set to false still exists.");
            Assert.That(Fact(FactConditionKind.FactMissing, "met").Evaluate(store), Is.False);
            Assert.That(Fact(FactConditionKind.FactExists, "never").Evaluate(store), Is.False);
            Assert.That(Fact(FactConditionKind.FactMissing, "never").Evaluate(store), Is.True);

            store.ClearFact("met");
            Assert.That(Fact(FactConditionKind.FactExists, "met").Evaluate(store), Is.False);
            Assert.That(Fact(FactConditionKind.FactMissing, "met").Evaluate(store), Is.True);
        }

        [Test]
        public void ANullKey_ReadsAsEmpty_AndIsNeverSet()
        {
            var store = NewStore();
            FactCondition condition = Fact(FactConditionKind.FactExists, null);
            Assert.That(condition.Key, Is.EqualTo(""));
            Assert.That(condition.Evaluate(store), Is.False);
            Assert.That(Fact(FactConditionKind.FactMissing, null).Evaluate(store), Is.True);
        }

        // ---- Chose / NotChose ----

        [Test]
        public void Chose_ASpecificOption_AndNotChose()
        {
            var store = NewStore();
            store.RecordChoice("birdy", "ask", "polite");

            Assert.That(FactCondition.Choice(true, "birdy", "ask", "polite").Evaluate(store), Is.True);
            Assert.That(FactCondition.Choice(true, "birdy", "ask", "rude").Evaluate(store), Is.False);
            Assert.That(FactCondition.Choice(false, "birdy", "ask", "rude").Evaluate(store), Is.True);
            Assert.That(FactCondition.Choice(false, "birdy", "ask", "polite").Evaluate(store), Is.False);
        }

        [Test]
        public void Chose_WithAnEmptyChoiceId_MeansAnyOptionAtThatNode()
        {
            var store = NewStore();
            FactCondition any = FactCondition.Choice(true, "birdy", "ask");
            FactCondition none = FactCondition.Choice(false, "birdy", "ask");
            Assert.That(any.Kind, Is.EqualTo(FactConditionKind.Chose));
            Assert.That(none.Kind, Is.EqualTo(FactConditionKind.NotChose));

            Assert.That(any.Evaluate(store), Is.False);
            Assert.That(none.Evaluate(store), Is.True);

            store.RecordChoice("birdy", "ask", "rude");

            Assert.That(any.Evaluate(store), Is.True);
            Assert.That(none.Evaluate(store), Is.False);
        }

        [Test]
        public void Chose_WithAnEmptyDialogueId_UsesTheCurrentConversation()
        {
            var store = NewStore();
            store.RecordChoice("birdy", "ask", "polite");
            FactCondition here = FactCondition.Choice(true, "", "ask", "polite");
            FactCondition notHere = FactCondition.Choice(false, null, "ask", "polite");

            Assert.That(here.Evaluate(store, "birdy"), Is.True);
            Assert.That(here.Evaluate(store, "newton"), Is.False);
            Assert.That(notHere.Evaluate(store, "birdy"), Is.False);
            Assert.That(notHere.Evaluate(store, "newton"), Is.True);
        }

        [Test]
        public void Chose_WithNoDialogueIdAnywhere_IsNeverMet()
        {
            var store = NewStore();
            store.RecordChoice("birdy", "ask", "polite");
            FactCondition here = FactCondition.Choice(true, "", "ask", "polite");

            Assert.That(here.Evaluate(store), Is.False);
            Assert.That(here.Evaluate(store, null), Is.False);
            Assert.That(FactCondition.Choice(false, "", "ask").Evaluate(store), Is.True);
        }

        [Test]
        public void Chose_AnExplicitDialogueId_IgnoresTheCurrentOne()
        {
            var store = NewStore();
            store.RecordChoice("birdy", "ask", "polite");
            Assert.That(FactCondition.Choice(true, "birdy", "ask").Evaluate(store, "newton"), Is.True);
            Assert.That(FactCondition.Choice(true, "newton", "ask").Evaluate(store, "birdy"), Is.False);
        }

        // ---- a null store behaves as an empty one ----

        [Test]
        public void ANullStore_ActsLikeAnEmptyOne()
        {
            Assert.That(Fact(FactConditionKind.FactEquals, "x", FactValue.Of(false)).Evaluate(null), Is.True);
            Assert.That(Fact(FactConditionKind.FactEquals, "x", FactValue.Of(0)).Evaluate(null), Is.True);
            Assert.That(Fact(FactConditionKind.FactEquals, "x", FactValue.Of(true)).Evaluate(null), Is.False);
            Assert.That(Fact(FactConditionKind.FactNotEquals, "x", FactValue.Of(true)).Evaluate(null), Is.True);
            Assert.That(Fact(FactConditionKind.FactAtLeast, "x", FactValue.Of(0)).Evaluate(null), Is.True);
            Assert.That(Fact(FactConditionKind.FactAtLeast, "x", FactValue.Of(1)).Evaluate(null), Is.False);
            Assert.That(Fact(FactConditionKind.FactAtMost, "x", FactValue.Of(0)).Evaluate(null), Is.True);
            Assert.That(Fact(FactConditionKind.FactExists, "x").Evaluate(null), Is.False);
            Assert.That(Fact(FactConditionKind.FactMissing, "x").Evaluate(null), Is.True);
            Assert.That(FactCondition.Choice(true, "birdy", "ask").Evaluate(null, "birdy"), Is.False);
            Assert.That(FactCondition.Choice(false, "birdy", "ask").Evaluate(null, "birdy"), Is.True);
        }

        // ---- AllMet ----

        [Test]
        public void AllMet_NullOrEmptyHolds_OneFailureFails()
        {
            var store = NewStore();
            store.SetFact("rep", FactValue.Of(5));
            store.RecordChoice("birdy", "ask", "polite");

            Assert.That(FactCondition.AllMet(null, store), Is.True);
            Assert.That(FactCondition.AllMet(new List<FactCondition>(), store), Is.True);

            var conditions = new List<FactCondition>
            {
                Fact(FactConditionKind.FactAtLeast, "rep", FactValue.Of(5)),
                FactCondition.Choice(true, "", "ask", "polite"),
            };
            Assert.That(FactCondition.AllMet(conditions, store, "birdy"), Is.True);
            Assert.That(FactCondition.AllMet(conditions, store, "newton"), Is.False, "The current dialogue reaches every condition.");

            conditions.Add(Fact(FactConditionKind.FactMissing, "rep"));
            Assert.That(FactCondition.AllMet(conditions, store, "birdy"), Is.False);
        }

        [Test]
        public void Accessors_ReturnWhatTheFactoriesSet_NeverNull()
        {
            FactCondition fact = Fact(FactConditionKind.FactAtMost, "rep", FactValue.Of(2));
            Assert.That(fact.Kind, Is.EqualTo(FactConditionKind.FactAtMost));
            Assert.That(fact.Key, Is.EqualTo("rep"));
            Assert.That(fact.Value, Is.EqualTo(FactValue.Of(2)));
            Assert.That(fact.DialogueId, Is.EqualTo(""));
            Assert.That(fact.NodeId, Is.EqualTo(""));
            Assert.That(fact.ChoiceId, Is.EqualTo(""));

            FactCondition choice = FactCondition.Choice(true, "d", "n", "c");
            Assert.That(choice.DialogueId, Is.EqualTo("d"));
            Assert.That(choice.NodeId, Is.EqualTo("n"));
            Assert.That(choice.ChoiceId, Is.EqualTo("c"));
            Assert.That(choice.Key, Is.EqualTo(""));
        }
    }
}
