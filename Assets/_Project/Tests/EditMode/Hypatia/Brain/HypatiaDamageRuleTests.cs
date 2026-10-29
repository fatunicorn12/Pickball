using NUnit.Framework;
using Pickleball.Sandbox.Boss.Hypatia;
using UnityEditor;

namespace Pickleball.Tests.Hypatia.Brain
{
    /// <summary>
    /// Her damage rule (the recalibration window is the punish) and the
    /// serialized surface Setup Hypatia Fight wires by name.
    /// </summary>
    public class HypatiaDamageRuleTests : BossTestFixture
    {
        [Test]
        public void InsideTheWindow_UsesTheRecalibrationMultiplier()
        {
            Assert.That(HypatiaBoss.ScaleIncomingDamage(10f, true, 2f, 1f), Is.EqualTo(20f).Within(1e-5f));
        }

        [Test]
        public void OutsideTheWindow_UsesTheNormalMultiplier()
        {
            Assert.That(HypatiaBoss.ScaleIncomingDamage(10f, false, 2f, 0.5f), Is.EqualTo(5f).Within(1e-5f));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(0f)]
        [TestCase(-4f)]
        public void BadAmounts_DealNothing(float amount)
        {
            Assert.That(HypatiaBoss.ScaleIncomingDamage(amount, true, 2f, 1f), Is.EqualTo(0f));
        }

        [TestCase(float.NaN)]
        [TestCase(-1f)]
        [TestCase(0f)]
        public void BadOrZeroMultipliers_DealNothing(float multiplier)
        {
            Assert.That(HypatiaBoss.ScaleIncomingDamage(10f, false, 2f, multiplier), Is.EqualTo(0f));
        }

        [Test]
        public void OverflowingProduct_DealsNothingRatherThanInfinity()
        {
            Assert.That(HypatiaBoss.ScaleIncomingDamage(float.MaxValue, true, 4f, 1f), Is.EqualTo(0f));
        }

        [TestCase("config")]
        [TestCase("arenaCentre")]
        [TestCase("player")]
        [TestCase("playerHealth")]
        [TestCase("rings")]
        [TestCase("conics")]
        [TestCase("burn")]
        [TestCase("body")]
        [TestCase("animator")]
        [TestCase("barks")]
        public void SetupWiresThisFieldByName(string field)
        {
            HypatiaBoss boss = NewComponent<HypatiaBoss>();
            var serialized = new SerializedObject(boss);
            Assert.That(serialized.FindProperty(field), Is.Not.Null,
                        $"HypatiaBoss lost serialized field '{field}'; Setup Hypatia Fight wires it by name.");
        }
    }
}
