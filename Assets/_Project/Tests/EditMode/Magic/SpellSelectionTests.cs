using NUnit.Framework;
using Pickleball.Sandbox.Magic;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// Spell selection: the slot arithmetic (<see cref="SpellSelection"/>: wrap,
    /// skip empty slots, never stall) and the <see cref="SpellSelector"/>
    /// component that hands the selected spell to the caster.
    /// </summary>
    public class SpellSelectionTests : MagicTestFixture
    {
        SpellDefinition _a, _b, _c;

        [SetUp]
        public void CreateSpells()
        {
            _a = NewAsset<SpellDefinition>();
            _b = NewAsset<SpellDefinition>();
            _c = NewAsset<SpellDefinition>();
        }

        // ------------------------------------------------------- arithmetic

        [Test]
        public void Cycle_Forward_Wraps()
        {
            var spells = new[] { _a, _b, _c };
            Assert.That(SpellSelection.Cycle(spells, 0, +1), Is.EqualTo(1));
            Assert.That(SpellSelection.Cycle(spells, 1, +1), Is.EqualTo(2));
            Assert.That(SpellSelection.Cycle(spells, 2, +1), Is.EqualTo(0));
        }

        [Test]
        public void Cycle_Backward_Wraps()
        {
            var spells = new[] { _a, _b, _c };
            Assert.That(SpellSelection.Cycle(spells, 0, -1), Is.EqualTo(2));
            Assert.That(SpellSelection.Cycle(spells, 2, -1), Is.EqualTo(1));
        }

        [Test]
        public void Cycle_SkipsEmptySlots()
        {
            var spells = new[] { _a, null, null, _c, null };
            Assert.That(SpellSelection.Cycle(spells, 0, +1), Is.EqualTo(3));
            Assert.That(SpellSelection.Cycle(spells, 3, +1), Is.EqualTo(0));
            Assert.That(SpellSelection.Cycle(spells, 0, -1), Is.EqualTo(3));
        }

        [Test]
        public void Cycle_LargeDirection_IsOneStep()
        {
            var spells = new[] { _a, _b, _c };
            Assert.That(SpellSelection.Cycle(spells, 0, +5), Is.EqualTo(1));
            Assert.That(SpellSelection.Cycle(spells, 0, -5), Is.EqualTo(2));
        }

        [Test]
        public void Cycle_SingleSpell_LandsOnItself()
        {
            var spells = new[] { null, _b, null };
            Assert.That(SpellSelection.Cycle(spells, 1, +1), Is.EqualTo(1));
            Assert.That(SpellSelection.Cycle(spells, 1, -1), Is.EqualTo(1));
        }

        [Test]
        public void Cycle_FromNothingSelected_StartsAtAnEnd()
        {
            var spells = new[] { _a, _b, _c };
            Assert.That(SpellSelection.Cycle(spells, -1, +1), Is.EqualTo(0));
            Assert.That(SpellSelection.Cycle(spells, -1, -1), Is.EqualTo(2));
            Assert.That(SpellSelection.Cycle(spells, 99, +1), Is.EqualTo(0));
        }

        [Test]
        public void Cycle_NoSpells_IsMinusOne()
        {
            Assert.That(SpellSelection.Cycle(new SpellDefinition[] { null, null }, 0, +1), Is.EqualTo(-1));
            Assert.That(SpellSelection.Cycle(new SpellDefinition[0], 0, +1), Is.EqualTo(-1));
            Assert.That(SpellSelection.Cycle(null, 0, +1), Is.EqualTo(-1));
        }

        [Test]
        public void Cycle_ZeroDirection_KeepsAUsableSlot()
        {
            var spells = new[] { null, _b, _c };
            Assert.That(SpellSelection.Cycle(spells, 2, 0), Is.EqualTo(2));
            Assert.That(SpellSelection.Cycle(spells, 0, 0), Is.EqualTo(1), "empty current falls back to the first filled");
        }

        [Test]
        public void IsUsable_RangeAndEmptiness()
        {
            var spells = new[] { _a, null };
            Assert.That(SpellSelection.IsUsable(spells, 0), Is.True);
            Assert.That(SpellSelection.IsUsable(spells, 1), Is.False);
            Assert.That(SpellSelection.IsUsable(spells, 2), Is.False);
            Assert.That(SpellSelection.IsUsable(spells, -1), Is.False);
            Assert.That(SpellSelection.FirstUsable(new[] { null, null, _c }), Is.EqualTo(2));
        }

        // -------------------------------------------------------- component

        SpellSelector NewSelector(out SpellCaster caster, params SpellDefinition[] spells)
        {
            caster = NewComponent<SpellCaster>();
            var selector = caster.gameObject.AddComponent<SpellSelector>();
            SetReference(selector, "caster", caster);
            SetArray(selector, "spells", spells);
            return selector;
        }

        [Test]
        public void Select_HandsTheSpellToTheCaster_AndAnnouncesIt()
        {
            var selector = NewSelector(out var caster, _a, _b, _c);
            int changed = 0;
            selector.SelectionChanged += _ => changed++;

            Assert.That(selector.Select(1), Is.True);
            Assert.That(caster.Spell, Is.SameAs(_b));
            Assert.That(selector.SelectedIndex, Is.EqualTo(1));
            Assert.That(selector.Selected, Is.SameAs(_b));
            Assert.That(changed, Is.EqualTo(1));
        }

        [Test]
        public void Select_SameEmptyOrOutOfRange_DoesNothing()
        {
            var selector = NewSelector(out var caster, _a, null, _c);
            selector.Select(0);
            int changed = 0;
            selector.SelectionChanged += _ => changed++;

            Assert.That(selector.Select(0), Is.False, "already selected");
            Assert.That(selector.Select(1), Is.False, "empty slot");
            Assert.That(selector.Select(7), Is.False, "out of range");
            Assert.That(caster.Spell, Is.SameAs(_a));
            Assert.That(changed, Is.EqualTo(0));
        }

        [Test]
        public void Cycle_WalksTheFilledSlots()
        {
            var selector = NewSelector(out var caster, _a, null, _c);
            selector.Select(0);
            Assert.That(selector.Cycle(+1), Is.True);
            Assert.That(caster.Spell, Is.SameAs(_c));
            Assert.That(selector.Cycle(+1), Is.True);
            Assert.That(caster.Spell, Is.SameAs(_a));
            Assert.That(selector.Cycle(-1), Is.True);
            Assert.That(caster.Spell, Is.SameAs(_c));
        }

        [Test]
        public void CasterSetSpell_RaisesSpellChangedOnlyOnChange()
        {
            var caster = NewComponent<SpellCaster>();
            int changed = 0;
            caster.SpellChanged += _ => changed++;
            caster.SetSpell(_a);
            caster.SetSpell(_a);
            caster.SetSpell(_b);
            Assert.That(changed, Is.EqualTo(2));
            Assert.That(caster.Spell, Is.SameAs(_b));
        }
    }
}
