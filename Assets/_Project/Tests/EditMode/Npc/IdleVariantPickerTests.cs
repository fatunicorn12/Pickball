using System;
using NUnit.Framework;
using Pickleball.Sandbox.Npc;

namespace Pickleball.Tests.Npc
{
    /// <summary>
    /// Idle variety: the main idle sometimes repeats and sometimes hands over
    /// to a fidget, and a fidget always comes back to the main idle.
    /// </summary>
    public class IdleVariantPickerTests
    {
        [TestCase(0)]
        [TestCase(1)]
        public void OneOrNoVariants_AlwaysMain(int count)
        {
            var picker = new IdleVariantPicker(new Random(1));
            for (int i = 0; i < 20; i++)
                Assert.That(picker.Next(0, count, 0f), Is.EqualTo(0));
        }

        [Test]
        public void Fidget_ReturnsToMain()
        {
            var picker = new IdleVariantPicker(new Random(2));
            for (int current = 1; current < 4; current++)
                Assert.That(picker.Next(current, 4, 0f), Is.EqualTo(0));
        }

        [Test]
        public void MainChanceOne_NeverFidgets()
        {
            var picker = new IdleVariantPicker(new Random(3));
            for (int i = 0; i < 50; i++)
                Assert.That(picker.Next(0, 4, 1f), Is.EqualTo(0));
        }

        [Test]
        public void MainChanceZero_AlwaysFidgetsAndReachesEveryFidget()
        {
            var picker = new IdleVariantPicker(new Random(4));
            var seen = new bool[4];
            for (int i = 0; i < 200; i++)
            {
                int next = picker.Next(0, 4, 0f);
                Assert.That(next, Is.InRange(1, 3));
                seen[next] = true;
            }
            Assert.That(seen[1] && seen[2] && seen[3], Is.True, "every fidget is reachable");
        }

        [Test]
        public void MainChance_IsRoughlyHonoured()
        {
            var picker = new IdleVariantPicker(new Random(5));
            int stays = 0;
            const int draws = 2000;
            for (int i = 0; i < draws; i++)
                if (picker.Next(0, 3, 0.6f) == 0) stays++;
            Assert.That(stays / (float)draws, Is.EqualTo(0.6f).Within(0.05f));
        }
    }
}
