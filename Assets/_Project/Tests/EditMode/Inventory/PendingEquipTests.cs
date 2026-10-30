using NUnit.Framework;
using Pickleball.Sandbox.Inventory;

namespace Pickleball.Tests.Inventory
{
    /// <summary>
    /// The switch rule (<see cref="PendingEquip"/>): never mid-swing, latest
    /// request wins, and asking for what is in hand cancels.
    /// </summary>
    public class PendingEquipTests : InventoryTestFixture
    {
        [Test]
        public void Idle_AppliesAtOnce()
        {
            var compass = Weapon("compass");
            var ruler = Weapon("ruler");
            var pending = new PendingEquip();
            Assert.That(pending.Request(ruler, compass), Is.True);
            Assert.That(pending.TryTake(busy: false, out var now), Is.True);
            Assert.That(now, Is.SameAs(ruler));
            Assert.That(pending.HasPending, Is.False, "taken once");
            Assert.That(pending.TryTake(false, out _), Is.False);
        }

        [Test]
        public void Busy_Waits_ThenAppliesWhenTheSwingEnds()
        {
            var pending = new PendingEquip();
            var ruler = Weapon("ruler");
            pending.Request(ruler, Weapon("compass"));
            for (int frame = 0; frame < 5; frame++)
                Assert.That(pending.TryTake(busy: true, out _), Is.False, "mid-swing: still waiting");
            Assert.That(pending.HasPending, Is.True);
            Assert.That(pending.Pending, Is.SameAs(ruler));
            Assert.That(pending.TryTake(busy: false, out var now), Is.True);
            Assert.That(now, Is.SameAs(ruler));
        }

        [Test]
        public void LatestRequestWins()
        {
            var pending = new PendingEquip();
            var compass = Weapon("compass");
            var ruler = Weapon("ruler");
            var axe = Weapon("axe");
            pending.Request(ruler, compass);
            pending.Request(axe, compass);
            Assert.That(pending.TryTake(false, out var now), Is.True);
            Assert.That(now, Is.SameAs(axe));
        }

        [Test]
        public void AskingForTheWeaponInHand_CancelsTheWait()
        {
            var pending = new PendingEquip();
            var compass = Weapon("compass");
            pending.Request(Weapon("ruler"), compass);
            Assert.That(pending.Request(compass, compass), Is.False);
            Assert.That(pending.HasPending, Is.False, "compass -> ruler -> compass mid-swing: no swap");
            Assert.That(pending.TryTake(false, out _), Is.False);
        }

        [Test]
        public void NothingInHandYet_FirstRequestIsTaken()
        {
            var pending = new PendingEquip();
            var compass = Weapon("compass");
            Assert.That(pending.Request(compass, null), Is.True);
            Assert.That(pending.TryTake(false, out var now), Is.True);
            Assert.That(now, Is.SameAs(compass));
        }
    }
}
