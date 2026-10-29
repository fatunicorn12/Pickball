using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox.Dialogue;

namespace Pickleball.Tests.Dialogue
{
    /// <summary>
    /// <see cref="FactEffect"/>: Set stores the value, Add counts a missing fact
    /// as 0, Clear removes the fact, effects apply in order, and a null store or
    /// an empty key is ignored rather than thrown.
    /// </summary>
    public class FactEffectTests : ChoiceTestFixture
    {
        [Test]
        public void Set_StoresTheValue_AndOverwrites()
        {
            var store = NewStore();
            FactEffect.Set("met_birdy", FactValue.Of(true)).Apply(store);
            Assert.That(store.Facts["met_birdy"], Is.EqualTo(FactValue.Of(true)));

            FactEffect.Set("met_birdy", FactValue.Of("yes")).Apply(store);
            Assert.That(store.Facts["met_birdy"], Is.EqualTo(FactValue.Of("yes")));
        }

        [Test]
        public void Add_OnAMissingFact_CountsFromZero_AndStoresAnInt()
        {
            var store = NewStore();
            FactEffect.Add("rep", 3).Apply(store);
            Assert.That(store.Facts["rep"], Is.EqualTo(FactValue.Of(3)));
        }

        [Test]
        public void Add_ToAnExistingFact_ReadsItAsAnInt()
        {
            var store = NewStore();
            store.SetFact("rep", FactValue.Of(3));
            FactEffect.Add("rep", -5).Apply(store);
            Assert.That(store.GetInt("rep"), Is.EqualTo(-2));

            store.SetFact("flag", FactValue.Of(true));
            FactEffect.Add("flag", 2).Apply(store);
            Assert.That(store.Facts["flag"], Is.EqualTo(FactValue.Of(3)), "true reads as 1, and the result is an int.");
        }

        [Test]
        public void Add_Zero_ToAnIntFact_IsNotAChange()
        {
            var store = NewStore();
            store.SetFact("rep", FactValue.Of(3));
            int raised = 0;
            store.FactChanged += _ => raised++;

            FactEffect.Add("rep", 0).Apply(store);

            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void Clear_RemovesTheFact_AndIsQuietWhenItIsMissing()
        {
            var store = NewStore();
            store.SetFact("rep", FactValue.Of(3));
            var changes = new List<FactChange>();
            store.FactChanged += changes.Add;

            FactEffect.Clear("rep").Apply(store);
            FactEffect.Clear("rep").Apply(store);

            Assert.That(store.TryGetFact("rep", out _), Is.False);
            Assert.That(changes.Count, Is.EqualTo(1));
        }

        [Test]
        public void Factories_SetKindKeyAndValue()
        {
            FactEffect set = FactEffect.Set("k", FactValue.Of("v"));
            Assert.That(set.Kind, Is.EqualTo(FactEffectKind.Set));
            Assert.That(set.Key, Is.EqualTo("k"));
            Assert.That(set.Value, Is.EqualTo(FactValue.Of("v")));

            FactEffect add = FactEffect.Add("k", 4);
            Assert.That(add.Kind, Is.EqualTo(FactEffectKind.Add));
            Assert.That(add.Value, Is.EqualTo(FactValue.Of(4)));

            FactEffect clear = FactEffect.Clear("k");
            Assert.That(clear.Kind, Is.EqualTo(FactEffectKind.Clear));
            Assert.That(FactEffect.Clear(null).Key, Is.EqualTo(""));
        }

        [Test]
        public void ANullStore_OrAnEmptyKey_IsIgnored()
        {
            Assert.DoesNotThrow(() => FactEffect.Set("k", FactValue.Of(1)).Apply(null));
            Assert.DoesNotThrow(() => FactEffect.Add("k", 1).Apply(null));
            Assert.DoesNotThrow(() => FactEffect.Clear("k").Apply(null));
            Assert.DoesNotThrow(() => FactEffect.ApplyAll(new[] { FactEffect.Add("k", 1) }, null));

            var warnings = new List<string>();
            var store = NewStore(warnings);
            FactEffect.Set("", FactValue.Of(1)).Apply(store);
            FactEffect.Add(null, 1).Apply(store);
            FactEffect.Clear("").Apply(store);
            Assert.That(store.Facts, Is.Empty);
            Assert.That(warnings, Is.Empty, "The effect skips an empty key before reaching the store.");
        }

        [Test]
        public void ApplyAll_RunsInOrder_AndANullListIsANoOp()
        {
            var store = NewStore();
            FactEffect.ApplyAll(new[]
            {
                FactEffect.Set("rep", FactValue.Of(1)),
                FactEffect.Add("rep", 2),
                FactEffect.Set("met", FactValue.Of(true)),
                FactEffect.Clear("met"),
            }, store);

            Assert.That(store.GetInt("rep"), Is.EqualTo(3));
            Assert.That(store.TryGetFact("met", out _), Is.False);
            Assert.DoesNotThrow(() => FactEffect.ApplyAll(null, store));
        }
    }
}
