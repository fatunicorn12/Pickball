using System.Collections.Generic;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Inventory;

namespace Pickleball.Tests.Inventory
{
    /// <summary>
    /// <see cref="InventoryModel"/>: slot order, ownership, capacity, the
    /// equipped-weapon rules and cycling.
    /// </summary>
    public class InventoryModelTests : InventoryTestFixture
    {
        // ------------------------------------------------------------ slots

        [Test]
        public void Add_KeepsOrder_PerTab()
        {
            var model = NewModel();
            var compass = Weapon("compass");
            var ruler = Weapon("ruler");
            var dash = Key(AbilityKind.Dash);
            Assert.That(model.Add(compass), Is.EqualTo(InventoryAddResult.Added));
            Assert.That(model.Add(dash), Is.EqualTo(InventoryAddResult.Added));
            Assert.That(model.Add(ruler), Is.EqualTo(InventoryAddResult.Added));

            Assert.That(model.Items(InventoryCategory.Weapon), Is.EqualTo(new[] { compass, ruler }));
            Assert.That(model.Items(InventoryCategory.KeyItem), Is.EqualTo(new[] { dash }));
            Assert.That(model.Count(InventoryCategory.Magic), Is.Zero);
            Assert.That(model.IndexOf(ruler), Is.EqualTo(1));
            Assert.That(model.ItemAt(InventoryCategory.Weapon, 5), Is.Null, "an empty slot reads as null");
        }

        [Test]
        public void Add_SameAssetTwice_IsAlreadyOwned()
        {
            var model = NewModel();
            var compass = Weapon("compass");
            model.Add(compass);
            Assert.That(model.Add(compass), Is.EqualTo(InventoryAddResult.AlreadyOwned));
            Assert.That(model.Count(InventoryCategory.Weapon), Is.EqualTo(1));
        }

        [Test]
        public void Add_AnotherAssetWithTheSameId_IsAlreadyOwned()
        {
            var model = NewModel();
            model.Add(Weapon("ruler"));
            var duplicate = Weapon("ruler");
            Assert.That(model.Add(duplicate), Is.EqualTo(InventoryAddResult.AlreadyOwned));
            Assert.That(model.Has(duplicate), Is.True, "same id counts as owned");
            Assert.That(model.Find("ruler"), Is.Not.SameAs(duplicate), "the first asset is the one kept");
        }

        [Test]
        public void Add_Null_IsInvalid()
            => Assert.That(NewModel().Add(null), Is.EqualTo(InventoryAddResult.Invalid));

        [Test]
        public void Remove_ClosesTheGap()
        {
            var model = NewModel();
            var a = Weapon("a");
            var b = Weapon("b");
            var c = Weapon("c");
            model.Add(a);
            model.Add(b);
            model.Add(c);
            Assert.That(model.Remove(b), Is.True);
            Assert.That(model.Items(InventoryCategory.Weapon), Is.EqualTo(new[] { a, c }));
            Assert.That(model.Remove(b), Is.False, "removing what is not owned does nothing");
        }

        [Test]
        public void Events_ReportTheTabThatChanged()
        {
            var model = NewModel();
            var seen = new List<InventoryCategory>();
            model.Changed += seen.Add;
            var dash = Key(AbilityKind.Dash);
            model.Add(dash);
            model.Add(Magic("orb"));
            model.Remove(dash);
            Assert.That(seen, Is.EqualTo(new[]
                { InventoryCategory.KeyItem, InventoryCategory.Magic, InventoryCategory.KeyItem }));
        }

        // ------------------------------------------------------------ capacity

        [Test]
        public void FullTab_RefusesTheItem_OtherTabsUnaffected()
        {
            var model = NewModel(weapons: 2, magic: 1, keys: 1);
            Assert.That(model.CapacityOf(InventoryCategory.Weapon), Is.EqualTo(2));
            model.Add(Weapon("a"));
            model.Add(Weapon("b"));
            var third = Weapon("c");
            Assert.That(model.Add(third), Is.EqualTo(InventoryAddResult.Full));
            Assert.That(model.Has(third), Is.False);
            Assert.That(model.Add(Key(AbilityKind.Dash)), Is.EqualTo(InventoryAddResult.Added));
            Assert.That(model.Add(Key(AbilityKind.Tether)), Is.EqualTo(InventoryAddResult.Full));
        }

        [Test]
        public void FreedSlot_TakesAnItemAgain()
        {
            var model = NewModel(weapons: 1);
            var a = Weapon("a");
            model.Add(a);
            var b = Weapon("b");
            Assert.That(model.Add(b), Is.EqualTo(InventoryAddResult.Full));
            model.Remove(a);
            Assert.That(model.Add(b), Is.EqualTo(InventoryAddResult.Added));
        }

        [Test]
        public void Capacity_FromStruct_PerCategory()
        {
            var capacity = new InventoryCapacity(3, 4, 5);
            Assert.That(capacity.For(InventoryCategory.Weapon), Is.EqualTo(3));
            Assert.That(capacity.For(InventoryCategory.Magic), Is.EqualTo(4));
            Assert.That(capacity.For(InventoryCategory.KeyItem), Is.EqualTo(5));
            Assert.That(new InventoryCapacity(-2, 0, 1).For(InventoryCategory.Weapon), Is.Zero, "never negative");
        }

        // ------------------------------------------------------------ equip

        [Test]
        public void FirstWeaponAdded_IsEquipped_LaterOnesAreNot()
        {
            var model = NewModel();
            var seen = new List<WeaponDefinition>();
            model.EquippedChanged += seen.Add;
            var compass = Weapon("compass");
            model.Add(compass);
            model.Add(Weapon("ruler"));
            Assert.That(model.Equipped, Is.SameAs(compass));
            Assert.That(seen, Is.EqualTo(new[] { compass }));
        }

        [Test]
        public void Equip_OwnedWeapon_ChangesAndRaises()
        {
            var model = NewModel();
            model.Add(Weapon("compass"));
            var ruler = Weapon("ruler");
            model.Add(ruler);
            WeaponDefinition raised = null;
            model.EquippedChanged += w => raised = w;
            Assert.That(model.Equip(ruler), Is.True);
            Assert.That(model.Equipped, Is.SameAs(ruler));
            Assert.That(raised, Is.SameAs(ruler));
        }

        [Test]
        public void Equip_Refuses_UnownedOrAlreadyEquipped()
        {
            var model = NewModel();
            var compass = Weapon("compass");
            model.Add(compass);
            int raised = 0;
            model.EquippedChanged += _ => raised++;
            Assert.That(model.Equip(Weapon("stranger")), Is.False, "not owned");
            Assert.That(model.Equip(compass), Is.False, "already equipped");
            Assert.That(model.Equip(null), Is.False);
            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void RemovingTheEquippedWeapon_EquipsTheOneThatSlidesIn()
        {
            var model = NewModel();
            var a = Weapon("a");
            var b = Weapon("b");
            var c = Weapon("c");
            model.Add(a);
            model.Add(b);
            model.Add(c);
            model.Equip(b);
            model.Remove(b);
            Assert.That(model.Equipped, Is.SameAs(c), "the next weapon moved into its slot");
            model.Remove(c);
            Assert.That(model.Equipped, Is.SameAs(a), "no next one: the new last");
            model.Remove(a);
            Assert.That(model.Equipped, Is.Null, "nothing left");
        }

        [Test]
        public void RemovingAnotherWeapon_KeepsTheEquippedOne()
        {
            var model = NewModel();
            var a = Weapon("a");
            var b = Weapon("b");
            model.Add(a);
            model.Add(b);
            model.Remove(b);
            Assert.That(model.Equipped, Is.SameAs(a));
        }

        [Test]
        public void ClearingWeapons_EquipsNone()
        {
            var model = NewModel();
            model.Add(Weapon("a"));
            model.Clear(InventoryCategory.Weapon);
            Assert.That(model.Equipped, Is.Null);
        }

        // ------------------------------------------------------------ cycle

        [Test]
        public void CycleWeapon_WrapsBothWays()
        {
            var model = NewModel();
            var a = Weapon("a");
            var b = Weapon("b");
            var c = Weapon("c");
            model.Add(a);
            model.Add(b);
            model.Add(c);
            Assert.That(model.CycleWeapon(+1), Is.SameAs(b));
            Assert.That(model.CycleWeapon(+1), Is.SameAs(c));
            Assert.That(model.CycleWeapon(+1), Is.SameAs(a), "wraps forward");
            Assert.That(model.CycleWeapon(-1), Is.SameAs(c), "wraps backward");
            Assert.That(model.Equipped, Is.SameAs(c));
        }

        [Test]
        public void CycleWeapon_WithOneOrNone_ChangesNothing()
        {
            var model = NewModel();
            Assert.That(model.CycleWeapon(+1), Is.Null, "no weapons");
            var a = Weapon("a");
            model.Add(a);
            Assert.That(model.CycleWeapon(+1), Is.Null, "one weapon cycles onto itself: no change");
            Assert.That(model.Equipped, Is.SameAs(a));
        }

        [Test]
        public void CycleIndex_Cases()
        {
            Assert.That(InventoryModel.CycleIndex(0, 0, 1), Is.EqualTo(-1), "no slots");
            Assert.That(InventoryModel.CycleIndex(3, 2, 1), Is.EqualTo(0));
            Assert.That(InventoryModel.CycleIndex(3, 0, -1), Is.EqualTo(2));
            Assert.That(InventoryModel.CycleIndex(3, 1, 5), Is.EqualTo(2), "any positive is one step");
            Assert.That(InventoryModel.CycleIndex(3, -1, 1), Is.EqualTo(0), "none current: forward starts first");
            Assert.That(InventoryModel.CycleIndex(3, -1, -1), Is.EqualTo(2), "none current: backward starts last");
            Assert.That(InventoryModel.CycleIndex(3, 1, 0), Is.EqualTo(1), "zero keeps a valid current");
        }
    }
}
