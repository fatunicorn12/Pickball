using System;
using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// <see cref="ChoiceStore"/> against every rule in <see cref="IChoiceStore"/>'s
    /// docs: the append-only log stamped by the injected clock, HasChosen's
    /// "anything at that node", LastChoice's latest pick, FactChanged only on a
    /// real change, ClearFact's event only when the fact existed, ResetAll's
    /// per-fact events then Reset, a load's single Reset, bad keys ignored
    /// without throwing, and listeners isolated from each other.
    /// </summary>
    public class ChoiceStoreTests : ChoiceTestFixture
    {
        // ---- log ----

        [Test]
        public void NewStore_IsEmpty_AndItsViewsAreNeverNull()
        {
            var store = NewStore();
            Assert.That(store.Choices, Is.Not.Null.And.Empty);
            Assert.That(store.Facts, Is.Not.Null.And.Empty);
            Assert.That(store.HasChosen("d", "n"), Is.False);
            Assert.That(store.LastChoice("d", "n"), Is.Null);
        }

        [Test]
        public void RecordChoice_AppendsInOrder_StampedWithTheClock_AndRaisesChoiceRecorded()
        {
            var store = NewStore();
            var heard = new List<ChoiceRecord>();
            store.ChoiceRecorded += heard.Add;

            Now = 1.5;
            store.RecordChoice("birdy", "ask", "polite");
            Now = 4.25;
            store.RecordChoice("birdy", "ask", "rude");

            Assert.That(store.Choices.Count, Is.EqualTo(2));
            Assert.That(store.Choices[0].ChoiceId, Is.EqualTo("polite"));
            Assert.That(store.Choices[0].Time, Is.EqualTo(1.5));
            Assert.That(store.Choices[1].ChoiceId, Is.EqualTo("rude"));
            Assert.That(store.Choices[1].Time, Is.EqualTo(4.25));
            Assert.That(heard, Is.EqualTo(store.Choices));
            Assert.That(heard[1].DialogueId, Is.EqualTo("birdy"));
            Assert.That(heard[1].NodeId, Is.EqualTo("ask"));
        }

        [Test]
        public void RecordChoice_SamePickTwice_IsLoggedTwice()
        {
            var store = NewStore();
            store.RecordChoice("d", "n", "a");
            store.RecordChoice("d", "n", "a");
            Assert.That(store.Choices.Count, Is.EqualTo(2));
        }

        [TestCase(null, "n", "c")]
        [TestCase("", "n", "c")]
        [TestCase("d", null, "c")]
        [TestCase("d", "", "c")]
        [TestCase("d", "n", null)]
        [TestCase("d", "n", "")]
        public void RecordChoice_WithAnEmptyId_IsIgnoredWithAWarning_NotThrown(string dialogue, string node, string choice)
        {
            var warnings = new List<string>();
            var store = NewStore(warnings);
            int raised = 0;
            store.ChoiceRecorded += _ => raised++;

            Assert.DoesNotThrow(() => store.RecordChoice(dialogue, node, choice));

            Assert.That(store.Choices, Is.Empty);
            Assert.That(raised, Is.Zero);
            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("RecordChoice"));
        }

        [Test]
        public void Choices_IsALiveReadOnlyView()
        {
            var store = NewStore();
            IReadOnlyList<ChoiceRecord> view = store.Choices;
            store.RecordChoice("d", "n", "c");

            Assert.That(view.Count, Is.EqualTo(1), "The view is live, not a snapshot.");
            var asCollection = (ICollection<ChoiceRecord>)view;
            Assert.That(asCollection.IsReadOnly, Is.True);
            Assert.Throws<NotSupportedException>(() => asCollection.Clear());
        }

        // ---- HasChosen / LastChoice ----

        [Test]
        public void HasChosen_ASpecificOption()
        {
            var store = NewStore();
            store.RecordChoice("birdy", "ask", "polite");

            Assert.That(store.HasChosen("birdy", "ask", "polite"), Is.True);
            Assert.That(store.HasChosen("birdy", "ask", "rude"), Is.False);
            Assert.That(store.HasChosen("birdy", "other", "polite"), Is.False, "Another node.");
            Assert.That(store.HasChosen("newton", "ask", "polite"), Is.False, "Another dialogue.");
        }

        [Test]
        public void HasChosen_NullOrEmptyChoiceId_MeansAnythingAtThatNode()
        {
            var store = NewStore();
            Assert.That(store.HasChosen("birdy", "ask"), Is.False);

            store.RecordChoice("birdy", "ask", "polite");

            Assert.That(store.HasChosen("birdy", "ask"), Is.True);
            Assert.That(store.HasChosen("birdy", "ask", null), Is.True);
            Assert.That(store.HasChosen("birdy", "ask", ""), Is.True);
            Assert.That(store.HasChosen("birdy", "other"), Is.False);
        }

        [TestCase(null, "ask")]
        [TestCase("", "ask")]
        [TestCase("birdy", null)]
        [TestCase("birdy", "")]
        public void HasChosen_AndLastChoice_WithAnEmptyDialogueOrNode_FindNothing(string dialogue, string node)
        {
            var store = NewStore();
            store.RecordChoice("birdy", "ask", "polite");

            Assert.That(store.HasChosen(dialogue, node), Is.False);
            Assert.That(store.HasChosen(dialogue, node, "polite"), Is.False);
            Assert.That(store.LastChoice(dialogue, node), Is.Null);
        }

        [Test]
        public void HasChosen_IsCaseSensitive()
        {
            var store = NewStore();
            store.RecordChoice("birdy", "ask", "polite");
            Assert.That(store.HasChosen("Birdy", "ask", "polite"), Is.False);
            Assert.That(store.HasChosen("birdy", "ask", "Polite"), Is.False);
        }

        [Test]
        public void LastChoice_IsTheLatestPickAtThatNode()
        {
            var store = NewStore();
            Assert.That(store.LastChoice("birdy", "ask"), Is.Null);

            store.RecordChoice("birdy", "ask", "polite");
            store.RecordChoice("birdy", "other", "x");
            store.RecordChoice("birdy", "ask", "rude");
            store.RecordChoice("newton", "ask", "apple");

            Assert.That(store.LastChoice("birdy", "ask"), Is.EqualTo("rude"));
            Assert.That(store.LastChoice("birdy", "other"), Is.EqualTo("x"));
            Assert.That(store.LastChoice("newton", "ask"), Is.EqualTo("apple"));
            Assert.That(store.LastChoice("birdy", "never"), Is.Null);
        }

        // ---- facts ----

        [Test]
        public void TryGetFact_MissingOrEmptyKey_IsFalseWithTheDefaultValue()
        {
            var warnings = new List<string>();
            var store = NewStore(warnings);

            Assert.That(store.TryGetFact("nope", out FactValue value), Is.False);
            Assert.That(value, Is.EqualTo(default(FactValue)));
            Assert.That(store.TryGetFact(null, out _), Is.False);
            Assert.That(store.TryGetFact("", out _), Is.False);
            Assert.That(warnings, Is.Empty, "Reads with a bad key are quiet.");
        }

        [Test]
        public void SetFact_NewKey_StoresIt_AndRaisesWithExistedFalse()
        {
            var store = NewStore();
            var changes = new List<FactChange>();
            store.FactChanged += changes.Add;

            store.SetFact("met_birdy", FactValue.Of(true));

            Assert.That(store.TryGetFact("met_birdy", out FactValue value), Is.True);
            Assert.That(value, Is.EqualTo(FactValue.Of(true)));
            Assert.That(store.Facts["met_birdy"], Is.EqualTo(FactValue.Of(true)));
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Key, Is.EqualTo("met_birdy"));
            Assert.That(changes[0].Existed, Is.False);
            Assert.That(changes[0].Exists, Is.True);
            Assert.That(changes[0].Current, Is.EqualTo(FactValue.Of(true)));
        }

        [Test]
        public void SetFact_SameValueAgain_RaisesNothing()
        {
            var store = NewStore();
            store.SetFact("rep", FactValue.Of(3));
            int raised = 0;
            store.FactChanged += _ => raised++;

            store.SetFact("rep", FactValue.Of(3));

            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void SetFact_NewValue_RaisesWithThePreviousOne()
        {
            var store = NewStore();
            store.SetFact("rep", FactValue.Of(3));
            var changes = new List<FactChange>();
            store.FactChanged += changes.Add;

            store.SetFact("rep", FactValue.Of(5));

            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Existed, Is.True);
            Assert.That(changes[0].Previous, Is.EqualTo(FactValue.Of(3)));
            Assert.That(changes[0].Exists, Is.True);
            Assert.That(changes[0].Current, Is.EqualTo(FactValue.Of(5)));
            Assert.That(store.GetInt("rep"), Is.EqualTo(5));
        }

        [Test]
        public void SetFact_SamePayloadAsAnotherType_IsAChange()
        {
            var store = NewStore();
            store.SetFact("flag", FactValue.Of(true));
            int raised = 0;
            store.FactChanged += _ => raised++;

            store.SetFact("flag", FactValue.Of(1));

            Assert.That(raised, Is.EqualTo(1));
            Assert.That(store.Facts["flag"].Type, Is.EqualTo(FactType.Int));
        }

        [TestCase(null)]
        [TestCase("")]
        public void SetFact_EmptyKey_IsIgnoredWithAWarning(string key)
        {
            var warnings = new List<string>();
            var store = NewStore(warnings);
            int raised = 0;
            store.FactChanged += _ => raised++;

            Assert.DoesNotThrow(() => store.SetFact(key, FactValue.Of(1)));

            Assert.That(store.Facts, Is.Empty);
            Assert.That(raised, Is.Zero);
            Assert.That(warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void Facts_IsALiveReadOnlyView()
        {
            var store = NewStore();
            IReadOnlyDictionary<string, FactValue> view = store.Facts;
            store.SetFact("a", FactValue.Of(1));

            Assert.That(view.Count, Is.EqualTo(1));
            var asDictionary = (IDictionary<string, FactValue>)view;
            Assert.Throws<NotSupportedException>(() => asDictionary.Add("b", FactValue.Of(2)));
            Assert.Throws<NotSupportedException>(() => asDictionary.Remove("a"));
        }

        [Test]
        public void ClearFact_Existing_RemovesIt_ReturnsTrue_AndRaisesWithExistsFalse()
        {
            var store = NewStore();
            store.SetFact("rep", FactValue.Of(7));
            var changes = new List<FactChange>();
            store.FactChanged += changes.Add;

            Assert.That(store.ClearFact("rep"), Is.True);

            Assert.That(store.TryGetFact("rep", out _), Is.False);
            Assert.That(changes.Count, Is.EqualTo(1));
            Assert.That(changes[0].Key, Is.EqualTo("rep"));
            Assert.That(changes[0].Existed, Is.True);
            Assert.That(changes[0].Previous, Is.EqualTo(FactValue.Of(7)));
            Assert.That(changes[0].Exists, Is.False);
        }

        [Test]
        public void ClearFact_Missing_ReturnsFalse_AndRaisesNothing()
        {
            var store = NewStore();
            int raised = 0;
            store.FactChanged += _ => raised++;

            Assert.That(store.ClearFact("never_set"), Is.False);
            Assert.That(raised, Is.Zero);
        }

        [TestCase(null)]
        [TestCase("")]
        public void ClearFact_EmptyKey_ReturnsFalseWithAWarning(string key)
        {
            var warnings = new List<string>();
            var store = NewStore(warnings);
            Assert.That(store.ClearFact(key), Is.False);
            Assert.That(warnings.Count, Is.EqualTo(1));
        }

        // ---- ResetAll / Restore ----

        [Test]
        public void ResetAll_EmptiesEverything_RaisingFactChangedPerFactInKeyOrder_ThenReset()
        {
            var store = NewStore();
            store.RecordChoice("d", "n", "c");
            store.SetFact("b", FactValue.Of(2));
            store.SetFact("a", FactValue.Of(true));

            var events = new List<string>();
            store.FactChanged += change =>
            {
                events.Add($"fact:{change.Key}:{change.Existed}->{change.Exists}");
                Assert.That(store.Facts, Is.Empty, "Listeners see the final, empty state.");
                Assert.That(store.Choices, Is.Empty);
            };
            store.Reset += () => events.Add("reset");

            store.ResetAll();

            Assert.That(events, Is.EqualTo(new[] { "fact:a:True->False", "fact:b:True->False", "reset" }));
            Assert.That(store.Choices, Is.Empty);
            Assert.That(store.Facts, Is.Empty);
        }

        [Test]
        public void ResetAll_OnAnEmptyStore_RaisesOnlyReset()
        {
            var store = NewStore();
            int facts = 0, resets = 0;
            store.FactChanged += _ => facts++;
            store.Reset += () => resets++;

            store.ResetAll();

            Assert.That(facts, Is.Zero);
            Assert.That(resets, Is.EqualTo(1));
        }

        [Test]
        public void Restore_ReplacesTheState_RaisingResetOnce_AndNoFactChanged()
        {
            var store = NewStore();
            store.RecordChoice("old", "n", "c");
            store.SetFact("old_fact", FactValue.Of(1));
            int facts = 0, resets = 0, picks = 0;
            store.FactChanged += _ => facts++;
            store.Reset += () => resets++;
            store.ChoiceRecorded += _ => picks++;

            store.Restore(
                new[] { new ChoiceRecord("birdy", "ask", "polite", 12.5) },
                new[]
                {
                    new KeyValuePair<string, FactValue>("met_birdy", FactValue.Of(true)),
                    new KeyValuePair<string, FactValue>("rep", FactValue.Of(4)),
                });

            Assert.That(resets, Is.EqualTo(1));
            Assert.That(facts, Is.Zero);
            Assert.That(picks, Is.Zero);
            Assert.That(store.Choices.Count, Is.EqualTo(1));
            Assert.That(store.Choices[0].Time, Is.EqualTo(12.5), "Saved times are kept, not restamped.");
            Assert.That(store.HasChosen("old", "n"), Is.False);
            Assert.That(store.TryGetFact("old_fact", out _), Is.False);
            Assert.That(store.GetBool("met_birdy"), Is.True);
            Assert.That(store.GetInt("rep"), Is.EqualTo(4));
        }

        [Test]
        public void Restore_SkipsEntriesWithEmptyIds_WithOneWarning_AndTheLastDuplicateKeyWins()
        {
            var warnings = new List<string>();
            var store = NewStore(warnings);

            store.Restore(
                new[] { new ChoiceRecord("d", "n", "c", 1), new ChoiceRecord("d", "", "c", 2), default },
                new[]
                {
                    new KeyValuePair<string, FactValue>("", FactValue.Of(1)),
                    new KeyValuePair<string, FactValue>("k", FactValue.Of(1)),
                    new KeyValuePair<string, FactValue>("k", FactValue.Of(2)),
                });

            Assert.That(store.Choices.Count, Is.EqualTo(1));
            Assert.That(store.Facts.Count, Is.EqualTo(1));
            Assert.That(store.GetInt("k"), Is.EqualTo(2));
            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("3"));
        }

        [Test]
        public void Restore_WithNulls_EmptiesTheStore()
        {
            var store = NewStore();
            store.SetFact("a", FactValue.Of(1));
            store.Restore(null, null);
            Assert.That(store.Facts, Is.Empty);
            Assert.That(store.Choices, Is.Empty);
        }

        // ---- listener isolation ----

        [Test]
        public void AThrowingListener_WithASink_IsReported_AndTheOthersStillHear()
        {
            var errors = new List<Exception>();
            var store = NewStore(listenerError: errors.Add);
            int second = 0;
            store.FactChanged += _ => throw new InvalidOperationException("boom");
            store.FactChanged += _ => second++;

            Assert.DoesNotThrow(() => store.SetFact("a", FactValue.Of(1)));

            Assert.That(second, Is.EqualTo(1));
            Assert.That(errors.Count, Is.EqualTo(1));
            Assert.That(store.GetInt("a"), Is.EqualTo(1));
        }

        [Test]
        public void AThrowingListener_WithoutASink_IsRethrown_AfterTheOthersHeardAndTheStateIsFinal()
        {
            var store = NewStore();
            store.SetFact("a", FactValue.Of(1));
            var heard = new List<string>();
            store.FactChanged += _ => throw new InvalidOperationException("boom");
            store.FactChanged += change => heard.Add(change.Key);
            store.Reset += () => heard.Add("reset");

            Assert.Throws<InvalidOperationException>(() => store.ResetAll());

            Assert.That(heard, Is.EqualTo(new[] { "a", "reset" }), "Reset still fires after a FactChanged listener threw.");
            Assert.That(store.Facts, Is.Empty);
        }

        // ---- clock and extensions ----

        [Test]
        public void DefaultClock_IsAStopwatch_NonNegativeAndNonDecreasing()
        {
            var store = new ChoiceStore();
            double first = store.Now;
            store.RecordChoice("d", "n", "c");
            Assert.That(first, Is.GreaterThanOrEqualTo(0));
            Assert.That(store.Choices[0].Time, Is.GreaterThanOrEqualTo(first));
        }

        [Test]
        public void TypedReads_ConvertAndFallBack()
        {
            var store = NewStore();
            store.SetFact("n", FactValue.Of(3));
            store.SetFact("s", FactValue.Of("12"));

            Assert.That(store.GetInt("n"), Is.EqualTo(3));
            Assert.That(store.GetBool("n"), Is.True);
            Assert.That(store.GetString("n"), Is.EqualTo("3"));
            Assert.That(store.GetInt("s"), Is.EqualTo(12));
            Assert.That(store.GetInt("missing", 9), Is.EqualTo(9));
            Assert.That(store.GetBool("missing", true), Is.True);
            Assert.That(store.GetString("missing", "x"), Is.EqualTo("x"));

            IChoiceStore none = null;
            Assert.That(none.GetInt("n", 5), Is.EqualTo(5));
            Assert.That(none.GetBool("n"), Is.False);
            Assert.That(none.GetString("n"), Is.EqualTo(""));
        }
    }
}
