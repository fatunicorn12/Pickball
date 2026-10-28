using NUnit.Framework;
using Pickleball.Sandbox.Magic;

namespace Pickleball.Tests.Magic
{
    /// <summary>
    /// Per-spell cooldowns (<see cref="SpellCooldowns"/>): each spell recovers
    /// on its own, a single spell behaves like the caster's old one cooldown
    /// (full on start, counted down to exactly zero), and overflowing the table
    /// only ever reuses the slot nearest done.
    /// </summary>
    public class SpellCooldownsTests
    {
        readonly object _orb = new(), _fire = new(), _bolt = new();

        [Test]
        public void Unknown_IsReady()
        {
            var table = new SpellCooldowns();
            Assert.That(table.Remaining(_orb), Is.EqualTo(0f));
            Assert.That(table.Normalised(_orb), Is.EqualTo(0f));
            Assert.That(table.Remaining(null), Is.EqualTo(0f));
        }

        [Test]
        public void Start_IsFull_ThenCountsDown()
        {
            var table = new SpellCooldowns();
            table.Start(_orb, 0.45f);
            Assert.That(table.Remaining(_orb), Is.EqualTo(0.45f));
            Assert.That(table.Normalised(_orb), Is.EqualTo(1f));
            table.Tick(0.15f);
            Assert.That(table.Remaining(_orb), Is.EqualTo(0.3f).Within(1e-6f));
            Assert.That(table.Normalised(_orb), Is.EqualTo(2f / 3f).Within(1e-5f));
        }

        [TestCase(1f / 30f)]
        [TestCase(1f / 60f)]
        [TestCase(1f / 144f)]
        public void ReadyAfterItsTime_NeverBelowZero(float dt)
        {
            var table = new SpellCooldowns();
            table.Start(_orb, 0.9f);
            float time = 0f;
            while (table.Remaining(_orb) > 0f && time < 10f)
            {
                table.Tick(dt);
                time += dt;
            }
            Assert.That(time, Is.EqualTo(0.9f).Within(dt + 1e-4f));
            table.Tick(1f);
            Assert.That(table.Remaining(_orb), Is.EqualTo(0f));
        }

        [Test]
        public void Spells_RecoverIndependently()
        {
            var table = new SpellCooldowns();
            table.Start(_orb, 0.45f);
            table.Start(_fire, 0.9f);
            table.Tick(0.5f);
            Assert.That(table.Remaining(_orb), Is.EqualTo(0f));
            Assert.That(table.Remaining(_fire), Is.EqualTo(0.4f).Within(1e-6f));
            Assert.That(table.Remaining(_bolt), Is.EqualTo(0f));
        }

        [Test]
        public void Restart_RefillsIt()
        {
            var table = new SpellCooldowns();
            table.Start(_orb, 1f);
            table.Tick(0.7f);
            table.Start(_orb, 1f);
            Assert.That(table.Remaining(_orb), Is.EqualTo(1f));
        }

        [Test]
        public void ZeroSeconds_ClearsIt()
        {
            var table = new SpellCooldowns();
            table.Start(_orb, 1f);
            table.Start(_orb, 0f);
            Assert.That(table.Remaining(_orb), Is.EqualTo(0f));
        }

        [Test]
        public void Overflow_ReusesTheSlotNearestDone()
        {
            var table = new SpellCooldowns(2);
            table.Start(_orb, 5f);
            table.Start(_fire, 1f);
            table.Start(_bolt, 3f);   // table full: the 1 s one is closest to done
            Assert.That(table.Remaining(_orb), Is.EqualTo(5f));
            Assert.That(table.Remaining(_bolt), Is.EqualTo(3f));
            Assert.That(table.Remaining(_fire), Is.EqualTo(0f));
        }

        [Test]
        public void Clear_ReadiesEverything()
        {
            var table = new SpellCooldowns();
            table.Start(_orb, 1f);
            table.Start(_fire, 2f);
            table.Clear();
            Assert.That(table.Remaining(_orb) + table.Remaining(_fire), Is.EqualTo(0f));
        }
    }
}
