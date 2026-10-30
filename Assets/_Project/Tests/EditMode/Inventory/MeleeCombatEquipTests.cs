using NUnit.Framework;
using Pickleball.Sandbox;
using Pickleball.Sandbox.Combat;
using UnityEngine;

namespace Pickleball.Tests.Inventory
{
    /// <summary>
    /// <see cref="MeleeCombat.Equip"/>, the seam the weapon switch goes
    /// through: it swaps the moveset and blade radius and is safe on a
    /// component that has never been enabled (EditMode runs no lifecycle).
    /// </summary>
    public class MeleeCombatEquipTests : InventoryTestFixture
    {
        SwordMoveset NewMoveset()
            => Track(SwordMoveset.CreateRuntime(new ComboStepDefinition[0], null, new ComboStepDefinition[0], null));

        [Test]
        public void Equip_SwapsMovesetAndRadius()
        {
            var melee = NewComponent<MeleeCombat>();
            var heavy = NewMoveset();
            melee.Equip(heavy, 0.25f);
            Assert.That(melee.Moveset, Is.SameAs(heavy));
            Assert.That(melee.BladeRadius, Is.EqualTo(0.25f));
            Assert.That(melee.IsAttacking, Is.False);
        }

        [Test]
        public void Equip_NullMoveset_KeepsTheCurrentOne()
        {
            var melee = NewComponent<MeleeCombat>();
            var light = NewMoveset();
            melee.Equip(light, 0.18f);
            melee.Equip(null, 0.3f);
            Assert.That(melee.Moveset, Is.SameAs(light));
            Assert.That(melee.BladeRadius, Is.EqualTo(0.3f));
        }

        [Test]
        public void Equip_RadiusIsClamped()
        {
            var melee = NewComponent<MeleeCombat>();
            melee.Equip(null, -1f);
            Assert.That(melee.BladeRadius, Is.EqualTo(0.01f));
        }
    }
}
