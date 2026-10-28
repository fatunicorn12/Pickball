using NUnit.Framework;
using Pickleball.Sandbox.Npc;

namespace Pickleball.Tests.Npc
{
    /// <summary>
    /// The death lift: nothing while the body is still standing, the full
    /// amount once it lies down, eased and never backwards in between.
    /// </summary>
    public class NpcDeathLiftTests
    {
        [Test]
        public void NothingBeforeTheStart()
        {
            Assert.That(NpcDeathLift.At(0f, 0.12f, 0.4f), Is.EqualTo(0f));
            Assert.That(NpcDeathLift.At(0.4f, 0.12f, 0.4f), Is.EqualTo(0f));
        }

        [TestCase(1f)]
        [TestCase(1.7f)]
        public void FullAmountAtAndAfterTheEnd(float t)
        {
            Assert.That(NpcDeathLift.At(t, 0.12f, 0.4f), Is.EqualTo(0.12f).Within(1e-6f));
        }

        [Test]
        public void RisesMonotonically()
        {
            float last = 0f;
            for (int i = 0; i <= 100; i++)
            {
                float lift = NpcDeathLift.At(i / 100f, 0.12f, 0.4f);
                Assert.That(lift, Is.GreaterThanOrEqualTo(last - 1e-7f));
                last = lift;
            }
        }

        [Test]
        public void ZeroAmountIsOff()
        {
            Assert.That(NpcDeathLift.At(1f, 0f, 0.4f), Is.EqualTo(0f));
        }

        [Test]
        public void StartAtOneSnapsAtTheEnd()
        {
            Assert.That(NpcDeathLift.At(0.99f, 0.1f, 1f), Is.EqualTo(0f));
            Assert.That(NpcDeathLift.At(1f, 0.1f, 1f), Is.EqualTo(0.1f));
        }
    }
}
