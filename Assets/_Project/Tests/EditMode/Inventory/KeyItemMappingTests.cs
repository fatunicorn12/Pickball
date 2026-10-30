using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Inventory;
using UnityEngine;

namespace Pickleball.Tests.Inventory
{
    /// <summary>
    /// Key items: the ability-to-item table (<see cref="KeyItemNames"/>), and
    /// the <see cref="Inventory"/> component mirroring <see cref="PlayerAbilities"/>
    /// rather than owning abilities itself.
    /// </summary>
    public class KeyItemMappingTests : InventoryTestFixture
    {
        [Test]
        public void Tether_IsThePendulum()
        {
            var entry = KeyItemNames.For(AbilityKind.Tether);
            Assert.That(entry.DisplayName, Is.EqualTo("Pendulum"));
            Assert.That(entry.Id, Is.EqualTo("pendulum"));
        }

        [Test]
        public void EveryAbility_HasOneUniqueNamedEntry_InTheOrder()
        {
            var kinds = (AbilityKind[])Enum.GetValues(typeof(AbilityKind));
            Assert.That(KeyItemNames.Order, Is.EquivalentTo(kinds), "the tab order lists every ability once");
            var ids = new HashSet<string>();
            foreach (var kind in kinds)
            {
                var entry = KeyItemNames.For(kind);
                Assert.That(entry.Id, Is.Not.Empty, kind.ToString());
                Assert.That(entry.Id, Is.EqualTo(entry.Id.ToLowerInvariant()).And.Not.Contains(" "), kind.ToString());
                Assert.That(entry.DisplayName, Is.Not.Empty, kind.ToString());
                Assert.That(entry.Description, Is.Not.Empty, kind.ToString());
                Assert.That(ids.Add(entry.Id), Is.True, $"duplicate id {entry.Id}");
            }
            Assert.That(KeyItemNames.For(AbilityKind.DoubleJump).DisplayName, Is.EqualTo("Double Jump"));
            Assert.That(KeyItemNames.For(AbilityKind.GravityAnchor).DisplayName, Is.EqualTo("Gravity Anchor"));
        }

        // ------------------------------------------------------------ component

        (Sandbox.Inventory.Inventory inventory, PlayerAbilities abilities) NewPlayer(params KeyItemDefinition[] catalog)
        {
            var host = Track(new GameObject("Test_Player") { hideFlags = HideFlags.DontSave });
            var abilities = host.AddComponent<PlayerAbilities>();
            var inventory = host.AddComponent<Sandbox.Inventory.Inventory>();
            if (catalog.Length > 0)
            {
                var so = new UnityEditor.SerializedObject(inventory);
                var p = so.FindProperty("keyItems");
                Assert.That(p, Is.Not.Null, "Inventory.keyItems renamed?");
                p.arraySize = catalog.Length;
                for (int i = 0; i < catalog.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = catalog[i];
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            inventory.Bind(abilities, null);
            return (inventory, abilities);
        }

        static IEnumerable<AbilityKind> KeyKinds(Sandbox.Inventory.Inventory inventory)
            => inventory.Model.Items(InventoryCategory.KeyItem).Cast<KeyItemDefinition>().Select(k => k.Ability);

        [Test]
        public void UnlockedAbility_ShowsItsCatalogItem()
        {
            var pendulum = Key(AbilityKind.Tether);
            var (inventory, abilities) = NewPlayer(pendulum, Key(AbilityKind.Dash));
            abilities.Grant(AbilityKind.Tether);
            inventory.Resync();
            Assert.That(inventory.Model.Items(InventoryCategory.KeyItem), Is.EqualTo(new[] { pendulum }));
        }

        [Test]
        public void Unlocked_Event_AddsTheItem_WithoutAResync()
        {
            var (inventory, abilities) = NewPlayer(Key(AbilityKind.Dash));
            abilities.Grant(AbilityKind.Dash);
            Assert.That(KeyKinds(inventory), Is.EqualTo(new[] { AbilityKind.Dash }));
        }

        [Test]
        public void AbilityWithoutCatalogEntry_StillShows_UnderItsTableName()
        {
            var (inventory, abilities) = NewPlayer();
            abilities.Grant(AbilityKind.GravityAnchor);
            inventory.Resync();
            var item = inventory.Model.ItemAt(InventoryCategory.KeyItem, 0);
            Track(item);
            Assert.That(item, Is.InstanceOf<KeyItemDefinition>());
            Assert.That(item.DisplayName, Is.EqualTo("Gravity Anchor"));
            Assert.That(item.Id, Is.EqualTo("gravity_anchor"));
        }

        [Test]
        public void RevokedAbility_LeavesTheTab_OnResync()
        {
            var (inventory, abilities) = NewPlayer(Key(AbilityKind.Dash), Key(AbilityKind.Tether));
            abilities.Grant(AbilityKind.Dash);
            abilities.Grant(AbilityKind.Tether);
            inventory.Resync();
            abilities.Revoke(AbilityKind.Dash);   // raises nothing
            inventory.Resync();
            Assert.That(KeyKinds(inventory), Is.EqualTo(new[] { AbilityKind.Tether }));
        }

        [Test]
        public void AddingAKeyItem_GrantsItsAbility()
        {
            var dash = Key(AbilityKind.Dash);
            var (inventory, abilities) = NewPlayer(dash);
            Assert.That(inventory.Add(dash), Is.EqualTo(InventoryAddResult.Added));
            Assert.That(abilities.Has(AbilityKind.Dash), Is.True, "PlayerAbilities stays the authority");
            Assert.That(inventory.Model.Count(InventoryCategory.KeyItem), Is.EqualTo(1), "shown once, not twice");
            Assert.That(inventory.Add(dash), Is.EqualTo(InventoryAddResult.AlreadyOwned));
        }

        [Test]
        public void RemovingAKeyItem_RevokesItsAbility()
        {
            var dash = Key(AbilityKind.Dash);
            var (inventory, abilities) = NewPlayer(dash);
            inventory.Add(dash);
            Assert.That(inventory.Remove(dash), Is.True);
            Assert.That(abilities.Has(AbilityKind.Dash), Is.False);
        }

        [Test]
        public void Weapons_FromStartingItems_StartingWeaponEquipped()
        {
            var host = Track(new GameObject("Test_Player") { hideFlags = HideFlags.DontSave });
            var inventory = host.AddComponent<Sandbox.Inventory.Inventory>();
            var compass = Weapon("compass");
            var ruler = Weapon("ruler");
            inventory.Configure(null, new InventoryItemDefinition[] { compass, ruler }, ruler);
            Assert.That(inventory.Model.Items(InventoryCategory.Weapon), Is.EqualTo(new[] { compass, ruler }));
            Assert.That(inventory.Equipped, Is.SameAs(ruler));
            Assert.That(inventory.CycleWeapon(+1), Is.SameAs(compass));
        }
    }
}
